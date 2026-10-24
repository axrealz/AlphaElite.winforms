using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// The Alpha Elite black/gold visual language, defined ONCE.
    ///
    /// These are exactly the values the application already used (they were duplicated in
    /// <c>Form1.Designer.cs</c> and <c>MasterDashboardForm.cs</c>); both now alias these fields
    /// instead of re-declaring them, so there is still ONE palette and no second theme system.
    /// Gold stays an accent: surfaces are near-black, only headings, active states, borders and
    /// highlights use gold.
    ///
    /// <see cref="Apply"/> normalizes a control tree that was still relying on WinForms'
    /// default LIGHT colours (tab pages, text boxes, combo boxes, plain buttons, grid cells)
    /// onto those dark surfaces, and repairs unreadable text. It only ever touches colours and
    /// FlatStyle - never layout, visibility, enabled state or any behaviour.
    /// </summary>
    internal static class UiTheme
    {
        // ----- palette (identical values to the pre-existing constants) -----
        internal static readonly Color Background = Color.FromArgb(18, 18, 18);
        internal static readonly Color Sidebar = Color.FromArgb(24, 24, 24);
        internal static readonly Color Panel = Color.FromArgb(28, 28, 28);
        internal static readonly Color Field = Color.FromArgb(38, 38, 38);
        internal static readonly Color Header = Color.FromArgb(24, 24, 24);
        internal static readonly Color Gold = Color.FromArgb(212, 175, 55);
        internal static readonly Color GoldHover = Color.FromArgb(230, 195, 90);
        internal static readonly Color NavIdle = Color.FromArgb(24, 24, 24);
        internal static readonly Color NavHover = Color.FromArgb(38, 38, 38);
        internal static readonly Color Text = Color.White;
        internal static readonly Color Muted = Color.FromArgb(150, 150, 150);
        internal static readonly Color Danger = Color.FromArgb(220, 90, 90);
        internal static readonly Color GridLine = Color.FromArgb(52, 52, 52);
        internal static readonly Color Border = Color.FromArgb(60, 60, 60);
        internal static readonly Color DarkText = Color.FromArgb(20, 20, 20);

        /// <summary>Minimum text/background contrast ratio (WCAG AA for large text / UI parts).</summary>
        internal const double MinContrast = 3.0;

        private const double LightThreshold = 0.5;

        // =====================================================================================
        // Colour math (WCAG relative luminance)
        // =====================================================================================

        private static double Channel(double value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        internal static double Luminance(Color color) =>
            0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

        /// <summary>True for the light surfaces the theme must never show (white / light grey).</summary>
        internal static bool IsLight(Color color) => Luminance(color) >= LightThreshold;

        internal static double Contrast(Color first, Color second)
        {
            var a = Luminance(first);
            var b = Luminance(second);
            var high = Math.Max(a, b);
            var low = Math.Min(a, b);
            return (high + 0.05) / (low + 0.05);
        }

        internal static bool IsReadable(Color fore, Color back) => Contrast(fore, back) >= MinContrast;

        // =====================================================================================
        // Application
        // =====================================================================================

        /// <summary>Normalizes a whole control tree (the control itself first, then its children,
        /// so a themed parent is already in place when a child's contrast is resolved).</summary>
        internal static void Apply(Control root)
        {
            if (root == null)
            {
                return;
            }

            ApplyToControl(root);

            foreach (Control child in root.Controls)
            {
                Apply(child);
            }
        }

        private static void ApplyToControl(Control control)
        {
            switch (control)
            {
                case DateTimePicker picker:
                    // The FIELD is painted by the OS on every Windows theme setting: neither
                    // BackColor nor disabling the control's own OS theme changes it (pixel-verified:
                    // #FFFFFF in both cases). The one rule that matters is that its text stays
                    // readable - never light text on that light field. Only the drop-down calendar
                    // is ours to theme.
                    if (IsLight(picker.ForeColor))
                    {
                        picker.ForeColor = DarkText;
                    }

                    picker.CalendarTitleBackColor = Panel;
                    picker.CalendarTitleForeColor = Text;
                    picker.CalendarMonthBackground = Field;
                    picker.CalendarForeColor = Text;
                    return;

                case DataGridView grid:
                    ApplyGrid(grid);
                    return;

                case TabPage page:
                    if (IsLight(page.BackColor))
                    {
                        page.BackColor = Background;
                    }
                    NormalizeForeColor(page, page.BackColor, Text);
                    return;

                case TabControl tabs:
                    if (IsLight(tabs.BackColor))
                    {
                        tabs.BackColor = Panel;
                    }
                    return;

                case CheckBox check:
                    NormalizeTransparentBack(check);
                    NormalizeForeColor(check, EffectiveBackground(check), Text);
                    return;

                case RadioButton radio:
                    NormalizeTransparentBack(radio);
                    NormalizeForeColor(radio, EffectiveBackground(radio), Text);
                    return;

                case Button button:
                    ApplyButton(button);
                    return;

                case TextBoxBase text:
                    if (IsLight(text.BackColor))
                    {
                        text.BackColor = Field;
                    }
                    NormalizeForeColor(text, text.BackColor, Text);
                    return;

                case ComboBox combo:
                    if (IsLight(combo.BackColor))
                    {
                        combo.BackColor = Field;
                        // A flat combo box honours BackColor/ForeColor for its drop-down list
                        // too; the default (Standard, visual styles) always paints it white.
                        if (combo.FlatStyle == FlatStyle.Standard || combo.FlatStyle == FlatStyle.System)
                        {
                            combo.FlatStyle = FlatStyle.Flat;
                        }
                    }
                    NormalizeForeColor(combo, combo.BackColor, Text);
                    return;

                case CheckedListBox checkedList:
                    if (IsLight(checkedList.BackColor))
                    {
                        checkedList.BackColor = Field;
                    }
                    NormalizeForeColor(checkedList, checkedList.BackColor, Text);
                    return;

                case ListBox list:
                    if (IsLight(list.BackColor))
                    {
                        list.BackColor = Field;
                    }
                    NormalizeForeColor(list, list.BackColor, Text);
                    return;

                case UpDownBase numeric:
                    if (IsLight(numeric.BackColor))
                    {
                        numeric.BackColor = Field;
                    }
                    NormalizeForeColor(numeric, numeric.BackColor, Text);
                    return;

                case GroupBox group:
                    if (IsLight(group.BackColor))
                    {
                        group.BackColor = Panel;
                    }
                    NormalizeForeColor(group, group.BackColor, Text);
                    return;

                case Panel panel:
                    if (IsLight(panel.BackColor))
                    {
                        panel.BackColor = Background;
                    }
                    return;

                case Label label:
                    NormalizeTransparentBack(label);
                    NormalizeForeColor(label, EffectiveBackground(label), Text);
                    return;
            }
        }

        private static void ApplyGrid(DataGridView grid)
        {
            var themed = false;

            if (IsLight(grid.BackgroundColor))
            {
                grid.BackgroundColor = Background;
                themed = true;
            }

            // An unset cell/header style renders with the system WINDOW colour (white).
            if (grid.DefaultCellStyle.BackColor.IsEmpty || IsLight(grid.DefaultCellStyle.BackColor))
            {
                grid.DefaultCellStyle.BackColor = Header;
                themed = true;
            }

            if (grid.DefaultCellStyle.ForeColor.IsEmpty || !IsReadable(grid.DefaultCellStyle.ForeColor, grid.DefaultCellStyle.BackColor))
            {
                grid.DefaultCellStyle.ForeColor = Text;
            }

            if (grid.ColumnHeadersDefaultCellStyle.BackColor.IsEmpty || IsLight(grid.ColumnHeadersDefaultCellStyle.BackColor))
            {
                grid.ColumnHeadersDefaultCellStyle.BackColor = Header;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Gold;
                // Header colours are ignored while the OS visual-style header painting is on.
                grid.EnableHeadersVisualStyles = false;
                themed = true;
            }
            else if (grid.ColumnHeadersDefaultCellStyle.ForeColor.IsEmpty
                || !IsReadable(grid.ColumnHeadersDefaultCellStyle.ForeColor, grid.ColumnHeadersDefaultCellStyle.BackColor))
            {
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Gold;
            }

            if (themed && IsLight(grid.GridColor))
            {
                grid.GridColor = GridLine;
            }
        }

        private static void ApplyButton(Button button)
        {
            if (!IsLight(button.BackColor))
            {
                NormalizeForeColor(button, button.BackColor, Text);
                return;
            }

            button.BackColor = Panel;
            button.ForeColor = Text;

            // A Standard button ignores BackColor while visual styles are enabled.
            if (button.FlatStyle != FlatStyle.Flat)
            {
                button.FlatStyle = FlatStyle.Flat;
            }

            if (button.FlatAppearance.BorderSize == 0 || IsLight(button.FlatAppearance.BorderColor))
            {
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Border;
            }
        }

        private static void NormalizeTransparentBack(Control control)
        {
            if (control.BackColor.A == 255 && IsLight(control.BackColor))
            {
                control.BackColor = Color.Transparent;
            }
        }

        private static void NormalizeForeColor(Control control, Color background, Color replacement)
        {
            if (background.A != 255)
            {
                return; // Transparent / unknown: fall back to the ambient colour.
            }

            if (!IsReadable(control.ForeColor, background))
            {
                control.ForeColor = replacement;
            }
        }

        /// <summary>The nearest opaque background behind a control (its own BackColor when it paints one).</summary>
        private static Color EffectiveBackground(Control control)
        {
            for (Control? current = control; current != null; current = current.Parent)
            {
                if (current.BackColor.A == 255)
                {
                    return current.BackColor;
                }
            }

            return Background;
        }

        // =====================================================================================
        // Verification helpers (headless): a themed screen must expose no light surface and no
        // drop-down may ever show a CLR type name.
        // =====================================================================================

        internal static IEnumerable<Control> Descendants(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
            {
                foreach (var descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>Every surface of the tree that is still painted with a light colour.</summary>
        internal static List<string> FindLightSurfaces(Control root)
        {
            var found = new List<string>();
            foreach (var control in Descendants(root))
            {
                foreach (var (what, color) in SurfacesOf(control))
                {
                    if (color.A == 255 && IsLight(color))
                    {
                        found.Add($"{Name(control)}[{what}]={Hex(color)}");
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Every drop-down whose displayed text (the selected item or any list item) is a CLR
        /// type name instead of a human-readable value. <paramref name="excluded"/> carries the
        /// controls that are deliberately locked out of the layout (documented at the call site).
        /// </summary>
        internal static List<string> FindTypeNameDisplayValues(Control root, params string[] excluded)
        {
            var skip = new HashSet<string>(excluded, StringComparer.Ordinal);
            var found = new List<string>();
            foreach (var combo in Descendants(root).OfType<ComboBox>())
            {
                if (skip.Contains(combo.Name))
                {
                    continue;
                }

                var texts = new List<string?> { combo.GetItemText(combo.SelectedItem) };
                foreach (var item in combo.Items)
                {
                    texts.Add(combo.GetItemText(item));
                }

                foreach (var text in texts.Where(LooksLikeTypeName).Distinct())
                {
                    found.Add($"{Name(combo)}='{text}'");
                }
            }

            return found;
        }

        internal static bool LooksLikeTypeName(string? text) =>
            !string.IsNullOrEmpty(text)
            && (text.StartsWith("AlphaElite.", StringComparison.Ordinal)
                || text.Contains("AnonymousType", StringComparison.Ordinal)
                || text.Contains("+<", StringComparison.Ordinal));

        internal static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        internal static string Name(Control control) =>
            string.IsNullOrEmpty(control.Name) ? control.GetType().Name : control.Name;

        private static IEnumerable<(string What, Color Color)> SurfacesOf(Control control)
        {
            switch (control)
            {
                case DateTimePicker:
                    // Its field is painted by the OS; see ApplyToControl.
                    yield break;

                case TabControl:
                    // WinForms IGNORES TabControl.BackColor (it always reports the light system
                    // colour), so it is not a surface the theme can own. Every screen here hides
                    // the native strip (FlatButtons + ItemSize height 1) and drives the pages with
                    // app-styled buttons, exactly like the tenant window does; only the selected
                    // TabPage is painted and that page's colour IS checked below.
                    yield break;

                case DataGridView grid:
                    yield return ("background", grid.BackgroundColor);
                    yield return ("cell", grid.DefaultCellStyle.BackColor.IsEmpty ? Color.White : grid.DefaultCellStyle.BackColor);
                    yield return ("header", grid.ColumnHeadersDefaultCellStyle.BackColor.IsEmpty ? Color.White : grid.ColumnHeadersDefaultCellStyle.BackColor);
                    yield break;

                default:
                    yield return ("backcolor", control.BackColor);
                    yield break;
            }
        }
    }
}
