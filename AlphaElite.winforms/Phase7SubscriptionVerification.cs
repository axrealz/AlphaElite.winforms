using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 5-7 acceptance verification (headless, focused battery of exactly 31 checks).
    ///
    /// Phases 5 and 6 are proven through the real user-facing surfaces:
    ///   * the tenant "My Subscription" screen (current plan/status/dates/snapshot, plan
    ///     comparison, submit request, cancel own request) driven through the same methods the
    ///     buttons call, and
    ///   * the composed module enforcement (Company plan AND role permission AND scope) read
    ///     back from a real Form1 session.
    ///
    /// Every master row this harness creates is removed again in the finally block and the
    /// three demo subscriptions are restored to their exact approved demo state. Tenant
    /// databases are only ever opened READ-ONLY (fingerprints). The master database must be
    /// db67598 or the run aborts before touching anything.
    /// </summary>
    internal static class Phase7SubscriptionVerification
    {
        private const string Reviewer = "superadmin-test";

        // Approved Tenant C fingerprint (read-only comparison).
        private const int CMembers = 21;
        private const int CAttendance = 17;
        private const decimal CPayments = 13650m;
        private const int CBranches = 3;
        private const int CUserAccounts = 10;
        private const int CMemberSubscriptions = 2;

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
            // Set only after the master guard passes: an aborted run (wrong database) must not
            // attempt any cleanup against a database we refused to touch.
            var masterVerified = false;

            try
            {
                ApplicationConfiguration.Initialize();
                System.Threading.SynchronizationContext.SetSynchronizationContext(null);

                var runtime = AppRuntime.Create();
                var masterDb = runtime.MasterDb;
                var service = runtime.SubscriptionService;

                var masterDatabase = masterDb.Database.GetDbConnection().Database;
                Write("Operation      : phase7-subscription-verify");
                Write($"Target database: {masterDatabase}");
                if (!masterDatabase.Contains("67598"))
                {
                    Write($"[ABORT] Refusing to run: the master connection does not point at db67598 (got '{masterDatabase}').");
                    return 1;
                }

                masterVerified = true;

                var companyA = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == "COMP001");
                var companyB = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == "COMP002");
                var companyC = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == "COMP003");
                var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();

                // ---------------------------------------------------------------- baselines
                int aMembersStart, aAttendanceStart, aPaymentsStart;
                decimal aPayTotalStart;
                using (var dbA = runtime.DataContextFactory.CreateAsync(companyA.CompanyId, false).GetAwaiter().GetResult())
                {
                    aMembersStart = dbA.Members.AsNoTracking().Count();
                    aAttendanceStart = dbA.Attendances.AsNoTracking().Count();
                    aPaymentsStart = dbA.Payments.AsNoTracking().Count();
                    aPayTotalStart = dbA.Payments.AsNoTracking().Sum(p => p.AmountPaid);
                }

                int bMembersStart, bAttendanceStart, bPaymentsStart;
                decimal bPayTotalStart;
                using (var dbB = runtime.DataContextFactory.CreateAsync(companyB.CompanyId, false).GetAwaiter().GetResult())
                {
                    bMembersStart = dbB.Members.AsNoTracking().Count();
                    bAttendanceStart = dbB.Attendances.AsNoTracking().Count();
                    bPaymentsStart = dbB.Payments.AsNoTracking().Count();
                    bPayTotalStart = dbB.Payments.AsNoTracking().Sum(p => p.AmountPaid);
                }

                string cMemberSubscriptionsStart;
                int cMembersStart;
                using (var dbC = runtime.DataContextFactory.CreateAsync(companyC.CompanyId, true).GetAwaiter().GetResult())
                {
                    cMembersStart = dbC.Members.AsNoTracking().Count();
                    cMemberSubscriptionsStart = string.Join(";", dbC.Subscriptions.AsNoTracking()
                        .OrderBy(s => s.SubscriptionId)
                        .Select(s => $"{s.SubscriptionId}:{s.MemberId}:{s.PlanName}"));
                }

                var tenantDatabaseA = runtime.Resolver.GetDatabaseInfoAsync(companyA.CompanyId).GetAwaiter().GetResult();
                var tenantDatabaseB = runtime.Resolver.GetDatabaseInfoAsync(companyB.CompanyId).GetAwaiter().GetResult();
                var tenantDatabaseC = runtime.Resolver.GetDatabaseInfoAsync(companyC.CompanyId).GetAwaiter().GetResult();

                UserSubscriptionContext BuildContext(int companyId)
                {
                    var info = service.GetCompanySubscriptionAsync(companyId).GetAwaiter().GetResult();
                    if (info == null)
                    {
                        return UserSubscriptionContext.None;
                    }

                    return new UserSubscriptionContext
                    {
                        HasActiveSubscription = info.EffectiveStatus == SubscriptionStatuses.Active,
                        EffectiveStatus = info.EffectiveStatus,
                        PlanName = info.PlanName,
                        EndDate = info.EndDate,
                        EnabledModuleCodes = info.EnabledModuleCodes
                    };
                }

                UserSession SessionFor(int companyId, string username, bool branchAware)
                {
                    var login = runtime.AuthenticationService.AuthenticateAsync(companyId, username, "123").GetAwaiter().GetResult()
                        ?? throw new InvalidOperationException($"Demo login '{username}' failed.");
                    return new UserSession
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
                        Subscription = BuildContext(companyId)
                    };
                }

                Form1 BuildTenantForm(int companyId, string username)
                {
                    var session = SessionFor(companyId, username, false);
                    var form = new Form1(session, tenants, runtime.DataContextFactory, runtime.AuthorizationService, runtime.SubscriptionService)
                    {
                        SuppressDialogs = true
                    };
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                    return form;
                }

                // AsNoTracking is required, not cosmetic: the subscription service now runs every
                // operation on its OWN per-operation MasterAlphaEliteDbContext
                // (ScopedSubscriptionService + IDbContextFactory, the fix for "A second operation
                // was started on this context instance"), so writes are committed by a context this
                // harness does not share. A tracked read here would identity-resolve to the copy
                // cached in this long-lived context (for example the one read below for
                // planIdA/planIdB/planIdC) and would report pre-change values even after the
                // change was committed. Reading the committed row is what the product UI and the
                // real screen do, so that is what the check must observe. Every LiveSubscription
                // call site below is a pure read; the cleanup block has its own tracked query.
                CompanySubscription? LiveSubscription(int companyId) =>
                    masterDb.CompanySubscriptions
                        .AsNoTracking()
                        .OrderByDescending(s => s.Status == SubscriptionStatuses.Active)
                        .ThenByDescending(s => s.CompanySubscriptionId)
                        .FirstOrDefault(s => s.CompanyId == companyId);

                int PlanId(string code) =>
                    masterDb.SubscriptionPlans.AsNoTracking().First(p => p.Code == code).SubscriptionPlanId;

                var planIdA = LiveSubscription(companyA.CompanyId)!.SubscriptionPlanId;
                var planIdB = LiveSubscription(companyB.CompanyId)!.SubscriptionPlanId;
                var planIdC = LiveSubscription(companyC.CompanyId)!.SubscriptionPlanId;

                // ============================================================================
                // SECTION 1 - deployed schema and seeded catalogue (checks 1-10)
                // ============================================================================
                Write(string.Empty);
                Write("--- 1. Deployed master schema + seeded catalogue ---");

                var applied = masterDb.Database.GetAppliedMigrations().ToList();
                Check("migration applied (AddSubscriptionRequestsAndHistory in __EFMigrationsHistory)",
                    applied.Contains("20261006000000_AddSubscriptionRequestsAndHistory"),
                    $"applied={applied.Count} last={applied.LastOrDefault() ?? "(none)"}");

                Check("master Identity users/roles intact (Users=1 Roles=1)",
                    masterDb.Users.AsNoTracking().Count() == 1 && masterDb.Roles.AsNoTracking().Count() == 1,
                    $"Users={masterDb.Users.AsNoTracking().Count()} Roles={masterDb.Roles.AsNoTracking().Count()}");

                Check("Companies intact (3, COMP001/COMP002/COMP003)",
                    masterDb.Companies.AsNoTracking().Count() == 3
                    && new[] { "COMP001", "COMP002", "COMP003" }.All(c => masterDb.Companies.AsNoTracking().Any(x => x.CompanyCode == c)),
                    $"Companies={masterDb.Companies.AsNoTracking().Count()}");

                Check("CompanyDatabase mappings intact (3 -> tenant A/B/C)",
                    masterDb.CompanyDatabase.AsNoTracking().Count() == 3,
                    $"CompanyDatabase={masterDb.CompanyDatabase.AsNoTracking().Count()} A={tenantDatabaseA.DatabaseName} B={tenantDatabaseB.DatabaseName} C={tenantDatabaseC.DatabaseName}");

                var plans = service.GetPlansAsync().GetAwaiter().GetResult();
                var basic = plans.FirstOrDefault(p => p.Code == SoftwarePlanCodes.Basic);
                var professional = plans.FirstOrDefault(p => p.Code == SoftwarePlanCodes.Professional);
                var enterprise = plans.FirstOrDefault(p => p.Code == SoftwarePlanCodes.Enterprise);
                Check("the master catalogue holds EXACTLY the three final plans (Basic 8 / Professional 12 / Enterprise 16) with no legacy plans",
                    plans.Count == 3
                    && basic != null && professional != null && enterprise != null
                    && basic.IsActive && professional.IsActive && enterprise.IsActive
                    && basic.ModuleCodes.Count == 8 && professional.ModuleCodes.Count == 12 && enterprise.ModuleCodes.Count == 16
                    && !plans.Any(p => p.Code == "MICRO_BUSINESS" || p.Code == "SMALL_BUSINESS"),
                    $"plans={plans.Count}: {string.Join(",", plans.Select(p => $"{p.Code}={p.ModuleCodes.Count}"))}");

                var modules = masterDb.SoftwareModules.AsNoTracking().ToList();
                Check("software module catalogue = the 16 approved codes",
                    modules.Count == 16
                    && SoftwareModuleCodes.All.All(c => modules.Any(m => m.Code == c)),
                    $"modules={modules.Count}");

                var mappings = masterDb.PlanModules.AsNoTracking().ToList();
                Check("PlanModule mappings intact (36) and the three plans keep 8/12/16",
                    mappings.Count == 36
                    && mappings.Count(pm => pm.SubscriptionPlanId == PlanId(SoftwarePlanCodes.Basic) && pm.IsActive) == 8
                    && mappings.Count(pm => pm.SubscriptionPlanId == PlanId(SoftwarePlanCodes.Professional) && pm.IsActive) == 12
                    && mappings.Count(pm => pm.SubscriptionPlanId == PlanId(SoftwarePlanCodes.Enterprise) && pm.IsActive) == 16,
                    $"mappings={mappings.Count} BASIC=8 PROFESSIONAL=12 ENTERPRISE=16");

                Check("CompanySubscription rows intact (3: COMP001=BASIC COMP002=PROFESSIONAL COMP003=ENTERPRISE, Active, demo window)",
                    masterDb.CompanySubscriptions.AsNoTracking().Count() == 3
                    && planIdA == PlanId(SoftwarePlanCodes.Basic)
                    && planIdB == PlanId(SoftwarePlanCodes.Professional)
                    && planIdC == PlanId(SoftwarePlanCodes.Enterprise)
                    && LiveSubscription(companyA.CompanyId)!.Status == SubscriptionStatuses.Active
                    && LiveSubscription(companyA.CompanyId)!.StartDate.Date == new DateTime(2026, 10, 1)
                    && LiveSubscription(companyA.CompanyId)!.EndDate.Date == new DateTime(2027, 10, 1),
                    $"subs={masterDb.CompanySubscriptions.AsNoTracking().Count()} A={planIdA} B={planIdB} C={planIdC}");

                var historyBefore = masterDb.SubscriptionHistories.AsNoTracking().Count();
                Check("SubscriptionHistory table exists and is empty before the run",
                    historyBefore == 0,
                    $"history={historyBefore}");

                var requestsBefore = masterDb.SubscriptionChangeRequests.AsNoTracking().Count();
                Check("SubscriptionChangeRequest table exists and is empty before the run",
                    requestsBefore == 0,
                    $"requests={requestsBefore}");

                // ============================================================================
                // SECTION 2 - the tenant screen submits a request (checks 11-14)
                // ============================================================================
                Write(string.Empty);
                Write("--- 2. Tenant screen: read plan, submit request (Pending only) ---");

                var navRegressionExpected = new Dictionary<string, string>
                {
                    ["adminA"] = "Members, Attendance, Payments, Feedback, Inquiries, Complaints",
                    ["adminB"] = "Business Intelligence, Members, Attendance, Payments, Feedback, Inquiries, Retention, Complaints, UserManagement"
                };

                Form1 formA;
                using (formA = BuildTenantForm(companyA.CompanyId, "adminA"))
                {
                    formA.LoadSoftwareSubscriptionAsync().GetAwaiter().GetResult();
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);

                    var screenOk = formA.SoftwareSubscriptionScreenAvailableForTest
                        && !formA.SoftwareSubscriptionNavHiddenForTest
                        && formA.TenantSubscriptionSummaryForTest.Contains("BASIC")
                        && formA.TenantSubscriptionSummaryForTest.Contains("Active");
                    Check("tenant screen reads the CURRENT plan/status/snapshot from the master DB and excludes the current plan from the targets",
                        screenOk
                        && formA.AvailablePlanChoicesForTest.Any(p => p.Code == SoftwarePlanCodes.Professional)
                        && formA.AvailablePlanChoicesForTest.All(p => p.Code != SoftwarePlanCodes.Basic),
                        formA.TenantSubscriptionSummaryForTest);

                    var layout = formA.DescribeNavLayoutForTest();
                    var positions = layout.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(part => int.Parse(part.Substring(part.IndexOf('@') + 1)))
                        .ToList();
                    var uniform = positions.Count > 0 && positions[0] == 100
                        && positions.Zip(positions.Skip(1), (a, b) => b - a).All(gap => gap == 44);
                    Check("adding the tenant subscription entry changes NO existing tenant navigation (nav string + uniform 44px sidebar)",
                        formA.DescribeNav() == navRegressionExpected["adminA"]
                        && layout.Contains("My Subscription@")
                        && uniform,
                        $"{formA.DescribeNav()} || {layout}");

                    // Rapid double-click on the screen's entry point must never overlap two
                    // master reads on the shared context ("A second operation was started on
                    // this context instance"). The screen's gate serializes/supersedes them.
                    var serialized = true;
                    var serializedDetail = "both overlapping loads completed";
                    try
                    {
                        var first = formA.LoadSoftwareSubscriptionAsync();
                        var second = formA.LoadSoftwareSubscriptionAsync();
                        Task.WhenAll(first, second).GetAwaiter().GetResult();
                    }
                    catch (Exception overlapEx)
                    {
                        serialized = false;
                        serializedDetail = $"{overlapEx.GetType().Name}: {overlapEx.GetBaseException().Message}";
                    }

                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                    Check("overlapping tenant subscription-screen loads are serialized on the shared master context",
                        serialized, serializedDetail);

                    formA.SelectPlanChoiceForTest(SoftwarePlanCodes.Professional);
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                    Check("plan comparison shows the exact module difference and the derived change type",
                        formA.TenantModuleComparisonForTest.Contains("retention")
                        && formA.TenantModuleComparisonForTest.Contains("businessIntelligence")
                        && formA.TenantModuleComparisonForTest.Contains("Upgrade"),
                        formA.TenantModuleComparisonForTest);

                    var submit = formA.SubmitSubscriptionRequestForTestAsync(SoftwarePlanCodes.Professional, "We need reports and retention.").GetAwaiter().GetResult();
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                    Check("tenant submits an upgrade request through the screen's own path",
                        submit.Success,
                        submit.Success ? "submitted" : submit.Error);

                    var pendingRow = masterDb.SubscriptionChangeRequests.AsNoTracking()
                        .OrderByDescending(r => r.SubscriptionChangeRequestId)
                        .FirstOrDefault(r => r.CompanyId == companyA.CompanyId);
                    if (pendingRow != null)
                    {
                        createdRequestIds.Add(pendingRow.SubscriptionChangeRequestId);
                    }

                    var pendingRequestId = pendingRow?.SubscriptionChangeRequestId ?? -1;

                    Check("the request is created as PENDING ONLY: the live subscription is untouched",
                        pendingRow != null
                        && pendingRow.Status == SubscriptionChangeRequestStatuses.Pending
                        && pendingRow.RequestType == SubscriptionRequestTypes.Upgrade
                        && pendingRow.RequestedPlanId == PlanId(SoftwarePlanCodes.Professional)
                        && pendingRow.ReviewedBy == null
                        && LiveSubscription(companyA.CompanyId)!.SubscriptionPlanId == PlanId(SoftwarePlanCodes.Basic)
                        && masterDb.SubscriptionHistories.AsNoTracking().Count() == 0
                        && formA.OwnRequestsForTest.Any(r => r.SubscriptionChangeRequestId == pendingRequestId && r.Status == SubscriptionChangeRequestStatuses.Pending),
                        $"status={pendingRow?.Status} type={pendingRow?.RequestType} livePlan={LiveSubscription(companyA.CompanyId)!.SubscriptionPlanId} history={masterDb.SubscriptionHistories.AsNoTracking().Count()}");

                    var selfReview = service.ReviewChangeRequestAsync(pendingRequestId, true, "adminA", "self approval").GetAwaiter().GetResult();
                    Check("the requesting identity CANNOT approve its own request (refused by the service, no plan change)",
                        !selfReview.Success
                        && LiveSubscription(companyA.CompanyId)!.SubscriptionPlanId == PlanId(SoftwarePlanCodes.Basic)
                        && masterDb.SubscriptionChangeRequests.AsNoTracking().First(r => r.SubscriptionChangeRequestId == pendingRequestId).Status == SubscriptionChangeRequestStatuses.Pending,
                        selfReview.Error);

                    // ---- Approved path (checks 15-17, 23, 24) ----
                    var visibleToSuperAdmin = service.GetChangeRequestsAsync(SubscriptionChangeRequestStatuses.Pending).GetAwaiter().GetResult()
                        .FirstOrDefault(r => r.SubscriptionChangeRequestId == pendingRequestId);
                    var approveA = service.ReviewChangeRequestAsync(pendingRequestId, true, Reviewer, "Approved after review.").GetAwaiter().GetResult();
                    var historyA = masterDb.SubscriptionHistories.AsNoTracking()
                        .Where(h => h.CompanyId == companyA.CompanyId)
                        .OrderByDescending(h => h.HistoryId)
                        .FirstOrDefault();
                    if (historyA != null)
                    {
                        createdHistoryIds.Add(historyA.HistoryId);
                    }

                    Check("the Super Admin review grid sees the request as Pending",
                        visibleToSuperAdmin != null
                        && visibleToSuperAdmin.CompanyCode == "COMP001"
                        && visibleToSuperAdmin.Status == SubscriptionChangeRequestStatuses.Pending,
                        visibleToSuperAdmin == null ? "(not visible)" : $"{visibleToSuperAdmin.CompanyCode}/{visibleToSuperAdmin.RequestedPlanCode}/{visibleToSuperAdmin.Status}");

                    Check("approval succeeds and the approval CHANGES the CompanySubscription (plan, window, renewal)",
                        approveA.Success
                        && LiveSubscription(companyA.CompanyId)!.SubscriptionPlanId == PlanId(SoftwarePlanCodes.Professional)
                        && LiveSubscription(companyA.CompanyId)!.Status == SubscriptionStatuses.Active
                        && LiveSubscription(companyA.CompanyId)!.StartDate.Date == DateTime.UtcNow.Date
                        && LiveSubscription(companyA.CompanyId)!.EndDate.Date == DateTime.UtcNow.Date.AddYears(1)
                        && LiveSubscription(companyA.CompanyId)!.RenewalDate.HasValue,
                        approveA.Success ? "subscription updated" : approveA.Error);

                    Check("approval writes exactly one SubscriptionHistory row (Upgrade, previous -> new plan, reviewer)",
                        masterDb.SubscriptionHistories.AsNoTracking().Count() == 1
                        && historyA != null
                        && historyA.Action == SubscriptionHistoryActions.Upgrade
                        && historyA.PreviousPlanId == PlanId(SoftwarePlanCodes.Basic)
                        && historyA.NewPlanId == PlanId(SoftwarePlanCodes.Professional)
                        && historyA.ChangedBy == Reviewer
                        && masterDb.SubscriptionChangeRequests.AsNoTracking().First(r => r.SubscriptionChangeRequestId == pendingRequestId).Status == SubscriptionChangeRequestStatuses.Approved,
                        historyA == null ? "(no history)" : $"{historyA.Action} {historyA.PreviousPlanId}->{historyA.NewPlanId} by {historyA.ChangedBy}");
                }

                // ============================================================================
                // SECTION 3 - upgrade exposes modules / downgrade restricts (checks 18-19)
                // ============================================================================
                Write(string.Empty);
                Write("--- 3. Approved changes take effect in the tenant window ---");

                using (var upgradedForm = BuildTenantForm(companyA.CompanyId, "adminA"))
                {
                    var nav = upgradedForm.DescribeNav();
                    var enabled = service.GetEnabledModuleCodesAsync(companyA.CompanyId).GetAwaiter().GetResult();
                    Check("UPGRADE exposes the newly licensed modules to the tenant (Retention now reachable for COMP001)",
                        enabled.Contains(SoftwareModuleCodes.Retention)
                        && enabled.Contains(SoftwareModuleCodes.Reports)
                        && upgradedForm.IsModuleEnabledForTest(SoftwareModuleCodes.Retention)
                        && nav.Contains("Retention")
                        && upgradedForm.IsModuleEffectivelyAvailable(SoftwareModuleCodes.Retention),
                        $"modules={enabled.Count} nav={nav}");
                }

                // COMP002: PROFESSIONAL -> BASIC (downgrade), with an edited price/terms.
                var planBasic = masterDb.SubscriptionPlans.First(p => p.Code == SoftwarePlanCodes.Basic);
                const decimal tempBasicPrice = 12.34m;
                const string tempBasicTerms = "TEMP-TERMS-PHASE7";
                planBasic.Price = tempBasicPrice;
                planBasic.TermsAndAgreement = tempBasicTerms;
                planBasic.UpdatedAt = DateTime.UtcNow;
                masterDb.SaveChanges();

                var submitB = service.SubmitChangeRequestAsync(companyB.CompanyId, SoftwarePlanCodes.Basic, "adminB", "Downgrade please.").GetAwaiter().GetResult();
                var requestB = masterDb.SubscriptionChangeRequests.AsNoTracking()
                    .OrderByDescending(r => r.SubscriptionChangeRequestId)
                    .FirstOrDefault(r => r.CompanyId == companyB.CompanyId);
                if (requestB != null)
                {
                    createdRequestIds.Add(requestB.SubscriptionChangeRequestId);
                }

                var approveB = requestB == null
                    ? SubscriptionMutationResult.Fail("no request")
                    : service.ReviewChangeRequestAsync(requestB.SubscriptionChangeRequestId, true, Reviewer, "Approved downgrade.").GetAwaiter().GetResult();
                var historyB = masterDb.SubscriptionHistories.AsNoTracking()
                    .Where(h => h.CompanyId == companyB.CompanyId)
                    .OrderByDescending(h => h.HistoryId)
                    .FirstOrDefault();
                if (historyB != null)
                {
                    createdHistoryIds.Add(historyB.HistoryId);
                }

                using (var downgradedForm = BuildTenantForm(companyB.CompanyId, "adminB"))
                {
                    var nav = downgradedForm.DescribeNav();
                    var enabled = service.GetEnabledModuleCodesAsync(companyB.CompanyId).GetAwaiter().GetResult();
                    Check("DOWNGRADE restricts the tenant to the smaller plan (Retention/UserManagement gone, core modules kept)",
                        submitB.Success && approveB.Success
                        && requestB!.RequestType == SubscriptionRequestTypes.Downgrade
                        && enabled.Count == 8
                        && !enabled.Contains(SoftwareModuleCodes.Retention)
                        && !downgradedForm.IsModuleEnabledForTest(SoftwareModuleCodes.Retention)
                        && !nav.Contains("Retention")
                        && nav.Contains("Members")
                        && historyB != null
                        && historyB.Action == SubscriptionHistoryActions.Downgrade,
                        $"submit={submitB.Success} approve={approveB.Success} modules={enabled.Count} nav={nav}");

                    Check("approval SNAPSHOTS the plan price and terms (later plan edits never rewrite the subscription)",
                        Math.Abs(LiveSubscription(companyB.CompanyId)!.Price - tempBasicPrice) < 0.0001m
                        && LiveSubscription(companyB.CompanyId)!.TermsAndAgreement == tempBasicTerms
                        && requestB!.RequestedPrice == tempBasicPrice,
                        $"snapshot price={LiveSubscription(companyB.CompanyId)!.Price} terms={(LiveSubscription(companyB.CompanyId)!.TermsAndAgreement.Length > 0 ? "captured" : "empty")}");

                    planBasic.Price = 99.99m;
                    planBasic.TermsAndAgreement = "LIMITTERMS-PHASE7";
                    planBasic.UpdatedAt = DateTime.UtcNow;
                    masterDb.SaveChanges();
                    Check("editing the plan afterwards leaves the company's snapshot untouched",
                        Math.Abs(LiveSubscription(companyB.CompanyId)!.Price - tempBasicPrice) < 0.0001m
                        && LiveSubscription(companyB.CompanyId)!.TermsAndAgreement == tempBasicTerms,
                        $"plan={planBasic.Price} subscription={LiveSubscription(companyB.CompanyId)!.Price}");
                }

                // ============================================================================
                // SECTION 4 - rejection changes nothing (checks 20-21)
                // ============================================================================
                Write(string.Empty);
                Write("--- 4. Rejection: records the reviewer + notes, changes nothing ---");

                var submitC = service.SubmitChangeRequestAsync(companyC.CompanyId, SoftwarePlanCodes.Professional, "adminC", "Downgrade request.").GetAwaiter().GetResult();
                var requestC = masterDb.SubscriptionChangeRequests.AsNoTracking()
                    .OrderByDescending(r => r.SubscriptionChangeRequestId)
                    .FirstOrDefault(r => r.CompanyId == companyC.CompanyId);
                if (requestC != null)
                {
                    createdRequestIds.Add(requestC.SubscriptionChangeRequestId);
                }

                var historyCountBeforeReject = masterDb.SubscriptionHistories.AsNoTracking().Count();
                var rejectC = requestC == null
                    ? SubscriptionMutationResult.Fail("no request")
                    : service.ReviewChangeRequestAsync(requestC.SubscriptionChangeRequestId, false, Reviewer, "Not this quarter - keep Enterprise.").GetAwaiter().GetResult();
                var rejectedRow = requestC == null
                    ? null
                    : masterDb.SubscriptionChangeRequests.AsNoTracking().First(r => r.SubscriptionChangeRequestId == requestC.SubscriptionChangeRequestId);

                Check("rejection does NOT change the company's plan (ENTERPRISE still live)",
                    submitC.Success && rejectC.Success
                    && LiveSubscription(companyC.CompanyId)!.SubscriptionPlanId == planIdC
                    && LiveSubscription(companyC.CompanyId)!.Status == SubscriptionStatuses.Active
                    && rejectedRow!.Status == SubscriptionChangeRequestStatuses.Rejected,
                    $"status={rejectedRow?.Status} livePlan={LiveSubscription(companyC.CompanyId)!.SubscriptionPlanId}");

                Check("rejection records the reviewer, the review time and the notes and writes NO history row",
                    rejectedRow!.ReviewedBy == Reviewer
                    && rejectedRow.ReviewedAt.HasValue
                    && rejectedRow.ReviewNotes == "Not this quarter - keep Enterprise."
                    && masterDb.SubscriptionHistories.AsNoTracking().Count() == historyCountBeforeReject,
                    $"reviewedBy={rejectedRow.ReviewedBy} notes=recorded history={masterDb.SubscriptionHistories.AsNoTracking().Count()}");

                // ============================================================================
                // SECTION 5 - plan lifecycle (checks 22-24)
                // ============================================================================
                Write(string.Empty);
                Write("--- 5. Plan lifecycle: inactive plans are unselectable and deactivation destroys nothing ---");

                var setInactive = service.SetPlanActiveAsync(PlanId(SoftwarePlanCodes.Basic), false).GetAwaiter().GetResult();

                using (var lifecycleForm = BuildTenantForm(companyA.CompanyId, "adminA"))
                {
                    lifecycleForm.LoadSoftwareSubscriptionAsync().GetAwaiter().GetResult();
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                    var offeredBasic = lifecycleForm.AvailablePlanChoicesForTest.Any(p => p.Code == SoftwarePlanCodes.Basic);
                    var blockedSubmit = lifecycleForm.SubmitSubscriptionRequestForTestAsync(SoftwarePlanCodes.Basic, "try inactive").GetAwaiter().GetResult();
                    var blockedAtService = service.SubmitChangeRequestAsync(companyA.CompanyId, SoftwarePlanCodes.Basic, "adminA", "try inactive").GetAwaiter().GetResult();
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);

                    Check("an INACTIVE plan cannot be selected or requested (hidden from the screen and refused by the service)",
                        setInactive.Success && !offeredBasic && !blockedSubmit.Success && !blockedAtService.Success,
                        $"offered={offeredBasic} screenRefused={!blockedSubmit.Success} serviceRefused={!blockedAtService.Success}");
                }

                var basicOwnerSub = LiveSubscription(companyB.CompanyId);
                var basicEnabledWhileInactive = service.GetEnabledModuleCodesAsync(companyB.CompanyId).GetAwaiter().GetResult();
                Check("deactivating a plan does NOT destroy existing subscriptions (COMP002 keeps BASIC and its modules)",
                    basicOwnerSub!.SubscriptionPlanId == PlanId(SoftwarePlanCodes.Basic)
                    && masterDb.CompanySubscriptions.AsNoTracking().Count() == 3
                    && basicEnabledWhileInactive.Count == 8,
                    $"plan={basicOwnerSub.SubscriptionPlanId} subs={masterDb.CompanySubscriptions.AsNoTracking().Count()} modules={basicEnabledWhileInactive.Count}");

                var reactivateBasic = service.SetPlanActiveAsync(PlanId(SoftwarePlanCodes.Basic), true).GetAwaiter().GetResult();
                var basicActiveAgain = masterDb.SubscriptionPlans.AsNoTracking().First(p => p.Code == SoftwarePlanCodes.Basic).IsActive;
                Check("the plan is restored to Active and the company subscription still works (reactivation path intact)",
                    reactivateBasic.Success && basicActiveAgain
                    && service.GetEnabledModuleCodesAsync(companyB.CompanyId).GetAwaiter().GetResult().Count == 8,
                    $"basicActive={basicActiveAgain}");

                // ============================================================================
                // SECTION 6 - tenant immutability of subscription state (checks 25-27)
                // ============================================================================
                Write(string.Empty);
                Write("--- 6. Tenants can request, never administer ---");

                var authorization = runtime.AuthorizationService;
                var masterCtorNeedsMasterSession = typeof(MasterDashboardForm).GetConstructors()
                    .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(MasterUserSession)));

                var tenantMethods = typeof(Form1)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(m => m.Name)
                    .ToList();

                // Tenant-side subscription surface: only the submit/cancel/load paths may exist.
                // (The retention module has its own unrelated "Approve" button, so the check is
                // scoped to the subscription surface plus the master-only mutation APIs.)
                var subscriptionSurface = tenantMethods
                    .Where(n => n.Contains("Subscription", StringComparison.OrdinalIgnoreCase)
                                || n.Contains("SoftwareSubscription", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var hasApprovalSurface = subscriptionSurface.Any(n =>
                        n.Contains("Approve", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("Reject", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("Review", StringComparison.OrdinalIgnoreCase))
                    || tenantMethods.Any(n =>
                        n.Contains("ReviewChangeRequest", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("SetPlanActive", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("UpdatePlan", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("CreatePlan", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("AddModuleToPlan", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("RemoveModuleFromPlan", StringComparison.OrdinalIgnoreCase));

                Check("the tenant window exposes NO approval/plan-editing surface and no tenant role holds master subscription permissions",
                    !hasApprovalSurface
                    && !authorization.IsAuthorized(Roles.Admin, Permission.TenantManagement)
                    && !authorization.IsAuthorized(Roles.Admin, Permission.AdminPanel)
                    && !authorization.IsAuthorized(Roles.Admin, Permission.ManageSubscriptions)
                    && !authorization.IsAuthorized(Roles.Manager, Permission.ManageSubscriptions)
                    && !authorization.IsAuthorized(Roles.GymStaff, Permission.ManageSubscriptions)
                    && masterCtorNeedsMasterSession,
                    $"approvalSurface={hasApprovalSurface} adminTenantMgmt={authorization.IsAuthorized(Roles.Admin, Permission.TenantManagement)} masterWindowGated={masterCtorNeedsMasterSession}");

                using (var staffForm = BuildTenantForm(companyA.CompanyId, "adminA"))
                using (var gymStaffForm = BuildTenantForm(companyB.CompanyId, "gymstaffB"))
                {
                    Check("only the company Admin is offered the subscription screen (GymStaff gets no entry at all)",
                        staffForm.SoftwareSubscriptionScreenAvailableForTest
                        && !staffForm.SoftwareSubscriptionNavHiddenForTest
                        && gymStaffForm.SoftwareSubscriptionNavHiddenForTest
                        && !gymStaffForm.SoftwareSubscriptionScreenAvailableForTest,
                        $"adminVisible={!staffForm.SoftwareSubscriptionNavHiddenForTest} gymstaffHidden={gymStaffForm.SoftwareSubscriptionNavHiddenForTest}");
                }

                // The SUPER ADMIN screen's own render path must work: it previously dereferenced
                // an unloaded CompanySubscription.SubscriptionPlan navigation (NullReferenceException).
                var masterRenderCheck =
                    "the Super Admin Subscription screen renders every company row without throwing (no unloaded-navigation dereference)";
                try
                {
                    using var masterForm = new MasterDashboardForm(runtime, new MasterUserSession
                    {
                        MasterUserId = "phase7-superadmin",
                        Username = "superadmin",
                        Role = Roles.MasterAdmin,
                        IdentityRoleName = MasterIdentityService.SuperAdminRoleName
                    });

                    // Creating the window's controls re-installs a WinForms synchronization
                    // context on this thread; blocking on async work would then deadlock. Reset
                    // it (the established harness pattern) before awaiting the render path.
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                    var masterRender = masterForm.RenderSubscriptionCompaniesForTestAsync().GetAwaiter().GetResult();
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);

                    Check(masterRenderCheck,
                        masterForm.SubscriptionCompanyRowCountForTest == 3
                        && masterRender.Contains("COMP001:") && masterRender.Contains("COMP002:") && masterRender.Contains("COMP003:")
                        && masterRender.Contains("Price: 0.00 / Monthly")
                        && masterRender.Contains("Status: Active"),
                        masterRender);
                }
                catch (Exception renderEx)
                {
                    var frame = (renderEx.StackTrace ?? string.Empty)
                        .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(f => f.Trim())
                        .FirstOrDefault() ?? "(no stack)";
                    Check(masterRenderCheck, false, $"{renderEx.GetType().Name}: {renderEx.GetBaseException().Message} @ {frame}");
                }

                // The Super Admin screen fires overlapping master reads when tabs are switched
                // quickly; they must queue on the serialization gate instead of racing.
                var masterOverlapCheck =
                    "overlapping Super Admin subscription loads are serialized on the shared master context";
                try
                {
                    using var masterForm = new MasterDashboardForm(runtime, new MasterUserSession
                    {
                        MasterUserId = "phase7-superadmin",
                        Username = "superadmin",
                        Role = Roles.MasterAdmin,
                        IdentityRoleName = MasterIdentityService.SuperAdminRoleName
                    });
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                    var overlap = masterForm.RunOverlappingSubscriptionLoadsForTestAsync().GetAwaiter().GetResult();
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);

                    Check(masterOverlapCheck,
                        overlap.Contains("companies=3") && overlap.Contains("plans=3"),
                        overlap);
                }
                catch (Exception overlapEx)
                {
                    Check(masterOverlapCheck, false, $"{overlapEx.GetType().Name}: {overlapEx.GetBaseException().Message}");
                }

                var cancelRequest = service.SubmitChangeRequestAsync(companyA.CompanyId, SoftwarePlanCodes.Basic, "adminA", "back to basic").GetAwaiter().GetResult();
                var ownRequest = masterDb.SubscriptionChangeRequests.AsNoTracking()
                    .OrderByDescending(r => r.SubscriptionChangeRequestId)
                    .FirstOrDefault(r => r.CompanyId == companyA.CompanyId);
                if (ownRequest != null)
                {
                    createdRequestIds.Add(ownRequest.SubscriptionChangeRequestId);
                }

                var cancelByOther = ownRequest == null
                    ? SubscriptionMutationResult.Fail("no request")
                    : service.CancelChangeRequestAsync(ownRequest.SubscriptionChangeRequestId, "someone-else").GetAwaiter().GetResult();
                var cancelByOwner = ownRequest == null
                    ? SubscriptionMutationResult.Fail("no request")
                    : service.CancelChangeRequestAsync(ownRequest.SubscriptionChangeRequestId, "adminA").GetAwaiter().GetResult();

                using (var cancelForm = BuildTenantForm(companyA.CompanyId, "adminA"))
                {
                    cancelForm.LoadSoftwareSubscriptionAsync().GetAwaiter().GetResult();
                    System.Threading.SynchronizationContext.SetSynchronizationContext(null);
                    Check("the tenant can cancel its OWN pending request, another identity cannot, and a cancelled request never changed the plan",
                        cancelRequest.Success && !cancelByOther.Success && cancelByOwner.Success
                        && masterDb.SubscriptionChangeRequests.AsNoTracking().First(r => r.SubscriptionChangeRequestId == ownRequest!.SubscriptionChangeRequestId).Status == SubscriptionChangeRequestStatuses.Cancelled
                        && LiveSubscription(companyA.CompanyId)!.SubscriptionPlanId == PlanId(SoftwarePlanCodes.Professional)
                        && cancelForm.OwnRequestsForTest.All(r => r.Status != SubscriptionChangeRequestStatuses.Pending),
                        $"othersRefused={!cancelByOther.Success} ownerCancelled={cancelByOwner.Success}");
                }

                // ============================================================================
                // SECTION 7 - isolation, login, build (checks 28-31)
                // ============================================================================
                Write(string.Empty);
                Write("--- 7. Isolation, login and build ---");

                int aMembersEnd, aAttendanceEnd, aPaymentsEnd;
                decimal aPayTotalEnd;
                using (var dbAEnd = runtime.DataContextFactory.CreateAsync(companyA.CompanyId, false).GetAwaiter().GetResult())
                {
                    aMembersEnd = dbAEnd.Members.AsNoTracking().Count();
                    aAttendanceEnd = dbAEnd.Attendances.AsNoTracking().Count();
                    aPaymentsEnd = dbAEnd.Payments.AsNoTracking().Count();
                    aPayTotalEnd = dbAEnd.Payments.AsNoTracking().Sum(p => p.AmountPaid);
                }

                int bMembersEnd, bAttendanceEnd, bPaymentsEnd;
                decimal bPayTotalEnd;
                using (var dbBEnd = runtime.DataContextFactory.CreateAsync(companyB.CompanyId, false).GetAwaiter().GetResult())
                {
                    bMembersEnd = dbBEnd.Members.AsNoTracking().Count();
                    bAttendanceEnd = dbBEnd.Attendances.AsNoTracking().Count();
                    bPaymentsEnd = dbBEnd.Payments.AsNoTracking().Count();
                    bPayTotalEnd = dbBEnd.Payments.AsNoTracking().Sum(p => p.AmountPaid);
                }

                int cMembersEnd, cAttendanceEnd, cBranchesEnd, cUsersEnd, cSubsEnd;
                decimal cPaymentsEnd;
                using (var dbCEnd = runtime.DataContextFactory.CreateAsync(companyC.CompanyId, true).GetAwaiter().GetResult())
                {
                    cMembersEnd = dbCEnd.Members.AsNoTracking().Count();
                    cAttendanceEnd = dbCEnd.Attendances.AsNoTracking().Count();
                    cBranchesEnd = dbCEnd.Branches.AsNoTracking().Count();
                    cUsersEnd = dbCEnd.UserAccounts.AsNoTracking().Count();
                    cSubsEnd = dbCEnd.Subscriptions.AsNoTracking().Count();
                    cPaymentsEnd = dbCEnd.Payments.AsNoTracking().Sum(p => p.AmountPaid);
                }

                Check("tenant A/B operational data unchanged (members/attendance/payments + totals vs start-of-run baseline)",
                    aMembersEnd == aMembersStart && aAttendanceEnd == aAttendanceStart && aPaymentsEnd == aPaymentsStart && aPayTotalEnd == aPayTotalStart
                    && bMembersEnd == bMembersStart && bAttendanceEnd == bAttendanceStart && bPaymentsEnd == bPaymentsStart && bPayTotalEnd == bPayTotalStart
                    && aMembersStart == 2 && aAttendanceStart == 2 && aPaymentsStart == 2 && aPayTotalStart == 1500m
                    && bMembersStart == 1 && bAttendanceStart == 1 && bPaymentsStart == 1 && bPayTotalStart == 1000m,
                    $"A {aMembersStart}/{aAttendanceStart}/{aPaymentsStart}/{aPayTotalStart}->{aMembersEnd}/{aAttendanceEnd}/{aPaymentsEnd}/{aPayTotalEnd}; B {bMembersStart}/{bAttendanceStart}/{bPaymentsStart}/{bPayTotalStart}->{bMembersEnd}/{bAttendanceEnd}/{bPaymentsEnd}/{bPayTotalEnd}");

                Check("tenant C fingerprint unchanged (21 members / 17 attendance / 3 branches / 13650 payments / 10 users / 2 member subscriptions)",
                    cMembersEnd == CMembers && cMembersEnd == cMembersStart
                    && cAttendanceEnd == CAttendance
                    && cBranchesEnd == CBranches
                    && cUsersEnd == CUserAccounts
                    && cSubsEnd == CMemberSubscriptions
                    && cPaymentsEnd == CPayments,
                    $"members={cMembersEnd} attendance={cAttendanceEnd} branches={cBranchesEnd} users={cUsersEnd} subs={cSubsEnd} payments={cPaymentsEnd}");

                string cMemberSubscriptionsEnd;
                using (var dbCRaw = runtime.DataContextFactory.CreateAsync(companyC.CompanyId, true).GetAwaiter().GetResult())
                {
                    cMemberSubscriptionsEnd = string.Join(";", dbCRaw.Subscriptions.AsNoTracking()
                        .OrderBy(s => s.SubscriptionId)
                        .Select(s => $"{s.SubscriptionId}:{s.MemberId}:{s.PlanName}"));
                }

                Check("Tenant C member Subscriptions (gym memberships) are byte-for-byte identical - the SOFTWARE subscription system never touches them",
                    cMemberSubscriptionsEnd == cMemberSubscriptionsStart && cSubsEnd == CMemberSubscriptions,
                    $"rows=[{cMemberSubscriptionsEnd}]");

                var loginA = runtime.AuthenticationService.AuthenticateAsync(companyA.CompanyId, "adminA", "123").GetAwaiter().GetResult();
                var loginB = runtime.AuthenticationService.AuthenticateAsync(companyB.CompanyId, "adminB", "123").GetAwaiter().GetResult();
                // Tenant C's branch demo accounts are the verified "123" logins for COMP003.
                var loginC = runtime.AuthenticationService.AuthenticateAsync(companyC.CompanyId, "managerCebu", "123").GetAwaiter().GetResult();
                var badLogin = runtime.AuthenticationService.AuthenticateAsync(companyA.CompanyId, "adminA", "wrong-password").GetAwaiter().GetResult();
                Check("tenant login still works after the subscription phases (demo accounts authenticate; a wrong password does not)",
                    loginA != null && loginB != null && loginC != null && badLogin == null
                    && loginA.Role == Roles.Admin && loginB.Role == Roles.Admin && loginC.Role == Roles.Manager
                    && loginC.IsBranchAware,
                    $"A={loginA?.Role} B={loginB?.Role} C={loginC?.Role} wrongPasswordRejected={badLogin == null}");

                Check("build artifacts present for every project (build itself: see the separate dotnet build log, 0 errors)",
                    new[]
                    {
                        "AlphaElite.domain/bin/Debug/net10.0/AlphaElite.domain.dll",
                        "WinFormsLibrary1/bin/Debug/net10.0/AlphaElite.infrastructure.dll",
                        "AlphaElite.api/bin/Debug/net10.0/AlphaElite.api.dll",
                        "AlphaElite.winforms/bin/Debug/net10.0-windows/AlphaElite.winforms.dll"
                    }.All(p => File.Exists(Path.Combine(RepositoryRoot(), p))),
                    "checked " + RepositoryRoot());
            }
            finally
            {
                if (!masterVerified)
                {
                    // The master guard refused this target: nothing was read or written, so no
                    // cleanup (and no database connection) is attempted.
                    Console.WriteLine("Cleanup skipped: the master guard refused this target, so nothing was touched.");
                }
                else
                {
                    try
                    {
                    var runtime = AppRuntime.Create();
                    var masterDb = runtime.MasterDb;
                    var utcNow = DateTime.UtcNow;

                    foreach (var requestId in createdRequestIds.Distinct())
                    {
                        var request = masterDb.SubscriptionChangeRequests.FirstOrDefault(r => r.SubscriptionChangeRequestId == requestId);
                        if (request != null)
                        {
                            masterDb.SubscriptionChangeRequests.Remove(request);
                        }
                    }

                    foreach (var historyId in createdHistoryIds.Distinct())
                    {
                        var history = masterDb.SubscriptionHistories.FirstOrDefault(h => h.HistoryId == historyId);
                        if (history != null)
                        {
                            masterDb.SubscriptionHistories.Remove(history);
                        }
                    }

                    // The approved demo state has BOTH workflow tables EMPTY (Phase 3 and Phase 4
                    // verify that as their start-of-run baseline), so every remaining row must go -
                    // including rows left behind by an earlier run that was aborted before its
                    // cleanup (e.g. the process was killed). Rows this run created are removed
                    // above; this sweep makes the restore complete and deterministic.
                    foreach (var request in masterDb.SubscriptionChangeRequests.ToList())
                    {
                        masterDb.SubscriptionChangeRequests.Remove(request);
                    }

                    foreach (var history in masterDb.SubscriptionHistories.ToList())
                    {
                        masterDb.SubscriptionHistories.Remove(history);
                    }


                    masterDb.SaveChanges();

                    void RestoreSubscription(string companyCode, string planCode)
                    {
                        var planId = masterDb.SubscriptionPlans.AsNoTracking().First(p => p.Code == planCode).SubscriptionPlanId;
                        var companyId = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == companyCode).CompanyId;
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
                        sub.PaymentStatus = "NotApplicable";
                        sub.TermsAndAgreement = string.Empty;

                        // The plan-version link MUST follow the restored plan. The downgrade this
                        // run exercises moved the link to the downgrade plan's version; restoring
                        // only the plan would leave a CROSS-PLAN pointer behind (exactly the residue
                        // that used to accumulate in the demo database), so it is re-pointed at the
                        // restored plan's own current version here.
                        var currentVersion = masterDb.SubscriptionPlanVersions
                            .Where(v => v.SubscriptionPlanId == planId)
                            .OrderByDescending(v => v.IsCurrent)
                            .ThenByDescending(v => v.VersionNumber)
                            .FirstOrDefault();
                        sub.SubscriptionPlanVersionId = currentVersion?.SubscriptionPlanVersionId;
                        sub.UpdatedAt = utcNow;
                    }

                    RestoreSubscription("COMP001", SoftwarePlanCodes.Basic);
                    RestoreSubscription("COMP002", SoftwarePlanCodes.Professional);
                    RestoreSubscription("COMP003", SoftwarePlanCodes.Enterprise);

                    // The BASIC plan was temporarily given a price/terms (the downgrade snapshot
                    // check) and deactivated/reactivated (the lifecycle check). Restore both so
                    // the demo catalogue starts from its approved state on the next run.
                    var basicPlanRow = masterDb.SubscriptionPlans.FirstOrDefault(p => p.Code == SoftwarePlanCodes.Basic);
                    if (basicPlanRow != null)
                    {
                        basicPlanRow.Price = 0.00m;
                        basicPlanRow.TermsAndAgreement = string.Empty;
                        basicPlanRow.IsActive = true;
                        basicPlanRow.UpdatedAt = utcNow;
                    }

                    masterDb.SaveChanges();
                    Console.WriteLine("Cleanup: master demo state restored (demo subscriptions, empty workflow tables, plan price/terms/active flags).");
                }
                    catch (Exception cleanupEx)
                    {
                        Console.WriteLine($"[CLEANUP ERROR] {cleanupEx.GetType().Name}: {cleanupEx.GetBaseException().Message}");
                        failures.Add($"cleanup: {cleanupEx.GetBaseException().Message}");
                    }
                }
            }

            Write(string.Empty);
            Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | checks: {checks} | failures: {failures.Count} ---");
            return failures.Count == 0 ? 0 : 1;
        }

        /// <summary>
        /// Walks up from the executable to the repository root (the folder that CONTAINS the
        /// AlphaElite.winforms project folder - the repository root and the project folder
        /// share a name, so the marker is the nested project file, not the folder name).
        /// </summary>
        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null
                   && !File.Exists(Path.Combine(directory.FullName, "AlphaElite.winforms", "AlphaElite.winforms.csproj")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? AppContext.BaseDirectory;
        }
    }
}
