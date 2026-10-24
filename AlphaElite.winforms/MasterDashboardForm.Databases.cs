using AlphaElite.infrastructure.services;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>Tenant database administration: configuration view (no secrets) + read-only health check.</summary>
    internal partial class MasterDashboardForm
    {
        private Panel BuildDatabasesPanel()
        {
            var panel = NewContentPanel();

            AddSectionTitle(panel, "Tenant Databases", 24, 20);
            AddNote(panel, "Credential keys are lookup references into server configuration - no secret is ever displayed. The health check is read-only (SELECT 1).", 24, 52, 920, 20);

            var dgv = NewGrid();
            dgv.Location = new Point(24, 84);
            dgv.Size = new Size(980, 420);

            var btnHealth = NewPrimaryButton("Run connection health check", 24, 520, 240);
            // Serialized against every other master-database action (shared context).
            btnHealth.Click += async (s, e) => await RunMasterOperationAsync(RunHealthCheckAsync);

            panel.Controls.Add(dgv);
            panel.Controls.Add(btnHealth);

            panel.Tag = (Func<Task>)(async () =>
            {
                var overviews = await _runtime.MasterTenantAdminService.GetCompanyOverviewsAsync();
                dgv.DataSource = overviews
                    .Where(o => o.Database != null)
                    .Select(o => new
                    {
                        o.CompanyId,
                        Company = o.CompanyName,
                        Server = o.Database!.ServerName,
                        Database = o.Database.DatabaseName,
                        CredentialKey = o.Database.CredentialKey,
                        Status = o.Database.IsActive ? "Active" : "Inactive"
                    })
                    .ToList();
            });

            return panel;
        }

        /// <summary>Read-only health probe through the existing resolver + factory. Never writes.</summary>
        private async Task RunHealthCheckAsync()
        {
            var overviews = await _runtime.MasterTenantAdminService.GetCompanyOverviewsAsync();
            var lines = new List<string>();

            foreach (var overview in overviews.Where(o => o.Database != null && o.Database.IsActive))
            {
                try
                {
                    var info = await _runtime.Resolver.GetDatabaseInfoAsync(overview.CompanyId);
                    var connectionString = _runtime.ConnectionFactory.CreateConnectionString(info);

                    await using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();
                    await using var command = new SqlCommand("SELECT 1", connection);
                    await command.ExecuteScalarAsync();

                    lines.Add($"CompanyId {overview.CompanyId} ({overview.Database!.DatabaseName}): OK - SELECT 1 succeeded");
                }
                catch (Exception ex)
                {
                    lines.Add($"CompanyId {overview.CompanyId} ({overview.Database!.DatabaseName}): FAIL - {TrimError(ex.Message)}");
                }
            }

            MessageBox.Show(
                lines.Count == 0 ? "No active tenant databases configured." : string.Join(Environment.NewLine, lines),
                "Connection health (read-only)", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
