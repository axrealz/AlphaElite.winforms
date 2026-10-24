using AlphaElite.infrastructure.services;
using System;
using System.Linq;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 9: SOFTWARE-subscription gating for the tenant window.
    ///
    /// The subscription NEVER replaces RBAC and never grants anything: a module is usable
    /// only when BOTH the role permission AND the company subscription include it, and the
    /// existing branch/member scope still applies on top. If a role already cannot access a
    /// module, the subscription cannot grant it; if the subscription does not include a
    /// module, no role permission can bypass it.
    ///
    /// The subscription context is read once at login (master DB) and travels with the
    /// session - this window never queries the master database itself.
    /// </summary>
    public partial class Form1
    {
        // Headless-safe visibility tracking: Control.Visible is always false while the form is
        // unshown, so the verification harness reads these recorded decisions instead.
        private bool _reportsVisibleForTest = true;
        private bool _advancedBiHiddenForTest;

        /// <summary>Internal flag -> module code map for the approved 16 modules.</summary>
        private static readonly System.Collections.Generic.Dictionary<Permission, string> PermissionModuleMap =
            new()
            {
                [Permission.ViewDashboard] = SoftwareModuleCodes.Dashboard,
                [Permission.ManageMembers] = SoftwareModuleCodes.Members,
                [Permission.ManageAttendance] = SoftwareModuleCodes.Attendance,
                [Permission.ManagePayments] = SoftwareModuleCodes.Payments,
                [Permission.ManageSubscriptions] = SoftwareModuleCodes.Subscriptions,
                [Permission.ManageInquiries] = SoftwareModuleCodes.Inquiries,
                [Permission.ManageFeedback] = SoftwareModuleCodes.Feedback,
                [Permission.SubmitFeedback] = SoftwareModuleCodes.Feedback,
                [Permission.ManageComplaints] = SoftwareModuleCodes.Complaints,
                [Permission.SubmitComplaint] = SoftwareModuleCodes.Complaints,
                [Permission.ManageRetention] = SoftwareModuleCodes.Retention,
                [Permission.ManageUsers] = SoftwareModuleCodes.UserManagement,
                [Permission.ManageBranches] = SoftwareModuleCodes.BranchManagement
            };

        /// <summary>
        /// True when subscription gating is inactive (harness/legacy session without a
        /// subscription context). Used by verification to prove Phase <= 8 behaviour is intact.
        /// </summary>
        internal bool SubscriptionGatingActive => _subscription != null;

        /// <summary>The company's effective subscription status for the header/UI ("" when inert).</summary>
        internal string SubscriptionStatusText =>
            _subscription == null ? string.Empty : _subscription.EffectiveStatus;

        /// <summary>
        /// The module-code gate. Self-service permissions (Submit/Submit) map to their module;
        /// everything else requires the exact module code to be enabled. When no subscription
        /// context exists (verification harness / legacy), gating is inert => true.
        /// </summary>
        internal bool ModuleEnabled(string moduleCode)
        {
            if (_subscription == null)
            {
                return true;
            }

            return _subscription.IsModuleEnabled(moduleCode);
        }

        /// <summary>
        /// Composed check: role permission AND subscription module. Used by the UI before
        /// opening a module; the data-layer EnsurePermission stays authoritative for actions.
        /// </summary>
        internal bool HasModuleAccess(Permission permission)
        {
            if (!HasPermission(permission))
            {
                return false;
            }

            return PermissionModuleMap.TryGetValue(permission, out var module)
                ? ModuleEnabled(module)
                : true;
        }

        /// <summary>
        /// Hides every navigation button, header control and dashboard element whose module is
        /// not included in the company's subscription. Runs AFTER ApplyRoleVisibility() and
        /// ApplyPhase8Visibility() so it can only ever REMOVE items, never add or restore them
        /// (the _visibleNav diagnostics list stays owned by ApplyRoleVisibility). The Current
        /// Branch selector stays visible for branch-scoped roles: it is part of the session/
        /// branching infrastructure (Phase 8 fix), not a licensed module.
        /// </summary>
        private void ApplySubscriptionVisibility()
        {
            if (_subscription == null)
            {
                return;
            }

            bool gate(string module) => ModuleEnabled(module);

            // --- Core modules (Phase 1-4 navigation). Labels must mirror ApplyRoleVisibility's
            //     SetNav calls so removing them from the diagnostics list stays exact. The
            //     dashboard label is live text: Tenant B/C relabel it "Business Intelligence",
            //     so a hardcoded "Dashboard" would leave the module listed when gated away.
            HideNavIfGated(btnNavDashboard, gate(SoftwareModuleCodes.Dashboard), btnNavDashboard.Text);
            HideNavIfGated(btnNavMembers, gate(SoftwareModuleCodes.Members), btnNavMembers.Text);
            HideNavIfGated(btnNavAttendance, gate(SoftwareModuleCodes.Attendance), btnNavAttendance.Text);
            HideNavIfGated(btnNavPayments, gate(SoftwareModuleCodes.Payments), btnNavPayments.Text);
            HideNavIfGated(btnNavFeedback, gate(SoftwareModuleCodes.Feedback), "Feedback");
            HideNavIfGated(btnNavInquiries, gate(SoftwareModuleCodes.Inquiries), "Inquiries");
            HideNavIfGated(btnNavSubscriptions, gate(SoftwareModuleCodes.Subscriptions), "Subscriptions");
            HideNavIfGated(btnNavRetention, gate(SoftwareModuleCodes.Retention), "Retention");

            // --- Phase 4 modules ---
            HideNavIfGated(btnNavComplaints, gate(SoftwareModuleCodes.Complaints), "Complaints");
            HideNavIfGated(btnNavUserManagement, gate(SoftwareModuleCodes.UserManagement), "UserManagement");

            // --- Phase 8 module ---
            if (btnNavBranches != null)
            {
                HideNavIfGated(btnNavBranches, gate(SoftwareModuleCodes.BranchManagement), "Branches");
            }

            // --- Enterprise-only dashboard extras (Advanced BI / Branch Comparison) ---
            if (!gate(SoftwareModuleCodes.AdvancedBi))
            {
                _advancedBiHiddenForTest = true;
                if (lblBranchComparisonHeader != null) lblBranchComparisonHeader.Visible = false;
                if (dgvBranchComparison != null) dgvBranchComparison.Visible = false;
            }

            // --- Member Portal (Enterprise-only): the member self-service wording is the portal ---
            if (_role == Roles.Member && !gate(SoftwareModuleCodes.MemberPortal))
            {
                btnNavMembers.Text = "My Membership";
                btnNavAttendance.Text = "My Attendance";
                btnNavPayments.Text = "My Payments";
            }

            // --- Reports: PDF export buttons ---
            SetReportsVisible(gate(SoftwareModuleCodes.Reports));
        }

        /// <summary>
        /// Hides one nav button when its module is not licensed and removes it from the
        /// visible-nav diagnostics list (which stays owned by ApplyRoleVisibility for role
        /// visibility). Never shows or re-adds anything.
        /// </summary>
        private void HideNavIfGated(System.Windows.Forms.Control control, bool allowed, string label)
        {
            if (!allowed)
            {
                control.Visible = false;
                _visibleNav.Remove(label.Trim());
                _visibleNavButtons.Remove(control);
            }
        }

        /// <summary>Hides the Report/PDF export buttons when the Reports module is not licensed.</summary>
        private void SetReportsVisible(bool visible)
        {
            _reportsVisibleForTest = visible;

            foreach (var button in new[]
                     {
                         btnExportMembers, btnExportAttendance, btnExportPayments, btnExportFeedback,
                         btnExportInquiries, btnExportSubscriptions, btnExportRetention, btnExportComplaints
                     })
            {
                if (button != null)
                {
                    button.Visible = visible;
                }
            }
        }

        /// <summary>Headless probe: is the Reports module (PDF exports) visible for this session.</summary>
        internal bool ReportsVisibleForTest => _reportsVisibleForTest;

        /// <summary>Headless probe: is the Advanced BI / Branch Comparison panel visible.</summary>
        internal bool AdvancedBiVisibleForTest => !_advancedBiHiddenForTest;

        /// <summary>
        /// Builds the subscription banner shown under the header when the subscription is not
        /// in good standing (Expired / Suspended / none). The banner is informational: login
        /// still works, data is untouched, and MasterAdmin is never affected.
        /// </summary>
        private void ShowSubscriptionBannerIfDegraded()
        {
            if (_subscription == null)
            {
                return;
            }

            var status = _subscription.EffectiveStatus;
            if (status == SubscriptionStatuses.Active)
            {
                return;
            }

            var message = status switch
            {
                SubscriptionStatuses.Expired =>
                    $"Your company's software subscription ({_subscription.PlanName}) expired on {_subscription.EndDate:MMM d, yyyy}. " +
                    "Please contact Alpha Elite to renew. Plan-gated modules are unavailable until then; your data is safe.",
                SubscriptionStatuses.Suspended =>
                    $"Your company's software subscription ({_subscription.PlanName}) is currently SUSPENDED. " +
                    "Please contact Alpha Elite. Plan-gated modules are unavailable until reactivation; your data is safe.",
                _ =>
                    "Your company has no active software subscription. Plan-gated modules are unavailable; your data is safe."
            };

            if (!SuppressDialogs)
            {
                MessageBox.Show(message, "Subscription notice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Diagnostics: the composed module access (role + subscription) for the current session.
        /// </summary>
        internal string DescribeSubscriptionGating()
        {
            if (_subscription == null)
            {
                return "subscription=(inert)";
            }

            return "status=" + _subscription.EffectiveStatus +
                   "; plan=" + _subscription.PlanName +
                   "; modules=" + _subscription.EnabledModuleCodes.Count +
                   "; enabled=[" + string.Join(",", _subscription.EnabledModuleCodes) + "]";
        }

        /// <summary>Diagnostics: is one specific module enabled for this session's company.</summary>
        internal bool IsModuleEnabledForTest(string moduleCode) => ModuleEnabled(moduleCode);

        // =====================================================================================
        // Phase 6: enforcement composition (role permission AND subscription plan AND scope).
        // The navigation and every module load already pass through exactly this composition
        // (HasPermission(...) && ModuleEnabled(...) in Form1.cs / Form1.Phase9.cs); the members
        // below expose the same rule to the verification harness so the three layers can be
        // proven independently per module instead of being inferred from a screenshot.
        // =====================================================================================

        /// <summary>Module codes whose navigation requires a role permission (the rest are content-only).</summary>
        internal static IReadOnlyList<string> PermissionMappedModuleCodes =>
            PermissionModuleMap.Values.Distinct(StringComparer.Ordinal).ToList();

        /// <summary>True when the signed-in role holds at least one permission that maps to the module.</summary>
        internal bool RoleAllowsModuleForTest(string moduleCode)
        {
            var permissions = PermissionModuleMap
                .Where(kv => string.Equals(kv.Value, moduleCode, StringComparison.Ordinal))
                .Select(kv => kv.Key)
                .ToArray();

            // Modules without a permission mapping (content-only surfaces such as Reports or
            // Advanced BI) are not role-gated - only the subscription decides them.
            return permissions.Length == 0 || permissions.Any(HasPermission);
        }

        /// <summary>
        /// Phase 6 composed rule: a module surface is reachable only when the role permission
        /// AND the company subscription both allow it. The branch/member scope is applied on
        /// top of the data queries (ApplyScope) and is reported separately.
        /// </summary>
        internal bool IsModuleEffectivelyAvailable(string moduleCode) =>
            RoleAllowsModuleForTest(moduleCode) && ModuleEnabled(moduleCode);

        /// <summary>True when this session is restricted to a branch (so scope still applies on top).</summary>
        internal bool BranchScopeAppliesForTest => _isBranchAware;

        /// <summary>
        /// Diagnostics: one line per approved module with the three enforcement layers, i.e.
        /// "module: permission=..; subscription=..; effective=..".
        /// </summary>
        internal string DescribeModuleEnforcementForTest()
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (var module in SoftwareModuleCodes.All)
            {
                var mapped = PermissionModuleMap.Any(kv => string.Equals(kv.Value, module, StringComparison.Ordinal));
                lines.Add($"{module}: permission={(mapped ? RoleAllowsModuleForTest(module).ToString() : "n/a")}; " +
                          $"subscription={ModuleEnabled(module)}; effective={IsModuleEffectivelyAvailable(module)}");
            }

            return string.Join(" | ", lines);
        }
    }
}
