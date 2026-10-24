using AlphaElite.infrastructure.data;
using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.infrastructure.services
{
    public interface ITenantCDbContextFactory
    {
        Task<TenantCAlphaEliteDbContext> CreateAsync(int companyId);
    }
}
