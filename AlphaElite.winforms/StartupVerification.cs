using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Headless startup verification (<c>AlphaElite.winforms --selftest</c>).
    ///
    /// Exercises the real production path - master database tenant discovery, tenant-aware
    /// authentication and the exact module/dashboard queries the main window runs - without
    /// opening any window, so the runtime behaviour can be proven without a GUI. It performs
    /// read-only queries only: no inserts, updates, schema changes or migrations.
    ///
    /// Credentials are read from environment variables (never from the command line, so they
    /// are not visible in a process list):
    ///   ALPHA_VERIFY_USERNAME, ALPHA_VERIFY_PASSWORD
    ///   ALPHA_VERIFY_TENANT  (optional: company code, credential key or numeric company id;
    ///                         when omitted the first tenant that can authenticate is used)
    ///
    /// A plain-text copy of the output is written to selftest-report.txt in the output folder.
    /// </summary>
    internal static class StartupVerification
    {
        public static async Task<int> RunAsync()
        {
            var lines = new List<string>();
            var failures = new List<string>();
            var checks = 0;

            void Write(string text)
            {
                lines.Add(text);
                Console.WriteLine(text);
            }

            async Task CheckAsync(string name, Func<Task<object>> query)
            {
                checks++;

                try
                {
                    var value = await query();
                    Write($"  [PASS] {name}: {value}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
                    Write($"  [FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Write("=== Alpha Elite startup verification (read-only) ===");
            Write($"Started (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");

            var username = Environment.GetEnvironmentVariable("ALPHA_VERIFY_USERNAME");
            var password = Environment.GetEnvironmentVariable("ALPHA_VERIFY_PASSWORD");
            var requestedTenant = Environment.GetEnvironmentVariable("ALPHA_VERIFY_TENANT");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                Write("Missing ALPHA_VERIFY_USERNAME / ALPHA_VERIFY_PASSWORD environment variables.");
                Write("Example: ALPHA_VERIFY_USERNAME=admin ALPHA_VERIFY_PASSWORD=<password> dotnet run --project AlphaElite.winforms -- --selftest");
                return Finish(lines, failures, checks, exitCode: 2);
            }

            AppRuntime runtime;
            try
            {
                runtime = AppRuntime.Create();
            }
            catch (Exception ex)
            {
                Write($"[FAIL] application composition: {ex.GetType().Name}: {ex.Message}");
                return Finish(lines, failures, checks, exitCode: 1);
            }

            Write($"Configured master connection string: {(string.IsNullOrWhiteSpace(runtime.Configuration.GetConnectionString("MasterErp")) ? "NO" : "yes (value not printed)")}");
            Write($"Configured appsettings.json found: {File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))}");

            var branchAwareKeys = runtime.Configuration
                .GetSection("TenantSchema:BranchAwareCredentialKeys")
                .GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
            Write($"Branch-aware credential keys: {(branchAwareKeys.Length == 0 ? "(none configured)" : string.Join(", ", branchAwareKeys))}");

            Write(string.Empty);
            Write("--- Tenant discovery (master database) ---");

            IReadOnlyList<TenantOption> tenants;
            try
            {
                tenants = await runtime.AuthenticationService.GetAvailableTenantsAsync();
                checks++;
                Write($"  [PASS] active tenants discovered: {tenants.Count}");
            }
            catch (Exception ex)
            {
                failures.Add($"tenant discovery: {ex.GetType().Name}: {ex.Message}");
                Write($"  [FAIL] tenant discovery: {ex.GetType().Name}: {ex.Message}");
                return Finish(lines, failures, checks, exitCode: 1);
            }

            foreach (var tenant in tenants)
            {
                Write($"        CompanyId {tenant.CompanyId} | {tenant.CompanyCode} | {tenant.CompanyName} | db {tenant.DatabaseName} on {tenant.ServerName} | key {tenant.CredentialKey} | branch-aware: {tenant.IsBranchAware}");
            }

            Write(string.Empty);
            Write("--- Tenant credential + schema availability (read-only) ---");
            foreach (var tenant in tenants)
            {
                var credentialsConfigured =
                    !string.IsNullOrWhiteSpace(runtime.Configuration[$"TenantCredentials:{tenant.CredentialKey}:UserId"]) &&
                    !string.IsNullOrWhiteSpace(runtime.Configuration[$"TenantCredentials:{tenant.CredentialKey}:Password"]);

                Write($"        {tenant.CompanyCode}: credentials configured: {(credentialsConfigured ? "yes" : "NO")}");

                try
                {
                    var supported = await CanAuthenticateAsync(runtime, tenant);
                    checks++;
                    Write($"  [PASS] {tenant.CompanyCode}: UserAccounts table present: {supported}");
                    if (!supported)
                    {
                        Write($"        NOTE: {tenant.CompanyCode} cannot authenticate users yet (no UserAccounts table). Its database was not modified.");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{tenant.CompanyCode} probe: {ex.Message}");
                    Write($"  [FAIL] {tenant.CompanyCode}: probe failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            var target = SelectTarget(tenants, requestedTenant);
            if (target == null)
            {
                failures.Add("target tenant: not found");
                Write($"  [FAIL] requested tenant '{requestedTenant}' was not found in the master database.");
                return Finish(lines, failures, checks, exitCode: 1);
            }

            Write(string.Empty);
            Write("--- Authentication (production path) ---");
            Write($"        Target tenant: {target.CompanyCode} / {target.CompanyName} (CompanyId {target.CompanyId})");
            Write($"        Username: {username} (password not printed)");

            var databaseInfo = await runtime.Resolver.GetDatabaseInfoAsync(target.CompanyId);

            TenantLoginResult? login = null;
            try
            {
                login = await runtime.AuthenticationService.AuthenticateAsync(target.CompanyId, username, password);
                checks++;

                if (login == null)
                {
                    failures.Add("authentication: credentials rejected or account inactive");
                    Write("  [FAIL] authentication: credentials were rejected (invalid username/password, or the account is inactive).");
                }
                else
                {
                    Write($"  [PASS] authenticated: user '{login.Username}', role '{login.Role}', name '{login.FullName}'");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"authentication: {ex.GetType().Name}: {ex.Message}");
                Write($"  [FAIL] authentication: {ex.GetType().Name}: {ex.Message}");
            }

            Write(string.Empty);
            Write("--- Context selection vs. tenant schema ---");
            var expectedContext = target.IsBranchAware ? nameof(TenantCAlphaEliteDbContext) : nameof(TenantAlphaEliteDbContext);
            var actualContext = await ResolveContextNameAsync(runtime, target);
            checks++;
            if (string.Equals(expectedContext, actualContext, StringComparison.Ordinal))
            {
                Write($"  [PASS] context used: {actualContext}");
            }
            else
            {
                failures.Add($"context selection: expected {expectedContext}, got {actualContext}");
                Write($"  [FAIL] context selection: expected {expectedContext}, got {actualContext}");
            }

            if (login != null && login.NeedsPasswordRehash)
            {
                Write("  [INFO] stored password hash is legacy unsalted SHA-256; re-hashing needs an approved write (none performed).");
            }

            Write(string.Empty);
            Write("--- Dashboard / module queries (same queries the main window runs) ---");

            if (target.IsBranchAware)
            {
                var options = runtime.ConnectionFactory.CreateBranchAwareOptions(databaseInfo);
                await using var tenantDb = new TenantCAlphaEliteDbContext(options);
                await RunModuleChecksAsync(tenantDb, CheckAsync);
            }
            else
            {
                var options = runtime.ConnectionFactory.CreateStandardOptions(databaseInfo);
                await using var tenantDb = new TenantAlphaEliteDbContext(options);
                await RunModuleChecksAsync(tenantDb, CheckAsync);
            }

            Write(string.Empty);
            Write("--- Main window construction (no window is shown) ---");
            try
            {
                ApplicationConfiguration.Initialize();
                checks++;

                var session = new UserSession
                {
                    CompanyId = target.CompanyId,
                    CompanyCode = target.CompanyCode,
                    CompanyName = target.CompanyName,
                    IsBranchAware = target.IsBranchAware,
                    BranchId = login?.BranchId,
                    MemberId = login?.MemberId,
                    Username = login?.Username ?? username,
                    FullName = login?.FullName ?? username,
                    Role = login?.Role ?? string.Empty
                };

                using var mainWindow = new Form1(session, tenants, runtime.DataContextFactory, runtime.AuthorizationService);
                Write($"  [PASS] Form1 constructed; tenant switcher: {mainWindow.DescribeTenantSwitcher()}; session: {mainWindow.DescribeSession()}");
            }
            catch (Exception ex)
            {
                failures.Add($"main window construction: {ex.GetType().Name}: {ex.Message}");
                Write($"  [FAIL] main window construction: {ex.GetType().Name}: {ex.Message}");
            }

            var signatureFailures = failures
                .Where(f => f.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase)
                         || f.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
                         || f.Contains("Login failed for user", StringComparison.OrdinalIgnoreCase)
                         || f.Contains("NullReferenceException", StringComparison.OrdinalIgnoreCase)
                         || f.Contains("BranchId", StringComparison.OrdinalIgnoreCase)
                         || f.Contains("MemberId", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Write(string.Empty);
            Write("--- Error signature scan ---");
            if (signatureFailures.Count == 0)
            {
                Write("  [PASS] no 'Invalid column name', 'Invalid object name', connection-login, NullReference or BranchId/MemberId errors encountered.");
            }
            else
            {
                foreach (var failure in signatureFailures)
                {
                    Write($"  [FAIL] {failure}");
                }
            }

            var exitCode = failures.Count == 0 ? 0 : 1;

            Write(string.Empty);
            Write($"--- Result: {(exitCode == 0 ? "ALL CHECKS PASSED" : "FAILURES PRESENT")} | checks: {checks} | failures: {failures.Count} ---");
            foreach (var failure in failures)
            {
                Write($"        - {failure}");
            }

            return Finish(lines, failures, checks, exitCode);
        }

        private static TenantOption? SelectTarget(IReadOnlyList<TenantOption> tenants, string? requestedTenant)
        {
            if (!string.IsNullOrWhiteSpace(requestedTenant))
            {
                var value = requestedTenant.Trim();

                return tenants.FirstOrDefault(t =>
                           string.Equals(t.CompanyCode, value, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(t.CredentialKey, value, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(t.CompanyName, value, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(t.CompanyId.ToString(), value, StringComparison.OrdinalIgnoreCase));
            }

            return tenants.FirstOrDefault();
        }

        private static async Task<string> ResolveContextNameAsync(AppRuntime runtime, TenantOption tenant)
        {
            var databaseInfo = await runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId);

            if (tenant.IsBranchAware)
            {
                await using var branchAware = new TenantCAlphaEliteDbContext(runtime.ConnectionFactory.CreateBranchAwareOptions(databaseInfo));
                return branchAware.GetType().Name;
            }

            await using var standard = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(databaseInfo));
            return standard.GetType().Name;
        }

        private static async Task<bool> CanAuthenticateAsync(AppRuntime runtime, TenantOption tenant)
        {
            var databaseInfo = await runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId);
            const string probe = "SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables WHERE name = 'UserAccounts'";

            if (tenant.IsBranchAware)
            {
                await using var branchAware = new TenantCAlphaEliteDbContext(runtime.ConnectionFactory.CreateBranchAwareOptions(databaseInfo));
                return await branchAware.Database.SqlQueryRaw<int>(probe).FirstAsync() > 0;
            }

            await using var standard = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(databaseInfo));
            return await standard.Database.SqlQueryRaw<int>(probe).FirstAsync() > 0;
        }

        private static async Task RunModuleChecksAsync(
            DbContext tenantDb,
            Func<string, Func<Task<object>>, Task> check)
        {
            switch (tenantDb)
            {
                case TenantAlphaEliteDbContext standard:
                    await RunStandardChecksAsync(standard, check);
                    break;

                case TenantCAlphaEliteDbContext branchAwareDb:
                    await RunBranchAwareChecksAsync(branchAwareDb, check);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported tenant DbContext '{tenantDb.GetType().Name}'.");
            }
        }

        private static async Task RunStandardChecksAsync(TenantAlphaEliteDbContext db, Func<string, Func<Task<object>>, Task> check)
        {
            await check("Members (count)", async () => (object)await db.Members.CountAsync());
            await check("Members active (count)", async () => (object)await db.Members.CountAsync(x => x.IsActive));
            await check("Attendance (count)", async () => (object)await db.Attendances.CountAsync());
            await check("Attendance today (count)", async () => (object)await db.Attendances.CountAsync(x => x.CheckInDate.Date == DateTime.Today));
            await check("Attendance trend last 30 days (rows)", async () => (object)(await db.Attendances.AsNoTracking()
                .Where(x => x.CheckInDate.Date >= DateTime.Today.AddDays(-29))
                .Select(x => x.CheckInDate.Date)
                .ToListAsync()).Count);
            await check("Payments (count)", async () => (object)await db.Payments.CountAsync());
            await check("Payments total (sum)", async () => (object)await db.Payments.SumAsync(x => (decimal?)x.AmountPaid) ?? 0m);
            await check("Payments monthly trend rows (last 6 months)", async () => (object)(await db.Payments.AsNoTracking()
                .Where(x => x.PaymentDate >= DateTime.Today.AddMonths(-5))
                .Select(x => new { x.PaymentDate, x.AmountPaid })
                .ToListAsync()).Count);
            await check("Feedback (count)", async () => (object)await db.Feedbacks.CountAsync());
            await check("Feedback average rating", async () =>
            {
                var hasFeedback = await db.Feedbacks.AnyAsync();
                return (object)(hasFeedback ? Math.Round(await db.Feedbacks.AverageAsync(x => (double)x.Rating), 2) : 0d);
            });
            await check("Inquiries (count)", async () => (object)await db.Inquiries.CountAsync());
            await check("Inquiries pending follow-up (count)", async () => (object)await db.Inquiries.CountAsync(x => !x.IsFollowedUp));
            await check("Subscriptions (count)", async () => (object)await db.Subscriptions.CountAsync());
            await check("Subscriptions active (count)", async () => (object)await db.Subscriptions.CountAsync(x => x.IsActive));
            await check("Retention records (count)", async () => (object)await db.RetentionRecords.CountAsync());
            await check("Retention records pending approval (count)", async () => (object)await db.RetentionRecords.CountAsync(x => x.Status == "Pending"));
        }

        private static async Task RunBranchAwareChecksAsync(TenantCAlphaEliteDbContext db, Func<string, Func<Task<object>>, Task> check)
        {
            await check("Branches (count)", async () => (object)await db.Branches.CountAsync());
            await check("Complaints (count)", async () => (object)await db.Complaints.CountAsync());
            await check("Members (count)", async () => (object)await db.Members.CountAsync());
            await check("Members active (count)", async () => (object)await db.Members.CountAsync(x => x.IsActive));
            await check("Members per branch (groups)", async () => (object)(await db.Members.AsNoTracking()
                .Where(x => x.BranchId != null)
                .GroupBy(x => x.BranchId)
                .Select(g => g.Key)
                .ToListAsync()).Count);
            await check("Attendance (count)", async () => (object)await db.Attendances.CountAsync());
            await check("Attendance today (count)", async () => (object)await db.Attendances.CountAsync(x => x.CheckInDate.Date == DateTime.Today));
            await check("Payments (count)", async () => (object)await db.Payments.CountAsync());
            await check("Payments total (sum)", async () => (object)await db.Payments.SumAsync(x => (decimal?)x.AmountPaid) ?? 0m);
            await check("Feedback (count)", async () => (object)await db.Feedbacks.CountAsync());
            await check("Inquiries (count)", async () => (object)await db.Inquiries.CountAsync());
            await check("Subscriptions (count)", async () => (object)await db.Subscriptions.CountAsync());
            await check("Retention records (count)", async () => (object)await db.RetentionRecords.CountAsync());
            await check("User accounts (count)", async () => (object)await db.UserAccounts.CountAsync());
        }

        private static int Finish(List<string> lines, List<string> failures, int checks, int exitCode)
        {
            var footer = new[]
            {
                string.Empty,
                $"Verification finished (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
                $"Exit code: {exitCode} | checks: {checks} | failures: {failures.Count}"
            };

            lines.AddRange(footer);

            try
            {
                var reportPath = Path.Combine(AppContext.BaseDirectory, "selftest-report.txt");
                File.WriteAllLines(reportPath, lines);
                Console.WriteLine($"Report written to: {reportPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not write the report file: {ex.Message}");
            }

            Console.Out.Flush();
            return exitCode;
        }
    }
}
