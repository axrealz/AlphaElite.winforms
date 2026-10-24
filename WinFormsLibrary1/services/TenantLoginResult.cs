namespace AlphaElite.infrastructure.services
{
    /// <summary>
    /// Result of a successful tenant authentication. Contains no credential material.
    /// </summary>
    public sealed class TenantLoginResult
    {
        public int CompanyId { get; init; }
        public string CompanyCode { get; init; } = string.Empty;
        public string CompanyName { get; init; } = string.Empty;
        public int UserAccountId { get; init; }
        public string Username { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;

        /// <summary>True when the tenant database uses the branch-aware model.</summary>
        public bool IsBranchAware { get; init; }

        /// <summary>Branch this user belongs to (Tenant C). Null for tenant-wide roles such as Admin.</summary>
        public int? BranchId { get; init; }

        /// <summary>Member record linked to this login (Tenant C member accounts only).</summary>
        public int? MemberId { get; init; }

        /// <summary>
        /// True when the stored password hash is the legacy unsalted SHA-256 format.
        /// Re-hashing is a database write and therefore needs explicit approval.
        /// </summary>
        public bool NeedsPasswordRehash { get; init; }
    }
}
