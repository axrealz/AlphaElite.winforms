using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Tenant C deployment operations. Every operation resolves its target from the master
    /// database plus configuration (never a hardcoded id), uses the branch-aware context only
    /// (<see cref="TenantCAlphaEliteDbContext"/>) and prints the exact target database before
    /// touching anything.
    ///
    /// Modes:
    ///   --tenantc-migrate : applies pending EF migrations for the Tenant C context (EF's own
    ///                       migration mechanism; no CLI, no hand-pasted DDL).
    ///   --tenantc-seed    : idempotently inserts the approved branches BR-A / BR-B.
    ///   --tenantc-verify  : read-only schema/connectivity verification.
    /// </summary>
    internal static class TenantCDeployment
    {
        private sealed record BranchSeed(string Code, string Name);

        private static readonly BranchSeed[] ApprovedBranches =
        {
            // Phase 8: BR-A / BR-B were renamed in place to BR-MAIN / BR-CEBU by the
            // AddBranchManagement migration; the seeder only guarantees the three
            // approved Phase 8 branches exist (never renames or duplicates rows).
            new("BR-MAIN", "AlphaElite Manila - Head Office"),
            new("BR-CEBU", "AlphaElite Cebu Branch"),
            new("BR-DAVAO", "AlphaElite Davao Branch")
        };

        public static int Migrate() => Run("migrate");

        public static int SeedBranches() => Run("seed");

        public static int Verify() => Run("verify");

        private static int Run(string operation)
        {
            var log = new List<string>();
            var failures = new List<string>();

            void Write(string text)
            {
                log.Add(text);
                Console.WriteLine(text);
            }

            var exitCode = 0;

            try
            {
                var runtime = AppRuntime.Create();

                // Target = the tenant marked branch-aware in configuration, resolved through the
                // master database. Tenant A / Tenant B / Master can never be selected here.
                var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();
                var tenant = tenants.FirstOrDefault(t => t.IsBranchAware);

                if (tenant == null)
                {
                    Write("Aborted: no branch-aware tenant is configured (TenantSchema:BranchAwareCredentialKeys).");
                    return 1;
                }

                var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();

                Write($"Operation      : {operation}");
                Write($"Target tenant  : {tenant.CompanyCode} / {tenant.CompanyName} (CompanyId {tenant.CompanyId})");
                Write($"Target database: {databaseInfo.ServerName} / {databaseInfo.DatabaseName} (credential key '{databaseInfo.CredentialKey}')");
                Write($"Context        : {nameof(TenantCAlphaEliteDbContext)}");
                Write(string.Empty);

                var options = runtime.ConnectionFactory.CreateBranchAwareOptions(databaseInfo);
                using var context = new TenantCAlphaEliteDbContext(options);

                switch (operation)
                {
                    case "migrate":
                        exitCode = ApplyMigrations(context, Write);
                        break;

                    case "seed":
                        exitCode = SeedBranches(context, Write);
                        break;

                    case "verify":
                        exitCode = VerifySchema(context, Write, failures);
                        break;

                    default:
                        Write($"Unknown operation '{operation}'.");
                        exitCode = 2;
                        break;
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{ex.GetType().Name}: {ex.Message}");
                Write($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                exitCode = 1;
            }

            Write(string.Empty);
            Write($"--- Result: {(exitCode == 0 ? "OK" : "PROBLEMS")} | failures: {failures.Count} ---");
            foreach (var failure in failures)
            {
                Write($"        - {failure}");
            }

            try
            {
                var reportPath = Path.Combine(AppContext.BaseDirectory, $"tenantc-{operation}-report.txt");
                File.WriteAllLines(reportPath, log);
                Console.WriteLine($"Report written to: {reportPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not write the report file: {ex.Message}");
            }

            return exitCode;
        }

        private static int ApplyMigrations(TenantCAlphaEliteDbContext context, Action<string> write)
        {
            var applied = context.Database.GetAppliedMigrations().ToList();
            var pending = context.Database.GetPendingMigrations().ToList();

            var tablesBefore = CountTables(context);
            write($"Tables in target database before: {tablesBefore}");
            write($"Applied migrations before: {(applied.Count == 0 ? "(none)" : string.Join(", ", applied))}");
            write($"Pending migrations        : {(pending.Count == 0 ? "(none)" : string.Join(", ", pending))}");
            write(string.Empty);

            if (pending.Count == 0)
            {
                write("Nothing to apply: the Tenant C schema is already up to date.");
                return 0;
            }

            // Every pending migration of this context belongs to TenantCAlphaEliteDbContext
            // (EF filters by the [DbContext] attribute), which is why the standard tenant and
            // master migrations can never be applied here.
            write("Applying pending migrations to the target database listed above ...");
            write("WARNING: this changes ONLY the Tenant C database. Tenant A, Tenant B and the master database are not referenced.");
            context.Database.Migrate();
            write("Migration application completed.");

            var appliedAfter = context.Database.GetAppliedMigrations().ToList();
            var pendingAfter = context.Database.GetPendingMigrations().ToList();

            write(string.Empty);
            write($"Applied migrations after : {string.Join(", ", appliedAfter)}");
            write($"Pending migrations after : {(pendingAfter.Count == 0 ? "(none)" : string.Join(", ", pendingAfter))}");
            write($"Tables in target database after: {CountTables(context)}");

            return pendingAfter.Count == 0 ? 0 : 1;
        }

        private static int SeedBranches(TenantCAlphaEliteDbContext context, Action<string> write)
        {
            if (!context.Database.GetAppliedMigrations().Any())
            {
                write("Aborted: the Tenant C schema has not been applied yet. Run --tenantc-migrate first.");
                return 1;
            }

            write("Branches before:");

            foreach (var branch in context.Branches.AsNoTracking().OrderBy(b => b.BranchId).ToList())
            {
                write($"        BranchId {branch.BranchId} | {branch.BranchCode} | {branch.BranchName} | active: {branch.IsActive}");
            }

            var inserted = 0;

            foreach (var seed in ApprovedBranches)
            {
                var exists = context.Branches.Any(b => b.BranchCode == seed.Code);

                if (exists)
                {
                    write($"Skipping {seed.Code}: already present (existing rows are never modified).");
                    continue;
                }

                context.Branches.Add(new Branch
                {
                    BranchCode = seed.Code,
                    BranchName = seed.Name,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

                inserted++;
            }

            if (inserted > 0)
            {
                context.SaveChanges();
            }

            write(string.Empty);
            write($"Branches inserted: {inserted}");
            write("Branches after:");

            foreach (var branch in context.Branches.AsNoTracking().OrderBy(b => b.BranchId).ToList())
            {
                write($"        BranchId {branch.BranchId} | {branch.BranchCode} | {branch.BranchName} | active: {branch.IsActive}");
            }

            return 0;
        }

        private static int VerifySchema(TenantCAlphaEliteDbContext context, Action<string> write, List<string> failures)
        {
            var applied = context.Database.GetAppliedMigrations().ToList();
            write($"Applied migrations: {(applied.Count == 0 ? "(none)" : string.Join(", ", applied))}");

            var expectedTables = new[]
            {
                "Attendances", "Branches", "Complaints", "Feedbacks", "Inquiries",
                "Members", "Payments", "RetentionRecords", "Subscriptions", "UserAccounts"
            };

            write($"Table count in target database: {CountTables(context)}");

            // Each projection selects every mapped column, so a missing or renamed column in the
            // database surfaces as a SQL error instead of being silently hidden.
            Check(write, failures, "Members", () => context.Members.AsNoTracking().Select(m => new
            {
                m.MemberId, m.MemberCode, m.FullName, m.Email, m.IsActive, m.CreatedAt, m.BranchId
            }).FirstOrDefault());

            Check(write, failures, "Branches", () => context.Branches.AsNoTracking().Select(b => new
            {
                b.BranchId, b.BranchCode, b.BranchName, b.Address, b.ContactNumber, b.City,
                b.ManagerUserAccountId, b.IsActive, b.CreatedAt, b.UpdatedAt
            }).ToList());

            Check(write, failures, "UserAccounts", () => context.UserAccounts.AsNoTracking().Select(u => new
            {
                u.UserAccountId, u.Username, u.PasswordHash, u.FullName, u.Role, u.IsActive, u.CreatedAt, u.MemberId, u.BranchId
            }).FirstOrDefault());

            Check(write, failures, "Attendances", () => context.Attendances.AsNoTracking().Select(a => new
            {
                a.AttendanceId, a.MemberId, a.CheckInDate, a.BranchId
            }).FirstOrDefault());

            Check(write, failures, "Payments", () => context.Payments.AsNoTracking().Select(p => new
            {
                p.PaymentId, p.MemberId, p.AmountPaid, p.PaymentDate, p.NextDueDate, p.Notes, p.BranchId
            }).FirstOrDefault());

            Check(write, failures, "Feedbacks", () => context.Feedbacks.AsNoTracking().Select(f => new
            {
                f.FeedbackId, f.MemberId, f.Rating, f.Comments, f.SubmittedAt, f.BranchId
            }).FirstOrDefault());

            Check(write, failures, "Inquiries", () => context.Inquiries.AsNoTracking().Select(i => new
            {
                i.InquiryId, i.FullName, i.ContactNumber, i.EmailAddress, i.InterestedPlan, i.Message,
                i.IsFollowedUp, i.CreatedAt, i.BranchId
            }).FirstOrDefault());

            Check(write, failures, "Subscriptions", () => context.Subscriptions.AsNoTracking().Select(s => new
            {
                s.SubscriptionId, s.MemberId, s.PlanName, s.Price, s.BillingCycle, s.StartDate, s.EndDate, s.IsActive, s.BranchId
            }).FirstOrDefault());

            Check(write, failures, "RetentionRecords", () => context.RetentionRecords.AsNoTracking().Select(r => new
            {
                r.RetentionRecordId, r.MemberId, r.Reason, r.RetentionAction, r.Promotion, r.PromotionDetails,
                r.Notes, r.CreatedAt, r.CreatedBy, r.Status, r.ApprovedBy, r.ApprovedAt, r.ApprovalReason,
                r.RejectionReason, r.CompletedAt, r.CompletionNotes, r.AssignedStaff, r.BranchId
            }).FirstOrDefault());

            Check(write, failures, "Complaints", () => context.Complaints.AsNoTracking().Select(c => new
            {
                c.ComplaintId, c.MemberId, c.BranchId, c.Subject, c.Description, c.Status, c.CreatedAt,
                c.ResolvedAt, c.ResolutionNotes
            }).FirstOrDefault());

            write(string.Empty);
            write("Row counts:");
            write($"        Members {context.Members.Count()} | Branches {context.Branches.Count()} | "
                + $"UserAccounts {context.UserAccounts.Count()} | Attendances {context.Attendances.Count()} | "
                + $"Payments {context.Payments.Count()} | Feedbacks {context.Feedbacks.Count()} | "
                + $"Inquiries {context.Inquiries.Count()} | Subscriptions {context.Subscriptions.Count()} | "
                + $"RetentionRecords {context.RetentionRecords.Count()} | Complaints {context.Complaints.Count()}");

            write(string.Empty);
            write("Branches in Tenant C:");
            foreach (var branch in context.Branches.AsNoTracking().OrderBy(b => b.BranchCode).ToList())
            {
                write($"        BranchId {branch.BranchId} | {branch.BranchCode} | {branch.BranchName} | active: {branch.IsActive}");
            }

            var presentTables = context.Database
                .SqlQueryRaw<string>("SELECT name AS [Value] FROM sys.tables WHERE name NOT LIKE '\\_\\_EF%' ESCAPE '\\' ORDER BY name")
                .ToList();

            var unexpected = presentTables.Except(expectedTables, StringComparer.OrdinalIgnoreCase).ToList();

            if (unexpected.Count > 0)
            {
                failures.Add($"unexpected tables: {string.Join(", ", unexpected)}");
                write($"  [FAIL] unexpected tables present: {string.Join(", ", unexpected)}");
            }
            else
            {
                write($"  [PASS] exactly the expected {expectedTables.Length} application tables are present.");
            }

            return failures.Count == 0 ? 0 : 1;
        }

        private static void Check<T>(Action<string> write, List<string> failures, string table, Func<T> query)
        {
            try
            {
                var result = query();

                if (result is System.Collections.ICollection collection)
                {
                    write($"  [PASS] {table}: query over all mapped columns succeeded ({collection.Count} row(s)).");
                }
                else
                {
                    write($"  [PASS] {table}: query over all mapped columns succeeded ({(result == null ? "no rows" : "1 row")}).");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{table}: {ex.GetType().Name}: {ex.Message}");
                write($"  [FAIL] {table}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static int CountTables(TenantCAlphaEliteDbContext context)
        {
            return context.Database
                .SqlQueryRaw<int>("SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables")
                .First();
        }
    }
}
