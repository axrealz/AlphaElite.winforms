using AlphaElite.infrastructure.services;
using System;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 5: login window for the MASTER (platform) administrator.
    ///
    /// Visually matches the tenant login (dark charcoal + gold) but authenticates EXCLUSIVELY
    /// against the Master DB via ASP.NET Identity (<see cref="IMasterIdentityService"/>) - never
    /// against tenant UserAccounts, and never with the tenant password hasher. On success it
    /// yields a <see cref="MasterUserSession"/> which cannot be passed to the tenant window.
    /// </summary>
    internal class MasterLoginForm : Form
    {
        private static readonly Color ColorBackground = Color.FromArgb(18, 18, 18);
        private static readonly Color ColorPanel = Color.FromArgb(28, 28, 28);
        private static readonly Color ColorFieldBackground = Color.FromArgb(38, 38, 38);
        private static readonly Color ColorGold = Color.FromArgb(212, 175, 55);
        private static readonly Color ColorGoldHover = Color.FromArgb(230, 195, 90);
        private static readonly Color ColorTextPrimary = Color.White;
        private static readonly Color ColorTextMuted = Color.FromArgb(150, 150, 150);
        private static readonly Color ColorError = Color.FromArgb(220, 90, 90);

        private readonly IMasterIdentityService _masterIdentity;

        private TextBox txtUsername = null!;
        private TextBox txtPassword = null!;
        private Button btnLogin = null!;
        private Button btnCancel = null!;
        private Label lblError = null!;

        /// <summary>Set on successful authentication; handed to the master dashboard.</summary>
        public MasterUserSession? MasterSession { get; private set; }

        public MasterLoginForm(IMasterIdentityService masterIdentity)
        {
            _masterIdentity = masterIdentity ?? throw new ArgumentNullException(nameof(masterIdentity));

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Alpha Elite - Master Admin Login";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 470);
            BackColor = ColorBackground;
            Font = new Font("Segoe UI", 9F);

            // ---- Brand block ----
            var lblBrandTitle = new Label
            {
                Text = "ALPHA ELITE",
                ForeColor = ColorGold,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 40),
                Size = new Size(420, 30)
            };

            var lblBrandSubtitle = new Label
            {
                Text = "M A S T E R   A D M I N I S T R A T I O N",
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI", 9F),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 72),
                Size = new Size(420, 20)
            };

            var lblWelcome = new Label
            {
                Text = "Platform administrator sign-in",
                ForeColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 110),
                Size = new Size(420, 28)
            };

            // ---- Username field ----
            var lblUsername = new Label
            {
                Text = "USERNAME",
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Location = new Point(50, 158),
                Size = new Size(320, 16)
            };

            var pnlUsername = new Panel
            {
                Location = new Point(50, 177),
                Size = new Size(320, 40),
                BackColor = ColorFieldBackground
            };

            txtUsername = new TextBox
            {
                Location = new Point(12, 10),
                Size = new Size(296, 20),
                BackColor = ColorFieldBackground,
                ForeColor = ColorTextPrimary,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10F)
            };
            pnlUsername.Controls.Add(txtUsername);

            // ---- Password field ----
            var            lblPassword = new Label
            {
                Text = "PASSWORD",
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Location = new Point(50, 222),
                Size = new Size(320, 16)
            };

            var pnlPassword = new Panel
            {
                Location = new Point(50, 241),
                Size = new Size(320, 40),
                BackColor = ColorFieldBackground
            };

            txtPassword = new TextBox
            {
                Location = new Point(12, 10),
                Size = new Size(296, 20),
                BackColor = ColorFieldBackground,
                ForeColor = ColorTextPrimary,
                BorderStyle = BorderStyle.None,
                UseSystemPasswordChar = true,
                Font = new Font("Segoe UI", 10F)
            };
            pnlPassword.Controls.Add(txtPassword);

            // ---- Error message ----
            lblError = new Label
            {
                Text = string.Empty,
                ForeColor = ColorError,
                Font = new Font("Segoe UI", 8.5F),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(50, 285),
                Size = new Size(320, 32)
            };

            // ---- Login button ----
            btnLogin = new Button
            {
                Text = "LOGIN",
                Location = new Point(50, 322),
                Size = new Size(320, 42),
                BackColor = ColorGold,
                ForeColor = Color.FromArgb(20, 20, 20),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnLogin.FlatAppearance.BorderSize = 0;
            btnLogin.FlatAppearance.MouseOverBackColor = ColorGoldHover;
            btnLogin.Click += async (s, e) => await BtnLogin_Click(s, e);

            // ---- Cancel ----
            btnCancel = new Button
            {
                Text = "Back to tenant login",
                Location = new Point(50, 370),
                Size = new Size(320, 30),
                BackColor = ColorBackground,
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI", 9F),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.FlatAppearance.MouseOverBackColor = ColorPanel;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Retry; Close(); };

            // ---- Footer ----
            var lblFooter = new Label
            {
                Text = "Master database administration",
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI", 8F),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 412),
                Size = new Size(420, 20)
            };

            AcceptButton = btnLogin;

            Controls.Add(lblBrandTitle);
            Controls.Add(lblBrandSubtitle);
            Controls.Add(lblWelcome);
            Controls.Add(lblUsername);
            Controls.Add(pnlUsername);
            Controls.Add(lblPassword);
            Controls.Add(pnlPassword);
            Controls.Add(lblError);
            Controls.Add(btnLogin);
            Controls.Add(btnCancel);
            Controls.Add(lblFooter);
        }

        private async Task BtnLogin_Click(object? sender, EventArgs e)
        {
            lblError.Text = string.Empty;

            var username = txtUsername.Text?.Trim();
            var password = txtPassword.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                lblError.Text = "Please enter username and password.";
                return;
            }

            try
            {
                btnLogin.Enabled = false;
                btnCancel.Enabled = false;

                // Master authentication via ASP.NET Identity (Master DB only).
                var result = await _masterIdentity.AuthenticateAsync(username, password);

                if (!result.Success)
                {
                    lblError.Text = string.IsNullOrWhiteSpace(result.Error)
                        ? "Invalid username or password."
                        : result.Error;
                    return;
                }

                MasterSession = new MasterUserSession
                {
                    MasterUserId = result.MasterUserId,
                    Username = result.Username,
                    Role = result.Role,
                    IdentityRoleName = result.Role
                };

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                lblError.Text = "Login failed: " + ex.Message;
            }
            finally
            {
                btnLogin.Enabled = true;
                btnCancel.Enabled = true;
            }
        }
    }
}
