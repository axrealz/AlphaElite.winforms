using AlphaElite.infrastructure.services;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Everything the main window needs to know about who is signed in. Built by the login
    /// flow from the tenant-aware authentication result; contains no credential material.
    /// </summary>
    public sealed class UserSession
    {
        public int CompanyId { get; init; }
        public string CompanyCode { get; init; } = string.Empty;
        public string CompanyName { get; init; } = string.Empty;

        /// <summary>True when the tenant uses the branch-aware schema (Tenant C).</summary>
        public bool IsBranchAware { get; init; }

        /// <summary>Branch scope; null for tenant-wide roles (Admin/MasterAdmin) and for legacy tenants.</summary>
        public int? BranchId { get; init; }

        /// <summary>Member record linked to this login (Tenant C member accounts only).</summary>
        public int? MemberId { get; init; }

        public string Username { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;

        /// <summary>
        /// Phase 9: the company's SOFTWARE subscription context, read once from the master
        /// database at login and used to compose module gating with RBAC. Null when no
        /// subscription lookup was performed (legacy callers / verification harnesses) - in
        /// that case subscription gating is inert and pure role authorization applies, exactly
        /// as before Phase 9.
        /// </summary>
        public UserSubscriptionContext? Subscription { get; init; }
    }
}
