using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AlphaElite.infrastructure.data
{
    // Design-time factory to avoid loading the startup assembly during EF tooling operations
    public class MasterAlphaEliteDbContextFactory : IDesignTimeDbContextFactory<MasterAlphaEliteDbContext>
    {
        public MasterAlphaEliteDbContext CreateDbContext(string[] args)
        {
            // NOTE (Phase 1 rollback): the original version of this factory
            // hardcoded the master database connection string here, matching
            // ConnectionStrings:MasterErp in the API/winforms appsettings.
            // That credential value was removed during Phase 1 and cannot be
            // reconstructed from this workspace. Restore your original master
            // database password in the placeholder below before running EF
            // design-time commands against the master model.
            var connectionString =
                "Server=db67598.public.databaseasp.net;Database=db67598;User Id=db67598;Password=RESTORE_YOUR_ORIGINAL_MASTER_DB_PASSWORD_HERE;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True;";

            var optionsBuilder = new DbContextOptionsBuilder<MasterAlphaEliteDbContext>();
            // Same resilient master configuration as the run-time master contexts.
            optionsBuilder.UseSqlServer(connectionString, MasterDbContextOptions.Configure);

            return new MasterAlphaEliteDbContext(optionsBuilder.Options);
        }
    }
}
