using System;
using System.Collections.Generic;

namespace AlphaElite.domain.entities
{
    /// <summary>
    /// Phase 9: a COMPANY's Alpha Elite SOFTWARE subscription (which plan the company has
    /// bought, for how long, and whether it is active). Lives only in the MASTER database.
    /// It is NOT a gym member's membership subscription (tenant-side <see cref="Subscription"/>),
    /// which remains untouched in the tenant databases.
    /// </summary>
    public class CompanySubscription
    {
        public int CompanySubscriptionId { get; set; }

        public int CompanyId { get; set; }

        public int SubscriptionPlanId { get; set; }

        public DateTime StartDate { get; set; }

        public DateTime EndDate { get; set; }

        /// <summary>Next renewal date (null when not yet scheduled).</summary>
        public DateTime? RenewalDate { get; set; }

        /// <summary>Active / Pending / Expired / Suspended / Cancelled. Suspended is an explicit administrative state.</summary>
        public string Status { get; set; } = "Active";

        /// <summary>
        /// SNAPSHOT of the plan price at the time this subscription became active.
        /// Later plan price edits must NOT change this value.
        /// </summary>
        public decimal Price { get; set; }

        /// <summary>Paid / Pending / Failed / NotApplicable (vocabulary in infrastructure PaymentStatuses; software state only - no gateway).</summary>
        public string PaymentStatus { get; set; } = "NotApplicable";

        /// <summary>When the company subscribed (preserved independently of StartDate edits).</summary>
        public DateTime DateSubscribed { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The PLAN VERSION (Terms &amp; Agreement + module set snapshot) the company received.
        /// Purely additive association: it records WHICH configuration was approved while the
        /// existing Price / TermsAndAgreement snapshots keep holding the agreed values.
        /// Nullable so pre-existing subscriptions keep working; the idempotent version
        /// initializer links them to their plan's current version.
        /// </summary>
        public int? SubscriptionPlanVersionId { get; set; }

        /// <summary>
        /// SNAPSHOT of the plan TermsAndAgreement accepted when this subscription became
        /// active. Later plan terms edits must NOT rewrite this value.
        /// </summary>
        public string TermsAndAgreement { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Company Company { get; set; } = null!;

        public SubscriptionPlan SubscriptionPlan { get; set; } = null!;

        public SubscriptionPlanVersion? SubscriptionPlanVersion { get; set; }
    }
}
