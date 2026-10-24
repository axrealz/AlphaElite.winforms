using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 3 verification (read-only): the FINAL subscription plan catalogue.
    ///
    /// The subscription system has EXACTLY THREE plans - BASIC, PROFESSIONAL and ENTERPRISE -
    /// with their approved module matrices. The earlier MICRO_BUSINESS / SMALL_BUSINESS launch
    /// plans were removed from the product, so they must be absent from the master catalogue, the
    /// module mappings, the version history and any subscription or drop-down. The demo company
    /// subscriptions and the Tenant A/B/C isolation fingerprints are verified unchanged.
    /// Never writes.
    /// </summary>
    internal static class Phase3LaunchPlanVerification
    {
        public static int Run()
        {
            var log = new List<string>();
            var failures = new List<string>();
            var checks = 0;

            void Write(string text)
            {
                log.Add(text);
                Console.WriteLine(text);
            }

            void Check(string name, bool ok, string detail)
            {
                checks++;
                Write($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
                if (!ok)
                {
                    failures.Add($"{name}: {detail}");
                }
            }

            try
            {
                var runtime = AppRuntime.Create();
                var masterDb = runtime.MasterDb;
                var service = runtime.SubscriptionService;

                var masterDatabase = masterDb.Database.GetDbConnection().Database;
                Write($"Operation      : phase3-subscription-verify");
                Write($"Target database: {masterDatabase}");
                if (!masterDatabase.Contains("67598"))
                {
                    Write($"[ABORT] Refusing to run: the master connection does not point at db67598 (got '{masterDatabase}').");
                    return 1;
                }

                // Live tenant baselines (same pattern as the phase 9 harness): A and B counts
                // are captured at run start (manual testing may legitimately shift them); the
                // C fingerprint is the approved constant set.
                var tenantA = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == "COMP001");
                var tenantB = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == "COMP002");
                var tenantC = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == "COMP003");

                int aMembersStart, aAttendanceStart, aPaymentsStart;
                decimal aPayTotalStart;
                using (var dbA0 = runtime.DataContextFactory.CreateAsync(tenantA.CompanyId, false).GetAwaiter().GetResult())
                {
                    aMembersStart = dbA0.Members.Count();
                    aAttendanceStart = dbA0.Attendances.Count();
                    aPaymentsStart = dbA0.Payments.Count();
                    aPayTotalStart = dbA0.Payments.Sum(p => p.AmountPaid);
                }

                int bMembersStart, bAttendanceStart, bPaymentsStart;
                decimal bPayTotalStart;
                using (var dbB0 = runtime.DataContextFactory.CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult())
                {
                    bMembersStart = dbB0.Members.Count();
                    bAttendanceStart = dbB0.Attendances.Count();
                    bPaymentsStart = dbB0.Payments.Count();
                    bPayTotalStart = dbB0.Payments.Sum(p => p.AmountPaid);
                }

                int cComplaintsStart;
                string cOpenByBranchStart;
                using (var dbC0 = runtime.DataContextFactory.CreateAsync(tenantC.CompanyId, true).GetAwaiter().GetResult())
                {
                    cComplaintsStart = dbC0.Complaints.AsNoTracking().Count();
                    cOpenByBranchStart = string.Join("/",
                        dbC0.Branches.AsNoTracking().OrderBy(b => b.BranchId).Select(b =>
                            dbC0.Complaints.AsNoTracking().Count(c => c.BranchId == b.BranchId && c.Status != "Resolved" && c.Status != "Closed")));
                }

                Write(string.Empty);
                Write("--- 1. Final plan catalogue (exactly three plans) ---");

                var plans = masterDb.SubscriptionPlans.AsNoTracking().OrderBy(p => p.SubscriptionPlanId).ToList();
                var retiredCodes = new[] { "MICRO_BUSINESS", "SMALL_BUSINESS" };
                var retainedCodes = new[] { SoftwarePlanCodes.Basic, SoftwarePlanCodes.Professional, SoftwarePlanCodes.Enterprise };

                Check("1. exactly 3 SubscriptionPlans rows", plans.Count == 3,
                    $"count={plans.Count}: {string.Join(", ", plans.Select(p => p.Code))}");
                Check("2. the three plans are exactly BASIC, PROFESSIONAL, ENTERPRISE",
                    plans.Select(p => p.Code).OrderBy(c => c, StringComparer.Ordinal)
                        .SequenceEqual(retainedCodes.OrderBy(c => c, StringComparer.Ordinal)),
                    string.Join(", ", plans.Select(p => p.Code)));
                foreach (var code in retainedCodes)
                {
                    Check($"3. {code} exists exactly once and is Active",
                        plans.Count(p => p.Code == code) == 1 && plans.First(p => p.Code == code).IsActive,
                        $"count={plans.Count(p => p.Code == code)}");
                }

                Check("4. MICRO_BUSINESS is completely gone (no active, inactive or hidden row)",
                    plans.Count(p => p.Code == "MICRO_BUSINESS") == 0,
                    $"count={plans.Count(p => p.Code == "MICRO_BUSINESS")}");
                Check("5. SMALL_BUSINESS is completely gone (no active, inactive or hidden row)",
                    plans.Count(p => p.Code == "SMALL_BUSINESS") == 0,
                    $"count={plans.Count(p => p.Code == "SMALL_BUSINESS")}");

                var modules = masterDb.SoftwareModules.AsNoTracking().ToList();
                var mappings = masterDb.PlanModules.AsNoTracking().ToList();
                var retiredPlanIds = plans.Where(p => retiredCodes.Contains(p.Code, StringComparer.Ordinal))
                    .Select(p => p.SubscriptionPlanId).ToHashSet();

                Write(string.Empty);
                Write("--- 2. Module matrices ---");

                var duplicateMappings = mappings
                    .GroupBy(pm => new { pm.SubscriptionPlanId, pm.ModuleId })
                    .Count(g => g.Count() > 1);
                Check("6. no duplicate (PlanId, ModuleId) mappings", duplicateMappings == 0, $"duplicates={duplicateMappings}");

                var unresolved = mappings
                    .Where(pm => !modules.Any(m => m.ModuleId == pm.ModuleId))
                    .Count();
                Check("7. every mapping resolves to an existing SoftwareModule", unresolved == 0, $"unresolved={unresolved}");
                Check("8. no mapping row belongs to a removed plan",
                    !mappings.Any(pm => retiredPlanIds.Contains(pm.SubscriptionPlanId)),
                    $"mappings={mappings.Count}");
                Check("9. the SoftwareModules catalogue is intact (16 modules, none deleted)",
                    modules.Count == 16 && SoftwareModuleCodes.All.All(c => modules.Any(m => m.Code == c)),
                    $"modules={modules.Count}");

                IReadOnlyCollection<string> PlanModules(int planId) =>
                    mappings.Where(pm => pm.SubscriptionPlanId == planId && pm.IsActive)
                        .Join(modules, pm => pm.ModuleId, m => m.ModuleId, (pm, m) => m.Code)
                        .ToList();

                Write(string.Empty);
                Write("--- 3. The three plans keep their approved module sets ---");

                var frozenMatrix = SubscriptionService.GetPlanModuleMatrix();
                var planIdByCode = plans.ToDictionary(p => p.Code, p => p.SubscriptionPlanId);
                foreach (var (code, expectedCodes) in frozenMatrix)
                {
                    var actual = PlanModules(planIdByCode[code]);
                    Check($"10. {code} module set ({expectedCodes.Length})",
                        actual.Count == expectedCodes.Length && expectedCodes.All(c => actual.Contains(c, StringComparer.Ordinal)),
                        $"count={actual.Count}: {string.Join(",", SoftwareModuleCodes.All.Where(c => actual.Contains(c, StringComparer.Ordinal)))}");
                }

                Write(string.Empty);
                Write("--- 4. Company subscriptions + versioned state ---");

                var subs = masterDb.CompanySubscriptions.AsNoTracking().ToList();
                foreach (var (company, planCode) in new[] { (tenantA, SoftwarePlanCodes.Basic), (tenantB, SoftwarePlanCodes.Professional), (tenantC, SoftwarePlanCodes.Enterprise) })
                {
                    var sub = subs.FirstOrDefault(s => s.CompanyId == company.CompanyId);
                    var planName = sub != null ? plans.First(p => p.SubscriptionPlanId == sub.SubscriptionPlanId).Code : "(none)";
                    Check($"11. {company.CompanyCode} -> {planCode} (Active, demo window, snapshot fields untouched)",
                        sub != null && planName == planCode
                        && sub.Status == SubscriptionStatuses.Active
                        && sub.StartDate.Date == new DateTime(2026, 10, 1)
                        && sub.EndDate.Date == new DateTime(2027, 10, 1)
                        && sub.PaymentStatus == PaymentStatuses.NotApplicable,
                        sub == null ? "(none)" : $"{planName} {sub.Status} {sub.StartDate:yyyy-MM-dd}..{sub.EndDate:yyyy-MM-dd} price={sub.Price:0.00} pay={sub.PaymentStatus}");
                }

                var serviceInfos = service.GetAllCompanySubscriptionsAsync().GetAwaiter().GetResult();
                Check("12. subscription service resolves the three demo companies to their plans",
                    serviceInfos.First(i => i.CompanyCode == "COMP001").PlanCode == SoftwarePlanCodes.Basic
                    && serviceInfos.First(i => i.CompanyCode == "COMP002").PlanCode == SoftwarePlanCodes.Professional
                    && serviceInfos.First(i => i.CompanyCode == "COMP003").PlanCode == SoftwarePlanCodes.Enterprise,
                    string.Join(", ", serviceInfos.Select(i => $"{i.CompanyCode}={i.PlanCode}")));

                var versions = masterDb.SubscriptionPlanVersions.AsNoTracking().ToList();
                var versionModules = masterDb.SubscriptionPlanVersionModules.AsNoTracking().ToList();
                Check("13. every retained plan has exactly one current version and a readable 1..n history",
                    plans.All(p =>
                    {
                        var numbers = versions.Where(v => v.SubscriptionPlanId == p.SubscriptionPlanId)
                            .OrderBy(v => v.VersionNumber).Select(v => v.VersionNumber).ToList();
                        return versions.Count(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.IsCurrent) == 1
                            && numbers.Count > 0 && numbers.SequenceEqual(Enumerable.Range(1, numbers.Count));
                    }),
                    string.Join(", ", plans.Select(p => $"{p.Code}=v{versions.Where(v => v.SubscriptionPlanId == p.SubscriptionPlanId).Select(v => v.VersionNumber).DefaultIfEmpty(0).Max()}")));
                Check("14. no version or version-module row remains for a removed plan",
                    !versions.Any(v => retiredPlanIds.Contains(v.SubscriptionPlanId))
                    && !versionModules.Any(m => !versions.Any(v => v.SubscriptionPlanVersionId == m.SubscriptionPlanVersionId)),
                    $"versions={versions.Count} versionModules={versionModules.Count}");
                Check("15. every subscription link points at its OWN plan's current version",
                    subs.All(s => versions.Any(v => v.SubscriptionPlanVersionId == s.SubscriptionPlanVersionId
                        && v.SubscriptionPlanId == s.SubscriptionPlanId && v.IsCurrent)),
                    string.Join(", ", subs.Select(s => $"#{s.CompanySubscriptionId}={s.SubscriptionPlanVersionId?.ToString() ?? "null"}")));

                Check("16. workflow tables still empty (no requests, no history)",
                    masterDb.SubscriptionChangeRequests.AsNoTracking().Count() == 0
                    && masterDb.SubscriptionHistories.AsNoTracking().Count() == 0,
                    $"history={masterDb.SubscriptionHistories.AsNoTracking().Count()} requests={masterDb.SubscriptionChangeRequests.AsNoTracking().Count()}");

                Write(string.Empty);
                Write("--- 5. Tenant A/B/C isolation ---");

                using (var dbA = runtime.DataContextFactory.CreateAsync(tenantA.CompanyId, false).GetAwaiter().GetResult())
                {
                    var payA = dbA.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                    Check("17. Tenant A unchanged",
                        dbA.Members.Count() == aMembersStart && dbA.Attendances.Count() == aAttendanceStart
                        && dbA.Payments.Count() == aPaymentsStart && payA == aPayTotalStart,
                        $"members={dbA.Members.Count()}/{aMembersStart} attendance={dbA.Attendances.Count()}/{aAttendanceStart} payments={dbA.Payments.Count()}/{aPaymentsStart} total={payA}/{aPayTotalStart}");
                }

                using (var dbB = runtime.DataContextFactory.CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult())
                {
                    var payB = dbB.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                    Check("18. Tenant B unchanged",
                        dbB.Members.Count() == bMembersStart && dbB.Attendances.Count() == bAttendanceStart
                        && dbB.Payments.Count() == bPaymentsStart && payB == bPayTotalStart,
                        $"members={dbB.Members.Count()}/{bMembersStart} attendance={dbB.Attendances.Count()}/{bAttendanceStart} payments={dbB.Payments.Count()}/{bPaymentsStart} total={payB}/{bPayTotalStart}");
                }

                using (var truthDb = runtime.DataContextFactory.CreateAsync(tenantC.CompanyId, true).GetAwaiter().GetResult())
                {
                    var members = truthDb.Members.AsNoTracking().Count();
                    var attendance = truthDb.Attendances.AsNoTracking().Count();
                    var payments = truthDb.Payments.AsNoTracking().Count();
                    var payTotal = truthDb.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                    var payByBranch = truthDb.Branches.AsNoTracking().OrderBy(b => b.BranchId)
                        .Select(b => (int)(truthDb.Payments.AsNoTracking().Where(p => p.BranchId == b.BranchId).Sum(p => (decimal?)p.AmountPaid) ?? 0m))
                        .ToList();
                    var branches = truthDb.Branches.AsNoTracking().Count();
                    var users = truthDb.UserAccounts.AsNoTracking().Count();
                    var memberSubs = truthDb.Subscriptions.AsNoTracking().Count();
                    var openByBranch = string.Join("/",
                        truthDb.Branches.AsNoTracking().OrderBy(b => b.BranchId).Select(b =>
                            truthDb.Complaints.AsNoTracking().Count(c => c.BranchId == b.BranchId && c.Status != "Resolved" && c.Status != "Closed")));

                    Check("19. Tenant C unchanged (21 members 7/8/6, attendance 17, pay 4000/4150/5500=13650, 3 branches, 10 users, 2 member subs, complaints start-of-run baseline)",
                        members == 21 && attendance == 17 && payTotal == 13650m
                        && payByBranch.SequenceEqual(new[] { 4000, 4150, 5500 })
                        && branches == 3 && users == 10 && memberSubs == 2
                        && truthDb.Complaints.AsNoTracking().Count() == cComplaintsStart && openByBranch == cOpenByBranchStart,
                        $"members={members} attendance={attendance} payByBranch={string.Join("/", payByBranch)} total={payTotal} branches={branches} users={users} memberSubs={memberSubs} open={openByBranch}/{cOpenByBranchStart}");
                }

                var companies = masterDb.Companies.AsNoTracking().Count();
                var databases = masterDb.CompanyDatabase.AsNoTracking().Count();
                var devices = masterDb.Devices.AsNoTracking().Count();
                var identityUsers = masterDb.Users.AsNoTracking().Count();
                Check("20. master pre-existing data unchanged (Companies=3 CompanyDatabase=3 Devices=1 IdentityUsers=1)",
                    companies == 3 && databases == 3 && devices == 1 && identityUsers == 1,
                    $"Companies={companies} CompanyDatabase={databases} Devices={devices} IdentityUsers={identityUsers}");
            }
            catch (Exception ex)
            {
                Write($"  [FAIL] harness: {ex.GetType().Name}: {ex.GetBaseException().Message}");
                failures.Add($"harness: {ex.GetType().Name}: {ex.GetBaseException().Message}");
            }

            Write(string.Empty);
            Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | checks: {checks} | failures: {failures.Count} ---");
            return failures.Count == 0 ? 0 : 1;
        }
    }
}
