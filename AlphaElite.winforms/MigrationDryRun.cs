using AlphaElite.infrastructure.services;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// PHASE 3 helper: strictly READ-ONLY inventory of the source tenants (A, B) and the
    /// destination tenant (C), used to design the data migration dry run.
    ///
    /// Every statement in this file is a SELECT against the system catalog views or the
    /// business tables. Nothing is inserted, updated, deleted or altered. Connection
    /// strings are built by the existing credential pipeline so no credential is ever
    /// written to the console or the report. Columns whose name contains "password"
    /// are never projected.
    /// </summary>
    internal static class MigrationDryRun
    {
        public static int Run()
        {
            var log = new List<string>();

            void Write(string text)
            {
                log.Add(text);
                Console.WriteLine(text);
            }

            var exitCode = 0;
            var reports = new List<(string Path, List<string> Lines)>();
            var failures = new List<string>();

            try
            {
                var runtime = AppRuntime.Create();
                var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();

                var branchAware = tenants.FirstOrDefault(t => t.IsBranchAware);
                var sources = tenants.Where(t => !t.IsBranchAware).OrderBy(t => t.CompanyId).ToList();

                Write("PHASE 3 - READ-ONLY MIGRATION INVENTORY");
                Write($"Discovered tenants: {tenants.Count} (branch-aware: {tenants.Count(t => t.IsBranchAware)}, source: {sources.Count})");
                Write(string.Empty);

                if (branchAware == null)
                {
                    Write("Aborted: no branch-aware tenant (Tenant C) is configured.");
                    return 1;
                }

                if (sources.Count != 2)
                {
                    Write($"Aborted: expected exactly 2 source tenants (A, B) but found {sources.Count}.");
                    return 1;
                }

                var tenantA = sources[0];
                var tenantB = sources[1];

                Write($"Tenant A : {tenantA.CompanyCode} / {tenantA.CompanyName} (CompanyId {tenantA.CompanyId}, credential key '{tenantA.CredentialKey}')");
                Write($"Tenant B : {tenantB.CompanyCode} / {tenantB.CompanyName} (CompanyId {tenantB.CompanyId}, credential key '{tenantB.CredentialKey}')");
                Write($"Tenant C : {branchAware.CompanyCode} / {branchAware.CompanyName} (CompanyId {branchAware.CompanyId}, credential key '{branchAware.CredentialKey}')");
                Write(string.Empty);

                var target = new[]
                {
                    ("TenantA", tenantA),
                    ("TenantB", tenantB),
                    ("TenantC", branchAware)
                };

                foreach (var (tag, tenant) in target)
                {
                    var info = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
                    var connectionString = runtime.ConnectionFactory.CreateConnectionString(info);
                    var lines = new List<string>();

                    using var connection = new SqlConnection(connectionString);
                    connection.Open();

                    lines.Add($"Database inventory: {tag}");
                    lines.Add($"Company        : {tenant.CompanyCode} / {tenant.CompanyName} (CompanyId {tenant.CompanyId})");
                    lines.Add($"Credential key : {tenant.CredentialKey}");
                    lines.Add($"Server         : {connection.DataSource}");
                    lines.Add($"Database       : {connection.Database}");
                    lines.Add($"Branch aware   : {tenant.IsBranchAware}");
                    lines.Add(string.Empty);

                    InventoryDatabase(connection, lines, failures);

                    var reportPath = Path.Combine(AppContext.BaseDirectory, $"phase3-inventory-{tag}.txt");
                    File.WriteAllLines(reportPath, lines);
                    reports.Add((reportPath, lines));

                    Write($"--- {tag}: inventory written to {reportPath}");
                }

                Write(string.Empty);
                Write("Cross-source duplicate / collision analysis:");
                var collisions = AnalyzeCollisions(runtime, tenantA, tenantB, Write);
                Write($"        findings: {collisions}");
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

            return exitCode;
        }

        // ---------------------------------------------------------------------------------
        // Metadata + data
        // ---------------------------------------------------------------------------------

        private static void InventoryDatabase(SqlConnection connection, List<string> lines, List<string> failures)
        {
            void L(string text)
            {
                lines.Add(text);
            }

            try
            {
                L("=== Tables and row counts ===");
                var counts = Query(connection,
                    "SELECT t.name, CAST(ISNULL(SUM(p.rows), 0) AS bigint) " +
                    "FROM sys.tables t " +
                    "LEFT JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1) " +
                    "GROUP BY t.name ORDER BY t.name");

                foreach (var row in counts)
                {
                    L($"        {row[0],-32} rows = {row[1]}");
                }

                L(string.Empty);
                L("=== Primary keys ===");
                var pks = Query(connection,
                    "SELECT t.name, c.name, c.system_type_id " +
                    "FROM sys.tables t " +
                    "JOIN sys.indexes i ON i.object_id = t.object_id AND i.is_primary_key = 1 " +
                    "JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 " +
                    "JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
                    "ORDER BY t.name");

                var pkByTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in pks)
                {
                    var table = (string)row[0]!;
                    var column = (string)row[1]!;
                    pkByTable[table] = column;
                    L($"        {table,-32} PK = {column}");
                }

                L(string.Empty);
                L("=== Primary-key ranges ===");
                foreach (var (table, column) in pkByTable.OrderBy(k => k.Key))
                {
                    if (table.StartsWith("__EF", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        var range = Query(connection,
                            $"SELECT COUNT(*), MIN([{column}]), MAX([{column}]) FROM [{table}]");
                        var r = range[0];
                        L($"        {table,-32} count = {r[0],-4} min = {r[1] ?? "(null)",-6} max = {r[2] ?? "(null)"}");
                    }
                    catch (Exception ex)
                    {
                        L($"        {table,-32} range query failed: {ex.Message}");
                    }
                }

                L(string.Empty);
                L("=== Row fingerprints (CHECKSUM_AGG(BINARY_CHECKSUM(*) over all columns)) ===");
                foreach (var table in Query(connection, "SELECT name FROM sys.tables ORDER BY name")
                             .Select(r => (string)r[0]!)
                             .Where(t => !t.StartsWith("__EF", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var checksum = Query(connection, $"SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM [{table}]");
                        L($"        {table,-32} checksum = {checksum[0][0] ?? "(null / empty)"}");
                    }
                    catch (Exception ex)
                    {
                        L($"        {table,-32} checksum unavailable: {ex.Message}");
                    }
                }

                L(string.Empty);
                L("=== Columns ===");
                var columns = Query(connection,
                    "SELECT c.TABLE_NAME, c.ORDINAL_POSITION, c.COLUMN_NAME, c.DATA_TYPE, " +
                    "       c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.IS_NULLABLE, " +
                    "       COLUMNPROPERTY(OBJECT_ID(c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity " +
                    "FROM INFORMATION_SCHEMA.COLUMNS c " +
                    "WHERE c.TABLE_NAME NOT LIKE '[_][_]EF%' " +
                    "ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION");

                string currentTable = string.Empty;
                foreach (var row in columns)
                {
                    var table = (string)row[0]!;
                    if (!string.Equals(table, currentTable, StringComparison.OrdinalIgnoreCase))
                    {
                        L(string.Empty);
                        L($"  Table: {table}");
                        currentTable = table;
                    }

                    var type = (string)row[3]!;
                    var length = row[4] != null ? $"({row[4]})"
                               : row[5] != null ? $"({row[5]},{row[6]})"
                               : string.Empty;
                    var nullable = (string)row[7]!;
                    var identity = Convert.ToInt32(row[8] ?? 0) == 1 ? " IDENTITY" : string.Empty;
                    L($"        {row[2],-30} {type}{length,-12} null={nullable}{identity}");
                }

                L(string.Empty);
                L("=== Foreign keys ===");
                var fks = Query(connection,
                    "SELECT fk.name, OBJECT_NAME(fk.parent_object_id), COL_NAME(fkc.parent_object_id, fkc.parent_column_id), " +
                    "       OBJECT_NAME(fk.referenced_object_id), COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id), " +
                    "       fk.delete_referential_action_desc " +
                    "FROM sys.foreign_keys fk " +
                    "JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id " +
                    "ORDER BY OBJECT_NAME(fk.parent_object_id), fk.name");

                if (fks.Count == 0)
                {
                    L("        (none)");
                }

                foreach (var row in fks)
                {
                    L($"        {row[1]}.{row[2]} -> {row[3]}.{row[4]}  ON DELETE {row[5]}  [{row[0]}]");
                }

                L(string.Empty);
                L("=== Unique indexes / constraints ===");
                var indexes = Query(connection,
                    "SELECT OBJECT_NAME(i.object_id), i.name, i.is_primary_key, i.is_unique_constraint " +
                    "FROM sys.indexes i " +
                    "WHERE i.is_unique = 1 AND i.object_id IN (SELECT object_id FROM sys.tables) " +
                    "ORDER BY OBJECT_NAME(i.object_id), i.name");

                foreach (var row in indexes)
                {
                    var kind = Convert.ToBoolean(row[2]) ? "PRIMARY KEY" : Convert.ToBoolean(row[3]) ? "UNIQUE CONSTRAINT" : "UNIQUE INDEX";
                    var cols = Query(connection,
                        "SELECT c.name FROM sys.index_columns ic JOIN sys.columns c " +
                        "ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
                        "JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id " +
                        $"WHERE i.name = '{((string)row[1]!).Replace("'", "''")}' AND OBJECT_NAME(i.object_id) = '{((string)row[0]!).Replace("'", "''")}' " +
                        "ORDER BY ic.key_ordinal");
                    var colList = string.Join(", ", cols.Select(c => c[0]?.ToString()));
                    L($"        {row[0],-32} {row[1],-40} {kind} ({colList})");
                }

                L(string.Empty);
                L("=== Data (password columns excluded) ===");
                foreach (var (table, _) in Query(connection, "SELECT name FROM sys.tables ORDER BY name")
                             .Select(r => ((string)r[0]!, 0))
                             .Where(t => !t.Item1.StartsWith("__EF", StringComparison.OrdinalIgnoreCase)))
                {
                    DumpTable(connection, table, pkByTable, L);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{connection.Database}: {ex.GetType().Name}: {ex.Message}");
                L($"[ERROR] {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void DumpTable(SqlConnection connection, string table, Dictionary<string, string> pkByTable, Action<string> L)
        {
            var columns = Query(connection,
                "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
                $"WHERE TABLE_NAME = '{table.Replace("'", "''")}' " +
                "ORDER BY ORDINAL_POSITION");

            var safeColumns = columns
                .Select(c => (string)c[0]!)
                .Where(c => !c.Contains("password", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var hidden = columns.Count - safeColumns.Count;

            L(string.Empty);

            if (safeColumns.Count == 0)
            {
                L($"  {table}: no readable columns.");
                return;
            }

            var orderBy = pkByTable.TryGetValue(table, out var pk) && safeColumns.Contains(pk) ? $" ORDER BY [{pk}]" : string.Empty;
            var columnList = string.Join(", ", safeColumns.Select(c => $"[{c}]"));

            List<object?[]> rows;
            try
            {
                rows = Query(connection, $"SELECT {columnList} FROM [{table}]{orderBy}");
            }
            catch (Exception ex)
            {
                L($"  {table}: data query failed: {ex.Message}");
                return;
            }

            L($"  {table} ({rows.Count} row(s)){ (hidden > 0 ? $" [{hidden} password column(s) hidden]" : string.Empty)}");
            L("        " + string.Join(" | ", safeColumns));

            foreach (var row in rows)
            {
                L("        " + string.Join(" | ", row.Select(v => v switch
                {
                    null => "NULL",
                    byte[] bytes => $"0x{BitConverter.ToString(bytes).Replace("-", string.Empty)}",
                    DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    decimal dec => dec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => v.ToString()
                })));
            }
        }

        // ---------------------------------------------------------------------------------
        // Cross-source analysis
        // ---------------------------------------------------------------------------------

        private static string AnalyzeCollisions(
            AppRuntime runtime,
            TenantOption tenantA,
            TenantOption tenantB,
            Action<string> write)
        {
            var notes = new List<string>();

            var aCodes = ReadStrings(runtime, tenantA, "Members", "MemberCode", write);
            var bCodes = ReadStrings(runtime, tenantB, "Members", "MemberCode", write);

            notes.Add(CompareSet("MemberCode", "Tenant A", aCodes, "Tenant B", bCodes, write));

            var bUserNames = ReadStrings(runtime, tenantB, "UserAccounts", "Username", write);
            write($"        Tenant A UserAccounts table present: {TableExists(runtime, tenantA, "UserAccounts", write)}");
            write($"        Tenant B usernames: {(bUserNames.Count == 0 ? "(none)" : string.Join(", ", bUserNames))}");
            notes.Add($"usernames disjoint check deferred to destination (single source: Tenant B, {bUserNames.Count} row(s))");

            var branchCodes = ReadStrings(runtime, runtime.AuthenticationService
                    .GetAvailableTenantsAsync().GetAwaiter().GetResult().First(t => t.IsBranchAware),
                "Branches", "BranchCode", write);
            write($"        Tenant C BranchCode values: {(branchCodes.Count == 0 ? "(none)" : string.Join(", ", branchCodes))}");
            notes.Add($"branch codes in destination: {branchCodes.Count}");

            return string.Join("; ", notes);
        }

        private static string CompareSet(
            string label,
            string leftName, List<string> left,
            string rightName, List<string> right,
            Action<string> write)
        {
            var leftDup = left.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            var rightDup = right.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            var overlap = left.Intersect(right, StringComparer.OrdinalIgnoreCase).ToList();

            write($"        {label}: {leftName}={left.Count} {rightName}={right.Count} overlap={(overlap.Count == 0 ? "none" : string.Join(",", overlap))}"
                + $" {leftName}Dup={(leftDup.Count == 0 ? "none" : string.Join(",", leftDup))}"
                + $" {rightName}Dup={(rightDup.Count == 0 ? "none" : string.Join(",", rightDup))}");

            return $"{label} overlap {(overlap.Count == 0 ? "none" : string.Join(",", overlap))}";
        }

        private static List<string> ReadStrings(AppRuntime runtime, TenantOption tenant, string table, string column, Action<string> write)
        {
            try
            {
                var info = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
                using var connection = new SqlConnection(runtime.ConnectionFactory.CreateConnectionString(info));
                connection.Open();

                if (!TableExists(connection, table))
                {
                    return new List<string>();
                }

                return Query(connection, $"SELECT [{column}] FROM [{table}] ORDER BY [{column}]")
                    .Select(r => r[0]?.ToString() ?? string.Empty)
                    .ToList();
            }
            catch (Exception ex)
            {
                write($"        [warning] could not read {table}.{column} from {tenant.CompanyCode}: {ex.Message}");
                return new List<string>();
            }
        }

        private static bool TableExists(AppRuntime runtime, TenantOption tenant, string table, Action<string> write)
        {
            var info = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
            using var connection = new SqlConnection(runtime.ConnectionFactory.CreateConnectionString(info));
            connection.Open();
            return TableExists(connection, table);
        }

        private static bool TableExists(SqlConnection connection, string table)
        {
            var result = Query(connection,
                $"SELECT CAST(COUNT(*) AS int) FROM sys.tables WHERE name = '{table.Replace("'", "''")}'");
            return Convert.ToInt32(result[0][0] ?? 0) > 0;
        }

        // ---------------------------------------------------------------------------------
        // Raw helpers
        // ---------------------------------------------------------------------------------

        private static List<object?[]> Query(SqlConnection connection, string sql)
        {
            var result = new List<object?[]>();

            using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var row = new object?[reader.FieldCount];

                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                result.Add(row);
            }

            return result;
        }
    }
}
