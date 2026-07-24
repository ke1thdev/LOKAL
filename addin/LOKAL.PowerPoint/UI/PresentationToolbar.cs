using System;
using System.Drawing;
using System.Windows.Forms;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// Presentation mode toolbar — appears at the bottom of the slideshow.
    /// Contains buttons: Index, Previous, Next, Cursor, Laser, Pen, Highlighter,
    /// Eraser, Shapes, Text, Whiteboard, Draggable Objects, Timer, Quick Poll,
    /// Name Picker, Leader Board, Show/Hide Toolbar, Exit Slideshow.
    /// Matches ClassPoint's presentation toolbar exactly.
    /// </summary>
    public partial class PresentationToolbar : UserControl
    {
        private readonly ThisAddIn _addIn;
        private readonly Color _bgColor = Color.FromArgb(30, 27, 75); // #1e1b4b
        private readonly Color _btnHover = Color.FromArgb(67, 56, 202); // #4338ca
        private readonly Color _accentOrange = Color.FromArgb(249, 115, 22); // #f97316
        private Label _slideLabel;
        private Label _classCodeLabel;

        // Toolbar buttons
        private readonly string[] _buttonNames = {
            "Index", "Previous", "Next", "|",
            "Cursor", "Laser", "Pen", "Highlighter", "Eraser", "|",
            "Shapes", "Text", "Whiteboard", "|",
            "Draggable", "Timer", "Quick Poll", "Name Picker", "Leader Board", "|",
            "☰", "✕"
        };

        private readonly string[] _buttonIcons = {
            "▦", "◀", "▶", "",
            "↖", "◉", "✏", "🖌", "⌫", "",
            "□", "A", "🗒", "",
            "✋", "⏱", "📊", "🎯", "🏆", "",
            "☰", "✕"
        };

        public PresentationToolbar(ThisAddIn addIn)
        {
            _addIn = addIn;
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.BackColor = _bgColor;
            this.Dock = DockStyle.Fill;
            this.Height = 48;
            this.Padding = new Padding(8, 4, 8, 4);

            // Main layout
            var mainPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            // Add toolbar buttons
            for (int i = 0; i < _buttonNames.Length; i++)
            {
                if (_buttonNames[i] == "|")
                {
                    // Separator
                    var sep = new Panel
                    {
                        Width = 1,
                        Height = 32,
                        BackColor = Color.FromArgb(60, 255, 255, 255),
                        Margin = new Padding(6, 4, 6, 4)
                    };
                    mainPanel.Controls.Add(sep);
                    continue;
                }

                var btn = CreateToolbarButton(_buttonIcons[i], _buttonNames[i], i);
                mainPanel.Controls.Add(btn);
            }

            // Class code badge (top-right position overlay)
            _classCodeLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = LokalUi.Primary,
                AutoSize = true,
                Padding = new Padding(8, 3, 8, 3),
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            // Slide counter
            _slideLabel = new Label
            {
                Text = "Slide 1 / 1",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(180, 255, 255, 255),
                BackColor = Color.Transparent,
                AutoSize = true,
                Padding = new Padding(12, 8, 12, 8),
                Dock = DockStyle.Right
            };

            this.Controls.Add(mainPanel);
            this.Controls.Add(_slideLabel);
            this.Controls.Add(_classCodeLabel);
        }

        private Button CreateToolbarButton(string icon, string tooltip, int index)
        {
            var btn = new Button
            {
                Text = icon,
                Font = new Font("Segoe UI Emoji", 11f),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(36, 36),
                Margin = new Padding(1),
                Cursor = Cursors.Hand,
                Tag = tooltip
            };

            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = _btnHover;
            btn.FlatAppearance.MouseDownBackColor = LokalUi.PrimaryLight;

            if (tooltip == "Leader Board" && LokalUi.TrophyImage != null)
            {
                btn.Text = string.Empty;
                btn.BackgroundImage = LokalUi.TrophyImage;
                btn.BackgroundImageLayout = ImageLayout.Zoom;
                btn.Padding = new Padding(6);
            }

            // Add tooltip
            var tt = new ToolTip();
            tt.SetToolTip(btn, tooltip);

            // Click handler
            btn.Click += (s, e) => HandleToolbarClick(tooltip);

            return btn;
        }

        private void HandleToolbarClick(string action)
        {
            switch (action)
            {
                case "Index":
                    // Show slide overview
                    break;
                case "Previous":
                    try { _addIn.Application.SlideShowWindows[1].View.Previous(); } catch { }
                    break;
                case "Next":
                    try { _addIn.Application.SlideShowWindows[1].View.Next(); } catch { }
                    break;
                case "Cursor":
                    try { _addIn.Application.SlideShowWindows[1].View.PointerType =
                        Microsoft.Office.Interop.PowerPoint.PpSlideShowPointerType.ppSlideShowPointerArrow; } catch { }
                    break;
                case "Laser":
                    try { _addIn.Application.SlideShowWindows[1].View.PointerType =
                        Microsoft.Office.Interop.PowerPoint.PpSlideShowPointerType.ppSlideShowPointerAutoArrow; } catch { }
                    break;
                case "Pen":
                    try { _addIn.Application.SlideShowWindows[1].View.PointerType =
                        Microsoft.Office.Interop.PowerPoint.PpSlideShowPointerType.ppSlideShowPointerPen; } catch { }
                    break;
                case "Highlighter":
                    try {
                        _addIn.Application.SlideShowWindows[1].View.PointerType =
                            Microsoft.Office.Interop.PowerPoint.PpSlideShowPointerType.ppSlideShowPointerPen;
                        _addIn.Application.SlideShowWindows[1].View.PointerColor.RGB = 
                            ColorTranslator.ToOle(Color.FromArgb(255, 255, 0));
                    } catch { }
                    break;
                case "Eraser":
                    try { _addIn.Application.SlideShowWindows[1].View.EraseDrawing(); } catch { }
                    break;
                case "Timer":
                    using (var dlg = new TimerDialog()) { dlg.ShowDialog(); }
                    break;
                case "Quick Poll":
                    using (var dlg = new QuickPollDialog(_addIn)) { dlg.ShowDialog(); }
                    break;
                case "Name Picker":
                    using (var dlg = new NamePickerDialog(_addIn)) { dlg.ShowDialog(); }
                    break;
                case "Leader Board":
                    using (var dlg = new LeaderboardDialog(_addIn)) { dlg.ShowDialog(); }
                    break;
                case "☰":
                    // Toggle toolbar visibility (minimize)
                    break;
                case "✕":
                    // Exit slideshow
                    try { _addIn.Application.SlideShowWindows[1].View.Exit(); } catch { }
                    break;
            }
        }

        public void UpdateSlideInfo(int current, int total)
        {
            if (_slideLabel != null && !_slideLabel.IsDisposed)
            {
                _slideLabel.Invoke((Action)(() =>
                {
                    _slideLabel.Text = $"Slide {current} / {total}";
                }));
            }
        }

        public void SetClassCode(string code)
        {
            if (_classCodeLabel != null && !_classCodeLabel.IsDisposed)
            {
                _classCodeLabel.Invoke((Action)(() =>
                {
                    _classCodeLabel.Text = code;
                    _classCodeLabel.Visible = !string.IsNullOrEmpty(code);
                }));
            }
        }
    }
}
