using AlphaElite.infrastructure.data;
using System;
using System.Threading.Tasks;

namespace AlphaElite.infrastructure.services
{
    /// <summary>
    /// Opens the branch-aware tenant database (Tenant C) using the same tenant
    /// resolution and credential lookup as the standard factory. Reused by the login
    /// flow today and by the Tenant C user interface in the branch phase.
    /// </summary>
    public class TenantCDbContextFactory : ITenantCDbContextFactory
    {
        private readonly ITenantDatabaseResolver _resolver;
        private readonly TenantDbConnectionFactory _connectionFactory;

        public TenantCDbContextFactory(
            ITenantDatabaseResolver resolver,
            TenantDbConnectionFactory connectionFactory)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<TenantCAlphaEliteDbContext> CreateAsync(int companyId)
        {
            var databaseInfo = await _resolver.GetDatabaseInfoAsync(companyId);

            var options = _connectionFactory.CreateBranchAwareOptions(databaseInfo);

            return new TenantCAlphaEliteDbContext(options);
        }
    }
}