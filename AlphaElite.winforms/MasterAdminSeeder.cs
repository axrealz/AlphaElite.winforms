using AlphaElite.infrastructure.services;
using System;
using System.Collections.Generic;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 5: seeds exactly ONE verified SuperAdmin demo account in the MASTER database via
    /// ASP.NET Identity. Idempotent: if the username already exists, nothing is created and the
    /// password is never changed. The generated password is printed ONCE to the console and
    /// stored nowhere (no source, config, migration, git or report file).
    /// </summary>
    internal static class MasterAdminSeeder
    {
        public const string DemoUsername = "superadmin";

        public static int Run()
        {
            var failures = new List<string>();
            void Write(string text) => Console.WriteLine(text);

            try
            {
                var runtime = AppRuntime.Create();

                Write("======================================================================");
                Write("MASTER ADMIN SEEDING (ASP.NET Identity, Master DB)");
                Write("Write footprint: 1 AspNetRoles row + 1 AspNetUsers row + 1 AspNetUserRoles link");
                Write("======================================================================");

                var service = runtime.MasterIdentityService;

                // Existing data is never replaced: if any master user already exists, seed nothing.
                if (service.AnyUserExistsAsync().GetAwaiter().GetResult())
                {
                    Write("  [SKIP] A master Identity user already exists - the seed step never overwrites existing users.");
                    Write("  Re-run --phase5-verify to check the existing account instead.");
                    return 0;
                }

                var password = GenerateSecurePassword();

                var (success, error) = service.CreateUserAsync(DemoUsername, password, string.Empty, MasterIdentityService.SuperAdminRoleName)
                    .GetAwaiter().GetResult();

                if (!success)
                {
                    Write($"  [FAIL] could not create '{DemoUsername}': {error}");
                    return 1;
                }

                Write($"  Created master user '{DemoUsername}' with role '{MasterIdentityService.SuperAdminRoleName}'.");
                Write(string.Empty);

                // In-process verification: the account must authenticate through Identity.
                var login = service.AuthenticateAsync(DemoUsername, password).GetAwaiter().GetResult();
                if (!login.Success || login.Role != Roles.MasterAdmin)
                {
                    Write($"  [FAIL] verification login failed (success={login.Success}, role={login.Role}).");
                    failures.Add("verification login failed");
                }
                else
                {
                    Write($"  [PASS] verification login: username={login.Username}, role={login.Role}, masterUserId={login.MasterUserId}");
                }

                Write(string.Empty);
                Write("----------------------------------------------------------------------");
                Write("ONE-TIME DEMO CREDENTIALS (not stored anywhere - copy them now)");
                Write("----------------------------------------------------------------------");
                Write($"  Username: {DemoUsername}");
                Write($"  Password: {password}");
                Write("----------------------------------------------------------------------");
                Write("These credentials are printed exactly once and stored nowhere.");

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
            // Crypto-random, 20 chars across upper/lower/digit/symbol so Identity's default
            // complexity requirements are satisfied by construction.
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghijkmnopqrstuvwxyz";
            const string digits = "23456789";
            const string symbols = "!@#$%&*";
            var alphabet = upper + lower + digits + symbols;

            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(20);
            var chars = new char[20];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = alphabet[bytes[i] % alphabet.Length];
            }

            // Force one character from each class so complexity validators always pass.
            chars[0] = upper[bytes[0] % upper.Length];
            chars[1] = lower[bytes[1] % lower.Length];
            chars[2] = digits[bytes[2] % digits.Length];
            chars[3] = symbols[bytes[3] % symbols.Length];

            return new string(chars);
        }
    }
}
