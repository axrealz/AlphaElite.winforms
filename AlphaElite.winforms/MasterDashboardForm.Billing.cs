using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 9: Subscriptions &amp; Billing is now the real Alpha Elite SOFTWARE subscription
    /// / module management screen (master DB).
    ///
    ///   Top    : COMPANY SOFTWARE SUBSCRIPTIONS - every company, its plan, status, dates and
    ///            enabled module count; the master admin can change plan, edit dates, suspend
    ///            and reactivate.
    ///   Bottom : TENANT MEMBER SUBSCRIPTIONS - the pre-existing read-only view of a tenant's
    ///            GYM MEMBER memberships (Tenant C Subscriptions table). This is a completely
    ///            different concept and is deliberately kept separate and read-only.
    /// </summary>
    internal partial class MasterDashboardForm
    {
        private DataGridView dgvSoftwareSubs = null!;
        private Label lblSubDetailPlan = null!;
        private Label lblSubDetailStatus = null!;
        private Label lblSubDetailStart = null!;
        private Label lblSubDetailEnd = null!;
        private Label lblSubDetailModules = null!;
        private ListBox lstSubModules = null!;
        private ComboBox cboSubPlan = null!;
        private DateTimePicker dtpSubStart = null!;
        private DateTimePicker dtpSubEnd = null!;
        private Button btnSubSavePlan = null!;
        private Button btnSubSaveDates = null!;
        private Button btnSubSuspend = null!;
        private Button btnSubRefresh = null!;

        private ComboBox cboTenantSubs = null!;
        private DataGridView dgvTenantSubs = null!;

        private int? _selectedSubCompanyId;
        private IReadOnlyList<CompanySubscriptionInfo> _subscriptionRows = new List<CompanySubscriptionInfo>();

        private Panel BuildBillingPanel()
        {
            var panel = NewContentPanel();

            AddSectionTitle(panel, "Alpha Elite Software Subscriptions", 24, 20);
            AddNote(panel,
                "COMPANY SOFTWARE SUBSCRIPTIONS (master DB). This manages which Alpha Elite plan " +
                "each company subscribes to and which modules it unlocks. This is NOT billing and NOT " +
                "a gym member membership.",
                24, 52, 940, 32);

            // ---- All companies grid ----
            dgvSoftwareSubs = NewGrid();
            dgvSoftwareSubs.Location = new System.Drawing.Point(24, 92);
            dgvSoftwareSubs.Size = new System.Drawing.Size(980, 140);
            dgvSoftwareSubs.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (dgvSoftwareSubs.Rows[e.RowIndex].DataBoundItem is CompanySubscriptionInfo info)
                {
                    SelectCompany(info.CompanyId);
                }
            };
            panel.Controls.Add(dgvSoftwareSubs);

            // ---- Detail: left column ----
            var lblDetailTitle = new Label
            {
                Text = "Selected company",
                ForeColor = ColorMuted,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new System.Drawing.Point(24, 246),
                Size = new System.Drawing.Size(300, 20)
            };
            panel.Controls.Add(lblDetailTitle);

            lblSubDetailPlan = MakeDetailLabel(24, 272);
            lblSubDetailStatus = MakeDetailLabel(24, 296);
            lblSubDetailStart = MakeDetailLabel(24, 320);
            lblSubDetailEnd = MakeDetailLabel(24, 344);
            lblSubDetailModules = MakeDetailLabel(24, 368);

            lstSubModules = new ListBox
            {
                Location = new System.Drawing.Point(24, 392),
                Size = new System.Drawing.Size(460, 130),
                BackColor = ColorField,
                ForeColor = ColorText,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            panel.Controls.Add(lstSubModules);

            // ---- Detail: right column (admin actions) ----
            panel.Controls.Add(new Label
            {
                Text = "Change plan",
                ForeColor = ColorMuted,
                Location = new System.Drawing.Point(520, 246),
                Size = new System.Drawing.Size(120, 20)
            });

            cboSubPlan = NewCombo(520, 268, 200, SoftwarePlanCodes.Basic, SoftwarePlanCodes.Professional, SoftwarePlanCodes.Enterprise);
            panel.Controls.Add(cboSubPlan);

            btnSubSavePlan = NewPrimaryButton("Save Plan", 730, 266, 130);
            btnSubSavePlan.Click += async (s, e) => await SavePlanAsync();
            panel.Controls.Add(btnSubSavePlan);

            panel.Controls.Add(new Label
            {
                Text = "Edit dates",
                ForeColor = ColorMuted,
                Location = new System.Drawing.Point(520, 306),
                Size = new System.Drawing.Size(120, 20)
            });

            dtpSubStart = NewDatePicker(520, 328);
            dtpSubEnd = NewDatePicker(700, 328);
            panel.Controls.Add(dtpSubStart);
            panel.Controls.Add(dtpSubEnd);

            btnSubSaveDates = NewPrimaryButton("Save Dates", 520, 360, 130);
            btnSubSaveDates.Click += async (s, e) => await SaveDatesAsync();
            panel.Controls.Add(btnSubSaveDates);

            btnSubSuspend = NewSecondaryButton("Suspend", 660, 360, 130);
            btnSubSuspend.Click += async (s, e) => await SuspendOrReactivateAsync();
            panel.Controls.Add(btnSubSuspend);

            btnSubRefresh = NewSecondaryButton("Refresh", 800, 360, 110);
            btnSubRefresh.Click += async (s, e) => await ReloadAsync(_selectedSubCompanyId);
            panel.Controls.Add(btnSubRefresh);

            AddNote(panel,
                "Suspended is an explicit administrative state and takes precedence over dates. " +
                "Expiring or suspending never deletes any subscription record or tenant data.",
                520, 400, 470, 40);

            // ---- TENANT MEMBER SUBSCRIPTIONS (read-only, separate concept) ----
            var lblMemberTitle = new Label
            {
                Text = "TENANT MEMBER SUBSCRIPTIONS (read-only - gym member memberships, tenant databases)",
                ForeColor = ColorGold,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new System.Drawing.Point(24, 540),
                Size = new System.Drawing.Size(940, 20)
            };
            panel.Controls.Add(lblMemberTitle);

            cboTenantSubs = NewCombo(24, 564, 340);
            foreach (var tenant in _tenants)
            {
                cboTenantSubs.Items.Add(tenant);
            }
            if (cboTenantSubs.Items.Count > 0)
            {
                cboTenantSubs.SelectedIndex = 0;
            }

            dgvTenantSubs = NewGrid();
            dgvTenantSubs.Location = new System.Drawing.Point(24, 596);
            dgvTenantSubs.Size = new System.Drawing.Size(980, 220);

            cboTenantSubs.SelectedIndexChanged += async (s, e) =>
            {
                try
                {
                    await LoadTenantMemberSubscriptionsAsync(cboTenantSubs, dgvTenantSubs);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not load member subscriptions: " + ex.Message,
                        "Tenant Member Subscriptions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            panel.Controls.Add(cboTenantSubs);
            panel.Controls.Add(dgvTenantSubs);

            panel.Tag = (Func<Task>)(async () => await ReloadAsync(null));

            return panel;
        }

        private static Label MakeDetailLabel(int x, int y) => new()
        {
            ForeColor = ColorText,
            AutoSize = false,
            Location = new System.Drawing.Point(x, y),
            Size = new System.Drawing.Size(460, 20)
        };

        private static DateTimePicker NewDatePicker(int x, int y) => new()
        {
            Format = DateTimePickerFormat.Short,
            Location = new System.Drawing.Point(x, y),
            Size = new System.Drawing.Size(170, 24)
        };

        // =====================================================================================
        // COMPANY SOFTWARE SUBSCRIPTIONS
        // =====================================================================================

        private async Task ReloadAsync(int? selectCompanyId)
        {
            try
            {
                _subscriptionRows = await _runtime.SubscriptionService.GetAllCompanySubscriptionsAsync();

                dgvSoftwareSubs.DataSource = _subscriptionRows
                    .Select(r => new
                    {
                        r.CompanyId,
                        r.CompanyName,
                        r.CompanyCode,
                        r.PlanName,
                        Status = r.EffectiveStatus,
                        StartDate = r.StartDate,
                        EndDate = r.EndDate,
                        Modules = r.EnabledModuleCount
                    })
                    .ToList();

                var target = selectCompanyId
                             ?? _selectedSubCompanyId
                             ?? _subscriptionRows.FirstOrDefault()?.CompanyId;

                if (target.HasValue)
                {
                    SelectCompany(target.Value);
                }
            }
            catch (Exception ex)
            {
                dgvSoftwareSubs.DataSource = null;
                MasterDbErrorReporter.ShowDialog(this, "Could not load software subscriptions.", ex);
            }
        }

        private void SelectCompany(int companyId)
        {
            var info = _subscriptionRows.FirstOrDefault(r => r.CompanyId == companyId);
            if (info == null)
            {
                return;
            }

            _selectedSubCompanyId = companyId;

            lblSubDetailPlan.Text = "Plan: " + info.PlanName;
            lblSubDetailStatus.Text = "Status: " + info.EffectiveStatus +
                (string.Equals(info.Status, info.EffectiveStatus, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : $" (stored: {info.Status})");
            lblSubDetailStart.Text = "Start date: " + info.StartDate.ToString("MMM d, yyyy");
            lblSubDetailEnd.Text = "End date: " + info.EndDate.ToString("MMM d, yyyy");
            lblSubDetailModules.Text = $"Modules: {info.EnabledModuleCount} enabled";

            lstSubModules.BeginUpdate();
            lstSubModules.Items.Clear();
            for (var i = 0; i < info.EnabledModuleCodes.Count; i++)
            {
                lstSubModules.Items.Add($"{i + 1}. {info.EnabledModuleCodes[i]}");
            }
            lstSubModules.EndUpdate();

            if (cboSubPlan.Items.Contains(info.PlanCode))
            {
                cboSubPlan.SelectedItem = info.PlanCode;
            }

            dtpSubStart.Value = info.StartDate.Date;
            dtpSubEnd.Value = info.EndDate.Date;

            var storedStatus = info.Status;
            btnSubSuspend.Text = string.Equals(storedStatus, SubscriptionStatuses.Suspended, StringComparison.OrdinalIgnoreCase)
                ? "Reactivate"
                : "Suspend";
        }

        private async Task SavePlanAsync()
        {
            if (!_selectedSubCompanyId.HasValue)
            {
                MessageBox.Show("Select a company first.", "Software Subscriptions",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var planCode = cboSubPlan.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(planCode))
            {
                return;
            }

            var result = await _runtime.SubscriptionService.ChangePlanAsync(_selectedSubCompanyId.Value, planCode);
            if (!result.Success)
            {
                MessageBox.Show("Could not change the plan: " + result.Error, "Software Subscriptions",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show($"Plan changed to {planCode}.", "Software Subscriptions",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            await ReloadAsync(_selectedSubCompanyId);
        }

        private async Task SaveDatesAsync()
        {
            if (!_selectedSubCompanyId.HasValue)
            {
                MessageBox.Show("Select a company first.", "Software Subscriptions",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = await _runtime.SubscriptionService.UpdateDatesAsync(
                _selectedSubCompanyId.Value, dtpSubStart.Value.Date, dtpSubEnd.Value.Date);

            if (!result.Success)
            {
                MessageBox.Show("Could not save the dates: " + result.Error, "Software Subscriptions",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show("Subscription dates saved.", "Software Subscriptions",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            await ReloadAsync(_selectedSubCompanyId);
        }

        private async Task SuspendOrReactivateAsync()
        {
            if (!_selectedSubCompanyId.HasValue)
            {
                MessageBox.Show("Select a company first.", "Software Subscriptions",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var suspending = btnSubSuspend.Text == "Suspend";

            var result = suspending
                ? await _runtime.SubscriptionService.SuspendAsync(_selectedSubCompanyId.Value)
                : await _runtime.SubscriptionService.ReactivateAsync(_selectedSubCompanyId.Value);

            if (!result.Success)
            {
                MessageBox.Show("Could not update the subscription: " + result.Error, "Software Subscriptions",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(suspending
                    ? "Subscription suspended. The company's plan-gated modules are now unavailable; no data was deleted."
                    : "Subscription reactivated. Date rules decide Active vs Expired from here.",
                "Software Subscriptions", MessageBoxButtons.OK, MessageBoxIcon.Information);

            await ReloadAsync(_selectedSubCompanyId);
        }

        // =====================================================================================
        // TENANT MEMBER SUBSCRIPTIONS (read-only; the pre-Phase 9 view, unchanged semantics)
        // =====================================================================================

        private async Task LoadTenantMemberSubscriptionsAsync(ComboBox cbo, DataGridView dgv)
        {
            if (cbo.SelectedItem is not TenantOption tenant)
            {
                return;
            }

            try
            {
                await using var db = await _runtime.DataContextFactory.CreateAsync(tenant.CompanyId, tenant.IsBranchAware);

                var rows = await db.Subscriptions
                    .AsNoTracking()
                    .OrderByDescending(s => s.StartDate)
                    .Select(s => new
                    {
                        s.SubscriptionId,
                        Member = s.Member != null ? s.Member.FullName : ("Member " + s.MemberId),
                        s.PlanName,
                        s.Price,
                        s.BillingCycle,
                        s.StartDate,
                        s.EndDate,
                        Status = s.IsActive ? "Active" : "Inactive"
                    })
                    .ToListAsync();

                dgv.DataSource = rows;
            }
            catch (Exception ex)
            {
                dgv.DataSource = null;
                MessageBox.Show(
                    "Could not read member subscriptions for this tenant: " + TrimError(ex.Message),
                    "Tenant Member Subscriptions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
