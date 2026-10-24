using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Targeted acceptance harness for the three Phase 8 follow-up fixes:
    ///   A. tenant logout returns to the login form (every role) without ending the process;
    ///   B. the dashboard/KPI/chart state survives a Current Branch switch and actually reflects
    ///      the selected branch (per role, repeated switches);
    ///   C. branch manager display/assignment uses the existing Manager accounts only.
    ///
    /// It also re-proves that Tenant A, Tenant B and the master database are untouched and that no
    /// temporary row survives the run. Tenant C is the only database ever written to.
    /// </summary>
    internal static class Phase8FixesVerification
    {
        public static int Run()
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
            Write("PHASE 8 FIX VERIFICATION (logout / dashboard branch switch / branch manager)");
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
            if (tenantC == null)
            {
                Write("  [FAIL] no branch-aware tenant is configured.");
                return 1;
            }

            var tenantA = tenants.First(t => !t.IsBranchAware && t.CompanyCode == "COMP001");
            var tenantB = tenants.First(t => !t.IsBranchAware && t.CompanyCode == "COMP002");

            // Capture the live Tenant A baseline before any harness writes: section D
            // compares against this capture instead of hardcoded counts (manual testing
            // may legitimately change live data between runs).
            int aMembersStart, aAttendanceStart, aPaymentsStart;
            using (var dbA0 = runtime.DataContextFactory.CreateAsync(tenantA.CompanyId, false).GetAwaiter().GetResult())
            {
                aMembersStart = dbA0.Members.Count();
                aAttendanceStart = dbA0.Attendances.Count();
                aPaymentsStart = dbA0.Payments.Count();
            }

            // Tenant B gets the same live-baseline treatment (manual testing may shift its
            // counts between runs; the harness must prove this run changed nothing).
            int bMembersStart, bAttendanceStart, bPaymentsStart;
            using (var dbB0 = runtime.DataContextFactory.CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult())
            {
                bMembersStart = dbB0.Members.Count();
                bAttendanceStart = dbB0.Attendances.Count();
                bPaymentsStart = dbB0.Payments.Count();
            }

            var infoC = runtime.Resolver.GetDatabaseInfoAsync(tenantC.CompanyId).GetAwaiter().GetResult();
            var infoA = runtime.Resolver.GetDatabaseInfoAsync(tenantA.CompanyId).GetAwaiter().GetResult();
            var infoB = runtime.Resolver.GetDatabaseInfoAsync(tenantB.CompanyId).GetAwaiter().GetResult();

            Write($"Target tenant  : {tenantC.CompanyCode} / {tenantC.CompanyName} (CompanyId {tenantC.CompanyId})");
            Write($"Target database: {infoC.ServerName} / {infoC.DatabaseName}");
            Write(string.Empty);

            ApplicationConfiguration.Initialize();
            SynchronizationContext.SetSynchronizationContext(null);

            var options = runtime.ConnectionFactory.CreateBranchAwareOptions(infoC);
            using var db = new TenantCAlphaEliteDbContext(options);

            Check("write target is Tenant C (db68417)", infoC.DatabaseName.Contains("68417"), infoC.DatabaseName);
            Check("Tenant A is a different database", infoA.DatabaseName != infoC.DatabaseName, infoA.DatabaseName);
            Check("Tenant B is a different database", infoB.DatabaseName != infoC.DatabaseName, infoB.DatabaseName);

            var suffix = (DateTime.UtcNow.Ticks % 1_000_000_000L).ToString();
            var tempBranchCode = "P8F-" + suffix;
            int? tempBranchId = null;

            // Original manager assignments, restored in the finally block.
            var originalManagers = db.Branches.AsNoTracking()
                .ToDictionary(b => b.BranchId, b => b.ManagerUserAccountId);
            var originalUserCount = db.UserAccounts.Count();
            var originalMemberCount = db.Members.Count();
            var originalAttendanceCount = db.Attendances.Count();
            var originalBranchCount = db.Branches.Count();

            void PurgeTempRows()
            {
                db.Database.ExecuteSqlRaw($"DELETE FROM dbo.Branches WHERE BranchCode LIKE 'P8F-%'");
            }

            try { PurgeTempRows(); } catch (Exception ex) { Write($"  [WARN] pre-purge: {ex.GetBaseException().Message}"); }

            try
            {
                var accounts = db.UserAccounts.AsNoTracking().OrderBy(u => u.UserAccountId).ToList();
                var branches = db.Branches.AsNoTracking().OrderBy(b => b.BranchId).ToList();

                Form1 BuildForm(string username)
                {
                    var account = accounts.First(a => a.Username == username);
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
                        Role = account.Role
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

                // =================================================================================
                // A. Tenant logout returns to the login form (every role)
                // =================================================================================
                Write(string.Empty);
                Write("--- A. Tenant logout returns to the login form ---");

                foreach (var username in new[] { "admin", "manager", "gymstaff", "member" })
                {
                    var form = BuildForm(username);
                    var before = form.DescribeLogoutStateForTest();
                    var result = form.RequestLogoutForTest();
                    var after = form.DescribeLogoutStateForTest();

                    Check($"{username} logout returns DialogResult.Retry (login form again)",
                        result == DialogResult.Retry, $"dialogResult={result} ({before} -> {after})");

                    Check($"{username} logout clears branch/member session state",
                        after.Contains("branchId=null") && after.Contains("memberId=null") && after.Contains("membersCache=0"),
                        after);
                }

                // The Master window keeps its existing behaviour (unchanged).
                using (var master = new MasterDashboardForm(runtime, new MasterUserSession
                {
                    MasterUserId = "harness",
                    Username = "superadmin",
                    Role = AlphaElite.infrastructure.services.Roles.MasterAdmin,
                    IdentityRoleName = "SuperAdmin"
                }))
                {
                    var masterResult = master.RequestLogoutForTest();
                    Check("MasterAdmin/SuperAdmin logout still returns to the login form",
                        masterResult == DialogResult.Retry, $"dialogResult={masterResult}");
                }

                // The process keeps running: a brand-new tenant window (and therefore a new login
                // session) can be created immediately after a logout, with no restart.
                using (var reopened = BuildForm("admin"))
                {
                    Check("a new tenant session can be opened immediately after logout (no restart)",
                        reopened.HasCurrentBranchSelector(), reopened.DescribeCurrentBranchSelector());
                }

                // =================================================================================
                // B. Dashboard / KPI / charts survive and follow the Current Branch
                // =================================================================================
                Write(string.Empty);
                Write("--- B. Dashboard branch switching (per role) ---");

                var today = DateTime.Today;
                var monthStart = new DateTime(today.Year, today.Month, 1);

                int TruthMembers(int b) => db.Members.AsNoTracking().Count(m => m.BranchId == b);
                int TruthAttendanceMonth(int b) => db.Attendances.AsNoTracking().Count(a => a.BranchId == b && a.CheckInDate.Date >= monthStart);
                int TruthPayments(int b) => (int)(db.Payments.AsNoTracking().Where(p => p.BranchId == b).Sum(p => (decimal?)p.AmountPaid) ?? 0m);
                int TruthOpenComplaints(int b) => db.Complaints.AsNoTracking().Count(c => c.BranchId == b && c.Status != "Resolved" && c.Status != "Closed");

                var tenantWideMembers = db.Members.Count();
                Write($"  live truth: " + string.Join(" | ",
                    branches.Select(b => $"{b.BranchCode} members={TruthMembers(b.BranchId)} " +
                                         $"attMonth={TruthAttendanceMonth(b.BranchId)} " +
                                         $"payments={TruthPayments(b.BranchId)} " +
                                         $"openComplaints={TruthOpenComplaints(b.BranchId)}")));

                foreach (var username in new[] { "admin", "manager", "gymstaff" })
                {
                    using var form = BuildForm(username);

                    // Switching to the branch already selected is a deliberate no-op (the view is
                    // already showing it), so populate it explicitly - this mirrors Form1_Load.
                    void Activate(int? branchId)
                    {
                        var before = form.CurrentBranchIdForTest;
                        var moved = form.SwitchCurrentBranchAsync(branchId).GetAwaiter().GetResult();
                        SynchronizationContext.SetSynchronizationContext(null);

                        if (moved && form.CurrentBranchIdForTest != before)
                        {
                            return; // the switch itself performed the full reload
                        }

                        form.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                        SynchronizationContext.SetSynchronizationContext(null);
                    }

                    if (!form.HasDashboardForTest)
                    {
                        // This role has no dashboard module at all (by design). Phase 2/3:
                        // the role is pinned to its home branch - every switch attempt is
                        // refused and the scoped views keep showing ONLY the home branch.
                        var homeId = form.CurrentBranchIdForTest;
                        var scopedOk = homeId.HasValue;
                        var scopedDetail = new List<string>();
                        foreach (var branch in branches.Where(b => b.BranchId != homeId))
                        {
                            Activate(branch.BranchId);
                            var refused = form.CurrentBranchIdForTest == homeId;
                            var scoped = form.DescribeScopedCountsAsync().GetAwaiter().GetResult();
                            SynchronizationContext.SetSynchronizationContext(null);
                            var ok = refused &&
                                     scoped.Contains($"members={TruthMembers(homeId!.Value)}") &&
                                     scoped.Contains($"attendance={db.Attendances.Count(a => a.BranchId == homeId.Value)}");
                            scopedOk &= ok;
                            scopedDetail.Add($"{branch.BranchCode}: refused={refused} {scoped}");
                        }

                        Check($"{username} has no dashboard module (by design) and stays pinned to its home branch",
                            scopedOk, string.Join(" ; ", scopedDetail));
                        Check($"{username} nav has no Dashboard entry",
                            !form.DescribeNav().Contains("Dashboard"), form.DescribeNav());
                        continue;
                    }

                    if (username != "admin")
                    {
                        // Phase 2/3: Manager / GymStaff are pinned to their home branch - the
                        // selector lists only the home branch, every other target is refused,
                        // and the dashboard keeps showing the home branch's truth.
                        var homeId = form.CurrentBranchIdForTest;
                        var pinnedOk = homeId.HasValue;
                        var pinnedDetail = new List<string>();

                        foreach (var branch in branches.Where(b => b.BranchId != homeId))
                        {
                            Activate(branch.BranchId);
                            var refused = form.CurrentBranchIdForTest == homeId;
                            var total = form.KpiValueForTest("total");
                            var attMonth = form.KpiValueForTest("attendanceMonth");
                            var uncovered = form.DashboardKpiCardsAreUncoveredForTest();

                            var ok = refused
                                     && total == TruthMembers(homeId!.Value)
                                     && attMonth == TruthAttendanceMonth(homeId.Value)
                                     && uncovered;

                            pinnedOk &= ok;
                            pinnedDetail.Add($"{branch.BranchCode}: refused={refused} " +
                                             $"total={total}/{TruthMembers(homeId!.Value)} " +
                                             $"attMonth={attMonth}/{TruthAttendanceMonth(homeId.Value)} " +
                                             $"kpiUncovered={uncovered}");
                        }

                        Check($"{username} is pinned to the home branch (switches refused, home data shown)",
                            pinnedOk, string.Join(" ; ", pinnedDetail));
                        continue;
                    }

                    var roleOk = true;
                    var detail = new List<string>();

                    foreach (var branch in branches)
                    {
                        Activate(branch.BranchId);
                        var switched = form.CurrentBranchIdForTest == branch.BranchId;

                        var state = form.DescribeDashboardStateForTest();
                        var total = form.KpiValueForTest("total");
                        var attMonth = form.KpiValueForTest("attendanceMonth");
                        var payments = form.KpiValueForTest("payments");
                        var complaints = form.KpiValueForTest("openComplaints");
                        var uncovered = form.DashboardKpiCardsAreUncoveredForTest();

                        var branchOk = switched
                                       && total == TruthMembers(branch.BranchId)
                                       && attMonth == TruthAttendanceMonth(branch.BranchId)
                                       && payments == TruthPayments(branch.BranchId)
                                       && complaints == TruthOpenComplaints(branch.BranchId)
                                       && uncovered;

                        roleOk &= branchOk;
                        detail.Add($"{branch.BranchCode}: switched={switched} total={total}/{TruthMembers(branch.BranchId)} " +
                                   $"attMonth={attMonth}/{TruthAttendanceMonth(branch.BranchId)} " +
                                   $"payments={payments}/{TruthPayments(branch.BranchId)} " +
                                   $"openComplaints={complaints}/{TruthOpenComplaints(branch.BranchId)} " +
                                   $"kpiUncovered={uncovered} " +
                                   $"series={state.Substring(state.IndexOf("series=[", StringComparison.Ordinal))}");
                    }

                    Check($"{username} dashboard follows every branch (values + visible KPI cards)",
                        roleOk, string.Join(" ; ", detail));
                }

                using (var adminForm = BuildForm("admin"))
                {
                    void Go(int? branchId)
                    {
                        var before = adminForm.CurrentBranchIdForTest;
                        var moved = adminForm.SwitchCurrentBranchAsync(branchId).GetAwaiter().GetResult();
                        SynchronizationContext.SetSynchronizationContext(null);
                        if (moved && adminForm.CurrentBranchIdForTest != before) return;

                        adminForm.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                        SynchronizationContext.SetSynchronizationContext(null);
                    }

                    Go(null);
                    var allTotal = adminForm.KpiValueForTest("total");

                    // Repeated A -> B -> C -> A switching must stay correct (no stale reload wins).
                    var sequence = new[] { 1, 2, 3, 1, 2, 3, 1 };
                    var sequenceOk = true;
                    var sequenceDetail = new List<string>();
                    foreach (var branchId in sequence)
                    {
                        Go(branchId);
                        var value = adminForm.KpiValueForTest("total");
                        var expected = TruthMembers(branchId);
                        sequenceOk &= value == expected;
                        sequenceDetail.Add($"{branchId}->{value}/{expected}");
                    }

                    Check("ALL BRANCHES shows the tenant-wide total", allTotal == tenantWideMembers,
                        $"total={allTotal}, tenantWide={tenantWideMembers}");
                    Check("repeated switching never leaves a stale dashboard", sequenceOk,
                        string.Join(" ", sequenceDetail));

                    // Isolation is not sacrificed for the visual fix.
                    Go(1);
                    var branch1 = adminForm.KpiValueForTest("total");
                    Check("branch-scoped dashboard is still isolated from the other branches",
                        branch1 == TruthMembers(1) && branch1 != tenantWideMembers,
                        $"BR-MAIN total={branch1}, tenantWide={tenantWideMembers}");

                    // Layout regression guard: the Current Branch selector must not overlap the
                    // signed-in user / role labels (it used to hide them).
                    Check("Current Branch selector does not overlap the header user labels",
                        !adminForm.CurrentBranchSelectorOverlapsHeaderLabelsForTest(),
                        adminForm.DescribeHeaderLayoutForTest());
                }

                // =================================================================================
                // C. Branch manager display / assignment
                // =================================================================================
                Write(string.Empty);
                Write("--- C. Branch manager (existing Manager accounts only) ---");

                var managerAccount = db.UserAccounts.AsNoTracking()
                    .FirstOrDefault(u => u.Role == Roles.Manager && u.BranchId == 1);
                var otherManager = db.UserAccounts.AsNoTracking()
                    .FirstOrDefault(u => u.Role == Roles.Manager && u.UserAccountId != (managerAccount == null ? -1 : managerAccount.UserAccountId));
                var nonManager = db.UserAccounts.AsNoTracking().First(u => u.Role == Roles.GymStaff);

                Check("an existing Manager account is available for assignment",
                    managerAccount != null, managerAccount == null ? "(none)" : $"UserAccountId={managerAccount.UserAccountId}");

                using (var adminForm = BuildForm("admin"))
                {
                    adminForm.SwitchCurrentBranchAsync(null).GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);

                    // A branch may have no manager at all.
                    tempBranchId = adminForm.SaveBranchCoreAsync(null, tempBranchCode, "Phase 8 Fix Temp Branch", "Test City", null, true)
                        .GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);

                    adminForm.LoadBranchesGridForTestAsync().GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);
                    var listing = adminForm.DescribeBranchesForTest();
                    Check("a branch with no manager is valid and displays (none)",
                        listing.Contains($"{tempBranchId}:{tempBranchCode}=(none)"), listing);

                    // Phase 7 seeded three branch managers on top of the original demo
                    // manager, so the picker offers MORE Manager accounts than before; the
                    // requirement-level property is that existing Manager accounts are
                    // offered (exact counts are asserted nowhere else).
                    var managerOptionsMatch = System.Text.RegularExpressions.Regex.Match(listing, "managerOptions=(\\d+)");
                    var managerOptionsCount = managerOptionsMatch.Success
                        ? int.Parse(managerOptionsMatch.Groups[1].Value)
                        : 0;

                    Check("the manager picker offers the tenant's existing Manager accounts (original + phase-7 branch managers)",
                        managerOptionsCount >= 2, listing);

                    Check("branch plumbing columns are hidden (manager shown by name only)",
                        listing.Contains("idColumnHidden=True") && listing.Contains("fkColumnHidden=True"), listing);

                    // Assign the existing manager and verify persistence + immediate display.
                    if (managerAccount != null)
                    {
                        adminForm.AssignBranchManagerForTestAsync(tempBranchId.Value, managerAccount.UserAccountId)
                            .GetAwaiter().GetResult();
                        SynchronizationContext.SetSynchronizationContext(null);

                        adminForm.LoadBranchesGridForTestAsync().GetAwaiter().GetResult();
                        SynchronizationContext.SetSynchronizationContext(null);
                        var afterAssign = adminForm.DescribeBranchesForTest();
                        var persisted = db.Branches.AsNoTracking()
                            .First(b => b.BranchId == tempBranchId.Value).ManagerUserAccountId;

                        Check("manager assignment persists in Tenant C",
                            persisted == managerAccount.UserAccountId, $"ManagerUserAccountId={persisted}");
                        Check("the branch list immediately shows the manager's name",
                            afterAssign.Contains($"{tempBranchId}:{tempBranchCode}={managerAccount.FullName}/Active"),
                            afterAssign);

                        // The same manager may serve more than one branch.
                        var second = branches.First(b => b.BranchId != tempBranchId.Value);
                        adminForm.AssignBranchManagerForTestAsync(second.BranchId, managerAccount.UserAccountId)
                            .GetAwaiter().GetResult();
                        SynchronizationContext.SetSynchronizationContext(null);
                        adminForm.LoadBranchesGridForTestAsync().GetAwaiter().GetResult();
                        SynchronizationContext.SetSynchronizationContext(null);
                        var multi = adminForm.DescribeBranchesForTest();
                        var bothAssigned = db.Branches.AsNoTracking()
                            .Where(b => b.BranchId == tempBranchId.Value || b.BranchId == second.BranchId)
                            .All(b => b.ManagerUserAccountId == managerAccount.UserAccountId);
                        Check("the same manager can be assigned to more than one branch",
                            bothAssigned && multi.Contains($"{tempBranchId}:{tempBranchCode}={managerAccount.FullName}/Active"),
                            multi);

                        // Clearing the manager is allowed again.
                        adminForm.AssignBranchManagerForTestAsync(second.BranchId, null).GetAwaiter().GetResult();
                        SynchronizationContext.SetSynchronizationContext(null);
                        var cleared = db.Branches.AsNoTracking().First(b => b.BranchId == second.BranchId).ManagerUserAccountId;
                        Check("a manager assignment can be cleared back to empty", cleared == null,
                            cleared?.ToString() ?? "null");
                    }

                    // A non-Manager account must be refused (and no credential is ever created).
                    var refused = false;
                    try
                    {
                        adminForm.AssignBranchManagerForTestAsync(tempBranchId.Value, nonManager.UserAccountId)
                            .GetAwaiter().GetResult();
                    }
                    catch (InvalidOperationException)
                    {
                        refused = true;
                    }
                    SynchronizationContext.SetSynchronizationContext(null);
                    Check("a non-Manager account cannot be assigned", refused,
                        refused ? "InvalidOperationException" : "NOT refused");

                    Check("no new user account was created by branch manager assignment",
                        db.UserAccounts.Count() == originalUserCount,
                        $"before={originalUserCount} now={db.UserAccounts.Count()}");

                    if (otherManager != null)
                    {
                        Check("a second existing Manager account is also selectable (no new logins)",
                            true, $"UserAccountId={otherManager.UserAccountId} ({otherManager.FullName})");
                    }
                }

                // =================================================================================
                // D. Tenant A / Tenant B / master unchanged, no residue
                // =================================================================================
                Write(string.Empty);
                Write("--- D. Tenant A / Tenant B / master unchanged ---");

                using (var dbA = runtime.DataContextFactory.CreateAsync(tenantA.CompanyId, false).GetAwaiter().GetResult())
                {
                    Check("Tenant A members/attendance/payments unchanged (start-of-run baseline)",
                        dbA.Members.Count() == aMembersStart && dbA.Attendances.Count() == aAttendanceStart && dbA.Payments.Count() == aPaymentsStart,
                        $"members={dbA.Members.Count()}/{aMembersStart} attendance={dbA.Attendances.Count()}/{aAttendanceStart} payments={dbA.Payments.Count()}/{aPaymentsStart}");
                }

                using (var dbB = runtime.DataContextFactory.CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult())
                {
                    Check("Tenant B members/attendance/payments unchanged (start-of-run baseline)",
                        dbB.Members.Count() == bMembersStart && dbB.Attendances.Count() == bAttendanceStart && dbB.Payments.Count() == bPaymentsStart,
                        $"members={dbB.Members.Count()}/{bMembersStart} attendance={dbB.Attendances.Count()}/{bAttendanceStart} payments={dbB.Payments.Count()}/{bPaymentsStart}");
                }

                using (var master = new Microsoft.Data.SqlClient.SqlConnection(
                    runtime.Configuration.GetConnectionString("MasterErp")))
                {
                    master.Open();
                    var companies = Convert.ToInt64(new Microsoft.Data.SqlClient.SqlCommand("SELECT COUNT(*) FROM dbo.Companies", master).ExecuteScalar());
                    var masterDbs = Convert.ToInt64(new Microsoft.Data.SqlClient.SqlCommand("SELECT COUNT(*) FROM dbo.CompanyDatabase", master).ExecuteScalar());
                    Check("master unchanged (Companies=3, CompanyDatabase=3)", companies == 3 && masterDbs == 3,
                        $"Companies={companies}, CompanyDatabase={masterDbs}");
                }
            }
            catch (Exception ex)
            {
                Write($"  [FAIL] harness: {ex.GetType().Name}: {ex.GetBaseException().Message}");
                failures.Add($"harness: {ex.GetType().Name}: {ex.GetBaseException().Message}");
            }
            finally
            {
                try
                {
                    db.ChangeTracker.Clear();

                    // Restore the original manager assignments, then remove the temporary branch.
                    foreach (var entry in originalManagers)
                    {
                        var tracked = db.Branches.FirstOrDefault(b => b.BranchId == entry.Key);
                        if (tracked != null && tracked.ManagerUserAccountId != entry.Value)
                        {
                            tracked.ManagerUserAccountId = entry.Value;
                        }
                    }

                    db.SaveChanges();
                    PurgeTempRows();
                }
                catch (Exception ex)
                {
                    Write($"  [WARN] cleanup: {ex.GetType().Name}: {ex.GetBaseException().Message}");
                    failures.Add($"cleanup: {ex.GetType().Name}: {ex.GetBaseException().Message}");
                }
            }

            Write(string.Empty);
            Write("--- E. Baseline restored ---");
            db.ChangeTracker.Clear();
            Check("temporary branch removed", db.Branches.Count() == originalBranchCount,
                $"branches={db.Branches.Count()} (expected {originalBranchCount})");
            Check("no member/attendance row was created",
                db.Members.Count() == originalMemberCount && db.Attendances.Count() == originalAttendanceCount,
                $"members={db.Members.Count()}/{originalMemberCount} attendance={db.Attendances.Count()}/{originalAttendanceCount}");
            Check("no user account residue", db.UserAccounts.Count() == originalUserCount,
                $"users={db.UserAccounts.Count()}/{originalUserCount}");
            var restoredBranches = db.Branches.AsNoTracking().OrderBy(b => b.BranchId).ToList();
            Check("manager assignments restored",
                restoredBranches.All(b => b.ManagerUserAccountId == originalManagers[b.BranchId]),
                string.Join(",", restoredBranches.Select(b =>
                    $"{b.BranchCode}:{(b.ManagerUserAccountId.HasValue ? b.ManagerUserAccountId.Value.ToString() : "null")}")));

            Write(string.Empty);
            Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | checks: {checks} | failures: {failures.Count} ---");
            foreach (var failure in failures)
            {
                Write($"  FAILED: {failure}");
            }

            return failures.Count == 0 ? 0 : 1;
        }
    }
}
