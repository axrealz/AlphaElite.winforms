namespace AlphaElite.winforms
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private TabControl tabControlMain;
        private TabPage tabDashboard;
        private TabPage tabMembers;
        private TabPage tabAttendance;
        private TabPage tabPayments;
        private TabPage tabFeedback;
        private TabPage tabInquiries;
        private TabPage tabSubscriptions;
        private TabPage tabRetention;

        // Shell: sidebar
        private Panel pnlSidebar;
        private Label lblSidebarBrandTitle;
        private Label lblSidebarBrandSubtitle;
        private Button btnNavDashboard;
        private Button btnNavMembers;
        private Button btnNavAttendance;
        private Button btnNavPayments;
        private Button btnNavFeedback;
        private Button btnNavInquiries;
        private Button btnNavSubscriptions;
        private Button btnNavRetention;
        private Button btnLogout;

        // Shell: header
        private Panel pnlHeader;
        private Label lblHeaderAppName;
        private Label lblHeaderUserName;
        private Label lblHeaderUserRole;
        private Label lblTenantSwitcher;
        private ComboBox cboTenantSwitcher;
        private Button btnRefreshDashboard;

        // Shell: content host
        private Panel pnlContent;

        // Members
        private Label lblMemberCode;
        private TextBox txtMemberCode;
        private Label lblMemberFullName;
        private TextBox txtMemberFullName;
        private Label lblMemberEmail;
        private TextBox txtMemberEmail;
        private CheckBox chkMemberIsActive;
        private Label lblMemberCreatedAt;
        private DateTimePicker dtpMemberCreatedAt;
        private Button btnMemberAdd;
        private Button btnMemberUpdate;
        private Button btnMemberDelete;
        private Button btnMemberClear;
        private Button btnExportMembers;
        private DataGridView dgvMembers;

        // Attendance
        private Label lblAttendanceMember;
        private ComboBox cboAttendanceMember;
        private Label lblAttendanceCheckIn;
        private DateTimePicker dtpAttendanceCheckIn;
        private Button btnAttendanceAdd;
        private Button btnAttendanceUpdate;
        private Button btnAttendanceDelete;
        private Button btnAttendanceClear;
        private Button btnExportAttendance;
        private DataGridView dgvAttendance;

        // Payments
        private Label lblPaymentMember;
        private ComboBox cboPaymentMember;
        private Label lblPaymentAmount;
        private TextBox txtPaymentAmount;
        private Label lblPaymentDate;
        private DateTimePicker dtpPaymentDate;
        private CheckBox chkPaymentHasDueDate;
        private Label lblPaymentNextDue;
        private DateTimePicker dtpPaymentNextDue;
        private Label lblPaymentNotes;
        private TextBox txtPaymentNotes;
        private Button btnPaymentAdd;
        private Button btnPaymentUpdate;
        private Button btnPaymentDelete;
        private Button btnPaymentClear;
        private Button btnExportPayments;
        private DataGridView dgvPayments;

        // Feedback
        private Label lblFeedbackMember;
        private ComboBox cboFeedbackMember;
        private Label lblFeedbackRating;
        private ComboBox cboFeedbackRating;
        private Label lblFeedbackComments;
        private TextBox txtFeedbackComments;
        private Label lblFeedbackSubmitted;
        private DateTimePicker dtpFeedbackSubmitted;
        private Button btnFeedbackAdd;
        private Button btnFeedbackUpdate;
        private Button btnFeedbackDelete;
        private Button btnFeedbackClear;
        private Button btnExportFeedback;
        private DataGridView dgvFeedback;

        // Inquiries
        private Label lblInquiryFullName;
        private TextBox txtInquiryFullName;
        private Label lblInquiryContact;
        private TextBox txtInquiryContact;
        private Label lblInquiryEmail;
        private TextBox txtInquiryEmail;
        private Label lblInquiryPlan;
        private TextBox txtInquiryPlan;
        private Label lblInquiryMessage;
        private TextBox txtInquiryMessage;
        private CheckBox chkInquiryFollowedUp;
        private Label lblInquiryCreatedAt;
        private DateTimePicker dtpInquiryCreatedAt;
        private Button btnInquiryAdd;
        private Button btnInquiryUpdate;
        private Button btnInquiryDelete;
        private Button btnInquiryClear;
        private Button btnExportInquiries;
        private DataGridView dgvInquiries;

        // Subscriptions
        private Label lblSubscriptionMember;
        private ComboBox cboSubscriptionMember;
        private Label lblPlanName;
        private TextBox txtPlanName;
        private Label lblPrice;
        private TextBox txtPrice;
        private Label lblBillingCycle;
        private ComboBox cboBillingCycle;
        private Label lblSubscriptionStartDate;
        private DateTimePicker dtpSubscriptionStartDate;
        private Label lblSubscriptionEndDate;
        private DateTimePicker dtpSubscriptionEndDate;
        private CheckBox chkSubscriptionIsActive;
        private Button btnSubscriptionAdd;
        private Button btnSubscriptionUpdate;
        private Button btnSubscriptionDelete;
        private Button btnSubscriptionClear;
        private DataGridView dgvSubscriptions;

        // Dashboard
        private Label lblKpiTotalMembers;
        private Label lblKpiActiveMembers;
        private Label lblKpiInactiveMembers;
        private Label lblKpiAvgRating;
        private Label lblKpiAttendanceToday;
        private Label lblKpiAttendanceWeek;
        private Label lblKpiPendingInquiries;
        private Button btnExportDashboard;
        private Label lblMembersFilterInfo;
        private Label lblAttendanceFilterInfo;
        private Label lblFeedbackFilterInfo;
        private Label lblInquiriesFilterInfo;
        private LiveChartsCore.SkiaSharpView.WinForms.CartesianChart chartAttendanceTrend;
        private LiveChartsCore.SkiaSharpView.WinForms.CartesianChart chartFeedbackDistribution;
        private LiveChartsCore.SkiaSharpView.WinForms.CartesianChart chartMonthlyPayments;
        private LiveChartsCore.SkiaSharpView.WinForms.PieChart chartActiveVsInactive;
        private Label lblChartAttendanceTitle;
        private Label lblChartFeedbackTitle;
        private Label lblChartPaymentsTitle;
        private Label lblChartActiveVsInactiveTitle;

        // Retention
        private Label lblInactivityThreshold;
        private NumericUpDown nudInactivityDays;
        private Label lblDueSoonWindow;
        private NumericUpDown nudDueSoonDays;
        private Button btnApplyRetentionFilters;
        private Label lblInactiveMembersHeader;
        private DataGridView dgvInactiveMembers;
        private Label lblDueSoonHeader;
        private DataGridView dgvDueSoonPayments;
        private Label lblPendingInquiriesHeader;
        private DataGridView dgvPendingInquiries;
        private Label lblExpiringSubscriptionsHeader;
        private DataGridView dgvExpiringSubscriptions;

        // Retention Records (workflow)
        private Label lblRetentionSectionHeader;
        private Label lblRetentionAtRisk;
        private Label lblRetentionPendingApproval;
        private Label lblRetentionApprovedCount;
        private Label lblRetentionMember;
        private ComboBox cboRetentionMember;
        private Label lblRetentionReason;
        private ComboBox cboRetentionReason;
        private Label lblRetentionAction;
        private ComboBox cboRetentionAction;
        private Label lblRetentionPromotion;
        private TextBox txtRetentionPromotion;
        private Label lblRetentionNotes;
        private TextBox txtRetentionNotes;
        private Button btnRetentionSubmit;
        private Button btnRetentionClear;
        private Label lblRetentionGridHeader;
        private DataGridView dgvRetentionRecords;
        private Button btnRetentionApprove;
        private Button btnRetentionReject;
        private Button btnRetentionComplete;
        private Button btnExportRetention;

        // ===== Alpha Elite brand palette (shared with LoginForm) =====
        // The palette lives in one place (UiTheme) so the tenant window, the master window and
        // the theme normalizer can never drift apart. Values are unchanged.
        private static readonly System.Drawing.Color ColorBackground = UiTheme.Background;
        private static readonly System.Drawing.Color ColorSidebar = UiTheme.Sidebar;
        private static readonly System.Drawing.Color ColorHeader = UiTheme.Panel;
        private static readonly System.Drawing.Color ColorFieldBackground = UiTheme.Field;
        private static readonly System.Drawing.Color ColorGold = UiTheme.Gold;
        private static readonly System.Drawing.Color ColorGoldHover = UiTheme.GoldHover;
        private static readonly System.Drawing.Color ColorNavIdle = UiTheme.NavIdle;
        private static readonly System.Drawing.Color ColorNavHover = UiTheme.NavHover;
        private static readonly System.Drawing.Color ColorTextPrimary = UiTheme.Text;
        private static readonly System.Drawing.Color ColorTextMuted = UiTheme.Muted;

        private void InitializeComponent()
        {
            tabControlMain = new TabControl();
            tabDashboard = new TabPage();
            tabMembers = new TabPage();
            tabAttendance = new TabPage();
            tabPayments = new TabPage();
            tabFeedback = new TabPage();
            tabInquiries = new TabPage();
            tabSubscriptions = new TabPage();
            tabRetention = new TabPage();

            BackColor = ColorBackground;
            Font = new System.Drawing.Font("Segoe UI", 9F);

            // ===== SIDEBAR =====
            pnlSidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 210,
                BackColor = ColorSidebar
            };

            lblSidebarBrandTitle = new Label
            {
                Text = "ALPHA ELITE",
                ForeColor = ColorGold,
                Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Location = new System.Drawing.Point(0, 24),
                Size = new System.Drawing.Size(210, 30)
            };

            lblSidebarBrandSubtitle = new Label
            {
                Text = "GYM MANAGEMENT",
                ForeColor = ColorTextMuted,
                Font = new System.Drawing.Font("Segoe UI", 7.5F),
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Location = new System.Drawing.Point(0, 54),
                Size = new System.Drawing.Size(210, 18)
            };

            btnNavDashboard = CreateNavButton("Dashboard", 100);
            btnNavMembers = CreateNavButton("Members", 144);
            btnNavAttendance = CreateNavButton("Attendance", 188);
            btnNavPayments = CreateNavButton("Payments", 232);
            btnNavFeedback = CreateNavButton("Feedback", 276);
            btnNavInquiries = CreateNavButton("Inquiries", 320);
            btnNavSubscriptions = CreateNavButton("Subscriptions", 364);
            btnNavRetention = CreateNavButton("Retention", 408);

            btnNavDashboard.Click += btnNavDashboard_Click;
            btnNavMembers.Click += btnNavMembers_Click;
            btnNavAttendance.Click += btnNavAttendance_Click;
            btnNavPayments.Click += btnNavPayments_Click;
            btnNavFeedback.Click += btnNavFeedback_Click;
            btnNavInquiries.Click += btnNavInquiries_Click;
            btnNavSubscriptions.Click += btnNavSubscriptions_Click;
            btnNavRetention.Click += btnNavRetention_Click;

            btnLogout = new Button
            {
                Text = "Logout",
                Location = new System.Drawing.Point(0, 620),
                Size = new System.Drawing.Size(210, 44),
                BackColor = ColorSidebar,
                ForeColor = ColorTextMuted,
                Font = new System.Drawing.Font("Segoe UI", 9.5F),
                FlatStyle = FlatStyle.Flat,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(24, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btnLogout.FlatAppearance.BorderSize = 0;
            btnLogout.FlatAppearance.MouseOverBackColor = ColorNavHover;
            btnLogout.Click += btnLogout_Click;

            pnlSidebar.Controls.Add(lblSidebarBrandTitle);
            pnlSidebar.Controls.Add(lblSidebarBrandSubtitle);
            pnlSidebar.Controls.Add(btnNavDashboard);
            pnlSidebar.Controls.Add(btnNavMembers);
            pnlSidebar.Controls.Add(btnNavAttendance);
            pnlSidebar.Controls.Add(btnNavPayments);
            pnlSidebar.Controls.Add(btnNavFeedback);
            pnlSidebar.Controls.Add(btnNavInquiries);
            pnlSidebar.Controls.Add(btnNavSubscriptions);
            pnlSidebar.Controls.Add(btnNavRetention);
            pnlSidebar.Controls.Add(btnLogout);

            // ===== HEADER =====
            pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = ColorHeader
            };

            lblHeaderAppName = new Label
            {
                Text = "Alpha Elite",
                ForeColor = ColorTextPrimary,
                Font = new System.Drawing.Font("Segoe UI", 13F, System.Drawing.FontStyle.Bold),
                Location = new System.Drawing.Point(24, 18),
                Size = new System.Drawing.Size(220, 28)
            };

            lblTenantSwitcher = new Label
            {
                Text = "Tenant:",
                ForeColor = ColorTextMuted,
                Location = new System.Drawing.Point(300, 22),
                Size = new System.Drawing.Size(50, 20)
            };
            cboTenantSwitcher = new ComboBox
            {
                Location = new System.Drawing.Point(350, 19),
                Size = new System.Drawing.Size(220, 20),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            // Tenant items are populated at runtime from the master database tenant list.
            btnRefreshDashboard = new Button
            {
                Text = "Refresh",
                Location = new System.Drawing.Point(470, 17),
                Size = new System.Drawing.Size(70, 24),
                BackColor = ColorFieldBackground,
                ForeColor = ColorTextPrimary,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnRefreshDashboard.FlatAppearance.BorderSize = 0;

            cboTenantSwitcher.SelectedIndexChanged += cboTenantSwitcher_SelectedIndexChanged;
            btnRefreshDashboard.Click += btnRefreshDashboard_Click;

            lblHeaderUserName = new Label
            {
                Text = "User",
                ForeColor = ColorTextPrimary,
                Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold),
                TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new System.Drawing.Point(600, 14),
                Size = new System.Drawing.Size(260, 18)
            };
            lblHeaderUserRole = new Label
            {
                Text = "Role",
                ForeColor = ColorGold,
                Font = new System.Drawing.Font("Segoe UI", 8F),
                TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new System.Drawing.Point(600, 34),
                Size = new System.Drawing.Size(260, 16)
            };

            pnlHeader.Controls.Add(lblHeaderAppName);
            pnlHeader.Controls.Add(lblTenantSwitcher);
            pnlHeader.Controls.Add(cboTenantSwitcher);
            pnlHeader.Controls.Add(btnRefreshDashboard);
            pnlHeader.Controls.Add(lblHeaderUserName);
            pnlHeader.Controls.Add(lblHeaderUserRole);

            // ===== CONTENT HOST (hosts the tab pages, native tab strip hidden) =====
            pnlContent = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColorBackground,
                Padding = new Padding(0)
            };

            var filterInfoColor = System.Drawing.Color.FromArgb(160, 120, 10);
            lblMembersFilterInfo = new Label { Text = string.Empty, Location = new System.Drawing.Point(20, 0), Size = new System.Drawing.Size(820, 18), ForeColor = filterInfoColor, Font = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Italic) };
            lblAttendanceFilterInfo = new Label { Text = string.Empty, Location = new System.Drawing.Point(20, 0), Size = new System.Drawing.Size(820, 18), ForeColor = filterInfoColor, Font = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Italic) };
            lblFeedbackFilterInfo = new Label { Text = string.Empty, Location = new System.Drawing.Point(20, 0), Size = new System.Drawing.Size(820, 18), ForeColor = filterInfoColor, Font = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Italic) };
            lblInquiriesFilterInfo = new Label { Text = string.Empty, Location = new System.Drawing.Point(20, 0), Size = new System.Drawing.Size(820, 18), ForeColor = filterInfoColor, Font = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Italic) };

            // ===== MEMBERS TAB =====
            lblMemberCode = new Label { Text = "Member Code", Location = new System.Drawing.Point(20, 20), Size = new System.Drawing.Size(100, 20) };
            txtMemberCode = new TextBox { Location = new System.Drawing.Point(130, 20), Size = new System.Drawing.Size(200, 20) };
            lblMemberFullName = new Label { Text = "Full Name", Location = new System.Drawing.Point(20, 50), Size = new System.Drawing.Size(100, 20) };
            txtMemberFullName = new TextBox { Location = new System.Drawing.Point(130, 50), Size = new System.Drawing.Size(200, 20) };
            lblMemberEmail = new Label { Text = "Email", Location = new System.Drawing.Point(20, 80), Size = new System.Drawing.Size(100, 20) };
            txtMemberEmail = new TextBox { Location = new System.Drawing.Point(130, 80), Size = new System.Drawing.Size(200, 20) };
            chkMemberIsActive = new CheckBox { Text = "Is Active", Location = new System.Drawing.Point(350, 50), Size = new System.Drawing.Size(100, 20), Checked = true };
            lblMemberCreatedAt = new Label { Text = "Created At", Location = new System.Drawing.Point(20, 110), Size = new System.Drawing.Size(100, 20) };
            dtpMemberCreatedAt = new DateTimePicker { Location = new System.Drawing.Point(130, 110), Size = new System.Drawing.Size(200, 20) };
            btnMemberAdd = new Button { Text = "Add", Location = new System.Drawing.Point(130, 150), Size = new System.Drawing.Size(80, 30) };
            btnMemberUpdate = new Button { Text = "Update", Location = new System.Drawing.Point(220, 150), Size = new System.Drawing.Size(80, 30) };
            btnMemberDelete = new Button { Text = "Delete", Location = new System.Drawing.Point(310, 150), Size = new System.Drawing.Size(80, 30) };
            btnMemberClear = new Button { Text = "Clear", Location = new System.Drawing.Point(400, 150), Size = new System.Drawing.Size(80, 30) };
            btnExportMembers = new Button { Text = "Export PDF", Location = new System.Drawing.Point(500, 150), Size = new System.Drawing.Size(110, 30) };
            dgvMembers = new DataGridView { Location = new System.Drawing.Point(20, 200), Size = new System.Drawing.Size(820, 340), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };

            btnMemberAdd.Click += btnMemberAdd_Click;
            btnMemberUpdate.Click += btnMemberUpdate_Click;
            btnMemberDelete.Click += btnMemberDelete_Click;
            btnMemberClear.Click += btnMemberClear_Click;
            btnExportMembers.Click += btnExportMembers_Click;
            dgvMembers.CellClick += dgvMembers_CellClick;

            tabMembers.Text = "Members";
            tabMembers.Controls.Add(lblMemberCode);
            tabMembers.Controls.Add(txtMemberCode);
            tabMembers.Controls.Add(lblMemberFullName);
            tabMembers.Controls.Add(txtMemberFullName);
            tabMembers.Controls.Add(lblMemberEmail);
            tabMembers.Controls.Add(txtMemberEmail);
            tabMembers.Controls.Add(chkMemberIsActive);
            tabMembers.Controls.Add(lblMemberCreatedAt);
            tabMembers.Controls.Add(dtpMemberCreatedAt);
            tabMembers.Controls.Add(btnMemberAdd);
            tabMembers.Controls.Add(btnMemberUpdate);
            tabMembers.Controls.Add(btnMemberDelete);
            tabMembers.Controls.Add(btnMemberClear);
            tabMembers.Controls.Add(btnExportMembers);
            tabMembers.Controls.Add(lblMembersFilterInfo);
            tabMembers.Controls.Add(dgvMembers);

            // ===== ATTENDANCE TAB =====
            lblAttendanceMember = new Label { Text = "Member", Location = new System.Drawing.Point(20, 20), Size = new System.Drawing.Size(100, 20) };
            cboAttendanceMember = new ComboBox { Location = new System.Drawing.Point(130, 20), Size = new System.Drawing.Size(200, 20), DropDownStyle = ComboBoxStyle.DropDownList };
            lblAttendanceCheckIn = new Label { Text = "Check-in Date", Location = new System.Drawing.Point(20, 50), Size = new System.Drawing.Size(100, 20) };
            dtpAttendanceCheckIn = new DateTimePicker { Location = new System.Drawing.Point(130, 50), Size = new System.Drawing.Size(200, 20), Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm" };
            btnAttendanceAdd = new Button { Text = "Add", Location = new System.Drawing.Point(130, 90), Size = new System.Drawing.Size(80, 30) };
            btnAttendanceUpdate = new Button { Text = "Update", Location = new System.Drawing.Point(220, 90), Size = new System.Drawing.Size(80, 30) };
            btnAttendanceDelete = new Button { Text = "Delete", Location = new System.Drawing.Point(310, 90), Size = new System.Drawing.Size(80, 30) };
            btnAttendanceClear = new Button { Text = "Clear", Location = new System.Drawing.Point(400, 90), Size = new System.Drawing.Size(80, 30) };
            btnExportAttendance = new Button { Text = "Export PDF", Location = new System.Drawing.Point(500, 90), Size = new System.Drawing.Size(110, 30) };
            dgvAttendance = new DataGridView { Location = new System.Drawing.Point(20, 140), Size = new System.Drawing.Size(820, 400), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };

            btnAttendanceAdd.Click += btnAttendanceAdd_Click;
            btnAttendanceUpdate.Click += btnAttendanceUpdate_Click;
            btnAttendanceDelete.Click += btnAttendanceDelete_Click;
            btnAttendanceClear.Click += btnAttendanceClear_Click;
            btnExportAttendance.Click += btnExportAttendance_Click;
            dgvAttendance.CellClick += dgvAttendance_CellClick;

            tabAttendance.Text = "Attendance";
            tabAttendance.Controls.Add(lblAttendanceMember);
            tabAttendance.Controls.Add(cboAttendanceMember);
            tabAttendance.Controls.Add(lblAttendanceCheckIn);
            tabAttendance.Controls.Add(dtpAttendanceCheckIn);
            tabAttendance.Controls.Add(btnAttendanceAdd);
            tabAttendance.Controls.Add(btnAttendanceUpdate);
            tabAttendance.Controls.Add(btnAttendanceDelete);
            tabAttendance.Controls.Add(btnAttendanceClear);
            tabAttendance.Controls.Add(btnExportAttendance);
            tabAttendance.Controls.Add(lblAttendanceFilterInfo);
            tabAttendance.Controls.Add(dgvAttendance);

            // ===== PAYMENTS TAB =====
            lblPaymentMember = new Label { Text = "Member", Location = new System.Drawing.Point(20, 20), Size = new System.Drawing.Size(100, 20) };
            cboPaymentMember = new ComboBox { Location = new System.Drawing.Point(130, 20), Size = new System.Drawing.Size(200, 20), DropDownStyle = ComboBoxStyle.DropDownList };
            lblPaymentAmount = new Label { Text = "Amount Paid", Location = new System.Drawing.Point(20, 50), Size = new System.Drawing.Size(100, 20) };
            txtPaymentAmount = new TextBox { Location = new System.Drawing.Point(130, 50), Size = new System.Drawing.Size(200, 20) };
            lblPaymentDate = new Label { Text = "Payment Date", Location = new System.Drawing.Point(20, 80), Size = new System.Drawing.Size(100, 20) };
            dtpPaymentDate = new DateTimePicker { Location = new System.Drawing.Point(130, 80), Size = new System.Drawing.Size(200, 20) };
            chkPaymentHasDueDate = new CheckBox { Text = "Has Due Date", Location = new System.Drawing.Point(350, 80), Size = new System.Drawing.Size(120, 20) };
            lblPaymentNextDue = new Label { Text = "Next Due Date", Location = new System.Drawing.Point(20, 110), Size = new System.Drawing.Size(100, 20) };
            dtpPaymentNextDue = new DateTimePicker { Location = new System.Drawing.Point(130, 110), Size = new System.Drawing.Size(200, 20) };
            lblPaymentNotes = new Label { Text = "Notes", Location = new System.Drawing.Point(20, 140), Size = new System.Drawing.Size(100, 20) };
            txtPaymentNotes = new TextBox { Location = new System.Drawing.Point(130, 140), Size = new System.Drawing.Size(300, 20) };
            btnPaymentAdd = new Button { Text = "Add", Location = new System.Drawing.Point(130, 180), Size = new System.Drawing.Size(80, 30) };
            btnPaymentUpdate = new Button { Text = "Update", Location = new System.Drawing.Point(220, 180), Size = new System.Drawing.Size(80, 30) };
            btnPaymentDelete = new Button { Text = "Delete", Location = new System.Drawing.Point(310, 180), Size = new System.Drawing.Size(80, 30) };
            btnPaymentClear = new Button { Text = "Clear", Location = new System.Drawing.Point(400, 180), Size = new System.Drawing.Size(80, 30) };
            btnExportPayments = new Button { Text = "Export PDF", Location = new System.Drawing.Point(500, 180), Size = new System.Drawing.Size(110, 30) };
            dgvPayments = new DataGridView { Location = new System.Drawing.Point(20, 230), Size = new System.Drawing.Size(820, 310), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };

            btnPaymentAdd.Click += btnPaymentAdd_Click;
            btnPaymentUpdate.Click += btnPaymentUpdate_Click;
            btnPaymentDelete.Click += btnPaymentDelete_Click;
            btnPaymentClear.Click += btnPaymentClear_Click;
            btnExportPayments.Click += btnExportPayments_Click;
            dgvPayments.CellClick += dgvPayments_CellClick;

            tabPayments.Text = "Payments";
            tabPayments.Controls.Add(lblPaymentMember);
            tabPayments.Controls.Add(cboPaymentMember);
            tabPayments.Controls.Add(lblPaymentAmount);
            tabPayments.Controls.Add(txtPaymentAmount);
            tabPayments.Controls.Add(lblPaymentDate);
            tabPayments.Controls.Add(dtpPaymentDate);
            tabPayments.Controls.Add(chkPaymentHasDueDate);
            tabPayments.Controls.Add(lblPaymentNextDue);
            tabPayments.Controls.Add(dtpPaymentNextDue);
            tabPayments.Controls.Add(lblPaymentNotes);
            tabPayments.Controls.Add(txtPaymentNotes);
            tabPayments.Controls.Add(btnPaymentAdd);
            tabPayments.Controls.Add(btnPaymentUpdate);
            tabPayments.Controls.Add(btnPaymentDelete);
            tabPayments.Controls.Add(btnPaymentClear);
            tabPayments.Controls.Add(btnExportPayments);
            tabPayments.Controls.Add(dgvPayments);

            // ===== FEEDBACK TAB =====
            lblFeedbackMember = new Label { Text = "Member", Location = new System.Drawing.Point(20, 20), Size = new System.Drawing.Size(100, 20) };
            cboFeedbackMember = new ComboBox { Location = new System.Drawing.Point(130, 20), Size = new System.Drawing.Size(200, 20), DropDownStyle = ComboBoxStyle.DropDownList };
            lblFeedbackRating = new Label { Text = "Rating", Location = new System.Drawing.Point(20, 50), Size = new System.Drawing.Size(100, 20) };
            cboFeedbackRating = new ComboBox { Location = new System.Drawing.Point(130, 50), Size = new System.Drawing.Size(100, 20), DropDownStyle = ComboBoxStyle.DropDownList };
            lblFeedbackComments = new Label { Text = "Comments", Location = new System.Drawing.Point(20, 80), Size = new System.Drawing.Size(100, 20) };
            txtFeedbackComments = new TextBox { Location = new System.Drawing.Point(130, 80), Size = new System.Drawing.Size(300, 60), Multiline = true };
            lblFeedbackSubmitted = new Label { Text = "Submitted At", Location = new System.Drawing.Point(20, 150), Size = new System.Drawing.Size(100, 20) };
            dtpFeedbackSubmitted = new DateTimePicker { Location = new System.Drawing.Point(130, 150), Size = new System.Drawing.Size(200, 20) };
            btnFeedbackAdd = new Button { Text = "Add", Location = new System.Drawing.Point(130, 190), Size = new System.Drawing.Size(80, 30) };
            btnFeedbackUpdate = new Button { Text = "Update", Location = new System.Drawing.Point(220, 190), Size = new System.Drawing.Size(80, 30) };
            btnFeedbackDelete = new Button { Text = "Delete", Location = new System.Drawing.Point(310, 190), Size = new System.Drawing.Size(80, 30) };
            btnFeedbackClear = new Button { Text = "Clear", Location = new System.Drawing.Point(400, 190), Size = new System.Drawing.Size(80, 30) };
            btnExportFeedback = new Button { Text = "Export PDF", Location = new System.Drawing.Point(500, 190), Size = new System.Drawing.Size(110, 30) };
            dgvFeedback = new DataGridView { Location = new System.Drawing.Point(20, 240), Size = new System.Drawing.Size(820, 300), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };

            btnFeedbackAdd.Click += btnFeedbackAdd_Click;
            btnFeedbackUpdate.Click += btnFeedbackUpdate_Click;
            btnFeedbackDelete.Click += btnFeedbackDelete_Click;
            btnFeedbackClear.Click += btnFeedbackClear_Click;
            btnExportFeedback.Click += btnExportFeedback_Click;
            dgvFeedback.CellClick += dgvFeedback_CellClick;

            tabFeedback.Text = "Feedback";
            tabFeedback.Controls.Add(lblFeedbackMember);
            tabFeedback.Controls.Add(cboFeedbackMember);
            tabFeedback.Controls.Add(lblFeedbackRating);
            tabFeedback.Controls.Add(cboFeedbackRating);
            tabFeedback.Controls.Add(lblFeedbackComments);
            tabFeedback.Controls.Add(txtFeedbackComments);
            tabFeedback.Controls.Add(lblFeedbackSubmitted);
            tabFeedback.Controls.Add(dtpFeedbackSubmitted);
            tabFeedback.Controls.Add(btnFeedbackAdd);
            tabFeedback.Controls.Add(btnFeedbackUpdate);
            tabFeedback.Controls.Add(btnFeedbackDelete);
            tabFeedback.Controls.Add(btnFeedbackClear);
            tabFeedback.Controls.Add(btnExportFeedback);
            tabFeedback.Controls.Add(lblFeedbackFilterInfo);
            tabFeedback.Controls.Add(dgvFeedback);

            // ===== INQUIRIES TAB =====
            lblInquiryFullName = new Label { Text = "Full Name", Location = new System.Drawing.Point(20, 20), Size = new System.Drawing.Size(100, 20) };
            txtInquiryFullName = new TextBox { Location = new System.Drawing.Point(150, 20), Size = new System.Drawing.Size(200, 20) };
            lblInquiryContact = new Label { Text = "Contact Number", Location = new System.Drawing.Point(20, 50), Size = new System.Drawing.Size(100, 20) };
            txtInquiryContact = new TextBox { Location = new System.Drawing.Point(150, 50), Size = new System.Drawing.Size(200, 20) };
            lblInquiryEmail = new Label { Text = "Email", Location = new System.Drawing.Point(20, 80), Size = new System.Drawing.Size(100, 20) };
            txtInquiryEmail = new TextBox { Location = new System.Drawing.Point(150, 80), Size = new System.Drawing.Size(200, 20) };
            lblInquiryPlan = new Label { Text = "Interested Plan", Location = new System.Drawing.Point(20, 110), Size = new System.Drawing.Size(100, 20) };
            txtInquiryPlan = new TextBox { Location = new System.Drawing.Point(150, 110), Size = new System.Drawing.Size(200, 20) };
            lblInquiryMessage = new Label { Text = "Message", Location = new System.Drawing.Point(20, 140), Size = new System.Drawing.Size(100, 20) };
            txtInquiryMessage = new TextBox { Location = new System.Drawing.Point(150, 140), Size = new System.Drawing.Size(300, 60), Multiline = true };
            chkInquiryFollowedUp = new CheckBox { Text = "Followed Up", Location = new System.Drawing.Point(470, 140), Size = new System.Drawing.Size(120, 20) };
            lblInquiryCreatedAt = new Label { Text = "Created At", Location = new System.Drawing.Point(20, 210), Size = new System.Drawing.Size(100, 20) };
            dtpInquiryCreatedAt = new DateTimePicker { Location = new System.Drawing.Point(150, 210), Size = new System.Drawing.Size(200, 20) };
            btnInquiryAdd = new Button { Text = "Add", Location = new System.Drawing.Point(150, 250), Size = new System.Drawing.Size(80, 30) };
            btnInquiryUpdate = new Button { Text = "Update", Location = new System.Drawing.Point(240, 250), Size = new System.Drawing.Size(80, 30) };
            btnInquiryDelete = new Button { Text = "Delete", Location = new System.Drawing.Point(330, 250), Size = new System.Drawing.Size(80, 30) };
            btnInquiryClear = new Button { Text = "Clear", Location = new System.Drawing.Point(420, 250), Size = new System.Drawing.Size(80, 30) };
            btnExportInquiries = new Button { Text = "Export PDF", Location = new System.Drawing.Point(510, 250), Size = new System.Drawing.Size(110, 30) };
            dgvInquiries = new DataGridView { Location = new System.Drawing.Point(20, 300), Size = new System.Drawing.Size(820, 240), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };

            btnInquiryAdd.Click += btnInquiryAdd_Click;
            btnInquiryUpdate.Click += btnInquiryUpdate_Click;
            btnInquiryDelete.Click += btnInquiryDelete_Click;
            btnInquiryClear.Click += btnInquiryClear_Click;
            btnExportInquiries.Click += btnExportInquiries_Click;
            dgvInquiries.CellClick += dgvInquiries_CellClick;

            tabInquiries.Text = "Inquiries";
            tabInquiries.Controls.Add(lblInquiryFullName);
            tabInquiries.Controls.Add(txtInquiryFullName);
            tabInquiries.Controls.Add(lblInquiryContact);
            tabInquiries.Controls.Add(txtInquiryContact);
            tabInquiries.Controls.Add(lblInquiryEmail);
            tabInquiries.Controls.Add(txtInquiryEmail);
            tabInquiries.Controls.Add(lblInquiryPlan);
            tabInquiries.Controls.Add(txtInquiryPlan);
            tabInquiries.Controls.Add(lblInquiryMessage);
            tabInquiries.Controls.Add(txtInquiryMessage);
            tabInquiries.Controls.Add(chkInquiryFollowedUp);
            tabInquiries.Controls.Add(lblInquiryCreatedAt);
            tabInquiries.Controls.Add(dtpInquiryCreatedAt);
            tabInquiries.Controls.Add(btnInquiryAdd);
            tabInquiries.Controls.Add(btnInquiryUpdate);
            tabInquiries.Controls.Add(btnInquiryDelete);
            tabInquiries.Controls.Add(btnInquiryClear);
            tabInquiries.Controls.Add(btnExportInquiries);
            tabInquiries.Controls.Add(lblInquiriesFilterInfo);
            tabInquiries.Controls.Add(dgvInquiries);

            // ===== SUBSCRIPTIONS TAB =====
            lblSubscriptionMember = new Label { Text = "Member", Location = new System.Drawing.Point(20, 20), Size = new System.Drawing.Size(100, 20) };
            cboSubscriptionMember = new ComboBox { Location = new System.Drawing.Point(130, 20), Size = new System.Drawing.Size(200, 20), DropDownStyle = ComboBoxStyle.DropDownList };
            lblPlanName = new Label { Text = "Plan Name", Location = new System.Drawing.Point(20, 50), Size = new System.Drawing.Size(100, 20) };
            txtPlanName = new TextBox { Location = new System.Drawing.Point(130, 50), Size = new System.Drawing.Size(200, 20) };
            lblPrice = new Label { Text = "Price", Location = new System.Drawing.Point(20, 80), Size = new System.Drawing.Size(100, 20) };
            txtPrice = new TextBox { Location = new System.Drawing.Point(130, 80), Size = new System.Drawing.Size(200, 20) };
            lblBillingCycle = new Label { Text = "Billing Cycle", Location = new System.Drawing.Point(20, 110), Size = new System.Drawing.Size(100, 20) };
            cboBillingCycle = new ComboBox { Location = new System.Drawing.Point(130, 110), Size = new System.Drawing.Size(200, 20), DropDownStyle = ComboBoxStyle.DropDownList };
            cboBillingCycle.Items.Add("Monthly");
            cboBillingCycle.Items.Add("Quarterly");
            cboBillingCycle.Items.Add("Annual");
            lblSubscriptionStartDate = new Label { Text = "Start Date", Location = new System.Drawing.Point(20, 140), Size = new System.Drawing.Size(100, 20) };
            dtpSubscriptionStartDate = new DateTimePicker { Location = new System.Drawing.Point(130, 140), Size = new System.Drawing.Size(200, 20) };
            lblSubscriptionEndDate = new Label { Text = "End Date", Location = new System.Drawing.Point(20, 170), Size = new System.Drawing.Size(100, 20) };
            dtpSubscriptionEndDate = new DateTimePicker { Location = new System.Drawing.Point(130, 170), Size = new System.Drawing.Size(200, 20) };
            chkSubscriptionIsActive = new CheckBox { Text = "Is Active", Location = new System.Drawing.Point(350, 140), Size = new System.Drawing.Size(100, 20), Checked = true };
            btnSubscriptionAdd = new Button { Text = "Add", Location = new System.Drawing.Point(130, 210), Size = new System.Drawing.Size(80, 30) };
            btnSubscriptionUpdate = new Button { Text = "Update", Location = new System.Drawing.Point(220, 210), Size = new System.Drawing.Size(80, 30) };
            btnSubscriptionDelete = new Button { Text = "Delete", Location = new System.Drawing.Point(310, 210), Size = new System.Drawing.Size(80, 30) };
            btnSubscriptionClear = new Button { Text = "Clear", Location = new System.Drawing.Point(400, 210), Size = new System.Drawing.Size(80, 30) };
            dgvSubscriptions = new DataGridView { Location = new System.Drawing.Point(20, 260), Size = new System.Drawing.Size(820, 280), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };

            btnSubscriptionAdd.Click += btnSubscriptionAdd_Click;
            btnSubscriptionUpdate.Click += btnSubscriptionUpdate_Click;
            btnSubscriptionDelete.Click += btnSubscriptionDelete_Click;
            btnSubscriptionClear.Click += btnSubscriptionClear_Click;
            dgvSubscriptions.CellClick += dgvSubscriptions_CellClick;

            tabSubscriptions.Text = "Subscriptions";
            tabSubscriptions.Controls.Add(lblSubscriptionMember);
            tabSubscriptions.Controls.Add(cboSubscriptionMember);
            tabSubscriptions.Controls.Add(lblPlanName);
            tabSubscriptions.Controls.Add(txtPlanName);
            tabSubscriptions.Controls.Add(lblPrice);
            tabSubscriptions.Controls.Add(txtPrice);
            tabSubscriptions.Controls.Add(lblBillingCycle);
            tabSubscriptions.Controls.Add(cboBillingCycle);
            tabSubscriptions.Controls.Add(lblSubscriptionStartDate);
            tabSubscriptions.Controls.Add(dtpSubscriptionStartDate);
            tabSubscriptions.Controls.Add(lblSubscriptionEndDate);
            tabSubscriptions.Controls.Add(dtpSubscriptionEndDate);
            tabSubscriptions.Controls.Add(chkSubscriptionIsActive);
            tabSubscriptions.Controls.Add(btnSubscriptionAdd);
            tabSubscriptions.Controls.Add(btnSubscriptionUpdate);
            tabSubscriptions.Controls.Add(btnSubscriptionDelete);
            tabSubscriptions.Controls.Add(btnSubscriptionClear);
            tabSubscriptions.Controls.Add(dgvSubscriptions);

            // ===== DASHBOARD TAB =====
            lblKpiTotalMembers = new Label { Text = "Total Members\n0", Location = new System.Drawing.Point(20, 20), Size = new System.Drawing.Size(190, 40), Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold), BackColor = ColorHeader, ForeColor = ColorGold, BorderStyle = BorderStyle.FixedSingle, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
            lblKpiActiveMembers = new Label { Text = "Active Members\n0", Location = new System.Drawing.Point(220, 20), Size = new System.Drawing.Size(190, 40), Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold), BackColor = ColorHeader, ForeColor = ColorGold, BorderStyle = BorderStyle.FixedSingle, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
            lblKpiInactiveMembers = new Label { Text = "Inactive Members\n0", Location = new System.Drawing.Point(420, 20), Size = new System.Drawing.Size(190, 40), Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold), BackColor = ColorHeader, ForeColor = ColorGold, BorderStyle = BorderStyle.FixedSingle, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
            lblKpiAvgRating = new Label { Text = "Avg Rating\n0", Location = new System.Drawing.Point(620, 20), Size = new System.Drawing.Size(190, 40), Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold), BackColor = ColorHeader, ForeColor = ColorGold, BorderStyle = BorderStyle.FixedSingle, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };

            lblKpiAttendanceToday = new Label { Text = "Attendance Today\n0", Location = new System.Drawing.Point(20, 70), Size = new System.Drawing.Size(190, 40), Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold), BackColor = ColorHeader, ForeColor = ColorGold, BorderStyle = BorderStyle.FixedSingle, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
            lblKpiAttendanceWeek = new Label { Text = "Attendance This Week\n0", Location = new System.Drawing.Point(220, 70), Size = new System.Drawing.Size(190, 40), Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold), BackColor = ColorHeader, ForeColor = ColorGold, BorderStyle = BorderStyle.FixedSingle, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
            lblKpiPendingInquiries = new Label { Text = "Pending Inquiries\n0", Location = new System.Drawing.Point(420, 70), Size = new System.Drawing.Size(190, 40), Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold), BackColor = ColorHeader, ForeColor = ColorGold, BorderStyle = BorderStyle.FixedSingle, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };

            btnExportDashboard = new Button { Text = "Export PDF", Location = new System.Drawing.Point(700, 20), Size = new System.Drawing.Size(110, 30), BackColor = ColorFieldBackground, ForeColor = ColorTextPrimary, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnExportDashboard.FlatAppearance.BorderSize = 0;
            btnExportDashboard.Click += btnExportDashboard_Click;

            lblChartAttendanceTitle = new Label { Text = "Attendance Trend (30 days)", Location = new System.Drawing.Point(20, 130), Size = new System.Drawing.Size(400, 20), Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold), ForeColor = ColorGold };
            chartAttendanceTrend = new LiveChartsCore.SkiaSharpView.WinForms.CartesianChart { Location = new System.Drawing.Point(20, 152), Size = new System.Drawing.Size(400, 198), BackColor = ColorHeader };

            lblChartFeedbackTitle = new Label { Text = "Feedback Rating Distribution", Location = new System.Drawing.Point(440, 130), Size = new System.Drawing.Size(400, 20), Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold), ForeColor = ColorGold };
            chartFeedbackDistribution = new LiveChartsCore.SkiaSharpView.WinForms.CartesianChart { Location = new System.Drawing.Point(440, 152), Size = new System.Drawing.Size(400, 198), BackColor = ColorHeader };

            lblChartPaymentsTitle = new Label { Text = "Monthly Payment Totals", Location = new System.Drawing.Point(20, 370), Size = new System.Drawing.Size(400, 20), Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold), ForeColor = ColorGold };
            chartMonthlyPayments = new LiveChartsCore.SkiaSharpView.WinForms.CartesianChart { Location = new System.Drawing.Point(20, 392), Size = new System.Drawing.Size(400, 198), BackColor = ColorHeader };

            lblChartActiveVsInactiveTitle = new Label { Text = "Active vs Inactive Members", Location = new System.Drawing.Point(440, 370), Size = new System.Drawing.Size(400, 20), Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold), ForeColor = ColorGold };
            chartActiveVsInactive = new LiveChartsCore.SkiaSharpView.WinForms.PieChart { Location = new System.Drawing.Point(440, 392), Size = new System.Drawing.Size(400, 198), BackColor = ColorHeader };

            tabDashboard.Text = "Dashboard";
            tabDashboard.BackColor = ColorBackground;
            tabDashboard.AutoScroll = true;
            tabDashboard.Controls.Add(lblKpiTotalMembers);
            tabDashboard.Controls.Add(lblKpiActiveMembers);
            tabDashboard.Controls.Add(lblKpiInactiveMembers);
            tabDashboard.Controls.Add(lblKpiAvgRating);
            tabDashboard.Controls.Add(lblKpiAttendanceToday);
            tabDashboard.Controls.Add(lblKpiAttendanceWeek);
            tabDashboard.Controls.Add(lblKpiPendingInquiries);
            tabDashboard.Controls.Add(btnExportDashboard);
            tabDashboard.Controls.Add(lblChartAttendanceTitle);
            tabDashboard.Controls.Add(chartAttendanceTrend);
            tabDashboard.Controls.Add(lblChartFeedbackTitle);
            tabDashboard.Controls.Add(chartFeedbackDistribution);
            tabDashboard.Controls.Add(lblChartPaymentsTitle);
            tabDashboard.Controls.Add(chartMonthlyPayments);
            tabDashboard.Controls.Add(lblChartActiveVsInactiveTitle);
            tabDashboard.Controls.Add(chartActiveVsInactive);

            // ===== RETENTION TAB =====
            lblInactivityThreshold = new Label { Text = "Inactivity threshold (days):", Location = new System.Drawing.Point(20, 20), Size = new System.Drawing.Size(180, 20) };
            nudInactivityDays = new NumericUpDown { Location = new System.Drawing.Point(210, 18), Size = new System.Drawing.Size(60, 20), Minimum = 1, Maximum = 365, Value = 14 };
            lblDueSoonWindow = new Label { Text = "Due-soon window (days):", Location = new System.Drawing.Point(290, 20), Size = new System.Drawing.Size(180, 20) };
            nudDueSoonDays = new NumericUpDown { Location = new System.Drawing.Point(480, 18), Size = new System.Drawing.Size(60, 20), Minimum = 1, Maximum = 90, Value = 7 };
            btnApplyRetentionFilters = new Button { Text = "Apply", Location = new System.Drawing.Point(560, 16), Size = new System.Drawing.Size(80, 25) };
            btnApplyRetentionFilters.Click += btnApplyRetentionFilters_Click;

            lblInactiveMembersHeader = new Label { Text = "Members with no recent check-in", Location = new System.Drawing.Point(20, 55), Size = new System.Drawing.Size(300, 20), Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold) };
            dgvInactiveMembers = new DataGridView { Location = new System.Drawing.Point(20, 80), Size = new System.Drawing.Size(820, 120), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ReadOnly = true, AllowUserToAddRows = false };

            lblDueSoonHeader = new Label { Text = "Payments due soon or overdue", Location = new System.Drawing.Point(20, 210), Size = new System.Drawing.Size(300, 20), Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold) };
            dgvDueSoonPayments = new DataGridView { Location = new System.Drawing.Point(20, 235), Size = new System.Drawing.Size(820, 120), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ReadOnly = true, AllowUserToAddRows = false };

            lblPendingInquiriesHeader = new Label { Text = "Pending inquiry follow-ups", Location = new System.Drawing.Point(20, 365), Size = new System.Drawing.Size(300, 20), Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold) };
            dgvPendingInquiries = new DataGridView { Location = new System.Drawing.Point(20, 390), Size = new System.Drawing.Size(820, 150), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ReadOnly = true, AllowUserToAddRows = false };

            lblExpiringSubscriptionsHeader = new Label { Text = "Subscriptions expiring soon (uses due-soon window)", Location = new System.Drawing.Point(20, 560), Size = new System.Drawing.Size(400, 20), Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold) };
            dgvExpiringSubscriptions = new DataGridView { Location = new System.Drawing.Point(20, 585), Size = new System.Drawing.Size(820, 150), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ReadOnly = true, AllowUserToAddRows = false };

            tabRetention.Text = "Retention";
            tabRetention.AutoScroll = true;
            tabRetention.Controls.Add(lblInactivityThreshold);
            tabRetention.Controls.Add(nudInactivityDays);
            tabRetention.Controls.Add(lblDueSoonWindow);
            tabRetention.Controls.Add(nudDueSoonDays);
            tabRetention.Controls.Add(btnApplyRetentionFilters);
            tabRetention.Controls.Add(lblInactiveMembersHeader);
            tabRetention.Controls.Add(dgvInactiveMembers);
            tabRetention.Controls.Add(lblDueSoonHeader);
            tabRetention.Controls.Add(dgvDueSoonPayments);
            tabRetention.Controls.Add(lblPendingInquiriesHeader);
            tabRetention.Controls.Add(dgvPendingInquiries);
            tabRetention.Controls.Add(lblExpiringSubscriptionsHeader);
            tabRetention.Controls.Add(dgvExpiringSubscriptions);

            // ---- Retention Records (workflow) ----
            lblRetentionSectionHeader = new Label { Text = "Retention Records", Location = new System.Drawing.Point(20, 760), Size = new System.Drawing.Size(400, 26), Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold) };

            lblRetentionAtRisk = new Label { Text = "AT-RISK MEMBERS\n0", Location = new System.Drawing.Point(20, 795), Size = new System.Drawing.Size(190, 50), Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold), BorderStyle = BorderStyle.FixedSingle, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
            lblRetentionPendingApproval = new Label { Text = "PENDING APPROVAL\n0", Location = new System.Drawing.Point(220, 795), Size = new System.Drawing.Size(190, 50), Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold), BorderStyle = BorderStyle.FixedSingle, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };
            lblRetentionApprovedCount = new Label { Text = "APPROVED\n0", Location = new System.Drawing.Point(420, 795), Size = new System.Drawing.Size(190, 50), Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold), BorderStyle = BorderStyle.FixedSingle, TextAlign = System.Drawing.ContentAlignment.MiddleCenter };

            lblRetentionMember = new Label { Text = "Member", Location = new System.Drawing.Point(20, 865), Size = new System.Drawing.Size(90, 20) };
            cboRetentionMember = new ComboBox { Location = new System.Drawing.Point(110, 863), Size = new System.Drawing.Size(200, 20), DropDownStyle = ComboBoxStyle.DropDownList };

            lblRetentionReason = new Label { Text = "Reason", Location = new System.Drawing.Point(330, 865), Size = new System.Drawing.Size(70, 20) };
            cboRetentionReason = new ComboBox { Location = new System.Drawing.Point(400, 863), Size = new System.Drawing.Size(220, 20), DropDownStyle = ComboBoxStyle.DropDownList };
            cboRetentionReason.Items.AddRange(new object[] {
                "Low Attendance", "No Recent Check-in", "Inactive Member",
                "Payment Issue", "Expiring Membership", "Negative Feedback",
                "Cancellation Risk", "Other"
            });

            lblRetentionAction = new Label { Text = "Action", Location = new System.Drawing.Point(20, 900), Size = new System.Drawing.Size(90, 20) };
            cboRetentionAction = new ComboBox { Location = new System.Drawing.Point(110, 898), Size = new System.Drawing.Size(200, 20), DropDownStyle = ComboBoxStyle.DropDownList };
            cboRetentionAction.Items.AddRange(new object[] {
                "Contact Member", "Phone Call", "Email", "Offer Promotion",
                "Offer Discount", "Membership Renewal", "Personal Follow-up", "Other"
            });

            lblRetentionPromotion = new Label { Text = "Promotion", Location = new System.Drawing.Point(330, 900), Size = new System.Drawing.Size(70, 20) };
            txtRetentionPromotion = new TextBox { Location = new System.Drawing.Point(400, 898), Size = new System.Drawing.Size(220, 20) };

            lblRetentionNotes = new Label { Text = "Notes", Location = new System.Drawing.Point(20, 935), Size = new System.Drawing.Size(90, 20) };
            txtRetentionNotes = new TextBox { Location = new System.Drawing.Point(110, 933), Size = new System.Drawing.Size(510, 50), Multiline = true };

            btnRetentionSubmit = new Button { Text = "Submit for Approval", Location = new System.Drawing.Point(110, 995), Size = new System.Drawing.Size(160, 30) };
            btnRetentionClear = new Button { Text = "Clear", Location = new System.Drawing.Point(280, 995), Size = new System.Drawing.Size(80, 30) };

            btnRetentionSubmit.Click += btnRetentionSubmit_Click;
            btnRetentionClear.Click += btnRetentionClear_Click;

            lblRetentionGridHeader = new Label { Text = "All Retention Records", Location = new System.Drawing.Point(20, 1040), Size = new System.Drawing.Size(300, 20), Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold) };
            dgvRetentionRecords = new DataGridView { Location = new System.Drawing.Point(20, 1065), Size = new System.Drawing.Size(820, 220), AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false };
            dgvRetentionRecords.CellClick += dgvRetentionRecords_CellClick;

            btnRetentionApprove = new Button { Text = "Approve", Location = new System.Drawing.Point(20, 1295), Size = new System.Drawing.Size(140, 32) };
            btnRetentionReject = new Button { Text = "Reject", Location = new System.Drawing.Point(170, 1295), Size = new System.Drawing.Size(140, 32) };
            btnRetentionComplete = new Button { Text = "Mark Completed", Location = new System.Drawing.Point(320, 1295), Size = new System.Drawing.Size(140, 32) };
            btnExportRetention = new Button { Text = "Export PDF Report", Location = new System.Drawing.Point(480, 1295), Size = new System.Drawing.Size(150, 32) };

            btnRetentionApprove.Click += btnRetentionApprove_Click;
            btnRetentionReject.Click += btnRetentionReject_Click;
            btnRetentionComplete.Click += btnRetentionComplete_Click;
            btnExportRetention.Click += btnExportRetention_Click;

            tabRetention.Controls.Add(lblRetentionSectionHeader);
            tabRetention.Controls.Add(lblRetentionAtRisk);
            tabRetention.Controls.Add(lblRetentionPendingApproval);
            tabRetention.Controls.Add(lblRetentionApprovedCount);
            tabRetention.Controls.Add(lblRetentionMember);
            tabRetention.Controls.Add(cboRetentionMember);
            tabRetention.Controls.Add(lblRetentionReason);
            tabRetention.Controls.Add(cboRetentionReason);
            tabRetention.Controls.Add(lblRetentionAction);
            tabRetention.Controls.Add(cboRetentionAction);
            tabRetention.Controls.Add(lblRetentionPromotion);
            tabRetention.Controls.Add(txtRetentionPromotion);
            tabRetention.Controls.Add(lblRetentionNotes);
            tabRetention.Controls.Add(txtRetentionNotes);
            tabRetention.Controls.Add(btnRetentionSubmit);
            tabRetention.Controls.Add(btnRetentionClear);
            tabRetention.Controls.Add(lblRetentionGridHeader);
            tabRetention.Controls.Add(dgvRetentionRecords);
            tabRetention.Controls.Add(btnRetentionApprove);
            tabRetention.Controls.Add(btnRetentionReject);
            tabRetention.Controls.Add(btnRetentionComplete);
            tabRetention.Controls.Add(btnExportRetention);

            // ===== TAB CONTROL (native headers hidden; sidebar drives navigation) =====
            tabControlMain.Dock = DockStyle.Fill;
            tabControlMain.Appearance = TabAppearance.FlatButtons;
            tabControlMain.ItemSize = new System.Drawing.Size(0, 1);
            tabControlMain.SizeMode = TabSizeMode.Fixed;
            tabControlMain.TabPages.Add(tabDashboard);
            tabControlMain.TabPages.Add(tabMembers);
            tabControlMain.TabPages.Add(tabAttendance);
            tabControlMain.TabPages.Add(tabPayments);
            tabControlMain.TabPages.Add(tabFeedback);
            tabControlMain.TabPages.Add(tabInquiries);
            tabControlMain.TabPages.Add(tabSubscriptions);
            tabControlMain.TabPages.Add(tabRetention);

            pnlContent.Controls.Add(tabControlMain);

            // ===== FORM =====
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(1100, 700);
            MinimumSize = new System.Drawing.Size(900, 600);

            // Dock order matters: Fill must be added first so Top/Left panels
            // lay out around it correctly.
            Controls.Add(pnlContent);
            Controls.Add(pnlHeader);
            Controls.Add(pnlSidebar);

            Text = "Alpha Elite Management";
            Load += Form1_Load;
        }

        private Button CreateNavButton(string text, int topOffset)
        {
            var button = new Button
            {
                Text = "   " + text,
                Location = new System.Drawing.Point(0, topOffset),
                Size = new System.Drawing.Size(210, 40),
                BackColor = ColorNavIdle,
                ForeColor = ColorTextPrimary,
                Font = new System.Drawing.Font("Segoe UI", 9.5F),
                FlatStyle = FlatStyle.Flat,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ColorNavHover;
            return button;
        }
    }
}