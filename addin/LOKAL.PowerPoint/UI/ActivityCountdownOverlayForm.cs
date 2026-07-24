using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LOKAL.PowerPoint
{
    internal sealed class ActivityCountdownOverlayForm : Form
    {
        private readonly ThisAddIn _addIn;
        private readonly Timer _timer;
        private DateTime _deadline;
        private int _remaining;
        private bool _warningPlayed;
        private bool _isClosingActivity;

        internal ActivityCountdownOverlayForm(ThisAddIn addIn)
        {
            _addIn = addIn;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(255, 166, 20);
            Size = new Size(350, 82);
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            LokalUi.ApplyBrandIcon(this);

            _timer = new Timer { Interval = 250 };
            _timer.Tick += Timer_Tick;
            UpdateRoundedRegion();
        }

        internal void SetActivity(Activity activity)
        {
            LokalUi.StopSoundAsset("lokal_timer_warning");
            LokalUi.StopSoundAsset("lokal_timer_finished");
            int seconds = activity == null ? 0 : Math.Max(0, activity.AutoCloseSeconds);
            _remaining = seconds;
            _deadline = DateTime.UtcNow.AddSeconds(seconds);
            _warningPlayed = false;
            _isClosingActivity = false;
            Visible = seconds > 0;
            if (seconds > 0)
            {
                PositionOnSlideshow();
                _timer.Start();
                if (seconds <= 10) PlayWarning();
                Invalidate();
            }
            else
            {
                _timer.Stop();
            }
        }

        internal void PositionOnSlideshow()
        {
            Screen screen = Screen.PrimaryScreen;
            try
            {
                if (_addIn.Application.SlideShowWindows.Count > 0)
                {
                    var hwnd = new IntPtr(_addIn.Application.SlideShowWindows[1].HWND);
                    screen = Screen.FromHandle(hwnd);
                }
            }
            catch { }

            int x = screen.WorkingArea.Left + (screen.WorkingArea.Width - Width) / 2;
            int y = screen.WorkingArea.Bottom - SlideshowToolbarForm.BarHeight - Height - 18;
            Location = new Point(x, y);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            PositionOnSlideshow();
            BringToFront();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedRect(rect, 18))
            using (LinearGradientBrush brush = new LinearGradientBrush(rect,
                _remaining <= 10 ? Color.FromArgb(244, 92, 80) : Color.FromArgb(255, 176, 24),
                _remaining <= 10 ? Color.FromArgb(224, 54, 66) : Color.FromArgb(255, 137, 16),
                LinearGradientMode.Horizontal))
            {
                e.Graphics.FillPath(brush, path);
            }

            using (Pen pen = new Pen(Color.White, 3f))
            {
                e.Graphics.DrawEllipse(pen, 32, 23, 30, 30);
                e.Graphics.DrawLine(pen, 47, 17, 47, 25);
                e.Graphics.DrawLine(pen, 39, 17, 55, 17);
                e.Graphics.DrawLine(pen, 47, 38, 47, 29);
                e.Graphics.DrawLine(pen, 47, 38, 55, 42);
            }

            string text = string.Format("{0:00}:{1:00}", Math.Max(0, _remaining) / 60, Math.Max(0, _remaining) % 60);
            using (Font labelFont = new Font("Segoe UI", 10.5f, FontStyle.Bold))
            using (Font timeFont = new Font("Segoe UI", 25f, FontStyle.Bold))
            using (Brush white = new SolidBrush(Color.White))
            {
                e.Graphics.DrawString(_remaining <= 10 ? "Submissions closing" : "Time remaining",
                    labelFont, white, new PointF(82, 13));
                e.Graphics.DrawString(text, timeFont, white, new PointF(78, 28));
            }
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            if (_isClosingActivity) return;
            _remaining = Math.Max(0, (int)Math.Ceiling((_deadline - DateTime.UtcNow).TotalSeconds));
            if (_remaining > 0 && _remaining <= 10) PlayWarning();
            Invalidate();

            if (_remaining > 0) return;

            _isClosingActivity = true;
            _timer.Stop();
            // The running-out clip is only a countdown warning.  It must never
            // continue underneath the time-up bell or after submissions close.
            LokalUi.StopSoundAsset("lokal_timer_warning");
            LokalUi.PlaySoundAsset("ring-bell-after-timer.mp3", "lokal_timer_finished");
            _ = StopFinishedBellAfterDelayAsync();
            try { await _addIn.SessionManager.CloseActivityAsync(true); }
            catch { }
            try { Close(); } catch { }
        }

        private void PlayWarning()
        {
            if (_warningPlayed) return;
            _warningPlayed = true;
            LokalUi.PlaySoundAsset("timer-running-out.mp3", "lokal_timer_warning");
        }

        private static async Task StopFinishedBellAfterDelayAsync()
        {
            await Task.Delay(5000);
            LokalUi.StopSoundAsset("lokal_timer_finished");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            LokalUi.StopSoundAsset("lokal_timer_warning");
            base.OnFormClosed(e);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRoundedRegion();
        }

        private void UpdateRoundedRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width, Height), 18))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
