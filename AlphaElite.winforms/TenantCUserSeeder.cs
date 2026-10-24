using AlphaElite.domain.entities;
using AlphaElite.infrastructure.services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 4F: creates the minimum Tenant C demo accounts (Manager, GymStaff, Member) needed
    /// to demonstrate every role. The existing migrated 'admin' account is the Admin demo and
    /// is never recreated or modified.
    ///
    /// Passwords are generated with a cryptographic RNG, hashed immediately with salted PBKDF2,
    /// and printed exactly once to the console (the delivery mechanism the user chose). They are
    /// never written to source, config, migration, git or any report file.
    /// </summary>
    internal static class TenantCUserSeeder
    {
        private sealed record SeedPlan(string Username, string FullName, string Role, string BranchCode, bool LinkToMember);

        private static readonly SeedPlan[] Plans =
        {
            new("manager",  "Branch A Manager", Roles.Manager,  "BR-A", false),
            new("gymstaff", "Branch B Gym Staff", Roles.GymStaff, "BR-B", false),
            new("member",   "Member (Branch A)", Roles.Member,   "BR-A", true)
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
                Write("TENANT C DEMO USER SEEDING");
                Write("======================================================================");
                Write($"Target: {tenantC.CompanyCode} / {tenantC.CompanyName} (CompanyId {tenantC.CompanyId})");
                Write(string.Empty);

                var admin = db.UserAccounts.AsNoTracking().FirstOrDefault(u => u.Username == "admin");
                if (admin == null)
                {
                    Write("  [FAIL] the migrated 'admin' account is missing - aborting so it is not replaced.");
                    return 1;
                }

                Write($"  Existing Admin account preserved: username='{admin.Username}', role='{admin.Role}', active={admin.IsActive}, branchId={admin.BranchId?.ToString() ?? "null"}");
                Write(string.Empty);

                var createdCredentials = new List<(string Username, string Role, string Password, string Scope)>();

                foreach (var plan in Plans)
                {
                    var existing = db.UserAccounts.AsNoTracking().FirstOrDefault(u => u.Username == plan.Username);
                    if (existing != null)
                    {
                        Write($"  Skipping '{plan.Username}': already exists (role '{existing.Role}'). Existing rows are never modified.");
                        continue;
                    }

                    var branch = db.Branches.AsNoTracking().FirstOrDefault(b => b.BranchCode == plan.BranchCode);
                    if (branch == null)
                    {
                        failures.Add($"branch {plan.BranchCode} not found");
                        Write($"  [FAIL] branch {plan.BranchCode} not found.");
                        continue;
                    }

                    int? memberId = null;
                    if (plan.LinkToMember)
                    {
                        var member = db.Members.AsNoTracking()
                            .Where(m => m.BranchId == branch.BranchId)
                            .OrderBy(m => m.MemberId)
                            .FirstOrDefault();

                        if (member == null)
                        {
                            failures.Add($"no member in {plan.BranchCode} to link '{plan.Username}'");
                            Write($"  [FAIL] no member available in {plan.BranchCode} to link '{plan.Username}'.");
                            continue;
                        }

                        memberId = member.MemberId;
                    }

                    var password = GenerateSecurePassword();

                    db.UserAccounts.Add(new UserAccount
                    {
                        Username = plan.Username,
                        PasswordHash = PasswordHasher.Hash(password),
                        FullName = plan.FullName,
                        Role = plan.Role,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        MemberId = memberId,
                        BranchId = branch.BranchId
                    });

                    db.SaveChangesAsync().GetAwaiter().GetResult();

                    var scope = $"branch {branch.BranchCode} ({branch.BranchId})" + (memberId.HasValue ? $", member {memberId}" : string.Empty);
                    createdCredentials.Add((plan.Username, plan.Role, password, scope));
                    Write($"  Created '{plan.Username}' role {plan.Role} -> {scope}");
                }

                Write(string.Empty);
                Write("----------------------------------------------------------------------");
                Write("ONE-TIME DEMO CREDENTIALS (not stored anywhere - copy them now)");
                Write("----------------------------------------------------------------------");
                Write($"  {"Role",-12} {"Username",-10} {"Password",-20} Scope");
                Write($"  {"Admin",-12} {"admin",-10} {"(unchanged - existing account)",-20} branch BR-B (2), memberId null");

                foreach (var (username, role, password, scope) in createdCredentials)
                {
                    Write($"  {role,-12} {username,-10} {password,-20} {scope}");
                }

                Write(string.Empty);
                Write("----------------------------------------------------------------------");
                Write("In-process login verification for the generated accounts");
                Write("----------------------------------------------------------------------");

                foreach (var (username, role, password, _) in createdCredentials)
                {
                    var result = runtime.AuthenticationService
                        .AuthenticateAsync(tenantC.CompanyId, username, password)
                        .GetAwaiter().GetResult();

                    if (result == null)
                    {
                        failures.Add($"login failed for {username}");
                        Write($"  [FAIL] login for '{username}' was rejected.");
                        continue;
                    }

                    Write($"  [PASS] '{username}': role={result.Role}, branchId={result.BranchId?.ToString() ?? "null"}, memberId={result.MemberId?.ToString() ?? "null"}, branchAware={result.IsBranchAware}");
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

        private static string GenerateSecurePassword()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%&*";
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
            var chars = new char[16];

            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = alphabet[bytes[i] % alphabet.Length];
            }

            return new string(chars);
        }
    }
}
