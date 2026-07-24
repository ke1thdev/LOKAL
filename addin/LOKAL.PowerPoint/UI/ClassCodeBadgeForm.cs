using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// Large, presentation-readable class-code badge. The dark label and green
    /// code/count area mirror the visual hierarchy used by premium classroom tools.
    /// </summary>
    public sealed class ClassCodeBadgeForm : Form
    {
        private readonly ThisAddIn _addIn;
        private string _code = "-----";
        private int _participantCount;

        public ClassCodeBadgeForm(ThisAddIn addIn)
        {
            _addIn = addIn;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(31, 47, 43);
            DoubleBuffered = true;
            Opacity = .98;
            Cursor = Cursors.Hand;
            Size = new Size(276, 62);
            Region = RoundedRegion(ClientRectangle, 14);
            Click += OpenClass;
            new ToolTip { InitialDelay = 150 }.SetToolTip(this, "Open class details");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.FromArgb(31, 47, 43));

            int labelWidth = 70;
            Rectangle green = new Rectangle(labelWidth, 0, Width - labelWidth, Height);
            using (var brush = new LinearGradientBrush(green,
                Color.FromArgb(25, 150, 55), Color.FromArgb(7, 117, 48), 0f))
                g.FillRectangle(brush, green);
            using (var brush = new SolidBrush(Color.FromArgb(30, 255, 255, 255)))
                g.FillRectangle(brush, labelWidth, 0, 1, Height);

            using (var font = new Font("Segoe UI", 9.2f, FontStyle.Regular))
                TextRenderer.DrawText(g, "class\ncode", font, new Rectangle(10, 6, labelWidth - 16, Height - 12),
                    Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            int countWidth = Math.Max(28, TextRenderer.MeasureText(_participantCount.ToString(),
                new Font("Segoe UI", 12f, FontStyle.Bold), new Size(100, Height), TextFormatFlags.NoPadding).Width + 6);
            Rectangle countRect = new Rectangle(Width - countWidth - 10, 0, countWidth, Height);
            DrawPeople(g, countRect.Left - 29, Height / 2 - 10);
            using (var font = new Font("Segoe UI", 12.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, _participantCount.ToString(), font, countRect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            Rectangle codeRect = new Rectangle(labelWidth + 14, 0, Math.Max(70, countRect.Left - labelWidth - 49), Height);
            using (var font = new Font("Segoe UI", 20f, FontStyle.Bold))
                TextRenderer.DrawText(g, string.IsNullOrWhiteSpace(_code) ? "-----" : _code.ToUpperInvariant(), font,
                    codeRect, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            using (var pen = new Pen(Color.FromArgb(35, 0, 0, 0), 1f))
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }

        private static void DrawPeople(Graphics g, int x, int y)
        {
            using (var pen = new Pen(Color.FromArgb(225, 255, 255, 255), 1.8f))
            {
                g.DrawEllipse(pen, x + 2, y, 8, 8);
                g.DrawArc(pen, x - 1, y + 10, 14, 10, 190, 160);
                g.DrawEllipse(pen, x + 13, y + 2, 7, 7);
                g.DrawArc(pen, x + 10, y + 11, 13, 9, 190, 145);
            }
        }

        public void SetCode(string code)
        {
            _code = code ?? "-----";
            UpdateBadgeSizeAndPosition();
        }

        public void SetParticipantCount(int count)
        {
            _participantCount = Math.Max(0, count);
            UpdateBadgeSizeAndPosition();
        }

        private void UpdateBadgeSizeAndPosition()
        {
            Action update = () =>
            {
                using (var font = new Font("Segoe UI", 20f, FontStyle.Bold))
                {
                    int codeWidth = TextRenderer.MeasureText(_code.ToUpperInvariant(), font,
                        new Size(400, 62), TextFormatFlags.NoPadding).Width;
                    int countWidth = TextRenderer.MeasureText(_participantCount.ToString(),
                        new Font("Segoe UI", 12.5f, FontStyle.Bold), new Size(100, 62), TextFormatFlags.NoPadding).Width;
                    Width = Math.Max(260, Math.Min(390, 70 + 14 + codeWidth + 42 + countWidth + 18));
                }
                Height = 62;
                Region = RoundedRegion(ClientRectangle, 14);
                PositionOnSlideshow();
                Invalidate();
            };
            if (IsHandleCreated && InvokeRequired) BeginInvoke(update); else update();
        }

        public void PositionOnSlideshow()
        {
            Screen screen = Screen.FromPoint(Cursor.Position);
            Rectangle bounds = screen.Bounds;
            Left = bounds.Right - Width - 14;
            Top = bounds.Top + 14;
        }

        private void OpenClass(object sender, EventArgs e)
        {
            _addIn?.ShowMyClassForm();
        }

        private static Region RoundedRegion(Rectangle rect, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            var region = new Region(path);
            path.Dispose();
            return region;
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }
        }
    }
}
