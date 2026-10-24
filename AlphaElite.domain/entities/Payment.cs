using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.domain.entities
{
    public class Payment
    {
        public int PaymentId { get; set; }
        public int MemberId { get; set; }
        public decimal AmountPaid { get; set; }
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
        public DateTime? NextDueDate { get; set; }
        public string? Notes { get; set; }
        public Member? Member { get; set; }
        public int? BranchId { get; set; }
    }
}
