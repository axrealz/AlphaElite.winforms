using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Single composition root for the desktop application.
    ///
    /// Configuration is layered as: appsettings.json -> optional appsettings.Local.json
    /// (not committed / used for local secrets) -> environment variables, so database
    /// credentials never have to live in source code.
    /// </summary>
    internal sealed class AppRuntime
    {
        private AppRuntime(
            IConfiguration configuration,
            MasterAlphaEliteDbContext masterDb,
            IDbContextFactory<MasterAlphaEliteDbContext> masterContextFactory,
            ITenantDatabaseResolver resolver,
            TenantDbConnectionFactory connectionFactory,
            ITenantDbContextFactory tenantFactory,
            ITenantCDbContextFactory tenantCContextFactory,
            ITenantDataContextFactory dataContextFactory,
            ITenantAuthenticationService authenticationService,
            IAuthorizationService authorizationService,
            IMasterIdentityService masterIdentityService,
            IMasterTenantAdminService masterTenantAdminService,
            ISubscriptionService subscriptionService,
            IMasterBusinessIntelligenceService businessIntelligenceService)
        {
            Configuration = configuration;
            MasterDb = masterDb;
            MasterContextFactory = masterContextFactory;
            Resolver = resolver;
            ConnectionFactory = connectionFactory;
            TenantFactory = tenantFactory;
            TenantCContextFactory = tenantCContextFactory;
            DataContextFactory = dataContextFactory;
            AuthenticationService = authenticationService;
            AuthorizationService = authorizationService;
            MasterIdentityService = masterIdentityService;
            MasterTenantAdminService = masterTenantAdminService;
            SubscriptionService = subscriptionService;
            BusinessIntelligenceService = businessIntelligenceService;
        }

        public IConfiguration Configuration { get; }

        /// <summary>
        /// The composition-root master context. It is a long-lived "root" context used by the
        /// deployment/verification harnesses (which run strictly sequentially); interactive UI
        /// code must use <see cref="SubscriptionService"/> (per-operation contexts) or
        /// <see cref="MasterContextFactory"/> instead, so two overlapping operations can never
        /// share one context instance.
        /// </summary>
        public MasterAlphaEliteDbContext MasterDb { get; }

        /// <summary>
        /// Creates a fresh master context for ONE operation. Used where a caller needs a context
        /// that cannot possibly be shared with anything else (for example a synchronous UI read).
        /// </summary>
        public IDbContextFactory<MasterAlphaEliteDbContext> MasterContextFactory { get; }

        public ITenantDatabaseResolver Resolver { get; }
        public TenantDbConnectionFactory ConnectionFactory { get; }

        /// <summary>Standard tenant context (Tenant A / Tenant B) used by the main window.</summary>
        public ITenantDbContextFactory TenantFactory { get; }

        /// <summary>Branch-aware context (Tenant C); used from the branch phase onward.</summary>
        public ITenantCDbContextFactory TenantCContextFactory { get; }

        /// <summary>Context-agnostic tenant data access used by the UI (legacy vs branch-aware).</summary>
        public ITenantDataContextFactory DataContextFactory { get; }

        public ITenantAuthenticationService AuthenticationService { get; }

        /// <summary>Central role/permission service shared by the UI and the data layer.</summary>
        public IAuthorizationService AuthorizationService { get; }

        /// <summary>Master (Identity) authentication and master user administration (Phase 5).</summary>
        public IMasterIdentityService MasterIdentityService { get; }

        /// <summary>Master-level company/tenant administration and tenant aggregates (Phase 5).</summary>
        public IMasterTenantAdminService MasterTenantAdminService { get; }

        /// <summary>Master-level SOFTWARE subscription administration (Phase 9).</summary>
        public ISubscriptionService SubscriptionService { get; }

        /// <summary>
        /// Super Admin Business Intelligence over the master database (subscription KPIs and
        /// charts). Per-operation context: each snapshot is one self-contained master read.
        /// </summary>
        public IMasterBusinessIntelligenceService BusinessIntelligenceService { get; }

        /// <summary>
        /// Environment-variable overrides using the standard double-underscore convention
        /// (for example TenantCredentials__TenantB__Password or ConnectionStrings__MasterErp),
        /// so credentials can be moved out of appsettings.json without a code change.
        /// </summary>
        private static IDictionary<string, string?> ReadEnvironmentOverrides()
        {
            var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                var key = entry.Key?.ToString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                overrides[key.Replace("__", ":")] = entry.Value?.ToString();
            }

            return overrides;
        }

        public static AppRuntime Create()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
                .AddInMemoryCollection(ReadEnvironmentOverrides())
                .Build();

            var masterConnectionString = configuration.GetConnectionString("MasterErp");

            if (string.IsNullOrWhiteSpace(masterConnectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:MasterErp is not configured. Set it in appsettings.json, " +
                    "appsettings.Local.json, or the ConnectionStrings__MasterErp environment variable.");
            }

            // ONE shared resilient configuration for every master context. Transient-error
            // resilience (bounded retry) lives in MasterDbContextOptions so the composition root,
            // the per-operation factory, the Identity clone, the design-time factory and the API
            // host can never drift apart.
            var masterOptions = MasterDbContextOptions.Create(masterConnectionString);

            var masterDb = new MasterAlphaEliteDbContext(masterOptions);

            // Per-operation master contexts (concurrency fix). Every interactive master operation
            // gets its own DbContext, so overlapping operations are legal by construction.
            IDbContextFactory<MasterAlphaEliteDbContext> masterContextFactory =
                new MasterDbContextFactory(masterConnectionString);

            ITenantDatabaseResolver resolver = new TenantDatabaseResolver(masterDb);
            var connectionFactory = new TenantDbConnectionFactory(configuration);

            ITenantDbContextFactory tenantFactory = new TenantDbContextFactory(resolver, connectionFactory);
            ITenantCDbContextFactory tenantCContextFactory = new TenantCDbContextFactory(resolver, connectionFactory);
            ITenantDataContextFactory dataContextFactory = new TenantDataContextFactory(tenantFactory, tenantCContextFactory);
            IAuthorizationService authorizationService = new RoleAuthorizationService();

            ITenantAuthenticationService authenticationService =
                new TenantAuthenticationService(masterDb, resolver, connectionFactory, configuration);

            IMasterIdentityService masterIdentityService = new MasterIdentityService(masterDb);
            IMasterTenantAdminService masterTenantAdminService =
                new MasterTenantAdminService(masterDb, resolver, connectionFactory);

            // The UI-facing subscription service is scoped PER OPERATION (one context per call).
            // SubscriptionService itself stays context-bound and is still used directly by the
            // sequential deployment/verification steps (MasterSubscriptionDeployment).
            ISubscriptionService subscriptionService = new ScopedSubscriptionService(masterContextFactory);

            // Business Intelligence is master-only and uses the same per-operation context
            // factory, so a BI read can overlap a subscription read without any shared state.
            IMasterBusinessIntelligenceService businessIntelligenceService =
                new MasterBusinessIntelligenceService(masterContextFactory);

            return new AppRuntime(
                configuration,
                masterDb,
                masterContextFactory,
                resolver,
                connectionFactory,
                tenantFactory,
                tenantCContextFactory,
                dataContextFactory,
                authenticationService,
                authorizationService,
                masterIdentityService,
                masterTenantAdminService,
                subscriptionService,
                businessIntelligenceService);
        }
    }
}
