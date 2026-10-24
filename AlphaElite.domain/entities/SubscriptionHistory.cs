using System;

namespace AlphaElite.domain.entities
{
    /// <summary>
    /// Company software subscription audit trail (master DB only). One row is written for
    /// every APPROVED subscription plan change (and for administrative states like
    /// suspension/reactivation). Rejected subscription change requests are audited through
    /// the SubscriptionChangeRequest row itself - they never create a plan-change history
    /// entry. Nothing here touches the tenant-side gym member Subscription.
    /// </summary>
    public class SubscriptionHistory
    {
        public int HistoryId { get; set; }

        public int CompanyId { get; set; }

        /// <summary>Plan before the change (null for InitialSubscription).</summary>
        public int? PreviousPlanId { get; set; }

        /// <summary>Plan after the change (null for Cancellation).</summary>
        public int? NewPlanId { get; set; }

        /// <summary>InitialSubscription / Upgrade / Downgrade / Renewal / Revert / Suspension / Reactivation / Cancellation.</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>Snapshot of the previous subscription price (null when there was none).</summary>
        public decimal? PreviousPrice { get; set; }

        /// <summary>Snapshot of the new subscription price at the time of the change.</summary>
        public decimal? NewPrice { get; set; }

        /// <summary>Authenticated identity that performed the change (e.g. the Super Admin username).</summary>
        public string ChangedBy { get; set; } = string.Empty;

        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        public string Reason { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;

        public Company? Company { get; set; }
    }
}
