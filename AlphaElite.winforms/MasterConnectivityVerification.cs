using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Targeted verification for the MASTER tenant/company load path and the transient-failure
    /// resilience that protects it (Super Admin screen).
    ///
    /// Covers, in this order:
    ///   1. every master context path runs the SAME bounded retrying execution strategy;
    ///   2. the retry policy is bounded (never infinite, never an indefinite UI block);
    ///   3. failure classification is honest - a real outage is reported as "temporarily
    ///      unavailable", while authentication / configuration / schema problems are NOT;
    ///   4. a genuinely unreachable endpoint really retries, really terminates, and logs the
    ///      ORIGINAL exception (with its inner chain) for diagnostics;
    ///   5. the live load path (only when the master database is actually reachable): the
    ///      tenant/company list loads, overlapping master reads use separate contexts, one
    ///      dashboard load reads the list exactly once, panel navigation never re-reads it and
    ///      rapid navigation neither throws nor duplicates loads.
    ///
    /// Read-only against the live database (SELECTs only); the unreachable-endpoint probe uses a
    /// synthetic loopback connection string and never touches any real database. No tenant
    /// database is opened by this harness.
    /// </summary>
    internal static class MasterConnectivityVerification
    {
        private const string Headline = "Alpha Elite could not load the tenant list from the master database.";

        public static int Run()
        {
            var failures = new List<string>();
            var skipped = new List<string>();
            var checks = 0;

            void Write(string text) => Console.WriteLine(text);

            void Check(string name, bool ok, string detail)
            {
                checks++;
                Write($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
                if (!ok)
                {
                    failures.Add($"{name}: {detail}");
                }
            }

            void Skip(string name, string reason)
            {
                skipped.Add(name);
                Write($"  [SKIP] {name}: {reason}");
            }

            try
            {
                var runtime = AppRuntime.Create();
                var masterDatabase = runtime.MasterDb.Database.GetDbConnection().Database;
                var masterConnectionString = runtime.Configuration.GetConnectionString("MasterErp");

                Write("Operation      : master-connectivity-verify");
                Write($"Target database: {masterDatabase} (ConnectionStrings:MasterErp)");

                if (!masterDatabase.Contains("67598"))
                {
                    Write($"[ABORT] Refusing to run: the master connection does not point at db67598 (got '{masterDatabase}').");
                    return 1;
                }

                // =====================================================================================
                // 1. One resilient configuration on every master context path
                // =====================================================================================
                Write(string.Empty);
                Write("--- 1. Master transient-failure retry configuration ---");

                var rootStrategy = runtime.MasterDb.Database.CreateExecutionStrategy();
                Check("1. the master composition-root context uses a RETRYING execution strategy",
                    rootStrategy.RetriesOnFailure && rootStrategy.GetType().Name.Contains("Retrying", StringComparison.Ordinal),
                    $"{rootStrategy.GetType().Name} RetriesOnFailure={rootStrategy.RetriesOnFailure}");

                using (var perOperation = runtime.MasterContextFactory.CreateDbContext())
                {
                    var strategy = perOperation.Database.CreateExecutionStrategy();
                    Check("2. the per-operation master context uses the SAME retrying strategy",
                        strategy.RetriesOnFailure && strategy.GetType() == rootStrategy.GetType(),
                        $"{strategy.GetType().Name} RetriesOnFailure={strategy.RetriesOnFailure}");
                }

                using (var fromSharedBuilder = new MasterAlphaEliteDbContext(MasterDbContextOptions.Create(masterConnectionString!)))
                {
                    Check("3. every context built through MasterDbContextOptions.Create retries",
                        fromSharedBuilder.Database.CreateExecutionStrategy().RetriesOnFailure,
                        "shared builder (used by the root, the factory, the Identity clone and the API host)");
                }

                // The configured bound is a compile-time constant, so the REAL proof that retrying
                // is bounded is the observed behaviour in section 4 (checks 18b / 19): the number of
                // retries seen stays within the bound and the attempt terminates. Here only the
                // delay ceiling is asserted at run time.
                Check("4. the configured retry delay keeps a single retry short",
                    MasterDbContextOptions.MaxRetryDelay > TimeSpan.Zero
                    && MasterDbContextOptions.MaxRetryDelay <= TimeSpan.FromSeconds(30),
                    $"maxRetryCount={MasterDbContextOptions.MaxRetryCount} maxRetryDelay={MasterDbContextOptions.MaxRetryDelay.TotalSeconds:0}s");

                Check("5. the master policy matches the established tenant policy (5 retries / 10 s)",
                    MasterDbContextOptions.MaxRetryCount == 5
                    && MasterDbContextOptions.MaxRetryDelay == TimeSpan.FromSeconds(10),
                    "same bounded values as TenantDbConnectionFactory.ConfigureSqlServer");

                using (var first = runtime.MasterContextFactory.CreateDbContext())
                using (var second = runtime.MasterContextFactory.CreateDbContext())
                {
                    Check("6. per-operation contexts remain DISTINCT instances (concurrency fix intact)",
                        !ReferenceEquals(first, second),
                        $"{first.ContextId} != {second.ContextId}");
                }

                // =====================================================================================
                // 2. Failure classification must stay honest
                // =====================================================================================
                Write(string.Empty);
                Write("--- 2. Failure classification ---");

                Check("7. connection refused is an availability failure",
                    MasterDbContextOptions.IsAvailabilityFailure(new SocketException(10061)), "WSAECONNREFUSED 10061");
                Check("8. 'the network path was not found' is an availability failure",
                    MasterDbContextOptions.IsAvailabilityFailure(new Win32Exception(53)), "Win32 53 (ERROR_BAD_NETPATH)");
                Check("9. 'the network name is no longer available' is an availability failure",
                    MasterDbContextOptions.IsAvailabilityFailure(new Win32Exception(64)), "Win32 64 (ERROR_NETNAME_DELETED)");
                Check("10. a connection timeout is an availability failure",
                    MasterDbContextOptions.IsAvailabilityFailure(new TimeoutException("connection timed out")), "TimeoutException");
                Check("11. a transient error wrapped by EF is still detected",
                    MasterDbContextOptions.IsAvailabilityFailure(new InvalidOperationException("EF wrapper", new SocketException(11001))),
                    "inner SocketException(11001)");
                Check("12. a PERMANENT problem is NOT reported as an outage",
                    !MasterDbContextOptions.IsAvailabilityFailure(new InvalidOperationException("Invalid object name 'Members'."))
                    && !MasterDbContextOptions.IsAvailabilityFailure(new Win32Exception(5)),
                    "schema error and access-denied keep their real meaning");

                // =====================================================================================
                // 3. Operator-facing message
                // =====================================================================================
                Write(string.Empty);
                Write("--- 3. Operator-facing message ---");

                var connectivityFailure = new InvalidOperationException("EF wrapper", new Win32Exception(64));
                var outageMessage = MasterDbErrorReporter.BuildUserMessage(Headline, connectivityFailure);
                Check("13. an outage shows the clear, actionable message",
                    outageMessage.Contains("The Master Database is temporarily unavailable.", StringComparison.Ordinal)
                    && outageMessage.Contains("Please check the database connection and try again.", StringComparison.Ordinal)
                    && outageMessage.Contains(Headline, StringComparison.Ordinal),
                    "friendly text plus the original headline");
                Check("14. the developer-facing EF hint / stack trace is NOT shown to the operator",
                    !outageMessage.Contains("EnableRetryOnFailure", StringComparison.Ordinal)
                    && !outageMessage.Contains("InvalidOperationException", StringComparison.Ordinal)
                    && !outageMessage.Contains("   at ", StringComparison.Ordinal),
                    "no EF advice, no exception type name, no stack trace");
                var permanentMessage = MasterDbErrorReporter.BuildUserMessage(Headline, new InvalidOperationException("Invalid object name 'Members'."));
                Check("15. a permanent problem is NOT disguised as a temporary outage",
                    !permanentMessage.Contains("temporarily unavailable", StringComparison.Ordinal)
                    && permanentMessage.Contains("Invalid object name 'Members'.", StringComparison.Ordinal),
                    "the real reason is preserved");

                // =====================================================================================
                // 4. Bounded retry against a genuinely unreachable endpoint (real attempt)
                // =====================================================================================
                Write(string.Empty);
                Write("--- 4. Bounded retry against an unreachable endpoint ---");

                var efLog = new List<string>();
                // Loopback port 1: refuse-only. Synthetic credentials/config; no real database involved.
                const string unreachable =
                    "Server=127.0.0.1,1;Database=master;User Id=connectivity-probe;Password=connectivity-probe;" +
                    "Encrypt=False;TrustServerCertificate=True;Connect Timeout=1";
                var probeOptions = new DbContextOptionsBuilder<MasterAlphaEliteDbContext>()
                    .UseSqlServer(unreachable, MasterDbContextOptions.Configure)
                    .LogTo(efLog.Add, LogLevel.Information)
                    .Options;

                Exception? probeFailure = null;
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    using var probe = new MasterAlphaEliteDbContext(probeOptions);
                    _ = probe.Companies.AsNoTracking().Count();
                }
                catch (Exception ex)
                {
                    probeFailure = ex;
                }
                stopwatch.Stop();

                Check("16. an unreachable master endpoint FAILS (never a silent success)",
                    probeFailure != null,
                    probeFailure == null
                        ? "NO EXCEPTION THROWN - a silent failure would hide an outage"
                        : probeFailure.GetBaseException().GetType().Name + ": " + probeFailure.GetBaseException().Message);
                Check("17. that failure is classified as an availability failure",
                    probeFailure != null && MasterDbContextOptions.IsAvailabilityFailure(probeFailure),
                    probeFailure == null ? "(no failure)" : "classified as outage");
                var retryEvents = efLog.Where(line => line.Contains("will be retried", StringComparison.OrdinalIgnoreCase)).ToList();
                Check("18. the bounded retry policy is actually engaged (the operation was retried)",
                    retryEvents.Count > 0,
                    retryEvents.Count > 0
                        ? $"{retryEvents.Count} retry event(s), first: {retryEvents[0]}"
                        : $"{efLog.Count} EF log entries but no retry event; last entry: {(efLog.Count == 0 ? "(none)" : efLog[^1])}");
                Check("18b. the retries OBSERVED stay within the configured bound",
                    retryEvents.Count >= 1 && retryEvents.Count <= MasterDbContextOptions.MaxRetryCount,
                    $"{retryEvents.Count} retry event(s) observed, configured bound={MasterDbContextOptions.MaxRetryCount}");
                // Worst case is bounded by (Connect Timeout x attempts) + the capped backoff, so a
                // generous ceiling still fails loudly if retrying ever stopped terminating.
                Check("19. retrying TERMINATES - bounded, no infinite loop, no indefinite block",
                    stopwatch.Elapsed < TimeSpan.FromSeconds(120),
                    $"elapsed {stopwatch.Elapsed.TotalSeconds:0.0}s (bound {MasterDbContextOptions.MaxRetryCount} retries / {MasterDbContextOptions.MaxRetryDelay.TotalSeconds:0}s max delay)");
                Check("20. the operator would see the friendly message for this real failure",
                    probeFailure != null
                    && MasterDbErrorReporter.BuildUserMessage(Headline, probeFailure)
                        .Contains("The Master Database is temporarily unavailable.", StringComparison.Ordinal),
                    "BuildUserMessage(real failure)");

                var logPath = MasterDbErrorReporter.DiagnosticsLogPath;
                var logLengthBefore = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
                if (probeFailure != null)
                {
                    MasterDbErrorReporter.Log("connectivity-verify probe", probeFailure);
                }
                var logText = File.Exists(logPath) ? File.ReadAllText(logPath) : string.Empty;
                Check("21. the ORIGINAL exception and its inner chain are logged for diagnostics",
                    logText.Length > logLengthBefore
                    && logText.Contains("connectivity-verify probe", StringComparison.Ordinal)
                    && logText.Contains("Inner chain:", StringComparison.Ordinal),
                    logPath);

                // =====================================================================================
                // 5. The live tenant/company load path (requires a reachable master database)
                // =====================================================================================
                Write(string.Empty);
                Write("--- 5. Live master tenant/company load path ---");

                var reachable = false;
                try
                {
                    reachable = runtime.MasterDb.Database.CanConnect();
                }
                catch
                {
                    reachable = false;
                }

                Write($"Master database reachable during this run: {(reachable ? "yes" : "no")}");
                if (!reachable)
                {
                    var reason = "the master database is unreachable from this machine right now";
                    Skip("22. tenant/company list load succeeds", reason);
                    Skip("23. overlapping master reads use separate contexts", reason);
                    Skip("24. one dashboard load reads the tenant list exactly once", reason);
                    Skip("25. panel navigation does not reload the tenant list", reason);
                    Skip("26. rapid navigation neither throws nor duplicates loads", reason);
                }
                else
                {
                    var expected = runtime.MasterDb.CompanyDatabase.AsNoTracking()
                        .Count(d => d.IsActive && d.Company!.IsActive);
                    var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();
                    Check("22. tenant/company list load succeeds and matches the master rows",
                        expected > 0 && tenants.Count == expected,
                        $"tenants={tenants.Count} expected={expected}");

                    var overlapDetail = "OK";
                    try
                    {
                        using var left = runtime.MasterContextFactory.CreateDbContext();
                        using var right = runtime.MasterContextFactory.CreateDbContext();
                        var leftRead = left.SoftwareModules.AsNoTracking().CountAsync();
                        var rightRead = right.SubscriptionPlans.AsNoTracking().CountAsync();
                        Task.WhenAll(leftRead, rightRead).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        overlapDetail = ex.GetBaseException().Message;
                    }

                    Check("23. overlapping master reads use separate contexts and both succeed",
                        overlapDetail == "OK",
                        overlapDetail);

                    using var masterForm = new MasterDashboardForm(runtime, new MasterUserSession
                    {
                        MasterUserId = "connectivity-probe-superadmin",
                        Username = "connectivity-probe",
                        Role = Roles.MasterAdmin,
                        IdentityRoleName = MasterIdentityService.SuperAdminRoleName
                    });
                    InstallHeadlessContext();

                    masterForm.LoadTenantListForTestAsync().GetAwaiter().GetResult();
                    InstallHeadlessContext();
                    var afterOneLoad = masterForm.TenantListLoadCount;

                    masterForm.LoadTenantListForTestAsync().GetAwaiter().GetResult();
                    InstallHeadlessContext();
                    var afterTwoLoads = masterForm.TenantListLoadCount;

                    Check("24. one dashboard load reads the tenant/company list EXACTLY once",
                        afterOneLoad == 1 && afterTwoLoads == 2,
                        $"loads after 1 load={afterOneLoad}, after 2 loads={afterTwoLoads}");

                    var navigationReport = masterForm.ApplyAndDescribeMasterThemeForTestAsync().GetAwaiter().GetResult();
                    InstallHeadlessContext();
                    var afterNavigation = masterForm.TenantListLoadCount;

                    Check("25. panel navigation does NOT reload the tenant/company list",
                        afterNavigation == afterTwoLoads
                        && !navigationReport.Contains("OPEN-TIMEOUT", StringComparison.Ordinal),
                        $"loads={afterNavigation} nav=[{navigationReport}]");

                    Exception? navigationFailure = null;
                    for (var pass = 0; pass < 3 && navigationFailure == null; pass++)
                    {
                        try
                        {
                            masterForm.ApplyAndDescribeMasterThemeForTestAsync().GetAwaiter().GetResult();
                            InstallHeadlessContext();
                        }
                        catch (Exception ex)
                        {
                            navigationFailure = ex;
                        }
                    }

                    Check("26. rapid navigation neither throws nor duplicates loads",
                        navigationFailure == null && masterForm.TenantListLoadCount == afterTwoLoads,
                        navigationFailure == null
                            ? $"3 extra navigation passes, tenant-list loads still {masterForm.TenantListLoadCount}"
                            : navigationFailure.GetBaseException().Message);
                }
            }
            catch (Exception ex)
            {
                Write($"  [FAIL] harness: {ex.GetType().Name}: {ex.GetBaseException().Message}");
                failures.Add($"harness: {ex.GetType().Name}: {ex.GetBaseException().Message}");
            }

            Write(string.Empty);
            Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | checks: {checks} | skipped: {skipped.Count} | failures: {failures.Count} ---");
            foreach (var skip in skipped)
            {
                Write($"  SKIPPED {skip}");
            }
            foreach (var failure in failures)
            {
                Write("  FAILED " + failure);
            }

            return failures.Count == 0 ? 0 : 1;
        }

        /// <summary>WinForms needs a sync context for the headless awaits on the dashboard form.</summary>
        private static void InstallHeadlessContext() =>
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
    }
}
