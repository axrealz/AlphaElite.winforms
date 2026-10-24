using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.domain.entities
{
    public class Feedback
    {
        public int FeedbackId { get; set; }
        public int MemberId { get; set; }
        public int Rating { get; set; }
        public string? Comments { get; set; }
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public Member? Member { get; set; }
        public int? BranchId { get; set; }

    }
}
