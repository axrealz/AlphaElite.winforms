namespace AlphaElite.winforms
{
    /// <summary>
    /// Session for a MASTER (platform) administrator. A distinct type from <see cref="UserSession"/>
    /// so the compiler itself prevents a master login from flowing into the tenant window
    /// (<c>Form1</c>) or a tenant login into the master window (<c>MasterDashboardForm</c>).
    /// Contains no credential material.
    /// </summary>
    public sealed class MasterUserSession
    {
        /// <summary>Always true for this session type; guards against accidental misuse.</summary>
        public bool IsMasterAdmin => true;

        /// <summary>AspNetUsers.Id of the signed-in master user.</summary>
        public string MasterUserId { get; init; } = string.Empty;

        public string Username { get; init; } = string.Empty;

        /// <summary>Canonical application role (MasterAdmin after normalization).</summary>
        public string Role { get; init; } = string.Empty;

        /// <summary>Identity-side role name (e.g. SuperAdmin) for display only.</summary>
        public string IdentityRoleName { get; init; } = string.Empty;
    }
}
