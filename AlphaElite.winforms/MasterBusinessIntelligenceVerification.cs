using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AlphaElite.winforms
{
    /// <summary>
    /// SUPER ADMIN BUSINESS INTELLIGENCE acceptance battery (headless, read-only).
    ///
    /// Proves the delivered Business Intelligence area:
    ///   * it is a top-level SUPER ADMIN page (and the tenant window has no path to it),
    ///   * its six KPI cards render REAL master-database values (recomputed independently here),
    ///   * its four charts use real master data with the mandated categories - the plan chart
    ///     holds exactly Basic / Professional / Enterprise and never a removed plan,
    ///   * there is NO export control of any kind (display-only),
    ///   * no obsolete plan terminology and no CLR type name is visible anywhere on it,
    ///   * opening and re-loading it writes nothing (row counts are identical afterwards).
    ///
    /// The master database must be db67598 or the run aborts before touching anything. Tenant
    /// databases are never opened by this battery.
    /// </summary>
    internal static class MasterBusinessIntelligenceVerification
    {
        public static int Run()
        {
            var failures = new List<string>();
            var checks = 0;

            void Write(string text) => Console.WriteLine(text);

            void Check(string name, bool ok, string detail)
            {
                checks++;
                Write($"  [{(ok ? "PASS" : "FAIL")}] {checks}. {name}: {detail}");
                if (!ok)
                {
                    failures.Add($"{checks}. {name}: {detail}");
                }
            }

            try
            {
                ApplicationConfiguration.Initialize();
                InstallHeadlessContext();

                var runtime = AppRuntime.Create();
                var masterDb = runtime.MasterDb;

                var masterDatabase = masterDb.Database.GetDbConnection().Database;
                Write("Operation      : super-admin-bi-verify");
                Write($"Target database: {masterDatabase}");
                if (!masterDatabase.Contains("67598"))
                {
                    Write($"[ABORT] Refusing to run: the master connection does not point at db67598 (got '{masterDatabase}').");
                    return 1;
                }

                // ---- Read-only baseline (the BI page must not write anything) --------------------
                int Plans() => masterDb.SubscriptionPlans.AsNoTracking().Count();
                int Modules() => masterDb.SoftwareModules.AsNoTracking().Count();
                int Mappings() => masterDb.PlanModules.AsNoTracking().Count();
                int Versions() => masterDb.SubscriptionPlanVersions.AsNoTracking().Count();
                int VersionModules() => masterDb.SubscriptionPlanVersionModules.AsNoTracking().Count();
                int Subs() => masterDb.CompanySubscriptions.AsNoTracking().Count();
                int Requests() => masterDb.SubscriptionChangeRequests.AsNoTracking().Count();
                int History() => masterDb.SubscriptionHistories.AsNoTracking().Count();

                var beforeFingerprint = (Plans(), Modules(), Mappings(), Versions(), VersionModules(), Subs(), Requests(), History());

                // ---- Independent recomputation of the expected KPI/chart values ------------------
                var companiesExpected = masterDb.Companies.AsNoTracking().Count();
                var plansExpected = masterDb.SubscriptionPlans.AsNoTracking().OrderBy(p => p.SubscriptionPlanId).ToList();
                var subsExpected = masterDb.CompanySubscriptions.AsNoTracking().ToList();
                var activeExpected = subsExpected.Count(s => s.Status == SubscriptionStatuses.Active);
                var pendingExpected = masterDb.SubscriptionChangeRequests.AsNoTracking()
                    .Count(r => r.Status == SubscriptionChangeRequestStatuses.Pending);
                var expiredExpected = subsExpected.Count(s => s.Status == SubscriptionStatuses.Expired);
                var paidExpected = subsExpected
                    .Where(s => string.Equals(s.PaymentStatus, PaymentStatuses.Paid, StringComparison.Ordinal)).ToList();
                var totalRevenueExpected = paidExpected.Sum(s => s.Price);

                // ============================================================================
                // SECTION 1 - navigation: Super Admin sees BI, tenants cannot
                // ============================================================================
                Write(string.Empty);
                Write("--- 1. Business Intelligence is a top-level Super Admin page ---");

                using var masterForm = new MasterDashboardForm(runtime, new MasterUserSession
                {
                    MasterUserId = "bi-verify-superadmin",
                    Username = "superadmin",
                    Role = Roles.MasterAdmin,
                    IdentityRoleName = MasterIdentityService.SuperAdminRoleName
                });
                InstallHeadlessContext();

                var nav = masterForm.DescribeMasterNav();
                Check("Super Admin navigation contains the top-level Business Intelligence entry",
                    nav.Split(new[] { ", " }, StringSplitOptions.None)
                        .SequenceEqual(new[] { "Subscription", "Companies/Tenants", "TenantDatabase", "BusinessIntelligence", "Logout" }),
                    nav);
                Check("Subscription remains ONE section (Business Intelligence is NOT a sub-section of it)",
                    nav.Split(new[] { ", " }, StringSplitOptions.None).Take(3).Contains("BusinessIntelligence") == false,
                    nav);

                var masterFormNeedsMasterSession = typeof(MasterDashboardForm).GetConstructors()
                    .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(MasterUserSession)));
                var tenantHasBusinessIntelligenceSurface = typeof(Form1)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(m => m.Name.Contains("BusinessIntelligenceForTest", StringComparison.Ordinal)
                              || m.Name.Contains("BusinessIntelligencePanel", StringComparison.Ordinal));
                Check("the tenant window has NO Business Intelligence surface and the master window is session-gated",
                    !tenantHasBusinessIntelligenceSurface && masterFormNeedsMasterSession,
                    $"tenantSurface={tenantHasBusinessIntelligenceSurface} masterWindowGated={masterFormNeedsMasterSession}");

                // Tenant A / B / C windows: none of them may carry the Super Admin BI entry.
                var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();
                var tenantBleed = new List<string>();
                foreach (var (code, username) in new[] { ("COMP001", "adminA"), ("COMP002", "adminB"), ("COMP003", "managerCebu") })
                {
                    InstallHeadlessContext();
                    var login = runtime.AuthenticationService.AuthenticateAsync(
                        masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == code).CompanyId, username, "123").GetAwaiter().GetResult();
                    if (login == null)
                    {
                        tenantBleed.Add($"{code}: login failed");
                        continue;
                    }

                    var tenantForm = new Form1(new UserSession
                    {
                        CompanyId = login.CompanyId,
                        CompanyCode = login.CompanyCode,
                        CompanyName = login.CompanyName,
                        IsBranchAware = login.IsBranchAware,
                        BranchId = login.BranchId,
                        MemberId = login.MemberId,
                        Username = login.Username,
                        FullName = login.FullName,
                        Role = login.Role,
                        Subscription = UserSubscriptionContext.None
                    }, tenants, runtime.DataContextFactory, runtime.AuthorizationService, runtime.SubscriptionService)
                    {
                        SuppressDialogs = true
                    };
                    InstallHeadlessContext();
                    tenantForm.Dispose();

                    var methods = typeof(Form1)
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Select(m => m.Name)
                        .ToList();
                    if (methods.Any(m => m.Contains("MasterDashboard", StringComparison.OrdinalIgnoreCase)))
                    {
                        tenantBleed.Add($"{code}: exposes a MasterDashboard member");
                    }
                }

                Check("no tenant window can open the Super Admin Business Intelligence page",
                    tenantBleed.Count == 0,
                    tenantBleed.Count == 0 ? "COMP001 / COMP002 / COMP003 checked" : string.Join("; ", tenantBleed));

                // ============================================================================
                // SECTION 2 - the six KPI cards
                // ============================================================================
                Write(string.Empty);
                Write("--- 2. The six KPI cards (real master data) ---");

                var description = masterForm.OpenBusinessIntelligenceForTestAsync().GetAwaiter().GetResult();
                InstallHeadlessContext();
                var kpis = masterForm.DescribeBusinessIntelligenceKpisForTest();
                Write("    .. " + kpis);

                var kpiCount = description.Contains("kpi=[") && description.Contains("|")
                    ? description.Substring(description.IndexOf("kpi=[", StringComparison.Ordinal) + 5)
                        .Split('|').Length
                    : 0;
                Check("exactly six KPI cards render with a value (never blank)",
                    kpiCount == 6 && description.Contains("Total Companies / Tenants=")
                    && !description.Contains("=\u2026]") && !description.Contains("=]"),
                    $"kpiCards={kpiCount}");

                Check("KPI 1 - Total Companies / Tenants equals the master company count",
                    kpis.Contains($"companies={companiesExpected}"), kpis);
                Check("KPI 2 - Active Subscriptions equals the Active CompanySubscription count",
                    kpis.Contains($"active={activeExpected}"), kpis);
                Check("KPI 3 - Pending Subscription Requests equals the Pending request count",
                    kpis.Contains($"pending={pendingExpected}"), kpis);
                Check("KPI 4 - Expired Subscriptions equals the Expired count",
                    kpis.Contains($"expired={expiredExpected}"), kpis);
                Check("KPI 5 - Total Subscription Revenue equals the paid subscription amounts",
                    kpis.Contains($"totalRevenue={totalRevenueExpected:0.00}"), kpis);
                Check("zero is rendered as 0 (not blank / NaN / error)",
                    kpis.Contains("=") && !kpis.Contains("NaN") && !kpis.Contains("error") && !kpis.Contains("=)"),
                    kpis);

                // ============================================================================
                // SECTION 3 - the four charts
                // ============================================================================
                Write(string.Empty);
                Write("--- 3. The four subscription-focused charts ---");

                var planSeries = ExtractSeries(description, "plans=");
                var revenueSeries = ExtractSeries(description, "revenue=");
                var statusSeries = ExtractSeries(description, "status=");
                var paymentSeries = ExtractSeries(description, "payment=");

                var planNames = plansExpected.Select(p => p.Name).ToList();
                var planCounts = plansExpected
                    .Select(p => subsExpected.Count(s => s.SubscriptionPlanId == p.SubscriptionPlanId)).ToList();

                Check("A. Subscription Plans Overview has exactly the 3 plan categories (Basic / Professional / Enterprise)",
                    planSeries.Categories.SequenceEqual(planNames) && planSeries.Categories.Count == 3,
                    $"cats=[{string.Join(",", planSeries.Categories)}]");
                Check("A2. the plan chart counts come from CompanySubscriptions and never from a removed plan",
                    planSeries.Values.SequenceEqual(planCounts.Select(c => c.ToString()).ToList())
                    && !planSeries.Categories.Any(c => c.Contains("Micro", StringComparison.OrdinalIgnoreCase)
                                                       || c.Contains("Small", StringComparison.OrdinalIgnoreCase)),
                    $"vals=[{string.Join(",", planSeries.Values)}] expected=[{string.Join(",", planCounts)}]");

                Check("B. Subscription Revenue over time uses real monthly buckets (one value per category, no fabrication)",
                    revenueSeries.Categories.Count > 0
                    && revenueSeries.Categories.Count == revenueSeries.Values.Count
                    && revenueSeries.Values.All(v => v != "NaN"),
                    $"cats={revenueSeries.Categories.Count} vals=[{string.Join(",", revenueSeries.Values)}]");

                var statusCounts = SubscriptionStatuses.All
                    .Select(status => subsExpected.Count(s => s.Status == status)).ToList();
                Check("C. Subscription Status uses real status data (Active/Pending/Expired/Suspended/Cancelled)",
                    statusSeries.Categories.SequenceEqual(SubscriptionStatuses.All)
                    && statusSeries.Values.SequenceEqual(statusCounts.Select(c => c.ToString()).ToList()),
                    $"cats=[{string.Join(",", statusSeries.Categories)}] vals=[{string.Join(",", statusSeries.Values)}]");

                var paymentCounts = PaymentStatuses.All
                    .Select(status => subsExpected.Count(s => s.PaymentStatus == status)).ToList();
                Check("D. Payment Overview uses real payment-status data (no invented payment records)",
                    paymentSeries.Categories.SequenceEqual(PaymentStatuses.All)
                    && paymentSeries.Values.SequenceEqual(paymentCounts.Select(c => c.ToString()).ToList()),
                    $"cats=[{string.Join(",", paymentSeries.Categories)}] vals=[{string.Join(",", paymentSeries.Values)}]");

                // ============================================================================
                // SECTION 4 - display-only, terminology, type names
                // ============================================================================
                Write(string.Empty);
                Write("--- 4. Display-only, terminology and type names ---");

                var exportControls = ExtractAfter(description, "exportControls=");
                var buttonCount = ExtractAfter(description, "buttons=");
                Check("no export surface exists (no Export / PDF / CSV / Excel control) and the page has no buttons at all",
                    exportControls.StartsWith("0", StringComparison.Ordinal) && buttonCount.StartsWith("0", StringComparison.Ordinal),
                    $"exportControls={exportControls.Split(' ')[0]} buttons={buttonCount.Split(' ')[0]}");

                Check("no obsolete plan terminology is visible on the BI page",
                    !description.Contains("Micro Business", StringComparison.OrdinalIgnoreCase)
                    && !description.Contains("Small Business", StringComparison.OrdinalIgnoreCase)
                    && !description.Contains("MICRO_BUSINESS", StringComparison.Ordinal)
                    && !description.Contains("SMALL_BUSINESS", StringComparison.Ordinal),
                    "Micro/Small Business absent from cards, charts and captions");

                Check("no C# class / namespace name is visible on the BI page",
                    !description.Contains("AlphaElite.", StringComparison.Ordinal)
                    && description.Contains("typename=0")
                    && !description.Contains("SubscriptionPlanInfo")
                    && !description.Contains("CompanySubscriptionInfo"),
                    description);

                Check("the BI page is dark (no light surface)",
                    description.Contains("light=0"),
                    description);

                // ============================================================================
                // SECTION 4b - rapid navigation across ALL FOUR areas, Business Intelligence
                //              included (Subscription -> Companies/Tenants -> Tenant Database ->
                //              Business Intelligence -> Subscription)
                // ============================================================================
                Write(string.Empty);
                Write("--- 4b. Rapid switching across all four Super Admin areas ---");

                var navProbe = masterForm.RunRapidMasterAreaLoadsForTestAsync().GetAwaiter().GetResult();
                InstallHeadlessContext();
                Write("    .. " + navProbe);

                var navElapsedMs = 0L;
                var elapsedIndex = navProbe.IndexOf("elapsedMs=", StringComparison.Ordinal);
                if (elapsedIndex >= 0)
                {
                    var rest = navProbe[(elapsedIndex + "elapsedMs=".Length)..];
                    var end = rest.IndexOf(' ');
                    _ = long.TryParse(end < 0 ? rest : rest[..end], out navElapsedMs);
                }

                Check("rapid switching across all four areas (including Business Intelligence) raises no DbContext concurrency exception",
                    navProbe.Contains("waves=3") && navProbe.Contains("tenantsLoaded=3"), navProbe);
                Check("rapid switching performs NO duplicate load (tenant list read delta 0; BI charts still bound; every area loaded real rows)",
                    navProbe.Contains("tenantListLoadDelta=0") && navProbe.Contains("biCharts=4")
                    && navProbe.Contains("tenantsLoaded=3") && navProbe.Contains(" companies=3 "), navProbe);
                Check("rapid top-level switching does not freeze the UI (well under the 60s gate-stall bound)",
                    navElapsedMs > 0 && navElapsedMs < 60000, $"elapsedMs={navElapsedMs}");
                Check("the Business Intelligence KPIs are the real master values after the navigation cycle",
                    navProbe.Contains("biKpis='companies=3") && navProbe.Contains($"active={activeExpected}"), navProbe);

                // ============================================================================
                // SECTION 5 - read-only (no writes, idempotent refresh)
                // ============================================================================
                Write(string.Empty);
                Write("--- 5. Read-only (no writes, idempotent refresh) ---");

                masterForm.LoadBusinessIntelligenceForTestAsync().GetAwaiter().GetResult();
                InstallHeadlessContext();
                var second = masterForm.DescribeBusinessIntelligenceKpisForTest();

                var afterFingerprint = (Plans(), Modules(), Mappings(), Versions(), VersionModules(), Subs(), Requests(), History());
                Check("opening and re-loading the BI page wrote nothing to the master database",
                    beforeFingerprint == afterFingerprint,
                    $"before={beforeFingerprint} after={afterFingerprint}");
                Check("the BI refresh is deterministic (same values on the second load)",
                    kpis == second, $"first='{kpis}' second='{second}'");
            }
            catch (Exception ex)
            {
                Write($"  [FAIL] harness: {ex.GetType().Name}: {ex.GetBaseException().Message}");
                failures.Add($"harness: {ex.GetType().Name}: {ex.GetBaseException().Message}");
            }

            Write(string.Empty);
            Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | checks: {checks} | failures: {failures.Count} ---");
            foreach (var failure in failures)
            {
                Write("  FAILED " + failure);
            }

            return failures.Count == 0 ? 0 : 1;
        }

        /// <summary>
        /// Installs a PLAIN SynchronizationContext (the established harness pattern). It must not be
        /// null: <c>Control.CreateHandle</c> installs a WindowsFormsSynchronizationContext whenever
        /// the current context is null, and its continuations need a message loop a headless run
        /// never pumps - which would deadlock the blocking <c>GetResult()</c> below. With a plain
        /// context already in place, WinForms leaves it alone and continuations go to the pool.
        /// </summary>
        private static void InstallHeadlessContext() =>
            System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Threading.SynchronizationContext());

        /// <summary>Parses "{cats=[a,b] vals=[1,2]}" following a chart key into its parts.</summary>
        private static (List<string> Categories, List<string> Values) ExtractSeries(string description, string key)
        {
            var start = description.IndexOf(key + "{cats=[", StringComparison.Ordinal);
            if (start < 0)
            {
                return (new List<string>(), new List<string>());
            }

            var catsStart = start + key.Length + "{cats=[".Length;
            var catsEnd = description.IndexOf(']', catsStart);
            var valsStart = description.IndexOf("vals=[", catsEnd, StringComparison.Ordinal) + "vals=[".Length;
            var valsEnd = description.IndexOf(']', valsStart);

            string Slice(int from, int to) => from < 0 || to < from ? string.Empty : description.Substring(from, to - from);

            var cats = Slice(catsStart, catsEnd)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            var vals = Slice(valsStart, valsEnd)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            return (cats, vals);
        }

        private static string ExtractAfter(string description, string key)
        {
            var index = description.IndexOf(key, StringComparison.Ordinal);
            if (index < 0)
            {
                return "(missing)";
            }

            var rest = description[(index + key.Length)..];
            var end = rest.IndexOfAny(new[] { ' ', '\n' });
            return end < 0 ? rest : rest[..end];
        }
    }
}
