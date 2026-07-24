using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using PPT = Microsoft.Office.Interop.PowerPoint;

namespace LOKAL.PowerPoint
{
    /// <summary>Full-screen slide navigator kept above the slideshow toolbar.</summary>
    public sealed class SlideIndexDialog : Form
    {
        private readonly ThisAddIn _addIn;
        private readonly string _tempDir;
        private readonly List<Image> _images = new List<Image>();
        private const int ThumbW = 326;
        private const int ThumbH = 184;

        public SlideIndexDialog(ThisAddIn addIn)
        {
            _addIn = addIn;
            _tempDir = Path.Combine(Path.GetTempPath(), "lokal_slide_thumbs");
            BuildUi();
        }

        private void BuildUi()
        {
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            Text = "LOKAL — Slide Index";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(25, 25, 25);
            Bounds = new Rectangle(screen.Left, screen.Top, screen.Width, Math.Max(300, screen.Height - SlideshowToolbarForm.BarHeight));
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            var grid = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(25, 25, 25),
                Padding = new Padding(24, 8, 24, 24),
                WrapContents = true
            };
            var header = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = Color.FromArgb(25, 25, 25) };
            var back = new IndexBackButton
            {
                Size = new Size(54, 54),
                Location = new Point(17, 14),
                Cursor = Cursors.Hand
            };
            back.Click += (s, e) => Close();
            header.Controls.Add(back);
            Controls.Add(grid);
            Controls.Add(header);
            Populate(grid);
        }

        private void Populate(FlowLayoutPanel grid)
        {
            try
            {
                Directory.CreateDirectory(_tempDir);
                PPT.Presentation presentation = _addIn.Application.ActivePresentation;
                int current = 0;
                try
                {
                    var windows = _addIn.Application.SlideShowWindows;
                    if (windows.Count > 0) current = windows[1].View.Slide.SlideIndex;
                }
                catch { }

                foreach (PPT.Slide slide in presentation.Slides)
                {
                    int index = slide.SlideIndex;
                    bool selected = index == current;
                    var card = new Panel
                    {
                        Size = new Size(ThumbW + 8, ThumbH + 42),
                        Margin = new Padding(10, 8, 18, 14),
                        BackColor = Color.FromArgb(25, 25, 25),
                        Cursor = Cursors.Hand
                    };
                    card.Paint += (s, e) =>
                    {
                        if (!selected) return;
                        using (var pen = new Pen(Color.FromArgb(255, 112, 42), 2f))
                            e.Graphics.DrawRectangle(pen, 2, 2, ThumbW + 3, ThumbH + 3);
                    };
                    var picture = new PictureBox
                    {
                        Size = new Size(ThumbW, ThumbH),
                        Location = new Point(4, 4),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.White,
                        Cursor = Cursors.Hand
                    };
                    try
                    {
                        string path = Path.Combine(_tempDir, "slide_" + index + ".png");
                        slide.Export(path, "PNG", ThumbW * 2, ThumbH * 2);
                        using (var source = Image.FromFile(path)) picture.Image = new Bitmap(source);
                        _images.Add(picture.Image);
                    }
                    catch { }
                    var number = new Label
                    {
                        Text = index.ToString(),
                        Font = new Font("Segoe UI", 10f, selected ? FontStyle.Bold : FontStyle.Regular),
                        ForeColor = selected ? Color.FromArgb(255, 112, 42) : Color.FromArgb(190, 193, 202),
                        Location = new Point(4, ThumbH + 9),
                        Size = new Size(ThumbW, 26),
                        TextAlign = ContentAlignment.MiddleLeft,
                        Cursor = Cursors.Hand
                    };
                    EventHandler navigate = (s, e) =>
                    {
                        try
                        {
                            var windows = _addIn.Application.SlideShowWindows;
                            if (windows.Count > 0) windows[1].View.GotoSlide(index);
                        }
                        catch { }
                        Close();
                    };
                    card.Click += navigate; picture.Click += navigate; number.Click += navigate;
                    card.Controls.Add(picture); card.Controls.Add(number); grid.Controls.Add(card);
                }
            }
            catch (Exception ex)
            {
                grid.Controls.Add(new Label { Text = "Could not load slides: " + ex.Message, ForeColor = Color.White, AutoSize = true });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (Image image in _images) image.Dispose();
            base.Dispose(disposing);
        }

        private sealed class IndexBackButton : Control
        {
            private bool _hover;

            public IndexBackButton()
            {
                SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
                BackColor = Color.Transparent;
                TabStop = false;
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (_hover)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(46, 46, 50)))
                        g.FillEllipse(brush, 3, 3, Width - 6, Height - 6);
                }
                Color color = _hover ? Color.White : Color.FromArgb(205, 208, 218);
                using (var pen = new Pen(color, 2.6f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    int cy = Height / 2;
                    g.DrawLine(pen, 15, cy, Width - 13, cy);
                    g.DrawLine(pen, 15, cy, 25, cy - 10);
                    g.DrawLine(pen, 15, cy, 25, cy + 10);
                }
            }
        }
    }
}
