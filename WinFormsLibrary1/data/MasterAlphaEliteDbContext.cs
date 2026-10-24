using AlphaElite.domain.entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace AlphaElite.infrastructure.data
{
    public class MasterAlphaEliteDbContext : IdentityDbContext
    {
        public MasterAlphaEliteDbContext(
            DbContextOptions<MasterAlphaEliteDbContext> options ) : base( options ) 
        { 
        }
        public DbSet<Company> Companies => Set<Company>();

        public DbSet<CompanyDatabase> CompanyDatabase => Set<CompanyDatabase>();
        public DbSet<Device> Devices { get; set; }

        // Phase 9: Alpha Elite SOFTWARE subscription / module management (master DB only).
        public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
        public DbSet<SoftwareModule> SoftwareModules => Set<SoftwareModule>();
        public DbSet<PlanModule> PlanModules => Set<PlanModule>();
        public DbSet<CompanySubscription> CompanySubscriptions => Set<CompanySubscription>();

        // Subscription-request phase (OPTION 2): approval workflow + audit trail.
        public DbSet<SubscriptionHistory> SubscriptionHistories => Set<SubscriptionHistory>();
        public DbSet<SubscriptionChangeRequest> SubscriptionChangeRequests => Set<SubscriptionChangeRequest>();

        // Plan-configuration versioning: immutable per-version snapshots of a plan's
        // Terms & Agreement / price / billing cycle AND its exact module set.
        public DbSet<SubscriptionPlanVersion> SubscriptionPlanVersions => Set<SubscriptionPlanVersion>();
        public DbSet<SubscriptionPlanVersionModule> SubscriptionPlanVersionModules => Set<SubscriptionPlanVersionModule>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Company>(entity =>
            {
                entity.HasKey(x => x.CompanyId);
                entity.Property(x => x.CompanyCode).HasMaxLength(50).IsRequired();
                entity.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
                entity.HasIndex(x => x.CompanyCode).IsUnique();
            });

            builder.Entity<CompanyDatabase>(entity =>
            {
                entity.HasKey(x => x.CompanyDatabaseId);
                entity.Property(x => x.ServerName).HasMaxLength(200).IsRequired();
                entity.Property(x => x.DatabaseName).HasMaxLength(200).IsRequired();
                entity.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            });

            // Phase 9: software subscription / module management. Stable codes are the
            // identity for plans and modules (seeding never relies on identity ids).
            builder.Entity<SubscriptionPlan>(entity =>
            {
                entity.HasKey(x => x.SubscriptionPlanId);
                entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(400);
                entity.Property(x => x.BillingCycle).HasMaxLength(20).IsRequired();
                entity.HasIndex(x => x.Code).IsUnique();
            });

            builder.Entity<SoftwareModule>(entity =>
            {
                entity.HasKey(x => x.ModuleId);
                entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
                entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(400);
                entity.HasIndex(x => x.Code).IsUnique();
            });

            builder.Entity<PlanModule>(entity =>
            {
                entity.HasKey(x => new { x.SubscriptionPlanId, x.ModuleId });

                entity.HasOne(x => x.SubscriptionPlan)
                    .WithMany(x => x.PlanModules)
                    .HasForeignKey(x => x.SubscriptionPlanId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Module)
                    .WithMany()
                    .HasForeignKey(x => x.ModuleId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<CompanySubscription>(entity =>
            {
                entity.HasKey(x => x.CompanySubscriptionId);
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.PaymentStatus).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Notes).HasMaxLength(400);

                entity.HasOne(x => x.Company)
                    .WithMany()
                    .HasForeignKey(x => x.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.SubscriptionPlan)
                    .WithMany()
                    .HasForeignKey(x => x.SubscriptionPlanId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Plan-version association (additive, nullable). Deleting a plan version never
                // deletes a subscription: the pointer is simply cleared (SetNull).
                entity.HasOne(x => x.SubscriptionPlanVersion)
                    .WithMany()
                    .HasForeignKey(x => x.SubscriptionPlanVersionId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(x => new { x.CompanyId, x.Status });
                entity.HasIndex(x => x.SubscriptionPlanVersionId);
            });

            builder.Entity<SubscriptionPlanVersion>(entity =>
            {
                entity.HasKey(x => x.SubscriptionPlanVersionId);
                entity.Property(x => x.PlanCode).HasMaxLength(50).IsRequired();
                entity.Property(x => x.PlanName).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Description).HasMaxLength(400);
                entity.Property(x => x.BillingCycle).HasMaxLength(20).IsRequired();
                entity.Property(x => x.ChangeType).HasMaxLength(30).IsRequired();
                entity.Property(x => x.ChangeSummary).HasMaxLength(400);
                entity.Property(x => x.ChangedBy).HasMaxLength(100).IsRequired();

                // Version numbers are sequential PER PLAN - never a global sequence.
                entity.HasIndex(x => new { x.SubscriptionPlanId, x.VersionNumber }).IsUnique();
                entity.HasIndex(x => new { x.SubscriptionPlanId, x.IsCurrent });

                // A plan row is never removed by the application (deactivation only). Cascade is
                // used so that if a plan row IS removed (verification cleanup of a temporary
                // plan) its snapshots go with it instead of blocking the delete - no plan EDIT
                // or module change ever deletes a version.
                entity.HasOne(x => x.SubscriptionPlan)
                    .WithMany()
                    .HasForeignKey(x => x.SubscriptionPlanId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<SubscriptionPlanVersionModule>(entity =>
            {
                // Snapshot rows belong to their version: if a version row were ever removed
                // (only by removing its plan) its own snapshot rows go with it. Nothing in the
                // application deletes versions or their module snapshots.
                entity.HasKey(x => new { x.SubscriptionPlanVersionId, x.ModuleId });
                entity.Property(x => x.ModuleCode).HasMaxLength(50).IsRequired();
                entity.Property(x => x.ModuleName).HasMaxLength(100).IsRequired();

                entity.HasOne(x => x.Version)
                    .WithMany(x => x.Modules)
                    .HasForeignKey(x => x.SubscriptionPlanVersionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Module)
                    .WithMany()
                    .HasForeignKey(x => x.ModuleId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<SubscriptionHistory>(entity =>
            {
                // Singular table name per the approved schema (EF would otherwise pluralize
                // the DbSet name). Matches the AddSubscriptionRequestsAndHistory migration.
                entity.ToTable("SubscriptionHistory");
                entity.HasKey(x => x.HistoryId);
                entity.Property(x => x.Action).HasMaxLength(30).IsRequired();
                entity.Property(x => x.ChangedBy).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Reason).HasMaxLength(400);
                entity.Property(x => x.Notes).HasMaxLength(400);

                entity.HasOne(x => x.Company)
                    .WithMany()
                    .HasForeignKey(x => x.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Plan links are informational (nullable audit references); no cascade.
                entity.HasOne<SubscriptionPlan>()
                    .WithMany()
                    .HasForeignKey(x => x.PreviousPlanId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<SubscriptionPlan>()
                    .WithMany()
                    .HasForeignKey(x => x.NewPlanId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.CompanyId, x.ChangedAt });
            });

            builder.Entity<SubscriptionChangeRequest>(entity =>
            {
                // Singular table name per the approved schema (EF would otherwise pluralize
                // the DbSet name). Matches the AddSubscriptionRequestsAndHistory migration.
                entity.ToTable("SubscriptionChangeRequest");
                entity.HasKey(x => x.SubscriptionChangeRequestId);
                entity.Property(x => x.RequestType).HasMaxLength(20).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
                entity.Property(x => x.RequestedBy).HasMaxLength(100).IsRequired();
                entity.Property(x => x.ReviewedBy).HasMaxLength(100);
                entity.Property(x => x.ReviewNotes).HasMaxLength(400);

                entity.HasOne(x => x.Company)
                    .WithMany()
                    .HasForeignKey(x => x.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Plan references are nullable and informational; no cascade.
                entity.HasOne<SubscriptionPlan>()
                    .WithMany()
                    .HasForeignKey(x => x.CurrentPlanId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<SubscriptionPlan>()
                    .WithMany()
                    .HasForeignKey(x => x.RequestedPlanId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Tenants see their own company's requests; Super Admin screens Pending first.
                entity.HasIndex(x => new { x.CompanyId, x.Status });
                entity.HasIndex(x => x.Status);
            });

            builder.Entity<Device>(entity =>

            {

                entity.HasKey(x => x.DeviceId);



                entity.Property(x => x.DeviceCode)

                  .HasMaxLength(50)

                  .IsRequired();



                entity.Property(x => x.DeviceName)

                  .HasMaxLength(200)

                  .IsRequired();



                entity.HasOne(x => x.Company)

                  .WithMany(x => x.Devices)

                  .HasForeignKey(x => x.CompanyId)

                  .OnDelete(DeleteBehavior.Restrict);



                entity.HasIndex(x => new { x.CompanyId, x.DeviceCode })

                  .IsUnique();

            });
        }
    }
}
