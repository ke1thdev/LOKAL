using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LOKAL.PowerPoint
{
    /// <summary>Compact palette shown above pen, highlighter, shape and text tools.</summary>
    internal sealed class ToolPaletteForm : Form
    {
        private readonly AnnotationOverlayForm _overlay;
        private readonly string _tool;
        private static readonly Color[] Colors =
        {
            Color.FromArgb(30,30,34), Color.White, Color.FromArgb(245,55,38),
            Color.FromArgb(65,73,235), Color.FromArgb(0,169,139), Color.FromArgb(142,105,226),
            Color.FromArgb(255,196,0), Color.FromArgb(226,37,156)
        };

        internal ToolPaletteForm(string tool, AnnotationOverlayForm overlay)
        {
            _tool = tool; _overlay = overlay;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            BackColor = Color.White; Padding = new Padding(10); AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Build();
            Paint += (s, e) => { using (var pen = new Pen(Color.FromArgb(218, 221, 232))) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); };
        }

        private void Build()
        {
            var root = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.White, Padding = new Padding(2), Margin = Padding.Empty };
            if (_tool == "Shapes") AddShapeButtons(root);
            else
            {
                foreach (Color color in Colors) root.Controls.Add(ColorButton(color, false));
                root.Controls.Add(new Panel { Width = 1, Height = 30, BackColor = Color.FromArgb(220, 222, 230), Margin = new Padding(7, 2, 7, 2) });
                if (_tool == "Text")
                {
                    Color[] backgrounds = { Color.Transparent, Color.FromArgb(255,235,76), Color.FromArgb(244,187,225), Color.FromArgb(255,182,79), Color.FromArgb(208,239,129), Color.FromArgb(173,216,255) };
                    foreach (Color color in backgrounds) root.Controls.Add(ColorButton(color, true));
                }
                else AddWidthButtons(root);
            }
            Controls.Add(root);
        }

        private Control ColorButton(Color color, bool background)
        {
            var button = new Button { Size = new Size(30, 30), Margin = new Padding(3), FlatStyle = FlatStyle.Flat, BackColor = color.A == 0 ? Color.White : color, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Color.FromArgb(205, 207, 216); button.FlatAppearance.BorderSize = 1;
            button.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(color.A == 0 ? Color.White : color)) e.Graphics.FillEllipse(brush, 4, 4, 21, 21);
                if (color.A == 0) using (var pen = new Pen(Color.FromArgb(130, 135, 145), 1.5f)) e.Graphics.DrawLine(pen, 6, 24, 24, 6);
                if (background) using (var font = new Font("Segoe UI", 8f, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, "A", font, button.ClientRectangle, Color.FromArgb(35,35,40), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            button.Click += (s, e) =>
            {
                if (_tool == "Pen") _overlay.PenColor = color;
                else if (_tool == "Highlighter") _overlay.HighlightColor = color;
                else if (_tool == "Text") { if (background) _overlay.TextBackColor = color; else _overlay.TextColor = color; }
            };
            return button;
        }

        private void AddWidthButtons(FlowLayoutPanel root)
        {
            float[] widths = _tool == "Highlighter" ? new[] { 14f, 22f, 32f } : new[] { 3f, 6f, 10f };
            foreach (float width in widths)
            {
                var button = new Button { Size = new Size(38, 30), Margin = new Padding(2), FlatStyle = FlatStyle.Flat, BackColor = Color.White, Cursor = Cursors.Hand };
                button.FlatAppearance.BorderSize = 0;
                button.Paint += (s, e) => { using (var pen = new Pen(Color.FromArgb(65,65,72), Math.Max(2, width / 3))) { pen.StartCap = pen.EndCap = LineCap.Round; e.Graphics.DrawLine(pen, 9, 15, 29, 15); } };
                button.Click += (s, e) => { if (_tool == "Pen") _overlay.PenWidth = width; else _overlay.HighlightWidth = width; };
                root.Controls.Add(button);
            }
        }

        private void AddShapeButtons(FlowLayoutPanel root)
        {
            AddShape(root, "□", AnnotationShape.Rectangle); AddShape(root, "○", AnnotationShape.Ellipse);
            AddShape(root, "△", AnnotationShape.Triangle); AddShape(root, "╱", AnnotationShape.Line); AddShape(root, "↗", AnnotationShape.Arrow);
            root.Controls.Add(new Panel { Width = 1, Height = 30, BackColor = Color.FromArgb(220, 222, 230), Margin = new Padding(7, 2, 7, 2) });
            foreach (Color color in Colors) root.Controls.Add(ShapeColorButton(color));
            root.Controls.Add(new Panel { Width = 1, Height = 30, BackColor = Color.FromArgb(220, 222, 230), Margin = new Padding(7, 2, 7, 2) });
            var outline = new Button { Text = "□", Font = new Font("Segoe UI Symbol", 18f), Size = new Size(38, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(2), Cursor = Cursors.Hand };
            var filled = new Button { Text = "▧", Font = new Font("Segoe UI Symbol", 18f), Size = new Size(38, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(2), Cursor = Cursors.Hand };
            outline.FlatAppearance.BorderSize = filled.FlatAppearance.BorderSize = 0;
            outline.Click += (s, e) => _overlay.ShapeFilled = false; filled.Click += (s, e) => _overlay.ShapeFilled = true;
            root.Controls.Add(outline); root.Controls.Add(filled);
        }

        private void AddShape(FlowLayoutPanel root, string glyph, AnnotationShape shape)
        {
            var button = new Button { Text = glyph, Font = new Font("Segoe UI Symbol", 18f), Size = new Size(38, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(2), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderSize = 0; button.Click += (s, e) => _overlay.ShapeKind = shape; root.Controls.Add(button);
        }

        private Control ShapeColorButton(Color color)
        {
            Control button = ColorButton(color, false); button.Click += (s, e) => _overlay.ShapeColor = color; return button;
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }
    }
}
