using AlphaElite.domain.entities;
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
    /// Subscription-request phase: the Super Admin "Subscription" screen (master DB).
    ///
    ///   Companies tab : every company's current software subscription (plan, status, dates,
    ///                   price, payment status, renewal) with a detail view.
    ///   Requests tab  : OPTION 2 review queue - pending subscription change requests with
    ///                   Approve / Reject (reject requires review notes).
    ///   Plans tab     : plan management (create, edit, activate/deactivate, terms) and the
    ///                   plan &lt;-&gt; module assignment editor.
    ///   History tab   : the subscription audit trail.
    /// </summary>
    internal partial class MasterDashboardForm
    {
        private TabControl tabSubscription = null!;
        private ComboBox cboSubCompany = null!;
        private Label lblCompanyPlan = null!;
        private Label lblCompanyStatus = null!;
        private Label lblCompanyDates = null!;
        private Label lblCompanyPayment = null!;
        private Label lblCompanyTerms = null!;
        private ListBox lstCompanyModules = null!;

        private DataGridView dgvRequests = null!;
        private TextBox txtRequestNotes = null!;
        private Button btnApproveRequest = null!;
        private Button btnRejectRequest = null!;
        private ComboBox cboRequestStatusFilter = null!;

        private ComboBox cboPlanPicker = null!;
        private TextBox txtPlanName = null!;
        private TextBox txtPlanDescription = null!;
        private TextBox txtPlanPrice = null!;
        private ComboBox cboPlanBilling = null!;
        private TextBox txtPlanTerms = null!;
        private Label lblPlanActiveState = null!;
        private Button btnPlanSave = null!;
        private Button btnPlanToggleActive = null!;
        private Button btnPlanCreate = null!;
        private CheckedListBox clbPlanModules = null!;
        private Button btnPlanAddModule = null!;
        private Button btnPlanRemoveModule = null!;

        private DataGridView dgvHistory = null!;

        // Plans & Modules redesign: plan cards view + plan editor view (the editor controls keep
        // their original coordinates and handlers - one management path, two presentations).
        private Panel pnlPlanCardsView = null!;
        private Panel? pnlPlanEditorView;
        private FlowLayoutPanel pnlPlanCards = null!;
        private Label lblPlanEditorTitle = null!;
        private Label lblPlanVersionInfo = null!;
        private Button btnPlanBackToCards = null!;
        private Button btnPlanViewHistory = null!;

        // Plan-configuration versioning (History tab).
        private DataGridView dgvPlanVersions = null!;
        private Panel pnlVersionDetails = null!;
        private Label lblVersionDetails = null!;
        private TextBox txtVersionTerms = null!;
        private Label lblHistoryFilter = null!;
        private Button btnHistoryClearFilter = null!;

        private IReadOnlyList<CompanySubscriptionInfo> _companyRows = new List<CompanySubscriptionInfo>();
        private IReadOnlyList<SubscriptionPlanInfo> _planRows = new List<SubscriptionPlanInfo>();
        private IReadOnlyList<SoftwareModule> _moduleCatalog = Array.Empty<SoftwareModule>();
        private IReadOnlyList<SubscriptionPlanVersionInfo> _planVersionRows = Array.Empty<SubscriptionPlanVersionInfo>();
        private int? _planVersionHistoryFilterPlanId;

        /// <summary>Which plans view is selected (the card list or the editor). Tracked explicitly
        /// because Control.Visible also reports false while the owning TabPage is not the selected
        /// page, which would make the probe read the wrong thing.</summary>
        private bool _planCardsViewActive = true;

        /// <summary>True while the version grid is being (re)bound: the selection event fired by
        /// the rebind itself must not start a nested master operation.</summary>
        private bool _rebindingPlanVersions;

        private Panel BuildSubscriptionPanel()
        {
            var panel = NewContentPanel();

            AddSectionTitle(panel, "Subscription", 24, 16);
            AddNote(panel,
                "Company software subscriptions (master DB). Companies request plan changes; you review them here. " +
                "Approving changes the company's plan, snapshots the plan price and terms, and writes the audit history. " +
                "Rejecting leaves the active subscription unchanged.",
                24, 48, 940, 32);

            // The native tab strip is HIDDEN (the same treatment the tenant window uses): WinForms
            // paints that strip with the light system theme and IGNORES TabControl.BackColor, so it
            // cannot be themed at all. The four entries below are ordinary Alpha Elite buttons
            // (dark idle / gold active) that switch the pages and load them.
            tabSubscription = new TabControl
            {
                Appearance = TabAppearance.FlatButtons,
                ItemSize = new System.Drawing.Size(0, 1),
                SizeMode = TabSizeMode.Fixed,
                Location = new System.Drawing.Point(24, 126),
                Size = new System.Drawing.Size(980, 662)
            };

            tabSubscription.TabPages.Add(BuildCompaniesTabPage());
            tabSubscription.TabPages.Add(BuildRequestsTabPage());
            tabSubscription.TabPages.Add(BuildPlansTabPage());
            tabSubscription.TabPages.Add(BuildHistoryTabPage());

            panel.Controls.Add(tabSubscription);

            // The mandated sub-sections of the Subscription section, in order.
            var tabNames = new[] { "Companies / Tenants", "Subscription Requests", "Plans & Modules", "History" };
            _subscriptionTabButtons = new Button[tabNames.Length];
            for (var i = 0; i < tabNames.Length; i++)
            {
                var name = tabNames[i];
                var button = NewSecondaryButton(name, 24 + i * 170, 84, 160);
                button.Click += async (s, e) => await SelectSubscriptionTabAsync(name);
                _subscriptionTabButtons[i] = button;
                panel.Controls.Add(button);
            }

            SetActiveSubscriptionTab(tabSubscription.SelectedTab?.Text ?? "Companies / Tenants");

            // ShowPanelAsync awaits this: initial data load for the selected page.
            panel.Tag = (Func<Task>)LoadSubscriptionTabDataAsync;

            return panel;
        }

        private Button[] _subscriptionTabButtons = Array.Empty<Button>();

        /// <summary>Switches the visible subscription page (the native strip is hidden).</summary>
        private async Task SelectSubscriptionTabAsync(string tabName)
        {
            var page = tabSubscription.TabPages.Cast<TabPage>()
                .FirstOrDefault(p => string.Equals(p.Text, tabName, StringComparison.Ordinal));
            if (page == null)
            {
                return;
            }

            tabSubscription.SelectedTab = page;
            SetActiveSubscriptionTab(page.Text);

            // Serialized: switching pages quickly used to overlap two loads on the shared context.
            await RunMasterOperationAsync(LoadSubscriptionTabDataAsync);
        }

        /// <summary>Dark idle / gold active, matching the master navigation treatment.</summary>
        private void SetActiveSubscriptionTab(string activeTab)
        {
            for (var i = 0; i < _subscriptionTabButtons.Length && i < tabSubscription.TabPages.Count; i++)
            {
                var page = tabSubscription.TabPages[i];
                var active = string.Equals(page.Text, activeTab, StringComparison.Ordinal);
                _subscriptionTabButtons[i].BackColor = active ? ColorHeader : ColorPanel;
                _subscriptionTabButtons[i].ForeColor = active ? ColorGold : ColorText;
            }
        }

        // =====================================================================================
        // Tab 1: company subscriptions
        // =====================================================================================

        private TabPage BuildCompaniesTabPage()
        {
            var page = new TabPage("Companies / Tenants");

            cboSubCompany = NewCombo(24, 24, 420);
            // Root cause of "AlphaElite.infrastructure.services.CompanySubscriptionInfo" in this
            // drop-down: the rows were pushed into Items with no display mapping, so WinForms
            // fell back to object.ToString(). The mapping below is the SAME convention the tenant
            // member pickers already use (DataSource + DisplayMember + ValueMember).
            cboSubCompany.DisplayMember = nameof(CompanySubscriptionInfo.CompanyName);
            cboSubCompany.ValueMember = nameof(CompanySubscriptionInfo.CompanyId);
            cboSubCompany.SelectedIndexChanged += (s, e) => RenderSelectedCompany();

            lblCompanyPlan = MakeDetailLabel2(24, 70);
            lblCompanyStatus = MakeDetailLabel2(24, 96);
            lblCompanyDates = MakeDetailLabel2(24, 122);
            lblCompanyPayment = MakeDetailLabel2(24, 148);
            lblCompanyTerms = MakeDetailLabel2(24, 174, 920);

            page.Controls.Add(cboSubCompany);
            page.Controls.Add(lblCompanyPlan);
            page.Controls.Add(lblCompanyStatus);
            page.Controls.Add(lblCompanyDates);
            page.Controls.Add(lblCompanyPayment);
            page.Controls.Add(lblCompanyTerms);

            page.Controls.Add(new Label
            {
                Text = "Enabled modules (plan AND subscription state)",
                ForeColor = ColorMuted,
                Location = new System.Drawing.Point(24, 210),
                Size = new System.Drawing.Size(400, 20)
            });

            lstCompanyModules = new ListBox
            {
                Location = new System.Drawing.Point(24, 232),
                Size = new System.Drawing.Size(920, 180),
                BackColor = ColorField,
                ForeColor = ColorText,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            page.Controls.Add(lstCompanyModules);

            return page;
        }

        private async Task LoadCompanySubscriptionsAsync()
        {
            _companyRows = await _runtime.SubscriptionService.GetAllCompanySubscriptionsAsync();

            var selected = cboSubCompany.SelectedItem as CompanySubscriptionInfo;
            // Rebind (never Items.Add): the display name comes from DisplayMember, and the
            // selected VALUE stays the company's identifier. (BeginUpdate/EndUpdate is not used
            // around a data-bound assignment.)
            cboSubCompany.DataSource = _companyRows.ToList();

            var keep = selected == null ? null : _companyRows.FirstOrDefault(r => r.CompanyId == selected.CompanyId);
            if (keep != null)
            {
                cboSubCompany.SelectedItem = keep;
            }
            else if (cboSubCompany.Items.Count > 0)
            {
                cboSubCompany.SelectedIndex = 0;
            }
            else
            {
                RenderSelectedCompany();
            }
        }

        private void RenderSelectedCompany()
        {
            if (cboSubCompany.SelectedItem is not CompanySubscriptionInfo info)
            {
                lblCompanyPlan.Text = string.Empty;
                lblCompanyStatus.Text = string.Empty;
                lblCompanyDates.Text = string.Empty;
                lblCompanyPayment.Text = string.Empty;
                lblCompanyTerms.Text = string.Empty;
                lstCompanyModules.Items.Clear();
                return;
            }

            lblCompanyPlan.Text = $"Company: {info.CompanyName} ({info.CompanyCode})    Plan: {(info.PlanName.Length == 0 ? "(no subscription)" : info.PlanName)}";
            lblCompanyStatus.Text = "Status: " + info.EffectiveStatus +
                (string.Equals(info.Status, info.EffectiveStatus, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : $" (stored: {info.Status})");
            lblCompanyDates.Text = info.StartDate == default
                ? "Start: -    End: -    Renewal: -"
                : $"Start: {info.StartDate:MMM d, yyyy}    End: {info.EndDate:MMM d, yyyy}";

            // The subscription's own snapshot values (price, billing cycle, payment status,
            // renewal, terms) already arrive with the service projection, so the render reads
            // them from `info`. It must NOT dereference CompanySubscription.SubscriptionPlan:
            // that navigation is not loaded for an untracked CompanySubscription (and is not
            // loaded by the service either), which threw NullReferenceException here.
            var hasSubscription = info.PlanCode.Length > 0;
            var billingCycle = string.IsNullOrWhiteSpace(info.BillingCycle) ? "-" : info.BillingCycle;
            lblCompanyPayment.Text = !hasSubscription
                ? "Price: -    Payment status: -"
                : $"Price: {info.Price:0.00} / {billingCycle}    Payment status: {info.PaymentStatus}" +
                  (info.RenewalDate.HasValue ? $"    Renewal: {info.RenewalDate.Value:MMM d, yyyy}" : string.Empty);
            lblCompanyTerms.Text = !hasSubscription || info.TermsAndAgreement.Length == 0
                ? "Terms: -"
                : "Terms (snapshot): " + info.TermsAndAgreement;

            lstCompanyModules.BeginUpdate();
            lstCompanyModules.Items.Clear();
            for (var i = 0; i < info.EnabledModuleCodes.Count; i++)
            {
                lstCompanyModules.Items.Add($"{i + 1}. {info.EnabledModuleCodes[i]}");
            }
            if (info.EnabledModuleCodes.Count == 0)
            {
                lstCompanyModules.Items.Add("(no modules - no active subscription or plan has none)");
            }
            lstCompanyModules.EndUpdate();
        }

        // =====================================================================================
        // Tab 2: subscription requests (OPTION 2 review)
        // =====================================================================================

        private TabPage BuildRequestsTabPage()
        {
            var page = new TabPage("Subscription Requests");

            page.Controls.Add(new Label
            {
                Text = "Status filter",
                ForeColor = ColorMuted,
                Location = new System.Drawing.Point(24, 20),
                Size = new System.Drawing.Size(90, 20)
            });

            cboRequestStatusFilter = NewCombo(118, 18, 140, "(all)", SubscriptionChangeRequestStatuses.Pending,
                SubscriptionChangeRequestStatuses.Approved, SubscriptionChangeRequestStatuses.Rejected, SubscriptionChangeRequestStatuses.Cancelled);
            // Serialized: flipping the filter while a load runs used to overlap two master reads.
            cboRequestStatusFilter.SelectedIndexChanged += async (s, e) => await RunMasterOperationAsync(LoadRequestsAsync);
            page.Controls.Add(cboRequestStatusFilter);

            dgvRequests = NewGrid();
            dgvRequests.Location = new System.Drawing.Point(24, 52);
            dgvRequests.Size = new System.Drawing.Size(920, 380);
            page.Controls.Add(dgvRequests);

            page.Controls.Add(new Label
            {
                Text = "Review notes (required content for a rejection)",
                ForeColor = ColorMuted,
                Location = new System.Drawing.Point(24, 448),
                Size = new System.Drawing.Size(400, 20)
            });

            txtRequestNotes = new TextBox
            {
                Multiline = true,
                Location = new System.Drawing.Point(24, 470),
                Size = new System.Drawing.Size(640, 90),
                BackColor = ColorField,
                ForeColor = ColorText,
                BorderStyle = BorderStyle.FixedSingle
            };
            page.Controls.Add(txtRequestNotes);

            btnApproveRequest = NewPrimaryButton("Approve", 684, 470, 120);
            btnApproveRequest.Click += async (s, e) => await RunMasterOperationAsync(() => ReviewSelectedRequestAsync(approve: true));
            page.Controls.Add(btnApproveRequest);

            btnRejectRequest = NewSecondaryButton("Reject", 684, 512, 120);
            btnRejectRequest.Click += async (s, e) => await RunMasterOperationAsync(() => ReviewSelectedRequestAsync(approve: false));
            page.Controls.Add(btnRejectRequest);

            AddNote(page,
                "Approve: the company's subscription switches to the requested plan; the plan price and terms are " +
                "snapshot-copied and a history row is written (one transaction). Reject: nothing changes - only the " +
                "reviewer, date and notes are recorded.",
                24, 572, 900, 40);

            return page;
        }

        private async Task LoadRequestsAsync()
        {
            var filter = cboRequestStatusFilter.SelectedItem?.ToString();
            var status = string.IsNullOrEmpty(filter) || filter == "(all)" ? null : filter;

            var rows = await _runtime.SubscriptionService.GetChangeRequestsAsync(status);
            dgvRequests.DataSource = rows.Select(r => new
            {
                r.SubscriptionChangeRequestId,
                Company = $"{r.CompanyName} ({r.CompanyCode})",
                CurrentPlan = r.CurrentPlanCode,
                RequestedPlan = r.RequestedPlanCode,
                r.RequestType,
                RequestedAt = r.RequestedAt,
                r.RequestedBy,
                r.Status,
                ReviewedBy = r.ReviewedBy ?? string.Empty,
                ReviewNotes = r.ReviewNotes
            }).ToList();
        }

        private async Task ReviewSelectedRequestAsync(bool approve)
        {
            if (dgvRequests.CurrentRow?.DataBoundItem is not { } bound)
            {
                MessageBox.Show("Select a request first.", "Subscription Requests",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var requestId = (int)bound.GetType().GetProperty("SubscriptionChangeRequestId")!.GetValue(bound)!;
            var status = (string)bound.GetType().GetProperty("Status")!.GetValue(bound)!;

            if (!string.Equals(status, SubscriptionChangeRequestStatuses.Pending, StringComparison.Ordinal))
            {
                MessageBox.Show($"This request has already been reviewed ({status}).", "Subscription Requests",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var notes = txtRequestNotes.Text.Trim();
            if (!approve && notes.Length == 0)
            {
                MessageBox.Show("A rejection requires review notes / a reason.", "Subscription Requests",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = await _runtime.SubscriptionService.ReviewChangeRequestAsync(
                requestId, approve, _session.Username, notes);

            if (!result.Success)
            {
                MessageBox.Show("The review could not be saved: " + result.Error, "Subscription Requests",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(approve
                    ? "Approved: the company's subscription now uses the requested plan (price/terms snapshot + history recorded)."
                    : "Rejected: the active subscription is unchanged; the reviewer and reason are recorded.",
                "Subscription Requests", MessageBoxButtons.OK, MessageBoxIcon.Information);

            txtRequestNotes.Clear();
            await LoadRequestsAsync();
            await LoadCompanySubscriptionsAsync();
        }

        // =====================================================================================
        // Tab 3: plans + module assignment
        // =====================================================================================

        private TabPage BuildPlansTabPage()
        {
            var page = new TabPage("Plans & Modules");

            // =====================================================================================
            // VIEW 1 - the plan CARDS (primary presentation of Plans & Modules).
            // One dark card per plan, read from the master database: name, price, billing cycle,
            // status, current version, effective date, the included modules and the actions.
            // =====================================================================================
            var cardsView = new Panel
            {
                Location = new System.Drawing.Point(0, 0),
                Size = new System.Drawing.Size(960, 634),
                BackColor = ColorBackground
            };
            pnlPlanCardsView = cardsView;

            cardsView.Controls.Add(new Label
            {
                Text = "Plan",
                ForeColor = ColorMuted,
                BackColor = Color.Transparent,
                Location = new System.Drawing.Point(24, 22),
                Size = new System.Drawing.Size(40, 20)
            });

            cboPlanPicker = NewCombo(68, 18, 240);
            // Same root cause as the company drop-down: plan objects were added with no display
            // mapping. Bind the plan NAME for display and the plan CODE as the value.
            cboPlanPicker.DisplayMember = nameof(SubscriptionPlanInfo.Name);
            cboPlanPicker.ValueMember = nameof(SubscriptionPlanInfo.Code);
            cboPlanPicker.SelectedIndexChanged += (s, e) => RenderSelectedPlan();
            cardsView.Controls.Add(cboPlanPicker);

            btnPlanCreate = NewSecondaryButton("New Plan", 320, 16, 120);
            btnPlanCreate.Click += async (s, e) => await OpenNewPlanDialogAsync();
            cardsView.Controls.Add(btnPlanCreate);

            AddNote(cardsView,
                "Adding or removing a module - or changing the price, billing cycle or Terms & Agreement - creates a NEW " +
                "immutable version. Older versions keep their own terms and their own module list forever.",
                464, 20, 480, 32);

            pnlPlanCards = new FlowLayoutPanel
            {
                Location = new System.Drawing.Point(24, 72),
                Size = new System.Drawing.Size(920, 552),
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = ColorBackground
            };
            cardsView.Controls.Add(pnlPlanCards);

            // =====================================================================================
            // VIEW 2 - the plan EDITOR. It is built by the unchanged control code below (same
            // coordinates) and re-parented into its own view; the two views occupy the same area
            // and are switched by the card's Edit action / the Back button.
            // =====================================================================================
            lblPlanActiveState = MakeDetailLabel2(600, 56, 320);
            page.Controls.Add(lblPlanActiveState);

            page.Controls.Add(new Label { Text = "Name", ForeColor = ColorMuted, Location = new System.Drawing.Point(24, 60), Size = new System.Drawing.Size(120, 20) });
            txtPlanName = NewField(150, 56, 300);
            page.Controls.Add(txtPlanName);

            page.Controls.Add(new Label { Text = "Price", ForeColor = ColorMuted, Location = new System.Drawing.Point(24, 90), Size = new System.Drawing.Size(120, 20) });
            txtPlanPrice = NewField(150, 86, 140);
            page.Controls.Add(txtPlanPrice);

            page.Controls.Add(new Label { Text = "Billing cycle", ForeColor = ColorMuted, Location = new System.Drawing.Point(310, 90), Size = new System.Drawing.Size(90, 20) });
            cboPlanBilling = NewCombo(404, 86, 120, BillingCycles.Monthly, BillingCycles.Yearly);
            page.Controls.Add(cboPlanBilling);

            page.Controls.Add(new Label { Text = "Description", ForeColor = ColorMuted, Location = new System.Drawing.Point(24, 120), Size = new System.Drawing.Size(120, 20) });
            txtPlanDescription = NewField(150, 116, 560);
            page.Controls.Add(txtPlanDescription);

            page.Controls.Add(new Label { Text = "Terms & Agreement", ForeColor = ColorMuted, Location = new System.Drawing.Point(24, 150), Size = new System.Drawing.Size(120, 20) });
            txtPlanTerms = new TextBox
            {
                Multiline = true,
                Location = new System.Drawing.Point(150, 148),
                Size = new System.Drawing.Size(560, 84),
                BackColor = ColorField,
                ForeColor = ColorText,
                BorderStyle = BorderStyle.FixedSingle
            };
            page.Controls.Add(txtPlanTerms);

            btnPlanSave = NewPrimaryButton("Save Plan", 24, 244, 130);
            btnPlanSave.Click += async (s, e) => await RunMasterOperationAsync(SaveSelectedPlanAsync);
            page.Controls.Add(btnPlanSave);

            btnPlanToggleActive = NewSecondaryButton("Deactivate", 164, 244, 130);
            btnPlanToggleActive.Click += async (s, e) => await RunMasterOperationAsync(ToggleSelectedPlanActiveAsync);
            page.Controls.Add(btnPlanToggleActive);

            // Terms & Agreement view: the selected plan's CURRENT version, status and effective
            // date are shown above the agreed text (historical versions are read in History).
            lblPlanEditorTitle = new Label
            {
                Text = "Plan",
                ForeColor = ColorGold,
                BackColor = Color.Transparent,
                Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold),
                AutoSize = false,
                Location = new System.Drawing.Point(24, 12),
                Size = new System.Drawing.Size(700, 24)
            };
            page.Controls.Add(lblPlanEditorTitle);

            lblPlanVersionInfo = new Label
            {
                ForeColor = ColorText,
                BackColor = Color.Transparent,
                Font = new System.Drawing.Font("Segoe UI", 8.5F),
                AutoSize = false,
                Location = new System.Drawing.Point(24, 34),
                Size = new System.Drawing.Size(700, 20)
            };
            page.Controls.Add(lblPlanVersionInfo);

            btnPlanBackToCards = NewSecondaryButton("Back to plan cards", 760, 12, 184);
            btnPlanBackToCards.Click += (s, e) => ShowPlanCardsView();
            page.Controls.Add(btnPlanBackToCards);

            btnPlanViewHistory = NewSecondaryButton("View version history", 760, 48, 184);
            btnPlanViewHistory.Click += async (s, e) => await OpenSelectedPlanVersionHistoryAsync();
            page.Controls.Add(btnPlanViewHistory);

            AddNote(page,
                "Plans are never deleted: deactivate instead. Existing subscriptions keep a deactivated plan and always " +
                "keep their own price/terms snapshot, so editing a plan here never rewrites what a company agreed to.",
                24, 290, 900, 34);

            page.Controls.Add(new Label
            {
                Text = "Modules included in the selected plan",
                ForeColor = ColorMuted,
                Location = new System.Drawing.Point(24, 344),
                Size = new System.Drawing.Size(400, 20)
            });

            clbPlanModules = new CheckedListBox
            {
                Location = new System.Drawing.Point(24, 366),
                Size = new System.Drawing.Size(640, 260),
                CheckOnClick = true,
                BackColor = ColorField,
                ForeColor = ColorText,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            page.Controls.Add(clbPlanModules);

            btnPlanAddModule = NewPrimaryButton("Add checked", 684, 366, 130);
            btnPlanAddModule.Click += async (s, e) => await RunMasterOperationAsync(() => ApplyModuleChangesAsync(add: true));
            page.Controls.Add(btnPlanAddModule);

            btnPlanRemoveModule = NewSecondaryButton("Remove checked", 684, 408, 130);
            btnPlanRemoveModule.Click += async (s, e) => await RunMasterOperationAsync(() => ApplyModuleChangesAsync(add: false));
            page.Controls.Add(btnPlanRemoveModule);

            AddNote(page,
                "Adding or removing a module, changing the price, the billing cycle or the Terms & Agreement creates a NEW " +
                "plan version. Every older version keeps its own module list and its own terms text.",
                684, 448, 250, 60);

            // The editor controls were created directly on the page (unchanged coordinates); move
            // them into their own view so the CARDS view can occupy the same area. Exactly one of
            // the two views is visible at a time.
            pnlPlanEditorView = new Panel
            {
                Location = new System.Drawing.Point(0, 0),
                Size = new System.Drawing.Size(960, 634),
                BackColor = ColorBackground,
                Visible = false
            };

            foreach (var control in page.Controls.Cast<Control>().ToList())
            {
                page.Controls.Remove(control);
                pnlPlanEditorView.Controls.Add(control);
            }

            page.Controls.Add(cardsView);
            page.Controls.Add(pnlPlanEditorView);
            cardsView.Visible = true;
            pnlPlanEditorView.Visible = false;

            return page;
        }

        // =====================================================================================
        // Plan cards
        // =====================================================================================

        private const int PlanCardWidth = 430;
        private const int PlanCardHeight = 344;

        /// <summary>Dark idle / gold active plan-card action button.</summary>
        private Button NewCardButton(string text, int x, int y, int width, bool gold)
        {
            var button = gold ? NewPrimaryButton(text, x, y, width) : NewSecondaryButton(text, x, y, width);
            button.Size = new System.Drawing.Size(width, 30);
            button.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            return button;
        }

        private void RebuildPlanCards()
        {
            if (pnlPlanCards == null)
            {
                return;
            }

            var namesByCode = ModuleNamesByCode();

            pnlPlanCards.SuspendLayout();
            foreach (Control old in pnlPlanCards.Controls)
            {
                old.Dispose();
            }
            pnlPlanCards.Controls.Clear();

            foreach (var plan in _planRows)
            {
                pnlPlanCards.Controls.Add(BuildPlanCard(plan, namesByCode));
            }

            pnlPlanCards.ResumeLayout();
        }

        /// <summary>
        /// ONE subscription card for a plan: name (+ stable code), price + billing cycle,
        /// status, current version, effective date, the included modules and the actions.
        /// Nothing is hardcoded - every value comes from the master database projection.
        /// </summary>
        private Panel BuildPlanCard(SubscriptionPlanInfo plan, IReadOnlyDictionary<string, string> namesByCode)
        {
            var card = new Panel
            {
                Location = new System.Drawing.Point(0, 0),
                Size = new System.Drawing.Size(PlanCardWidth, PlanCardHeight),
                BackColor = ColorPanel,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new System.Windows.Forms.Padding(0, 0, 10, 10)
            };

            card.Controls.Add(new Label
            {
                Text = plan.Name,
                ForeColor = ColorGold,
                BackColor = Color.Transparent,
                Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold),
                AutoSize = false,
                Location = new System.Drawing.Point(14, 12),
                Size = new System.Drawing.Size(400, 26)
            });

            card.Controls.Add(new Label
            {
                Text = plan.Code + (plan.Code.Length == 0 ? string.Empty : "  |  " + (plan.IsActive ? "selectable" : "not selectable")),
                ForeColor = ColorMuted,
                BackColor = Color.Transparent,
                Font = new System.Drawing.Font("Segoe UI", 8F),
                AutoSize = false,
                Location = new System.Drawing.Point(14, 38),
                Size = new System.Drawing.Size(400, 16)
            });

            card.Controls.Add(new Label
            {
                Text = $"{plan.Price:0.00}  /  {plan.BillingCycle}",
                ForeColor = ColorText,
                BackColor = Color.Transparent,
                Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold),
                AutoSize = false,
                Location = new System.Drawing.Point(14, 60),
                Size = new System.Drawing.Size(400, 24)
            });

            card.Controls.Add(new Label
            {
                Text = "Status: " + (plan.IsActive ? "Active" : "Inactive"),
                ForeColor = plan.IsActive ? ColorText : ColorDanger,
                BackColor = Color.Transparent,
                Font = new System.Drawing.Font("Segoe UI", 9F),
                AutoSize = false,
                Location = new System.Drawing.Point(14, 88),
                Size = new System.Drawing.Size(400, 20)
            });

            card.Controls.Add(new Label
            {
                Text = plan.CurrentVersionNumber.HasValue
                    ? $"Version: {plan.CurrentVersionNumber.Value}"
                    : "Version: - (not initialized)",
                ForeColor = ColorText,
                BackColor = Color.Transparent,
                Font = new System.Drawing.Font("Segoe UI", 9F),
                AutoSize = false,
                Location = new System.Drawing.Point(14, 108),
                Size = new System.Drawing.Size(400, 20)
            });

            card.Controls.Add(new Label
            {
                Text = "Effective: " + (plan.CurrentVersionEffectiveDate.HasValue
                    ? plan.CurrentVersionEffectiveDate.Value.ToString("MMM d, yyyy")
                    : "-"),
                ForeColor = ColorText,
                BackColor = Color.Transparent,
                Font = new System.Drawing.Font("Segoe UI", 9F),
                AutoSize = false,
                Location = new System.Drawing.Point(14, 128),
                Size = new System.Drawing.Size(400, 20)
            });

            card.Controls.Add(new Label
            {
                Text = $"Included modules ({plan.ModuleCodes.Count})",
                ForeColor = ColorGold,
                BackColor = Color.Transparent,
                Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold),
                AutoSize = false,
                Location = new System.Drawing.Point(14, 152),
                Size = new System.Drawing.Size(400, 20)
            });

            var modules = new ListBox
            {
                Location = new System.Drawing.Point(14, 174),
                Size = new System.Drawing.Size(400, 112),
                BackColor = ColorField,
                ForeColor = ColorText,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };
            foreach (var code in plan.ModuleCodes)
            {
                var name = namesByCode.TryGetValue(code, out var resolved) ? resolved : code;
                modules.Items.Add($"✓ {name}");
            }
            if (modules.Items.Count == 0)
            {
                modules.Items.Add("(no modules included)");
            }
            card.Controls.Add(modules);

            var code2 = plan.Code;
            var edit = NewCardButton("Edit", 14, 296, 114, gold: true);
            edit.Click += (s, e) =>
            {
                SelectPlanRow(code2);
                ShowPlanEditorView();
            };
            card.Controls.Add(edit);

            var history = NewCardButton("View History", 134, 296, 114, gold: false);
            history.Click += async (s, e) => await OpenPlanVersionHistoryForCardAsync(code2);
            card.Controls.Add(history);

            var toggle = NewCardButton(plan.IsActive ? "Deactivate" : "Activate", 254, 296, 160, gold: false);
            toggle.Click += async (s, e) => await TogglePlanActiveForCardAsync(code2);
            card.Controls.Add(toggle);

            // The card is built AFTER the panel-wide theme pass ran, so it is normalized here.
            UiTheme.Apply(card);
            return card;
        }

        /// <summary>Module code -> display name, from the master catalog (never hardcoded).</summary>
        private IReadOnlyDictionary<string, string> ModuleNamesByCode()
        {
            var catalog = ModuleCatalog();
            return catalog
                .GroupBy(m => m.Code, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        }

        /// <summary>
        /// The module catalog, cached after the first load. A cache miss reads a brand-new
        /// per-operation context, so this never overlaps anything (it replaced a read on the
        /// composition root's shared context, which was one of the DbContext race paths).
        /// </summary>
        private IReadOnlyList<SoftwareModule> ModuleCatalog()
        {
            if (_moduleCatalog.Count > 0)
            {
                return _moduleCatalog;
            }

            using var db = _runtime.MasterContextFactory.CreateDbContext();
            _moduleCatalog = db.SoftwareModules
                .AsNoTracking()
                .OrderBy(m => m.ModuleId)
                .ToList();
            return _moduleCatalog;
        }

        private void ShowPlanCardsView()
        {
            _planCardsViewActive = true;
            if (pnlPlanEditorView != null)
            {
                pnlPlanEditorView.Visible = false;
            }
            if (pnlPlanCardsView != null)
            {
                pnlPlanCardsView.Visible = true;
            }
        }

        private void ShowPlanEditorView()
        {
            _planCardsViewActive = false;
            if (pnlPlanCardsView != null)
            {
                pnlPlanCardsView.Visible = false;
            }
            if (pnlPlanEditorView != null)
            {
                pnlPlanEditorView.Visible = true;
            }
        }

        /// <summary>Selects a plan in the (preserved) drop-down; the value stays the plan CODE.</summary>
        private void SelectPlanRow(string planCode)
        {
            var row = _planRows.FirstOrDefault(p => p.Code == planCode);
            if (row != null && !ReferenceEquals(cboPlanPicker.SelectedItem, row))
            {
                cboPlanPicker.SelectedItem = row;
            }

            RenderSelectedPlan();
        }

        private async Task LoadPlansAsync()
        {
            _planRows = await _runtime.SubscriptionService.GetPlansAsync();

            // The module catalog (code -> name) is read once and cached; plan rendering then
            // never touches the database.
            _moduleCatalog = await _runtime.SubscriptionService.GetModulesAsync();

            var selectedCode = cboPlanPicker.SelectedItem is SubscriptionPlanInfo existing ? existing.Code : null;
            cboPlanPicker.DataSource = _planRows.ToList();

            var keep = selectedCode == null ? null : _planRows.FirstOrDefault(p => p.Code == selectedCode);
            if (keep != null)
            {
                cboPlanPicker.SelectedItem = keep;
            }
            else if (cboPlanPicker.Items.Count > 0)
            {
                cboPlanPicker.SelectedIndex = 0;
            }
            else
            {
                RenderSelectedPlan();
            }

            RebuildPlanCards();
        }

        private void RenderSelectedPlan()
        {
            if (cboPlanPicker.SelectedItem is not SubscriptionPlanInfo plan)
            {
                txtPlanName.Text = string.Empty;
                txtPlanPrice.Text = string.Empty;
                txtPlanDescription.Text = string.Empty;
                txtPlanTerms.Text = string.Empty;
                lblPlanActiveState.Text = string.Empty;
                lblPlanEditorTitle.Text = "Plan";
                lblPlanVersionInfo.Text = string.Empty;
                clbPlanModules.Items.Clear();
                return;
            }

            txtPlanName.Text = plan.Name;
            txtPlanPrice.Text = plan.Price.ToString("0.00");
            cboPlanBilling.SelectedItem = plan.BillingCycle;
            txtPlanDescription.Text = plan.Description;
            txtPlanTerms.Text = plan.TermsAndAgreement;
            lblPlanActiveState.Text = plan.IsActive ? "State: Active" : "State: INACTIVE (cannot be newly selected)";
            btnPlanToggleActive.Text = plan.IsActive ? "Deactivate" : "Activate";

            // Terms & Agreement view header: current version, status and effective date.
            lblPlanEditorTitle.Text = $"Plan: {plan.Name} ({plan.Code})";
            lblPlanVersionInfo.Text = "Current version: " + (plan.CurrentVersionNumber.HasValue
                    ? "v" + plan.CurrentVersionNumber.Value
                    : "-") +
                "   |   Status: " + (plan.IsActive ? "Active" : "Inactive") +
                "   |   Effective: " + (plan.CurrentVersionEffectiveDate.HasValue
                    ? plan.CurrentVersionEffectiveDate.Value.ToString("MMM d, yyyy")
                    : "-") +
                "   |   Versions: " + plan.VersionCount +
                "   |   Modules: " + plan.ModuleCodes.Count;

            // The catalog is cached (loaded by LoadPlansAsync); this used to be a SYNCHRONOUS query
            // on the composition root's shared master context, which raced every load.
            var moduleCatalog = ModuleCatalog();

            clbPlanModules.BeginUpdate();
            clbPlanModules.Items.Clear();
            foreach (var module in moduleCatalog)
            {
                var index = clbPlanModules.Items.Add($"{module.Code}  ({module.Name})");
                clbPlanModules.SetItemChecked(index, plan.ModuleCodes.Contains(module.Code, StringComparer.Ordinal));
            }
            clbPlanModules.EndUpdate();
        }

        private async Task SaveSelectedPlanAsync()
        {
            if (cboPlanPicker.SelectedItem is not SubscriptionPlanInfo plan)
            {
                return;
            }

            if (!decimal.TryParse(txtPlanPrice.Text.Trim(), out var price))
            {
                MessageBox.Show("Price must be a number (e.g. 499.00).", "Plans",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // changedBy: the authenticated Super Admin identity recorded on the new version.
            var result = await _runtime.SubscriptionService.UpdatePlanAsync(
                plan.SubscriptionPlanId,
                txtPlanName.Text, txtPlanDescription.Text, price,
                cboPlanBilling.SelectedItem?.ToString() ?? BillingCycles.Monthly,
                txtPlanTerms.Text,
                changedBy: _session.Username);

            if (!result.Success)
            {
                MessageBox.Show("The plan could not be saved: " + result.Error, "Plans",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await LoadPlansAsync();
            await LoadCompanySubscriptionsAsync();
        }

        private async Task ToggleSelectedPlanActiveAsync()
        {
            if (cboPlanPicker.SelectedItem is not SubscriptionPlanInfo plan)
            {
                return;
            }

            var activating = !plan.IsActive;
            var result = await _runtime.SubscriptionService.SetPlanActiveAsync(plan.SubscriptionPlanId, activating);
            if (!result.Success)
            {
                MessageBox.Show("The plan state could not be changed: " + result.Error, "Plans",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Activation state is not a subscribed-terms change: no version is created here.
            await LoadPlansAsync();
        }

        /// <summary>Card action: activate/deactivate the plan the card belongs to.</summary>
        private async Task TogglePlanActiveForCardAsync(string planCode)
        {
            await RunMasterOperationAsync(async () =>
            {
                SelectPlanRow(planCode);
                await ToggleSelectedPlanActiveAsync();
            });
        }

        /// <summary>Card action: open the version history filtered to this plan.</summary>
        private async Task OpenPlanVersionHistoryForCardAsync(string planCode)
        {
            var plan = _planRows.FirstOrDefault(p => p.Code == planCode);
            _planVersionHistoryFilterPlanId = plan?.SubscriptionPlanId;
            await SelectSubscriptionTabAsync("History");
        }

        private async Task OpenSelectedPlanVersionHistoryAsync()
        {
            if (cboPlanPicker.SelectedItem is SubscriptionPlanInfo plan)
            {
                _planVersionHistoryFilterPlanId = plan.SubscriptionPlanId;
            }

            await SelectSubscriptionTabAsync("History");
        }

        /// <summary>
        /// "New Plan": asks for the stable code and creates the plan from the editor fields.
        /// Previously this started a fire-and-forget gated task; it is now a properly awaited
        /// click handler (async handler -> awaited operation), so a failure is reported instead of
        /// being lost.
        /// </summary>
        private async Task OpenNewPlanDialogAsync()
        {
            string? code = null;
            using (var dialog = new Form
            {
                Text = "New Plan",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                Size = new System.Drawing.Size(420, 150),
                BackColor = ColorPanel,
                MinimizeBox = false,
                MaximizeBox = false
            })
            {
                var label = new Label
                {
                    Text = "Plan code (stable identifier, e.g. LAUNCH_PLAN):",
                    ForeColor = ColorText,
                    Location = new System.Drawing.Point(12, 12),
                    Size = new System.Drawing.Size(380, 20)
                };
                var input = new TextBox
                {
                    Location = new System.Drawing.Point(12, 36),
                    Size = new System.Drawing.Size(380, 24),
                    BackColor = ColorField,
                    ForeColor = ColorText,
                    BorderStyle = BorderStyle.FixedSingle
                };
                var ok = NewPrimaryButton("Create", 264, 72, 128);
                ok.DialogResult = DialogResult.OK;
                dialog.Controls.Add(label);
                dialog.Controls.Add(input);
                dialog.Controls.Add(ok);
                dialog.AcceptButton = ok;

                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    code = input.Text.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return;
            }

            // The editor fields are the source of the new plan's values (unchanged behaviour), so
            // the editor view is shown first and the create runs INSIDE the master gate as a
            // properly awaited operation (it used to be a fire-and-forget task).
            ShowPlanEditorView();
            await RunMasterOperationAsync(() => CreatePlanFromEditorAsync(code));
        }

        private async Task CreatePlanFromEditorAsync(string code)
        {
            if (!decimal.TryParse(txtPlanPrice.Text.Trim(), out var price))
            {
                MessageBox.Show("Price must be a number (e.g. 499.00).", "Plans",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = await _runtime.SubscriptionService.CreatePlanAsync(
                code, txtPlanName.Text, txtPlanDescription.Text, price,
                cboPlanBilling.SelectedItem?.ToString() ?? BillingCycles.Monthly,
                txtPlanTerms.Text,
                changedBy: _session.Username);

            if (!result.Success)
            {
                MessageBox.Show("The plan could not be created: " + result.Error, "Plans",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await LoadPlansAsync();
            SelectPlanRow(code);

            // The new plan already has its Version 1; show the cards so it is visible immediately.
            ShowPlanCardsView();
        }

        private async Task ApplyModuleChangesAsync(bool add)
        {
            if (cboPlanPicker.SelectedItem is not SubscriptionPlanInfo plan)
            {
                return;
            }

            SubscriptionMutationResult lastResult = SubscriptionMutationResult.Ok();
            var applied = 0;

            foreach (var checkedItem in clbPlanModules.CheckedItems)
            {
                var code = checkedItem.ToString()!.Split(' ')[0];
                lastResult = add
                    ? await _runtime.SubscriptionService.AddModuleToPlanAsync(plan.SubscriptionPlanId, code, changedBy: _session.Username)
                    : await _runtime.SubscriptionService.RemoveModuleFromPlanAsync(plan.SubscriptionPlanId, code, changedBy: _session.Username);
                if (lastResult.Success)
                {
                    applied++;
                }
                else
                {
                    break;
                }
            }

            if (!lastResult.Success)
            {
                MessageBox.Show("Module update stopped: " + lastResult.Error, "Plans",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (applied > 0)
            {
                await LoadPlansAsync();
                await LoadCompanySubscriptionsAsync();
            }
        }

        // =====================================================================================
        // Tab 4: history
        // =====================================================================================

        private TabPage BuildHistoryTabPage()
        {
            var page = new TabPage("History");

            lblHistoryFilter = new Label
            {
                ForeColor = ColorMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Location = new System.Drawing.Point(24, 16),
                Size = new System.Drawing.Size(724, 20)
            };
            page.Controls.Add(lblHistoryFilter);

            btnHistoryClearFilter = NewSecondaryButton("Show all plans", 760, 12, 184);
            btnHistoryClearFilter.Click += async (s, e) =>
            {
                _planVersionHistoryFilterPlanId = null;
                await RunMasterOperationAsync(LoadHistoryAsync);
            };
            page.Controls.Add(btnHistoryClearFilter);

            AddSectionTitle(page, "Plan versions (Terms & Modules snapshots)", 24, 40);

            dgvPlanVersions = NewGrid();
            dgvPlanVersions.Location = new System.Drawing.Point(24, 68);
            dgvPlanVersions.Size = new System.Drawing.Size(600, 236);
            dgvPlanVersions.SelectionChanged += async (s, e) => await OnPlanVersionSelectionChangedAsync();
            page.Controls.Add(dgvPlanVersions);

            pnlVersionDetails = new Panel
            {
                Location = new System.Drawing.Point(636, 68),
                Size = new System.Drawing.Size(308, 236),
                BackColor = ColorPanel,
                BorderStyle = BorderStyle.FixedSingle,
                AutoScroll = true
            };

            lblVersionDetails = new Label
            {
                ForeColor = ColorText,
                BackColor = Color.Transparent,
                AutoSize = false,
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(272, 420)
            };
            pnlVersionDetails.Controls.Add(lblVersionDetails);
            page.Controls.Add(pnlVersionDetails);

            AddSectionTitle(page, "Selected version - Terms & Agreement snapshot", 24, 312);

            txtVersionTerms = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new System.Drawing.Point(24, 336),
                Size = new System.Drawing.Size(920, 96),
                BackColor = ColorField,
                ForeColor = ColorText,
                BorderStyle = BorderStyle.FixedSingle
            };
            page.Controls.Add(txtVersionTerms);

            AddSectionTitle(page, "Company subscription changes (audit trail)", 24, 440);

            dgvHistory = NewGrid();
            dgvHistory.Location = new System.Drawing.Point(24, 466);
            dgvHistory.Size = new System.Drawing.Size(920, 158);
            page.Controls.Add(dgvHistory);

            return page;
        }

        /// <summary>
        /// Selecting a plan version renders what changed against the version before it. It uses
        /// the per-operation subscription service directly (a read that writes only two labels),
        /// so it can never stack a nested master operation on the gate that is currently loading
        /// the History tab.
        /// </summary>
        private async Task OnPlanVersionSelectionChangedAsync()
        {
            if (_rebindingPlanVersions)
            {
                return;
            }

            var versionId = SelectedPlanVersionId();
            if (versionId == null)
            {
                return;
            }

            try
            {
                await LoadPlanVersionDetailsAsync(versionId.Value);
            }
            catch (Exception ex)
            {
                // Reported, never swallowed: the failure is visible in the details panel.
                lblVersionDetails.Text = $"Could not load this version: {ex.GetType().Name}: {ex.GetBaseException().Message}";
            }
        }

        private int? SelectedPlanVersionId()
        {
            if (dgvPlanVersions.CurrentRow?.DataBoundItem is not { } bound)
            {
                return null;
            }

            var property = bound.GetType().GetProperty("SubscriptionPlanVersionId");
            return property?.GetValue(bound) as int?;
        }

        private async Task LoadPlanVersionDetailsAsync(int subscriptionPlanVersionId)
        {
            var details = await _runtime.SubscriptionService.GetPlanVersionDetailsAsync(subscriptionPlanVersionId);
            if (details == null)
            {
                lblVersionDetails.Text = "(version not found)";
                txtVersionTerms.Text = string.Empty;
                return;
            }

            var v = details.Version;
            var previous = details.PreviousVersion;
            var lines = new List<string>
            {
                $"Plan: {v.PlanName} ({v.PlanCode})",
                $"Version: v{v.VersionNumber}" + (v.IsCurrent ? "  (current)" : string.Empty),
                $"Change: {v.ChangeTypeLabel}",
                $"Summary: {(v.ChangeSummary.Length == 0 ? "-" : v.ChangeSummary)}",
                $"Previous: {(previous == null ? "(none - first version)" : "v" + previous.VersionNumber)}",
                $"Module count: {details.PreviousModuleCount} -> {details.NewModuleCount}",
                "Added: " + (details.AddedModuleNames.Count == 0 ? "(none)" : string.Join(", ", details.AddedModuleNames)),
                "Removed: " + (details.RemovedModuleNames.Count == 0 ? "(none)" : string.Join(", ", details.RemovedModuleNames)),
                $"Price: {(details.PreviousPrice.HasValue ? details.PreviousPrice.Value.ToString("0.00") : "-")} -> {details.NewPrice:0.00}",
                $"Billing cycle: {(previous == null ? "-" : details.PreviousBillingCycle)} -> {details.NewBillingCycle}",
                "Terms changed: " + (previous == null ? "-" : details.TermsChanged ? "yes" : "no"),
                $"Effective: {v.EffectiveDate:MMM d, yyyy}",
                $"Changed: {v.ChangedAt:MMM d, yyyy HH:mm} by {v.ChangedBy}"
            };

            lblVersionDetails.Text = string.Join(Environment.NewLine, lines);

            // The version's OWN terms snapshot. Never the live plan's wording, so an old version
            // keeps showing exactly the text that was agreed for it.
            txtVersionTerms.Text = v.TermsAndAgreement;
        }

        private async Task LoadHistoryAsync()
        {
            // 1. Plan-configuration versions (immutable Terms & Modules snapshots).
            _planVersionRows = await _runtime.SubscriptionService.GetPlanVersionsAsync(_planVersionHistoryFilterPlanId);

            lblHistoryFilter.Text = _planVersionHistoryFilterPlanId.HasValue
                ? $"Showing: {(_planVersionRows.Count == 0 ? "(no versions for this plan)" : $"{_planVersionRows[0].PlanName} ({_planVersionRows[0].PlanCode}) - {_planVersionRows.Count} version(s)")}"
                : $"Showing: all plans - {_planVersionRows.Count} version(s) across {_planVersionRows.Select(v => v.SubscriptionPlanId).Distinct().Count()} plan(s)";

            _rebindingPlanVersions = true;
            try
            {
                dgvPlanVersions.DataSource = _planVersionRows.Select(v => new
                {
                    v.SubscriptionPlanVersionId,
                    Plan = $"{v.PlanName} ({v.PlanCode})",
                    Version = "v" + v.VersionNumber + (v.IsCurrent ? " (current)" : string.Empty),
                    Change = v.ChangeTypeLabel,
                    Effective = v.EffectiveDate.ToString("MMM d, yyyy"),
                    Changed = v.ChangedAt.ToString("MMM d, yyyy HH:mm"),
                    ChangedBy = v.ChangedBy,
                    Modules = v.ModuleCount
                }).ToList();
            }
            finally
            {
                _rebindingPlanVersions = false;
            }

            // 2. Company subscription audit trail (the existing SubscriptionHistory behaviour).
            var rows = await _runtime.SubscriptionService.GetHistoryAsync();
            dgvHistory.DataSource = rows.Select(h => new
            {
                h.HistoryId,
                Company = $"{h.CompanyName} ({h.CompanyCode})",
                h.Action,
                From = h.PreviousPlanCode,
                To = h.NewPlanCode,
                PreviousPrice = h.PreviousPrice,
                NewPrice = h.NewPrice,
                h.ChangedBy,
                ChangedAt = h.ChangedAt,
                h.Reason,
                h.Notes
            }).ToList();

            // 3. Details of the newest version - inside the SAME gated operation, so it is a
            //    plain helper call and never a nested gate acquisition.
            if (_planVersionRows.Count > 0)
            {
                await LoadPlanVersionDetailsAsync(_planVersionRows[0].SubscriptionPlanVersionId);
            }
            else
            {
                lblVersionDetails.Text = "(no plan versions recorded yet)";
                txtVersionTerms.Text = string.Empty;
            }
        }

        // =====================================================================================
        // Panel loading
        // =====================================================================================

        private async Task LoadSubscriptionTabDataAsync()
        {
            switch (tabSubscription.SelectedTab?.Text)
            {
                case "Companies / Tenants":
                    await LoadCompanySubscriptionsAsync();
                    break;
                case "Subscription Requests":
                    await LoadRequestsAsync();
                    break;
                case "Plans & Modules":
                    await LoadPlansAsync();
                    break;
                case "History":
                    await LoadHistoryAsync();
                    break;
            }
        }

        private static Label MakeDetailLabel2(int x, int y, int width = 900) => new()
        {
            ForeColor = ColorText,
            AutoSize = false,
            Location = new System.Drawing.Point(x, y),
            Size = new System.Drawing.Size(width, 22)
        };

        // =====================================================================================
        // Headless verification hooks (Super Admin Subscription screen render path).
        // The Phase 4 harness verified the workflow through the SERVICE only, so the screen's
        // own render path was never exercised - which is how an unloaded SubscriptionPlan
        // navigation reached a NullReferenceException at runtime. These hooks close that gap.
        // =====================================================================================

        /// <summary>Headless: how many companies the Companies tab currently holds.</summary>
        internal int SubscriptionCompanyRowCountForTest => _companyRows.Count;

        /// <summary>Headless: the subscription sub-navigation (buttons instead of the OS tab strip,
        /// which WinForms refuses to theme) and every page's surface colour. Requires the
        /// Subscription panel to have been built (the nav item or the render hook).</summary>
        internal string DescribeSubscriptionTabStripForTest()
        {
            var pages = tabSubscription == null
                ? "(not built)"
                : string.Join(", ", tabSubscription.TabPages.Cast<TabPage>().Select(p => $"{p.Text}={UiTheme.Hex(p.BackColor)}"));
            var buttons = _subscriptionTabButtons.Length == 0
                ? "(not built)"
                : string.Join(", ", _subscriptionTabButtons.Select(b => $"{b.Text}:back={UiTheme.Hex(b.BackColor)} fore={UiTheme.Hex(b.ForeColor)}"));

            return $"appearance={tabSubscription?.Appearance} itemSize={tabSubscription?.ItemSize.Height} pages=[{pages}] buttons=[{buttons}]";
        }

        /// <summary>Headless: what the selected company's detail labels show.</summary>
        internal string DescribeSelectedCompanyForTest() =>
            $"plan={lblCompanyPlan.Text}; status={lblCompanyStatus.Text}; dates={lblCompanyDates.Text}; " +
            $"payment={lblCompanyPayment.Text}; terms={lblCompanyTerms.Text}; modules={lstCompanyModules.Items.Count}";

        /// <summary>
        /// Headless: runs the exact Companies-tab render path (open the screen the way its nav
        /// item does, load the rows, then render every company) and returns the rendered detail
        /// lines. Throws only what the real path throws.
        /// </summary>
        internal async Task<string> RenderSubscriptionCompaniesForTestAsync()
        {
            // The screen is built lazily when its navigation item is activated, so the probe
            // must go through the same path (otherwise the controls do not exist yet).
            await ShowPanelAsync(BuildSubscriptionPanel, btnNavSubscription);
            await RunMasterOperationAsync(LoadCompanySubscriptionsAsync);

            var rendered = new List<string>();
            foreach (var row in _companyRows)
            {
                cboSubCompany.SelectedItem = row;
                RenderSelectedCompany();
                rendered.Add($"{row.CompanyCode}: {DescribeSelectedCompanyForTest()}");
            }

            return rendered.Count == 0 ? "(no companies)" : string.Join(" || ", rendered);
        }

        /// <summary>
        /// Headless: loads the Plans &amp; Modules tab through the same gated entry point the tab
        /// switch uses (without moving the tab selection, which would race the tab's own async
        /// handler in a headless run).
        /// </summary>
        internal async Task<string> LoadPlansForTestAsync()
        {
            await RunMasterOperationAsync(LoadPlansAsync);
            return $"plans={_planRows.Count} selectedTab={tabSubscription.SelectedTab?.Text ?? "(none)"}";
        }

        /// <summary>
        /// Headless: what the Companies drop-down DISPLAYS (the human-readable text WinForms
        /// renders for every item) and what it SELECTS underneath (the company identifier).
        /// </summary>
        internal string DescribeCompanyChoicesForTest()
        {
            var displayed = cboSubCompany.Items.Cast<object>()
                .Select(cboSubCompany.GetItemText)
                .ToList();
            var selected = cboSubCompany.SelectedItem as CompanySubscriptionInfo;

            return $"count={cboSubCompany.Items.Count} display=[{string.Join(" | ", displayed)}] " +
                $"selectedText='{cboSubCompany.Text}' " +
                $"selectedValue={cboSubCompany.SelectedValue ?? "(null)"} " +
                $"selectedItemCompanyId={(selected == null ? "(none)" : selected.CompanyId.ToString())} " +
                $"valueMember={cboSubCompany.ValueMember} displayMember={cboSubCompany.DisplayMember}";
        }

        /// <summary>Headless: selects one company by identifier and reports the rendered detail.</summary>
        internal string SelectCompanyForTest(string companyCode)
        {
            var row = _companyRows.FirstOrDefault(r => r.CompanyCode == companyCode);
            if (row == null)
            {
                return $"(no row {companyCode})";
            }

            cboSubCompany.SelectedItem = row;
            RenderSelectedCompany();
            return DescribeSelectedCompanyForTest();
        }

        /// <summary>
        /// Headless: what the Plan &amp; Modules drop-down DISPLAYS and SELECTS, plus what the
        /// plan editor renders for the selected plan (proof the selection plumbing still works).
        /// </summary>
        internal string DescribePlanChoicesForTest()
        {
            var displayed = cboPlanPicker.Items.Cast<object>()
                .Select(cboPlanPicker.GetItemText)
                .ToList();
            var selected = cboPlanPicker.SelectedItem as SubscriptionPlanInfo;

            return $"count={cboPlanPicker.Items.Count} display=[{string.Join(" | ", displayed)}] " +
                $"selectedText='{cboPlanPicker.Text}' selectedValue={cboPlanPicker.SelectedValue ?? "(null)"} " +
                $"selectedItemCode={(selected == null ? "(none)" : selected.Code)} " +
                $"editorName='{txtPlanName.Text}' editorPrice='{txtPlanPrice.Text}' " +
                $"valueMember={cboPlanPicker.ValueMember} displayMember={cboPlanPicker.DisplayMember}";
        }

        /// <summary>Headless: selects a plan by code, shows the editor view and reports it.</summary>
        internal string SelectPlanForTest(string planCode)
        {
            var plan = _planRows.FirstOrDefault(p => p.Code == planCode);
            if (plan == null)
            {
                return $"(no plan {planCode})";
            }

            SelectPlanRow(plan.Code);
            ShowPlanEditorView();
            return $"plan={plan.Code} name='{txtPlanName.Text}' price='{txtPlanPrice.Text}' " +
                $"billing='{cboPlanBilling.Text}' modules={clbPlanModules.CheckedItems.Count}/{clbPlanModules.Items.Count} state='{lblPlanActiveState.Text}'";
        }

        /// <summary>
        /// Headless: the PLAN CARDS the Super Admin sees - one entry per plan with every value it
        /// renders (name, price, billing cycle, status, current version, effective date and the
        /// included modules) plus the actions. Proves the presentation is really card based and
        /// that nothing on a card is a CLR type name.
        /// </summary>
        internal string DescribePlanCardsForTest()
        {
            var cards = new List<string>();

            foreach (Control card in pnlPlanCards.Controls)
            {
                string TextOf(string prefix)
                {
                    var label = card.Controls.OfType<Label>()
                        .FirstOrDefault(l => l.Text.StartsWith(prefix, StringComparison.Ordinal));
                    return label == null ? string.Empty : label.Text;
                }

                var moduleList = card.Controls.OfType<ListBox>().FirstOrDefault();
                var moduleCount = moduleList?.Items.Count ?? 0;
                var firstModules = moduleList == null
                    ? string.Empty
                    : string.Join(", ", moduleList.Items.Cast<object>().Take(3).Select(i => i.ToString()));
                var priceLabel = card.Controls.OfType<Label>().FirstOrDefault(l => l.Text.Contains(" / ", StringComparison.Ordinal));
                var buttons = string.Join("/", card.Controls.OfType<Button>().Select(b => b.Text));

                cards.Add($"[{card.Controls.OfType<Label>().First().Text}] price='{priceLabel?.Text}' {TextOf("Status: ")} | " +
                    $"{TextOf("Version: ")} | {TextOf("Effective: ")} | {TextOf("Included modules")} " +
                    $"({moduleCount} listed: {firstModules}) | buttons={buttons}");
            }

            var lightSurfaces = UiTheme.FindLightSurfaces(pnlPlanCards);
            var typeNames = UiTheme.FindTypeNameDisplayValues(pnlPlanCards);
            return $"cards={pnlPlanCards.Controls.Count} cardsSelected={_planCardsViewActive} " +
                $"editorSelected={!_planCardsViewActive} light={lightSurfaces.Count} typename={typeNames.Count} " +
                string.Join(" || ", cards);
        }

        /// <summary>Headless: switches back to the cards view (as the Back button does).</summary>
        internal string ShowPlanCardsViewForTest()
        {
            ShowPlanCardsView();
            return $"cardsSelected={_planCardsViewActive} editorSelected={!_planCardsViewActive} " +
                $"cardsLocalVisible={pnlPlanCardsView?.Visible} editorLocalVisible={pnlPlanEditorView?.Visible}";
        }

        /// <summary>Headless: what the Terms &amp; Agreement / version header of the editor shows.</summary>
        internal string DescribePlanEditorVersionForTest() =>
            $"title='{lblPlanEditorTitle.Text}' version='{lblPlanVersionInfo.Text}' state='{lblPlanActiveState.Text}' " +
            $"termsChars={txtPlanTerms.Text.Length}";

        /// <summary>Headless: the plan-version history grid (plan / version / change / dates / by).</summary>
        internal string DescribePlanVersionHistoryForTest()
        {
            var rows = new List<string>();
            foreach (DataGridViewRow row in dgvPlanVersions.Rows)
            {
                rows.Add(string.Join(" | ", row.Cells.Cast<DataGridViewCell>().Select(c => c.Value?.ToString() ?? string.Empty)));
            }

            return $"filter='{lblHistoryFilter.Text}' gridRows={dgvPlanVersions.Rows.Count} " +
                $"companyHistoryRows={dgvHistory?.Rows.Count ?? 0} termsChars={txtVersionTerms.Text.Length} " +
                string.Join(" ;; ", rows);
        }

        /// <summary>Headless: selects one plan version (by its number) and renders its details.</summary>
        internal async Task<string> SelectPlanVersionForTestAsync(int versionNumber)
        {
            foreach (DataGridViewRow row in dgvPlanVersions.Rows)
            {
                var versionText = row.Cells.Cast<DataGridViewCell>()
                    .Select(c => c.Value?.ToString() ?? string.Empty)
                    .FirstOrDefault(v => v.StartsWith("v", StringComparison.Ordinal));
                if (versionText == null || !versionText.Contains("v" + versionNumber, StringComparison.Ordinal))
                {
                    continue;
                }

                row.Selected = true;
                dgvPlanVersions.CurrentCell = row.Cells[0];
                break;
            }

            var versionId = SelectedPlanVersionId();
            if (versionId.HasValue)
            {
                await LoadPlanVersionDetailsAsync(versionId.Value);
            }

            return DescribePlanVersionDetailsForTest();
        }

        /// <summary>Headless: the selected version's diff + its own Terms &amp; Agreement snapshot.</summary>
        internal string DescribePlanVersionDetailsForTest() =>
            $"details=[{lblVersionDetails.Text.Replace(Environment.NewLine, " / ")}] terms='{txtVersionTerms.Text}'";

        /// <summary>
        /// Headless: the "impatient user" probe - the four tab loaders plus the plan editor are
        /// driven at the SAME time WITHOUT the master gate, which is what a user clicking through
        /// the section quickly used to do. Each loader reaches its data through the per-operation
        /// subscription service, so no two operations can share a context; nothing here needs the
        /// gate to be correct.
        /// </summary>
        internal async Task<string> RunUngatedRapidInteractionForTestAsync()
        {
            // A card can only be clicked when it is rendered, so the plan rows are loaded first;
            // then EVERYTHING is hammered at once, exactly like an impatient user.
            await LoadPlansAsync();
            ShowPlanCardsView();

            var loads = new List<Task>
            {
                LoadCompanySubscriptionsAsync(),
                LoadRequestsAsync(),
                LoadPlansAsync(),
                LoadHistoryAsync()
            };

            // Opening the plan editor in the middle of those loads (renders from cached values
            // only - the module catalog is no longer read from a shared context here).
            ShowPlanEditorView();
            SelectPlanRow(SoftwarePlanCodes.Enterprise);
            var midLoadEditor = DescribePlanEditorVersionForTest();

            await Task.WhenAll(loads);

            return $"companies={_companyRows.Count} requests={dgvRequests?.Rows.Count ?? 0} plans={_planRows.Count} " +
                $"versions={dgvPlanVersions?.Rows.Count ?? 0} cards={pnlPlanCards?.Controls.Count ?? 0} " +
                $"midLoadEditor='{midLoadEditor}' editorLight={UiTheme.FindLightSurfaces(pnlPlanEditorView ?? pnlPlanCardsView).Count}";
        }

        /// <summary>Headless: opens the version history for one plan (as the card action does).</summary>
        internal async Task<string> OpenPlanVersionHistoryForTestAsync(string planCode)
        {
            await OpenPlanVersionHistoryForCardAsync(planCode);
            return $"filter='{lblHistoryFilter.Text}' rows={dgvPlanVersions.Rows.Count}";
        }

        /// <summary>
        /// Headless: rapid Super Admin interaction - overlapping master reads AND rapid sub-section
        /// switching. With the serialization gate the UI state changes queue; the DATA layer no
        /// longer depends on the gate at all (each subscription call owns its context), so the
        /// "A second operation was started on this context instance" exception cannot occur.
        /// </summary>
        internal async Task<string> RunOverlappingSubscriptionLoadsForTestAsync()
        {
            // The probe opens the screen first: the screen's controls are built lazily when its
            // navigation item is activated, and the tab loads only ever run after that.
            await ShowPanelAsync(BuildSubscriptionPanel, btnNavSubscription);

            var first = RunMasterOperationAsync(async () =>
            {
                await LoadCompanySubscriptionsAsync();
                await LoadRequestsAsync();
            });

            var second = RunMasterOperationAsync(async () =>
            {
                await LoadPlansAsync();
                await LoadHistoryAsync();
            });

            // Propagates the real failure when the gate did not serialize the two reads.
            await Task.WhenAll(first, second);

            // Rapid sub-section switching through the REAL tab path (Companies -> Requests ->
            // Plans & Modules -> History -> Companies), queued on the gate exactly like clicks.
            foreach (var tabName in new[] { "Subscription Requests", "Plans & Modules", "History", "Companies / Tenants" })
            {
                await SelectSubscriptionTabAsync(tabName);
            }

            return $"companies={_companyRows.Count} requests={dgvRequests?.Rows.Count ?? 0} plans={_planRows.Count} " +
                $"history={dgvHistory?.Rows.Count ?? 0} planVersions={dgvPlanVersions?.Rows.Count ?? 0} " +
                $"cards={(pnlPlanCards == null ? 0 : pnlPlanCards.Controls.Count)} tab={tabSubscription.SelectedTab?.Text}";
        }
    }
}
