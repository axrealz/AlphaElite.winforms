using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaElite.infrastructure.services
{
    /// <summary>
    /// Tenant-aware authentication and tenant discovery.
    ///
    /// Contract:
    ///  - Tenant A and Tenant B authenticate against
    ///    <see cref="AlphaElite.infrastructure.data.TenantAlphaEliteDbContext"/>.
    ///  - Tenant C authenticates against
    ///    <see cref="AlphaElite.infrastructure.data.TenantCAlphaEliteDbContext"/>.
    ///  - Phase 1: the tenant is no longer chosen in the UI. It is derived from the
    ///    credentials via <see cref="FindTenantsContainingUserAsync"/> (read-only username
    ///    lookup) and authentication then happens in that single tenant only - never as a
    ///    fallback loop across tenants.
    /// </summary>
    public interface ITenantAuthenticationService
    {
        /// <summary>Active tenants (company + database) available for login.</summary>
        Task<IReadOnlyList<TenantOption>> GetAvailableTenantsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Authenticates a user against a specific tenant database.
        /// Returns null when the credentials are invalid or the account is inactive.
        /// Throws when the tenant database cannot be read (for example when it has no
        /// UserAccounts table yet), so callers can report the exact limitation.
        /// </summary>
        Task<TenantLoginResult?> AuthenticateAsync(
            int companyId,
            string username,
            string password,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Phase 1: finds the tenants whose <c>UserAccounts</c> table contains the username.
        ///
        /// Read-only by design: it projects the username column only - never password hashes
        /// or any other account data. Tenants whose database has no UserAccounts table are
        /// skipped (they cannot hold accounts), and a tenant that cannot be read yields no
        /// match so that infrastructure problems never leak into the login error.
        ///
        /// The returned list is what the login flow uses to resolve the tenant from the
        /// credentials alone:
        ///   * exactly one match  -> authenticate only in that tenant;
        ///   * no match           -> the username is not a tenant user (master path);
        ///   * more than one      -> ambiguous: the login MUST fail closed, the password must
        ///                           never be tried against every candidate tenant.
        /// </summary>
        Task<IReadOnlyList<TenantOption>> FindTenantsContainingUserAsync(
            string username,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Transparently upgrades a legacy unsalted SHA-256 hash to salted PBKDF2 after a
        /// successful login. Never stores or logs the password or either hash.
        ///
        /// Scope: branch-aware tenants (Tenant C) only. Legacy tenants (Tenant A/B) are
        /// source databases and are never written to.
        ///
        /// Returns true when a hash was upgraded.
        /// </summary>
        Task<bool> UpgradePasswordHashIfNeededAsync(
            int companyId,
            string username,
            string password,
            CancellationToken cancellationToken = default);
    }
}
