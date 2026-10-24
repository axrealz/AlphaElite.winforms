using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.infrastructure.services
{
    public interface ITenantDatabaseResolver
    {
        Task<TenantDatabaseInfo> GetDatabaseInfoAsync(int companyId);
    }
}
