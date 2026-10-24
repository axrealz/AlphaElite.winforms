using System;
using System.Collections.Generic;
using System.Linq;

namespace AlphaElite.infrastructure.services
{
    /// <summary>Application roles. These are the values stored in UserAccounts.Role.</summary>
    public static class Roles
    {
        public const string MasterAdmin = "MasterAdmin";
        public const string Admin = "Admin";
        public const string Manager = "Manager";
        public const string GymStaff = "GymStaff";
        public const string Member = "Member";

        public static readonly string[] All = { MasterAdmin, Admin, Manager, GymStaff, Member };

        /// <summary>
        /// Maps a stored role string to a canonical role. Unknown values fall back to the
        /// most restrictive role (<see cref="Member"/>) so a bad value can never grant access.
        /// </summary>
        public static string Normalize(string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return Member;
            }

            var value = role.Trim();

            if (value.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Super Admin", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Master Admin", StringComparison.OrdinalIgnoreCase)
                || value.Equals("MasterAdmin", StringComparison.OrdinalIgnoreCase))
            {
                return MasterAdmin;
            }

            foreach (var known in All)
            {
                if (known.Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }

            // "Gym Staff" / "gymstaff" style inputs.
            var compact = value.Replace(" ", string.Empty);
            foreach (var known in All)
            {
                if (known.Equals(compact, StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }

            return Member;
        }

        /// <summary>True for the tenant-side roles that manage other users' data.</summary>
        public static bool IsStaffOrAbove(string? role)
        {
            var normalized = Normalize(role);
            return normalized is MasterAdmin or Admin or Manager or GymStaff;
        }

        /// <summary>True for roles that are restricted to a single branch when they carry a BranchId.</summary>
        public static bool IsBranchScopedByDefault(string? role)
        {
            var normalized = Normalize(role);
            return normalized is Manager or GymStaff;
        }
    }

    /// <summary>Functional permissions. Nothing in the UI or data layer hardcodes a role string.</summary>
    [Flags]
    public enum Permission : long
    {
        None = 0,

        // Staff / management capabilities
        ViewDashboard = 1L << 0,
        ManageMembers = 1L << 1,
        ManageAttendance = 1L << 2,
        ManagePayments = 1L << 3,
        ManageFeedback = 1L << 4,
        ManageInquiries = 1L << 5,
        ManageSubscriptions = 1L << 6,
        ManageRetention = 1L << 7,
        ManageComplaints = 1L << 8,
        ManageUsers = 1L << 9,
        ManageBranches = 1L << 16,
        AdminPanel = 1L << 10,
        BusinessIntelligence = 1L << 11,
        TenantManagement = 1L << 12,

        // Member self-service capabilities
        SelfService = 1L << 13,
        SubmitFeedback = 1L << 14,
        SubmitComplaint = 1L << 15
    }

    public interface IAuthorizationService
    {
        string NormalizeRole(string? role);
        IReadOnlyCollection<Permission> GetPermissions(string? role);
        bool IsAuthorized(string? role, Permission permission);

        /// <summary>
        /// Throws <see cref="UnauthorizedAccessException"/> unless the role holds
        /// <paramref name="permission"/>. Used by the data layer so that hiding a control
        /// is never the only protection.
        /// </summary>
        void EnsureAuthorized(string? role, Permission permission);
    }

    /// <summary>
    /// Single source of truth for the role -> permission matrix. The UI (which controls are
    /// visible) and the data layer (which operations are allowed) both consult this service.
    /// </summary>
    public sealed class RoleAuthorizationService : IAuthorizationService
    {
        private static readonly IReadOnlyDictionary<string, Permission> Matrix =
            new Dictionary<string, Permission>(StringComparer.Ordinal)
            {
                [Roles.Member] = Permission.SelfService | Permission.SubmitFeedback | Permission.SubmitComplaint,

                // Phase 2: Gym Staff additionally handles member feedback and complaints
                // (user-requested: "Feedback & Complaints" appears in the Gym Staff nav).
                [Roles.GymStaff] = Permission.ManageInquiries | Permission.ManageMembers
                                   | Permission.ManageAttendance | Permission.ManagePayments
                                   | Permission.ManageFeedback | Permission.ManageComplaints
                                   | Permission.ManageRetention,

                [Roles.Manager] = Permission.ViewDashboard | Permission.ManageInquiries
                                  | Permission.ManageMembers | Permission.ManageAttendance
                                  | Permission.ManageFeedback | Permission.ManageComplaints
                                  | Permission.ManageRetention | Permission.ManageUsers
                                  | Permission.ManageBranches,

                // Phase 2: the Subscription module is MASTER-ADMIN ONLY (user requirement:
                // "Only Super Admin/Master Admin should have access to the Subscription module").
                // Tenant Admin therefore no longer holds ManageSubscriptions.
                [Roles.Admin] = Permission.ViewDashboard | Permission.ManageMembers
                                | Permission.ManageAttendance | Permission.ManagePayments
                                | Permission.ManageFeedback | Permission.ManageInquiries
                                | Permission.ManageRetention
                                | Permission.ManageComplaints | Permission.ManageUsers
                                | Permission.ManageBranches,

                // Informational for this phase: the Master DB administration itself is out of scope.
                [Roles.MasterAdmin] = Permission.ViewDashboard | Permission.ManageSubscriptions
                                      | Permission.ManageRetention | Permission.ManageUsers
                                      | Permission.AdminPanel | Permission.BusinessIntelligence
                                      | Permission.TenantManagement
            };

        public string NormalizeRole(string? role) => Roles.Normalize(role);

        public IReadOnlyCollection<Permission> GetPermissions(string? role)
        {
            var normalized = Roles.Normalize(role);

            if (!Matrix.TryGetValue(normalized, out var permissions))
            {
                return new[] { Permission.None };
            }

            return Expand(permissions).ToArray();
        }

        public bool IsAuthorized(string? role, Permission permission)
        {
            if (permission == Permission.None)
            {
                return true;
            }

            return GetPermissions(role).Contains(permission);
        }

        public void EnsureAuthorized(string? role, Permission permission)
        {
            if (!IsAuthorized(role, permission))
            {
                throw new UnauthorizedAccessException(
                    $"Role '{NormalizeRole(role)}' is not permitted to perform '{permission}'.");
            }
        }

        /// <summary>Enumerates the individual flags held for a role (for diagnostics/UI).</summary>
        private static IEnumerable<Permission> Expand(Permission permissions)
        {
            foreach (Permission candidate in Enum.GetValues(typeof(Permission)))
            {
                if (candidate != Permission.None && permissions.HasFlag(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }
}
