using AlphaElite.infrastructure.services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>How a Business Intelligence chart renders its series.</summary>
    internal enum MasterBiChartKind
    {
        Bars,
        Donut
    }

    /// <summary>
    /// A small, self-contained Business Intelligence chart drawn in the Alpha Elite black/gold
    /// palette. It holds the raw series (categories + values) that the master service returned,
    /// so the numbers on screen are exactly the numbers the master database produced - the control
    /// never invents a value and never extrapolates.
    ///
    /// Display-only: it has no interaction surface at all, and no export of any kind.
    /// </summary>
    internal sealed class MasterBiChart : Control
    {
        private static readonly Color[] Palette =
        {
            Color.FromArgb(212, 175, 55),   // gold
            Color.FromArgb(176, 146, 48),
            Color.FromArgb(120, 120, 120),
            Color.FromArgb(90, 140, 200),
            Color.FromArgb(120, 180, 120)
        };

        public MasterBiChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = UiTheme.Panel;
        }

        // How the series renders. Deliberately a FIELD with a setter method, not a property: the
        // WinForms designer-serialization analyzer (WFO1000) only accepts auto properties that
        // declare their serialization, and this is never a designer-authored value.
        private MasterBiChartKind _kind = MasterBiChartKind.Bars;

        /// <summary>Selects how the bound series renders.</summary>
        internal void SetKind(MasterBiChartKind kind)
        {
            _kind = kind;
            Invalidate();
        }

        public IReadOnlyList<string> Categories { get; private set; } = Array.Empty<string>();

        public IReadOnlyList<decimal> Values { get; private set; } = Array.Empty<decimal>();

        /// <summary>Binds a real series to the control (values line up with categories by index).</summary>
        public void SetSeries(IEnumerable<string> categories, IEnumerable<decimal> values)
        {
            Categories = categories?.ToList() ?? new List<string>();
            Values = values?.ToList() ?? new List<decimal>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            if (Width <= 8 || Height <= 8)
            {
                return;
            }

            using var titleFont = new Font("Segoe UI", 9F, FontStyle.Bold);
            using var labelFont = new Font("Segoe UI", 7.5F);
            using var valueFont = new Font("Segoe UI", 8F, FontStyle.Bold);
            using var goldBrush = new SolidBrush(UiTheme.Gold);
            using var textBrush = new SolidBrush(UiTheme.Text);
            using var mutedBrush = new SolidBrush(UiTheme.Muted);

            g.DrawString(Text, titleFont, goldBrush, 8, 6);

            // A Control has no BorderStyle (and a themed border must not be painted by the OS),
            // so the card outline is drawn here in the palette's border colour.
            using (var borderPen = new Pen(UiTheme.Border))
            {
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }

            var plot = new RectangleF(8, 28, Width - 16, Height - 28 - 20);
            if (plot.Width <= 10 || plot.Height <= 10)
            {
                return;
            }

            if (Values.Count == 0 || Categories.Count == 0)
            {
                g.DrawString("No data", labelFont, mutedBrush, plot.X, plot.Y + plot.Height / 2);
                return;
            }

            if (_kind == MasterBiChartKind.Donut)
            {
                DrawDonut(g, plot, labelFont, valueFont, textBrush, mutedBrush);
            }
            else
            {
                DrawBars(g, plot, labelFont, valueFont, textBrush, mutedBrush);
            }
        }

        private void DrawBars(Graphics g, RectangleF plot, Font labelFont, Font valueFont, Brush textBrush, Brush mutedBrush)
        {
            var max = Values.Count == 0 ? 0m : Values.Max();
            var count = Math.Min(Categories.Count, Values.Count);
            var slot = plot.Width / Math.Max(1, count);
            var barWidth = Math.Max(6f, slot - 12f);
            var baseline = plot.Bottom;

            using var barBrush = new LinearGradientBrush(
                new RectangleF(plot.X, plot.Y, Math.Max(1f, plot.Width), Math.Max(1f, plot.Height)),
                UiTheme.Gold, Color.FromArgb(140, 112, 30), LinearGradientMode.Vertical);
            using var axisPen = new Pen(UiTheme.GridLine);

            g.DrawLine(axisPen, plot.X, baseline, plot.Right, baseline);

            for (var i = 0; i < count; i++)
            {
                var value = Values[i];
                // A zero bar is drawn as a 2px stub at the baseline so the category is still
                // visibly present with the value "0" above it - never blank, never NaN.
                var barHeight = max <= 0m ? 2f : Math.Max(2f, (float)(value / max) * (plot.Height - 18f));
                var x = plot.X + i * slot + (slot - barWidth) / 2f;
                var y = baseline - barHeight;

                g.FillRectangle(barBrush, x, y, barWidth, barHeight);
                g.DrawRectangle(axisPen, x, y, barWidth, barHeight);

                var valueText = FormatValue(value);
                var valueSize = g.MeasureString(valueText, valueFont);
                g.DrawString(valueText, valueFont, textBrush, x + (barWidth - valueSize.Width) / 2f, Math.Max(plot.Y, y - 15f));

                var label = Truncate(Categories[i], 12);
                var labelSize = g.MeasureString(label, labelFont);
                g.DrawString(label, labelFont, mutedBrush, x + (barWidth - labelSize.Width) / 2f, baseline + 2f);
            }
        }

        private void DrawDonut(Graphics g, RectangleF plot, Font labelFont, Font valueFont, Brush textBrush, Brush mutedBrush)
        {
            var total = Values.Sum();
            var size = Math.Min(plot.Height, plot.Width * 0.55f);
            var diameter = Math.Max(20f, size - 12f);
            var ring = new RectangleF(plot.X + 6f, plot.Y + 6f, diameter, diameter);

            if (total <= 0m)
            {
                using var emptyPen = new Pen(UiTheme.GridLine, Math.Max(8f, diameter * 0.18f));
                g.DrawArc(emptyPen, ring, 0, 360);
                g.DrawString("0", valueFont, mutedBrush, ring.X + ring.Width / 2f - 4f, ring.Y + ring.Height / 2f - 8f);
            }
            else
            {
                var startAngle = -90f;
                for (var i = 0; i < Values.Count && i < Categories.Count; i++)
                {
                    var sweep = (float)(Values[i] / total) * 360f;
                    using var sliceBrush = new SolidBrush(Palette[i % Palette.Length]);
                    using var pen = new Pen(sliceBrush.Color, Math.Max(8f, diameter * 0.18f));
                    if (sweep > 0.01f)
                    {
                        g.DrawArc(pen, ring, startAngle, sweep);
                    }

                    startAngle += sweep;
                }
            }

            // Legend: category -> value, laid out to the right of the ring.
            var legendX = ring.Right + 12f;
            var legendY = plot.Y + 4f;
            for (var i = 0; i < Categories.Count && i < Values.Count; i++)
            {
                using var swatch = new SolidBrush(Palette[i % Palette.Length]);
                g.FillRectangle(swatch, legendX, legendY + 3f, 10f, 10f);
                g.DrawString($"{Truncate(Categories[i], 16)}: {FormatValue(Values[i])}", labelFont, textBrush, legendX + 16f, legendY);
                legendY += 18f;
                if (legendY > plot.Bottom - 14f)
                {
                    break;
                }
            }
        }

        private static string FormatValue(decimal value) =>
            value == decimal.Truncate(value) ? decimal.Truncate(value).ToString("0") : value.ToString("0.00");

        private static string Truncate(string text, int maxLength) =>
            text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
    }

    /// <summary>
    /// SUPER ADMIN BUSINESS INTELLIGENCE (top-level area, master database only).
    ///
    /// It answers the business questions of the subscription PLATFORM - how many tenants exist,
    /// which plans they use, subscription and payment status, pending reviews, and subscription
    /// revenue - and it is deliberately separate from the tenant-side BI (Tenant A/B/C never see
    /// this screen). Every number is a real master-database aggregate; nothing is hardcoded and no
    /// zero is ever rendered blank, NaN or as an error. DISPLAY-ONLY: there is no Export / PDF /
    /// CSV / Excel control anywhere on this page, and no write of any kind.
    /// </summary>
    internal partial class MasterDashboardForm
    {
        private Panel? pnlBusinessIntelligence;
        private Label lblBiGenerated = null!;
        private Label lblBiKpiCompanies = null!;
        private Label lblBiKpiActive = null!;
        private Label lblBiKpiPending = null!;
        private Label lblBiKpiExpired = null!;
        private Label lblBiKpiTotalRevenue = null!;
        private Label lblBiKpiMonthlyRevenue = null!;
        private MasterBiChart chartBiPlans = null!;
        private MasterBiChart chartBiRevenue = null!;
        private MasterBiChart chartBiStatus = null!;
        private MasterBiChart chartBiPayment = null!;

        private MasterBiSnapshot? _biSnapshot;

        private const int BiKpiWidth = 150;
        private const int BiChartWidth = 452;
        private const int BiChartHeight = 246;

        private Panel BuildBusinessIntelligencePanel()
        {
            var panel = NewContentPanel();
            pnlBusinessIntelligence = panel;

            AddSectionTitle(panel, "Business Intelligence", 24, 16);
            AddNote(panel,
                "Subscription-platform intelligence from the MASTER database: tenant/company counts, plan usage, subscription " +
                "and payment status, pending reviews and subscription revenue. Read-only - there is nothing to export here.",
                24, 48, 940, 30);

            // ---- Six KPI cards (all real master data; zeros are shown as 0) ----
            lblBiKpiCompanies = AddKpiCard(panel, "Total Companies / Tenants", 24, 84, BiKpiWidth);
            lblBiKpiActive = AddKpiCard(panel, "Active Subscriptions", 24 + 156, 84, BiKpiWidth);
            lblBiKpiPending = AddKpiCard(panel, "Pending Requests", 24 + 312, 84, BiKpiWidth);
            lblBiKpiExpired = AddKpiCard(panel, "Expired Subscriptions", 24 + 468, 84, BiKpiWidth);
            lblBiKpiTotalRevenue = AddKpiCard(panel, "Total Subscription Revenue", 24 + 624, 84, BiKpiWidth);
            lblBiKpiMonthlyRevenue = AddKpiCard(panel, "Monthly Subscription Revenue", 24 + 780, 84, BiKpiWidth);

            // ---- Four subscription-focused charts (2 x 2) ----
            chartBiPlans = NewBiChart("Subscription Plans Overview", MasterBiChartKind.Bars, 24, 152);
            chartBiRevenue = NewBiChart("Subscription Revenue (monthly)", MasterBiChartKind.Bars, 484, 152);
            chartBiStatus = NewBiChart("Subscription Status", MasterBiChartKind.Donut, 24, 410);
            chartBiPayment = NewBiChart("Payment Overview", MasterBiChartKind.Donut, 484, 410);

            panel.Controls.Add(chartBiPlans);
            panel.Controls.Add(chartBiRevenue);
            panel.Controls.Add(chartBiStatus);
            panel.Controls.Add(chartBiPayment);

            lblBiGenerated = new Label
            {
                ForeColor = ColorMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Location = new Point(24, 668),
                Size = new Size(940, 20)
            };
            panel.Controls.Add(lblBiGenerated);

            // ShowPanelAsync awaits this: the whole dashboard is one master read.
            panel.Tag = (Func<Task>)LoadBusinessIntelligenceAsync;

            return panel;
        }

        private MasterBiChart NewBiChart(string title, MasterBiChartKind kind, int x, int y)
        {
            var chart = new MasterBiChart
            {
                Text = title,
                Location = new Point(x, y),
                Size = new Size(BiChartWidth, BiChartHeight),
                BackColor = UiTheme.Panel
            };
            chart.SetKind(kind);
            // Chart captions are drawn with the theme palette, so the control is normalized here
            // exactly like the plan cards (it is created after the panel-wide theme pass).
            UiTheme.Apply(chart);
            return chart;
        }

        private async Task LoadBusinessIntelligenceAsync()
        {
            var snapshot = await _runtime.BusinessIntelligenceService.GetSnapshotAsync();
            _biSnapshot = snapshot;
            RenderBusinessIntelligence(snapshot);
        }

        /// <summary>Renders a snapshot into the six KPI cards and the four charts.</summary>
        private void RenderBusinessIntelligence(MasterBiSnapshot snapshot)
        {
            SetKpi(lblBiKpiCompanies, snapshot.TotalCompanies.ToString("N0"));
            SetKpi(lblBiKpiActive, snapshot.ActiveSubscriptions.ToString("N0"));
            SetKpi(lblBiKpiPending, snapshot.PendingSubscriptionRequests.ToString("N0"));
            SetKpi(lblBiKpiExpired, snapshot.ExpiredSubscriptions.ToString("N0"));
            SetKpi(lblBiKpiTotalRevenue, snapshot.TotalSubscriptionRevenue.ToString("0.00"));
            SetKpi(lblBiKpiMonthlyRevenue, snapshot.MonthlySubscriptionRevenue.ToString("0.00"));

            chartBiPlans.SetSeries(snapshot.PlanOverview.Categories, snapshot.PlanOverview.Values);
            chartBiRevenue.SetSeries(snapshot.RevenueOverTime.Categories, snapshot.RevenueOverTime.Values);
            chartBiStatus.SetSeries(snapshot.SubscriptionStatus.Categories, snapshot.SubscriptionStatus.Values);
            chartBiPayment.SetSeries(snapshot.PaymentOverview.Categories, snapshot.PaymentOverview.Values);

            lblBiGenerated.Text =
                $"Master database only. Generated {snapshot.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC." +
                "  Revenue = paid subscription amounts (PaymentStatus = Paid), grouped by subscription start month.";
        }

        // =====================================================================================
        // Headless verification hooks (Super Admin Business Intelligence screen).
        // =====================================================================================

        /// <summary>Headless: opens the BI area through the real navigation path and loads it.</summary>
        internal async Task<string> OpenBusinessIntelligenceForTestAsync()
        {
            // The screen's controls are built lazily when its navigation item is activated, so the
            // probe goes through the same path (ShowPanelAsync awaits the panel's own loader).
            await ShowPanelAsync(BuildBusinessIntelligencePanel, btnNavBusiness);
            return DescribeBusinessIntelligenceForTest();
        }

        /// <summary>Headless: re-runs the BI load without switching panels (idempotent refresh).</summary>
        internal Task LoadBusinessIntelligenceForTestAsync() => RunMasterOperationAsync(LoadBusinessIntelligenceAsync);

        /// <summary>
        /// Headless: how many of the four BI charts currently hold a bound series (4 once loaded).
        /// The chart controls are plain data holders, so this stays readable even after the panel
        /// has been swapped out by a later navigation step.
        /// </summary>
        internal int BiChartCountForTest => new[] { chartBiPlans, chartBiRevenue, chartBiStatus, chartBiPayment }
            .Count(chart => chart != null && chart.Categories.Count > 0 && chart.Values.Count > 0);

        /// <summary>Headless: the six KPI values currently rendered.</summary>
        internal string DescribeBusinessIntelligenceKpisForTest() => _biSnapshot == null
            ? "(not loaded)"
            : $"companies={_biSnapshot.TotalCompanies} active={_biSnapshot.ActiveSubscriptions} " +
              $"pending={_biSnapshot.PendingSubscriptionRequests} expired={_biSnapshot.ExpiredSubscriptions} " +
              $"totalRevenue={_biSnapshot.TotalSubscriptionRevenue:0.00} monthlyRevenue={_biSnapshot.MonthlySubscriptionRevenue:0.00}";

        /// <summary>
        /// Headless: a full description of the BI page - the six rendered KPI labels, each chart's
        /// real categories/values, whether ANY export-looking control exists, the number of light
        /// (un-themed) surfaces and of type-name drop-down values.
        /// </summary>
        internal string DescribeBusinessIntelligenceForTest()
        {
            if (pnlBusinessIntelligence == null)
            {
                return "(not built)";
            }

            string Series(MasterBiChart chart) =>
                $"{{cats=[{string.Join(",", chart.Categories)}] vals=[{string.Join(",", chart.Values.Select(v => v.ToString("0.##")))}]}}";

            var exportControls = pnlBusinessIntelligence.Controls
                .OfType<Control>()
                .SelectMany(AllDescendants)
                .Where(c => !string.IsNullOrEmpty(c.Text)
                            && (c.Text.Contains("Export", StringComparison.OrdinalIgnoreCase)
                                || c.Text.Contains("PDF", StringComparison.OrdinalIgnoreCase)
                                || c.Text.Contains("CSV", StringComparison.OrdinalIgnoreCase)
                                || c.Text.Contains("Excel", StringComparison.OrdinalIgnoreCase)))
                .Select(c => $"{UiTheme.Name(c)}='{c.Text}'")
                .ToList();

            return $"kpi=[{lblBiKpiCompanies.Text.Replace("\n", "=")}|{lblBiKpiActive.Text.Replace("\n", "=")}|" +
                   $"{lblBiKpiPending.Text.Replace("\n", "=")}|{lblBiKpiExpired.Text.Replace("\n", "=")}|" +
                   $"{lblBiKpiTotalRevenue.Text.Replace("\n", "=")}|{lblBiKpiMonthlyRevenue.Text.Replace("\n", "=")}] " +
                   $"charts=4 plans={Series(chartBiPlans)} revenue={Series(chartBiRevenue)} " +
                   $"status={Series(chartBiStatus)} payment={Series(chartBiPayment)} " +
                   $"buttons={pnlBusinessIntelligence.Controls.OfType<Control>().SelectMany(AllDescendants).OfType<Button>().Count()} " +
                   $"exportControls={exportControls.Count}" +
                   (exportControls.Count == 0 ? string.Empty : $" [{string.Join("; ", exportControls)}]") +
                   $" light={UiTheme.FindLightSurfaces(pnlBusinessIntelligence).Count} " +
                   $"typename={UiTheme.FindTypeNameDisplayValues(pnlBusinessIntelligence).Count}";
        }

        private static IEnumerable<Control> AllDescendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var descendant in AllDescendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}
