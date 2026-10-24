using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaElite.infrastructure.services
{
    /// <summary>
    /// Stable Alpha Elite SOFTWARE module codes (Phase 9). These are the exact codes seeded
    /// into the master SoftwareModules table and the exact strings the tenant navigation
    /// gating compares against. They are unrelated to RBAC Permission flags: a user still
    /// needs BOTH the role permission AND the company subscription to see a module.
    /// </summary>
    public static class SoftwareModuleCodes
    {
        public const string Dashboard = "dashboard";
        public const string Members = "members";
        public const string Attendance = "attendance";
        public const string Payments = "payments";
        public const string Subscriptions = "subscriptions";
        public const string Inquiries = "inquiries";
        public const string Feedback = "feedback";
        public const string Complaints = "complaints";
        public const string Retention = "retention";
        public const string Reports = "reports";
        public const string BusinessIntelligence = "businessIntelligence";
        public const string UserManagement = "userManagement";
        public const string BranchManagement = "branchManagement";
        public const string MemberPortal = "memberPortal";
        public const string AdvancedBi = "advancedBi";
        public const string AdvancedRetention = "advancedRetention";

        /// <summary>The canonical 16 codes, in the approved plan order.</summary>
        public static readonly string[] All =
        {
            Dashboard, Members, Attendance, Payments, Subscriptions, Inquiries,
            Feedback, Complaints, Retention, Reports, BusinessIntelligence, UserManagement,
            BranchManagement, MemberPortal, AdvancedBi, AdvancedRetention
        };
    }

    /// <summary>
    /// Stable software plan codes of the FINAL subscription system. There are exactly THREE
    /// plans; the earlier MICRO_BUSINESS / SMALL_BUSINESS launch plans were removed from the
    /// product (see the guarded master cleanup), so their codes no longer exist anywhere in the
    /// application - a request or assignment that names them is refused as an unknown plan.
    /// </summary>
    public static class SoftwarePlanCodes
    {
        public const string Basic = "BASIC";
        public const string Professional = "PROFESSIONAL";
        public const string Enterprise = "ENTERPRISE";

        /// <summary>The three and only three plans, in catalogue order.</summary>
        public static readonly string[] All = { Basic, Professional, Enterprise };
    }

    /// <summary>
    /// Classifies a plan change from the two ACTIVE module sets. Shared by the service
    /// (which stores the derived RequestType) and the tenant screen (which shows the tenant
    /// what its request will be recorded as), so both can never disagree.
    /// </summary>
    public static class PlanChangeClassifier
    {
        public static string Classify(IEnumerable<string> currentCodes, IEnumerable<string> requestedCodes)
        {
            var current = currentCodes.ToHashSet(StringComparer.Ordinal);
            var requested = requestedCodes.ToHashSet(StringComparer.Ordinal);

            if (requested.IsSupersetOf(current) && requested.Count > current.Count)
            {
                return SubscriptionRequestTypes.Upgrade;
            }

            if (current.IsSupersetOf(requested) && current.Count > requested.Count)
            {
                return SubscriptionRequestTypes.Downgrade;
            }

            return SubscriptionRequestTypes.ChangePlan;
        }
    }

    /// <summary>CompanySubscriptions.Status values.</summary>
    public static class SubscriptionStatuses
    {
        public const string Active = "Active";
        public const string Pending = "Pending";
        public const string Expired = "Expired";
        public const string Suspended = "Suspended";
        public const string Cancelled = "Cancelled";

        public static readonly string[] All = { Active, Pending, Expired, Suspended, Cancelled };
    }

    /// <summary>SubscriptionPlan.BillingCycle values (stored as strings, never hardcoded in UI logic).</summary>
    public static class BillingCycles
    {
        public const string Monthly = "Monthly";
        public const string Yearly = "Yearly";

        public static readonly string[] All = { Monthly, Yearly };
    }

    /// <summary>CompanySubscription.PaymentStatus values (software state only - no gateway).</summary>
    public static class PaymentStatuses
    {
        public const string Paid = "Paid";
        public const string Pending = "Pending";
        public const string Failed = "Failed";
        public const string NotApplicable = "NotApplicable";

        public static readonly string[] All = { Paid, Pending, Failed, NotApplicable };
    }

    /// <summary>SubscriptionChangeRequest.Status values.</summary>
    public static class SubscriptionChangeRequestStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
        public const string Cancelled = "Cancelled";

        public static readonly string[] All = { Pending, Approved, Rejected, Cancelled };
    }

    /// <summary>SubscriptionChangeRequest.RequestType values.</summary>
    public static class SubscriptionRequestTypes
    {
        public const string Upgrade = "Upgrade";
        public const string Downgrade = "Downgrade";
        public const string ChangePlan = "ChangePlan";

        public static readonly string[] All = { Upgrade, Downgrade, ChangePlan };
    }

    /// <summary>SubscriptionHistory.Action values.</summary>
    public static class SubscriptionHistoryActions
    {
        public const string InitialSubscription = "InitialSubscription";
        public const string Upgrade = "Upgrade";
        public const string Downgrade = "Downgrade";
        public const string Renewal = "Renewal";
        public const string Revert = "Revert";
        public const string Suspension = "Suspension";
        public const string Reactivation = "Reactivation";
        public const string Cancellation = "Cancellation";

        public static readonly string[] All =
            { InitialSubscription, Upgrade, Downgrade, Renewal, Revert, Suspension, Reactivation, Cancellation };
    }

    /// <summary>
    /// SubscriptionPlanVersion.ChangeType values (plan-configuration versioning). These are
    /// stable identifiers; the Super Admin UI maps them to readable labels through
    /// <see cref="PlanVersionChangeTypes.Describe"/>.
    /// </summary>
    public static class PlanVersionChangeTypes
    {
        /// <summary>Version 1 of a plan that already existed when versioning was introduced.</summary>
        public const string InitialVersion = "InitialVersion";

        /// <summary>Version 1 of a plan created through the Super Admin plan editor.</summary>
        public const string PlanCreated = "PlanCreated";

        public const string ModuleAdded = "ModuleAdded";
        public const string ModuleRemoved = "ModuleRemoved";
        public const string TermsChanged = "TermsChanged";
        public const string PriceChanged = "PriceChanged";
        public const string BillingCycleChanged = "BillingCycleChanged";

        /// <summary>More than one material field changed in the same save.</summary>
        public const string ConfigurationChanged = "ConfigurationChanged";

        public static readonly string[] All =
        {
            InitialVersion, PlanCreated, ModuleAdded, ModuleRemoved,
            TermsChanged, PriceChanged, BillingCycleChanged, ConfigurationChanged
        };

        /// <summary>Readable label for the History grid (never a raw CLR identifier).</summary>
        public static string Describe(string? changeType) => changeType switch
        {
            InitialVersion => "Original plan",
            PlanCreated => "Plan created",
            ModuleAdded => "Added module",
            ModuleRemoved => "Removed module",
            TermsChanged => "Updated terms",
            PriceChanged => "Updated price",
            BillingCycleChanged => "Changed billing cycle",
            ConfigurationChanged => "Configuration updated",
            _ => string.IsNullOrWhiteSpace(changeType) ? "(unknown)" : changeType!
        };
    }

    /// <summary>
    /// Everything a client (login flow, tenant window, master UI) needs to know about a
    /// company's SOFTWARE subscription. Carries no credential material.
    /// </summary>
    public sealed class CompanySubscriptionInfo
    {
        public int CompanySubscriptionId { get; init; }
        public int CompanyId { get; init; }
        public string CompanyName { get; init; } = string.Empty;
        public string CompanyCode { get; init; } = string.Empty;

        public int SubscriptionPlanId { get; init; }
        public string PlanCode { get; init; } = string.Empty;
        public string PlanName { get; init; } = string.Empty;

        public DateTime StartDate { get; init; }
        public DateTime EndDate { get; init; }
        public string Status { get; init; } = string.Empty;
        public string Notes { get; init; } = string.Empty;

        /// <summary>Effective status after applying the date rules to the stored status.</summary>
        public string EffectiveStatus { get; init; } = string.Empty;

        // ------------------------------------------------------------------
        // Subscription-request phase: the subscription's own SNAPSHOT values. These are
        // shown to the tenant on its "My Subscription" screen and are never re-derived
        // from the live plan, so a later plan edit cannot rewrite what a company agreed to.
        // ------------------------------------------------------------------

        /// <summary>Next renewal date (null when not scheduled).</summary>
        public DateTime? RenewalDate { get; init; }

        /// <summary>Snapshot of the plan price at activation.</summary>
        public decimal Price { get; init; }

        /// <summary>The plan's billing cycle at display time (informational).</summary>
        public string BillingCycle { get; init; } = string.Empty;

        /// <summary>Paid / Pending / Failed / NotApplicable (software state, no gateway).</summary>
        public string PaymentStatus { get; init; } = string.Empty;

        /// <summary>Snapshot of the accepted Terms &amp; Agreement.</summary>
        public string TermsAndAgreement { get; init; } = string.Empty;

        /// <summary>When the company originally subscribed.</summary>
        public DateTime DateSubscribed { get; init; }

        /// <summary>
        /// Plan-configuration VERSION this subscription received (null for subscriptions that
        /// predate versioning or whose plan has no version yet). The subscription's own price /
        /// terms snapshots stay the authoritative agreed values.
        /// </summary>
        public int? PlanVersionNumber { get; init; }

        public DateTime? PlanVersionEffectiveDate { get; init; }

        public IReadOnlyList<string> EnabledModuleCodes { get; init; } = Array.Empty<string>();

        public int EnabledModuleCount => EnabledModuleCodes.Count;
    }

    /// <summary>The result of a master-side subscription mutation.</summary>
    public sealed class SubscriptionMutationResult
    {
        public bool Success { get; init; }
        public string Error { get; init; } = string.Empty;

        public static SubscriptionMutationResult Ok() => new() { Success = true };

        public static SubscriptionMutationResult Fail(string error) => new() { Success = false, Error = error };
    }

    /// <summary>A plan as shown in the Super Admin plan editor (module codes resolved).</summary>
    public sealed class SubscriptionPlanInfo
    {
        public int SubscriptionPlanId { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public string BillingCycle { get; init; } = string.Empty;
        public string TermsAndAgreement { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public IReadOnlyList<string> ModuleCodes { get; init; } = Array.Empty<string>();

        // ---------------------------------------------------------------------------------
        // Plan-configuration versioning (plan cards + Terms & Agreement view). Null while a
        // plan has no version yet (never happens after the idempotent initializer has run).
        // ---------------------------------------------------------------------------------

        public int? CurrentVersionId { get; init; }

        /// <summary>The plan's current Terms &amp; Agreement / module-set version (1, 2, 3 ...).</summary>
        public int? CurrentVersionNumber { get; init; }

        public DateTime? CurrentVersionEffectiveDate { get; init; }

        /// <summary>How many versions exist for the plan (history depth, informational).</summary>
        public int VersionCount { get; init; }
    }

    /// <summary>
    /// ONE immutable plan-configuration version as shown in the plan cards and the History
    /// grid. Everything here is a SNAPSHOT stored on the version row (plus its own module
    /// snapshot rows) - nothing is re-derived from the live plan or from PlanModules.
    /// </summary>
    public sealed class SubscriptionPlanVersionInfo
    {
        public int SubscriptionPlanVersionId { get; init; }
        public int SubscriptionPlanId { get; init; }
        public string PlanCode { get; init; } = string.Empty;
        public string PlanName { get; init; } = string.Empty;
        public int VersionNumber { get; init; }
        public string ChangeType { get; init; } = string.Empty;
        public string ChangeTypeLabel { get; init; } = string.Empty;
        public string ChangeSummary { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public string BillingCycle { get; init; } = string.Empty;
        public string TermsAndAgreement { get; init; } = string.Empty;
        public bool PlanIsActive { get; init; }
        public DateTime EffectiveDate { get; init; }
        public bool IsCurrent { get; init; }
        public string ChangedBy { get; init; } = string.Empty;
        public DateTime ChangedAt { get; init; }

        /// <summary>The version's OWN module codes, in the canonical plan order.</summary>
        public IReadOnlyList<string> ModuleCodes { get; init; } = Array.Empty<string>();

        /// <summary>The version's OWN module display names (snapshotted).</summary>
        public IReadOnlyList<string> ModuleNames { get; init; } = Array.Empty<string>();

        public int ModuleCount => ModuleCodes.Count;
    }

    /// <summary>
    /// A plan version compared against the version before it - what the History details view
    /// shows. Both sides come from the immutable snapshot rows of each version.
    /// </summary>
    public sealed class SubscriptionPlanVersionDetails
    {
        public SubscriptionPlanVersionInfo Version { get; init; } = new();
        public SubscriptionPlanVersionInfo? PreviousVersion { get; init; }

        public IReadOnlyList<string> AddedModuleCodes { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> AddedModuleNames { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> RemovedModuleCodes { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> RemovedModuleNames { get; init; } = Array.Empty<string>();

        public int PreviousModuleCount => PreviousVersion?.ModuleCount ?? 0;
        public int NewModuleCount => Version.ModuleCount;

        public decimal? PreviousPrice => PreviousVersion?.Price;
        public decimal NewPrice => Version.Price;

        public string PreviousBillingCycle => PreviousVersion?.BillingCycle ?? string.Empty;
        public string NewBillingCycle => Version.BillingCycle;

        public string PreviousTermsAndAgreement => PreviousVersion?.TermsAndAgreement ?? string.Empty;
        public string NewTermsAndAgreement => Version.TermsAndAgreement;

        public bool TermsChanged => PreviousVersion != null
            && !string.Equals(PreviousVersion.TermsAndAgreement, Version.TermsAndAgreement, StringComparison.Ordinal);

        public bool PriceChanged => PreviousVersion != null && PreviousVersion.Price != Version.Price;

        public bool BillingCycleChanged => PreviousVersion != null
            && !string.Equals(PreviousVersion.BillingCycle, Version.BillingCycle, StringComparison.Ordinal);
    }

    /// <summary>Result of the idempotent Version-1 initializer.</summary>
    public sealed class PlanVersionInitializationResult
    {
        public bool Success { get; init; }
        public string Error { get; init; } = string.Empty;
        public int PlansScanned { get; init; }
        public int VersionsCreated { get; init; }
        public int PlansAlreadyVersioned { get; init; }
        public int SubscriptionsLinked { get; init; }
        public IReadOnlyList<string> Detail { get; init; } = Array.Empty<string>();

        public static PlanVersionInitializationResult Fail(string error) => new() { Success = false, Error = error };
    }

    /// <summary>One OPTION 2 subscription change request as shown in the review grids.</summary>
    public sealed class SubscriptionChangeRequestInfo
    {
        public int SubscriptionChangeRequestId { get; init; }
        public int CompanyId { get; init; }
        public string CompanyCode { get; init; } = string.Empty;
        public string CompanyName { get; init; } = string.Empty;
        public string CurrentPlanCode { get; init; } = string.Empty;
        public string RequestedPlanCode { get; init; } = string.Empty;
        public string RequestType { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string RequestedBy { get; init; } = string.Empty;
        public DateTime RequestedAt { get; init; }
        public string? ReviewedBy { get; init; }
        public DateTime? ReviewedAt { get; init; }
        public string ReviewNotes { get; init; } = string.Empty;
        public decimal? RequestedPrice { get; init; }
    }

    /// <summary>One subscription audit/history row as shown in the history viewer.</summary>
    public sealed class SubscriptionHistoryInfo
    {
        public int HistoryId { get; init; }
        public int CompanyId { get; init; }
        public string CompanyCode { get; init; } = string.Empty;
        public string CompanyName { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string PreviousPlanCode { get; init; } = string.Empty;
        public string NewPlanCode { get; init; } = string.Empty;
        public decimal? PreviousPrice { get; init; }
        public decimal? NewPrice { get; init; }
        public string ChangedBy { get; init; } = string.Empty;
        public DateTime ChangedAt { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string Notes { get; init; } = string.Empty;
    }

    /// <summary>
    /// Master-DB-only subscription management (Phase 9). Master access is fully encapsulated
    /// here: the tenant window never queries the master database directly, it only receives
    /// the effective module list through <see cref="UserSubscriptionContext"/>.
    ///
    /// Subscription-request phase additions: plan/module catalog administration and the
    /// OPTION 2 workflow (tenant requests a plan change -> Pending SubscriptionChangeRequest
    /// -> Super Admin approves (transactional CompanySubscription update + price/terms
    /// snapshot + SubscriptionHistory row) or rejects (reviewer + notes, no plan change)).
    /// </summary>
    public interface ISubscriptionService
    {
        /// <summary>Every company with its current software subscription (null when none).</summary>
        Task<IReadOnlyList<CompanySubscriptionInfo>> GetAllCompanySubscriptionsAsync(CancellationToken cancellationToken = default);

        /// <summary>One company's current software subscription, or null when it has none.</summary>
        Task<CompanySubscriptionInfo?> GetCompanySubscriptionAsync(int companyId, CancellationToken cancellationToken = default);

        /// <summary>The enabled module codes for the company's current plan (empty when no active subscription).</summary>
        Task<IReadOnlyList<string>> GetEnabledModuleCodesAsync(int companyId, CancellationToken cancellationToken = default);

        /// <summary>True when the company's subscription is currently in a state that includes the module.</summary>
        Task<bool> IsModuleEnabledAsync(int companyId, string moduleCode, CancellationToken cancellationToken = default);

        /// <summary>Change the plan of a company's current subscription.</summary>
        Task<SubscriptionMutationResult> ChangePlanAsync(int companyId, string planCode, CancellationToken cancellationToken = default);

        /// <summary>Update the start/end dates of a company's current subscription.</summary>
        Task<SubscriptionMutationResult> UpdateDatesAsync(int companyId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

        /// <summary>Explicitly suspend a company's subscription.</summary>
        Task<SubscriptionMutationResult> SuspendAsync(int companyId, CancellationToken cancellationToken = default);

        /// <summary>Reactivate a suspended/expired subscription (dates unchanged).</summary>
        Task<SubscriptionMutationResult> ReactivateAsync(int companyId, CancellationToken cancellationToken = default);

        // -----------------------------------------------------------------------
        // Subscription-request phase: plans, modules, requests, history (OPTION 2)
        // -----------------------------------------------------------------------

        /// <summary>Every plan (including inactive) with its resolved module codes.</summary>
        Task<IReadOnlyList<SubscriptionPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default);

        /// <summary>The full module catalog.</summary>
        Task<IReadOnlyList<SoftwareModule>> GetModulesAsync(CancellationToken cancellationToken = default);

        /// <summary>Create a plan (the UI offers no plan deletion - deactivation only).
        /// A plan always receives Version 1 of its configuration.</summary>
        Task<SubscriptionMutationResult> CreatePlanAsync(
            string code, string name, string description, decimal price, string billingCycle, string termsAndAgreement,
            CancellationToken cancellationToken = default, string changedBy = "");

        /// <summary>Edit a plan's name / description / price / billing cycle / terms. A material
        /// change (price, billing cycle or Terms &amp; Agreement) creates a NEW immutable version;
        /// saving identical values creates nothing.</summary>
        Task<SubscriptionMutationResult> UpdatePlanAsync(
            int subscriptionPlanId, string name, string description, decimal price, string billingCycle, string termsAndAgreement,
            CancellationToken cancellationToken = default, string changedBy = "");

        /// <summary>Activate or deactivate a plan (inactive plans cannot be newly selected).
        /// Activation state is NOT a subscribed-terms change, so it never creates a version.</summary>
        Task<SubscriptionMutationResult> SetPlanActiveAsync(int subscriptionPlanId, bool isActive, CancellationToken cancellationToken = default);

        /// <summary>Include a module in a plan (upsert; re-activates a previously removed mapping).
        /// A module that BECOMES active creates a new immutable version with its own module set.</summary>
        Task<SubscriptionMutationResult> AddModuleToPlanAsync(int subscriptionPlanId, string moduleCode, CancellationToken cancellationToken = default, string changedBy = "");

        /// <summary>Remove a module from a plan (deactivates the mapping row; never deletes).
        /// A module that BECOMES inactive creates a new immutable version with its own module set.</summary>
        Task<SubscriptionMutationResult> RemoveModuleFromPlanAsync(int subscriptionPlanId, string moduleCode, CancellationToken cancellationToken = default, string changedBy = "");

        // -----------------------------------------------------------------------
        // Plan-configuration versioning (immutable Terms & Agreement / module snapshots)
        // -----------------------------------------------------------------------

        /// <summary>Every version (newest first per plan), optionally for one plan only.</summary>
        Task<IReadOnlyList<SubscriptionPlanVersionInfo>> GetPlanVersionsAsync(int? subscriptionPlanId = null, CancellationToken cancellationToken = default);

        /// <summary>One version with its own module set and the diff against the version before it.</summary>
        Task<SubscriptionPlanVersionDetails?> GetPlanVersionDetailsAsync(int subscriptionPlanVersionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Idempotent Version 1 initialization: every plan WITHOUT any version receives Version 1
        /// built from its CURRENT configuration (name / description / price / billing cycle /
        /// terms / activation flag) and its CURRENT module set, and existing subscriptions are
        /// linked to their plan's current version. Running it again creates nothing.
        /// </summary>
        Task<PlanVersionInitializationResult> EnsureInitialPlanVersionsAsync(string changedBy = "", CancellationToken cancellationToken = default);

        /// <summary>
        /// OPTION 2: submit a plan-change request for a company. Creates a Pending
        /// SubscriptionChangeRequest and NOTHING else - the active subscription is untouched.
        /// The request type is derived from the module sets of the current vs requested plan.
        /// </summary>
        Task<SubscriptionMutationResult> SubmitChangeRequestAsync(
            int companyId, string requestedPlanCode, string requestedBy, string notes,
            CancellationToken cancellationToken = default);

        /// <summary>Requests for the review grids (newest first; optionally filtered by status).</summary>
        Task<IReadOnlyList<SubscriptionChangeRequestInfo>> GetChangeRequestsAsync(string? status = null, CancellationToken cancellationToken = default);

        /// <summary>Cancel the company's own Pending request (tenant-side self-service).</summary>
        Task<SubscriptionMutationResult> CancelChangeRequestAsync(int requestId, string requestedBy, CancellationToken cancellationToken = default);

        /// <summary>
        /// Super Admin review. Approve: validates company + plan (exists and still active),
        /// updates/creates the current CompanySubscription inside a transaction (price and
        /// terms are SNAPSHOT-copied from the plan), sets the request Approved and writes the
        /// SubscriptionHistory row. Reject: records reviewer + notes and changes nothing.
        /// </summary>
        Task<SubscriptionMutationResult> ReviewChangeRequestAsync(
            int requestId, bool approve, string reviewedBy, string reviewNotes,
            CancellationToken cancellationToken = default);

        /// <summary>Subscription audit history, newest first (optionally per company).</summary>
        Task<IReadOnlyList<SubscriptionHistoryInfo>> GetHistoryAsync(int? companyId = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One-time subscription lookup result consumed by the tenant login flow (built in
    /// Program.cs from a single master read) and used to gate the tenant navigation.
    /// </summary>
    public sealed class UserSubscriptionContext
    {
        /// <summary>True when the company's subscription currently grants modules at all.</summary>
        public bool HasActiveSubscription { get; init; }

        /// <summary>Effective status: Active / Expired / Suspended ("" when the company has no subscription).</summary>
        public string EffectiveStatus { get; init; } = string.Empty;

        /// <summary>Display plan name ("" when the company has no subscription).</summary>
        public string PlanName { get; init; } = string.Empty;

        public DateTime EndDate { get; init; }

        public IReadOnlyList<string> EnabledModuleCodes { get; init; } = Array.Empty<string>();

        public bool IsModuleEnabled(string moduleCode) => EnabledModuleCodes.Contains(moduleCode);

        public static UserSubscriptionContext None { get; } = new();
    }

    /// <summary>
    /// Subscription status rules (approved Phase 9A design):
    ///   Suspended - explicit administrative state, takes precedence over everything;
    ///   Expired   - EndDate has passed (and not explicitly suspended);
    ///   Active    - otherwise.
    /// Nothing is ever deleted when a subscription expires or is suspended.
    /// </summary>
    public static class SubscriptionStatusCalculator
    {
        public static string Calculate(string storedStatus, DateTime endDate, DateTime? nowUtc = null)
        {
            var now = (nowUtc ?? DateTime.UtcNow).Date;

            if (string.Equals(storedStatus, SubscriptionStatuses.Suspended, StringComparison.OrdinalIgnoreCase))
            {
                return SubscriptionStatuses.Suspended;
            }

            return endDate.Date < now ? SubscriptionStatuses.Expired : SubscriptionStatuses.Active;
        }
    }

    /// <summary>Master DB implementation of the software subscription management.</summary>
    public sealed class SubscriptionService : ISubscriptionService
    {
        private readonly MasterAlphaEliteDbContext _masterDb;

        public SubscriptionService(MasterAlphaEliteDbContext masterDb)
        {
            _masterDb = masterDb ?? throw new ArgumentNullException(nameof(masterDb));
        }

        public async Task<IReadOnlyList<CompanySubscriptionInfo>> GetAllCompanySubscriptionsAsync(CancellationToken cancellationToken = default)
        {
            var companies = await _masterDb.Companies
                .AsNoTracking()
                .OrderBy(c => c.CompanyId)
                .ToListAsync(cancellationToken);

            var result = new List<CompanySubscriptionInfo>();
            foreach (var company in companies)
            {
                var info = await BuildInfoAsync(company.CompanyId, company.CompanyCode, company.CompanyName, cancellationToken);
                if (info != null)
                {
                    result.Add(info);
                }
                else
                {
                    result.Add(new CompanySubscriptionInfo
                    {
                        CompanyId = company.CompanyId,
                        CompanyCode = company.CompanyCode,
                        CompanyName = company.CompanyName,
                        PlanCode = string.Empty,
                        PlanName = "(no subscription)",
                        Status = string.Empty,
                        EffectiveStatus = "None"
                    });
                }
            }

            return result;
        }

        public async Task<CompanySubscriptionInfo?> GetCompanySubscriptionAsync(int companyId, CancellationToken cancellationToken = default)
        {
            var company = await _masterDb.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CompanyId == companyId, cancellationToken);

            if (company == null)
            {
                return null;
            }

            return await BuildInfoAsync(company.CompanyId, company.CompanyCode, company.CompanyName, cancellationToken);
        }

        public async Task<IReadOnlyList<string>> GetEnabledModuleCodesAsync(int companyId, CancellationToken cancellationToken = default)
        {
            var info = await GetCompanySubscriptionAsync(companyId, cancellationToken);
            return info?.EnabledModuleCodes ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        public async Task<bool> IsModuleEnabledAsync(int companyId, string moduleCode, CancellationToken cancellationToken = default)
        {
            var codes = await GetEnabledModuleCodesAsync(companyId, cancellationToken);
            return codes.Contains(moduleCode, StringComparer.Ordinal);
        }

        public async Task<SubscriptionMutationResult> ChangePlanAsync(int companyId, string planCode, CancellationToken cancellationToken = default)
        {
            if (!SoftwarePlanCodes.All.Contains(planCode, StringComparer.OrdinalIgnoreCase))
            {
                return SubscriptionMutationResult.Fail($"Unknown plan code '{planCode}'.");
            }

            var subscription = await GetCurrentSubscriptionAsync(companyId, cancellationToken);
            if (subscription == null)
            {
                return SubscriptionMutationResult.Fail($"Company {companyId} has no subscription to change.");
            }

            var plan = await _masterDb.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Code == planCode, cancellationToken);

            if (plan == null || !plan.IsActive)
            {
                return SubscriptionMutationResult.Fail($"Plan '{planCode}' does not exist or is inactive.");
            }

            subscription.SubscriptionPlanId = plan.SubscriptionPlanId;
            // Informational association with the plan configuration the company now uses. The
            // agreed price/terms/date snapshots are deliberately untouched by this legacy
            // administrative path (the OPTION 2 approval is the snapshot-taking workflow).
            subscription.SubscriptionPlanVersionId = await GetCurrentPlanVersionIdAsync(plan.SubscriptionPlanId, cancellationToken);
            subscription.UpdatedAt = DateTime.UtcNow;
            await _masterDb.SaveChangesAsync(cancellationToken);
            return SubscriptionMutationResult.Ok();
        }

        public async Task<SubscriptionMutationResult> UpdateDatesAsync(int companyId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
        {
            if (endDate.Date < startDate.Date)
            {
                return SubscriptionMutationResult.Fail("End date must not be before the start date.");
            }

            var subscription = await GetCurrentSubscriptionAsync(companyId, cancellationToken);
            if (subscription == null)
            {
                return SubscriptionMutationResult.Fail($"Company {companyId} has no subscription to update.");
            }

            subscription.StartDate = startDate.Date;
            subscription.EndDate = endDate.Date;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _masterDb.SaveChangesAsync(cancellationToken);
            return SubscriptionMutationResult.Ok();
        }

        public async Task<SubscriptionMutationResult> SuspendAsync(int companyId, CancellationToken cancellationToken = default)
        {
            var subscription = await GetCurrentSubscriptionAsync(companyId, cancellationToken);
            if (subscription == null)
            {
                return SubscriptionMutationResult.Fail($"Company {companyId} has no subscription to suspend.");
            }

            subscription.Status = SubscriptionStatuses.Suspended;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _masterDb.SaveChangesAsync(cancellationToken);
            return SubscriptionMutationResult.Ok();
        }

        public async Task<SubscriptionMutationResult> ReactivateAsync(int companyId, CancellationToken cancellationToken = default)
        {
            var subscription = await GetCurrentSubscriptionAsync(companyId, cancellationToken);
            if (subscription == null)
            {
                return SubscriptionMutationResult.Fail($"Company {companyId} has no subscription to reactivate.");
            }

            // Explicit suspension is lifted; the date rules decide Active vs Expired from here.
            subscription.Status = SubscriptionStatuses.Active;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _masterDb.SaveChangesAsync(cancellationToken);
            return SubscriptionMutationResult.Ok();
        }

        // -------------------------------------------------------------------------------------
        // Subscription-request phase: plans, modules, requests, history (OPTION 2)
        // -------------------------------------------------------------------------------------

        public async Task<IReadOnlyList<SubscriptionPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default)
        {
            var plans = await _masterDb.SubscriptionPlans
                .AsNoTracking()
                .OrderBy(p => p.SubscriptionPlanId)
                .ToListAsync(cancellationToken);

            // Plan-configuration versioning: the plan cards and the editor must show the plan's
            // CURRENT version (number + effective date) without re-deriving anything.
            var versions = await _masterDb.SubscriptionPlanVersions
                .AsNoTracking()
                .Select(v => new { v.SubscriptionPlanVersionId, v.SubscriptionPlanId, v.VersionNumber, v.IsCurrent, v.EffectiveDate })
                .ToListAsync(cancellationToken);

            var result = new List<SubscriptionPlanInfo>();
            foreach (var plan in plans)
            {
                var codes = await GetActiveModuleCodesAsync(plan.SubscriptionPlanId, cancellationToken);
                var planVersions = versions.Where(v => v.SubscriptionPlanId == plan.SubscriptionPlanId).ToList();
                var current = planVersions.FirstOrDefault(v => v.IsCurrent)
                    ?? planVersions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

                result.Add(new SubscriptionPlanInfo
                {
                    SubscriptionPlanId = plan.SubscriptionPlanId,
                    Code = plan.Code,
                    Name = plan.Name,
                    Description = plan.Description,
                    Price = plan.Price,
                    BillingCycle = plan.BillingCycle,
                    TermsAndAgreement = plan.TermsAndAgreement,
                    IsActive = plan.IsActive,
                    ModuleCodes = codes,
                    CurrentVersionId = current?.SubscriptionPlanVersionId,
                    CurrentVersionNumber = current?.VersionNumber,
                    CurrentVersionEffectiveDate = current?.EffectiveDate,
                    VersionCount = planVersions.Count
                });
            }

            return result;
        }

        public async Task<IReadOnlyList<SoftwareModule>> GetModulesAsync(CancellationToken cancellationToken = default)
        {
            return await _masterDb.SoftwareModules
                .AsNoTracking()
                .OrderBy(m => m.ModuleId)
                .ToListAsync(cancellationToken);
        }

        public async Task<SubscriptionMutationResult> CreatePlanAsync(
            string code, string name, string description, decimal price, string billingCycle, string termsAndAgreement,
            CancellationToken cancellationToken = default, string changedBy = "")
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                return SubscriptionMutationResult.Fail("Plan code and name are required.");
            }

            if (!BillingCycles.All.Contains(billingCycle, StringComparer.Ordinal))
            {
                return SubscriptionMutationResult.Fail($"Billing cycle must be one of: {string.Join(", ", BillingCycles.All)}.");
            }

            if (price < 0)
            {
                return SubscriptionMutationResult.Fail("Price must not be negative.");
            }

            var normalizedCode = code.Trim().ToUpperInvariant();
            if (await _masterDb.SubscriptionPlans.AnyAsync(p => p.Code == normalizedCode, cancellationToken))
            {
                return SubscriptionMutationResult.Fail($"A plan with code '{normalizedCode}' already exists.");
            }

            var utcNow = DateTime.UtcNow;
            var plan = new SubscriptionPlan
            {
                Code = normalizedCode,
                Name = name.Trim(),
                Description = description?.Trim() ?? string.Empty,
                Price = price,
                BillingCycle = billingCycle,
                TermsAndAgreement = termsAndAgreement ?? string.Empty,
                IsActive = true,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            };

            _masterDb.SubscriptionPlans.Add(plan);

            // A plan always starts at Version 1 of its configuration. The version is attached to
            // the plan through the navigation property, so EF fixes up the identity key and the
            // plan row is inserted first (one transaction, no half-versioned plan).
            var version = new SubscriptionPlanVersion
            {
                SubscriptionPlan = plan,
                VersionNumber = 1,
                PlanCode = plan.Code,
                PlanName = plan.Name,
                Description = plan.Description,
                Price = plan.Price,
                BillingCycle = plan.BillingCycle,
                TermsAndAgreement = plan.TermsAndAgreement,
                PlanIsActive = plan.IsActive,
                EffectiveDate = utcNow.Date,
                IsCurrent = true,
                ChangeType = PlanVersionChangeTypes.PlanCreated,
                ChangeSummary = $"Plan '{plan.Code}' created (no modules included yet).",
                ChangedBy = ResolveChangedBy(changedBy),
                ChangedAt = utcNow,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            };
            _masterDb.SubscriptionPlanVersions.Add(version);

            await _masterDb.SaveChangesAsync(cancellationToken);

            return SubscriptionMutationResult.Ok();
        }

        public async Task<SubscriptionMutationResult> UpdatePlanAsync(
            int subscriptionPlanId, string name, string description, decimal price, string billingCycle, string termsAndAgreement,
            CancellationToken cancellationToken = default, string changedBy = "")
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return SubscriptionMutationResult.Fail("Plan name is required.");
            }

            if (!BillingCycles.All.Contains(billingCycle, StringComparer.Ordinal))
            {
                return SubscriptionMutationResult.Fail($"Billing cycle must be one of: {string.Join(", ", BillingCycles.All)}.");
            }

            if (price < 0)
            {
                return SubscriptionMutationResult.Fail("Price must not be negative.");
            }

            var plan = await _masterDb.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.SubscriptionPlanId == subscriptionPlanId, cancellationToken);
            if (plan == null)
            {
                return SubscriptionMutationResult.Fail($"Plan {subscriptionPlanId} does not exist.");
            }

            var normalizedName = name.Trim();
            var normalizedDescription = description?.Trim() ?? string.Empty;
            var normalizedTerms = termsAndAgreement ?? string.Empty;

            // Material-change rule: ONLY price, billing cycle and Terms & Agreement alter the
            // subscribed terms. Re-saving identical values (or editing the name/description)
            // therefore creates NO new version - it just updates the live plan row.
            var materialChange = plan.Price != price
                || !string.Equals(plan.BillingCycle, billingCycle, StringComparison.Ordinal)
                || !string.Equals(plan.TermsAndAgreement, normalizedTerms, StringComparison.Ordinal);

            // Capture the configuration BEFORE the change so that - when a plan has no version
            // yet - Version 1 can be created from the pre-change values (history is never
            // reconstructed from the post-change state).
            var before = materialChange
                ? await CapturePlanSnapshotAsync(plan, cancellationToken)
                : null;

            var utcNow = DateTime.UtcNow;
            var previousPrice = plan.Price;
            var previousBillingCycle = plan.BillingCycle;
            var previousTerms = plan.TermsAndAgreement;

            plan.Name = normalizedName;
            plan.Description = normalizedDescription;
            plan.Price = price;
            plan.BillingCycle = billingCycle;
            plan.TermsAndAgreement = normalizedTerms;
            plan.UpdatedAt = utcNow;

            if (materialChange)
            {
                var (changeType, summary) = DescribePlanConfigurationChange(
                    previousPrice, price, previousBillingCycle, billingCycle, previousTerms, normalizedTerms);

                await PublishPlanVersionAsync(plan, before, changeType, summary, changedBy, utcNow, cancellationToken);
            }

            await _masterDb.SaveChangesAsync(cancellationToken);

            return SubscriptionMutationResult.Ok();
        }

        public async Task<SubscriptionMutationResult> SetPlanActiveAsync(int subscriptionPlanId, bool isActive, CancellationToken cancellationToken = default)
        {
            var plan = await _masterDb.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.SubscriptionPlanId == subscriptionPlanId, cancellationToken);
            if (plan == null)
            {
                return SubscriptionMutationResult.Fail($"Plan {subscriptionPlanId} does not exist.");
            }

            // Deactivation never deletes anything: existing subscriptions keep the plan and
            // the plan simply cannot be newly selected (requests reject it at review time).
            plan.IsActive = isActive;
            plan.UpdatedAt = DateTime.UtcNow;
            await _masterDb.SaveChangesAsync(cancellationToken);

            return SubscriptionMutationResult.Ok();
        }

        public async Task<SubscriptionMutationResult> AddModuleToPlanAsync(int subscriptionPlanId, string moduleCode, CancellationToken cancellationToken = default, string changedBy = "")
        {
            var plan = await _masterDb.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.SubscriptionPlanId == subscriptionPlanId, cancellationToken);
            if (plan == null)
            {
                return SubscriptionMutationResult.Fail($"Plan {subscriptionPlanId} does not exist.");
            }

            var module = await _masterDb.SoftwareModules
                .FirstOrDefaultAsync(m => m.Code == moduleCode, cancellationToken);
            if (module == null)
            {
                return SubscriptionMutationResult.Fail($"Module '{moduleCode}' does not exist in the catalog.");
            }

            var mapping = await _masterDb.PlanModules
                .FirstOrDefaultAsync(pm => pm.SubscriptionPlanId == subscriptionPlanId && pm.ModuleId == module.ModuleId, cancellationToken);

            // Already active: no material change at all - nothing is written and NO version is
            // created (pressing "Add checked" twice must never invent a version).
            if (mapping != null && mapping.IsActive)
            {
                return SubscriptionMutationResult.Ok();
            }

            var before = await CapturePlanSnapshotAsync(plan, cancellationToken);
            var utcNow = DateTime.UtcNow;

            if (mapping != null)
            {
                mapping.IsActive = true;
                mapping.UpdatedAt = utcNow;
            }
            else
            {
                _masterDb.PlanModules.Add(new PlanModule
                {
                    SubscriptionPlanId = subscriptionPlanId,
                    ModuleId = module.ModuleId,
                    IsActive = true,
                    CreatedAt = utcNow,
                    UpdatedAt = utcNow
                });
            }

            // The mapping change is deliberately NOT saved on its own: the new version and the
            // mapping change commit in the SAME SaveChanges (one transaction), so a version can
            // never describe a module set that was not stored.
            var moduleIsSelectable = module.IsActive;
            var nextModuleCodes = moduleIsSelectable
                ? CanonicalModuleOrder(before.ModuleCodes.Append(module.Code))
                : before.ModuleCodes;

            await PublishPlanVersionAsync(
                plan, before, PlanVersionChangeTypes.ModuleAdded,
                $"Added module: {module.Code} ({module.Name}).",
                changedBy, utcNow, cancellationToken, nextModuleCodes);

            await _masterDb.SaveChangesAsync(cancellationToken);

            return SubscriptionMutationResult.Ok();
        }

        public async Task<SubscriptionMutationResult> RemoveModuleFromPlanAsync(int subscriptionPlanId, string moduleCode, CancellationToken cancellationToken = default, string changedBy = "")
        {
            var plan = await _masterDb.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.SubscriptionPlanId == subscriptionPlanId, cancellationToken);
            if (plan == null)
            {
                return SubscriptionMutationResult.Fail($"Plan {subscriptionPlanId} does not exist.");
            }

            var module = await _masterDb.SoftwareModules
                .FirstOrDefaultAsync(m => m.Code == moduleCode, cancellationToken);
            if (module == null)
            {
                return SubscriptionMutationResult.Fail($"Module '{moduleCode}' does not exist in the catalog.");
            }

            var mapping = await _masterDb.PlanModules
                .FirstOrDefaultAsync(pm => pm.SubscriptionPlanId == subscriptionPlanId && pm.ModuleId == module.ModuleId, cancellationToken);

            // Nothing changes when the module is not (or no longer) part of the plan: no write,
            // no version.
            if (mapping == null || !mapping.IsActive)
            {
                return SubscriptionMutationResult.Ok();
            }

            var before = await CapturePlanSnapshotAsync(plan, cancellationToken);
            var utcNow = DateTime.UtcNow;

            // The mapping row is kept (IsActive=false); nothing is deleted.
            mapping.IsActive = false;
            mapping.UpdatedAt = utcNow;

            var nextModuleCodes = CanonicalModuleOrder(before.ModuleCodes.Where(c => !string.Equals(c, module.Code, StringComparison.Ordinal)));

            await PublishPlanVersionAsync(
                plan, before, PlanVersionChangeTypes.ModuleRemoved,
                $"Removed module: {module.Code} ({module.Name}).",
                changedBy, utcNow, cancellationToken, nextModuleCodes);

            await _masterDb.SaveChangesAsync(cancellationToken);

            return SubscriptionMutationResult.Ok();
        }

        public async Task<SubscriptionMutationResult> SubmitChangeRequestAsync(
            int companyId, string requestedPlanCode, string requestedBy, string notes,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(requestedBy))
            {
                return SubscriptionMutationResult.Fail("An authenticated requesting identity is required.");
            }

            var company = await _masterDb.Companies
                .FirstOrDefaultAsync(c => c.CompanyId == companyId, cancellationToken);
            if (company == null)
            {
                return SubscriptionMutationResult.Fail($"Company {companyId} does not exist.");
            }

            var requestedPlan = await _masterDb.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Code == requestedPlanCode, cancellationToken);
            if (requestedPlan == null)
            {
                return SubscriptionMutationResult.Fail($"Requested plan '{requestedPlanCode}' does not exist.");
            }

            if (!requestedPlan.IsActive)
            {
                return SubscriptionMutationResult.Fail($"Requested plan '{requestedPlanCode}' is not active and cannot be newly selected.");
            }

            var pending = await _masterDb.SubscriptionChangeRequests
                .AnyAsync(r => r.CompanyId == companyId && r.Status == SubscriptionChangeRequestStatuses.Pending, cancellationToken);
            if (pending)
            {
                return SubscriptionMutationResult.Fail("This company already has a pending subscription request. Wait for its review or cancel it first.");
            }

            var current = await GetCurrentSubscriptionAsync(companyId, cancellationToken);

            string requestType;
            if (current == null)
            {
                requestType = SubscriptionRequestTypes.ChangePlan;
            }
            else
            {
                var currentCodes = await GetActiveModuleCodesAsync(current.SubscriptionPlanId, cancellationToken);
                var requestedCodes = await GetActiveModuleCodesAsync(requestedPlan.SubscriptionPlanId, cancellationToken);
                requestType = PlanChangeClassifier.Classify(currentCodes, requestedCodes);
            }

            var utcNow = DateTime.UtcNow;
            _masterDb.SubscriptionChangeRequests.Add(new SubscriptionChangeRequest
            {
                CompanyId = companyId,
                CurrentPlanId = current?.SubscriptionPlanId,
                RequestedPlanId = requestedPlan.SubscriptionPlanId,
                RequestType = requestType,
                Status = SubscriptionChangeRequestStatuses.Pending,
                RequestedBy = requestedBy.Trim(),
                RequestedAt = utcNow,
                ReviewNotes = notes?.Trim() ?? string.Empty,
                RequestedPrice = requestedPlan.Price,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            });

            // OPTION 2: ONLY a Pending request row is written. The active CompanySubscription,
            // its price/terms snapshot and the history table are untouched until an approval.
            await _masterDb.SaveChangesAsync(cancellationToken);

            return SubscriptionMutationResult.Ok();
        }

        public async Task<IReadOnlyList<SubscriptionChangeRequestInfo>> GetChangeRequestsAsync(string? status = null, CancellationToken cancellationToken = default)
        {
            var query = _masterDb.SubscriptionChangeRequests.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => r.Status == status);
            }

            var requests = await query
                .OrderByDescending(r => r.SubscriptionChangeRequestId)
                .ToListAsync(cancellationToken);

            var companies = await _masterDb.Companies.AsNoTracking().ToDictionaryAsync(c => c.CompanyId, cancellationToken);
            var plans = await _masterDb.SubscriptionPlans.AsNoTracking().ToDictionaryAsync(p => p.SubscriptionPlanId, cancellationToken);

            return requests.Select(r =>
            {
                companies.TryGetValue(r.CompanyId, out var company);
                var requestedPlan = r.RequestedPlanId.HasValue && plans.TryGetValue(r.RequestedPlanId.Value, out var p) ? p : null;
                var currentPlan = r.CurrentPlanId.HasValue && plans.TryGetValue(r.CurrentPlanId.Value, out var cp) ? cp : null;

                return new SubscriptionChangeRequestInfo
                {
                    SubscriptionChangeRequestId = r.SubscriptionChangeRequestId,
                    CompanyId = r.CompanyId,
                    CompanyCode = company?.CompanyCode ?? string.Empty,
                    CompanyName = company?.CompanyName ?? string.Empty,
                    CurrentPlanCode = currentPlan?.Code ?? string.Empty,
                    RequestedPlanCode = requestedPlan?.Code ?? string.Empty,
                    RequestType = r.RequestType,
                    Status = r.Status,
                    RequestedBy = r.RequestedBy,
                    RequestedAt = r.RequestedAt,
                    ReviewedBy = r.ReviewedBy,
                    ReviewedAt = r.ReviewedAt,
                    ReviewNotes = r.ReviewNotes,
                    RequestedPrice = r.RequestedPrice
                };
            }).ToList();
        }

        public async Task<SubscriptionMutationResult> CancelChangeRequestAsync(int requestId, string requestedBy, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(requestedBy))
            {
                return SubscriptionMutationResult.Fail("An authenticated requesting identity is required.");
            }

            var request = await _masterDb.SubscriptionChangeRequests
                .FirstOrDefaultAsync(r => r.SubscriptionChangeRequestId == requestId, cancellationToken);
            if (request == null)
            {
                return SubscriptionMutationResult.Fail($"Request {requestId} does not exist.");
            }

            if (!string.Equals(request.Status, SubscriptionChangeRequestStatuses.Pending, StringComparison.Ordinal))
            {
                return SubscriptionMutationResult.Fail("Only a pending request can be cancelled.");
            }

            if (!string.Equals(request.RequestedBy, requestedBy.Trim(), StringComparison.Ordinal))
            {
                return SubscriptionMutationResult.Fail("Only the identity that submitted the request can cancel it.");
            }

            request.Status = SubscriptionChangeRequestStatuses.Cancelled;
            request.ReviewedBy = requestedBy.Trim();
            request.ReviewedAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;
            await _masterDb.SaveChangesAsync(cancellationToken);

            return SubscriptionMutationResult.Ok();
        }

        public async Task<SubscriptionMutationResult> ReviewChangeRequestAsync(
            int requestId, bool approve, string reviewedBy, string reviewNotes,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(reviewedBy))
            {
                return SubscriptionMutationResult.Fail("An authenticated reviewing identity is required.");
            }

            var request = await _masterDb.SubscriptionChangeRequests
                .FirstOrDefaultAsync(r => r.SubscriptionChangeRequestId == requestId, cancellationToken);
            if (request == null)
            {
                return SubscriptionMutationResult.Fail($"Request {requestId} does not exist.");
            }

            if (!string.Equals(request.Status, SubscriptionChangeRequestStatuses.Pending, StringComparison.Ordinal))
            {
                return SubscriptionMutationResult.Fail($"Request {requestId} has already been reviewed ({request.Status}).");
            }

            // OPTION 2 guard: the identity that submitted a request can never review it
            // (defense in depth - the review UI is master-only as well).
            if (string.Equals(request.RequestedBy, reviewedBy.Trim(), StringComparison.Ordinal))
            {
                return SubscriptionMutationResult.Fail("The identity that submitted a subscription request cannot review it.");
            }

            var company = await _masterDb.Companies
                .FirstOrDefaultAsync(c => c.CompanyId == request.CompanyId, cancellationToken);
            if (company == null)
            {
                return SubscriptionMutationResult.Fail($"Company {request.CompanyId} does not exist.");
            }

            var requestedPlanId = request.RequestedPlanId;
            if (!requestedPlanId.HasValue)
            {
                return SubscriptionMutationResult.Fail($"Request {requestId} has no requested plan.");
            }

            var plan = await _masterDb.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.SubscriptionPlanId == requestedPlanId.Value, cancellationToken);
            if (plan == null)
            {
                return SubscriptionMutationResult.Fail("The requested plan does not exist.");
            }

            if (!approve)
            {
                // Reject: reviewer + notes are recorded; the active subscription and the
                // history table are deliberately NOT touched (no fake plan-change entry).
                request.Status = SubscriptionChangeRequestStatuses.Rejected;
                request.ReviewedBy = reviewedBy.Trim();
                request.ReviewedAt = DateTime.UtcNow;
                request.ReviewNotes = reviewNotes?.Trim() ?? string.Empty;
                request.UpdatedAt = DateTime.UtcNow;
                await _masterDb.SaveChangesAsync(cancellationToken);
                return SubscriptionMutationResult.Ok();
            }

            if (!plan.IsActive)
            {
                return SubscriptionMutationResult.Fail($"The requested plan '{plan.Code}' is no longer active and cannot be approved.");
            }

            // Approve: everything happens inside one transaction so the subscription update,
            // the request status change and the history row either all commit or none does.
            var utcNow = DateTime.UtcNow;
            var reviewer = reviewedBy.Trim();

            return await _masterDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await _masterDb.Database.BeginTransactionAsync(cancellationToken);

                // Re-check inside the transaction so a concurrent review cannot double-apply.
                var tracked = await _masterDb.SubscriptionChangeRequests
                    .FirstAsync(r => r.SubscriptionChangeRequestId == requestId, cancellationToken);
                if (!string.Equals(tracked.Status, SubscriptionChangeRequestStatuses.Pending, StringComparison.Ordinal))
                {
                    return SubscriptionMutationResult.Fail($"Request {requestId} has already been reviewed ({tracked.Status}).");
                }

                var current = await GetCurrentSubscriptionAsync(request.CompanyId, cancellationToken);
                var previousPlanId = current?.SubscriptionPlanId;
                var previousPrice = current?.Price;

                // The plan CONFIGURATION VERSION being approved (Terms & Agreement + module set).
                // Additive association: the subscription keeps its own price/terms snapshots.
                var approvedPlanVersionId = await GetCurrentPlanVersionIdAsync(plan.SubscriptionPlanId, cancellationToken);

                if (current == null)
                {
                    _masterDb.CompanySubscriptions.Add(new CompanySubscription
                    {
                        CompanyId = request.CompanyId,
                        SubscriptionPlanId = plan.SubscriptionPlanId,
                        SubscriptionPlanVersionId = approvedPlanVersionId,
                        StartDate = utcNow.Date,
                        EndDate = utcNow.AddYears(1).Date,
                        RenewalDate = utcNow.AddYears(1).Date,
                        Status = SubscriptionStatuses.Active,
                        Price = plan.Price,
                        PaymentStatus = PaymentStatuses.Pending,
                        DateSubscribed = utcNow,
                        TermsAndAgreement = plan.TermsAndAgreement,
                        Notes = $"Approved subscription request #{requestId} ({plan.Code}).",
                        CreatedAt = utcNow,
                        UpdatedAt = utcNow
                    });
                }
                else
                {
                    current.SubscriptionPlanId = plan.SubscriptionPlanId;
                    current.SubscriptionPlanVersionId = approvedPlanVersionId;
                    // SNAPSHOT semantics: the plan's CURRENT price and terms are copied now;
                    // later plan edits never rewrite this subscription.
                    current.Price = plan.Price;
                    current.TermsAndAgreement = plan.TermsAndAgreement;
                    current.StartDate = utcNow.Date;
                    current.EndDate = utcNow.AddYears(1).Date;
                    current.RenewalDate = utcNow.AddYears(1).Date;
                    current.Status = SubscriptionStatuses.Active;
                    current.UpdatedAt = utcNow;
                    // DateSubscribed is preserved (when the company first subscribed).
                }

                tracked.Status = SubscriptionChangeRequestStatuses.Approved;
                tracked.ReviewedBy = reviewer;
                tracked.ReviewedAt = utcNow;
                tracked.ReviewNotes = reviewNotes?.Trim() ?? string.Empty;
                tracked.UpdatedAt = utcNow;

                var action = previousPlanId == null
                    ? SubscriptionHistoryActions.InitialSubscription
                    : previousPlanId.Value == plan.SubscriptionPlanId
                        ? SubscriptionHistoryActions.Renewal
                        : await DeterminePlanChangeActionAsync(previousPlanId.Value, plan.SubscriptionPlanId, cancellationToken);

                _masterDb.SubscriptionHistories.Add(new SubscriptionHistory
                {
                    CompanyId = request.CompanyId,
                    PreviousPlanId = previousPlanId,
                    NewPlanId = plan.SubscriptionPlanId,
                    Action = action,
                    PreviousPrice = previousPrice,
                    NewPrice = plan.Price,
                    ChangedBy = reviewer,
                    ChangedAt = utcNow,
                    Reason = $"Approved subscription request #{requestId} ({request.RequestType})",
                    Notes = reviewNotes?.Trim() ?? string.Empty
                });

                await _masterDb.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return SubscriptionMutationResult.Ok();
            });
        }

        public async Task<IReadOnlyList<SubscriptionHistoryInfo>> GetHistoryAsync(int? companyId = null, CancellationToken cancellationToken = default)
        {
            var query = _masterDb.SubscriptionHistories.AsNoTracking();
            if (companyId.HasValue)
            {
                query = query.Where(h => h.CompanyId == companyId.Value);
            }

            var rows = await query
                .OrderByDescending(h => h.HistoryId)
                .ToListAsync(cancellationToken);

            var companies = await _masterDb.Companies.AsNoTracking().ToDictionaryAsync(c => c.CompanyId, cancellationToken);
            var plans = await _masterDb.SubscriptionPlans.AsNoTracking().ToDictionaryAsync(p => p.SubscriptionPlanId, cancellationToken);

            return rows.Select(h =>
            {
                companies.TryGetValue(h.CompanyId, out var company);
                var previousPlan = h.PreviousPlanId.HasValue && plans.TryGetValue(h.PreviousPlanId.Value, out var pp) ? pp : null;
                var newPlan = h.NewPlanId.HasValue && plans.TryGetValue(h.NewPlanId.Value, out var np) ? np : null;

                return new SubscriptionHistoryInfo
                {
                    HistoryId = h.HistoryId,
                    CompanyId = h.CompanyId,
                    CompanyCode = company?.CompanyCode ?? string.Empty,
                    CompanyName = company?.CompanyName ?? string.Empty,
                    Action = h.Action,
                    PreviousPlanCode = previousPlan?.Code ?? string.Empty,
                    NewPlanCode = newPlan?.Code ?? string.Empty,
                    PreviousPrice = h.PreviousPrice,
                    NewPrice = h.NewPrice,
                    ChangedBy = h.ChangedBy,
                    ChangedAt = h.ChangedAt,
                    Reason = h.Reason,
                    Notes = h.Notes
                };
            }).ToList();
        }

        // -------------------------------------------------------------------------------------
        // Plan-configuration versioning (immutable snapshots)
        // -------------------------------------------------------------------------------------

        public async Task<IReadOnlyList<SubscriptionPlanVersionInfo>> GetPlanVersionsAsync(
            int? subscriptionPlanId = null, CancellationToken cancellationToken = default)
        {
            var query = _masterDb.SubscriptionPlanVersions.AsNoTracking();
            if (subscriptionPlanId.HasValue)
            {
                var planId = subscriptionPlanId.Value;
                query = query.Where(v => v.SubscriptionPlanId == planId);
            }

            var versions = await query
                .OrderBy(v => v.SubscriptionPlanId)
                .ThenByDescending(v => v.VersionNumber)
                .ToListAsync(cancellationToken);

            var ids = versions.Select(v => v.SubscriptionPlanVersionId).ToList();
            var moduleRows = ids.Count == 0
                ? new List<SubscriptionPlanVersionModule>()
                : await _masterDb.SubscriptionPlanVersionModules.AsNoTracking()
                    .Where(m => ids.Contains(m.SubscriptionPlanVersionId))
                    .ToListAsync(cancellationToken);

            return versions.Select(v => BuildVersionInfo(v, moduleRows)).ToList();
        }

        public async Task<SubscriptionPlanVersionDetails?> GetPlanVersionDetailsAsync(
            int subscriptionPlanVersionId, CancellationToken cancellationToken = default)
        {
            var version = await _masterDb.SubscriptionPlanVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.SubscriptionPlanVersionId == subscriptionPlanVersionId, cancellationToken);
            if (version == null)
            {
                return null;
            }

            var versionIds = new List<int> { version.SubscriptionPlanVersionId };
            var previous = await _masterDb.SubscriptionPlanVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.SubscriptionPlanId == version.SubscriptionPlanId
                    && v.VersionNumber == version.VersionNumber - 1, cancellationToken);
            if (previous != null)
            {
                versionIds.Add(previous.SubscriptionPlanVersionId);
            }

            var moduleRows = await _masterDb.SubscriptionPlanVersionModules.AsNoTracking()
                .Where(m => versionIds.Contains(m.SubscriptionPlanVersionId))
                .ToListAsync(cancellationToken);

            var currentInfo = BuildVersionInfo(version, moduleRows);
            var previousInfo = previous == null ? null : BuildVersionInfo(previous, moduleRows);

            // The diff is computed between the two IMMUTABLE module snapshots - the live
            // PlanModules mapping is never consulted, so old versions keep showing exactly what
            // they contained even after modules are removed from the plan later.
            var previousCodes = previousInfo?.ModuleCodes ?? (IReadOnlyList<string>)Array.Empty<string>();
            var currentCodes = currentInfo.ModuleCodes;

            var addedCodes = currentCodes.Where(c => !previousCodes.Contains(c, StringComparer.Ordinal)).ToList();
            var removedCodes = previousCodes.Where(c => !currentCodes.Contains(c, StringComparer.Ordinal)).ToList();
            var namesByCode = moduleRows
                .GroupBy(m => m.ModuleCode, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().ModuleName, StringComparer.Ordinal);

            return new SubscriptionPlanVersionDetails
            {
                Version = currentInfo,
                PreviousVersion = previousInfo,
                AddedModuleCodes = addedCodes,
                RemovedModuleCodes = removedCodes,
                AddedModuleNames = addedCodes.Select(c => DescribeVersionModule(c, namesByCode)).ToList(),
                RemovedModuleNames = removedCodes.Select(c => DescribeVersionModule(c, namesByCode)).ToList()
            };
        }

        public async Task<PlanVersionInitializationResult> EnsureInitialPlanVersionsAsync(
            string changedBy = "", CancellationToken cancellationToken = default)
        {
            var detail = new List<string>();
            var utcNow = DateTime.UtcNow;
            var actor = ResolveChangedBy(changedBy);

            var plans = await _masterDb.SubscriptionPlans.OrderBy(p => p.SubscriptionPlanId).ToListAsync(cancellationToken);
            var modulesByCode = await _masterDb.SoftwareModules.ToDictionaryAsync(m => m.Code, StringComparer.Ordinal, cancellationToken);
            var created = 0;
            var already = 0;

            foreach (var plan in plans)
            {
                var versions = await _masterDb.SubscriptionPlanVersions
                    .Where(v => v.SubscriptionPlanId == plan.SubscriptionPlanId)
                    .OrderByDescending(v => v.VersionNumber)
                    .ToListAsync(cancellationToken);

                if (versions.Count > 0)
                {
                    already++;

                    // Repair pass: a plan must never have zero or two current versions.
                    var currentRows = versions.Where(v => v.IsCurrent).ToList();
                    var expectedCurrent = currentRows.Count == 0 ? versions[0] : currentRows.OrderByDescending(v => v.VersionNumber).First();
                    var repaired = false;
                    foreach (var row in versions)
                    {
                        var shouldBeCurrent = ReferenceEquals(row, expectedCurrent);
                        if (row.IsCurrent != shouldBeCurrent)
                        {
                            row.IsCurrent = shouldBeCurrent;
                            row.UpdatedAt = utcNow;
                            repaired = true;
                        }
                    }

                    detail.Add($"{plan.Code}: {versions.Count} version(s) already present " +
                        $"(current=V{expectedCurrent.VersionNumber}{(repaired ? ", current flag repaired" : string.Empty)}).");
                    continue;
                }

                // Version 1 = the plan's configuration AT THE MOMENT versioning is introduced.
                // The ACTIVE plan is not modified in any way.
                var codes = await GetActiveModuleCodesAsync(plan.SubscriptionPlanId, cancellationToken);
                var version = new SubscriptionPlanVersion
                {
                    SubscriptionPlanId = plan.SubscriptionPlanId,
                    VersionNumber = 1,
                    PlanCode = plan.Code,
                    PlanName = plan.Name,
                    Description = plan.Description,
                    Price = plan.Price,
                    BillingCycle = plan.BillingCycle,
                    TermsAndAgreement = plan.TermsAndAgreement,
                    PlanIsActive = plan.IsActive,
                    EffectiveDate = utcNow.Date,
                    IsCurrent = true,
                    ChangeType = PlanVersionChangeTypes.InitialVersion,
                    ChangeSummary = "Original plan configuration at the moment versioning was introduced.",
                    ChangedBy = actor,
                    ChangedAt = utcNow,
                    CreatedAt = utcNow,
                    UpdatedAt = utcNow
                };

                AddVersionModules(version, codes, modulesByCode);
                _masterDb.SubscriptionPlanVersions.Add(version);
                created++;
                detail.Add($"{plan.Code}: Version 1 created ({codes.Count} modules, price {plan.Price:0.00} / {plan.BillingCycle}).");
            }

            if (created > 0 || _masterDb.ChangeTracker.HasChanges())
            {
                await _masterDb.SaveChangesAsync(cancellationToken);
            }

            // Link existing (demo) subscriptions to the current version of their plan. Purely
            // additive: an existing pointer is never rewritten, and no price/terms/date snapshot
            // of any subscription is touched.
            var currentByPlan = (await _masterDb.SubscriptionPlanVersions
                    .Where(v => v.IsCurrent)
                    .ToListAsync(cancellationToken))
                .GroupBy(v => v.SubscriptionPlanId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.VersionNumber).First().SubscriptionPlanVersionId);

            var unlinked = await _masterDb.CompanySubscriptions
                .Where(s => s.SubscriptionPlanVersionId == null)
                .ToListAsync(cancellationToken);
            var linked = 0;
            foreach (var subscription in unlinked)
            {
                if (!currentByPlan.TryGetValue(subscription.SubscriptionPlanId, out var versionId))
                {
                    continue;
                }

                subscription.SubscriptionPlanVersionId = versionId;
                linked++;
            }

            if (linked > 0)
            {
                await _masterDb.SaveChangesAsync(cancellationToken);
            }

            detail.Add($"subscriptions linked to their plan's current version: {linked} of {unlinked.Count} unlinked row(s).");

            return new PlanVersionInitializationResult
            {
                Success = true,
                PlansScanned = plans.Count,
                VersionsCreated = created,
                PlansAlreadyVersioned = already,
                SubscriptionsLinked = linked,
                Detail = detail
            };
        }

        // -------------------------------------------------------------------------------------
        // Internals
        // -------------------------------------------------------------------------------------

        /// <summary>Rejects are never attributed to nobody: an empty actor becomes the system identity.</summary>
        private const string SystemActorName = "system";

        private static string ResolveChangedBy(string? changedBy) =>
            string.IsNullOrWhiteSpace(changedBy) ? SystemActorName : changedBy!.Trim();

        /// <summary>Keeps a module set in the canonical plan order (a subset of the approved sequence).</summary>
        private static IReadOnlyList<string> CanonicalModuleOrder(IEnumerable<string> codes)
        {
            var set = codes.ToHashSet(StringComparer.Ordinal);
            return SoftwareModuleCodes.All.Where(set.Contains).ToList();
        }

        private static string DescribeVersionModule(string code, IReadOnlyDictionary<string, string> namesByCode) =>
            namesByCode.TryGetValue(code, out var name) ? $"{code} ({name})" : code;

        private static SubscriptionPlanVersionInfo BuildVersionInfo(
            SubscriptionPlanVersion version, IReadOnlyList<SubscriptionPlanVersionModule> moduleRows)
        {
            var rows = moduleRows.Where(m => m.SubscriptionPlanVersionId == version.SubscriptionPlanVersionId).ToList();
            var codes = CanonicalModuleOrder(rows.Select(m => m.ModuleCode));
            var namesByCode = rows
                .GroupBy(m => m.ModuleCode, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().ModuleName, StringComparer.Ordinal);

            return new SubscriptionPlanVersionInfo
            {
                SubscriptionPlanVersionId = version.SubscriptionPlanVersionId,
                SubscriptionPlanId = version.SubscriptionPlanId,
                PlanCode = version.PlanCode,
                PlanName = version.PlanName,
                VersionNumber = version.VersionNumber,
                ChangeType = version.ChangeType,
                ChangeTypeLabel = PlanVersionChangeTypes.Describe(version.ChangeType),
                ChangeSummary = version.ChangeSummary,
                Price = version.Price,
                BillingCycle = version.BillingCycle,
                TermsAndAgreement = version.TermsAndAgreement,
                PlanIsActive = version.PlanIsActive,
                EffectiveDate = version.EffectiveDate,
                IsCurrent = version.IsCurrent,
                ChangedBy = version.ChangedBy,
                ChangedAt = version.ChangedAt,
                ModuleCodes = codes,
                ModuleNames = codes.Select(c => namesByCode.TryGetValue(c, out var name) ? name : c).ToList()
            };
        }

        /// <summary>The pre/post difference of the three material plan fields, as a change type + summary.</summary>
        private static (string ChangeType, string Summary) DescribePlanConfigurationChange(
            decimal previousPrice, decimal newPrice,
            string previousBillingCycle, string newBillingCycle,
            string previousTerms, string newTerms)
        {
            var parts = new List<string>();
            var kinds = 0;
            var single = PlanVersionChangeTypes.ConfigurationChanged;

            if (previousPrice != newPrice)
            {
                parts.Add($"Price {previousPrice:0.00} -> {newPrice:0.00}");
                kinds++;
                single = PlanVersionChangeTypes.PriceChanged;
            }

            if (!string.Equals(previousBillingCycle, newBillingCycle, StringComparison.Ordinal))
            {
                parts.Add($"Billing cycle {previousBillingCycle} -> {newBillingCycle}");
                kinds++;
                single = PlanVersionChangeTypes.BillingCycleChanged;
            }

            if (!string.Equals(previousTerms, newTerms, StringComparison.Ordinal))
            {
                parts.Add($"Terms & Agreement updated ({previousTerms.Length} -> {newTerms.Length} chars)");
                kinds++;
                single = PlanVersionChangeTypes.TermsChanged;
            }

            return (kinds == 1 ? single : PlanVersionChangeTypes.ConfigurationChanged, string.Join("; ", parts));
        }

        /// <summary>Immutable state of a plan captured BEFORE a material change is applied.</summary>
        private sealed record PlanConfigurationSnapshot(
            string Code, string Name, string Description, decimal Price, string BillingCycle,
            string TermsAndAgreement, bool IsActive, IReadOnlyList<string> ModuleCodes);

        private async Task<PlanConfigurationSnapshot> CapturePlanSnapshotAsync(
            SubscriptionPlan plan, CancellationToken cancellationToken)
        {
            var codes = await GetActiveModuleCodesAsync(plan.SubscriptionPlanId, cancellationToken);
            return new PlanConfigurationSnapshot(
                plan.Code, plan.Name, plan.Description, plan.Price, plan.BillingCycle,
                plan.TermsAndAgreement, plan.IsActive, codes);
        }

        /// <summary>Adds the immutable module snapshot rows of a version (code + name snapshotted).</summary>
        private static void AddVersionModules(
            SubscriptionPlanVersion version,
            IReadOnlyList<string> moduleCodes, IReadOnlyDictionary<string, SoftwareModule> modulesByCode)
        {
            foreach (var code in moduleCodes)
            {
                if (!modulesByCode.TryGetValue(code, out var module))
                {
                    continue;
                }

                version.Modules.Add(new SubscriptionPlanVersionModule
                {
                    ModuleId = module.ModuleId,
                    ModuleCode = module.Code,
                    ModuleName = module.Name,
                    CreatedAt = version.CreatedAt
                });
            }
        }

        /// <summary>
        /// Creates the NEXT immutable version of a plan. The module set of the version is passed
        /// in explicitly (the post-change set), so the version row can never disagree with the
        /// mapping change that commits in the SAME SaveChanges. When the plan has no version yet
        /// (versioning introduced after the plan was configured), Version 1 is created first from
        /// the PRE-change snapshot, so the original configuration is preserved verbatim.
        /// </summary>
        private async Task PublishPlanVersionAsync(
            SubscriptionPlan plan, PlanConfigurationSnapshot? before,
            string changeType, string changeSummary, string changedBy, DateTime utcNow,
            CancellationToken cancellationToken, IReadOnlyList<string>? nextModuleCodes = null)
        {
            var modulesByCode = await _masterDb.SoftwareModules.ToDictionaryAsync(m => m.Code, StringComparer.Ordinal, cancellationToken);

            var existing = await _masterDb.SubscriptionPlanVersions
                .Where(v => v.SubscriptionPlanId == plan.SubscriptionPlanId)
                .OrderByDescending(v => v.VersionNumber)
                .ToListAsync(cancellationToken);

            var current = existing.FirstOrDefault(v => v.IsCurrent) ?? existing.FirstOrDefault();

            if (current == null && before != null)
            {
                var baseline = new SubscriptionPlanVersion
                {
                    SubscriptionPlanId = plan.SubscriptionPlanId,
                    VersionNumber = 1,
                    PlanCode = before.Code,
                    PlanName = before.Name,
                    Description = before.Description,
                    Price = before.Price,
                    BillingCycle = before.BillingCycle,
                    TermsAndAgreement = before.TermsAndAgreement,
                    PlanIsActive = before.IsActive,
                    EffectiveDate = utcNow.Date,
                    IsCurrent = false,
                    ChangeType = PlanVersionChangeTypes.InitialVersion,
                    ChangeSummary = $"Original plan configuration (captured before: {changeSummary})",
                    ChangedBy = ResolveChangedBy(changedBy),
                    ChangedAt = utcNow,
                    CreatedAt = utcNow,
                    UpdatedAt = utcNow
                };

                AddVersionModules(baseline, before.ModuleCodes, modulesByCode);
                _masterDb.SubscriptionPlanVersions.Add(baseline);
                existing.Insert(0, baseline);
                current = baseline;
            }

            // Exactly one current version per plan: the previous one is flagged out in the same
            // SaveChanges as the new version is inserted (atomic, never two ambiguous current rows).
            if (current != null)
            {
                current.IsCurrent = false;
                current.UpdatedAt = utcNow;
            }

            var versionNumber = (existing.Count == 0 ? 0 : existing.Max(v => v.VersionNumber)) + 1;
            var codes = nextModuleCodes ?? await GetActiveModuleCodesAsync(plan.SubscriptionPlanId, cancellationToken);

            var version = new SubscriptionPlanVersion
            {
                SubscriptionPlanId = plan.SubscriptionPlanId,
                VersionNumber = versionNumber,
                PlanCode = plan.Code,
                PlanName = plan.Name,
                Description = plan.Description,
                Price = plan.Price,
                BillingCycle = plan.BillingCycle,
                TermsAndAgreement = plan.TermsAndAgreement,
                PlanIsActive = plan.IsActive,
                EffectiveDate = utcNow.Date,
                IsCurrent = true,
                ChangeType = changeType,
                ChangeSummary = changeSummary,
                ChangedBy = ResolveChangedBy(changedBy),
                ChangedAt = utcNow,
                CreatedAt = utcNow,
                UpdatedAt = utcNow
            };

            AddVersionModules(version, codes, modulesByCode);
            _masterDb.SubscriptionPlanVersions.Add(version);
        }

        /// <summary>The active module codes of one plan (mapping and module both active).</summary>
        private async Task<IReadOnlyList<string>> GetActiveModuleCodesAsync(int subscriptionPlanId, CancellationToken cancellationToken)
        {
            var codes = await _masterDb.PlanModules
                .AsNoTracking()
                .Where(pm => pm.SubscriptionPlanId == subscriptionPlanId && pm.IsActive && pm.Module.IsActive)
                .Join(_masterDb.SoftwareModules.Where(m => m.IsActive),
                    pm => pm.ModuleId,
                    m => m.ModuleId,
                    (pm, m) => m.Code)
                .ToListAsync(cancellationToken);

            return SoftwareModuleCodes.All.Where(c => codes.Contains(c, StringComparer.Ordinal)).ToList();
        }

        /// <summary>Classifies an approved plan change as Upgrade or Downgrade by module sets.</summary>
        private async Task<string> DeterminePlanChangeActionAsync(int previousPlanId, int newPlanId, CancellationToken cancellationToken)
        {
            var previous = (await GetActiveModuleCodesAsync(previousPlanId, cancellationToken)).ToHashSet(StringComparer.Ordinal);
            var next = (await GetActiveModuleCodesAsync(newPlanId, cancellationToken)).ToHashSet(StringComparer.Ordinal);

            if (next.IsSupersetOf(previous) && next.Count > previous.Count)
            {
                return SubscriptionHistoryActions.Upgrade;
            }

            if (previous.IsSupersetOf(next) && previous.Count > next.Count)
            {
                return SubscriptionHistoryActions.Downgrade;
            }

            // Same size / overlapping sets: classify by price direction as a fallback.
            var previousPlan = await _masterDb.SubscriptionPlans.AsNoTracking()
                .FirstAsync(p => p.SubscriptionPlanId == previousPlanId, cancellationToken);
            var newPlan = await _masterDb.SubscriptionPlans.AsNoTracking()
                .FirstAsync(p => p.SubscriptionPlanId == newPlanId, cancellationToken);
            return newPlan.Price > previousPlan.Price
                ? SubscriptionHistoryActions.Upgrade
                : SubscriptionHistoryActions.Downgrade;
        }

        private async Task<CompanySubscription?> GetCurrentSubscriptionAsync(int companyId, CancellationToken cancellationToken)
        {
            var subscriptions = await _masterDb.CompanySubscriptions
                .Where(s => s.CompanyId == companyId)
                .OrderByDescending(s => s.Status == SubscriptionStatuses.Active)
                .ThenByDescending(s => s.CompanySubscriptionId)
                .ToListAsync(cancellationToken);

            return subscriptions.FirstOrDefault();
        }

        private async Task<CompanySubscriptionInfo?> BuildInfoAsync(
            int companyId, string companyCode, string companyName, CancellationToken cancellationToken)
        {
            var subscription = await GetCurrentSubscriptionAsync(companyId, cancellationToken);
            if (subscription == null)
            {
                return null;
            }

            var plan = await _masterDb.SubscriptionPlans
                .AsNoTracking()
                .FirstAsync(p => p.SubscriptionPlanId == subscription.SubscriptionPlanId, cancellationToken);

            var enabledCodes = await _masterDb.PlanModules
                .AsNoTracking()
                .Where(pm => pm.SubscriptionPlanId == plan.SubscriptionPlanId && pm.IsActive && pm.Module.IsActive)
                .Join(_masterDb.SoftwareModules.Where(m => m.IsActive),
                    pm => pm.ModuleId,
                    m => m.ModuleId,
                    (pm, m) => m.Code)
                .ToListAsync(cancellationToken);

            // Keep the canonical order (plan order is a subset of the approved sequence).
            var ordered = SoftwareModuleCodes.All.Where(c => enabledCodes.Contains(c, StringComparer.Ordinal)).ToList();

            // Which plan configuration VERSION this subscription received (informational link).
            int? planVersionNumber = null;
            DateTime? planVersionEffectiveDate = null;
            if (subscription.SubscriptionPlanVersionId.HasValue)
            {
                var version = await _masterDb.SubscriptionPlanVersions.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.SubscriptionPlanVersionId == subscription.SubscriptionPlanVersionId.Value, cancellationToken);
                planVersionNumber = version?.VersionNumber;
                planVersionEffectiveDate = version?.EffectiveDate;
            }

            var effectiveStatus = SubscriptionStatusCalculator.Calculate(subscription.Status, subscription.EndDate);

            // Status rule: an Expired or Suspended subscription grants NO modules. Nothing is
            // deleted - the plan content stays in the master DB and the modules come back
            // automatically as soon as the subscription is Active again (renewal/reactivation).
            var granted = effectiveStatus == SubscriptionStatuses.Active
                ? ordered
                : (IReadOnlyList<string>)Array.Empty<string>();

            return new CompanySubscriptionInfo
            {
                CompanySubscriptionId = subscription.CompanySubscriptionId,
                CompanyId = companyId,
                CompanyCode = companyCode,
                CompanyName = companyName,
                SubscriptionPlanId = plan.SubscriptionPlanId,
                PlanCode = plan.Code,
                PlanName = plan.Name,
                StartDate = subscription.StartDate,
                EndDate = subscription.EndDate,
                Status = subscription.Status,
                Notes = subscription.Notes,
                EffectiveStatus = effectiveStatus,
                RenewalDate = subscription.RenewalDate,
                Price = subscription.Price,
                BillingCycle = plan.BillingCycle,
                PaymentStatus = subscription.PaymentStatus,
                TermsAndAgreement = subscription.TermsAndAgreement,
                DateSubscribed = subscription.DateSubscribed,
                PlanVersionNumber = planVersionNumber,
                PlanVersionEffectiveDate = planVersionEffectiveDate,
                EnabledModuleCodes = granted
            };
        }

        /// <summary>The CURRENT plan-configuration version id of a plan (null when it has none).</summary>
        private async Task<int?> GetCurrentPlanVersionIdAsync(int subscriptionPlanId, CancellationToken cancellationToken)
        {
            var version = await _masterDb.SubscriptionPlanVersions.AsNoTracking()
                .Where(v => v.SubscriptionPlanId == subscriptionPlanId)
                .OrderByDescending(v => v.IsCurrent)
                .ThenByDescending(v => v.VersionNumber)
                .Select(v => new { v.SubscriptionPlanVersionId })
                .FirstOrDefaultAsync(cancellationToken);

            return version?.SubscriptionPlanVersionId;
        }

        // -------------------------------------------------------------------------------------
        // Plan content (approved Phase 9A design). Used by the idempotent seeder only.
        // -------------------------------------------------------------------------------------

        /// <summary>BASIC = the first 6 modules; PROFESSIONAL = the first 12; ENTERPRISE = all 16.</summary>
        public static IReadOnlyDictionary<string, string[]> GetPlanModuleMatrix()
        {
            var m = SoftwareModuleCodes.All;
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                // Phase 2: the Basic plan now includes Feedback and Complaints (positions 7-8
                // of the approved order) so Tenant A's staff can use those modules; the rest of
                // the plan structure (Professional = first 12, Enterprise = all 16) is unchanged.
                [SoftwarePlanCodes.Basic] = m.Take(8).ToArray(),
                [SoftwarePlanCodes.Professional] = m.Take(12).ToArray(),
                [SoftwarePlanCodes.Enterprise] = m
            };
        }

    }
}
