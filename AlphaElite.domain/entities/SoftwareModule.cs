using System;

namespace AlphaElite.domain.entities
{
    /// <summary>
    /// Phase 9: one Alpha Elite SOFTWARE module (Dashboard, Members, Attendance, ...).
    /// Lives only in the MASTER database. The stable <see cref="Code"/> is what the
    /// application gates navigation with; the display Name is cosmetic.
    /// </summary>
    public class SoftwareModule
    {
        public int ModuleId { get; set; }

        /// <summary>Stable unique code, e.g. "dashboard". Seeding and gating always go through this.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Display name, e.g. "Dashboard".</summary>
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
