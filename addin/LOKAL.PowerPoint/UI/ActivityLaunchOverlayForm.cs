using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using PPT = Microsoft.Office.Interop.PowerPoint;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// Click target placed over a slide's activity button when automatic launch is
    /// disabled. PowerPoint does not expose a reliable shape-click event during a
    /// slideshow, so this small overlay preserves the expected question-button
    /// workflow without adding an unrelated control to the presenter toolbar.
    /// </summary>
    internal sealed class ActivityLaunchOverlayForm : Form
    {
        private readonly ThisAddIn _addIn;
        private readonly Button _button;
        private PPT.Shape _shape;

        internal ActivityLaunchOverlayForm(ThisAddIn addIn)
        {
            _addIn = addIn;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = LokalUi.Primary;

            _button = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = LokalUi.Primary,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter,
                TabStop = false
            };
            _button.FlatAppearance.BorderSize = 0;
            _button.FlatAppearance.MouseOverBackColor = LokalUi.PrimaryMedium;
            _button.FlatAppearance.MouseDownBackColor = LokalUi.Brand800;
            _button.Click += (s, e) =>
            {
                Hide();
                _addIn.TryAutoStartActivityForCurrentSlide(true);
            };
            Controls.Add(_button);
        }

        internal void SetActivity(PPT.Shape shape, string activityType, string configJson)
        {
            _shape = shape;
            string label = FriendlyName(activityType);
            bool quizMode = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(configJson))
                    quizMode = JObject.Parse(configJson).Value<bool?>("quiz_mode") ?? false;
            }
            catch { }

            _button.Text = (quizMode ? "★  " : "▥  ") + label;
            PositionOnSlideshow();
        }

        internal void PositionOnSlideshow()
        {
            if (_shape == null) return;
            try
            {
                Rectangle screenBounds = Screen.PrimaryScreen.Bounds;
                if (_addIn.Application.SlideShowWindows.Count > 0)
                {
                    IntPtr hwnd = new IntPtr(_addIn.Application.SlideShowWindows[1].HWND);
                    screenBounds = Screen.FromHandle(hwnd).Bounds;
                }

                float slideWidth = _addIn.Application.ActivePresentation.PageSetup.SlideWidth;
                float slideHeight = _addIn.Application.ActivePresentation.PageSetup.SlideHeight;
                float scale = Math.Min(screenBounds.Width / Math.Max(1f, slideWidth),
                    screenBounds.Height / Math.Max(1f, slideHeight));
                float renderedWidth = slideWidth * scale;
                float renderedHeight = slideHeight * scale;
                float offsetX = screenBounds.Left + (screenBounds.Width - renderedWidth) / 2f;
                float offsetY = screenBounds.Top + (screenBounds.Height - renderedHeight) / 2f;

                int left = (int)Math.Round(offsetX + _shape.Left * scale);
                int top = (int)Math.Round(offsetY + _shape.Top * scale);
                int width = Math.Max(120, (int)Math.Round(_shape.Width * scale));
                int height = Math.Max(42, (int)Math.Round(_shape.Height * scale));
                Bounds = new Rectangle(left, top, width, height);
                Region = RoundedRegion(ClientRectangle, Math.Max(8, height / 5));
            }
            catch { }
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        private static Region RoundedRegion(Rectangle bounds, int radius)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return null;
            int diameter = Math.Max(2, radius * 2);
            using (var path = new GraphicsPath())
            {
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                return new Region(path);
            }
        }

        private static string FriendlyName(string activityType)
        {
            switch ((activityType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "multiple_choice": return "Multiple Choice";
                case "short_answer": return "Short Answer";
                case "word_cloud": return "Word Cloud";
                case "slide_drawing": return "Slide Drawing";
                case "image_upload": return "Image Upload";
                case "fill_blanks": return "Fill in the Blanks";
                case "audio_record": return "Audio Record";
                case "video_upload": return "Video Upload";
                default: return "Start activity";
            }
        }
    }
}
