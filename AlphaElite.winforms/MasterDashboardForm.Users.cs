using AlphaElite.infrastructure.services;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Master user administration on top of ASP.NET Identity. Passwords are hashed by Identity
    /// (PBKDF2); hashes are never displayed. Destructive operations require confirmation, and the
    /// last active SuperAdmin can never be disabled.
    /// </summary>
    internal partial class MasterDashboardForm
    {
        private DataGridView dgvUsers = null!;

        private Panel BuildUsersPanel()
        {
            var panel = NewContentPanel();

            AddSectionTitle(panel, "Master Users / Administrators (ASP.NET Identity)", 24, 20);
            AddNote(panel, "Passwords are hashed by Identity (PBKDF2); hashes are never displayed. Disabling sets a far-future lockout end date.", 24, 52, 920, 20);

            dgvUsers = NewGrid();
            dgvUsers.Location = new Point(24, 84);
            dgvUsers.Size = new Size(980, 290);

            var txtUsername = NewField(24, 396, 200);
            var txtEmail = NewField(240, 396, 220);
            var txtPassword = NewField(476, 396, 200, password: true);
            var cboRole = NewCombo(692, 395, 160, "SuperAdmin", "Admin", "Manager", "GymStaff", "Member");

            var lblCreate = new Label
            {
                Text = "Username / Email / Password / Role:",
                ForeColor = ColorMuted,
                AutoSize = false,
                Location = new Point(24, 374),
                Size = new Size(400, 18)
            };

            var btnCreate = NewPrimaryButton("Create user", 24, 434, 140);
            var btnSetRole = NewSecondaryButton("Set role of selected", 180, 434, 170);
            var btnToggleActive = NewSecondaryButton("Enable / Disable selected", 366, 434, 190);
            var btnResetPassword = NewSecondaryButton("Reset password of selected", 572, 434, 200);
            var btnRefresh = NewSecondaryButton("Refresh list", 788, 434, 120);

            // Every user action is serialized against every other master-database action
            // (the panels and services share one MasterAlphaEliteDbContext instance).
            btnCreate.Click += async (s, e) => await RunMasterOperationAsync(() => CreateUserAsync(txtUsername, txtEmail, txtPassword, cboRole));
            btnSetRole.Click += async (s, e) => await RunMasterOperationAsync(() => SetRoleAsync(cboRole));
            btnToggleActive.Click += async (s, e) => await RunMasterOperationAsync(ToggleUserActiveAsync);
            btnResetPassword.Click += async (s, e) => await RunMasterOperationAsync(ResetPasswordAsync);
            btnRefresh.Click += async (s, e) => await RunMasterOperationAsync(LoadUsersAsync);

            panel.Controls.Add(dgvUsers);
            panel.Controls.Add(lblCreate);
            panel.Controls.Add(txtUsername);
            panel.Controls.Add(txtEmail);
            panel.Controls.Add(txtPassword);
            panel.Controls.Add(cboRole);
            panel.Controls.Add(btnCreate);
            panel.Controls.Add(btnSetRole);
            panel.Controls.Add(btnToggleActive);
            panel.Controls.Add(btnResetPassword);
            panel.Controls.Add(btnRefresh);

            panel.Tag = (Func<Task>)(async () => await LoadUsersAsync());

            return panel;
        }

        private async Task LoadUsersAsync()
        {
            var users = await _runtime.MasterIdentityService.GetUsersAsync();
            dgvUsers.DataSource = users.Select(u => new
            {
                u.Username,
                u.Email,
                Roles = string.Join(", ", u.Roles),
                Status = u.IsActive ? "Active" : "Disabled",
                FailedLogins = u.AccessFailedCount
            }).ToList();
        }

        private async Task CreateUserAsync(TextBox txtUsername, TextBox txtEmail, TextBox txtPassword, ComboBox cboRole)
        {
            var username = txtUsername.Text?.Trim() ?? string.Empty;
            var password = txtPassword.Text ?? string.Empty;

            if (username.Length == 0 || password.Length == 0)
            {
                MessageBox.Show("Username and password are required.", "Create user",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var role = (string)cboRole.SelectedItem!;
            var confirm = MessageBox.Show(
                $"Create master user '{username}' with role {role}?",
                "Confirm user creation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            var (success, error) = await _runtime.MasterIdentityService.CreateUserAsync(username, password, txtEmail.Text?.Trim() ?? string.Empty, role);
            MessageBox.Show(
                success ? $"User '{username}' created." : "Failed: " + error,
                success ? "User created" : "Creation failed",
                MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Error);

            if (success)
            {
                txtUsername.Text = string.Empty;
                txtEmail.Text = string.Empty;
                txtPassword.Text = string.Empty;
                await LoadUsersAsync();
            }
        }

        private async Task SetRoleAsync(ComboBox cboRole)
        {
            if (!TryGetSelectedUsername(out var username))
            {
                return;
            }

            var role = (string)cboRole.SelectedItem!;
            var confirm = MessageBox.Show(
                $"Change the role of '{username}' to {role}?",
                "Confirm role change", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            var userId = await ResolveUserIdAsync(username);
            var (success, error) = await _runtime.MasterIdentityService.SetRoleAsync(userId, role);
            MessageBox.Show(success ? "Role updated." : "Failed: " + error,
                "Role", MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Error);

            if (success)
            {
                await LoadUsersAsync();
            }
        }

        private async Task ToggleUserActiveAsync()
        {
            if (!TryGetSelectedUsername(out var username))
            {
                return;
            }

            var users = await _runtime.MasterIdentityService.GetUsersAsync();
            var info = users.FirstOrDefault(u => u.Username == username);
            if (info == null)
            {
                return;
            }

            // Lockout protection: the LAST active SuperAdmin can never be disabled here.
            if (info.IsActive)
            {
                var activeSuperAdmins = users.Count(u =>
                    u.Roles.Contains(MasterIdentityService.SuperAdminRoleName) && u.IsActive);
                if (activeSuperAdmins <= 1 && info.Roles.Contains(MasterIdentityService.SuperAdminRoleName))
                {
                    MessageBox.Show("Cannot disable the last active SuperAdmin.", "Protected",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            var confirm = MessageBox.Show(
                $"{(info.IsActive ? "Disable" : "Enable")} master user '{username}'?",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            var (success, error) = await _runtime.MasterIdentityService.SetActiveAsync(info.Id, !info.IsActive);
            MessageBox.Show(success ? "User status updated." : "Failed: " + error,
                "User status", MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Error);

            if (success)
            {
                await LoadUsersAsync();
            }
        }

        private async Task ResetPasswordAsync()
        {
            if (!TryGetSelectedUsername(out var username))
            {
                return;
            }

            var prompt = new MasterPasswordPromptForm("Reset password: " + username);
            if (prompt.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var newPassword = prompt.PasswordValue;
            if (string.IsNullOrEmpty(newPassword))
            {
                return;
            }

            var userId = await ResolveUserIdAsync(username);
            var (success, error) = await _runtime.MasterIdentityService.ResetPasswordAsync(userId, newPassword);
            MessageBox.Show(success ? "Password reset." : "Failed: " + error,
                "Password reset", MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }

        private bool TryGetSelectedUsername(out string username)
        {
            username = string.Empty;

            if (dgvUsers.CurrentRow?.DataBoundItem == null)
            {
                MessageBox.Show("Select a user row first.", "Master users",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            var value = dgvUsers.CurrentRow.DataBoundItem.GetType()
                .GetProperty("Username")?
                .GetValue(dgvUsers.CurrentRow.DataBoundItem) as string;

            if (string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show("Select a user row first.", "Master users",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            username = value;
            return true;
        }

        private async Task<string> ResolveUserIdAsync(string username)
        {
            var users = await _runtime.MasterIdentityService.GetUsersAsync();
            var user = users.FirstOrDefault(u => u.Username == username)
                       ?? throw new InvalidOperationException($"Master user '{username}' was not found.");
            return user.Id;
        }
    }
}
