using System;
using System.Collections.Generic;

namespace AlphaElite.domain.entities
{
    /// <summary>
    /// Phase 9: an Alpha Elite SOFTWARE plan (Basic / Professional / Enterprise) that a
    /// company subscribes to. This is the SaaS offering sold by Alpha Elite itself and
    /// lives only in the MASTER database. It has nothing to do with a gym member's
    /// membership subscription (the tenant-side <see cref="Subscription"/> entity),
    /// which stays completely separate.
    ///
    /// Subscription-request phase: plans additionally carry a Price, a BillingCycle and
    /// the TermsAndAgreement text a company accepts when it subscribes. The plan price is
    /// SNAPSHOT-COPIED into CompanySubscription.Price when a subscription becomes active,
    /// so later plan price edits never rewrite historical subscriptions.
    /// </summary>
    public class SubscriptionPlan
    {
        public int SubscriptionPlanId { get; set; }

        /// <summary>Display name, e.g. "Basic".</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Stable unique code, e.g. "BASIC". Seeding and lookups always go through this.</summary>
        public string Code { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        /// <summary>Current selling price of the plan (snapshot-copied into CompanySubscription on activation).</summary>
        public decimal Price { get; set; }

        /// <summary>"Monthly" or "Yearly" (vocabulary in infrastructure BillingCycles). Never hardcoded in the UI.</summary>
        public string BillingCycle { get; set; } = "Monthly";

        /// <summary>Terms &amp; Agreement text accepted at subscription time; copied into CompanySubscription.</summary>
        public string TermsAndAgreement { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<PlanModule> PlanModules { get; set; } = new List<PlanModule>();
    }
}
