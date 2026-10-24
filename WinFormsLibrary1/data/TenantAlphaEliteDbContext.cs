using AlphaElite.domain.entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.infrastructure.data
{
    public class TenantAlphaEliteDbContext : DbContext
    {
        public TenantAlphaEliteDbContext(
        DbContextOptions<TenantAlphaEliteDbContext> options)
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

        // Phase 2: complaints are now a normal tenant module for Tenant A / Tenant B too
        // (no branching: the legacy table simply has no BranchId column).
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

            });

            builder.Entity<Attendance>()
                        .HasOne(x => x.Member)
                        .WithMany()
                        .HasForeignKey(x => x.MemberId);

            builder.Entity<Payment>()
                        .HasOne(x => x.Member)
                        .WithMany()
                        .HasForeignKey(x => x.MemberId);

            builder.Entity<Payment>()
                        .Property(x => x.AmountPaid)
                        .HasPrecision(18, 2);

            builder.Entity<Feedback>()
                        .HasOne(x => x.Member)
                        .WithMany()
                        .HasForeignKey(x => x.MemberId);

            builder.Entity<Subscription>()
                        .HasOne(x => x.Member)
                        .WithMany()
                        .HasForeignKey(x => x.MemberId);

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
            });

            builder.Entity<RetentionRecord>()
                        .HasOne(x => x.Member)
                        .WithMany()
                        .HasForeignKey(x => x.MemberId);

            // ---- Tenant C-only properties: ignored here so Tenant B never
            // queries columns that don't exist in its database. ----
            builder.Entity<Member>().Ignore(x => x.BranchId);
            builder.Entity<Attendance>().Ignore(x => x.BranchId);
            builder.Entity<Payment>().Ignore(x => x.BranchId);
            builder.Entity<Inquiry>().Ignore(x => x.BranchId);
            builder.Entity<Feedback>().Ignore(x => x.BranchId);
            builder.Entity<RetentionRecord>().Ignore(x => x.BranchId);
            builder.Entity<RetentionRecord>().Ignore(x => x.AssignedStaff);
            builder.Entity<UserAccount>().Ignore(x => x.BranchId);
            builder.Entity<UserAccount>().Ignore(x => x.MemberId);
            builder.Entity<UserAccount>().Ignore(x => x.Member);

            // Phase 2 fix: Subscription.BranchId was added to the shared entity by the branch
            // phase but never ignored here, so every Subscriptions SELECT asked Tenant A/B for
            // a BranchId column their (non-branching) schema does not have - "Invalid column
            // name BranchId" in the Dashboard / Subscriptions / Retention modules. Subscriptions
            // in a legacy tenant are filtered by member set, never by branch.
            builder.Entity<Subscription>().Ignore(x => x.BranchId);

            // Phase 2: Complaints without any branching - no BranchId column, no Branch nav.
            builder.Entity<Complaint>(entity =>
            {
                entity.HasKey(x => x.ComplaintId);

                entity.Property(x => x.Subject)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.HasOne(x => x.Member)
                    .WithMany()
                    .HasForeignKey(x => x.MemberId);

                entity.Ignore(x => x.BranchId);
                entity.Ignore(x => x.Branch);

                // Complaint CRUD fields (Tenant A phase): mapped for BOTH legacy tenants
                // (Tenant A + Tenant B share this context), so both Complaints tables carry
                // these three nullable columns. CreatedAt (Date Submitted), ResolvedAt
                // (Date Resolved) and ResolutionNotes (Resolution / Response) are already
                // mapped by convention.
                entity.Property(x => x.Category).HasMaxLength(100);
                entity.Property(x => x.Priority).HasMaxLength(50);
                entity.Property(x => x.StaffAssigned).HasMaxLength(100);
            });
        }
    }
}