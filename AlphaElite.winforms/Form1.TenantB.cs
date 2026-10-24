using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Tenant B phase (COMP002): headless verification hooks for the Business Intelligence
    /// page lifecycle (the Refresh-button path) and the Tenant B user-management rules.
    /// Diagnostics only - no application behavior lives in this file.
    /// </summary>
    public partial class Form1
    {
        /// <summary>Switches to the Business Intelligence page the way the nav button does.</summary>
        internal void SelectBiPageForTest()
        {
            tabControlMain.SelectedTab = tabDashboard;
            SetActiveNavButton(btnNavDashboard);
        }

        /// <summary>Switches to the Members page the way the nav button does.</summary>
        internal void SelectMembersPageForTest()
        {
            tabControlMain.SelectedTab = tabMembers;
            SetActiveNavButton(btnNavMembers);
        }

        /// <summary>
        /// The BI page exactly as the operator's window sees it: the selected tab, dashboard
        /// visibility/scroll layout, and every chart control's visibility, parent attachment,
        /// bounds, handle state, live instance count on the dashboard, series count and axis
        /// label count - plus all eleven KPI card texts. Two captures taken before/after a
        /// Refresh must be byte-identical when the underlying data did not change.
        /// </summary>
        internal string DescribeBiPageForTest()
        {
            static string Flat(string s) => (s ?? string.Empty).Replace("\n", "/");

            string Chart(string name, Control chart, int series, int labels)
            {
                var parent = ReferenceEquals(chart.Parent, tabDashboard)
                    ? "dashboard"
                    : (chart.Parent?.GetType().Name ?? "none");
                var instances = tabDashboard.Controls.Cast<Control>().Count(c => ReferenceEquals(c, chart));
                return $"{name}{{vis={chart.Visible} parent={parent} bounds={chart.Location.X},{chart.Location.Y},{chart.Width}x{chart.Height} " +
                       $"handle={chart.IsHandleCreated} instances={instances} series={series} labels={labels}}}";
            }

            return $"tab={(ReferenceEquals(tabControlMain.SelectedTab, tabDashboard) ? "bi" : tabControlMain.SelectedTab?.Text ?? "(none)")} " +
                   $"dashboardVisible={tabDashboard.Visible} children={tabDashboard.Controls.Count} " +
                   $"autoScroll={tabDashboard.AutoScroll} scrollPos={tabDashboard.AutoScrollPosition.X},{tabDashboard.AutoScrollPosition.Y} " +
                   $"display={tabDashboard.DisplayRectangle.Width}x{tabDashboard.DisplayRectangle.Height} " +
                   Chart("trend", chartAttendanceTrend, chartAttendanceTrend.Series?.Count() ?? 0,
                       chartAttendanceTrend.XAxes?.FirstOrDefault()?.Labels?.Count() ?? 0) + " " +
                   Chart("feedback", chartFeedbackDistribution, chartFeedbackDistribution.Series?.Count() ?? 0,
                       chartFeedbackDistribution.XAxes?.FirstOrDefault()?.Labels?.Count() ?? 0) + " " +
                   Chart("payments", chartMonthlyPayments, chartMonthlyPayments.Series?.Count() ?? 0,
                       chartMonthlyPayments.XAxes?.FirstOrDefault()?.Labels?.Count() ?? 0) + " " +
                   Chart("pie", chartActiveVsInactive, chartActiveVsInactive.Series?.Count() ?? 0, 0) + " " +
                   $"kpis=[total:'{Flat(lblKpiTotalMembers.Text)}' active:'{Flat(lblKpiActiveMembers.Text)}' " +
                   $"inactive:'{Flat(lblKpiInactiveMembers.Text)}' avg:'{Flat(lblKpiAvgRating.Text)}' " +
                   $"today:'{Flat(lblKpiAttendanceToday.Text)}' week:'{Flat(lblKpiAttendanceWeek.Text)}' " +
                   $"pending:'{Flat(lblKpiPendingInquiries.Text)}' month:'{Flat(lblKpiAttendanceMonth.Text)}' " +
                   $"payments:'{Flat(lblKpiTotalPayments.Text)}' complaints:'{Flat(lblKpiOpenComplaints.Text)}' " +
                   $"atRisk:'{Flat(lblKpiAtRisk.Text)}']";
        }

        /// <summary>The role choices the user-management form offers this session.</summary>
        internal string DescribeUserRoleOptionsForTest() =>
            string.Join(",", cboUserRole.Items.Cast<object>().Select(i => i.ToString()));

        /// <summary>
        /// Asks the exact guard the Create/Update user paths call: may the current session
        /// manage an account with <paramref name="targetRole"/> and change it to
        /// <paramref name="newRole"/>? Reports allowed/denied without touching data.
        /// </summary>
        internal string CheckUserManageableForTest(string targetRole, string newRole)
        {
            try
            {
                EnsureUserManageableBySession(targetRole, newRole);
                return "allowed";
            }
            catch (UnauthorizedAccessException ex)
            {
                return "denied: " + ex.Message;
            }
        }

        /// <summary>Visible user-grid columns (their headers, in display order).</summary>
        internal string DescribeUserGridColumnsForTest() =>
            string.Join(",", dgvUsers.Columns.Cast<DataGridViewColumn>()
                .Where(c => c.Visible)
                .Select(c => c.HeaderText));

        /// <summary>
        /// Type, cell-template type, declared value type and the bound value of every Status
        /// cell. The old bool column auto-generated a DataGridViewCheckBoxColumn, whose
        /// formatting pipeline converted the displayed string back to Boolean and threw
        /// FormatException ("Active is not a valid value for Boolean"). A text cell template
        /// with ValueType=string and already-string bound values makes that conversion
        /// impossible by construction.
        /// </summary>
        internal string DescribeUserStatusColumnForTest()
        {
            if (dgvUsers.Columns["Status"] is not { } statusColumn)
            {
                return "type=missing";
            }

            var values = new List<string>();
            foreach (DataGridViewRow row in dgvUsers.Rows)
            {
                if (row.IsNewRow) continue;
                values.Add(row.Cells[statusColumn.Index].Value?.ToString() ?? "(null)");
            }

            var allStrings = values.Count > 0
                && values.All(v => v == "Active" || v == "Inactive");
            var typeText = statusColumn.GetType().Name;
            var templateText = statusColumn.CellTemplate?.GetType().Name ?? "(none)";
            var valueTypeText = statusColumn.ValueType?.Name ?? "(none)";
            var joined = string.Join(",", values);
            return $"type={typeText} template={templateText} valueType={valueTypeText} allStrings={allStrings} values=[{joined}]";
        }

        /// <summary>
        /// State of the user editor controls: selection id, Username / Full Name text, role
        /// reset position and the Status checkbox - used to prove a deleted user's values do
        /// not remain in the editor.
        /// </summary>
        internal string DescribeUserEditorForTest()
        {
            var selectedText = _selectedUserAccountId?.ToString() ?? "none";
            var roleIndexZero = cboUserRole.Items.Count > 0 && cboUserRole.SelectedIndex == 0;
            return $"selectedId={selectedText} username='{txtUserUsername.Text}' " +
                   $"fullname='{txtUserFullName.Text}' roleIndex0={roleIndexZero} active={chkUserActive.Checked}";
        }

        /// <summary>
        /// The Members / Inquiries branch UI as the operator sees it: picker visibility and
        /// whether either grid exposes a visible BranchId column. Tenant A / Tenant B must
        /// report everything branch-related hidden/absent; Tenant C keeps it.
        /// </summary>
        internal string DescribeMemberInquiryBranchUiForTest()
        {
            static bool ColumnVisible(DataGridView grid, string name) =>
                grid.Columns[name] is { } column && column.Visible;

            static string VisibleColumns(DataGridView grid) =>
                string.Join(",", grid.Columns.Cast<DataGridViewColumn>()
                    .Where(c => c.Visible)
                    .Select(c => c.Name));

            const string branchColumn = "BranchId";
            return $"branchLabelVisible={lblMemberBranch.Visible} branchPickerVisible={cboMemberBranch.Visible} " +
                   $"branchPickerEnabled={cboMemberBranch.Enabled} " +
                   $"membersBranchColVisible={ColumnVisible(dgvMembers, branchColumn)} " +
                   $"inquiriesBranchColVisible={ColumnVisible(dgvInquiries, branchColumn)} " +
                   $"membersCols=[{VisibleColumns(dgvMembers)}] inquiriesCols=[{VisibleColumns(dgvInquiries)}]";
        }

        // ---- Complete CRUD drivers (headless; the UI methods run with SuppressDialogs) ----

        /// <summary>Populates the create/update form fields exactly an operator would.</summary>
        internal void SetUserFormForTest(string username, string fullName, string role, bool active = true)
        {
            txtUserUsername.Text = username;
            txtUserFullName.Text = fullName;
            chkUserActive.Checked = active;
            if (cboUserRole.Items.Contains(role)) cboUserRole.SelectedItem = role;
        }

        /// <summary>Points the update/delete action at a specific grid row.</summary>
        internal void SelectUserRowForTest(int accountId) => _selectedUserAccountId = accountId;

        /// <summary>The real create path (dialogs suppressed by SuppressDialogs).</summary>
        internal Task CreateUserForTestAsync() => CreateUserAsync();

        /// <summary>The real update path (dialogs suppressed by SuppressDialogs).</summary>
        internal Task UpdateUserForTestAsync() => UpdateUserAsync();

        /// <summary>The real delete core with an explicit confirmation decision.</summary>
        internal Task<string> DeleteUserCoreForTestAsync(int accountId, bool confirmed) =>
            DeleteUserCoreAsync(accountId, confirmed);

        /// <summary>
        /// Live rendering state of the four BI charts while the form is actually SHOWN:
        /// handle creation, visibility and the number of distinct colors sampled from the
        /// control's real screen area (a flat, undrawn surface yields 1-2 colors; a chart
        /// that painted shows grid/axes/series colors well above that).
        /// </summary>
        internal string CaptureChartRenderForTest()
        {
            int SampleColors(Control chart)
            {
                try
                {
                    var origin = chart.PointToScreen(Point.Empty);
                    using var bmp = new Bitmap(Math.Max(1, chart.Width), Math.Max(1, chart.Height));
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(origin, Point.Empty, bmp.Size);
                    }

                    var seen = new HashSet<int>();
                    for (var y = 0; y < bmp.Height; y += 4)
                    {
                        for (var x = 0; x < bmp.Width; x += 4)
                        {
                            seen.Add(bmp.GetPixel(x, y).ToArgb());
                        }
                    }
                    return seen.Count;
                }
                catch
                {
                    return -1;
                }
            }

            var charts = new (string Name, Control Chart)[]
            {
                ("trend", chartAttendanceTrend),
                ("feedback", chartFeedbackDistribution),
                ("payments", chartMonthlyPayments),
                ("pie", chartActiveVsInactive)
            };

            var allHandles = true;
            var allVisible = true;
            var minColors = int.MaxValue;
            var details = new List<string>();

            foreach (var (name, chart) in charts)
            {
                var colors = SampleColors(chart);
                allHandles &= chart.IsHandleCreated;
                allVisible &= chart.Visible;
                minColors = Math.Min(minColors, colors);
                details.Add($"{name}{{handle={chart.IsHandleCreated} vis={chart.Visible} colors={colors}}}");
            }

            var detailText = string.Join(" ", details);
            return $"allHandlesCreated={allHandles} allVisible={allVisible} minColors={minColors} " +
                   $"detail=[{detailText}]";
        }

        /// <summary>
        /// User-management page layout: Branch / Linked member picker visibility (with
        /// their labels) and the current role options. Tenant B must report all pickers
        /// hidden; Tenant C must keep them visible.
        /// </summary>
        internal string DescribeUserManagementLayoutForTest() =>
            $"branchLabelVisible={lblUserBranch.Visible} branchPickerVisible={cboUserBranch.Visible} " +
            $"memberLabelVisible={lblUserMember.Visible} memberPickerVisible={cboUserMember.Visible} " +
            $"roleOptions=[{DescribeUserRoleOptionsForTest()}]";

        /// <summary>
        /// Branch picker contents (harness-only). Effective Visible is always False for a
        /// form that is never shown, so the Tenant C harness proves the branch picker
        /// through its populated items instead: tenant-wide sessions list "(tenant-wide)"
        /// plus every branch; branch-scoped sessions list only the home branch.
        /// </summary>
        internal string DescribeUserBranchPickerForTest() =>
            $"items=[{string.Join("|", cboUserBranch.Items.Cast<object>().Select(i => i.ToString()))}] " +
            $"selected={cboUserBranch.SelectedItem?.ToString() ?? "(none)"}";

        /// <summary>Harness-only per-chart LiveCharts event counters (Measuring/Started/Finished/HandleCreated/HandleDestroyed).</summary>
        private readonly Dictionary<string, (int Measuring, int Started, int Finished, int HcCreated, int HcDestroyed)> _chartEventCountsForTest = new();

        /// <summary>Subscribes the diagnostic measure-lifecycle counters (harness-only).</summary>
        internal void AttachChartEventCountersForTest()
        {
            var charts = new (string Name, Control Chart)[]
            {
                ("trend", chartAttendanceTrend),
                ("feedback", chartFeedbackDistribution),
                ("payments", chartMonthlyPayments),
                ("pie", chartActiveVsInactive)
            };

            foreach (var (name, chart) in charts)
            {
                var core = chart.GetType().GetProperty("CoreChart")?.GetValue(chart) as LiveChartsCore.Chart;
                if (core == null) continue;
                _chartEventCountsForTest[name] = (0, 0, 0, 0, 0);
                core.Measuring += _ =>
                {
                    var c = _chartEventCountsForTest[name];
                    _chartEventCountsForTest[name] = (c.Measuring + 1, c.Started, c.Finished, c.HcCreated, c.HcDestroyed);
                };
                core.UpdateStarted += _ =>
                {
                    var c = _chartEventCountsForTest[name];
                    _chartEventCountsForTest[name] = (c.Measuring, c.Started + 1, c.Finished, c.HcCreated, c.HcDestroyed);
                };
                core.UpdateFinished += _ =>
                {
                    var c = _chartEventCountsForTest[name];
                    _chartEventCountsForTest[name] = (c.Measuring, c.Started, c.Finished + 1, c.HcCreated, c.HcDestroyed);
                };
                chart.HandleCreated += (_, _) =>
                {
                    var c = _chartEventCountsForTest[name];
                    _chartEventCountsForTest[name] = (c.Measuring, c.Started, c.Finished, c.HcCreated + 1, c.HcDestroyed);
                };
                chart.HandleDestroyed += (_, _) =>
                {
                    var c = _chartEventCountsForTest[name];
                    _chartEventCountsForTest[name] = (c.Measuring, c.Started, c.Finished, c.HcCreated, c.HcDestroyed + 1);
                };
            }
        }

        /// <summary>
        /// Diagnostic (harness-only) probe of each BI chart's LiveCharts internals: load
        /// state, canvas validity/paint-task count, and the frame/measure timestamps whose
        /// equality makes Chart.Measure() skip itself (IsRendering). Read-only reflection;
        /// used to verify the chart root-cause fix, never by production logic.
        /// </summary>
        internal string DescribeChartInternalsForTest()
        {
            var chartType = typeof(LiveChartsCore.Chart);
            var frameField = typeof(LiveChartsCore.Motion.CoreMotionCanvas).GetField("_lastFrameTimestamp",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var measureField = chartType.GetField("_lastMeasureTimeStamp",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            var charts = new (string Name, Control Chart)[]
            {
                ("trend", chartAttendanceTrend),
                ("feedback", chartFeedbackDistribution),
                ("payments", chartMonthlyPayments),
                ("pie", chartActiveVsInactive)
            };

            var parts = new List<string>();
            foreach (var (name, chart) in charts)
            {
                try
                {
                    var core = chart.GetType().GetProperty("CoreChart")?.GetValue(chart) as LiveChartsCore.Chart;
                    if (core == null)
                    {
                        parts.Add($"{name}{{core=null}}");
                        continue;
                    }

                    var frame = frameField?.GetValue(core.Canvas);
                    var measure = measureField?.GetValue(core);
                    var paintTasks = core.Canvas.CountPaintTasks();
                    var geometries = core.Canvas.CountGeometries();
                    var everMeasuredField = chartType.GetField("_everMeasuredElements",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    var everMeasured = (everMeasuredField?.GetValue(core) as IEnumerable<object>)?.Count() ?? -1;
                    var size = core.ControlSize;
                    var coreSeries = core.Series?.Count() ?? -1;
                    var visibleSeries = core.VisibleSeries?.Count() ?? -1;
                    var xAxesVal = core.GetType().GetProperty("XAxes")?.GetValue(core) as IEnumerable<object>;
                    var xAxes = xAxesVal?.Count() ?? -1;
                    _chartEventCountsForTest.TryGetValue(name, out var ev);
                    parts.Add($"{name}{{loaded={core.IsLoaded} canvasValid={core.Canvas.IsValid} " +
                              $"paintTasks={paintTasks} geometries={geometries} everMeasured={everMeasured} " +
                              $"size={size.Width}x{size.Height} coreSeries={coreSeries} visibleSeries={visibleSeries} xAxes={xAxes} " +
                              $"ev={ev.Measuring}/{ev.Started}/{ev.Finished}/{ev.HcCreated}/{ev.HcDestroyed} " +
                              $"frameTs={frame} measureTs={measure} suppressed={Equals(frame, measure)}}}");
                }
                catch (Exception ex)
                {
                    parts.Add($"{name}{{error={ex.GetType().Name}}}");
                }
            }

            return string.Join(" ", parts);
        }

        /// <summary>True while a Refresh-button run is in progress (the re-entrancy guard).</summary>
        internal bool RefreshGuardEngagedForTest => Volatile.Read(ref _refreshInProgress) == 1;

        /// <summary>Refresh button enabled-state + guard state (the guard must always release).</summary>
        internal string DescribeRefreshButtonStateForTest() =>
            $"buttonEnabled={btnRefreshDashboard.Enabled} guardEngaged={Volatile.Read(ref _refreshInProgress) == 1}";

        /// <summary>Loads the users grid as the module load does and reports its roles/options.</summary>
        internal async Task<string> DescribeUsersForTestAsync()
        {
            await LoadUsersAsync();

            static object? RoleOf(DataGridViewRow row)
            {
                var item = row.DataBoundItem;
                return item?.GetType().GetProperty("Role", BindingFlags.Public | BindingFlags.Instance)?.GetValue(item);
            }

            var roles = dgvUsers.Rows.Cast<DataGridViewRow>()
                .Select(r => RoleOf(r)?.ToString() ?? "?")
                .Distinct()
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();

            return $"rows={dgvUsers.Rows.Count} roles=[{string.Join(",", roles)}] roleOptions=[{DescribeUserRoleOptionsForTest()}]";
        }
    }
}
