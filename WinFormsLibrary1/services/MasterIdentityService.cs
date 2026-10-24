using AlphaElite.infrastructure.data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlphaElite.infrastructure.services
{
    /// <summary>Result of a master (Identity) authentication attempt. Contains no credential material.</summary>
    public sealed class MasterLoginResult
    {
        public bool Success { get; init; }
        public string Error { get; init; } = string.Empty;

        /// <summary>AspNetUsers.Id of the authenticated master user.</summary>
        public string MasterUserId { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;

        /// <summary>Canonical application role (e.g. <see cref="Roles.MasterAdmin"/>).</summary>
        public string Role { get; init; } = string.Empty;

        /// <summary>True when the account is locked out (reported separately from a wrong password).</summary>
        public bool IsLockedOut { get; init; }
    }

    /// <summary>Read-only projection of a master Identity user for the admin UI. Never exposes hashes.</summary>
    public sealed class MasterUserInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public bool IsLockedOut { get; init; }
        public int AccessFailedCount { get; init; }
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Master / platform-level authentication and user administration on top of ASP.NET Core
    /// Identity in the MASTER database.
    ///
    /// Deliberately separate from <see cref="TenantAuthenticationService"/>: master accounts live
    /// in the Master DB AspNet* tables, passwords are hashed by Identity's own PBKDF2 hasher
    /// (never the tenant <c>PasswordHasher</c>), and a master login can never be confused with a
    /// tenant account or role. Every mutating operation runs on a short-lived context so the
    /// shared long-lived master context never accumulates tracked entities.
    /// </summary>
    public interface IMasterIdentityService
    {
        Task<MasterLoginResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<MasterUserInfo>> GetUsersAsync(CancellationToken cancellationToken = default);
        Task<MasterUserInfo?> FindByIdAsync(string userId, CancellationToken cancellationToken = default);
        Task<(bool Success, string Error)> CreateUserAsync(string username, string password, string email, string role, CancellationToken cancellationToken = default);
        Task<(bool Success, string Error)> SetRoleAsync(string userId, string role, CancellationToken cancellationToken = default);
        Task<(bool Success, string Error)> SetActiveAsync(string userId, bool active, CancellationToken cancellationToken = default);
        Task<(bool Success, string Error)> ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default);
        Task<bool> AnyUserExistsAsync(CancellationToken cancellationToken = default);
    }

    public sealed class MasterIdentityService : IMasterIdentityService
    {
        /// <summary>Role name as stored in AspNetRoles. The app normalizes it to Roles.MasterAdmin.</summary>
        public const string SuperAdminRoleName = "SuperAdmin";

        private readonly MasterAlphaEliteDbContext _masterDb;

        public MasterIdentityService(MasterAlphaEliteDbContext masterDb)
        {
            _masterDb = masterDb ?? throw new ArgumentNullException(nameof(masterDb));
        }

        public async Task<MasterLoginResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                return new MasterLoginResult { Success = false, Error = "Invalid username or password." };
            }

            // All Identity work happens on a short-lived context (see class remarks).
            await using var db = await CloneContextAsync(cancellationToken);
            var userManager = CreateUserManager(db);

            var user = await userManager.FindByNameAsync(username.Trim());
            if (user == null)
            {
                return new MasterLoginResult { Success = false, Error = "Invalid username or password." };
            }

            // Identity's own verification: PBKDF2 hash check with transparent rehash-on-verify.
            var valid = await userManager.CheckPasswordAsync(user, password);
            if (!valid)
            {
                return new MasterLoginResult { Success = false, Error = "Invalid username or password." };
            }

            if (await userManager.IsLockedOutAsync(user))
            {
                return new MasterLoginResult { Success = false, Error = "This account is locked out.", IsLockedOut = true };
            }

            var identityRoles = await userManager.GetRolesAsync(user);
            var canonicalRole = identityRoles.Count > 0
                ? Roles.Normalize(identityRoles[0])
                : Roles.Normalize(null);

            return new MasterLoginResult
            {
                Success = true,
                MasterUserId = user.Id,
                Username = user.UserName ?? username.Trim(),
                Role = canonicalRole
            };
        }

        public async Task<IReadOnlyList<MasterUserInfo>> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            var users = await _masterDb.Users
                .AsNoTracking()
                .OrderBy(u => u.UserName)
                .ToListAsync(cancellationToken);

            var roleRows = await _masterDb.UserRoles.AsNoTracking().ToListAsync(cancellationToken);
            var roleNames = await _masterDb.Roles.AsNoTracking()
                .ToDictionaryAsync(r => r.Id, r => r.Name ?? string.Empty, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            return users.Select(user => new MasterUserInfo
            {
                Id = user.Id,
                Username = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                IsActive = user.LockoutEnd == null || user.LockoutEnd <= now,
                IsLockedOut = user.LockoutEnd != null && user.LockoutEnd > now,
                AccessFailedCount = user.AccessFailedCount,
                Roles = roleRows
                    .Where(r => r.UserId == user.Id)
                    .Select(r => roleNames.TryGetValue(r.RoleId, out var name) ? name : string.Empty)
                    .Where(n => n.Length > 0)
                    .ToList()
            }).ToList();
        }

        public async Task<MasterUserInfo?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            var all = await GetUsersAsync(cancellationToken);
            return all.FirstOrDefault(u => u.Id == userId);
        }

        public async Task<bool> AnyUserExistsAsync(CancellationToken cancellationToken = default)
        {
            return await _masterDb.Users.AsNoTracking().AnyAsync(cancellationToken);
        }

        public async Task<(bool Success, string Error)> CreateUserAsync(
            string username, string password, string email, string role, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                return (false, "Username and password are required.");
            }

            var identityRoleName = ToIdentityRoleName(role);

            await using var db = await CloneContextAsync(cancellationToken);
            var userManager = CreateUserManager(db);
            var roleManager = CreateRoleManager(db);

            var existing = await userManager.FindByNameAsync(username.Trim());
            if (existing != null)
            {
                return (false, $"A master user named '{username.Trim()}' already exists.");
            }

            await EnsureRoleAsync(roleManager, identityRoleName);

            var user = new IdentityUser
            {
                UserName = username.Trim(),
                Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
                EmailConfirmed = !string.IsNullOrWhiteSpace(email)
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                return (false, Describe(createResult));
            }

            var roleResult = await userManager.AddToRoleAsync(user, identityRoleName);
            return roleResult.Succeeded
                ? (true, string.Empty)
                : (false, Describe(roleResult));
        }

        public async Task<(bool Success, string Error)> SetRoleAsync(string userId, string role, CancellationToken cancellationToken = default)
        {
            var identityRoleName = ToIdentityRoleName(role);

            await using var db = await CloneContextAsync(cancellationToken);
            var userManager = CreateUserManager(db);
            var roleManager = CreateRoleManager(db);

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return (false, "User not found.");
            }

            await EnsureRoleAsync(roleManager, identityRoleName);

            var currentRoles = await userManager.GetRolesAsync(user);
            if (currentRoles.Count > 0)
            {
                var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeResult.Succeeded)
                {
                    return (false, Describe(removeResult));
                }
            }

            var addResult = await userManager.AddToRoleAsync(user, identityRoleName);
            return addResult.Succeeded
                ? (true, string.Empty)
                : (false, Describe(addResult));
        }

        /// <summary>
        /// Enable/disable is implemented with Identity lockout dates: disabling sets
        /// LockoutEnd to DateTimeOffset.MaxValue, enabling clears it. No Identity schema
        /// change and no custom column is required.
        /// </summary>
        public async Task<(bool Success, string Error)> SetActiveAsync(string userId, bool active, CancellationToken cancellationToken = default)
        {
            await using var db = await CloneContextAsync(cancellationToken);
            var userManager = CreateUserManager(db);

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return (false, "User not found.");
            }

            var result = await userManager.SetLockoutEndDateAsync(user, active ? null : DateTimeOffset.MaxValue);
            if (!result.Succeeded)
            {
                return (false, Describe(result));
            }

            await userManager.ResetAccessFailedCountAsync(user);
            return (true, string.Empty);
        }

        public async Task<(bool Success, string Error)> ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(newPassword))
            {
                return (false, "A new password is required.");
            }

            await using var db = await CloneContextAsync(cancellationToken);
            var userManager = CreateUserManager(db);

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return (false, "User not found.");
            }

            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var result = await userManager.ResetPasswordAsync(user, token, newPassword);
            if (!result.Succeeded)
            {
                return (false, Describe(result));
            }

            await userManager.ResetAccessFailedCountAsync(user);
            await userManager.SetLockoutEndDateAsync(user, null);
            return (true, string.Empty);
        }

        private static string ToIdentityRoleName(string? role)
        {
            var canonical = Roles.Normalize(role);
            return canonical == Roles.MasterAdmin ? SuperAdminRoleName : canonical;
        }

        private static async Task EnsureRoleAsync(RoleManager<IdentityRole> roleManager, string roleName)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var createResult = await roleManager.CreateAsync(new IdentityRole(roleName));
                if (!createResult.Succeeded)
                {
                    throw new InvalidOperationException("Could not create the Identity role: " + Describe(createResult));
                }
            }
        }

        private static UserManager<IdentityUser> CreateUserManager(MasterAlphaEliteDbContext db)
        {
            var manager = new UserManager<IdentityUser>(
                new UserStore<IdentityUser>(db),
                new OptionsWrapper<IdentityOptions>(new IdentityOptions()),
                new PasswordHasher<IdentityUser>(),
                Enumerable.Empty<IUserValidator<IdentityUser>>(),
                Enumerable.Empty<IPasswordValidator<IdentityUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null!,
                null!);

            manager.Logger = new LoggerFactory().CreateLogger<UserManager<IdentityUser>>();
            return manager;
        }

        private static RoleManager<IdentityRole> CreateRoleManager(MasterAlphaEliteDbContext db)
        {
            var manager = new RoleManager<IdentityRole>(
                new RoleStore<IdentityRole>(db),
                Enumerable.Empty<IRoleValidator<IdentityRole>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null!);

            manager.Logger = new LoggerFactory().CreateLogger<RoleManager<IdentityRole>>();
            return manager;
        }

        private static string Describe(IdentityResult result) =>
            string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));

        /// <summary>Opens a second context on the same master connection string (see class remarks).</summary>
        private async Task<MasterAlphaEliteDbContext> CloneContextAsync(CancellationToken cancellationToken)
        {
            var connectionString = _masterDb.Database.GetConnectionString();
            // Same resilient master configuration as every other master context.
            var options = MasterDbContextOptions.Create(connectionString!);

            var db = new MasterAlphaEliteDbContext(options);
            await db.Database.CanConnectAsync(cancellationToken);
            return db;
        }
    }
}
