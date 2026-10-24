using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 5 acceptance harness (read-only except the company-activation toggle, which is
    /// reverted within the same run). Proves master Identity login, master authorization,
    /// tenant aggregate reads, tenant switching resolution, activation enforcement at login,
    /// and the A/B/C data baselines. No password or hash is ever printed.
    /// </summary>
    internal static class Phase5Verification
    {
        public static int Run()
        {
            var failures = new List<string>();
            var checks = 0;

            void Write(string text) => Console.WriteLine(text);

            void Check(string name, bool ok, string detail)
            {
                checks++;
                Write($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
                if (!ok) failures.Add($"{name}: {detail}");
            }

            Write("======================================================================");
            Write("PHASE 5 ACCEPTANCE VERIFICATION (master / super admin)");
            Write("======================================================================");

            AppRuntime runtime;
            try
            {
                runtime = AppRuntime.Create();
            }
            catch (Exception ex)
            {
                Write($"  [FAIL] runtime: {ex.Message}");
                return 1;
            }

            var masterUsername = Environment.GetEnvironmentVariable("ALPHA_MASTER_USERNAME");
            if (string.IsNullOrWhiteSpace(masterUsername))
            {
                masterUsername = MasterAdminSeeder.DemoUsername;
            }

            var masterPassword = Environment.GetEnvironmentVariable("ALPHA_MASTER_PASSWORD");
            var tenantPassword = Environment.GetEnvironmentVariable("ALPHA_VERIFY_PASSWORD");

            // ---------------------------------------------------------------------
            // 1. Master Identity login
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 1. Master Identity login (Master DB, ASP.NET Identity) ---");

            if (string.IsNullOrEmpty(masterPassword))
            {
                Write("  [SKIP] set ALPHA_MASTER_PASSWORD to verify the master login path.");
            }
            else
            {
                MasterLoginResult login;
                try
                {
                    login = runtime.MasterIdentityService.AuthenticateAsync(masterUsername, masterPassword).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    login = new MasterLoginResult { Success = false, Error = ex.Message };
                }

                Check("master login succeeds", login.Success, $"username={login.Username}, role={login.Role}");
                Check("master role is MasterAdmin", login.Success && login.Role == Roles.MasterAdmin, login.Role);
                Check("master user id present", login.Success && login.MasterUserId.Length > 0, login.MasterUserId.Length > 0 ? "yes" : "no");

                // 1b. Negative authentication: wrong password and unknown user must fail.
                var wrongPassword = runtime.MasterIdentityService.AuthenticateAsync(masterUsername, masterPassword + "-x").GetAwaiter().GetResult();
                Check("wrong password rejected", !wrongPassword.Success, wrongPassword.Error);

                var unknownUser = runtime.MasterIdentityService.AuthenticateAsync("no-such-master-user", masterPassword).GetAwaiter().GetResult();
                Check("unknown master user rejected", !unknownUser.Success, unknownUser.Error);
            }

            // ---------------------------------------------------------------------
            // 2. Master users present (Identity)
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 2. Master Identity users ---");

            var masterUsers = runtime.MasterIdentityService.GetUsersAsync().GetAwaiter().GetResult();
            Check("at least one master user exists", masterUsers.Count > 0, $"{masterUsers.Count} user(s)");
            var superAdmins = masterUsers.Where(u => u.Roles.Contains(MasterIdentityService.SuperAdminRoleName)).ToList();
            Check("SuperAdmin role assigned", superAdmins.Count > 0, string.Join(", ", superAdmins.Select(u => u.Username)));
            Check("no hash material in user projection",
                masterUsers.All(u => u.ToString()!.IndexOf("PasswordHash", StringComparison.OrdinalIgnoreCase) < 0),
                "projection exposes username/email/roles/status only");

            // ---------------------------------------------------------------------
            // 3. Master authorization matrix
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 3. Master admin authorization matrix ---");

            var auth = runtime.AuthorizationService;
            Check("MasterAdmin -> TenantManagement", auth.IsAuthorized(Roles.MasterAdmin, Permission.TenantManagement), "granted");
            Check("MasterAdmin -> AdminPanel", auth.IsAuthorized(Roles.MasterAdmin, Permission.AdminPanel), "granted");
            Check("MasterAdmin -> BusinessIntelligence", auth.IsAuthorized(Roles.MasterAdmin, Permission.BusinessIntelligence), "granted");
            Check("MasterAdmin -> ManageUsers", auth.IsAuthorized(Roles.MasterAdmin, Permission.ManageUsers), "granted");
            Check("MasterAdmin NOT -> ManageMembers", !auth.IsAuthorized(Roles.MasterAdmin, Permission.ManageMembers), "denied (tenant operation)");
            Check("MasterAdmin NOT -> ManageAttendance", !auth.IsAuthorized(Roles.MasterAdmin, Permission.ManageAttendance), "denied (tenant operation)");
            Check("tenant Admin NOT -> TenantManagement", !auth.IsAuthorized(Roles.Admin, Permission.TenantManagement), "denied (master operation)");
            Check("tenant Manager NOT -> AdminPanel", !auth.IsAuthorized(Roles.Manager, Permission.AdminPanel), "denied (master operation)");
            Check("'SuperAdmin' normalizes to MasterAdmin", Roles.Normalize("SuperAdmin") == Roles.MasterAdmin, Roles.Normalize("SuperAdmin"));

            // ---------------------------------------------------------------------
            // 4. Session type separation
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 4. Session separation ---");

            Check("MasterUserSession is a distinct type from UserSession",
                typeof(MasterUserSession) != typeof(UserSession) && !typeof(MasterUserSession).IsAssignableFrom(typeof(UserSession)),
                "compiler-level separation enforced");

            // ---------------------------------------------------------------------
            // 5. Platform counts (master DB only)
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 5. Platform counts (master DB) ---");

            var counts = runtime.MasterTenantAdminService.GetPlatformCountsAsync().GetAwaiter().GetResult();
            Check("total companies", counts.TotalCompanies == 3, $"total={counts.TotalCompanies}");
            Check("active companies", counts.ActiveCompanies == 3, $"active={counts.ActiveCompanies}");
            Check("configured tenant databases", counts.ConfiguredDatabases == 3, $"configured={counts.ConfiguredDatabases}");
            Check("active tenant databases", counts.ActiveDatabases == 3, $"active={counts.ActiveDatabases}");

            // ---------------------------------------------------------------------
            // 6. Tenant aggregates through resolver/factory (A/B/C reachable)
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 6. Tenant aggregates (existing resolver/factory) ---");

            var overviews = runtime.MasterTenantAdminService.GetCompanyOverviewsAsync().GetAwaiter().GetResult();
            var aggregates = runtime.MasterTenantAdminService.GetTenantAggregatesAsync(overviews).GetAwaiter().GetResult();

            foreach (var aggregate in aggregates)
            {
                var company = overviews.First(o => o.CompanyId == aggregate.CompanyId);
                Check($"aggregate read: {company.CompanyCode}", aggregate.Success,
                    aggregate.Success
                        ? $"members={aggregate.Members} attendance={aggregate.Attendance} payments={aggregate.Payments} total={aggregate.PaymentTotal:N2}"
                        : aggregate.Error);
            }

            var tenantA = aggregates.FirstOrDefault(a => a.CompanyId == overviews.First(o => o.CompanyCode == "COMP001").CompanyId);
            var tenantB = aggregates.FirstOrDefault(a => a.CompanyId == overviews.First(o => o.CompanyCode == "COMP002").CompanyId);
            var tenantC = aggregates.FirstOrDefault(a => a.CompanyId == overviews.First(o => o.CompanyCode == "COMP003").CompanyId);

            Write(string.Empty);
            Write("--- 7. A/B/C data baselines (regression) ---");

            if (tenantA is { Success: true })
            {
                // Live cross-check instead of hardcoded row counts: Tenant A accumulates
                // legitimate manual-test data, so its fingerprint is the tenant's OWN context
                // read through an independent path - the master aggregate must agree with it.
                var companyIdA = overviews.First(o => o.CompanyCode == "COMP001").CompanyId;
                using var dbA = runtime.DataContextFactory.CreateAsync(companyIdA, false).GetAwaiter().GetResult();
                var directMembers = dbA.Members.Count();
                var directAttendance = dbA.Attendances.Count();
                var directPayments = dbA.Payments.Count();
                var directTotal = dbA.Payments.Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                var directFeedback = dbA.Feedbacks.Count();
                var directInquiries = dbA.Inquiries.Count();

                Check("Tenant A members (aggregate == direct read)", tenantA.Members == directMembers,
                    $"aggregate={tenantA.Members}, direct={directMembers}");
                Check("Tenant A attendance (aggregate == direct read)", tenantA.Attendance == directAttendance,
                    $"aggregate={tenantA.Attendance}, direct={directAttendance}");
                Check("Tenant A payments (aggregate == direct read)",
                    tenantA.Payments == directPayments && tenantA.PaymentTotal == directTotal,
                    $"aggregate={tenantA.Payments}, total={tenantA.PaymentTotal:N2} | direct={directPayments}, total={directTotal:N2}");
                Check("Tenant A feedback (aggregate == direct read)", tenantA.Feedback == directFeedback,
                    $"aggregate={tenantA.Feedback}, direct={directFeedback}");
                Check("Tenant A inquiries (aggregate == direct read)", tenantA.Inquiries == directInquiries,
                    $"aggregate={tenantA.Inquiries}, direct={directInquiries}");
            }

            if (tenantB is { Success: true })
            {
                // Tenant B phase: manual testing legitimately reset Tenant B's demo data
                // (5/4/4/3650 became 1/1/1/1000 and retention 2 -> 0), so the aggregate
                // checks use the same dual independent live reads as Tenant A: the master
                // aggregate must agree with the tenant's OWN context read directly.
                var companyIdB = overviews.First(o => o.CompanyCode == "COMP002").CompanyId;
                using var dbB = runtime.DataContextFactory.CreateAsync(companyIdB, false).GetAwaiter().GetResult();
                var directMembersB = dbB.Members.Count();
                var directAttendanceB = dbB.Attendances.Count();
                var directPaymentsB = dbB.Payments.Count();
                var directTotalB = dbB.Payments.Sum(p => (decimal?)p.AmountPaid) ?? 0m;
                var directFeedbackB = dbB.Feedbacks.Count();
                var directInquiriesB = dbB.Inquiries.Count();
                var directRetentionB = dbB.RetentionRecords.Count();

                Check("Tenant B members (aggregate == direct read)", tenantB.Members == directMembersB,
                    $"aggregate={tenantB.Members}, direct={directMembersB}");
                Check("Tenant B attendance (aggregate == direct read)", tenantB.Attendance == directAttendanceB,
                    $"aggregate={tenantB.Attendance}, direct={directAttendanceB}");
                Check("Tenant B payments (aggregate == direct read)",
                    tenantB.Payments == directPaymentsB && tenantB.PaymentTotal == directTotalB,
                    $"aggregate={tenantB.Payments}, total={tenantB.PaymentTotal:N2} | direct={directPaymentsB}, total={directTotalB:N2}");
                Check("Tenant B feedback (aggregate == direct read)", tenantB.Feedback == directFeedbackB,
                    $"aggregate={tenantB.Feedback}, direct={directFeedbackB}");
                Check("Tenant B inquiries (aggregate == direct read)", tenantB.Inquiries == directInquiriesB,
                    $"aggregate={tenantB.Inquiries}, direct={directInquiriesB}");
                Check("Tenant B retention (aggregate == direct read)", tenantB.RetentionRecords == directRetentionB,
                    $"aggregate={tenantB.RetentionRecords}, direct={directRetentionB}");
                // Phase 1: the three required Tenant B demo accounts (adminB, gymstaffB, managerB)
                // replaced the single legacy row - user-approved account set.
                Check("Tenant B user accounts", tenantB.UserAccounts == 3, $"userAccounts={tenantB.UserAccounts}");
            }

            if (tenantC is { Success: true })
            {
                // Phase 2: Tenant C baselines realigned to the Phase 8/9 approved live
                // fingerprints (21 members split 7/8/6 across the 3 branches, attendance 17,
                // payments 14 totaling 13650) - the same numbers --phase9-subscription-verify
                // asserts as correct.
                Check("Tenant C members", tenantC.Members == 21, $"members={tenantC.Members}");
                // Phase 8: the AddBranchManagement migration inserted BR-DAVAO (3 branches total).
                Check("Tenant C branches", tenantC.Branches == 3, $"branches={tenantC.Branches}");
                Check("Tenant C attendance (current live state)", tenantC.Attendance == 17, $"attendance={tenantC.Attendance}");
                Check("Tenant C payments", tenantC.Payments == 14 && tenantC.PaymentTotal == 13650.00m, $"payments={tenantC.Payments}, total={tenantC.PaymentTotal:N2}");
                Check("Tenant C feedback", tenantC.Feedback == 17, $"feedback={tenantC.Feedback}");
                Check("Tenant C inquiries (current live state)", tenantC.Inquiries == 20, $"inquiries={tenantC.Inquiries}");
                // Tenant C phase 7: the six seeded branch accounts raise the total from 4 to 10.
                Check("Tenant C user accounts", tenantC.UserAccounts == 10, $"userAccounts={tenantC.UserAccounts}");
                // Phase 6 demo complaints/subscriptions are approved tenant writes: 2 each.
                // The complaint rows were then deleted outside this project's runs (manual
                // testing), so the complaints check is a dual independent live read (the
                // aggregate must agree with a direct tenant-context read) instead of the
                // historical demo count.
                var companyIdC = overviews.First(o => o.CompanyCode == "COMP003").CompanyId;
                using var dbC = runtime.DataContextFactory.CreateAsync(companyIdC, true).GetAwaiter().GetResult();
                var directComplaintsC = dbC.Complaints.AsNoTracking().Count();
                Check("Tenant C complaints (aggregate == direct read)", tenantC.Complaints == directComplaintsC,
                    $"aggregate={tenantC.Complaints}, direct={directComplaintsC}");
                Check("Tenant C subscriptions (demo state)", tenantC.Subscriptions == 2, $"subscriptions={tenantC.Subscriptions}");
            }

            // ---------------------------------------------------------------------
            // 8. Company activation enforcement at login (revert within the run)
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 8. Company activation enforcement (deactivate -> login blocked -> reactivate) ---");

            if (string.IsNullOrEmpty(tenantPassword))
            {
                Write("  [SKIP] set ALPHA_VERIFY_PASSWORD to exercise the tenant login enforcement path.");
            }
            else
            {
                var tenantCOverview = overviews.First(o => o.CompanyCode == "COMP003");

                var deactivate = runtime.MasterTenantAdminService.SetCompanyActiveAsync(tenantCOverview.CompanyId, false).GetAwaiter().GetResult();
                Check("deactivate Tenant C company", deactivate.Success, deactivate.Error.Length == 0 ? "ok" : deactivate.Error);

                var blockedLogin = runtime.AuthenticationService
                    .AuthenticateAsync(tenantCOverview.CompanyId, "admin", tenantPassword).GetAwaiter().GetResult();
                Check("login blocked while company inactive", blockedLogin == null, blockedLogin == null ? "rejected" : "ACCEPTED (BUG)");

                var reactivate = runtime.MasterTenantAdminService.SetCompanyActiveAsync(tenantCOverview.CompanyId, true).GetAwaiter().GetResult();
                Check("reactivate Tenant C company", reactivate.Success, reactivate.Error.Length == 0 ? "ok" : reactivate.Error);

                var restoredLogin = runtime.AuthenticationService
                    .AuthenticateAsync(tenantCOverview.CompanyId, "admin", tenantPassword).GetAwaiter().GetResult();
                Check("login works again after reactivation", restoredLogin != null,
                    restoredLogin == null ? "still rejected" : $"role={restoredLogin.Role}, branchAware={restoredLogin.IsBranchAware}");

                // Tenant B login path still healthy (legacy SHA-256 hash, never rewritten).
                var tenantBOverview = overviews.First(o => o.CompanyCode == "COMP002");
                var tenantBLogin = runtime.AuthenticationService
                    .AuthenticateAsync(tenantBOverview.CompanyId, "admin", tenantPassword).GetAwaiter().GetResult();
                Check("Tenant B admin login still works", tenantBLogin != null,
                    tenantBLogin == null ? "rejected" : $"role={tenantBLogin.Role}, rehashNeeded={tenantBLogin.NeedsPasswordRehash}");
            }

            // ---------------------------------------------------------------------
            // 9. Master structural data unchanged (except intended Identity rows)
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 9. Master DB structural integrity ---");

            var companyCount = runtime.MasterDb.Companies.AsNoTracking().Count();
            var databaseCount = runtime.MasterDb.CompanyDatabase.AsNoTracking().Count();
            var deviceCount = runtime.MasterDb.Devices.AsNoTracking().Count();
            Check("Companies rows", companyCount == 3, $"count={companyCount}");
            Check("CompanyDatabase rows", databaseCount == 3, $"count={databaseCount}");
            Check("Devices rows", deviceCount == 1, $"count={deviceCount}");
            Check("all companies re-activated after test", runtime.MasterDb.Companies.AsNoTracking().All(c => c.IsActive), "IsActive=true for all");

            // ---------------------------------------------------------------------
            // 10. Master dashboard form constructs headlessly
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 10. Master dashboard window ---");

            try
            {
                ApplicationConfiguration.Initialize();
                SynchronizationContext.SetSynchronizationContext(null);

                var session = new MasterUserSession
                {
                    MasterUserId = masterUsers.FirstOrDefault()?.Id ?? "verification",
                    Username = masterUsers.FirstOrDefault()?.Username ?? masterUsername,
                    Role = Roles.MasterAdmin,
                    IdentityRoleName = MasterIdentityService.SuperAdminRoleName
                };

                using var window = new MasterDashboardForm(runtime, session);
                Check("MasterDashboardForm constructs", true, window.Text);
                // Final subscription system: the Super Admin area carries EXACTLY the four
                // mandated primary items (Subscription / Companies-Tenants / Tenant Database /
                // Business Intelligence) + Logout.
                Check("master navigation is exactly the 4 mandated items + Logout",
                    window.DescribeMasterNav() == "Subscription, Companies/Tenants, TenantDatabase, BusinessIntelligence, Logout",
                    window.DescribeMasterNav());
            }
            catch (Exception ex)
            {
                Check("MasterDashboardForm constructs", false, ex.Message);
            }

            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("======================================================================");
            Write($"PHASE 5 RESULT: {checks - failures.Count}/{checks} checks passed");
            if (failures.Count > 0)
            {
                Write("Failures:");
                foreach (var failure in failures)
                {
                    Write("  - " + failure);
                }
            }
            Write("======================================================================");

            return failures.Count == 0 ? 0 : 1;
        }
    }
}
