using System;
using System.Collections.Generic;

namespace AlphaElite.domain.entities
{
    /// <summary>
    /// Phase 9: which SOFTWARE modules are included in a software plan.
    /// Lives only in the MASTER database. Composite key (SubscriptionPlanId + ModuleId).
    /// </summary>
    public class PlanModule
    {
        public int SubscriptionPlanId { get; set; }

        public int ModuleId { get; set; }

        /// <summary>Allows a module to be disabled within a plan without deleting the mapping row.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public SubscriptionPlan SubscriptionPlan { get; set; } = null!;

        public SoftwareModule Module { get; set; } = null!;
    }
}
