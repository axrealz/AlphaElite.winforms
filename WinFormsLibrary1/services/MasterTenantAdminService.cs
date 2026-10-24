using AlphaElite.infrastructure.data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaElite.infrastructure.services
{
    /// <summary>Company row + its database configuration, as shown to the master admin. No secrets.</summary>
    public sealed class CompanyOverview
    {
        public int CompanyId { get; init; }
        public string CompanyCode { get; init; } = string.Empty;
        public string CompanyName { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public DateTime CreatedAt { get; init; }

        public CompanyDatabaseInfo? Database { get; init; }
    }

    /// <summary>CompanyDatabase projection for the master UI. Carries the credential KEY only - never a secret.</summary>
    public sealed class CompanyDatabaseInfo
    {
        public int CompanyDatabaseId { get; init; }
        public int CompanyId { get; init; }
        public string ServerName { get; init; } = string.Empty;
        public string DatabaseName { get; init; } = string.Empty;

        /// <summary>Lookup key into configuration (e.g. "TenantA"). Not a login; not secret-bearing.</summary>
        public string CredentialKey { get; init; } = string.Empty;
        public bool IsActive { get; init; }
    }

    public sealed class CompanyMutationResult
    {
        public bool Success { get; init; }
        public string Error { get; init; } = string.Empty;
    }

    /// <summary>Per-tenant aggregate counts read from the tenant database. Success=false when unreadable.</summary>
    public sealed class TenantAggregate
    {
        public int CompanyId { get; init; }
        public bool Success { get; init; }
        public string Error { get; init; } = string.Empty;

        public int Members { get; init; }
        public int ActiveMembers { get; init; }
        public int Attendance { get; init; }
        public int Payments { get; init; }
        public decimal PaymentTotal { get; init; }
        public int Feedback { get; init; }
        public int Inquiries { get; init; }
        public int RetentionRecords { get; init; }
        public int Complaints { get; init; }
        public int Branches { get; init; }
        public int UserAccounts { get; init; }
        public int Subscriptions { get; init; }
    }

    /// <summary>Platform-level counts for the master dashboard (master DB only).</summary>
    public sealed class MasterPlatformCounts
    {
        public int TotalCompanies { get; set; }
        public int ActiveCompanies { get; set; }
        public int InactiveCompanies => TotalCompanies - ActiveCompanies;
        public int ConfiguredDatabases { get; set; }
        public int ActiveDatabases { get; set; }
    }

    /// <summary>
    /// Master-level company/tenant administration. Company and CompanyDatabase data comes from the
    /// master DB; per-tenant aggregates are read through the EXISTING resolver + connection factory
    /// (no arbitrary connection strings, no hardcoded CompanyId). One unavailable tenant database
    /// degrades only that tenant's row - it never crashes the master dashboard.
    /// </summary>
    public interface IMasterTenantAdminService
    {
        Task<IReadOnlyList<CompanyOverview>> GetCompanyOverviewsAsync(CancellationToken cancellationToken = default);
        Task<CompanyMutationResult> SetCompanyActiveAsync(int companyId, bool active, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TenantAggregate>> GetTenantAggregatesAsync(IReadOnlyList<CompanyOverview> companies, CancellationToken cancellationToken = default);
        Task<MasterPlatformCounts> GetPlatformCountsAsync(CancellationToken cancellationToken = default);
    }

    public sealed class MasterTenantAdminService : IMasterTenantAdminService
    {
        private readonly MasterAlphaEliteDbContext _masterDb;
        private readonly ITenantDatabaseResolver _resolver;
        private readonly TenantDbConnectionFactory _connectionFactory;

        public MasterTenantAdminService(
            MasterAlphaEliteDbContext masterDb,
            ITenantDatabaseResolver resolver,
            TenantDbConnectionFactory connectionFactory)
        {
            _masterDb = masterDb ?? throw new ArgumentNullException(nameof(masterDb));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task<IReadOnlyList<CompanyOverview>> GetCompanyOverviewsAsync(CancellationToken cancellationToken = default)
        {
            var companies = await _masterDb.Companies
                .AsNoTracking()
                .OrderBy(c => c.CompanyId)
                .ToListAsync(cancellationToken);

            var databases = await _masterDb.CompanyDatabase
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var databasesByCompany = databases
                .GroupBy(d => d.CompanyId)
                .ToDictionary(g => g.Key, g => g.OrderBy(d => d.CompanyDatabaseId).First());

            var result = new List<CompanyOverview>();
            foreach (var company in companies)
            {
                CompanyDatabaseInfo? database = null;
                if (databasesByCompany.TryGetValue(company.CompanyId, out var row))
                {
                    database = new CompanyDatabaseInfo
                    {
                        CompanyDatabaseId = row.CompanyDatabaseId,
                        CompanyId = row.CompanyId,
                        ServerName = row.ServerName,
                        DatabaseName = row.DatabaseName,
                        CredentialKey = row.CredentialKey,
                        IsActive = row.IsActive
                    };
                }

                result.Add(new CompanyOverview
                {
                    CompanyId = company.CompanyId,
                    CompanyCode = company.CompanyCode,
                    CompanyName = company.CompanyName,
                    IsActive = company.IsActive,
                    CreatedAt = company.CreatedAt,
                    Database = database
                });
            }

            return result;
        }

        /// <summary>
        /// Activates/deactivates a company by writing Companies.IsActive in the MASTER DB.
        /// Enforcement happens at login: <see cref="TenantAuthenticationService.AuthenticateAsync"/>
        /// rejects logins for inactive companies. Companies are never deleted.
        /// </summary>
        public async Task<CompanyMutationResult> SetCompanyActiveAsync(int companyId, bool active, CancellationToken cancellationToken = default)
        {
            var company = await _masterDb.Companies.FirstOrDefaultAsync(c => c.CompanyId == companyId, cancellationToken);
            if (company == null)
            {
                return new CompanyMutationResult { Success = false, Error = $"Company {companyId} not found." };
            }

            if (company.IsActive == active)
            {
                return new CompanyMutationResult { Success = true, Error = string.Empty };
            }

            company.IsActive = active;
            await _masterDb.SaveChangesAsync(cancellationToken);
            return new CompanyMutationResult { Success = true, Error = string.Empty };
        }

        public async Task<IReadOnlyList<TenantAggregate>> GetTenantAggregatesAsync(
            IReadOnlyList<CompanyOverview> companies, CancellationToken cancellationToken = default)
        {
            var aggregates = new List<TenantAggregate>();

            foreach (var company in companies)
            {
                if (company.Database == null || !company.Database.IsActive)
                {
                    aggregates.Add(new TenantAggregate
                    {
                        CompanyId = company.CompanyId,
                        Success = false,
                        Error = company.Database == null
                            ? "No database configured."
                            : "Database configuration is inactive."
                    });
                    continue;
                }

                try
                {
                    aggregates.Add(await ReadAggregateAsync(company.CompanyId, cancellationToken));
                }
                catch (Exception ex)
                {
                    // Never let one unreachable tenant crash the master dashboard.
                    aggregates.Add(new TenantAggregate
                    {
                        CompanyId = company.CompanyId,
                        Success = false,
                        Error = TrimError(ex.Message)
                    });
                }
            }

            return aggregates;
        }

        public async Task<MasterPlatformCounts> GetPlatformCountsAsync(CancellationToken cancellationToken = default)
        {
            var companies = await _masterDb.Companies.AsNoTracking().ToListAsync(cancellationToken);
            var databases = await _masterDb.CompanyDatabase.AsNoTracking().ToListAsync(cancellationToken);

            return new MasterPlatformCounts
            {
                TotalCompanies = companies.Count,
                ActiveCompanies = companies.Count(c => c.IsActive),
                ConfiguredDatabases = databases.Count,
                ActiveDatabases = databases.Count(d => d.IsActive)
            };
        }

        /// <summary>
        /// Reads per-tenant counts through the existing resolver + connection factory. Raw
        /// projections are used so a tenant whose schema predates a module (e.g. Tenant A / B have
        /// no Branches / Complaints / Subscriptions / UserAccounts tables) reports 0 for that
        /// module instead of failing the whole row.
        /// </summary>
        private async Task<TenantAggregate> ReadAggregateAsync(int companyId, CancellationToken cancellationToken)
        {
            var databaseInfo = await _resolver.GetDatabaseInfoAsync(companyId);

            // The factory resolves credentials from configuration (TenantCredentials:{key}:*).
            var connectionString = _connectionFactory.CreateConnectionString(databaseInfo);
            var options = new DbContextOptionsBuilder<MasterTenantProbeContext>()
                .UseSqlServer(connectionString)
                .Options;

            await using var db = new MasterTenantProbeContext(options);

            var members = await CountAsync(db, "SELECT CAST(COUNT(*) AS int) AS [Value] FROM dbo.Members", cancellationToken);
            var activeMembers = await CountAsync(db, "SELECT CAST(COUNT(*) AS int) AS [Value] FROM dbo.Members WHERE IsActive = 1", cancellationToken);
            var attendance = await CountAsync(db, "SELECT CAST(COUNT(*) AS int) AS [Value] FROM dbo.Attendances", cancellationToken);
            var payments = await CountAsync(db, "SELECT CAST(COUNT(*) AS int) AS [Value] FROM dbo.Payments", cancellationToken);
            var feedback = await CountAsync(db, "SELECT CAST(COUNT(*) AS int) AS [Value] FROM dbo.Feedbacks", cancellationToken);
            var inquiries = await CountAsync(db, "SELECT CAST(COUNT(*) AS int) AS [Value] FROM dbo.Inquiries", cancellationToken);

            var paymentTotalRow = await db.Database
                .SqlQueryRaw<decimal?>("SELECT CAST(COALESCE(SUM(AmountPaid), 0) AS decimal(18,2)) AS [Value] FROM dbo.Payments")
                .FirstOrDefaultAsync(cancellationToken);

            return new TenantAggregate
            {
                CompanyId = companyId,
                Success = true,
                Members = members,
                ActiveMembers = activeMembers,
                Attendance = attendance,
                Payments = payments,
                PaymentTotal = paymentTotalRow ?? 0m,
                Feedback = feedback,
                Inquiries = inquiries,
                Subscriptions = await CountIfTableExistsAsync(db, "Subscriptions", cancellationToken),
                UserAccounts = await CountIfTableExistsAsync(db, "UserAccounts", cancellationToken),
                RetentionRecords = await CountIfTableExistsAsync(db, "RetentionRecords", cancellationToken),
                Branches = await CountIfTableExistsAsync(db, "Branches", cancellationToken),
                Complaints = await CountIfTableExistsAsync(db, "Complaints", cancellationToken)
            };
        }

        private static async Task<int> CountAsync(MasterTenantProbeContext db, string sql, CancellationToken cancellationToken)
        {
            return await db.Database
                .SqlQueryRaw<int>(sql)
                .FirstAsync(cancellationToken);
        }

        /// <summary>Counts a table that may not exist in an older tenant schema; 0 when missing.
        /// The table name always comes from the compile-time whitelist below, never from input.</summary>
        private static async Task<int> CountIfTableExistsAsync(MasterTenantProbeContext db, string tableName, CancellationToken cancellationToken)
        {
            // Every count SQL is a compile-time constant (no interpolation -> nothing to inject).
            // The caller passes a fixed table name; unknown names are ignored.
            var existsSql = tableName switch
            {
                "Subscriptions" => "SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables WHERE name = 'Subscriptions'",
                "UserAccounts" => "SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables WHERE name = 'UserAccounts'",
                "RetentionRecords" => "SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables WHERE name = 'RetentionRecords'",
                "Branches" => "SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables WHERE name = 'Branches'",
                "Complaints" => "SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables WHERE name = 'Complaints'",
                _ => null
            };

            if (existsSql == null)
            {
                return 0;
            }

            try
            {
                var exists = await db.Database
                    .SqlQueryRaw<int>(existsSql)
                    .FirstAsync(cancellationToken);

                if (exists == 0)
                {
                    return 0;
                }

                var countSql = "SELECT CAST(COUNT(*) AS int) AS [Value] FROM dbo." + tableName;
                return await db.Database
                    .SqlQueryRaw<int>(countSql)
                    .FirstAsync(cancellationToken);
            }
            catch
            {
                return 0;
            }
        }

        private static string TrimError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "Unknown error.";
            }

            var single = message.Replace("\r", " ").Replace("\n", " ");
            return single.Length <= 160 ? single : single[..160] + "...";
        }

        /// <summary>
        /// Minimal read-only EF context over any tenant database. It maps no entities; every read
        /// is a raw SqlQuery so a missing column in an older tenant schema can never break
        /// master-level counting.
        /// </summary>
        private sealed class MasterTenantProbeContext : DbContext
        {
            public MasterTenantProbeContext(DbContextOptions<MasterTenantProbeContext> options) : base(options) { }
        }
    }
}
