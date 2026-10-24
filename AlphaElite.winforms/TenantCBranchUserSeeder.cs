using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Tenant C phase 7: idempotently seeds the six per-branch demo accounts
    /// (gymstaffMain/managerMain, gymstaffCebu/managerCebu, gymstaffDavao/managerDavao)
    /// with the approved demo password "123" (salted PBKDF2, same hasher as the login).
    /// Existing rows are skipped, never modified; the migrated 'admin' account is never
    /// touched. Re-running always converges to exactly these six accounts.
    /// </summary>
    internal static class TenantCBranchUserSeeder
    {
        public const string DemoPassword = "123";

        private sealed record SeedPlan(string Username, string FullName, string Role, string BranchCode);

        private static readonly SeedPlan[] Plans =
        {
            new("gymstaffMain",   "Gym Staff - BR-MAIN",   Roles.GymStaff, "BR-MAIN"),
            new("managerMain",    "Manager - BR-MAIN",     Roles.Manager,  "BR-MAIN"),
            new("gymstaffCebu",   "Gym Staff - BR-CEBU",   Roles.GymStaff, "BR-CEBU"),
            new("managerCebu",    "Manager - BR-CEBU",     Roles.Manager,  "BR-CEBU"),
            new("gymstaffDavao",  "Gym Staff - BR-DAVAO",  Roles.GymStaff, "BR-DAVAO"),
            new("managerDavao",   "Manager - BR-DAVAO",    Roles.Manager,  "BR-DAVAO")
        };

        public static int Run()
        {
            var failures = new List<string>();
            void Write(string text) => Console.WriteLine(text);

            try
            {
                var runtime = AppRuntime.Create();
                var tenants = runtime.AuthenticationService.GetAvailableTenantsAsync().GetAwaiter().GetResult();
                var tenantC = tenants.FirstOrDefault(t => t.IsBranchAware);

                if (tenantC == null)
                {
                    Write("Aborted: no branch-aware tenant (Tenant C) is configured.");
                    return 1;
                }

                using var db = runtime.DataContextFactory.CreateAsync(tenantC.CompanyId, true).GetAwaiter().GetResult();

                Write("======================================================================");
                Write("TENANT C BRANCH DEMO ACCOUNTS (phase 7)");
                Write("======================================================================");
                Write($"Target: {tenantC.CompanyCode} / {tenantC.CompanyName} (CompanyId {tenantC.CompanyId})");
                Write(string.Empty);

                // The migrated admin account is only ever READ here - never modified.
                var admin = db.UserAccounts.AsNoTracking().FirstOrDefault(u => u.Username == "admin");
                if (admin == null)
                {
                    Write("  [FAIL] the migrated 'admin' account is missing - aborting.");
                    return 1;
                }

                Write($"  Admin account untouched: username='admin', role='{admin.Role}', branchId={admin.BranchId?.ToString() ?? "null"}");
                Write(string.Empty);

                foreach (var plan in Plans)
                {
                    var existing = db.UserAccounts.AsNoTracking().FirstOrDefault(u => u.Username == plan.Username);
                    if (existing != null)
                    {
                        var correctRole = string.Equals(existing.Role, plan.Role, StringComparison.OrdinalIgnoreCase);
                        var correctBranch = existing.BranchId == (db.Branches.AsNoTracking()
                            .FirstOrDefault(b => b.BranchCode == plan.BranchCode)?.BranchId);

                        if (correctRole && correctBranch && existing.IsActive)
                        {
                            Write($"  Skipping '{plan.Username}': already exists with the expected role + branch (rows are never modified).");
                            continue;
                        }

                        failures.Add($"'{plan.Username}' exists with unexpected state (role='{existing.Role}', branchId={existing.BranchId?.ToString() ?? "null"}, active={existing.IsActive})");
                        Write($"  [FAIL] '{plan.Username}' exists with unexpected state - existing rows are never modified; fix manually.");
                        continue;
                    }

                    var branch = db.Branches.AsNoTracking().FirstOrDefault(b => b.BranchCode == plan.BranchCode);
                    if (branch == null)
                    {
                        failures.Add($"branch {plan.BranchCode} not found");
                        Write($"  [FAIL] branch {plan.BranchCode} not found.");
                        continue;
                    }

                    db.UserAccounts.Add(new UserAccount
                    {
                        Username = plan.Username,
                        PasswordHash = PasswordHasher.Hash(DemoPassword),
                        FullName = plan.FullName,
                        Role = plan.Role,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        MemberId = null,
                        BranchId = branch.BranchId
                    });

                    db.SaveChangesAsync().GetAwaiter().GetResult();
                    Write($"  Created '{plan.Username}' role {plan.Role} -> branch {plan.BranchCode} (id {branch.BranchId}), demo password");
                }

                Write(string.Empty);
                Write("----------------------------------------------------------------------");
                Write("In-process login verification (demo password, never printed here)");
                Write("----------------------------------------------------------------------");

                foreach (var plan in Plans)
                {
                    var result = runtime.AuthenticationService
                        .AuthenticateAsync(tenantC.CompanyId, plan.Username, DemoPassword)
                        .GetAwaiter().GetResult();

                    var branch = db.Branches.AsNoTracking().FirstOrDefault(b => b.BranchCode == plan.BranchCode);

                    if (result == null || result.Role != plan.Role || result.BranchId != branch?.BranchId)
                    {
                        failures.Add($"login verification failed for '{plan.Username}'");
                        Write($"  [FAIL] '{plan.Username}': {(result == null ? "login rejected" : $"role={result.Role}, branchId={result.BranchId?.ToString() ?? "null"}")}");
                        continue;
                    }

                    Write($"  [PASS] '{plan.Username}': role={result.Role}, branchId={result.BranchId}, branchAware={result.IsBranchAware}");
                }

                Write(string.Empty);
                Write($"--- Result: {(failures.Count == 0 ? "OK" : "PROBLEMS")} | failures: {failures.Count} ---");
                return failures.Count == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }
    }
}
