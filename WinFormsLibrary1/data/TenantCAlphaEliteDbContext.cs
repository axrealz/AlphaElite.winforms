using System;
using AlphaElite.domain.entities;
using Microsoft.EntityFrameworkCore;

namespace AlphaElite.infrastructure.data
{
    // Tenant C: multi-branch version. Shares the same entity classes as
    // TenantAlphaEliteDbContext (Tenant B), but this context additionally
    // maps BranchId columns and the new Branch/Complaint entities.
    // Tenant B's context (TenantAlphaEliteDbContext) explicitly Ignore()s
    // these same properties, so the two contexts never conflict.
    public class TenantCAlphaEliteDbContext : DbContext
    {
        public TenantCAlphaEliteDbContext(
            DbContextOptions<TenantCAlphaEliteDbContext> options)
            : base(options)
        {
        }

        public DbSet<Member> Members => Set<Member>();
        public DbSet<Attendance> Attendances => Set<Attendance>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Inquiry> Inquiries => Set<Inquiry>();
        public DbSet<Feedback> Feedbacks => Set<Feedback>();
        public DbSet<Subscription> Subscriptions => Set<Subscription>();
        public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
        public DbSet<RetentionRecord> RetentionRecords => Set<RetentionRecord>();
        public DbSet<Branch> Branches => Set<Branch>();
        public DbSet<Complaint> Complaints => Set<Complaint>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Member>(entity =>
            {
                entity.HasKey(x => x.MemberId);

                entity.Property(x => x.MemberCode)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(x => x.FullName)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(x => x.Email)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.HasIndex(x => x.MemberCode)
                    .IsUnique();

                entity.HasOne<Branch>()
                    .WithMany()
                    .HasForeignKey(x => x.BranchId)
                    .IsRequired(false);
            });

            builder.Entity<Attendance>()
                .HasOne(x => x.Member)
                .WithMany()
                .HasForeignKey(x => x.MemberId);

            builder.Entity<Attendance>()
                .HasOne<Branch>()
                .WithMany()
                .HasForeignKey(x => x.BranchId)
                .IsRequired(false);

            builder.Entity<Payment>()
                .HasOne(x => x.Member)
                .WithMany()
                .HasForeignKey(x => x.MemberId);

            builder.Entity<Payment>()
                .Property(x => x.AmountPaid)
                .HasPrecision(18, 2);

            builder.Entity<Payment>()
                .HasOne<Branch>()
                .WithMany()
                .HasForeignKey(x => x.BranchId)
                .IsRequired(false);

            builder.Entity<Inquiry>()
                .HasOne<Branch>()
                .WithMany()
                .HasForeignKey(x => x.BranchId)
                .IsRequired(false);

            builder.Entity<Feedback>()
                .HasOne(x => x.Member)
                .WithMany()
                .HasForeignKey(x => x.MemberId);

            builder.Entity<Feedback>()
                .HasOne<Branch>()
                .WithMany()
                .HasForeignKey(x => x.BranchId)
                .IsRequired(false);

            builder.Entity<Subscription>()
                .HasOne(x => x.Member)
                .WithMany()
                .HasForeignKey(x => x.MemberId);

            // Phase 8: subscriptions are branch-scoped too (nullable + backfill).
            builder.Entity<Subscription>()
                .HasOne<Branch>()
                .WithMany()
                .HasForeignKey(x => x.BranchId)
                .IsRequired(false);

            builder.Entity<Subscription>()
                .Property(x => x.Price)
                .HasPrecision(18, 2);

            builder.Entity<UserAccount>(entity =>
            {
                entity.HasKey(x => x.UserAccountId);

                entity.Property(x => x.Username)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.HasIndex(x => x.Username)
                    .IsUnique();

                entity.HasOne(x => x.Member)
                    .WithMany()
                    .HasForeignKey(x => x.MemberId)
                    .IsRequired(false);

                entity.HasOne<Branch>()
                    .WithMany()
                    .HasForeignKey(x => x.BranchId)
                    .IsRequired(false);
            });

            builder.Entity<RetentionRecord>()
                .HasOne(x => x.Member)
                .WithMany()
                .HasForeignKey(x => x.MemberId);

            builder.Entity<RetentionRecord>()
                .HasOne<Branch>()
                .WithMany()
                .HasForeignKey(x => x.BranchId)
                .IsRequired(false);

            builder.Entity<Branch>(entity =>
            {
                entity.HasKey(x => x.BranchId);

                entity.Property(x => x.BranchCode)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.BranchName)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(x => x.City)
                    .HasMaxLength(100);

                entity.HasIndex(x => x.BranchCode)
                    .IsUnique();

                // Phase 8: branch manager references an EXISTING UserAccount (no separate
                // credentials). Nullable = "Unassigned". Deleting the user account would
                // break the reference, so SQL Server refuses it (NO ACTION).
                entity.HasOne(x => x.Manager)
                    .WithMany()
                    .HasForeignKey(x => x.ManagerUserAccountId)
                    .IsRequired(false);
            });

            builder.Entity<Complaint>(entity =>
            {
                entity.HasKey(x => x.ComplaintId);

                entity.Property(x => x.Subject)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.HasOne(x => x.Member)
                    .WithMany()
                    .HasForeignKey(x => x.MemberId);

                entity.HasOne(x => x.Branch)
                    .WithMany()
                    .HasForeignKey(x => x.BranchId)
                    .IsRequired(false);

                // Tenant A Complaints CRUD phase: the shared Complaint entity gained
                // Category / Priority / StaffAssigned. Tenant C's table keeps its original
                // schema - IGNORE the new properties here so every C query and insert is
                // byte-for-byte the same as before (no Tenant C schema change).
                entity.Ignore(x => x.Category);
                entity.Ignore(x => x.Priority);
                entity.Ignore(x => x.StaffAssigned);
            });
        }
    }
}