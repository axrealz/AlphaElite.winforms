using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.domain.entities
{
    public class UserAccount
    {
        public int UserAccountId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;

        // "Member", "GymStaff", "Manager", "Admin", "SuperAdmin"
        public string Role { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Tenant C additions:
        // - MemberId: set only when Role == "Member", links this login to their own Member record.
        // - BranchId: set for staff/managers who belong to one branch; null for Admin/SuperAdmin (all branches).
        public int? MemberId { get; set; }
        public int? BranchId { get; set; }
        public Member? Member { get; set; }
    }
}
