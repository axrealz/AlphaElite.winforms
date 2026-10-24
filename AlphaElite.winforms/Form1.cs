using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.Measure;
using SkiaSharp;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;
using Microsoft.EntityFrameworkCore;
using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;

namespace AlphaElite.winforms
{
    public partial class Form1 : Form
    {
        private readonly ITenantDataContextFactory _dataContextFactory;
        private readonly IAuthorizationService _authorization;
        private readonly UserSession _session;
        private readonly IReadOnlyList<TenantOption> _tenants;
        private int _companyId;

        // Phase 4 session scope. _branchId is null for tenant-wide roles (Admin/MasterAdmin);
        // _memberId is set only for member accounts.
        private bool _isBranchAware;
        private int? _branchId;
        private int? _memberId;
        private string _role = Roles.Member;

        // Phase 9: the company's SOFTWARE subscription (read once at login from the master DB).
        // Null for harness/legacy sessions, which makes subscription gating inert.
        private readonly UserSubscriptionContext? _subscription;

        // Guards the tenant switcher while it is being populated at startup so that
        // programmatic selection does not trigger a data reload.
        private bool _suppressTenantSwitch;

        // Phase 2: recorded switcher state for headless verification (Visible is unreliable
        // while the form is unshown). True when the Tenant dropdown was removed entirely
        // (Tenant A / Tenant B sessions).
        private bool _tenantSwitcherHiddenForTest;

        // Module load failures for the current view. A module whose data cannot be read
        // (for example a tenant whose schema predates that module) is reported and
        // skipped instead of aborting dashboard initialisation.
        private readonly List<string> _loadFailures = new();

        private List<Member> _membersCache = new();

        private int? _selectedMemberId;
        private int? _selectedAttendanceId;
        private int? _selectedPaymentId;
        private int? _selectedFeedbackId;
        private int? _selectedInquiryId;
        private int? _selectedSubscriptionId;
        private int? _selectedRetentionRecordId;

        // Dashboard-driven filters (set when a KPI card or chart is clicked;
        // reset to "All" when the user opens the module from the sidebar).
        private string _membersFilter = "All";        // All | Active | Inactive
        private string _attendanceFilter = "All";     // All | Today | ThisWeek
        private string _inquiriesFilter = "All";      // All | Pending
        private int? _feedbackRatingFilter = null;    // null = all ratings

        private readonly ToolTip _dashboardToolTip = new ToolTip();
        private string _loggedInFullName = "Unknown User";
        private string _loggedInRole = "Unknown";

        private class AttendanceRow
        {
            public int AttendanceId { get; set; }
            public string MemberName { get; set; } = string.Empty;
            public DateTime CheckInDate { get; set; }
        }

        private class PaymentRow
        {
            public int PaymentId { get; set; }
            public string MemberName { get; set; } = string.Empty;
            public decimal AmountPaid { get; set; }
            public DateTime PaymentDate { get; set; }
            public DateTime? NextDueDate { get; set; }
            public string? Notes { get; set; }
        }

        private class FeedbackRow
        {
            public int FeedbackId { get; set; }
            public string MemberName { get; set; } = string.Empty;
            public int Rating { get; set; }
            public string? Comments { get; set; }
            public DateTime SubmittedAt { get; set; }
        }

        private class InactiveMemberRow
        {
            public int MemberId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string LastCheckIn { get; set; } = string.Empty;
            public string DaysSince { get; set; } = string.Empty;
        }

        private class DueSoonPaymentRow
        {
            public int MemberId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public DateTime NextDueDate { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        private class SubscriptionRow
        {
            public int SubscriptionId { get; set; }
            public string MemberName { get; set; } = string.Empty;
            public string PlanName { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public string BillingCycle { get; set; } = string.Empty;
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
            public bool IsActive { get; set; }
            public string StatusText { get; set; } = string.Empty;
        }

        private class ExpiringSubscriptionRow
        {
            public int MemberId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string PlanName { get; set; } = string.Empty;
            public DateTime EndDate { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        private class RetentionRecordRow
        {
            public int RetentionRecordId { get; set; }
            public string MemberName { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public string RetentionAction { get; set; } = string.Empty;
            public string? Promotion { get; set; }
            public string Status { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
        }

        public Form1(UserSession session, IReadOnlyList<TenantOption> tenants, ITenantDataContextFactory dataContextFactory, IAuthorizationService authorizationService, ISubscriptionService? subscriptionService = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _dataContextFactory = dataContextFactory ?? throw new ArgumentNullException(nameof(dataContextFactory));
            _authorization = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
            _tenants = tenants ?? Array.Empty<TenantOption>();

            _companyId = session.CompanyId;
            _isBranchAware = session.IsBranchAware;
            _role = _authorization.NormalizeRole(session.Role);

            // Branch scope comes from the ROLE, not merely from the stored BranchId: Admin /
            // MasterAdmin are tenant-wide (even when the account row carries a home branch),
            // while Manager / GymStaff are restricted to their own branch. Member accounts are
            // further restricted to their own MemberId by ApplyScope.
            _branchId = Roles.IsBranchScopedByDefault(_role) ? session.BranchId : null;
            // Home branch is remembered so the Current Branch selector can fall back to it
            // (or to the first active branch) when the stored home branch is inactive.
            _homeBranchId = _branchId;
            _memberId = session.MemberId;
            _loggedInFullName = session.FullName;
            _loggedInRole = _role;
            _subscription = session.Subscription;

            InitializeComponent();

            InitializePhase4();

            InitializePhase6Controls();

            InitializePhase8Controls();

            // Phase 5: the company's own subscription screen (master-DB read via the
            // subscription service; the tenant window never opens the master database itself).
            InitializePhase5Controls(subscriptionService);

            PopulateTenantSwitcher();
            SetupDashboardInteractions();

            Text = $"Alpha Elite Management - {session.FullName} ({_role})";
            lblHeaderUserName.Text = $"Welcome, {session.FullName}";
            lblHeaderUserRole.Text = $"Role: {_role}";

            // Phase 8 controls exist now; align their visibility with the role that was
            // already resolved by InitializePhase4's ApplyRoleVisibility().
            ApplyPhase8Visibility();

            // Phase 9: apply SOFTWARE-subscription gating on top of the role visibility.
            // Runs last so every control created by the phase partials exists.
            ApplySubscriptionVisibility();

            // Phase 5/6: the "My Subscription" entry is only offered to the company Admin.
            ApplySoftwareSubscriptionVisibility();

            // Phase 2: stack the remaining visible nav buttons with uniform spacing so a
            // hidden module never leaves a gap in the sidebar.
            ReflowNavButtons();

            // Visual only (no behaviour change): the legacy screens were built on WinForms'
            // default LIGHT surfaces. This normalizes every control that still relies on them
            // onto the Alpha Elite dark/gold palette, and repairs unreadable text. It runs last
            // so every control created by the phase partials exists.
            UiTheme.Apply(this);
        }

        /// <summary>
        /// Phase 2 + Tenant C phase 5: the tenant is fixed by the login credentials, so
        /// there is no longer a usable tenant selector in the header. No tenant session
        /// (Tenant A / B / C) gets a Tenant dropdown at all; the label goes with it so no
        /// orphaned "Tenant:" text is left in the header.
        /// </summary>
        private void PopulateTenantSwitcher()
        {
            _suppressTenantSwitch = true;

            cboTenantSwitcher.Items.Clear();

            var current = _tenants.FirstOrDefault(t => t.CompanyId == _companyId);
            if (current != null)
            {
                cboTenantSwitcher.Items.Add(current);
                cboTenantSwitcher.SelectedIndex = 0;
            }

            // Locked in every case: selecting another tenant is no longer possible.
            cboTenantSwitcher.Enabled = false;

            if (!_isBranchAware || IsTenantC)
            {
                // Tenant A / Tenant B (phase 2) and Tenant C (phase 5): remove the Tenant
                // dropdown entirely (user requirement). The label goes with it so no
                // orphaned "Tenant:" text is left in the header.
                lblTenantSwitcher.Visible = false;
                cboTenantSwitcher.Visible = false;
                _tenantSwitcherHiddenForTest = true;
            }

            _suppressTenantSwitch = false;
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            cboFeedbackRating.DataSource = new List<int> { 1, 2, 3, 4, 5 };

            if (DashboardHiddenForAdminA)
            {
                // Tenant A has no Dashboard: open on the first module Admin A can actually use.
                tabControlMain.SelectedTab = tabMembers;
                SetActiveNavButton(btnNavMembers);
            }
            else
            {
                tabControlMain.SelectedTab = tabDashboard;
                SetActiveNavButton(btnNavDashboard);
            }

            // The Current Branch selector must be populated before the first module load so
            // every scoped query uses the branch the user actually starts on.
            await InitializeCurrentBranchSelectorAsync();

            // Phase 9: an Expired/Suspended subscription is informational - login still works,
            // data is untouched, but the plan-gated modules have already been hidden.
            ShowSubscriptionBannerIfDegraded();

            await LoadAllModulesAsync();
        }

        /// <summary>
        /// Diagnostics only: describes the populated tenant switcher so startup can be
        /// verified without a GUI interaction.
        /// </summary>
        internal string DescribeTenantSwitcher()
        {
            return $"items={cboTenantSwitcher.Items.Count}, selected={cboTenantSwitcher.SelectedItem ?? "(none)"}";
        }

        /// <summary>
        /// Loads every module, isolating failures so one unreadable module cannot abort
        /// dashboard initialisation.
        /// </summary>
        /// <summary>
        /// Serialises module loads. The Current Branch selector is usable while the previous load
        /// is still running, so without this two loads could interleave, re-bind the same grids and
        /// charts, and let a stale load finish last - overwriting the newly selected branch's data.
        /// Only one load runs at a time and an obsolete one stops as soon as it can.
        /// </summary>
        private readonly SemaphoreSlim _moduleLoadGate = new SemaphoreSlim(1, 1);
        private int _moduleLoadGeneration;

        /// <summary>True when a newer module load has been requested since <paramref name="generation"/>.</summary>
        private bool IsSuperseded(int generation) => generation != Volatile.Read(ref _moduleLoadGeneration);

        private async Task LoadAllModulesAsync()
        {
            var generation = Interlocked.Increment(ref _moduleLoadGeneration);

            if (!await _moduleLoadGate.WaitAsync(TimeSpan.FromSeconds(120)))
            {
                return;
            }

            try
            {
                // A newer load is already queued/in flight: it will bind everything correctly.
                if (IsSuperseded(generation))
                {
                    return;
                }

                _loadFailures.Clear();

                // Only modules the signed-in role is permitted to use AND the company's SOFTWARE
                // subscription includes are loaded at all, so an unauthorized or unlicensed
                // module is never queried (not merely hidden). Subscription composes WITH the
                // role: neither alone can grant a module.
                if (IsSuperseded(generation)) return;
                if ((HasPermission(Permission.ManageMembers) || HasPermission(Permission.SelfService)) && ModuleEnabled(SoftwareModuleCodes.Members))
                    await SafeLoadAsync("Members", LoadMembersAsync, dgvMembers);

                if (IsSuperseded(generation)) return;
                if ((HasPermission(Permission.ManageAttendance) || HasPermission(Permission.SelfService)) && ModuleEnabled(SoftwareModuleCodes.Attendance))
                    await SafeLoadAsync("Attendance", LoadAttendanceAsync, dgvAttendance);

                if (IsSuperseded(generation)) return;
                if ((HasPermission(Permission.ManagePayments) || HasPermission(Permission.SelfService)) && ModuleEnabled(SoftwareModuleCodes.Payments))
                    await SafeLoadAsync("Payments", LoadPaymentsAsync, dgvPayments);

                if (IsSuperseded(generation)) return;
                if ((HasPermission(Permission.ManageFeedback) || HasPermission(Permission.SubmitFeedback)) && ModuleEnabled(SoftwareModuleCodes.Feedback))
                    await SafeLoadAsync("Feedback", LoadFeedbackAsync, dgvFeedback);

                if (IsSuperseded(generation)) return;
                if (HasPermission(Permission.ManageInquiries) && ModuleEnabled(SoftwareModuleCodes.Inquiries))
                    await SafeLoadAsync("Inquiries", LoadInquiriesAsync, dgvInquiries);

                // The dashboard (KPIs + charts) is refreshed IMMEDIATELY after the grids it derives
                // from, so a branch switch is never left showing the previous branch's numbers.
                if (IsSuperseded(generation)) return;
                if (HasPermission(Permission.ViewDashboard) && ModuleEnabled(SoftwareModuleCodes.Dashboard) && !DashboardHiddenForAdminA)
                    await SafeLoadAsync("Dashboard", LoadDashboardAsync);

                if (IsSuperseded(generation)) return;
                if (HasPermission(Permission.ManageSubscriptions) && ModuleEnabled(SoftwareModuleCodes.Subscriptions))
                    await SafeLoadAsync("Subscriptions", LoadSubscriptionsAsync, dgvSubscriptions);

                if (IsSuperseded(generation)) return;
                if (HasPermission(Permission.ManageRetention) && ModuleEnabled(SoftwareModuleCodes.Retention))
                    await SafeLoadAsync("Retention", LoadRetentionAsync, dgvRetentionRecords);

                if (IsSuperseded(generation)) return;
                if ((HasPermission(Permission.ManageComplaints) || HasPermission(Permission.SubmitComplaint)) && ModuleEnabled(SoftwareModuleCodes.Complaints))
                    await SafeLoadAsync("Complaints", LoadComplaintsAsync, dgvComplaints);

                if (IsSuperseded(generation)) return;
                if ((_isBranchAware || IsTenantB) && HasPermission(Permission.ManageUsers) && ModuleEnabled(SoftwareModuleCodes.UserManagement))
                    await SafeLoadAsync("Users", LoadUsersAsync, dgvUsers);

                if (IsSuperseded(generation)) return;
                if (CanManageBranches && ModuleEnabled(SoftwareModuleCodes.BranchManagement))
                    await SafeLoadAsync("Branches", LoadBranchesGridAsync, dgvBranches);

                if (IsSuperseded(generation)) return;
            }
            finally
            {
                _moduleLoadGate.Release();
            }

            if (_loadFailures.Count > 0 && !SuppressDialogs)
            {
                MessageBox.Show(
                    "Some modules could not be loaded:"
                        + Environment.NewLine + Environment.NewLine
                        + string.Join(Environment.NewLine, _loadFailures),
                    "Alpha Elite",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private async Task SafeLoadAsync(string moduleName, Func<Task> load, DataGridView? grid = null)
        {
            try
            {
                await load();
            }
            catch (Exception ex)
            {
                if (grid != null)
                {
                    grid.DataSource = null;
                }

                _loadFailures.Add($"{moduleName}: {ex.Message}");
            }
        }

        // ============================================================
        // TENANT SWITCHER
        // ============================================================

        private async void cboTenantSwitcher_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressTenantSwitch)
            {
                return;
            }

            if (cboTenantSwitcher.SelectedItem is not TenantOption tenant)
            {
                return; // No valid tenant selected.
            }

            // Phase 2: the tenant comes from the authenticated session and can NEVER be
            // changed in-place - any selection of a different company is ignored so no
            // code path can re-point this window at another tenant's data.
            if (tenant.CompanyId != _companyId)
            {
                return;
            }

            await LoadAllModulesAsync();
        }

        /// <summary>
        /// 0 = idle, 1 = a Refresh run is in progress. Interlocked so the guard is correct
        /// even if a trigger ever arrives off the UI thread.
        /// </summary>
        private int _refreshInProgress;

        private async void btnRefreshDashboard_Click(object sender, EventArgs e)
        {
            await RunDashboardRefreshAsync();
        }

        /// <summary>
        /// The exact code path behind the Refresh button (re-binds every view like a branch
        /// switch does). ROOT-CAUSE layering for the BI "charts disappear" bug:
        ///  1. Every tenant-context creation funnels through the (now serialized) shared
        ///     master resolver, so a Refresh can no longer abort with the EF
        ///     "second operation was started on this context instance" error mid-load,
        ///     which previously stopped LoadDashboardAsync before the charts were re-bound.
        ///  2. Re-entrancy guard: while a Refresh runs, the button is disabled and any
        ///     further trigger (rapid clicks) is a no-op instead of queueing additional
        ///     overlapping full reloads. The guard is always released in finally.
        /// </summary>
        internal async Task RunDashboardRefreshAsync()
        {
            if (Interlocked.CompareExchange(ref _refreshInProgress, 1, 0) != 0)
            {
                return; // A Refresh is already running: this overlapping trigger is ignored.
            }

            btnRefreshDashboard.Enabled = false;
            try
            {
                ForceContentRepaint();
                await LoadAllModulesAsync();
                ForceContentRepaint();
            }
            finally
            {
                btnRefreshDashboard.Enabled = true;
                Interlocked.Exchange(ref _refreshInProgress, 0);
            }
        }

        // ============================================================
        // SIDEBAR NAVIGATION
        // ============================================================

        /// <summary>
        /// Phase 2: stacks the nav buttons that are actually visible for this session,
        /// top to bottom with the designer's uniform step (100 + 44*i). Hidden modules
        /// (role-restricted or plan-gated) therefore leave no gap in the menu.
        /// Visibility is read from the recorded decisions, not Control.Visible, so the
        /// reflow is deterministic headless as well.
        /// </summary>
        private void ReflowNavButtons()
        {
            var ordered = new Button?[]
            {
                btnNavDashboard, btnNavMembers, btnNavAttendance, btnNavPayments,
                btnNavFeedback, btnNavInquiries, btnNavSubscriptions, btnNavRetention,
                btnNavComplaints, btnNavUserManagement, btnNavBranches, btnNavSoftwareSubscription
            };

            const int startY = 100;
            const int step = 44;
            var y = startY;

            foreach (var button in ordered)
            {
                if (button == null || !_visibleNavButtons.Contains(button))
                {
                    continue;
                }

                button.Location = new Point(button.Location.X, y);
                y += step;
            }
        }

        /// <summary>Diagnostics only: "label@y" for every visible nav button, in sidebar order.</summary>
        internal string DescribeNavLayoutForTest()
        {
            var ordered = new (Button? button, string label)[]
            {
                // Read the LIVE button text: the dashboard button is relabeled
                // "Business Intelligence" for Tenant B / Tenant C.
                (btnNavDashboard, btnNavDashboard.Text.Trim()),
                (btnNavMembers, btnNavMembers.Text.Trim()),
                (btnNavAttendance, btnNavAttendance.Text.Trim()),
                (btnNavPayments, btnNavPayments.Text.Trim()),
                (btnNavFeedback, "Feedback"),
                (btnNavInquiries, "Inquiries"),
                (btnNavSubscriptions, "Subscriptions"),
                (btnNavRetention, "Retention"),
                (btnNavComplaints, "Complaints"),
                (btnNavUserManagement, "UserManagement"),
                (btnNavBranches, "Branches"),
                // Phase 5: label read live; the entry only exists for the company Admin, so
                // this never changes the nav string of any other role/tenant.
                (btnNavSoftwareSubscription, btnNavSoftwareSubscription?.Text.Trim() ?? "My Subscription")
            };

            return string.Join(", ", ordered
                .Where(x => x.button != null && _visibleNavButtons.Contains(x.button))
                .Select(x => $"{x.label}@{x.button!.Location.Y}"));
        }

        /// <summary>Diagnostics only: true when the header Tenant dropdown was removed (Tenant A / B).</summary>
        internal bool TenantSwitcherHiddenForTest => _tenantSwitcherHiddenForTest;

        /// <summary>Diagnostics only: modules whose data could not be loaded in the current view.</summary>
        internal IReadOnlyList<string> LoadFailuresForTest => _loadFailures;

        private void SetActiveNavButton(Button activeButton)
        {
            var allNavButtons = new[]
            {
                btnNavDashboard, btnNavMembers, btnNavAttendance, btnNavPayments,
                btnNavFeedback, btnNavInquiries, btnNavSubscriptions, btnNavRetention,
                btnNavComplaints, btnNavUserManagement
            };

            foreach (var button in allNavButtons)
            {
                bool isActive = button == activeButton;
                button.BackColor = isActive
                    ? System.Drawing.Color.FromArgb(38, 38, 38)
                    : System.Drawing.Color.FromArgb(24, 24, 24);
                button.ForeColor = isActive
                    ? System.Drawing.Color.FromArgb(212, 175, 55)
                    : System.Drawing.Color.White;
            }
        }

        private void btnNavDashboard_Click(object sender, EventArgs e)
        {
            tabControlMain.SelectedTab = tabDashboard;
            SetActiveNavButton(btnNavDashboard);
        }

        private async void btnNavMembers_Click(object sender, EventArgs e)
        {
            await OpenMembersAsync("All");
        }

        private async void btnNavAttendance_Click(object sender, EventArgs e)
        {
            await OpenAttendanceAsync("All");
        }

        private void btnNavPayments_Click(object sender, EventArgs e)
        {
            tabControlMain.SelectedTab = tabPayments;
            SetActiveNavButton(btnNavPayments);
        }

        private async void btnNavFeedback_Click(object sender, EventArgs e)
        {
            await OpenFeedbackAsync(null);
        }

        private async void btnNavInquiries_Click(object sender, EventArgs e)
        {
            await OpenInquiriesAsync("All");
        }

        private void btnNavSubscriptions_Click(object sender, EventArgs e)
        {
            tabControlMain.SelectedTab = tabSubscriptions;
            SetActiveNavButton(btnNavSubscriptions);
        }

        private void btnNavRetention_Click(object sender, EventArgs e)
        {
            tabControlMain.SelectedTab = tabRetention;
            SetActiveNavButton(btnNavRetention);
        }

        // ============================================================
        // DASHBOARD INTERACTIONS (clickable KPI cards + chart click-through)
        // ============================================================

        private static readonly System.Drawing.Color KpiNormalBack = System.Drawing.Color.FromArgb(28, 28, 28);
        private static readonly System.Drawing.Color KpiHoverBack = System.Drawing.Color.FromArgb(50, 46, 30);
        private static readonly System.Drawing.Color KpiPressedBack = System.Drawing.Color.FromArgb(20, 20, 20);
        private static readonly System.Drawing.Color KpiNormalFore = System.Drawing.Color.FromArgb(212, 175, 55);
        private static readonly System.Drawing.Color KpiHoverFore = System.Drawing.Color.FromArgb(245, 215, 110);

        private void MakeKpiClickable(Label card, string hint, Func<Task> onClick)
        {
            card.Cursor = Cursors.Hand;
            _dashboardToolTip.SetToolTip(card, hint);

            card.MouseEnter += (s, e) => { card.BackColor = KpiHoverBack; card.ForeColor = KpiHoverFore; };
            card.MouseLeave += (s, e) => { card.BackColor = KpiNormalBack; card.ForeColor = KpiNormalFore; };
            card.MouseDown += (s, e) => { card.BackColor = KpiPressedBack; };
            card.MouseUp += (s, e) => { card.BackColor = KpiHoverBack; };

            card.Click += async (s, e) =>
            {
                try
                {
                    await onClick();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not open the selected view: " + ex.Message);
                }
            };
        }

        private void SetupDashboardInteractions()
        {
            MakeKpiClickable(lblKpiTotalMembers, "Click to view all members", () => OpenMembersAsync("All"));
            MakeKpiClickable(lblKpiActiveMembers, "Click to view active members", () => OpenMembersAsync("Active"));
            MakeKpiClickable(lblKpiInactiveMembers, "Click to view inactive members", () => OpenMembersAsync("Inactive"));
            MakeKpiClickable(lblKpiAvgRating, "Click to view feedback", () => OpenFeedbackAsync(null));
            MakeKpiClickable(lblKpiAttendanceToday, "Click to view today's attendance", () => OpenAttendanceAsync("Today"));
            MakeKpiClickable(lblKpiAttendanceWeek, "Click to view this week's attendance", () => OpenAttendanceAsync("ThisWeek"));
            MakeKpiClickable(lblKpiPendingInquiries, "Click to view inquiries that need follow-up", () => OpenInquiriesAsync("Pending"));

            // Donut chart: clicking a slice opens Members filtered to that status.
            chartActiveVsInactive.DataPointerDown += async (chart, points) =>
            {
                var point = points.FirstOrDefault();
                if (point == null) return;

                string? sliceName = point.Context.Series.Name;
                if (sliceName == "Active") await OpenMembersAsync("Active");
                else if (sliceName == "Inactive") await OpenMembersAsync("Inactive");
            };

            // Feedback bars: clicking a bar opens Feedback filtered to that star rating.
            // Bars are plotted in order 1..5, so the bar's position gives the rating.
            chartFeedbackDistribution.DataPointerDown += async (chart, points) =>
            {
                var point = points.FirstOrDefault();
                if (point == null) return;

                int rating = (int)Math.Round(point.Coordinate.SecondaryValue) + 1;
                if (rating >= 1 && rating <= 5) await OpenFeedbackAsync(rating);
            };

            // BI chart visibility lifecycle: every tab change routes through the root-cause
            // fix in Form1.Phase8.cs (OnDashboardTabSelected), which re-issues a dropped
            // measure when the dashboard tab becomes visible again.
            tabControlMain.SelectedIndexChanged += (_, _) => OnDashboardTabSelected();
        }

        private void UpdateFilterLabels()
        {
            lblMembersFilterInfo.Text = _membersFilter switch
            {
                "Active" => "Filter: active members only (click Members in the sidebar to show all)",
                "Inactive" => "Filter: inactive members only (click Members in the sidebar to show all)",
                _ => string.Empty
            };

            lblAttendanceFilterInfo.Text = _attendanceFilter switch
            {
                "Today" => "Filter: today's attendance (click Attendance in the sidebar to show all)",
                "ThisWeek" => "Filter: this week's attendance (click Attendance in the sidebar to show all)",
                _ => string.Empty
            };

            lblFeedbackFilterInfo.Text = _feedbackRatingFilter.HasValue
                ? $"Filter: {_feedbackRatingFilter.Value}-star feedback (click Feedback in the sidebar to show all)"
                : string.Empty;

            lblInquiriesFilterInfo.Text = _inquiriesFilter == "Pending"
                ? "Filter: inquiries needing follow-up (click Inquiries in the sidebar to show all)"
                : string.Empty;
        }

        private async Task OpenMembersAsync(string filter)
        {
            if (!RequireAny(Permission.ManageMembers, Permission.SelfService)) return;

            _membersFilter = filter;
            UpdateFilterLabels();
            await LoadMembersAsync();
            tabControlMain.SelectedTab = tabMembers;
            SetActiveNavButton(btnNavMembers);
        }

        private async Task OpenAttendanceAsync(string filter)
        {
            if (!RequireAny(Permission.ManageAttendance, Permission.SelfService)) return;

            _attendanceFilter = filter;
            UpdateFilterLabels();
            await LoadAttendanceAsync();
            tabControlMain.SelectedTab = tabAttendance;
            SetActiveNavButton(btnNavAttendance);
        }

        private async Task OpenFeedbackAsync(int? rating)
        {
            if (!RequireAny(Permission.ManageFeedback, Permission.SubmitFeedback)) return;

            _feedbackRatingFilter = rating;
            UpdateFilterLabels();
            await LoadFeedbackAsync();
            tabControlMain.SelectedTab = tabFeedback;
            SetActiveNavButton(btnNavFeedback);
        }

        private async Task OpenInquiriesAsync(string filter)
        {
            if (!Require(Permission.ManageInquiries)) return;

            _inquiriesFilter = filter;
            UpdateFilterLabels();
            await LoadInquiriesAsync();
            tabControlMain.SelectedTab = tabInquiries;
            SetActiveNavButton(btnNavInquiries);
        }

        private void btnLogout_Click(object sender, EventArgs e)
        {
            var confirm = MessageBox.Show("Are you sure you want to log out?", "Confirm Logout",
                MessageBoxButtons.YesNo);

            if (confirm == DialogResult.Yes)
            {
                RequestLogout();
            }
        }

        /// <summary>
        /// Ends the tenant session and returns to the login form. The window is closed with
        /// <see cref="DialogResult.Retry"/>, which the application entry point treats as
        /// "show the login form again" - exactly like the Master Admin window. The process is
        /// never terminated, so the user can sign in again immediately; all session, branch and
        /// member scope held by this window is cleared first and the window is then disposed.
        /// </summary>
        internal void RequestLogout()
        {
            ClearSessionState();
            DialogResult = DialogResult.Retry;
            Close();
        }

        /// <summary>
        /// Clears everything that must not survive a logout: the branch/member scope this window
        /// was operating in, the row selections, the cached member list and every bound grid.
        /// </summary>
        private void ClearSessionState()
        {
            _branchId = null;
            _homeBranchId = null;
            _memberId = null;
            _branchName = string.Empty;

            _selectedMemberId = null;
            _selectedAttendanceId = null;
            _selectedPaymentId = null;
            _selectedFeedbackId = null;
            _selectedInquiryId = null;
            _selectedSubscriptionId = null;
            _selectedRetentionRecordId = null;
            _selectedComplaintId = null;
            _selectedUserAccountId = null;
            _selectedBranchId = null;

            _membersCache = new List<Member>();
            _loadFailures.Clear();

            ClearTenantGrids();
        }

        /// <summary>Unbinds every data grid so no row of the previous session stays visible.</summary>
        private void ClearTenantGrids()
        {
            dgvMembers.DataSource = null;
            dgvAttendance.DataSource = null;
            dgvPayments.DataSource = null;
            dgvFeedback.DataSource = null;
            dgvInquiries.DataSource = null;
            dgvSubscriptions.DataSource = null;
            dgvRetentionRecords.DataSource = null;
            if (dgvComplaints != null) dgvComplaints.DataSource = null;
            if (dgvUsers != null) dgvUsers.DataSource = null;
            if (dgvBranches != null) dgvBranches.DataSource = null;
        }

        // ============================================================
        // MEMBERS
        // ============================================================

        private async Task LoadMembersAsync()
        {
            await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            _membersCache = await ApplyScope(tenantDb.Members.AsNoTracking(), tenantDb, x => x.BranchId, x => x.MemberId)
                .OrderBy(x => x.MemberId)
                .ToListAsync();

            IEnumerable<Member> displayMembers = _membersFilter switch
            {
                "Active" => _membersCache.Where(x => x.IsActive),
                "Inactive" => _membersCache.Where(x => !x.IsActive),
                _ => _membersCache
            };

            if (_memberSearch.Length > 0)
            {
                displayMembers = displayMembers.Where(x =>
                    MatchesSearch(x.FullName, _memberSearch) ||
                    MatchesSearch(x.MemberCode, _memberSearch) ||
                    MatchesSearch(x.Email, _memberSearch));
            }

            dgvMembers.DataSource = null;
            dgvMembers.DataSource = displayMembers.ToList();

            // Tenant A / Tenant B phase (non-branching tenants): the Members grid shows NO
            // BranchId column - branch columns belong to Tenant C only. Re-applied after
            // every bind because AutoGenerateColumns rebuilds the columns each time.
            if (!_isBranchAware && dgvMembers.Columns["BranchId"] is { } branchColumn)
            {
                branchColumn.Visible = false;
            }

            if (_role == Roles.Member)
            {
                await LoadMemberSummaryAsync(tenantDb);
            }

            await PopulateMemberBranchPickerAsync(tenantDb);

            cboAttendanceMember.DataSource = new List<Member>(_membersCache);
            cboAttendanceMember.DisplayMember = "FullName";
            cboAttendanceMember.ValueMember = "MemberId";

            cboPaymentMember.DataSource = new List<Member>(_membersCache);
            cboPaymentMember.DisplayMember = "FullName";
            cboPaymentMember.ValueMember = "MemberId";

            cboFeedbackMember.DataSource = new List<Member>(_membersCache);
            cboFeedbackMember.DisplayMember = "FullName";
            cboFeedbackMember.ValueMember = "MemberId";

            cboSubscriptionMember.DataSource = new List<Member>(_membersCache);
            cboSubscriptionMember.DisplayMember = "FullName";
            cboSubscriptionMember.ValueMember = "MemberId";

            cboRetentionMember.DataSource = new List<Member>(_membersCache);
            cboRetentionMember.DisplayMember = "FullName";
            cboRetentionMember.ValueMember = "MemberId";
        }

        private void dgvMembers_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var member = (Member)dgvMembers.Rows[e.RowIndex].DataBoundItem;

            _selectedMemberId = member.MemberId;
            txtMemberCode.Text = member.MemberCode;
            txtMemberFullName.Text = member.FullName;
            txtMemberEmail.Text = member.Email;
            chkMemberIsActive.Checked = member.IsActive;
            dtpMemberCreatedAt.Value = member.CreatedAt;
        }

        private async void btnMemberAdd_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageMembers)) return;
            if (string.IsNullOrWhiteSpace(txtMemberCode.Text) ||
                string.IsNullOrWhiteSpace(txtMemberFullName.Text) ||
                string.IsNullOrWhiteSpace(txtMemberEmail.Text))
            {
                MessageBox.Show("Member Code, Full Name, and Email are required.");
                return;
            }

            // Branch-aware tenants: assign the member to a branch at creation so
            // branch-scoped staff can actually see (and manage) them.
            int? newMemberBranchId = null;
            if (_isBranchAware)
            {
                newMemberBranchId = _branchId ?? ParseBranchPicker(cboMemberBranch);
                if (!newMemberBranchId.HasValue)
                {
                    MessageBox.Show("Select a branch for the new member.");
                    return;
                }
            }

            var member = new Member
            {
                MemberCode = txtMemberCode.Text.Trim(),
                FullName = txtMemberFullName.Text.Trim(),
                Email = txtMemberEmail.Text.Trim(),
                IsActive = chkMemberIsActive.Checked,
                CreatedAt = dtpMemberCreatedAt.Value,
                BranchId = newMemberBranchId
            };

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                tenantDb.Members.Add(member);
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Member added.");
                ClearMemberInputs();
                await LoadMembersAsync();
                await LoadDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnMemberUpdate_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageMembers)) return;
            if (_selectedMemberId == null)
            {
                MessageBox.Show("Select a member first.");
                return;
            }

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var member = await tenantDb.Members
                    .FirstOrDefaultAsync(x => x.MemberId == _selectedMemberId);

                if (member == null)
                {
                    MessageBox.Show("Member not found.");
                    return;
                }

                EnsureBranchAccess(member.BranchId);
                EnsureOwnMember(member.MemberId);

                member.MemberCode = txtMemberCode.Text.Trim();
                member.FullName = txtMemberFullName.Text.Trim();
                member.Email = txtMemberEmail.Text.Trim();
                member.IsActive = chkMemberIsActive.Checked;
                member.CreatedAt = dtpMemberCreatedAt.Value;

                // Branch reassignment only for tenant-wide roles (Admin/MasterAdmin).
                if (_isBranchAware && !_branchId.HasValue)
                {
                    member.BranchId = ParseBranchPicker(cboMemberBranch) ?? member.BranchId;
                }

                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Member updated.");
                ClearMemberInputs();
                await LoadMembersAsync();
                await LoadDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnMemberDelete_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageMembers)) return;
            if (_selectedMemberId == null)
            {
                MessageBox.Show("Select a member first.");
                return;
            }

            var confirm = MessageBox.Show("Delete this member?", "Confirm",
                MessageBoxButtons.YesNo);
            if (confirm != DialogResult.Yes) return;

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var member = await tenantDb.Members.FirstOrDefaultAsync(x => x.MemberId == _selectedMemberId);
                if (member == null)
                {
                    MessageBox.Show("Member not found.");
                    return;
                }

                EnsureBranchAccess(member.BranchId);
                EnsureOwnMember(member.MemberId);

                tenantDb.Members.Remove(member);
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Member deleted.");
                ClearMemberInputs();
                await LoadMembersAsync();
                await LoadDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private void btnMemberClear_Click(object sender, EventArgs e)
        {
            ClearMemberInputs();
        }

        private void ClearMemberInputs()
        {
            _selectedMemberId = null;
            txtMemberCode.Text = string.Empty;
            txtMemberFullName.Text = string.Empty;
            txtMemberEmail.Text = string.Empty;
            chkMemberIsActive.Checked = true;
            dtpMemberCreatedAt.Value = DateTime.Now;
        }

        // ============================================================
        // ATTENDANCE
        // ============================================================

        private async Task LoadAttendanceAsync()
        {
            await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            var attendanceQuery = ApplyScope(tenantDb.Attendances.AsNoTracking().Include(x => x.Member), tenantDb, x => x.BranchId, x => x.MemberId);

            if (_attendanceFilter == "Today")
            {
                var todayDate = DateTime.Today;
                attendanceQuery = attendanceQuery.Where(x => x.CheckInDate.Date == todayDate);
            }
            else if (_attendanceFilter == "ThisWeek")
            {
                var todayDate = DateTime.Today;
                int daysFromMonday = ((int)todayDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                var weekStart = todayDate.AddDays(-daysFromMonday);
                attendanceQuery = attendanceQuery.Where(x => x.CheckInDate.Date >= weekStart);
            }

            var records = await attendanceQuery
                .OrderByDescending(x => x.CheckInDate)
                .Select(x => new AttendanceRow
                {
                    AttendanceId = x.AttendanceId,
                    MemberName = x.Member != null ? x.Member.FullName : "(unknown)",
                    CheckInDate = x.CheckInDate
                })
                .ToListAsync();

            if (_attendanceSearch.Length > 0)
            {
                records = records.Where(x => MatchesSearch(x.MemberName, _attendanceSearch)).ToList();
            }

            dgvAttendance.DataSource = null;
            dgvAttendance.DataSource = records;
        }

        private void dgvAttendance_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var row = (AttendanceRow)dgvAttendance.Rows[e.RowIndex].DataBoundItem;
            _selectedAttendanceId = row.AttendanceId;
            dtpAttendanceCheckIn.Value = row.CheckInDate;

            var member = _membersCache.FirstOrDefault(x => x.FullName == row.MemberName);
            if (member != null)
            {
                cboAttendanceMember.SelectedValue = member.MemberId;
            }
        }

        private async void btnAttendanceAdd_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageAttendance)) return;
            if (cboAttendanceMember.SelectedValue == null)
            {
                MessageBox.Show("Select a member first.");
                return;
            }

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

                var selectedMemberId = (int)cboAttendanceMember.SelectedValue;
                var selectedMember = await tenantDb.Members.AsNoTracking().FirstOrDefaultAsync(m => m.MemberId == selectedMemberId);
                if (selectedMember == null)
                {
                    MessageBox.Show("The selected member no longer exists.");
                    return;
                }

                EnsureBranchAccess(selectedMember.BranchId);
                EnsureOwnMember(selectedMemberId);

                // Phase 8: the Current Branch (when one is selected) is the authoritative
                // stamp, so a tenant-wide Admin working in a branch context writes to it.
                await EnsureCurrentBranchActiveAsync(tenantDb);

                var checkInDate = dtpAttendanceCheckIn.Value;

                // Duplicate check-in guard: one record per member per day.
                var alreadyCheckedIn = await tenantDb.Attendances
                    .AnyAsync(a => a.MemberId == selectedMemberId && a.CheckInDate.Date == checkInDate.Date);
                if (alreadyCheckedIn)
                {
                    MessageBox.Show($"{selectedMember.FullName} already has a check-in for {checkInDate:MMM d, yyyy}. " +
                        "Select the existing record and use Update to change it.");
                    return;
                }

                tenantDb.Attendances.Add(new Attendance
                {
                    MemberId = selectedMemberId,
                    CheckInDate = checkInDate,
                    BranchId = ResolveWriteBranch(selectedMember.BranchId)
                });
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Attendance recorded.");
                ClearAttendanceInputs();
                await LoadAttendanceAsync();
                await LoadDashboardAsync();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnAttendanceUpdate_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageAttendance)) return;
            if (_selectedAttendanceId == null)
            {
                MessageBox.Show("Select an attendance record first.");
                return;
            }

            if (cboAttendanceMember.SelectedValue == null)
            {
                MessageBox.Show("Select a member first.");
                return;
            }

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var attendance = await tenantDb.Attendances
                    .FirstOrDefaultAsync(x => x.AttendanceId == _selectedAttendanceId);

                if (attendance == null)
                {
                    MessageBox.Show("Attendance record not found.");
                    return;
                }

                EnsureBranchAccess(attendance.BranchId);
                EnsureOwnMember(attendance.MemberId);

                attendance.MemberId = (int)cboAttendanceMember.SelectedValue;
                attendance.CheckInDate = dtpAttendanceCheckIn.Value;

                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Attendance updated.");
                ClearAttendanceInputs();
                await LoadAttendanceAsync();
                await LoadDashboardAsync();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnAttendanceDelete_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageAttendance)) return;
            if (_selectedAttendanceId == null)
            {
                MessageBox.Show("Select an attendance record first.");
                return;
            }

            var confirm = MessageBox.Show("Delete this attendance record?", "Confirm",
                MessageBoxButtons.YesNo);
            if (confirm != DialogResult.Yes) return;

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var attendance = await tenantDb.Attendances
                    .FirstOrDefaultAsync(x => x.AttendanceId == _selectedAttendanceId);

                if (attendance == null)
                {
                    MessageBox.Show("Attendance record not found.");
                    return;
                }

                EnsureBranchAccess(attendance.BranchId);
                EnsureOwnMember(attendance.MemberId);

                tenantDb.Attendances.Remove(attendance);
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Attendance deleted.");
                ClearAttendanceInputs();
                await LoadAttendanceAsync();
                await LoadDashboardAsync();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private void btnAttendanceClear_Click(object sender, EventArgs e)
        {
            ClearAttendanceInputs();
        }

        private void ClearAttendanceInputs()
        {
            _selectedAttendanceId = null;
            if (cboAttendanceMember.Items.Count > 0)
                cboAttendanceMember.SelectedIndex = -1;
            dtpAttendanceCheckIn.Value = DateTime.Now;
        }

        // ============================================================
        // PAYMENTS
        // ============================================================

        private async Task LoadPaymentsAsync()
        {
            await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            var records = await ApplyScope(tenantDb.Payments.AsNoTracking().Include(x => x.Member), tenantDb, x => x.BranchId, x => x.MemberId)
                .OrderByDescending(x => x.PaymentDate)
                .Select(x => new PaymentRow
                {
                    PaymentId = x.PaymentId,
                    MemberName = x.Member != null ? x.Member.FullName : "(unknown)",
                    AmountPaid = x.AmountPaid,
                    PaymentDate = x.PaymentDate,
                    NextDueDate = x.NextDueDate,
                    Notes = x.Notes
                })
                .ToListAsync();

            if (_paymentSearch.Length > 0)
            {
                records = records.Where(x =>
                    MatchesSearch(x.MemberName, _paymentSearch) ||
                    MatchesSearch(x.Notes, _paymentSearch)).ToList();
            }

            dgvPayments.DataSource = null;
            dgvPayments.DataSource = records;

            // Outstanding/due summary for the visible (already scoped) rows.
            var todayForSummary = DateTime.Today;
            var latestDuePerMember = records
                .Where(x => x.NextDueDate.HasValue)
                .GroupBy(x => x.MemberName)
                .ToDictionary(g => g.Key, g => g.Max(x => x.NextDueDate!.Value));
            var overdue = latestDuePerMember.Values.Count(d => d.Date < todayForSummary);
            var dueSoon = latestDuePerMember.Values.Count(d => d.Date >= todayForSummary && d.Date <= todayForSummary.AddDays(7));
            lblPaymentsSummary.Text = $"{records.Count} payment(s) | Total {records.Sum(x => x.AmountPaid):C2} | {overdue} overdue | {dueSoon} due within 7 days";
        }

        private void dgvPayments_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var row = (PaymentRow)dgvPayments.Rows[e.RowIndex].DataBoundItem;
            _selectedPaymentId = row.PaymentId;
            txtPaymentAmount.Text = row.AmountPaid.ToString();
            dtpPaymentDate.Value = row.PaymentDate;

            if (row.NextDueDate.HasValue)
            {
                chkPaymentHasDueDate.Checked = true;
                dtpPaymentNextDue.Value = row.NextDueDate.Value;
            }
            else
            {
                chkPaymentHasDueDate.Checked = false;
            }

            txtPaymentNotes.Text = row.Notes ?? string.Empty;

            var member = _membersCache.FirstOrDefault(x => x.FullName == row.MemberName);
            if (member != null)
            {
                cboPaymentMember.SelectedValue = member.MemberId;
            }
        }

        private async void btnPaymentAdd_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManagePayments)) return;
            if (cboPaymentMember.SelectedValue == null)
            {
                MessageBox.Show("Select a member first.");
                return;
            }

            if (!decimal.TryParse(txtPaymentAmount.Text, out decimal amount))
            {
                MessageBox.Show("Amount Paid must be a valid number.");
                return;
            }

            if (amount <= 0)
            {
                MessageBox.Show("Amount Paid must be greater than zero.");
                return;
            }

            var payment = new Payment
            {
                MemberId = (int)cboPaymentMember.SelectedValue,
                AmountPaid = amount,
                PaymentDate = dtpPaymentDate.Value,
                NextDueDate = chkPaymentHasDueDate.Checked ? dtpPaymentNextDue.Value : (DateTime?)null,
                Notes = string.IsNullOrWhiteSpace(txtPaymentNotes.Text) ? null : txtPaymentNotes.Text.Trim()
            };

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

                var paymentMember = await tenantDb.Members.AsNoTracking().FirstOrDefaultAsync(m => m.MemberId == payment.MemberId);
                if (paymentMember == null)
                {
                    MessageBox.Show("The selected member no longer exists.");
                    return;
                }

                EnsureBranchAccess(paymentMember.BranchId);
                EnsureOwnMember(payment.MemberId);

                await EnsureCurrentBranchActiveAsync(tenantDb);

                payment.BranchId = ResolveWriteBranch(paymentMember.BranchId);
                tenantDb.Payments.Add(payment);
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Payment recorded.");
                ClearPaymentInputs();
                await LoadPaymentsAsync();
                await LoadDashboardAsync();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnPaymentUpdate_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManagePayments)) return;
            if (_selectedPaymentId == null)
            {
                MessageBox.Show("Select a payment record first.");
                return;
            }

            if (cboPaymentMember.SelectedValue == null)
            {
                MessageBox.Show("Select a member first.");
                return;
            }

            if (!decimal.TryParse(txtPaymentAmount.Text, out decimal amount))
            {
                MessageBox.Show("Amount Paid must be a valid number.");
                return;
            }

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var payment = await tenantDb.Payments
                    .FirstOrDefaultAsync(x => x.PaymentId == _selectedPaymentId);

                if (payment == null)
                {
                    MessageBox.Show("Payment record not found.");
                    return;
                }

                EnsureBranchAccess(payment.BranchId);
                EnsureOwnMember(payment.MemberId);

                payment.MemberId = (int)cboPaymentMember.SelectedValue;
                payment.AmountPaid = amount;
                payment.PaymentDate = dtpPaymentDate.Value;
                payment.NextDueDate = chkPaymentHasDueDate.Checked ? dtpPaymentNextDue.Value : (DateTime?)null;
                payment.Notes = string.IsNullOrWhiteSpace(txtPaymentNotes.Text) ? null : txtPaymentNotes.Text.Trim();

                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Payment updated.");
                ClearPaymentInputs();
                await LoadPaymentsAsync();
                await LoadDashboardAsync();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnPaymentDelete_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManagePayments)) return;
            if (_selectedPaymentId == null)
            {
                MessageBox.Show("Select a payment record first.");
                return;
            }

            var confirm = MessageBox.Show("Delete this payment record?", "Confirm",
                MessageBoxButtons.YesNo);
            if (confirm != DialogResult.Yes) return;

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var payment = await tenantDb.Payments
                    .FirstOrDefaultAsync(x => x.PaymentId == _selectedPaymentId);

                if (payment == null)
                {
                    MessageBox.Show("Payment record not found.");
                    return;
                }

                EnsureBranchAccess(payment.BranchId);
                EnsureOwnMember(payment.MemberId);

                tenantDb.Payments.Remove(payment);
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Payment deleted.");
                ClearPaymentInputs();
                await LoadPaymentsAsync();
                await LoadDashboardAsync();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private void btnPaymentClear_Click(object sender, EventArgs e)
        {
            ClearPaymentInputs();
        }

        private void ClearPaymentInputs()
        {
            _selectedPaymentId = null;
            if (cboPaymentMember.Items.Count > 0)
                cboPaymentMember.SelectedIndex = -1;
            txtPaymentAmount.Clear();
            dtpPaymentDate.Value = DateTime.Now;
            chkPaymentHasDueDate.Checked = false;
            dtpPaymentNextDue.Value = DateTime.Now;
            txtPaymentNotes.Clear();
        }

        // ============================================================
        // FEEDBACK
        // ============================================================

        private async Task LoadFeedbackAsync()
        {
            await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            var feedbackQuery = ApplyScope(tenantDb.Feedbacks.AsNoTracking().Include(x => x.Member), tenantDb, x => x.BranchId, x => x.MemberId);

            if (_feedbackRatingFilter.HasValue)
            {
                int ratingFilter = _feedbackRatingFilter.Value;
                feedbackQuery = feedbackQuery.Where(x => x.Rating == ratingFilter);
            }

            var records = await feedbackQuery
                .OrderByDescending(x => x.SubmittedAt)
                .Select(x => new FeedbackRow
                {
                    FeedbackId = x.FeedbackId,
                    MemberName = x.Member != null ? x.Member.FullName : "(unknown)",
                    Rating = x.Rating,
                    Comments = x.Comments,
                    SubmittedAt = x.SubmittedAt
                })
                .ToListAsync();

            if (_feedbackSearch.Length > 0)
            {
                records = records.Where(x =>
                    MatchesSearch(x.MemberName, _feedbackSearch) ||
                    MatchesSearch(x.Comments, _feedbackSearch)).ToList();
            }

            lblFeedbackStats.Text = records.Count == 0
                ? "No feedback"
                : $"{records.Count} feedback | avg {records.Average(x => (double)x.Rating):0.0}/5";

            dgvFeedback.DataSource = null;
            dgvFeedback.DataSource = records;
        }

        private void dgvFeedback_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var row = (FeedbackRow)dgvFeedback.Rows[e.RowIndex].DataBoundItem;
            _selectedFeedbackId = row.FeedbackId;
            cboFeedbackRating.SelectedItem = row.Rating;
            txtFeedbackComments.Text = row.Comments ?? string.Empty;
            dtpFeedbackSubmitted.Value = row.SubmittedAt;

            var member = _membersCache.FirstOrDefault(x => x.FullName == row.MemberName);
            if (member != null)
            {
                cboFeedbackMember.SelectedValue = member.MemberId;
            }
        }

        private async void btnFeedbackAdd_Click(object sender, EventArgs e)
        {
            if (!RequireAny(Permission.ManageFeedback, Permission.SubmitFeedback)) return;
            if (cboFeedbackMember.SelectedValue == null)
            {
                MessageBox.Show("Select a member first.");
                return;
            }

            if (cboFeedbackRating.SelectedItem == null)
            {
                MessageBox.Show("Select a rating.");
                return;
            }

            int rating = (int)cboFeedbackRating.SelectedItem;
            if (rating < 1 || rating > 5)
            {
                MessageBox.Show("Rating must be between 1 and 5.");
                return;
            }

            var feedback = new Feedback
            {
                MemberId = (int)cboFeedbackMember.SelectedValue,
                Rating = rating,
                Comments = string.IsNullOrWhiteSpace(txtFeedbackComments.Text) ? null : txtFeedbackComments.Text.Trim(),
                SubmittedAt = dtpFeedbackSubmitted.Value
            };

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

                // Stamp the member's branch so branch-scoped staff can see the feedback.
                var feedbackMember = await tenantDb.Members.AsNoTracking().FirstOrDefaultAsync(m => m.MemberId == feedback.MemberId);
                if (feedbackMember == null)
                {
                    MessageBox.Show("The selected member no longer exists.");
                    return;
                }

                EnsureOwnMember(feedback.MemberId);

                await EnsureCurrentBranchActiveAsync(tenantDb);

                feedback.BranchId = ResolveWriteBranch(feedbackMember.BranchId);
                tenantDb.Feedbacks.Add(feedback);
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Feedback submitted.");
                ClearFeedbackInputs();
                await LoadFeedbackAsync();
                await LoadDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnFeedbackUpdate_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageFeedback)) return;
            if (_selectedFeedbackId == null)
            {
                MessageBox.Show("Select a feedback record first.");
                return;
            }

            if (cboFeedbackMember.SelectedValue == null || cboFeedbackRating.SelectedItem == null)
            {
                MessageBox.Show("Select a member and a rating.");
                return;
            }

            int rating = (int)cboFeedbackRating.SelectedItem;
            if (rating < 1 || rating > 5)
            {
                MessageBox.Show("Rating must be between 1 and 5.");
                return;
            }

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var feedback = await tenantDb.Feedbacks
                    .FirstOrDefaultAsync(x => x.FeedbackId == _selectedFeedbackId);                if (feedback == null)
                {
                    MessageBox.Show("Feedback record not found.");
                    return;
                }

                EnsureBranchAccess(feedback.BranchId);
                EnsureOwnMember(feedback.MemberId);

                feedback.MemberId = (int)cboFeedbackMember.SelectedValue;
                feedback.Rating = rating;
                feedback.Comments = string.IsNullOrWhiteSpace(txtFeedbackComments.Text) ? null : txtFeedbackComments.Text.Trim();
                feedback.SubmittedAt = dtpFeedbackSubmitted.Value;

                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Feedback updated.");
                ClearFeedbackInputs();
                await LoadFeedbackAsync();
                await LoadDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnFeedbackDelete_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageFeedback)) return;
            if (_selectedFeedbackId == null)
            {
                MessageBox.Show("Select a feedback record first.");
                return;
            }

            var confirm = MessageBox.Show("Delete this feedback record?", "Confirm",
                MessageBoxButtons.YesNo);
            if (confirm != DialogResult.Yes) return;

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);                var feedback = await tenantDb.Feedbacks
                    .FirstOrDefaultAsync(x => x.FeedbackId == _selectedFeedbackId);                if (feedback == null)
                {
                    MessageBox.Show("Feedback record not found.");
                    return;
                }

                EnsureBranchAccess(feedback.BranchId);
                EnsureOwnMember(feedback.MemberId);

                tenantDb.Feedbacks.Remove(feedback);
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Feedback deleted.");
                ClearFeedbackInputs();
                await LoadFeedbackAsync();
                await LoadDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private void btnFeedbackClear_Click(object sender, EventArgs e)
        {
            ClearFeedbackInputs();
        }

        private void ClearFeedbackInputs()
        {
            _selectedFeedbackId = null;
            if (cboFeedbackMember.Items.Count > 0)
                cboFeedbackMember.SelectedIndex = -1;
            cboFeedbackRating.SelectedIndex = -1;
            txtFeedbackComments.Clear();
            dtpFeedbackSubmitted.Value = DateTime.Now;
        }

        // ============================================================
        // INQUIRIES
        // ============================================================

        private async Task LoadInquiriesAsync()
        {
            await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            var inquiryQuery = ApplyScope(tenantDb.Inquiries.AsNoTracking(), tenantDb, x => x.BranchId, null);

            if (_inquiriesFilter == "Pending")
            {
                inquiryQuery = inquiryQuery.Where(x => !x.IsFollowedUp);
            }

            var inquiries = await inquiryQuery
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            if (_inquirySearch.Length > 0)
            {
                inquiries = inquiries.Where(x =>
                    MatchesSearch(x.FullName, _inquirySearch) ||
                    MatchesSearch(x.InterestedPlan, _inquirySearch)).ToList();
            }

            lblInquiryStats.Text = $"{inquiries.Count} shown | {inquiries.Count(x => !x.IsFollowedUp)} pending follow-up | {inquiries.Count(x => x.IsFollowedUp)} followed up";

            dgvInquiries.DataSource = null;
            dgvInquiries.DataSource = inquiries;

            // Tenant A / Tenant B phase (non-branching tenants): the Inquiries grid shows
            // NO BranchId column. Re-applied after every bind (auto-generated columns).
            if (!_isBranchAware && dgvInquiries.Columns["BranchId"] is { } inquiryBranchColumn)
            {
                inquiryBranchColumn.Visible = false;
            }
        }

        private void dgvInquiries_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var inquiry = (Inquiry)dgvInquiries.Rows[e.RowIndex].DataBoundItem;

            _selectedInquiryId = inquiry.InquiryId;
            txtInquiryFullName.Text = inquiry.FullName;
            txtInquiryContact.Text = inquiry.ContactNumber ?? string.Empty;
            txtInquiryEmail.Text = inquiry.EmailAddress ?? string.Empty;
            txtInquiryPlan.Text = inquiry.InterestedPlan ?? string.Empty;
            txtInquiryMessage.Text = inquiry.Message ?? string.Empty;
            chkInquiryFollowedUp.Checked = inquiry.IsFollowedUp;
            dtpInquiryCreatedAt.Value = inquiry.CreatedAt;
        }

        private async void btnInquiryAdd_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageInquiries)) return;
            if (string.IsNullOrWhiteSpace(txtInquiryFullName.Text))
            {
                MessageBox.Show("Full Name is required.");
                return;
            }

            var inquiry = new Inquiry
            {
                FullName = txtInquiryFullName.Text.Trim(),
                ContactNumber = string.IsNullOrWhiteSpace(txtInquiryContact.Text) ? null : txtInquiryContact.Text.Trim(),
                EmailAddress = string.IsNullOrWhiteSpace(txtInquiryEmail.Text) ? null : txtInquiryEmail.Text.Trim(),
                InterestedPlan = string.IsNullOrWhiteSpace(txtInquiryPlan.Text) ? null : txtInquiryPlan.Text.Trim(),
                Message = string.IsNullOrWhiteSpace(txtInquiryMessage.Text) ? null : txtInquiryMessage.Text.Trim(),
                IsFollowedUp = chkInquiryFollowedUp.Checked,
                CreatedAt = dtpInquiryCreatedAt.Value
            };

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

                await EnsureCurrentBranchActiveAsync(tenantDb);

                if (_isBranchAware)
                {
                    inquiry.BranchId = ResolveWriteBranch(inquiry.BranchId);
                }

                tenantDb.Inquiries.Add(inquiry);
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Inquiry submitted.");
                ClearInquiryInputs();
                await LoadInquiriesAsync();
                await LoadDashboardAsync();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnInquiryUpdate_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageInquiries)) return;
            if (_selectedInquiryId == null)
            {
                MessageBox.Show("Select an inquiry record first.");
                return;
            }

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var inquiry = await tenantDb.Inquiries
                    .FirstOrDefaultAsync(x => x.InquiryId == _selectedInquiryId);

                if (inquiry == null)
                {
                    MessageBox.Show("Inquiry record not found.");
                    return;
                }

                inquiry.FullName = txtInquiryFullName.Text.Trim();
                inquiry.ContactNumber = string.IsNullOrWhiteSpace(txtInquiryContact.Text) ? null : txtInquiryContact.Text.Trim();
                inquiry.EmailAddress = string.IsNullOrWhiteSpace(txtInquiryEmail.Text) ? null : txtInquiryEmail.Text.Trim();
                inquiry.InterestedPlan = string.IsNullOrWhiteSpace(txtInquiryPlan.Text) ? null : txtInquiryPlan.Text.Trim();
                inquiry.Message = string.IsNullOrWhiteSpace(txtInquiryMessage.Text) ? null : txtInquiryMessage.Text.Trim();
                inquiry.IsFollowedUp = chkInquiryFollowedUp.Checked;
                inquiry.CreatedAt = dtpInquiryCreatedAt.Value;

                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Inquiry updated.");
                ClearInquiryInputs();
                await LoadInquiriesAsync();
                await LoadDashboardAsync();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnInquiryDelete_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageInquiries)) return;
            if (_selectedInquiryId == null)
            {
                MessageBox.Show("Select an inquiry record first.");
                return;
            }

            var confirm = MessageBox.Show("Delete this inquiry record?", "Confirm",
                MessageBoxButtons.YesNo);
            if (confirm != DialogResult.Yes) return;

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var inquiry = await tenantDb.Inquiries
                    .FirstOrDefaultAsync(x => x.InquiryId == _selectedInquiryId);

                if (inquiry == null)
                {
                    MessageBox.Show("Inquiry record not found.");
                    return;
                }

                tenantDb.Inquiries.Remove(inquiry);
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Inquiry deleted.");
                ClearInquiryInputs();
                await LoadInquiriesAsync();
                await LoadDashboardAsync();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private void btnInquiryClear_Click(object sender, EventArgs e)
        {
            ClearInquiryInputs();
        }

        private void ClearInquiryInputs()
        {
            _selectedInquiryId = null;
            txtInquiryFullName.Clear();
            txtInquiryContact.Clear();
            txtInquiryEmail.Clear();
            txtInquiryPlan.Clear();
            txtInquiryMessage.Clear();
            chkInquiryFollowedUp.Checked = false;
            dtpInquiryCreatedAt.Value = DateTime.Now;
        }

        // ============================================================
        // SUBSCRIPTIONS
        // ============================================================

        private async Task LoadSubscriptionsAsync()
        {
            await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            // Phase 8: subscriptions are branch-scoped (Subscription.BranchId), with the
            // member filter still applied for self-service member accounts.
            IQueryable<Subscription> subscriptionQuery = tenantDb.Subscriptions
                .AsNoTracking()
                .Include(x => x.Member);

            subscriptionQuery = ApplyScope(subscriptionQuery, tenantDb, x => x.BranchId, x => x.MemberId);

            var subscriptionEntities = await subscriptionQuery
                .OrderByDescending(x => x.StartDate)
                .ToListAsync();

            var todayForSubs = DateTime.Today;
            var records = subscriptionEntities
                .Select(x => new SubscriptionRow
                {
                    SubscriptionId = x.SubscriptionId,
                    MemberName = x.Member != null ? x.Member.FullName : "(unknown)",
                    PlanName = x.PlanName,
                    Price = x.Price,
                    BillingCycle = x.BillingCycle,
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    IsActive = x.IsActive,
                    StatusText = DescribeSubscriptionStatus(x, todayForSubs)
                })
                .ToList();

            lblSubscriptionStats.Text =
                $"Active: {records.Count(r => r.IsActive && r.EndDate.Date >= todayForSubs)} | Expired: {records.Count(r => r.IsActive && r.EndDate.Date < todayForSubs)} | Inactive: {records.Count(r => !r.IsActive)} | Total: {records.Count}";

            dgvSubscriptions.DataSource = null;
            dgvSubscriptions.DataSource = records;
        }

        private void dgvSubscriptions_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var row = (SubscriptionRow)dgvSubscriptions.Rows[e.RowIndex].DataBoundItem;
            _selectedSubscriptionId = row.SubscriptionId;
            txtPlanName.Text = row.PlanName;
            txtPrice.Text = row.Price.ToString();
            cboBillingCycle.SelectedItem = row.BillingCycle;
            dtpSubscriptionStartDate.Value = row.StartDate;
            dtpSubscriptionEndDate.Value = row.EndDate;
            chkSubscriptionIsActive.Checked = row.IsActive;

            var member = _membersCache.FirstOrDefault(x => x.FullName == row.MemberName);
            if (member != null)
            {
                cboSubscriptionMember.SelectedValue = member.MemberId;
            }
        }

        private async void btnSubscriptionAdd_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageSubscriptions)) return;
            if (cboSubscriptionMember.SelectedValue == null)
            {
                MessageBox.Show("Select a member first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(txtPlanName.Text))
            {
                MessageBox.Show("Plan Name is required.");
                return;
            }

            if (!decimal.TryParse(txtPrice.Text, out decimal price))
            {
                MessageBox.Show("Price must be a valid number.");
                return;
            }

            if (cboBillingCycle.SelectedItem == null)
            {
                MessageBox.Show("Select a billing cycle.");
                return;
            }

            var subscription = new Subscription
            {
                MemberId = (int)cboSubscriptionMember.SelectedValue,
                PlanName = txtPlanName.Text.Trim(),
                Price = price,
                BillingCycle = cboBillingCycle.SelectedItem.ToString() ?? string.Empty,
                StartDate = dtpSubscriptionStartDate.Value,
                EndDate = dtpSubscriptionEndDate.Value,
                IsActive = chkSubscriptionIsActive.Checked
            };

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

                if (_isBranchAware)
                {
                    var subscriptionMember = await tenantDb.Members.AsNoTracking()
                        .FirstOrDefaultAsync(m => m.MemberId == subscription.MemberId);
                    if (subscriptionMember == null)
                    {
                        MessageBox.Show("The selected member no longer exists.");
                        return;
                    }

                    await EnsureCurrentBranchActiveAsync(tenantDb);
                    subscription.BranchId = ResolveWriteBranch(subscriptionMember.BranchId);
                }

                tenantDb.Subscriptions.Add(subscription);
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Subscription added.");
                ClearSubscriptionInputs();
                await LoadSubscriptionsAsync();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnSubscriptionUpdate_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageSubscriptions)) return;
            if (_selectedSubscriptionId == null)
            {
                MessageBox.Show("Select a subscription first.");
                return;
            }

            if (cboSubscriptionMember.SelectedValue == null)
            {
                MessageBox.Show("Select a member first.");
                return;
            }

            if (!decimal.TryParse(txtPrice.Text, out decimal price))
            {
                MessageBox.Show("Price must be a valid number.");
                return;
            }

            if (cboBillingCycle.SelectedItem == null)
            {
                MessageBox.Show("Select a billing cycle.");
                return;
            }

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var subscription = await tenantDb.Subscriptions
                    .FirstOrDefaultAsync(x => x.SubscriptionId == _selectedSubscriptionId);

                if (subscription == null)
                {
                    MessageBox.Show("Subscription not found.");
                    return;
                }

                EnsureOwnMember(subscription.MemberId);

                subscription.MemberId = (int)cboSubscriptionMember.SelectedValue;
                subscription.PlanName = txtPlanName.Text.Trim();
                subscription.Price = price;
                subscription.BillingCycle = cboBillingCycle.SelectedItem.ToString() ?? string.Empty;
                subscription.StartDate = dtpSubscriptionStartDate.Value;
                subscription.EndDate = dtpSubscriptionEndDate.Value;
                subscription.IsActive = chkSubscriptionIsActive.Checked;

                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Subscription updated.");
                ClearSubscriptionInputs();
                await LoadSubscriptionsAsync();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnSubscriptionDelete_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageSubscriptions)) return;
            if (_selectedSubscriptionId == null)
            {
                MessageBox.Show("Select a subscription first.");
                return;
            }

            var confirm = MessageBox.Show("Delete this subscription?", "Confirm",
                MessageBoxButtons.YesNo);
            if (confirm != DialogResult.Yes) return;

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var subscription = await tenantDb.Subscriptions
                    .FirstOrDefaultAsync(x => x.SubscriptionId == _selectedSubscriptionId);

                if (subscription == null)
                {
                    MessageBox.Show("Subscription not found.");
                    return;
                }

                EnsureOwnMember(subscription.MemberId);

                tenantDb.Subscriptions.Remove(subscription);
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Subscription deleted.");
                ClearSubscriptionInputs();
                await LoadSubscriptionsAsync();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private void btnSubscriptionClear_Click(object sender, EventArgs e)
        {
            ClearSubscriptionInputs();
        }

        private void ClearSubscriptionInputs()
        {
            _selectedSubscriptionId = null;
            if (cboSubscriptionMember.Items.Count > 0)
                cboSubscriptionMember.SelectedIndex = -1;
            txtPlanName.Clear();
            txtPrice.Clear();
            if (cboBillingCycle.Items.Count > 0)
                cboBillingCycle.SelectedIndex = -1;
            dtpSubscriptionStartDate.Value = DateTime.Now;
            dtpSubscriptionEndDate.Value = DateTime.Now.AddMonths(1);
            chkSubscriptionIsActive.Checked = true;
        }

        // ============================================================
        // DASHBOARD (KPIs + CHARTS)
        // ============================================================

        private async Task LoadDashboardAsync()
        {
            await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            var scopedMembers = ApplyScope(tenantDb.Members.AsNoTracking(), tenantDb, m => m.BranchId, m => m.MemberId);
            var scopedAttendance = ApplyScope(tenantDb.Attendances.AsNoTracking(), tenantDb, x => x.BranchId, x => x.MemberId);
            var scopedFeedback = ApplyScope(tenantDb.Feedbacks.AsNoTracking(), tenantDb, x => x.BranchId, x => x.MemberId);

            var totalMembers = await scopedMembers.CountAsync();
            var activeMembers = await scopedMembers.CountAsync(x => x.IsActive);
            var inactiveMembers = totalMembers - activeMembers;

            var today = DateTime.Today;
            var attendanceToday = await scopedAttendance.CountAsync(x => x.CheckInDate.Date == today);

            int diffToMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var startOfWeek = today.AddDays(-diffToMonday);
            var attendanceThisWeek = await scopedAttendance.CountAsync(x => x.CheckInDate.Date >= startOfWeek);

            var pendingInquiries = await ApplyScope(tenantDb.Inquiries.AsNoTracking(), tenantDb, x => x.BranchId, null).CountAsync(x => !x.IsFollowedUp);

            var hasFeedback = await scopedFeedback.AnyAsync();
            var avgRating = hasFeedback ? await scopedFeedback.AverageAsync(x => x.Rating) : 0.0;

            lblKpiTotalMembers.Text = $"Total Members\n{totalMembers}";
            lblKpiActiveMembers.Text = $"Active Members\n{activeMembers}";
            lblKpiInactiveMembers.Text = $"Inactive Members\n{inactiveMembers}";
            lblKpiAvgRating.Text = $"Avg Rating\n{avgRating:0.0}";
            lblKpiAttendanceToday.Text = $"Attendance Today\n{attendanceToday}";
            lblKpiAttendanceWeek.Text = $"Attendance This Week\n{attendanceThisWeek}";
            lblKpiPendingInquiries.Text = $"Pending Inquiries\n{pendingInquiries}";

            // ---- Phase 6 KPIs ----
            var attendanceThisMonth = await scopedAttendance.CountAsync(x => x.CheckInDate.Date >= new DateTime(today.Year, today.Month, 1));
            var totalPayments = await ApplyScope(tenantDb.Payments.AsNoTracking(), tenantDb, x => x.BranchId, x => x.MemberId)
                .SumAsync(x => (decimal?)x.AmountPaid) ?? 0m;

            // Phase 2: complaints now exist in every tenant schema (non-branching for A/B),
            // so the KPI is computed for legacy tenants too.
            var openComplaints = await ApplyScope(tenantDb.Complaints.AsNoTracking(), tenantDb, c => c.BranchId, c => c.MemberId)
                .CountAsync(c => c.Status != "Resolved" && c.Status != "Closed");

            // At-risk inputs (same rules as the Retention tab, all scope-filtered).
            var membersForRisk = await scopedMembers.Where(x => x.IsActive).ToListAsync();
            var attendancesForRisk = await scopedAttendance.ToListAsync();
            var paymentsForRisk = await ApplyScope(tenantDb.Payments.AsNoTracking(), tenantDb, x => x.BranchId, x => x.MemberId)
                .ToListAsync();
            var scopedMemberIds = await ApplyScope(tenantDb.Members.AsNoTracking(), tenantDb, m => m.BranchId, m => m.MemberId)
                .Select(m => m.MemberId)
                .ToListAsync();
            var memberSet = scopedMemberIds.ToHashSet();
            var subscriptionsForRisk = (await tenantDb.Subscriptions.AsNoTracking().ToListAsync())
                .Where(s => memberSet.Contains(s.MemberId))
                .ToList();

            var atRiskCount = await ComputeAtRiskCountAsync(
                membersForRisk, attendancesForRisk, paymentsForRisk, subscriptionsForRisk,
                (int)nudInactivityDays.Value, (int)nudDueSoonDays.Value);

            lblKpiAttendanceMonth.Text = $"Attendance This Month\n{attendanceThisMonth}";
            lblKpiTotalPayments.Text = $"Total Payments\n{totalPayments:C0}";
            lblKpiOpenComplaints.Text = $"Open Complaints\n{openComplaints}";
            lblKpiAtRisk.Text = $"At-Risk Members\n{atRiskCount}";

            // Charts read pre-scoped queries so branch-scoped roles only ever see
            // their own branch's numbers.
            await LoadAttendanceTrendChartAsync(tenantDb, scopedAttendance);
            await LoadFeedbackDistributionChartAsync(tenantDb, scopedFeedback);
            await LoadMonthlyPaymentsChartAsync(tenantDb, ApplyScope(tenantDb.Payments.AsNoTracking(), tenantDb, x => x.BranchId, x => x.MemberId));
            LoadActiveVsInactiveChart(activeMembers, inactiveMembers);

            await LoadBranchComparisonAsync(tenantDb);
        }

        // ---- Dark-theme paints for LiveCharts ----
        //
        // ROOT CAUSE of the disappearing charts (verified against LiveCharts 2.0.5 and a
        // minimal repro): when a chart re-measures (Refresh / branch switch), the canvas
        // collects the previous measure's geometries and DISPOSES the SKPaint task each
        // drawn geometry used. Reusing one Paint INSTANCE for the next bind (as these
        // statics used to be) therefore points the new bind at an already-disposed canvas
        // task: the geometries never attach and the chart draws NOTHING from that measure
        // on (blank canvas, data still bound). Creating a fresh paint per bind always
        // renders, so every paint is a factory method and no LiveCharts paint instance is
        // ever shared across rebinds.
        private static SolidColorPaint ChartTextPaint() => new(new SKColor(190, 190, 190));
        private static SolidColorPaint ChartGridPaint() => new(new SKColor(55, 55, 55));
        private static SolidColorPaint ChartGoldStrokePaint() => new(new SKColor(212, 175, 55)) { StrokeThickness = 3 };
        private static SolidColorPaint ChartGoldFillPaint() => new(new SKColor(212, 175, 55));
        private static SolidColorPaint ChartGrayFillPaint() => new(new SKColor(90, 90, 90));

        private async Task LoadAttendanceTrendChartAsync(ITenantDataContext tenantDb, IQueryable<Attendance> scopedAttendance)
        {
            var since = DateTime.Today.AddDays(-29);

            var raw = await scopedAttendance
                .Where(x => x.CheckInDate.Date >= since)
                .Select(x => x.CheckInDate.Date)
                .ToListAsync();

            var grouped = raw
                .GroupBy(d => d)
                .ToDictionary(g => g.Key, g => g.Count());

            var labels = new List<string>();
            var values = new List<double>();

            for (var d = since; d <= DateTime.Today; d = d.AddDays(1))
            {
                labels.Add(d.ToString("MM/dd"));
                values.Add(grouped.TryGetValue(d, out var c) ? c : 0);
            }

            chartAttendanceTrend.Series = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = values,
                    Name = "Check-ins",
                    Stroke = ChartGoldStrokePaint(),
                    Fill = new SolidColorPaint(new SKColor(212, 175, 55, 45)),
                    GeometryStroke = ChartGoldStrokePaint(),
                    GeometryFill = new SolidColorPaint(new SKColor(18, 18, 18)),
                    GeometrySize = 6,
                    LineSmoothness = 0.3
                }
            };
            chartAttendanceTrend.XAxes = new[] { new Axis { Labels = labels, LabelsPaint = ChartTextPaint(), SeparatorsPaint = ChartGridPaint(), TextSize = 10 } };
            chartAttendanceTrend.YAxes = new[] { new Axis { MinLimit = 0, LabelsPaint = ChartTextPaint(), SeparatorsPaint = ChartGridPaint() } };
            chartAttendanceTrend.LegendPosition = LegendPosition.Hidden;
        }

        private async Task LoadFeedbackDistributionChartAsync(ITenantDataContext tenantDb, IQueryable<Feedback> scopedFeedback)
        {
            var ratingCounts = await scopedFeedback
                .GroupBy(x => x.Rating)
                .Select(g => new { Rating = g.Key, Count = g.Count() })
                .ToListAsync();

            var labels = new List<string>();
            var values = new List<double>();

            for (int r = 1; r <= 5; r++)
            {
                labels.Add($"{r} star");
                values.Add(ratingCounts.FirstOrDefault(x => x.Rating == r)?.Count ?? 0);
            }

            chartFeedbackDistribution.Series = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Values = values,
                    Name = "Feedback Count",
                    Fill = ChartGoldFillPaint(),
                    Stroke = null
                }
            };
            chartFeedbackDistribution.XAxes = new[] { new Axis { Labels = labels, LabelsPaint = ChartTextPaint(), SeparatorsPaint = ChartGridPaint(), TextSize = 10 } };
            chartFeedbackDistribution.YAxes = new[] { new Axis { MinLimit = 0, LabelsPaint = ChartTextPaint(), SeparatorsPaint = ChartGridPaint() } };
            chartFeedbackDistribution.LegendPosition = LegendPosition.Hidden;
        }

        private async Task LoadMonthlyPaymentsChartAsync(ITenantDataContext tenantDb, IQueryable<Payment> scopedPayments)
        {
            var sixMonthsAgo = DateTime.Today.AddMonths(-5);
            var startOfRange = new DateTime(sixMonthsAgo.Year, sixMonthsAgo.Month, 1);

            var raw = await scopedPayments
                .Where(x => x.PaymentDate >= startOfRange)
                .Select(x => new { x.PaymentDate, x.AmountPaid })
                .ToListAsync();

            var grouped = raw
                .GroupBy(x => new { x.PaymentDate.Year, x.PaymentDate.Month })
                .ToDictionary(g => new DateTime(g.Key.Year, g.Key.Month, 1), g => g.Sum(x => x.AmountPaid));

            var labels = new List<string>();
            var values = new List<double>();

            for (int i = 0; i < 6; i++)
            {
                var month = startOfRange.AddMonths(i);
                decimal total = grouped.TryGetValue(month, out var t) ? t : 0m;
                labels.Add(month.ToString("MMM yyyy"));
                values.Add((double)total);
            }

            chartMonthlyPayments.Series = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Values = values,
                    Name = "Total Paid",
                    Fill = ChartGoldFillPaint(),
                    Stroke = null
                }
            };
            chartMonthlyPayments.XAxes = new[] { new Axis { Labels = labels, LabelsPaint = ChartTextPaint(), SeparatorsPaint = ChartGridPaint(), TextSize = 10 } };
            chartMonthlyPayments.YAxes = new[] { new Axis { MinLimit = 0, LabelsPaint = ChartTextPaint(), SeparatorsPaint = ChartGridPaint(), Labeler = value => value.ToString("C0") } };
            chartMonthlyPayments.LegendPosition = LegendPosition.Hidden;
        }

        private void LoadActiveVsInactiveChart(int activeMembers, int inactiveMembers)
        {
            chartActiveVsInactive.Series = new ISeries[]
            {
                new PieSeries<double>
                {
                    Values = new double[] { activeMembers },
                    Name = "Active",
                    InnerRadius = 55,
                    Fill = ChartGoldFillPaint(),
                    DataLabelsPaint = ChartTextPaint(),
                    DataLabelsSize = 12,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle
                },
                new PieSeries<double>
                {
                    Values = new double[] { inactiveMembers },
                    Name = "Inactive",
                    InnerRadius = 55,
                    Fill = ChartGrayFillPaint(),
                    DataLabelsPaint = ChartTextPaint(),
                    DataLabelsSize = 12,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle
                }
            };
            chartActiveVsInactive.LegendPosition = LegendPosition.Right;
            chartActiveVsInactive.LegendTextPaint = ChartTextPaint();
        }

        // ============================================================
        // RETENTION MANAGEMENT (read-only)
        // ============================================================

        private async void btnApplyRetentionFilters_Click(object sender, EventArgs e)
        {
            try
            {
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async Task LoadRetentionAsync()
        {
            await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            int inactivityDays = (int)nudInactivityDays.Value;
            int dueSoonDays = (int)nudDueSoonDays.Value;
            var today = DateTime.Today;

            var members = await ApplyScope(tenantDb.Members.AsNoTracking(), tenantDb, x => x.BranchId, x => x.MemberId)
                .Where(x => x.IsActive)
                .ToListAsync();

            var lastCheckIns = await ApplyScope(tenantDb.Attendances.AsNoTracking(), tenantDb, x => x.BranchId, x => x.MemberId)
                .GroupBy(x => x.MemberId)
                .Select(g => new { MemberId = g.Key, LastCheckIn = g.Max(a => a.CheckInDate) })
                .ToListAsync();

            var lastCheckInLookup = lastCheckIns.ToDictionary(x => x.MemberId, x => x.LastCheckIn);

            var inactiveRows = new List<InactiveMemberRow>();
            foreach (var m in members)
            {
                if (lastCheckInLookup.TryGetValue(m.MemberId, out var lastCheckIn))
                {
                    var daysSince = (today - lastCheckIn.Date).Days;
                    if (daysSince >= inactivityDays)
                    {
                        inactiveRows.Add(new InactiveMemberRow
                        {
                            MemberId = m.MemberId,
                            FullName = m.FullName,
                            LastCheckIn = lastCheckIn.ToString("yyyy-MM-dd"),
                            DaysSince = $"{daysSince} days"
                        });
                    }
                }
                else
                {
                    inactiveRows.Add(new InactiveMemberRow
                    {
                        MemberId = m.MemberId,
                        FullName = m.FullName,
                        LastCheckIn = "(never)",
                        DaysSince = "N/A"
                    });
                }
            }

            dgvInactiveMembers.DataSource = null;
            dgvInactiveMembers.DataSource = inactiveRows.OrderByDescending(x => x.DaysSince).ToList();

            var latestPayments = await ApplyScope(tenantDb.Payments.AsNoTracking().Include(x => x.Member), tenantDb, x => x.BranchId, x => x.MemberId)
                .Where(x => x.NextDueDate != null)
                .GroupBy(x => x.MemberId)
                .Select(g => g.OrderByDescending(p => p.PaymentDate).First())
                .ToListAsync();

            var dueSoonCutoff = today.AddDays(dueSoonDays);

            var dueSoonRows = latestPayments
                .Where(p => p.NextDueDate.HasValue && p.NextDueDate.Value.Date <= dueSoonCutoff)
                .Select(p => new DueSoonPaymentRow
                {
                    MemberId = p.MemberId,
                    FullName = p.Member != null ? p.Member.FullName : "(unknown)",
                    NextDueDate = p.NextDueDate!.Value,
                    Status = p.NextDueDate!.Value.Date < today
                        ? $"Overdue by {(today - p.NextDueDate.Value.Date).Days} days"
                        : $"Due in {(p.NextDueDate.Value.Date - today).Days} days"
                })
                .OrderBy(x => x.NextDueDate)
                .ToList();

            dgvDueSoonPayments.DataSource = null;
            dgvDueSoonPayments.DataSource = dueSoonRows;

            var pendingInquiries = await ApplyScope(tenantDb.Inquiries.AsNoTracking(), tenantDb, x => x.BranchId, null)
                .Where(x => !x.IsFollowedUp)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            dgvPendingInquiries.DataSource = null;
            dgvPendingInquiries.DataSource = pendingInquiries;

            // ---- Subscriptions expiring soon (reuses the due-soon window) ----
            // Subscriptions carry no BranchId, so they are filtered through the scope's
            // member set instead (correct for branch and member self-service sessions).
            var scopedMemberIdsForSubs = await ApplyScope(tenantDb.Members.AsNoTracking(), tenantDb, x => x.BranchId, x => x.MemberId)
                .Select(m => m.MemberId)
                .ToListAsync();
            var scopedMemberSet = scopedMemberIdsForSubs.ToHashSet();

            var activeSubscriptions = (await tenantDb.Subscriptions
                .AsNoTracking()
                .Include(x => x.Member)
                .Where(x => x.IsActive)
                .ToListAsync())
                .Where(s => scopedMemberSet.Contains(s.MemberId))
                .ToList();

            var expiringRows = activeSubscriptions
                .Where(s => s.EndDate.Date <= dueSoonCutoff)
                .Select(s => new ExpiringSubscriptionRow
                {
                    MemberId = s.MemberId,
                    FullName = s.Member != null ? s.Member.FullName : "(unknown)",
                    PlanName = s.PlanName,
                    EndDate = s.EndDate,
                    Status = s.EndDate.Date < today
                        ? $"Expired {(today - s.EndDate.Date).Days} days ago"
                        : $"Expires in {(s.EndDate.Date - today).Days} days"
                })
                .OrderBy(x => x.EndDate)
                .ToList();

            dgvExpiringSubscriptions.DataSource = null;
            dgvExpiringSubscriptions.DataSource = expiringRows;

            // ---- At-Risk KPI: distinct members flagged by any detection rule ----
            var atRiskMemberIds = new HashSet<int>();
            foreach (var row in inactiveRows) atRiskMemberIds.Add(row.MemberId);
            foreach (var row in dueSoonRows) atRiskMemberIds.Add(row.MemberId);
            foreach (var row in expiringRows) atRiskMemberIds.Add(row.MemberId);
            lblRetentionAtRisk.Text = $"AT-RISK MEMBERS\n{atRiskMemberIds.Count}";

            await LoadRetentionRecordsAsync(tenantDb);
        }

        // ============================================================
        // RETENTION RECORDS (workflow: create -> approve/reject -> complete)
        // ============================================================

        private async Task LoadRetentionRecordsAsync(ITenantDataContext tenantDb)
        {
            var records = await ApplyScope(tenantDb.RetentionRecords.AsNoTracking().Include(x => x.Member), tenantDb, x => x.BranchId, x => x.MemberId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new RetentionRecordRow
                {
                    RetentionRecordId = x.RetentionRecordId,
                    MemberName = x.Member != null ? x.Member.FullName : "(unknown)",
                    Reason = x.Reason,
                    RetentionAction = x.RetentionAction,
                    Promotion = x.Promotion,
                    Status = x.Status,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();

            dgvRetentionRecords.DataSource = null;
            dgvRetentionRecords.DataSource = records;

            int pendingCount = records.Count(x => x.Status == "Pending");
            int approvedCount = records.Count(x => x.Status == "Approved");
            lblRetentionPendingApproval.Text = $"PENDING APPROVAL\n{pendingCount}";
            lblRetentionApprovedCount.Text = $"APPROVED\n{approvedCount}";
        }

        private void dgvRetentionRecords_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            var row = (RetentionRecordRow)dgvRetentionRecords.Rows[e.RowIndex].DataBoundItem;
            _selectedRetentionRecordId = row.RetentionRecordId;
        }

        private async void btnRetentionSubmit_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageRetention)) return;
            if (cboRetentionMember.SelectedValue == null)
            {
                MessageBox.Show("Select a member first.");
                return;
            }

            if (cboRetentionReason.SelectedItem == null)
            {
                MessageBox.Show("Select a reason.");
                return;
            }

            if (cboRetentionAction.SelectedItem == null)
            {
                MessageBox.Show("Select an action.");
                return;
            }

            var record = new RetentionRecord
            {
                MemberId = (int)cboRetentionMember.SelectedValue,
                Reason = cboRetentionReason.SelectedItem.ToString() ?? string.Empty,
                RetentionAction = cboRetentionAction.SelectedItem.ToString() ?? string.Empty,
                Promotion = string.IsNullOrWhiteSpace(txtRetentionPromotion.Text) ? null : txtRetentionPromotion.Text.Trim(),
                Notes = string.IsNullOrWhiteSpace(txtRetentionNotes.Text) ? null : txtRetentionNotes.Text.Trim(),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _loggedInFullName,
                Status = "Pending"
            };

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

                var retentionMember = await tenantDb.Members.AsNoTracking().FirstOrDefaultAsync(m => m.MemberId == record.MemberId);
                if (retentionMember == null)
                {
                    MessageBox.Show("The selected member no longer exists.");
                    return;
                }

                EnsureBranchAccess(retentionMember.BranchId);
                EnsureOwnMember(record.MemberId);

                await EnsureCurrentBranchActiveAsync(tenantDb);

                // Stamp the Current Branch (when selected) so the record is visible to
                // branch-scoped staff; otherwise fall back to the member's home branch.
                record.BranchId = ResolveWriteBranch(retentionMember.BranchId);
                tenantDb.RetentionRecords.Add(record);
                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Retention record submitted for approval.");
                ClearRetentionCreateInputs();
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private void btnRetentionClear_Click(object sender, EventArgs e)
        {
            ClearRetentionCreateInputs();
        }

        private void ClearRetentionCreateInputs()
        {
            if (cboRetentionMember.Items.Count > 0)
                cboRetentionMember.SelectedIndex = -1;
            if (cboRetentionReason.Items.Count > 0)
                cboRetentionReason.SelectedIndex = -1;
            if (cboRetentionAction.Items.Count > 0)
                cboRetentionAction.SelectedIndex = -1;
            txtRetentionPromotion.Clear();
            txtRetentionNotes.Clear();
        }

        private async void btnRetentionApprove_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageRetention)) return;
            if (_selectedRetentionRecordId == null)
            {
                MessageBox.Show("Select a retention record first.");
                return;
            }

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var record = await tenantDb.RetentionRecords
                    .FirstOrDefaultAsync(x => x.RetentionRecordId == _selectedRetentionRecordId);

                if (record == null)
                {
                    MessageBox.Show("Retention record not found.");
                    return;
                }

                EnsureBranchAccess(record.BranchId);
                EnsureOwnMember(record.MemberId);

                if (record.Status != "Pending")
                {
                    MessageBox.Show($"Only Pending records can be approved. This record is currently '{record.Status}'.");
                    return;
                }

                string? reason = ShowInputDialog("Approve Retention Record", "Approval reason:");
                if (reason == null) return; // user cancelled

                record.Status = "Approved";
                record.ApprovedBy = _loggedInFullName;
                record.ApprovedAt = DateTime.UtcNow;
                record.ApprovalReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Retention record approved.");
                _selectedRetentionRecordId = null;
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnRetentionReject_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageRetention)) return;
            if (_selectedRetentionRecordId == null)
            {
                MessageBox.Show("Select a retention record first.");
                return;
            }

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var record = await tenantDb.RetentionRecords
                    .FirstOrDefaultAsync(x => x.RetentionRecordId == _selectedRetentionRecordId);

                if (record == null)
                {
                    MessageBox.Show("Retention record not found.");
                    return;
                }

                EnsureBranchAccess(record.BranchId);
                EnsureOwnMember(record.MemberId);

                if (record.Status != "Pending")
                {
                    MessageBox.Show($"Only Pending records can be rejected. This record is currently '{record.Status}'.");
                    return;
                }

                string? reason = ShowInputDialog("Reject Retention Record", "Rejection reason (required):");
                if (reason == null) return; // user cancelled

                if (string.IsNullOrWhiteSpace(reason))
                {
                    MessageBox.Show("A rejection reason is required.");
                    return;
                }

                record.Status = "Rejected";
                record.ApprovedBy = _loggedInFullName;
                record.ApprovedAt = DateTime.UtcNow;
                record.RejectionReason = reason.Trim();

                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Retention record rejected.");
                _selectedRetentionRecordId = null;
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async void btnRetentionComplete_Click(object sender, EventArgs e)
        {
            if (!Require(Permission.ManageRetention)) return;
            if (_selectedRetentionRecordId == null)
            {
                MessageBox.Show("Select a retention record first.");
                return;
            }

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var record = await tenantDb.RetentionRecords
                    .FirstOrDefaultAsync(x => x.RetentionRecordId == _selectedRetentionRecordId);

                if (record == null)
                {
                    MessageBox.Show("Retention record not found.");
                    return;
                }

                EnsureBranchAccess(record.BranchId);
                EnsureOwnMember(record.MemberId);

                if (record.Status != "Approved")
                {
                    MessageBox.Show($"Only Approved records can be marked Completed. This record is currently '{record.Status}'.");
                    return;
                }

                string? notes = ShowInputDialog("Complete Retention Record", "Completion notes:");
                if (notes == null) return; // user cancelled

                record.Status = "Completed";
                record.CompletedAt = DateTime.UtcNow;
                record.CompletionNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

                await tenantDb.SaveChangesAsync();

                MessageBox.Show("Retention record marked as completed.");
                _selectedRetentionRecordId = null;
                await LoadRetentionAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        // Small reusable input dialog for capturing a short reason/notes value.
        // Returns null if the user cancels; returns the (possibly empty) text otherwise.
        private static string? ShowInputDialog(string title, string prompt)
        {
            using var dialog = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new System.Drawing.Size(380, 160)
            };

            var lblPrompt = new Label
            {
                Text = prompt,
                Location = new System.Drawing.Point(15, 15),
                Size = new System.Drawing.Size(350, 20)
            };

            var txtInput = new TextBox
            {
                Location = new System.Drawing.Point(15, 40),
                Size = new System.Drawing.Size(350, 60),
                Multiline = true
            };

            var btnOk = new Button
            {
                Text = "OK",
                Location = new System.Drawing.Point(190, 110),
                Size = new System.Drawing.Size(80, 30),
                DialogResult = DialogResult.OK
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                Location = new System.Drawing.Point(280, 110),
                Size = new System.Drawing.Size(80, 30),
                DialogResult = DialogResult.Cancel
            };

            dialog.Controls.Add(lblPrompt);
            dialog.Controls.Add(txtInput);
            dialog.Controls.Add(btnOk);
            dialog.Controls.Add(btnCancel);
            dialog.AcceptButton = btnOk;
            dialog.CancelButton = btnCancel;

            return dialog.ShowDialog() == DialogResult.OK ? txtInput.Text : null;
        }

        // ============================================================
        // PDF REPORT EXPORT (QuestPDF)
        // ============================================================

        // Shared builder: renders a title page + a simple data table.
        // columnWidths must sum to roughly 1.0 (relative proportions).
        private void GeneratePdfReport(
            string reportTitle,
            string fileName,
            string[] columnHeaders,
            float[] columnWidths,
            List<string[]> rows,
            string summaryLine)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = fileName
            };

            if (saveDialog.ShowDialog() != DialogResult.OK) return;

            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("ALPHA ELITE").FontSize(20).Bold();
                        col.Item().Text(reportTitle).FontSize(14).SemiBold();
                        col.Item().PaddingTop(4).Text($"Generated: {DateTime.Now:MMMM d, yyyy h:mm tt}").FontSize(9);
                        if (!string.IsNullOrWhiteSpace(summaryLine))
                        {
                            col.Item().Text(summaryLine).FontSize(9);
                        }
                        col.Item().PaddingTop(8).LineHorizontal(1);
                    });

                    page.Content().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            foreach (var width in columnWidths)
                            {
                                columns.RelativeColumn(width);
                            }
                        });

                        table.Header(header =>
                        {
                            foreach (var headerText in columnHeaders)
                            {
                                header.Cell().Element(c => c.Background(Colors.Grey.Lighten2).Padding(4))
                                    .Text(headerText).Bold();
                            }
                        });

                        foreach (var row in rows)
                        {
                            foreach (var cellText in row)
                            {
                                table.Cell().Element(c => c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4))
                                    .Text(cellText ?? string.Empty);
                            }
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Alpha Elite Management System   |   Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf(saveDialog.FileName);

            var openConfirm = MessageBox.Show(
                "Report exported successfully. Open the file now?",
                "Export Complete", MessageBoxButtons.YesNo);

            if (openConfirm == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = saveDialog.FileName,
                    UseShellExecute = true
                });
            }
        }

        private async void btnExportMembers_Click(object sender, EventArgs e)
        {
            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var members = await ApplyScope(tenantDb.Members.AsNoTracking(), tenantDb, m => m.BranchId, m => m.MemberId)
                    .OrderBy(x => x.MemberId).ToListAsync();

                var rows = members.Select(m => new[]
                {
                    m.MemberCode,
                    m.FullName,
                    m.Email,
                    m.IsActive ? "Active" : "Inactive",
                    m.CreatedAt.ToString("yyyy-MM-dd")
                }).ToList();

                GeneratePdfReport(
                    "Member Report",
                    $"AlphaElite_MemberReport_{DateTime.Now:yyyy-MM-dd}.pdf",
                    new[] { "Code", "Full Name", "Email", "Status", "Created" },
                    new[] { 0.15f, 0.3f, 0.3f, 0.13f, 0.12f },
                    rows,
                    $"Total Members: {members.Count}{ScopeLabel()}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export error: " + ex.Message);
            }
        }

        private async void btnExportAttendance_Click(object sender, EventArgs e)
        {
            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var records = await ApplyScope(tenantDb.Attendances.AsNoTracking().Include(x => x.Member), tenantDb, a => a.BranchId, a => a.MemberId)
                    .OrderByDescending(x => x.CheckInDate)
                    .ToListAsync();

                var rows = records.Select(a => new[]
                {
                    a.Member != null ? a.Member.MemberCode : "-",
                    a.Member != null ? a.Member.FullName : "(unknown)",
                    a.CheckInDate.ToString("yyyy-MM-dd HH:mm")
                }).ToList();

                GeneratePdfReport(
                    "Attendance Report",
                    $"AlphaElite_AttendanceReport_{DateTime.Now:yyyy-MM-dd}.pdf",
                    new[] { "Member Code", "Member", "Check-in Date" },
                    new[] { 0.2f, 0.4f, 0.4f },
                    rows,
                    $"Total Attendance Records: {records.Count}{ScopeLabel()}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export error: " + ex.Message);
            }
        }

        private async void btnExportPayments_Click(object sender, EventArgs e)
        {
            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var records = await ApplyScope(tenantDb.Payments.AsNoTracking().Include(x => x.Member), tenantDb, p => p.BranchId, p => p.MemberId)
                    .OrderByDescending(x => x.PaymentDate)
                    .ToListAsync();

                var rows = records.Select(p => new[]
                {
                    p.Member != null ? p.Member.FullName : "(unknown)",
                    p.AmountPaid.ToString("C2"),
                    p.PaymentDate.ToString("yyyy-MM-dd"),
                    p.NextDueDate.HasValue ? p.NextDueDate.Value.ToString("yyyy-MM-dd") : "-",
                    p.Notes ?? "-"
                }).ToList();

                decimal totalPaid = records.Sum(x => x.AmountPaid);

                GeneratePdfReport(
                    "Payment Report",
                    $"AlphaElite_PaymentReport_{DateTime.Now:yyyy-MM-dd}.pdf",
                    new[] { "Member", "Amount Paid", "Payment Date", "Next Due", "Notes" },
                    new[] { 0.25f, 0.15f, 0.15f, 0.15f, 0.3f },
                    rows,
                    $"Total Payments: {records.Count}   |   Total Collected: {totalPaid:C2}{ScopeLabel()}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export error: " + ex.Message);
            }
        }

        private async void btnExportFeedback_Click(object sender, EventArgs e)
        {
            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var records = await ApplyScope(tenantDb.Feedbacks.AsNoTracking().Include(x => x.Member), tenantDb, f => f.BranchId, f => f.MemberId)
                    .OrderByDescending(x => x.SubmittedAt)
                    .ToListAsync();

                var rows = records.Select(f => new[]
                {
                    f.Member != null ? f.Member.FullName : "(unknown)",
                    new string('*', f.Rating),
                    f.Comments ?? "-",
                    f.SubmittedAt.ToString("yyyy-MM-dd")
                }).ToList();

                double avgRating = records.Count > 0 ? records.Average(x => x.Rating) : 0;

                GeneratePdfReport(
                    "Feedback Report",
                    $"AlphaElite_FeedbackReport_{DateTime.Now:yyyy-MM-dd}.pdf",
                    new[] { "Member", "Rating", "Comments", "Submitted" },
                    new[] { 0.25f, 0.15f, 0.45f, 0.15f },
                    rows,
                    $"Total Feedback: {records.Count}   |   Average Rating: {avgRating:0.0}{ScopeLabel()}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export error: " + ex.Message);
            }
        }

        private async void btnExportInquiries_Click(object sender, EventArgs e)
        {
            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var records = await ApplyScope(tenantDb.Inquiries.AsNoTracking(), tenantDb, i => i.BranchId, null)
                    .OrderByDescending(x => x.CreatedAt)
                    .ToListAsync();

                var rows = records.Select(i => new[]
                {
                    i.FullName,
                    i.ContactNumber ?? "-",
                    i.EmailAddress ?? "-",
                    i.InterestedPlan ?? "-",
                    i.IsFollowedUp ? "Followed Up" : "Pending",
                    i.CreatedAt.ToString("yyyy-MM-dd")
                }).ToList();

                GeneratePdfReport(
                    "Inquiry Report",
                    $"AlphaElite_InquiryReport_{DateTime.Now:yyyy-MM-dd}.pdf",
                    new[] { "Full Name", "Contact", "Email", "Interested Plan", "Status", "Created" },
                    new[] { 0.2f, 0.15f, 0.2f, 0.2f, 0.13f, 0.12f },
                    rows,
                    $"Total Inquiries: {records.Count}{ScopeLabel()}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export error: " + ex.Message);
            }
        }

        private async void btnExportRetention_Click(object sender, EventArgs e)
        {
            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var records = await ApplyScope(tenantDb.RetentionRecords.AsNoTracking().Include(x => x.Member), tenantDb, r => r.BranchId, r => r.MemberId)
                    .OrderByDescending(x => x.CreatedAt)
                    .ToListAsync();

                var rows = records.Select(r => new[]
                {
                    r.Member != null ? r.Member.FullName : "(unknown)",
                    r.Reason,
                    r.RetentionAction,
                    r.Promotion ?? "-",
                    r.Status,
                    r.CreatedAt.ToString("yyyy-MM-dd")
                }).ToList();

                GeneratePdfReport(
                    "Retention Report",
                    $"AlphaElite_RetentionReport_{DateTime.Now:yyyy-MM-dd}.pdf",
                    new[] { "Member", "Reason", "Action", "Promotion", "Status", "Created" },
                    new[] { 0.2f, 0.2f, 0.18f, 0.17f, 0.13f, 0.12f },
                    rows,
                    $"Total Retention Records: {records.Count}{ScopeLabel()}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export error: " + ex.Message);
            }
        }

        private async void btnExportDashboard_Click(object sender, EventArgs e)
        {
            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

                // Scope every metric the same way the on-screen dashboard does.
                var scopedMembers = ApplyScope(tenantDb.Members.AsNoTracking(), tenantDb, m => m.BranchId, m => m.MemberId);
                var scopedAttendance = ApplyScope(tenantDb.Attendances.AsNoTracking(), tenantDb, a => a.BranchId, a => a.MemberId);
                var scopedPayments = ApplyScope(tenantDb.Payments.AsNoTracking(), tenantDb, p => p.BranchId, p => p.MemberId);
                var scopedFeedback = ApplyScope(tenantDb.Feedbacks.AsNoTracking(), tenantDb, f => f.BranchId, f => f.MemberId);

                var totalMembers = await scopedMembers.CountAsync();
                var activeMembers = await scopedMembers.CountAsync(x => x.IsActive);
                var inactiveMembers = totalMembers - activeMembers;

                var today = DateTime.Today;
                var attendanceToday = await scopedAttendance.CountAsync(x => x.CheckInDate.Date == today);

                int diffToMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                var startOfWeek = today.AddDays(-diffToMonday);
                var attendanceThisWeek = await scopedAttendance.CountAsync(x => x.CheckInDate.Date >= startOfWeek);
                var attendanceThisMonth = await scopedAttendance.CountAsync(x => x.CheckInDate.Date >= new DateTime(today.Year, today.Month, 1));

                var pendingInquiries = await ApplyScope(tenantDb.Inquiries.AsNoTracking(), tenantDb, i => i.BranchId, null).CountAsync(x => !x.IsFollowedUp);

                var hasFeedback = await scopedFeedback.AnyAsync();
                var avgRating = hasFeedback ? await scopedFeedback.AverageAsync(x => x.Rating) : 0.0;

                var totalPayments = await scopedPayments.SumAsync(x => (decimal?)x.AmountPaid) ?? 0m;

                var openComplaints = 0;
                if (tenantDb.IsBranchAware)
                {
                    openComplaints = await ApplyScope(tenantDb.Complaints.AsNoTracking(), tenantDb, c => c.BranchId, c => c.MemberId)
                        .CountAsync(c => c.Status != "Resolved" && c.Status != "Closed");
                }

                // At-risk (same rules as the Retention tab, scope-filtered).
                var membersForRisk = await scopedMembers.Where(x => x.IsActive).ToListAsync();
                var attendancesForRisk = await scopedAttendance.ToListAsync();
                var paymentsForRisk = await scopedPayments.ToListAsync();
                var memberIds = await ApplyScope(tenantDb.Members.AsNoTracking(), tenantDb, m => m.BranchId, m => m.MemberId)
                    .Select(m => m.MemberId).ToListAsync();
                var memberSet = memberIds.ToHashSet();
                var subscriptionsForRisk = (await tenantDb.Subscriptions.AsNoTracking().ToListAsync())
                    .Where(s => memberSet.Contains(s.MemberId)).ToList();
                var atRiskCount = await ComputeAtRiskCountAsync(membersForRisk, attendancesForRisk, paymentsForRisk, subscriptionsForRisk,
                    (int)nudInactivityDays.Value, (int)nudDueSoonDays.Value);

                var rows = new List<string[]>
                {
                    new[] { "Total Members", totalMembers.ToString() },
                    new[] { "Active Members", activeMembers.ToString() },
                    new[] { "Inactive Members", inactiveMembers.ToString() },
                    new[] { "Average Rating", $"{avgRating:0.0} / 5" },
                    new[] { "Attendance Today", attendanceToday.ToString() },
                    new[] { "Attendance This Week", attendanceThisWeek.ToString() },
                    new[] { "Attendance This Month", attendanceThisMonth.ToString() },
                    new[] { "Total Payments", totalPayments.ToString("C2") },
                    new[] { "Open Complaints", openComplaints.ToString() },
                    new[] { "At-Risk Members", atRiskCount.ToString() },
                    new[] { "Pending Inquiries", pendingInquiries.ToString() }
                };

                // ---- Monthly payment totals (last 6 months, same window as the dashboard chart) ----
                var sixMonthsAgo = DateTime.Today.AddMonths(-5);
                var startOfRange = new DateTime(sixMonthsAgo.Year, sixMonthsAgo.Month, 1);

                var paymentRaw = await scopedPayments
                    .Where(x => x.PaymentDate >= startOfRange)
                    .Select(x => new { x.PaymentDate, x.AmountPaid })
                    .ToListAsync();

                var paymentGrouped = paymentRaw
                    .GroupBy(x => new { x.PaymentDate.Year, x.PaymentDate.Month })
                    .ToDictionary(g => new DateTime(g.Key.Year, g.Key.Month, 1), g => g.Sum(x => x.AmountPaid));

                rows.Add(new[] { "", "" });
                rows.Add(new[] { "Monthly Payment Totals (last 6 months)", "" });
                for (int i = 0; i < 6; i++)
                {
                    var month = startOfRange.AddMonths(i);
                    decimal total = paymentGrouped.TryGetValue(month, out var t) ? t : 0m;
                    rows.Add(new[] { "  " + month.ToString("MMMM yyyy"), total.ToString("C2") });
                }

                // ---- Feedback rating distribution ----
                var ratingCounts = await scopedFeedback
                    .GroupBy(x => x.Rating)
                    .Select(g => new { Rating = g.Key, Count = g.Count() })
                    .ToListAsync();

                rows.Add(new[] { "", "" });
                rows.Add(new[] { "Feedback Rating Distribution", "" });
                for (int r = 1; r <= 5; r++)
                {
                    int count = ratingCounts.FirstOrDefault(x => x.Rating == r)?.Count ?? 0;
                    rows.Add(new[] { "  " + new string('*', r), count.ToString() });
                }

                GeneratePdfReport(
                    "Dashboard Summary",
                    $"AlphaElite_DashboardSummary_{DateTime.Now:yyyy-MM-dd}.pdf",
                    new[] { "Metric", "Value" },
                    new[] { 0.6f, 0.4f },
                    rows,
                    null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export error: " + ex.Message);
            }
        }
    }
}