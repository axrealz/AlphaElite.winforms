using AlphaElite.infrastructure.services;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>Master platform dashboard: master DB counts plus tenant DB aggregates.</summary>
    internal partial class MasterDashboardForm
    {
        private Panel BuildDashboardPanel()
        {
            var panel = NewContentPanel();

            AddSectionTitle(panel, "Platform Dashboard", 24, 20);
            AddNote(panel, "Master DB data (companies/databases) and tenant DB aggregates are shown separately. Unavailable tenants are skipped and noted.", 24, 52, 920, 20);

            var lblCompanies = AddKpiCard(panel, "COMPANIES (T / A / I)", 24, 84, 220);
            var lblDatabases = AddKpiCard(panel, "TENANT DATABASES", 264, 84, 220);
            var lblMembers = AddKpiCard(panel, "TENANT MEMBERS", 504, 84, 220);
            var lblFeedback = AddKpiCard(panel, "TENANT FEEDBACK", 744, 84, 220);
            var lblPayments = AddKpiCard(panel, "TENANT PAYMENTS", 24, 142, 220);
            var lblAttendance = AddKpiCard(panel, "ATTENDANCE RECORDS", 264, 142, 220);
            var lblInquiries = AddKpiCard(panel, "INQUIRIES", 504, 142, 220);
            var lblSubscriptions = AddKpiCard(panel, "SUBSCRIPTIONS", 744, 142, 220);

            var lblDetail = new Label
            {
                Text = string.Empty,
                ForeColor = ColorMuted,
                Font = new Font("Segoe UI", 8.5F),
                AutoSize = false,
                Location = new Point(24, 200),
                Size = new Size(940, 60)
            };
            panel.Controls.Add(lblDetail);

            panel.Tag = (Func<Task>)(async () =>
            {
                var counts = await _runtime.MasterTenantAdminService.GetPlatformCountsAsync();
                var overviews = await _runtime.MasterTenantAdminService.GetCompanyOverviewsAsync();
                var aggregates = await _runtime.MasterTenantAdminService.GetTenantAggregatesAsync(overviews);

                var ok = aggregates.Where(a => a.Success).ToList();

                SetKpi(lblCompanies, $"{counts.TotalCompanies} / {counts.ActiveCompanies} / {counts.InactiveCompanies}");
                SetKpi(lblDatabases, $"{counts.ConfiguredDatabases} configured / {counts.ActiveDatabases} active");
                SetKpi(lblMembers, ok.Sum(a => a.Members).ToString());
                SetKpi(lblFeedback, ok.Sum(a => a.Feedback).ToString());
                SetKpi(lblPayments, ok.Sum(a => a.Payments) + " records / " + ok.Sum(a => a.PaymentTotal).ToString("N2"));
                SetKpi(lblAttendance, ok.Sum(a => a.Attendance).ToString());
                SetKpi(lblInquiries, ok.Sum(a => a.Inquiries).ToString());
                SetKpi(lblSubscriptions, ok.Sum(a => a.Subscriptions).ToString());

                lblDetail.Text = string.Join(Environment.NewLine, aggregates.Select(a =>
                {
                    var company = overviews.First(o => o.CompanyId == a.CompanyId);
                    return a.Success
                        ? $"  {company.CompanyName}: {a.Members} members, {a.Payments} payments ({a.PaymentTotal:N2})"
                        : $"  {company.CompanyName}: UNAVAILABLE - {a.Error}";
                }));
            });

            return panel;
        }
    }
}
