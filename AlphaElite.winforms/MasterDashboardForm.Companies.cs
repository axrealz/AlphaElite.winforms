using AlphaElite.infrastructure.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>Company/tenant management: view, activate/deactivate, open a tenant window. No deletes.</summary>
    internal partial class MasterDashboardForm
    {
        private Panel BuildCompaniesPanel()
        {
            var panel = NewContentPanel();

            AddSectionTitle(panel, "Companies / Tenants", 24, 20);
            AddNote(panel, "Deactivating a company blocks its users at login (enforced by the tenant authentication service). Companies are NEVER deleted.", 24, 52, 920, 20);

            var dgv = NewGrid();
            dgv.Location = new Point(24, 84);
            dgv.Size = new Size(980, 420);

            var btnToggle = NewPrimaryButton("Activate / Deactivate selected", 24, 520, 240);
            // Serialized against every other master-database action (shared context).
            btnToggle.Click += async (s, e) => await RunMasterOperationAsync(() => ToggleCompanyAsync(dgv));

            var btnOpenTenant = NewSecondaryButton("Open selected tenant", 280, 520, 200);
            btnOpenTenant.Click += async (s, e) => await OpenTenantAsync(dgv);

            panel.Controls.Add(dgv);
            panel.Controls.Add(btnToggle);
            panel.Controls.Add(btnOpenTenant);

            panel.Tag = (Func<Task>)(async () =>
            {
                var overviews = await _runtime.MasterTenantAdminService.GetCompanyOverviewsAsync();
                dgv.DataSource = ToCompanyRows(overviews).ToList();
            });

            return panel;
        }

        private static IEnumerable<object> ToCompanyRows(IReadOnlyList<CompanyOverview> overviews) =>
            overviews.Select(o => new
            {
                o.CompanyId,
                o.CompanyCode,
                o.CompanyName,
                Status = o.IsActive ? "Active" : "Inactive",
                Database = o.Database == null ? "(none)" : o.Database.DatabaseName,
                Server = o.Database == null ? "(none)" : o.Database.ServerName,
                CredentialKey = o.Database == null ? "(none)" : o.Database.CredentialKey,
                DBActive = o.Database == null ? "(n/a)" : (o.Database.IsActive ? "Active" : "Inactive"),
                Created = o.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd")
            });

        private async Task ToggleCompanyAsync(DataGridView dgv)
        {
            if (dgv.CurrentRow?.DataBoundItem == null)
            {
                MessageBox.Show("Select a company row first.", "Companies",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var row = dgv.CurrentRow.DataBoundItem;
            var companyId = (int)(row.GetType().GetProperty("CompanyId")?.GetValue(row) ?? 0);
            var status = row.GetType().GetProperty("Status")?.GetValue(row) as string ?? "Active";

            var confirm = MessageBox.Show(
                $"Set company {companyId} to {(status == "Active" ? "INACTIVE" : "ACTIVE")}?" +
                (status == "Active"
                    ? "\n\nIts users will no longer be able to log in."
                    : "\n\nIts users will be able to log in again."),
                "Confirm company activation change",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            var result = await _runtime.MasterTenantAdminService.SetCompanyActiveAsync(companyId, status != "Active");
            if (!result.Success)
            {
                MessageBox.Show("Failed: " + result.Error, "Companies",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var overviews = await _runtime.MasterTenantAdminService.GetCompanyOverviewsAsync();
            dgv.DataSource = ToCompanyRows(overviews).ToList();
        }

        /// <summary>
        /// Master-admin tenant switching (Step 7): resolves the selected company through the
        /// master DB + CompanyDatabase via the EXISTING resolver/factory, then opens a tenant
        /// window with tenant authorization rules intact. No CompanyId is hardcoded.
        /// </summary>
        private async Task OpenTenantAsync(DataGridView dgv)
        {
            if (dgv.CurrentRow?.DataBoundItem == null)
            {
                MessageBox.Show("Select a company row first.", "Companies",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var row = dgv.CurrentRow.DataBoundItem;
            var companyId = (int)(row.GetType().GetProperty("CompanyId")?.GetValue(row) ?? 0);

            try
            {
                var tenant = _tenants.FirstOrDefault(t => t.CompanyId == companyId);
                if (tenant == null)
                {
                    MessageBox.Show(
                        "This tenant is not currently available for switching (no active database configuration).",
                        "Companies", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var confirmed = MessageBox.Show(
                    $"Open tenant '{tenant.DisplayName}' in a separate window?\n\n" +
                    "Tenant authorization rules for the selected tenant remain fully enforced.",
                    "Open tenant", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (confirmed != DialogResult.Yes)
                {
                    return;
                }

                // Verify the context can actually be created through the existing factory
                // (this also surfaces inactive/missing database configuration cleanly).
                await using var db = await _runtime.DataContextFactory.CreateAsync(tenant.CompanyId, tenant.IsBranchAware);

                var session = new UserSession
                {
                    CompanyId = tenant.CompanyId,
                    CompanyCode = tenant.CompanyCode,
                    CompanyName = tenant.CompanyName,
                    IsBranchAware = tenant.IsBranchAware,

                    // A master admin opens the tenant WITHOUT a tenant identity: no BranchId and
                    // no MemberId. Tenant-wide role rules therefore apply inside the tenant window.
                    BranchId = null,
                    MemberId = null,
                    Username = _session.Username,
                    FullName = _session.Username,
                    Role = Roles.Admin
                };

                var window = new Form1(session, _tenants, _runtime.DataContextFactory, _runtime.AuthorizationService);

                // The master window stays open behind the tenant window.
                window.Show(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open the tenant: " + ex.Message, "Open tenant",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
