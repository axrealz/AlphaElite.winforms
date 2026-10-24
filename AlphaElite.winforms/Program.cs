using AlphaElite.infrastructure.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    internal static class Program
    {
        // Roots the verification watchdog for the whole process lifetime (a local would be
        // garbage-collected and the timer would silently stop).
        private static System.Threading.Timer? _verificationWatchdog;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            // Headless verification suites must never wedge the regression battery: arm a
            // process-level watchdog for every --* verification mode. A suite completes in
            // minutes; if the process is still alive after 20 minutes something is
            // irrecoverably stuck, so abort loudly (the battery logs EXIT=124) instead of
            // hanging forever. The production UI path (no arguments) is never time-boxed.
            if (args.Any(a => a.StartsWith("--", StringComparison.Ordinal)))
            {
                _verificationWatchdog = new System.Threading.Timer(
                    _ =>
                    {
                        Console.WriteLine("GLOBAL WATCHDOG: verification mode exceeded 20 minutes - aborting the process (EXIT=124)");
                        Console.Out.Flush();
                        Environment.Exit(124);
                    },
                    null,
                    20 * 60 * 1000,
                    System.Threading.Timeout.Infinite);
            }

            // Headless verification mode: exercises tenant discovery, authentication and
            // every dashboard query without opening any window. Never starts the UI.
            if (args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
            {
                return StartupVerification.RunAsync().GetAwaiter().GetResult();
            }

            // Schema dry run: prints the DDL for the Tenant C schema (model and migrations) and
            // compares them. Generates scripts only - it never executes any DDL.
            if (args.Any(a => string.Equals(a, "--tenantc-schema-sql", StringComparison.OrdinalIgnoreCase)))
            {
                return TenantCSchemaPreview.Run();
            }

            // Tenant C deployment: apply pending migrations, seed the approved branches, or verify.
            // All three resolve their target from the master database and always print it first.
            if (args.Any(a => string.Equals(a, "--tenantc-migrate", StringComparison.OrdinalIgnoreCase)))
            {
                return TenantCDeployment.Migrate();
            }

            if (args.Any(a => string.Equals(a, "--tenantc-seed", StringComparison.OrdinalIgnoreCase)))
            {
                return TenantCDeployment.SeedBranches();
            }

            if (args.Any(a => string.Equals(a, "--tenantc-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return TenantCDeployment.Verify();
            }

            // Phase 3 helper: strictly read-only inventory of Tenant A, Tenant B and Tenant C
            // used to design the data migration dry run. Never writes to any database.
            if (args.Any(a => string.Equals(a, "--migration-dryrun", StringComparison.OrdinalIgnoreCase)))
            {
                return MigrationDryRun.Run();
            }

            // Phase 3 execution: preflight, transactional migration of Tenant A + Tenant B
            // data into Tenant C, and read-only post-migration verification.
            if (args.Any(a => string.Equals(a, "--tenantc-preflight", StringComparison.OrdinalIgnoreCase)))
            {
                return TenantCDataMigration.Preflight();
            }

            if (args.Any(a => string.Equals(a, "--tenantc-migrate-data", StringComparison.OrdinalIgnoreCase)))
            {
                return TenantCDataMigration.MigrateData();
            }

            if (args.Any(a => string.Equals(a, "--tenantc-verify-migration", StringComparison.OrdinalIgnoreCase)))
            {
                return TenantCDataMigration.VerifyMigration();
            }

            // Phase 4F: create the Tenant C demo accounts (Manager/GymStaff/Member).
            if (args.Any(a => string.Equals(a, "--tenantc-seed-users", StringComparison.OrdinalIgnoreCase)))
            {
                return TenantCUserSeeder.Run();
            }

            // Phase 4J: headless acceptance verification (roles, menus, isolation, authz, rehash).
            if (args.Any(a => string.Equals(a, "--phase4-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase4Verification.Run();
            }

            // Tenant C BI acceptance: real-window chart rendering across branch switches and
            // refreshes (proves the dropped-measure root-cause fix on the dashboard tab).
            if (args.Any(a => string.Equals(a, "--tenantc-verify-bi", StringComparison.OrdinalIgnoreCase)))
            {
                return TenantCVerification.RunBiVerify();
            }

            // Tenant C phase 7: idempotent seeder for the six per-branch demo accounts
            // (demo password, PBKDF2; existing rows are skipped, admin is never touched).
            if (args.Any(a => string.Equals(a, "--tenantc-seed-branch-users", StringComparison.OrdinalIgnoreCase)))
            {
                return TenantCBranchUserSeeder.Run();
            }

            // Phase 5: seed exactly ONE verified SuperAdmin demo account in the MASTER database
            // (ASP.NET Identity). Idempotent: re-running never creates a second account.
            if (args.Any(a => string.Equals(a, "--master-seed-admin", StringComparison.OrdinalIgnoreCase)))
            {
                return MasterAdminSeeder.Run();
            }

            // Phase 5: headless acceptance verification (master Identity, dashboard, switching,
            // tenant-unchanged regression checks).
            if (args.Any(a => string.Equals(a, "--phase5-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase5Verification.Run();
            }

            // Phase 6: seed the user-approved demo dataset into Tenant C (2 subscriptions,
            // current-week attendance, 2 complaints). Idempotent, migrated rows untouched.
            if (args.Any(a => string.Equals(a, "--phase6-demo", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase6DemoDataSeeder.RunAsync().GetAwaiter().GetResult();
            }

            // Phase 6: headless acceptance verification (features, scope rules, baselines).
            if (args.Any(a => string.Equals(a, "--phase6-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase6Verification.Run();
            }

            // Phase 8: headless acceptance verification (branch catalogue, Current Branch
            // authority, write stamping, member transfer, branch CRUD, deactivation, role gating).
            if (args.Any(a => string.Equals(a, "--phase8-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase8Verification.Run();
            }

            // Phase 8 follow-up fixes: tenant logout, dashboard branch switching, branch manager.
            if (args.Any(a => string.Equals(a, "--phase8-fixes-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase8FixesVerification.Run();
            }

            // Phase 9: master-DB subscription schema deployment (additive migration, EF's own
            // mechanism; refuses to run against anything but the configured master database).
            if (args.Any(a => string.Equals(a, "--master-migrate", StringComparison.OrdinalIgnoreCase)))
            {
                return MasterSubscriptionDeployment.Migrate();
            }

            // Phase 9: idempotent seed of plans / modules / mappings / demo company subscriptions.
            if (args.Any(a => string.Equals(a, "--master-seed-subs", StringComparison.OrdinalIgnoreCase)))
            {
                return MasterSubscriptionDeployment.Seed();
            }

            // Phase 9: read-only verification of the seeded subscription data.
            if (args.Any(a => string.Equals(a, "--master-verify-subs", StringComparison.OrdinalIgnoreCase)))
            {
                return MasterSubscriptionDeployment.Verify();
            }

            // Subscription-request phase: read-only live master schema/data inspection.
            if (args.Any(a => string.Equals(a, "--master-inspect-schema", StringComparison.OrdinalIgnoreCase)))
            {
                return MasterSubscriptionDeployment.InspectSchema();
            }

            // Subscription-request phase: read-only snapshot-vs-CLR model diff (migration hygiene).
            if (args.Any(a => string.Equals(a, "--master-model-diff", StringComparison.OrdinalIgnoreCase)))
            {
                return MasterSubscriptionDeployment.InspectModelDiff();
            }

            // Final subscription system: one-off, guarded removal of the obsolete MICRO_BUSINESS /
            // SMALL_BUSINESS plans and their exclusive configuration (MASTER DB ONLY). Refuses to
            // run if either plan is referenced by a subscription, request or history row. Add
            // --dry-run to classify and report without writing.
            if (args.Any(a => string.Equals(a, "--master-remove-legacy-plans", StringComparison.OrdinalIgnoreCase)))
            {
                return MasterLegacyPlanCleanup.Run(
                    args.Any(a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase)));
            }

            // Super Admin Business Intelligence acceptance battery (read-only): the top-level BI
            // page, its six KPI cards, its four real-data charts, no export surface, no obsolete
            // plan terminology and no CLR type names in the visible subscription UI.
            if (args.Any(a => string.Equals(a, "--super-admin-bi-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return MasterBusinessIntelligenceVerification.Run();
            }

            // Phase 3: focused subscription-plan verification (final 3-plan shape, isolation).
            if (args.Any(a => string.Equals(a, "--phase3-subscription-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase3LaunchPlanVerification.Run();
            }

            // Phase 4: OPTION 2 workflow verification (request -> approve/reject -> snapshots).
            if (args.Any(a => string.Equals(a, "--phase4-subscription-workflow-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase4SubscriptionWorkflowVerification.Run();
            }

            // Phase 9: headless acceptance verification (plans, modules, gating, statuses,
            // fingerprints, no residue).
            if (args.Any(a => string.Equals(a, "--phase9-subscription-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase9SubscriptionVerification.Run();
            }

            // Phase 5-7: tenant subscription screen (request/cancel), module-enforcement
            // composition and the focused 31-check acceptance battery.
            if (args.Any(a => string.Equals(a, "--phase7-subscription-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase7SubscriptionVerification.Run();
            }

            // UI phase: Super Admin / tenant theme + drop-down binding acceptance battery
            // (read-only: the company and plan names it expects come from the master database).
            if (args.Any(a => string.Equals(a, "--theme-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return ThemeVerification.Run();
            }

            // Plan-configuration versioning: idempotent Version-1 initialization for the existing
            // plans (master DB only, additive, safe to run repeatedly).
            if (args.Any(a => string.Equals(a, "--master-init-plan-versions", StringComparison.OrdinalIgnoreCase)))
            {
                return PlanVersionInitializer.Run();
            }

            // One-off, guarded maintenance: removes plan-configuration versions created ONLY by
            // automated verification runs (never by a person) from the demo master database.
            // Add --dry-run to classify and report without writing.
            if (args.Any(a => string.Equals(a, "--master-clean-plan-version-artifacts", StringComparison.OrdinalIgnoreCase)))
            {
                return PlanVersionArtifactCleanup.Run(
                    args.Any(a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase)));
            }

            // Master (Super Admin) connectivity: the tenant/company load path, the bounded
            // transient-failure retry policy on every master context, honest failure classification
            // and the operator-facing outage message.
            if (args.Any(a => string.Equals(a, "--master-connectivity-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return MasterConnectivityVerification.Run();
            }

            // Super Admin Subscription redesign acceptance battery: plan cards, real versioned
            // terms/module snapshots, history details, upgrade/downgrade safety, drop-down
            // regression, DbContext concurrency (overlapping operations) and the real render path.
            if (args.Any(a => string.Equals(a, "--subscription-redesign-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return SubscriptionRedesignVerification.Run();
            }

            // Phase 1: read-only inventory of every login-related account store (tenant
            // UserAccounts tables + master Identity). Never writes to any database.
            if (args.Any(a => string.Equals(a, "--phase1-inspect", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase1LoginAccounts.RunInspect();
            }

            // Phase 1: create/fix the demo login accounts (idempotent; additive schema only).
            if (args.Any(a => string.Equals(a, "--phase1-seed-accounts", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase1LoginAccounts.RunSeed();
            }

            // Phase 1: headless acceptance verification (credential -> tenant/role resolution,
            // cross-tenant isolation, master separation, data-isolation, no residue).
            if (args.Any(a => string.Equals(a, "--phase1-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase1LoginVerification.Run();
            }

            // Phase 2: read-only schema inventory of every tenant (tables, columns,
            // branch-shaped objects, core row counts). Never writes to any database.
            if (args.Any(a => string.Equals(a, "--phase2-inspect", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase2Verification.RunInspect();
            }

            // Phase 2: creates missing LEGACY (non-branching) tables in Tenant A / Tenant B.
            // Additive only (IF NOT EXISTS); explained here because schema changes must be
            // explained before applying: Subscriptions + RetentionRecords (Tenant A only,
            // never applied there) and Complaints (both, without BranchId - no branching).
            if (args.Any(a => string.Equals(a, "--phase2-fix-schema", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase2Verification.RunFixSchema();
            }

            // Phase 2: headless acceptance verification (nav per role/tenant, module loads,
            // no tenant dropdown, nav spacing, tenant data separation, RBAC changes).
            if (args.Any(a => string.Equals(a, "--phase2-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return Phase2Verification.RunVerify();
            }

            // Complaints CRUD (Tenant A phase): read-only schema inventory of the Complaints
            // tables in Tenant A / B / C. Never writes to any database.
            if (args.Any(a => string.Equals(a, "--complaints-inspect", StringComparison.OrdinalIgnoreCase)))
            {
                return ComplaintsVerification.RunInspect();
            }

            // Complaints CRUD (Tenant A phase): additive, guarded schema change - adds the three
            // nullable complaint columns (Category / Priority / StaffAssigned) to Tenant A and
            // Tenant B (they share the legacy context). Nothing is dropped; existing rows are
            // preserved; Tenant C is NOT touched (its context ignores these properties).
            if (args.Any(a => string.Equals(a, "--complaints-fix-schema", StringComparison.OrdinalIgnoreCase)))
            {
                return ComplaintsVerification.RunFixSchema();
            }

            // Complaints CRUD (Tenant A phase): headless acceptance verification - full CRUD
            // round-trip with all 11 fields, member-dropdown tenant isolation, Admin A has no
            // Dashboard (and no Business Intelligence), regressions for Tenant B / Tenant C / Gym Staff A.
            if (args.Any(a => string.Equals(a, "--complaints-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return ComplaintsVerification.RunVerify();
            }

            // Tenant B phase: Business Intelligence refresh lifecycle, role navigation
            // (Dashboard labeled "Business Intelligence" for Tenant B, no Subscription for
            // Admin B / Manager B), Manager B staff-only user management, non-branching,
            // Tenant A / Tenant C isolation and the three Tenant B logins.
            if (args.Any(a => string.Equals(a, "--tenantb-verify", StringComparison.OrdinalIgnoreCase)))
            {
                return TenantBVerification.RunVerify();
            }

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            // Required by QuestPDF: declares this app uses the free Community license.
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            AppRuntime runtime;
            try
            {
                runtime = AppRuntime.Create();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Alpha Elite could not start." + Environment.NewLine + Environment.NewLine + ex.Message,
                    "Alpha Elite",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }

            IReadOnlyList<TenantOption> tenants;
            try
            {
                tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // A genuinely unreachable master database is reported as such (the bounded retry
                // policy has already been exhausted); authentication/configuration problems are
                // reported as themselves. Either way the original exception - with its inner
                // chain - is written to the diagnostics log before the dialog is shown.
                MasterDbErrorReporter.ShowDialog(
                    null,
                    "Alpha Elite could not load the tenant list from the master database.",
                    ex);
                return 1;
            }

            if (tenants.Count == 0)
            {
                MessageBox.Show(
                    "No active tenants were found in the master database.",
                    "Alpha Elite",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return 1;
            }

            // Phase 5 loop: tenant login and Master Admin login alternate until one succeeds
            // or the user exits. DialogResult.Retry signals "show the tenant login again".
            while (true)
            {
                MasterUserSession? masterSession = null;
                UserSession? tenantSession = null;

                // Phase 1: the login form no longer receives the tenant list - the tenant and
                // the role are resolved from the credentials themselves inside the form.
                using (var loginForm = new LoginForm(runtime.AuthenticationService, runtime.MasterIdentityService))
                {
                    if (loginForm.ShowDialog() != DialogResult.OK)
                    {
                        // User closed the login window or failed to log in.
                        return 0;
                    }

                    if (loginForm.MasterLoginSelected)
                    {
                        masterSession = loginForm.MasterSession;
                    }
                    else
                    {
                        // The company id / branch / member / role come from the login result (the
                        // tenant the user selected). Nothing here is hardcoded.
                        //
                        // Phase 9: the company's SOFTWARE subscription is read ONCE from the
                        // master database here (the only master read in the tenant flow) and
                        // travels with the session. Form1 never queries the master database;
                        // it composes this context with the role permissions. A failure to read
                        // the subscription degrades to "no subscription" which hides the plan-
                        // gated modules but never blocks login or the dashboard basics.
                        UserSubscriptionContext subscriptionContext;
                        try
                        {
                            var info = runtime.SubscriptionService
                                .GetCompanySubscriptionAsync(loginForm.CompanyId)
                                .GetAwaiter().GetResult();

                            if (info == null)
                            {
                                subscriptionContext = new UserSubscriptionContext();
                            }
                            else
                            {
                                // Expired/Suspended: the plan's modules are unavailable (nothing is
                                // deleted - the plan content stays in the master DB and returns
                                // automatically when the subscription becomes Active again).
                                subscriptionContext = new UserSubscriptionContext
                                {
                                    HasActiveSubscription = info.EffectiveStatus == SubscriptionStatuses.Active,
                                    EffectiveStatus = info.EffectiveStatus,
                                    PlanName = info.PlanName,
                                    EndDate = info.EndDate,
                                    EnabledModuleCodes = info.EffectiveStatus == SubscriptionStatuses.Active
                                        ? info.EnabledModuleCodes
                                        : Array.Empty<string>()
                                };
                            }
                        }
                        catch
                        {
                            subscriptionContext = UserSubscriptionContext.None;
                        }

                        tenantSession = new UserSession
                        {
                            CompanyId = loginForm.CompanyId,
                            CompanyCode = loginForm.CompanyCode,
                            CompanyName = loginForm.CompanyName,
                            IsBranchAware = loginForm.IsBranchAware,
                            BranchId = loginForm.BranchId,
                            MemberId = loginForm.MemberId,
                            Username = loginForm.LoggedInUsername,
                            FullName = loginForm.LoggedInFullName,
                            Role = loginForm.LoggedInRole,
                            Subscription = subscriptionContext
                        };
                    }
                }

                if (masterSession != null)
                {
                    DialogResult masterResult;
                    using (var masterWindow = new MasterDashboardForm(runtime, masterSession))
                    {
                        masterResult = masterWindow.ShowDialog();
                    }

                    if (masterResult == DialogResult.Retry)
                    {
                        // Logout: back to the login loop.
                        continue;
                    }

                    return 0;
                }

                // Tenant window. Logging out closes it with DialogResult.Retry, exactly like the
                // Master window above, so the loop returns to the login form WITHOUT terminating
                // the process: the user can sign in again immediately.
                DialogResult tenantResult;
                using (var mainWindow = new Form1(
                    tenantSession!, tenants, runtime.DataContextFactory, runtime.AuthorizationService, runtime.SubscriptionService))
                {
                    tenantResult = mainWindow.ShowDialog();
                }

                if (tenantResult == DialogResult.Retry)
                {
                    // Logout: back to the login loop.
                    continue;
                }

                return 0;
            }
        }
    }
}
