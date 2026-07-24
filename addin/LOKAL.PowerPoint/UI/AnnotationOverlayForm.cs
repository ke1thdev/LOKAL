using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Office = Microsoft.Office.Core;
using PPT = Microsoft.Office.Interop.PowerPoint;

namespace LOKAL.PowerPoint
{
    internal enum AnnotationMode { None, Laser, Pen, Highlighter, Shape, Text, Select, Eraser }
    internal enum AnnotationShape { Rectangle, Ellipse, Triangle, Line, Arrow }

    /// <summary>
    /// Transparent annotation surface placed over the slideshow. Unlike the
    /// native PowerPoint pointer, this provides translucent highlighter ink,
    /// self-erasing laser trails, and editable shape/text objects.
    /// </summary>
    internal sealed class AnnotationOverlayForm : Form
    {
        private readonly ThisAddIn _addIn;
        private readonly Dictionary<int, SlideAnnotations> _slides = new Dictionary<int, SlideAnnotations>();
        private readonly Timer _animation;
        private readonly AnnotationDisplayForm _display;
        private AnnotationMode _mode;
        private AnnotationShape _shape = AnnotationShape.Rectangle;
        private Color _penColor = Color.FromArgb(30, 30, 34);
        private Color _highlightColor = Color.FromArgb(255, 220, 40);
        private Color _shapeColor = Color.FromArgb(32, 34, 42);
        private Color _textColor = Color.FromArgb(25, 25, 30);
        private Color _textBackColor = Color.Transparent;
        private bool _shapeFilled;
        private float _penWidth = 5f;
        private float _highlightWidth = 22f;
        private int _slideIndex;
        private InkStroke _activeStroke;
        private DrawableObject _activeObject;
        private DrawableObject _selected;
        private Point _start;
        private Rectangle _original;
        private bool _resizing;

        internal AnnotationOverlayForm(ThisAddIn addIn)
        {
            _addIn = addIn;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            // A color-keyed transparent form does not receive mouse messages in
            // its transparent pixels. Use a nearly invisible capture window for
            // input, and a separate click-through window for the rendered ink.
            BackColor = Color.Black;
            Opacity = 0.01d;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            Bounds = new Rectangle(screen.Left, screen.Top, screen.Width, Math.Max(200, screen.Height - SlideshowToolbarForm.BarHeight));
            _display = new AnnotationDisplayForm(this) { Bounds = Bounds };
            Shown += (s, e) => ShowDisplaySurface();
            VisibleChanged += (s, e) =>
            {
                if (_display == null || _display.IsDisposed) return;
                if (Visible) ShowDisplaySurface();
                else _display.Hide();
            };
            Invalidated += (s, e) => { if (_display != null && !_display.IsDisposed) _display.RefreshSurface(); };
            _slideIndex = CurrentSlideIndex();
            _animation = new Timer { Interval = 40 };
            _animation.Tick += (s, e) =>
            {
                int current = CurrentSlideIndex();
                if (current != _slideIndex) { _slideIndex = current; _selected = null; Invalidate(); }
                bool removed = false;
                DateTime cutoff = DateTime.UtcNow.AddMilliseconds(-1900);
                foreach (SlideAnnotations annotations in _slides.Values)
                    removed |= annotations.Laser.RemoveAll(x => x.CreatedUtc < cutoff) > 0;
                if (removed) Invalidate();
            };
            _animation.Start();
        }

        internal AnnotationMode Mode
        {
            get { return _mode; }
            set { _mode = value; _selected = null; Cursor = CursorFor(value); Invalidate(); }
        }
        internal AnnotationShape ShapeKind { get { return _shape; } set { _shape = value; } }
        internal Color PenColor { get { return _penColor; } set { _penColor = value; } }
        internal Color HighlightColor { get { return _highlightColor; } set { _highlightColor = value; } }
        internal Color ShapeColor { get { return _shapeColor; } set { _shapeColor = value; } }
        internal bool ShapeFilled { get { return _shapeFilled; } set { _shapeFilled = value; } }
        internal Color TextColor { get { return _textColor; } set { _textColor = value; } }
        internal Color TextBackColor { get { return _textBackColor; } set { _textBackColor = value; } }
        internal float PenWidth { get { return _penWidth; } set { _penWidth = Math.Max(2, value); } }
        internal float HighlightWidth { get { return _highlightWidth; } set { _highlightWidth = Math.Max(10, value); } }

        internal void PositionOnSlideshow()
        {
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            Bounds = new Rectangle(screen.Left, screen.Top, screen.Width, Math.Max(200, screen.Height - SlideshowToolbarForm.BarHeight));
            if (_display != null && !_display.IsDisposed) _display.Bounds = Bounds;
        }

        private void ShowDisplaySurface()
        {
            if (_display == null || _display.IsDisposed) return;
            _display.Bounds = Bounds;
            if (!_display.Visible) _display.Show(this);
            _display.BringToFront();
            _display.RefreshSurface();
        }

        internal void ClearCurrentSlide()
        {
            _slides.Remove(CurrentSlideIndex());
            _selected = null;
            Invalidate();
        }

        private SlideAnnotations CurrentAnnotations()
        {
            int index = CurrentSlideIndex();
            _slideIndex = index;
            SlideAnnotations value;
            if (!_slides.TryGetValue(index, out value))
            {
                value = new SlideAnnotations();
                _slides[index] = value;
            }
            return value;
        }

        private int CurrentSlideIndex()
        {
            try
            {
                var windows = _addIn.Application.SlideShowWindows;
                return windows.Count > 0 ? windows[1].View.Slide.SlideIndex : 1;
            }
            catch { return Math.Max(1, _slideIndex); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Rendering is performed by AnnotationDisplayForm. This window only
            // captures input so pen/highlighter/shape tools work over slideshow.
            e.Graphics.Clear(Color.Black);
        }

        private void RenderAnnotations(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            SlideAnnotations data = CurrentAnnotations();
            if (data.Whiteboard) graphics.FillRectangle(Brushes.White, ClientRectangle);
            foreach (InkStroke stroke in data.Ink) DrawStroke(graphics, stroke);
            foreach (InkStroke stroke in data.Laser) DrawStroke(graphics, stroke);
            foreach (DrawableObject item in data.Objects)
            {
                // Before mouse-up the overlay is the live drawing preview. Once
                // PersistObject creates the real PowerPoint shape, PowerPoint
                // itself renders it in the slideshow. Painting it here as well
                // produces the doubled/offset shapes that were visible locally
                // (the student stream only contained the persisted copy).
                bool drawPreview = item == _activeObject || item.PowerPointShape == null;
                DrawObject(graphics, item, item == _selected, drawPreview);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _start = e.Location;
            SlideAnnotations data = CurrentAnnotations();
            if (_mode == AnnotationMode.Pen || _mode == AnnotationMode.Highlighter || _mode == AnnotationMode.Laser)
            {
                bool laser = _mode == AnnotationMode.Laser;
                _activeStroke = new InkStroke
                {
                    Color = laser ? Color.FromArgb(245, 45, 55) : (_mode == AnnotationMode.Highlighter ? _highlightColor : _penColor),
                    Width = laser ? 5f : (_mode == AnnotationMode.Highlighter ? _highlightWidth : _penWidth),
                    Alpha = _mode == AnnotationMode.Highlighter ? 90 : 255,
                    IsHighlighter = _mode == AnnotationMode.Highlighter,
                    CreatedUtc = DateTime.UtcNow
                };
                _activeStroke.Points.Add(e.Location);
                (laser ? data.Laser : data.Ink).Add(_activeStroke);
            }
            else if (_mode == AnnotationMode.Shape)
            {
                _activeObject = new DrawableObject { Kind = DrawableKind.Shape, Shape = _shape, Color = _shapeColor, Filled = _shapeFilled, Bounds = new Rectangle(e.X, e.Y, 1, 1) };
                data.Objects.Add(_activeObject); _selected = _activeObject;
            }
            else if (_mode == AnnotationMode.Text)
            {
                using (var dialog = new TextEntryDialog(_textColor, _textBackColor))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.Value))
                    {
                        _activeObject = new DrawableObject
                        {
                            Kind = DrawableKind.Text, Text = dialog.Value, Color = _textColor,
                            Background = _textBackColor, Bounds = new Rectangle(e.X, e.Y, 230, 82)
                        };
                        data.Objects.Add(_activeObject); _selected = _activeObject;
                        PersistObject(_activeObject); Invalidate();
                    }
                }
            }
            else if (_mode == AnnotationMode.Select)
            {
                _selected = data.Objects.LastOrDefault(x => Inflate(x.Bounds, 8).Contains(e.Location));
                if (_selected != null)
                {
                    _original = _selected.Bounds;
                    _resizing = ResizeHandle(_selected.Bounds).Contains(e.Location);
                }
                Invalidate();
            }
            else if (_mode == AnnotationMode.Eraser)
            {
                DrawableObject hit = data.Objects.LastOrDefault(x => Inflate(x.Bounds, 12).Contains(e.Location));
                if (hit != null) { data.Objects.Remove(hit); DeletePersistentObject(hit); }
                else
                {
                    InkStroke stroke = data.Ink.LastOrDefault(x => x.Points.Any(p => Distance(p, e.Location) < Math.Max(16, x.Width)));
                    if (stroke != null) data.Ink.Remove(stroke);
                }
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_activeStroke != null)
            {
                if (_activeStroke.Points.Count == 0 || Distance(_activeStroke.Points[_activeStroke.Points.Count - 1], e.Location) > 2)
                    _activeStroke.Points.Add(e.Location);
                Invalidate();
            }
            else if (_activeObject != null && _mode == AnnotationMode.Shape)
            {
                _activeObject.Bounds = Normalize(_start, e.Location); Invalidate();
            }
            else if (_mode == AnnotationMode.Select && _selected != null)
            {
                if (_resizing)
                    _selected.Bounds = new Rectangle(_original.Left, _original.Top,
                        Math.Max(24, _original.Width + e.X - _start.X), Math.Max(24, _original.Height + e.Y - _start.Y));
                else
                    _selected.Bounds = new Rectangle(_original.X + e.X - _start.X, _original.Y + e.Y - _start.Y, _original.Width, _original.Height);
                UpdatePersistentObject(_selected);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _activeStroke = null;
            if (_activeObject != null && _activeObject.Bounds.Width < 8 && _activeObject.Bounds.Height < 8)
                _activeObject.Bounds = new Rectangle(_activeObject.Bounds.X, _activeObject.Bounds.Y, 140, 90);
            if (_activeObject != null) PersistObject(_activeObject);
            _activeObject = null; _resizing = false; Invalidate();
        }

        private void PersistObject(DrawableObject item)
        {
            if (item == null || item.PowerPointShape != null) { UpdatePersistentObject(item); return; }
            try
            {
                PPT.Slide slide = _addIn.Application.SlideShowWindows[1].View.Slide;
                RectangleF p = ToSlideRectangle(item.Bounds);
                if (item.Kind == DrawableKind.Text)
                {
                    PPT.Shape shape = slide.Shapes.AddTextbox(Office.MsoTextOrientation.msoTextOrientationHorizontal, p.Left, p.Top, p.Width, p.Height);
                    shape.TextFrame.TextRange.Text = item.Text ?? string.Empty;
                    shape.TextFrame.TextRange.Font.Name = "Segoe UI";
                    shape.TextFrame.TextRange.Font.Size = 22;
                    shape.TextFrame.TextRange.Font.Color.RGB = ColorTranslator.ToOle(item.Color);
                    shape.Line.Visible = Office.MsoTriState.msoFalse;
                    if (item.Background.A == 0) shape.Fill.Visible = Office.MsoTriState.msoFalse;
                    else { shape.Fill.Visible = Office.MsoTriState.msoTrue; shape.Fill.ForeColor.RGB = ColorTranslator.ToOle(item.Background); }
                    item.PowerPointShape = shape;
                }
                else if (item.Shape == AnnotationShape.Line || item.Shape == AnnotationShape.Arrow)
                {
                    PPT.Shape line = slide.Shapes.AddLine(p.Left, p.Top, p.Right, p.Bottom);
                    line.Line.ForeColor.RGB = ColorTranslator.ToOle(item.Color); line.Line.Weight = 2.5f;
                    if (item.Shape == AnnotationShape.Arrow) line.Line.EndArrowheadStyle = Office.MsoArrowheadStyle.msoArrowheadTriangle;
                    item.PowerPointShape = line;
                }
                else
                {
                    Office.MsoAutoShapeType type = item.Shape == AnnotationShape.Ellipse ? Office.MsoAutoShapeType.msoShapeOval :
                        item.Shape == AnnotationShape.Triangle ? Office.MsoAutoShapeType.msoShapeIsoscelesTriangle : Office.MsoAutoShapeType.msoShapeRectangle;
                    PPT.Shape shape = slide.Shapes.AddShape(type, p.Left, p.Top, p.Width, p.Height);
                    shape.Fill.Visible = item.Filled ? Office.MsoTriState.msoTrue : Office.MsoTriState.msoFalse;
                    if (item.Filled) shape.Fill.ForeColor.RGB = ColorTranslator.ToOle(item.Color);
                    shape.Line.ForeColor.RGB = ColorTranslator.ToOle(item.Color); shape.Line.Weight = 2.5f;
                    item.PowerPointShape = shape;
                }
                if (item.PowerPointShape != null) item.PowerPointShape.Tags.Add("LOKAL_ANNOTATION", "1");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Could not persist annotation: " + ex.Message); }
        }

        private void UpdatePersistentObject(DrawableObject item)
        {
            if (item == null || item.PowerPointShape == null) return;
            try
            {
                RectangleF p = ToSlideRectangle(item.Bounds);
                item.PowerPointShape.Left = p.Left; item.PowerPointShape.Top = p.Top;
                item.PowerPointShape.Width = Math.Max(2, p.Width); item.PowerPointShape.Height = Math.Max(2, p.Height);
            }
            catch { }
        }

        private void DeletePersistentObject(DrawableObject item)
        {
            try { item?.PowerPointShape?.Delete(); } catch { }
            if (item != null) item.PowerPointShape = null;
        }

        private RectangleF ToSlideRectangle(Rectangle rectangle)
        {
            try
            {
                PPT.Presentation presentation = _addIn.Application.ActivePresentation;
                float sx = presentation.PageSetup.SlideWidth / Math.Max(1f, ClientSize.Width);
                float sy = presentation.PageSetup.SlideHeight / Math.Max(1f, ClientSize.Height);
                return new RectangleF(rectangle.Left * sx, rectangle.Top * sy, Math.Max(2, rectangle.Width * sx), Math.Max(2, rectangle.Height * sy));
            }
            catch { return rectangle; }
        }

        internal void ToggleWhiteboard()
        {
            CurrentAnnotations().Whiteboard = !CurrentAnnotations().Whiteboard;
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST && _mode == AnnotationMode.None)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000; // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animation.Dispose();
                try { if (_display != null && !_display.IsDisposed) _display.Close(); } catch { }
            }
            base.Dispose(disposing);
        }

        private static Cursor CursorFor(AnnotationMode mode)
        {
            return mode == AnnotationMode.None ? Cursors.Default :
                   mode == AnnotationMode.Text ? Cursors.IBeam :
                   mode == AnnotationMode.Select ? Cursors.SizeAll : Cursors.Cross;
        }

        private static void DrawStroke(Graphics g, InkStroke stroke)
        {
            if (stroke.Points.Count < 2) return;
            using (var pen = new Pen(Color.FromArgb(stroke.Alpha, stroke.Color), stroke.Width))
            {
                // A marker has a broad, flat nib. Pens and the laser retain their
                // rounded handwritten appearance.
                pen.StartCap = pen.EndCap = stroke.IsHighlighter ? LineCap.Square : LineCap.Round;
                pen.LineJoin = stroke.IsHighlighter ? LineJoin.Bevel : LineJoin.Round;
                g.DrawLines(pen, stroke.Points.ToArray());
            }
        }

        private static void DrawObject(Graphics g, DrawableObject item, bool selected, bool drawContent)
        {
            Rectangle r = item.Bounds;
            if (drawContent && item.Kind == DrawableKind.Text)
            {
                if (item.Background.A > 0) using (var brush = new SolidBrush(item.Background)) g.FillRectangle(brush, r);
                using (var font = new Font("Segoe UI", Math.Max(12f, Math.Min(28f, r.Height * .34f)), FontStyle.Regular))
                using (var brush = new SolidBrush(item.Color))
                    g.DrawString(item.Text ?? "", font, brush, r);
            }
            else if (drawContent)
            {
                if (item.Filled && item.Shape != AnnotationShape.Line && item.Shape != AnnotationShape.Arrow)
                {
                    using (var fill = new SolidBrush(Color.FromArgb(215, item.Color)))
                    {
                        if (item.Shape == AnnotationShape.Rectangle) g.FillRectangle(fill, r);
                        else if (item.Shape == AnnotationShape.Ellipse) g.FillEllipse(fill, r);
                        else if (item.Shape == AnnotationShape.Triangle)
                            g.FillPolygon(fill, new[] { new Point(r.Left + r.Width / 2, r.Top), new Point(r.Right, r.Bottom), new Point(r.Left, r.Bottom) });
                    }
                }
                using (var pen = new Pen(item.Color, 4f))
                {
                    pen.LineJoin = LineJoin.Round; pen.StartCap = LineCap.Round;
                    if (item.Shape == AnnotationShape.Rectangle) g.DrawRectangle(pen, r);
                    else if (item.Shape == AnnotationShape.Ellipse) g.DrawEllipse(pen, r);
                    else if (item.Shape == AnnotationShape.Triangle)
                        g.DrawPolygon(pen, new[] { new Point(r.Left + r.Width / 2, r.Top), new Point(r.Right, r.Bottom), new Point(r.Left, r.Bottom) });
                    else
                    {
                        if (item.Shape == AnnotationShape.Arrow) pen.CustomEndCap = new AdjustableArrowCap(5, 6);
                        g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom);
                    }
                }
            }
            if (selected)
            {
                using (var pen = new Pen(LokalUi.Primary, 1.5f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, r);
                Rectangle handle = ResizeHandle(r); g.FillRectangle(Brushes.White, handle);
                using (var pen = new Pen(LokalUi.Primary, 1.5f)) g.DrawRectangle(pen, handle);
            }
        }

        private static Rectangle Normalize(Point a, Point b) { return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)); }
        private static Rectangle Inflate(Rectangle r, int amount) { r.Inflate(amount, amount); return r; }
        private static Rectangle ResizeHandle(Rectangle r) { return new Rectangle(r.Right - 7, r.Bottom - 7, 14, 14); }
        private static double Distance(Point a, Point b) { double x = a.X - b.X, y = a.Y - b.Y; return Math.Sqrt(x * x + y * y); }

        private sealed class SlideAnnotations
        {
            public readonly List<InkStroke> Ink = new List<InkStroke>();
            public readonly List<InkStroke> Laser = new List<InkStroke>();
            public readonly List<DrawableObject> Objects = new List<DrawableObject>();
            public bool Whiteboard;
        }
        private sealed class InkStroke
        {
            public readonly List<Point> Points = new List<Point>();
            public Color Color; public float Width; public int Alpha; public bool IsHighlighter; public DateTime CreatedUtc;
        }
        private enum DrawableKind { Shape, Text }
        private sealed class DrawableObject
        {
            public DrawableKind Kind; public AnnotationShape Shape; public Rectangle Bounds;
            public Color Color, Background; public string Text; public bool Filled;
            public PPT.Shape PowerPointShape;
        }

        /// <summary>
        /// Fully visible annotation renderer. WS_EX_TRANSPARENT/NOACTIVATE keeps
        /// it from intercepting clicks; the companion form above handles input.
        /// </summary>
        private sealed class AnnotationDisplayForm : Form
        {
            private readonly AnnotationOverlayForm _owner;

            internal AnnotationDisplayForm(AnnotationOverlayForm owner)
            {
                _owner = owner;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.Black;
                SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                RefreshSurface();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (Visible) RefreshSurface();
            }

            /// <summary>
            /// Renders the overlay through UpdateLayeredWindow so every pixel keeps
            /// its alpha value. TransparencyKey only supports fully opaque or fully
            /// transparent pixels and was turning highlighter ink into a dark pen.
            /// </summary>
            internal void RefreshSurface()
            {
                if (IsDisposed || !IsHandleCreated || Width <= 0 || Height <= 0) return;
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(RefreshSurface));
                    return;
                }

                using (var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    _owner.RenderAnnotations(graphics);

                    IntPtr screenDc = GetDC(IntPtr.Zero);
                    IntPtr memoryDc = CreateCompatibleDC(screenDc);
                    IntPtr hBitmap = IntPtr.Zero;
                    IntPtr oldBitmap = IntPtr.Zero;
                    try
                    {
                        hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                        oldBitmap = SelectObject(memoryDc, hBitmap);
                        var destination = new NativePoint(Left, Top);
                        var source = new NativePoint(0, 0);
                        var size = new NativeSize(bitmap.Width, bitmap.Height);
                        var blend = new BlendFunction
                        {
                            BlendOp = 0,
                            BlendFlags = 0,
                            SourceConstantAlpha = 255,
                            AlphaFormat = 1
                        };
                        UpdateLayeredWindow(Handle, screenDc, ref destination, ref size,
                            memoryDc, ref source, 0, ref blend, 2);
                    }
                    finally
                    {
                        if (oldBitmap != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
                        if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                        DeleteDC(memoryDc);
                        ReleaseDC(IntPtr.Zero, screenDc);
                    }
                }
            }

            protected override void WndProc(ref Message m)
            {
                const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
                if (m.Msg == WM_NCHITTEST)
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
                base.WndProc(ref m);
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x80 | 0x20 | 0x00080000 | 0x08000000;
                    // tool window, click-through, per-pixel layered, no-activate
                    return cp;
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct NativePoint
            {
                public int X;
                public int Y;
                public NativePoint(int x, int y) { X = x; Y = y; }
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct NativeSize
            {
                public int Width;
                public int Height;
                public NativeSize(int width, int height) { Width = width; Height = height; }
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private struct BlendFunction
            {
                public byte BlendOp;
                public byte BlendFlags;
                public byte SourceConstantAlpha;
                public byte AlphaFormat;
            }

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destinationDc,
                ref NativePoint destination, ref NativeSize size, IntPtr sourceDc,
                ref NativePoint source, int colorKey, ref BlendFunction blend, int flags);

            [DllImport("user32.dll")]
            private static extern IntPtr GetDC(IntPtr hwnd);

            [DllImport("user32.dll")]
            private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

            [DllImport("gdi32.dll")]
            private static extern IntPtr CreateCompatibleDC(IntPtr dc);

            [DllImport("gdi32.dll")]
            private static extern bool DeleteDC(IntPtr dc);

            [DllImport("gdi32.dll")]
            private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);

            [DllImport("gdi32.dll")]
            private static extern bool DeleteObject(IntPtr value);
        }
    }

    internal sealed class TextEntryDialog : Form
    {
        private readonly TextBox _text;
        internal string Value { get { return _text.Text; } }
        internal TextEntryDialog(Color foreground, Color background)
        {
            Text = "LOKAL — Add text"; StartPosition = FormStartPosition.CenterParent; Size = new Size(450, 220);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; TopMost = true;
            BackColor = Color.White; LokalUi.ApplyBrandIcon(this);
            _text = new TextBox { Multiline = true, Font = new Font("Segoe UI", 16f), ForeColor = foreground,
                BackColor = background.A == 0 ? Color.White : background, Location = new Point(22, 22), Size = new Size(390, 90) };
            var add = new Button { Text = "Add text", DialogResult = DialogResult.OK, BackColor = LokalUi.Primary,
                ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Location = new Point(278, 125), Size = new Size(134, 42) };
            add.FlatAppearance.BorderSize = 0; Controls.Add(_text); Controls.Add(add); AcceptButton = add;
        }
    }
}
