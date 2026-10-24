using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Super Admin / tenant THEME acceptance battery (read-only).
    ///
    /// It exists because the reported defects were UI-only: two Super Admin drop-downs showed
    /// "AlphaElite.infrastructure.services.CompanySubscriptionInfo" instead of company/plan names,
    /// and several screens still painted WinForms' default LIGHT surfaces. Neither was covered by
    /// any existing suite (they verified data, not what the user sees).
    ///
    /// Checks:
    ///   1  master navigation is still exactly 3 items + Logout
    ///   2  Companies drop-down DISPLAYS the company names (never a CLR type name)
    ///   3  Companies drop-down still SELECTS by the company identifier (detail view renders)
    ///   4  Plan drop-down DISPLAYS the real plan names (never a CLR type name)
    ///   5  Plan drop-down still SELECTS by plan code and drives the plan editor
    ///   6  the subscription tab strip is owner-drawn on the dark surface
    ///   7  every Super Admin panel exposes no light surface and no type-name drop-down
    ///   8  Tenant A  : no light surface, no type-name drop-down
    ///   9  Tenant B  : no light surface, no type-name drop-down
    ///  10  Tenant C  : no light surface, no type-name drop-down
    ///
    /// It only READS the master database (plus a bitmap render of the built screens), so there is
    /// nothing to clean up. It still refuses to run against any database but the master (db67598).
    /// </summary>
    internal static class ThemeVerification
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
                var service = runtime.SubscriptionService;

                var masterDatabase = masterDb.Database.GetDbConnection().Database;
                Write("Operation      : theme-verify");
                Write($"Target database: {masterDatabase}");
                if (!masterDatabase.Contains("67598"))
                {
                    Write($"[ABORT] Refusing to run: the master connection does not point at db67598 (got '{masterDatabase}').");
                    return 1;
                }

                var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();

                // The expected company names are read from the MASTER DATABASE (never hardcoded),
                // so the drop-down is proven to show what is actually stored.
                var companyNames = masterDb.Companies.AsNoTracking()
                    .OrderBy(c => c.CompanyId)
                    .Select(c => c.CompanyName)
                    .ToList();
                var planNames = service.GetPlansAsync().GetAwaiter().GetResult()
                    .OrderBy(p => p.SubscriptionPlanId)
                    .Select(p => p.Name)
                    .ToList();
                var planCodes = service.GetPlansAsync().GetAwaiter().GetResult()
                    .OrderBy(p => p.SubscriptionPlanId)
                    .Select(p => p.Code)
                    .ToList();

                Write(string.Empty);
                Write("--- 1. Super Admin drop-down bindings ---");

                using (var masterForm = new MasterDashboardForm(runtime, new MasterUserSession
                {
                    MasterUserId = "theme-verify-superadmin",
                    Username = "superadmin",
                    Role = Roles.MasterAdmin,
                    IdentityRoleName = MasterIdentityService.SuperAdminRoleName
                }))
                {
                    InstallHeadlessContext();
                    Check("master navigation is Subscription, Companies/Tenants, Tenant Database, Business Intelligence + Logout",
                        masterForm.DescribeMasterNav() == "Subscription, Companies/Tenants, TenantDatabase, BusinessIntelligence, Logout",
                        masterForm.DescribeMasterNav());

                    // Opens the Subscription screen through its real navigation path and loads it.
                    var render = masterForm.RenderSubscriptionCompaniesForTestAsync().GetAwaiter().GetResult();
                    InstallHeadlessContext();

                    Check("Super Admin Subscription screen renders every company row (no unloaded-navigation dereference)",
                        masterForm.SubscriptionCompanyRowCountForTest == companyNames.Count && render.Contains("COMP001:"),
                        $"rows={masterForm.SubscriptionCompanyRowCountForTest} companies={companyNames.Count}");

                    Write("    .. loading the Plans & Modules tab");
                    var planLoad = masterForm.LoadPlansForTestAsync().GetAwaiter().GetResult();
                    InstallHeadlessContext();
                    Write($"    .. {planLoad}");

                    var companies = masterForm.DescribeCompanyChoicesForTest();
                    Check("Companies drop-down DISPLAYS every company/tenant name and no CLR type name",
                        companies.Contains($"count={companyNames.Count}")
                        && companyNames.All(name => companies.Contains(name))
                        && !companies.Contains("AlphaElite.")
                        && companies.Contains($"displayMember={nameof(CompanySubscriptionInfo.CompanyName)}"),
                        companies);

                    var afterSelect = masterForm.SelectCompanyForTest("COMP002");
                    Check("selecting a company still works and resolves the identifier underneath",
                        companies.Contains($"valueMember={nameof(CompanySubscriptionInfo.CompanyId)}")
                        && afterSelect.Contains("(COMP002)"),
                        afterSelect);

                    var plans = masterForm.DescribePlanChoicesForTest();
                    Check("Plan & Modules drop-down DISPLAYS the real plan names and no CLR type name",
                        plans.Contains($"count={planNames.Count}")
                        && planNames.All(name => plans.Contains(name))
                        && !plans.Contains("AlphaElite."),
                        plans);

                    var expectedEnterprise = planNames[planCodes.IndexOf("ENTERPRISE")];
                    var planEdit = masterForm.SelectPlanForTest("ENTERPRISE");
                    Check("selecting a plan still works and drives the plan editor (code stays the value)",
                        planEdit.Contains("plan=ENTERPRISE")
                        && planEdit.Contains($"name='{expectedEnterprise}'")
                        && planEdit.Contains("modules=16/16"),
                        planEdit + $" || expectedName='{expectedEnterprise}'");

                    Check("all Super Admin drop-down values are human-readable (no type name anywhere on the screen)",
                        !masterForm.DescribeCompanyChoicesForTest().Contains("CompanySubscriptionInfo")
                        && !masterForm.DescribePlanChoicesForTest().Contains("CompanySubscriptionInfo")
                        && !masterForm.DescribePlanChoicesForTest().Contains("SubscriptionPlanInfo"),
                        masterForm.DescribePlanChoicesForTest());

                    var strip = masterForm.DescribeSubscriptionTabStripForTest();
                    Check("subscription sub-navigation is app-styled (native OS tab strip hidden) over dark pages",
                        strip.Contains("appearance=FlatButtons")
                        && strip.Contains("itemSize=1")
                        && strip.Contains("Companies / Tenants:back=#181818 fore=#D4AF37")
                        && strip.Contains("Subscription Requests:back=#1C1C1C fore=#FFFFFF")
                        && !strip.Contains("#F0F0F0") && !strip.Contains("#C6D4E2")
                        && strip.Split("=").Length > 8,
                        strip);

                    Write(string.Empty);
                    Write("--- 2. Super Admin dark surfaces ---");

                    Write("    .. opening every Super Admin panel (real navigation path)");
                    var masterTheme = masterForm.ApplyAndDescribeMasterThemeForTestAsync().GetAwaiter().GetResult();
                    InstallHeadlessContext();
                    Check("every Super Admin panel exposes no light surface and no type-name drop-down",
                        !masterTheme.Contains("TIMEOUT") && !masterTheme.Contains("no panel hosted")
                        && masterTheme.Split("light=0 typename=0").Length == 5,
                        masterTheme);

                    // Informational: an actual bitmap render of the built Subscription screen.
                    Write("  [info] rendered pixels: " + RenderProbe(masterForm) + " | DateTimePicker: " + PickerProbe());
                }

                Write(string.Empty);
                Write("--- 3. Tenant A / B / C surfaces (visual only) ---");

                // Same composition the production login flow uses for the session context.
                UserSubscriptionContext BuildContext(int companyId)
                {
                    var info = service.GetCompanySubscriptionAsync(companyId).GetAwaiter().GetResult();
                    if (info == null)
                    {
                        return UserSubscriptionContext.None;
                    }

                    return new UserSubscriptionContext
                    {
                        HasActiveSubscription = info.EffectiveStatus == SubscriptionStatuses.Active,
                        EffectiveStatus = info.EffectiveStatus,
                        PlanName = info.PlanName,
                        EndDate = info.EndDate,
                        EnabledModuleCodes = info.EnabledModuleCodes
                    };
                }

                var tenantCases = new (string Label, string CompanyCode, string Username)[]
                {
                    ("Tenant A", "COMP001", "adminA"),
                    ("Tenant B", "COMP002", "adminB"),
                    ("Tenant C", "COMP003", "managerCebu")
                };

                foreach (var (label, companyCode, username) in tenantCases)
                {
                    InstallHeadlessContext();
                    var company = masterDb.Companies.AsNoTracking().First(c => c.CompanyCode == companyCode);
                    var login = runtime.AuthenticationService.AuthenticateAsync(company.CompanyId, username, "123")
                        .GetAwaiter().GetResult();
                    InstallHeadlessContext();
                    if (login == null)
                    {
                        Check($"{label}: shell renders with the dark theme", false, $"demo login '{username}' failed");
                        continue;
                    }

                    var session = new UserSession
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
                        Subscription = BuildContext(company.CompanyId)
                    };

                    using var tenantForm = new Form1(session, tenants, runtime.DataContextFactory,
                        runtime.AuthorizationService, runtime.SubscriptionService) { SuppressDialogs = true };
                    InstallHeadlessContext();

                    var theme = tenantForm.DescribeThemeForTest();
                    Check($"{label}: no light surface and no type-name drop-down",
                        theme.Contains("light=0") && theme.Contains("typename=0"),
                        theme + " || tabs=" + tenantForm.DescribeTabSurfaceForTest());
                }

                Write(string.Empty);
                Write($"--- Result: {(failures.Count == 0 ? "OK" : "FAILED")} | checks: {checks} | failures: {failures.Count} ---");
                foreach (var failure in failures)
                {
                    Write("  FAILED " + failure);
                }

                return failures.Count == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Write($"[FATAL] {ex.GetType().Name}: {ex.GetBaseException().Message}");
                Write(ex.StackTrace ?? string.Empty);
                Write($"--- Result: FAILED | checks: {checks} | failures: {failures.Count + 1} ---");
                return 1;
            }
        }

        /// <summary>
        /// Installs a PLAIN SynchronizationContext for the rest of the run. Creating a WinForms
        /// control handle installs a WindowsFormsSynchronizationContext on this thread, and every
        /// await then posts its continuation to a message loop that a headless run never pumps -
        /// which stalls the battery. AutoInstall only acts while Current is null, so keeping a
        /// plain (thread-pool posting) context in place makes every await completable.
        /// </summary>
        private static void InstallHeadlessContext() =>
            System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Threading.SynchronizationContext());

        /// <summary>
        /// Best-effort bitmap render of the built Super Admin screen. The application runs
        /// headless here, so a render that is unavailable is reported as such instead of
        /// failing the battery - the property-level checks above carry the verdict.
        /// </summary>
        private static string RenderProbe(Control control)
        {
            try
            {
                using var bitmap = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height));
                control.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

                var samples = new List<string>();
                foreach (var (label, x, y) in new (string, int, int)[]
                {
                    // The three points that used to be light: the strip above the page
                    // (left and right of the old tabs) and the page surface itself.
                    ("tabRow", 700, 96), ("tabRowRight", 960, 96), ("tabPage", 500, 400),
                    ("contentBackground", 1050, 640),
                    ("subNavActive", 100, 100), ("subNavIdle", 260, 100)
                })
                {
                    if (x < bitmap.Width && y < bitmap.Height)
                    {
                        samples.Add($"{label}={UiTheme.Hex(bitmap.GetPixel(x, y))}");
                    }
                }

                var light = 0;
                var sampled = 0;
                for (var y = 0; y < bitmap.Height; y += 4)
                {
                    for (var x = 0; x < bitmap.Width; x += 4)
                    {
                        sampled++;
                        if (UiTheme.IsLight(bitmap.GetPixel(x, y)))
                        {
                            light++;
                        }
                    }
                }

                return $"{bitmap.Width}x{bitmap.Height} lightPixels={light}/{sampled} " +
                    $"({(sampled == 0 ? 0 : 100.0 * light / sampled):0.0}% - includes light text) " +
                    string.Join(" ", samples);
            }
            catch (Exception ex)
            {
                return $"unavailable ({ex.GetType().Name}: {ex.GetBaseException().Message})";
            }
        }

        /// <summary>
        /// Informational: documents that a DateTimePicker's field is painted by the OS and stays
        /// light even when its BackColor is set (so the theme keeps dark text on it and only themes
        /// the drop-down calendar). Not a pass/fail check - the light-surface scan deliberately
        /// excludes this OS-drawn surface.
        /// </summary>
        private static string PickerProbe()
        {
            try
            {
                using var host = new Panel { BackColor = UiTheme.Background, Width = 220, Height = 60 };
                var picker = new DateTimePicker
                {
                    Location = new Point(10, 6),
                    Size = new Size(140, 24),
                    BackColor = UiTheme.Background,
                    ForeColor = UiTheme.Text
                };
                host.Controls.Add(picker);
                host.CreateControl();
                picker.CreateControl();

                using var bitmap = new Bitmap(host.Width, host.Height);
                host.DrawToBitmap(bitmap, new Rectangle(0, 0, host.Width, host.Height));
                // Sample inside the picker's text field (left of its calendar/up-down glyphs).
                var fieldPixel = bitmap.GetPixel(60, 18);
                return $"fieldIsOsDrawn={UiTheme.IsLight(fieldPixel)} field={UiTheme.Hex(fieldPixel)} (dark text kept)";
            }
            catch (Exception ex)
            {
                return $"unavailable ({ex.GetType().Name}: {ex.GetBaseException().Message})";
            }
        }
    }
}
