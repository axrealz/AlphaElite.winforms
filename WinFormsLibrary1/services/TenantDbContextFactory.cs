using AlphaElite.infrastructure.data;
using System;
using System.Threading.Tasks;

namespace AlphaElite.infrastructure.services
{
    /// <summary>
    /// Opens the standard (non-branch-aware) tenant database used by Tenant A and Tenant B.
    /// Credentials are resolved from configuration by <see cref="TenantDbConnectionFactory"/>;
    /// the master database credential key is a lookup key, not a database login.
    /// </summary>
    public class TenantDbContextFactory : ITenantDbContextFactory
    {
        private readonly ITenantDatabaseResolver _resolver;
        private readonly TenantDbConnectionFactory _connectionFactory;

        public TenantDbContextFactory(
            ITenantDatabaseResolver resolver,
            TenantDbConnectionFactory connectionFactory)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<TenantAlphaEliteDbContext> CreateAsync(int companyId)
        {
            var databaseInfo = await _resolver.GetDatabaseInfoAsync(companyId);

            var options = _connectionFactory.CreateStandardOptions(databaseInfo);

            return new TenantAlphaEliteDbContext(options);
        }
    }
}
