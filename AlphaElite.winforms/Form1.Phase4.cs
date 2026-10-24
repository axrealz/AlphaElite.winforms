using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 4 additions to the main window: session context, central authorization,
    /// branch/member scoping, and the Complaints + User Management tabs.
    ///
    /// Kept in a separate partial file so the existing (verified) Form1 behaviour stays intact.
    /// </summary>
    public partial class Form1
    {
        private static readonly Color Phase4Panel = Color.FromArgb(24, 24, 24);
        private static readonly Color Phase4Field = Color.FromArgb(38, 38, 38);
        private static readonly Color Phase4Text = Color.White;

        private TabPage tabComplaints = null!;
        private TabPage tabUserManagement = null!;
        private Button btnNavComplaints = null!;
        private Button btnNavUserManagement = null!;

        private DataGridView dgvComplaints = null!;
        private TextBox txtComplaintSubject = null!;
        private TextBox txtComplaintDescription = null!;
        private Button btnComplaintSubmit = null!;
        private ComboBox cboComplaintStatus = null!;
        private TextBox txtComplaintResolution = null!;
        private Button btnComplaintUpdate = null!;
        private Button btnComplaintDelete = null!;
        private int? _selectedComplaintId;

        // Complaint CRUD additions (Tenant A phase): member dropdown, category / priority /
        // staff pickers, the two date pickers and the selected-complaint indicator.
        private ComboBox cboComplaintMember = null!;
        private ComboBox cboComplaintCategory = null!;
        private ComboBox cboComplaintPriority = null!;
        private ComboBox cboComplaintStaff = null!;
        private DateTimePicker dtpComplaintSubmitted = null!;
        private DateTimePicker dtpComplaintResolved = null!;
        private Label lblComplaintSelected = null!;
        private Label lblComplaintCategory = null!;
        private Label lblComplaintPriority = null!;
        private Label lblComplaintStaff = null!;

        // Single definitions of the complaint input vocabularies (the project previously had
        // none; Status reuses the exact values the existing module already used).
        internal static readonly string[] ComplaintCategories =
            { "Service", "Staff", "Facility", "Equipment", "Billing", "Membership", "Other" };
        internal static readonly string[] ComplaintPriorities = { "Low", "Medium", "High", "Critical" };
        internal static readonly string[] ComplaintStatuses = { "Pending", "Under Review", "Resolved", "Closed" };

        /// <summary>The complaint form's fields, captured in one place for create and update.</summary>
        private sealed class ComplaintFormValues
        {
            public int MemberId { get; set; }
            public string? Category { get; set; }
            public string Subject { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public DateTime DateSubmitted { get; set; }
            public string? Priority { get; set; }
            public string Status { get; set; } = "Pending";
            public string? StaffAssigned { get; set; }
            public string? Resolution { get; set; }
            public DateTime? DateResolved { get; set; }
        }

        private DataGridView dgvUsers = null!;
        private TextBox txtUserUsername = null!;
        private TextBox txtUserFullName = null!;
        private ComboBox cboUserRole = null!;
        private Label lblUserBranch = null!;
        private ComboBox cboUserBranch = null!;
        private Label lblUserMember = null!;
        private ComboBox cboUserMember = null!;
        private CheckBox chkUserActive = null!;
        private Button btnUserCreate = null!;
        private Button btnUserUpdate = null!;
        private Button btnUserDelete = null!;
        private int? _selectedUserAccountId;

        private string _branchName = string.Empty;

        // =====================================================================================
        // Session / permissions
        // =====================================================================================

        private bool HasPermission(Permission permission) =>
            _authorization.IsAuthorized(_role, permission) || SessionGrants(permission);

        /// <summary>
        /// Tenant A phase: Tenant A (COMP001) has no Dashboard, so its Admin must neither see
        /// the navigation entry nor load the module. Every other tenant/role keeps its
        /// Dashboard exactly as before (Tenant B, Tenant C and Master are untouched).
        /// </summary>
        private bool DashboardHiddenForAdminA =>
            _role == Roles.Admin && string.Equals(_session.CompanyCode, "COMP001", StringComparison.OrdinalIgnoreCase);

        /// <summary>Tenant B phase: true for Tenant B (COMP002) sessions only.</summary>
        private bool IsTenantB => string.Equals(_session.CompanyCode, "COMP002", StringComparison.OrdinalIgnoreCase);

        /// <summary>Tenant C phase: true for Tenant C (COMP003) sessions only.</summary>
        private bool IsTenantC => string.Equals(_session.CompanyCode, "COMP003", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Tenant C phase: User Management uses the modernized layout (Tenant B's Status
        /// projection, operational columns, Delete button, record-only creates) while
        /// keeping the branch-aware pickers Tenant B does not have.
        /// </summary>
        private bool UsesModernUserManagement => IsTenantB || IsTenantC;

        /// <summary>
        /// Tenant B phase: session-scoped permission additions on top of the shared role
        /// matrix. Manager B holds ManagePayments ("Payment History &amp; Renewals" per the
        /// Tenant B requirement). Gated to Tenant B sessions only, so Tenant A, Tenant C
        /// and Master sessions resolve permissions exactly as before - Authorization.cs
        /// (shared by every tenant) is never modified.
        /// </summary>
        private bool SessionGrants(Permission permission) =>
            IsTenantB && _role == Roles.Manager && permission == Permission.ManagePayments;

        /// <summary>
        /// Central authorization gate used by every action. Hiding a control is never the
        /// only protection: an action calls this before it touches data.
        /// </summary>
        private void EnsurePermission(Permission permission)
        {
            if (SessionGrants(permission))
            {
                return;
            }

            _authorization.EnsureAuthorized(_role, permission);
        }        /// <summary>
        /// Tenant B phase: Tenant B's Manager is a staff supervisor - it may only create,
        /// edit and manage Gym Staff accounts (never Admin, Manager or Master users; other
        /// tenants' users are already unreachable through the tenant-scoped context).
        /// Admin B keeps full Tenant B user administration over the administrative/staff
        /// roles. Member is a customer self-service record, NOT an administrative role:
        /// for every Tenant B session (Admin and Manager) Member is neither selectable in
        /// the role selector nor manageable through this page - Member ROWS AND DATA STAY
        /// UNTOUCHED in the database. Throws so the existing handlers surface their
        /// standard "Not authorized" message.
        /// </summary>
        private void EnsureUserManageableBySession(string targetRole, string newRole)
        {
            // Tenant C phase: Member is a customer self-service record, NOT an
            // administrative role - no Tenant C session may create, edit or delete Member
            // accounts here (Member ROWS AND DATA STAY UNTOUCHED in the database). A branch
            // Manager may only manage Gym Staff and branch Manager accounts inside its own
            // branch (branch scoping is enforced separately via _branchId /
            // EnsureBranchAccess); Admin C manages the administrative/staff roles.
            if (IsTenantC)
            {
                if (string.Equals(targetRole, Roles.Member, StringComparison.Ordinal)
                    || string.Equals(newRole, Roles.Member, StringComparison.Ordinal))
                {
                    throw new UnauthorizedAccessException(
                        "Member is not an administrative/staff role and cannot be managed from Tenant C User Management.");
                }

                if (_role == Roles.Manager)
                {
                    var targetManageable = string.Equals(targetRole, Roles.GymStaff, StringComparison.Ordinal)
                        || string.Equals(targetRole, Roles.Manager, StringComparison.Ordinal);
                    var newRoleManageable = string.Equals(newRole, Roles.GymStaff, StringComparison.Ordinal)
                        || string.Equals(newRole, Roles.Manager, StringComparison.Ordinal);

                    if (!targetManageable || !newRoleManageable)
                    {
                        throw new UnauthorizedAccessException(
                            "A Tenant C Manager may only manage Gym Staff and branch Manager accounts.");
                    }
                }

                return;
            }

            if (!IsTenantB)
            {
                return;
            }

            if (string.Equals(targetRole, Roles.Member, StringComparison.Ordinal)
                || string.Equals(newRole, Roles.Member, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    "Member is not an administrative/staff role and cannot be managed from Tenant B User Management.");
            }

            if (_role != Roles.Manager)
            {
                return;
            }

            if (!string.Equals(targetRole, Roles.GymStaff, StringComparison.Ordinal)
                || !string.Equals(newRole, Roles.GymStaff, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    "A Tenant B Manager may only manage Gym Staff accounts.");
            }
        }

        /// <summary>
        /// UI-side guard for a single action. Returns false (and explains why) when the role
        /// lacks the permission; the data-layer check (<see cref="EnsurePermission"/>) remains
        /// the authoritative one.
        /// </summary>
        private bool Require(Permission permission)
        {
            if (HasPermission(permission))
            {
                return true;
            }

            MessageBox.Show($"Your role ({_role}) is not authorized to perform this action.",
                "Access denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return false;
        }

        private bool RequireAny(params Permission[] permissions)
        {
            if (permissions.Any(HasPermission))
            {
                return true;
            }

            MessageBox.Show($"Your role ({_role}) is not authorized to perform this action.",
                "Access denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return false;
        }

        private void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show("Not authorized: " + ex.Message, "Access denied",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database error: " + ex.Message);
            }
        }

        // =====================================================================================
        // Branch / member scoping
        //
        // Branch-scoped roles (Manager / GymStaff) only ever read their own BranchId's rows.
        // Member accounts are additionally restricted to their own MemberId. Admin / MasterAdmin
        // have no BranchId and therefore see the whole tenant.
        //
        // The predicates are only added to the query tree for branch-aware contexts, so the
        // legacy Tenant A / Tenant B model (which has no BranchId column) is never affected.
        // =====================================================================================

        private IQueryable<T> ApplyScope<T>(
            IQueryable<T> source,
            ITenantDataContext db,
            Expression<Func<T, int?>> branchKey,
            Expression<Func<T, int>>? memberKey)
        {
            if (db.IsBranchAware && _branchId.HasValue)
            {
                source = source.Where(KeyEquals(branchKey, _branchId.Value));
            }

            if (memberKey != null && _memberId.HasValue)
            {
                source = source.Where(KeyEquals(memberKey, _memberId.Value));
            }

            return source;
        }

        private static Expression<Func<T, bool>> KeyEquals<T>(Expression<Func<T, int?>> key, int value) =>
            Expression.Lambda<Func<T, bool>>(
                Expression.Equal(key.Body, Expression.Constant(value, typeof(int?))),
                key.Parameters);

        private static Expression<Func<T, bool>> KeyEquals<T>(Expression<Func<T, int>> key, int value) =>
            Expression.Lambda<Func<T, bool>>(
                Expression.Equal(key.Body, Expression.Constant(value, typeof(int))),
                key.Parameters);

        /// <summary>Throws when a branch-scoped user tries to touch another branch's record.</summary>
        private void EnsureBranchAccess(int? recordBranchId)
        {
            if (!_isBranchAware || !_branchId.HasValue || !recordBranchId.HasValue)
            {
                return;
            }

            if (recordBranchId.Value != _branchId.Value)
            {
                throw new UnauthorizedAccessException(
                    "This record belongs to another branch and cannot be accessed.");
            }
        }

        private void EnsureOwnMember(int memberId)
        {
            if (_memberId.HasValue && _memberId.Value != memberId)
            {
                throw new UnauthorizedAccessException("You can only access your own member records.");
            }
        }

        // =====================================================================================
        // Startup
        // =====================================================================================

        private void InitializePhase4()
        {
            if (_isBranchAware && _branchId.HasValue)
            {
                try
                {
                    // InitializeComponent() has already created controls, which installs a
                    // WindowsFormsSynchronizationContext. Blocking on async work directly here
                    // would deadlock, so the lookup runs on the thread pool via Task.Run.
                    _branchName = Task.Run(() => ResolveBranchNameAsync()).GetAwaiter().GetResult();
                }
                catch
                {
                    _branchName = $"Branch {_branchId}";
                }
            }

            BuildNav();
            ApplyRoleVisibility();
        }

        private async Task<string> ResolveBranchNameAsync()
        {
            await using var db = await _dataContextFactory.CreateAsync(_companyId, true);
            var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.BranchId == _branchId!.Value);
            return branch == null ? $"Branch {_branchId}" : $"{branch.BranchName} ({branch.BranchCode})";
        }

        private void BuildNav()
        {
            btnNavComplaints = CreateNavButton("Complaints", 452);
            btnNavComplaints.Click += async (s, e) =>
            {
                await LoadComplaintsAsync();
                tabControlMain.SelectedTab = tabComplaints;
                SetActiveNavButton(btnNavComplaints);
            };
            pnlSidebar.Controls.Add(btnNavComplaints);

            btnNavUserManagement = CreateNavButton("User Management", 496);
            btnNavUserManagement.Click += async (s, e) =>
            {
                await LoadUsersAsync();
                tabControlMain.SelectedTab = tabUserManagement;
                SetActiveNavButton(btnNavUserManagement);
            };
            pnlSidebar.Controls.Add(btnNavUserManagement);

            tabComplaints = BuildComplaintsTab();
            tabUserManagement = BuildUserManagementTab();

            tabControlMain.TabPages.Add(tabComplaints);
            tabControlMain.TabPages.Add(tabUserManagement);
        }

        // The intended navigation visibility. Desired visibility is recorded here because
        // Control.Visible reports effective visibility (always false while the form is unshown),
        // which would make a headless check meaningless.
        private readonly List<string> _visibleNav = new();
        private readonly System.Collections.Generic.HashSet<Control> _visibleNavButtons = new();

        private void SetNav(Control control, bool visible, string label)
        {
            control.Visible = visible;

            if (visible)
            {
                // Nav button captions carry leading padding for icon alignment.
                _visibleNav.Add(label.Trim());
                _visibleNavButtons.Add(control);
            }
            else
            {
                _visibleNavButtons.Remove(control);
            }
        }

        /// <summary>Shows only the navigation a role is actually permitted to use.</summary>
        private void ApplyRoleVisibility()
        {
            _visibleNav.Clear();
            _visibleNavButtons.Clear();

            // Header context: tenant + branch + user + role.
            var context = _session.CompanyName + " (" + _session.CompanyCode + ")";
            if (_isBranchAware)
            {
                context += "  |  " + (_branchName.Length > 0 ? _branchName : "All branches");
            }

            lblHeaderUserRole.Text = $"Role: {_role}   |   {context}";

            // Member self-service rewording (the controls are the same, the intent differs).
            if (_role == Roles.Member)
            {
                btnNavMembers.Text = "My Membership";
                btnNavAttendance.Text = "My Attendance";
                btnNavPayments.Text = "My Payments";
            }

            // Tenant A phase: Tenant A has no Dashboard - Admin A neither sees the entry nor
            // loads the module (every other tenant/role keeps it, unchanged).
            // Tenant B phase: Tenant B presents the same dashboard page under the label
            // "Business Intelligence" (label only - no second dashboard, no functional
            // change); every other tenant keeps the "Dashboard" label it has today.
            // Tenant C phase 9: the rename is per-tenant; Tenant A keeps "Dashboard".
            var dashboardLabel = IsTenantB || IsTenantC ? "Business Intelligence" : "Dashboard";
            btnNavDashboard.Text = "   " + dashboardLabel;
            if ((IsTenantB || IsTenantC) && tabDashboard != null) tabDashboard.Text = dashboardLabel;
            SetNav(btnNavDashboard, HasPermission(Permission.ViewDashboard) && !DashboardHiddenForAdminA, dashboardLabel);
            SetNav(btnNavMembers, HasPermission(Permission.ManageMembers) || HasPermission(Permission.SelfService), btnNavMembers.Text);
            SetNav(btnNavAttendance, HasPermission(Permission.ManageAttendance) || HasPermission(Permission.SelfService), btnNavAttendance.Text);
            SetNav(btnNavPayments, HasPermission(Permission.ManagePayments) || HasPermission(Permission.SelfService), btnNavPayments.Text);
            SetNav(btnNavFeedback, HasPermission(Permission.ManageFeedback) || HasPermission(Permission.SubmitFeedback), "Feedback");
            SetNav(btnNavInquiries, HasPermission(Permission.ManageInquiries), "Inquiries");
            SetNav(btnNavSubscriptions, HasPermission(Permission.ManageSubscriptions), "Subscriptions");
            SetNav(btnNavRetention, HasPermission(Permission.ManageRetention), "Retention");

            // Complaints is a normal tenant module now (Tenant A / Tenant B have a
            // non-branching Complaints table); only User Management stays a Tenant C feature.
            SetNav(btnNavComplaints, HasPermission(Permission.ManageComplaints) || HasPermission(Permission.SubmitComplaint), "Complaints");
            // Tenant B phase: User Management is a Tenant C feature AND a Tenant B feature
            // (Tenant B is non-branching, so the old _isBranchAware-only gate hid it).
            // Tenant A stays excluded: adminA holds ManageUsers but is gated out by the
            // company check (COMP002 only), so Tenant A's nav is unchanged.
            SetNav(btnNavUserManagement, (_isBranchAware || IsTenantB) && HasPermission(Permission.ManageUsers), "UserManagement");

            // Tenant A phase: the complaint form's staff-only fields (Category / Priority /
            // Staff Assigned) follow the same role rule as the rest of the module.
            ApplyComplaintFormVisibility();
        }

        /// <summary>Diagnostics only: the navigation items this role can see.</summary>
        internal string DescribeNav() =>
            _visibleNav.Count == 0 ? "(none)" : string.Join(", ", _visibleNav);

        /// <summary>Diagnostics only: proves the branch/member scoping actually filters data.</summary>
        internal async Task<string> DescribeScopedCountsAsync()
        {
            await using var db = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            if (!db.IsBranchAware)
            {
                return $"legacy: members={await db.Members.CountAsync()} payments={await db.Payments.CountAsync()}";
            }

            var members = await ApplyScope(db.Members.AsNoTracking(), db, m => m.BranchId, m => m.MemberId).CountAsync();
            var attendance = await ApplyScope(db.Attendances.AsNoTracking(), db, x => x.BranchId, x => x.MemberId).CountAsync();
            var payments = await ApplyScope(db.Payments.AsNoTracking(), db, x => x.BranchId, x => x.MemberId).CountAsync();
            var feedback = await ApplyScope(db.Feedbacks.AsNoTracking(), db, x => x.BranchId, x => x.MemberId).CountAsync();
            var inquiries = await ApplyScope(db.Inquiries.AsNoTracking(), db, x => x.BranchId, null).CountAsync();
            var complaints = await ApplyScope(db.Complaints.AsNoTracking(), db, c => c.BranchId, c => c.MemberId).CountAsync();

            return $"members={members} attendance={attendance} payments={payments} feedback={feedback} inquiries={inquiries} complaints={complaints}";
        }

        /// <summary>Diagnostics only: the effective permission check used by the data layer.</summary>
        internal bool CanPerform(Permission permission) => HasPermission(permission);

        /// <summary>Diagnostics only: describes the effective session scope without a GUI.</summary>
        internal string DescribeSession() =>
            $"role={_role}; branchAware={_isBranchAware}; branchId={(_branchId?.ToString() ?? "all")}; " +
            $"memberId={(_memberId?.ToString() ?? "n/a")}; permissions={_authorization.GetPermissions(_role).Count}";

        // =====================================================================================
        // COMPLAINTS
        // =====================================================================================

        private TabPage BuildComplaintsTab()
        {
            var tab = new TabPage("Complaints") { BackColor = Color.FromArgb(18, 18, 18), ForeColor = Phase4Text };

            // ---- Row 1 (y=16): Member | Category | Priority | Status ----
            tab.Controls.Add(new Label { Text = "Member", ForeColor = Phase4Text, Location = new Point(20, 18), Size = new Size(48, 20) });
            cboComplaintMember = new ComboBox { Location = new Point(70, 16), Size = new Size(180, 24), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Phase4Field, ForeColor = Phase4Text };
            tab.Controls.Add(cboComplaintMember);

            lblComplaintCategory = new Label { Text = "Category", ForeColor = Phase4Text, Location = new Point(260, 18), Size = new Size(62, 20) };
            tab.Controls.Add(lblComplaintCategory);
            cboComplaintCategory = new ComboBox { Location = new Point(324, 16), Size = new Size(110, 24), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Phase4Field, ForeColor = Phase4Text };
            cboComplaintCategory.Items.AddRange(ComplaintCategories);
            tab.Controls.Add(cboComplaintCategory);

            lblComplaintPriority = new Label { Text = "Priority", ForeColor = Phase4Text, Location = new Point(446, 18), Size = new Size(50, 20) };
            tab.Controls.Add(lblComplaintPriority);
            cboComplaintPriority = new ComboBox { Location = new Point(498, 16), Size = new Size(105, 24), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Phase4Field, ForeColor = Phase4Text };
            cboComplaintPriority.Items.AddRange(ComplaintPriorities);
            tab.Controls.Add(cboComplaintPriority);

            tab.Controls.Add(new Label { Text = "Status", ForeColor = Phase4Text, Location = new Point(615, 18), Size = new Size(45, 20) });
            cboComplaintStatus = new ComboBox { Location = new Point(662, 16), Size = new Size(110, 24), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Phase4Field, ForeColor = Phase4Text };
            cboComplaintStatus.Items.AddRange(ComplaintStatuses);
            tab.Controls.Add(cboComplaintStatus);

            // ---- Row 2 (y=44): Subject | Staff Assigned | Date Submitted | Date Resolved ----
            tab.Controls.Add(new Label { Text = "Subject", ForeColor = Phase4Text, Location = new Point(20, 46), Size = new Size(55, 20) });
            txtComplaintSubject = new TextBox { Location = new Point(78, 44), Size = new Size(140, 24), BackColor = Phase4Field, ForeColor = Phase4Text, BorderStyle = BorderStyle.FixedSingle };
            tab.Controls.Add(txtComplaintSubject);

            lblComplaintStaff = new Label { Text = "Staff Assigned", ForeColor = Phase4Text, Location = new Point(230, 46), Size = new Size(95, 20) };
            tab.Controls.Add(lblComplaintStaff);
            cboComplaintStaff = new ComboBox { Location = new Point(327, 44), Size = new Size(125, 24), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Phase4Field, ForeColor = Phase4Text };
            tab.Controls.Add(cboComplaintStaff);

            tab.Controls.Add(new Label { Text = "Date Submitted", ForeColor = Phase4Text, Location = new Point(464, 46), Size = new Size(92, 20) });
            dtpComplaintSubmitted = new DateTimePicker { Location = new Point(558, 44), Size = new Size(112, 24), Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm", BackColor = Phase4Field, ForeColor = Phase4Text, Value = DateTime.Now };
            tab.Controls.Add(dtpComplaintSubmitted);

            tab.Controls.Add(new Label { Text = "Date Resolved", ForeColor = Phase4Text, Location = new Point(682, 46), Size = new Size(90, 20) });
            dtpComplaintResolved = new DateTimePicker { Location = new Point(774, 44), Size = new Size(96, 24), Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", ShowCheckBox = true, Checked = false, Value = DateTime.Today, BackColor = Phase4Field, ForeColor = Phase4Text };
            tab.Controls.Add(dtpComplaintResolved);

            // ---- Row 3 (y=74): Description | Resolution / Response (multiline) ----
            tab.Controls.Add(new Label { Text = "Description", ForeColor = Phase4Text, Location = new Point(20, 76), Size = new Size(85, 20) });
            txtComplaintDescription = new TextBox { Location = new Point(110, 74), Size = new Size(290, 54), Multiline = true, BackColor = Phase4Field, ForeColor = Phase4Text, BorderStyle = BorderStyle.FixedSingle };
            tab.Controls.Add(txtComplaintDescription);

            tab.Controls.Add(new Label { Text = "Resolution / Response", ForeColor = Phase4Text, Location = new Point(412, 76), Size = new Size(125, 20) });
            txtComplaintResolution = new TextBox { Location = new Point(540, 74), Size = new Size(330, 54), Multiline = true, BackColor = Phase4Field, ForeColor = Phase4Text, BorderStyle = BorderStyle.FixedSingle };
            tab.Controls.Add(txtComplaintResolution);

            // ---- Row 4 (y=131): actions + selected-complaint indicator ----
            btnComplaintSubmit = new Button { Text = "Add complaint", Location = new Point(110, 131), Size = new Size(125, 25), BackColor = Color.FromArgb(212, 175, 55), ForeColor = Color.Black, FlatStyle = FlatStyle.Flat };
            btnComplaintSubmit.Click += async (s, e) => await SubmitComplaintAsync();
            tab.Controls.Add(btnComplaintSubmit);

            btnComplaintUpdate = new Button { Text = "Update selected", Location = new Point(243, 131), Size = new Size(135, 25), BackColor = Phase4Panel, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnComplaintUpdate.Click += async (s, e) => await UpdateComplaintAsync();
            tab.Controls.Add(btnComplaintUpdate);

            btnComplaintDelete = new Button { Text = "Delete selected", Location = new Point(386, 131), Size = new Size(125, 25), BackColor = Phase4Panel, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnComplaintDelete.Click += async (s, e) => await DeleteComplaintAsync();
            tab.Controls.Add(btnComplaintDelete);

            lblComplaintSelected = new Label { Text = "No complaint selected", ForeColor = Color.FromArgb(212, 175, 55), Location = new Point(525, 135), Size = new Size(340, 20) };
            tab.Controls.Add(lblComplaintSelected);

            // New-form defaults (a fresh complaint starts as an open Service/Medium/Pending
            // record submitted right now; Date Resolved stays empty until resolved).
            cboComplaintCategory.SelectedIndex = 0;
            cboComplaintPriority.SelectedItem = "Medium";
            cboComplaintStatus.SelectedItem = "Pending";

            dgvComplaints = new DataGridView
            {
                Location = new Point(20, 160),
                Size = new Size(960, 400),
                BackgroundColor = Color.FromArgb(18, 18, 18),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false
            };
            dgvComplaints.CellClick += async (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var item = dgvComplaints.Rows[e.RowIndex].DataBoundItem;
                var id = item?.GetType().GetProperty("ComplaintId")?.GetValue(item);
                if (id is int complaintId)
                {
                    try
                    {
                        // Selecting a row loads the complaint's FULL details into the form
                        // (detail view): all 11 fields, including Description and Resolution.
                        await LoadComplaintIntoFormAsync(complaintId);
                    }
                    catch (Exception ex)
                    {
                        ShowDbError(ex);
                    }
                }
            };
            tab.Controls.Add(dgvComplaints);

            return tab;
        }

        private async Task LoadComplaintsAsync()
        {
            await using var db = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            var query = ApplyScope(db.Complaints.AsNoTracking().Include(c => c.Member), db, c => c.BranchId, c => c.MemberId);

            // Materialize FIRST, then project in memory: the three CRUD columns (Category /
            // Priority / StaffAssigned) are mapped only in the legacy (Tenant A/B) model and
            // ignored in Tenant C's, so referencing them inside the SQL projection would break
            // Tenant C. Materializing lets every model bind the same grid shape.
            var records = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();

            dgvComplaints.DataSource = null;
            dgvComplaints.DataSource = records
                .Select(c => new
                {
                    c.ComplaintId,
                    Member = c.Member != null ? c.Member.FullName : "(unknown)",
                    c.Category,
                    c.Subject,
                    c.Description,
                    c.CreatedAt,
                    c.Priority,
                    c.Status,
                    c.StaffAssigned,
                    c.ResolutionNotes,
                    c.ResolvedAt
                })
                .ToList();

            ApplyComplaintGridFormatting();
            await PopulateComplaintPickersAsync(db);
        }

        private void ApplyComplaintGridFormatting()
        {
            var headers = new Dictionary<string, string>
            {
                ["ComplaintId"] = "Complaint ID",
                ["CreatedAt"] = "Date Submitted",
                ["StaffAssigned"] = "Staff Assigned",
                ["ResolvedAt"] = "Date Resolved"
            };

            foreach (var pair in headers)
            {
                var column = dgvComplaints.Columns[pair.Key];
                if (column != null)
                {
                    column.HeaderText = pair.Value;
                }
            }

            // Description and Resolution are detail-only (the form re-queries the record by id
            // when a row is selected), so they stay bound but hidden - leaving the list room
            // for the nine required columns.
            foreach (var property in new[] { "Description", "ResolutionNotes" })
            {
                var column = dgvComplaints.Columns[property];
                if (column != null)
                {
                    column.Visible = false;
                }
            }

            var submittedColumn = dgvComplaints.Columns["CreatedAt"];
            if (submittedColumn != null)
            {
                submittedColumn.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm";
            }

            var resolvedColumn = dgvComplaints.Columns["ResolvedAt"];
            if (resolvedColumn != null)
            {
                resolvedColumn.DefaultCellStyle.Format = "yyyy-MM-dd";
            }
        }

        /// <summary>
        /// Fills the Member and Staff Assigned pickers from the AUTHENTICATED tenant's own
        /// tables only (this tenant's members / this tenant's non-member accounts). The member
        /// picker is a DropDownList, so a MemberId can never be typed by hand and neither
        /// Tenant B nor Tenant C members can ever appear in Tenant A's form.
        /// </summary>
        private async Task PopulateComplaintPickersAsync(ITenantDataContext db)
        {
            var previousMemberId = ParsePickerId(cboComplaintMember.SelectedItem?.ToString());
            var previousStaff = cboComplaintStaff.SelectedItem?.ToString();

            cboComplaintMember.Items.Clear();
            var members = await ApplyScope(db.Members.AsNoTracking(), db, m => m.BranchId, m => m.MemberId)
                .OrderBy(m => m.FullName)
                .ThenBy(m => m.MemberId)
                .ToListAsync();

            foreach (var member in members)
            {
                cboComplaintMember.Items.Add($"{member.MemberId}: {member.FullName} ({member.MemberCode})");
            }

            if (HasPermission(Permission.ManageComplaints))
            {
                cboComplaintMember.Enabled = true;
                if (previousMemberId.HasValue)
                {
                    SelectMemberOption(cboComplaintMember, previousMemberId.Value);
                }
            }
            else
            {
                // Member self-service accounts can only ever complain about themselves.
                cboComplaintMember.Enabled = false;
                if (_memberId.HasValue)
                {
                    SelectMemberOption(cboComplaintMember, _memberId.Value);
                }
            }

            cboComplaintStaff.Items.Clear();
            var staffUsernames = await db.UserAccounts.AsNoTracking()
                .Where(u => u.Role != Roles.Member)
                .OrderBy(u => u.Username)
                .Select(u => u.Username)
                .ToListAsync();

            foreach (var username in staffUsernames)
            {
                cboComplaintStaff.Items.Add(username);
            }

            if (previousStaff != null && cboComplaintStaff.Items.Contains(previousStaff))
            {
                cboComplaintStaff.SelectedItem = previousStaff;
            }
        }

        private static void SelectMemberOption(ComboBox combo, int memberId)
        {
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (ParsePickerId(combo.Items[i]?.ToString()) == memberId)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            combo.SelectedIndex = -1;
        }

        private static void SelectComboValue(ComboBox combo, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                combo.SelectedIndex = -1;
                return;
            }

            if (!combo.Items.Contains(value))
            {
                // Values stored before the standard list existed stay selectable.
                combo.Items.Add(value);
            }

            combo.SelectedItem = value;
        }

        /// <summary>
        /// Create path shared by the button handler and the headless harness. Throws instead of
        /// showing dialogs so both call sites behave identically.
        /// </summary>
        private async Task<string> CreateComplaintFromFormCoreAsync()
        {
            var values = ReadComplaintForm();

            if (HasPermission(Permission.ManageComplaints))
            {
                // Staff/admin flow: the complaint belongs to the member selected in the dropdown.
                if (values.MemberId <= 0)
                {
                    throw new InvalidOperationException("Select a member for the complaint.");
                }
            }
            else
            {
                // Member self-service flow (unchanged semantics): always the caller's own
                // member record, always starting as Pending.
                EnsurePermission(Permission.SubmitComplaint);
                if (!_memberId.HasValue)
                {
                    throw new InvalidOperationException("This account is not linked to a member record, so it cannot submit a complaint.");
                }

                EnsureOwnMember(_memberId.Value);
                values.MemberId = _memberId.Value;
                values.Status = "Pending";

                if (string.IsNullOrWhiteSpace(values.Description))
                {
                    throw new InvalidOperationException("Enter a subject and details.");
                }
            }

            if (string.IsNullOrWhiteSpace(values.Subject))
            {
                throw new InvalidOperationException("Enter a subject for the complaint.");
            }

            await using var db = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            var member = await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.MemberId == values.MemberId);
            if (member == null)
            {
                throw new InvalidOperationException("The selected member record no longer exists.");
            }

            if (_isBranchAware)
            {
                EnsureBranchAccess(member.BranchId);
            }

            var complaint = new Complaint
            {
                MemberId = values.MemberId,
                Category = values.Category,
                Subject = values.Subject,
                Description = values.Description,
                CreatedAt = values.DateSubmitted,
                Priority = values.Priority,
                Status = values.Status,
                StaffAssigned = values.StaffAssigned,
                ResolutionNotes = string.IsNullOrWhiteSpace(values.Resolution) ? null : values.Resolution,
                ResolvedAt = values.DateResolved,
                // Legacy tenants ignore BranchId (no such column); Tenant C stamps it from the
                // selected member, exactly like the previous implementation.
                BranchId = member.BranchId
            };

            db.Complaints.Add(complaint);
            await db.SaveChangesAsync();

            return $"created:{complaint.ComplaintId}";
        }

        private async Task<string> CreateComplaintAndRefreshAsync()
        {
            var result = await CreateComplaintFromFormCoreAsync();
            var complaintId = int.Parse(result.Split(':')[1]);

            await LoadComplaintsAsync();
            SelectComplaintRowInGrid(complaintId);
            await LoadComplaintIntoFormAsync(complaintId); // display the newly created complaint

            return result;
        }

        private async Task SubmitComplaintAsync()
        {
            try
            {
                var result = await CreateComplaintAndRefreshAsync();
                MessageBox.Show($"Complaint #{result.Split(':')[1]} created.", "Complaints",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Complaints", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show("Not authorized: " + ex.Message, "Access denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                ShowDbError(ex);
            }
        }

        private async Task<string> UpdateSelectedComplaintCoreAsync()
        {
            EnsurePermission(Permission.ManageComplaints);

            if (!_selectedComplaintId.HasValue)
            {
                throw new InvalidOperationException("Select a complaint first.");
            }

            var values = ReadComplaintForm();
            if (string.IsNullOrWhiteSpace(values.Subject))
            {
                throw new InvalidOperationException("Enter a subject for the complaint.");
            }

            await using var db = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            var complaint = await db.Complaints.FirstOrDefaultAsync(c => c.ComplaintId == _selectedComplaintId.Value);
            if (complaint == null)
            {
                throw new InvalidOperationException("Complaint not found.");
            }

            EnsureBranchAccess(complaint.BranchId);

            var member = await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.MemberId == values.MemberId);
            if (member == null)
            {
                throw new InvalidOperationException("The selected member record no longer exists.");
            }

            if (_isBranchAware)
            {
                EnsureBranchAccess(member.BranchId);
            }

            // The Complaint ID is preserved (same tracked row, no new record is created).
            // DateSubmitted was loaded from the stored value, so it only changes if the user
            // actually edited the picker - never merely because the complaint was updated.
            complaint.MemberId = values.MemberId;
            complaint.Category = values.Category;
            complaint.Subject = values.Subject;
            complaint.Description = values.Description;
            complaint.CreatedAt = values.DateSubmitted;
            complaint.Priority = values.Priority;
            complaint.Status = values.Status;
            complaint.StaffAssigned = values.StaffAssigned;
            complaint.ResolutionNotes = string.IsNullOrWhiteSpace(values.Resolution) ? null : values.Resolution;
            complaint.ResolvedAt = values.DateResolved;
            complaint.BranchId = member.BranchId;

            await db.SaveChangesAsync();

            return $"updated:{complaint.ComplaintId}";
        }

        private async Task<string> UpdateSelectedComplaintAndRefreshAsync()
        {
            var result = await UpdateSelectedComplaintCoreAsync();
            var complaintId = int.Parse(result.Split(':')[1]);

            await LoadComplaintsAsync();
            SelectComplaintRowInGrid(complaintId);
            await LoadComplaintIntoFormAsync(complaintId); // the form shows the saved state

            return result;
        }

        private async Task UpdateComplaintAsync()
        {
            try
            {
                var result = await UpdateSelectedComplaintAndRefreshAsync();
                MessageBox.Show($"Complaint #{result.Split(':')[1]} updated.", "Complaints",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Complaints", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show("Not authorized: " + ex.Message, "Access denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                ShowDbError(ex);
            }
        }

        // =====================================================================================
        // Complaints: detail view, delete, and shared helpers
        // =====================================================================================

        private ComplaintFormValues ReadComplaintForm()
        {
            return new ComplaintFormValues
            {
                MemberId = ParsePickerId(cboComplaintMember.SelectedItem?.ToString()) ?? 0,
                Category = cboComplaintCategory.SelectedItem?.ToString(),
                Subject = txtComplaintSubject.Text?.Trim() ?? string.Empty,
                Description = txtComplaintDescription.Text?.Trim() ?? string.Empty,
                DateSubmitted = dtpComplaintSubmitted.Value,
                Priority = cboComplaintPriority.SelectedItem?.ToString(),
                Status = cboComplaintStatus.SelectedItem?.ToString() ?? "Pending",
                StaffAssigned = cboComplaintStaff.SelectedItem?.ToString(),
                Resolution = txtComplaintResolution.Text?.Trim(),
                DateResolved = dtpComplaintResolved.Checked ? dtpComplaintResolved.Value : null
            };
        }

        /// <summary>Loads a complaint's stored values into the form (the detail/edit view).</summary>
        private async Task LoadComplaintIntoFormAsync(int complaintId)
        {
            await using var db = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
            var complaint = await db.Complaints.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ComplaintId == complaintId);

            if (complaint == null)
            {
                _selectedComplaintId = null;
                lblComplaintSelected.Text = "Complaint not found.";
                return;
            }

            _selectedComplaintId = complaintId;
            SelectMemberOption(cboComplaintMember, complaint.MemberId);
            SelectComboValue(cboComplaintCategory, complaint.Category);
            txtComplaintSubject.Text = complaint.Subject ?? string.Empty;
            txtComplaintDescription.Text = complaint.Description ?? string.Empty;
            dtpComplaintSubmitted.Value =
                complaint.CreatedAt >= dtpComplaintSubmitted.MinDate && complaint.CreatedAt <= dtpComplaintSubmitted.MaxDate
                    ? complaint.CreatedAt
                    : DateTime.Now;
            SelectComboValue(cboComplaintPriority, complaint.Priority);
            if (complaint.Status != null && cboComplaintStatus.Items.Contains(complaint.Status))
            {
                cboComplaintStatus.SelectedItem = complaint.Status;
            }

            SelectComboValue(cboComplaintStaff, complaint.StaffAssigned);
            txtComplaintResolution.Text = complaint.ResolutionNotes ?? string.Empty;
            dtpComplaintResolved.Checked = complaint.ResolvedAt.HasValue;
            dtpComplaintResolved.Value = complaint.ResolvedAt
                ?? (DateTime.Today >= dtpComplaintResolved.MinDate ? DateTime.Today : dtpComplaintResolved.MinDate);
            lblComplaintSelected.Text = $"Selected: Complaint #{complaint.ComplaintId}";
        }

        private void SelectComplaintRowInGrid(int complaintId)
        {
            foreach (DataGridViewRow row in dgvComplaints.Rows)
            {
                if (row.Cells.Count > 0 && row.Cells["ComplaintId"].Value is int id && id == complaintId)
                {
                    dgvComplaints.ClearSelection();
                    row.Selected = true;
                    dgvComplaints.CurrentCell = row.Cells["ComplaintId"];
                    return;
                }
            }
        }

        private void ClearComplaintForm()
        {
            _selectedComplaintId = null;
            cboComplaintMember.SelectedIndex = -1;
            cboComplaintCategory.SelectedIndex = 0;
            txtComplaintSubject.Clear();
            txtComplaintDescription.Clear();
            dtpComplaintSubmitted.Value = DateTime.Now;
            cboComplaintPriority.SelectedItem = "Medium";
            cboComplaintStatus.SelectedItem = "Pending";
            cboComplaintStaff.SelectedIndex = -1;
            txtComplaintResolution.Clear();
            dtpComplaintResolved.Checked = false;
            dtpComplaintResolved.Value = DateTime.Today;
            lblComplaintSelected.Text = "No complaint selected";
        }

        private async Task<string> DeleteSelectedComplaintCoreAsync(int complaintId)
        {
            EnsurePermission(Permission.ManageComplaints);

            await using var db = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
            var complaint = await db.Complaints.FirstOrDefaultAsync(c => c.ComplaintId == complaintId);
            if (complaint == null)
            {
                throw new InvalidOperationException("Complaint not found.");
            }

            EnsureBranchAccess(complaint.BranchId);

            db.Complaints.Remove(complaint);
            await db.SaveChangesAsync();

            return $"deleted:{complaintId}";
        }

        private async Task<string> DeleteSelectedComplaintAndRefreshAsync(int complaintId)
        {
            var result = await DeleteSelectedComplaintCoreAsync(complaintId);
            await LoadComplaintsAsync();
            ClearComplaintForm();
            return result;
        }

        private async Task DeleteComplaintAsync()
        {
            try
            {
                if (!_selectedComplaintId.HasValue)
                {
                    MessageBox.Show("Select a complaint first.", "Complaints",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var complaintId = _selectedComplaintId.Value;
                var confirm = MessageBox.Show(
                    $"Delete complaint #{complaintId}? This cannot be undone.",
                    "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                {
                    return; // never delete without explicit confirmation
                }

                await DeleteSelectedComplaintAndRefreshAsync(complaintId);
                MessageBox.Show($"Complaint #{complaintId} deleted.", "Complaints",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Complaints", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show("Not authorized: " + ex.Message, "Access denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                ShowDbError(ex);
            }
        }

        /// <summary>
        /// Tenant A phase: Category / Priority / Staff Assigned are staff-handled fields.
        /// Member self-service accounts keep the original compact form (those values are not
        /// part of the self-service flow), and the button keeps its role-appropriate label.
        /// </summary>
        private void ApplyComplaintFormVisibility()
        {
            if (cboComplaintMember == null) return;

            var staffForm = HasPermission(Permission.ManageComplaints);
            foreach (var control in new Control[]
                     {
                         lblComplaintCategory, cboComplaintCategory, lblComplaintPriority, cboComplaintPriority,
                         lblComplaintStaff, cboComplaintStaff
                     })
            {
                control.Visible = staffForm;
            }

            btnComplaintSubmit.Text = staffForm ? "Add complaint" : "Submit complaint";
        }

        // =====================================================================================
        // Headless test hooks (Complaints CRUD verification)
        // =====================================================================================

        internal Task<string> SubmitComplaintForTestAsync() => CreateComplaintAndRefreshAsync();
        internal Task<string> UpdateComplaintForTestAsync() => UpdateSelectedComplaintAndRefreshAsync();
        internal Task<string> DeleteComplaintForTestAsync(int complaintId) => DeleteSelectedComplaintAndRefreshAsync(complaintId);
        internal Task LoadComplaintIntoFormForTestAsync(int complaintId) => LoadComplaintIntoFormAsync(complaintId);

        /// <summary>Sets the form's "selected complaint" the way a grid click would.</summary>
        internal void SelectComplaintForTest(int complaintId) => _selectedComplaintId = complaintId;

        /// <summary>Writes only the supplied (non-null) fields; the rest keep their current values.</summary>
        internal void SetComplaintFormForTest(
            int? memberId = null, string? category = null, string? subject = null, string? description = null,
            DateTime? dateSubmitted = null, string? priority = null, string? status = null, string? staff = null,
            string? resolution = null, DateTime? dateResolved = null)
        {
            if (memberId.HasValue) SelectMemberOption(cboComplaintMember, memberId.Value);
            if (category != null) SelectComboValue(cboComplaintCategory, category);
            if (subject != null) txtComplaintSubject.Text = subject;
            if (description != null) txtComplaintDescription.Text = description;
            if (dateSubmitted.HasValue) dtpComplaintSubmitted.Value = dateSubmitted.Value;
            if (priority != null) SelectComboValue(cboComplaintPriority, priority);
            if (status != null) SelectComboValue(cboComplaintStatus, status);
            if (staff != null) SelectComboValue(cboComplaintStaff, staff);
            if (resolution != null) txtComplaintResolution.Text = resolution;
            if (dateResolved.HasValue)
            {
                dtpComplaintResolved.Value = dateResolved.Value;
                dtpComplaintResolved.Checked = true;
            }
        }

        /// <summary>Reloads the list and returns its nine visible columns, one row per complaint.</summary>
        internal async Task<string> DescribeComplaintGridForTest()
        {
            await LoadComplaintsAsync();

            static string Get(object item, string property) =>
                item.GetType().GetProperty(property)?.GetValue(item)?.ToString() ?? string.Empty;

            var rows = new List<string>();
            foreach (DataGridViewRow row in dgvComplaints.Rows)
            {
                var item = row.DataBoundItem;
                if (item == null) continue;

                rows.Add(string.Join("|",
                    Get(item, "ComplaintId"), Get(item, "Member"), Get(item, "Category"), Get(item, "Subject"),
                    Get(item, "CreatedAt"), Get(item, "Priority"), Get(item, "Status"),
                    Get(item, "StaffAssigned"), Get(item, "ResolvedAt")));
            }

            return string.Join(";", rows);
        }

        /// <summary>Dumps the form's current values (the detail/edit view state).</summary>
        internal string DescribeComplaintFormForTest()
        {
            return $"member={cboComplaintMember.SelectedItem ?? "(none)"}; " +
                   $"category={cboComplaintCategory.SelectedItem ?? "(none)"}; " +
                   $"subject={txtComplaintSubject.Text}; " +
                   $"description={txtComplaintDescription.Text}; " +
                   $"submitted={dtpComplaintSubmitted.Value:yyyy-MM-dd HH:mm}; " +
                   $"priority={cboComplaintPriority.SelectedItem ?? "(none)"}; " +
                   $"status={cboComplaintStatus.SelectedItem ?? "(none)"}; " +
                   $"staff={cboComplaintStaff.SelectedItem ?? "(none)"}; " +
                   $"resolution={txtComplaintResolution.Text}; " +
                   $"resolved={(dtpComplaintResolved.Checked ? dtpComplaintResolved.Value.ToString("yyyy-MM-dd") : "(none)")}; " +
                   $"selected={_selectedComplaintId?.ToString() ?? "(none)"}; " +
                   $"memberStyle={cboComplaintMember.DropDownStyle}; " +
                   $"memberEnabled={cboComplaintMember.Enabled}";
        }

        /// <summary>Lists the member dropdown contents (proves the tenant-local source).</summary>
        internal async Task<string> DescribeComplaintMemberOptionsForTest()
        {
            await LoadComplaintsAsync();

            var members = new List<string>();
            for (var i = 0; i < cboComplaintMember.Items.Count; i++)
            {
                members.Add(cboComplaintMember.Items[i]?.ToString() ?? string.Empty);
            }

            var staff = new List<string>();
            for (var i = 0; i < cboComplaintStaff.Items.Count; i++)
            {
                staff.Add(cboComplaintStaff.Items[i]?.ToString() ?? string.Empty);
            }

            return $"style={cboComplaintMember.DropDownStyle}; " +
                   $"members=[{string.Join(" ; ", members)}]; " +
                   $"staff=[{string.Join(", ", staff)}]";
        }

        // =====================================================================================
        // USER MANAGEMENT
        // =====================================================================================

        private TabPage BuildUserManagementTab()
        {
            var tab = new TabPage("User Management") { BackColor = Color.FromArgb(18, 18, 18), ForeColor = Phase4Text };

            tab.Controls.Add(new Label { Text = "Username", ForeColor = Phase4Text, Location = new Point(20, 20), Size = new Size(100, 20) });
            txtUserUsername = new TextBox { Location = new Point(120, 18), Size = new Size(180, 24), BackColor = Phase4Field, ForeColor = Phase4Text, BorderStyle = BorderStyle.FixedSingle };
            tab.Controls.Add(txtUserUsername);

            tab.Controls.Add(new Label { Text = "Full name", ForeColor = Phase4Text, Location = new Point(320, 20), Size = new Size(100, 20) });
            txtUserFullName = new TextBox { Location = new Point(420, 18), Size = new Size(180, 24), BackColor = Phase4Field, ForeColor = Phase4Text, BorderStyle = BorderStyle.FixedSingle };
            tab.Controls.Add(txtUserFullName);

            tab.Controls.Add(new Label { Text = "Role", ForeColor = Phase4Text, Location = new Point(620, 20), Size = new Size(50, 20) });
            cboUserRole = new ComboBox { Location = new Point(670, 18), Size = new Size(130, 24), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Phase4Field, ForeColor = Phase4Text };
            // Tenant B phase: Member is a customer self-service record, not an
            // administrative/staff role, so Tenant B's selector offers ONLY Admin / Manager
            // / Gym Staff (never Member). Every other tenant keeps its four options.
            if (IsTenantB)
            {
                cboUserRole.Items.AddRange(new object[] { Roles.Admin, Roles.Manager, Roles.GymStaff });
            }
            else if (IsTenantC)
            {
                // Tenant C phase: Member is self-service, never an administrative role.
                // PopulateUserPickersAsync re-scopes this list per managing role.
                cboUserRole.Items.AddRange(new object[] { Roles.GymStaff, Roles.Manager, Roles.Admin });
            }
            else
            {
                cboUserRole.Items.AddRange(new object[] { Roles.Admin, Roles.Manager, Roles.GymStaff, Roles.Member });
            }
            tab.Controls.Add(cboUserRole);

            lblUserBranch = new Label { Text = "Branch", ForeColor = Phase4Text, Location = new Point(20, 56), Size = new Size(100, 20) };
            cboUserBranch = new ComboBox { Location = new Point(120, 54), Size = new Size(180, 24), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Phase4Field, ForeColor = Phase4Text };

            lblUserMember = new Label { Text = "Linked member", ForeColor = Phase4Text, Location = new Point(320, 56), Size = new Size(100, 20) };
            cboUserMember = new ComboBox { Location = new Point(420, 54), Size = new Size(180, 24), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Phase4Field, ForeColor = Phase4Text };

            // Tenant B phase (Admin + Manager): Tenant B is a NON-BRANCHING tenant - users
            // are scoped by tenant/database identity only, so the Branch and Linked member
            // pickers have no meaning here and are removed from the page. Tenant C
            // (branch-aware) keeps both pickers unchanged.
            if (IsTenantB)
            {
                lblUserBranch.Visible = false;
                cboUserBranch.Visible = false;
                lblUserMember.Visible = false;
                cboUserMember.Visible = false;
            }

            tab.Controls.Add(lblUserBranch);
            tab.Controls.Add(cboUserBranch);
            tab.Controls.Add(lblUserMember);
            tab.Controls.Add(cboUserMember);

            chkUserActive = new CheckBox { Text = "Active", ForeColor = Phase4Text, Location = new Point(620, 54), Size = new Size(80, 24), Checked = true };
            tab.Controls.Add(chkUserActive);

            btnUserCreate = new Button { Text = "Create user", Location = new Point(670, 90), Size = new Size(130, 30), BackColor = Color.FromArgb(212, 175, 55), ForeColor = Color.Black, FlatStyle = FlatStyle.Flat };
            btnUserCreate.Click += async (s, e) => await CreateUserAsync();
            tab.Controls.Add(btnUserCreate);

            btnUserUpdate = new Button { Text = "Update selected", Location = new Point(820, 90), Size = new Size(140, 30), BackColor = Phase4Panel, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnUserUpdate.Click += async (s, e) => await UpdateUserAsync();
            tab.Controls.Add(btnUserUpdate);

            // Complete CRUD (Tenant B + Tenant C): delete with confirmation and the same
            // role guards as create/update. Placed in the free left side of the same action
            // row. Tenant A keeps its existing user-management surface unchanged.
            if (UsesModernUserManagement)
            {
                btnUserDelete = new Button { Text = "Delete selected", Location = new Point(530, 90), Size = new Size(130, 30), BackColor = Color.FromArgb(150, 44, 44), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                btnUserDelete.Click += async (s, e) => await DeleteUserAsync();
                tab.Controls.Add(btnUserDelete);
            }

            dgvUsers = new DataGridView
            {
                Location = new Point(20, 130),
                Size = new Size(960, 430),
                BackgroundColor = Color.FromArgb(18, 18, 18),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false
            };
            dgvUsers.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var item = dgvUsers.Rows[e.RowIndex].DataBoundItem;
                if (item == null) return;

                var id = item.GetType().GetProperty("UserAccountId")?.GetValue(item);
                if (id is int accountId) _selectedUserAccountId = accountId;

                txtUserUsername.Text = item.GetType().GetProperty("Username")?.GetValue(item)?.ToString() ?? string.Empty;
                txtUserFullName.Text = item.GetType().GetProperty("FullName")?.GetValue(item)?.ToString() ?? string.Empty;
                chkUserActive.Checked = item.GetType().GetProperty("IsActive")?.GetValue(item) is true;

                var role = item.GetType().GetProperty("Role")?.GetValue(item)?.ToString();
                if (role != null && cboUserRole.Items.Contains(role)) cboUserRole.SelectedItem = role;
            };

            // (No CellFormatting mapping: for Tenant B the Status value is already the
            // display string in the projection - see LoadUsersAsync - so the Status column is
            // a plain text column and no bool-to-string formatting happens at paint time.
            // The raw bool column is removed after every bind, so it can never be formatted.
            // Tenant C keeps the raw flag exactly as today.)
            tab.Controls.Add(dgvUsers);

            return tab;
        }

        private async Task LoadUsersAsync()
        {
            EnsurePermission(Permission.ManageUsers);

            // Never select PasswordHash: only the safe columns are projected.
            await using var db = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            var query = db.UserAccounts.AsNoTracking().AsQueryable();
            if (_isBranchAware && _branchId.HasValue)
            {
                query = query.Where(u => u.BranchId == _branchId.Value);
            }

            // Tenant B phase: a Tenant B Manager is a staff supervisor - it views and
            // manages Gym Staff accounts only (never Admin/Manager/Member rows).
            if (IsTenantB && _role == Roles.Manager)
            {
                query = query.Where(u => u.Role == Roles.GymStaff);
            }

            dgvUsers.DataSource = null;
            if (UsesModernUserManagement)
            {
                // Tenant B phase: Status is projected as the DISPLAY STRING (Active /
                // Inactive) so the auto-generated grid column is a plain TEXT column.
                // Projecting the raw bool would auto-generate a DataGridViewCheckBoxColumn,
                // whose cell formatting throws FormatException ("Active is not a valid value
                // for Boolean") as soon as the string is displayed. The raw IsActive flag
                // stays in the projection because the row-selection handler reads it from
                // the DataBoundItem (its auto-generated checkbox column is removed below).
                dgvUsers.DataSource = await query
                    .OrderBy(u => u.Username)
                    .Select(u => new
                    {
                        u.UserAccountId,
                        u.Username,
                        u.FullName,
                        u.Role,
                        u.IsActive,
                        Status = u.IsActive ? "Active" : "Inactive",
                        BranchId = u.BranchId,
                        MemberId = u.MemberId,
                        u.CreatedAt
                    })
                    .ToListAsync();
            }
            else
            {
                // Tenant A / Tenant C: unchanged raw flag.
                dgvUsers.DataSource = await query
                    .OrderBy(u => u.Username)
                    .Select(u => new
                    {
                        u.UserAccountId,
                        u.Username,
                        u.FullName,
                        u.Role,
                        u.IsActive,
                        BranchId = u.BranchId,
                        MemberId = u.MemberId,
                        u.CreatedAt
                    })
                    .ToListAsync();
            }

            // Tenant B + Tenant C: the grid shows ONLY the operational fields -
            // Username, Full Name, Role, Status (the projected string) and Created At. The
            // bookkeeping columns (UserAccountId, BranchId, MemberId) stay in the projection
            // because the row-selection handler reads them from the DataBoundItem, but their
            // grid columns are hidden. Applied after EVERY bind: AutoGenerateColumns rebuilds
            // the columns each time, so a one-off hide would be undone by the next Refresh.
            // Tenant A keeps all columns unchanged.
            if (UsesModernUserManagement)
            {
                // The raw flag auto-generates a checkbox column: drop it so no Boolean-typed
                // column can ever be formatted or painted on this page.
                if (dgvUsers.Columns["IsActive"] is { } boolFlagColumn)
                {
                    dgvUsers.Columns.Remove(boolFlagColumn);
                }

                foreach (var column in dgvUsers.Columns.Cast<DataGridViewColumn>())
                {
                    column.Visible = column.Name is "Username" or "FullName" or "Role" or "Status" or "CreatedAt";
                }

                if (dgvUsers.Columns["FullName"] is { } fullNameColumn) fullNameColumn.HeaderText = "Full Name";
                if (dgvUsers.Columns["Status"] is { } statusColumn) statusColumn.HeaderText = "Status";
                if (dgvUsers.Columns["CreatedAt"] is { } createdAtColumn) createdAtColumn.HeaderText = "Created At";
            }

            await PopulateUserPickersAsync(db);
        }

        private async Task PopulateUserPickersAsync(ITenantDataContext db)
        {
            cboUserBranch.Items.Clear();

            if (_isBranchAware && _branchId.HasValue)
            {
                // Tenant C phase: a branch-scoped manager is pinned to its own branch - the
                // picker shows only that branch (no tenant-wide option, no other branches).
                var ownBranch = await db.Branches.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.BranchId == _branchId.Value);

                if (ownBranch != null)
                {
                    cboUserBranch.Items.Add($"{ownBranch.BranchId}: {ownBranch.BranchCode}");
                }
            }
            else
            {
                cboUserBranch.Items.Add("(tenant-wide)");

                if (db.IsBranchAware)
                {
                    foreach (var branch in await db.Branches.AsNoTracking().OrderBy(b => b.BranchId).ToListAsync())
                    {
                        cboUserBranch.Items.Add($"{branch.BranchId}: {branch.BranchCode}");
                    }
                }
            }

            if (cboUserBranch.SelectedIndex < 0 && cboUserBranch.Items.Count > 0) cboUserBranch.SelectedIndex = 0;

            cboUserMember.Items.Clear();
            cboUserMember.Items.Add("(none)");

            if (db.IsBranchAware)
            {
                var members = await ApplyScope(db.Members.AsNoTracking(), db, m => m.BranchId, m => m.MemberId)
                    .OrderBy(m => m.MemberId)
                    .ToListAsync();

                foreach (var member in members)
                {
                    cboUserMember.Items.Add($"{member.MemberId}: {member.FullName} ({member.MemberCode})");
                }
            }

            if (cboUserMember.SelectedIndex < 0 && cboUserMember.Items.Count > 0) cboUserMember.SelectedIndex = 0;
            // Tenant B phase: role options depend on the managing role - a Tenant B Manager
            // can only ever assign the Gym Staff role, while Tenant B Admin manages the
            // administrative/staff roles (Admin / Manager / Gym Staff, never Member).
            // Tenant C phase: Member is never an administrative role - Admin C manages
            // Gym Staff / Manager / Admin, and every other C role manages the staff roles
            // (the permission matrix still gates who reaches this page at all).
            // Every other tenant keeps the full role list it has today.
            var desiredRoles = IsTenantB
                ? (_role == Roles.Manager
                    ? new[] { Roles.GymStaff }
                    : new[] { Roles.Admin, Roles.Manager, Roles.GymStaff })
                : IsTenantC
                    ? (_role == Roles.Admin
                        ? new[] { Roles.GymStaff, Roles.Manager, Roles.Admin }
                        : new[] { Roles.GymStaff, Roles.Manager })
                    : new[] { Roles.Admin, Roles.Manager, Roles.GymStaff, Roles.Member };

            if (cboUserRole.Items.Count != desiredRoles.Length
                || !desiredRoles.Select((role, i) => Equals(cboUserRole.Items[i], role)).All(matches => matches))
            {
                cboUserRole.Items.Clear();
                cboUserRole.Items.AddRange(desiredRoles.Cast<object>().ToArray());
            }

            if (cboUserRole.SelectedIndex < 0 && cboUserRole.Items.Count > 0) cboUserRole.SelectedIndex = 0;
        }

        private async Task CreateUserAsync()
        {
            try
            {
                EnsurePermission(Permission.ManageUsers);

                await using var db = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                if (!db.IsBranchAware && !IsTenantB)
                {
                    if (!SuppressDialogs)
                    {
                        MessageBox.Show("User management is available for branch-aware tenants or Tenant B only.");
                    }
                    return;
                }

                var username = txtUserUsername.Text?.Trim();
                var fullName = txtUserFullName.Text?.Trim();
                var role = cboUserRole.SelectedItem?.ToString() ?? Roles.Member;

                // Tenant B phase: a Tenant B Manager may only ever create Gym Staff accounts.
                EnsureUserManageableBySession(role, role);

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName))
                {
                    if (!SuppressDialogs) MessageBox.Show("Enter a username and full name.");
                    return;
                }

                if (await db.UserAccounts.AnyAsync(u => u.Username == username))
                {
                    if (!SuppressDialogs) MessageBox.Show("That username already exists.");
                    return;
                }

                int? branchId = ParsePickerId(cboUserBranch.SelectedItem?.ToString());
                int? memberId = ParsePickerId(cboUserMember.SelectedItem?.ToString());

                // A branch-scoped manager can only create users inside their own branch.
                if (_branchId.HasValue && branchId.HasValue)
                {
                    EnsureBranchAccess(branchId);
                }
                else if (_branchId.HasValue && !branchId.HasValue)
                {
                    branchId = _branchId;
                }

                // Tenant C phase: a Gym Staff / Manager account must be stamped with a
                // branch. Branch-scoped sessions auto-stamp their own branch (above); the
                // tenant-wide Admin must pick one explicitly.
                if (IsTenantC && !branchId.HasValue
                    && (role == Roles.GymStaff || role == Roles.Manager))
                {
                    if (!SuppressDialogs)
                    {
                        MessageBox.Show("Select a branch for Gym Staff / Manager accounts.");
                    }

                    return;
                }

                if (role == Roles.Member && !memberId.HasValue)
                {
                    if (!SuppressDialogs) MessageBox.Show("A Member account must be linked to a member record.");
                    return;
                }

                // Tenant B + Tenant C: User Management creates the staff/USER RECORD ONLY.
                var password = UsesModernUserManagement ? null : GenerateSecurePassword();

                db.UserAccounts.Add(new UserAccount
                {
                    Username = username,
                    // Tenant B + Tenant C phase: User Management creates the staff/USER
                    // RECORD ONLY. It must NOT mint a login credential (no password, no
                    // one-time password dialog) - PasswordHasher.Verify rejects an empty
                    // hash at login, so the record carries no authentication account until
                    // credentials are provisioned through an explicit workflow.
                    // Existing accounts and their credentials are untouched, and every
                    // other tenant keeps its previous credential behaviour.
                    PasswordHash = password == null ? string.Empty : PasswordHasher.Hash(password),
                    FullName = fullName,
                    Role = role,
                    IsActive = chkUserActive.Checked,
                    CreatedAt = DateTime.UtcNow,
                    MemberId = role == Roles.Member ? memberId : memberId,
                    BranchId = branchId
                });

                await db.SaveChangesAsync();

                if (password != null)
                {
                    ShowOneTimeCredential(username, password);
                }
                else if (!SuppressDialogs)
                {
                    MessageBox.Show(
                        $"User record '{username}' created. No login credential was created for this record.",
                        "User record created", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                await LoadUsersAsync();
            }
            catch (UnauthorizedAccessException ex)
            {
                if (!SuppressDialogs)
                {
                    MessageBox.Show("Not authorized: " + ex.Message, "Access denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                if (!SuppressDialogs) MessageBox.Show("Database error: " + ex.Message);
            }
        }

        private async Task UpdateUserAsync()
        {
            try
            {
                EnsurePermission(Permission.ManageUsers);

                if (!_selectedUserAccountId.HasValue)
                {
                    if (!SuppressDialogs) MessageBox.Show("Select a user first.");
                    return;
                }

                await using var db = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

                var account = await db.UserAccounts.FirstOrDefaultAsync(u => u.UserAccountId == _selectedUserAccountId.Value);
                if (account == null)
                {
                    if (!SuppressDialogs) MessageBox.Show("User not found.");
                    return;
                }

                EnsureBranchAccess(account.BranchId);

                var role = cboUserRole.SelectedItem?.ToString() ?? account.Role;

                // Tenant B phase: a Tenant B Manager may only ever manage Gym Staff accounts.
                EnsureUserManageableBySession(account.Role, role);
                var branchId = ParsePickerId(cboUserBranch.SelectedItem?.ToString());
                var memberId = ParsePickerId(cboUserMember.SelectedItem?.ToString());

                if (_branchId.HasValue && branchId.HasValue)
                {
                    EnsureBranchAccess(branchId);
                }

                // Tenant C phase: an unset branch picker keeps the account's current branch
                // instead of clearing it - only an explicit branch choice reassigns.
                if (IsTenantC && !branchId.HasValue)
                {
                    branchId = account.BranchId;
                }

                account.FullName = txtUserFullName.Text?.Trim() ?? account.FullName;
                account.Role = role;
                account.IsActive = chkUserActive.Checked;
                account.BranchId = branchId;
                account.MemberId = memberId;

                await db.SaveChangesAsync();

                if (!SuppressDialogs) MessageBox.Show("User updated.");
                await LoadUsersAsync();
            }
            catch (UnauthorizedAccessException ex)
            {
                if (!SuppressDialogs)
                {
                    MessageBox.Show("Not authorized: " + ex.Message, "Access denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                if (!SuppressDialogs) MessageBox.Show("Database error: " + ex.Message);
            }
        }

        /// <summary>
        /// Complete CRUD: deletes the selected user record after confirmation, with the
        /// SAME role guards as create/update (Manager B: Gym Staff accounts only; Admin B:
        /// the allowed Tenant B roles; the signed-in account is never deletable). Nothing
        /// outside the single UserAccounts row is touched.
        /// </summary>
        private async Task DeleteUserAsync()
        {
            try
            {
                EnsurePermission(Permission.ManageUsers);

                if (!_selectedUserAccountId.HasValue)
                {
                    if (!SuppressDialogs) MessageBox.Show("Select a user first.");
                    return;
                }

                // Confirmation before deletion (headless verification runs treat a
                // suppressed dialog as confirmed and drive the core directly with an
                // explicit decision).
                var confirmed = SuppressDialogs
                    || MessageBox.Show(
                        "Delete the selected user record? This cannot be undone.",
                        "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

                var outcome = await DeleteUserCoreAsync(_selectedUserAccountId.Value, confirmed);

                if (SuppressDialogs)
                {
                    return;
                }

                switch (outcome)
                {
                    case "deleted":
                        MessageBox.Show("User deleted.");
                        break;
                    case "notfound":
                        MessageBox.Show("User not found.");
                        break;
                    case "self":
                        MessageBox.Show("You cannot delete the account you are signed in with.");
                        break;
                    case "canceled":
                        break;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                if (!SuppressDialogs)
                {
                    MessageBox.Show("Not authorized: " + ex.Message, "Access denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                if (!SuppressDialogs) MessageBox.Show("Database error: " + ex.Message);
            }
        }

        /// <summary>
        /// The deletion itself, UI-free so the confirmation decision can be made by the
        /// caller. Returns "deleted" | "notfound" | "self" | "canceled". Refreshes the
        /// grid after a successful delete (correct persistence + refresh requirement) and
        /// clears the editor controls so the deleted user's values never remain in them.
        /// </summary>
        private async Task<string> DeleteUserCoreAsync(int accountId, bool confirmed)
        {
            EnsurePermission(Permission.ManageUsers);

            await using var db = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

            var account = await db.UserAccounts.FirstOrDefaultAsync(u => u.UserAccountId == accountId);
            if (account == null)
            {
                return "notfound";
            }

            // Same authorization gate as create/update: Manager B deletes Gym Staff only;
            // Admin B the allowed roles; Member records are not manageable from Tenant B.
            EnsureUserManageableBySession(account.Role, account.Role);

            if (string.Equals(account.Username, _session.Username, StringComparison.OrdinalIgnoreCase))
            {
                return "self";
            }

            if (!confirmed)
            {
                return "canceled";
            }

            db.UserAccounts.Remove(account);
            await db.SaveChangesAsync();

            _selectedUserAccountId = null;

            // Stale-editor fix: CellClick fills the editor controls from the selected row,
            // so a deleted user's values must not stay in them. Clear Username / Full Name,
            // reset Role to the first allowed option and reset Status to the normal "Active"
            // default - the same state as "no selection" - then refresh the grid (below).
            txtUserUsername.Text = string.Empty;
            txtUserFullName.Text = string.Empty;
            chkUserActive.Checked = true;
            if (cboUserRole.Items.Count > 0) cboUserRole.SelectedIndex = 0;

            await LoadUsersAsync();
            return "deleted";
        }

        private static int? ParsePickerId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var head = value.Split(':')[0].Trim();
            return int.TryParse(head, out var id) ? id : null;
        }

        private static string GenerateSecurePassword()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%&*";
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var chars = new char[16];

            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = alphabet[bytes[i] % alphabet.Length];
            }

            return new string(chars);
        }

        /// <summary>
        /// Shows a newly generated password once. It is never written to a file, config or log.
        /// </summary>
        private static void ShowOneTimeCredential(string username, string password)
        {
            MessageBox.Show(
                $"User '{username}' created.\r\n\r\n" +
                $"One-time password (copy it now, it cannot be shown again):\r\n\r\n{password}\r\n\r\n" +
                "The password is stored only as a salted PBKDF2 hash.",
                "One-time credential",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
