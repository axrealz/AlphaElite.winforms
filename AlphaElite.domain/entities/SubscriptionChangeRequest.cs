using System;

namespace AlphaElite.domain.entities
{
    /// <summary>
    /// OPTION 2 request/approval record (master DB only): a tenant asks for a plan
    /// upgrade/downgrade/plan change, the request starts Pending, and ONLY a Super Admin
    /// approval changes the company's active CompanySubscription (which also snapshots the
    /// plan price and terms and writes a SubscriptionHistory row). A rejection never
    /// touches the active subscription. Tenants can neither approve their own request nor
    /// modify the subscription directly.
    /// </summary>
    public class SubscriptionChangeRequest
    {
        public int SubscriptionChangeRequestId { get; set; }

        public int CompanyId { get; set; }

        /// <summary>The plan the company was on when the request was submitted (null when it had no subscription).</summary>
        public int? CurrentPlanId { get; set; }

        /// <summary>The plan the company asked for (null when the request is a cancellation-type request).</summary>
        public int? RequestedPlanId { get; set; }

        /// <summary>Upgrade / Downgrade / ChangePlan.</summary>
        public string RequestType { get; set; } = string.Empty;

        /// <summary>Pending / Approved / Rejected / Cancelled (vocabulary in infrastructure SubscriptionChangeRequestStatuses).</summary>
        public string Status { get; set; } = "Pending";

        /// <summary>Authenticated tenant identity that submitted the request.</summary>
        public string RequestedBy { get; set; } = string.Empty;

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Authenticated Super Admin identity that reviewed the request (null while Pending).</summary>
        public string? ReviewedBy { get; set; }

        public DateTime? ReviewedAt { get; set; }

        /// <summary>Reviewer notes; required content for a rejection reason.</summary>
        public string ReviewNotes { get; set; } = string.Empty;

        /// <summary>Snapshot of the requested plan's price when the request was submitted.</summary>
        public decimal? RequestedPrice { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Company? Company { get; set; }
    }
}
