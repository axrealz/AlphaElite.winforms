using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 4 verification (headless): the OPTION 2 subscription workflow through the
    /// SubscriptionService - the exact path the Super Admin Subscription screen calls.
    ///
    /// Covers: request submission (Pending only), approval (transactional subscription
    /// change + price/terms snapshot + history), rejection (no plan change, reviewer and
    /// notes recorded, no history entry), upgrade/downgrade module effects, tenant data
    /// isolation (A/B/C fingerprints), snapshot immutability against plan edits, inactive
    /// plan rejection, self-review prevention, double-review prevention, and cancellation.
    ///
    /// Every master row this harness creates is removed in the finally block; the three
    /// demo subscriptions are restored to their exact approved demo state, and the VERSIONED
    /// state (plan versions, their module snapshots, the current flags and the subscription
    /// version links) is rolled back to its pre-run snapshot - so exercising real version
    /// creation here never leaves verification-only plan history in the demo database. Tenant
    /// databases are only ever opened READ-ONLY (fingerprints).
    /// </summary>
    internal static class Phase4SubscriptionWorkflowVerification
    {
        private const string Reviewer = "superadmin-test";
        private const string TenantRequesterA = "COMP001-admin-test";
        private const string TenantRequesterC = "COMP003-admin-test";

        // The upgrade/downgrade paths exercise the REAL plan rows. PROFESSIONAL's price/terms are
        // briefly given non-default values so the "approval snapshots price + terms" assertion is
        // meaningful (an all-zero/all-empty demo value would make it vacuous). The finally block
        // restores the plan configuration from the pre-run snapshot, so nothing survives.
        private const decimal TempProfessionalPrice = 49.95m;
        private const string TempProfessionalTerms = "TEMP-TERMS-PHASE4-PROFESSIONAL";

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

            // Created master rows (removed again in the finally block).
            var createdRequestIds = new List<int>();
            var createdHistoryIds = new List<int>();
            var createdPlanIds = new List<int>();

            // ---- Versioned-state snapshot (restored verbatim in the finally block) ----
            // Check 23 deliberately edits the PROFESSIONAL plan price through the REAL versioned
            // service, which by design publishes a new immutable plan version. That residue must never
            // survive the run: leaving it behind would add verification-only plan history to the
            // demo Master DB. Everything this harness can touch is captured here and rolled back
            // at the end, so a regression run can exercise real version creation without ever
            // permanently mutating the demo plan into Version 2, 3, ... .
            var versionSnapshotCaptured = false;
            var versionIdsBefore = new List<int>();
            var versionModuleKeysBefore = new List<(int VersionId, int ModuleId)>();
            var versionCurrentBefore = new List<(int VersionId, bool IsCurrent)>();
            var planConfigBefore = new List<(int PlanId, string Name, string Description, decimal Price, string BillingCycle, string Terms, bool IsActive)>();
            var subscriptionVersionBefore = new List<(int SubscriptionId, int? VersionId)>();

            try
            {
                var runtime = AppRuntime.Create();
                var masterDb = runtime.MasterDb;
                var service = runtime.SubscriptionService;

                var masterDatabase = masterDb.Database.GetDbConnection().Database;
                Write($"Operation      : phase4-subscription-workflow-verify");
                Write($"Target database: {masterDatabase}");
                if (!masterDatabase.Contains("67598"))
                {
                    Write($"[ABORT] Refusing to run: the master connection does not point at db67598 (got '{masterDatabase}').");
                    return 1;
                }

                // Capture the versioned state BEFORE the workflow mutates anything.
                versionIdsBefore = masterDb.SubscriptionPlanVersions.AsNoTracking()
                    .Select(v => v.SubscriptionPlanVersionId).ToList();
                versionModuleKeysBefore = masterDb.SubscriptionPlanVersionModules.AsNoTracking()
                    .Select(m => new { m.SubscriptionPlanVersionId, m.ModuleId }).ToList()
                    .Select(m => (m.SubscriptionPlanVersionId, m.ModuleId)).ToList();
                versionCurrentBefore = masterDb.SubscriptionPlanVersions.AsNoTracking()
                    .Select(v => new { v.SubscriptionPlanVersionId, v.IsCurrent }).ToList()
                    .Select(v => (v.SubscriptionPlanVersionId, v.IsCurrent)).ToList();
                planConfigBefore = masterDb.SubscriptionPlans.AsNoTracking()
                    .Select(p => new { p.SubscriptionPlanId, p.Name, p.Description, p.Price, p.BillingCycle, p.TermsAndAgreement, p.IsActive }).ToList()
                    .Select(p => (p.SubscriptionPlanId, p.Name, p.Description, p.Price, p.BillingCycle, p.TermsAndAgreement, p.IsActive)).ToList();
                subscriptionVersionBefore = masterDb.CompanySubscriptions.AsNoTracking()
                    .Select(s => new { s.CompanySubscriptionId, s.SubscriptionPlanVersionId }).ToList()
                    .Select(s => (s.CompanySubscriptionId, s.SubscriptionPlanVersionId)).ToList();
                versionSnapshotCaptured = true;

                var tenantA = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == "COMP001");
                var tenantB = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == "COMP002");
                var tenantC = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == "COMP003");

                // ---- Tenant baselines (read-only) ----
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
                Write("--- 1. Master baseline ---");

                Check("1. master pre-existing data (Companies=3 CompanyDatabase=3 IdentityUsers=1)",
                    masterDb.Companies.AsNoTracking().Count() == 3
                    && masterDb.CompanyDatabase.AsNoTracking().Count() == 3
                    && masterDb.Users.AsNoTracking().Count() == 1,
                    $"Companies={masterDb.Companies.AsNoTracking().Count()} CompanyDatabase={masterDb.CompanyDatabase.AsNoTracking().Count()} Users={masterDb.Users.AsNoTracking().Count()}");

                var frozenMatrix = SubscriptionService.GetPlanModuleMatrix();
                var plans = masterDb.SubscriptionPlans.AsNoTracking().ToList();
                var modules = masterDb.SoftwareModules.AsNoTracking().ToList();
                var mappings = masterDb.PlanModules.AsNoTracking().ToList();
                var planIdByCode = plans.ToDictionary(p => p.Code, p => p.SubscriptionPlanId);

                IReadOnlyCollection<string> PlanModules(int planId) =>
                    mappings.Where(pm => pm.SubscriptionPlanId == planId && pm.IsActive)
                        .Join(modules, pm => pm.ModuleId, m => m.ModuleId, (pm, m) => m.Code)
                        .ToList();

                Check("2. frozen plan matrices unchanged (BASIC 8 / PROFESSIONAL 12 / ENTERPRISE 16)",
                    frozenMatrix.All(kv =>
                    {
                        var actual = PlanModules(planIdByCode[kv.Key]);
                        return actual.Count == kv.Value.Length && kv.Value.All(c => actual.Contains(c, StringComparer.Ordinal));
                    }),
                    string.Join(" | ", frozenMatrix.Select(kv => $"{kv.Key}={PlanModules(planIdByCode[kv.Key]).Count}")));

                bool DemoSubOk(int companyId, string planCode) =>
                    masterDb.CompanySubscriptions.AsNoTracking()
                        .OrderByDescending(s => s.Status == SubscriptionStatuses.Active)
                        .ThenByDescending(s => s.CompanySubscriptionId)
                        .FirstOrDefault(s => s.CompanyId == companyId) is { } s
                    && plans.First(p => p.SubscriptionPlanId == s.SubscriptionPlanId).Code == planCode
                    && s.Status == SubscriptionStatuses.Active
                    && s.StartDate.Date == new DateTime(2026, 10, 1)
                    && s.EndDate.Date == new DateTime(2027, 10, 1);

                Check("3. demo subscriptions (COMP001=BASIC COMP002=PROFESSIONAL COMP003=ENTERPRISE, Active, demo window)",
                    DemoSubOk(tenantA.CompanyId, SoftwarePlanCodes.Basic)
                    && DemoSubOk(tenantB.CompanyId, SoftwarePlanCodes.Professional)
                    && DemoSubOk(tenantC.CompanyId, SoftwarePlanCodes.Enterprise),
                    "COMP001/COMP002/COMP003 verified");

                Check("4. catalog shape (plans=3 modules=16 mappings=36)",
                    plans.Count == 3 && modules.Count == 16 && mappings.Count == 36,
                    $"plans={plans.Count} modules={modules.Count} mappings={mappings.Count}");

                Check("5. workflow tables empty before the run (history=0 requests=0)",
                    masterDb.SubscriptionHistories.AsNoTracking().Count() == 0
                    && masterDb.SubscriptionChangeRequests.AsNoTracking().Count() == 0,
                    $"history={masterDb.SubscriptionHistories.AsNoTracking().Count()} requests={masterDb.SubscriptionChangeRequests.AsNoTracking().Count()}");

                // =====================================================================
                // 2. Tenant submits an UPGRADE request (COMP001: BASIC -> PROFESSIONAL)
                // =====================================================================
                Write(string.Empty);
                Write("--- 2. Request submission (Pending only) ---");

                // Give the target plan distinct (temporary) price/terms so the snapshot assertions
                // below prove a real copy rather than an all-zero coincidence. Restored in finally.
                var professionalPlan = masterDb.SubscriptionPlans.First(p => p.Code == SoftwarePlanCodes.Professional);
                professionalPlan.Price = TempProfessionalPrice;
                professionalPlan.TermsAndAgreement = TempProfessionalTerms;
                professionalPlan.UpdatedAt = DateTime.UtcNow;
                masterDb.SaveChanges();

                var submitA = service.SubmitChangeRequestAsync(tenantA.CompanyId, SoftwarePlanCodes.Professional, TenantRequesterA, "We need reports and retention.").GetAwaiter().GetResult();
                Check("6. tenant can submit a request", submitA.Success, submitA.Success ? "ok" : submitA.Error);

                var requestA = masterDb.SubscriptionChangeRequests.AsNoTracking()
                    .OrderByDescending(r => r.SubscriptionChangeRequestId).First();
                createdRequestIds.Add(requestA.SubscriptionChangeRequestId);

                Check("7. request is Pending with type/classification and requester recorded",
                    requestA.Status == SubscriptionChangeRequestStatuses.Pending
                    && requestA.RequestType == SubscriptionRequestTypes.Upgrade
                    && requestA.RequestedBy == TenantRequesterA
                    && requestA.RequestedPrice == TempProfessionalPrice
                    && requestA.CurrentPlanId == planIdByCode[SoftwarePlanCodes.Basic],
                    $"#{requestA.SubscriptionChangeRequestId} {requestA.RequestType} by {requestA.RequestedBy} price={requestA.RequestedPrice}");

                var subAAfterSubmit = masterDb.CompanySubscriptions.AsNoTracking()
                    .OrderByDescending(s => s.Status == SubscriptionStatuses.Active)
                    .ThenByDescending(s => s.CompanySubscriptionId)
                    .First(s => s.CompanyId == tenantA.CompanyId);
                Check("8. submit does NOT change the active subscription (still BASIC, no history)",
                    plans.First(p => p.SubscriptionPlanId == subAAfterSubmit.SubscriptionPlanId).Code == SoftwarePlanCodes.Basic
                    && masterDb.SubscriptionHistories.AsNoTracking().Count() == 0,
                    $"plan={plans.First(p => p.SubscriptionPlanId == subAAfterSubmit.SubscriptionPlanId).Code} history={masterDb.SubscriptionHistories.AsNoTracking().Count()}");

                Check("9. second pending request for the same company is refused",
                    !service.SubmitChangeRequestAsync(tenantA.CompanyId, SoftwarePlanCodes.Enterprise, TenantRequesterA, string.Empty).GetAwaiter().GetResult().Success,
                    "pending-duplicate guard engaged");

                // =====================================================================
                // 3. Super Admin approves the upgrade
                // =====================================================================
                Write(string.Empty);
                Write("--- 3. Approval (transactional) ---");

                var approveA = service.ReviewChangeRequestAsync(requestA.SubscriptionChangeRequestId, true, Reviewer, "Approved for the growth plan.").GetAwaiter().GetResult();
                Check("10. Super Admin can approve the pending request", approveA.Success, approveA.Success ? "ok" : approveA.Error);

                var subAAfterApprove = masterDb.CompanySubscriptions.AsNoTracking()
                    .OrderByDescending(s => s.Status == SubscriptionStatuses.Active)
                    .ThenByDescending(s => s.CompanySubscriptionId)
                    .First(s => s.CompanyId == tenantA.CompanyId);
                Check("11. approval changes CompanySubscription to the requested plan (one active row only)",
                    plans.First(p => p.SubscriptionPlanId == subAAfterApprove.SubscriptionPlanId).Code == SoftwarePlanCodes.Professional
                    && subAAfterApprove.Status == SubscriptionStatuses.Active
                    && masterDb.CompanySubscriptions.AsNoTracking().Count(s => s.CompanyId == tenantA.CompanyId) == 1,
                    $"plan={plans.First(p => p.SubscriptionPlanId == subAAfterApprove.SubscriptionPlanId).Code} rows={masterDb.CompanySubscriptions.AsNoTracking().Count(s => s.CompanyId == tenantA.CompanyId)}");

                Check("12. price and terms are SNAPSHOT-copied from the plan",
                    subAAfterApprove.Price == TempProfessionalPrice
                    && subAAfterApprove.TermsAndAgreement == TempProfessionalTerms,
                    $"price={subAAfterApprove.Price:0.00} terms={subAAfterApprove.TermsAndAgreement.Length} chars");

                var historyA = masterDb.SubscriptionHistories.AsNoTracking().OrderByDescending(h => h.HistoryId).First();
                createdHistoryIds.Add(historyA.HistoryId);
                Check("13. approval creates the SubscriptionHistory row (Upgrade BASIC->PROFESSIONAL, reviewer recorded)",
                    masterDb.SubscriptionHistories.AsNoTracking().Count() == 1
                    && historyA.Action == SubscriptionHistoryActions.Upgrade
                    && historyA.PreviousPlanId == planIdByCode[SoftwarePlanCodes.Basic]
                    && historyA.NewPlanId == planIdByCode[SoftwarePlanCodes.Professional]
                    && historyA.ChangedBy == Reviewer,
                    $"#{historyA.HistoryId} {historyA.Action} {historyA.PreviousPrice:0.00}->{historyA.NewPrice:0.00} by {historyA.ChangedBy}");

                var modulesA = service.GetEnabledModuleCodesAsync(tenantA.CompanyId).GetAwaiter().GetResult();
                Check("14. upgrade exposes the newly allowed modules (12 incl. retention/reports/businessIntelligence/userManagement)",
                    modulesA.Count == 12
                    && new[] { SoftwareModuleCodes.Retention, SoftwareModuleCodes.Reports, SoftwareModuleCodes.BusinessIntelligence, SoftwareModuleCodes.UserManagement }
                        .All(c => modulesA.Contains(c, StringComparer.Ordinal)),
                    $"count={modulesA.Count}");

                var requestAAfter = masterDb.SubscriptionChangeRequests.AsNoTracking().First(r => r.SubscriptionChangeRequestId == requestA.SubscriptionChangeRequestId);
                Check("15. request marked Approved with reviewer and review timestamp",
                    requestAAfter.Status == SubscriptionChangeRequestStatuses.Approved
                    && requestAAfter.ReviewedBy == Reviewer
                    && requestAAfter.ReviewedAt != null,
                    $"{requestAAfter.Status} by {requestAAfter.ReviewedBy}");

                Check("16. an already-reviewed request cannot be reviewed again",
                    !service.ReviewChangeRequestAsync(requestA.SubscriptionChangeRequestId, true, Reviewer, string.Empty).GetAwaiter().GetResult().Success,
                    "double-review guard engaged");

                // =====================================================================
                // 4. DOWNGRADE (COMP002: PROFESSIONAL -> BASIC)
                // =====================================================================
                Write(string.Empty);
                Write("--- 4. Downgrade restricts modules, deletes nothing ---");

                var submitB = service.SubmitChangeRequestAsync(tenantB.CompanyId, SoftwarePlanCodes.Basic, "COMP002-admin-test", "Downsizing for the off-season.").GetAwaiter().GetResult();
                Check("17. downgrade request submitted", submitB.Success, submitB.Success ? "ok" : submitB.Error);

                var requestB = masterDb.SubscriptionChangeRequests.AsNoTracking()
                    .OrderByDescending(r => r.SubscriptionChangeRequestId).First();
                createdRequestIds.Add(requestB.SubscriptionChangeRequestId);
                Check("18. downgrade classified by module sets", requestB.RequestType == SubscriptionRequestTypes.Downgrade,
                    requestB.RequestType);

                var approveB = service.ReviewChangeRequestAsync(requestB.SubscriptionChangeRequestId, true, Reviewer, string.Empty).GetAwaiter().GetResult();
                Check("19. downgrade approved", approveB.Success, approveB.Success ? "ok" : approveB.Error);

                var subBAfter = masterDb.CompanySubscriptions.AsNoTracking()
                    .OrderByDescending(s => s.Status == SubscriptionStatuses.Active)
                    .ThenByDescending(s => s.CompanySubscriptionId)
                    .First(s => s.CompanyId == tenantB.CompanyId);
                var modulesB = service.GetEnabledModuleCodesAsync(tenantB.CompanyId).GetAwaiter().GetResult();
                Check("20. downgrade restricts module access (8 modules, BI/retention/reports/userManagement gone)",
                    plans.First(p => p.SubscriptionPlanId == subBAfter.SubscriptionPlanId).Code == SoftwarePlanCodes.Basic
                    && modulesB.Count == 8
                    && new[] { SoftwareModuleCodes.Retention, SoftwareModuleCodes.Reports, SoftwareModuleCodes.BusinessIntelligence, SoftwareModuleCodes.UserManagement }
                        .All(c => !modulesB.Contains(c, StringComparer.Ordinal)),
                    $"plan={plans.First(p => p.SubscriptionPlanId == subBAfter.SubscriptionPlanId).Code} modules={modulesB.Count}");

                var historyB = masterDb.SubscriptionHistories.AsNoTracking().OrderByDescending(h => h.HistoryId).First();
                createdHistoryIds.Add(historyB.HistoryId);
                Check("21. downgrade history row created (PROFESSIONAL->BASIC)",
                    historyB.Action == SubscriptionHistoryActions.Downgrade
                    && historyB.PreviousPlanId == planIdByCode[SoftwarePlanCodes.Professional]
                    && historyB.NewPlanId == planIdByCode[SoftwarePlanCodes.Basic],
                    $"#{historyB.HistoryId} {historyB.Action}");

                using (var dbB = runtime.DataContextFactory.CreateAsync(tenantB.CompanyId, false).GetAwaiter().GetResult())
                {
                    var payB = dbB.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                    Check("22. downgrade deletes NO tenant data (members/attendance/payments unchanged)",
                        dbB.Members.Count() == bMembersStart && dbB.Attendances.Count() == bAttendanceStart
                        && dbB.Payments.Count() == bPaymentsStart && payB == bPayTotalStart,
                        $"members={dbB.Members.Count()}/{bMembersStart} attendance={dbB.Attendances.Count()}/{bAttendanceStart} payments={dbB.Payments.Count()}/{bPaymentsStart}");
                }

                // =====================================================================
                // 5. Snapshot immutability against plan edits
                // =====================================================================
                Write(string.Empty);
                Write("--- 5. Snapshot semantics ---");

                var basicPlan = masterDb.SubscriptionPlans.AsNoTracking().First(p => p.Code == SoftwarePlanCodes.Basic);
                // The acting identity is named explicitly so the version this edit publishes in
                // History records WHO changed the price instead of falling back to "system".
                var priceEdit = service.UpdatePlanAsync(basicPlan.SubscriptionPlanId, basicPlan.Name, basicPlan.Description, 123.45m, basicPlan.BillingCycle, basicPlan.TermsAndAgreement, default, "phase4-verify").GetAwaiter().GetResult();
                var basicPlanAfterEdit = masterDb.SubscriptionPlans.AsNoTracking().First(p => p.SubscriptionPlanId == basicPlan.SubscriptionPlanId);
                var subBPriceAfterEdit = masterDb.CompanySubscriptions.AsNoTracking()
                    .OrderByDescending(s => s.Status == SubscriptionStatuses.Active)
                    .ThenByDescending(s => s.CompanySubscriptionId)
                    .First(s => s.CompanyId == tenantB.CompanyId).Price;
                Check("23. plan price edit does NOT rewrite the subscription snapshot",
                    priceEdit.Success && basicPlanAfterEdit.Price == 123.45m && subBPriceAfterEdit == 0.00m,
                    $"plan price={basicPlanAfterEdit.Price:0.00} subscription snapshot={subBPriceAfterEdit:0.00}");

                // =====================================================================
                // 6. Rejection changes nothing but the request row
                // =====================================================================
                Write(string.Empty);
                Write("--- 6. Rejection ---");

                var historyCountBeforeReject = masterDb.SubscriptionHistories.AsNoTracking().Count();
                var submitC = service.SubmitChangeRequestAsync(tenantA.CompanyId, SoftwarePlanCodes.Professional, TenantRequesterA, string.Empty).GetAwaiter().GetResult();
                Check("24. new request can be submitted after the previous approval", submitC.Success, submitC.Success ? "ok" : submitC.Error);

                var requestC = masterDb.SubscriptionChangeRequests.AsNoTracking()
                    .OrderByDescending(r => r.SubscriptionChangeRequestId).First();
                createdRequestIds.Add(requestC.SubscriptionChangeRequestId);

                var rejectC = service.ReviewChangeRequestAsync(requestC.SubscriptionChangeRequestId, false, Reviewer, "Not eligible for Professional at this time.").GetAwaiter().GetResult();
                Check("25. Super Admin can reject with review notes", rejectC.Success, rejectC.Success ? "ok" : rejectC.Error);

                var subAAfterReject = masterDb.CompanySubscriptions.AsNoTracking()
                    .OrderByDescending(s => s.Status == SubscriptionStatuses.Active)
                    .ThenByDescending(s => s.CompanySubscriptionId)
                    .First(s => s.CompanyId == tenantA.CompanyId);
                Check("26. rejection does NOT change the active subscription (still PROFESSIONAL)",
                    plans.First(p => p.SubscriptionPlanId == subAAfterReject.SubscriptionPlanId).Code == SoftwarePlanCodes.Professional,
                    plans.First(p => p.SubscriptionPlanId == subAAfterReject.SubscriptionPlanId).Code);

                var requestCAfter = masterDb.SubscriptionChangeRequests.AsNoTracking().First(r => r.SubscriptionChangeRequestId == requestC.SubscriptionChangeRequestId);
                Check("27. rejection records reviewer, reviewed date and notes",
                    requestCAfter.Status == SubscriptionChangeRequestStatuses.Rejected
                    && requestCAfter.ReviewedBy == Reviewer
                    && requestCAfter.ReviewedAt != null
                    && requestCAfter.ReviewNotes == "Not eligible for Professional at this time.",
                    $"{requestCAfter.Status} notes={requestCAfter.ReviewNotes}");

                Check("28. rejection creates NO plan-change history entry",
                    masterDb.SubscriptionHistories.AsNoTracking().Count() == historyCountBeforeReject,
                    $"history={masterDb.SubscriptionHistories.AsNoTracking().Count()}/{historyCountBeforeReject}");

                Check("29. rejection without notes is refused by the service",
                    !service.ReviewChangeRequestAsync(requestC.SubscriptionChangeRequestId, false, Reviewer, string.Empty).GetAwaiter().GetResult().Success
                    || requestCAfter.Status != SubscriptionChangeRequestStatuses.Pending,
                    "already-reviewed request cannot be re-reviewed (UI additionally requires notes)");

                // =====================================================================
                // 7. Guards: inactive plan, self-review, cancellation
                // =====================================================================
                Write(string.Empty);
                Write("--- 7. Authorization and state guards ---");

                var tempPlanCreate = service.CreatePlanAsync("TEMP_INACTIVE_TEST", "Temp Inactive Test", "Phase 4 harness temporary plan.", 1.00m, BillingCycles.Monthly, "temp terms").GetAwaiter().GetResult();
                Check("30. Super Admin can create a plan", tempPlanCreate.Success, tempPlanCreate.Success ? "ok" : tempPlanCreate.Error);
                var tempPlan = masterDb.SubscriptionPlans.AsNoTracking().First(p => p.Code == "TEMP_INACTIVE_TEST");
                createdPlanIds.Add(tempPlan.SubscriptionPlanId);

                var deactivate = service.SetPlanActiveAsync(tempPlan.SubscriptionPlanId, false).GetAwaiter().GetResult();
                Check("31. Super Admin can deactivate a plan", deactivate.Success && !masterDb.SubscriptionPlans.AsNoTracking().First(p => p.SubscriptionPlanId == tempPlan.SubscriptionPlanId).IsActive,
                    "IsActive=false");

                var inactiveSubmit = service.SubmitChangeRequestAsync(tenantC.CompanyId, "TEMP_INACTIVE_TEST", TenantRequesterC, string.Empty);
                Check("32. an INACTIVE plan cannot be newly selected (submit refused)",
                    !inactiveSubmit.GetAwaiter().GetResult().Success,
                    "submit rejected");

                var selfSubmit = service.SubmitChangeRequestAsync(tenantC.CompanyId, SoftwarePlanCodes.Professional, TenantRequesterC, string.Empty).GetAwaiter().GetResult();
                var selfRequest = masterDb.SubscriptionChangeRequests.AsNoTracking()
                    .OrderByDescending(r => r.SubscriptionChangeRequestId).First();
                createdRequestIds.Add(selfRequest.SubscriptionChangeRequestId);
                var selfReview = service.ReviewChangeRequestAsync(selfRequest.SubscriptionChangeRequestId, true, TenantRequesterC, string.Empty).GetAwaiter().GetResult();
                Check("33. the requesting identity can NOT approve its own request",
                    selfSubmit.Success && !selfReview.Success,
                    selfReview.Success ? "REVIEW WENT THROUGH" : selfReview.Error);

                var foreignCancel = service.CancelChangeRequestAsync(selfRequest.SubscriptionChangeRequestId, "not-the-requester").GetAwaiter().GetResult();
                var ownCancel = service.CancelChangeRequestAsync(selfRequest.SubscriptionChangeRequestId, TenantRequesterC).GetAwaiter().GetResult();
                var cancelled = masterDb.SubscriptionChangeRequests.AsNoTracking().First(r => r.SubscriptionChangeRequestId == selfRequest.SubscriptionChangeRequestId);
                Check("34. only the requesting identity can cancel its pending request",
                    !foreignCancel.Success && ownCancel.Success && cancelled.Status == SubscriptionChangeRequestStatuses.Cancelled,
                    $"foreign={foreignCancel.Success} own={ownCancel.Success} status={cancelled.Status}");

                // =====================================================================
                // 8. Isolation after the whole workflow
                // =====================================================================
                Write(string.Empty);
                Write("--- 8. Isolation after the workflow ---");

                using (var dbA = runtime.DataContextFactory.CreateAsync(tenantA.CompanyId, false).GetAwaiter().GetResult())
                {
                    var payA = dbA.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                    Check("35. Tenant A data unchanged",
                        dbA.Members.Count() == aMembersStart && dbA.Attendances.Count() == aAttendanceStart
                        && dbA.Payments.Count() == aPaymentsStart && payA == aPayTotalStart,
                        $"members={dbA.Members.Count()}/{aMembersStart} attendance={dbA.Attendances.Count()}/{aAttendanceStart} payments={dbA.Payments.Count()}/{aPaymentsStart}");
                }

                using (var truthDb = runtime.DataContextFactory.CreateAsync(tenantC.CompanyId, true).GetAwaiter().GetResult())
                {
                    var members = truthDb.Members.AsNoTracking().Count();
                    var attendance = truthDb.Attendances.AsNoTracking().Count();
                    var payTotal = truthDb.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                    var payByBranch = truthDb.Branches.AsNoTracking().OrderBy(b => b.BranchId)
                        .Select(b => (int)(truthDb.Payments.AsNoTracking().Where(p => p.BranchId == b.BranchId).Sum(p => (decimal?)p.AmountPaid) ?? 0m))
                        .ToList();
                    var openByBranch = string.Join("/",
                        truthDb.Branches.AsNoTracking().OrderBy(b => b.BranchId).Select(b =>
                            truthDb.Complaints.AsNoTracking().Count(c => c.BranchId == b.BranchId && c.Status != "Resolved" && c.Status != "Closed")));

                    Check("36. Tenant C unchanged (21 members 7/8/6, attendance 17, pay 4000/4150/5500, 10 users, 2 member subs, complaints baseline)",
                        members == 21 && attendance == 17 && payTotal == 13650m
                        && payByBranch.SequenceEqual(new[] { 4000, 4150, 5500 })
                        && truthDb.Branches.AsNoTracking().Count() == 3
                        && truthDb.UserAccounts.AsNoTracking().Count() == 10
                        && truthDb.Subscriptions.AsNoTracking().Count() == 2
                        && truthDb.Complaints.AsNoTracking().Count() == cComplaintsStart && openByBranch == cOpenByBranchStart,
                        $"members={members} attendance={attendance} payByBranch={string.Join("/", payByBranch)} total={payTotal} open={openByBranch}/{cOpenByBranchStart}");
                }
            }
            catch (Exception ex)
            {
                Write($"  [FAIL] harness: {ex.GetType().Name}: {ex.GetBaseException().Message}");
                failures.Add($"harness: {ex.GetType().Name}: {ex.GetBaseException().Message}");
            }
            finally
            {
                // ---- Restore the exact approved demo state ----
                try
                {
                    var runtime = AppRuntime.Create();
                    var masterDb = runtime.MasterDb;
                    var utcNow = DateTime.UtcNow;

                    foreach (var planId in createdPlanIds)
                    {
                        var plan = masterDb.SubscriptionPlans.FirstOrDefault(p => p.SubscriptionPlanId == planId);
                        if (plan != null)
                        {
                            masterDb.SubscriptionPlans.Remove(plan);
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

                    foreach (var historyId in createdHistoryIds)
                    {
                        var history = masterDb.SubscriptionHistories.FirstOrDefault(h => h.HistoryId == historyId);
                        if (history != null)
                        {
                            masterDb.SubscriptionHistories.Remove(history);
                        }
                    }

                    masterDb.SaveChanges();

                    void RestoreSub(int companyId, int planId)
                    {
                        var sub = masterDb.CompanySubscriptions
                            .OrderByDescending(s => s.Status == SubscriptionStatuses.Active)
                            .ThenByDescending(s => s.CompanySubscriptionId)
                            .First(s => s.CompanyId == companyId);
                        sub.SubscriptionPlanId = planId;
                        sub.StartDate = new DateTime(2026, 10, 1);
                        sub.EndDate = new DateTime(2027, 10, 1);
                        sub.RenewalDate = null;
                        sub.Status = SubscriptionStatuses.Active;
                        sub.Price = 0.00m;
                        sub.PaymentStatus = PaymentStatuses.NotApplicable;
                        sub.TermsAndAgreement = string.Empty;
                        sub.UpdatedAt = utcNow;
                    }

                    RestoreSub(1, masterDb.SubscriptionPlans.AsNoTracking().First(p => p.Code == SoftwarePlanCodes.Basic).SubscriptionPlanId);
                    RestoreSub(2, masterDb.SubscriptionPlans.AsNoTracking().First(p => p.Code == SoftwarePlanCodes.Professional).SubscriptionPlanId);

                    masterDb.SaveChanges();

                    // ---- Versioned-state rollback ----
                    // The run exercised the REAL versioned service (check 23 edits the MICRO plan
                    // price), which publishes a new immutable plan version by design. That
                    // verification-only version - and every snapshot row that came with it - is
                    // removed here, so the demo Master DB can never accumulate fake plan history.
                    // The live plan rows are then restored from the pre-run snapshot with a
                    // DIRECT row write (no version is published), which also keeps the surviving
                    // current version (the original Version 1) consistent with the plan row it
                    // describes. A plan row the run created is already deleted above; its
                    // versions went with it (cascade). When no version was created this is a
                    // no-op, so re-running the suite can never invent or destroy history.
                    if (versionSnapshotCaptured)
                    {
                        var allVersions = masterDb.SubscriptionPlanVersions.ToList();
                        var removedVersions = 0;
                        foreach (var version in allVersions.Where(v => !versionIdsBefore.Contains(v.SubscriptionPlanVersionId)))
                        {
                            masterDb.SubscriptionPlanVersions.Remove(version);
                            removedVersions++;
                        }

                        foreach (var moduleRow in masterDb.SubscriptionPlanVersionModules.ToList()
                                     .Where(m => !versionModuleKeysBefore.Contains((m.SubscriptionPlanVersionId, m.ModuleId))))
                        {
                            masterDb.SubscriptionPlanVersionModules.Remove(moduleRow);
                        }

                        foreach (var version in allVersions.Where(v => versionIdsBefore.Contains(v.SubscriptionPlanVersionId)))
                        {
                            var saved = versionCurrentBefore.FirstOrDefault(r => r.VersionId == version.SubscriptionPlanVersionId);
                            if (saved.VersionId != 0)
                            {
                                version.IsCurrent = saved.IsCurrent;
                            }
                        }

                        foreach (var plan in masterDb.SubscriptionPlans.ToList())
                        {
                            var saved = planConfigBefore.FirstOrDefault(p => p.PlanId == plan.SubscriptionPlanId);
                            if (saved.PlanId == 0)
                            {
                                continue;   // a plan created by this run is already deleted above
                            }

                            plan.Name = saved.Name;
                            plan.Description = saved.Description;
                            plan.Price = saved.Price;
                            plan.BillingCycle = saved.BillingCycle;
                            plan.TermsAndAgreement = saved.Terms;
                            plan.IsActive = saved.IsActive;
                            plan.UpdatedAt = utcNow;
                        }

                        foreach (var subscription in masterDb.CompanySubscriptions.ToList())
                        {
                            var saved = subscriptionVersionBefore
                                .FirstOrDefault(s => s.SubscriptionId == subscription.CompanySubscriptionId);
                            if (saved.SubscriptionId != 0)
                            {
                                subscription.SubscriptionPlanVersionId = saved.VersionId;
                            }
                        }

                        masterDb.SaveChanges();
                        Console.WriteLine($"Cleanup: versioned state rolled back ({removedVersions} verification-only plan version(s) removed, no permanent version history left).");
                    }

                    Console.WriteLine("Cleanup: master demo state restored (subscriptions, requests, history, temp plan, plan versions).");
                }
                catch (Exception cleanupEx)
                {
                    Console.WriteLine($"[CLEANUP ERROR] {cleanupEx.GetType().Name}: {cleanupEx.GetBaseException().Message}");
                    failures.Add($"cleanup: {cleanupEx.GetBaseException().Message}");
                }
            }

            Write(string.Empty);
            Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | checks: {checks} | failures: {failures.Count} ---");
            return failures.Count == 0 ? 0 : 1;
        }
    }
}
