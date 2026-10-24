using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaElite.infrastructure.services
{
    /// <summary>
    /// Tenant-aware authentication.
    ///
    /// Each tenant is authenticated against the DbContext that actually matches its
    /// database schema:
    ///   * Tenant A / Tenant B -> <see cref="TenantAlphaEliteDbContext"/> (standard model)
    ///   * Tenant C            -> <see cref="TenantCAlphaEliteDbContext"/> (branch-aware model)
    ///
    /// No CompanyId is hardcoded and no property is silently ignored to hide a schema
    /// mismatch: the right context is chosen per tenant, and when a tenant database
    /// cannot authenticate (its schema predates the authentication module) a precise
    /// error is reported instead of a raw SQL failure.
    /// </summary>
    public sealed class TenantAuthenticationService : ITenantAuthenticationService
    {
        private readonly MasterAlphaEliteDbContext _masterDb;
        private readonly ITenantDatabaseResolver _resolver;
        private readonly TenantDbConnectionFactory _connectionFactory;
        private readonly IConfiguration _configuration;

        public TenantAuthenticationService(
            MasterAlphaEliteDbContext masterDb,
            ITenantDatabaseResolver resolver,
            TenantDbConnectionFactory connectionFactory,
            IConfiguration configuration)
        {
            _masterDb = masterDb ?? throw new ArgumentNullException(nameof(masterDb));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public async Task<IReadOnlyList<TenantOption>> GetAvailableTenantsAsync(CancellationToken cancellationToken = default)
        {
            var branchAwareKeys = GetBranchAwareCredentialKeys();

            var rows = await _masterDb.CompanyDatabase
                .AsNoTracking()
                .Where(d => d.IsActive && d.Company!.IsActive)
                .OrderBy(d => d.CompanyId)
                .Select(d => new
                {
                    d.CompanyId,
                    d.ServerName,
                    d.DatabaseName,
                    d.CredentialKey,
                    CompanyCode = d.Company!.CompanyCode,
                    CompanyName = d.Company!.CompanyName
                })
                .ToListAsync(cancellationToken);

            return rows
                .Select(r => new TenantOption
                {
                    CompanyId = r.CompanyId,
                    CompanyCode = r.CompanyCode,
                    CompanyName = r.CompanyName,
                    CredentialKey = r.CredentialKey,
                    DatabaseName = r.DatabaseName,
                    ServerName = r.ServerName,
                    IsBranchAware = branchAwareKeys.Contains(r.CredentialKey)
                })
                .ToList();
        }

        public async Task<IReadOnlyList<TenantOption>> FindTenantsContainingUserAsync(
            string username,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return Array.Empty<TenantOption>();
            }

            var login = username.Trim();
            var branchAwareKeys = GetBranchAwareCredentialKeys();
            var tenants = await GetAvailableTenantsAsync(cancellationToken);

            var matches = new List<TenantOption>();

            foreach (var tenant in tenants)
            {
                bool contains;

                try
                {
                    var databaseInfo = await _resolver.GetDatabaseInfoAsync(tenant.CompanyId);

                    if (branchAwareKeys.Contains(tenant.CredentialKey))
                    {
                        var options = _connectionFactory.CreateBranchAwareOptions(databaseInfo);
                        await using var tenantDb = new TenantCAlphaEliteDbContext(options);
                        contains = await TenantContainsUserAsync(tenantDb, login, cancellationToken);
                    }
                    else
                    {
                        var options = _connectionFactory.CreateStandardOptions(databaseInfo);
                        await using var tenantDb = new TenantAlphaEliteDbContext(options);
                        contains = await TenantContainsUserAsync(tenantDb, login, cancellationToken);
                    }
                }
                catch
                {
                    // A tenant database that cannot be read is treated as "no match" so a
                    // connection problem never changes the outcome of a login attempt or
                    // leaks infrastructure details through the generic login error.
                    contains = false;
                }

                if (contains)
                {
                    matches.Add(tenant);
                }
            }

            return matches;
        }

        /// <summary>
        /// Read-only username probe: projects nothing but the existence of the username.
        /// A tenant whose schema predates the authentication module (no UserAccounts table)
        /// simply holds no accounts.
        /// </summary>
        private static async Task<bool> TenantContainsUserAsync(DbContext tenantDb, string username, CancellationToken cancellationToken)
        {
            if (!await UserAccountsTableExistsAsync(tenantDb, cancellationToken))
            {
                return false;
            }

            return await tenantDb.Set<UserAccount>()
                .AsNoTracking()
                .AnyAsync(u => u.Username == username, cancellationToken);
        }

        public async Task<TenantLoginResult?> AuthenticateAsync(
            int companyId,
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            if (companyId <= 0) throw new ArgumentOutOfRangeException(nameof(companyId));
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;

            var login = username.Trim();

            var databaseInfo = await _resolver.GetDatabaseInfoAsync(companyId);

            var company = await _masterDb.Companies
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .Select(c => new { c.CompanyCode, c.CompanyName, c.IsActive })
                .FirstOrDefaultAsync(cancellationToken);

            // Phase 5: a deactivated company must not authenticate, even for users who know
            // their credentials and keep an old tenant list. This is the single enforcement
            // point so master-admin activation/deactivation is consistent at login.
            if (company == null || !company.IsActive)
            {
                return null;
            }

            var companyCode = company.CompanyCode;
            var companyName = company.CompanyName;
            var isBranchAware = GetBranchAwareCredentialKeys().Contains(databaseInfo.CredentialKey);

            UserRow? user;

            if (isBranchAware)
            {
                var options = _connectionFactory.CreateBranchAwareOptions(databaseInfo);
                await using var tenantDb = new TenantCAlphaEliteDbContext(options);
                await EnsureAuthenticationIsSupportedAsync(tenantDb, companyName, companyId, cancellationToken);
                user = await FindUserAsync(tenantDb, login, cancellationToken);
            }
            else
            {
                var options = _connectionFactory.CreateStandardOptions(databaseInfo);
                await using var tenantDb = new TenantAlphaEliteDbContext(options);
                await EnsureAuthenticationIsSupportedAsync(tenantDb, companyName, companyId, cancellationToken);
                user = await FindUserAsync(tenantDb, login, cancellationToken);
            }

            if (user == null || !user.IsActive)
            {
                return null;
            }

            if (!PasswordHasher.Verify(user.PasswordHash, password, out var needsRehash))
            {
                return null;
            }

            return new TenantLoginResult
            {
                CompanyId = companyId,
                CompanyCode = companyCode,
                CompanyName = companyName,
                UserAccountId = user.UserAccountId,
                Username = user.Username,
                FullName = user.FullName,
                Role = Roles.Normalize(user.Role),
                IsBranchAware = isBranchAware,
                BranchId = user.BranchId,
                MemberId = user.MemberId,
                NeedsPasswordRehash = needsRehash
            };
        }

        public async Task<bool> UpgradePasswordHashIfNeededAsync(
            int companyId,
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            if (companyId <= 0 || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                return false;
            }

            var databaseInfo = await _resolver.GetDatabaseInfoAsync(companyId);

            // Only branch-aware tenants (Tenant C) are writable. Tenant A and Tenant B are
            // source databases and must never be modified.
            if (!GetBranchAwareCredentialKeys().Contains(databaseInfo.CredentialKey))
            {
                return false;
            }

            var options = _connectionFactory.CreateBranchAwareOptions(databaseInfo);
            await using var tenantDb = new TenantCAlphaEliteDbContext(options);

            var login = username.Trim();
            var account = await tenantDb.UserAccounts
                .FirstOrDefaultAsync(u => u.Username == login, cancellationToken);

            if (account == null || !account.IsActive)
            {
                return false;
            }

            // Re-verify here so a caller cannot force an upgrade with the wrong password.
            if (!PasswordHasher.Verify(account.PasswordHash, password, out var needsRehash) || !needsRehash)
            {
                return false;
            }

            account.PasswordHash = PasswordHasher.Hash(password);
            await tenantDb.SaveChangesAsync(cancellationToken);
            return true;
        }

        /// <summary>
        /// Projects only the columns the login flow needs, so the query never depends on
        /// optional (branch-aware) columns of a tenant database that does not have them.
        /// </summary>
        private static async Task<UserRow?> FindUserAsync(DbContext tenantDb, string username, CancellationToken cancellationToken)
        {
            switch (tenantDb)
            {
                case TenantCAlphaEliteDbContext branchAware:
                    return await branchAware.UserAccounts
                        .AsNoTracking()
                        .Where(u => u.Username == username)
                        .Select(u => new UserRow(u.UserAccountId, u.Username, u.PasswordHash, u.FullName, u.Role, u.IsActive, u.BranchId, u.MemberId))
                        .FirstOrDefaultAsync(cancellationToken);

                case TenantAlphaEliteDbContext standard:
                    return await standard.UserAccounts
                        .AsNoTracking()
                        .Where(u => u.Username == username)
                        .Select(u => new UserRow(u.UserAccountId, u.Username, u.PasswordHash, u.FullName, u.Role, u.IsActive, null, null))
                        .FirstOrDefaultAsync(cancellationToken);

                default:
                    throw new InvalidOperationException($"Unsupported tenant DbContext '{tenantDb.GetType().Name}'.");
            }
        }

        /// <summary>
        /// Reports a precise limitation when a tenant database predates the authentication
        /// module (for example Tenant A has no UserAccounts table) instead of surfacing a
        /// raw "Invalid object name" SQL error.
        /// </summary>
        private static async Task EnsureAuthenticationIsSupportedAsync(
            DbContext tenantDb,
            string companyName,
            int companyId,
            CancellationToken cancellationToken)
        {
            if (!await UserAccountsTableExistsAsync(tenantDb, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Tenant '{companyName}' (CompanyId {companyId}) does not have a UserAccounts table yet, " +
                    "so it cannot authenticate users. Its database schema predates the authentication module; " +
                    "the database was not modified by this check.");
            }
        }

        private static async Task<bool> UserAccountsTableExistsAsync(DbContext tenantDb, CancellationToken cancellationToken)
        {
            var userAccountTableCount = await tenantDb.Database
                .SqlQueryRaw<int>("SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables WHERE name = 'UserAccounts'")
                .FirstAsync(cancellationToken);

            return userAccountTableCount > 0;
        }

        private IReadOnlyCollection<string> GetBranchAwareCredentialKeys()
        {
            // Configuration-driven so that no tenant id is hardcoded. This moves to master
            // database metadata when the tenant metadata table is introduced.
            var keys = _configuration
                .GetSection("TenantSchema:BranchAwareCredentialKeys")
                .GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToArray();

            return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        }

        private sealed record UserRow(
            int UserAccountId,
            string Username,
            string PasswordHash,
            string FullName,
            string Role,
            bool IsActive,
            int? BranchId,
            int? MemberId);
    }
}
