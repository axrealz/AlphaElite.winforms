using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaElite.infrastructure.services
{
    /// <summary>
    /// A tenant data context that hides which concrete DbContext is in use.
    ///
    /// Tenant A and Tenant B use the legacy <see cref="TenantAlphaEliteDbContext"/>;
    /// Tenant C uses the branch-aware <see cref="TenantCAlphaEliteDbContext"/>. The UI
    /// depends only on this interface, so a branch-aware tenant can never accidentally be
    /// queried through the legacy context (and vice versa).
    /// </summary>
    public interface ITenantDataContext : IDisposable, IAsyncDisposable
    {
        DbSet<Member> Members { get; }
        DbSet<Attendance> Attendances { get; }
        DbSet<Payment> Payments { get; }
        DbSet<Inquiry> Inquiries { get; }
        DbSet<Feedback> Feedbacks { get; }
        DbSet<Subscription> Subscriptions { get; }
        DbSet<UserAccount> UserAccounts { get; }
        DbSet<RetentionRecord> RetentionRecords { get; }
        DbSet<Branch> Branches { get; }
        DbSet<Complaint> Complaints { get; }

        /// <summary>True for the branch-aware model (Tenant C).</summary>
        bool IsBranchAware { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>Wraps the legacy (non branch-aware) context used by Tenant A / Tenant B.</summary>
    public sealed class LegacyTenantDataContext : ITenantDataContext
    {
        private readonly TenantAlphaEliteDbContext _context;

        public LegacyTenantDataContext(TenantAlphaEliteDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public DbSet<Member> Members => _context.Members;
        public DbSet<Attendance> Attendances => _context.Attendances;
        public DbSet<Payment> Payments => _context.Payments;
        public DbSet<Inquiry> Inquiries => _context.Inquiries;
        public DbSet<Feedback> Feedbacks => _context.Feedbacks;
        public DbSet<Subscription> Subscriptions => _context.Subscriptions;
        public DbSet<UserAccount> UserAccounts => _context.UserAccounts;
        public DbSet<RetentionRecord> RetentionRecords => _context.RetentionRecords;

        public DbSet<Branch> Branches =>
            throw new NotSupportedException("Branches do not exist in the legacy Tenant A / Tenant B schema.");

        // Phase 2: complaints exist in the legacy schema too - as a plain, NON-branching
        // table (the entity's BranchId/Branch stay unmapped by TenantAlphaEliteDbContext).
        public DbSet<Complaint> Complaints => _context.Complaints;

        public bool IsBranchAware => false;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            _context.SaveChangesAsync(cancellationToken);

        public void Dispose() => _context.Dispose();
        public ValueTask DisposeAsync() => _context.DisposeAsync();
    }

    /// <summary>Wraps the branch-aware context used by Tenant C.</summary>
    public sealed class BranchAwareTenantDataContext : ITenantDataContext
    {
        private readonly TenantCAlphaEliteDbContext _context;

        public BranchAwareTenantDataContext(TenantCAlphaEliteDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public DbSet<Member> Members => _context.Members;
        public DbSet<Attendance> Attendances => _context.Attendances;
        public DbSet<Payment> Payments => _context.Payments;
        public DbSet<Inquiry> Inquiries => _context.Inquiries;
        public DbSet<Feedback> Feedbacks => _context.Feedbacks;
        public DbSet<Subscription> Subscriptions => _context.Subscriptions;
        public DbSet<UserAccount> UserAccounts => _context.UserAccounts;
        public DbSet<RetentionRecord> RetentionRecords => _context.RetentionRecords;
        public DbSet<Branch> Branches => _context.Branches;
        public DbSet<Complaint> Complaints => _context.Complaints;

        public bool IsBranchAware => true;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            _context.SaveChangesAsync(cancellationToken);

        public void Dispose() => _context.Dispose();
        public ValueTask DisposeAsync() => _context.DisposeAsync();
    }

    public interface ITenantDataContextFactory
    {
        /// <summary>
        /// Opens the context that matches the tenant's schema. <paramref name="isBranchAware"/>
        /// comes from the master database tenant metadata, never from a hardcoded id.
        /// </summary>
        Task<ITenantDataContext> CreateAsync(int companyId, bool isBranchAware);
    }

    /// <summary>Chooses between the legacy and branch-aware contexts per tenant.</summary>
    public sealed class TenantDataContextFactory : ITenantDataContextFactory
    {
        private readonly ITenantDbContextFactory _legacyFactory;
        private readonly ITenantCDbContextFactory _branchAwareFactory;

        public TenantDataContextFactory(
            ITenantDbContextFactory legacyFactory,
            ITenantCDbContextFactory branchAwareFactory)
        {
            _legacyFactory = legacyFactory ?? throw new ArgumentNullException(nameof(legacyFactory));
            _branchAwareFactory = branchAwareFactory ?? throw new ArgumentNullException(nameof(branchAwareFactory));
        }

        public async Task<ITenantDataContext> CreateAsync(int companyId, bool isBranchAware)
        {
            if (isBranchAware)
            {
                var branchAware = await _branchAwareFactory.CreateAsync(companyId);
                return new BranchAwareTenantDataContext(branchAware);
            }

            var legacy = await _legacyFactory.CreateAsync(companyId);
            return new LegacyTenantDataContext(legacy);
        }
    }
}
