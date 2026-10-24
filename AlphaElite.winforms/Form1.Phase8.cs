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
    /// Phase 8 additions to the main window (Tenant C only):
    ///   - Current Branch selector in the header (Admin / Manager / GymStaff). The selected
    ///     branch is the authoritative branch context: every branch-scoped read and every
    ///     branch-stamped write uses it, while server-side authorization stays in charge.
    ///   - Branches management tab for Admin / Manager (create, edit, assign an EXISTING
    ///     Manager account, activate / deactivate). There is deliberately no delete.
    ///   - Member transfer to the current branch (Members.BranchId only; historical rows
    ///     of every other table keep the branch where they originally occurred).
    /// </summary>
    public partial class Form1
    {
        private Label? lblCurrentBranch;
        private ComboBox? cboCurrentBranch;
        private bool _suppressBranchSwitch;
        private int? _homeBranchId;

        private Button? btnNavBranches;
        private TabPage? tabBranches;
        private DataGridView? dgvBranches;
        private TextBox? txtBranchCode;
        private TextBox? txtBranchName;
        private TextBox? txtBranchCity;
        private ComboBox? cboBranchManager;
        private ComboBox? cboBranchStatus;
        private Button? btnBranchSave;
        private Button? btnBranchCancel;
        private Button? btnBranchActivate;
        private Button? btnBranchDeactivate;
        private int? _selectedBranchId;
        private List<UserAccount> _managerCache = new();

        private Button? btnMemberTransfer;

        /// <summary>The authoritative branch context for reads and write-stamping.</summary>
        private int? CurrentBranchId => _branchId;

        // Phase 2/3: only Admin manages branches. Manager keeps the ManageBranches
        // permission for staff supervision but loses the Branches module and nav.
        private bool CanManageBranches => _isBranchAware && _role == Roles.Admin && HasPermission(Permission.ManageBranches);

        private bool GetsBranchSelector => _isBranchAware &&
            (_role is Roles.Admin or Roles.Manager or Roles.GymStaff);

        // =====================================================================================
        // Construction / layout
        // =====================================================================================

        private void InitializePhase8Controls()
        {
            if (!_isBranchAware)
            {
                return;
            }

            var gold = Color.FromArgb(212, 175, 55);
            var panel = Color.FromArgb(24, 24, 24);
            var field = Color.FromArgb(38, 38, 38);

            // ---- Header: Current Branch selector (Admin / Manager / GymStaff) ----
            // The header grows to a second row so the selector can never paint over the signed-in
            // user / role labels (it used to be placed exactly on top of them at normal window
            // size, which hid them).
            if (GetsBranchSelector)
            {
                pnlHeader.Height = 92;

                lblCurrentBranch = new Label
                {
                    Text = "Current Branch:",
                    ForeColor = Color.FromArgb(150, 150, 150),
                    Location = new Point(24, 58),
                    Size = new Size(96, 20)
                };
                cboCurrentBranch = new ComboBox
                {
                    Location = new Point(124, 54),
                    Size = new Size(300, 24),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = field,
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    IntegralHeight = false
                };
                cboCurrentBranch.SelectedIndexChanged += async (s, e) => await OnCurrentBranchChangedAsync();

                // The dropdown list opens downwards over the dashboard. When it closes, Windows can
                // leave the area it covered showing stale pixels (the controls are still there and
                // still clickable, they are just not redrawn), so repaint the content explicitly.
                cboCurrentBranch.DropDownClosed += (s, e) => ForceContentRepaint();

                pnlHeader.Controls.Add(lblCurrentBranch);
                pnlHeader.Controls.Add(cboCurrentBranch);

                // User / role labels join the selector on the second row, on the right.
                lblHeaderUserName.Location = new Point(lblHeaderUserName.Left, 56);
                lblHeaderUserRole.Location = new Point(lblHeaderUserRole.Left, 74);
            }

            // ---- Sidebar: Branches nav (Admin / Manager) ----
            if (CanManageBranches)
            {
                btnNavBranches = CreateNavButton("Branches", 540);
                btnNavBranches.Click += async (s, e) =>
                {
                    await LoadBranchesGridAsync();
                    tabControlMain.SelectedTab = tabBranches!;
                    SetActiveNavButton(btnNavBranches);
                };
                pnlSidebar.Controls.Add(btnNavBranches);

                tabBranches = BuildBranchesTab();
                tabControlMain.TabPages.Add(tabBranches);
            }

            // ---- Members tab: transfer action (Admin / Manager) ----
            if (_isBranchAware && HasPermission(Permission.ManageMembers) && _role is Roles.Admin or Roles.Manager)
            {
                btnMemberTransfer = new Button
                {
                    Text = "Transfer to current branch",
                    Location = new Point(620, 150),
                    Size = new Size(200, 30),
                    BackColor = field,
                    ForeColor = gold,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnMemberTransfer.Click += async (s, e) => await TransferSelectedMemberToCurrentBranchAsync();
                tabMembers.Controls.Add(btnMemberTransfer);
            }
        }

        private TabPage BuildBranchesTab()
        {
            var tab = new TabPage("Branches")
            {
                BackColor = Color.FromArgb(18, 18, 18),
                ForeColor = Phase4Text
            };

            tab.Controls.Add(new Label
            {
                Text = "Branch management (soft deactivation only - branches are never deleted)",
                ForeColor = Color.Gold,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(20, 18),
                Size = new Size(560, 20)
            });

            dgvBranches = new DataGridView
            {
                Location = new Point(20, 45),
                Size = new Size(850, 200),
                BackgroundColor = Color.FromArgb(18, 18, 18),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false
            };
            dgvBranches.CellClick += dgvBranches_CellClick;
            tab.Controls.Add(dgvBranches);

            tab.Controls.Add(new Label { Text = "Code", ForeColor = Phase4Text, Location = new Point(20, 270), Size = new Size(110, 20) });
            txtBranchCode = new TextBox { Location = new Point(140, 268), Size = new Size(200, 24), BackColor = Phase4Field, ForeColor = Phase4Text, BorderStyle = BorderStyle.FixedSingle };
            tab.Controls.Add(txtBranchCode);

            tab.Controls.Add(new Label { Text = "Branch", ForeColor = Phase4Text, Location = new Point(20, 302), Size = new Size(110, 20) });
            txtBranchName = new TextBox { Location = new Point(140, 300), Size = new Size(200, 24), BackColor = Phase4Field, ForeColor = Phase4Text, BorderStyle = BorderStyle.FixedSingle };
            tab.Controls.Add(txtBranchName);

            tab.Controls.Add(new Label { Text = "City", ForeColor = Phase4Text, Location = new Point(20, 334), Size = new Size(110, 20) });
            txtBranchCity = new TextBox { Location = new Point(140, 332), Size = new Size(200, 24), BackColor = Phase4Field, ForeColor = Phase4Text, BorderStyle = BorderStyle.FixedSingle };
            tab.Controls.Add(txtBranchCity);

            tab.Controls.Add(new Label { Text = "Manager", ForeColor = Phase4Text, Location = new Point(20, 366), Size = new Size(110, 20) });
            cboBranchManager = new ComboBox { Location = new Point(140, 364), Size = new Size(240, 24), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Phase4Field, ForeColor = Phase4Text };
            tab.Controls.Add(cboBranchManager);

            tab.Controls.Add(new Label { Text = "Status", ForeColor = Phase4Text, Location = new Point(20, 398), Size = new Size(110, 20) });
            cboBranchStatus = new ComboBox { Location = new Point(140, 396), Size = new Size(120, 24), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Phase4Field, ForeColor = Phase4Text };
            cboBranchStatus.Items.AddRange(new object[] { "Active", "Inactive" });
            tab.Controls.Add(cboBranchStatus);

            btnBranchSave = new Button { Text = "Save", Location = new Point(140, 434), Size = new Size(110, 30), BackColor = Color.FromArgb(212, 175, 55), ForeColor = Color.Black, FlatStyle = FlatStyle.Flat };
            btnBranchSave.Click += async (s, e) => await SaveBranchAsync();
            tab.Controls.Add(btnBranchSave);

            btnBranchCancel = new Button { Text = "Cancel", Location = new Point(260, 434), Size = new Size(80, 30), BackColor = Phase4Panel, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnBranchCancel.Click += (s, e) => ClearBranchInputs();
            tab.Controls.Add(btnBranchCancel);

            btnBranchActivate = new Button { Text = "Activate", Location = new Point(360, 434), Size = new Size(90, 30), BackColor = Phase4Panel, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnBranchActivate.Click += async (s, e) => await SetSelectedBranchActiveAsync(true);
            tab.Controls.Add(btnBranchActivate);

            btnBranchDeactivate = new Button { Text = "Deactivate", Location = new Point(460, 434), Size = new Size(100, 30), BackColor = Phase4Panel, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnBranchDeactivate.Click += async (s, e) => await SetSelectedBranchActiveAsync(false);
            tab.Controls.Add(btnBranchDeactivate);

            return tab;
        }

        /// <summary>Members must not see the branch selector at all (self-only scope).</summary>
        private void ApplyPhase8Visibility()
        {
            if (lblCurrentBranch != null) lblCurrentBranch.Visible = GetsBranchSelector;
            if (cboCurrentBranch != null) cboCurrentBranch.Visible = GetsBranchSelector;
            if (btnMemberTransfer != null) btnMemberTransfer.Visible = _role is Roles.Admin or Roles.Manager;

            // Record the Branches nav in the same visible-nav diagnostics ApplyRoleVisibility
            // builds, so Admin/Manager nav visibility is provable headlessly.
            if (btnNavBranches != null) SetNav(btnNavBranches, CanManageBranches, "Branches");
        }

        /// <summary>
        /// WinForms can leave a container painted with its own background while its re-bound or
        /// re-shown children keep stale pixels - the controls still exist and are still clickable,
        /// they simply are not drawn. Forcing a layout pass plus a full invalidation of the content
        /// area guarantees the dashboard (KPIs and charts) is visually correct after a branch
        /// switch and after the Current Branch dropdown closes over it.
        /// </summary>
        private void ForceContentRepaint()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            pnlContent.PerformLayout();
            tabControlMain.PerformLayout();
            tabDashboard.PerformLayout();

            tabControlMain.Invalidate(true);
            pnlContent.Invalidate(true);
            pnlContent.Update();
        }

        // =====================================================================================
        // Current Branch selector
        // =====================================================================================

        /// <summary>
        /// Loads the selector with ACTIVE branches only. Admin additionally gets the
        /// tenant-wide "ALL BRANCHES" entry. Called once from Form1_Load (before the first
        /// module load) and after every branch management change.
        /// </summary>
        private async Task InitializeCurrentBranchSelectorAsync()
        {
            if (!_isBranchAware || cboCurrentBranch == null)
            {
                return;
            }

            _suppressBranchSwitch = true;
            try
            {
                await using var db = await _dataContextFactory.CreateAsync(_companyId, true);

                var branches = await db.Branches.AsNoTracking()
                    .Where(b => b.IsActive)
                    .OrderBy(b => b.BranchId)
                    .Select(b => new { b.BranchId, b.BranchCode, b.BranchName })
                    .ToListAsync();

                cboCurrentBranch.Items.Clear();

                if (_role == Roles.Admin)
                {
                    cboCurrentBranch.Items.Add("(ALL BRANCHES)");

                    foreach (var b in branches)
                    {
                        cboCurrentBranch.Items.Add($"{b.BranchId}: {b.BranchCode} - {b.BranchName}");
                    }
                }
                else
                {
                    // Phase 2/3: Manager / GymStaff are pinned to their home branch - the
                    // selector lists ONLY their own branch (active or not), so switching
                    // away is impossible from the UI.
                    var home = _homeBranchId.HasValue
                        ? await db.Branches.AsNoTracking()
                            .Where(b => b.BranchId == _homeBranchId.Value)
                            .Select(b => new { b.BranchId, b.BranchCode, b.BranchName })
                            .FirstOrDefaultAsync()
                        : null;

                    if (home != null)
                    {
                        cboCurrentBranch.Items.Add($"{home.BranchId}: {home.BranchCode} - {home.BranchName}");
                    }
                }

                // Current selection: staff start at their home branch, Admin at ALL BRANCHES.
                var selectedIndex = _role == Roles.Admin
                    ? (_branchId.HasValue ? IndexOfBranchItem(_branchId.Value) : 0)
                    : IndexOfBranchItem(_homeBranchId);

                if (selectedIndex < 0 && cboCurrentBranch.Items.Count > 0)
                {
                    // Home branch is gone or inactive: Admin falls back to ALL BRANCHES,
                    // pinned roles fall back to their only listed branch (their own).
                    selectedIndex = 0;
                }

                if (selectedIndex >= 0)
                {
                    cboCurrentBranch.SelectedIndex = selectedIndex;
                }

                // The change handler is suppressed while the list is rebuilt, so the authoritative
                // branch id must be brought in step explicitly. Without this, deactivating the
                // working branch (or an inactive home branch falling back to another one) would
                // leave _branchId pointing at a branch the selector no longer shows.
                var restored = ParseCurrentBranchItem(cboCurrentBranch.SelectedItem);
                if (_branchId != restored)
                {
                    _branchId = restored;
                    _branchName = string.Empty;
                }
            }
            finally
            {
                _suppressBranchSwitch = false;
            }
        }

        private int IndexOfBranchItem(int? branchId)
        {
            if (cboCurrentBranch == null || !branchId.HasValue)
            {
                return -1;
            }

            for (var i = 0; i < cboCurrentBranch.Items.Count; i++)
            {
                var item = cboCurrentBranch.Items[i]?.ToString() ?? string.Empty;
                var head = item.Split(':')[0].Trim();
                if (int.TryParse(head, out var id) && id == branchId.Value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int? ParseCurrentBranchItem(object? item)
        {
            var value = item?.ToString();
            if (string.IsNullOrWhiteSpace(value) || value == "(ALL BRANCHES)")
            {
                return null;
            }

            var head = value.Split(':')[0].Trim();
            return int.TryParse(head, out var id) ? id : null;
        }

        /// <summary>
        /// Branch switch: re-resolve the scope, refresh the header context and reload every
        /// branch-scoped module so no stale rows from the previous branch stay visible.
        /// </summary>
        private async Task OnCurrentBranchChangedAsync()
        {
            if (_suppressBranchSwitch || cboCurrentBranch == null)
            {
                return;
            }

            var selected = ParseCurrentBranchItem(cboCurrentBranch.SelectedItem);

            // Phase 2/3: Manager / GymStaff are pinned to their home branch. The selector only
            // lists it, and any other target (stale UI state or a programmatic switch) is
            // refused here and restored to the home branch.
            if (_role != Roles.Admin && _homeBranchId.HasValue && selected != _homeBranchId)
            {
                var homeIndex = IndexOfBranchItem(_homeBranchId);
                if (homeIndex >= 0 && cboCurrentBranch.SelectedIndex != homeIndex)
                {
                    _suppressBranchSwitch = true;
                    try
                    {
                        cboCurrentBranch.SelectedIndex = homeIndex;
                    }
                    finally
                    {
                        _suppressBranchSwitch = false;
                    }
                }

                return;
            }

            if (_branchId == selected)
            {
                return;
            }

            _branchId = selected;
            _branchName = string.Empty;

            if (_branchId.HasValue)
            {
                try
                {
                    _branchName = await Task.Run(() => ResolveBranchNameAsync());
                }
                catch
                {
                    _branchName = $"Branch {_branchId}";
                }
            }

            ApplyRoleVisibility();          // refreshes the header context text
            ApplyPhase8Visibility();

            // Clear every scoped view immediately so no row of the previous branch stays visible,
            // and repaint so the cleared state is actually drawn.
            ClearTenantGrids();
            ForceContentRepaint();

            // LoadAllModulesAsync serialises itself and lets the newest request win, so a switch
            // made while the previous load is still running can never be overwritten by it.
            await LoadAllModulesAsync();

            ForceContentRepaint();
        }

        // =====================================================================================
        // BI chart visibility lifecycle (ROOT-CAUSE FIX for the disappearing charts)
        // =====================================================================================

        /// <summary>
        /// Set while the BI tab is (or may be) showing charts whose pending measure was dropped,
        /// cleared once the tab is visible again and the update has been re-issued.
        /// </summary>
        private bool _biChartsNeedReissue = true;

        /// <summary>
        /// Why this exists: in LiveCharts 2.0.5 the view-side <c>chart.Series = ...</c> write
        /// only schedules a re-measure (CoreChart.Update -&gt; throttled Task.Run -&gt;
        /// InvokeOnUIThread(Measure)), and Chart.Measure() skips itself via IsRendering() when
        /// the motion canvas has not painted a frame since the previous measure
        /// (Canvas._lastFrameTimestamp == _lastMeasureTimeStamp). While the dashboard tab is
        /// NOT the selected tab, its SkiaSharp control never receives WM_PAINT, so no frame is
        /// drawn and that timestamp never advances: every measure request that fires while the
        /// tab is hidden is silently dropped and nothing ever re-fires it. The charts then show
        /// the pre-refresh/pre-switch content (Tenant B) or the previous branch's data
        /// (Tenant C, whose Current Branch selector is reachable from every tab), or blank
        /// pixels when the canvas was cleared in between. The library ships the counterpart
        /// hook for exactly this situation (CoreMotionCanvas.NotifyPlatformVisible, added for
        /// the Avalonia tab/virtualization bug #1986) but the WinForms view never calls it, so
        /// the platform re-activating the tab must do it: whenever the BI tab is left, flag the
        /// charts as pending; when it becomes the selected tab again, re-issue the update
        /// through the library's own pipeline (CoreChart.Update - the same call the generated
        /// chart code makes on every observed property change) so a measure actually runs while
        /// the tab is visible. This is data-driven (the normal update pipeline), not a repaint
        /// hack: no delays, no recreation, no swallowed exceptions. The flag starts true so the
        /// first show also recovers if the charts were bound while the tab was still hidden.
        /// </summary>
        private void OnDashboardTabSelected()
        {
            if (tabControlMain.SelectedTab != tabDashboard)
            {
                // A hidden chart control gets no WM_PAINT, so any measure fired from here on
                // would be dropped by IsRendering(); remember to re-issue once it is visible.
                _biChartsNeedReissue = true;
                return;
            }

            if (!_biChartsNeedReissue)
            {
                return;
            }

            _biChartsNeedReissue = false;
            tabDashboard.PerformLayout();

            ReissueChartUpdate(chartAttendanceTrend);
            ReissueChartUpdate(chartFeedbackDistribution);
            ReissueChartUpdate(chartMonthlyPayments);
            ReissueChartUpdate(chartActiveVsInactive);
        }

        /// <summary>
        /// Re-issues the throttled measure pipeline of one chart while its tab is actually
        /// visible (IsRendering() can now pass because a frame will paint). Uses the public
        /// CoreChart.Update, the same entry point the library's own property-observer code
        /// calls; the default ChartUpdateParams keep the normal throttled automatic update.
        /// </summary>
        private static void ReissueChartUpdate(Control chart)
        {
            if (chart is LiveChartsCore.SkiaSharpView.WinForms.CartesianChart cartesian)
            {
                cartesian.CoreChart.Update();
            }
            else if (chart is LiveChartsCore.SkiaSharpView.WinForms.PieChart pie)
            {
                pie.CoreChart.Update();
            }
        }

        /// <summary>
        /// Write guard: the currently selected branch must exist and be active before any
        /// new operational record is stamped with it.
        /// </summary>
        private async Task EnsureCurrentBranchActiveAsync(ITenantDataContext db)
        {
            if (!_isBranchAware || !_branchId.HasValue)
            {
                return;
            }

            var branch = await db.Branches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BranchId == _branchId.Value);

            if (branch == null)
            {
                throw new InvalidOperationException("The selected branch no longer exists. Choose another branch.");
            }

            if (!branch.IsActive)
            {
                throw new InvalidOperationException(
                    $"Branch {branch.BranchCode} is inactive and cannot receive new records. Select an active branch.");
            }
        }

        // =====================================================================================
        // Branch CRUD (Admin / Manager)
        // =====================================================================================

        /// <summary>
        /// One row of the Branches grid. Typed (instead of an anonymous projection) so the raw
        /// <see cref="ManagerUserAccountId"/> can stay hidden while the manager's name is shown.
        /// </summary>
        private sealed class BranchRow
        {
            public int BranchId { get; set; }
            public string BranchCode { get; set; } = string.Empty;
            public string BranchName { get; set; } = string.Empty;
            public string? City { get; set; }
            public int? ManagerUserAccountId { get; set; }
            public string Manager { get; set; } = "(none)";
            public string Status { get; set; } = "Active";
            public DateTime? UpdatedAt { get; set; }
        }

        private async Task LoadBranchesGridAsync()
        {
            if (!CanManageBranches || dgvBranches == null)
            {
                return;
            }

            await using var db = await _dataContextFactory.CreateAsync(_companyId, true);

            // The picker is filled from EXISTING Manager user accounts only; no login is created.
            await PopulateBranchManagerPickerAsync(db);

            var branches = await db.Branches.AsNoTracking().OrderBy(b => b.BranchId).ToListAsync();
            var managerAccounts = await db.UserAccounts.AsNoTracking()
                .Where(u => u.Role == Roles.Manager)
                .ToListAsync();

            var rows = branches.Select(b => new BranchRow
            {
                BranchId = b.BranchId,
                BranchCode = b.BranchCode,
                BranchName = b.BranchName,
                City = b.City,
                ManagerUserAccountId = b.ManagerUserAccountId,
                Manager = ResolveManagerDisplayName(managerAccounts, b.ManagerUserAccountId),
                Status = b.IsActive ? "Active" : "Inactive",
                UpdatedAt = b.UpdatedAt
            }).ToList();

            dgvBranches.DataSource = rows;

            // BranchId / ManagerUserAccountId are plumbing, not information for the operator.
            var idColumn = dgvBranches.Columns["BranchId"];
            if (idColumn != null) idColumn.Visible = false;
            var managerIdColumn = dgvBranches.Columns["ManagerUserAccountId"];
            if (managerIdColumn != null) managerIdColumn.Visible = false;
        }

        /// <summary>
        /// The branch list must show the ASSIGNED MANAGER BY NAME (the id is plumbing). A branch
        /// with no manager is valid and displays "(none)"; an account that is no longer active is
        /// shown as "(inactive)" so the assignment is never silently hidden.
        /// </summary>
        private static string ResolveManagerDisplayName(IReadOnlyList<UserAccount> managers, int? managerUserAccountId)
        {
            if (!managerUserAccountId.HasValue)
            {
                return "(none)";
            }

            var manager = managers.FirstOrDefault(u => u.UserAccountId == managerUserAccountId.Value);
            if (manager == null)
            {
                return $"(missing #{managerUserAccountId.Value})";
            }

            return manager.IsActive ? manager.FullName : manager.FullName + " (inactive)";
        }

        private void dgvBranches_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || dgvBranches == null) return;

            if (dgvBranches.Rows[e.RowIndex].DataBoundItem is BranchRow row)
            {
                ApplyBranchRowToInputs(row);
            }
        }

        /// <summary>Copies a branch row into the edit fields and the manager picker.</summary>
        private void ApplyBranchRowToInputs(BranchRow row)
        {
            _selectedBranchId = row.BranchId;
            txtBranchCode!.Text = row.BranchCode;
            txtBranchName!.Text = row.BranchName;
            txtBranchCity!.Text = row.City ?? string.Empty;
            cboBranchStatus!.SelectedItem = row.Status;

            if (cboBranchManager != null)
            {
                var index = row.ManagerUserAccountId.HasValue
                    ? IndexOfManagerItem(row.ManagerUserAccountId.Value)
                    : IndexOfUnassignedItem();
                if (index >= 0) cboBranchManager.SelectedIndex = index;
            }
        }

        /// <summary>Selects the grid row of a branch and mirrors it in the edit fields.</summary>
        private void SelectBranchRowInGrid(int branchId)
        {
            if (dgvBranches == null) return;

            foreach (DataGridViewRow gridRow in dgvBranches.Rows)
            {
                if (gridRow.DataBoundItem is BranchRow row && row.BranchId == branchId)
                {
                    gridRow.Selected = true;
                    var cell = gridRow.Cells.Cast<DataGridViewCell>().FirstOrDefault(c => c.Visible) ?? gridRow.Cells[0];
                    dgvBranches.CurrentCell = cell;
                    ApplyBranchRowToInputs(row);
                    return;
                }
            }
        }

        private int IndexOfManagerItem(int userAccountId)
        {
            if (cboBranchManager == null) return -1;

            for (var i = 0; i < cboBranchManager.Items.Count; i++)
            {
                var item = cboBranchManager.Items[i]?.ToString() ?? string.Empty;
                var head = item.Split(':')[0].Trim();
                if (int.TryParse(head, out var id) && id == userAccountId) return i;
            }

            return -1;
        }

        private int IndexOfUnassignedItem()
        {
            if (cboBranchManager == null) return -1;
            for (var i = 0; i < cboBranchManager.Items.Count; i++)
            {
                if ((cboBranchManager.Items[i]?.ToString() ?? string.Empty) == "(Unassigned)") return i;
            }
            return -1;
        }

        private async Task PopulateBranchManagerPickerAsync(ITenantDataContext db)
        {
            if (cboBranchManager == null) return;

            // Every EXISTING Manager account is selectable, so the same manager can be assigned to
            // one or more branches. Nothing is created here - these are the tenant's own logins.
            _managerCache = await db.UserAccounts.AsNoTracking()
                .Where(u => u.Role == Roles.Manager)
                .OrderBy(u => u.FullName)
                .ToListAsync();

            var previous = ParseManagerPicker(cboBranchManager);

            cboBranchManager.Items.Clear();
            cboBranchManager.Items.Add("(Unassigned)");
            foreach (var m in _managerCache)
            {
                cboBranchManager.Items.Add($"{m.UserAccountId}: {m.FullName}" + (m.IsActive ? string.Empty : " (inactive)"));
            }

            if (cboBranchManager.Items.Count == 0)
            {
                return;
            }

            // Keep the operator's current choice across grid refreshes instead of silently
            // resetting it to "(Unassigned)".
            var index = previous.HasValue ? IndexOfManagerItem(previous.Value) : IndexOfUnassignedItem();
            cboBranchManager.SelectedIndex = index >= 0 ? index : 0;
        }

        private static int? ParseManagerPicker(ComboBox? combo)
        {
            var value = combo?.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(value) || value == "(Unassigned)") return null;

            var head = value.Split(':')[0].Trim();
            return int.TryParse(head, out var id) ? id : null;
        }

        private async Task SaveBranchAsync()
        {
            try
            {
                EnsurePermission(Permission.ManageBranches);

                var code = txtBranchCode!.Text?.Trim().ToUpperInvariant();
                var name = txtBranchName!.Text?.Trim();

                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show("Branch code and branch name are required.");
                    return;
                }

                var managerId = ParseManagerPicker(cboBranchManager);
                var isActive = cboBranchStatus!.SelectedItem?.ToString() != "Inactive";

                var branchId = await SaveBranchCoreAsync(
                    _selectedBranchId, code, name, txtBranchCity!.Text, managerId, isActive);

                var managerLabel = managerId.HasValue
                    ? (_managerCache.FirstOrDefault(u => u.UserAccountId == managerId.Value)?.FullName ?? $"#{managerId}")
                    : "(none)";

                MessageBox.Show($"Branch {code} saved. Manager: {managerLabel}.");

                // Reload and re-select the saved branch so the grid immediately shows the assigned
                // manager's name and the fields keep reflecting what was just saved.
                await LoadBranchesGridAsync();
                SelectBranchRowInGrid(branchId);
                await InitializeCurrentBranchSelectorAsync();
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
        /// Core branch save shared by the UI and the headless verification harness. Creates a new
        /// branch when <paramref name="branchId"/> is null, otherwise updates it. Validates the
        /// unique branch code and that an assigned manager is an EXISTING Manager user account
        /// (never a new login); a null manager is allowed. Returns the branch id.
        /// </summary>
        internal async Task<int> SaveBranchCoreAsync(
            int? branchId,
            string branchCode,
            string branchName,
            string? city,
            int? managerUserAccountId,
            bool isActive)
        {
            EnsurePermission(Permission.ManageBranches);

            var code = branchCode.Trim().ToUpperInvariant();
            var name = branchName.Trim();

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Branch code and branch name are required.");
            }

            await using var db = await _dataContextFactory.CreateAsync(_companyId, true);
            if (!db.IsBranchAware)
            {
                throw new InvalidOperationException("Branch management requires a branch-aware tenant.");
            }

            // Duplicate BranchCode protection (unique within Tenant C).
            var duplicate = await db.Branches.AsNoTracking()
                .AnyAsync(b => b.BranchCode == code && b.BranchId != branchId);
            if (duplicate)
            {
                throw new InvalidOperationException($"A branch with code '{code}' already exists.");
            }

            if (managerUserAccountId.HasValue)
            {
                var isManager = await db.UserAccounts.AsNoTracking()
                    .AnyAsync(u => u.UserAccountId == managerUserAccountId.Value && u.Role == Roles.Manager);
                if (!isManager)
                {
                    throw new InvalidOperationException("The selected account is not a Manager account.");
                }
            }

            Branch branch;
            if (branchId.HasValue)
            {
                branch = await db.Branches.FirstOrDefaultAsync(b => b.BranchId == branchId.Value)
                    ?? throw new InvalidOperationException("Branch not found.");
            }
            else
            {
                branch = new Branch();
                db.Branches.Add(branch);
            }

            branch.BranchCode = code;
            branch.BranchName = name;
            branch.City = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
            branch.ManagerUserAccountId = managerUserAccountId;
            branch.IsActive = isActive;
            branch.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return branch.BranchId;
        }

        private async Task SetSelectedBranchActiveAsync(bool active)
        {
            try
            {
                EnsurePermission(Permission.ManageBranches);

                if (!_selectedBranchId.HasValue)
                {
                    MessageBox.Show("Select a branch first.");
                    return;
                }

                await using var db = await _dataContextFactory.CreateAsync(_companyId, true);

                var branch = await db.Branches.FirstOrDefaultAsync(b => b.BranchId == _selectedBranchId.Value);
                if (branch == null)
                {
                    MessageBox.Show("Branch not found.");
                    return;
                }

                branch.IsActive = active;
                branch.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                MessageBox.Show($"Branch {(active ? "activated" : "deactivated")} (no data was deleted).");

                // If the current working branch just became inactive, drop it immediately;
                // InitializeCurrentBranchSelectorAsync picks a valid replacement and keeps
                // _branchId in step with the selection it restores.
                if (!active && _branchId == branch.BranchId)
                {
                    _branchId = null;
                    cboCurrentBranch!.SelectedIndex = _role == Roles.Admin ? 0 : -1;
                }

                ClearBranchInputs();
                await LoadBranchesGridAsync();
                await InitializeCurrentBranchSelectorAsync();
                ForceContentRepaint();
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

        private void ClearBranchInputs()
        {
            _selectedBranchId = null;
            txtBranchCode!.Text = string.Empty;
            txtBranchName!.Text = string.Empty;
            txtBranchCity!.Text = string.Empty;
            if (cboBranchStatus != null && cboBranchStatus.Items.Count > 0) cboBranchStatus.SelectedIndex = 0;
            // Reset the manager too, so a following "new branch" save cannot silently inherit the
            // manager of the branch that was being edited before.
            if (cboBranchManager != null && cboBranchManager.Items.Count > 0) cboBranchManager.SelectedIndex = 0;
        }

        // =====================================================================================
        // Member transfer (Admin / Manager)
        // =====================================================================================

        /// <summary>
        /// Moves the selected member to the CURRENT branch. Only Members.BranchId changes —
        /// attendance, payments, feedback, complaints, inquiries, subscriptions and retention
        /// records keep the branch where they originally occurred (historical integrity).
        /// </summary>
        private async Task TransferSelectedMemberToCurrentBranchAsync()
        {
            try
            {
                EnsurePermission(Permission.ManageMembers);

                if (!_selectedMemberId.HasValue)
                {
                    MessageBox.Show("Select a member first.");
                    return;
                }

                if (!_branchId.HasValue)
                {
                    MessageBox.Show("Select a target branch in 'Current Branch' first (ALL BRANCHES is not a target).");
                    return;
                }

                await using var db = await _dataContextFactory.CreateAsync(_companyId, true);

                var result = await TransferMemberCoreAsync(db, _selectedMemberId.Value, _branchId.Value);

                MessageBox.Show(
                    result + Environment.NewLine +
                    "Historical attendance, payments, feedback, complaints, inquiries, subscriptions and retention records keep their original branch.");

                await LoadMembersAsync();
                await LoadDashboardAsync();
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
        /// Core member transfer shared by the UI action and the headless verification harness.
        /// Only <c>Members.BranchId</c> is modified; every historical row keeps the branch it
        /// was recorded in. The target branch must exist and be active.
        /// </summary>
        internal async Task<string> TransferMemberCoreAsync(ITenantDataContext db, int memberId, int targetBranchId)
        {
            var targetBranch = await db.Branches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BranchId == targetBranchId)
                ?? throw new InvalidOperationException("The target branch no longer exists.");

            if (!targetBranch.IsActive)
            {
                throw new InvalidOperationException(
                    $"Branch {targetBranch.BranchCode} is inactive and cannot receive transfers.");
            }

            var member = await db.Members.FirstOrDefaultAsync(m => m.MemberId == memberId)
                ?? throw new InvalidOperationException("Member not found.");

            EnsureOwnMember(member.MemberId);

            if (member.BranchId == targetBranchId)
            {
                return $"{member.FullName} already belongs to {targetBranch.BranchCode}.";
            }

            var previousCode = member.BranchId.HasValue
                ? (await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.BranchId == member.BranchId))?.BranchCode ?? "?"
                : "(none)";

            member.BranchId = targetBranchId;
            await db.SaveChangesAsync();

            return $"{member.FullName} transferred: {previousCode} -> {targetBranch.BranchCode}.";
        }

        // =====================================================================================
        // Headless verification hooks (no MessageBox; deterministic)
        // =====================================================================================

        /// <summary>When true, module-load failure dialogs are suppressed (headless runs).</summary>
        internal bool SuppressDialogs;

        /// <summary>
        /// The single write-stamping rule: a selected Current Branch wins; when a tenant-wide
        /// Admin works in ALL BRANCHES the member's own branch is used as a safe fallback so a
        /// branch-aware row is never left un-branched. Every Phase 8 write site calls this.
        /// </summary>
        private int? ResolveWriteBranch(int? memberBranchId) =>
            _isBranchAware ? (_branchId ?? memberBranchId) : memberBranchId;

        internal int? ResolveWriteBranchForTest(int? memberBranchId) => ResolveWriteBranch(memberBranchId);

        /// <summary>Exercises the inactive-branch write guard against the live Current Branch.</summary>
        internal async Task EnsureCurrentBranchActiveForTestAsync()
        {
            await using var db = await _dataContextFactory.CreateAsync(_companyId, true);
            await EnsureCurrentBranchActiveAsync(db);
        }

        /// <summary>
        /// Headless attendance write mirroring the button handler's guard + stamping, used by the
        /// Phase 8 harness to prove branch stamp propagation end-to-end.
        /// </summary>
        internal async Task<string> RecordAttendanceForTestAsync(int memberId, DateTime checkInDate)
        {
            await using var db = await _dataContextFactory.CreateAsync(_companyId, true);

            var member = await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.MemberId == memberId)
                ?? throw new InvalidOperationException("Member not found.");

            EnsureBranchAccess(member.BranchId);
            await EnsureCurrentBranchActiveAsync(db);

            var branch = ResolveWriteBranch(member.BranchId);

            var existing = await db.Attendances.FirstOrDefaultAsync(
                a => a.MemberId == memberId && a.CheckInDate.Date == checkInDate.Date);

            if (existing != null)
            {
                existing.BranchId = branch;
                await db.SaveChangesAsync();
                return $"updated:{branch}";
            }

            var attendance = new Attendance
            {
                MemberId = memberId,
                CheckInDate = checkInDate,
                BranchId = branch
            };
            db.Attendances.Add(attendance);
            await db.SaveChangesAsync();
            return $"inserted:{branch}";
        }

        /// <summary>Populates the selector exactly as Form1_Load does (headless runs).</summary>
        internal async Task InitializeCurrentBranchSelectorForTestAsync() =>
            await InitializeCurrentBranchSelectorAsync();

        internal bool HasCurrentBranchSelector() => cboCurrentBranch != null;

        internal bool HasBranchManagement() => CanManageBranches;

        internal int? CurrentBranchIdForTest => _branchId;

        /// <summary>
        /// The dashboard exactly as the operator would see it: the selected tab, the KPI texts and
        /// the charts' series. Used to prove a Current Branch switch refreshes the dashboard (and
        /// leaves it visible) for every role.
        /// </summary>
        internal string DescribeDashboardStateForTest()
        {
            static string Flat(string s) => (s ?? string.Empty).Replace("\n", "/");

            return $"tab={(tabControlMain.SelectedTab?.Text ?? "(none)")} " +
                   $"dashboardVisible={tabDashboard.Visible} " +
                   $"kpiSize={lblKpiTotalMembers.Width}x{lblKpiTotalMembers.Height} " +
                   $"total='{Flat(lblKpiTotalMembers.Text)}' " +
                   $"attendanceMonth='{Flat(lblKpiAttendanceMonth.Text)}' " +
                   $"payments='{Flat(lblKpiTotalPayments.Text)}' " +
                   $"openComplaints='{Flat(lblKpiOpenComplaints.Text)}' " +
                   $"atRisk='{Flat(lblKpiAtRisk.Text)}' " +
                   $"series=[trend:{chartAttendanceTrend.Series?.Count() ?? 0} " +
                   $"feedback:{chartFeedbackDistribution.Series?.Count() ?? 0} " +
                   $"payments:{chartMonthlyPayments.Series?.Count() ?? 0} " +
                   $"pie:{chartActiveVsInactive.Series?.Count() ?? 0}]";
        }

        /// <summary>
        /// Layout proof that nothing on the dashboard tab covers the KPI cards (the controls stay
        /// visible, not merely present).
        /// </summary>
        internal bool DashboardKpiCardsAreUncoveredForTest()
        {
            var kpi = lblKpiTotalMembers.Bounds;
            return !tabDashboard.Controls.Cast<Control>()
                .Where(c => c != lblKpiTotalMembers && c.Visible)
                .Any(c => c.Bounds.IntersectsWith(kpi));
        }

        /// <summary>Reads the numeric part of a dashboard KPI card's two-line text.</summary>
        internal int KpiValueForTest(string card)
        {
            var label = card switch
            {
                "total" => lblKpiTotalMembers,
                "active" => lblKpiActiveMembers,
                "attendanceMonth" => lblKpiAttendanceMonth,
                "payments" => lblKpiTotalPayments,
                "openComplaints" => lblKpiOpenComplaints,
                "atRisk" => lblKpiAtRisk,
                _ => lblKpiTotalMembers
            };

            var parts = (label.Text ?? string.Empty).Split('\n');
            var raw = parts.Length > 1 ? parts[1] : parts[0];
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var value) ? value : -1;
        }

        /// <summary>Branch list as the operator sees it (manager by name, plumbing columns hidden).</summary>
        internal string DescribeBranchesForTest()
        {
            if (dgvBranches == null)
            {
                return "(no branches grid)";
            }

            var idHidden = dgvBranches.Columns["BranchId"] == null || !dgvBranches.Columns["BranchId"].Visible;
            var fkHidden = dgvBranches.Columns["ManagerUserAccountId"] == null || !dgvBranches.Columns["ManagerUserAccountId"].Visible;

            var rows = dgvBranches.Rows.Cast<DataGridViewRow>()
                .Select(r => r.DataBoundItem as BranchRow)
                .Where(r => r != null)
                .Select(r => $"{r!.BranchId}:{r.BranchCode}={r.Manager}/{r.Status}");

            return $"idColumnHidden={idHidden} fkColumnHidden={fkHidden} managerOptions={cboBranchManager?.Items.Count ?? 0} rows=[{string.Join(" ", rows)}]";
        }

        /// <summary>Assigns a manager to a branch through the real save path (headless).</summary>
        internal async Task<int> AssignBranchManagerForTestAsync(int branchId, int? managerUserAccountId)
        {
            await using var db = await _dataContextFactory.CreateAsync(_companyId, true);
            var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.BranchId == branchId)
                ?? throw new InvalidOperationException("Branch not found.");

            return await SaveBranchCoreAsync(
                branchId, branch.BranchCode, branch.BranchName, branch.City, managerUserAccountId, branch.IsActive);
        }

        /// <summary>Reloads the Branches grid without going through the nav button (headless).</summary>
        internal async Task LoadBranchesGridForTestAsync() => await LoadBranchesGridAsync();

        /// <summary>True when this role owns the dashboard module at all.</summary>
        internal bool HasDashboardForTest => HasPermission(Permission.ViewDashboard);

        /// <summary>
        /// Runs the same full module load a branch switch runs. Headless windows never fire
        /// Form1_Load, so the harness uses this to populate the first view.
        /// </summary>
        internal async Task ReloadAllModulesForTestAsync() => await LoadAllModulesAsync();

        /// <summary>Performs the real logout (no confirmation dialog) and reports the result.</summary>
        internal DialogResult RequestLogoutForTest()
        {
            RequestLogout();
            return DialogResult;
        }

        /// <summary>The session scope left behind by a logout - it must be empty.</summary>
        internal string DescribeLogoutStateForTest() =>
            $"dialogResult={DialogResult} branchId={(_branchId?.ToString() ?? "null")} " +
            $"homeBranchId={(_homeBranchId?.ToString() ?? "null")} memberId={(_memberId?.ToString() ?? "null")} " +
            $"membersCache={_membersCache.Count}";

        /// <summary>
        /// True when the Current Branch selector paints over the signed-in user / role labels.
        /// At normal window size the header is narrow, so this is a real regression guard.
        /// </summary>
        internal bool CurrentBranchSelectorOverlapsHeaderLabelsForTest()
        {
            if (cboCurrentBranch == null || lblCurrentBranch == null)
            {
                return false;
            }

            var selector = Rectangle.Union(cboCurrentBranch.Bounds, lblCurrentBranch.Bounds);
            return selector.IntersectsWith(lblHeaderUserName.Bounds) || selector.IntersectsWith(lblHeaderUserRole.Bounds);
        }

        internal string DescribeHeaderLayoutForTest() =>
            $"header={pnlHeader.Height} branch={lblCurrentBranch?.Bounds.ToString() ?? "(none)"} " +
            $"combo={cboCurrentBranch?.Bounds.ToString() ?? "(none)"} " +
            $"user={lblHeaderUserName.Bounds} role={lblHeaderUserRole.Bounds}";

        internal string DescribeCurrentBranchSelector() =>
            cboCurrentBranch == null
                ? "(no selector)"
                : $"items={cboCurrentBranch.Items.Count}, selected={cboCurrentBranch.SelectedItem ?? "(none)"}";

        /// <summary>
        /// Drives the real Current Branch switch path headlessly (selector change + full
        /// branch-scoped module reload). Returns false when the branch is not selectable.
        /// </summary>
        internal async Task<bool> SwitchCurrentBranchAsync(int? branchId)
        {
            if (cboCurrentBranch == null)
            {
                return false;
            }

            var index = branchId.HasValue ? IndexOfBranchItem(branchId.Value) : 0;
            if (index < 0)
            {
                return false;
            }

            _suppressBranchSwitch = true;
            try
            {
                cboCurrentBranch.SelectedIndex = index;
            }
            finally
            {
                _suppressBranchSwitch = false;
            }

            await OnCurrentBranchChangedAsync();
            return true;
        }

        /// <summary>Headless transfer used by the Phase 8 harness (creates its own context).</summary>
        internal async Task<string> TransferMemberForTestAsync(int memberId, int targetBranchId)
        {
            EnsurePermission(Permission.ManageMembers);

            await using var db = await _dataContextFactory.CreateAsync(_companyId, true);
            return await TransferMemberCoreAsync(db, memberId, targetBranchId);
        }
    }
}
