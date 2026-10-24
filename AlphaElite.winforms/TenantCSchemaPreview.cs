using AlphaElite.infrastructure.data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Dry run for the Tenant C schema (<c>AlphaElite.winforms --tenantc-schema-sql</c>).
    ///
    /// Produces, without executing anything:
    ///   1. the DDL EF generates from the TenantCAlphaEliteDbContext model, and
    ///   2. the DDL EF generates from the Tenant C migrations,
    /// then compares the two statement sets so the hand-authored migration can be proven
    /// consistent with the model.
    ///
    /// It never runs CREATE/ALTER/DROP: the only database access is a read-only count of the
    /// target database's tables, before and after, to prove nothing was created.
    /// </summary>
    internal static class TenantCSchemaPreview
    {
        public static int Run()
        {
            var runtime = AppRuntime.Create();

            // The target tenant comes from the master database + configuration, never a hardcoded id.
            var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();
            var tenant = tenants.FirstOrDefault(t => t.IsBranchAware);

            if (tenant == null)
            {
                Console.WriteLine("No branch-aware tenant is configured. Check TenantSchema:BranchAwareCredentialKeys in appsettings.json.");
                return 1;
            }

            var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
            var options = runtime.ConnectionFactory.CreateBranchAwareOptions(databaseInfo);

            using var context = new TenantCAlphaEliteDbContext(options);

            var outputLines = new List<string>();
            void Write(string text)
            {
                outputLines.Add(text);
                Console.WriteLine(text);
            }

            Write("=== Tenant C schema dry run (nothing is executed) ===");
            Write($"Target tenant : {tenant.CompanyCode} / {tenant.CompanyName} (CompanyId {tenant.CompanyId})");
            Write($"Target database: {databaseInfo.ServerName} / {databaseInfo.DatabaseName}  (credential key '{databaseInfo.CredentialKey}')");
            Write($"Context        : {context.GetType().Name}");

            var tablesBefore = CountTargetTables(context);
            Write($"Target database tables before: {tablesBefore}");

            Write(string.Empty);
            Write("--- DDL generated from the MODEL (Database.GenerateCreateScript) ---");
            var modelScript = context.Database.GenerateCreateScript();
            Write(modelScript);

            Write(string.Empty);
            Write("--- DDL generated from the MIGRATIONS (IMigrator.GenerateScript) ---");
            string migrationScript;
            try
            {
                var migrator = context.Database.GetService<IMigrator>();
                migrationScript = migrator.GenerateScript(
                    fromMigration: "0",
                    toMigration: null,
                    options: MigrationsSqlGenerationOptions.Default);
                Write(migrationScript);
            }
            catch (Exception ex)
            {
                migrationScript = string.Empty;
                Write($"[ERROR] could not generate a migration script: {ex.GetType().Name}: {ex.Message}");
            }

            Write(string.Empty);
            Write("--- Model vs migration comparison ---");
            var comparison = CompareScripts(modelScript, migrationScript);
            Write(comparison.Summary);
            foreach (var line in comparison.Details)
            {
                Write("        " + line);
            }

            var tablesAfter = CountTargetTables(context);
            Write(string.Empty);
            Write($"Target database tables after: {tablesAfter} (unchanged: {tablesBefore == tablesAfter})");
            Write("No CREATE/ALTER/DROP statement was executed: this mode only generates scripts.");

            var reportPath = Path.Combine(AppContext.BaseDirectory, "tenantc-schema-dryrun.txt");
            File.WriteAllLines(reportPath, outputLines);
            Console.WriteLine($"Report written to: {reportPath}");

            return comparison.Equivalent && tablesBefore == tablesAfter ? 0 : 1;
        }

        private static int CountTargetTables(TenantCAlphaEliteDbContext context)
        {
            return context.Database
                .SqlQueryRaw<int>("SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables")
                .First();
        }

        private static (bool Equivalent, string Summary, List<string> Details) CompareScripts(string modelScript, string migrationScript)
        {
            var modelStatements = NormalizeStatements(modelScript);
            var migrationStatements = NormalizeStatements(migrationScript);

            var onlyInModel = modelStatements.Except(migrationStatements, StringComparer.OrdinalIgnoreCase).ToList();
            var onlyInMigration = migrationStatements.Except(modelStatements, StringComparer.OrdinalIgnoreCase).ToList();

            var details = new List<string>
            {
                $"model statements: {modelStatements.Count}, migration statements: {migrationStatements.Count}"
            };

            foreach (var statement in onlyInModel)
            {
                details.Add($"only in model: {Truncate(statement)}");
            }

            foreach (var statement in onlyInMigration)
            {
                details.Add($"only in migration: {Truncate(statement)}");
            }

            var equivalent = onlyInModel.Count == 0 && onlyInMigration.Count == 0;
            var summary = equivalent
                ? "  [PASS] the migration produces exactly the same statements as the model."
                : "  [FAIL] the migration and the model produce different statements (listed below).";

            return (equivalent, summary, details);
        }

        /// <summary>
        /// Splits a script into comparable statements. Batch separators (GO), transaction
        /// wrappers and the EF migrations history table are removed because they are expected
        /// scaffolding differences between the model script and the migrations script, not
        /// schema differences.
        /// </summary>
        private static List<string> NormalizeStatements(string script)
        {
            var batchKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "GO", "BEGIN", "TRANSACTION", "COMMIT", "END", "SET"
            };

            var statements = new List<string>();

            foreach (var raw in script.Split(';'))
            {
                var tokens = raw
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Replace("\t", " ")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(token => !batchKeywords.Contains(token))
                    .ToArray();

                if (tokens.Length == 0)
                {
                    continue;
                }

                var statement = string.Join(" ", tokens);

                if (statement.Contains("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                statements.Add(statement);
            }

            return statements;
        }

        private static string Truncate(string value)
        {
            const int max = 220;
            return value.Length <= max ? value : value[..max] + " ...";
        }
    }
}
