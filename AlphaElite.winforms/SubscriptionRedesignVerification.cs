using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
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
    /// SUPER ADMIN SUBSCRIPTION REDESIGN acceptance battery (master DB only, read-mostly).
    ///
    /// Covers the approved checklist A-L:
    ///   A  Subscription navigation (top-level nav unchanged, 4 sub-areas inside Subscription)
    ///   B  Plan cards (real DB plans, names, price, billing cycle, status, version, effective,
    ///      modules - all read from the master database)
    ///   C  Version 1 initialization of existing plans + idempotency
    ///   D  Version creation (material change only; V1 stays immutable; new module in V2)
    ///   E  Module diff (added / removed)
    ///   F  Terms & Agreement history (old wording preserved, current version updated)
    ///   G  Version history retrievability (plan / version / change / date / by / modules / terms)
    ///   H  Upgrade: the OPTION 2 request -> Pending -> Super Admin approve workflow is intact and
    ///      the approved subscription carries the right plan/version/price/terms snapshot
    ///   I  Downgrade: restricted modules become inaccessible, tenant data is untouched
    ///   J  Drop-down regression (company names / plan names, zero CLR type names)
    ///   K  DbContext concurrency: a deterministic overlapping-operation regression test that
    ///      ACTUALLY exercises the path, plus a non-vacuity control proving the probe detects the
    ///      real "A second operation was started on this context instance" failure mode
    ///   L  UI render through the real navigation path
    ///
    /// The harness restores the master demo state in a finally block and asserts the tenant A/B/C
    /// baselines are unchanged, so tenant functionality is proven untouched.
    /// </summary>
    internal static class SubscriptionRedesignVerification
    {
        private const string Reviewer = "superadmin-version-probe";
        private const string TempPlanCode = "TEMP_VERSION_PROBE";

        public static int Run()
        {
            var failures = new List<string>();
            var checks = 0;

            void Write(string text) => Console.WriteLine(text);

            void Check(string name, bool ok, string detail)
            {
                checks++;
                Write($"  [{(ok ? "PASS" : "FAIL")}] {checks}. {name}: {detail}");
                if (!ok)
                {
                    failures.Add($"{checks}. {name}: {detail}");
                }
            }

            var createdRequestIds = new List<int>();
            var createdHistoryIds = new List<int>();

            try
            {
                ApplicationConfiguration.Initialize();
                System.Threading.SynchronizationContext.SetSynchronizationContext(null);

                var runtime = AppRuntime.Create();
                var masterDb = runtime.MasterDb;
                var service = runtime.SubscriptionService;

                var masterDatabase = masterDb.Database.GetDbConnection().Database;
                Write("Operation      : subscription-redesign-verify");
                Write($"Target database: {masterDatabase}");
                if (!masterDatabase.Contains("67598"))
                {
                    Write($"[ABORT] Refusing to run: the master connection does not point at db67598 (got '{masterDatabase}').");
                    return 1;
                }

                // ---- Tenant baselines (captured at run start; must be identical at the end) ----
                var tenantA = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == "COMP001");
                var tenantB = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == "COMP002");
                var tenantC = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == "COMP003");

                int aMembers, aAttendance, aPayments;
                decimal aPayTotal;
                using (var dbA = runtime.DataContextFactory.CreateAsync(tenantA.CompanyId, false).GetAwaiter().GetResult())
                {
                    aMembers = dbA.Members.AsNoTracking().Count();
                    aAttendance = dbA.Attendances.AsNoTracking().Count();
                    aPayments = dbA.Payments.AsNoTracking().Count();
                    aPayTotal = dbA.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                }

                int bMembers, bAttendance, bPayments;
                decimal bPayTotal;
                using (var dbB = runtime.DataContextFactory.CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult())
                {
                    bMembers = dbB.Members.AsNoTracking().Count();
                    bAttendance = dbB.Attendances.AsNoTracking().Count();
                    bPayments = dbB.Payments.AsNoTracking().Count();
                    bPayTotal = dbB.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                }

                int cMembers, cAttendance, cBranches, cUsers, cSubscriptions;
                decimal cPayTotal;
                using (var dbC = runtime.DataContextFactory.CreateAsync(tenantC.CompanyId, true).GetAwaiter().GetResult())
                {
                    cMembers = dbC.Members.AsNoTracking().Count();
                    cAttendance = dbC.Attendances.AsNoTracking().Count();
                    cBranches = dbC.Branches.AsNoTracking().Count();
                    cUsers = dbC.UserAccounts.AsNoTracking().Count();
                    cSubscriptions = dbC.Subscriptions.AsNoTracking().Count();
                    cPayTotal = dbC.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                }

                var masterCompaniesStart = masterDb.Companies.AsNoTracking().Count();
                var masterPlansStart = masterDb.SubscriptionPlans.AsNoTracking().Count();
                var masterModulesStart = masterDb.SoftwareModules.AsNoTracking().Count();
                var masterMappingsStart = masterDb.PlanModules.AsNoTracking().Count();
                var masterSubscriptionsStart = masterDb.CompanySubscriptions.AsNoTracking().Count();
                var masterHistoryStart = masterDb.SubscriptionHistories.AsNoTracking().Count();
                var masterRequestsStart = masterDb.SubscriptionChangeRequests.AsNoTracking().Count();
                var masterVersionsStart = masterDb.SubscriptionPlanVersions.AsNoTracking().Count();
                var masterVersionModulesStart = masterDb.SubscriptionPlanVersionModules.AsNoTracking().Count();

                Write($"Master baseline: companies={masterCompaniesStart} plans={masterPlansStart} modules={masterModulesStart} " +
                      $"mappings={masterMappingsStart} subscriptions={masterSubscriptionsStart} history={masterHistoryStart} " +
                      $"requests={masterRequestsStart} versions={masterVersionsStart} versionModules={masterVersionModulesStart}");
                Write($"Tenant baseline: A members={aMembers} attendance={aAttendance} payments={aPayments} sum={aPayTotal:0.00} | " +
                      $"B members={bMembers} attendance={bAttendance} payments={bPayments} sum={bPayTotal:0.00} | " +
                      $"C members={cMembers} attendance={cAttendance} branches={cBranches} users={cUsers} subscriptions={cSubscriptions} sum={cPayTotal:0.00}");

                var planRows = masterDb.SubscriptionPlans.AsNoTracking().OrderBy(p => p.SubscriptionPlanId).ToList();
                var moduleRows = masterDb.SoftwareModules.AsNoTracking().OrderBy(m => m.ModuleId).ToList();
                var mappingRows = masterDb.PlanModules.AsNoTracking().Where(pm => pm.IsActive).ToList();

                // ============================================================================
                // A. Subscription navigation
                // ============================================================================
                Write(string.Empty);
                Write("--- A. Subscription navigation ---");

                using var masterForm = new MasterDashboardForm(runtime, new MasterUserSession
                {
                    MasterUserId = "redesign-probe-superadmin",
                    Username = Reviewer,
                    Role = Roles.MasterAdmin,
                    IdentityRoleName = MasterIdentityService.SuperAdminRoleName
                });

                InstallHeadlessContext();
                Check("A1. Super Admin top-level navigation is exactly Subscription / Companies-Tenants / Tenant Database / Business Intelligence / Logout",
                    masterForm.DescribeMasterNav() == "Subscription, Companies/Tenants, TenantDatabase, BusinessIntelligence, Logout",
                    masterForm.DescribeMasterNav());

                // Opening Subscription through its real navigation item.
                var companiesRender = masterForm.RenderSubscriptionCompaniesForTestAsync().GetAwaiter().GetResult();
                InstallHeadlessContext();

                var strip = masterForm.DescribeSubscriptionTabStripForTest();
                Check("A2. Subscription is ONE section with the four sub-areas (no extra top-level items)",
                    strip.Contains("Companies / Tenants:") && strip.Contains("Subscription Requests:")
                    && strip.Contains("Plans & Modules:") && strip.Contains("History:")
                    && strip.Contains("appearance=FlatButtons") && strip.Contains("itemSize=1"),
                    strip);

                var rapid = masterForm.RunOverlappingSubscriptionLoadsForTestAsync().GetAwaiter().GetResult();
                InstallHeadlessContext();
                Check("A3. every sub-area is reachable and loads through the real tab path",
                    rapid.Contains("companies=3") && rapid.Contains("plans=3") && rapid.Contains("planVersions=")
                    && rapid.Contains("cards=") && !rapid.Contains("cards=0") && rapid.Contains("tab=Companies / Tenants"),
                    rapid);

                // ============================================================================
                // B. Plan cards
                // ============================================================================
                Write(string.Empty);
                Write("--- B. Plan cards ---");

                var planLoad = masterForm.LoadPlansForTestAsync().GetAwaiter().GetResult();
                InstallHeadlessContext();
                var cards = masterForm.DescribePlanCardsForTest();
                InstallHeadlessContext();
                Write("    .. " + planLoad);

                Check("B1. one card per plan, rendered dark, with no CLR type name anywhere on them",
                    cards.Contains($"cards={planRows.Count}") && cards.Contains("light=0") && cards.Contains("typename=0")
                    && !cards.Contains("AlphaElite.") && !cards.Contains("SubscriptionPlanInfo"),
                    cards);

                var missingNames = planRows.Where(p => !cards.Contains("[" + p.Name + "]")).Select(p => p.Code).ToList();
                Check("B2. every card shows the plan NAME read from the master database",
                    missingNames.Count == 0,
                    missingNames.Count == 0 ? string.Join(", ", planRows.Select(p => p.Name)) : "missing: " + string.Join(", ", missingNames));

                var missingPrices = planRows
                    .Where(p => !cards.Contains($"{p.Price:0.00}") || !cards.Contains(p.BillingCycle))
                    .Select(p => p.Code)
                    .ToList();
                Check("B3. every card shows the plan price and billing cycle",
                    missingPrices.Count == 0,
                    missingPrices.Count == 0
                        ? string.Join(", ", planRows.Select(p => $"{p.Code}={p.Price:0.00}/{p.BillingCycle}"))
                        : "missing: " + string.Join(", ", missingPrices));

                var statusProblems = planRows
                    .Where(p => !cards.Contains("Status: " + (p.IsActive ? "Active" : "Inactive")))
                    .Select(p => p.Code)
                    .ToList();
                Check("B4. every card shows Active / Inactive exactly as stored",
                    statusProblems.Count == 0,
                    statusProblems.Count == 0 ? "all plans match IsActive" : "missing: " + string.Join(", ", statusProblems));

                var currentVersions = masterDb.SubscriptionPlanVersions.AsNoTracking().Where(v => v.IsCurrent).ToList();
                var versionProblems = planRows
                    .Where(p =>
                    {
                        var current = currentVersions.FirstOrDefault(v => v.SubscriptionPlanId == p.SubscriptionPlanId);
                        return current == null
                            || !cards.Contains("Version: " + current.VersionNumber)
                            || !cards.Contains("Effective: " + current.EffectiveDate.ToString("MMM d, yyyy"));
                    })
                    .Select(p => p.Code)
                    .ToList();
                Check("B5. every card shows the current version number and effective date",
                    versionProblems.Count == 0,
                    versionProblems.Count == 0
                        ? string.Join(", ", planRows.Select(p => p.Code + "=v" + currentVersions.First(v => v.SubscriptionPlanId == p.SubscriptionPlanId).VersionNumber))
                        : "missing: " + string.Join(", ", versionProblems));

                var moduleProblems = planRows
                    .Where(p => !cards.Contains($"Included modules ({mappingRows.Count(m => m.SubscriptionPlanId == p.SubscriptionPlanId)})"))
                    .Select(p => p.Code)
                    .ToList();
                Check("B6. every card lists the modules included in that plan (count matches the mapping)",
                    moduleProblems.Count == 0,
                    moduleProblems.Count == 0
                        ? string.Join(", ", planRows.Select(p => p.Code + "=" + mappingRows.Count(m => m.SubscriptionPlanId == p.SubscriptionPlanId)))
                        : "missing: " + string.Join(", ", moduleProblems));

                var editor = masterForm.SelectPlanForTest(SoftwarePlanCodes.Enterprise);
                InstallHeadlessContext();
                var editorVersion = masterForm.DescribePlanEditorVersionForTest();
                Check("B7. the card's Edit action opens the plan editor with its version / status / effective header",
                    editor.Contains("plan=ENTERPRISE") && editor.Contains("modules=16/16")
                    && editorVersion.Contains("Current version: v")
                    && editorVersion.Contains("Status: Active"),
                    editor + " || " + editorVersion);

                // (the theme probe rebuilds the Subscription panel, so it runs at the END - L6)

            // ============================================================================
            // C. Version 1 initialization (existing plans) + idempotency
            // ============================================================================
            Write(string.Empty);
            Write("--- C. Version 1 initialization ---");

            var versionsBeforeReInit = masterDb.SubscriptionPlanVersions.AsNoTracking().Count();
            var versionModulesBeforeReInit = masterDb.SubscriptionPlanVersionModules.AsNoTracking().Count();

            var reInit = service.EnsureInitialPlanVersionsAsync("idempotency-probe").GetAwaiter().GetResult();
            InstallHeadlessContext();

            var versionsAfterReInit = masterDb.SubscriptionPlanVersions.AsNoTracking().Count();
            var versionModulesAfterReInit = masterDb.SubscriptionPlanVersionModules.AsNoTracking().Count();

            Check("C1. every existing plan has at least one version (initialized or created here)",
                planRows.All(p => masterDb.SubscriptionPlanVersions.AsNoTracking().Any(v => v.SubscriptionPlanId == p.SubscriptionPlanId)),
                $"plans={planRows.Count} versions={versionsAfterReInit}");

            Check("C2. version 1 of every plan = its CURRENT configuration (price / cycle / terms / modules)",
                planRows.All(p =>
                {
                    var v1 = masterDb.SubscriptionPlanVersions.AsNoTracking()
                        .FirstOrDefault(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.VersionNumber == 1);
                    if (v1 == null || v1.Price != p.Price || v1.BillingCycle != p.BillingCycle
                        || v1.TermsAndAgreement != p.TermsAndAgreement || v1.PlanName != p.Name)
                    {
                        return false;
                    }

                    var expectedModules = mappingRows.Count(m => m.SubscriptionPlanId == p.SubscriptionPlanId);
                    var actualModules = masterDb.SubscriptionPlanVersionModules.AsNoTracking()
                        .Count(m => m.SubscriptionPlanVersionId == v1.SubscriptionPlanVersionId);
                    return expectedModules == actualModules;
                }),
                $"checked {planRows.Count} plans (v1 price/cycle/terms/name/module-count vs live plan)");

            Check("C3. the initializer is IDEMPOTENT: re-running creates no version and no duplicate row",
                reInit.Success && reInit.VersionsCreated == 0
                && versionsAfterReInit == versionsBeforeReInit
                && versionModulesAfterReInit == versionModulesBeforeReInit,
                $"created={reInit.VersionsCreated} alreadyVersioned={reInit.PlansAlreadyVersioned} " +
                $"versions {versionsBeforeReInit}->{versionsAfterReInit} versionModules {versionModulesBeforeReInit}->{versionModulesAfterReInit}");

            Check("C4. exactly one CURRENT version per plan (never two ambiguous current versions)",
                planRows.All(p => masterDb.SubscriptionPlanVersions.AsNoTracking()
                    .Count(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.IsCurrent) == 1),
                string.Join(", ", planRows.Select(p => p.Code + "=" + masterDb.SubscriptionPlanVersions.AsNoTracking()
                    .Count(v => v.SubscriptionPlanId == p.SubscriptionPlanId && v.IsCurrent))));

            Check("C5. re-running the initializer never duplicates plans / modules / companies / subscriptions",
                masterDb.SubscriptionPlans.AsNoTracking().Count() == masterPlansStart
                && masterDb.SoftwareModules.AsNoTracking().Count() == masterModulesStart
                && masterDb.Companies.AsNoTracking().Count() == masterCompaniesStart
                && masterDb.CompanySubscriptions.AsNoTracking().Count() == masterSubscriptionsStart,
                $"plans={masterDb.SubscriptionPlans.AsNoTracking().Count()}/{masterPlansStart} " +
                $"modules={masterDb.SoftwareModules.AsNoTracking().Count()}/{masterModulesStart} " +
                $"companies={masterDb.Companies.AsNoTracking().Count()}/{masterCompaniesStart} " +
                $"subscriptions={masterDb.CompanySubscriptions.AsNoTracking().Count()}/{masterSubscriptionsStart}");

            // ============================================================================
            // D + E + F + G. Version creation, module diff, terms history, retrievability
            // ============================================================================
            Write(string.Empty);
            Write("--- D/E/F/G. Plan configuration versioning (isolated temp plan) ---");

            // A dedicated plan keeps the change sequence observable without touching the frozen
            // demo plans or any company subscription. A left-over probe plan from an aborted run
            // is removed first (its version snapshots go with it - plan rows are never deleted by
            // the application itself, only by this battery's cleanup).
            var tempPlanExisting = masterDb.SubscriptionPlans.FirstOrDefault(p => p.Code == TempPlanCode);
            if (tempPlanExisting != null)
            {
                masterDb.PlanModules.RemoveRange(masterDb.PlanModules.Where(pm => pm.SubscriptionPlanId == tempPlanExisting.SubscriptionPlanId));
                masterDb.SubscriptionPlans.Remove(tempPlanExisting);
                masterDb.SaveChanges();
            }

            const decimal tempPrice = 250.00m;
            const string tempTermsV1 = "TEMPORARY probe plan - version 1 terms (original wording).";
            const string tempTermsV2 = "TEMPORARY probe plan - version 1 terms REWRITTEN as version 2 wording.";

            var createTemp = service.CreatePlanAsync(
                TempPlanCode, "Temp Version Probe", "Verification-only plan (removed again by this battery).",
                tempPrice, BillingCycles.Monthly, tempTermsV1, changedBy: Reviewer).GetAwaiter().GetResult();
            InstallHeadlessContext();

            var tempPlan = masterDb.SubscriptionPlans.AsNoTracking().First(p => p.Code == TempPlanCode);
            var tempVersions = service.GetPlanVersionsAsync(tempPlan.SubscriptionPlanId).GetAwaiter().GetResult();
            InstallHeadlessContext();

            Check("D1. creating a plan creates exactly its Version 1 (recorded against the acting identity)",
                createTemp.Success && tempVersions.Count == 1
                && tempVersions[0].VersionNumber == 1 && tempVersions[0].IsCurrent
                && tempVersions[0].ChangedBy == Reviewer,
                $"versions={tempVersions.Count} v1current={tempVersions.FirstOrDefault()?.IsCurrent} by='{tempVersions.FirstOrDefault()?.ChangedBy}'");

            // Material change 1: add a module.
            const string addedModuleCode = SoftwareModuleCodes.Feedback;
            var addModule = service.AddModuleToPlanAsync(tempPlan.SubscriptionPlanId, addedModuleCode, changedBy: Reviewer).GetAwaiter().GetResult();
            InstallHeadlessContext();

            var afterAdd = service.GetPlanVersionsAsync(tempPlan.SubscriptionPlanId).GetAwaiter().GetResult();

            Check("D2. adding a module creates Version 2 with the new module",
                addModule.Success && afterAdd.Count == 2
                && afterAdd.First(v => v.VersionNumber == 2).ModuleCodes.Contains(addedModuleCode, StringComparer.Ordinal),
                $"versions={afterAdd.Count} v2modules=[{string.Join(",", afterAdd.FirstOrDefault(v => v.VersionNumber == 2)?.ModuleCodes ?? Array.Empty<string>())}]");

            Check("D3. Version 1 stays IMMUTABLE: still zero modules and the original terms",
                afterAdd.First(v => v.VersionNumber == 1).ModuleCodes.Count == 0
                && afterAdd.First(v => v.VersionNumber == 1).TermsAndAgreement == tempTermsV1
                && afterAdd.First(v => v.VersionNumber == 1).Price == tempPrice,
                $"v1modules={afterAdd.First(v => v.VersionNumber == 1).ModuleCodes.Count} v1terms={afterAdd.First(v => v.VersionNumber == 1).TermsAndAgreement.Length} chars");

            // Non-material saves: the same module again, then an identical plan save.
            var addAgain = service.AddModuleToPlanAsync(tempPlan.SubscriptionPlanId, addedModuleCode, changedBy: Reviewer).GetAwaiter().GetResult();
            var identicalSave = service.UpdatePlanAsync(
                tempPlan.SubscriptionPlanId, "Temp Version Probe", "Verification-only plan (removed again by this battery).",
                tempPrice, BillingCycles.Monthly, tempTermsV1, changedBy: Reviewer).GetAwaiter().GetResult();
            var afterNoChange = service.GetPlanVersionsAsync(tempPlan.SubscriptionPlanId).GetAwaiter().GetResult();

            Check("D4. re-adding the same module and re-saving identical values create NO version",
                addAgain.Success && identicalSave.Success && afterNoChange.Count == 2,
                $"versions={afterNoChange.Count} (expected 2)");

            // Material change 2: rewrite the Terms & Agreement.
            var termsEdit = service.UpdatePlanAsync(
                tempPlan.SubscriptionPlanId, "Temp Version Probe", "Verification-only plan (removed again by this battery).",
                tempPrice, BillingCycles.Monthly, tempTermsV2, changedBy: Reviewer).GetAwaiter().GetResult();
            var afterTerms = service.GetPlanVersionsAsync(tempPlan.SubscriptionPlanId).GetAwaiter().GetResult();
            var v3 = afterTerms.First(v => v.VersionNumber == 3);
            var v2 = afterTerms.First(v => v.VersionNumber == 2);

            Check("F1. changing the Terms & Agreement creates a new version with the NEW wording",
                termsEdit.Success && afterTerms.Count == 3 && v3.TermsAndAgreement == tempTermsV2
                && v3.ChangeType == PlanVersionChangeTypes.TermsChanged,
                $"v3terms={v3.TermsAndAgreement.Length} chars change={v3.ChangeType}");

            Check("F2. the previous version keeps its OWN (old) terms wording",
                v2.TermsAndAgreement == tempTermsV1 && v2.TermsAndAgreement != tempTermsV2,
                $"v2terms != v3terms ({v2.TermsAndAgreement.Length} vs {v3.TermsAndAgreement.Length} chars)");

            // Material change 3: price, then billing cycle.
            var priceEdit = service.UpdatePlanAsync(
                tempPlan.SubscriptionPlanId, "Temp Version Probe", "Verification-only plan (removed again by this battery).",
                tempPrice + 100m, BillingCycles.Monthly, tempTermsV2, changedBy: Reviewer).GetAwaiter().GetResult();
            var cycleEdit = service.UpdatePlanAsync(
                tempPlan.SubscriptionPlanId, "Temp Version Probe", "Verification-only plan (removed again by this battery).",
                tempPrice + 100m, BillingCycles.Yearly, tempTermsV2, changedBy: Reviewer).GetAwaiter().GetResult();

            // Material change 4: remove the module again.
            var removeModule = service.RemoveModuleFromPlanAsync(tempPlan.SubscriptionPlanId, addedModuleCode, changedBy: Reviewer).GetAwaiter().GetResult();
            var afterAll = service.GetPlanVersionsAsync(tempPlan.SubscriptionPlanId).GetAwaiter().GetResult();

            Check("D5. price and billing-cycle changes each create their own version (per-plan numbering 1..6)",
                priceEdit.Success && cycleEdit.Success && afterAll.Count == 6
                && afterAll.Select(v => v.VersionNumber).OrderBy(n => n).SequenceEqual(Enumerable.Range(1, 6)),
                string.Join(",", afterAll.OrderBy(v => v.VersionNumber).Select(v => "v" + v.VersionNumber + ":" + v.ChangeType)));

            Check("D6. removing the module creates the newest version WITHOUT it, older versions keep it",
                removeModule.Success
                && afterAll.First(v => v.VersionNumber == 6).ModuleCodes.Count == 0
                && afterAll.First(v => v.VersionNumber == 2).ModuleCodes.Contains(addedModuleCode, StringComparer.Ordinal)
                && afterAll.Count(v => v.IsCurrent) == 1
                && afterAll.First(v => v.IsCurrent).VersionNumber == 6,
                $"v6modules={afterAll.First(v => v.VersionNumber == 6).ModuleCodes.Count} v2modules={afterAll.First(v => v.VersionNumber == 2).ModuleCodes.Count} current={afterAll.First(v => v.IsCurrent).VersionNumber}");

            var v2Details = service.GetPlanVersionDetailsAsync(v2.SubscriptionPlanVersionId).GetAwaiter().GetResult();
            var v6Details = service.GetPlanVersionDetailsAsync(afterAll.First(v => v.VersionNumber == 6).SubscriptionPlanVersionId).GetAwaiter().GetResult();

            Check("E1. the module diff reports the ADDED module against the previous version",
                v2Details != null && v2Details.AddedModuleCodes.Contains(addedModuleCode, StringComparer.Ordinal)
                && v2Details.AddedModuleNames.Any(n => n.StartsWith(addedModuleCode + " (", StringComparison.Ordinal))
                && v2Details.RemovedModuleCodes.Count == 0
                && v2Details.PreviousModuleCount == 0 && v2Details.NewModuleCount == 1,
                $"added=[{string.Join(",", v2Details?.AddedModuleNames ?? Array.Empty<string>())}] counts {v2Details?.PreviousModuleCount}->{v2Details?.NewModuleCount}");

            Check("E2. the module diff reports the REMOVED module against the previous version",
                v6Details != null && v6Details.RemovedModuleCodes.Contains(addedModuleCode, StringComparer.Ordinal)
                && v6Details.RemovedModuleNames.Any(n => n.StartsWith(addedModuleCode + " (", StringComparison.Ordinal))
                && v6Details.AddedModuleCodes.Count == 0
                && v6Details.PreviousModuleCount == 1 && v6Details.NewModuleCount == 0,
                $"removed=[{string.Join(",", v6Details?.RemovedModuleNames ?? Array.Empty<string>())}] counts {v6Details?.PreviousModuleCount}->{v6Details?.NewModuleCount}");

            var priceDetails = service.GetPlanVersionDetailsAsync(afterAll.First(v => v.VersionNumber == 4).SubscriptionPlanVersionId).GetAwaiter().GetResult();
            Check("E3. price / billing-cycle / terms changes are reported with previous vs new values",
                priceDetails != null && priceDetails.PriceChanged && priceDetails.PreviousPrice == tempPrice
                && priceDetails.NewPrice == tempPrice + 100m && !priceDetails.BillingCycleChanged,
                $"price {priceDetails?.PreviousPrice:0.00} -> {priceDetails?.NewPrice:0.00} cycleChanged={priceDetails?.BillingCycleChanged}");

            Check("G1. every version is retrievable with plan / version / change / date / by / modules / terms",
                afterAll.Count == 6 && afterAll.All(v =>
                    v.PlanCode == TempPlanCode && v.PlanName.Length > 0 && v.VersionNumber > 0
                    && v.ChangeType.Length > 0 && v.ChangeTypeLabel.Length > 0
                    && v.EffectiveDate != default && v.ChangedAt != default && v.ChangedBy == Reviewer
                    && v.ChangeSummary.Length > 0),
                string.Join(" | ", afterAll.OrderBy(v => v.VersionNumber).Select(v =>
                    $"v{v.VersionNumber}:{v.ChangeTypeLabel}:{v.EffectiveDate:MMM d}:{v.ChangedBy}:{v.ModuleCount}mod")));

            Check("G2. historical module snapshots are independent of the live mapping (never reconstructed)",
                masterDb.PlanModules.AsNoTracking().Count(pm => pm.SubscriptionPlanId == tempPlan.SubscriptionPlanId && pm.IsActive) == 0
                && afterAll.First(v => v.VersionNumber == 2).ModuleCodes.Count == 1
                && masterDb.SubscriptionPlanVersionModules.AsNoTracking()
                    .Count(m => m.SubscriptionPlanVersionId == v2.SubscriptionPlanVersionId) == 1,
                "live plan has 0 active modules while v2 still lists 1");

            // ============================================================================
            // H + I. OPTION 2 workflow: upgrade with a version snapshot, then downgrade
            // ============================================================================
            Write(string.Empty);
            Write("--- H/I. Approval workflow, version snapshots, downgrade without data loss ---");

            var basicPlan = masterDb.SubscriptionPlans.AsNoTracking().First(p => p.Code == SoftwarePlanCodes.Basic);
            var professionalPlan = masterDb.SubscriptionPlans.AsNoTracking().First(p => p.Code == SoftwarePlanCodes.Professional);

            var historyBeforeWorkflow = masterDb.SubscriptionHistories.AsNoTracking().Count();
            var versionsBeforeWorkflow = masterDb.SubscriptionPlanVersions.AsNoTracking().Count();

            var upgradeSubmit = service.SubmitChangeRequestAsync(
                tenantA.CompanyId, SoftwarePlanCodes.Professional, "adminA", "We need reports and retention.").GetAwaiter().GetResult();
            var upgradeRequest = masterDb.SubscriptionChangeRequests.AsNoTracking()
                .OrderByDescending(r => r.SubscriptionChangeRequestId)
                .FirstOrDefault(r => r.CompanyId == tenantA.CompanyId);
            if (upgradeRequest != null)
            {
                createdRequestIds.Add(upgradeRequest.SubscriptionChangeRequestId);
            }

            var pendingVisible = upgradeRequest != null
                && service.GetChangeRequestsAsync(SubscriptionChangeRequestStatuses.Pending).GetAwaiter().GetResult()
                    .Any(r => r.SubscriptionChangeRequestId == upgradeRequest.SubscriptionChangeRequestId);

            Check("H1. a tenant request starts Pending and reaches the Super Admin review queue (workflow unchanged)",
                upgradeSubmit.Success && pendingVisible,
                $"submitted={upgradeSubmit.Success} pendingVisible={pendingVisible} type={upgradeRequest?.RequestType}");

            var selfReview = upgradeRequest == null
                ? SubscriptionMutationResult.Fail("no request")
                : service.ReviewChangeRequestAsync(upgradeRequest.SubscriptionChangeRequestId, true, "adminA", "self").GetAwaiter().GetResult();
            Check("H2. the requesting identity still cannot approve its own request",
                !selfReview.Success,
                selfReview.Success ? "SELF-APPROVAL WENT THROUGH" : selfReview.Error);

            var approveUpgrade = upgradeRequest == null
                ? SubscriptionMutationResult.Fail("no request")
                : service.ReviewChangeRequestAsync(upgradeRequest.SubscriptionChangeRequestId, true, Reviewer, "Approved: needs reporting.").GetAwaiter().GetResult();
            var afterUpgrade = service.GetCompanySubscriptionAsync(tenantA.CompanyId).GetAwaiter().GetResult();

            var professionalCurrentVersion = masterDb.SubscriptionPlanVersions.AsNoTracking()
                .First(v => v.SubscriptionPlanId == professionalPlan.SubscriptionPlanId && v.IsCurrent);

            Check("H3. approval switches the company to the requested plan and snapshots price + terms",
                approveUpgrade.Success && afterUpgrade != null
                && afterUpgrade.PlanCode == SoftwarePlanCodes.Professional
                && afterUpgrade.Price == professionalPlan.Price
                && afterUpgrade.TermsAndAgreement == professionalPlan.TermsAndAgreement,
                $"plan={afterUpgrade?.PlanCode} price={afterUpgrade?.Price:0.00} terms={afterUpgrade?.TermsAndAgreement.Length} chars");

            Check("H4. the approved subscription is associated with the plan CONFIGURATION VERSION it received",
                afterUpgrade?.PlanVersionNumber == professionalCurrentVersion.VersionNumber
                && afterUpgrade?.PlanVersionEffectiveDate == professionalCurrentVersion.EffectiveDate,
                $"subscriptionVersion=v{afterUpgrade?.PlanVersionNumber} planCurrentVersion=v{professionalCurrentVersion.VersionNumber}");

            var upgradeHistory = masterDb.SubscriptionHistories.AsNoTracking()
                .OrderByDescending(h => h.HistoryId).FirstOrDefault(h => h.CompanyId == tenantA.CompanyId);
            if (upgradeHistory != null)
            {
                createdHistoryIds.Add(upgradeHistory.HistoryId);
            }

            Check("H5. the approval writes the change history (upgrade, previous->new plan, reviewer)",
                upgradeHistory != null
                && upgradeHistory.Action == SubscriptionHistoryActions.Upgrade
                && upgradeHistory.PreviousPlanId == basicPlan.SubscriptionPlanId
                && upgradeHistory.NewPlanId == professionalPlan.SubscriptionPlanId
                && upgradeHistory.ChangedBy == Reviewer,
                $"action={upgradeHistory?.Action} from={upgradeHistory?.PreviousPlanId} to={upgradeHistory?.NewPlanId} by={upgradeHistory?.ChangedBy}");

            var upgradedModules = service.GetEnabledModuleCodesAsync(tenantA.CompanyId).GetAwaiter().GetResult();
            Check("H6. the upgraded company receives exactly the requested plan's modules",
                upgradedModules.Count == mappingRows.Count(m => m.SubscriptionPlanId == professionalPlan.SubscriptionPlanId),
                $"modules={upgradedModules.Count}");

            // Rejection must change nothing but the review fields.
            var rejectSubmit = service.SubmitChangeRequestAsync(tenantA.CompanyId, SoftwarePlanCodes.Enterprise, "adminA", "everything please").GetAwaiter().GetResult();
            var rejectRequest = masterDb.SubscriptionChangeRequests.AsNoTracking()
                .OrderByDescending(r => r.SubscriptionChangeRequestId).First();
            createdRequestIds.Add(rejectRequest.SubscriptionChangeRequestId);
            var historyBeforeReject = masterDb.SubscriptionHistories.AsNoTracking().Count();
            var rejectResult = service.ReviewChangeRequestAsync(rejectRequest.SubscriptionChangeRequestId, false, Reviewer, "Not this quarter.").GetAwaiter().GetResult();
            var afterReject = service.GetCompanySubscriptionAsync(tenantA.CompanyId).GetAwaiter().GetResult();

            Check("H7. rejection records reviewer/date/notes and changes neither the plan nor the history",
                rejectSubmit.Success && rejectResult.Success
                && afterReject?.PlanCode == SoftwarePlanCodes.Professional
                && masterDb.SubscriptionHistories.AsNoTracking().Count() == historyBeforeReject,
                $"plan={afterReject?.PlanCode} history={masterDb.SubscriptionHistories.AsNoTracking().Count()}/{historyBeforeReject}");

            // Downgrade: Professional (12 modules) -> Basic (8 modules).
            var downgradeSubmit = service.SubmitChangeRequestAsync(tenantA.CompanyId, SoftwarePlanCodes.Basic, "adminA", "budget cut").GetAwaiter().GetResult();
            var downgradeRequest = masterDb.SubscriptionChangeRequests.AsNoTracking()
                .OrderByDescending(r => r.SubscriptionChangeRequestId).First();
            createdRequestIds.Add(downgradeRequest.SubscriptionChangeRequestId);
            var approveDowngrade = service.ReviewChangeRequestAsync(downgradeRequest.SubscriptionChangeRequestId, true, Reviewer, "Approved downgrade.").GetAwaiter().GetResult();
            var afterDowngrade = service.GetCompanySubscriptionAsync(tenantA.CompanyId).GetAwaiter().GetResult();
            var downgradedModules = service.GetEnabledModuleCodesAsync(tenantA.CompanyId).GetAwaiter().GetResult();
            var downgradeHistory = masterDb.SubscriptionHistories.AsNoTracking()
                .OrderByDescending(h => h.HistoryId).First();
            createdHistoryIds.Add(downgradeHistory.HistoryId);

            var restricted = new[]
            {
                SoftwareModuleCodes.Retention, SoftwareModuleCodes.Reports,
                SoftwareModuleCodes.BusinessIntelligence, SoftwareModuleCodes.UserManagement
            };

            Check("I1. the downgrade is classified as a Downgrade and applies the smaller plan",
                downgradeSubmit.Success && approveDowngrade.Success
                && downgradeHistory.Action == SubscriptionHistoryActions.Downgrade
                && afterDowngrade?.PlanCode == SoftwarePlanCodes.Basic,
                $"action={downgradeHistory.Action} plan={afterDowngrade?.PlanCode} requestedType={downgradeRequest.RequestType}");

            Check("I2. the downgraded company loses access to the restricted modules",
                downgradedModules.Count == mappingRows.Count(m => m.SubscriptionPlanId == basicPlan.SubscriptionPlanId)
                && restricted.All(m => !downgradedModules.Contains(m, StringComparer.Ordinal)),
                $"modules={downgradedModules.Count} restrictedPresent=[{string.Join(",", restricted.Where(m => downgradedModules.Contains(m, StringComparer.Ordinal)))}]");

            Check("I3. the downgrade is associated with Basic's current version and no version is invented",
                afterDowngrade?.PlanVersionNumber == masterDb.SubscriptionPlanVersions.AsNoTracking()
                    .First(v => v.SubscriptionPlanId == basicPlan.SubscriptionPlanId && v.IsCurrent).VersionNumber
                && masterDb.SubscriptionPlanVersions.AsNoTracking().Count() == versionsBeforeWorkflow,
                $"subscriptionVersion=v{afterDowngrade?.PlanVersionNumber} versions={masterDb.SubscriptionPlanVersions.AsNoTracking().Count()}/{versionsBeforeWorkflow}");

            Check("I4. the approval workflow never deletes subscription rows (history grew, rows kept)",
                masterDb.SubscriptionHistories.AsNoTracking().Count() == historyBeforeWorkflow + 2
                && masterDb.CompanySubscriptions.AsNoTracking().Count() == masterSubscriptionsStart,
                $"history={masterDb.SubscriptionHistories.AsNoTracking().Count()}/{historyBeforeWorkflow + 2} subscriptions={masterDb.CompanySubscriptions.AsNoTracking().Count()}/{masterSubscriptionsStart}");

            // ============================================================================
            // J. Drop-down regression
            // ============================================================================
            Write(string.Empty);
            Write("--- J. Drop-down regression ---");

            var companyNames = masterDb.Companies.AsNoTracking().OrderBy(c => c.CompanyId).Select(c => c.CompanyName).ToList();
            var planNamesFromDb = planRows.Select(p => p.Name).ToList();
            var companiesChoices = masterForm.DescribeCompanyChoicesForTest();
            InstallHeadlessContext();
            var planChoices = masterForm.DescribePlanChoicesForTest();
            InstallHeadlessContext();

            Check("J1. the company drop-down DISPLAYS every company name and never a CLR type name",
                companiesChoices.Contains($"count={companyNames.Count}")
                && companyNames.All(n => companiesChoices.Contains(n))
                && !companiesChoices.Contains("AlphaElite.")
                && companiesChoices.Contains($"displayMember={nameof(CompanySubscriptionInfo.CompanyName)}")
                && companiesChoices.Contains($"valueMember={nameof(CompanySubscriptionInfo.CompanyId)}"),
                companiesChoices);

            Check("J2. the plan drop-down DISPLAYS every plan name and never a CLR type name",
                planChoices.Contains($"count={planNamesFromDb.Count}")
                && planNamesFromDb.All(n => planChoices.Contains(n))
                && !planChoices.Contains("AlphaElite.")
                && planChoices.Contains($"valueMember={nameof(SubscriptionPlanInfo.Code)}"),
                planChoices);

            Check("J3. CompanySubscriptionInfo / SubscriptionPlanInfo never appear in any visible drop-down",
                !companiesChoices.Contains("CompanySubscriptionInfo")
                && !planChoices.Contains("CompanySubscriptionInfo")
                && !planChoices.Contains("SubscriptionPlanInfo"),
                "company + plan selectors checked");

            // ============================================================================
            // K. DbContext concurrency (overlapping operations, actually exercised)
            // ============================================================================
            Write(string.Empty);
            Write("--- K. DbContext concurrency ---");

            // K1 CONTROL (non-vacuity): the same shape of overlap on ONE shared context MUST throw
            // the reported exception, proving the probe really detects that failure mode.
            var sharedContextMessage = RunSharedContextOverlapProbeAsync(runtime.MasterContextFactory).GetAwaiter().GetResult();
            Check("K1. CONTROL - two overlapping operations on ONE shared context throw the reported exception",
                sharedContextMessage.Contains("second operation was started", StringComparison.OrdinalIgnoreCase),
                MasterDashboardForm.TrimError(sharedContextMessage));

            // K2 THE FIX: identical overlap with one context per operation completes cleanly.
            var perOperationResult = RunPerOperationOverlapProbeAsync(runtime.MasterContextFactory).GetAwaiter().GetResult();
            Check("K2. the SAME overlap with one context per operation completes with no exception",
                perOperationResult == "ok",
                perOperationResult);

            // K3: 20 genuinely overlapping subscription calls through the exact service the Super
            // Admin screen uses (Task.Run is TEST-ONLY: it forces real parallelism; the production
            // path never wraps an EF call in Task.Run).
            var scopedService = service as ScopedSubscriptionService;
            var contextsBefore = scopedService?.CreatedContextCount ?? -1;
            var concurrencyFailure = "none";
            var completedBatches = 0;
            try
            {
                var batches = new List<Task>();
                for (var batch = 0; batch < 4; batch++)
                {
                    batches.Add(Task.Run(async () =>
                    {
                        var plans = await service.GetPlansAsync();
                        var versions = await service.GetPlanVersionsAsync();
                        var subscriptions = await service.GetAllCompanySubscriptionsAsync();
                        var history = await service.GetHistoryAsync();
                        var modules = await service.GetModulesAsync();
                        var total = plans.Count + versions.Count + subscriptions.Count + history.Count + modules.Count;
                        if (total <= 0)
                        {
                            throw new InvalidOperationException("an overlapping read returned no rows");
                        }

                        System.Threading.Interlocked.Increment(ref completedBatches);
                    }));
                }

                Task.WhenAll(batches).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                concurrencyFailure = $"{ex.GetType().Name}: {ex.GetBaseException().Message}";
            }

            var contextsCreated = (scopedService?.CreatedContextCount ?? -1) - contextsBefore;
            InstallHeadlessContext();
            Check("K3. 20 overlapping subscription reads complete with ONE CONTEXT PER OPERATION",
                concurrencyFailure == "none" && completedBatches == 4 && contextsCreated >= 20,
                $"failures={concurrencyFailure} batches={completedBatches}/4 contextsCreated={contextsCreated} (expected >= 20)");

            // K4: the DATA layer no longer needs the UI gate - the four loaders and the plan editor
            // are driven at once WITHOUT it (exactly how rapid clicking lands).
            var planCountNow = masterDb.SubscriptionPlans.AsNoTracking().Count();
            var ungatedFailure = "none";
            var ungatedResult = "(not run)";
            try
            {
                ungatedResult = masterForm.RunUngatedRapidInteractionForTestAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                ungatedFailure = $"{ex.GetType().Name}: {ex.GetBaseException().Message}";
            }

            InstallHeadlessContext();
            Check("K4. ungated overlapping UI loads + plan editor opened mid-load: no exception, correct data",
                ungatedFailure == "none" && ungatedResult.Contains("companies=3") && ungatedResult.Contains($"plans={planCountNow}")
                && ungatedResult.Contains($"cards={planCountNow}") && ungatedResult.Contains("midLoadEditor='title='Plan: Enterprise")
                && ungatedResult.Contains("editorLight=0"),
                $"failure={ungatedFailure} | {ungatedResult}");

            // K5: rapid sub-section switching through the real tab path must complete promptly -
            // a nested gate acquisition would sit on the 120s timeout instead.
            var switchStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var switchProbe = masterForm.RunOverlappingSubscriptionLoadsForTestAsync().GetAwaiter().GetResult();
            switchStopwatch.Stop();
            InstallHeadlessContext();
            Check("K5. rapid Companies/Requests/Plans/History switching completes promptly (no gate stall)",
                switchStopwatch.Elapsed < TimeSpan.FromSeconds(60)
                && switchProbe.Contains("tab=Companies / Tenants") && switchProbe.Contains($"cards={planCountNow}"),
                $"elapsed={switchStopwatch.ElapsedMilliseconds}ms | {switchProbe}");

            // ============================================================================
            // L. UI render through the real navigation path
            // ============================================================================
            Write(string.Empty);
            Write("--- L. UI render (real navigation path) ---");

            Check("L1. the Companies tab renders every company through the real path",
                masterForm.SubscriptionCompanyRowCountForTest == masterCompaniesStart
                && companiesRender.Contains("COMP001:") && companiesRender.Contains("COMP002:") && companiesRender.Contains("COMP003:")
                && companiesRender.Contains("Status: Active"),
                $"rows={masterForm.SubscriptionCompanyRowCountForTest}/{masterCompaniesStart}");

            var cardsViewProbe = masterForm.ShowPlanCardsViewForTest();
            var historyProbe = masterForm.OpenPlanVersionHistoryForTestAsync(SoftwarePlanCodes.Professional).GetAwaiter().GetResult();
            InstallHeadlessContext();
            var versionHistory = masterForm.DescribePlanVersionHistoryForTest();
            InstallHeadlessContext();
            var v1Details = masterForm.SelectPlanVersionForTestAsync(1).GetAwaiter().GetResult();
            InstallHeadlessContext();

            var professionalV1 = masterDb.SubscriptionPlanVersions.AsNoTracking()
                .First(v => v.SubscriptionPlanId == professionalPlan.SubscriptionPlanId && v.VersionNumber == 1);
            var professionalModules = mappingRows.Count(m => m.SubscriptionPlanId == professionalPlan.SubscriptionPlanId);

            Check("L2. the cards view is the default presentation and the editor is a separate view",
                cardsViewProbe.Contains("cardsSelected=True") && cardsViewProbe.Contains("editorSelected=False"),
                cardsViewProbe);

            Check("L3. the card's View History opens the version grid filtered to that plan",
                historyProbe.Contains($"{professionalPlan.Name} ({professionalPlan.Code})")
                && versionHistory.Contains("gridRows=" + masterDb.SubscriptionPlanVersions.AsNoTracking()
                    .Count(v => v.SubscriptionPlanId == professionalPlan.SubscriptionPlanId)),
                historyProbe + " || " + versionHistory.Substring(0, Math.Min(120, versionHistory.Length)));

            Check("L4. selecting a version renders the previous-vs-new diff and THAT version's own terms",
                v1Details.Contains("Version: v1")
                && v1Details.Contains("Previous: (none")                && v1Details.Contains($"Module count: 0 -> {professionalModules}")
                && v1Details.Contains("Changed: ")
                && v1Details.Contains($"terms='{professionalV1.TermsAndAgreement}'")
                // The version's OWN terms snapshot is exactly the plan row's accepted text (the
                // demo plans carry empty terms, so this pins the snapshot instead of a placeholder).
                && professionalV1.TermsAndAgreement == professionalPlan.TermsAndAgreement,
                MasterDashboardForm.TrimError(v1Details));

            Check("L5. the company subscription audit grid still renders beside the version history",
                versionHistory.Contains("companyHistoryRows=" + masterDb.SubscriptionHistories.AsNoTracking().Count()),
                versionHistory.Substring(0, Math.Min(160, versionHistory.Length)));

            // L6 runs LAST on purpose: the theme probe replaces the hosted Subscription panel with a
            // freshly built one, so every control-level probe above has already been captured.
            Check("L6. the whole Super Admin Subscription panel is dark and type-name free (cards + history included)",
                masterForm.ApplyAndDescribeMasterThemeForTestAsync().GetAwaiter().GetResult()
                    .Split("light=0 typename=0").Length == 5,
                "Subscription / Companies-Tenants / TenantDatabase / BusinessIntelligence panels");

            // ============================================================================
            // Regression: tenant baselines + master demo data
            // ============================================================================
            Write(string.Empty);
            Write("--- Regression: tenants + master demo state ---");

            int aMembersEnd, aAttendanceEnd, aPaymentsEnd;
            decimal aPayTotalEnd;
            using (var dbA = runtime.DataContextFactory.CreateAsync(tenantA.CompanyId, false).GetAwaiter().GetResult())
            {
                aMembersEnd = dbA.Members.AsNoTracking().Count();
                aAttendanceEnd = dbA.Attendances.AsNoTracking().Count();
                aPaymentsEnd = dbA.Payments.AsNoTracking().Count();
                aPayTotalEnd = dbA.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
            }

            int bMembersEnd, bAttendanceEnd, bPaymentsEnd;
            decimal bPayTotalEnd;
            using (var dbB = runtime.DataContextFactory.CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult())
            {
                bMembersEnd = dbB.Members.AsNoTracking().Count();
                bAttendanceEnd = dbB.Attendances.AsNoTracking().Count();
                bPaymentsEnd = dbB.Payments.AsNoTracking().Count();
                bPayTotalEnd = dbB.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
            }

            int cMembersEnd, cAttendanceEnd, cBranchesEnd, cUsersEnd, cSubscriptionsEnd;
            decimal cPayTotalEnd;
            using (var dbC = runtime.DataContextFactory.CreateAsync(tenantC.CompanyId, true).GetAwaiter().GetResult())
            {
                cMembersEnd = dbC.Members.AsNoTracking().Count();
                cAttendanceEnd = dbC.Attendances.AsNoTracking().Count();
                cBranchesEnd = dbC.Branches.AsNoTracking().Count();
                cUsersEnd = dbC.UserAccounts.AsNoTracking().Count();
                cSubscriptionsEnd = dbC.Subscriptions.AsNoTracking().Count();
                cPayTotalEnd = dbC.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
            }

            Check("R1. Tenant A business data is untouched (members / attendance / payments / sum)",
                aMembersEnd == aMembers && aAttendanceEnd == aAttendance && aPaymentsEnd == aPayments && aPayTotalEnd == aPayTotal,
                $"members={aMembersEnd}/{aMembers} attendance={aAttendanceEnd}/{aAttendance} payments={aPaymentsEnd}/{aPayments} sum={aPayTotalEnd:0.00}/{aPayTotal:0.00}");

            Check("R2. Tenant B business data is untouched (members / attendance / payments / sum)",
                bMembersEnd == bMembers && bAttendanceEnd == bAttendance && bPaymentsEnd == bPayments && bPayTotalEnd == bPayTotal,
                $"members={bMembersEnd}/{bMembers} attendance={bAttendanceEnd}/{bAttendance} payments={bPaymentsEnd}/{bPayments} sum={bPayTotalEnd:0.00}/{bPayTotal:0.00}");

            Check("R3. Tenant C business data is untouched (members / attendance / branches / users / member subscriptions / payments)",
                cMembersEnd == cMembers && cAttendanceEnd == cAttendance && cBranchesEnd == cBranches
                && cUsersEnd == cUsers && cSubscriptionsEnd == cSubscriptions && cPayTotalEnd == cPayTotal,
                $"members={cMembersEnd}/{cMembers} attendance={cAttendanceEnd}/{cAttendance} branches={cBranchesEnd}/{cBranches} users={cUsersEnd}/{cUsers} memberSubs={cSubscriptionsEnd}/{cSubscriptions} sum={cPayTotalEnd:0.00}/{cPayTotal:0.00}");

            Check("R4. no subscription row was deleted or duplicated by the workflow (only versions were added)",
                masterDb.CompanySubscriptions.AsNoTracking().Count() == masterSubscriptionsStart
                && masterDb.SubscriptionPlanVersions.AsNoTracking().Count() >= masterVersionsStart,
                $"subscriptions={masterDb.CompanySubscriptions.AsNoTracking().Count()}/{masterSubscriptionsStart} " +
                $"versions={masterDb.SubscriptionPlanVersions.AsNoTracking().Count()} (started {masterVersionsStart})");
            }
            catch (Exception ex)
            {
                Write($"[FATAL] {ex.GetType().Name}: {ex.GetBaseException().Message}");
                Write(ex.StackTrace ?? string.Empty);
                failures.Add($"fatal: {ex.GetType().Name}: {ex.GetBaseException().Message}");
            }
            finally
            {
                // The master demo state is restored even when a check above fails, so the next
                // battery starts from the approved demo data.
                RestoreMasterDemoState(Write, failures, createdRequestIds, createdHistoryIds);
            }

            Write(string.Empty);
            Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | checks: {checks} | failures: {failures.Count} ---");
            foreach (var failure in failures)
            {
                Write("  FAILED " + failure);
            }

            return failures.Count == 0 ? 0 : 1;
        }

        /// <summary>
        /// Installs a PLAIN SynchronizationContext (the established harness pattern): creating a
        /// WinForms control handle installs a WindowsFormsSynchronizationContext whose
        /// continuations need a message loop a headless run never pumps.
        /// </summary>
        private static void InstallHeadlessContext() =>
            System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Threading.SynchronizationContext());

        /// <summary>
        /// Non-vacuity control: starts a SLOW query and a second query on the SAME context and
        /// returns the exception message the context instance produces (the reported failure mode).
        /// </summary>
        private static async Task<string> RunSharedContextOverlapProbeAsync(IDbContextFactory<MasterAlphaEliteDbContext> factory)
        {
            using var shared = factory.CreateDbContext();

            // A non-query command is used for the slow operation so the second query stays a plain
            // composable SELECT (SqlQueryRaw over a batch cannot be composed by EF).
            var slow = shared.Database.ExecuteSqlRawAsync("WAITFOR DELAY '00:00:01'");

            // Give the first operation a moment to actually be in flight.
            await Task.Delay(50);

            try
            {
                await shared.Database
                    .SqlQueryRaw<int>("SELECT CAST(2 AS int) AS [Value]")
                    .FirstOrDefaultAsync();

                await slow;
                return "(no exception: both operations completed on one context)";
            }
            catch (Exception ex)
            {
                var message = $"{ex.GetType().Name}: {ex.GetBaseException().Message}";
                try
                {
                    await slow;
                }
                catch
                {
                    // The slow operation is expected to complete or to be discarded; it is not
                    // part of the assertion.
                }

                return message;
            }
        }

        /// <summary>
        /// The same overlap with ONE CONTEXT PER OPERATION (the fix): both operations complete and
        /// the slow one still returns its row.
        /// </summary>
        private static async Task<string> RunPerOperationOverlapProbeAsync(IDbContextFactory<MasterAlphaEliteDbContext> factory)
        {
            try
            {
                await using var slowContext = factory.CreateDbContext();
                await using var quickContext = factory.CreateDbContext();

                var slow = slowContext.Database.ExecuteSqlRawAsync("WAITFOR DELAY '00:00:01'");

                await Task.Delay(50);

                var quick = await quickContext.Database
                    .SqlQueryRaw<int>("SELECT CAST(2 AS int) AS [Value]")
                    .FirstOrDefaultAsync();

                // The slow command is a non-query: it only has to complete without throwing.
                await slow;
                return quick == 2 ? "ok" : $"(unexpected value quick={quick})";
            }
            catch (Exception ex)
            {
                return $"{ex.GetType().Name}: {ex.GetBaseException().Message}";
            }
        }

        /// <summary>
        /// Restores the approved master demo state: remove the probe plan (its immutable version
        /// snapshots go with it), remove the requests/history rows this battery created, restore
        /// COMP001's demo subscription and re-link the demo subscriptions to their plan's current
        /// version. Existing subscriptions, plans, modules and tenants are never rewritten beyond
        /// the fields the demo seed itself owns.
        /// </summary>
        private static void RestoreMasterDemoState(
            Action<string> write, List<string> failures, List<int> createdRequestIds, List<int> createdHistoryIds)
        {
            try
            {
                var runtime = AppRuntime.Create();
                var masterDb = runtime.MasterDb;
                var utcNow = DateTime.UtcNow;

                foreach (var historyId in createdHistoryIds)
                {
                    var history = masterDb.SubscriptionHistories.FirstOrDefault(h => h.HistoryId == historyId);
                    if (history != null)
                    {
                        masterDb.SubscriptionHistories.Remove(history);
                    }
                }

                foreach (var requestId in createdRequestIds)
                {
                    var request = masterDb.SubscriptionChangeRequests.FirstOrDefault(r => r.SubscriptionChangeRequestId == requestId);
                    if (request != null)
                    {
                        masterDb.SubscriptionChangeRequests.Remove(request);
                    }
                }

                var tempPlan = masterDb.SubscriptionPlans.FirstOrDefault(p => p.Code == TempPlanCode);
                if (tempPlan != null)
                {
                    masterDb.PlanModules.RemoveRange(masterDb.PlanModules.Where(pm => pm.SubscriptionPlanId == tempPlan.SubscriptionPlanId));
                    masterDb.SubscriptionPlans.Remove(tempPlan);
                }

                masterDb.SaveChanges();

                var company = masterDb.Companies.FirstOrDefault(c => c.CompanyCode == "COMP001");
                var basicPlan = masterDb.SubscriptionPlans.FirstOrDefault(p => p.Code == SoftwarePlanCodes.Basic);
                if (company != null && basicPlan != null)
                {
                    var subscription = masterDb.CompanySubscriptions
                        .OrderByDescending(s => s.Status == SubscriptionStatuses.Active)
                        .ThenByDescending(s => s.CompanySubscriptionId)
                        .FirstOrDefault(s => s.CompanyId == company.CompanyId);

                    if (subscription != null)
                    {
                        subscription.SubscriptionPlanId = basicPlan.SubscriptionPlanId;
                        subscription.StartDate = new DateTime(2026, 10, 1);
                        subscription.EndDate = new DateTime(2027, 10, 1);
                        subscription.RenewalDate = null;
                        subscription.Status = SubscriptionStatuses.Active;
                        subscription.Price = 0.00m;
                        subscription.PaymentStatus = PaymentStatuses.NotApplicable;
                        subscription.TermsAndAgreement = string.Empty;
                        subscription.UpdatedAt = utcNow;

                        var basicVersion = masterDb.SubscriptionPlanVersions
                            .Where(v => v.SubscriptionPlanId == basicPlan.SubscriptionPlanId)
                            .OrderByDescending(v => v.IsCurrent)
                            .ThenByDescending(v => v.VersionNumber)
                            .FirstOrDefault();
                        subscription.SubscriptionPlanVersionId = basicVersion?.SubscriptionPlanVersionId;
                    }

                    masterDb.SaveChanges();
                }

                write("Cleanup: master demo state restored (probe plan, requests, history, COMP001 subscription).");
            }
            catch (Exception ex)
            {
                write($"[CLEANUP ERROR] {ex.GetType().Name}: {ex.GetBaseException().Message}");
                failures.Add($"cleanup: {ex.GetBaseException().Message}");
            }
        }
    }
}
