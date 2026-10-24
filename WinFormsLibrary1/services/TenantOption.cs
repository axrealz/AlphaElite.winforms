using System;

namespace AlphaElite.infrastructure.services
{
    /// <summary>
    /// A tenant/company that can be selected for login. Populated from the master
    /// database (Companies + CompanyDatabase) so no tenant id is ever hardcoded.
    /// </summary>
    public sealed class TenantOption
    {
        public int CompanyId { get; init; }
        public string CompanyCode { get; init; } = string.Empty;
        public string CompanyName { get; init; } = string.Empty;
        public string CredentialKey { get; init; } = string.Empty;
        public string DatabaseName { get; init; } = string.Empty;
        public string ServerName { get; init; } = string.Empty;

        /// <summary>
        /// True when this tenant uses the branch-aware model
        /// (<see cref="AlphaElite.infrastructure.data.TenantCAlphaEliteDbContext"/>).
        /// Sourced from configuration (TenantSchema:BranchAwareCredentialKeys) until the
        /// master database carries that metadata itself.
        /// </summary>
        public bool IsBranchAware { get; init; }

        public string DisplayName => $"{CompanyName} ({CompanyCode})";

        public override string ToString() => DisplayName;
    }
}
