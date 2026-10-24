using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.domain.entities
{
    public class Inquiry
    {
        public int InquiryId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? ContactNumber { get; set; }
        public string? EmailAddress { get; set; }
        public string? InterestedPlan { get; set; }
        public string? Message { get; set; }
        public bool IsFollowedUp { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int? BranchId { get; set; }
    }
}
