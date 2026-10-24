using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 8 acceptance harness. Proves the Tenant C multi-branch redesign end to end:
    /// the approved branch catalogue (rename in place, BranchIds preserved), the Branch-scoped
    /// data migration, Current Branch selector authority for reads and write-stamping, member
    /// transfer with historical preservation, soft-deactivation protection and role gating.
    ///
    /// Every temporary row this harness creates (a throwaway member, an attendance row and a
    /// throwaway branch) is removed in a finally block, so the live dataset returns to its
    /// baseline. Tenant A, Tenant B and the master database are only ever read.
    ///
    /// Phase 2 realignment (October 2026): count expectations were moved from the stale
    /// pre-Phase-9 demo dataset to the Phase 9-approved live baselines (21 members split
    /// 7/8/6 across BR-MAIN/BR-CEBU/BR-DAVAO, 17 attendance, 14 payments, 17 feedback,
    /// 20 inquiries, 2 complaints). Restoration checks in section 9 compare against
    /// baselines captured before any temporary write instead of hardcoded counts.
    /// </summary>
    internal static class Phase8Verification
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
            Write("PHASE 8 ACCEPTANCE VERIFICATION (branch management; Tenant C only)");
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

            // Capture the live Tenant A baseline before any harness writes: section 8
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

            // Tenant C complaints are externally mutable (the Phase 6 demo rows were deleted
            // outside this project's runs), so their per-branch split is a start-of-run
            // baseline too - the "historical complaints preserved" check compares against it.
            int cmpMainStart, cmpCebuStart;
            using (var dbC0 = runtime.DataContextFactory.CreateAsync(tenantC.CompanyId, true).GetAwaiter().GetResult())
            {
                cmpMainStart = dbC0.Complaints.AsNoTracking().Count(c => c.BranchId == 1);
                cmpCebuStart = dbC0.Complaints.AsNoTracking().Count(c => c.BranchId == 2);
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

            // =================================================================================
            // 0. Write-target guard (never Tenant A / Tenant B)
            // =================================================================================
            Write("--- 0. Write-target guard ---");
            Check("write target is Tenant C (db68417)", infoC.DatabaseName.Contains("68417"), infoC.DatabaseName);
            Check("Tenant A is a different database", infoA.DatabaseName != infoC.DatabaseName, infoA.DatabaseName);
            Check("Tenant B is a different database", infoB.DatabaseName != infoC.DatabaseName, infoB.DatabaseName);

            // Branch.BranchCode is nvarchar(20): keep the throwaway codes short but unique.
            var suffix = (DateTime.UtcNow.Ticks % 1_000_000_000_000L).ToString();
            var tempBranchCode = "P8T-" + suffix;   // <= 16 chars
            var tempMemberCode = "P8-MEM-" + suffix; // <= 26 chars (Members.MemberCode is nvarchar(50))
            int tempMemberId = 0;
            int? tempBranchId = null;

            // Remove any throwaway rows left behind by an interrupted earlier run (idempotent;
            // these codes only ever belong to this harness).
            void PurgeTempRows()
            {
                db.Database.ExecuteSqlRaw("DELETE FROM dbo.Attendances WHERE MemberId IN (SELECT MemberId FROM dbo.Members WHERE MemberCode LIKE 'P8-MEM-%')");
                db.Database.ExecuteSqlRaw("DELETE FROM dbo.Members WHERE MemberCode LIKE 'P8-MEM-%'");
                db.Database.ExecuteSqlRaw("DELETE FROM dbo.Branches WHERE BranchCode LIKE 'P8T-%'");
            }

            try { PurgeTempRows(); } catch (Exception ex) { Write($"  [WARN] pre-purge: {ex.GetBaseException().Message}"); }

            // Phase 9 live-baseline realignment: capture the approved live dataset before any
            // temporary write so section 9 proves full restoration (no harness residue) against
            // the real Phase 9 state instead of stale pre-Phase-9 hardcoded counts.
            var membersBaseline = db.Members.AsNoTracking().Count();
            var attendanceBaseline = db.Attendances.AsNoTracking().Count();
            var branch3MembersBaseline = db.Members.AsNoTracking().Count(m => m.BranchId == 3);

            try
            {
                // =================================================================================
                // 1. Branch catalogue (rename in place, BranchIds preserved)
                // =================================================================================
                Write(string.Empty);
                Write("--- 1. Branch catalogue ---");

                var branches = db.Branches.AsNoTracking().OrderBy(b => b.BranchId).ToList();
                Branch? B(int id) => branches.FirstOrDefault(b => b.BranchId == id);

                Check("exactly 3 branches", branches.Count == 3, $"count={branches.Count}");
                Check("BranchId 1 preserved as BR-MAIN", B(1)?.BranchCode == "BR-MAIN", B(1)?.BranchCode ?? "(missing)");
                Check("BR-MAIN name + city",
                    B(1)?.BranchName == "AlphaElite Manila - Head Office" && B(1)?.City == "Manila City",
                    $"{B(1)?.BranchName} / {B(1)?.City}");
                Check("BR-MAIN manager = UserAccountId 2", B(1)?.ManagerUserAccountId == 2,
                    B(1)?.ManagerUserAccountId?.ToString() ?? "(none)");
                Check("BranchId 2 preserved as BR-CEBU", B(2)?.BranchCode == "BR-CEBU", B(2)?.BranchCode ?? "(missing)");
                Check("BR-CEBU name + city",
                    B(2)?.BranchName == "AlphaElite Cebu Branch" && B(2)?.City == "Cebu City",
                    $"{B(2)?.BranchName} / {B(2)?.City}");
                Check("BR-DAVAO created (BranchId 3)", B(3)?.BranchCode == "BR-DAVAO", B(3)?.BranchCode ?? "(missing)");
                Check("BR-DAVAO name + city",
                    B(3)?.BranchName == "AlphaElite Davao Branch" && B(3)?.City == "Davao City",
                    $"{B(3)?.BranchName} / {B(3)?.City}");
                Check("all branches active", branches.All(b => b.IsActive),
                    string.Join(",", branches.Select(b => $"{b.BranchCode}:{b.IsActive}")));

                var managerAcct = db.UserAccounts.AsNoTracking().FirstOrDefault(u => u.UserAccountId == 2);
                Check("assigned manager is an active Manager account",
                    managerAcct != null && managerAcct.Role == Roles.Manager && managerAcct.IsActive,
                    managerAcct == null ? "(missing)" : $"role={managerAcct.Role} active={managerAcct.IsActive}");
                // Phase 9 live state: BR-DAVAO was populated during the Phase 8→9 demo-data
                // expansion (members 7/8/6 across BR-MAIN/BR-CEBU/BR-DAVAO), so it is no longer empty.
                Check("BR-DAVAO at its Phase 9 live baseline (6 members)",
                    db.Members.AsNoTracking().Count(m => m.BranchId == 3) == 6,
                    $"members={db.Members.AsNoTracking().Count(m => m.BranchId == 3)}");

                // =================================================================================
                // 2. Approved data migration (backfill + admin hygiene)
                // =================================================================================
                Write(string.Empty);
                Write("--- 2. Data migration ---");

                var subs = db.Subscriptions.AsNoTracking().Include(s => s.Member).ToList();
                Check("subscriptions backfilled with the member's branch",
                    subs.Count == 2 && subs.All(s => s.BranchId.HasValue && s.Member != null && s.BranchId == s.Member.BranchId),
                    string.Join(",", subs.Select(s => $"sub{s.SubscriptionId}->branch{s.BranchId}")));

                var adminAcct = db.UserAccounts.AsNoTracking().First(u => u.Username == "admin");
                Check("admin carry-over BranchId nulled (hygiene only)", adminAcct.BranchId == null,
                    adminAcct.BranchId?.ToString() ?? "null");

                // Historical branch distribution preserved by the rename-in-place (no BranchId rewrite).
                int Att(int? b) => db.Attendances.AsNoTracking().Count(a => a.BranchId == b);
                int Pay(int? b) => db.Payments.AsNoTracking().Count(p => p.BranchId == b);
                int Fb(int? b) => db.Feedbacks.AsNoTracking().Count(f => f.BranchId == b);
                int Inq(int? b) => db.Inquiries.AsNoTracking().Count(i => i.BranchId == b);
                int Cmp(int? b) => db.Complaints.AsNoTracking().Count(c => c.BranchId == b);

                // Phase 9 live distribution (totals: attendance 17, payments 14, feedback 17, inquiries 20).
                Check("historical attendance preserved (BR-MAIN 6 / BR-CEBU 6 / BR-DAVAO 5, Phase 9 live state)",
                    Att(1) == 6 && Att(2) == 6 && Att(3) == 5, $"main={Att(1)} cebu={Att(2)} davao={Att(3)}");
                Check("historical payments preserved (BR-MAIN 4 / BR-CEBU 5 / BR-DAVAO 5, Phase 9 live state)",
                    Pay(1) == 4 && Pay(2) == 5 && Pay(3) == 5, $"main={Pay(1)} cebu={Pay(2)} davao={Pay(3)}");
                Check("historical feedback preserved (BR-MAIN 6 / BR-CEBU 6 / BR-DAVAO 5, Phase 9 live state)",
                    Fb(1) == 6 && Fb(2) == 6 && Fb(3) == 5, $"main={Fb(1)} cebu={Fb(2)} davao={Fb(3)}");
                // Live inquiries are 5/6/0 by branch: 9 of the 20 live rows were seeded without a
                // BranchId during the Phase 9 demo-data expansion (they only appear in ALL BRANCHES scope).
                Check("historical inquiries preserved (BR-MAIN 5 / BR-CEBU 6 / BR-DAVAO 0, Phase 9 live state)",
                    Inq(1) == 5 && Inq(2) == 6 && Inq(3) == 0, $"main={Inq(1)} cebu={Inq(2)} davao={Inq(3)}");
                // Live split (start-of-run baseline): the demo rows are externally mutable,
                // so preservation is asserted against the run-start capture, not a pinned count.
                Check("historical complaints preserved (start-of-run baseline)",
                    Cmp(1) == cmpMainStart && Cmp(2) == cmpCebuStart, $"main={Cmp(1)}/{cmpMainStart} cebu={Cmp(2)}/{cmpCebuStart}");

                // =================================================================================
                // 3. Role gating: selector + branch management nav
                // =================================================================================
                Write(string.Empty);
                Write("--- 3. Role gating (Current Branch selector / Branches nav) ---");

                var accounts = db.UserAccounts.AsNoTracking().OrderBy(u => u.UserAccountId).ToList();

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

                    // Form1_Load (which populates the selector) never fires headlessly.
                    form.InitializeCurrentBranchSelectorForTestAsync().GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);
                    return form;
                }

                using (var adminForm = BuildForm("admin"))
                {
                    Check("admin has the Current Branch selector", adminForm.HasCurrentBranchSelector(),
                        adminForm.DescribeCurrentBranchSelector());
                    Check("admin can manage branches", adminForm.HasBranchManagement(), "ManageBranches");
                    Check("admin nav includes Branches", adminForm.DescribeNav().Contains("Branches"), adminForm.DescribeNav());
                }

                using (var managerForm = BuildForm("manager"))
                {
                    Check("manager has the Current Branch selector", managerForm.HasCurrentBranchSelector(),
                        managerForm.DescribeCurrentBranchSelector());
                    // Phase 2/3: branch management is Admin-only now; the Manager keeps the
                    // ManageBranches permission but loses the Branches module and nav.
                    Check("manager CANNOT manage branches (Admin-only)", !managerForm.HasBranchManagement(), "no ManageBranches");
                    Check("manager nav has no Branches", !managerForm.DescribeNav().Contains("Branches"), managerForm.DescribeNav());
                }

                using (var staffForm = BuildForm("gymstaff"))
                {
                    Check("gymstaff has the Current Branch selector", staffForm.HasCurrentBranchSelector(),
                        staffForm.DescribeCurrentBranchSelector());
                    Check("gymstaff CANNOT manage branches", !staffForm.HasBranchManagement(), "no ManageBranches");
                    Check("gymstaff nav has no Branches", !staffForm.DescribeNav().Contains("Branches"), staffForm.DescribeNav());
                }

                using (var memberForm = BuildForm("member"))
                {
                    Check("member has NO branch selector", !memberForm.HasCurrentBranchSelector(),
                        memberForm.DescribeCurrentBranchSelector());
                    Check("member CANNOT manage branches", !memberForm.HasBranchManagement(), "no ManageBranches");
                }

                // =================================================================================
                // 4. Current Branch switching drives scoped reads (ALL BRANCHES = tenant-wide)
                // =================================================================================
                Write(string.Empty);
                Write("--- 4. Current Branch switching (scoped reads) ---");

                using (var adminForm = BuildForm("admin"))
                {
                    adminForm.SwitchCurrentBranchAsync(null).GetAwaiter().GetResult(); // ALL BRANCHES
                    var all = adminForm.DescribeScopedCountsAsync().GetAwaiter().GetResult();
                    Check("admin ALL BRANCHES sees every branch", all.Contains("members=21") && all.Contains("attendance=17"), all);

                    adminForm.SwitchCurrentBranchAsync(1).GetAwaiter().GetResult(); // BR-MAIN
                    var main = adminForm.DescribeScopedCountsAsync().GetAwaiter().GetResult();
                    Check("admin switched to BR-MAIN sees only branch 1",
                        main.Contains("members=7") && main.Contains("attendance=6") && main.Contains("inquiries=5"), main);

                    adminForm.SwitchCurrentBranchAsync(2).GetAwaiter().GetResult(); // BR-CEBU
                    var cebu = adminForm.DescribeScopedCountsAsync().GetAwaiter().GetResult();
                    Check("admin switched to BR-CEBU sees only branch 2",
                        cebu.Contains("members=8") && cebu.Contains("attendance=6") && cebu.Contains("inquiries=6"), cebu);

                    // Write stamping: current branch wins; ALL BRANCHES falls back to the member's branch.
                    adminForm.SwitchCurrentBranchAsync(1).GetAwaiter().GetResult();
                    Check("stamp uses Current Branch when set", adminForm.ResolveWriteBranchForTest(2) == 1,
                        $"branch={adminForm.ResolveWriteBranchForTest(2)} (member branch 2, current 1)");

                    adminForm.SwitchCurrentBranchAsync(null).GetAwaiter().GetResult();
                    Check("stamp falls back to member branch in ALL BRANCHES", adminForm.ResolveWriteBranchForTest(2) == 2,
                        $"branch={adminForm.ResolveWriteBranchForTest(2)}");
                }

                using (var managerForm = BuildForm("manager"))
                {
                    // Phase 2/3 tightening (replaces the deliberate Phase 8 loosening): a
                    // Manager is pinned to the home branch and may not switch away.
                    managerForm.SwitchCurrentBranchAsync(2).GetAwaiter().GetResult();
                    var refusedCebu = managerForm.DescribeScopedCountsAsync().GetAwaiter().GetResult();
                    Check("manager may NOT switch to BR-CEBU (stays on home BR-MAIN)",
                        managerForm.CurrentBranchIdForTest == 1
                        && refusedCebu.Contains("members=7") && refusedCebu.Contains("attendance=6"),
                        $"branch={managerForm.CurrentBranchIdForTest} {refusedCebu}");
                }

                using (var staffForm = BuildForm("gymstaff"))
                {
                    // Phase 2/3: GymStaff is pinned to the home branch as well.
                    staffForm.SwitchCurrentBranchAsync(1).GetAwaiter().GetResult();
                    var refusedMain = staffForm.DescribeScopedCountsAsync().GetAwaiter().GetResult();
                    Check("gymstaff may NOT switch to BR-MAIN (stays on home BR-CEBU)",
                        staffForm.CurrentBranchIdForTest == 2 && refusedMain.Contains("members=8"),
                        $"branch={staffForm.CurrentBranchIdForTest} {refusedMain}");
                }

                // =================================================================================
                // 5. Write stamping end-to-end (temp member + attendance, cleaned up)
                // =================================================================================
                Write(string.Empty);
                Write("--- 5. Write stamping + branch access guard (temporary rows) ---");

                var tempMember = new Member
                {
                    MemberCode = tempMemberCode,
                    FullName = "Phase 8 Temporary Member",
                    Email = "phase8.temp@example.invalid",
                    IsActive = false,
                    CreatedAt = DateTime.UtcNow,
                    BranchId = 1
                };
                db.Members.Add(tempMember);
                db.SaveChanges();
                tempMemberId = tempMember.MemberId;

                using (var adminForm = BuildForm("admin"))
                {
                    adminForm.SwitchCurrentBranchAsync(1).GetAwaiter().GetResult();
                    var d1 = DateTime.Today.AddDays(-1000);
                    var r1 = adminForm.RecordAttendanceForTestAsync(tempMemberId, d1).GetAwaiter().GetResult();
                    var stored1 = db.Attendances.AsNoTracking().FirstOrDefault(a => a.MemberId == tempMemberId)?.BranchId;
                    Check("attendance stamped with the Current Branch (BR-MAIN)",
                        r1 == "inserted:1" && stored1 == 1, $"result={r1}, stored={stored1}");

                    // Branch access guard: a different current branch cannot touch this member.
                    adminForm.SwitchCurrentBranchAsync(2).GetAwaiter().GetResult();
                    var blocked = false;
                    try
                    {
                        adminForm.RecordAttendanceForTestAsync(tempMemberId, DateTime.Today.AddDays(-999)).GetAwaiter().GetResult();
                    }
                    catch (UnauthorizedAccessException)
                    {
                        blocked = true;
                    }

                    Check("cross-branch write blocked (EnsureBranchAccess)", blocked, blocked ? "UnauthorizedAccessException" : "NOT blocked");

                    // ALL BRANCHES falls back to the member's branch for the stamp.
                    adminForm.SwitchCurrentBranchAsync(null).GetAwaiter().GetResult();
                    var d2 = DateTime.Today.AddDays(-998);
                    var r2 = adminForm.RecordAttendanceForTestAsync(tempMemberId, d2).GetAwaiter().GetResult();
                    var stored2 = db.Attendances.AsNoTracking()
                        .FirstOrDefault(a => a.MemberId == tempMemberId && a.CheckInDate >= d2 && a.CheckInDate < d2.AddDays(1))?.BranchId;
                    Check("ALL BRANCHES stamp falls back to member branch",
                        r2 == "inserted:1" && stored2 == 1, $"result={r2}, stored={stored2}");
                }

                // =================================================================================
                // 6. Member transfer with historical preservation
                // =================================================================================
                Write(string.Empty);
                Write("--- 6. Member transfer (history preserved) ---");

                using (var adminForm = BuildForm("admin"))
                {
                    adminForm.SwitchCurrentBranchAsync(null).GetAwaiter().GetResult();
                    var result = adminForm.TransferMemberForTestAsync(tempMemberId, 3).GetAwaiter().GetResult();

                    var movedMemberBranch = db.Members.AsNoTracking().First(m => m.MemberId == tempMemberId).BranchId;
                    Check("member moved to BR-DAVAO", movedMemberBranch == 3, $"branch={movedMemberBranch} ({result})");

                    var historyBranch = db.Attendances.AsNoTracking()
                        .Where(a => a.MemberId == tempMemberId)
                        .Select(a => a.BranchId)
                        .Distinct()
                        .ToList();
                    Check("historical attendance keeps its original branch",
                        historyBranch.Count == 1 && historyBranch[0] == 1,
                        $"history branches={string.Join(",", historyBranch)}");
                }

                // =================================================================================
                // 7. Soft-deactivation: inactive branch cannot be selected / written to
                // =================================================================================
                Write(string.Empty);
                Write("--- 7. Soft deactivation protection ---");

                var tempBranch = new Branch
                {
                    BranchCode = tempBranchCode,
                    BranchName = "Phase 8 Temporary Branch",
                    City = "Test City",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.Branches.Add(tempBranch);
                db.SaveChanges();
                tempBranchId = tempBranch.BranchId;

                using (var adminForm = BuildForm("admin"))
                {
                    var selected = adminForm.SwitchCurrentBranchAsync(tempBranchId.Value).GetAwaiter().GetResult();
                    Check("new active branch is selectable", selected, $"branchId={tempBranchId}");

                    // Deactivate while it is the current branch.
                    var tracked = db.Branches.First(b => b.BranchId == tempBranchId.Value);
                    tracked.IsActive = false;
                    db.SaveChanges();

                    var guardThrew = false;
                    try
                    {
                        adminForm.EnsureCurrentBranchActiveForTestAsync().GetAwaiter().GetResult();
                    }
                    catch (InvalidOperationException)
                    {
                        guardThrew = true;
                    }

                    Check("inactive current branch blocks new writes", guardThrew,
                        guardThrew ? "InvalidOperationException" : "NOT blocked");
                }

                var activeCount = db.Branches.AsNoTracking().Count(b => b.IsActive);
                Check("deactivation removed the branch from active selection", activeCount == 3, $"active={activeCount}");

                // =================================================================================
                // 8. Tenant A / Tenant B / master unchanged (read-only regression)
                // =================================================================================
                Write(string.Empty);
                Write("--- 8. Tenant A / Tenant B / master unchanged ---");

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
                    var dbs = Convert.ToInt64(new Microsoft.Data.SqlClient.SqlCommand("SELECT COUNT(*) FROM dbo.CompanyDatabase", master).ExecuteScalar());
                    Check("master unchanged (Companies=3, CompanyDatabase=3)", companies == 3 && dbs == 3,
                        $"Companies={companies}, CompanyDatabase={dbs}");
                }
            }
            catch (Exception ex)
            {
                Write($"  [FAIL] harness: {ex.GetType().Name}: {ex.GetBaseException().Message}");
                failures.Add($"harness: {ex.GetType().Name}: {ex.GetBaseException().Message}");
            }
            finally
            {
                // ---- mandatory cleanup: remove every temporary row created above ----
                // Raw SQL is used so cleanup works even if the change tracker is in a bad state.
                try
                {
                    db.ChangeTracker.Clear();
                    PurgeTempRows();
                }
                catch (Exception ex)
                {
                    Write($"  [WARN] cleanup: {ex.GetType().Name}: {ex.GetBaseException().Message}");
                    failures.Add($"cleanup: {ex.GetType().Name}: {ex.GetBaseException().Message}");
                }
            }

            // =================================================================================
            // 9. Baseline restored after the temporary writes
            // =================================================================================
            Write(string.Empty);
            Write("--- 9. Baseline restored ---");
            var finalBranches = db.Branches.AsNoTracking().Count();
            var finalMembers = db.Members.AsNoTracking().Count();
            var finalAttendance = db.Attendances.AsNoTracking().Count();
            Check("temporary branch removed (3 branches)",
                finalBranches == 3, $"branches={finalBranches}");
            Check("temporary member removed (back to captured live baseline)",
                finalMembers == membersBaseline, $"members={finalMembers} (captured baseline {membersBaseline})");
            Check("attendance back to the captured live baseline",
                finalAttendance == attendanceBaseline, $"attendance={finalAttendance} (captured baseline {attendanceBaseline})");
            Check("BR-DAVAO back to its live baseline (no transfer residue)",
                db.Members.AsNoTracking().Count(m => m.BranchId == 3) == branch3MembersBaseline,
                $"branch3 members={db.Members.AsNoTracking().Count(m => m.BranchId == 3)} (captured baseline {branch3MembersBaseline})");

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
