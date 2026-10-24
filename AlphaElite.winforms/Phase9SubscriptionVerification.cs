using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 9 headless acceptance verification for the Alpha Elite SOFTWARE subscription /
    /// module management feature.
    ///
    /// Covers: master schema + seed shape, company plan assignments, effective statuses and
    /// dates, master-only write authority, per-plan module gating in the existing Form1
    /// navigation, RBAC / member self-only / branch isolation / branch switching regression,
    /// tenant member-subscription separation, Expired / Suspended / Reactivated behaviour,
    /// duplicate guards and the Tenant A / B / C + master fingerprints. Every temporary master
    /// subscription mutation is saved and restored; the run leaves the approved demo state
    /// (COMP001=BASIC, COMP002=PROFESSIONAL, COMP003=ENTERPRISE, all Active).
    /// </summary>
    internal static class Phase9SubscriptionVerification
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
            Write("PHASE 9 SUBSCRIPTION / MODULE MANAGEMENT VERIFICATION");
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
            var tenantC = tenants.First(t => t.IsBranchAware);
            var tenantA = tenants.First(t => !t.IsBranchAware && t.CompanyCode == "COMP001");
            var tenantB = tenants.First(t => !t.IsBranchAware && t.CompanyCode == "COMP002");

            // Capture the live Tenant A baseline before the subscription writes: check 35
            // compares against this capture instead of hardcoded counts (manual testing
            // may legitimately change live data between runs).
            int aMembersStart, aAttendanceStart, aPaymentsStart;
            decimal aPayTotalStart;
            using (var dbA0 = runtime.DataContextFactory.CreateAsync(tenantA.CompanyId, false).GetAwaiter().GetResult())
            {
                aMembersStart = dbA0.Members.Count();
                aAttendanceStart = dbA0.Attendances.Count();
                aPaymentsStart = dbA0.Payments.Count();
                aPayTotalStart = dbA0.Payments.Sum(p => p.AmountPaid);
            }

            // Tenant B gets the same live-baseline treatment (manual testing may shift its
            // counts between runs; check 36 compares against this capture).
            int bMembersStart, bAttendanceStart, bPaymentsStart;
            decimal bPayTotalStart;
            using (var dbB0 = runtime.DataContextFactory.CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult())
            {
                bMembersStart = dbB0.Members.Count();
                bAttendanceStart = dbB0.Attendances.Count();
                bPaymentsStart = dbB0.Payments.Count();
                bPayTotalStart = dbB0.Payments.Sum(p => p.AmountPaid);
            }

            // Tenant C complaint rows are externally mutable (the Phase 6 demo complaints
            // were deleted outside this project's runs), so their count and open-by-branch
            // split are a start-of-run baseline; the subscription writes never touch
            // complaints, so the end-of-run fingerprint must equal this capture.
            int cComplaintsStart;
            string cOpenByBranchStart;
            using (var dbC0 = runtime.DataContextFactory.CreateAsync(tenantC.CompanyId, true).GetAwaiter().GetResult())
            {
                cComplaintsStart = dbC0.Complaints.AsNoTracking().Count();
                cOpenByBranchStart = string.Join("/",
                    dbC0.Branches.AsNoTracking().OrderBy(b => b.BranchId).Select(b =>
                        dbC0.Complaints.AsNoTracking().Count(c => c.BranchId == b.BranchId && c.Status != "Resolved" && c.Status != "Closed")));
            }

            ApplicationConfiguration.Initialize();
            SynchronizationContext.SetSynchronizationContext(null);

            var masterDb = runtime.MasterDb;
            var service = runtime.SubscriptionService;

            // ---------------------------------------------------------------------------------
            // Original demo state (restored in finally, verified at the end).
            // ---------------------------------------------------------------------------------
            var originalSubs = masterDb.CompanySubscriptions.AsNoTracking()
                .Where(s => s.CompanyId == tenantA.CompanyId
                         || s.CompanyId == tenantB.CompanyId
                         || s.CompanyId == tenantC.CompanyId)
                .ToDictionary(s => s.CompanySubscriptionId, s => (s.CompanyId, s.SubscriptionPlanId, s.Status, s.StartDate, s.EndDate));

            var planIdByCode = masterDb.SubscriptionPlans.AsNoTracking().ToDictionary(p => p.Code, p => p.SubscriptionPlanId);

            void RestoreDemoState()
            {
                foreach (var sub in masterDb.CompanySubscriptions.ToList())
                {
                    if (!originalSubs.TryGetValue(sub.CompanySubscriptionId, out var original))
                    {
                        continue;
                    }

                    sub.SubscriptionPlanId = original.SubscriptionPlanId;
                    sub.Status = original.Status;
                    sub.StartDate = original.StartDate;
                    sub.EndDate = original.EndDate;
                    sub.UpdatedAt = DateTime.UtcNow;
                }

                masterDb.SaveChanges();
            }

            try
            {
                // =============================================================================
                // A. Master DB connection and schema
                // =============================================================================
                Write(string.Empty);
                Write("--- A. Master DB connection / schema ---");

                var masterDatabase = masterDb.Database.GetDbConnection().Database;
                Check("1. master DB connection targets db67598", masterDatabase.Contains("67598"), masterDatabase);

                var masterTables = masterDb.Database
                    .SqlQueryRaw<string>("SELECT name AS [Value] FROM sys.tables WHERE name NOT LIKE '\\_\\_EF%' ESCAPE '\\' ORDER BY name")
                    .ToList();

                foreach (var expected in new[] { "SubscriptionPlans", "SoftwareModules", "PlanModules", "CompanySubscriptions" })
                {
                    Check($"new table {expected} exists", masterTables.Contains(expected), string.Join(", ", masterTables));
                }

                var preExisting = new[] { "AspNetRoleClaims", "AspNetRoles", "AspNetUserClaims", "AspNetUserLogins", "AspNetUserRoles", "AspNetUsers", "AspNetUserTokens", "Companies", "CompanyDatabase", "Devices" };
                // Subscription-request phase: SubscriptionHistory (audit) and
                // SubscriptionChangeRequest (OPTION 2 approval workflow) are approved master
                // additions; the allow-list is extended, never used to hide other changes.
                // Plan-versioning phase: SubscriptionPlanVersions holds one immutable snapshot
                // per plan version (terms/price/billing cycle/status) and
                // SubscriptionPlanVersionModules holds that version's own immutable module set
                // (old versions are never reconstructed from the live PlanModules table). Both
                // are approved additive master tables created by migration
                // 20261007000000_AddPlanVersioning; nothing else is exempted.
                var unexpected = masterTables.Except(preExisting.Concat(new[]
                {
                    "SubscriptionPlans", "SoftwareModules", "PlanModules", "CompanySubscriptions",
                    "SubscriptionHistory", "SubscriptionChangeRequest",
                    "SubscriptionPlanVersions", "SubscriptionPlanVersionModules"
                })).ToList();
                Check("no unexpected master tables were created", unexpected.Count == 0,
                    unexpected.Count == 0 ? "only the approved subscription tables were added" : "unexpected: " + string.Join(", ", unexpected));

                // =============================================================================
                // B. Seed shape
                // =============================================================================
                Write(string.Empty);
                Write("--- B. Plans / modules / mappings ---");

                var plans = masterDb.SubscriptionPlans.AsNoTracking().OrderBy(p => p.SubscriptionPlanId).ToList();
                var modules = masterDb.SoftwareModules.AsNoTracking().OrderBy(m => m.ModuleId).ToList();
                var mappings = masterDb.PlanModules.AsNoTracking().ToList();

                // Final subscription system: EXACTLY the three plans (no legacy launch plans).
                Check("2. exactly 3 plans (BASIC, PROFESSIONAL, ENTERPRISE)",
                    plans.Count == 3
                    && plans.Select(p => p.Code).OrderBy(c => c, StringComparer.Ordinal)
                        .SequenceEqual(new[] { SoftwarePlanCodes.Basic, SoftwarePlanCodes.Professional, SoftwarePlanCodes.Enterprise }
                            .OrderBy(c => c, StringComparer.Ordinal)),
                    string.Join(", ", plans.Select(p => p.Code)));
                Check("3. exactly 16 modules", modules.Count == 16, string.Join(", ", modules.Select(m => m.Code)));

                var expectedCodes = SoftwareModuleCodes.All.ToHashSet();
                Check("3b. module codes are exactly the approved 16",
                    modules.Select(m => m.Code).OrderBy(c => c).SequenceEqual(expectedCodes.OrderBy(c => c)),
                    string.Join(",", modules.Select(m => m.Code)));

                var planCodes = plans.Select(p => p.Code).ToHashSet();
                // Final subscription system: the three plans and nothing else.
                Check("3c. plan codes are exactly the three final plans (no legacy launch plans)",
                    new[] { SoftwarePlanCodes.Basic, SoftwarePlanCodes.Professional, SoftwarePlanCodes.Enterprise }
                        .All(c => planCodes.Contains(c))
                    && planCodes.Count == 3,
                    string.Join(",", planCodes));

                var matrix = SubscriptionService.GetPlanModuleMatrix();

                int MappingCount(string code) =>
                    mappings.Count(pm => pm.SubscriptionPlanId == planIdByCode[code] && pm.IsActive);

                Check("5. Basic has 8 modules", MappingCount(SoftwarePlanCodes.Basic) == 8, $"count={MappingCount(SoftwarePlanCodes.Basic)}");
                Check("6. Professional has 12 modules", MappingCount(SoftwarePlanCodes.Professional) == 12, $"count={MappingCount(SoftwarePlanCodes.Professional)}");
                Check("7. Enterprise has 16 modules", MappingCount(SoftwarePlanCodes.Enterprise) == 16, $"count={MappingCount(SoftwarePlanCodes.Enterprise)}");

                var basicSet = mappings.Where(pm => pm.SubscriptionPlanId == planIdByCode[SoftwarePlanCodes.Basic]).Join(
                    modules, pm => pm.ModuleId, m => m.ModuleId, (pm, m) => m.Code).ToHashSet();
                Check("4. Basic module set is exactly the approved first 8",
                    basicSet.SetEquals(matrix[SoftwarePlanCodes.Basic].ToHashSet()),
                    string.Join(",", basicSet.OrderBy(c => c)));

                var profSet = mappings.Where(pm => pm.SubscriptionPlanId == planIdByCode[SoftwarePlanCodes.Professional]).Join(
                    modules, pm => pm.ModuleId, m => m.ModuleId, (pm, m) => m.Code).ToHashSet();
                Check("4b. Professional module set is exactly the approved first 12",
                    profSet.SetEquals(matrix[SoftwarePlanCodes.Professional].ToHashSet()),
                    string.Join(",", profSet.OrderBy(c => c)));

                var entSet = mappings.Where(pm => pm.SubscriptionPlanId == planIdByCode[SoftwarePlanCodes.Enterprise]).Join(
                    modules, pm => pm.ModuleId, m => m.ModuleId, (pm, m) => m.Code).ToHashSet();
                Check("4c. Enterprise module set is exactly all 16",
                    entSet.SetEquals(expectedCodes), string.Join(",", entSet.OrderBy(c => c)));

                // =============================================================================
                // C. Company subscriptions (via the service the master UI uses)
                // =============================================================================
                Write(string.Empty);
                Write("--- C. Company subscriptions ---");

                var sub1 = service.GetCompanySubscriptionAsync(tenantA.CompanyId).GetAwaiter().GetResult();
                var sub2 = service.GetCompanySubscriptionAsync(tenantB.CompanyId).GetAwaiter().GetResult();
                var sub3 = service.GetCompanySubscriptionAsync(tenantC.CompanyId).GetAwaiter().GetResult();

                Check("8. Company 1 (COMP001) = Basic", sub1?.PlanCode == SoftwarePlanCodes.Basic, sub1 == null ? "(none)" : $"{sub1.PlanCode} {sub1.EffectiveStatus}");
                Check("9. Company 2 (COMP002) = Professional", sub2?.PlanCode == SoftwarePlanCodes.Professional, sub2 == null ? "(none)" : $"{sub2.PlanCode} {sub2.EffectiveStatus}");
                Check("10. Company 3 (COMP003) = Enterprise", sub3?.PlanCode == SoftwarePlanCodes.Enterprise, sub3 == null ? "(none)" : $"{sub3.PlanCode} {sub3.EffectiveStatus}");

                Check("11. all three subscriptions are Active (effective)",
                    sub1?.EffectiveStatus == SubscriptionStatuses.Active
                    && sub2?.EffectiveStatus == SubscriptionStatuses.Active
                    && sub3?.EffectiveStatus == SubscriptionStatuses.Active,
                    $"{sub1?.EffectiveStatus} / {sub2?.EffectiveStatus} / {sub3?.EffectiveStatus}");

                Check("12. demo dates are 2026-10-01 .. 2027-10-01",
                    sub1!.StartDate.Date == new DateTime(2026, 10, 1) && sub1.EndDate.Date == new DateTime(2027, 10, 1)
                    && sub2!.StartDate.Date == new DateTime(2026, 10, 1) && sub2.EndDate.Date == new DateTime(2027, 10, 1)
                    && sub3!.StartDate.Date == new DateTime(2026, 10, 1) && sub3.EndDate.Date == new DateTime(2027, 10, 1),
                    string.Join(" | ", new[] { sub1, sub2, sub3 }.Select(s => $"{s.PlanCode}: {s.StartDate:yyyy-MM-dd}..{s.EndDate:yyyy-MM-dd}")));

                // 13/14: the read path the MASTER UI uses; the master window is only reachable
                // with a MasterUserSession, and SuperAdmin normalizes to MasterAdmin.
                var allRead = service.GetAllCompanySubscriptionsAsync().GetAwaiter().GetResult();
                Check("13. MasterAdmin can read every company subscription",
                    allRead.Count == 3 && allRead.All(r => !string.IsNullOrEmpty(r.PlanCode)),
                    $"rows={allRead.Count}");
                Check("14. SuperAdmin resolves to MasterAdmin (same read authority)",
                    Roles.Normalize("SuperAdmin") == Roles.MasterAdmin,
                    $"normalize(SuperAdmin)={Roles.Normalize("SuperAdmin")}");

                // 15/16: the write path the MASTER UI calls (mutate + restore).
                var change1 = service.ChangePlanAsync(tenantA.CompanyId, SoftwarePlanCodes.Professional).GetAwaiter().GetResult();
                var afterChange = service.GetCompanySubscriptionAsync(tenantA.CompanyId).GetAwaiter().GetResult();
                Check("15. MasterAdmin can change a plan (COMP001 -> PROFESSIONAL)",
                    change1.Success && afterChange?.PlanCode == SoftwarePlanCodes.Professional,
                    change1.Success ? $"now={afterChange?.PlanCode}" : change1.Error);
                service.ChangePlanAsync(tenantA.CompanyId, SoftwarePlanCodes.Basic).GetAwaiter().GetResult();

                var change2 = service.ChangePlanAsync(tenantA.CompanyId, SoftwarePlanCodes.Enterprise).GetAwaiter().GetResult();
                var afterChange2 = service.GetCompanySubscriptionAsync(tenantA.CompanyId).GetAwaiter().GetResult();
                Check("16. SuperAdmin (same service authority) can change a plan (COMP001 -> ENTERPRISE)",
                    change2.Success && afterChange2?.PlanCode == SoftwarePlanCodes.Enterprise,
                    change2.Success ? $"now={afterChange2?.PlanCode}" : change2.Error);
                service.ChangePlanAsync(tenantA.CompanyId, SoftwarePlanCodes.Basic).GetAwaiter().GetResult();

                // 17-20: tenant roles have NO path to subscription writes: the master window is
                // only constructible with a MasterUserSession (compiler-level), the tenant
                // UserSession can never become one, and no tenant role holds TenantManagement.
                var masterCtor = typeof(MasterDashboardForm).GetConstructors()
                    .FirstOrDefault(c => c.GetParameters().Any(p => p.ParameterType == typeof(MasterUserSession)));
                Check("17. tenant Admin cannot modify subscriptions (master window requires MasterUserSession)",
                    masterCtor != null && !typeof(MasterUserSession).IsAssignableFrom(typeof(UserSession))
                        && !runtime.AuthorizationService.IsAuthorized(Roles.Admin, Permission.TenantManagement),
                    "Admin TenantManagement=" + runtime.AuthorizationService.IsAuthorized(Roles.Admin, Permission.TenantManagement));
                Check("18. Manager cannot modify subscriptions",
                    !runtime.AuthorizationService.IsAuthorized(Roles.Manager, Permission.TenantManagement)
                        && !runtime.AuthorizationService.IsAuthorized(Roles.Manager, Permission.AdminPanel),
                    "Manager TenantManagement=" + runtime.AuthorizationService.IsAuthorized(Roles.Manager, Permission.TenantManagement));
                Check("19. GymStaff cannot modify subscriptions",
                    !runtime.AuthorizationService.IsAuthorized(Roles.GymStaff, Permission.TenantManagement)
                        && !runtime.AuthorizationService.IsAuthorized(Roles.GymStaff, Permission.AdminPanel),
                    "GymStaff TenantManagement=" + runtime.AuthorizationService.IsAuthorized(Roles.GymStaff, Permission.TenantManagement));
                Check("20. Member cannot modify subscriptions",
                    !runtime.AuthorizationService.IsAuthorized(Roles.Member, Permission.TenantManagement)
                        && !runtime.AuthorizationService.IsAuthorized(Roles.Member, Permission.ManageUsers),
                    "Member TenantManagement=" + runtime.AuthorizationService.IsAuthorized(Roles.Member, Permission.TenantManagement));

                // =============================================================================
                // D. Module gating in the existing Form1 navigation (Tenant C sessions)
                // =============================================================================
                Write(string.Empty);
                Write("--- D. Plan-based module gating (existing Form1 navigation) ---");

                Form1 BuildForm(string username, string planCode)
                {
                    var db = runtime.ConnectionFactory.CreateBranchAwareOptions(
                        runtime.Resolver.GetDatabaseInfoAsync(tenantC.CompanyId).GetAwaiter().GetResult());
                    using var tenantDb = new TenantCAlphaEliteDbContext(db);
                    var account = tenantDb.UserAccounts.AsNoTracking().First(u => u.Username == username);

                    var enabled = planCode.Length == 0
                        ? Array.Empty<string>()
                        : matrix[planCode];

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
                        Subscription = new UserSubscriptionContext
                        {
                            HasActiveSubscription = true,
                            EffectiveStatus = SubscriptionStatuses.Active,
                            PlanName = planCode,
                            EnabledModuleCodes = enabled
                        }
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

                using (var form = BuildForm("admin", SoftwarePlanCodes.Basic))
                {
                    var nav = form.DescribeNav();
                    var gatedAway = new[] { "Retention", "UserManagement", "Branches" };
                    // Tenant C phase 9: the dashboard nav is labeled "Business Intelligence".
                    var kept = new[] { "Business Intelligence", "Members", "Attendance", "Payments", "Feedback", "Inquiries", "Complaints" };
                    var roleGated = new[] { "Subscriptions" }; // tenant Admin no longer has this permission

                    Check("21. Basic admin nav = Basic modules minus role-gated Subscriptions",
                        kept.All(nav.Contains) && gatedAway.All(g => !nav.Contains(g))
                        && roleGated.All(g => !nav.Contains(g))
                        && form.SubscriptionGatingActive,
                        nav);

                    Check("21b. Basic hides Reports exports and Advanced BI panel",
                        !form.ReportsVisibleForTest && !form.AdvancedBiVisibleForTest,
                        $"exportsVisible={form.ReportsVisibleForTest} branchComparison={form.AdvancedBiVisibleForTest}");

                    Check("21c. Basic module gate denies enterprise module codes",
                        !form.IsModuleEnabledForTest(SoftwareModuleCodes.BusinessIntelligence)
                        && !form.IsModuleEnabledForTest(SoftwareModuleCodes.BranchManagement)
                        && !form.IsModuleEnabledForTest(SoftwareModuleCodes.MemberPortal)
                        && !form.IsModuleEnabledForTest(SoftwareModuleCodes.AdvancedRetention),
                        form.DescribeSubscriptionGating());
                }

                using (var form = BuildForm("admin", SoftwarePlanCodes.Professional))
                {
                    var nav = form.DescribeNav();
                    var proKept = new[] { "Business Intelligence", "Members", "Attendance", "Payments", "Feedback", "Inquiries", "Retention", "Complaints", "UserManagement" };
                    var proAway = new[] { "Branches", "Subscriptions" }; // Subscriptions is role-gated for tenant Admin

                    Check("22. Professional admin nav = role-filtered 12-module set (no Branches, no Subscriptions)",
                        proKept.All(nav.Contains) && proAway.All(g => !nav.Contains(g)),
                        nav);

                    Check("22b. Professional shows Reports exports but not Advanced BI panel",
                        form.ReportsVisibleForTest && !form.AdvancedBiVisibleForTest,
                        $"exportsVisible={form.ReportsVisibleForTest} branchComparison={form.AdvancedBiVisibleForTest}");
                }

                using (var form = BuildForm("admin", SoftwarePlanCodes.Enterprise))
                {
                    var nav = form.DescribeNav();
                    var entKept = new[] { "Business Intelligence", "Members", "Attendance", "Payments", "Feedback", "Inquiries", "Retention", "Complaints", "UserManagement", "Branches" };

                    Check("23. Enterprise admin nav includes every module incl. Branches (Subscriptions still role-gated)",
                        entKept.All(nav.Contains) && !nav.Contains("Subscriptions"), nav);

                    Check("23b. Enterprise shows Reports exports and Advanced BI panel",
                        form.ReportsVisibleForTest && form.AdvancedBiVisibleForTest,
                        $"exportsVisible={form.ReportsVisibleForTest} branchComparison={form.AdvancedBiVisibleForTest}");

                    Check("23c. Enterprise module gate allows all 16 codes",
                        SoftwareModuleCodes.All.All(c => form.IsModuleEnabledForTest(c)),
                        form.DescribeSubscriptionGating());
                }

                // =============================================================================
                // E. RBAC / member self-only / branch isolation / branch switching regression
                // =============================================================================
                Write(string.Empty);
                Write("--- E. Existing authorization + branching regression (Enterprise) ---");

                Check("24. RBAC: GymStaff cannot manage users or subscriptions; Subscription is MasterAdmin-only",
                    !runtime.AuthorizationService.IsAuthorized(Roles.GymStaff, Permission.ManageUsers)
                    && !runtime.AuthorizationService.IsAuthorized(Roles.Manager, Permission.ManageSubscriptions)
                    && !runtime.AuthorizationService.IsAuthorized(Roles.Admin, Permission.ManageSubscriptions)
                    && runtime.AuthorizationService.IsAuthorized(Roles.MasterAdmin, Permission.ManageSubscriptions),
                    "role matrix composition intact");

                using (var memberForm = BuildForm("member", SoftwarePlanCodes.Enterprise))
                {
                    var session = memberForm.DescribeSession();
                    var scoped = memberForm.DescribeScopedCountsAsync().GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);

                    // The memberId scope collapses the member list to exactly the member's own row.
                    Check("25. Member self-only still works (own memberId scope, own rows only)",
                        session.Contains("memberId=") && !session.Contains("memberId=null") && scoped.Contains("members=1"),
                        session + " | " + scoped);
                }

                // 26/27: branch isolation + switching with the Phase 8 dashboard machinery.
                var branchOptions = runtime.ConnectionFactory.CreateBranchAwareOptions(
                    runtime.Resolver.GetDatabaseInfoAsync(tenantC.CompanyId).GetAwaiter().GetResult());
                using var truthDb = new TenantCAlphaEliteDbContext(branchOptions);
                int TruthMembers(int b) => truthDb.Members.AsNoTracking().Count(m => m.BranchId == b);

                using (var adminForm = BuildForm("admin", SoftwarePlanCodes.Enterprise))
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

                    var sequence = new[] { 1, 2, 3, 1 };
                    var sequenceOk = true;
                    var detail = new List<string>();
                    foreach (var branchId in sequence)
                    {
                        Go(branchId);
                        var value = adminForm.KpiValueForTest("total");
                        sequenceOk &= value == TruthMembers(branchId);
                        detail.Add($"{branchId}->{value}/{TruthMembers(branchId)}");
                    }

                    Check("26. branch isolation still correct per branch (KPI totals)", sequenceOk, string.Join(" ", detail));
                    Check("27. repeated branch switching never leaves a stale dashboard", sequenceOk, string.Join(" ", detail));
                }

                // 28: the TENANT member subscription system is untouched and separate.
                var tenantSubCount = truthDb.Subscriptions.AsNoTracking().Count();
                var masterSubTable = masterDb.Model.FindEntityType(typeof(CompanySubscription))!.GetTableName();
                Check("28. Tenant C member subscriptions intact and separate from master CompanySubscriptions",
                    tenantSubCount == 2 && masterSubTable == "CompanySubscriptions",
                    $"tenant member subs={tenantSubCount}, master table={masterSubTable}");

                // =============================================================================
                // F. Expired / Suspended / Reactivated (temporary master state, restored below)
                // =============================================================================
                Write(string.Empty);
                Write("--- F. Status rules (temporary master mutations, restored) ---");

                // 29. Expired: move COMP001's end date into the past.
                var expireResult = service.UpdateDatesAsync(tenantA.CompanyId, new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)).GetAwaiter().GetResult();
                var expired = service.GetCompanySubscriptionAsync(tenantA.CompanyId).GetAwaiter().GetResult();
                Check("29. Expired: EndDate in the past -> EffectiveStatus=Expired, plan modules withdrawn",
                    expireResult.Success && expired?.EffectiveStatus == SubscriptionStatuses.Expired
                        && expired.EnabledModuleCodes.Count == 0,
                    expired == null ? "(none)" : $"{expired.EffectiveStatus} modules={expired.EnabledModuleCodes.Count}");
                Check("29b. expiry deletes nothing (row + plan intact)",
                    expired != null && expired.PlanCode == SoftwarePlanCodes.Basic,
                    $"row still present, plan={expired?.PlanCode}");

                // Expired login: a session whose subscription context carries zero modules must
                // hide every plan-gated module (status text stays informational).
                using (var expiredForm = BuildForm("admin", string.Empty))
                {
                    var nav = expiredForm.DescribeNav();
                    Check("29c. expired subscription hides all plan-gated modules in the tenant window",
                        !expiredForm.IsModuleEnabledForTest(SoftwareModuleCodes.Dashboard)
                        && !expiredForm.IsModuleEnabledForTest(SoftwareModuleCodes.Members)
                        && nav == "(none)",
                        $"{expiredForm.DescribeSubscriptionGating()} | nav=[{nav}]");
                }

                // 30/31. Suspended takes precedence; reactivation lifts it.
                var suspendResult = service.SuspendAsync(tenantB.CompanyId).GetAwaiter().GetResult();
                var suspended = service.GetCompanySubscriptionAsync(tenantB.CompanyId).GetAwaiter().GetResult();
                Check("30. Suspended: explicit state takes precedence, modules withdrawn, nothing deleted",
                    suspendResult.Success && suspended?.EffectiveStatus == SubscriptionStatuses.Suspended
                        && suspended.EnabledModuleCodes.Count == 0 && suspended.PlanCode == SoftwarePlanCodes.Professional,
                    suspended == null ? "(none)" : $"{suspended.EffectiveStatus} plan={suspended.PlanCode}");

                var reactivateResult = service.ReactivateAsync(tenantB.CompanyId).GetAwaiter().GetResult();
                var reactivated = service.GetCompanySubscriptionAsync(tenantB.CompanyId).GetAwaiter().GetResult();
                Check("31. Reactivation lifts suspension (Active again, modules return)",
                    reactivateResult.Success && reactivated?.EffectiveStatus == SubscriptionStatuses.Active
                        && reactivated.EnabledModuleCodes.Count == 12,
                    reactivated == null ? "(none)" : $"{reactivated.EffectiveStatus} modules={reactivated.EnabledModuleCodes.Count}");

                // =============================================================================
                // G. Duplicate guards
                // =============================================================================
                Write(string.Empty);
                Write("--- G. Duplicate guards ---");

                var dupActivePerCompany = masterDb.CompanySubscriptions.AsNoTracking().ToList()
                    .GroupBy(s => s.CompanyId)
                    .Where(g => g.Count(s => s.Status == SubscriptionStatuses.Active) > 1)
                    .Select(g => g.Key)
                    .ToList();
                Check("32. no company has more than one Active subscription", dupActivePerCompany.Count == 0,
                    dupActivePerCompany.Count == 0 ? "clean" : "companies: " + string.Join(",", dupActivePerCompany));

                var moduleCodes = masterDb.SoftwareModules.AsNoTracking().Select(m => m.Code).ToList();
                Check("33. no duplicate module codes", moduleCodes.Count == moduleCodes.Distinct().Count(),
                    $"modules={moduleCodes.Count} distinct={moduleCodes.Distinct().Count()}");

                var mappingPairs = masterDb.PlanModules.AsNoTracking()
                    .Select(pm => new { pm.SubscriptionPlanId, pm.ModuleId }).ToList();
                Check("34. no duplicate PlanModule mappings",
                    mappingPairs.Count == mappingPairs.Distinct().Count(),
                    $"mappings={mappingPairs.Count} distinct={mappingPairs.Distinct().Count()}");

                // =============================================================================
                // H. Fingerprints: Tenant A / Tenant B / Tenant C operational data unchanged
                // =============================================================================
                Write(string.Empty);
                Write("--- H. Tenant A / B / C fingerprints ---");

                using (var dbA = runtime.DataContextFactory.CreateAsync(tenantA.CompanyId, false).GetAwaiter().GetResult())
                {
                    var payA = dbA.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                    Check("35. Tenant A unchanged (start-of-run baseline)",
                        dbA.Members.Count() == aMembersStart && dbA.Attendances.Count() == aAttendanceStart
                            && dbA.Payments.Count() == aPaymentsStart && payA == aPayTotalStart,
                        $"members={dbA.Members.Count()}/{aMembersStart} attendance={dbA.Attendances.Count()}/{aAttendanceStart} payments={dbA.Payments.Count()}/{aPaymentsStart} total={payA}/{aPayTotalStart}");
                }

                using (var dbB = runtime.DataContextFactory.CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult())
                {
                    var payB = dbB.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                    Check("36. Tenant B unchanged (start-of-run baseline)",
                        dbB.Members.Count() == bMembersStart && dbB.Attendances.Count() == bAttendanceStart
                            && dbB.Payments.Count() == bPaymentsStart && payB == bPayTotalStart,
                        $"members={dbB.Members.Count()}/{bMembersStart} attendance={dbB.Attendances.Count()}/{bAttendanceStart} payments={dbB.Payments.Count()}/{bPaymentsStart} total={payB}/{bPayTotalStart}");
                }

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
                    var complaints = truthDb.Complaints.AsNoTracking().Count();
                    var openByBranch = string.Join("/",
                        truthDb.Branches.AsNoTracking().OrderBy(b => b.BranchId).Select(b =>
                            truthDb.Complaints.AsNoTracking().Count(c => c.BranchId == b.BranchId && c.Status != "Resolved" && c.Status != "Closed")));

                    // Approved fingerprint: 21 members (7/8/6), 17 attendance (6/6/5), payment
                    // totals 4000/4150/5500 = 13650, 3 branches, 10 users (the 4 original
                    // accounts plus the 6 branch accounts seeded in the Tenant C phase 7),
                    // 2 member subscriptions.
                    // Complaints (the Phase 6 demo rows) were deleted outside this project's
                    // runs, so the complaint count/open split is the start-of-run baseline.
                    Check("37. Tenant C operational data unchanged (members 7/8/6=21, attendance 17, pay 4000/4150/5500=13650, 3 branches, 10 users, 2 member subs, complaints start-of-run baseline)",
                        members == 21 && attendance == 17 && payTotal == 13650m
                        && payByBranch.SequenceEqual(new[] { 4000, 4150, 5500 })
                        && branches == 3 && users == 10 && memberSubs == 2
                        && complaints == cComplaintsStart && openByBranch == cOpenByBranchStart,
                        $"members={members} attendance={attendance} payByBranch={string.Join("/", payByBranch)} total={payTotal} branches={branches} users={users} memberSubs={memberSubs} complaints={complaints}/{cComplaintsStart} open={openByBranch}/{cOpenByBranchStart}");
                }

                {
                    var companies = masterDb.Companies.AsNoTracking().Count();
                    var databases = masterDb.CompanyDatabase.AsNoTracking().Count();
                    var devices = masterDb.Devices.AsNoTracking().Count();
                    var identityUsers = masterDb.Users.AsNoTracking().Count();
                    Check("master pre-existing data unchanged (Companies=3 CompanyDatabase=3 Devices=1 IdentityUsers=1)",
                        companies == 3 && databases == 3 && devices == 1 && identityUsers == 1,
                        $"Companies={companies} CompanyDatabase={databases} Devices={devices} IdentityUsers={identityUsers}");
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
                    RestoreDemoState();
                }
                catch (Exception ex)
                {
                    Write($"  [WARN] restore: {ex.GetType().Name}: {ex.GetBaseException().Message}");
                    failures.Add($"restore: {ex.GetBaseException().Message}");
                }
            }

            // ---------------------------------------------------------------------------------
            // I. Demo state restored
            // ---------------------------------------------------------------------------------
            Write(string.Empty);
            Write("--- I. Demo state restored ---");

            foreach (var (companyId, code, planCode, expectedModules) in new[]
                     {
                         (tenantA.CompanyId, "COMP001", SoftwarePlanCodes.Basic, 8),
                         (tenantB.CompanyId, "COMP002", SoftwarePlanCodes.Professional, 12),
                         (tenantC.CompanyId, "COMP003", SoftwarePlanCodes.Enterprise, 16)
                     })
            {
                var info = runtime.SubscriptionService.GetCompanySubscriptionAsync(companyId).GetAwaiter().GetResult();
                Check($"demo state: {code} = {planCode} Active 2026-10-01..2027-10-01 ({expectedModules} modules)",
                    info?.PlanCode == planCode
                    && info.EffectiveStatus == SubscriptionStatuses.Active
                    && info.StartDate.Date == new DateTime(2026, 10, 1)
                    && info.EndDate.Date == new DateTime(2027, 10, 1)
                    && info.EnabledModuleCount == expectedModules,
                    info == null ? "(none)" : $"{info.PlanCode} {info.EffectiveStatus} {info.StartDate:yyyy-MM-dd}..{info.EndDate:yyyy-MM-dd} modules={info.EnabledModuleCount}");
            }

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
