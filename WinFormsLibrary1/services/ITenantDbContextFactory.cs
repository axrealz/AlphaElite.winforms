using AlphaElite.infrastructure.data;
using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.infrastructure.services
{
    public interface ITenantDbContextFactory
    {
        /// <summary>
        /// Opens a tenant database context.
        /// </summary>
        /// <param name="companyId">Company (tenant) id from the master database.</param>
        Task<TenantAlphaEliteDbContext> CreateAsync(int companyId);
    }
}
