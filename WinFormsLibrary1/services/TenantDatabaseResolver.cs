using AlphaElite.infrastructure.data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.infrastructure.services
{
    public class TenantDatabaseResolver : ITenantDatabaseResolver
    {
        private readonly MasterAlphaEliteDbContext _masterDb;

        // ROOT-CAUSE FIX (DbContext concurrency): this resolver is an application-lifetime
        // singleton and EVERY tenant-context creation funnels through GetDatabaseInfoAsync
        // (module loads, KPI-card clicks, logins, subscriptions, master admin). The lookup
        // below runs against the ONE shared MasterAlphaEliteDbContext instance, so two
        // concurrent creations used to start two overlapping EF operations on that single
        // instance and abort with "A second operation was started on this context instance
        // before a previous operation completed." - which in turn aborted the running
        // LoadDashboardAsync mid-method and left the BI charts without data ("charts
        // disappear"). Serializing the tiny master lookup removes the shared-context race
        // for every caller; callers keep their own per-call tenant contexts unchanged.
        private readonly SemaphoreSlim _masterQueryGate = new SemaphoreSlim(1, 1);

        public TenantDatabaseResolver(MasterAlphaEliteDbContext masterDb)
        {
            _masterDb = masterDb ?? throw new ArgumentNullException(nameof(masterDb));
        }

        public async Task<TenantDatabaseInfo> GetDatabaseInfoAsync(int companyId)
        {
            await _masterQueryGate.WaitAsync();
            try
            {
                var tenantDatabase = await _masterDb.CompanyDatabase
                  .AsNoTracking()
                  .FirstOrDefaultAsync(x =>
                    x.CompanyId == companyId &&
                    x.IsActive);
                if (tenantDatabase == null)

                {
                    throw new InvalidOperationException(
                     $"No active tenant database found for CompanyId {companyId}.");
                }

                return new TenantDatabaseInfo
                {
                    ServerName = tenantDatabase.ServerName,
                    DatabaseName = tenantDatabase.DatabaseName,
                    CredentialKey = tenantDatabase.CredentialKey
                };
            }
            finally
            {
                _masterQueryGate.Release();
            }
        }
    }
}
