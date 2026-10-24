using System;
using System.Collections.Generic;

namespace AlphaElite.domain.entities
{
    /// <summary>
    /// IMMUTABLE snapshot of ONE Alpha Elite SOFTWARE plan configuration at a point in time
    /// (master database only). A version is never rewritten: when a Super Admin makes a
    /// material subscription-configuration change (module added/removed, price, billing cycle
    /// or Terms &amp; Agreement text) a NEW version row is created and the previous version keeps
    /// its own values.
    ///
    /// Version numbers are sequential PER PLAN (SubscriptionPlanId + VersionNumber is unique)
    /// and exactly one version per plan carries <see cref="IsCurrent"/> - that is the version
    /// the subscription workflow uses for new approvals.
    ///
    /// This entity is unrelated to a gym member's membership subscription (tenant-side
    /// <see cref="Subscription"/>), which stays completely untouched.
    /// </summary>
    public class SubscriptionPlanVersion
    {
        public int SubscriptionPlanVersionId { get; set; }

        public int SubscriptionPlanId { get; set; }

        /// <summary>Sequential per plan: 1, 2, 3 ... (never a global sequence across plans).</summary>
        public int VersionNumber { get; set; }

        // -------------------------------------------------------------------------------------
        // SNAPSHOT values (copied from the plan when the version was created - never re-read
        // from the live plan afterwards, so later edits cannot rewrite history).
        // -------------------------------------------------------------------------------------

        /// <summary>Stable plan code at the time of the snapshot (e.g. "BASIC").</summary>
        public string PlanCode { get; set; } = string.Empty;

        /// <summary>Plan display name at the time of the snapshot.</summary>
        public string PlanName { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public decimal Price { get; set; }

        /// <summary>"Monthly" / "Yearly" (vocabulary in infrastructure BillingCycles).</summary>
        public string BillingCycle { get; set; } = string.Empty;

        /// <summary>The exact Terms &amp; Agreement text of this version.</summary>
        public string TermsAndAgreement { get; set; } = string.Empty;

        /// <summary>Snapshot of the plan's activation flag (an inactive plan cannot be newly selected).</summary>
        public bool PlanIsActive { get; set; } = true;

        /// <summary>Date this configuration became effective (the day the change was made).</summary>
        public DateTime EffectiveDate { get; set; }

        /// <summary>True for the plan's current/latest version. Exactly one per plan.</summary>
        public bool IsCurrent { get; set; }

        /// <summary>InitialVersion / PlanCreated / ModuleAdded / ModuleRemoved / TermsChanged / PriceChanged / BillingCycleChanged / ConfigurationChanged.</summary>
        public string ChangeType { get; set; } = string.Empty;

        /// <summary>Human-readable description of what changed (e.g. "Added module: Feedback").</summary>
        public string ChangeSummary { get; set; } = string.Empty;

        /// <summary>Authenticated Super Admin identity that made the change (or the initializer identity).</summary>
        public string ChangedBy { get; set; } = string.Empty;

        public DateTime ChangedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public SubscriptionPlan SubscriptionPlan { get; set; } = null!;

        /// <summary>
        /// The IMMUTABLE module set of this version. Historical module membership is stored here
        /// on purpose: the live PlanModules mapping is never used to reconstruct an old version.
        /// </summary>
        public ICollection<SubscriptionPlanVersionModule> Modules { get; set; } = new List<SubscriptionPlanVersionModule>();
    }
}
