using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using PPT = Microsoft.Office.Interop.PowerPoint;

namespace LOKAL.PowerPoint
{
    /// <summary>ClassPoint-style presentation toolbar with working annotation tools.</summary>
    public sealed class SlideshowToolbarForm : Form
    {
        private readonly ThisAddIn _addIn;
        private readonly Dictionary<string, ToolButton> _buttons = new Dictionary<string, ToolButton>();
        private AnnotationOverlayForm _annotations;
        private ToolPaletteForm _palette;
        private string _active = "Cursor";
        private bool _toolbarHidden;
        private FlowLayoutPanel _leftZone, _centerZone, _rightZone;
        internal const int BarHeight = 78;

        private static readonly ToolSpec[] Specs =
        {
            new ToolSpec("logo", "LOKAL", 0), new ToolSpec("grid", "Slide Index", 1),
            new ToolSpec("prev", "Previous Slide", 1), new ToolSpec("next", "Next Slide", 1),
            new ToolSpec("cursor", "Cursor", 1), new ToolSpec("laser", "Laser Pointer", 1),
            new ToolSpec("|", "", 1), new ToolSpec("pen", "Pen", 1),
            new ToolSpec("highlighter", "Highlighter", 1), new ToolSpec("eraser", "Eraser", 1),
            new ToolSpec("shapes", "Shapes", 1), new ToolSpec("text", "Text", 1),
            new ToolSpec("board", "Whiteboard", 1), new ToolSpec("hand", "Select Objects", 1),
            new ToolSpec("|", "", 1),
            new ToolSpec("timer", "Timer", 1), new ToolSpec("poll", "Quick Poll", 1),
            new ToolSpec("wheel", "Name Picker", 1), new ToolSpec("trophy", "Leaderboard", 1),
            new ToolSpec("eye", "Hide toolbar", 2), new ToolSpec("exit", "Exit Slideshow", 2)
        };

        public SlideshowToolbarForm(ThisAddIn addIn)
        {
            _addIn = addIn;
            BuildUi();
            Shown += (s, e) => EnsureOverlay();
        }

        private void BuildUi()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = Color.White; Height = BarHeight;
            DoubleBuffered = true;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.White };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _leftZone = Zone(); _centerZone = Zone(); _rightZone = Zone(); _centerZone.Anchor = AnchorStyles.None;
            foreach (ToolSpec spec in Specs)
            {
                FlowLayoutPanel zone = spec.Zone == 0 ? _leftZone : spec.Zone == 1 ? _centerZone : _rightZone;
                if (spec.Icon == "|")
                {
                    zone.Controls.Add(new Panel { Width = 1, Height = 34, BackColor = Color.FromArgb(216, 218, 227), Margin = new Padding(7, 10, 7, 10) });
                    continue;
                }
                ToolButton button = new ToolButton(spec.Icon, spec.Name, () => _active == spec.Name, () => _toolbarHidden);
                button.Click += (s, e) => HandleClick(spec.Name, button);
                zone.Controls.Add(button); _buttons[spec.Name] = button;
            }
            layout.Controls.Add(_leftZone, 0, 0); layout.Controls.Add(_centerZone, 1, 0); layout.Controls.Add(_rightZone, 2, 0);
            Controls.Add(layout);
            Paint += (s, e) => { using (var p = new Pen(Color.FromArgb(228, 230, 238))) e.Graphics.DrawLine(p, 0, 0, Width, 0); };
        }

        private static FlowLayoutPanel Zone()
        {
            return new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent,
                Margin = new Padding(7, 0, 7, 0), Padding = new Padding(0, 5, 0, 0) };
        }

        public void PositionOnSlideshow()
        {
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int width = _toolbarHidden ? Math.Max(116, _rightZone == null ? 116 : _rightZone.PreferredSize.Width + 12) : screen.Width;
            Bounds = new Rectangle(_toolbarHidden ? screen.Right - width : screen.Left, screen.Bottom - BarHeight, width, BarHeight);
            if (_annotations != null && !_annotations.IsDisposed) _annotations.PositionOnSlideshow();
        }

        private void EnsureOverlay()
        {
            if (_annotations == null || _annotations.IsDisposed) _annotations = new AnnotationOverlayForm(_addIn);
            _annotations.PositionOnSlideshow();
            if (!_annotations.Visible) _annotations.Show();
            BringToFront();
        }

        private void SetActive(string name)
        {
            _active = name;
            foreach (ToolButton button in _buttons.Values) button.Invalidate();
        }

        private void HandleClick(string action, ToolButton source)
        {
            try
            {
                var windows = _addIn.Application.SlideShowWindows;
                if (windows.Count == 0) return;
                PPT.SlideShowView view = windows[1].View;
                switch (action)
                {
                    case "LOKAL": break;
                    case "Slide Index":
                        SetActive("Slide Index");
                        _annotations?.Hide();
                        using (var dialog = new SlideIndexDialog(_addIn)) dialog.ShowDialog(this);
                        EnsureOverlay(); break;
                    case "Previous Slide": SetActive("Previous Slide"); view.Previous(); break;
                    case "Next Slide": SetActive("Next Slide"); view.Next(); break;
                    case "Cursor": Activate(view, AnnotationMode.None, "Cursor"); break;
                    case "Laser Pointer": Activate(view, AnnotationMode.Laser, "Laser Pointer"); break;
                    case "Pen": Activate(view, AnnotationMode.Pen, "Pen"); ShowPalette("Pen", source); break;
                    case "Highlighter": Activate(view, AnnotationMode.Highlighter, "Highlighter"); ShowPalette("Highlighter", source); break;
                    case "Eraser": Activate(view, AnnotationMode.Eraser, "Eraser"); break;
                    case "Shapes": Activate(view, AnnotationMode.Shape, "Shapes"); ShowPalette("Shapes", source); break;
                    case "Text": Activate(view, AnnotationMode.Text, "Text"); ShowPalette("Text", source); break;
                    case "Whiteboard": EnsureOverlay(); _annotations.ToggleWhiteboard(); _annotations.Mode = AnnotationMode.None; SetActive("Whiteboard"); break;
                    case "Select Objects": Activate(view, AnnotationMode.Select, "Select Objects"); break;
                    case "Timer": SetActive("Timer"); using (var dialog = new TimerDialog()) ShowUtility(dialog); break;
                    case "Quick Poll": SetActive("Quick Poll"); using (var dialog = new QuickPollDialog(_addIn)) ShowUtility(dialog); break;
                    case "Name Picker": SetActive("Name Picker"); using (var dialog = new NamePickerDialog(_addIn)) ShowUtility(dialog); break;
                    case "Leaderboard": SetActive("Leaderboard"); using (var dialog = new LeaderboardDialog(_addIn)) ShowUtility(dialog); break;
                    case "Hide toolbar": ToggleToolbar(source); break;
                    case "Exit Slideshow": view.Exit(); break;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Toolbar action failed: " + ex.Message); }
        }

        private void Activate(PPT.SlideShowView view, AnnotationMode mode, string name)
        {
            view.PointerType = PPT.PpSlideShowPointerType.ppSlideShowPointerArrow;
            EnsureOverlay(); _annotations.Mode = mode; SetActive(name);
        }

        private void ShowUtility(Form dialog)
        {
            if (_annotations != null) _annotations.Hide();
            Visible = false; dialog.ShowDialog(); Visible = true; EnsureOverlay();
        }

        private void ShowPalette(string tool, Control source)
        {
            try { _palette?.Close(); } catch { }
            _palette = new ToolPaletteForm(tool, _annotations);
            Point point = source.PointToScreen(Point.Empty);
            _palette.StartPosition = FormStartPosition.Manual;
            _palette.Location = new Point(Math.Max(8, point.X - (_palette.Width - source.Width) / 2), point.Y - _palette.Height - 8);
            _palette.Show(this); BringToFront();
        }

        private void ToggleToolbar(ToolButton source)
        {
            _toolbarHidden = !_toolbarHidden;
            _leftZone.Visible = !_toolbarHidden; _centerZone.Visible = !_toolbarHidden;
            source.ToolName = _toolbarHidden ? "Show toolbar" : "Hide toolbar";
            SetActive(_toolbarHidden ? "Hide toolbar" : "Cursor");
            PositionOnSlideshow(); source.Invalidate();
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { _palette?.Close(); } catch { } try { _annotations?.Close(); } catch { } }
            base.Dispose(disposing);
        }

        private sealed class ToolSpec
        {
            public readonly string Icon, Name; public readonly int Zone;
            public ToolSpec(string icon, string name, int zone) { Icon = icon; Name = name; Zone = zone; }
        }

        private sealed class ToolButton : Control
        {
            private static readonly object AssetSync = new object();
            private static readonly Dictionary<string, Image> AssetImages = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
            private readonly string _icon; private readonly Func<bool> _active, _hidden; private readonly ToolTip _tip;
            private bool _hover; private string _toolName;
            public string ToolName { get { return _toolName; } set { _toolName = value; if (_tip != null) _tip.SetToolTip(this, value); } }
            public ToolButton(string icon, string name, Func<bool> active, Func<bool> hidden)
            {
                _icon = icon; _active = active; _hidden = hidden;
                SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                BackColor = Color.White;
                Size = new Size(name == "LOKAL" ? 58 : 56, 68); Margin = new Padding(2, 0, 2, 0);
                Cursor = Cursors.Hand; DoubleBuffered = true;
                _tip = new ToolTip { InitialDelay = 180, ReshowDelay = 80, AutoPopDelay = 4000 };
                ToolName = name;
            }
            protected override void OnMouseEnter(EventArgs e)
            {
                _hover = true; Invalidate();
                if (!string.IsNullOrWhiteSpace(ToolName)) _tip.Show(ToolName, this, Width / 2, -34, 3500);
                base.OnMouseEnter(e);
            }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; _tip.Hide(this); Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Color.White);
                if (_hover) using (var brush = new SolidBrush(Color.FromArgb(249, 250, 255))) e.Graphics.FillRectangle(brush, ClientRectangle);
                if (_active()) using (var pen = new Pen(LokalUi.Primary, 4f)) e.Graphics.DrawLine(pen, 0, 1, Width, 1);
                DrawIcon(e.Graphics, new Rectangle(0, 6, Width, Height - 6), _icon, _hidden());
            }
            private static void DrawIcon(Graphics g, Rectangle r, string icon, bool hidden)
            {
                float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
                g.TranslateTransform(cx, cy); g.ScaleTransform(1.18f, 1.18f);
                if (TryDrawAssetIcon(g, icon, hidden)) { g.ResetTransform(); return; }

                Color ink = Color.FromArgb(59, 61, 74);
                using (var pen = new Pen(ink, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                using (var brush = new SolidBrush(ink))
                {
                    if (icon == "logo")
                    {
                        using (var b = new SolidBrush(Color.FromArgb(9, 121, 107))) g.FillEllipse(b, -21, -21, 42, 42);
                    }
                    else if (icon == "grid") { for (int y = -9; y <= 3; y += 12) for (int x = -9; x <= 3; x += 12) g.DrawRectangle(pen, x, y, 7, 7); }
                    else if (icon == "prev") g.DrawLines(pen, new[] { new Point(5, -10), new Point(-5, 0), new Point(5, 10) });
                    else if (icon == "next") g.DrawLines(pen, new[] { new Point(-5, -10), new Point(5, 0), new Point(-5, 10) });
                    else if (icon == "cursor") g.DrawPolygon(pen, new[] { new Point(-7, -12), new Point(10, 2), new Point(2, 3), new Point(7, 12), new Point(2, 14), new Point(-3, 5), new Point(-8, 10) });
                    else if (icon == "laser") { g.RotateTransform(-38); g.DrawRectangle(pen, -4, -8, 8, 16); using (var red = new SolidBrush(Color.FromArgb(241, 66, 54))) g.FillEllipse(red, -3, 9, 6, 6); g.ResetTransform(); return; }
                    else if (icon == "pen" || icon == "highlighter") { g.RotateTransform(-38); g.DrawRectangle(pen, -4, -11, 8, 18); using (var tip = new SolidBrush(icon == "pen" ? Color.FromArgb(87, 111, 240) : Color.FromArgb(244, 190, 31))) g.FillPolygon(tip, new[] { new Point(-4, 7), new Point(4, 7), new Point(0, 13) }); g.ResetTransform(); return; }
                    else if (icon == "eraser") { g.RotateTransform(-38); g.DrawRectangle(pen, -6, -10, 12, 19); using (var green = new SolidBrush(Color.FromArgb(117, 190, 35))) g.FillRectangle(green, -5, 1, 10, 7); g.ResetTransform(); return; }
                    else if (icon == "shapes") { using (var red = new Pen(Color.Tomato, 2)) g.DrawEllipse(red, -11, -9, 13, 13); g.DrawRectangle(pen, -1, -1, 13, 13); }
                    else if (icon == "text") { pen.DashStyle = DashStyle.Dash; g.DrawRectangle(pen, -12, -12, 24, 24); using (var f = new Font("Segoe UI", 13, FontStyle.Bold)) g.DrawString("A", f, brush, -9, -12); }
                    else if (icon == "board") { g.DrawRectangle(pen, -12, -8, 24, 15); g.DrawLine(pen, -7, 7, -10, 12); g.DrawLine(pen, 7, 7, 10, 12); }
                    else if (icon == "hand") { g.DrawEllipse(pen, -3, -12, 7, 12); g.DrawArc(pen, -10, -5, 20, 19, 340, 200); }
                    else if (icon == "timer") { g.DrawEllipse(pen, -10, -8, 20, 20); g.DrawLine(pen, -3, -12, 3, -12); using (var red = new Pen(Color.Red, 2)) g.DrawLine(red, 0, 2, 5, -3); }
                    else if (icon == "poll") { using (var a = new SolidBrush(Color.FromArgb(155, 206, 40))) g.FillRectangle(a, -10, -1, 5, 12); using (var b = new SolidBrush(Color.FromArgb(95, 95, 99))) g.FillRectangle(b, -2, -7, 5, 18); using (var c = new SolidBrush(Color.FromArgb(155, 206, 40))) g.FillRectangle(c, 6, -4, 5, 15); }
                    else if (icon == "wheel") { g.DrawEllipse(pen, -13, -13, 26, 26); g.DrawEllipse(pen, -3, -3, 6, 6); for(int i=0;i<8;i++){double a=i*Math.PI/4;g.DrawLine(pen,(float)(Math.Cos(a)*5),(float)(Math.Sin(a)*5),(float)(Math.Cos(a)*12),(float)(Math.Sin(a)*12));} }
                    else if (icon == "trophy") { LokalUi.DrawTrophyImage(g, new Rectangle(-15, -15, 30, 30)); }
                    else if (icon == "eye") { g.DrawArc(pen, -12, -7, 24, 15, 200, 140); g.DrawEllipse(pen, -3, -2, 6, 6); if (!hidden) g.DrawLine(pen, -12, -12, 12, 12); }
                    else if (icon == "exit") { g.DrawRectangle(pen, -11, -10, 22, 15); g.DrawLine(pen, 0, 5, 0, 11); g.DrawLine(pen, -5, 11, 5, 11); using (var red = new Pen(Color.FromArgb(225, 72, 74), 2)) { g.DrawLine(red, -4, -7, 4, 1); g.DrawLine(red, -4, 1, 4, -7); } }
                }
                g.ResetTransform();
            }

            private static bool TryDrawAssetIcon(Graphics g, string icon, bool hidden)
            {
                string fileName = GetAssetFileName(icon, hidden);
                if (string.IsNullOrEmpty(fileName)) return false;

                Image image = GetAssetImage(fileName);
                if (image == null) return false;

                int size = icon == "logo" ? 46 : 31;
                InterpolationMode oldInterpolation = g.InterpolationMode;
                PixelOffsetMode oldPixelOffset = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(image, new Rectangle(-size / 2, -size / 2, size, size));
                g.InterpolationMode = oldInterpolation;
                g.PixelOffsetMode = oldPixelOffset;
                return true;
            }

            private static string GetAssetFileName(string icon, bool hidden)
            {
                switch (icon)
                {
                    case "logo": return "android-chrome-192x192.png";
                    case "grid": return "squares.png";
                    case "prev": return "left-arrow.png";
                    case "next": return "right-arrow.png";
                    case "cursor": return "cursor.png";
                    case "laser": return "laser.png";
                    case "pen": return "pen-tool.png";
                    case "highlighter": return "highlighter.png";
                    case "eraser": return "eraser.png";
                    case "shapes": return "shapes.png";
                    case "text": return "font.png";
                    case "board": return "whiteboard.png";
                    case "timer": return "stopwatch.png";
                    case "poll": return "polling.png";
                    case "wheel": return "color-wheel.png";
                    case "trophy": return "trophy.png";
                    case "eye": return hidden ? "eye.png" : "hidden.png";
                    default: return null;
                }
            }

            private static Image GetAssetImage(string fileName)
            {
                lock (AssetSync)
                {
                    Image cached;
                    if (AssetImages.TryGetValue(fileName, out cached)) return cached;

                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                    if (!File.Exists(path)) return null;
                    try
                    {
                        using (Image source = Image.FromFile(path)) cached = new Bitmap(source);
                        AssetImages[fileName] = cached;
                        return cached;
                    }
                    catch { return null; }
                }
            }
            protected override void Dispose(bool disposing) { if (disposing) _tip.Dispose(); base.Dispose(disposing); }
        }
    }
}
