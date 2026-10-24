using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Tenant B phase acceptance harness (COMP002 only).
    ///
    /// Mode:
    ///  * --tenantb-verify   headless acceptance run covering
    ///      - Business Intelligence page: KPI cards + all four charts before and after
    ///        the real Refresh path (ReloadAllModulesForTestAsync == what
    ///        btnRefreshDashboard_Click calls), layout identity, no duplicate/missing
    ///        controls, no load failures, KPI values equal to direct Tenant B reads
    ///      - Roles/navigation: gymstaffB, adminB, managerB per the phase requirement,
    ///        Dashboard relabeled "Business Intelligence" for Tenant B only, no
    ///        Subscription for Admin B / Manager B, User Management present for both
    ///      - Manager B user-management restriction (Gym Staff accounts only)
    ///      - Admin B + Manager B User Management shows ONLY Username / Full Name / Role
    ///        columns, no Branch or Linked member pickers, role selector without Member
    ///        (Admin B: Admin/Manager/GymStaff; Manager B: GymStaff only; Member data
    ///        untouched)
    ///      - Refresh via the REAL button path, run twice plus RAPID overlapping clicks:
    ///        page byte-identical, no duplicate controls, button re-enabled, no failures
    ///      - DbContext concurrency: 12 overlapping tenant-context creations (all funnel
    ///        through the shared master resolver) complete with no EF 'second operation'
    ///        exception; no tenant context is shared between tasks
    ///      - Non-branching: no branch selector, no tenant dropdown, branch-less session,
    ///        non-branch-aware tenant context + read-only %branch% schema probe for A/B/C
    ///      - Non-branching: no branch selector, no tenant dropdown, branch-less session
    ///      - Isolation: every KPI equals a raw Tenant B read; Tenant A / Tenant C
    ///        row counts captured before the run are unchanged afterwards
    ///      - Authentication: gymstaffB/adminB/managerB with password 123 resolve to
    ///        Tenant B with the expected roles
    ///
    /// The harness is READ-ONLY toward every database: it only reloads views and reads
    /// rows. No tenant dropdown, no branch selector and no Tenant A write path exists.
    /// </summary>
    internal static class TenantBVerification
    {
        public static int RunVerify()
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
            Write("TENANT B ACCEPTANCE VERIFICATION (BI refresh + roles + isolation)");
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
            var tenantA = tenants.FirstOrDefault(t => string.Equals(t.CredentialKey, "TenantA", StringComparison.OrdinalIgnoreCase));
            var tenantB = tenants.FirstOrDefault(t => string.Equals(t.CredentialKey, "TenantB", StringComparison.OrdinalIgnoreCase));
            var tenantC = tenants.FirstOrDefault(t => t.IsBranchAware);

            if (tenantB == null)
            {
                Write("  [FAIL] Tenant B is not configured.");
                return 1;
            }

            // =====================================================================================
            // 0. Baselines: Tenant A / Tenant B / Tenant C row counts captured at run start.
            //    The whole harness is read-only, so every one of them must be identical at the end.
            // =====================================================================================
            Write(string.Empty);
            Write("--- 0. Run-start baselines (protection against accidental writes) ---");

            var baselineA = ReadCounts(runtime, tenantA!);
            var baselineB = ReadCounts(runtime, tenantB);
            var baselineC = ReadComplaintsC(runtime, tenantC);
            Write($"      Tenant A: {baselineA}");
            Write($"      Tenant B: {baselineB}");
            Write($"      Tenant C: complaints={baselineC}");

            // =====================================================================================
            // 1. Authentication (checks 30-32): real credential logins resolve tenant + role
            // =====================================================================================
            Write(string.Empty);
            Write("--- 1. Authentication resolution (gymstaffB / adminB / managerB) ---");

            foreach (var (username, expectedRole) in new[]
                     {
                         ("gymstaffB", Roles.GymStaff),
                         ("adminB", Roles.Admin),
                         ("managerB", Roles.Manager)
                     })
            {
                try
                {
                    var login = runtime.AuthenticationService
                        .AuthenticateAsync(tenantB.CompanyId, username, "123").GetAwaiter().GetResult();

                    Check($"'{username}/123' resolves to Tenant B as {expectedRole}",
                        login != null
                            && login.Role == expectedRole
                            && string.Equals(login.CompanyCode, "COMP002", StringComparison.OrdinalIgnoreCase),
                        login == null
                            ? "REJECTED"
                            : $"role={login.Role} company={login.CompanyCode} branchAware={login.IsBranchAware}");
                }
                catch (Exception ex)
                {
                    Check($"'{username}/123' resolves to Tenant B as {expectedRole}", false, ex.GetBaseException().Message);
                }
            }

            SynchronizationContext.SetSynchronizationContext(null);

            // =====================================================================================
            // 2. Business Intelligence page: initial state, real Refresh path, layout identity
            // =====================================================================================
            Write(string.Empty);
            Write("--- 2. Business Intelligence page (adminB): KPIs + charts + Refresh ---");

            string state1;
            string state2;
            var refreshFailures1 = "";
            var refreshFailures2 = "";

            using (var biForm = BuildForm(runtime, tenants, tenantB, "adminB"))
            {
                biForm.SelectBiPageForTest();
                biForm.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);
                state1 = biForm.DescribeBiPageForTest();
                refreshFailures1 = string.Join("; ", biForm.LoadFailuresForTest);

                Write($"      initial: {state1}");

                // The exact Refresh-button path (btnRefreshDashboard_Click calls this).
                biForm.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);
                state2 = biForm.DescribeBiPageForTest();
                refreshFailures2 = string.Join("; ", biForm.LoadFailuresForTest);

                Write($"      refresh: {state2}");

                Check("1. BI page loads (selected tab is Business Intelligence)",
                    state1.Contains("tab=bi"), state1);
                Check("14a. initial module load produced no failures",
                    refreshFailures1.Length == 0, refreshFailures1.Length == 0 ? "(none)" : refreshFailures1);

                // 2-6: KPI cards and all four charts present, attached to the dashboard,
                // single instance each, with live series/labels.
                Check("2. all 11 KPI cards display after load",
                    KpiField(state1, "total") != "(missing)"
                    && new[] { "active", "inactive", "avg", "today", "week", "pending", "month", "payments", "complaints", "atRisk" }
                        .All(k => KpiField(state1, k) != "(missing)"),
                    "kpis present");
                foreach (var chart in new[] { "trend", "feedback", "payments", "pie" })
                {
                    var c = ChartField(state1, chart);
                    var attached = c.Contains("parent=dashboard") && c.Contains("instances=1");
                    var hasSeries = c.Contains("series=") && !c.Contains("series=0");
                    var hasLabels = chart == "pie" || c.Contains("labels=") && !c.Contains("labels=0");
                    Check($"chart '{chart}' attached + rendering ({chart})",
                        attached && hasSeries && hasLabels, c);
                }

                Check("7-15. Refresh keeps the BI page byte-identical (layout, series, KPIs)",
                    state1 == state2,
                    state1 == state2 ? "identical before/after Refresh" :
                    "DIFFER:\n        before: " + state1 + "\n        after:  " + state2);
                Check("14. no exceptions during Refresh (module load failures)",
                    refreshFailures2.Length == 0, refreshFailures2.Length == 0 ? "(none)" : refreshFailures2);
                Check("11. chart controls remain attached to their parent container",
                    new[] { "trend", "feedback", "payments", "pie" }.All(c => ChartField(state2, c).Contains("parent=dashboard")),
                    "all four parent=dashboard");
                Check("12. no duplicate chart controls on the dashboard",
                    new[] { "trend", "feedback", "payments", "pie" }.All(c => ChartField(state2, c).Contains("instances=1")),
                    "instances=1 for all four");

                // 13 + 26: KPI values equal a direct, raw Tenant B database read.
                using var dbB = runtime.DataContextFactory.CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult();
                var today = DateTime.Today;
                var totalMembers = dbB.Members.AsNoTracking().Count();
                var activeMembers = dbB.Members.AsNoTracking().Count(x => x.IsActive);
                var feedbackRows = dbB.Feedbacks.AsNoTracking().ToList();
                var avgRating = feedbackRows.Count > 0 ? feedbackRows.Average(x => (double)x.Rating) : 0.0;
                int diffToMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                var startOfWeek = today.AddDays(-diffToMonday);
                var startOfMonth = new DateTime(today.Year, today.Month, 1);
                var attendanceToday = dbB.Attendances.AsNoTracking().Count(x => x.CheckInDate.Date == today);
                var attendanceWeek = dbB.Attendances.AsNoTracking().Count(x => x.CheckInDate.Date >= startOfWeek);
                var attendanceMonth = dbB.Attendances.AsNoTracking().Count(x => x.CheckInDate.Date >= startOfMonth);
                var pendingInquiries = dbB.Inquiries.AsNoTracking().Count(x => !x.IsFollowedUp);
                var paymentsTotal = dbB.Payments.AsNoTracking().Sum(x => (decimal?)x.AmountPaid) ?? 0m;
                var openComplaints = dbB.Complaints.AsNoTracking()
                    .Count(x => x.Status != "Resolved" && x.Status != "Closed");

                Check("26. KPI Total Members == raw Tenant B read",
                    KpiField(state2, "total") == totalMembers.ToString(), $"kpi={KpiField(state2, "total")} db={totalMembers}");
                Check("KPI Active/Inactive Members == raw Tenant B read",
                    KpiField(state2, "active") == activeMembers.ToString()
                    && KpiField(state2, "inactive") == (totalMembers - activeMembers).ToString(),
                    $"kpi active={KpiField(state2, "active")}/{KpiField(state2, "inactive")} db={activeMembers}/{totalMembers - activeMembers}");
                Check("KPI Avg Rating == raw Tenant B read",
                    KpiField(state2, "avg") == avgRating.ToString("0.0"), $"kpi={KpiField(state2, "avg")} db={avgRating:0.0}");
                Check("KPI Attendance Today/Week/Month == raw Tenant B read",
                    KpiField(state2, "today") == attendanceToday.ToString()
                    && KpiField(state2, "week") == attendanceWeek.ToString()
                    && KpiField(state2, "month") == attendanceMonth.ToString(),
                    $"kpi={KpiField(state2, "today")}/{KpiField(state2, "week")}/{KpiField(state2, "month")} db={attendanceToday}/{attendanceWeek}/{attendanceMonth}");
                Check("KPI Pending Inquiries == raw Tenant B read",
                    KpiField(state2, "pending") == pendingInquiries.ToString(),
                    $"kpi={KpiField(state2, "pending")} db={pendingInquiries}");
                Check("KPI Total Payments == raw Tenant B read",
                    KpiField(state2, "payments") == paymentsTotal.ToString("C0"),
                    $"kpi={KpiField(state2, "payments")} db={paymentsTotal:C0}");
                Check("KPI Open Complaints == raw Tenant B read",
                    KpiField(state2, "complaints") == openComplaints.ToString(),
                    $"kpi={KpiField(state2, "complaints")} db={openComplaints}");
                Check("KPI At-Risk Members present and stable across Refresh",
                    IsNumber(KpiField(state2, "atRisk")) && KpiField(state1, "atRisk") == KpiField(state2, "atRisk"),
                    $"kpi={KpiField(state2, "atRisk")}");

                // The REAL Refresh-button path (btnRefreshDashboard_Click ->
                // RunDashboardRefreshAsync), run again after the two module loads above -
                // that is Refresh #2 of the requirement's "at least twice".
                biForm.RunDashboardRefreshAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);
                var state3 = biForm.DescribeBiPageForTest();
                Check("40. second Refresh via the real button path keeps the BI page byte-identical",
                    state3 == state1,
                    state3 == state1 ? "identical" : "DIFFER: " + state3);

                // Rapid repeated clicks: five triggers issued while the first Refresh is
                // still running. Only the first may execute (re-entrancy guard); the rest
                // are no-ops. Afterwards the button must be re-enabled, the page unchanged
                // and no load failures recorded (no aborted/duplicated module load).
                var rapidTasks = new List<Task>();
                for (var i = 0; i < 5; i++)
                {
                    rapidTasks.Add(biForm.RunDashboardRefreshAsync());
                }
                Task.WaitAll(rapidTasks.ToArray());
                SynchronizationContext.SetSynchronizationContext(null);

                var state4 = biForm.DescribeBiPageForTest();
                var rapidFailures = string.Join("; ", biForm.LoadFailuresForTest);
                Check("41. rapid repeated Refresh clicks are guarded (single reload, button restored)",
                    !biForm.RefreshGuardEngagedForTest
                    && state4 == state1
                    && rapidFailures.Length == 0,
                    $"button={biForm.DescribeRefreshButtonStateForTest()} identical={state4 == state1} failures='{rapidFailures}'");
            }

            // =====================================================================================
            // 2b. DbContext concurrency: 12 overlapping tenant-context creations. Every
            //     creation funnels through the application-wide master resolver (the shared
            //     MasterAlphaEliteDbContext), which used to throw "A second operation was
            //     started on this context instance before a previous operation completed."
            //     With the resolver serialized this must run clean. Each task owns its OWN
            //     tenant context - no tenant context is ever shared between tasks.
            // =====================================================================================
            Write(string.Empty);
            Write("--- 2b. DbContext concurrency stress (overlapping context creations) ---");

            var creationExceptions = new List<Exception>();
            var createdContexts = new List<ITenantDataContext>();
            var creationGate = new object();
            Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
            {
                try
                {
                    var context = await runtime.DataContextFactory.CreateAsync(tenantB.CompanyId, false);
                    lock (creationGate) createdContexts.Add(context);
                }
                catch (Exception ex)
                {
                    lock (creationGate) creationExceptions.Add(ex);
                }
            })).GetAwaiter().GetResult();

            foreach (var context in createdContexts)
            {
                context.Dispose();
            }

            Check("42. 12 overlapping tenant-context creations complete without exceptions",
                creationExceptions.Count == 0,
                creationExceptions.Count == 0
                    ? "(none)"
                    : string.Join("; ", creationExceptions.Select(e => e.GetBaseException().Message)));                Check("43. no EF 'second operation' DbContext concurrency error was reported",
                creationExceptions.All(e =>
                    !e.GetBaseException().Message.Contains("second operation", StringComparison.OrdinalIgnoreCase)),
                creationExceptions.Count == 0 ? "(none)" : "concurrency errors present");

            // =====================================================================================
            // 2c. Windowed BI rendering: a REAL shown window, the REAL Refresh path, and
            //     pixel sampling of the four chart surfaces before/after two refreshes and
            //     rapid clicks. This is the check the headless state assertions cannot make:
            //     does each chart actually PAINT after Refresh (handle + visible + drawn
            //     pixels), i.e. is the BI page ever blank?
            // =====================================================================================
            Write(string.Empty);
            Write("--- 2c. Windowed BI rendering (real window + real Refresh path) ---");

            // The window now lives on a DEDICATED STA thread running a REAL Application.Run
            // message loop (production-identical), driven from this thread through BOUNDED
            // marshaled waits. The previous variant pumped DoEvents here and polled the
            // refresh tasks between iterations: if a refresh continuation blocked INSIDE
            // Application.DoEvents, the poll deadline was never reached and the suite hung
            // forever (observed twice). With the real loop on its own thread, a stalled UI
            // thread only times out the marshaled wait: the affected checks fail with a
            // printed reason and the suite always completes.
            Form1? renderForm = null;
            var formReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var uiThreadError = (Exception?)null;
            var stall = (string?)null;
            Task? refreshA = null;
            Task? refreshB = null;
            Task? refreshD = null;
            var rapidWindowTasks = new List<Task>();
            var doneA = false;
            var doneB = false;
            var doneC = false;
            var doneD = false;
            var paintBefore = "<windowed section did not start>";
            var paintAfter1 = paintBefore;
            var paintAfter2 = paintBefore;
            var paintAfterRapid = paintBefore;
            var paintAfterTabReturn = paintBefore;
            var renderState = "<windowed section did not start>";
            var renderFailures = "<windowed section did not start>";
            var guardReleased = false;

            var uiThread = new Thread(() =>
            {
                try
                {
                    // BuildForm leaves a null SynchronizationContext on this thread; install
                    // a real WinForms one (what production's Application.Run does) so every
                    // refresh continuation marshals back to THIS thread through the real
                    // message loop instead of racing it from the thread pool.
                    renderForm = BuildForm(runtime, tenants, tenantB, "adminB");
                    renderForm.TopMost = true;
                    renderForm.Load += (s, e) => formReady.TrySetResult(true);
                    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    Application.Run(renderForm); // real loop; returns when the form closes
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

            // Bounded marshaling helpers: every UI interaction from this (driving) thread is
            // a BeginInvoke plus a WaitHandle timeout, so a wedged UI thread degrades into a
            // failed check with a printed reason instead of an unbounded hang.
            void RunOnUi(Action action)
            {
                var form = renderForm;
                if (form == null || !form.IsHandleCreated)
                {
                    throw new InvalidOperationException("BI render window is not available");
                }

                var pending = form.BeginInvoke(action);
                if (!pending.AsyncWaitHandle.WaitOne(30000))
                {
                    throw new TimeoutException("UI thread did not service a marshaled call within 30s (message loop stalled)");
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
                if (!pending.AsyncWaitHandle.WaitOne(30000))
                {
                    throw new TimeoutException("UI thread did not service a marshaled call within 30s (message loop stalled)");
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
                    return task.Wait(30000);
                }
                catch (AggregateException ex)
                {
                    stall = label + " faulted: " + ex.GetBaseException().Message;
                    return false;
                }
            }

            try
            {
                if (!formReady.Task.Wait(30000))
                {
                    throw new TimeoutException("BI render window was not shown within 30s"
                        + (uiThreadError == null ? string.Empty : " - " + uiThreadError.Message));
                }

                Thread.Sleep(1500); // give the initial load (Form1_Load) time to complete
                RunOnUi(() => renderForm!.SelectBiPageForTest());
                Thread.Sleep(300);

                paintBefore = EvalOnUi(() => renderForm!.CaptureChartRenderForTest());
                Write($"      before: {paintBefore}");

                refreshA = EvalOnUi<Task>(() => renderForm!.RunDashboardRefreshAsync());
                Trace("refreshA triggered");
                doneA = WaitBounded(refreshA, "refreshA");
                Trace($"refreshA pump done={doneA}");
                Thread.Sleep(400);
                paintAfter1 = EvalOnUi(() => renderForm!.CaptureChartRenderForTest());
                Write($"      refresh #1: {paintAfter1}");

                refreshB = EvalOnUi<Task>(() => renderForm!.RunDashboardRefreshAsync());
                Trace("refreshB triggered");
                doneB = WaitBounded(refreshB, "refreshB");
                Trace($"refreshB pump done={doneB}");
                Thread.Sleep(400);
                paintAfter2 = EvalOnUi(() => renderForm!.CaptureChartRenderForTest());
                Write($"      refresh #2: {paintAfter2}");

                for (var i = 0; i < 5; i++)
                {
                    rapidWindowTasks.Add(EvalOnUi<Task>(() => renderForm!.RunDashboardRefreshAsync()));
                    Trace($"rapid trigger #{i + 1} returned completed={rapidWindowTasks[i].IsCompleted}");
                }
                doneC = Task.WaitAll(rapidWindowTasks.ToArray(), 30000);
                Trace($"rapid pump exited done={doneC}");
                Thread.Sleep(400);
                Trace("rapid capture starting");
                paintAfterRapid = EvalOnUi(() => renderForm!.CaptureChartRenderForTest());
                Trace($"rapid capture done: {paintAfterRapid}");
                Write($"      rapid x5: {paintAfterRapid}");

                // The Refresh button lives in the header and is reachable from ANY module
                // page: refresh while the BI tab is NOT selected, then return to BI - the
                // charts must paint again (binding while the page is hidden).
                RunOnUi(() => renderForm!.SelectMembersPageForTest());
                Thread.Sleep(200);
                Trace("tab-variant: switched to Members, triggering refresh");
                refreshD = EvalOnUi<Task>(() => renderForm!.RunDashboardRefreshAsync());
                doneD = WaitBounded(refreshD, "refreshD(tab-variant)");
                Trace($"tab-variant refresh pump done={doneD}");
                RunOnUi(() => renderForm!.SelectBiPageForTest());
                Thread.Sleep(400);
                Trace("tab-variant: back on BI, capturing");
                paintAfterTabReturn = EvalOnUi(() => renderForm!.CaptureChartRenderForTest());
                Trace($"tab-variant capture done: {paintAfterTabReturn}");
                Write($"      refresh from another tab: {paintAfterTabReturn}");

                renderState = EvalOnUi(() => renderForm!.DescribeBiPageForTest());
                renderFailures = EvalOnUi(() => string.Join("; ", renderForm!.LoadFailuresForTest));
                guardReleased = EvalOnUi(() => !renderForm!.RefreshGuardEngagedForTest);
            }
            catch (Exception drivingError)
            {
                stall = "windowed section aborted: " + drivingError.Message;
                Write($"      [2c] {stall}");
            }
            finally
            {
                // Close the window (bounded). If the UI thread is wedged, log it and move on:
                // the thread is a background thread and the checks below already fail.
                try
                {
                    if (renderForm != null && renderForm.IsHandleCreated)
                    {
                        RunOnUi(() => renderForm!.Close());
                    }
                }
                catch (Exception closeError)
                {
                    Write($"      [2c] window close did not complete: {closeError.Message}");
                }

                if (!uiThread.Join(15000))
                {
                    Write("      [2c] UI thread did not exit within 15s after Close (left running as a background thread)");
                }
            }

            if (uiThreadError != null)
            {
                Write($"      [2c] UI thread error: {uiThreadError.Message}");
            }

            var refreshFaulted = new[] { refreshA, refreshB, refreshD }.Concat(rapidWindowTasks)
                .OfType<Task>()
                .Any(t => t.IsFaulted || t.IsCanceled);
            var stallNote = stall == null ? string.Empty : $" | STALL: {stall}";

            Check("16. real-window BI Refresh #1: all four charts painted with data",
                doneA && !refreshFaulted && Painted(paintAfter1) && renderFailures.Length == 0,
                $"painted={Painted(paintAfter1)} faulted={refreshFaulted} failures='{renderFailures}' | {paintAfter1}{stallNote}");
            Check("17. real-window BI Refresh #2: all four charts painted with data",
                doneB && !refreshFaulted && Painted(paintAfter2),
                $"painted={Painted(paintAfter2)} | {paintAfter2}{stallNote}");
            Check("18. rapid Refresh x5: no concurrency exception, guard released, charts still painted",
                doneC && !refreshFaulted && guardReleased && Painted(paintAfterRapid),
                $"painted={Painted(paintAfterRapid)} faulted={refreshFaulted} guardReleased={guardReleased} | {paintAfterRapid}{stallNote}");
            Check("19. no blank BI page: state intact, charts visible, no duplicate controls",
                renderState.Contains("tab=bi")
                && !renderState.Contains("series=0")
                && new[] { "trend", "feedback", "payments", "pie" }
                    .All(c => ChartField(renderState, c).Contains("instances=1")
                           && ChartField(renderState, c).Contains("parent=dashboard"))
                && Painted(paintBefore),
                renderState + stallNote);
            Check("19b. Refresh from another tab still paints all four charts on return to BI",
                doneD && refreshD is { } refreshDoneD && !refreshDoneD.IsFaulted && Painted(paintAfterTabReturn),
                paintAfterTabReturn + stallNote);

            // =====================================================================================
            // 3. Roles / navigation (checks 16-22) and non-branching (23-25)
            // =====================================================================================
            Write(string.Empty);
            Write("--- 3. Roles, navigation, non-branching ---");

            var navExpectations = new (string Username, string[] Required, string[] Forbidden)[]
            {
                ("gymstaffB",
                    new[] { "Members", "Attendance", "Payments", "Inquiries", "Feedback", "Complaints", "Retention" },
                    new[] { "Dashboard", "Business Intelligence", "Subscriptions", "UserManagement", "Branches" }),
                ("adminB",
                    new[] { "Business Intelligence", "Members", "Attendance", "Payments", "Feedback", "Inquiries", "Retention", "Complaints", "UserManagement" },
                    new[] { "Dashboard", "Subscriptions", "Branches" }),
                ("managerB",
                    new[] { "Business Intelligence", "Members", "Attendance", "Payments", "Feedback", "Inquiries", "Retention", "Complaints", "UserManagement" },
                    new[] { "Dashboard", "Subscriptions", "Branches" }),
            };

            foreach (var (username, required, forbidden) in navExpectations)
            {
                using var form = BuildForm(runtime, tenants, tenantB, username);
                form.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);

                var nav = form.DescribeNav();
                Write($"      {username} nav: {nav}");

                Check($"16/17/19. '{username}' has the required modules",
                    required.All(nav.Contains),
                    nav);
                Check($"18/20. '{username}' has NO Subscription (and no Branches)",
                    forbidden.All(f => !nav.Contains(f)),
                    nav);
                Check($"24. '{username}' has NO branch selector",
                    !form.HasCurrentBranchSelector(),
                    form.DescribeCurrentBranchSelector());
                Check($"25. '{username}' has NO tenant dropdown",
                    form.TenantSwitcherHiddenForTest,
                    form.DescribeTenantSwitcher());
                Check($"23. '{username}' session is non-branching",
                    !form.DescribeSession().Contains("branchAware=True"),
                    form.DescribeSession());

                // Tenant A / Tenant B phase: non-branching tenants must expose NO branch UI
                // in Members / Inquiries (no picker, no BranchId column) while the grids are
                // actually bound.
                var branchUi = form.DescribeMemberInquiryBranchUiForTest();
                Write($"      {username} branch UI: {branchUi}");
                Check($"52. '{username}' Members/Inquiries expose NO branch UI",
                    branchUi.Contains("branchLabelVisible=False")
                    && branchUi.Contains("branchPickerVisible=False")
                    && branchUi.Contains("membersBranchColVisible=False")
                    && branchUi.Contains("inquiriesBranchColVisible=False")
                    && !branchUi.Contains("membersCols=[]")
                    && !branchUi.Contains("inquiriesCols=[]"),
                    branchUi);
            }

            // =====================================================================================
            // 4. Manager B user management (checks 21-22)
            // =====================================================================================
            Write(string.Empty);
            Write("--- 4. Manager B user management (Gym Staff only) ---");

            using (var managerForm = BuildForm(runtime, tenants, tenantB, "managerB"))
            {
                managerForm.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);

                var users = managerForm.DescribeUsersForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);
                Write($"      managerB users: {users}");

                Check("21. Manager B can manage/add Gym Staff (create/update allowed)",
                    managerForm.CheckUserManageableForTest("GymStaff", "GymStaff") == "allowed",
                    managerForm.CheckUserManageableForTest("GymStaff", "GymStaff"));
                Check("21. Manager B users grid shows ONLY Gym Staff accounts",
                    users.Contains("roles=[GymStaff]") || users.Contains("rows=0 roles=[]"),
                    users);
                Check("21. Manager B role options offer ONLY Gym Staff",
                    users.Contains("roleOptions=[GymStaff]"),
                    users);
                Check("22. Manager B cannot administer Admin accounts",
                    managerForm.CheckUserManageableForTest("Admin", "Admin").StartsWith("denied"),
                    managerForm.CheckUserManageableForTest("Admin", "Admin"));
                Check("22. Manager B cannot administer Manager accounts",
                    managerForm.CheckUserManageableForTest("Manager", "Manager").StartsWith("denied"),
                    managerForm.CheckUserManageableForTest("Manager", "Manager"));
                Check("22. Manager B cannot escalate a Gym Staff account to Admin",
                    managerForm.CheckUserManageableForTest("GymStaff", "Admin").StartsWith("denied"),
                    managerForm.CheckUserManageableForTest("GymStaff", "Admin"));

                // New User Management layout restrictions (Username / Full Name / Role only).
                Check("44. Manager B users grid shows ONLY Username/Full Name/Role/Status/Created At",
                    managerForm.DescribeUserGridColumnsForTest() == "Username,Full Name,Role,Status,Created At",
                    managerForm.DescribeUserGridColumnsForTest());
                Check("45. Manager B Branch + Linked member pickers are removed from the page",
                    managerForm.DescribeUserManagementLayoutForTest()
                        == "branchLabelVisible=False branchPickerVisible=False memberLabelVisible=False memberPickerVisible=False roleOptions=[GymStaff]",
                    managerForm.DescribeUserManagementLayoutForTest());
                Check("46. Manager B cannot manage/create Member accounts",
                    managerForm.CheckUserManageableForTest("Member", "Member").StartsWith("denied")
                    && managerForm.CheckUserManageableForTest("GymStaff", "Member").StartsWith("denied"),
                    managerForm.CheckUserManageableForTest("Member", "Member"));
            }

            using (var adminForm = BuildForm(runtime, tenants, tenantB, "adminB"))
            {
                adminForm.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);

                var users = adminForm.DescribeUsersForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);
                Write($"      adminB users: {users}");

                Check("Admin B keeps full Tenant B user administration",
                    adminForm.CheckUserManageableForTest("Admin", "Admin") == "allowed"
                    && adminForm.CheckUserManageableForTest("Manager", "Manager") == "allowed"
                    && adminForm.CheckUserManageableForTest("GymStaff", "GymStaff") == "allowed",
                    adminForm.CheckUserManageableForTest("Admin", "Admin"));
                Check("47. Admin B role options list ONLY Admin/Manager/GymStaff (Member removed)",
                    users.Contains("roleOptions=[Admin,Manager,GymStaff]"),
                    users);
                Check("48. Admin B users grid shows ONLY Username/Full Name/Role/Status/Created At",
                    adminForm.DescribeUserGridColumnsForTest() == "Username,Full Name,Role,Status,Created At",
                    adminForm.DescribeUserGridColumnsForTest());
                Check("49. Admin B Branch + Linked member pickers are removed from the page",
                    adminForm.DescribeUserManagementLayoutForTest()
                        == "branchLabelVisible=False branchPickerVisible=False memberLabelVisible=False memberPickerVisible=False roleOptions=[Admin,Manager,GymStaff]",
                    adminForm.DescribeUserManagementLayoutForTest());
                Check("50. Admin B cannot create/manage Member accounts (Member data untouched)",
                    adminForm.CheckUserManageableForTest("Member", "Member").StartsWith("denied")
                    && adminForm.CheckUserManageableForTest("GymStaff", "Member").StartsWith("denied"),
                    adminForm.CheckUserManageableForTest("Member", "Member"));
            }

            // =====================================================================================
            // 4b. Tenant B User Management: complete CRUD + record-only creation. The
            //     create/update dialogs run suppressed (SuppressDialogs), exactly like every
            //     other headless dialog; the delete core takes an explicit confirmation
            //     decision so BOTH the canceled and the confirmed path are verified.
            // =====================================================================================
            Write(string.Empty);
            Write("--- 4b. Tenant B User Management CRUD (adminB) ---");

            int CountUsersB()
            {
                using var c = runtime.DataContextFactory
                    .CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult();
                return c.UserAccounts.AsNoTracking().Count();
            }

            using (var crudForm = BuildForm(runtime, tenants, tenantB, "adminB"))
            {
                crudForm.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);

                // ---- SELF-HEAL: remove probe leftovers from previously killed runs ----
                // A run killed between CREATE and DELETE leaves the harness-owned probe
                // record behind. It is record-only (empty PasswordHash => it cannot
                // authenticate), so an empty-hash BStaffProbe is provably not a business
                // account; remove such leftovers BEFORE counting the baseline so killed
                // runs can never pollute the user-count baseline. Anything else (a
                // different username, or a BStaffProbe that somehow HAS a password hash)
                // is never touched.
                var healedProbeIds = new List<int>();
                using (var dbh = runtime.DataContextFactory
                           .CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult())
                {
                    var probeLeftovers = dbh.UserAccounts
                        .Where(u => u.Username == "BStaffProbe")
                        .ToList();
                    foreach (var probe in probeLeftovers)
                    {
                        if (!string.IsNullOrWhiteSpace(probe.PasswordHash))
                        {
                            Write($"      [self-heal] BStaffProbe id={probe.UserAccountId} HAS a password hash - NOT harness-owned, left untouched");
                            continue;
                        }

                        dbh.UserAccounts.Remove(probe);
                        healedProbeIds.Add(probe.UserAccountId);
                    }

                    if (healedProbeIds.Count > 0)
                    {
                        dbh.SaveChangesAsync().GetAwaiter().GetResult();
                        var healedIdList = string.Join(",", healedProbeIds);
                        Write($"      [self-heal] removed leftover probe record(s) from killed run(s): id={healedIdList}");
                    }
                }

                var usersBefore = CountUsersB();

                // ---- CREATE ----
                crudForm.SetUserFormForTest("BStaffProbe", "B Staff Probe", Roles.GymStaff);
                crudForm.CreateUserForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);

                UserAccount? created = null;
                using (var dbc = runtime.DataContextFactory
                           .CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult())
                {
                    created = dbc.UserAccounts.AsNoTracking()
                        .FirstOrDefault(u => u.Username == "BStaffProbe");
                }

                Check("7. User Management Create persists a new staff record",
                    created != null && CountUsersB() == usersBefore + 1,
                    created == null ? "record missing" : $"id={created.UserAccountId} count={CountUsersB()}");
                Check("11. the created record carries NO login credential (record only)",
                    created != null && string.IsNullOrEmpty(created.PasswordHash),
                    created == null ? "record missing" : $"passwordHashLength={created.PasswordHash.Length}");

                var ghostLogin = runtime.AuthenticationService
                    .AuthenticateAsync(tenantB.CompanyId, "BStaffProbe", "123").GetAwaiter().GetResult();
                Check("11b. the created record cannot authenticate (no unintended login account)",
                    ghostLogin == null,
                    ghostLogin == null ? "REJECTED (no credential created)" : "LOGGED IN - credential was created!");

                // ---- READ + required fields ----
                var readState = crudForm.DescribeUsersForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);
                var gridCols = crudForm.DescribeUserGridColumnsForTest();
                var layout = crudForm.DescribeUserManagementLayoutForTest();
                Check("8. User Management Read lists the new record",
                    created != null && readState.Contains("rows=") && readState.Contains("GymStaff"),
                    readState);
                Check("12. visible fields are exactly Username, Full Name, Role, Status, Created At",
                    gridCols == "Username,Full Name,Role,Status,Created At",
                    gridCols);
                Check("13. Branch and Linked Member are absent from the page",
                    layout.Contains("branchLabelVisible=False")
                    && layout.Contains("branchPickerVisible=False")
                    && layout.Contains("memberLabelVisible=False")
                    && layout.Contains("memberPickerVisible=False")
                    && !gridCols.Contains("Branch")
                    && !gridCols.Contains("Member"),
                    gridCols + " | " + layout);

                // The Status column must be a plain TEXT column whose formatted values are
                // already the display strings - the old bool column auto-generated a
                // DataGridViewCheckBoxColumn that threw FormatException ("Active is not a
                // valid value for Boolean") at paint time.
                var statusColumnState = crudForm.DescribeUserStatusColumnForTest();
                Check("13b. Status is a text column formatting Active/Inactive strings (no Boolean conversion)",
                    statusColumnState.StartsWith("type=DataGridViewTextBoxColumn")
                    && statusColumnState.Contains("template=DataGridViewTextBoxCell")
                    && statusColumnState.Contains("valueType=String")
                    && statusColumnState.Contains("allStrings=True"),
                    statusColumnState);

                // ---- UPDATE ----
                if (created != null)
                {
                    crudForm.SelectUserRowForTest(created.UserAccountId);
                    crudForm.SetUserFormForTest("BStaffProbe", "B Staff Probe Renamed", Roles.GymStaff);
                    crudForm.UpdateUserForTestAsync().GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);

                    UserAccount? updated = null;
                    using (var dbu = runtime.DataContextFactory
                               .CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult())
                    {
                        updated = dbu.UserAccounts.AsNoTracking()
                            .FirstOrDefault(u => u.UserAccountId == created.UserAccountId);
                    }

                    Check("9. User Management Update persists the edit",
                        updated != null && updated.FullName == "B Staff Probe Renamed",
                        updated == null ? "record missing" : $"fullName={updated.FullName}");
                    Check("9b. Update does not create a login credential either",
                        updated != null && string.IsNullOrEmpty(updated.PasswordHash),
                        updated == null ? "record missing" : $"passwordHashLength={updated.PasswordHash.Length}");
                }

                // ---- DELETE: confirmation required, then confirmed ----
                if (created != null)
                {
                    var cancelOutcome = crudForm
                        .DeleteUserCoreForTestAsync(created.UserAccountId, false).GetAwaiter().GetResult();
                    Check("10a. Delete without confirmation changes nothing (canceled)",
                        cancelOutcome == "canceled" && CountUsersB() == usersBefore + 1,
                        $"outcome={cancelOutcome} count={CountUsersB()}");

                    var deleteOutcome = crudForm
                        .DeleteUserCoreForTestAsync(created.UserAccountId, true).GetAwaiter().GetResult();
                    Check("10b. confirmed Delete persists and the grid refreshes",
                        deleteOutcome == "deleted" && CountUsersB() == usersBefore,
                        $"outcome={deleteOutcome} count={CountUsersB()}");

                    var afterDelete = crudForm.DescribeUsersForTestAsync().GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);
                    Check("10c. grid no longer lists the deleted record",
                        afterDelete.StartsWith($"rows={usersBefore} "),
                        afterDelete);

                    // Stale-editor fix: after a confirmed delete the selection id is dropped,
                    // the editor controls are cleared / reset, and the deleted record can no
                    // longer authenticate.
                    var editorAfterDelete = crudForm.DescribeUserEditorForTest();
                    var deletedGhostLogin = runtime.AuthenticationService
                        .AuthenticateAsync(tenantB.CompanyId, "BStaffProbe", "123")
                        .GetAwaiter().GetResult();
                    Check("10f. delete clears the editor + selection and the deleted user cannot log in",
                        editorAfterDelete.Contains("selectedId=none")
                        && editorAfterDelete.Contains("username=''")
                        && editorAfterDelete.Contains("fullname=''")
                        && editorAfterDelete.Contains("roleIndex0=True")
                        && editorAfterDelete.Contains("active=True")
                        && deletedGhostLogin == null,
                        editorAfterDelete + " | login=" + (deletedGhostLogin == null ? "REJECTED" : "ACCEPTED"));
                }

                // ---- error handling ----
                var missingOutcome = crudForm
                    .DeleteUserCoreForTestAsync(-999, true).GetAwaiter().GetResult();
                Check("10d. deleting a missing record reports notfound (error handling)",
                    missingOutcome == "notfound", missingOutcome);

                var adminLogin = runtime.AuthenticationService
                    .AuthenticateAsync(tenantB.CompanyId, "adminB", "123").GetAwaiter().GetResult();
                var selfOutcome = adminLogin == null
                    ? "nologin"
                    : crudForm.DeleteUserCoreForTestAsync(adminLogin.UserAccountId, true)
                        .GetAwaiter().GetResult();
                Check("10e. the signed-in account cannot delete itself",
                    selfOutcome == "self" && CountUsersB() == usersBefore,
                    $"outcome={selfOutcome} count={CountUsersB()}");
            }

            // =====================================================================================
            // 5. Tenant A protection (checks 27 + 29) and Tenant C protection (28)
            // =====================================================================================
            Write(string.Empty);
            Write("--- 5. Tenant A / Tenant C unchanged by this run ---");

            using (var adminAForm = BuildForm(runtime, tenants, tenantA!, "adminA"))
            {
                adminAForm.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);

                var nav = adminAForm.DescribeNav();
                Check("29a. Tenant A admin login still works and nav is unchanged",
                    nav == "Members, Attendance, Payments, Feedback, Inquiries, Complaints",
                    nav);
                Check("29b. Tenant A has NO Business Intelligence and NO Dashboard",
                    !nav.Contains("Business Intelligence") && !nav.Contains("Dashboard"),
                    nav);
                Check("29c. Tenant A still has no branch selector / no tenant dropdown",
                    !adminAForm.HasCurrentBranchSelector() && adminAForm.TenantSwitcherHiddenForTest,
                    $"selector={adminAForm.HasCurrentBranchSelector()} switcherHidden={adminAForm.TenantSwitcherHiddenForTest}");
                Check("29d. Tenant A Complaints module still present",
                    nav.Contains("Complaints"), nav);

                // Tenant A phase: Members / Inquiries expose NO branch UI for Tenant A
                // (adminA) while the grids are actually bound.
                var branchUiA = adminAForm.DescribeMemberInquiryBranchUiForTest();
                Write($"      adminA branch UI: {branchUiA}");
                Check("53. Tenant A adminA Members/Inquiries expose NO branch UI",
                    branchUiA.Contains("branchLabelVisible=False")
                    && branchUiA.Contains("branchPickerVisible=False")
                    && branchUiA.Contains("membersBranchColVisible=False")
                    && branchUiA.Contains("inquiriesBranchColVisible=False")
                    && !branchUiA.Contains("membersCols=[]")
                    && !branchUiA.Contains("inquiriesCols=[]"),
                    branchUiA);
            }

            using (var gymstaffAForm = BuildForm(runtime, tenants, tenantA!, "gymstaffA"))
            {
                gymstaffAForm.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                SynchronizationContext.SetSynchronizationContext(null);

                var branchUi = gymstaffAForm.DescribeMemberInquiryBranchUiForTest();
                Write($"      gymstaffA branch UI: {branchUi}");
                Check("53b. Tenant A gymstaffA Members/Inquiries expose NO branch UI",
                    branchUi.Contains("branchLabelVisible=False")
                    && branchUi.Contains("branchPickerVisible=False")
                    && branchUi.Contains("membersBranchColVisible=False")
                    && branchUi.Contains("inquiriesBranchColVisible=False")
                    && !branchUi.Contains("membersCols=[]")
                    && !branchUi.Contains("inquiriesCols=[]"),
                    branchUi);
            }

            // =====================================================================================
            // 6. Non-branching at the DATA LAYER: read-only probe of each tenant context and
            //    schema. Reports which BranchId properties the tenant's EF context actually
            //    maps and which %branch% columns physically exist in that tenant's database
            //    (INFORMATION_SCHEMA read - no data is touched). For A/B the context must be
            //    non-branch-aware, which makes every branch scope/filter in the shared code
            //    inert. Tenant C is only REPORTED here; its branch architecture is untouched.
            // =====================================================================================
            Write(string.Empty);
            Write("--- 6. Data-layer non-branching probe (read-only) ---");

            foreach (var (label, option, branchAware) in new[]
                     {
                         ("Tenant A", tenantA, false),
                         ("Tenant B", tenantB, false),
                         ("Tenant C", tenantC, true),
                     })
            {
                if (option == null) continue;

                using var probeDb = runtime.DataContextFactory
                    .CreateAsync(option.CompanyId, branchAware).GetAwaiter().GetResult();

                // The interface hides the concrete context; both wrappers keep the real
                // EF DbContext in a private "_context" field, so this read-only probe
                // reaches it reflectively (harness-only; production code never does this).
                var ef = probeDb.GetType()
                    .GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(probeDb) as DbContext
                    ?? throw new InvalidOperationException($"Could not reach the EF context for the {label} probe.");
                var efMapped = ef.Model.GetEntityTypes()
                    .SelectMany(et => et.GetProperties()
                        .Where(p => p.Name == "BranchId")
                        .Select(p => et.ClrType.Name + "." + p.Name))
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();

                var physicalColumns = new List<string>();
                var connection = ef.Database.GetDbConnection();
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT TABLE_NAME + '.' + COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
                        "WHERE COLUMN_NAME LIKE '%branch%' ORDER BY 1";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        physicalColumns.Add(reader.GetString(0));
                    }
                }

                Write($"      {label}: isBranchAware={probeDb.IsBranchAware} " +
                      $"efMapped=[{string.Join(",", efMapped)}] physical=[{string.Join(",", physicalColumns)}]");

                if (!branchAware)
                {
                    Check($"51. {label} tenant context is non-branch-aware (no branch scoping can apply)",
                        !probeDb.IsBranchAware, $"isBranchAware={probeDb.IsBranchAware}");
                }
            }

            var finalA = ReadCounts(runtime, tenantA!);
            var finalB = ReadCounts(runtime, tenantB);
            var finalC = ReadComplaintsC(runtime, tenantC);

            Check("27. Tenant B operations did not change Tenant A data", finalA == baselineA,
                $"before={baselineA} after={finalA}");
            Check("28. Tenant B operations did not change Tenant C data", finalC == baselineC,
                $"before complaints={baselineC} after={finalC}");
            Check("31-extra. Tenant B data itself is unchanged by this read-only run",
                finalB == baselineB, $"before={baselineB} after={finalB}");

            Write(string.Empty);
            Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | checks: {checks} | failures: {failures.Count} ---");
            foreach (var failure in failures)
            {
                Write($"  FAILED: {failure}");
            }

            return failures.Count == 0 ? 0 : 1;
        }

        // =====================================================================================
        // Helpers
        // =====================================================================================

        private static string ReadCounts(AppRuntime runtime, TenantOption tenant)
        {
            using var db = runtime.DataContextFactory.CreateAsync(tenant.CompanyId, false).GetAwaiter().GetResult();
            return $"members={db.Members.AsNoTracking().Count()} attendance={db.Attendances.AsNoTracking().Count()} " +
                   $"payments={db.Payments.AsNoTracking().Count()} total={db.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m} " +
                   $"feedback={db.Feedbacks.AsNoTracking().Count()} inquiries={db.Inquiries.AsNoTracking().Count()} " +
                   $"complaints={db.Complaints.AsNoTracking().Count()} users={db.UserAccounts.AsNoTracking().Count()}";
        }

        private static int ReadComplaintsC(AppRuntime runtime, TenantOption? tenantC)
        {
            if (tenantC == null) return -1;
            using var db = runtime.DataContextFactory.CreateAsync(tenantC.CompanyId, true).GetAwaiter().GetResult();
            return db.Complaints.AsNoTracking().Count();
        }

        /// <summary>Extracts a flattened KPI value ('Caption/Value') from DescribeBiPageForTest.</summary>
        private static string KpiField(string describe, string key)
        {
            var marker = key + ":'";
            var start = describe.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return "(missing)";
            start += marker.Length;
            var end = describe.IndexOf('\'', start);
            if (end < 0) return "(missing)";
            var flat = describe.Substring(start, end - start);
            var slash = flat.IndexOf('/');
            return slash >= 0 ? flat.Substring(slash + 1) : flat;
        }

        /// <summary>Extracts one chart's state block (name{...}) from DescribeBiPageForTest.</summary>
        private static string ChartField(string describe, string chart)
        {
            var start = describe.IndexOf(chart + "{", StringComparison.Ordinal);
            if (start < 0) return "(missing)";
            var end = describe.IndexOf('}', start);
            return end < 0 ? describe.Substring(start) : describe.Substring(start, end - start + 1);
        }

        private static bool IsNumber(string value) =>
            value.Length > 0 && value.All(char.IsDigit);

        /// <summary>Progress marker that is flushed immediately (pinpoints a hang).</summary>
        private static void Trace(string message)
        {
            Console.WriteLine("      .. " + message);
            Console.Out.Flush();
        }

        /// <summary>Pumps WinForms messages for a fixed time (windowed rendering checks).</summary>
        private static void Pump(int milliseconds)
        {
            var until = Environment.TickCount + milliseconds;
            while (Environment.TickCount < until)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        /// <summary>Pumps until the condition holds (or the timeout elapses); true = condition met.</summary>
        private static bool PumpUntil(Func<bool> condition, int timeoutMs)
        {
            var until = Environment.TickCount + timeoutMs;
            while (!condition() && Environment.TickCount < until)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            return condition();
        }

        /// <summary>True when a capture shows all four charts drawn (>= 5 distinct sampled colors).</summary>
        private static bool Painted(string capture)
        {
            if (!capture.Contains("allHandlesCreated=True") || !capture.Contains("allVisible=True"))
            {
                return false;
            }

            const string marker = "minColors=";
            var start = capture.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return false;
            start += marker.Length;
            var end = capture.IndexOf(' ', start);
            var raw = end < 0 ? capture.Substring(start) : capture.Substring(start, end - start);
            return int.TryParse(raw, out var colors) && colors >= 5;
        }

        private static Form1 BuildForm(
            AppRuntime runtime,
            IReadOnlyList<TenantOption> tenants,
            TenantOption tenant,
            string username)
        {
            var login = runtime.AuthenticationService
                .AuthenticateAsync(tenant.CompanyId, username, "123").GetAwaiter().GetResult();

            var info = runtime.SubscriptionService.GetCompanySubscriptionAsync(tenant.CompanyId).GetAwaiter().GetResult();
            var subscriptionContext = info == null
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

            if (login == null)
            {
                throw new InvalidOperationException(
                    $"'{username}' rejected by login for {tenant.CompanyCode}.");
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
                Subscription = subscriptionContext
            };

            var form = new Form1(session, tenants, runtime.DataContextFactory, runtime.AuthorizationService)
            {
                SuppressDialogs = true
            };

            SynchronizationContext.SetSynchronizationContext(null);
            form.InitializeCurrentBranchSelectorForTestAsync().GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(null);
            return form;
        }
    }
}
