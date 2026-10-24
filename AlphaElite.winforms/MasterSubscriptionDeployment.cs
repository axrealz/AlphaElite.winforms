using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 9 master-DB deployment. Every operation prints its exact target before touching
    /// anything and refuses to run against any database other than the configured master
    /// (ConnectionStrings:MasterErp - db67598).
    ///
    /// Modes:
    ///   --master-migrate : applies pending MASTER context migrations via EF's own mechanism
    ///                      (identical to --tenantc-migrate, but for MasterAlphaEliteDbContext).
    ///   --master-seed-subs : idempotently seeds the 3 plans / 16 modules / PlanModule mappings
    ///                      and reconciles the 3 demo company subscriptions (one active row each).
    ///   --master-verify-subs : read-only verification of the seeded subscription schema/data.
    /// </summary>
    internal static class MasterSubscriptionDeployment
    {
        public static int Migrate() => Run("migrate");

        public static int Seed() => Run("seed");

        public static int Verify() => Run("verify");

        /// <summary>Read-only live schema/data inspection (Phase 2 preflight). Never writes.</summary>
        public static int InspectSchema() => Run("inspect-schema");

        /// <summary>Read-only diff between the migrations snapshot model and the live CLR model.</summary>
        public static int InspectModelDiff() => Run("model-diff");

        private static int Run(string operation)
        {
            var log = new List<string>();
            var failures = new List<string>();

            void Write(string text)
            {
                log.Add(text);
                Console.WriteLine(text);
            }

            try
            {
                var runtime = AppRuntime.Create();

                // Guard: the master context must point at the configured master database.
                var masterConnection = runtime.MasterDb.Database.GetDbConnection();
                var masterDatabase = masterConnection.Database;

                Write($"Operation      : {operation}");
                Write($"Target database: {masterDatabase} (ConnectionStrings:MasterErp)");
                Write($"Context        : {nameof(MasterAlphaEliteDbContext)}");

                if (!masterDatabase.Contains("67598"))
                {
                    Write($"[ABORT] Refusing to run: the master connection does not point at db67598 (got '{masterDatabase}').");
                    return 1;
                }

                switch (operation)
                {
                    case "migrate":
                        return ApplyMigrations(runtime, Write);
                    case "seed":
                        return SeedSubscriptions(runtime, Write, failures);
                    case "verify":
                        return VerifySubscriptions(runtime, Write, failures);
                    case "inspect-schema":
                        return InspectMasterSchema(runtime, Write);
                    case "model-diff":
                        return InspectModelDifferences(runtime, Write);
                    default:
                        Write($"Unknown operation '{operation}'.");
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Write($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        private static int ApplyMigrations(AppRuntime runtime, Action<string> write)
        {
            var context = runtime.MasterDb;

            var applied = context.Database.GetAppliedMigrations().ToList();
            var pending = context.Database.GetPendingMigrations().ToList();

            write($"Tables before        : {CountTables(context)}");
            write($"Applied migrations   : {(applied.Count == 0 ? "(none)" : string.Join(", ", applied))}");
            write($"Pending migrations   : {(pending.Count == 0 ? "(none)" : string.Join(", ", pending))}");
            write(string.Empty);

            if (pending.Count == 0)
            {
                write("Nothing to apply: the master schema is already up to date.");
                return 0;
            }

            // Safety: the only pending migration this phase adds is AddSubscriptionManagement.
            // Anything else pending is listed loudly before the user runs the command.
            write("Applying pending migrations to the MASTER database listed above ...");
            write("Additive only: existing Companies / CompanyDatabase / Devices / Identity rows are never modified.");
            context.Database.Migrate();
            write("Migration application completed.");

            var appliedAfter = context.Database.GetAppliedMigrations().ToList();
            var pendingAfter = context.Database.GetPendingMigrations().ToList();

            write(string.Empty);
            write($"Applied migrations after: {string.Join(", ", appliedAfter)}");
            write($"Pending migrations after: {(pendingAfter.Count == 0 ? "(none)" : string.Join(", ", pendingAfter))}");
            write($"Tables after            : {CountTables(context)}");

            return pendingAfter.Count == 0 ? 0 : 1;
        }

        private static int SeedSubscriptions(AppRuntime runtime, Action<string> write, List<string> failures)
        {
            var context = runtime.MasterDb;
            var utcNow = DateTime.UtcNow;

            // ---- 1. Plans (idempotent by stable Code) ----
            var planDefinitions = new (string Code, string Name, string Description)[]
            {
                (SoftwarePlanCodes.Basic, "Basic",
                    "Entry plan: Dashboard, Members, Attendance, Payments, Subscriptions, Inquiries, Feedback, Complaints."),
                (SoftwarePlanCodes.Professional, "Professional",
                    "Growth plan: Basic plus Feedback, Complaints, Retention, Reports, BI, User Management."),
                (SoftwarePlanCodes.Enterprise, "Enterprise",
                    "Full plan: Professional plus Branch Management, Member Portal, Advanced BI, Advanced Retention.")
            };

            var plans = context.SubscriptionPlans.ToDictionary(p => p.Code, p => p);

            foreach (var (code, name, description) in planDefinitions)
            {
                if (plans.TryGetValue(code, out var existing))
                {
                    // Phase 2: reconcile the description when the plan's module set changed.
                    if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
                    {
                        existing.Description = description;
                        existing.UpdatedAt = utcNow;
                        context.SaveChanges();
                        write($"Plan {code}: exists (SubscriptionPlanId {existing.SubscriptionPlanId}) - description reconciled.");
                    }
                    else
                    {
                        write($"Plan {code}: exists (SubscriptionPlanId {existing.SubscriptionPlanId}) - unchanged.");
                    }

                    continue;
                }

                var plan = new SubscriptionPlan
                {
                    Code = code,
                    Name = name,
                    Description = description,
                    IsActive = true,
                    CreatedAt = utcNow,
                    UpdatedAt = utcNow
                };
                context.SubscriptionPlans.Add(plan);
                plans[code] = plan;
                write($"Plan {code}: inserted.");
            }

            // ---- 2. Modules (idempotent by stable Code) ----
            var moduleDefinitions = new (string Code, string Name, string Description)[]
            {
                (SoftwareModuleCodes.Dashboard, "Dashboard", "KPIs and charts."),
                (SoftwareModuleCodes.Members, "Members", "Member management."),
                (SoftwareModuleCodes.Attendance, "Attendance", "Check-in tracking."),
                (SoftwareModuleCodes.Payments, "Payments", "Payment records."),
                (SoftwareModuleCodes.Subscriptions, "Subscriptions", "Member membership subscriptions."),
                (SoftwareModuleCodes.Inquiries, "Inquiries", "Prospect inquiries."),
                (SoftwareModuleCodes.Feedback, "Feedback", "Member feedback."),
                (SoftwareModuleCodes.Complaints, "Complaints", "Complaint handling."),
                (SoftwareModuleCodes.Retention, "Retention", "Retention / at-risk members."),
                (SoftwareModuleCodes.Reports, "Reports", "PDF exports."),
                (SoftwareModuleCodes.BusinessIntelligence, "Business Intelligence", "Dashboard BI charts."),
                (SoftwareModuleCodes.UserManagement, "User Management", "RBAC user administration."),
                (SoftwareModuleCodes.BranchManagement, "Branch Management", "Branch CRUD and managers."),
                (SoftwareModuleCodes.MemberPortal, "Member Portal", "Member self-service."),
                (SoftwareModuleCodes.AdvancedBi, "Advanced BI", "Branch comparison."),
                (SoftwareModuleCodes.AdvancedRetention, "Advanced Retention", "Advanced retention tooling.")
            };

            var modules = context.SoftwareModules.ToDictionary(m => m.Code, m => m);

            foreach (var (code, name, description) in moduleDefinitions)
            {
                if (modules.TryGetValue(code, out var existing))
                {
                    write($"Module {code}: exists (ModuleId {existing.ModuleId}) - unchanged.");
                    continue;
                }

                var module = new SoftwareModule
                {
                    Code = code,
                    Name = name,
                    Description = description,
                    IsActive = true,
                    CreatedAt = utcNow,
                    UpdatedAt = utcNow
                };
                context.SoftwareModules.Add(module);
                modules[code] = module;
                write($"Module {code}: inserted.");
            }

            context.SaveChanges();

            // ---- 3. PlanModule mappings (idempotent composite key; missing rows are added) ----
            var matrix = SubscriptionService.GetPlanModuleMatrix();
            var mappingsChanged = false;

            foreach (var (planCode, moduleCodes) in matrix)
            {
                var plan = plans[planCode];

                var existingIds = context.PlanModules
                    .Where(pm => pm.SubscriptionPlanId == plan.SubscriptionPlanId)
                    .Select(pm => pm.ModuleId)
                    .ToList();

                foreach (var moduleCode in moduleCodes)
                {
                    var module = modules[moduleCode];

                    if (existingIds.Contains(module.ModuleId))
                    {
                        continue;
                    }

                    context.PlanModules.Add(new PlanModule
                    {
                        SubscriptionPlanId = plan.SubscriptionPlanId,
                        ModuleId = module.ModuleId,
                        IsActive = true,
                        CreatedAt = utcNow,
                        UpdatedAt = utcNow
                    });
                    existingIds.Add(module.ModuleId);
                    mappingsChanged = true;
                    write($"PlanModule {planCode} <- {moduleCode}: inserted.");
                }
            }

            if (mappingsChanged)
            {
                context.SaveChanges();
            }

            // ---- 4. Demo company subscriptions (reconciled: exactly ONE active row each) ----
            // CompanyId -> plan code. Companies are looked up by stable CompanyCode, never hardcoded ids.
            var demoAssignments = new (string CompanyCode, string PlanCode)[]
            {
                ("COMP001", SoftwarePlanCodes.Basic),
                ("COMP002", SoftwarePlanCodes.Professional),
                ("COMP003", SoftwarePlanCodes.Enterprise)
            };

            const int reconcileWindowDays = 2;
            var companyCodes = demoAssignments.Select(a => a.CompanyCode).ToArray();
            var companies = context.Companies
                .Where(c => companyCodes.Contains(c.CompanyCode))
                .ToDictionary(c => c.CompanyCode, c => c);

            foreach (var (companyCode, planCode) in demoAssignments)
            {
                if (!companies.TryGetValue(companyCode, out var company))
                {
                    failures.Add($"company {companyCode} not found in the master database.");
                    continue;
                }

                var plan = plans[planCode];

                // Reconcile: the most recent row is authoritative; older rows are expired so
                // exactly one current row remains. Rows are never deleted.
                var rows = context.CompanySubscriptions
                    .Where(s => s.CompanyId == company.CompanyId)
                    .OrderByDescending(s => s.CompanySubscriptionId)
                    .ToList();

                if (rows.Count == 0)
                {
                    context.CompanySubscriptions.Add(new CompanySubscription
                    {
                        CompanyId = company.CompanyId,
                        SubscriptionPlanId = plan.SubscriptionPlanId,
                        StartDate = new DateTime(2026, 10, 1),
                        EndDate = new DateTime(2027, 10, 1),
                        Status = SubscriptionStatuses.Active,
                        Notes = "Phase 9 demo subscription (seeded).",
                        CreatedAt = utcNow,
                        UpdatedAt = utcNow
                    });
                    context.SaveChanges();
                    write($"Subscription {companyCode}: inserted ({planCode}, 2026-10-01 .. 2027-10-01, Active).");
                    continue;
                }

                var current = rows[0];

                // Keep the current row's plan when it was intentionally changed (e.g. by the UI
                // or the verification harness); the seeder only fills in the approved demo state
                // when the row is missing a plan or brand new. Dates never go backwards.
                if (current.EndDate == default)
                {
                    current.StartDate = new DateTime(2026, 10, 1);
                    current.EndDate = new DateTime(2027, 10, 1);
                    current.UpdatedAt = utcNow;
                    write($"Subscription {companyCode}: dates defaulted to the demo window.");
                }

                // Anything older than the current row gets expired (never deleted).
                foreach (var older in rows.Skip(1).Where(r => r.Status == SubscriptionStatuses.Active))
                {
                    older.Status = SubscriptionStatuses.Expired;
                    older.UpdatedAt = utcNow;
                    write($"Subscription {companyCode}: older row {older.CompanySubscriptionId} expired (reconciliation).");
                }

                context.SaveChanges();
                write($"Subscription {companyCode}: current row {current.CompanySubscriptionId} kept " +
                      $"(plan id {current.SubscriptionPlanId}, status {current.Status}) - not modified.");
            }

            // ---- 5. Duplicate guard ----
            var duplicatePlans = context.SubscriptionPlans.GroupBy(p => p.Code).Where(g => g.Count() > 1).Count();
            var duplicateModules = context.SoftwareModules.GroupBy(m => m.Code).Where(g => g.Count() > 1).Count();
            var duplicateMappings = context.PlanModules
                .GroupBy(pm => new { pm.SubscriptionPlanId, pm.ModuleId })
                .Count(g => g.Count() > 1);
            var duplicateCompanySubs = context.CompanySubscriptions
                .GroupBy(s => new { s.CompanyId, s.Status })
                .Where(g => g.Key.Status == SubscriptionStatuses.Active && g.Count() > 1)
                .Count();

            write(string.Empty);
            write($"Duplicate plans: {duplicatePlans} | duplicate modules: {duplicateModules} | " +
                  $"duplicate mappings: {duplicateMappings} | duplicate active company subs: {duplicateCompanySubs}");

            if (duplicatePlans + duplicateModules + duplicateMappings > 0)
            {
                failures.Add("duplicate seed rows detected.");
            }

            return failures.Count == 0 ? 0 : 1;
        }

        private static int VerifySubscriptions(AppRuntime runtime, Action<string> write, List<string> failures)
        {
            var context = runtime.MasterDb;

            void Check(string name, bool ok, string detail)
            {
                write($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
                if (!ok)
                {
                    failures.Add($"{name}: {detail}");
                }
            }

            var companies = context.Companies.AsNoTracking().OrderBy(c => c.CompanyId).ToList();
            var plans = context.SubscriptionPlans.AsNoTracking().OrderBy(p => p.SubscriptionPlanId).ToList();
            var modules = context.SoftwareModules.AsNoTracking().OrderBy(m => m.ModuleId).ToList();
            var mappings = context.PlanModules.AsNoTracking().ToList();
            var subs = context.CompanySubscriptions.AsNoTracking().ToList();

            write($"Companies={companies.Count} Plans={plans.Count} Modules={modules.Count} " +
                  $"Mappings={mappings.Count} Subscriptions={subs.Count}");

            // Final subscription system: EXACTLY the three plans (the obsolete MICRO_BUSINESS /
            // SMALL_BUSINESS launch plans have been removed from the product).
            Check("exactly 3 plans (BASIC, PROFESSIONAL, ENTERPRISE)", plans.Count == 3, string.Join(", ", plans.Select(p => p.Code)));
            Check("no MICRO_BUSINESS / SMALL_BUSINESS plan remains",
                !plans.Any(p => p.Code == "MICRO_BUSINESS" || p.Code == "SMALL_BUSINESS"),
                string.Join(", ", plans.Select(p => p.Code)));
            Check("exactly 16 modules", modules.Count == 16, string.Join(", ", modules.Select(m => m.Code)));

            // Expected sets come from the approved plan matrix for the three retained plans.
            var matrix = SubscriptionService.GetPlanModuleMatrix()
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var plan in plans)
            {
                var expected = matrix.TryGetValue(plan.Code, out var codes) ? codes.Length : -1;
                var actual = mappings.Count(pm => pm.SubscriptionPlanId == plan.SubscriptionPlanId && pm.IsActive);
                Check($"plan {plan.Code} has {expected} modules", actual == expected, $"actual={actual}");
            }

            var companyCodes = new[] { "COMP001", "COMP002", "COMP003" };
            foreach (var code in companyCodes)
            {
                var company = companies.FirstOrDefault(c => c.CompanyCode == code);
                if (company == null)
                {
                    Check($"company {code} exists", false, "missing");
                    continue;
                }

                var service = new SubscriptionService(context);
                var info = service.GetCompanySubscriptionAsync(company.CompanyId).GetAwaiter().GetResult();
                Check($"company {code} has a subscription", info != null,
                    info == null ? "(none)" : $"{info.PlanCode} {info.EffectiveStatus} {info.StartDate:yyyy-MM-dd}..{info.EndDate:yyyy-MM-dd} modules={info.EnabledModuleCount}");
            }

            Check("master identity/tables unchanged",
                companies.Count == 3,
                $"Companies={companies.Count}");

            return failures.Count == 0 ? 0 : 1;
        }

        private static int CountTables(MasterAlphaEliteDbContext context)
        {
            return context.Database
                .SqlQueryRaw<int>("SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables")
                .First();
        }

        /// <summary>
        /// Phase 2 preflight: prints the live master table list with row counts, the columns
        /// of the four existing subscription tables, and the Identity/Companies/CompanyDatabase
        /// data counts that must survive the migration. Strictly read-only.
        /// </summary>
        private static int InspectMasterSchema(AppRuntime runtime, Action<string> write)
        {
            var context = runtime.MasterDb;

            var tables = context.Database
                .SqlQueryRaw<string>("SELECT CAST(name AS nvarchar(max)) AS [Value] FROM sys.tables ORDER BY name")
                .ToList();
            write($"Live master tables ({tables.Count}): {string.Join(", ", tables)}");

            var rowCounts = context.Database
                .SqlQueryRaw<TableRowCount>(
                    "SELECT CAST(t.name AS nvarchar(max)) AS [TableName], CAST(SUM(p.rows) AS bigint) AS [Rows] " +
                    "FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1) " +
                    "GROUP BY t.name ORDER BY t.name")
                .ToDictionary(r => r.TableName, r => r.Rows);
            foreach (var (name, rows) in rowCounts)
            {
                write($"  {name}: {rows} rows");
            }

            foreach (var table in new[] { "SubscriptionPlans", "SoftwareModules", "PlanModules", "CompanySubscriptions" })
            {
                if (!tables.Contains(table))
                {
                    write($"  [missing] {table}");
                    continue;
                }

                var columns = context.Database
                    .SqlQueryRaw<ColumnRow>(
                        "SELECT CAST(c.name AS nvarchar(max)) AS [Name], CAST(t.name AS nvarchar(max)) AS [Type], " +
                        "c.max_length AS [MaxLength], c.is_nullable AS [Nullable] " +
                        "FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id " +
                        "WHERE c.object_id = OBJECT_ID(@p0) ORDER BY c.column_id",
                        new object[] { $"dbo.{table}" })
                    .ToList();
                write($"  {table}: " + string.Join(", ",
                    columns.Select(c => $"{c.Name} {c.Type}{(c.MaxLength > 0 && c.Type.Contains("char") ? $"({c.MaxLength})" : string.Empty)}{(c.Nullable ? " NULL" : string.Empty)}")));
            }

            write(string.Empty);
            write("Plan detail (Code | Name | Price | BillingCycle | IsActive | terms length | modules):");
            foreach (var plan in context.SubscriptionPlans.AsNoTracking().OrderBy(p => p.SubscriptionPlanId).ToList())
            {
                var moduleCodes = context.PlanModules.AsNoTracking()
                    .Where(pm => pm.SubscriptionPlanId == plan.SubscriptionPlanId && pm.IsActive)
                    .Join(context.SoftwareModules.AsNoTracking(),
                        pm => pm.ModuleId, m => m.ModuleId, (pm, m) => m.Code)
                    .ToList();
                var ordered = SoftwareModuleCodes.All.Where(c => moduleCodes.Contains(c, StringComparer.Ordinal));
                write($"  {plan.Code} | {plan.Name} | {plan.Price:0.00} | {plan.BillingCycle} | {(plan.IsActive ? "Active" : "Inactive")} | " +
                      $"terms={plan.TermsAndAgreement.Length} chars | {moduleCodes.Count} modules: {string.Join(",", ordered)}");
            }

            write("Company subscriptions (Company | PlanId | Status | dates | Price | PaymentStatus):"
                );
            foreach (var sub in context.CompanySubscriptions.AsNoTracking().OrderBy(s => s.CompanyId).ToList())
            {
                var company = context.Companies.AsNoTracking().First(c => c.CompanyId == sub.CompanyId);
                write($"  {company.CompanyCode} | plan {sub.SubscriptionPlanId} | {sub.Status} | " +
                      $"{sub.StartDate:yyyy-MM-dd}..{sub.EndDate:yyyy-MM-dd} | {sub.Price:0.00} | {sub.PaymentStatus}");
            }

            write($"SubscriptionHistory rows        : {context.SubscriptionHistories.AsNoTracking().Count()}");
            write($"SubscriptionChangeRequest rows  : {context.SubscriptionChangeRequests.AsNoTracking().Count()}");

            write(string.Empty);
            write("Data that must survive the migration (EF read, no tracking):");
            write($"  Identity users        : {context.Users.AsNoTracking().Count()}");
            write($"  Identity roles        : {context.Roles.AsNoTracking().Count()}");
            var companies = context.Companies.AsNoTracking().OrderBy(c => c.CompanyId).ToList();
            write($"  Companies             : {companies.Count} ({string.Join(", ", companies.Select(c => c.CompanyCode))})");
            write($"  CompanyDatabase rows  : {context.CompanyDatabase.AsNoTracking().Count()}");
            write($"  Devices               : {context.Devices.AsNoTracking().Count()}");
            write($"  SubscriptionPlans     : {context.SubscriptionPlans.AsNoTracking().Count()}");
            write($"  SoftwareModules       : {context.SoftwareModules.AsNoTracking().Count()}");
            write($"  PlanModules           : {context.PlanModules.AsNoTracking().Count()}");
            write($"  CompanySubscriptions  : {context.CompanySubscriptions.AsNoTracking().Count()}");

            return 0;
        }

        /// <summary>
        /// Prints exactly what EF's differ sees between the hand-authored snapshot model and
        /// the CLR context model. Read-only; used to drive the snapshot to a zero diff so the
        /// PendingModelChangesWarning disappears honestly (no warning suppression).
        /// </summary>
        private static int InspectModelDifferences(AppRuntime runtime, Action<string> write)
        {
            var context = runtime.MasterDb;
            var rawSnapshotModel = context.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;

            if (rawSnapshotModel == null)
            {
                write("No model snapshot found.");
                return 1;
            }

            // EF's own migrations tooling pattern (MigrationsOperations): the snapshot model
            // only needs runtime dependencies initialized (designTime: false), while the
            // current CLR model is compared through the design-time model.
            var snapshotModel = context.GetService<IModelRuntimeInitializer>().Initialize(rawSnapshotModel);

            var differ = context.GetService<IMigrationsModelDiffer>();
            var currentModel = context.GetService<IDesignTimeModel>().Model;
            var diffs = differ.GetDifferences(snapshotModel.GetRelationalModel(), currentModel.GetRelationalModel());

            write($"Pending model changes (snapshot -> CLR model): {diffs.Count}");

            // Render the operations as SQL so the affected tables/columns are explicit.
            if (diffs.Count > 0)
            {
                var commands = context.GetService<IMigrationsSqlGenerator>().Generate(diffs, currentModel);
                foreach (var command in commands)
                {
                    write(command.CommandText);
                }
            }

            return diffs.Count == 0 ? 0 : 1;
        }

        private sealed record TableRowCount(string TableName, long Rows);

        private sealed record ColumnRow(string Name, string Type, short MaxLength, bool Nullable);
    }
}
