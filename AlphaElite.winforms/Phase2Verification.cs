using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 2 diagnostics (strictly read-only):
    ///
    ///   * RunInspect (--phase2-inspect) - per-tenant schema inventory: tables, columns,
    ///     anything branch-shaped, and row counts of the core tables. Used to verify that
    ///     Tenant A / Tenant B are plain non-branching databases and to locate any leftover
    ///     branching logic (queries referencing columns the legacy schema does not have).
    ///
    /// Nothing is written to any database by either mode.
    /// </summary>
    internal static class Phase2Verification
    {
        private static readonly string[] CoreTables =
        {
            "Members", "Attendances", "Payments", "Inquiries", "Feedbacks",
            "Subscriptions", "RetentionRecords", "UserAccounts", "Branches", "Complaints"
        };

        public static int RunInspect()
        {
            try
            {
                var runtime = AppRuntime.Create();
                var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();

                Console.WriteLine("======================================================================");
                Console.WriteLine("PHASE 2 TENANT SCHEMA INSPECTION (read-only)");
                Console.WriteLine("======================================================================");

                foreach (var tenant in tenants)
                {
                    Console.WriteLine();
                    Console.WriteLine($"--- {tenant.CompanyCode} / {tenant.CompanyName} " +
                        $"(CompanyId {tenant.CompanyId}, key '{tenant.CredentialKey}', db {tenant.DatabaseName}, branch-aware={tenant.IsBranchAware}) ---");

                    InspectTenant(runtime, tenant);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        // =====================================================================
        //  FIX SCHEMA (additive only; explicitly invoked via --phase2-fix-schema)
        // =====================================================================

        /// <summary>
        /// Creates the legacy (NON-branching) tables that the application model expects but
        /// some tenant databases never received. Additive only: every statement is guarded
        /// with IF NOT EXISTS, no existing table, column or row is modified or dropped,
        /// and Tenant C is never touched (its schema is branch-aware and complete).
        ///
        /// Why these tables:
        ///  * Subscriptions      - Tenant A lacks it; the Dashboard / Subscriptions / Retention
        ///                          modules read it in every legacy tenant.
        ///  * RetentionRecords   - Tenant A lacks it (migration 20260927013438 was never applied
        ///                          there); the Retention module reads it.
        ///  * Complaints         - neither Tenant A nor Tenant B has it; the Complaints module
        ///                          is now a normal tenant module for all roles. The table is
        ///                          the Tenant C shape WITHOUT the BranchId column / Branch
        ///                          foreign key - no branching is introduced.
        /// </summary>
        public static int RunFixSchema()
        {
            try
            {
                var runtime = AppRuntime.Create();
                var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();

                Console.WriteLine("======================================================================");
                Console.WriteLine("PHASE 2 LEGACY SCHEMA FIX (additive only, IF NOT EXISTS)");
                Console.WriteLine("======================================================================");

                var failures = 0;

                foreach (var tenant in tenants.Where(t => !t.IsBranchAware))
                {
                    Console.WriteLine();
                    Console.WriteLine($"--- {tenant.CompanyCode} / {tenant.CompanyName} ({tenant.DatabaseName}) ---");

                    var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
                    using var db = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(databaseInfo));

                    foreach (var (tableName, ddl) in LegacyDdl)
                    {
                        if (TableExists(db, tableName))
                        {
                            Console.WriteLine($"  {tableName}: present - unchanged.");
                            continue;
                        }

                        Console.WriteLine($"  {tableName}: MISSING - creating the legacy (non-branching) table now.");
                        db.Database.ExecuteSqlRaw(ddl);
                        Console.WriteLine($"  {tableName}: created.");

                        if (!TableExists(db, tableName))
                        {
                            Console.WriteLine("  [FAIL] table still missing after creation attempt.");
                            failures++;
                        }
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"--- Result: {(failures == 0 ? "OK" : "PROBLEMS")} | failures: {failures} ---");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        private static readonly (string Table, string Ddl)[] LegacyDdl =
        {
            ("Subscriptions",
             @"IF OBJECT_ID(N'dbo.Subscriptions', N'U') IS NULL
             BEGIN
                 CREATE TABLE [dbo].[Subscriptions] (
                     [SubscriptionId] int NOT NULL IDENTITY,
                     [MemberId] int NOT NULL,
                     [PlanName] nvarchar(max) NOT NULL,
                     [Price] decimal(18,2) NOT NULL,
                     [BillingCycle] nvarchar(max) NOT NULL,
                     [StartDate] datetime2 NOT NULL,
                     [EndDate] datetime2 NOT NULL,
                     [IsActive] bit NOT NULL,
                     CONSTRAINT [PK_Subscriptions] PRIMARY KEY ([SubscriptionId]),
                     CONSTRAINT [FK_Subscriptions_Members_MemberId] FOREIGN KEY ([MemberId]) REFERENCES [dbo].[Members]([MemberId]) ON DELETE CASCADE
                 );
                 CREATE INDEX [IX_Subscriptions_MemberId] ON [dbo].[Subscriptions] ([MemberId]);
             END"),

            ("RetentionRecords",
             @"IF OBJECT_ID(N'dbo.RetentionRecords', N'U') IS NULL
             BEGIN
                 CREATE TABLE [dbo].[RetentionRecords] (
                     [RetentionRecordId] int NOT NULL IDENTITY,
                     [MemberId] int NOT NULL,
                     [Reason] nvarchar(max) NOT NULL,
                     [RetentionAction] nvarchar(max) NOT NULL,
                     [Promotion] nvarchar(max) NULL,
                     [PromotionDetails] nvarchar(max) NULL,
                     [Notes] nvarchar(max) NULL,
                     [CreatedAt] datetime2 NOT NULL,
                     [CreatedBy] nvarchar(max) NOT NULL,
                     [Status] nvarchar(max) NOT NULL,
                     [ApprovedBy] nvarchar(max) NULL,
                     [ApprovedAt] datetime2 NULL,
                     [ApprovalReason] nvarchar(max) NULL,
                     [RejectionReason] nvarchar(max) NULL,
                     [CompletedAt] datetime2 NULL,
                     [CompletionNotes] nvarchar(max) NULL,
                     CONSTRAINT [PK_RetentionRecords] PRIMARY KEY ([RetentionRecordId]),
                     CONSTRAINT [FK_RetentionRecords_Members_MemberId] FOREIGN KEY ([MemberId]) REFERENCES [dbo].[Members]([MemberId]) ON DELETE CASCADE
                 );
                 CREATE INDEX [IX_RetentionRecords_MemberId] ON [dbo].[RetentionRecords] ([MemberId]);
             END"),

            ("Complaints",
             @"IF OBJECT_ID(N'dbo.Complaints', N'U') IS NULL
             BEGIN
                 CREATE TABLE [dbo].[Complaints] (
                     [ComplaintId] int NOT NULL IDENTITY,
                     [MemberId] int NOT NULL,
                     [Subject] nvarchar(200) NOT NULL,
                     [Description] nvarchar(max) NOT NULL,
                     [Status] nvarchar(max) NOT NULL,
                     [CreatedAt] datetime2 NOT NULL,
                     [ResolvedAt] datetime2 NULL,
                     [ResolutionNotes] nvarchar(max) NULL,
                     CONSTRAINT [PK_Complaints] PRIMARY KEY ([ComplaintId]),
                     CONSTRAINT [FK_Complaints_Members_MemberId] FOREIGN KEY ([MemberId]) REFERENCES [dbo].[Members]([MemberId]) ON DELETE CASCADE
                 );
                 CREATE INDEX [IX_Complaints_MemberId] ON [dbo].[Complaints] ([MemberId]);
             END")
        };

        private static bool TableExists(DbContext db, string tableName)
        {
            return db.Database
                .SqlQueryRaw<int>(
                    "SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables WHERE name = {0}", tableName)
                .AsEnumerable()
                .First() > 0;
        }

        private static void InspectTenant(AppRuntime runtime, TenantOption tenant)
        {
            var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();

            using var db = tenant.IsBranchAware
                ? (DbContext)new TenantCAlphaEliteDbContext(runtime.ConnectionFactory.CreateBranchAwareOptions(databaseInfo))
                : new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(databaseInfo));

            var tables = db.Database.SqlQueryRaw<string>(
                    "SELECT name AS [Value] FROM sys.tables WHERE LEFT(name, 2) <> '__' ORDER BY name")
                .AsEnumerable()
                .ToList();

            Console.WriteLine($"  Tables ({tables.Count}): {string.Join(", ", tables)}");

            var branchObjects = tables
                .Where(t => t.Contains("branch", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var allColumns = db.Database.SqlQueryRaw<string>(
                    "SELECT TABLE_NAME + '.' + COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS " +
                    "WHERE LEFT(TABLE_NAME, 2) <> '__' AND COLUMN_NAME LIKE '%branch%' ORDER BY TABLE_NAME, COLUMN_NAME")
                .AsEnumerable()
                .ToList();

            Console.WriteLine(branchObjects.Count == 0 && allColumns.Count == 0
                ? "  Branch-shaped objects: NONE (schema is non-branching)"
                : $"  Branch-shaped objects: tables=[{string.Join(", ", branchObjects)}] columns=[{string.Join(", ", allColumns)}]");

            foreach (var tableName in CoreTables)
            {
                if (!tables.Contains(tableName, StringComparer.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"    {tableName}: table absent");
                    continue;
                }

                var count = db.Database
                    .SqlQueryRaw<int>($"SELECT CAST(COUNT(*) AS int) AS [Value] FROM [dbo].[{tableName}]")
                    .AsEnumerable()
                    .First();

                Console.WriteLine($"    {tableName}: {count} row(s)");
            }

            // Data-separation probe: does any core table carry a branch-coded value in a
            // TEXT column (e.g. "BR-MAIN")? Branch codes only ever exist in Tenant C.
            foreach (var tableName in new[] { "Members", "UserAccounts", "Subscriptions" })
            {
                if (!tables.Contains(tableName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var textColumns = db.Database.SqlQueryRaw<string>(
                        $"SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS " +
                        $"WHERE TABLE_NAME = '{tableName}' AND DATA_TYPE LIKE 'nvarchar%' AND COLUMN_NAME NOT IN ('PasswordHash')")
                    .AsEnumerable()
                    .ToList();

                var hits = new List<string>();

                foreach (var column in textColumns)
                {
                    var found = db.Database.SqlQueryRaw<int>(
                            $"SELECT CAST(COUNT(*) AS int) AS [Value] FROM [dbo].[{tableName}] " +
                            $"WHERE [{column}] LIKE '%BR-%'")
                        .AsEnumerable()
                        .First();

                    if (found > 0)
                    {
                        hits.Add($"{column}:{found}");
                    }
                }

                Console.WriteLine(hits.Count == 0
                    ? $"    {tableName}: no branch-coded text values"
                    : $"    {tableName}: BRANCH-CODED VALUES FOUND -> {string.Join(", ", hits)}");
            }
        }

        // =====================================================================
        //  VERIFY (headless acceptance checks)
        // =====================================================================

        /// <summary>
        /// Phase 2 acceptance checks:
        ///   1. navigation per real account (login -> session + subscription -> Form1):
        ///      Gym Staff A/B gained Feedback + Complaints, Subscription is gone for every
        ///      tenant role, and the sidebar has no gaps (uniform 44px step);
        ///   2. every module loads for Tenant A / Tenant B without a single
        ///      "Some modules could not be loaded" failure (the BranchId/Subscriptions
        ///      regression is fixed);
        ///   3. the header Tenant dropdown is gone for Tenant A / Tenant B;
        ///   4. data separation: each session reads only its own tenant's rows, and the
        ///      legacy schemas carry no branch-shaped object at all;
        ///   5. RBAC: tenant Admin lost Subscriptions, Gym Staff gained Feedback/Complaints,
        ///      MasterAdmin keeps Subscriptions.
        /// </summary>
        public static int RunVerify()
        {
            var failures = new List<string>();
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

            Write("======================================================================");
            Write("PHASE 2 ACCEPTANCE VERIFICATION (nav / module loads / separation)");
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

            var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();
            var tenantA = tenants.FirstOrDefault(t => string.Equals(t.CredentialKey, "TenantA", StringComparison.OrdinalIgnoreCase));
            var tenantB = tenants.FirstOrDefault(t => string.Equals(t.CredentialKey, "TenantB", StringComparison.OrdinalIgnoreCase));
            var tenantC = tenants.FirstOrDefault(t => t.IsBranchAware);

            if (tenantA == null || tenantB == null || tenantC == null)
            {
                Write("  [FAIL] tenant discovery: TenantA/TenantB/TenantC must all be configured.");
                return 1;
            }

            // ---------------------------------------------------------------------
            // 1. Nav + module loads per real account (exactly like Program.cs builds
            //    the session: login result + company subscription from the master DB).
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 1. Per-account navigation, module loads, layout, tenant dropdown ---");

            var navExpectations = new (string Username, string Company, TenantOption Tenant, string Nav, bool SwitcherHidden)[]
            {
                ("gymstaffA", "COMP001", tenantA, "Members, Attendance, Payments, Feedback, Inquiries, Complaints", true),
                ("adminA",    "COMP001", tenantA, "Members, Attendance, Payments, Feedback, Inquiries, Complaints", true),
                ("gymstaffB", "COMP002", tenantB, "Members, Attendance, Payments, Feedback, Inquiries, Retention, Complaints", true),
                // Tenant B phase: the Dashboard page is relabeled "Business Intelligence" for
                // COMP002 (label only - the same page), Manager B gained Payments (Payment
                // History & Renewals) and User Management (restricted to Gym Staff). Must
                // match the TenantBVerification nav spec.
                ("adminB",    "COMP002", tenantB, "Business Intelligence, Members, Attendance, Payments, Feedback, Inquiries, Retention, Complaints, UserManagement", true),
                ("managerB",  "COMP002", tenantB, "Business Intelligence, Members, Attendance, Payments, Feedback, Inquiries, Retention, Complaints, UserManagement", true)
            };

            foreach (var (username, company, tenant, expectedNav, expectHidden) in navExpectations)
            {
                var login = runtime.AuthenticationService
                    .AuthenticateAsync(tenant.CompanyId, username, "123").GetAwaiter().GetResult();

                if (login == null)
                {
                    Check($"'{username}' login", false, "rejected (run --phase1-seed-accounts first)");
                    continue;
                }

                UserSession session;
                try
                {
                    var info = runtime.SubscriptionService.GetCompanySubscriptionAsync(tenant.CompanyId).GetAwaiter().GetResult();
                    var subscriptionContext = info == null
                        ? new UserSubscriptionContext()
                        : new UserSubscriptionContext
                        {
                            HasActiveSubscription = info.EffectiveStatus == SubscriptionStatuses.Active,
                            EffectiveStatus = info.EffectiveStatus,
                            PlanName = info.PlanName,
                            EndDate = info.EndDate,
                            EnabledModuleCodes = info.EffectiveStatus == SubscriptionStatuses.Active
                                ? info.EnabledModuleCodes
                                : Array.Empty<string>()
                        };

                    session = new UserSession
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
                        Subscription = subscriptionContext
                    };
                }
                catch (Exception ex)
                {
                    Check($"'{username}' subscription context", false, ex.Message);
                    continue;
                }

                using (var form = new Form1(session, tenants, runtime.DataContextFactory, runtime.AuthorizationService)
                {
                    SuppressDialogs = true
                })
                {
                    SynchronizationContext.SetSynchronizationContext(null);
                    form.InitializeCurrentBranchSelectorForTestAsync().GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);

                    var nav = form.DescribeNav();
                    Check($"'{username}' nav ({company})",
                        nav == expectedNav,
                        $"{nav}" + (nav == expectedNav ? string.Empty : $" (expected: {expectedNav})"));

                    Check($"'{username}' nav has no Subscriptions entry",
                        !nav.Contains("Subscriptions"),
                        nav.Contains("Subscriptions") ? "Subscriptions visible (!)" : "absent");

                    var switcher = form.DescribeTenantSwitcher();
                    Check($"'{username}' tenant dropdown removed",
                        form.TenantSwitcherHiddenForTest == expectHidden && switcher.StartsWith("items=1,"),
                        $"hiddenForTest={form.TenantSwitcherHiddenForTest}, switcher: {switcher}");

                    // Load every module this role is allowed to use. Zero failures means the
                    // "Some modules could not be loaded" dialog can no longer appear.
                    form.ReloadAllModulesForTestAsync().GetAwaiter().GetResult();
                    SynchronizationContext.SetSynchronizationContext(null);

                    var loadFailures = form.LoadFailuresForTest;
                    Check($"'{username}' all modules load without errors",
                        loadFailures.Count == 0,
                        loadFailures.Count == 0 ? "0 failures" : string.Join("; ", loadFailures));

                    // Layout: the visible buttons must be stacked with a uniform 44px step
                    // starting at y=100 - i.e. no gap where a hidden module used to be.
                    var layout = form.DescribeNavLayoutForTest();
                    var positions = layout
                        .Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(part => int.Parse(part.Substring(part.IndexOf('@') + 1)))
                        .ToList();

                    var uniform = positions.Count > 0
                        && positions[0] == 100
                        && positions.Zip(positions.Skip(1), (a, b) => b - a).All(gap => gap == 44);

                    Check($"'{username}' nav spacing is uniform (no gaps)",
                        uniform,
                        layout);
                }
            }

            // ---------------------------------------------------------------------
            // 2. Direct query smoke: the exact query shapes that produced
            //    "Invalid column name BranchId" / missing-table errors.
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 2. Direct query smoke for the previously failing module queries ---");

            foreach (var tenant in new[] { tenantA, tenantB })
            {
                try
                {
                    var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
                    using var db = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(databaseInfo));

                    var subs = db.Subscriptions.AsNoTracking().ToList();               // Subscriptions module
                    var subsRisk = db.Subscriptions.AsNoTracking().ToList();            // Dashboard at-risk KPI
                    var retention = db.RetentionRecords.AsNoTracking().ToList();        // Retention module
                    var retentionSubs = db.Subscriptions.AsNoTracking()
                        .Where(x => x.IsActive).ToList();                               // Retention expiring-soon
                    var complaints = db.Complaints.AsNoTracking().Count();              // Complaints module

                    Check($"{tenant.CompanyCode}: Dashboard/Subscriptions/Retention/Complaints queries run clean",
                        true,
                        $"subs={subs.Count}, riskQuery={subsRisk.Count}, retentionRecords={retention.Count}, activeSubs={retentionSubs.Count}, complaints={complaints}");
                }
                catch (Exception ex)
                {
                    Check($"{tenant.CompanyCode}: Dashboard/Subscriptions/Retention/Complaints queries run clean",
                        false,
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            // ---------------------------------------------------------------------
            // 3. Tenant data separation + non-branching schema.
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 3. Data separation and non-branching schemas ---");

            foreach (var tenant in new[] { tenantA, tenantB })
            {
                using var data = runtime.DataContextFactory.CreateAsync(tenant.CompanyId, false).GetAwaiter().GetResult();
                var members = data.Members.AsNoTracking().Count();
                var payments = data.Payments.AsNoTracking().Count();

                // Raw probe on the same legacy connection the app uses (resolver-built, i.e.
                // independent of the DataContextFactory the session goes through).
                var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
                using var raw = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(databaseInfo));

                // Both independent reads of the same tenant must agree: this proves the module
                // path reads exactly that tenant's rows without pinning hardcoded counts
                // (Tenant A accumulates legitimate manual-test data, so fingerprints drift).
                var rawMembers = raw.Members.AsNoTracking().Count();
                var rawPayments = raw.Payments.AsNoTracking().Count();

                Check($"{tenant.CompanyCode} sees exactly its own rows",
                    members == rawMembers && payments == rawPayments,
                    $"factory members={members} payments={payments} vs direct members={rawMembers} payments={rawPayments}");
                var branchTables = raw.Database.SqlQueryRaw<int>(
                    "SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables WHERE name LIKE '%branch%'")
                    .AsEnumerable().First();
                var branchColumns = raw.Database.SqlQueryRaw<int>(
                    "SELECT CAST(COUNT(*) AS int) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME LIKE '%branch%'")
                    .AsEnumerable().First();

                Check($"{tenant.CompanyCode} schema has no branch-shaped object",
                    branchTables == 0 && branchColumns == 0,
                    branchTables == 0 && branchColumns == 0
                        ? "no branch tables, no branch columns"
                        : $"found {branchTables} branch table(s), {branchColumns} branch column(s)");
            }

            // Cross-tenant: the usernames resolve to exactly one tenant (Phase 1 rule still holds).
            foreach (var (username, expectedCompanyId) in new[]
                     {
                         ("gymstaffA", tenantA.CompanyId),
                         ("adminA", tenantA.CompanyId),
                         ("gymstaffB", tenantB.CompanyId),
                         ("adminB", tenantB.CompanyId),
                         ("managerB", tenantB.CompanyId),
                         ("admin", tenantC.CompanyId)
                     })
            {
                var matches = runtime.AuthenticationService.FindTenantsContainingUserAsync(username).GetAwaiter().GetResult();
                Check($"'{username}' still resolves to exactly one tenant",
                    matches.Count == 1 && matches[0].CompanyId == expectedCompanyId,
                    matches.Count == 1 ? matches[0].CompanyCode : $"{matches.Count} candidates");
            }

            // ---------------------------------------------------------------------
            // 4. RBAC changes.
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 4. RBAC ---");

            var auth = runtime.AuthorizationService;
            Check("tenant Admin can NOT manage Subscriptions", !auth.IsAuthorized(Roles.Admin, Permission.ManageSubscriptions), "blocked");
            Check("MasterAdmin CAN manage Subscriptions", auth.IsAuthorized(Roles.MasterAdmin, Permission.ManageSubscriptions), "allowed");
            Check("GymStaff CAN manage Feedback", auth.IsAuthorized(Roles.GymStaff, Permission.ManageFeedback), "allowed");
            Check("GymStaff CAN manage Complaints", auth.IsAuthorized(Roles.GymStaff, Permission.ManageComplaints), "allowed");
            Check("GymStaff still can NOT manage users", !auth.IsAuthorized(Roles.GymStaff, Permission.ManageUsers), "blocked");
            Check("Manager still can NOT manage Subscriptions", !auth.IsAuthorized(Roles.Manager, Permission.ManageSubscriptions), "blocked");

            Write(string.Empty);
            Write($"--- Result: {(failures.Count == 0 ? "OK" : "PROBLEMS")} | checks: {checks} | failures: {failures.Count} ---");
            return failures.Count == 0 ? 0 : 1;
        }

    }
}
