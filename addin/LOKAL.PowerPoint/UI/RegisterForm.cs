using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LOKAL.PowerPoint.UI
{
    public class RegisterForm : Form
    {
        private readonly ThisAddIn _addIn;
        private TextBox _txtDisplayName;
        private TextBox _txtUsername;
        private TextBox _txtEmail;
        private TextBox _txtPassword;
        private Button _btnRegister;
        private Label _lblError;
        
        // Light Theme Colors
        private readonly Color _bgWhite = Color.White;
        private readonly Color _inputBg = Color.FromArgb(245, 246, 250);
        private readonly Color _primaryPurple = LokalUi.Primary;
        private readonly Color _textDark = Color.FromArgb(30, 30, 30);
        private readonly Color _textGrey = Color.FromArgb(120, 120, 120);
        private readonly Color _borderGrey = Color.FromArgb(220, 220, 220);
        private readonly Color _errorRed = Color.FromArgb(220, 53, 69);

        public RegisterForm(ThisAddIn addIn)
        {
            _addIn = addIn;
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = "Create an account";
            this.Size = new Size(950, 600);
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
            mainSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420F)); 
            mainSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));  
            this.Controls.Add(mainSplit);

            // --- Left Panel (Placeholder / Image) ---
            var pnlLeft = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Margin = new Padding(0) };
            try {
                pnlLeft.BackgroundImage = Image.FromFile(@"c:\xampp\htdocs\LOKAL-ThesisSys\assets\bg.png");
                pnlLeft.BackgroundImageLayout = ImageLayout.Stretch;
            } catch { }
            mainSplit.Controls.Add(pnlLeft, 0, 0);

            var leftCenterTlp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = Color.Transparent };
            leftCenterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            leftCenterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            leftCenterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            leftCenterTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            leftCenterTlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            leftCenterTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            pnlLeft.Controls.Add(leftCenterTlp);

            var pnlPlaceholderBox = new Panel { Size = new Size(300, 150), BackColor = Color.Transparent, Margin = new Padding(0) };
            var lblPlaceholder = new Label { Text = "LOKAL", ForeColor = Color.White, Font = new Font("Segoe UI", 40f, FontStyle.Bold), AutoSize = false, Dock = DockStyle.Top, Height = 80, TextAlign = ContentAlignment.MiddleCenter };
            var lblPlaceholderSub = new Label { Text = "Icon Placeholder", ForeColor = Color.White, Font = new Font("Segoe UI", 12f), AutoSize = false, Dock = DockStyle.Bottom, Height = 40, TextAlign = ContentAlignment.MiddleCenter };
            pnlPlaceholderBox.Controls.Add(lblPlaceholderSub);
            pnlPlaceholderBox.Controls.Add(lblPlaceholder);
            leftCenterTlp.Controls.Add(pnlPlaceholderBox, 1, 1);


            // --- Right Panel (White Form) ---
            var pnlRight = new Panel { Dock = DockStyle.Fill, BackColor = _bgWhite, Margin = new Padding(0) };
            mainSplit.Controls.Add(pnlRight, 1, 0);

            var rightCenterTlp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = Color.Transparent };
            rightCenterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rightCenterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rightCenterTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            rightCenterTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            rightCenterTlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightCenterTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            pnlRight.Controls.Add(rightCenterTlp);

            var flowForm = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = _bgWhite,
                Margin = new Padding(0),
                WrapContents = false
            };
            rightCenterTlp.Controls.Add(flowForm, 1, 1);

            int inputWidth = 340;

            // Header
            var lblTitle = new Label { Text = "Create an account", Font = new Font("Segoe UI", 26f, FontStyle.Bold), ForeColor = _textDark, AutoSize = true, Margin = new Padding(0, 0, 0, 5) };
            flowForm.Controls.Add(lblTitle);
            
            var lblSub = new Label { Text = "Join LOKAL to manage your classes", Font = new Font("Segoe UI", 11f), ForeColor = _textGrey, AutoSize = true, Margin = new Padding(5, 0, 0, 20) };
            flowForm.Controls.Add(lblSub);

            // Display Name
            var lblName = new Label { Text = "Display Name", ForeColor = _textDark, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), AutoSize = true, Margin = new Padding(5, 0, 0, 5) };
            flowForm.Controls.Add(lblName);

            var pnlName = new Panel { Size = new Size(inputWidth, 42), BackColor = _inputBg, Margin = new Padding(0, 0, 0, 15) };
            pnlName.Paint += DrawRoundedBorder;
            _txtDisplayName = new TextBox { Location = new Point(15, 11), Size = new Size(inputWidth - 30, 20), BorderStyle = BorderStyle.None, BackColor = _inputBg, ForeColor = _textDark, Font = new Font("Segoe UI", 11f) };
            pnlName.Controls.Add(_txtDisplayName);
            flowForm.Controls.Add(pnlName);

            // Username
            var lblUser = new Label { Text = "Username", ForeColor = _textDark, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), AutoSize = true, Margin = new Padding(5, 0, 0, 5) };
            flowForm.Controls.Add(lblUser);

            var pnlUser = new Panel { Size = new Size(inputWidth, 42), BackColor = _inputBg, Margin = new Padding(0, 0, 0, 15) };
            pnlUser.Paint += DrawRoundedBorder;
            _txtUsername = new TextBox { Location = new Point(15, 11), Size = new Size(inputWidth - 30, 20), BorderStyle = BorderStyle.None, BackColor = _inputBg, ForeColor = _textDark, Font = new Font("Segoe UI", 11f) };
            pnlUser.Controls.Add(_txtUsername);
            flowForm.Controls.Add(pnlUser);

            // Email
            var lblEmail = new Label { Text = "Email", ForeColor = _textDark, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), AutoSize = true, Margin = new Padding(5, 0, 0, 5) };
            flowForm.Controls.Add(lblEmail);

            var pnlEmail = new Panel { Size = new Size(inputWidth, 42), BackColor = _inputBg, Margin = new Padding(0, 0, 0, 15) };
            pnlEmail.Paint += DrawRoundedBorder;
            _txtEmail = new TextBox { Location = new Point(15, 11), Size = new Size(inputWidth - 30, 20), BorderStyle = BorderStyle.None, BackColor = _inputBg, ForeColor = _textDark, Font = new Font("Segoe UI", 11f) };
            pnlEmail.Controls.Add(_txtEmail);
            flowForm.Controls.Add(pnlEmail);

            // Password
            var lblPass = new Label { Text = "Password", ForeColor = _textDark, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), AutoSize = true, Margin = new Padding(5, 0, 0, 5) };
            flowForm.Controls.Add(lblPass);

            var pnlPass = new Panel { Size = new Size(inputWidth, 42), BackColor = _inputBg, Margin = new Padding(0, 0, 0, 10) };
            pnlPass.Paint += DrawRoundedBorder;
            _txtPassword = new TextBox { Location = new Point(15, 11), Size = new Size(inputWidth - 80, 20), BorderStyle = BorderStyle.None, BackColor = _inputBg, ForeColor = _textDark, Font = new Font("Segoe UI", 11f), UseSystemPasswordChar = true };
            pnlPass.Controls.Add(_txtPassword);

            var lblShowPass = new Label { Text = "Show", Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = _textGrey, AutoSize = true, Cursor = Cursors.Hand, Location = new Point(inputWidth - 55, 12) };
            lblShowPass.Click += (s, e) => {
                _txtPassword.UseSystemPasswordChar = !_txtPassword.UseSystemPasswordChar;
                lblShowPass.Text = _txtPassword.UseSystemPasswordChar ? "Show" : "Hide";
            };
            pnlPass.Controls.Add(lblShowPass);
            flowForm.Controls.Add(pnlPass);

            // Error Label
            _lblError = new Label { Text = "Invalid input", ForeColor = _errorRed, Font = new Font("Segoe UI", 9.5f), AutoSize = true, Margin = new Padding(5, 0, 0, 10), Visible = false };
            flowForm.Controls.Add(_lblError);

            // Register Button
            _btnRegister = new Button { Text = "Create account", BackColor = _primaryPurple, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(inputWidth, 50), Font = new Font("Segoe UI", 12f, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(0, 5, 0, 25) };
            _btnRegister.FlatAppearance.BorderSize = 0;
            _btnRegister.Click += BtnRegister_Click;
            _btnRegister.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var p = new GraphicsPath(); int r = 8;
                p.AddArc(0, 0, r*2, r*2, 180, 90); p.AddArc(_btnRegister.Width - r*2, 0, r*2, r*2, 270, 90);
                p.AddArc(_btnRegister.Width - r*2, _btnRegister.Height - r*2, r*2, r*2, 0, 90); p.AddArc(0, _btnRegister.Height - r*2, r*2, r*2, 90, 90);
                p.CloseAllFigures(); _btnRegister.Region = new Region(p);
            };
            flowForm.Controls.Add(_btnRegister);

            // Sign In Link
            var pnlFooter = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            var lblHasAccount = new Label { Text = "Already have an account?", ForeColor = _textGrey, Font = new Font("Segoe UI", 10f), AutoSize = true, Margin = new Padding(0, 0, 5, 0) };
            var lnkSignIn = new LinkLabel { Text = "Log in", Font = new Font("Segoe UI", 10f, FontStyle.Bold), LinkColor = _primaryPurple, ActiveLinkColor = _textDark, AutoSize = true, Margin = new Padding(0), LinkBehavior = LinkBehavior.HoverUnderline };
            lnkSignIn.LinkClicked += (s, e) => {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            pnlFooter.Controls.Add(lblHasAccount);
            pnlFooter.Controls.Add(lnkSignIn);
            
            var footerWrapper = new Panel { Size = new Size(inputWidth, 30), BackColor = _bgWhite, Margin = new Padding(0) };
            footerWrapper.Controls.Add(pnlFooter);
            pnlFooter.Location = new Point((inputWidth - pnlFooter.PreferredSize.Width) / 2, 0);
            flowForm.Controls.Add(footerWrapper);
            
            this.AcceptButton = _btnRegister;
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

        private async void BtnRegister_Click(object sender, EventArgs e)
        {
            string name = _txtDisplayName.Text.Trim(); string user = _txtUsername.Text.Trim();
            string email = _txtEmail.Text.Trim(); string pass = _txtPassword.Text;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)) { ShowError("Please fill in all required fields."); return; }
            _btnRegister.Enabled = false; _btnRegister.Text = "Creating account..."; _lblError.Visible = false;
            try {
                var result = await _addIn.ApiClient.RegisterAsync(user, email, pass, name);
                Properties.Settings.Default.AuthToken = result.Token;
                Properties.Settings.Default.TeacherDisplayName = result.Teacher?.DisplayName ?? user;
                Properties.Settings.Default.TeacherEmail = result.Teacher?.Email ?? email;
                Properties.Settings.Default.Save();
                this.DialogResult = DialogResult.OK; this.Close();
            } catch (Exception ex) { ShowError(ex.Message); } finally { _btnRegister.Enabled = true; _btnRegister.Text = "Create account"; }
        }

        private void ShowError(string msg) { _lblError.Text = msg; _lblError.Visible = true; }
    }
}
