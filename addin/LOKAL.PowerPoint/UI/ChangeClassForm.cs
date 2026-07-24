using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LOKAL.PowerPoint.UI
{
    public class ChangeClassForm : Form
    {
        private readonly ThisAddIn _addIn;
        private FlowLayoutPanel _flowLayout;
        private Button _startBtn;
        
        private Class _selectedClass = null;
        private bool _isNewClassSelected = true; // Default
        
        private List<ClassCard> _cards = new List<ClassCard>();

        public string SelectedClassCode => _selectedClass?.Code;
        public long? SelectedClassId => _selectedClass?.Id;
        public bool CreateNewClass => _isNewClassSelected;

        public ChangeClassForm(ThisAddIn addIn)
        {
            _addIn = addIn;
            InitializeUI();
            LoadClassesAsync();
        }

        private void InitializeUI()
        {
            this.Size = new Size(600, 350);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.White;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;

            // Rounded corners
            this.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var p = new GraphicsPath();
                int r = 16;
                p.AddArc(0, 0, r, r, 180, 90);
                p.AddArc(this.Width - r, 0, r, r, 270, 90);
                p.AddArc(this.Width - r, this.Height - r, r, r, 0, 90);
                p.AddArc(0, this.Height - r, r, r, 90, 90);
                p.CloseAllFigures();
                this.Region = new Region(p);

                // Add drop shadow effect using border color for now
                using (var pen = new Pen(Color.FromArgb(220, 220, 230), 2))
                {
                    e.Graphics.DrawPath(pen, p);
                }
            };

            var titleLabel = new Label
            {
                Text = "Choose a class:",
                Font = new Font("Segoe UI", 11f),
                ForeColor = Color.FromArgb(50, 50, 50),
                Location = new Point(30, 25),
                AutoSize = true
            };
            this.Controls.Add(titleLabel);

            _flowLayout = new FlowLayoutPanel
            {
                Location = new Point(30, 60),
                Size = new Size(540, 180),
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight
            };
            this.Controls.Add(_flowLayout);

            _startBtn = new Button
            {
                Text = "Start new class",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = LokalUi.Primary,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(160, 40),
                Cursor = Cursors.Hand,
                Location = new Point((this.Width - 160) / 2, 270)
            };
            _startBtn.FlatAppearance.BorderSize = 0;
            _startBtn.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var p = new GraphicsPath();
                int r = _startBtn.Height;
                p.AddArc(0, 0, r, r, 90, 180);
                p.AddArc(_startBtn.Width - r, 0, r, r, 270, 180);
                p.CloseAllFigures();
                _startBtn.Region = new Region(p);
            };
            _startBtn.Click += async (s, e) => {
                _startBtn.Enabled = false;
                _startBtn.Text = "Starting...";
                
                if (_isNewClassSelected)
                {
                    try
                    {
                        var randomCode = GenerateRandomCode();
                        var newCls = await _addIn.ApiClient.CreateClassAsync("New Class", randomCode, "#0B1F1C");
                        _selectedClass = newCls;
                        this.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to create class: " + ex.Message);
                        _startBtn.Enabled = true;
                        _startBtn.Text = "Start new class";
                        return;
                    }
                }
                else
                {
                    this.DialogResult = DialogResult.OK;
                }
                this.Close();
            };
            this.Controls.Add(_startBtn);

            // Close button (X) top right
            var closeBtn = new Label
            {
                Text = "✕",
                Font = new Font("Segoe UI", 12f),
                ForeColor = Color.Gray,
                Location = new Point(560, 15),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(closeBtn);
        }

        private async void LoadClassesAsync()
        {
            _flowLayout.Controls.Clear();
            _cards.Clear();

            // Add "New public class" card
            var newClassCard = new ClassCard(null, true);
            newClassCard.Click += Card_Click;
            _flowLayout.Controls.Add(newClassCard);
            _cards.Add(newClassCard);
            newClassCard.SetSelected(true); // Default selected

            try
            {
                var classes = await _addIn.ApiClient.GetClassesAsync();
                foreach (var cls in classes)
                {
                    var card = new ClassCard(cls, false);
                    card.Click += Card_Click;
                    _flowLayout.Controls.Add(card);
                    _cards.Add(card);
                }
            }
            catch { }
        }

        private void Card_Click(object sender, EventArgs e)
        {
            var selectedCard = sender as ClassCard;
            if (selectedCard == null) return;

            foreach (var card in _cards)
            {
                card.SetSelected(false);
            }
            selectedCard.SetSelected(true);

            if (selectedCard.IsNewClass)
            {
                _isNewClassSelected = true;
                _selectedClass = null;
                _startBtn.Text = "Start new class";
            }
            else
            {
                _isNewClassSelected = false;
                _selectedClass = selectedCard.ClassData;
                _startBtn.Text = "Start class";
            }
        }

        private string GenerateRandomCode()
        {
            var rand = new Random();
            return rand.Next(10000, 99999).ToString();
        }

        private class ClassCard : Panel
        {
            public bool IsNewClass { get; }
            public Class ClassData { get; }
            private bool _isSelected = false;

            public ClassCard(Class cls, bool isNewClass)
            {
                IsNewClass = isNewClass;
                ClassData = cls;
                
                this.Size = new Size(180, 100);
                this.Margin = new Padding(10);
                this.Cursor = Cursors.Hand;
                this.DoubleBuffered = true;

                if (IsNewClass)
                {
                    this.BackColor = Color.FromArgb(240, 255, 244); // light green bg
                    
                    var lbl1 = new Label { Text = "New public class", Font = new Font("Segoe UI", 10f), ForeColor = Color.FromArgb(50, 50, 50), AutoSize = true, Location = new Point(20, 25) };
                    lbl1.Click += (s, e) => this.OnClick(e);
                    
                    var badge = new Label { Text = "RANDOM CODE", Font = new Font("Segoe UI", 8f, FontStyle.Bold), ForeColor = Color.FromArgb(34, 139, 34), BackColor = Color.FromArgb(200, 240, 210), AutoSize = true, Location = new Point(20, 55) };
                    badge.Click += (s, e) => this.OnClick(e);
                    
                    this.Controls.Add(lbl1);
                    this.Controls.Add(badge);
                }
                else
                {
                    this.BackColor = Color.White;
                    
                    var lbl1 = new Label { Text = cls.Name, Font = new Font("Segoe UI", 10f), ForeColor = Color.FromArgb(50, 50, 50), AutoSize = true, Location = new Point(20, 20) };
                    lbl1.Click += (s, e) => this.OnClick(e);
                    
                    var avatarContainer = new Panel { Size = new Size(24, 24), Location = new Point(140, 20) };
                    avatarContainer.Paint += (s, e) => {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.FillEllipse(new SolidBrush(LokalUi.Primary), 0, 0, 24, 24);
                        var font = new Font("Segoe UI", 10f, FontStyle.Bold);
                        var letter = cls.Name.Substring(0, 1).ToUpper();
                        var size = e.Graphics.MeasureString(letter, font);
                        e.Graphics.DrawString(letter, font, Brushes.White, (24 - size.Width) / 2, (24 - size.Height) / 2);
                    };
                    avatarContainer.Click += (s, e) => this.OnClick(e);
                    
                    var codeBadge = new Label { Text = cls.Code, Font = new Font("Segoe UI", 8f, FontStyle.Bold), ForeColor = LokalUi.Primary, BackColor = LokalUi.PrimaryLight, AutoSize = true, Location = new Point(20, 60) };
                    codeBadge.Click += (s, e) => this.OnClick(e);
                    
                    var pCount = new Label { Text = $"{cls.ParticipantCount} participants", Font = new Font("Segoe UI", 8f), ForeColor = Color.Gray, AutoSize = true, Location = new Point(80, 60) };
                    pCount.Click += (s, e) => this.OnClick(e);
                    
                    this.Controls.Add(lbl1);
                    this.Controls.Add(avatarContainer);
                    this.Controls.Add(codeBadge);
                    this.Controls.Add(pCount);
                }
            }

            public void SetSelected(bool selected)
            {
                _isSelected = selected;
                this.Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                
                var path = new GraphicsPath();
                int r = 12;
                path.AddArc(0, 0, r, r, 180, 90);
                path.AddArc(this.Width - r - 1, 0, r, r, 270, 90);
                path.AddArc(this.Width - r - 1, this.Height - r - 1, r, r, 0, 90);
                path.AddArc(0, this.Height - r - 1, r, r, 90, 90);
                path.CloseAllFigures();
                this.Region = new Region(path);

                Color borderColor = Color.FromArgb(220, 220, 230); // Default grey
                int borderWidth = 1;

                if (_isSelected)
                {
                    borderColor = IsNewClass ? Color.FromArgb(34, 139, 34) : LokalUi.Primary;
                    borderWidth = 2;
                }

                using (var pen = new Pen(borderColor, borderWidth))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
        }
    }
}
