using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.domain.entities
{
    public class Branch
    {
        public int BranchId { get; set; }
        public string BranchCode { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? ContactNumber { get; set; }

        // Phase 8: branch management additions (additive, nullable).
        public string? City { get; set; }

        // Manager of this branch: an EXISTING active UserAccount with Role = Manager.
        // May be null ("Unassigned"). One manager account may manage several branches.
        public int? ManagerUserAccountId { get; set; }
        public UserAccount? Manager { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
