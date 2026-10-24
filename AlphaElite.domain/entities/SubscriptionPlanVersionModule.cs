using System;

namespace AlphaElite.domain.entities
{
    /// <summary>
    /// IMMUTABLE membership row of ONE plan version's module set (master database only).
    /// Composite key (SubscriptionPlanVersionId + ModuleId). The module CODE and NAME are
    /// snapshotted as well, so a later module rename or catalog change cannot alter what a
    /// historical version listed. Rows are only ever inserted - a new version gets its own
    /// rows and the rows of older versions are never touched or removed.
    /// </summary>
    public class SubscriptionPlanVersionModule
    {
        public int SubscriptionPlanVersionId { get; set; }

        public int ModuleId { get; set; }

        /// <summary>Stable module code at snapshot time (e.g. "dashboard").</summary>
        public string ModuleCode { get; set; } = string.Empty;

        /// <summary>Module display name at snapshot time (e.g. "Dashboard").</summary>
        public string ModuleName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public SubscriptionPlanVersion Version { get; set; } = null!;

        public SoftwareModule Module { get; set; } = null!;
    }
}
