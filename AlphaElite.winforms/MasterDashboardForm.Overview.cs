using AlphaElite.infrastructure.services;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>Tenant Overview, Reports/BI and System/Admin Panel, plus a small password prompt.</summary>
    internal partial class MasterDashboardForm
    {
        private Panel BuildOverviewPanel()
        {
            var panel = NewContentPanel();

            AddSectionTitle(panel, "Tenant Overview", 24, 20);
            AddNote(panel, "Aggregates are read live from each tenant database through the existing resolver/factory. Nothing is copied into the master DB.", 24, 52, 920, 20);

            var dgv = NewGrid();
            dgv.Location = new Point(24, 84);
            dgv.Size = new Size(980, 470);

            panel.Controls.Add(dgv);

            panel.Tag = (Func<Task>)(async () =>
            {
                var overviews = await _runtime.MasterTenantAdminService.GetCompanyOverviewsAsync();
                var aggregates = await _runtime.MasterTenantAdminService.GetTenantAggregatesAsync(overviews);

                dgv.DataSource = aggregates.Select(a =>
                {
                    var company = overviews.First(o => o.CompanyId == a.CompanyId);
                    return new
                    {
                        a.CompanyId,
                        Company = company.CompanyName,
                        Result = a.Success ? "OK" : "UNAVAILABLE",
                        a.Members,
                        a.ActiveMembers,
                        a.Attendance,
                        a.Payments,
                        PaymentTotal = a.PaymentTotal,
                        a.Feedback,
                        a.Inquiries,
                        a.RetentionRecords,
                        a.Complaints,
                        a.Branches,
                        a.UserAccounts,
                        a.Subscriptions,
                        Note = a.Error
                    };
                }).ToList();
            });

            return panel;
        }

        private Panel BuildReportsPanel()
        {
            var panel = NewContentPanel();

            AddSectionTitle(panel, "Reports / BI (platform aggregates)", 24, 20);
            AddNote(panel, "Derived ratios per tenant, computed in-memory from the same read-only aggregates.", 24, 52, 920, 20);

            var dgv = NewGrid();
            dgv.Location = new Point(24, 84);
            dgv.Size = new Size(980, 470);

            panel.Controls.Add(dgv);

            panel.Tag = (Func<Task>)(async () =>
            {
                var overviews = await _runtime.MasterTenantAdminService.GetCompanyOverviewsAsync();
                var aggregates = await _runtime.MasterTenantAdminService.GetTenantAggregatesAsync(overviews);

                dgv.DataSource = aggregates.Where(a => a.Success).Select(a =>
                {
                    var company = overviews.First(o => o.CompanyId == a.CompanyId);
                    var members = Math.Max(a.Members, 1);
                    return new
                    {
                        Company = company.CompanyName,
                        PaymentPerMember = a.PaymentTotal / members,
                        AttendancePerMember = (decimal)a.Attendance / members,
                        FeedbackRate = a.Members == 0 ? 0m : (decimal)a.Feedback / a.Members,
                        ActiveMemberShare = a.Members == 0 ? 0m : (decimal)a.ActiveMembers / a.Members
                    };
                }).ToList();
            });

            return panel;
        }

        private Panel BuildSystemPanel()
        {
            var panel = NewContentPanel();

            AddSectionTitle(panel, "System / Admin Panel", 24, 20);
            AddNote(panel, "Read-only system information. No configuration value containing a secret is displayed.", 24, 52, 920, 20);

            var dgv = NewGrid();
            dgv.Location = new Point(24, 84);
            dgv.Size = new Size(980, 470);

            panel.Controls.Add(dgv);

            panel.Tag = (Func<Task>)(async () =>
            {
                var counts = await _runtime.MasterTenantAdminService.GetPlatformCountsAsync();
                var masterUsers = await _runtime.MasterIdentityService.GetUsersAsync();

                dgv.DataSource = new System.Collections.Generic.List<object>
                {
                    new { Item = "Master database", Value = "Master DB (server name not shown; connection resolved from configuration)" },
                    new { Item = "Companies", Value = $"{counts.TotalCompanies} total / {counts.ActiveCompanies} active / {counts.InactiveCompanies} inactive" },
                    new { Item = "Tenant databases", Value = $"{counts.ConfiguredDatabases} configured / {counts.ActiveDatabases} active" },
                    new { Item = "Master users (Identity)", Value = masterUsers.Count.ToString() },
                    new { Item = "Tenants available for switching", Value = _tenants.Count.ToString() },
                    new { Item = "Signed-in master admin", Value = _session.Username + " (" + _session.Role + ")" },
                    new { Item = "Master DB write policy", Value = "Identity users/roles only, plus the company IsActive flag" },
                    new { Item = "API authentication", Value = "Anonymous today (reported as a remaining security item in the Phase 5 preflight)" }
                };
            });

            return panel;
        }
    }

    /// <summary>
    /// Small modal prompt for typing a new password twice. The value lives only in memory -
    /// it is never logged, stored or displayed after the dialog closes.
    /// </summary>
    internal sealed class MasterPasswordPromptForm : Form
    {
        private static readonly Color ColorBackground = Color.FromArgb(18, 18, 18);
        private static readonly Color ColorField = Color.FromArgb(38, 38, 38);
        private static readonly Color ColorGold = Color.FromArgb(212, 175, 55);
        private static readonly Color ColorText = Color.White;
        private static readonly Color ColorMuted = Color.FromArgb(150, 150, 150);

        private readonly TextBox txtPassword = new();
        private readonly TextBox txtConfirm = new();
        private readonly Label lblError = new();

        public string? PasswordValue { get; private set; }

        public MasterPasswordPromptForm(string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(400, 190);
            BackColor = ColorBackground;
            Font = new Font("Segoe UI", 9F);

            var lblInfo = new Label
            {
                Text = "Identity minimum-length rules apply (default 6+ characters).",
                ForeColor = ColorMuted,
                AutoSize = false,
                Location = new Point(20, 14),
                Size = new Size(360, 18)
            };

            var lblPassword = new Label { Text = "New password:", ForeColor = ColorText, AutoSize = false, Location = new Point(20, 40), Size = new Size(150, 18) };
            txtPassword.Location = new Point(170, 37);
            txtPassword.Size = new Size(210, 24);
            txtPassword.BackColor = ColorField;
            txtPassword.ForeColor = ColorText;
            txtPassword.BorderStyle = BorderStyle.FixedSingle;
            txtPassword.UseSystemPasswordChar = true;

            var lblConfirm = new Label { Text = "Confirm password:", ForeColor = ColorText, AutoSize = false, Location = new Point(20, 72), Size = new Size(150, 18) };
            txtConfirm.Location = new Point(170, 69);
            txtConfirm.Size = new Size(210, 24);
            txtConfirm.BackColor = ColorField;
            txtConfirm.ForeColor = ColorText;
            txtConfirm.BorderStyle = BorderStyle.FixedSingle;
            txtConfirm.UseSystemPasswordChar = true;

            lblError.ForeColor = Color.FromArgb(220, 90, 90);
            lblError.AutoSize = false;
            lblError.Location = new Point(20, 100);
            lblError.Size = new Size(360, 18);

            var btnOk = new Button
            {
                Text = "Reset password",
                Location = new Point(170, 130),
                Size = new Size(150, 32),
                BackColor = ColorGold,
                ForeColor = Color.FromArgb(20, 20, 20),
                FlatStyle = FlatStyle.Flat
            };
            btnOk.Click += (s, e) =>
            {
                if (txtPassword.Text.Length == 0)
                {
                    lblError.Text = "Enter a new password.";
                    return;
                }

                if (txtPassword.Text != txtConfirm.Text)
                {
                    lblError.Text = "Passwords do not match.";
                    return;
                }

                PasswordValue = txtPassword.Text;
                DialogResult = DialogResult.OK;
                Close();
            };

            var btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(20, 130),
                Size = new Size(120, 32),
                BackColor = ColorBackground,
                ForeColor = ColorMuted,
                FlatStyle = FlatStyle.Flat
            };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(lblInfo);
            Controls.Add(lblPassword);
            Controls.Add(txtPassword);
            Controls.Add(lblConfirm);
            Controls.Add(txtConfirm);
            Controls.Add(lblError);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
        }
    }
}
