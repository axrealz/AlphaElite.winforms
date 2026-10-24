using System;
using System.Collections.Generic;
using System.Text;

namespace AlphaElite.domain.entities
{
    public class Subscription
    {
        public int SubscriptionId { get; set; }
        public int MemberId { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string BillingCycle { get; set; } = string.Empty; // "Monthly", "Quarterly", "Annual"
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; } = true;

        // Phase 8: branch scoping for subscriptions (nullable; backfilled from the
        // member's home branch during the approved migration).
        public int? BranchId { get; set; }
        public Member? Member { get; set; }
    }
}
