using AlphaElite.infrastructure.services;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlphaElite.winforms
{
    /// <summary>
    /// Phase 1: credentials-only login.
    ///
    /// The form asks for NOTHING except username and password. There is no tenant, role,
    /// company or Master Admin selector: the signed-in context is resolved from the
    /// credentials themselves:
    ///
    ///     1. <see cref="ITenantAuthenticationService.FindTenantsContainingUserAsync"/>
    ///        performs a read-only username lookup across the active tenant databases.
    ///     2. Exactly one match  -> the user is authenticated in that tenant only
    ///        (company, role, branch scope all come from that tenant's UserAccounts row).
    ///     3. No match           -> the username is not a tenant user; it is authenticated
    ///        against the MASTER database via ASP.NET Identity (the only path into the
    ///        Master Admin interface).
    ///     4. More than one      -> ambiguous credentials: the login fails closed with the
    ///        generic error. The password is never tried against every candidate tenant.
    ///
    /// A failed attempt always reports the same generic error, never which tenant (if any)
    /// holds the username, never connection or role details.
    /// </summary>
    internal class LoginForm : Form
    {
        private readonly ITenantAuthenticationService _authenticationService;
        private readonly IMasterIdentityService? _masterIdentityService;

        // ===== Alpha Elite brand palette =====
        private static readonly Color ColorBackground = Color.FromArgb(18, 18, 18);
        private static readonly Color ColorPanel = Color.FromArgb(28, 28, 28);
        private static readonly Color ColorFieldBackground = Color.FromArgb(38, 38, 38);
        private static readonly Color ColorGold = Color.FromArgb(212, 175, 55);
        private static readonly Color ColorGoldHover = Color.FromArgb(230, 195, 90);
        private static readonly Color ColorTextPrimary = Color.White;
        private static readonly Color ColorTextMuted = Color.FromArgb(150, 150, 150);
        private static readonly Color ColorError = Color.FromArgb(220, 90, 90);

        private TextBox txtUsername = null!;
        private TextBox txtPassword = null!;
        private Button btnLogin = null!;
        private Button btnCancel = null!;
        private Label lblError = null!;

        public string LoggedInFullName { get; private set; } = string.Empty;
        public string LoggedInRole { get; private set; } = string.Empty;
        public string LoggedInUsername { get; private set; } = string.Empty;

        /// <summary>Company/tenant the user authenticated against (never hardcoded, never chosen in the UI).</summary>
        public int CompanyId { get; private set; }
        public string CompanyCode { get; private set; } = string.Empty;
        public string CompanyName { get; private set; } = string.Empty;

        /// <summary>True when the authenticated tenant uses the branch-aware schema (Tenant C).</summary>
        public bool IsBranchAware { get; private set; }

        /// <summary>Branch scope of the signed-in user (Tenant C); null means tenant-wide.</summary>
        public int? BranchId { get; private set; }

        /// <summary>Member record linked to this login (Tenant C member accounts only).</summary>
        public int? MemberId { get; private set; }

        public string TenantDisplayName { get; private set; } = string.Empty;

        /// <summary>True when the stored hash is still legacy unsalted SHA-256 (no secure upgrade happened).</summary>
        public bool NeedsPasswordRehash { get; private set; }

        /// <summary>True when this login transparently upgraded a legacy hash to PBKDF2.</summary>
        public bool PasswordWasUpgraded { get; private set; }

        /// <summary>True when the credentials resolved to the Master Admin context; <see cref="MasterSession"/> is then set.</summary>
        public bool MasterLoginSelected { get; private set; }

        /// <summary>Master session produced by the Master DB Identity authentication.</summary>
        public MasterUserSession? MasterSession { get; private set; }

        public LoginForm(ITenantAuthenticationService authenticationService, IMasterIdentityService? masterIdentityService = null)
        {
            _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
            _masterIdentityService = masterIdentityService;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Alpha Elite - Login";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 540);
            BackColor = ColorBackground;
            Font = new Font("Segoe UI", 9F);

            // ---- Logo ----
            var pbLogo = new PictureBox
            {
                Location = new Point(120, 34),
                Size = new Size(180, 83),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };

            string logoPath = Path.Combine(AppContext.BaseDirectory, "logo.png");
            if (File.Exists(logoPath))
            {
                pbLogo.Image = Image.FromFile(logoPath);
            }

            // ---- Brand block ----
            var lblBrandTitle = new Label
            {
                Text = "ALPHA ELITE",
                ForeColor = ColorGold,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 130),
                Size = new Size(420, 30)
            };

            var lblBrandSubtitle = new Label
            {
                Text = "G Y M   M A N A G E M E N T",
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 162),
                Size = new Size(420, 20)
            };

            var lblWelcome = new Label
            {
                Text = "Welcome back",
                ForeColor = ColorTextPrimary,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 200),
                Size = new Size(420, 30)
            };

            // ---- Username field ----
            var lblUsername = new Label
            {
                Text = "USERNAME",
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Location = new Point(50, 243),
                Size = new Size(320, 16)
            };

            var pnlUsername = new Panel
            {
                Location = new Point(50, 262),
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
            var lblPassword = new Label
            {
                Text = "PASSWORD",
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Location = new Point(50, 315),
                Size = new Size(320, 16)
            };

            var pnlPassword = new Panel
            {
                Location = new Point(50, 334),
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
                Location = new Point(50, 378),
                Size = new Size(320, 32)
            };

            // ---- Login button ----
            btnLogin = new Button
            {
                Text = "LOGIN",
                Location = new Point(50, 418),
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

            // ---- Cancel (secondary, text-link style) ----
            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(50, 466),
                Size = new Size(320, 30),
                BackColor = ColorBackground,
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI", 9F),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.FlatAppearance.MouseOverBackColor = ColorPanel;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            // ---- Footer ----
            var lblFooter = new Label
            {
                Text = "Alpha Elite Management System",
                ForeColor = ColorTextMuted,
                Font = new Font("Segoe UI", 8F),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 506),
                Size = new Size(420, 20)
            };

            AcceptButton = btnLogin;

            Controls.Add(pbLogo);
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

                // -----------------------------------------------------------------
                // Step 1: resolve which tenant (if any) holds this username.
                // Read-only username probe - the password is not sent anywhere yet.
                // -----------------------------------------------------------------
                var candidateTenants = await _authenticationService.FindTenantsContainingUserAsync(username);

                if (candidateTenants.Count == 1)
                {
                    // ---------------------------------------------------------
                    // Step 2a: exactly one tenant holds this username. The tenant,
                    // company, role and branch scope ALL come from that account -
                    // nothing is selectable here.
                    // ---------------------------------------------------------
                    var result = await _authenticationService.AuthenticateAsync(
                        candidateTenants[0].CompanyId, username, password);

                    if (result == null)
                    {
                        lblError.Text = "Invalid username or password.";
                        return;
                    }

                    CompanyId = result.CompanyId;
                    CompanyCode = result.CompanyCode;
                    CompanyName = result.CompanyName;
                    TenantDisplayName = candidateTenants[0].DisplayName;
                    LoggedInUsername = result.Username;
                    LoggedInFullName = result.FullName;
                    LoggedInRole = result.Role;
                    IsBranchAware = result.IsBranchAware;
                    BranchId = result.BranchId;
                    MemberId = result.MemberId;
                    NeedsPasswordRehash = result.NeedsPasswordRehash;

                    // Transparent legacy-hash upgrade. Only branch-aware tenants are writable, so
                    // Tenant A/B keep working with NeedsPasswordRehash reported and no write.
                    // Nothing about the password or either hash is ever logged.
                    if (result.NeedsPasswordRehash)
                    {
                        try
                        {
                            PasswordWasUpgraded = await _authenticationService
                                .UpgradePasswordHashIfNeededAsync(result.CompanyId, result.Username, password);

                            if (PasswordWasUpgraded)
                            {
                                NeedsPasswordRehash = false;
                            }
                        }
                        catch
                        {
                            // An upgrade failure must never block an otherwise valid login.
                        }
                    }

                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                if (candidateTenants.Count == 0)
                {
                    // ---------------------------------------------------------
                    // Step 2b: no tenant holds this username. The only remaining
                    // context is the MASTER database (ASP.NET Identity). Tenant
                    // credentials can never reach the master context and vice versa.
                    // ---------------------------------------------------------
                    if (_masterIdentityService == null)
                    {
                        lblError.Text = "Invalid username or password.";
                        return;
                    }

                    var masterResult = await _masterIdentityService.AuthenticateAsync(username, password);

                    if (!masterResult.Success)
                    {
                        lblError.Text = "Invalid username or password.";
                        return;
                    }

                    MasterLoginSelected = true;
                    MasterSession = new MasterUserSession
                    {
                        MasterUserId = masterResult.MasterUserId,
                        Username = masterResult.Username,
                        Role = masterResult.Role,
                        IdentityRoleName = masterResult.Role
                    };

                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                // -------------------------------------------------------------
                // Step 2c: the username exists in MORE THAN ONE tenant. Credentials
                // alone cannot resolve the context, so the login fails closed -
                // the password is never tried against every candidate tenant and
                // no tenant information is revealed.
                // -------------------------------------------------------------
                lblError.Text = "Invalid username or password.";
            }
            catch
            {
                // Never leak exception details (connection strings, tenant names, schema).
                lblError.Text = "Invalid username or password.";
            }
            finally
            {
                btnLogin.Enabled = true;
                btnCancel.Enabled = true;
            }
        }
    }
}
