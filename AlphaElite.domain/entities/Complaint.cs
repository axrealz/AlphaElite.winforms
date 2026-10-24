using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.domain.entities
{
    public class Complaint
    {
        public int ComplaintId { get; set; }
        public int MemberId { get; set; }
        public int? BranchId { get; set; }

        public string Subject { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        // Pending -> Under Review -> Resolved / Closed
        public string Status { get; set; } = "Pending";

        // CreatedAt doubles as "Date Submitted" (the complaint's submission timestamp); it
        // is user-editable on the complaint form and is never overwritten by an edit unless
        // the user changes it. ResolvedAt = "Date Resolved" (nullable: an open complaint has
        // no resolution date) and ResolutionNotes = "Resolution / Response".
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
        public string? ResolutionNotes { get; set; }

        // Complaint-handling fields (Tenant A Complaints CRUD phase). All nullable so rows
        // that predate the columns stay valid. Tenant C's context ignores them - its table
        // keeps the original schema (no Tenant C schema change).
        public string? Category { get; set; }
        public string? Priority { get; set; }
        public string? StaffAssigned { get; set; }

        public Member? Member { get; set; }
        public Branch? Branch { get; set; }
    }
}
