using System;
using Microsoft.EntityFrameworkCore;

namespace AlphaElite.infrastructure.data
{
    /// <summary>
    /// Per-operation MASTER context factory. The desktop application used to hand ONE
    /// MasterAlphaEliteDbContext instance to every master service AND to the Super Admin
    /// window, which is what produced
    ///
    ///     System.InvalidOperationException:
    ///     A second operation was started on this context instance before a previous operation completed.
    ///
    /// EF Core forbids overlapping operations on a single context, and rapid UI interaction
    /// (switch sub-sections, reload, re-select a company/plan, approve while a refresh runs)
    /// legitimately overlaps them. A context is cheap and is a UNIT OF WORK, not a connection:
    /// creating one per operation makes the overlap legal instead of merely serialized.
    ///
    /// Tenant contexts/providers are untouched by this factory.
    /// </summary>
    public sealed class MasterDbContextFactory : IDbContextFactory<MasterAlphaEliteDbContext>
    {
        private readonly DbContextOptions<MasterAlphaEliteDbContext> _options;

        public MasterDbContextFactory(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("A master connection string is required.", nameof(connectionString));
            }

            // Options are built once and reused; each context still gets its own change tracker,
            // its own connection lease and its own model cache slot. The options carry the shared
            // master transient-failure retry policy (MasterDbContextOptions), so a dropped
            // connection is retried inside the operation instead of failing the whole screen.
            _options = MasterDbContextOptions.Create(connectionString);
        }

        public MasterAlphaEliteDbContext CreateDbContext() => new(_options);
    }
}
