using AlphaElite.infrastructure.data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using System;

namespace AlphaElite.infrastructure.services
{
    /// <summary>
    /// Builds tenant connection strings and DbContext options for the tenant databases.
    ///
    /// The master database stores a <c>CredentialKey</c> per tenant (for example
    /// "TenantB"). That key is a *lookup key* into configuration; it is NOT a
    /// database login. The actual user id / password are read from
    /// <c>TenantCredentials:{CredentialKey}:UserId</c> and <c>:Password</c> so that
    /// credentials never live in source code and never have to be hardcoded.
    /// </summary>
    public sealed class TenantDbConnectionFactory
    {
        private readonly IConfiguration _configuration;

        public TenantDbConnectionFactory(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Creates the connection string for the tenant described by <paramref name="databaseInfo"/>.
        /// Throws a value-free error when the credentials are not configured.
        /// </summary>
        public string CreateConnectionString(TenantDatabaseInfo databaseInfo)
        {
            if (databaseInfo == null) throw new ArgumentNullException(nameof(databaseInfo));

            var userId = _configuration[$"TenantCredentials:{databaseInfo.CredentialKey}:UserId"];
            var password = _configuration[$"TenantCredentials:{databaseInfo.CredentialKey}:Password"];

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    $"No database credentials are configured for tenant key '{databaseInfo.CredentialKey}'. " +
                    $"Provide TenantCredentials:{databaseInfo.CredentialKey}:UserId and " +
                    $"TenantCredentials:{databaseInfo.CredentialKey}:Password (environment variables are recommended).");
            }

            return new SqlConnectionStringBuilder
            {
                DataSource = databaseInfo.ServerName,
                InitialCatalog = databaseInfo.DatabaseName,
                UserID = userId,
                Password = password,
                Encrypt = true,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true
            }.ConnectionString;
        }

        /// <summary>Options for the standard (non-branch-aware) tenant context used by Tenant A and Tenant B.</summary>
        public DbContextOptions<TenantAlphaEliteDbContext> CreateStandardOptions(TenantDatabaseInfo databaseInfo)
        {
            return new DbContextOptionsBuilder<TenantAlphaEliteDbContext>()
                .UseSqlServer(CreateConnectionString(databaseInfo), ConfigureSqlServer)
                .Options;
        }

        /// <summary>Options for the branch-aware tenant context (Tenant C).</summary>
        public DbContextOptions<TenantCAlphaEliteDbContext> CreateBranchAwareOptions(TenantDatabaseInfo databaseInfo)
        {
            return new DbContextOptionsBuilder<TenantCAlphaEliteDbContext>()
                .UseSqlServer(CreateConnectionString(databaseInfo), ConfigureSqlServer)
                .Options;
        }

        private static void ConfigureSqlServer(SqlServerDbContextOptionsBuilder sqlOptions)
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        }
    }
}
