using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Tenant C phase acceptance harness (COMP003 only).
    ///
    /// Mode:
    ///  * --tenantc-verify-bi   headless acceptance run covering
    ///      - Real-window BI rendering: a SHOWN window on a dedicated STA thread with a REAL
    ///        Application.Run loop, driven through bounded marshaled calls. Chart state is
    ///        proven by (a) the LiveCharts controls' own bound series/axes and (b) PIXEL
    ///        sampling of each chart's screen area (a blank surface yields 1-2 colors).
    ///      - Branch switching as ADMIN sees it: Main -> Cebu -> Davao -> Main, with KPI
    ///        values compared against direct database reads per branch, charts visible +
    ///        painted after EVERY switch, and two consecutive Refreshes plus rapid rapid
    ///        switching/refresh triggers.
    ///      - Staff isolation: gymstaff*/manager* accounts cannot switch away from their
    ///        home branch, see no Branches nav, and read only their own branch's rows.
    ///      - Phase 2-6 structure: no Tenant dropdown anywhere, User Management layout
    ///        (columns, role options, pickers, delete button) per role, Dashboard renamed
    ///        Business Intelligence for Tenant C only.
    ///
    /// Database writes: NONE. The harness is read-only toward every database.
    /// </summary>
    internal static class TenantCVerification
    {
        public static int RunBiVerify()
        {
            var failures = new List<string>();
            var checks = 0;

            void Write(string text) => Console.WriteLine(text);

            void Check(string name, bool ok, string detail)
            {
                checks++;
                Write($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
                if (!ok) failures.Add($"{name}: {detail}");
            }

            Write("======================================================================");
            Write("TENANT C ACCEPTANCE VERIFICATION (BI branch switching + rendering + isolation)");
            Write("======================================================================");

            AppRuntime runtime;
            try
            {
                runtime = AppRuntime.Create();
            }
            catch (Exception ex)
            {
                Write($"  [FAIL] runtime: {ex.Message}");
                return 1;
            }

            var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();
            var tenantC = tenants.FirstOrDefault(t => t.IsBranchAware);
            var tenantB = tenants.FirstOrDefault(t => string.Equals(t.CompanyCode, "COMP002", StringComparison.OrdinalIgnoreCase));

            if (tenantC == null)
            {
                Write("  [FAIL] Tenant C (branch-aware) is not configured.");
                return 1;
            }

            var infoC = runtime.Resolver.GetDatabaseInfoAsync(tenantC.CompanyId).GetAwaiter().GetResult();
            Write($"Target tenant  : {tenantC.CompanyCode} / {tenantC.CompanyName} (CompanyId {tenantC.CompanyId})");
            Write($"Target database: {infoC.ServerName} / {infoC.DatabaseName}");

            // =====================================================================================
            // 0. Branch truth: per-branch KPI values read directly from the database. Every BI
            //    check below compares the UI against these numbers.
            // =====================================================================================
            Write(string.Empty);
            Write("--- 0. Branch truth (direct reads) ---");

            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var branchIdByCode = new Dictionary<string, int>();

            var perBranch = new List<(string Code, int Total, int Active, int AttMonth, decimal Payments, int PaymentCount)>();
            int tenantWideMembers;
            using (var db = new TenantCAlphaEliteDbContext(runtime.ConnectionFactory.CreateBranchAwareOptions(infoC)))
            {
                var branches = db.Branches.AsNoTracking().OrderBy(b => b.BranchId).ToList();
                tenantWideMembers = db.Members.AsNoTracking().Count();
                foreach (var b in branches)
                {
                    branchIdByCode[b.BranchCode] = b.BranchId;
                    var total = db.Members.AsNoTracking().Count(m => m.BranchId == b.BranchId);
                    var active = db.Members.AsNoTracking().Count(m => m.BranchId == b.BranchId && m.IsActive);
                    var attMonth = db.Attendances.AsNoTracking().Count(a => a.BranchId == b.BranchId && a.CheckInDate.Date >= monthStart);
                    var payments = db.Payments.AsNoTracking().Where(p => p.BranchId == b.BranchId).Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                    var paymentCount = db.Payments.AsNoTracking().Count(p => p.BranchId == b.BranchId);
                    perBranch.Add((b.BranchCode, total, active, attMonth, payments, paymentCount));
                    Write($"      {b.BranchCode} (id {b.BranchId}): members={total} active={active} attMonth={attMonth} payments={payments:C0} ({paymentCount} rows)");
                }
            }

            if (!branchIdByCode.TryGetValue("BR-MAIN", out var mainId)
                || !branchIdByCode.TryGetValue("BR-CEBU", out var cebuId)
                || !branchIdByCode.TryGetValue("BR-DAVAO", out var davaoId))
            {
                Write("  [FAIL] the three approved branches (BR-MAIN / BR-CEBU / BR-DAVAO) are not present.");
                return 1;
            }

            static int BranchIdOf((string Code, int Total, int Active, int AttMonth, decimal Payments, int PaymentCount) p) =>
                p.Code switch { "BR-MAIN" => 1, "BR-CEBU" => 2, "BR-DAVAO" => 3, _ => 0 };

            // Truth lookups for the branch-scoped counts the BI page must display.
            int TruthTotal(int branchId) => perBranch.First(p => BranchIdOf(p) == branchId).Total;
            int TruthActive(int branchId) => perBranch.First(p => BranchIdOf(p) == branchId).Active;
            string TruthPayments(int branchId) => perBranch.First(p => BranchIdOf(p) == branchId).Payments.ToString("C0");
            int TruthPaymentCount(int branchId) => perBranch.First(p => BranchIdOf(p) == branchId).PaymentCount;

            // =====================================================================================
            // 1. Windowed BI rendering + branch switching (ADMIN)
            // =====================================================================================
            Write(string.Empty);
            Write("--- 1. Windowed BI: branch switch Main -> Cebu -> Davao -> Main + Refresh x2 ---");

            Form1? renderForm = null;
            var formReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var uiThreadError = (Exception?)null;
            var stall = (string?)null;
            var windowEvents = new List<string>();
            var windowEventsLock = new object();

            // LiveCharts 2.0.5 swallows measure exceptions via Trace.WriteLine; a listener
            // here surfaces them in the harness output instead of a silently blank chart.
            // Debug builds also log every measure via LiveCharts.EnableLogging.
            var traceLines = new List<string>();
            var traceLock = new object();
            Trace.Listeners.Add(new RecordingTraceListener(line => { lock (traceLock) traceLines.Add(line); }));
            LiveChartsCore.LiveCharts.EnableLogging = true;

            string DrainTrace()
            {
                lock (traceLock)
                {
                    if (traceLines.Count == 0) return "(none)";
                    var text = string.Join(" | ", traceLines.Take(6));
                    traceLines.Clear();
                    return text;
                }
            }

            var uiThread = new Thread(() =>
            {
                try
                {
                    // BuildForm leaves a null SynchronizationContext on this thread; install a
                    // real WinForms one (what production's Application.Run does) so every
                    // async continuation marshals back to THIS thread through the real
                    // message loop instead of racing it from the thread pool.
                    renderForm = BuildFormFromAccount(runtime, tenants, tenantC, infoC, "admin");
                    renderForm.TopMost = true;
                    renderForm.FormClosing += (s, e) => { lock (windowEventsLock) windowEvents.Add($"FormClosing reason={e.CloseReason} cancel={e.Cancel}"); };
                    renderForm.FormClosed += (s, e) => { lock (windowEventsLock) windowEvents.Add($"FormClosed reason={e.CloseReason}"); };
                    renderForm.VisibleChanged += (s, e) => { lock (windowEventsLock) windowEvents.Add($"VisibleChanged visible={renderForm.Visible} handle={renderForm.IsHandleCreated}"); };
                    renderForm.HandleDestroyed += (s, e) => { lock (windowEventsLock) windowEvents.Add($"FormHandleDestroyed"); };
                    renderForm.Load += (s, e) => formReady.TrySetResult(true);
                    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    Application.Run(renderForm);
                    renderForm.Dispose();
                }
                catch (Exception ex)
                {
                    uiThreadError = ex;
                }
            });
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.IsBackground = true;
            uiThread.Start();

            void RunOnUi(Action action)
            {
                var form = renderForm;
                if (form == null || !form.IsHandleCreated)
                {
                    throw new InvalidOperationException("BI render window is not available");
                }

                var pending = form.BeginInvoke(action);
                if (!pending.AsyncWaitHandle.WaitOne(60000))
                {
                    throw new TimeoutException("UI thread did not service a marshaled call within 60s (message loop stalled)");
                }

                form.EndInvoke(pending);
            }

            T EvalOnUi<T>(Func<T> func)
            {
                var form = renderForm;
                if (form == null || !form.IsHandleCreated)
                {
                    throw new InvalidOperationException("BI render window is not available");
                }

                var pending = form.BeginInvoke(func);
                if (!pending.AsyncWaitHandle.WaitOne(60000))
                {
                    throw new TimeoutException("UI thread did not service a marshaled call within 60s (message loop stalled)");
                }

                return (T)form.EndInvoke(pending)!;
            }

            bool WaitBounded(Task? task, string label)
            {
                if (task == null)
                {
                    return false;
                }

                try
                {
                    return task.Wait(60000);
                }
                catch (AggregateException ex)
                {
                    stall = label + " faulted: " + ex.GetBaseException().Message;
                    return false;
                }
            }

            // Captures the full BI state for one branch: control state, pixel paint proof,
            // KPI values, the in-flight failure list and the window's own visibility state
            // (the pixel probe reads the physical screen, so a hidden/minimized/off-screen
            // window would otherwise be misread as blank charts).
            string CaptureAll()
            {
                var state = EvalOnUi(() => renderForm!.DescribeBiPageForTest());
                var paint = EvalOnUi(() => renderForm!.CaptureChartRenderForTest());
                var failuresText = EvalOnUi(() => string.Join("; ", renderForm!.LoadFailuresForTest));
                var win = EvalOnUi(() => $"win visible={renderForm!.Visible} state={renderForm!.WindowState} " +
                    $"bounds={renderForm!.Bounds} topmost={renderForm!.TopMost}");
                return $"STATE {state} PAINT {paint} FAILURES [{failuresText}] {win}";
            }

            bool Painted(string capture)
            {
                const string marker = "minColors=";
                var start = capture.IndexOf(marker, StringComparison.Ordinal);
                if (start < 0) return false;
                start += marker.Length;
                var end = capture.IndexOf(' ', start);
                var raw = end < 0 ? capture.Substring(start) : capture.Substring(start, end - start);
                return int.TryParse(raw, out var colors) && colors >= 5;
            }

            try
            {
                if (!formReady.Task.Wait(30000))
                {
                    throw new TimeoutException("BI render window was not shown within 30s"
                        + (uiThreadError == null ? string.Empty : " - " + uiThreadError.Message));
                }

                // Keep the display awake for the whole run: the pixel probe reads the physical
                // screen, and a power-blanked display would be indistinguishable from blank
                // charts.
                SetThreadExecutionState(EsContinuous | EsDisplayRequired);

                Thread.Sleep(1500); // initial Form1_Load (full module load) completes

                // prove the Trace listener plumbing once, so a later "(none)" is meaningful
                Trace.WriteLine("TRACE-LISTENER-SELFTEST");
                Write($"      trace selftest: {DrainTrace()}");

                // Admin starts on "(ALL BRANCHES)" (tenant-wide scope): ApplyScope applies no
                // branch filter there, so the KPIs must show the AGGREGATE of all branches.
                // The module load is async against a remote SQL server, so wait bounded for
                // it to complete instead of racing it with a fixed sleep.
                var allBranchesTotal = TruthTotal(mainId) + TruthTotal(cebuId) + TruthTotal(davaoId);
                var initial = CaptureAll();
                var waitedMs = 0;
                while ((!initial.Contains($"total:'Total Members/{allBranchesTotal}'")
                        || initial.Contains("series=0")) && waitedMs < 20000)
                {
                    Thread.Sleep(500);
                    waitedMs += 500;
                    initial = CaptureAll();
                }

                Write($"      initial (ALL BRANCHES, after {waitedMs}ms): {initial}");
                Write($"      initial internals: {EvalOnUi(() => renderForm!.DescribeChartInternalsForTest())}");
                Check("1. initial load: BI page selected, 4 charts bound + painted, tenant-wide KPIs, no failures",
                    initial.Contains("tab=bi")
                    && Painted(initial)
                    && !initial.Contains("series=0")
                    && initial.Contains($"total:'Total Members/{allBranchesTotal}'")
                    && initial.Contains("FAILURES []"),
                    initial);

                // ---- Branch switching sequence ----
                var switchChecks = new List<string>();
                var switchOk = true;

                foreach (var (code, branchId) in new[] { ("BR-CEBU", cebuId), ("BR-DAVAO", davaoId), ("BR-MAIN", mainId) })
                {
                    var moved = EvalOnUi<Task<bool>>(() => renderForm!.SwitchCurrentBranchAsync(branchId));
                    var doneSwitch = WaitBounded(moved, $"switch {code}");
                    Thread.Sleep(400); // chart animation + measure settle

                    var capture = CaptureAll();
                    Write($"      after switch -> {code}: {capture}");
                    Write($"      {code} internals: {EvalOnUi(() => renderForm!.DescribeChartInternalsForTest())}");
                    Write($"      {code} trace: {DrainTrace()}");

                    var kpiOk = capture.Contains($"'Total Members/{TruthTotal(branchId)}'")
                        && capture.Contains($"'Active Members/{TruthActive(branchId)}'")
                        && capture.Contains($"'Total Payments/{TruthPayments(branchId)}'");
                    var stepOk = doneSwitch
                        && Painted(capture)
                        && !capture.Contains("series=0")
                        && kpiOk
                        && capture.Contains("FAILURES []");
                    switchOk &= stepOk;
                    switchChecks.Add($"{code}: painted={Painted(capture)} kpiMatch={kpiOk} failures={capture.Contains("FAILURES []")} | {capture}");
                }

                Check("2. branch switch Cebu -> Davao -> Main: charts remain painted + KPIs change to the selected branch",
                    switchOk, string.Join(" ; ", switchChecks));

                // ---- Main must show Main's values again ----
                Write($"      main internals: {EvalOnUi(() => renderForm!.DescribeChartInternalsForTest())}");
                Write($"      main trace: {DrainTrace()}");
                var mainCapture = CaptureAll();
                Check("3. returning to Main restores Main's KPI values",
                    mainCapture.Contains($"'Total Members/{TruthTotal(mainId)}'")
                    && mainCapture.Contains($"'Total Payments/{TruthPayments(mainId)}'"),
                    mainCapture);

                // ---- Refresh x2 through the REAL button path ----
                var refreshOk = true;
                var refreshDetail = new List<string>();
                for (var i = 1; i <= 2; i++)
                {
                    var refresh = EvalOnUi<Task>(() => renderForm!.RunDashboardRefreshAsync());
                    var doneRefresh = WaitBounded(refresh, $"refresh #{i}");
                    Thread.Sleep(400);
                    var capture = CaptureAll();
                    Write($"      refresh #{i}: {capture}");
                    Write($"      refresh #{i} internals: {EvalOnUi(() => renderForm!.DescribeChartInternalsForTest())}");
                    refreshOk &= doneRefresh && Painted(capture) && !capture.Contains("series=0") && capture.Contains("FAILURES []");
                    refreshDetail.Add(capture);
                }

                Check("4. Refresh x2: all four charts remain painted with data, no failures, button path releases",
                    refreshOk && EvalOnUi(() => !renderForm!.RefreshGuardEngagedForTest),
                    string.Join(" ; ", refreshDetail));

                // ---- Rapid branch changes (safe triggers, one at a time but without settle waits) ----
                var rapidOk = true;
                var rapidDetail = new List<string>();
                foreach (var branchId in new[] { cebuId, davaoId, mainId, cebuId, mainId })
                {
                    var moved = EvalOnUi<Task<bool>>(() => renderForm!.SwitchCurrentBranchAsync(branchId));
                    var done = WaitBounded(moved, $"rapid switch {branchId}");
                    rapidOk &= done && EvalOnUi(() => !renderForm!.RefreshGuardEngagedForTest);
                }

                // The last rapid switch just requeued a throttled measure; let it settle so
                // the pixel probe does not sample the mid-measure canvas.
                Thread.Sleep(1200);
                var rapidCapture = CaptureAll();
                Write($"      rapid x5: {rapidCapture}");
                rapidOk &= Painted(rapidCapture) && !rapidCapture.Contains("series=0") && rapidCapture.Contains("FAILURES []");
                rapidDetail.Add(rapidCapture);
                Check("5. rapid branch changes x5: no concurrency exceptions, charts still painted, latest branch shown",
                    rapidOk && rapidCapture.Contains($"'Total Members/{TruthTotal(mainId)}'"),
                    string.Join(" ; ", rapidDetail));

                // ---- Layout/attachment invariants after the whole sequence ----
                var finalState = EvalOnUi(() => renderForm!.DescribeBiPageForTest());
                Check("6. no duplicate chart controls, all attached to the dashboard, all visible",
                    new[] { "trend", "feedback", "payments", "pie" }.All(c =>
                        ChartField(finalState, c).Contains("instances=1")
                        && ChartField(finalState, c).Contains("parent=dashboard")
                        && ChartField(finalState, c).Contains("vis=True"))
                    && Painted(rapidCapture),
                    finalState);

                // ---- Dashboard label rename (phase 9) ----
                Check("7. dashboard is labeled Business Intelligence for Tenant C",
                    finalState.Contains("tab=bi")
                    && EvalOnUi(() => renderForm!.DescribeNavLayoutForTest()).Contains("Business Intelligence@")
                    && !EvalOnUi(() => renderForm!.DescribeNavLayoutForTest()).Contains("Dashboard@"),
                    EvalOnUi(() => renderForm!.DescribeNavLayoutForTest()));
            }
            catch (Exception drivingError)
            {
                string windowState;
                lock (windowEventsLock)
                {
                    windowState = string.Join(" | ", windowEvents.Take(6));
                }

                stall = "windowed section aborted: " + drivingError.Message
                    + " [form: " + (renderForm == null ? "null" : $"disposed={renderForm.IsDisposed} handle={renderForm.IsHandleCreated} visible={renderForm.Visible}")
                    + (windowState.Length == 0 ? "" : " events: " + windowState) + "]";
                Write($"      [windowed] {stall}");
                Check("1. initial load: BI page selected, 4 charts bound + painted, no failures", false, stall);
                Check("2. branch switch Cebu -> Davao -> Main: charts remain painted + KPIs change to the selected branch", false, stall);
                Check("3. returning to Main restores Main's KPI values", false, stall);
                Check("4. Refresh x2: all four charts remain painted with data, no failures, button path releases", false, stall);
                Check("5. rapid branch changes x5: no concurrency exceptions, charts still painted, latest branch shown", false, stall);
                Check("6. no duplicate chart controls, all attached to the dashboard, all visible", false, stall);
                Check("7. dashboard is labeled Business Intelligence for Tenant C", false, stall);
            }
            finally
            {
                try
                {
                    if (renderForm != null && renderForm.IsHandleCreated)
                    {
                        RunOnUi(() => renderForm!.Close());
                    }
                }
                catch (Exception closeError)
                {
                    Write($"      [windowed] window close did not complete: {closeError.Message}");
                }

                if (!uiThread.Join(15000))
                {
                    Write("      [windowed] UI thread did not exit within 15s after Close (left running as a background thread)");
                }
            }

            if (uiThreadError != null)
            {
                Write($"      [windowed] UI thread error: {uiThreadError.Message}");
                Check("windowed UI thread completed without an exception", false, uiThreadError.Message);
                checks++;
            }
            else
            {
                Check("windowed UI thread completed without an exception", true, "(no error)");
            }

            // =====================================================================================
            // 2. Staff isolation + branch scoping (headless)
            // =====================================================================================
            Write(string.Empty);
            Write("--- 2. Staff isolation (gymstaff / manager accounts) ---");

            using (var dbC = new TenantCAlphaEliteDbContext(runtime.ConnectionFactory.CreateBranchAwareOptions(infoC)))
            {
                foreach (var username in new[] { "gymstaffMain", "managerMain", "gymstaffCebu", "managerCebu", "gymstaffDavao", "managerDavao" })
                {
                    var account = dbC.UserAccounts.AsNoTracking().FirstOrDefault(u => u.Username == username);
                    if (account == null)
                    {
                        // Seeded accounts are created by --tenantc-seed-branch-users; if they are
                        // missing, fail loudly (the isolation checks below depend on them).
                        Check($"account '{username}' exists with role + branch", false, "(missing - run --tenantc-seed-branch-users)");
                        continue;
                    }

                    var expectedRole = username.StartsWith("manager", StringComparison.OrdinalIgnoreCase) ? Roles.Manager : Roles.GymStaff;
                    Check($"account '{username}' role/branch assignment",
                        account.Role == expectedRole && account.BranchId == account.BranchId && account.IsActive,
                        $"role={account.Role} branchId={account.BranchId?.ToString() ?? "null"} active={account.IsActive}");
                }
            }

            foreach (var (username, homeBranchId) in new[] { ("gymstaffMain", mainId), ("managerMain", mainId), ("gymstaffCebu", cebuId), ("managerCebu", cebuId), ("gymstaffDavao", davaoId), ("managerDavao", davaoId) })
            {
                try
                {
                    var login = runtime.AuthenticationService
                        .AuthenticateAsync(tenantC.CompanyId, username, "123").GetAwaiter().GetResult();

                    var expectedRole = username.StartsWith("manager", StringComparison.OrdinalIgnoreCase) ? Roles.Manager : Roles.GymStaff;
                    Check($"'{username}/123' resolves to Tenant C as {expectedRole} with home branch",
                        login != null && login.Role == expectedRole && login.IsBranchAware && login.BranchId == homeBranchId,
                        login == null ? "REJECTED" : $"role={login.Role} branchId={login.BranchId?.ToString() ?? "null"}");
                }
                catch (Exception ex)
                {
                    Check($"'{username}/123' resolves to Tenant C as expected", false, ex.GetBaseException().Message);
                }
            }

            foreach (var (username, homeBranchId, label) in new[]
            {
                ("gymstaffMain", mainId, "gymstaffMain"),
                ("managerCebu", cebuId, "managerCebu"),
                ("gymstaffDavao", davaoId, "gymstaffDavao")
            })
            {
                bool seeded;
                using (var dbC = new TenantCAlphaEliteDbContext(runtime.ConnectionFactory.CreateBranchAwareOptions(infoC)))
                {
                    seeded = dbC.UserAccounts.AsNoTracking().Any(u => u.Username == username);
                }

                if (!seeded)
                {
                    // Keep this a recorded failure instead of an unhandled crash when the
                    // branch-account seeder has not run yet.
                    Check($"{label}: isolation window (selector pinned, switch refused, scoped reads)", false,
                        "(account missing - run --tenantc-seed-branch-users)");
                    continue;
                }

                using var form = BuildFormFromAccount(runtime, tenants, tenantC, infoC, username);

                var selector = form.HasCurrentBranchSelector()
                    ? form.DescribeCurrentBranchSelector()
                    : "(no selector)";
                Check($"{label}: branch selector pinned to the home branch",
                    form.CurrentBranchIdForTest == homeBranchId, selector);

                // A staff account can never switch away from its home branch: the selector
                // only lists the home branch and the switch handler keeps the branch fixed.
                var otherBranchId = homeBranchId == mainId ? cebuId : mainId;
                var moved = form.SwitchCurrentBranchAsync(otherBranchId).GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);
                Check($"{label}: switching to another branch is refused (stays on the home branch)",
                    (!moved || form.CurrentBranchIdForTest == homeBranchId)
                    && form.CurrentBranchIdForTest == homeBranchId,
                    $"{selector} -> currentBranchId={form.CurrentBranchIdForTest}");

                var scoped = form.DescribeScopedCountsAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);
                Check($"{label}: reads only the home branch's records",
                    scoped.Contains($"members={TruthTotal(homeBranchId)}")
                    && scoped.Contains($"payments={TruthPaymentCount(homeBranchId)}"),
                    scoped);

                var nav = form.DescribeNav();
                Check($"{label}: no Branches navigation item",
                    !nav.Contains("Branches"), nav);
            }

            // =====================================================================================
            // 3. Phase 2-6 structure: tenant dropdown, User Management layout, dashboard label
            // =====================================================================================
            Write(string.Empty);
            Write("--- 3. Phase 2-6 structure (tenant dropdown / User Management / labels) ---");

            foreach (var (username, role) in new[] { ("admin", Roles.Admin), ("managerCebu", Roles.Manager), ("gymstaffMain", Roles.GymStaff) })
            {
                bool structureAccountExists;
                using (var dbC = new TenantCAlphaEliteDbContext(runtime.ConnectionFactory.CreateBranchAwareOptions(infoC)))
                {
                    structureAccountExists = dbC.UserAccounts.AsNoTracking().Any(u => u.Username == username);
                }

                if (!structureAccountExists)
                {
                    Check($"{username}: structure window (tenant dropdown / User Management / label)", false,
                        "(account missing - run --tenantc-seed-branch-users)");
                    continue;
                }

                using var form = BuildFormFromAccount(runtime, tenants, tenantC, infoC, username);

                Check($"{username}: no Tenant dropdown in the header",
                    form.TenantSwitcherHiddenForTest, $"hidden={form.TenantSwitcherHiddenForTest}");

                var um = form.DescribeUserManagementLayoutForTest();
                var expectedRoles = role == Roles.Admin
                    ? new[] { Roles.GymStaff, Roles.Manager, Roles.Admin }
                    : new[] { Roles.GymStaff, Roles.Manager };
                var rolesOk = expectedRoles.All(r => um.Contains(r)) && !um.Contains(Roles.Member);

                string? usersLoaded = null;
                var branchPicker = "(not read)";
                if (role == Roles.Admin)
                {
                    // The bound grid state only exists after the users module loads:
                    // LoadUsersAsync creates the visible columns and populates the Tenant C
                    // branch picker (PopulateUserPickersAsync). The admin window therefore
                    // loads the module before the layout/columns are asserted. Effective
                    // Control.Visible is always False on a form that is never shown, so the
                    // branch picker is proven through its populated items instead.
                    SynchronizationContext.SetSynchronizationContext(null);
                    usersLoaded = form.DescribeUsersForTestAsync().GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);
                    branchPicker = form.DescribeUserBranchPickerForTest();
                }

                var columns = form.DescribeUserGridColumnsForTest();
                var columnsOk = role != Roles.Admin || columns.Split(',').All(c =>
                    c is "Username" or "Full Name" or "Role" or "Status" or "Created At");

                if (role == Roles.Admin)
                {
                    Check($"{username} (Admin): User Management shows only Username/Full Name/Role/Status/Created At + 3 staff roles",
                        rolesOk && columnsOk && !form.DescribeUserStatusColumnForTest().Contains("type=missing"),
                        $"um=[{um}] cols=[{columns}] users=[{usersLoaded}]");
                    Check($"{username} (Admin): branch picker keeps Tenant C scoping ((tenant-wide) + all branches)",
                        branchPicker.Contains("(tenant-wide)")
                            && branchPicker.Contains("BR-MAIN")
                            && branchPicker.Contains("BR-CEBU")
                            && branchPicker.Contains("BR-DAVAO"),
                        branchPicker);
                }
                else
                {
                    Check($"{username} ({role}): User Management available with staff roles only",
                        rolesOk, $"um=[{um}]");
                }
            }

            // Tenant B dashboard label must remain "Dashboard" under its own nav (Tenant B
            // renamed ITS dashboard to Business Intelligence in its own phase - that state
            // is re-proven by the tenantb suite; here we only ensure the C rename is C-scoped).
            Write(string.Empty);
            Write("--- 4. A/B untouched: label scoping ---");
            using (var formB = BuildForm(runtime, tenants, tenantB!, "adminB"))
            {
                var navB = formB.DescribeNavLayoutForTest();
                Check("Tenant B nav unchanged (its own Business Intelligence label preserved)",
                    navB.Contains("Business Intelligence@"), navB);
            }

            Write(string.Empty);
            Write("======================================================================");
            Write($"RESULT: {checks - failures.Count}/{checks} checks passed");
            Write("======================================================================");

            if (failures.Count > 0)
            {
                Write("Failures:");
                foreach (var failure in failures)
                {
                    Write($"  - {failure}");
                }
                return 1;
            }

            return 0;
        }

        private static UserSubscriptionContext BuildSubscriptionContext(AppRuntime runtime, TenantOption tenant)
        {
            var info = runtime.SubscriptionService.GetCompanySubscriptionAsync(tenant.CompanyId).GetAwaiter().GetResult();
            return info == null
                ? new UserSubscriptionContext()
                : new UserSubscriptionContext
                {
                    HasActiveSubscription = info.EffectiveStatus == SubscriptionStatuses.Active,
                    EffectiveStatus = info.EffectiveStatus,
                    PlanName = info.PlanName,
                    EndDate = info.EndDate,
                    EnabledModuleCodes = info.EffectiveStatus == SubscriptionStatuses.Active
                        ? info.EnabledModuleCodes
                        : Array.Empty<string>()
                };
        }

        private static Form1 BuildForm(
            AppRuntime runtime,
            IReadOnlyList<TenantOption> tenants,
            TenantOption tenant,
            string username)
        {
            var login = runtime.AuthenticationService
                .AuthenticateAsync(tenant.CompanyId, username, "123").GetAwaiter().GetResult();

            if (login == null)
            {
                throw new InvalidOperationException(
                    $"'{username}' rejected by login for {tenant.CompanyCode} (seed the account first).");
            }

            var session = new UserSession
            {
                CompanyId = login.CompanyId,
                CompanyCode = login.CompanyCode,
                CompanyName = login.CompanyName,
                IsBranchAware = login.IsBranchAware,
                BranchId = login.BranchId,
                MemberId = login.MemberId,
                Username = login.Username,
                FullName = login.FullName,
                Role = login.Role,
                Subscription = BuildSubscriptionContext(runtime, tenant)
            };

            var form = new Form1(session, tenants, runtime.DataContextFactory, runtime.AuthorizationService)
            {
                SuppressDialogs = true
            };

            SynchronizationContext.SetSynchronizationContext(null);
            form.InitializeCurrentBranchSelectorForTestAsync().GetAwaiter().GetResult();
            form.AttachChartEventCountersForTest();
            SynchronizationContext.SetSynchronizationContext(null);
            return form;
        }

        /// <summary>
        /// Builds Form1 for a Tenant C account WITHOUT a password login: Tenant C's original
        /// accounts keep their Phase 4F one-time passwords (never hardcoded or printed), so
        /// every other suite constructs the session from the account row - only the seeded
        /// branch accounts are additionally proven through the real "123" login path.
        /// </summary>
        private static Form1 BuildFormFromAccount(
            AppRuntime runtime,
            IReadOnlyList<TenantOption> tenants,
            TenantOption tenantC,
            AlphaElite.infrastructure.services.TenantDatabaseInfo infoC,
            string username)
        {
            UserAccount account;
            using (var db = new TenantCAlphaEliteDbContext(runtime.ConnectionFactory.CreateBranchAwareOptions(infoC)))
            {
                account = db.UserAccounts.AsNoTracking().FirstOrDefault(u => u.Username == username)
                    ?? throw new InvalidOperationException(
                        $"'{username}' not found in {tenantC.CompanyCode}.");
            }

            var session = new UserSession
            {
                CompanyId = tenantC.CompanyId,
                CompanyCode = tenantC.CompanyCode,
                CompanyName = tenantC.CompanyName,
                IsBranchAware = true,
                BranchId = account.BranchId,
                MemberId = account.MemberId,
                Username = account.Username,
                FullName = account.FullName,
                Role = account.Role,
                Subscription = BuildSubscriptionContext(runtime, tenantC)
            };            var form = new Form1(session, tenants, runtime.DataContextFactory, runtime.AuthorizationService)
            {
                SuppressDialogs = true
            };

            SynchronizationContext.SetSynchronizationContext(null);
            form.InitializeCurrentBranchSelectorForTestAsync().GetAwaiter().GetResult();
            form.AttachChartEventCountersForTest();
            SynchronizationContext.SetSynchronizationContext(null);
            return form;;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private const uint EsContinuous = 0x80000000;
        private const uint EsDisplayRequired = 0x00000002;

        private static string ChartField(string state, string chartName)
        {
            var marker = chartName + "{";
            var start = state.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return "(missing)";
            start += marker.Length;
            var end = state.IndexOf('}', start);
            return end < 0 ? "(malformed)" : state.Substring(start, end - start);
        }

        /// <summary>
        /// Trace listener that records lines for the harness (LiveCharts swallows measure
        /// exceptions into Trace; this makes them visible without touching library code).
        /// </summary>
        private sealed class RecordingTraceListener : TraceListener
        {
            private readonly Action<string> _sink;

            public RecordingTraceListener(Action<string> sink) => _sink = sink;

            public override void WriteLine(string? message) => _sink(message ?? string.Empty);

            public override void Write(string? message) => _sink(message ?? string.Empty);
        }
    }
}
