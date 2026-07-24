using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LOKAL.PowerPoint.UI
{
    public class LoginForm : Form
    {
        private readonly ThisAddIn _addIn;
        
        // Login View Controls
        private FlowLayoutPanel _pnlLoginView;
        private TextBox _txtLoginUsername;
        private TextBox _txtLoginPassword;
        private Button _btnLogin;
        private Label _lblLoginError;

        // Register View Controls
        private FlowLayoutPanel _pnlRegisterView;
        private TextBox _txtRegDisplayName;
        private TextBox _txtRegUsername;
        private TextBox _txtRegEmail;
        private TextBox _txtRegPassword;
        private Button _btnRegister;
        private Label _lblRegError;

        // Light Theme Colors
        private readonly Color _bgWhite = Color.White;
        private readonly Color _inputBg = Color.FromArgb(245, 246, 250);
        private readonly Color _primaryPurple = LokalUi.Primary;
        private readonly Color _textDark = Color.FromArgb(30, 30, 30);
        private readonly Color _textGrey = Color.FromArgb(120, 120, 120);
        private readonly Color _borderGrey = Color.FromArgb(220, 220, 220);
        private readonly Color _errorRed = Color.FromArgb(220, 53, 69);

        public LoginForm(ThisAddIn addIn)
        {
            _addIn = addIn;
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = "LOKAL Account";
            // Increased size to ensure Register form fits perfectly without cutting off
            this.Size = new Size(1000, 700); 
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = _bgWhite;
            this.Font = new Font("Segoe UI", 10f);
            this.ShowIcon = false;

            // --- Main Split Layout ---
            var mainSplit = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            mainSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 450F)); // Left side width
            mainSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));  // Right side fills remaining
            this.Controls.Add(mainSplit);

            // --- Left Panel (Background Image) ---
            var pnlLeft = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Margin = new Padding(0) };
            try {
                pnlLeft.BackgroundImage = Image.FromFile(@"c:\xampp\htdocs\LOKAL-ThesisSys\assets\bg.png");
                pnlLeft.BackgroundImageLayout = ImageLayout.Stretch;
            } catch { }
            mainSplit.Controls.Add(pnlLeft, 0, 0);

            // LOKAL Placeholder
            var leftCenterTlp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = Color.Transparent };
            leftCenterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            leftCenterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            leftCenterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            leftCenterTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            leftCenterTlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            leftCenterTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            pnlLeft.Controls.Add(leftCenterTlp);

            var pnlPlaceholderBox = new Panel { Size = new Size(300, 200), BackColor = Color.Transparent, Margin = new Padding(0) };
            
            PictureBox imgLogo = null;
            try {
                imgLogo = new PictureBox
                {
                    Image = Image.FromFile(@"c:\xampp\htdocs\LOKAL-ThesisSys\assets\android-chrome-512x512.png"),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Dock = DockStyle.Top,
                    Height = 120,
                    BackColor = Color.Transparent
                };
            } catch { }

            var lblPlaceholder = new Label { Text = "LOKAL", ForeColor = Color.White, Font = new Font("Segoe UI", 40f, FontStyle.Bold), AutoSize = false, Dock = DockStyle.Bottom, Height = 80, TextAlign = ContentAlignment.MiddleCenter };
            pnlPlaceholderBox.Controls.Add(lblPlaceholder);
            if (imgLogo != null) pnlPlaceholderBox.Controls.Add(imgLogo);
            leftCenterTlp.Controls.Add(pnlPlaceholderBox, 1, 1);

            // --- Right Panel (White Form Area) ---
            var pnlRight = new Panel { Dock = DockStyle.Fill, BackColor = _bgWhite, Margin = new Padding(0) };
            mainSplit.Controls.Add(pnlRight, 1, 0);

            // Centering layout for Right Panel
            var rightCenterTlp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = Color.Transparent };
            rightCenterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rightCenterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rightCenterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rightCenterTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            rightCenterTlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightCenterTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            pnlRight.Controls.Add(rightCenterTlp);

            // Container for both views in the center cell
            var viewContainer = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = _bgWhite, Margin = new Padding(0) };
            rightCenterTlp.Controls.Add(viewContainer, 1, 1);

            int inputWidth = 360; // Slightly wider inputs for better breathing room

            // ============================================
            // 1. LOGIN VIEW
            // ============================================
            _pnlLoginView = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = _bgWhite,
                Margin = new Padding(0),
                WrapContents = false,
                Visible = true // Default view
            };
            viewContainer.Controls.Add(_pnlLoginView);

            var lblLoginTitle = new Label { Text = "Welcome Back", Font = new Font("Segoe UI", 26f, FontStyle.Bold), ForeColor = _textDark, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
            _pnlLoginView.Controls.Add(lblLoginTitle);
            var lblLoginSub = new Label { Text = "Sign in to your account", Font = new Font("Segoe UI", 11f), ForeColor = _textGrey, AutoSize = true, Margin = new Padding(5, 0, 0, 30) };
            _pnlLoginView.Controls.Add(lblLoginSub);

            // Login Username
            var lblLoginUser = new Label { Text = "Username", ForeColor = _textDark, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), AutoSize = true, Margin = new Padding(5, 0, 0, 5) };
            _pnlLoginView.Controls.Add(lblLoginUser);
            var pnlLoginUser = new Panel { Size = new Size(inputWidth, 45), BackColor = _inputBg, Margin = new Padding(0, 0, 0, 20) };
            pnlLoginUser.Paint += DrawRoundedBorder;
            _txtLoginUsername = new TextBox { Location = new Point(15, 12), Size = new Size(inputWidth - 30, 20), BorderStyle = BorderStyle.None, BackColor = _inputBg, ForeColor = _textDark, Font = new Font("Segoe UI", 11f) };
            pnlLoginUser.Controls.Add(_txtLoginUsername);
            _pnlLoginView.Controls.Add(pnlLoginUser);

            // Login Password
            var lblLoginPass = new Label { Text = "Password", ForeColor = _textDark, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), AutoSize = true, Margin = new Padding(5, 0, 0, 5) };
            _pnlLoginView.Controls.Add(lblLoginPass);
            var pnlLoginPass = new Panel { Size = new Size(inputWidth, 45), BackColor = _inputBg, Margin = new Padding(0, 0, 0, 10) };
            pnlLoginPass.Paint += DrawRoundedBorder;
            _txtLoginPassword = new TextBox { Location = new Point(15, 12), Size = new Size(inputWidth - 80, 20), BorderStyle = BorderStyle.None, BackColor = _inputBg, ForeColor = _textDark, Font = new Font("Segoe UI", 11f), UseSystemPasswordChar = true };
            pnlLoginPass.Controls.Add(_txtLoginPassword);
            var lblShowLoginPass = new Label { Text = "Show", Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = _textGrey, AutoSize = true, Cursor = Cursors.Hand, Location = new Point(inputWidth - 55, 14) };
            lblShowLoginPass.Click += (s, e) => {
                _txtLoginPassword.UseSystemPasswordChar = !_txtLoginPassword.UseSystemPasswordChar;
                lblShowLoginPass.Text = _txtLoginPassword.UseSystemPasswordChar ? "Show" : "Hide";
            };
            pnlLoginPass.Controls.Add(lblShowLoginPass);
            _pnlLoginView.Controls.Add(pnlLoginPass);

            // Login Error
            _lblLoginError = new Label { Text = "Invalid username or password", ForeColor = _errorRed, Font = new Font("Segoe UI", 9.5f), AutoSize = true, Margin = new Padding(5, 0, 0, 15), Visible = false };
            _pnlLoginView.Controls.Add(_lblLoginError);

            // Login Button
            _btnLogin = new Button { Text = "Log in", BackColor = _primaryPurple, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(inputWidth, 50), Font = new Font("Segoe UI", 12f, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(0, 10, 0, 30) };
            _btnLogin.FlatAppearance.BorderSize = 0;
            _btnLogin.Click += BtnLogin_Click;
            _btnLogin.Paint += PaintRoundedButton;
            _pnlLoginView.Controls.Add(_btnLogin);
            this.AcceptButton = _btnLogin; // Set default accept button

            // Login Footer
            var pnlLoginFooter = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            pnlLoginFooter.Controls.Add(new Label { Text = "Don't have an account?", ForeColor = _textGrey, Font = new Font("Segoe UI", 10f), AutoSize = true, Margin = new Padding(0, 0, 5, 0) });
            var lnkGoRegister = new LinkLabel { Text = "Create one", Font = new Font("Segoe UI", 10f, FontStyle.Bold), LinkColor = _primaryPurple, ActiveLinkColor = _textDark, AutoSize = true, Margin = new Padding(0), LinkBehavior = LinkBehavior.HoverUnderline };
            lnkGoRegister.LinkClicked += (s, e) => SwitchView(false);
            pnlLoginFooter.Controls.Add(lnkGoRegister);
            var loginFooterWrapper = new Panel { Size = new Size(inputWidth, 30), BackColor = _bgWhite, Margin = new Padding(0) };
            loginFooterWrapper.Controls.Add(pnlLoginFooter);
            pnlLoginFooter.Location = new Point((inputWidth - pnlLoginFooter.PreferredSize.Width) / 2, 0);
            _pnlLoginView.Controls.Add(loginFooterWrapper);

            // ============================================
            // 2. REGISTER VIEW
            // ============================================
            _pnlRegisterView = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = _bgWhite,
                Margin = new Padding(0),
                WrapContents = false,
                Visible = false // Hidden initially
            };
            viewContainer.Controls.Add(_pnlRegisterView);

            var lblRegTitle = new Label { Text = "Create an account", Font = new Font("Segoe UI", 26f, FontStyle.Bold), ForeColor = _textDark, AutoSize = true, Margin = new Padding(0, 0, 0, 5) };
            _pnlRegisterView.Controls.Add(lblRegTitle);
            var lblRegSub = new Label { Text = "Join LOKAL to manage your classes", Font = new Font("Segoe UI", 11f), ForeColor = _textGrey, AutoSize = true, Margin = new Padding(5, 0, 0, 20) };
            _pnlRegisterView.Controls.Add(lblRegSub);

            // Register Display Name
            var lblRegName = new Label { Text = "Display Name", ForeColor = _textDark, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), AutoSize = true, Margin = new Padding(5, 0, 0, 5) };
            _pnlRegisterView.Controls.Add(lblRegName);
            var pnlRegName = new Panel { Size = new Size(inputWidth, 42), BackColor = _inputBg, Margin = new Padding(0, 0, 0, 15) };
            pnlRegName.Paint += DrawRoundedBorder;
            _txtRegDisplayName = new TextBox { Location = new Point(15, 11), Size = new Size(inputWidth - 30, 20), BorderStyle = BorderStyle.None, BackColor = _inputBg, ForeColor = _textDark, Font = new Font("Segoe UI", 11f) };
            pnlRegName.Controls.Add(_txtRegDisplayName);
            _pnlRegisterView.Controls.Add(pnlRegName);

            // Register Username
            var lblRegUser = new Label { Text = "Username", ForeColor = _textDark, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), AutoSize = true, Margin = new Padding(5, 0, 0, 5) };
            _pnlRegisterView.Controls.Add(lblRegUser);
            var pnlRegUser = new Panel { Size = new Size(inputWidth, 42), BackColor = _inputBg, Margin = new Padding(0, 0, 0, 15) };
            pnlRegUser.Paint += DrawRoundedBorder;
            _txtRegUsername = new TextBox { Location = new Point(15, 11), Size = new Size(inputWidth - 30, 20), BorderStyle = BorderStyle.None, BackColor = _inputBg, ForeColor = _textDark, Font = new Font("Segoe UI", 11f) };
            pnlRegUser.Controls.Add(_txtRegUsername);
            _pnlRegisterView.Controls.Add(pnlRegUser);

            // Register Email
            var lblRegEmail = new Label { Text = "Email", ForeColor = _textDark, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), AutoSize = true, Margin = new Padding(5, 0, 0, 5) };
            _pnlRegisterView.Controls.Add(lblRegEmail);
            var pnlRegEmail = new Panel { Size = new Size(inputWidth, 42), BackColor = _inputBg, Margin = new Padding(0, 0, 0, 15) };
            pnlRegEmail.Paint += DrawRoundedBorder;
            _txtRegEmail = new TextBox { Location = new Point(15, 11), Size = new Size(inputWidth - 30, 20), BorderStyle = BorderStyle.None, BackColor = _inputBg, ForeColor = _textDark, Font = new Font("Segoe UI", 11f) };
            pnlRegEmail.Controls.Add(_txtRegEmail);
            _pnlRegisterView.Controls.Add(pnlRegEmail);

            // Register Password
            var lblRegPass = new Label { Text = "Password", ForeColor = _textDark, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), AutoSize = true, Margin = new Padding(5, 0, 0, 5) };
            _pnlRegisterView.Controls.Add(lblRegPass);
            var pnlRegPass = new Panel { Size = new Size(inputWidth, 42), BackColor = _inputBg, Margin = new Padding(0, 0, 0, 10) };
            pnlRegPass.Paint += DrawRoundedBorder;
            _txtRegPassword = new TextBox { Location = new Point(15, 11), Size = new Size(inputWidth - 80, 20), BorderStyle = BorderStyle.None, BackColor = _inputBg, ForeColor = _textDark, Font = new Font("Segoe UI", 11f), UseSystemPasswordChar = true };
            pnlRegPass.Controls.Add(_txtRegPassword);
            var lblShowRegPass = new Label { Text = "Show", Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = _textGrey, AutoSize = true, Cursor = Cursors.Hand, Location = new Point(inputWidth - 55, 12) };
            lblShowRegPass.Click += (s, e) => {
                _txtRegPassword.UseSystemPasswordChar = !_txtRegPassword.UseSystemPasswordChar;
                lblShowRegPass.Text = _txtRegPassword.UseSystemPasswordChar ? "Show" : "Hide";
            };
            pnlRegPass.Controls.Add(lblShowRegPass);
            _pnlRegisterView.Controls.Add(pnlRegPass);

            // Register Error
            _lblRegError = new Label { Text = "Invalid input", ForeColor = _errorRed, Font = new Font("Segoe UI", 9.5f), AutoSize = true, Margin = new Padding(5, 0, 0, 10), Visible = false };
            _pnlRegisterView.Controls.Add(_lblRegError);

            // Register Button
            _btnRegister = new Button { Text = "Create account", BackColor = _primaryPurple, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(inputWidth, 50), Font = new Font("Segoe UI", 12f, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(0, 5, 0, 25) };
            _btnRegister.FlatAppearance.BorderSize = 0;
            _btnRegister.Click += BtnRegister_Click;
            _btnRegister.Paint += PaintRoundedButton;
            _pnlRegisterView.Controls.Add(_btnRegister);

            // Register Footer
            var pnlRegFooter = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            pnlRegFooter.Controls.Add(new Label { Text = "Already have an account?", ForeColor = _textGrey, Font = new Font("Segoe UI", 10f), AutoSize = true, Margin = new Padding(0, 0, 5, 0) });
            var lnkGoLogin = new LinkLabel { Text = "Log in", Font = new Font("Segoe UI", 10f, FontStyle.Bold), LinkColor = _primaryPurple, ActiveLinkColor = _textDark, AutoSize = true, Margin = new Padding(0), LinkBehavior = LinkBehavior.HoverUnderline };
            lnkGoLogin.LinkClicked += (s, e) => SwitchView(true);
            pnlRegFooter.Controls.Add(lnkGoLogin);
            var regFooterWrapper = new Panel { Size = new Size(inputWidth, 30), BackColor = _bgWhite, Margin = new Padding(0) };
            regFooterWrapper.Controls.Add(pnlRegFooter);
            pnlRegFooter.Location = new Point((inputWidth - pnlRegFooter.PreferredSize.Width) / 2, 0);
            _pnlRegisterView.Controls.Add(regFooterWrapper);
        }

        // Handle switching views seamlessly
        private void SwitchView(bool showLogin)
        {
            _pnlLoginView.Visible = showLogin;
            _pnlRegisterView.Visible = !showLogin;
            
            // Switch accept button to correct view
            this.AcceptButton = showLogin ? _btnLogin : _btnRegister;

            // Clear errors on switch
            _lblLoginError.Visible = false;
            _lblRegError.Visible = false;
        }

        private void PaintRoundedButton(object sender, PaintEventArgs e)
        {
            var btn = sender as Button;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var p = new GraphicsPath(); int r = 8;
            p.AddArc(0, 0, r*2, r*2, 180, 90); p.AddArc(btn.Width - r*2, 0, r*2, r*2, 270, 90);
            p.AddArc(btn.Width - r*2, btn.Height - r*2, r*2, r*2, 0, 90); p.AddArc(0, btn.Height - r*2, r*2, r*2, 90, 90);
            p.CloseAllFigures(); btn.Region = new Region(p);
        }

        private void DrawRoundedBorder(object sender, PaintEventArgs e)
        {
            var pnl = sender as Panel;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var path = new GraphicsPath(); int r = 8;
            path.AddArc(0, 0, r, r, 180, 90); path.AddArc(pnl.Width - r, 0, r, r, 270, 90);
            path.AddArc(pnl.Width - r, pnl.Height - r, r, r, 0, 90); path.AddArc(0, pnl.Height - r, r, r, 90, 90);
            path.CloseAllFigures();
            using (var pen = new Pen(_borderGrey, 1)) { e.Graphics.DrawPath(pen, path); }
            pnl.Region = new Region(path);
        }

        private async void BtnLogin_Click(object sender, EventArgs e)
        {
            string user = _txtLoginUsername.Text.Trim(); string pass = _txtLoginPassword.Text;
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)) { _lblLoginError.Text = "Please enter both username and password."; _lblLoginError.Visible = true; return; }
            _btnLogin.Enabled = false; _btnLogin.Text = "Logging in..."; _lblLoginError.Visible = false;
            try {
                var result = await _addIn.ApiClient.LoginAsync(user, pass);
                SaveAndClose(result.Token, result.Teacher?.DisplayName ?? user, result.Teacher?.Email ?? "");
            } catch (Exception ex) { _lblLoginError.Text = ex.Message; _lblLoginError.Visible = true; } finally { _btnLogin.Enabled = true; _btnLogin.Text = "Log in"; }
        }

        private async void BtnRegister_Click(object sender, EventArgs e)
        {
            string name = _txtRegDisplayName.Text.Trim(); string user = _txtRegUsername.Text.Trim();
            string email = _txtRegEmail.Text.Trim(); string pass = _txtRegPassword.Text;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)) { _lblRegError.Text = "Please fill in all required fields."; _lblRegError.Visible = true; return; }
            _btnRegister.Enabled = false; _btnRegister.Text = "Creating account..."; _lblRegError.Visible = false;
            try {
                var result = await _addIn.ApiClient.RegisterAsync(user, email, pass, name);
                SaveAndClose(result.Token, result.Teacher?.DisplayName ?? user, result.Teacher?.Email ?? email);
            } catch (Exception ex) { _lblRegError.Text = ex.Message; _lblRegError.Visible = true; } finally { _btnRegister.Enabled = true; _btnRegister.Text = "Create account"; }
        }

        private void SaveAndClose(string token, string displayName, string email)
        {
            Properties.Settings.Default.AuthToken = token;
            Properties.Settings.Default.TeacherDisplayName = displayName;
            Properties.Settings.Default.TeacherEmail = email;
            Properties.Settings.Default.Save();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
