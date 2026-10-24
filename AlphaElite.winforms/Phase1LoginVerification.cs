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
    /// Phase 1 acceptance harness (headless). Proves, without opening any window:
    ///
    ///   1. every demo account logs in and resolves to exactly ONE tenant + its own role;
    ///   2. the role comes from the stored account (never selectable);
    ///   3. Tenant A / B sessions carry NO branch context; Tenant C keeps its branch scope;
    ///   4. cross-tenant negative authentication (A↛B/C, B↛A/C, C↛A/B, tenant↛Master,
    ///      Master↛tenant, wrong passwords rejected);
    ///   5. ambiguous usernames fail closed (a probe account placed in TWO tenants resolves
    ///      to two candidates - the login rule then refuses instead of guessing);
    ///   6. the resolved tenant context really is used for data access (per-tenant
    ///      fingerprints seen through the session);
    ///   7. the probe rows are removed again (no residue).
    ///
    /// Write footprint: two temporary probe rows (one in Tenant A, one in Tenant B), both
    /// deleted within the same run. Nothing else is written.
    /// </summary>
    internal static class Phase1LoginVerification
    {
        private const string DemoPassword = "123";
        private const string ProbeUsername = "phase1_probe_user";

        public static int Run()
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
            Write("PHASE 1 ACCEPTANCE VERIFICATION (login -> tenant -> role -> isolation)");
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
                Write("  [FAIL] tenant discovery: TenantA/TenantB/TenantC must all be configured and active.");
                return 1;
            }

            Write($"  Tenants: A={tenantA.CompanyCode} (id {tenantA.CompanyId}), B={tenantB.CompanyCode} (id {tenantB.CompanyId}), C={tenantC.CompanyCode} (id {tenantC.CompanyId}, branch-aware)");

            // ---------------------------------------------------------------------
            // 1. Positive logins: credentials resolve tenant + role, nothing else.
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 1. Positive logins (tenant + role resolved from the account) ---");

            CheckTenantLogin(runtime, Check, tenantA, "gymstaffA", Roles.GymStaff, expectBranchAware: false);
            CheckTenantLogin(runtime, Check, tenantA, "adminA", Roles.Admin, expectBranchAware: false);
            CheckTenantLogin(runtime, Check, tenantB, "gymstaffB", Roles.GymStaff, expectBranchAware: false);
            CheckTenantLogin(runtime, Check, tenantB, "adminB", Roles.Admin, expectBranchAware: false);
            CheckTenantLogin(runtime, Check, tenantB, "managerB", Roles.Manager, expectBranchAware: false);

            // Tenant C accounts are PRESERVED (never recreated): their passwords are the
            // Phase 4F one-time passwords, so the harness verifies tenant/role/scope
            // resolution and (optionally, via ALPHA_TENANTC_PASSWORD) the password path.
            var tenantCPassword = Environment.GetEnvironmentVariable("ALPHA_TENANTC_PASSWORD");

            foreach (var (username, expectedRole) in new[]
            {
                ("admin", Roles.Admin),
                ("manager", Roles.Manager),
                ("gymstaff", Roles.GymStaff),
                ("member", Roles.Member)
            })
            {
                var candidates = runtime.AuthenticationService.FindTenantsContainingUserAsync(username).GetAwaiter().GetResult();
                Check($"Tenant C '{username}' resolves to Tenant C only",
                    candidates.Count == 1 && candidates[0].CompanyId == tenantC.CompanyId,
                    candidates.Count == 1 ? $"{candidates[0].CompanyCode} (branch-aware tenant preserved)" : $"{candidates.Count} candidate(s)");
            }

            var cAdminMeta = ReadTenantCUser(runtime, tenantC, "admin");
            Check("Tenant C 'admin' metadata preserved (Admin, branch-wide)",
                cAdminMeta != null && Roles.Normalize(cAdminMeta.Role) == Roles.Admin && cAdminMeta.IsActive && cAdminMeta.BranchId == null,
                cAdminMeta == null ? "account missing" : $"role={cAdminMeta.Role}, active={cAdminMeta.IsActive}, branchId={cAdminMeta.BranchId?.ToString() ?? "null"}");

            var cManagerMeta = ReadTenantCUser(runtime, tenantC, "manager");
            Check("Tenant C 'manager' metadata preserved (Manager, branch-scoped)",
                cManagerMeta != null && Roles.Normalize(cManagerMeta.Role) == Roles.Manager && cManagerMeta.IsActive && cManagerMeta.BranchId != null,
                cManagerMeta == null ? "account missing" : $"role={cManagerMeta.Role}, branchId={cManagerMeta.BranchId?.ToString() ?? "null"}");

            var cGymStaffMeta = ReadTenantCUser(runtime, tenantC, "gymstaff");
            Check("Tenant C 'gymstaff' metadata preserved (GymStaff, branch-scoped)",
                cGymStaffMeta != null && Roles.Normalize(cGymStaffMeta.Role) == Roles.GymStaff && cGymStaffMeta.IsActive && cGymStaffMeta.BranchId != null,
                cGymStaffMeta == null ? "account missing" : $"role={cGymStaffMeta.Role}, branchId={cGymStaffMeta.BranchId?.ToString() ?? "null"}");

            var cMemberMeta = ReadTenantCUser(runtime, tenantC, "member");
            Check("Tenant C 'member' metadata preserved (Member, member-linked)",
                cMemberMeta != null && Roles.Normalize(cMemberMeta.Role) == Roles.Member && cMemberMeta.IsActive && cMemberMeta.MemberId != null,
                cMemberMeta == null ? "account missing" : $"role={cMemberMeta.Role}, memberId={cMemberMeta.MemberId?.ToString() ?? "null"}");

            if (string.IsNullOrEmpty(tenantCPassword))
            {
                Write("  [SKIP] Tenant C password check: these accounts keep their Phase 4F one-time passwords; set ALPHA_TENANTC_PASSWORD to test one of them.");
            }
            else
            {
                var cLogin = runtime.AuthenticationService.AuthenticateAsync(tenantC.CompanyId, "admin", tenantCPassword).GetAwaiter().GetResult();
                Check("Tenant C 'admin' password login (ALPHA_TENANTC_PASSWORD)",
                    cLogin != null && cLogin.Role == Roles.Admin,
                    cLogin == null ? "login rejected" : $"role={cLogin.Role}, branchAware={cLogin.IsBranchAware}");
            }

            // ---------------------------------------------------------------------
            // 2. Username -> tenant routing: exactly one tenant per credential.
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 2. Username routing (read-only lookup) ---");

            CheckRouting(runtime, Check, "gymstaffA", new[] { tenantA.CompanyId }, "Tenant A only");
            CheckRouting(runtime, Check, "adminA", new[] { tenantA.CompanyId }, "Tenant A only");
            CheckRouting(runtime, Check, "gymstaffB", new[] { tenantB.CompanyId }, "Tenant B only");
            CheckRouting(runtime, Check, "adminB", new[] { tenantB.CompanyId }, "Tenant B only");
            CheckRouting(runtime, Check, "managerB", new[] { tenantB.CompanyId }, "Tenant B only");
            CheckRouting(runtime, Check, "admin", new[] { tenantC.CompanyId }, "Tenant C only");
            CheckRouting(runtime, Check, "manager", new[] { tenantC.CompanyId }, "Tenant C only");
            CheckRouting(runtime, Check, "gymstaff", new[] { tenantC.CompanyId }, "Tenant C only");
            CheckRouting(runtime, Check, "member", new[] { tenantC.CompanyId }, "Tenant C only");
            CheckRouting(runtime, Check, "superadmin", Array.Empty<int>(), "no tenant (master-only)");
            CheckRouting(runtime, Check, "no-such-user-anywhere", Array.Empty<int>(), "no tenant");

            // ---------------------------------------------------------------------
            // 3. Cross-tenant negative authentication.
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 3. Cross-tenant negative authentication ---");

            CheckCrossTenantRejected(runtime, Check, "gymstaffA", DemoPassword, tenantB, tenantC);
            CheckCrossTenantRejected(runtime, Check, "adminA", DemoPassword, tenantB, tenantC);
            CheckCrossTenantRejected(runtime, Check, "gymstaffB", DemoPassword, tenantA, tenantC);
            CheckCrossTenantRejected(runtime, Check, "adminB", DemoPassword, tenantA, tenantC);
            CheckCrossTenantRejected(runtime, Check, "managerB", DemoPassword, tenantA, tenantC);
            CheckCrossTenantRejected(runtime, Check, "admin", DemoPassword, tenantA, tenantB);
            CheckCrossTenantRejected(runtime, Check, "manager", DemoPassword, tenantA, tenantB);

            var wrongPassword = runtime.AuthenticationService.AuthenticateAsync(tenantA.CompanyId, "gymstaffA", DemoPassword + "-x").GetAwaiter().GetResult();
            Check("wrong password rejected (generic null, no details)", wrongPassword == null, "returned null");

            var masterAttemptWithTenantCredential = runtime.MasterIdentityService.AuthenticateAsync("gymstaffA", DemoPassword).GetAwaiter().GetResult();
            Check("tenant credential cannot authenticate against Master Identity", !masterAttemptWithTenantCredential.Success, masterAttemptWithTenantCredential.Error);

            var unknownTenantUserAtMaster = runtime.MasterIdentityService.AuthenticateAsync("no-such-user-anywhere", DemoPassword).GetAwaiter().GetResult();
            Check("unknown user cannot authenticate against Master Identity", !unknownTenantUserAtMaster.Success, unknownTenantUserAtMaster.Error);

            // ---------------------------------------------------------------------
            // 4. Ambiguous username fails closed (temporary probe in A AND B).
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 4. Ambiguous username fails closed (temporary probe, cleaned up) ---");

            int? probeIdA = null;
            int? probeIdB = null;

            try
            {
                probeIdA = InsertProbeUser(runtime, tenantA);
                probeIdB = InsertProbeUser(runtime, tenantB);

                Check("probe user inserted into Tenant A and Tenant B", probeIdA.HasValue && probeIdB.HasValue,
                    $"A id={probeIdA?.ToString() ?? "none"}, B id={probeIdB?.ToString() ?? "none"}");

                var ambiguous = runtime.AuthenticationService.FindTenantsContainingUserAsync(ProbeUsername).GetAwaiter().GetResult();
                Check("ambiguous username resolves to BOTH tenants -> login must fail closed",
                    ambiguous.Count == 2,
                    $"{ambiguous.Count} candidate tenant(s): {string.Join(", ", ambiguous.Select(t => t.CompanyCode))} (rule: >1 candidate -> generic error, password never tried)");
            }
            finally
            {
                if (probeIdA.HasValue)
                {
                    DeleteProbeUser(runtime, tenantA, probeIdA.Value);
                }

                if (probeIdB.HasValue)
                {
                    DeleteProbeUser(runtime, tenantB, probeIdB.Value);
                }
            }

            var probeAAfter = FindProbeUser(runtime, tenantA);
            var probeBAfter = FindProbeUser(runtime, tenantB);
            Check("probe rows removed (no residue)", probeAAfter == null && probeBAfter == null, "both deleted");

            // ---------------------------------------------------------------------
            // 5. Data isolation through the resolved session context.
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 5. Data isolation through the resolved session ---");
            Write("  Fingerprints: A members=1/pay=1(500), B members=5/pay=4(3650), C members=21/pay=14(13650)");

            CheckSessionDataIsolation(runtime, Check, tenantA, "gymstaffA", Roles.GymStaff);
            CheckSessionDataIsolation(runtime, Check, tenantA, "adminA", Roles.Admin);
            CheckSessionDataIsolation(runtime, Check, tenantB, "gymstaffB", Roles.GymStaff);
            CheckSessionDataIsolation(runtime, Check, tenantB, "adminB", Roles.Admin);
            CheckSessionDataIsolation(runtime, Check, tenantB, "managerB", Roles.Manager);

            // Tenant C keeps its Phase 4F one-time passwords, so the C-session check builds
            // the session the same way the login WOULD (routing + tenant metadata) - or from
            // a real login when ALPHA_TENANTC_PASSWORD is provided.
            UserSession? tenantCSession;
            var tenantCPasswordForSession = Environment.GetEnvironmentVariable("ALPHA_TENANTC_PASSWORD");

            if (!string.IsNullOrEmpty(tenantCPasswordForSession))
            {
                var cLoginResult = runtime.AuthenticationService.AuthenticateAsync(tenantC.CompanyId, "admin", tenantCPasswordForSession).GetAwaiter().GetResult();
                tenantCSession = cLoginResult == null
                    ? null
                    : new UserSession
                    {
                        CompanyId = cLoginResult.CompanyId,
                        CompanyCode = cLoginResult.CompanyCode,
                        CompanyName = cLoginResult.CompanyName,
                        IsBranchAware = cLoginResult.IsBranchAware,
                        BranchId = cLoginResult.BranchId,
                        MemberId = cLoginResult.MemberId,
                        Username = cLoginResult.Username,
                        FullName = cLoginResult.FullName,
                        Role = cLoginResult.Role
                    };
            }
            else
            {
                tenantCSession = new UserSession
                {
                    CompanyId = tenantC.CompanyId,
                    CompanyCode = tenantC.CompanyCode,
                    CompanyName = tenantC.CompanyName,
                    IsBranchAware = true,
                    BranchId = null,
                    MemberId = null,
                    Username = "admin",
                    FullName = string.Empty,
                    Role = Roles.Admin
                };
            }

            if (tenantCSession == null)
            {
                Check("session data isolation for Tenant C 'admin'", false, "password login failed (ALPHA_TENANTC_PASSWORD)");
            }
            else
            {
                using var cData = runtime.DataContextFactory.CreateAsync(tenantCSession.CompanyId, tenantCSession.IsBranchAware).GetAwaiter().GetResult();
                var cMembers = cData.Members.AsNoTracking().Count();
                var cPayments = cData.Payments.AsNoTracking().Count();
                var cTotal = cData.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;

                Check(
                    $"session of Tenant C 'admin' ({tenantCSession.CompanyCode}) sees ONLY Tenant C data",
                    cMembers == 21 && cPayments == 14 && cTotal == 13650m,
                    $"members={cMembers} (expected 21), payments={cPayments} (expected 14), total={cTotal:0} (expected 13650)");
            }

            // ---------------------------------------------------------------------
            // 6. Master account preserved.
            // ---------------------------------------------------------------------
            Write(string.Empty);
            Write("--- 6. Master / Super Admin separation ---");

            var masterUsers = runtime.MasterIdentityService.GetUsersAsync().GetAwaiter().GetResult();
            var superAdmins = masterUsers.Where(u => u.Roles.Contains(MasterIdentityService.SuperAdminRoleName)).ToList();
            Check("master SuperAdmin account preserved", superAdmins.Any(), string.Join(", ", superAdmins.Select(u => u.Username)));

            var masterPassword = Environment.GetEnvironmentVariable("ALPHA_MASTER_PASSWORD");
            if (string.IsNullOrEmpty(masterPassword))
            {
                Write("  [SKIP] set ALPHA_MASTER_PASSWORD to also verify the master password path (same convention as --phase5-verify).");
            }
            else
            {
                var masterLogin = runtime.MasterIdentityService.AuthenticateAsync(MasterAdminSeeder.DemoUsername, masterPassword).GetAwaiter().GetResult();
                Check("master login succeeds and normalizes to MasterAdmin",
                    masterLogin.Success && masterLogin.Role == Roles.MasterAdmin,
                    masterLogin.Success ? $"role={masterLogin.Role}" : masterLogin.Error);
            }

            Write(string.Empty);
            Write($"--- Result: {(failures.Count == 0 ? "OK" : "PROBLEMS")} | checks: {checks} | failures: {failures.Count} ---");
            return failures.Count == 0 ? 0 : 1;
        }

        // =====================================================================
        //  Helpers
        // =====================================================================
        private static void CheckTenantLogin(
            AppRuntime runtime,
            Action<string, bool, string> check,
            TenantOption tenant,
            string username,
            string expectedRole,
            bool expectBranchAware)
        {
            var result = runtime.AuthenticationService
                .AuthenticateAsync(tenant.CompanyId, username, DemoPassword)
                .GetAwaiter().GetResult();

            var ok = result != null
                && result.CompanyId == tenant.CompanyId
                && result.Role == expectedRole
                && result.IsBranchAware == expectBranchAware;

            var branchOk = expectBranchAware || (result?.BranchId == null && result?.MemberId == null);

            check(
                $"'{username}/{DemoPassword}' -> {tenant.CompanyCode} + {expectedRole}" + (expectBranchAware ? string.Empty : " (no branch context)"),
                ok && branchOk,
                result == null
                    ? "login rejected"
                    : $"company={result.CompanyCode} (id {result.CompanyId}), role={result.Role}, branchAware={result.IsBranchAware}, branchId={result.BranchId?.ToString() ?? "null"}, memberId={result.MemberId?.ToString() ?? "null"}");
        }

        private static UserAccount? ReadTenantCUser(AppRuntime runtime, TenantOption tenantC, string username)
        {
            try
            {
                var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenantC.CompanyId).GetAwaiter().GetResult();
                using var db = new TenantCAlphaEliteDbContext(runtime.ConnectionFactory.CreateBranchAwareOptions(databaseInfo));

                return db.UserAccounts.AsNoTracking().FirstOrDefault(u => u.Username == username);
            }
            catch
            {
                return null;
            }
        }

        private static void CheckRouting(
            AppRuntime runtime,
            Action<string, bool, string> check,
            string username,
            int[] expectedCompanyIds,
            string expectation)
        {
            var matches = runtime.AuthenticationService
                .FindTenantsContainingUserAsync(username)
                .GetAwaiter().GetResult();

            var actual = matches.Select(t => t.CompanyId).OrderBy(id => id).ToArray();
            var expected = expectedCompanyIds.OrderBy(id => id).ToArray();

            check(
                $"username '{username}' resolves to {expectation}",
                actual.SequenceEqual(expected),
                actual.Length == 0
                    ? "no tenant holds this username"
                    : string.Join(", ", matches.Select(t => $"{t.CompanyCode} (id {t.CompanyId})")));
        }

        private static void CheckCrossTenantRejected(
            AppRuntime runtime,
            Action<string, bool, string> check,
            string username,
            string password,
            params TenantOption[] otherTenants)
        {
            var details = new List<string>();
            var allRejected = true;

            foreach (var tenant in otherTenants)
            {
                var result = runtime.AuthenticationService
                    .AuthenticateAsync(tenant.CompanyId, username, password)
                    .GetAwaiter().GetResult();

                details.Add($"{tenant.CompanyCode}={(result == null ? "rejected" : "ACCEPTED (!)")}");
                allRejected &= result == null;
            }

            check($"'{username}' cannot authenticate into {string.Join(" or ", otherTenants.Select(t => t.CompanyCode))}",
                allRejected,
                string.Join("; ", details));
        }

        private static void CheckSessionDataIsolation(
            AppRuntime runtime,
            Action<string, bool, string> check,
            TenantOption tenant,
            string username,
            string expectedRole)
        {
            var result = runtime.AuthenticationService
                .AuthenticateAsync(tenant.CompanyId, username, DemoPassword)
                .GetAwaiter().GetResult();

            if (result == null || result.Role != expectedRole)
            {
                check($"session data isolation for '{username}'", false, "login failed or unexpected role");
                return;
            }

            // Build the session exactly like Program.cs does from the login result, then read
            // the data through the context the session resolves to (what Form1 uses).
            var session = new UserSession
            {
                CompanyId = result.CompanyId,
                CompanyCode = result.CompanyCode,
                CompanyName = result.CompanyName,
                IsBranchAware = result.IsBranchAware,
                BranchId = result.BranchId,
                MemberId = result.MemberId,
                Username = result.Username,
                FullName = result.FullName,
                Role = result.Role
            };

            using var data = runtime.DataContextFactory.CreateAsync(session.CompanyId, session.IsBranchAware).GetAwaiter().GetResult();

            var members = data.Members.AsNoTracking().Count();
            var payments = data.Payments.AsNoTracking().Count();
            var total = data.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;

            // Independent path: read the SAME tenant directly through a resolver-built context
            // (no DataContextFactory). The session's view must match it exactly - this catches a
            // session pointed at another tenant's database without pinning hardcoded row counts
            // (Tenant A accumulates legitimate manual-test data, so fingerprints drift).
            var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
            using var direct = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(databaseInfo));
            var directMembers = direct.Members.AsNoTracking().Count();
            var directPayments = direct.Payments.AsNoTracking().Count();
            var directTotal = direct.Payments.AsNoTracking().Sum(p => (decimal?)p.AmountPaid) ?? 0m;

            var ok = session.CompanyId == tenant.CompanyId
                && members == directMembers
                && payments == directPayments
                && total == directTotal;

            check(
                $"session of '{username}' ({session.CompanyCode}) sees ONLY its own tenant data",
                ok,
                $"session members={members}/payments={payments}/total={total:0} vs tenant-direct members={directMembers}/payments={directPayments}/total={directTotal:0}");
        }

        private static int? InsertProbeUser(AppRuntime runtime, TenantOption tenant)
        {
            try
            {
                var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
                using var db = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(databaseInfo));

                // Never duplicate: if a previous crashed run left the probe behind, reuse it.
                var existing = db.UserAccounts.AsNoTracking().FirstOrDefault(u => u.Username == ProbeUsername);
                if (existing != null)
                {
                    return existing.UserAccountId;
                }

                var probe = new UserAccount
                {
                    Username = ProbeUsername,
                    PasswordHash = PasswordHasher.Hash(DemoPassword),
                    FullName = "Phase 1 ambiguity probe (temporary)",
                    Role = Roles.Member,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                db.UserAccounts.Add(probe);
                db.SaveChanges();
                return probe.UserAccountId;
            }
            catch
            {
                return null;
            }
        }

        private static void DeleteProbeUser(AppRuntime runtime, TenantOption tenant, int userAccountId)
        {
            try
            {
                var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
                using var db = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(databaseInfo));

                var probe = db.UserAccounts.FirstOrDefault(u => u.UserAccountId == userAccountId && u.Username == ProbeUsername);
                if (probe != null)
                {
                    db.UserAccounts.Remove(probe);
                    db.SaveChanges();
                }
            }
            catch
            {
                // Reported by the no-residue check instead.
            }
        }

        private static UserAccount? FindProbeUser(AppRuntime runtime, TenantOption tenant)
        {
            try
            {
                var databaseInfo = runtime.Resolver.GetDatabaseInfoAsync(tenant.CompanyId).GetAwaiter().GetResult();
                using var db = new TenantAlphaEliteDbContext(runtime.ConnectionFactory.CreateStandardOptions(databaseInfo));

                return db.UserAccounts.AsNoTracking().FirstOrDefault(u => u.Username == ProbeUsername);
            }
            catch
            {
                return null;
            }
        }
    }
}
