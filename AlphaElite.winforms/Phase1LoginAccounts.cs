using AlphaElite.domain.entities;
using AlphaElite.infrastructure.data;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 1: deployment tool for the credential-driven login demo accounts.
    ///
    /// Two modes:
    ///   * RunInspect (--phase1-inspect)      - strictly read-only inventory of every
    ///     login-related account store (tenant UserAccounts tables + master Identity).
    ///   * RunSeed     (--phase1-seed-accounts) - creates/fixes exactly the five demo
    ///     accounts (gymstaffA/adminA in Tenant A, gymstaffB/adminB/managerB in Tenant B).
    ///
    /// Safety rules:
    ///   * Accounts are resolved by the master-database CredentialKey (TenantA/TenantB),
    ///     never by a hardcoded CompanyId.
    ///   * Idempotent: an existing username is UPDATED IN PLACE (role/password/active state
    ///     corrected to the approved demo spec) - never duplicated, never deleted.
    ///   * Tenant C accounts and the master Identity account are never touched.
    ///   * If a tenant database predates the authentication module (no UserAccounts table),
    ///     an additive-only CREATE TABLE is applied, matching the EF model exactly
    ///     (migration 20260923145800_AddSubscriptionAndUserAccountToTenantErp). No other
    ///     table, column or row of the tenant databases is modified.
    ///   * The demo password is hashed with the application's own PBKDF2 PasswordHasher
    ///     before it is stored; no plaintext password is ever written to any database.
    /// </summary>
    internal static class Phase1LoginAccounts
    {
        /// <summary>User-approved demo password for the five Phase 1 accounts (hashed at rest).</summary>
        private const string DemoPassword = "123";

        private sealed record AccountPlan(string Username, string FullName, string Role);

        private static readonly AccountPlan[] TenantAPlan =
        {
            new("gymstaffA", "Gym Staff A", Roles.GymStaff),
            new("adminA",    "Admin A",     Roles.Admin)
        };

        private static readonly AccountPlan[] TenantBPlan =
        {
            new("gymstaffB", "Gym Staff B", Roles.GymStaff),
            new("adminB",    "Admin B",     Roles.Admin),
            new("managerB",  "Manager B",   Roles.Manager)
        };

        // =====================================================================
        //  INSPECT (read-only)
        // =====================================================================
        public static int RunInspect()
        {
            try
            {
                var runtime = AppRuntime.Create();
                var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();

                Console.WriteLine("======================================================================");
                Console.WriteLine("PHASE 1 LOGIN INSPECTION (read-only - no database is modified)");
                Console.WriteLine("======================================================================");

                foreach (var tenant in tenants)
                {
                    Console.WriteLine();
                    Console.WriteLine($"--- {tenant.CompanyCode} / {tenant.CompanyName} " +
                        $"(CompanyId {tenant.CompanyId}, credential key '{tenant.CredentialKey}', " +
                        $"database {tenant.DatabaseName}, branch-aware={tenant.IsBranchAware}) ---");

                    var state = ReadTenantUserStore(runtime, tenant);
                    if (state == null)
                    {
                        Console.WriteLine("  UserAccounts table: MISSING (schema predates the authentication module).");
                        continue;
                    }

                    Console.WriteLine($"  UserAccounts table: present, {state.Count} row(s)");
                    foreach (var user in state.OrderBy(u => u.UserAccountId))
                    {
                        Console.WriteLine($"    id={user.UserAccountId} username='{user.Username}' role='{user.Role}' active={user.IsActive} (hash format: {DescribeHashFormat(user.PasswordHash)})");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("--- MASTER database (ASP.NET Identity) ---");
                var masterUsers = runtime.MasterIdentityService.GetUsersAsync().GetAwaiter().GetResult();
                Console.WriteLine($"  {masterUsers.Count} Identity user(s)");
                foreach (var user in masterUsers)
                {
                    Console.WriteLine($"    username='{user.Username}' roles=[{string.Join(", ", user.Roles)}] active={user.IsActive}");
                }

                Console.WriteLine();
                Console.WriteLine("--- Seed plan (--phase1-seed-accounts) ---");
                Console.WriteLine("  Tenant A (CredentialKey 'TenantA'): gymstaffA/GymStaff, adminA/Admin");
                Console.WriteLine("  Tenant B (CredentialKey 'TenantB'): gymstaffB/GymStaff, adminB/Admin, managerB/Manager");
                Console.WriteLine("  Tenant C and Master accounts are never modified.");
                Console.WriteLine("  Missing UserAccounts tables would be created additively (UserAccounts only).");

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        // =====================================================================
        //  SEED (idempotent)
        // =====================================================================
        public static int RunSeed()
        {
            var failures = new List<string>();

            try
            {
                var runtime = AppRuntime.Create();
                var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();

                Console.WriteLine("======================================================================");
                Console.WriteLine("PHASE 1 DEMO ACCOUNT SEEDING (idempotent)");
                Console.WriteLine("======================================================================");

                var tenantA = tenants.FirstOrDefault(t => string.Equals(t.CredentialKey, "TenantA", StringComparison.OrdinalIgnoreCase));
                var tenantB = tenants.FirstOrDefault(t => string.Equals(t.CredentialKey, "TenantB", StringComparison.OrdinalIgnoreCase));

                if (tenantA == null)
                {
                    Console.WriteLine("  [FAIL] no tenant with CredentialKey 'TenantA' found in the master database.");
                    failures.Add("TenantA not found");
                }

                if (tenantB == null)
                {
                    Console.WriteLine("  [FAIL] no tenant with CredentialKey 'TenantB' found in the master database.");
                    failures.Add("TenantB not found");
                }

                if (tenantA != null)
                {
                    SeedTenantAccounts(runtime, tenantA, TenantAPlan, failures);
                }

                if (tenantB != null)
                {
                    // Tenant B holds a legacy 'admin' row (untouched by any earlier phase,
                    // legacy SHA-256 hash). With credential-driven login the username 'admin'
                    // must stay unique to Tenant C (whose preserved 'admin' account must keep
                    // resolving there), so the legacy row is REUSED IN PLACE as the requested
                    // 'adminB' account - no duplicate, no deletion.
                    SeedTenantAccounts(runtime, tenantB, TenantBPlan, failures, renameFrom: "admin", renameTo: "adminB");
                }

                Console.WriteLine();
                Console.WriteLine("--- In-process login verification (credentials -> tenant + role) ---");
                foreach (var tenant in new[] { tenantA, tenantB })
                {
                    if (tenant == null)
                    {
                        continue;
                    }

                    var plans = ReferenceEquals(tenant, tenantA) ? TenantAPlan : TenantBPlan;

                    foreach (var plan in plans)
                    {
                        var candidates = runtime.AuthenticationService
                            .FindTenantsContainingUserAsync(plan.Username).GetAwaiter().GetResult();

                        if (candidates.Count != 1 || candidates[0].CompanyId != tenant.CompanyId)
                        {
                            failures.Add($"{plan.Username} resolves ambiguously");
                            Console.WriteLine($"  [FAIL] '{plan.Username}' username lookup returned {candidates.Count} candidate tenant(s), expected exactly {tenant.CompanyCode}.");
                            continue;
                        }

                        var result = runtime.AuthenticationService
                            .AuthenticateAsync(tenant.CompanyId, plan.Username, DemoPassword).GetAwaiter().GetResult();

                        if (result == null)
                        {
                            failures.Add($"login failed for {plan.Username}");
                            Console.WriteLine($"  [FAIL] login for '{plan.Username}' was rejected.");
                            continue;
                        }

                        Console.WriteLine($"  [PASS] '{plan.Username}': {result.CompanyCode} ({result.CompanyName}), role={result.Role}, branchAware={result.IsBranchAware}, branchId={result.BranchId?.ToString() ?? "null"}, memberId={result.MemberId?.ToString() ?? "null"}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine($"--- Result: {(failures.Count == 0 ? "OK" : "PROBLEMS")} | failures: {failures.Count} ---");
                return failures.Count == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        private static void SeedTenantAccounts(
            AppRuntime runtime,
            TenantOption tenant,
            AccountPlan[] plan,
            List<string> failures,
            string? renameFrom = null,
            string? renameTo = null)
        {
            Console.WriteLine();
            Console.WriteLine($"--- {tenant.CompanyCode} / {tenant.CompanyName} (CompanyId {tenant.CompanyId}, credential key '{tenant.CredentialKey}') ---");

            var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();

            using var db = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(databaseInfo));

            // Reuse an existing legacy row under its new, tenant-unique username (see caller).
            if (!string.IsNullOrWhiteSpace(renameFrom) && !string.IsNullOrWhiteSpace(renameTo))
            {
                var legacy = db.UserAccounts.FirstOrDefault(u => u.Username == renameFrom);
                var target = db.UserAccounts.FirstOrDefault(u => u.Username == renameTo);

                if (legacy != null && target == null)
                {
                    legacy.Username = renameTo;
                    db.SaveChanges();
                    Console.WriteLine($"  Reused existing '{renameFrom}' row (id {legacy.UserAccountId}) as '{renameTo}' so the username '{renameFrom}' stays unique to the tenant that requires it. No row was duplicated or deleted.");
                }
                else if (legacy != null && target != null)
                {
                    failures.Add($"'{renameFrom}' and '{renameTo}' both exist in {tenant.CompanyCode}");
                    Console.WriteLine($"  [FAIL] both '{renameFrom}' and '{renameTo}' exist in {tenant.CompanyCode} - refusing to merge accounts automatically.");
                }
            }

            if (!UserAccountsTableExists(db))
            {
                Console.WriteLine("  UserAccounts table is MISSING in this tenant database.");
                Console.WriteLine("  WHY: this tenant must authenticate its own users, and its schema predates");
                Console.WriteLine("       the authentication module. The table below is the SAME definition the");
                Console.WriteLine("       application's EF model (migration 20260923145800) expects - additive");
                Console.WriteLine("       only: no existing table, column or row is modified or dropped.");
                db.Database.ExecuteSqlRaw(
                    @"IF OBJECT_ID(N'dbo.UserAccounts', N'U') IS NULL
                      BEGIN
                          CREATE TABLE [dbo].[UserAccounts] (
                              [UserAccountId] int NOT NULL IDENTITY,
                              [Username] nvarchar(50) NOT NULL,
                              [PasswordHash] nvarchar(max) NOT NULL,
                              [FullName] nvarchar(max) NOT NULL,
                              [Role] nvarchar(max) NOT NULL,
                              [IsActive] bit NOT NULL,
                              [CreatedAt] datetime2 NOT NULL,
                              CONSTRAINT [PK_UserAccounts] PRIMARY KEY ([UserAccountId])
                          );
                          CREATE UNIQUE INDEX [IX_UserAccounts_Username] ON [dbo].[UserAccounts] ([Username]);
                      END");
                Console.WriteLine("  Created table [UserAccounts] (7 columns + unique username index).");
            }
            else
            {
                Console.WriteLine($"  UserAccounts table present, {db.UserAccounts.AsNoTracking().Count()} row(s).");
            }

            foreach (var account in plan)
            {
                var existing = db.UserAccounts.FirstOrDefault(u => u.Username == account.Username);

                if (existing == null)
                {
                    db.UserAccounts.Add(new UserAccount
                    {
                        Username = account.Username,
                        PasswordHash = PasswordHasher.Hash(DemoPassword),
                        FullName = account.FullName,
                        Role = account.Role,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });

                    db.SaveChanges();
                    Console.WriteLine($"  Created '{account.Username}' (role {account.Role}).");
                    continue;
                }

                // Reuse/update the existing row: fix role, active state and password to the
                // approved demo spec - the row is never duplicated or deleted.
                var changed = new List<string>();

                if (!string.Equals(existing.Role, account.Role, StringComparison.OrdinalIgnoreCase))
                {
                    changed.Add($"role '{existing.Role}' -> '{account.Role}'");
                    existing.Role = account.Role;
                }

                if (!existing.IsActive)
                {
                    changed.Add("active false -> true");
                    existing.IsActive = true;
                }

                if (string.IsNullOrWhiteSpace(existing.FullName))
                {
                    changed.Add("fullname set");
                    existing.FullName = account.FullName;
                }

                if (!PasswordHasher.Verify(existing.PasswordHash, DemoPassword, out _))
                {
                    changed.Add("password hash reset to demo password (hashed)");
                    existing.PasswordHash = PasswordHasher.Hash(DemoPassword);
                }

                if (changed.Count > 0)
                {
                    db.SaveChanges();
                    Console.WriteLine($"  Updated existing '{account.Username}': {string.Join("; ", changed)}.");
                }
                else
                {
                    Console.WriteLine($"  '{account.Username}' already exists and already matches the demo spec - unchanged.");
                }
            }
        }

        private static List<UserAccount>? ReadTenantUserStore(AppRuntime runtime, TenantOption tenant)
        {
            try
            {
                var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
                using var db = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(databaseInfo));

                if (!UserAccountsTableExists(db))
                {
                    return null;
                }

                return db.UserAccounts
                    .AsNoTracking()
                    .OrderBy(u => u.UserAccountId)
                    .ToList();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Names the hash FORMAT only - never exposes any hash material.</summary>
        private static string DescribeHashFormat(string? storedHash)
        {
            if (string.IsNullOrWhiteSpace(storedHash))
            {
                return "empty";
            }

            if (storedHash.StartsWith("PBKDF2-SHA256$", StringComparison.Ordinal))
            {
                return "PBKDF2-SHA256 (current)";
            }

            if (storedHash.Length == 64 && storedHash.All(c => Uri.IsHexDigit(c)))
            {
                return "legacy unsalted SHA-256 (needs rehash)";
            }

            return "unknown format";
        }

        private static bool UserAccountsTableExists(TenantAlphaEliteDbContext db)
        {
            var count = db.Database
                .SqlQueryRaw<int>("SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables WHERE name = 'UserAccounts'")
                .AsEnumerable()
                .First();

            return count > 0;
        }
    }
}
