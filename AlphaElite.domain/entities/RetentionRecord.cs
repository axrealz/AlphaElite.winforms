using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.domain.entities
{
    public class RetentionRecord
    {
        public int RetentionRecordId { get; set; }
        public int MemberId { get; set; }

        public string Reason { get; set; } = string.Empty;
        public string RetentionAction { get; set; } = string.Empty;
        public string? Promotion { get; set; }
        public string? PromotionDetails { get; set; }
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = string.Empty;

        public string Status { get; set; } = "Pending";

        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? ApprovalReason { get; set; }
        public string? RejectionReason { get; set; }

        public DateTime? CompletedAt { get; set; }
        public string? CompletionNotes { get; set; }

        public Member? Member { get; set; }

        // Tenant C additions: which staff member is handling this, and which branch it belongs to.
        public string? AssignedStaff { get; set; }
        public int? BranchId { get; set; }
    }
}