using AlphaElite.infrastructure.services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 5: dedicated Master / Super Admin window. It never reuses the tenant window
    /// (<c>Form1</c>) and is reachable only with a <see cref="MasterUserSession"/>, a distinct
    /// type a tenant session can never become. All tenant data is read through the existing
    /// resolver/factory; no CompanyId is hardcoded; no credential secret is ever displayed.
    ///
    /// The shell (sidebar, header, panel switching) lives in this file; the individual admin
    /// areas live in the MasterDashboardForm.*.cs partial files.
    /// </summary>
    internal partial class MasterDashboardForm : Form
    {
        // The palette lives in one place (UiTheme) so the tenant window, the master window and
        // the theme normalizer can never drift apart. Values are unchanged.
        private static readonly Color ColorBackground = UiTheme.Background;
        private static readonly Color ColorPanel = UiTheme.Panel;
        private static readonly Color ColorField = UiTheme.Field;
        private static readonly Color ColorHeader = UiTheme.Header;
        private static readonly Color ColorGold = UiTheme.Gold;
        private static readonly Color ColorGoldHover = UiTheme.GoldHover;
        private static readonly Color ColorText = UiTheme.Text;
        private static readonly Color ColorMuted = UiTheme.Muted;
        private static readonly Color ColorDanger = UiTheme.Danger;

        private const int NavWidth = 210;
        private const int NavButtonHeight = 40;

        private readonly AppRuntime _runtime;
        private readonly MasterUserSession _session;
        private IReadOnlyList<TenantOption> _tenants = new List<TenantOption>();

        /// <summary>How many times the tenant/company list was read (diagnostics only).</summary>
        private int _tenantListLoadCount;

        private Button btnNavSubscription = null!;
        private Button btnNavCompanies = null!;
        private Button btnNavDatabases = null!;
        private Button btnNavBusiness = null!;
        private Button btnLogout = null!;

        private Panel pnlContent = null!;

        public MasterDashboardForm(AppRuntime runtime, MasterUserSession session)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _session = session ?? throw new ArgumentNullException(nameof(session));

            // Headless-safe sync context (same guard used by the Phase 4 verification harness):
            // blocking on async work after control creation would otherwise deadlock.
            SynchronizationContext.SetSynchronizationContext(null);

            InitializeShell();
            Load += OnLoadAsync;
        }

        private void InitializeShell()
        {
            Text = $"Alpha Elite - Master Administration ({_session.Username})";
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            BackColor = ColorBackground;
            Font = new Font("Segoe UI", 9F);
            MinimumSize = new Size(1100, 660);

            var pnlSidebar = new Panel { Dock = DockStyle.Left, Width = NavWidth, BackColor = ColorPanel };

            var lblBrandTitle = new Label
            {
                Text = "ALPHA ELITE",
                ForeColor = ColorGold,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(0, 28),
                Size = new Size(NavWidth, 28),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var lblBrandSubtitle = new Label
            {
                Text = "MASTER ADMIN",
                ForeColor = ColorMuted,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(0, 56),
                Size = new Size(NavWidth, 18),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Final subscription system: the Super Admin area contains EXACTLY the four mandated
            // primary items - Subscription / Companies / Tenants / Tenant Database / Business
            // Intelligence - plus Logout. Business Intelligence is a top-level Super Admin page and
            // is deliberately NOT part of the tenant navigation.
            btnNavSubscription = CreateNavButton("Subscription", 100);
            btnNavCompanies = CreateNavButton("Companies / Tenants", 100 + 1 * NavButtonHeight);
            btnNavDatabases = CreateNavButton("Tenant Database", 100 + 2 * NavButtonHeight);
            btnNavBusiness = CreateNavButton("Business Intelligence", 100 + 3 * NavButtonHeight);

            // Named handlers - not async-void lambdas - so a failure in the navigation path is
            // reported and logged instead of escaping as an unhandled exception.
            btnNavSubscription.Click += (s, e) => OnNavClicked(btnNavSubscription);
            btnNavCompanies.Click += (s, e) => OnNavClicked(btnNavCompanies);
            btnNavDatabases.Click += (s, e) => OnNavClicked(btnNavDatabases);
            btnNavBusiness.Click += (s, e) => OnNavClicked(btnNavBusiness);

            btnLogout = new Button
            {
                Text = "Logout",
                Location = new Point(20, 560),
                Size = new Size(NavWidth - 40, 36),
                BackColor = ColorPanel,
                ForeColor = ColorGold,
                FlatStyle = FlatStyle.Flat
            };
            btnLogout.FlatAppearance.BorderSize = 1;
            btnLogout.FlatAppearance.BorderColor = ColorGold;
            btnLogout.Click += (s, e) => RequestLogout();

            pnlSidebar.Controls.Add(lblBrandTitle);
            pnlSidebar.Controls.Add(lblBrandSubtitle);
            pnlSidebar.Controls.Add(btnNavSubscription);
            pnlSidebar.Controls.Add(btnNavCompanies);
            pnlSidebar.Controls.Add(btnNavDatabases);
            pnlSidebar.Controls.Add(btnNavBusiness);
            pnlSidebar.Controls.Add(btnLogout);

            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = ColorHeader };

            var lblTitle = new Label
            {
                Text = "MASTER ADMINISTRATION",
                ForeColor = ColorGold,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(24, 8),
                Size = new Size(500, 24)
            };

            var lblUser = new Label
            {
                Name = "lblMasterUser",
                Text = $"Signed in: {_session.Username}   |   Role: {_session.Role}   |   Identity role: {_session.IdentityRoleName}",
                ForeColor = ColorMuted,
                AutoSize = false,
                Location = new Point(24, 34),
                Size = new Size(760, 18)
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblUser);

            pnlContent = new Panel { Dock = DockStyle.Fill, BackColor = ColorBackground };

            Controls.Add(pnlContent);
            Controls.Add(pnlHeader);
            Controls.Add(pnlSidebar);
        }

        /// <summary>
        /// Ends the master session and returns to the login entry point. DialogResult.Retry is what
        /// the application entry point treats as "show the login form again"; the process keeps
        /// running. Deliberately unchanged by the tenant-logout fix.
        /// </summary>
        private void RequestLogout()
        {
            DialogResult = DialogResult.Retry;
            Close();
        }

        /// <summary>Headless hook: performs the real master logout and reports the result.</summary>
        internal DialogResult RequestLogoutForTest()
        {
            RequestLogout();
            return DialogResult;
        }

        private Button CreateNavButton(string text, int top)
        {
            var button = new Button
            {
                Text = "   " + text,
                Location = new Point(0, top),
                Size = new Size(NavWidth, NavButtonHeight),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = ColorPanel,
                ForeColor = ColorText,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ColorHeader;
            return button;
        }

        private async void OnLoadAsync(object? sender, EventArgs e)
        {
            try
            {
                await LoadTenantListAsync();
                await ShowPanelAsync(BuildSubscriptionPanel, btnNavSubscription);
            }
            catch (Exception ex)
            {
                MasterDbErrorReporter.ShowDialog(this, "The master dashboard could not load its data.", ex);
            }
        }

        /// <summary>
        /// Handles a primary-navigation click. The (gated) panel switch is awaited and ANY failure
        /// is reported and logged, so rapid navigation can never escalate into an unhandled
        /// exception on an async-void handler.
        /// </summary>
        private async void OnNavClicked(Button navButton)
        {
            try
            {
                await ShowPanelAsync(FactoryFor(navButton), navButton);
            }
            catch (Exception ex)
            {
                MasterDbErrorReporter.ShowDialog(this, "The Super Admin area could not be opened.", ex);
            }
        }

        private Func<Panel> FactoryFor(Button navButton)
        {
            if (ReferenceEquals(navButton, btnNavCompanies))
            {
                return BuildCompaniesPanel;
            }

            if (ReferenceEquals(navButton, btnNavDatabases))
            {
                return BuildDatabasesPanel;
            }

            return ReferenceEquals(navButton, btnNavBusiness) ? BuildBusinessIntelligencePanel : BuildSubscriptionPanel;
        }

        /// <summary>
        /// Reads the tenant / company list from the MASTER database - exactly ONCE per dashboard
        /// load, inside the master gate. The list is then cached in <see cref="_tenants"/> and a
        /// panel switch never re-reads it, so opening the Super Admin screen cannot fire several
        /// identical master queries (and a refresh racing the initial load cannot double it up).
        ///
        /// The read is async end to end: the bounded retry policy lives inside EF Core's execution
        /// strategy, so a dropped connection is retried on the calling thread's async continuation
        /// and the UI message pump is never blocked (no Thread.Sleep, no Task.Run, no .Wait/.Result).
        /// </summary>
        private Task LoadTenantListAsync() => RunMasterOperationAsync(async () =>
        {
            Interlocked.Increment(ref _tenantListLoadCount);
            _tenants = await _runtime.AuthenticationService.GetAvailableTenantsAsync();
        });

        /// <summary>Diagnostics: how many times the master tenant/company list was actually read.</summary>
        internal int TenantListLoadCount => Volatile.Read(ref _tenantListLoadCount);

        /// <summary>
        /// Headless hook: runs the dashboard's REAL gated tenant-list read without switching panels
        /// (a panel switch can raise a modal error dialog, which cannot be dismissed unattended).
        /// Used to prove that one dashboard load reads the list exactly once.
        /// </summary>
        internal Task LoadTenantListForTestAsync() => LoadTenantListAsync();

        // =====================================================================================
        // Master-context serialization - CORRECTED, not replaced blindly.
        //
        // ROOT CAUSE of "A second operation was started on this context instance": the master
        // window, its panels and every master service were handed ONE shared
        // MasterAlphaEliteDbContext (composed once by AppRuntime) and ANY two overlapping master
        // operations on one context throw - so serializing USER ACTIONS alone could never make
        // the data layer safe.
        //
        // The real fix is in the data layer: the UI-facing subscription service is now
        // ScopedSubscriptionService, which creates ONE MasterAlphaEliteDbContext per operation
        // (see WinFormsLibrary1/services/ScopedSubscriptionService.cs), so overlapping reads and
        // writes are legal by construction. The few remaining shared-context paths on this screen
        // (the tenant list read and every panel switch) are additionally funnelled through the
        // gate below.
        //
        // The gate is therefore RETAINED for what it is genuinely good at: UI-level serialization
        // (no two panel switches or loads interleave and write over each other's controls, and a
        // panel is never torn down while its own load is still running). It is NOT the concurrency
        // defence any more - correctness no longer depends on the user clicking slowly.
        //
        // The gate is acquired EXACTLY ONCE per user action and never nested - the individual
        // load/CRUD helpers stay ungated and are only ever called from a gated entry point.
        // Modal dialogs are safe inside the gate because a modal window blocks further clicks
        // on the form, so nothing can stack up behind it.
        // =====================================================================================
        private readonly SemaphoreSlim _masterOperationGate = new(1, 1);

        /// <summary>Runs one master-database user action, serialized against every other one.</summary>
        private async Task RunMasterOperationAsync(Func<Task> operation)
        {
            if (!await _masterOperationGate.WaitAsync(TimeSpan.FromSeconds(120)))
            {
                return; // The previous action never finished: never stack another one on the context.
            }

            try
            {
                await operation();
            }
            finally
            {
                _masterOperationGate.Release();
            }
        }

        /// <summary>
        /// Swaps the content host to a freshly built panel and awaits its async loader.
        ///
        /// The WHOLE switch is one serialized master operation: switching two panels quickly used
        /// to overlap two loads on one shared context, and it also disposed the previous panel
        /// while its load was still running. The gate is still acquired exactly once here.
        /// </summary>
        private Task ShowPanelAsync(Func<Panel> factory, Button navButton)
            => RunMasterOperationAsync(() => ShowPanelCoreAsync(factory, navButton));

        private async Task ShowPanelCoreAsync(Func<Panel> factory, Button navButton)
        {
            SetActiveNav(navButton);

            foreach (Control old in pnlContent.Controls)
            {
                old.Dispose();
            }
            pnlContent.Controls.Clear();

            Panel panel;
            try
            {
                panel = factory();
                // Visual only: any control the panel still built with WinForms' default light
                // colours is normalized onto the themed surfaces here.
                UiTheme.Apply(panel);
            }
            catch (Exception ex)
            {
                pnlContent.Controls.Add(new Label
                {
                    Text = "Failed to build this view: " + ex.Message,
                    ForeColor = ColorDanger,
                    AutoSize = true,
                    Location = new Point(24, 24)
                });
                return;
            }

            pnlContent.Controls.Add(panel);

            if (panel.Tag is Func<Task> load)
            {
                try
                {
                    // The caller (ShowPanelAsync) already holds the master gate for the whole
                    // switch, so the loader runs inside it - never a nested acquisition.
                    await load();
                }
                catch (Exception ex)
                {
                    MasterDbErrorReporter.ShowDialog(this, "Could not load data for this view.", ex);
                }
            }
        }

        private void SetActiveNav(Button active)
        {
            foreach (var button in new[] { btnNavSubscription, btnNavCompanies, btnNavDatabases, btnNavBusiness })
            {
                button.BackColor = ReferenceEquals(button, active) ? ColorHeader : ColorPanel;
                button.ForeColor = ReferenceEquals(button, active) ? ColorGold : ColorText;
            }
        }

        // Shared helpers used by the partial panel files.

        internal static Panel NewContentPanel() => new()
        {
            Dock = DockStyle.Fill,
            BackColor = ColorBackground,
            AutoScroll = true
        };

        internal static void AddSectionTitle(Panel panel, string text, int x, int y)
        {
            panel.Controls.Add(new Label
            {
                Text = text,
                ForeColor = ColorGold,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(x, y),
                Size = new Size(760, 28)
            });
        }

        internal static void AddNote(Panel panel, string text, int x, int y, int width, int height)
        {
            panel.Controls.Add(new Label
            {
                Text = text,
                ForeColor = ColorMuted,
                Font = new Font("Segoe UI", 8.5F),
                AutoSize = false,
                Location = new Point(x, y),
                Size = new Size(width, height)
            });
        }

        internal static Label AddKpiCard(Panel panel, string title, int x, int y, int width)
        {
            var label = new Label
            {
                Text = title + "\n…",
                ForeColor = ColorGold,
                BackColor = ColorHeader,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(x, y),
                Size = new Size(width, 48)
            };
            panel.Controls.Add(label);
            return label;
        }

        internal static void SetKpi(Label label, string value)
        {
            var text = label.Text;
            var breakIndex = text.IndexOf('\n');
            label.Text = (breakIndex >= 0 ? text[..breakIndex] : text) + "\n" + value;
        }

        internal static DataGridView NewGrid() => new()
        {
            BackgroundColor = ColorBackground,
            DefaultCellStyle = { BackColor = ColorHeader, ForeColor = ColorText },
            ColumnHeadersDefaultCellStyle = { BackColor = ColorHeader, ForeColor = ColorGold },
            EnableHeadersVisualStyles = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false
        };

        internal static Button NewPrimaryButton(string text, int x, int y, int width)
        {
            var button = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 34),
                BackColor = ColorGold,
                ForeColor = Color.FromArgb(20, 20, 20),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        internal static Button NewSecondaryButton(string text, int x, int y, int width)
        {
            var button = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 34),
                BackColor = ColorPanel,
                ForeColor = ColorText,
                FlatStyle = FlatStyle.Flat
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            return button;
        }

        internal static TextBox NewField(int x, int y, int width, bool password = false)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(width, 24),
                BackColor = ColorField,
                ForeColor = ColorText,
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = password
            };
        }

        internal static ComboBox NewCombo(int x, int y, int width, params object[] items)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(x, y),
                Size = new Size(width, 26),
                BackColor = ColorField,
                ForeColor = ColorText,
                FlatStyle = FlatStyle.Flat
            };
            combo.Items.AddRange(items);
            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
            return combo;
        }

        internal static string TrimError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "Unknown error.";
            }

            var single = message.Replace("\r", " ").Replace("\n", " ");
            return single.Length <= 160 ? single : single[..160] + "...";
        }

        /// <summary>Diagnostics only: the master navigation items (headless verification).
        /// The Super Admin area carries exactly the four mandated primary items + Logout, including
        /// the top-level Business Intelligence page.</summary>
        internal string DescribeMasterNav() => string.Join(", ", new[]
        {
            "Subscription", "Companies/Tenants", "TenantDatabase", "BusinessIntelligence", "Logout"
        });

        /// <summary>
        /// Headless: the "impatient user" probe across ALL FOUR Super Admin areas. It fires
        /// overlapping loads for every area - Companies/Tenants, Tenant Database, Subscription (all
        /// four sub-sections) and Business Intelligence - through the same gate a sidebar click
        /// uses, three waves in a row, which is what clicking quickly through the sidebar produces.
        /// A DbContext concurrency failure would surface as an exception; a duplicated load would
        /// show up as a tenant-list read delta; a stalled gate would show up in the elapsed time and
        /// in the loaded row counts.
        ///
        /// The probe drives the LOADERS rather than re-parenting four panels in sequence: a headless
        /// process has no message pump, so repeatedly creating and destroying child windows cannot
        /// be finished reliably there. The real navigation path itself is still exercised for the
        /// two areas that need it (Subscription via RenderSubscriptionCompaniesForTestAsync,
        /// Business Intelligence via OpenBusinessIntelligenceForTestAsync) and the other two are
        /// built by their real factories and themed by the theme probe.
        /// </summary>
        internal async Task<string> RunRapidMasterAreaLoadsForTestAsync()
        {
            var loadsBefore = TenantListLoadCount;
            var overviewCount = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // The Subscription screen is built lazily by its nav item, so the probe builds it with
            // the REAL factory first. That constructs the control tree without parenting it - no
            // handle is created, which is the part a headless process cannot finish. A TabControl
            // only reports a SelectedTab once its handle exists, so the default page ("Companies /
            // Tenants", where a first click on the section lands) is selected explicitly before
            // the initial load.
            var subscriptionBuilt = tabSubscription is TabControl subscriptionTabs;
            if (!subscriptionBuilt)
            {
                BuildSubscriptionPanel();
                subscriptionTabs = tabSubscription
                    ?? throw new InvalidOperationException("the Subscription panel factory did not initialize its tab control");
                subscriptionTabs.SelectedTab = subscriptionTabs.TabPages[0];
                await RunMasterOperationAsync(LoadSubscriptionTabDataAsync);
            }

            for (var wave = 0; wave < 3; wave++)
            {
                var companies = RunMasterOperationAsync(async () =>
                    overviewCount = (await _runtime.MasterTenantAdminService.GetCompanyOverviewsAsync()).Count);
                var databases = RunMasterOperationAsync(async () =>
                    overviewCount = (await _runtime.MasterTenantAdminService.GetCompanyOverviewsAsync()).Count);
                var subscription = RunMasterOperationAsync(LoadSubscriptionTabDataAsync);
                var business = RunMasterOperationAsync(LoadBusinessIntelligenceAsync);

                await Task.WhenAll(companies, databases, subscription, business);
            }

            stopwatch.Stop();

            return $"elapsedMs={stopwatch.ElapsedMilliseconds} waves=3 tenantsLoaded={overviewCount} " +
                   $"tenantListLoadDelta={TenantListLoadCount - loadsBefore} " +
                   $"companies={SubscriptionCompanyRowCountForTest} biCharts={BiChartCountForTest} " +
                   $"biKpis='{DescribeBusinessIntelligenceKpisForTest()}'";
        }

        /// <summary>
        /// Headless: builds every Super Admin panel with its REAL factory, normalizes it with the
        /// same theme pass the navigation path uses, and reports any surface still painted with a
        /// light colour plus any drop-down that showed a CLR type name. The Subscription panel is
        /// additionally opened through the real navigation path.
        /// </summary>
        internal async Task<string> ApplyAndDescribeMasterThemeForTestAsync()
        {
            var report = new List<string>();

            // The first panel goes through the REAL navigation path (ShowPanelAsync): it replaces
            // the content host, themes the new panel and awaits its loader.
            //
            // The other three are built by their real factories and normalized by the same theme
            // pass, but are deliberately not parented into the window: creating the first control
            // HANDLE makes WinForms raise this form's Load event, whose handler starts a second
            // gated master operation - harmless in the running application (which pumps messages)
            // but un-finishable in a headless probe with no message loop. The panel tree that is
            // inspected is byte-for-byte the tree the user gets, minus the parenting.
            foreach (var (factory, button, label) in new (Func<Panel> Factory, Button Button, string Label)[]
            {
                (BuildSubscriptionPanel, btnNavSubscription, "Subscription"),
                (BuildCompaniesPanel, btnNavCompanies, "Companies/Tenants"),
                (BuildDatabasesPanel, btnNavDatabases, "TenantDatabase"),
                (BuildBusinessIntelligencePanel, btnNavBusiness, "BusinessIntelligence")
            })
            {
                Panel panel;
                if (label == "Subscription")
                {
                    // Bounded: a loader failure raises a MODAL dialog, which can never be dismissed
                    // in a headless run - report it instead of hanging the battery.
                    if (!ShowPanelAsync(factory, button).Wait(TimeSpan.FromSeconds(30)))
                    {
                        report.Add($"{label}: OPEN-TIMEOUT (headless modal dialog)");
                        continue;
                    }

                    if (pnlContent.Controls.OfType<Panel>().FirstOrDefault() is not { } hosted)
                    {
                        report.Add($"{label}: (no panel hosted)");
                        continue;
                    }

                    panel = hosted;
                }
                else
                {
                    panel = factory();
                    UiTheme.Apply(panel);
                }

                var light = UiTheme.FindLightSurfaces(panel);
                var typeNames = UiTheme.FindTypeNameDisplayValues(panel);

                report.Add($"{label}: light={light.Count}" +
                    (light.Count == 0 ? string.Empty : " [" + string.Join("; ", light) + "]") +
                    $" typename={typeNames.Count}" +
                    (typeNames.Count == 0 ? string.Empty : " [" + string.Join("; ", typeNames) + "]"));
            }

            return string.Join(" || ", report);
        }
    }
}
