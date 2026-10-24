using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace AlphaElite.infrastructure.services
{
    /// <summary>
    /// Per-operation scoping for the MASTER software-subscription service.
    ///
    /// ROOT CAUSE this type removes: the desktop application composed exactly ONE
    /// <see cref="MasterAlphaEliteDbContext"/> and shared it between the Super Admin window and
    /// every master service. EF Core forbids overlapping operations on one context instance, so
    /// any two master operations that overlapped in time (rapid sub-section switching, a refresh
    /// racing an approve, a plan selection while a load was running) threw
    ///
    ///     System.InvalidOperationException:
    ///     A second operation was started on this context instance before a previous operation completed.
    ///
    /// The earlier fix serialized USER ACTIONS through a gate, which is good for UI state but
    /// cannot make the data layer safe: the moment any path is not (or cannot be) gated the same
    /// exception returns. A DbContext is a unit of work, so the robust fix is one context PER
    /// OPERATION - each call below creates its own context from
    /// <see cref="IDbContextFactory{TContext}"/> and disposes it when the call completes.
    ///
    /// Overlapping calls are then legal BY CONSTRUCTION; nothing here swallows, retries or
    /// catches anything, and there is no blocking (.Result / .Wait) anywhere.
    ///
    /// Only the MASTER subscription service is scoped this way. Tenant database access keeps
    /// using its existing per-tenant factories and is not touched.
    /// </summary>
    public sealed class ScopedSubscriptionService : ISubscriptionService
    {
        private readonly IDbContextFactory<MasterAlphaEliteDbContext> _factory;

        public ScopedSubscriptionService(IDbContextFactory<MasterAlphaEliteDbContext> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        private int _createdContextCount;

        /// <summary>How many contexts this instance has created (diagnostics only).</summary>
        public int CreatedContextCount => System.Threading.Volatile.Read(ref _createdContextCount);

        private async Task<T> ScopeAsync<T>(Func<SubscriptionService, Task<T>> operation)
        {
            await using var context = _factory.CreateDbContext();
            System.Threading.Interlocked.Increment(ref _createdContextCount);
            return await operation(new SubscriptionService(context));
        }

        // ---- reads -------------------------------------------------------------------------

        public Task<IReadOnlyList<CompanySubscriptionInfo>> GetAllCompanySubscriptionsAsync(CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.GetAllCompanySubscriptionsAsync(cancellationToken));

        public Task<CompanySubscriptionInfo?> GetCompanySubscriptionAsync(int companyId, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.GetCompanySubscriptionAsync(companyId, cancellationToken));

        public Task<IReadOnlyList<string>> GetEnabledModuleCodesAsync(int companyId, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.GetEnabledModuleCodesAsync(companyId, cancellationToken));

        public Task<bool> IsModuleEnabledAsync(int companyId, string moduleCode, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.IsModuleEnabledAsync(companyId, moduleCode, cancellationToken));

        public Task<IReadOnlyList<SubscriptionPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.GetPlansAsync(cancellationToken));

        public Task<IReadOnlyList<SoftwareModule>> GetModulesAsync(CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.GetModulesAsync(cancellationToken));

        public Task<IReadOnlyList<SubscriptionChangeRequestInfo>> GetChangeRequestsAsync(string? status = null, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.GetChangeRequestsAsync(status, cancellationToken));

        public Task<IReadOnlyList<SubscriptionHistoryInfo>> GetHistoryAsync(int? companyId = null, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.GetHistoryAsync(companyId, cancellationToken));

        public Task<IReadOnlyList<SubscriptionPlanVersionInfo>> GetPlanVersionsAsync(int? subscriptionPlanId = null, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.GetPlanVersionsAsync(subscriptionPlanId, cancellationToken));

        public Task<SubscriptionPlanVersionDetails?> GetPlanVersionDetailsAsync(int subscriptionPlanVersionId, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.GetPlanVersionDetailsAsync(subscriptionPlanVersionId, cancellationToken));

        // ---- company subscription mutations (OPTION 2 workflow + administrative states) -------

        public Task<SubscriptionMutationResult> ChangePlanAsync(int companyId, string planCode, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.ChangePlanAsync(companyId, planCode, cancellationToken));

        public Task<SubscriptionMutationResult> UpdateDatesAsync(int companyId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.UpdateDatesAsync(companyId, startDate, endDate, cancellationToken));

        public Task<SubscriptionMutationResult> SuspendAsync(int companyId, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.SuspendAsync(companyId, cancellationToken));

        public Task<SubscriptionMutationResult> ReactivateAsync(int companyId, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.ReactivateAsync(companyId, cancellationToken));

        public Task<SubscriptionMutationResult> SubmitChangeRequestAsync(
            int companyId, string requestedPlanCode, string requestedBy, string notes,
            CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.SubmitChangeRequestAsync(companyId, requestedPlanCode, requestedBy, notes, cancellationToken));

        public Task<SubscriptionMutationResult> CancelChangeRequestAsync(int requestId, string requestedBy, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.CancelChangeRequestAsync(requestId, requestedBy, cancellationToken));

        public Task<SubscriptionMutationResult> ReviewChangeRequestAsync(
            int requestId, bool approve, string reviewedBy, string reviewNotes,
            CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.ReviewChangeRequestAsync(requestId, approve, reviewedBy, reviewNotes, cancellationToken));

        // ---- plan catalog + versioning --------------------------------------------------------

        public Task<SubscriptionMutationResult> CreatePlanAsync(
            string code, string name, string description, decimal price, string billingCycle, string termsAndAgreement,
            CancellationToken cancellationToken = default, string changedBy = "") =>
            ScopeAsync(s => s.CreatePlanAsync(code, name, description, price, billingCycle, termsAndAgreement, cancellationToken, changedBy));

        public Task<SubscriptionMutationResult> UpdatePlanAsync(
            int subscriptionPlanId, string name, string description, decimal price, string billingCycle, string termsAndAgreement,
            CancellationToken cancellationToken = default, string changedBy = "") =>
            ScopeAsync(s => s.UpdatePlanAsync(subscriptionPlanId, name, description, price, billingCycle, termsAndAgreement, cancellationToken, changedBy));

        public Task<SubscriptionMutationResult> SetPlanActiveAsync(int subscriptionPlanId, bool isActive, CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.SetPlanActiveAsync(subscriptionPlanId, isActive, cancellationToken));

        public Task<SubscriptionMutationResult> AddModuleToPlanAsync(int subscriptionPlanId, string moduleCode, CancellationToken cancellationToken = default, string changedBy = "") =>
            ScopeAsync(s => s.AddModuleToPlanAsync(subscriptionPlanId, moduleCode, cancellationToken, changedBy));

        public Task<SubscriptionMutationResult> RemoveModuleFromPlanAsync(int subscriptionPlanId, string moduleCode, CancellationToken cancellationToken = default, string changedBy = "") =>
            ScopeAsync(s => s.RemoveModuleFromPlanAsync(subscriptionPlanId, moduleCode, cancellationToken, changedBy));

        public Task<PlanVersionInitializationResult> EnsureInitialPlanVersionsAsync(string changedBy = "", CancellationToken cancellationToken = default) =>
            ScopeAsync(s => s.EnsureInitialPlanVersionsAsync(changedBy, cancellationToken));
    }
}
