using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 6 additions: exam-ready business features and UI polish.
    ///   - search boxes on the main modules
    ///   - new dashboard KPI cards (payments, open complaints, at-risk, attendance this month)
    ///   - branch comparison panel (Admin/Manager, branch-aware tenants only)
    ///   - member portal summary card
    ///   - payment renewal prefill + outstanding summary
    ///   - subscription active/expired status + export
    ///   - complaints export
    ///   - friendly database error messages (no raw SQL exceptions to end users)
    /// Kept in a separate partial so verified Phase 1-5 behaviour stays intact.
    /// </summary>
    public partial class Form1
    {
        // ----- Phase 6 controls (created in InitializePhase6Controls) -----
        private TextBox txtMemberSearch = null!;
        private Label lblMemberBranch = null!;
        private ComboBox cboMemberBranch = null!;
        private Label lblMemberSummary = null!;

        private TextBox txtAttendanceSearch = null!;
        private TextBox txtPaymentSearch = null!;
        private Button btnPaymentRenew = null!;
        private Label lblPaymentsSummary = null!;

        private TextBox txtInquirySearch = null!;
        private Label lblInquiryStats = null!;
        private TextBox txtFeedbackSearch = null!;
        private Label lblFeedbackStats = null!;
        private Label lblSubscriptionStats = null!;
        private Button btnExportSubscriptions = null!;
        private Button btnExportComplaints = null!;

        private Label lblKpiAttendanceMonth = null!;
        private Label lblKpiTotalPayments = null!;
        private Label lblKpiOpenComplaints = null!;
        private Label lblKpiAtRisk = null!;
        private Label lblBranchComparisonHeader = null!;
        private DataGridView dgvBranchComparison = null!;

        // Search terms (module-local, applied in the Load methods).
        private string _memberSearch = string.Empty;
        private string _attendanceSearch = string.Empty;
        private string _paymentSearch = string.Empty;
        private string _inquirySearch = string.Empty;
        private string _feedbackSearch = string.Empty;

        private const string DemoSuffix = "(demo)";

        // =====================================================================================
        // Control creation / layout (called once from InitializeComponent)
        // =====================================================================================

        private void InitializePhase6Controls()
        {
            var gold = Color.FromArgb(212, 175, 55);
            var cardBack = Color.FromArgb(28, 28, 28);
            var fieldBack = Color.FromArgb(38, 38, 38);
            var white = Color.White;
            var kpiFont = new Font("Segoe UI", 10F, FontStyle.Bold);

            // ---- KPI cards: taller (no clipped captions) + a third row of cards ----
            var kpiCards = new[] { lblKpiTotalMembers, lblKpiActiveMembers, lblKpiInactiveMembers, lblKpiAvgRating, lblKpiAttendanceToday, lblKpiAttendanceWeek, lblKpiPendingInquiries };
            for (var i = 0; i < kpiCards.Length; i++)
            {
                var row = i / 4; // first row 0..3, second row 4..6
                var col = i % 4;
                kpiCards[i].Size = new Size(190, 48);
                kpiCards[i].Location = new Point(20 + col * 200, 20 + row * 58);
                kpiCards[i].Font = kpiFont;
            }

            // Phase 7 fix: the designer placed the dashboard Export PDF button at (700,20),
            // where the relaid-out KPI cards completely covered it. Move it into the one
            // free cell of the KPI grid (right of the second row) and match the card size.
            btnExportDashboard.Location = new Point(620, 78);
            btnExportDashboard.Size = new Size(190, 48);

            lblKpiAttendanceMonth = new Label { Text = "Attendance This Month\n0", Location = new Point(20, 136), Size = new Size(190, 48), Font = kpiFont, BackColor = cardBack, ForeColor = gold, BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter };
            lblKpiTotalPayments = new Label { Text = "Total Payments\n0", Location = new Point(220, 136), Size = new Size(190, 48), Font = kpiFont, BackColor = cardBack, ForeColor = gold, BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter };
            lblKpiOpenComplaints = new Label { Text = "Open Complaints\n0", Location = new Point(420, 136), Size = new Size(190, 48), Font = kpiFont, BackColor = cardBack, ForeColor = gold, BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter };
            lblKpiAtRisk = new Label { Text = "At-Risk Members\n0", Location = new Point(620, 136), Size = new Size(190, 48), Font = kpiFont, BackColor = cardBack, ForeColor = gold, BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter };

            // Charts shift down to make room for the third KPI row.
            lblChartAttendanceTitle.Location = new Point(20, 200);
            chartAttendanceTrend.Location = new Point(20, 222);
            lblChartFeedbackTitle.Location = new Point(440, 200);
            chartFeedbackDistribution.Location = new Point(440, 222);
            lblChartPaymentsTitle.Location = new Point(20, 440);
            chartMonthlyPayments.Location = new Point(20, 462);
            lblChartActiveVsInactiveTitle.Location = new Point(440, 440);
            chartActiveVsInactive.Location = new Point(440, 462);

            // ---- Branch comparison panel (bottom of the dashboard) ----
            lblBranchComparisonHeader = new Label { Text = "Branch Comparison", Location = new Point(20, 690), Size = new Size(400, 20), Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = gold };
            dgvBranchComparison = new DataGridView
            {
                Location = new Point(20, 712),
                Size = new Size(820, 160),
                BackColor = Color.FromArgb(18, 18, 18),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false
            };
            tabDashboard.Controls.Add(lblKpiAttendanceMonth);
            tabDashboard.Controls.Add(lblKpiTotalPayments);
            tabDashboard.Controls.Add(lblKpiOpenComplaints);
            tabDashboard.Controls.Add(lblKpiAtRisk);
            tabDashboard.Controls.Add(lblBranchComparisonHeader);
            tabDashboard.Controls.Add(dgvBranchComparison);

            // ---- Members tab: search + branch picker + member summary card ----
            lblMemberSummary = new Label
            {
                Location = new Point(20, 20),
                Size = new Size(820, 120),
                BackColor = cardBack,
                ForeColor = gold,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };

            // Top-right of the staff form area (free space; keeps clear of the
            // existing entry fields and the Add/Update/Delete row).
            var lblMemberSearch = new Label { Text = "Search", Location = new Point(560, 24), Size = new Size(60, 20), ForeColor = white };
            txtMemberSearch = new TextBox { Location = new Point(620, 22), Size = new Size(220, 22), BackColor = fieldBack, ForeColor = white, BorderStyle = BorderStyle.FixedSingle };
            txtMemberSearch.PlaceholderText = "name, code or email...";
            txtMemberSearch.TextChanged += (s, e) => { _memberSearch = txtMemberSearch.Text.Trim(); _ = SafeReloadMembersAsync(); };

            lblMemberBranch = new Label { Text = "Branch", Location = new Point(560, 56), Size = new Size(60, 20), ForeColor = white };
            cboMemberBranch = new ComboBox { Location = new Point(620, 54), Size = new Size(180, 22), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = fieldBack, ForeColor = white };

            tabMembers.Controls.Add(lblMemberSummary);
            tabMembers.Controls.Add(lblMemberSearch);
            tabMembers.Controls.Add(txtMemberSearch);
            tabMembers.Controls.Add(lblMemberBranch);
            tabMembers.Controls.Add(cboMemberBranch);

            // ---- Attendance tab: search ----
            var lblAttendanceSearch = new Label { Text = "Search", Location = new Point(460, 24), Size = new Size(60, 20), ForeColor = white };
            txtAttendanceSearch = new TextBox { Location = new Point(520, 22), Size = new Size(200, 22), BackColor = fieldBack, ForeColor = white, BorderStyle = BorderStyle.FixedSingle };
            txtAttendanceSearch.PlaceholderText = "member name...";
            txtAttendanceSearch.TextChanged += (s, e) => { _attendanceSearch = txtAttendanceSearch.Text.Trim(); _ = SafeReloadAttendanceAsync(); };
            tabAttendance.Controls.Add(lblAttendanceSearch);
            tabAttendance.Controls.Add(txtAttendanceSearch);

            // ---- Payments tab: search + renew + outstanding summary ----
            // The payments grid moves down one row to make room for this strip.
            dgvPayments.Location = new Point(20, 250);
            dgvPayments.Size = new Size(820, 290);

            var lblPaymentSearch = new Label { Text = "Search", Location = new Point(160, 224), Size = new Size(60, 20), ForeColor = white };
            txtPaymentSearch = new TextBox { Location = new Point(220, 222), Size = new Size(200, 22), BackColor = fieldBack, ForeColor = white, BorderStyle = BorderStyle.FixedSingle };
            txtPaymentSearch.PlaceholderText = "member name or notes...";
            txtPaymentSearch.TextChanged += (s, e) => { _paymentSearch = txtPaymentSearch.Text.Trim(); _ = SafeReloadPaymentsAsync(); };

            btnPaymentRenew = new Button { Text = "Renew selected", Location = new Point(20, 220), Size = new Size(130, 26), BackColor = fieldBack, ForeColor = gold, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnPaymentRenew.Click += (s, e) => _ = RenewSelectedPaymentAsync();

            lblPaymentsSummary = new Label { Location = new Point(440, 224), Size = new Size(400, 20), ForeColor = gold, Font = new Font("Segoe UI", 9F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
            tabPayments.Controls.Add(lblPaymentSearch);
            tabPayments.Controls.Add(txtPaymentSearch);
            tabPayments.Controls.Add(btnPaymentRenew);
            tabPayments.Controls.Add(lblPaymentsSummary);

            // ---- Inquiries tab: search + stats (free strip right of the Export button) ----
            var lblInquirySearch = new Label { Text = "Search", Location = new Point(632, 254), Size = new Size(60, 20), ForeColor = white };
            txtInquirySearch = new TextBox { Location = new Point(692, 252), Size = new Size(148, 22), BackColor = fieldBack, ForeColor = white, BorderStyle = BorderStyle.FixedSingle };
            txtInquirySearch.PlaceholderText = "name or plan...";
            txtInquirySearch.TextChanged += (s, e) => { _inquirySearch = txtInquirySearch.Text.Trim(); _ = SafeReloadInquiriesAsync(); };

            lblInquiryStats = new Label { Location = new Point(470, 212), Size = new Size(370, 20), ForeColor = gold, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            tabInquiries.Controls.Add(lblInquirySearch);
            tabInquiries.Controls.Add(txtInquirySearch);
            tabInquiries.Controls.Add(lblInquiryStats);

            // ---- Feedback tab: search + stats (free strip right of the Export button) ----
            var lblFeedbackSearch = new Label { Text = "Search", Location = new Point(620, 194), Size = new Size(60, 20), ForeColor = white };
            txtFeedbackSearch = new TextBox { Location = new Point(680, 192), Size = new Size(160, 22), BackColor = fieldBack, ForeColor = white, BorderStyle = BorderStyle.FixedSingle };
            txtFeedbackSearch.PlaceholderText = "member or comments...";
            txtFeedbackSearch.TextChanged += (s, e) => { _feedbackSearch = txtFeedbackSearch.Text.Trim(); _ = SafeReloadFeedbackAsync(); };

            lblFeedbackStats = new Label { Location = new Point(620, 164), Size = new Size(220, 20), ForeColor = gold, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            tabFeedback.Controls.Add(lblFeedbackSearch);
            tabFeedback.Controls.Add(txtFeedbackSearch);
            tabFeedback.Controls.Add(lblFeedbackStats);

            // ---- Subscriptions tab: status stats + export ----
            lblSubscriptionStats = new Label { Location = new Point(500, 210), Size = new Size(340, 20), ForeColor = gold, Font = new Font("Segoe UI", 9F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
            btnExportSubscriptions = new Button { Text = "Export PDF", Location = new Point(500, 178), Size = new Size(110, 28), BackColor = fieldBack, ForeColor = white, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnExportSubscriptions.Click += btnExportSubscriptions_Click;
            tabSubscriptions.Controls.Add(lblSubscriptionStats);
            tabSubscriptions.Controls.Add(btnExportSubscriptions);

            // ---- Complaints tab: export + widen the grids so nothing is clipped ----
            btnExportComplaints = new Button { Text = "Export PDF", Location = new Point(660, 568), Size = new Size(150, 28), BackColor = fieldBack, ForeColor = white, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btnExportComplaints.Click += btnExportComplaints_Click;
            tabComplaints.Controls.Add(btnExportComplaints);

            dgvComplaints.Size = new Size(830, 400);
            dgvUsers.Size = new Size(830, 430);

            // The member-portal layout pass runs last so the Phase 6 controls exist.
            ApplyMemberPortalLayout();

            // Tenant A / Tenant B phase (non-branching tenants): the Members module carries
            // NO branch UI at all. The picker only ever populates for branch-aware tenants;
            // for Tenant A / Tenant B its label and combo are removed from the page right
            // after the portal pass (which would otherwise re-show them for staff roles).
            // Tenant C (branch-aware) keeps the picker exactly as before.
            if (!_isBranchAware)
            {
                lblMemberBranch.Visible = false;
                cboMemberBranch.Visible = false;
            }
        }

        // =====================================================================================
        // Friendly database errors (Step 15: never show raw SQL exceptions to normal users)
        // =====================================================================================

        private void ShowDbError(Exception ex)
        {
            // Staff with tenant-wide rights may see the technical detail; everyone else
            // gets a clean message plus the exception kind.
            var isPrivileged = _role is Roles.Admin or Roles.MasterAdmin;
            var detail = isPrivileged ? ex.Message : ex.GetType().Name;

            MessageBox.Show(
                "Something went wrong while contacting the database." + Environment.NewLine +
                "Please verify your input and try again." + Environment.NewLine + Environment.NewLine +
                "Details: " + detail,
                "Database error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // Quiet reload helpers for search-as-you-type (failures surface on the next
        // explicit refresh instead of popping a dialog per keystroke).
        private async Task SafeReloadMembersAsync() { try { await LoadMembersAsync(); } catch { /* ignored: transient while typing */ } }
        private async Task SafeReloadAttendanceAsync() { try { await LoadAttendanceAsync(); } catch { /* ignored */ } }
        private async Task SafeReloadPaymentsAsync() { try { await LoadPaymentsAsync(); } catch { /* ignored */ } }
        private async Task SafeReloadInquiriesAsync() { try { await LoadInquiriesAsync(); } catch { /* ignored */ } }
        private async Task SafeReloadFeedbackAsync() { try { await LoadFeedbackAsync(); } catch { /* ignored */ } }

        private static bool MatchesSearch(string? haystack, string needle) =>
            needle.Length == 0 || (haystack ?? string.Empty).Contains(needle, StringComparison.OrdinalIgnoreCase);

        // =====================================================================================
        // Member portal (Step 11): summary card + staff controls hidden for Member accounts
        // =====================================================================================

        /// <summary>
        /// Applies the member-portal layout: members see a personal summary card on the
        /// membership tab and no staff entry forms. Called from ApplyRoleVisibility.
        /// </summary>
        private void ApplyMemberPortalLayout()
        {
            // ApplyRoleVisibility runs before the Phase 6 controls are created during
            // construction; the pass below re-runs it once they exist.
            if (lblMemberSummary == null) return;

            var isMember = _role == Roles.Member;

            lblMemberSummary.Visible = isMember;

            // Staff-only entry forms are hidden for members (permissions already block the
            // actions; this only removes the confusing empty forms).
            foreach (Control c in new Control[] { txtMemberCode, txtMemberFullName, txtMemberEmail, chkMemberIsActive, dtpMemberCreatedAt, btnMemberAdd, btnMemberUpdate, btnMemberDelete, btnMemberClear, lblMemberCode, lblMemberFullName, lblMemberEmail, lblMemberCreatedAt, lblMemberBranch, cboMemberBranch, txtMemberSearch })
            {
                c.Visible = !isMember;
            }

            foreach (Control c in new Control[] { cboAttendanceMember, lblAttendanceMember, dtpAttendanceCheckIn, lblAttendanceCheckIn, btnAttendanceAdd, btnAttendanceUpdate, btnAttendanceDelete, btnAttendanceClear })
            {
                c.Visible = !isMember;
            }

            foreach (Control c in new Control[] { cboPaymentMember, lblPaymentMember, txtPaymentAmount, lblPaymentAmount, dtpPaymentDate, lblPaymentDate, chkPaymentHasDueDate, lblPaymentNextDue, dtpPaymentNextDue, lblPaymentNotes, txtPaymentNotes, btnPaymentAdd, btnPaymentUpdate, btnPaymentDelete, btnPaymentClear, btnPaymentRenew })
            {
                c.Visible = !isMember;
            }

            if (isMember)
            {
                btnNavMembers.Text = "My Membership"; // keep the Phase 4 wording consistent
            }
        }

        private async Task LoadMemberSummaryAsync(ITenantDataContext tenantDb)
        {
            if (!_memberId.HasValue)
            {
                lblMemberSummary.Text = "This account is not linked to a member record.";
                return;
            }

            var today = DateTime.Today;

            var member = await tenantDb.Members.AsNoTracking().FirstOrDefaultAsync(m => m.MemberId == _memberId.Value);
            if (member == null)
            {
                lblMemberSummary.Text = "Your member record could not be found. Please contact the front desk.";
                return;
            }

            var latestPayment = await tenantDb.Payments.AsNoTracking()
                .Where(p => p.MemberId == _memberId.Value)
                .OrderByDescending(p => p.PaymentDate)
                .FirstOrDefaultAsync();

            var subscription = await tenantDb.Subscriptions.AsNoTracking()
                .Where(s => s.MemberId == _memberId.Value && s.IsActive)
                .OrderByDescending(s => s.EndDate)
                .FirstOrDefaultAsync();

            var attendanceCount = await tenantDb.Attendances.AsNoTracking().CountAsync(a => a.MemberId == _memberId.Value);
            var lastCheckIn = await tenantDb.Attendances.AsNoTracking()
                .Where(a => a.MemberId == _memberId.Value)
                .OrderByDescending(a => a.CheckInDate)
                .Select(a => (DateTime?)a.CheckInDate)
                .FirstOrDefaultAsync();

            var openComplaints = db_IsBranchAware(tenantDb)
                ? await tenantDb.Complaints.AsNoTracking().CountAsync(c => c.MemberId == _memberId.Value && (c.Status == "Pending" || c.Status == "Under Review"))
                : 0;

            var feedbackCount = await tenantDb.Feedbacks.AsNoTracking().CountAsync(f => f.MemberId == _memberId.Value);

            string DescribeDue(DateTime? due)
            {
                if (!due.HasValue) return "no due date recorded";
                var days = (due.Value.Date - today).Days;
                return days < 0
                    ? $"{due.Value:MMM d, yyyy} (overdue by {-days} day{(-days == 1 ? "" : "s")})"
                    : days == 0 ? "due today" : $"{due.Value:MMM d, yyyy} (in {days} day{(days == 1 ? "" : "s")})";
            }

            string DescribeExpiry(Subscription? sub)
            {
                if (sub == null) return "no active subscription";
                var days = (sub.EndDate.Date - today).Days;
                var tail = days < 0 ? $"expired {-days} day{(-days == 1 ? "" : "s")} ago"
                    : days == 0 ? "expires today"
                    : $"expires in {days} day{(days == 1 ? "" : "s")}";
                return $"{sub.PlanName} - {tail}";
            }

            var branchText = tenantDb.IsBranchAware && member.BranchId.HasValue ? $" | Branch: {await ResolveBranchDisplayNameAsync(tenantDb, member.BranchId.Value)}" : string.Empty;

            lblMemberSummary.Text =
                $"Welcome, {member.FullName}   ({member.MemberCode})" + Environment.NewLine +
                $"Membership: {(member.IsActive ? "Active" : "Inactive")}{branchText}" + Environment.NewLine +
                $"Subscription: {DescribeExpiry(subscription)}" + Environment.NewLine +
                $"Next payment due: {DescribeDue(latestPayment?.NextDueDate)}" + Environment.NewLine +
                $"Attendance: {attendanceCount} visit{(attendanceCount == 1 ? "" : "s")}" +
                (lastCheckIn.HasValue ? $" | Last check-in: {lastCheckIn.Value:MMM d, yyyy}" : " | No check-ins yet") + Environment.NewLine +
                $"Open complaints: {openComplaints} | Feedback submitted: {feedbackCount}";
        }

        private static bool db_IsBranchAware(ITenantDataContext db) => db.IsBranchAware;

        private async Task<string> ResolveBranchDisplayNameAsync(ITenantDataContext db, int branchId)
        {
            var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.BranchId == branchId);
            return branch == null ? $"Branch {branchId}" : $"{branch.BranchName} ({branch.BranchCode})";
        }

        /// <summary>Populates the member branch picker (branch-aware sessions only).</summary>
        private async Task PopulateMemberBranchPickerAsync(ITenantDataContext db)
        {
            if (!db.IsBranchAware)
            {
                // Tenant A / Tenant B phase: non-branching tenants have no branch picker -
                // clear it, disable it AND remove it from the page (no branch selector can
                // ever appear for these sessions). Branch-aware tenants are untouched.
                cboMemberBranch.Items.Clear();
                cboMemberBranch.Enabled = false;
                lblMemberBranch.Visible = false;
                cboMemberBranch.Visible = false;
                return;
            }

            cboMemberBranch.Enabled = _branchId == null; // branch-scoped staff cannot reassign
            cboMemberBranch.Items.Clear();
            cboMemberBranch.Items.Add("(unassigned)");

            foreach (var branch in await db.Branches.AsNoTracking().OrderBy(b => b.BranchId).ToListAsync())
            {
                cboMemberBranch.Items.Add($"{branch.BranchId}: {branch.BranchCode}");
            }

            if (cboMemberBranch.SelectedIndex < 0 && cboMemberBranch.Items.Count > 0)
            {
                cboMemberBranch.SelectedIndex = 0;
            }
        }

        private static int? ParseBranchPicker(ComboBox combo)
        {
            var value = combo.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(value)) return null;
            var head = value.Split(':')[0].Trim();
            return int.TryParse(head, out var id) ? id : null;
        }

        private void SelectMemberBranchInPicker(int? branchId)
        {
            var index = cboMemberBranch.Items.IndexOf(branchId.HasValue ? $"{branchId.Value}: ?" : "(unassigned)");
            if (index >= 0)
            {
                cboMemberBranch.SelectedIndex = index;
                return;
            }

            // Match by leading id ("3: BR-B").
            for (var i = 0; i < cboMemberBranch.Items.Count; i++)
            {
                var item = cboMemberBranch.Items[i]?.ToString() ?? string.Empty;
                var head = item.Split(':')[0].Trim();
                if (branchId.HasValue && int.TryParse(head, out var id) && id == branchId.Value)
                {
                    cboMemberBranch.SelectedIndex = i;
                    return;
                }
            }

            if (cboMemberBranch.Items.Count > 0)
            {
                cboMemberBranch.SelectedIndex = 0;
            }
        }

        // =====================================================================================
        // Renewals (Step 5): prefill the payment form from the selected payment
        // =====================================================================================

        private async Task RenewSelectedPaymentAsync()
        {
            if (!Require(Permission.ManagePayments)) return;
            if (_selectedPaymentId == null)
            {
                MessageBox.Show("Select a payment record first.");
                return;
            }

            try
            {
                await using var tenantDb = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                var payment = await tenantDb.Payments.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PaymentId == _selectedPaymentId.Value);

                if (payment == null)
                {
                    MessageBox.Show("Payment record not found.");
                    return;
                }

                EnsureBranchAccess(payment.BranchId);

                // Guess the next cycle from the previous gap (monthly / quarterly / annual).
                var spanDays = payment.NextDueDate.HasValue
                    ? (payment.NextDueDate.Value.Date - payment.PaymentDate.Date).Days
                    : 30;
                var nextDue = payment.NextDueDate.HasValue
                    ? payment.NextDueDate.Value.AddDays(spanDays)
                    : DateTime.Today.AddMonths(1);

                cboPaymentMember.SelectedValue = payment.MemberId;
                txtPaymentAmount.Text = payment.AmountPaid.ToString("0.##");
                dtpPaymentDate.Value = DateTime.Now;
                chkPaymentHasDueDate.Checked = true;
                dtpPaymentNextDue.Value = nextDue;
                txtPaymentNotes.Text = $"Renewal of payment #{payment.PaymentId}";

                MessageBox.Show(
                    "Renewal prefilled from the selected payment." + Environment.NewLine +
                    $"Next due date: {nextDue:MMM d, yyyy}" + Environment.NewLine + Environment.NewLine +
                    "Review the details and click Add to record the renewal.",
                    "Renewal", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        // Dashboard additions (Step 12): new KPIs + branch comparison
        // =====================================================================================

        /// <summary>
        /// Shared at-risk computation (same rules as the Retention tab): distinct members
        /// flagged by inactivity, due/overdue payments, or expiring subscriptions.
        /// All inputs are already scope-filtered by the caller.
        /// </summary>
        private static async Task<int> ComputeAtRiskCountAsync(
            List<Member> members,
            List<Attendance> attendances,
            List<Payment> payments,
            List<Subscription> subscriptions,
            int inactivityDays,
            int dueSoonDays)
        {
            var today = DateTime.Today;
            var dueSoonCutoff = today.AddDays(dueSoonDays);
            var atRisk = new HashSet<int>();

            var lastCheckIn = attendances
                .GroupBy(a => a.MemberId)
                .ToDictionary(g => g.Key, g => g.Max(a => a.CheckInDate.Date));

            var latestPayment = payments
                .GroupBy(p => p.MemberId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.PaymentDate).First());

            foreach (var m in members.Where(x => x.IsActive))
            {
                if (lastCheckIn.TryGetValue(m.MemberId, out var last))
                {
                    if ((today - last).Days >= inactivityDays) atRisk.Add(m.MemberId);
                }
                else
                {
                    atRisk.Add(m.MemberId); // never checked in
                }

                if (latestPayment.TryGetValue(m.MemberId, out var payment) && payment.NextDueDate.HasValue)
                {
                    if (payment.NextDueDate.Value.Date <= dueSoonCutoff) atRisk.Add(m.MemberId);
                }

                if (subscriptions.Any(s => s.MemberId == m.MemberId && s.IsActive && s.EndDate.Date <= dueSoonCutoff))
                {
                    atRisk.Add(m.MemberId);
                }
            }

            await Task.CompletedTask;
            return atRisk.Count;
        }

        private async Task LoadBranchComparisonAsync(ITenantDataContext tenantDb)
        {
            // Admin/Manager only (ViewDashboard). Non-branch-aware tenants have no branches.
            if (!_isBranchAware || !HasPermission(Permission.ViewDashboard))
            {
                dgvBranchComparison.Visible = false;
                lblBranchComparisonHeader.Visible = false;
                return;
            }

            dgvBranchComparison.Visible = true;
            lblBranchComparisonHeader.Visible = true;

            var members = await tenantDb.Members.AsNoTracking().ToListAsync();
            var attendances = await tenantDb.Attendances.AsNoTracking().ToListAsync();
            var payments = await tenantDb.Payments.AsNoTracking().ToListAsync();
            var feedbacks = await tenantDb.Feedbacks.AsNoTracking().ToListAsync();
            var complaints = await tenantDb.Complaints.AsNoTracking().ToListAsync();
            var branches = await tenantDb.Branches.AsNoTracking().OrderBy(b => b.BranchId).ToListAsync();

            // Branch-scoped roles only ever see their own branch row; Admin sees all.
            var allowedBranchIds = _branchId.HasValue
                ? new[] { _branchId.Value }
                : branches.Select(b => b.BranchId).ToArray();

            var rows = new List<object>();
            foreach (var branch in branches.Where(b => allowedBranchIds.Contains(b.BranchId)))
            {
                var branchMembers = members.Where(m => m.BranchId == branch.BranchId).ToList();
                var memberIds = branchMembers.Select(m => m.MemberId).ToHashSet();
                var branchPayments = payments.Where(p => p.BranchId == branch.BranchId || (p.BranchId == null && memberIds.Contains(p.MemberId))).ToList();
                var branchFeedback = feedbacks.Where(f => f.BranchId == branch.BranchId || (f.BranchId == null && memberIds.Contains(f.MemberId))).ToList();

                rows.Add(new
                {
                    Branch = $"{branch.BranchName} ({branch.BranchCode})",
                    Members = branchMembers.Count,
                    Active = branchMembers.Count(m => m.IsActive),
                    Attendance = attendances.Count(a => a.BranchId == branch.BranchId || (a.BranchId == null && memberIds.Contains(a.MemberId))),
                    Payments = branchPayments.Sum(p => p.AmountPaid),
                    AvgRating = branchFeedback.Count > 0 ? Math.Round(branchFeedback.Average(f => (double)f.Rating), 2) : 0d,
                    OpenComplaints = complaints.Count(c => c.BranchId == branch.BranchId && c.Status != "Resolved" && c.Status != "Closed")
                });
            }

            dgvBranchComparison.DataSource = null;
            dgvBranchComparison.DataSource = rows;
        }

        // =====================================================================================
        // Phase 6 PDF exports (complaints + subscriptions), branch/member scoped
        // =====================================================================================

        private async void btnExportComplaints_Click(object sender, EventArgs e)
        {
            try
            {
                if (!RequireAny(Permission.ManageComplaints, Permission.SubmitComplaint)) return;
                await using var db = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);
                if (!db.IsBranchAware)
                {
                    MessageBox.Show("Complaints are available for branch-aware tenants only.");
                    return;
                }

                var records = await ApplyScope(db.Complaints.AsNoTracking().Include(c => c.Member), db, c => c.BranchId, c => c.MemberId)
                    .OrderByDescending(c => c.CreatedAt)
                    .ToListAsync();

                var rows = records.Select(c => new[]
                {
                    c.Member != null ? c.Member.FullName : "(unknown)",
                    c.Subject,
                    c.Status,
                    c.CreatedAt.ToString("yyyy-MM-dd"),
                    c.ResolvedAt.HasValue ? c.ResolvedAt.Value.ToString("yyyy-MM-dd") : "-",
                    c.ResolutionNotes ?? "-"
                }).ToList();

                var scope = _branchId.HasValue ? " | Branch: own branch" : " | Tenant-wide";
                GeneratePdfReport(
                    "Complaint Report",
                    $"AlphaElite_ComplaintReport_{DateTime.Now:yyyy-MM-dd}.pdf",
                    new[] { "Member", "Subject", "Status", "Created", "Resolved", "Resolution Notes" },
                    new[] { 0.16f, 0.2f, 0.1f, 0.11f, 0.11f, 0.32f },
                    rows,
                    $"Total Complaints: {records.Count}{scope}");
            }
            catch (Exception ex)
            {
                ShowDbError(ex);
            }
        }

        private async void btnExportSubscriptions_Click(object sender, EventArgs e)
        {
            try
            {
                if (!Require(Permission.ManageSubscriptions)) return;
                await using var db = await _dataContextFactory.CreateAsync(_companyId, _isBranchAware);

                // Phase 8: subscriptions are branch-scoped (Subscription.BranchId) so the export
                // respects the Current Branch just like the grid.
                var records = await ApplyScope(db.Subscriptions.AsNoTracking().Include(s => s.Member), db, s => s.BranchId, s => s.MemberId)
                    .OrderByDescending(s => s.StartDate)
                    .ToListAsync();

                var today = DateTime.Today;
                var rows = records.Select(s => new[]
                {
                    s.Member != null ? s.Member.FullName : "(unknown)",
                    s.PlanName,
                    s.Price.ToString("C2"),
                    s.BillingCycle,
                    s.StartDate.ToString("yyyy-MM-dd"),
                    s.EndDate.ToString("yyyy-MM-dd"),
                    DescribeSubscriptionStatus(s, today)
                }).ToList();

                GeneratePdfReport(
                    "Subscription Report",
                    $"AlphaElite_SubscriptionReport_{DateTime.Now:yyyy-MM-dd}.pdf",
                    new[] { "Member", "Plan", "Price", "Cycle", "Start", "End", "Status" },
                    new[] { 0.2f, 0.16f, 0.1f, 0.1f, 0.11f, 0.11f, 0.22f },
                    rows,
                    $"Total Subscriptions: {records.Count} | Active: {records.Count(s => s.IsActive && s.EndDate.Date >= today)}");
            }
            catch (Exception ex)
            {
                ShowDbError(ex);
            }
        }

        /// <summary>Report footer label describing the data scope of the export.</summary>
        private string ScopeLabel() => _branchId.HasValue ? " | Scope: own branch" : " | Scope: tenant-wide";

        /// <summary>Human-readable subscription status (shared by grid + export).</summary>
        private static string DescribeSubscriptionStatus(Subscription s, DateTime today)
        {
            if (!s.IsActive) return "Inactive";
            if (s.EndDate.Date < today) return "Expired";
            if (s.EndDate.Date == today) return "Expires today";
            if (s.EndDate.Date <= today.AddDays(7)) return $"Expiring in {(s.EndDate.Date - today).Days} day(s)";
            return "Active";
        }
    }
}
