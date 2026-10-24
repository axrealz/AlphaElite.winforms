using System.Linq;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Visual-only partial of the tenant window: it carries no behaviour, only the headless
    /// hook that proves the Alpha Elite dark/gold theme covers every tenant surface.
    ///
    /// The theme pass itself is <see cref="UiTheme.Apply"/>, called once at the end of the
    /// Form1 constructor. Nothing here (and nothing in that pass) changes data, permissions,
    /// navigation, branch behaviour or any other tenant functionality.
    /// </summary>
    public partial class Form1
    {
        /// <summary>
        /// Headless: reports every surface of the tenant window that is still light and every
        /// drop-down whose display text is a CLR type name.
        ///
        /// <c>cboTenantSwitcher</c> is excluded from the type-name probe for a documented reason:
        /// it is permanently locked (Enabled=false) and removed from the layout in every tenant
        /// session (Tenant A/B/C), so its text is never visible - the phase 2 / Tenant B suites
        /// assert that removal. Its object list is untouched, so nothing about tenant resolution
        /// changes.
        /// </summary>
        internal string DescribeThemeForTest()
        {
            var light = UiTheme.FindLightSurfaces(this);
            var typeNames = UiTheme.FindTypeNameDisplayValues(this, "cboTenantSwitcher");

            return $"light={light.Count}" +
                (light.Count == 0 ? string.Empty : " [" + string.Join("; ", light) + "]") +
                $" typename={typeNames.Count}" +
                (typeNames.Count == 0 ? string.Empty : " [" + string.Join("; ", typeNames) + "]");
        }

        /// <summary>Headless: the tenant's tab pages (must all be on the dark surface).</summary>
        internal string DescribeTabSurfaceForTest() => string.Join(", ", tabControlMain.TabPages
            .Cast<System.Windows.Forms.TabPage>()
            .Select(p => $"{p.Text}={UiTheme.Hex(p.BackColor)}"));
    }
}
