using AlphaElite.infrastructure.services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 5 (tenant side): the company's own SOFTWARE subscription screen.
    ///
    /// OPTION 2 is enforced by the surface itself: the tenant can READ its current plan,
    /// status, dates, renewal date and the plan/price/terms SNAPSHOT it signed up for, it
    /// can COMPARE the available plans module by module, it can SUBMIT an upgrade/downgrade
    /// request, and it can CANCEL its own pending request. There is deliberately no control
    /// (and no code path) that activates a plan, edits a price/module/terms, approves or
    /// rejects a request - those live only in the Super Admin window in the master DB.
    ///
    /// The screen is offered to the company Admin only. Every other role keeps exactly the
    /// navigation it had before this phase (the button is not added to the sidebar at all).
    /// </summary>
    public partial class Form1
    {
        private ISubscriptionService? _softwareSubscriptions;

        private Button? btnNavSoftwareSubscription;
        private TabPage? tabSoftwareSubscription;
        private Label? lblTenantSubscriptionSummary;
        private Label? lblTenantModuleComparison;
        private DataGridView? dgvTenantPlanChoices;
        private DataGridView? dgvTenantRequests;
        private TextBox? txtTenantRequestNotes;
        private Button? btnTenantSubmitRequest;
        private Button? btnTenantCancelRequest;
        private Button? btnTenantRefreshSubscription;

        /// <summary>Active plans the company does NOT currently have (the selectable request targets).</summary>
        private readonly List<SubscriptionPlanInfo> _tenantPlanChoices = new();

        /// <summary>
        /// The module set of the company's CURRENT plan (not the granted set), so the
        /// comparison shows the real plan difference even while the subscription is
        /// Expired/Suspended and grants no modules at all.
        /// </summary>
        private IReadOnlyList<string> _tenantCurrentPlanModules = Array.Empty<string>();

        /// <summary>This company's own requests, newest first.</summary>
        private List<SubscriptionChangeRequestInfo> _tenantOwnRequests = new();

        // Headless-recorded state (Control.Visible is always false while the form is unshown).
        private bool _softwareSubscriptionNavHiddenForTest = true;
        private string _tenantSubscriptionSummaryForTest = string.Empty;
        private string _tenantModuleComparisonForTest = string.Empty;

        private sealed class TenantPlanRow
        {
            public string Plan { get; set; } = string.Empty;
            public string Code { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public string Cycle { get; set; } = string.Empty;
            public int Modules { get; set; }
            public string Change { get; set; } = string.Empty;
        }

        private sealed class TenantRequestRow
        {
            public int RequestId { get; set; }
            public string Requested { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string RequestedBy { get; set; } = string.Empty;
            public string RequestedAt { get; set; } = string.Empty;
            public string Reviewed { get; set; } = string.Empty;
        }

        /// <summary>
        /// Builds the sidebar entry and the screen. Called from the constructor AFTER the other
        /// phase partials and BEFORE the subscription/role visibility passes, so the entry is
        /// removed again for every role the screen is not meant for.
        /// </summary>
        private void InitializePhase5Controls(ISubscriptionService? subscriptionService)
        {
            _softwareSubscriptions = subscriptionService;

            btnNavSoftwareSubscription = CreateNavButton("My Subscription", 100 + 44 * 11);
            btnNavSoftwareSubscription.Click += async (s, e) =>
            {
                await LoadSoftwareSubscriptionAsync();
                if (tabSoftwareSubscription != null)
                {
                    tabControlMain.SelectedTab = tabSoftwareSubscription;
                }

                SetActiveNavButton(btnNavSoftwareSubscription!);
                btnNavSoftwareSubscription!.BackColor = Color.FromArgb(38, 38, 38);
                btnNavSoftwareSubscription!.ForeColor = Color.FromArgb(212, 175, 55);
            };
            pnlSidebar.Controls.Add(btnNavSoftwareSubscription);

            tabSoftwareSubscription = BuildSoftwareSubscriptionTab();
            tabControlMain.TabPages.Add(tabSoftwareSubscription);
        }

        /// <summary>
        /// Phase 6 composition: the entry is removed unless the session is the company Admin
        /// AND a master-DB subscription service is available. It is registered with the
        /// recorded-nav diagnostics (so the sidebar reflow treats it like any other entry) but
        /// NOT with the role-visibility label list, which stays owned by ApplyRoleVisibility.
        /// </summary>
        private void ApplySoftwareSubscriptionVisibility()
        {
            var allowed = _softwareSubscriptions != null && _role == Roles.Admin;

            if (btnNavSoftwareSubscription == null)
            {
                _softwareSubscriptionNavHiddenForTest = true;
                return;
            }

            btnNavSoftwareSubscription.Visible = allowed;
            _softwareSubscriptionNavHiddenForTest = !allowed;

            if (allowed)
            {
                _visibleNavButtons.Add(btnNavSoftwareSubscription);
            }
            else
            {
                _visibleNavButtons.Remove(btnNavSoftwareSubscription);
            }
        }

        private TabPage BuildSoftwareSubscriptionTab()
        {
            var page = new TabPage("My Subscription")
            {
                BackColor = Color.FromArgb(24, 24, 24),
                Padding = new Padding(12)
            };

            lblTenantSubscriptionSummary = new Label
            {
                AutoSize = false,
                Width = 900,
                Height = 96,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5F),
                Text = "Loading your company subscription..."
            };

            var plansHeader = new Label
            {
                AutoSize = false,
                Width = 900,
                Height = 22,
                ForeColor = Color.FromArgb(212, 175, 55),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Text = "Available plans (select a row to see the module difference, then submit a request)"
            };

            dgvTenantPlanChoices = new DataGridView
            {
                Width = 900,
                Height = 150,
                AllowUserToAddRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false
            };
            dgvTenantPlanChoices.SelectionChanged += (s, e) => RenderPlanComparison();

            lblTenantModuleComparison = new Label
            {
                AutoSize = false,
                Width = 900,
                Height = 70,
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Segoe UI", 9F),
                Text = "Select a plan to compare its modules with your current plan."
            };

            var notesLabel = new Label
            {
                AutoSize = false,
                Width = 900,
                Height = 20,
                ForeColor = Color.White,
                Text = "Note for the Super Admin (optional)"
            };

            txtTenantRequestNotes = new TextBox
            {
                Width = 900,
                BackColor = Color.FromArgb(38, 38, 38),
                ForeColor = Color.White
            };

            btnTenantSubmitRequest = new Button
            {
                Text = "Submit upgrade / downgrade request",
                Width = 260,
                Height = 30,
                BackColor = Color.FromArgb(38, 38, 38),
                ForeColor = Color.FromArgb(212, 175, 55),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnTenantSubmitRequest.Click += async (s, e) => await SubmitSelectedPlanRequestAsync();

            btnTenantCancelRequest = new Button
            {
                Text = "Cancel my pending request",
                Width = 200,
                Height = 30,
                BackColor = Color.FromArgb(38, 38, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnTenantCancelRequest.Click += async (s, e) => await CancelSelectedRequestAsync();

            btnTenantRefreshSubscription = new Button
            {
                Text = "Refresh",
                Width = 100,
                Height = 30,
                BackColor = Color.FromArgb(38, 38, 38),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnTenantRefreshSubscription.Click += async (s, e) => await LoadSoftwareSubscriptionAsync();

            var requestsHeader = new Label
            {
                AutoSize = false,
                Width = 900,
                Height = 22,
                ForeColor = Color.FromArgb(212, 175, 55),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Text = "My requests (a request changes nothing until Alpha Elite approves it)"
            };

            dgvTenantRequests = new DataGridView
            {
                Width = 900,
                Height = 140,
                AllowUserToAddRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false
            };

            var confirmRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true
            };
            confirmRow.Controls.Add(btnTenantSubmitRequest);
            confirmRow.Controls.Add(btnTenantCancelRequest);
            confirmRow.Controls.Add(btnTenantRefreshSubscription);

            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.FromArgb(24, 24, 24)
            };

            layout.Controls.Add(lblTenantSubscriptionSummary);
            layout.Controls.Add(plansHeader);
            layout.Controls.Add(dgvTenantPlanChoices);
            layout.Controls.Add(lblTenantModuleComparison);
            layout.Controls.Add(notesLabel);
            layout.Controls.Add(txtTenantRequestNotes);
            layout.Controls.Add(confirmRow);
            layout.Controls.Add(requestsHeader);
            layout.Controls.Add(dgvTenantRequests);

            page.Controls.Add(layout);
            return page;
        }

        // =====================================================================================
        // Master-context serialization.
        //
        // The tenant window reads the master database ONLY through the shared subscription
        // service, and that service owns ONE MasterAlphaEliteDbContext instance (composed once
        // by AppRuntime). An EF Core context forbids overlapping operations, so a second user
        // action that starts while another is still running (e.g. clicking the navigation item
        // twice, or submitting while a load is in flight) used to throw "A second operation was
        // started on this context instance". Every action below is therefore funnelled through
        // this gate: it runs to completion, or it waits for the action in flight.
        //
        // The gate is acquired EXACTLY ONCE per user action and never nested: the ungated core
        // methods (LoadSoftwareSubscriptionCoreAsync) are only ever called from a gated entry
        // point. Loads are superseded: a newer load invalidates an older queued one.
        // =====================================================================================
        private readonly SemaphoreSlim _softwareSubscriptionGate = new(1, 1);
        private int _softwareSubscriptionLoadGeneration;

        /// <summary>Runs one tenant-side master-DB action, serialized against every other one.</summary>
        private async Task<T> RunSubscriptionOperationAsync<T>(Func<Task<T>> operation)
        {
            if (!await _softwareSubscriptionGate.WaitAsync(TimeSpan.FromSeconds(120)))
            {
                throw new TimeoutException("A previous subscription screen action never finished.");
            }

            try
            {
                return await operation();
            }
            finally
            {
                _softwareSubscriptionGate.Release();
            }
        }

        /// <summary>
        /// Reads the company's own subscription, the plan catalogue and its own requests from
        /// the master DB through the subscription service (the tenant window never opens the
        /// master database itself). Serialized: overlapping triggers supersede each other
        /// instead of racing on the shared context.
        /// </summary>
        internal async Task LoadSoftwareSubscriptionAsync()
        {
            if (_softwareSubscriptions == null)
            {
                return;
            }

            var generation = Interlocked.Increment(ref _softwareSubscriptionLoadGeneration);

            if (!await _softwareSubscriptionGate.WaitAsync(TimeSpan.FromSeconds(120)))
            {
                return; // The previous load never finished: never stack another one on the context.
            }

            try
            {
                // A newer load was requested while this one was queued: it will bind everything.
                if (generation != Volatile.Read(ref _softwareSubscriptionLoadGeneration))
                {
                    return;
                }

                await LoadSoftwareSubscriptionCoreAsync();
            }
            finally
            {
                _softwareSubscriptionGate.Release();
            }
        }

        /// <summary>Ungated load body; only ever called while the gate is held.</summary>
        private async Task LoadSoftwareSubscriptionCoreAsync()
        {
            var info = await _softwareSubscriptions!.GetCompanySubscriptionAsync(_companyId);
            var plans = await _softwareSubscriptions.GetPlansAsync();
            var requests = await _softwareSubscriptions.GetChangeRequestsAsync();

            _tenantPlanChoices.Clear();
            foreach (var plan in plans
                         .Where(p => p.IsActive && p.SubscriptionPlanId != (info?.SubscriptionPlanId ?? -1))
                         .OrderBy(p => p.ModuleCodes.Count)
                         .ThenBy(p => p.Code, StringComparer.Ordinal))
            {
                _tenantPlanChoices.Add(plan);
            }

            _tenantOwnRequests = requests
                .Where(r => r.CompanyId == _companyId)
                .OrderByDescending(r => r.SubscriptionChangeRequestId)
                .ToList();

            RenderSubscriptionSummary(info, plans);
            RenderPlanChoices(info, plans);
            RenderOwnRequests();
            RenderPlanComparison();
        }

        private void RenderSubscriptionSummary(CompanySubscriptionInfo? info, IReadOnlyList<SubscriptionPlanInfo> plans)
        {
            if (lblTenantSubscriptionSummary == null)
            {
                return;
            }

            if (info == null)
            {
                lblTenantSubscriptionSummary.Text =
                    "Your company has no software subscription yet. Submit a request below and Alpha Elite will set one up.";
                _tenantSubscriptionSummaryForTest = "no subscription";
                return;
            }

            var pendingRequest = _tenantOwnRequests.FirstOrDefault(r => r.Status == SubscriptionChangeRequestStatuses.Pending);

            var terms = string.IsNullOrWhiteSpace(info.TermsAndAgreement)
                ? "(no terms accepted yet)"
                : info.TermsAndAgreement.Trim();

            var text =
                $"Current plan: {info.PlanName} ({info.PlanCode})    Status: {info.EffectiveStatus}" +
                Environment.NewLine +
                $"Period: {info.StartDate:MMM d, yyyy} - {info.EndDate:MMM d, yyyy}" +
                $"    Renewal: {(info.RenewalDate.HasValue ? info.RenewalDate.Value.ToString("MMM d, yyyy") : "-")}" +
                $"    Payment: {(string.IsNullOrWhiteSpace(info.PaymentStatus) ? "-" : info.PaymentStatus)}" +
                Environment.NewLine +
                $"Agreed price: {info.Price:0.00} ({info.BillingCycle})    Modules: {info.EnabledModuleCodes.Count}" +
                Environment.NewLine +
                $"Terms: {terms}" +
                (pendingRequest == null
                    ? string.Empty
                    : Environment.NewLine + $"Pending request: {pendingRequest.RequestedPlanCode} ({pendingRequest.RequestType}), submitted {pendingRequest.RequestedAt:MMM d, yyyy}.");

            lblTenantSubscriptionSummary.Text = text;
            _tenantSubscriptionSummaryForTest = text.Replace(Environment.NewLine, " | ");
        }

        private void RenderPlanChoices(CompanySubscriptionInfo? info, IReadOnlyList<SubscriptionPlanInfo> plans)
        {
            if (dgvTenantPlanChoices == null)
            {
                return;
            }

            _tenantCurrentPlanModules = info == null
                ? Array.Empty<string>()
                : (plans.FirstOrDefault(p => p.SubscriptionPlanId == info.SubscriptionPlanId)?.ModuleCodes ?? Array.Empty<string>());
            var currentModules = _tenantCurrentPlanModules;

            dgvTenantPlanChoices.DataSource = _tenantPlanChoices
                .Select(p => new TenantPlanRow
                {
                    Plan = p.Name,
                    Code = p.Code,
                    Price = p.Price,
                    Cycle = p.BillingCycle,
                    Modules = p.ModuleCodes.Count,
                    Change = PlanChangeClassifier.Classify(currentModules, p.ModuleCodes)
                })
                .ToList();

            if (dgvTenantPlanChoices.Rows.Count > 0)
            {
                dgvTenantPlanChoices.Rows[0].Selected = true;
            }
        }

        private void RenderOwnRequests()
        {
            if (dgvTenantRequests == null)
            {
                return;
            }

            dgvTenantRequests.DataSource = _tenantOwnRequests
                .Select(r => new TenantRequestRow
                {
                    RequestId = r.SubscriptionChangeRequestId,
                    Requested = r.RequestedPlanCode,
                    Type = r.RequestType,
                    Status = r.Status,
                    RequestedBy = r.RequestedBy,
                    RequestedAt = r.RequestedAt.ToLocalTime().ToString("MMM d, yyyy HH:mm"),
                    Reviewed = r.ReviewedBy == null
                        ? string.Empty
                        : $"{r.ReviewedBy} @ {r.ReviewedAt:MMM d, yyyy}"
                })
                .ToList();
        }

        /// <summary>Shows exactly which modules the selected plan would add and remove.</summary>
        private void RenderPlanComparison()
        {
            if (lblTenantModuleComparison == null)
            {
                return;
            }

            var selected = SelectedPlanChoice();
            if (selected == null)
            {
                _tenantModuleComparisonForTest = "(no plan selected)";
                lblTenantModuleComparison.Text = _tenantModuleComparisonForTest;
                return;
            }

            var current = _tenantCurrentPlanModules;
            var added = selected.ModuleCodes.Where(c => !current.Contains(c, StringComparer.Ordinal)).ToList();
            var removed = current.Where(c => !selected.ModuleCodes.Contains(c, StringComparer.Ordinal)).ToList();

            var line =
                $"{selected.Code} would ADD: {(added.Count == 0 ? "-" : string.Join(", ", added))}" +
                Environment.NewLine +
                $"{selected.Code} would REMOVE: {(removed.Count == 0 ? "-" : string.Join(", ", removed))}" +
                $"   (change type: {PlanChangeClassifier.Classify(current, selected.ModuleCodes)})";

            _tenantModuleComparisonForTest = line.Replace(Environment.NewLine, " | ");
            lblTenantModuleComparison.Text = line;
        }

        private SubscriptionPlanInfo? SelectedPlanChoice()
        {
            if (dgvTenantPlanChoices == null || dgvTenantPlanChoices.CurrentRow == null)
            {
                return null;
            }

            if (dgvTenantPlanChoices.CurrentRow.DataBoundItem is not TenantPlanRow row)
            {
                return null;
            }

            return _tenantPlanChoices.FirstOrDefault(p => p.Code == row.Code);
        }

        private async Task SubmitSelectedPlanRequestAsync()
        {
            var selected = SelectedPlanChoice();
            if (selected == null)
            {
                ShowSubscriptionMessage("Select a plan to request.", MessageBoxIcon.Information);
                return;
            }

            var notes = txtTenantRequestNotes?.Text ?? string.Empty;
            await SubmitPlanChangeRequestAsync(selected, notes);
            txtTenantRequestNotes?.Clear();
        }

        /// <summary>
        /// The ONE tenant-side write path: it creates a Pending SubscriptionChangeRequest and
        /// nothing else. The current CompanySubscription, its price/terms snapshot and the
        /// history table are untouched until a Super Admin approves it. Serialized against the
        /// load path; the confirmation dialog is shown only after the gate is released.
        /// </summary>
        private async Task<SubscriptionMutationResult> SubmitPlanChangeRequestAsync(SubscriptionPlanInfo plan, string notes)
        {
            if (_softwareSubscriptions == null)
            {
                return SubscriptionMutationResult.Fail("The subscription service is not available in this session.");
            }

            var result = await RunSubscriptionOperationAsync(async () =>
            {
                var r = await _softwareSubscriptions.SubmitChangeRequestAsync(
                    _companyId, plan.Code, _session.Username, notes);
                if (r.Success)
                {
                    await LoadSoftwareSubscriptionCoreAsync();
                }

                return r;
            });

            if (result.Success)
            {
                ShowSubscriptionMessage(
                    $"Request for {plan.Code} submitted. It is Pending until Alpha Elite approves it - your current plan keeps working meanwhile.",
                    MessageBoxIcon.Information);
            }
            else
            {
                ShowSubscriptionMessage(result.Error, MessageBoxIcon.Warning);
            }

            return result;
        }

        private async Task CancelSelectedRequestAsync()
        {
            if (dgvTenantRequests?.CurrentRow?.DataBoundItem is not TenantRequestRow row)
            {
                ShowSubscriptionMessage("Select one of your own requests to cancel.", MessageBoxIcon.Information);
                return;
            }

            await CancelRequestAsync(row.RequestId);
        }

        /// <summary>Cancels one of the company's own requests (requester identity checked by the service). Serialized against the load path.</summary>
        private async Task<SubscriptionMutationResult> CancelRequestAsync(int requestId)
        {
            if (_softwareSubscriptions == null)
            {
                return SubscriptionMutationResult.Fail("The subscription service is not available in this session.");
            }

            var result = await RunSubscriptionOperationAsync(async () =>
            {
                var r = await _softwareSubscriptions.CancelChangeRequestAsync(requestId, _session.Username);
                if (r.Success)
                {
                    await LoadSoftwareSubscriptionCoreAsync();
                }

                return r;
            });

            if (!result.Success)
            {
                ShowSubscriptionMessage(result.Error, MessageBoxIcon.Warning);
            }

            return result;
        }

        private void ShowSubscriptionMessage(string message, MessageBoxIcon icon)
        {
            if (!SuppressDialogs)
            {
                MessageBox.Show(message, "My Subscription", MessageBoxButtons.OK, icon);
            }
        }

        // -------------------------------------------------------------------------------
        // Headless verification hooks (Phase 5 / Phase 7 harness).
        // -------------------------------------------------------------------------------

        /// <summary>True when this session is offered the company subscription screen at all.</summary>
        internal bool SoftwareSubscriptionScreenAvailableForTest =>
            _softwareSubscriptions != null && _role == Roles.Admin;

        /// <summary>True when the sidebar entry was removed for this session.</summary>
        internal bool SoftwareSubscriptionNavHiddenForTest => _softwareSubscriptionNavHiddenForTest;

        /// <summary>The rendered current-plan summary (one line).</summary>
        internal string TenantSubscriptionSummaryForTest => _tenantSubscriptionSummaryForTest;

        /// <summary>The rendered module difference for the selected plan (one line).</summary>
        internal string TenantModuleComparisonForTest => _tenantModuleComparisonForTest;

        /// <summary>The plans this company can currently request.</summary>
        internal IReadOnlyList<SubscriptionPlanInfo> AvailablePlanChoicesForTest => _tenantPlanChoices;

        /// <summary>The company's own requests as rendered on the screen.</summary>
        internal IReadOnlyList<SubscriptionChangeRequestInfo> OwnRequestsForTest => _tenantOwnRequests;

        /// <summary>Drives the exact path behind the Submit button for the given plan code.</summary>
        internal Task<SubscriptionMutationResult> SubmitSubscriptionRequestForTestAsync(string planCode, string notes)
        {
            var plan = _tenantPlanChoices.FirstOrDefault(p => p.Code == planCode);
            return plan == null
                ? Task.FromResult(SubscriptionMutationResult.Fail($"Plan '{planCode}' is not offered on this screen."))
                : SubmitPlanChangeRequestAsync(plan, notes);
        }

        /// <summary>Drives the exact path behind the Cancel button for one of the company's requests.</summary>
        internal Task<SubscriptionMutationResult> CancelSubscriptionRequestForTestAsync(int requestId) =>
            CancelRequestAsync(requestId);

        /// <summary>Selects a plan row so the module comparison reflects it (headless selection).</summary>
        internal void SelectPlanChoiceForTest(string planCode)
        {
            if (dgvTenantPlanChoices == null)
            {
                return;
            }

            for (var i = 0; i < dgvTenantPlanChoices.Rows.Count; i++)
            {
                if (dgvTenantPlanChoices.Rows[i].DataBoundItem is TenantPlanRow row && row.Code == planCode)
                {
                    dgvTenantPlanChoices.CurrentCell = dgvTenantPlanChoices.Rows[i].Cells[0];
                    RenderPlanComparison();
                    return;
                }
            }
        }
    }
}
