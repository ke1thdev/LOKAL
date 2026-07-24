using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LOKAL.PowerPoint
{
    /// <summary>Responsive, presentation-friendly countdown timer and stopwatch.</summary>
    public sealed class TimerDialog : Form
    {
        private readonly TimerSurface _surface;

        public TimerDialog()
        {
            Text = "LOKAL — Timer";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(540, 330);
            ClientSize = new Size(600, 320);
            MaximizeBox = false;
            MinimizeBox = true;
            TopMost = true;
            BackColor = Color.White;
            AutoScaleMode = AutoScaleMode.Dpi;
            LokalUi.ApplyBrandIcon(this);
            _surface = new TimerSurface { Dock = DockStyle.Fill };
            Controls.Add(_surface);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _surface.DisposeTimer();
            base.Dispose(disposing);
        }
    }

    internal sealed class TimerSurface : Control
    {
        private readonly Timer _timer = new Timer { Interval = 1000 };
        private bool _countdown = true;
        private bool _running;
        private bool _alarmPicker;
        private string _selectedAlarm = "Pager";
        private int _minutes = 5;
        private int _remaining = 300;
        private int _elapsed;
        private Rectangle _timerTab, _stopwatchTab, _minus, _plus, _start, _reset, _alarmRect, _backRect;
        private readonly Rectangle[] _soundRects = new Rectangle[6];
        private static readonly AlarmChoice[] AlarmChoices =
        {
            new AlarmChoice("Ding dong", "ding-dong_hlFmcuX.mp3"),
            new AlarmChoice("Pager", "dragon-studio-notification-sound-effect-372475.mp3"),
            new AlarmChoice("Tada", "viralaudio-ascent-braam-magma-brass-d-cinematic-trailer-sound-effect-222269.mp3"),
            new AlarmChoice("Fun alert", "dragon-studio-pathetic-screaming-sound-effect-312867.mp3"),
            new AlarmChoice("Bell chime", "freesound_community-kitchen-timer-33043.mp3"),
            new AlarmChoice("Carnival", "submority-traimory-mega-horn-angry-siren-f-cinematic-trailer-sound-effects-193408.mp3")
        };

        private static readonly Color Indigo = LokalUi.Primary;
        private static readonly Color Ink = Color.FromArgb(74, 72, 82);
        private static readonly Color Muted = Color.FromArgb(151, 150, 158);

        public TimerSurface()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            Cursor = Cursors.Default;
            _timer.Tick += (s, e) => TickTimer();
        }

        internal void DisposeTimer()
        {
            _timer.Stop();
            _timer.Dispose();
            AlarmAudio.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.White);

            if (_alarmPicker)
            {
                DrawAlarmPicker(g);
                return;
            }

            int tabH = Math.Max(58, Math.Min(72, Height / 4));
            using (var brush = new SolidBrush(LokalUi.PrimaryPale))
                g.FillRectangle(brush, 0, 0, Width, tabH);

            _timerTab = new Rectangle(0, 0, Width / 2, tabH);
            _stopwatchTab = new Rectangle(Width / 2, 0, Width - Width / 2, tabH);
            DrawTab(g, _timerTab, "Timer", true, _countdown ? Indigo : Muted);
            DrawTab(g, _stopwatchTab, "Stopwatch", false, !_countdown ? Indigo : Muted);

            Rectangle active = _countdown ? _timerTab : _stopwatchTab;
            using (var pen = new Pen(Indigo, 3f))
                g.DrawLine(pen, active.Left, tabH - 2, active.Right, tabH - 2);

            if (_countdown) DrawCountdown(g, tabH);
            else DrawStopwatch(g, tabH);
        }

        private void DrawTab(Graphics g, Rectangle rect, string text, bool hourglass, Color color)
        {
            using (var font = new Font("Segoe UI", ScaleFont(14f), FontStyle.Regular))
            {
                SizeF textSize = Measure(g, text, font);
                float iconWidth = 25f, gap = 9f;
                float x = rect.Left + (rect.Width - textSize.Width - iconWidth - gap) / 2f;
                RectangleF icon = new RectangleF(x, rect.Top + rect.Height / 2f - 12f, 24f, 24f);
                if (hourglass) DrawHourglass(g, icon, color); else DrawStopwatchIcon(g, icon, color);
                DrawText(g, text, font, color, x + iconWidth + gap, rect.Top + (rect.Height - textSize.Height) / 2f);
            }
        }

        private static void DrawHourglass(Graphics g, RectangleF rect, Color color)
        {
            using (var pen = new Pen(color, 1.8f))
            {
                g.DrawLine(pen, rect.Left + 5, rect.Top + 3, rect.Right - 5, rect.Top + 3);
                g.DrawLine(pen, rect.Left + 5, rect.Bottom - 3, rect.Right - 5, rect.Bottom - 3);
                g.DrawLine(pen, rect.Left + 7, rect.Top + 4, rect.Right - 7, rect.Bottom - 4);
                g.DrawLine(pen, rect.Right - 7, rect.Top + 4, rect.Left + 7, rect.Bottom - 4);
            }
        }

        private static void DrawStopwatchIcon(Graphics g, RectangleF rect, Color color)
        {
            using (var pen = new Pen(color, 1.8f))
            {
                g.DrawEllipse(pen, rect.Left + 4, rect.Top + 6, 16, 16);
                g.DrawLine(pen, rect.Left + 12, rect.Top + 1, rect.Left + 12, rect.Top + 6);
                g.DrawLine(pen, rect.Left + 9, rect.Top + 1, rect.Left + 15, rect.Top + 1);
                g.DrawLine(pen, rect.Left + 12, rect.Top + 9, rect.Left + 12, rect.Top + 15);
                g.DrawLine(pen, rect.Left + 12, rect.Top + 15, rect.Left + 16, rect.Top + 17);
            }
        }

        private void DrawAlarmPicker(Graphics g)
        {
            using (var font = new Font("Segoe UI", ScaleFont(14f), FontStyle.Bold))
            using (var arrowFont = new Font("Segoe UI", ScaleFont(25f), FontStyle.Regular))
            {
                _backRect = new Rectangle(18, 15, Width - 36, 48);
                DrawText(g, "‹", arrowFont, Indigo, 24, 10);
                DrawText(g, "Times-up alarm", font, Indigo, 58, 25);
            }

            int gap = 12;
            int side = 28;
            int columns = 3;
            int cardWidth = Math.Max(128, (Width - side * 2 - gap * (columns - 1)) / columns);
            int cardHeight = Math.Max(58, Math.Min(70, (Height - 102 - gap) / 2));
            int startY = 92;
            for (int i = 0; i < AlarmChoices.Length; i++)
            {
                int row = i / columns, column = i % columns;
                Rectangle card = new Rectangle(side + column * (cardWidth + gap), startY + row * (cardHeight + gap), cardWidth, cardHeight);
                _soundRects[i] = card;
                bool selected = string.Equals(_selectedAlarm, AlarmChoices[i].Name, StringComparison.OrdinalIgnoreCase);
                using (var path = RoundRect(card, 8))
                using (var brush = new SolidBrush(selected ? LokalUi.PrimaryLight : Color.White))
                using (var pen = new Pen(selected ? LokalUi.PrimaryMedium : Color.FromArgb(220, 226, 225), selected ? 1.6f : 1f))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }
                using (var font = new Font("Segoe UI", ScaleFont(11.2f), FontStyle.Regular))
                    DrawText(g, AlarmChoices[i].Name, font, Ink, card.Left + 14, card.Top + (card.Height - Measure(g, AlarmChoices[i].Name, font).Height) / 2f);
                if (selected) DrawCheck(g, new Rectangle(card.Right - 34, card.Top + card.Height / 2 - 10, 22, 22));
            }
        }

        private static void DrawCheck(Graphics g, Rectangle rect)
        {
            using (var pen = new Pen(Color.FromArgb(24, 183, 92), 2.2f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLine(pen, rect.Left + 2, rect.Top + 11, rect.Left + 8, rect.Top + 17);
                g.DrawLine(pen, rect.Left + 8, rect.Top + 17, rect.Right - 2, rect.Top + 4);
            }
        }

        private float ScaleFont(float normal)
        {
            float scale = Math.Min(1.25f, Math.Max(.82f, Width / 600f));
            return normal * scale;
        }

        private void DrawCountdown(Graphics g, int top)
        {
            int display = _running ? _remaining : _minutes * 60;
            string min = (display / 60).ToString();
            string sec = (display % 60).ToString("D2");
            float bigSize = ScaleFont(50f);
            float unitSize = ScaleFont(17f);

            using (var big = new Font("Segoe UI Light", bigSize, FontStyle.Regular, GraphicsUnit.Point))
            using (var unit = new Font("Segoe UI", unitSize, FontStyle.Regular, GraphicsUnit.Point))
            {
                SizeF minSize = Measure(g, min, big);
                SizeF mSize = Measure(g, "m", unit);
                SizeF secSize = Measure(g, sec, big);
                SizeF sSize = Measure(g, "s", unit);
                float gap = ScaleFont(10f);
                float total = minSize.Width + mSize.Width + gap + secSize.Width + sSize.Width;
                float x = (Width - total) / 2f;
                float y = top + Math.Max(22f, (Height - top - 78f - minSize.Height) / 2f - 2f);
                float unitY = y + minSize.Height * .49f;

                DrawText(g, min, big, Ink, x, y);
                DrawText(g, "m", unit, Ink, x + minSize.Width, unitY);
                float secX = x + minSize.Width + mSize.Width + gap;
                DrawText(g, sec, big, Ink, secX, y);
                DrawText(g, "s", unit, Ink, secX + secSize.Width, unitY);

                using (var pen = new Pen(LokalUi.PrimaryMedium, 1.2f))
                {
                    float underlineY = y + minSize.Height + 2f;
                    g.DrawLine(pen, x + 3f, underlineY, x + minSize.Width - 3f, underlineY);
                    g.DrawLine(pen, secX + 3f, underlineY, secX + secSize.Width - 3f, underlineY);
                }

                int adjustSize = Math.Max(38, (int)ScaleFont(44f));
                _minus = new Rectangle(Math.Max(16, (int)x - adjustSize - 28), (int)(y + 13), adjustSize, adjustSize);
                _plus = new Rectangle(Math.Min(Width - adjustSize - 16, (int)(x + total) + 28), (int)(y + 13), adjustSize, adjustSize);
                if (!_running)
                {
                    DrawAdjust(g, _minus, "−");
                    DrawAdjust(g, _plus, "+");
                }
            }
            DrawBottomControls(g);
        }

        private void DrawStopwatch(Graphics g, int top)
        {
            string value = _elapsed.ToString();
            using (var big = new Font("Segoe UI Light", ScaleFont(54f), FontStyle.Regular, GraphicsUnit.Point))
            using (var unit = new Font("Segoe UI", ScaleFont(18f), FontStyle.Regular, GraphicsUnit.Point))
            {
                SizeF valueSize = Measure(g, value, big);
                SizeF unitSize = Measure(g, "s", unit);
                float total = valueSize.Width + unitSize.Width;
                float x = (Width - total) / 2f;
                float y = top + Math.Max(22f, (Height - top - 78f - valueSize.Height) / 2f);
                DrawText(g, value, big, Ink, x, y);
                DrawText(g, "s", unit, Ink, x + valueSize.Width, y + valueSize.Height * .5f);
            }
            _minus = _plus = Rectangle.Empty;
            DrawBottomControls(g);
        }

        private void DrawBottomControls(Graphics g)
        {
            int buttonW = Math.Max(148, Math.Min(180, Width / 3));
            int buttonH = Math.Max(46, Math.Min(52, Height / 6));
            int y = Height - buttonH - 18;
            _start = new Rectangle(Width / 2 - buttonW / 2, y, buttonW, buttonH);
            using (var path = RoundRect(_start, buttonH / 2))
            using (var brush = new SolidBrush(Indigo))
                g.FillPath(brush, path);
            using (var font = new Font("Segoe UI", ScaleFont(13f), FontStyle.Bold))
                DrawCentered(g, _running ? "Pause" : "Start", font, _start, Color.White);

            _reset = new Rectangle(_start.Right + 12, y, Math.Max(68, Width / 9), buttonH);
            using (var font = new Font("Segoe UI", ScaleFont(9.5f), FontStyle.Bold))
                DrawCentered(g, "Reset", font, _reset, Indigo);

            int alarmSize = buttonH;
            _alarmRect = new Rectangle(Width - alarmSize - 20, y, alarmSize, alarmSize);
            DrawAlarm(g, _alarmRect);
        }

        private static SizeF Measure(Graphics g, string text, Font font)
        {
            return g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        }

        private static void DrawText(Graphics g, string text, Font font, Color color, float x, float y)
        {
            using (var brush = new SolidBrush(color))
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
                g.DrawString(text, font, brush, x, y, format);
        }

        private static void DrawCentered(Graphics g, string text, Font font, Rectangle rect, Color color)
        {
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, font, brush, rect, format);
        }

        private static void DrawAdjust(Graphics g, Rectangle r, string text)
        {
            using (var font = new Font("Segoe UI", 18f, FontStyle.Regular))
                DrawCentered(g, text, font, r, Indigo);
        }

        private static void DrawAlarm(Graphics g, Rectangle r)
        {
            Rectangle imageBounds = r;
            imageBounds.Inflate(-9, -9);
            if (LokalUi.DrawBellImage(g, imageBounds)) return;
            Color color = Indigo;
            float cx = r.Left + r.Width / 2f;
            float cy = r.Top + r.Height / 2f;
            using (var pen = new Pen(color, 2f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawArc(pen, cx - 10, cy - 11, 20, 22, 195, 150);
                g.DrawLine(pen, cx - 12, cy + 7, cx + 12, cy + 7);
                g.DrawArc(pen, cx - 3, cy + 6, 6, 8, 5, 170);
                g.DrawArc(pen, cx - 16, cy - 13, 7, 13, 95, 120);
                g.DrawArc(pen, cx + 9, cy - 13, 7, 13, 325, 120);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_alarmPicker)
            {
                bool soundHit = false;
                foreach (Rectangle rect in _soundRects) if (rect.Contains(e.Location)) { soundHit = true; break; }
                Cursor = (_backRect.Contains(e.Location) || soundHit) ? Cursors.Hand : Cursors.Default;
                return;
            }
            Cursor = (_timerTab.Contains(e.Location) || _stopwatchTab.Contains(e.Location) ||
                      _start.Contains(e.Location) || _reset.Contains(e.Location) || _alarmRect.Contains(e.Location) ||
                      (!_running && (_minus.Contains(e.Location) || _plus.Contains(e.Location))))
                ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (_alarmPicker)
            {
                if (_backRect.Contains(e.Location))
                {
                    _alarmPicker = false;
                    AlarmAudio.Stop();
                    Invalidate();
                    return;
                }
                for (int i = 0; i < _soundRects.Length; i++)
                {
                    if (!_soundRects[i].Contains(e.Location)) continue;
                    _selectedAlarm = AlarmChoices[i].Name;
                    AlarmAudio.Play(AlarmChoices[i].FileName);
                    Invalidate();
                    return;
                }
                return;
            }
            if (_timerTab.Contains(e.Location)) SwitchMode(true);
            else if (_stopwatchTab.Contains(e.Location)) SwitchMode(false);
            else if (_start.Contains(e.Location)) Toggle();
            else if (_reset.Contains(e.Location)) Reset();
            else if (_alarmRect.Contains(e.Location)) { _alarmPicker = true; Invalidate(); }
            else if (!_running && _countdown)
            {
                if (_minus.Contains(e.Location)) _minutes = Math.Max(1, _minutes - 1);
                else if (_plus.Contains(e.Location)) _minutes = Math.Min(99, _minutes + 1);
                _remaining = _minutes * 60;
                Invalidate();
            }
        }

        private void SwitchMode(bool countdown)
        {
            _timer.Stop();
            AlarmAudio.Stop();
            _running = false;
            _countdown = countdown;
            Invalidate();
        }

        private void Toggle()
        {
            if (_countdown && !_running && _remaining <= 0) _remaining = _minutes * 60;
            _running = !_running;
            if (_running)
            {
                AlarmAudio.Stop();
                _timer.Start();
            }
            else _timer.Stop();
            Invalidate();
        }

        private void Reset()
        {
            _timer.Stop();
            AlarmAudio.Stop();
            _running = false;
            if (_countdown) _remaining = _minutes * 60;
            else _elapsed = 0;
            Invalidate();
        }

        private void TickTimer()
        {
            if (_countdown)
            {
                if (_remaining > 0) _remaining--;
                if (_remaining <= 0)
                {
                    _timer.Stop();
                    _running = false;
                    AlarmAudio.Play(GetSelectedAlarmFile());
                }
            }
            else _elapsed++;
            Invalidate();
        }

        private string GetSelectedAlarmFile()
        {
            foreach (AlarmChoice choice in AlarmChoices)
                if (string.Equals(choice.Name, _selectedAlarm, StringComparison.OrdinalIgnoreCase)) return choice.FileName;
            return AlarmChoices[1].FileName;
        }

        private sealed class AlarmChoice
        {
            public readonly string Name;
            public readonly string FileName;
            public AlarmChoice(string name, string fileName) { Name = name; FileName = fileName; }
        }

        private static class AlarmAudio
        {
            private const string Alias = "lokal_timer_alarm";
            private static readonly object Sync = new object();
            private static System.Threading.Timer _autoStopTimer;
            private static int _playGeneration;

            [DllImport("winmm.dll", CharSet = CharSet.Auto)]
            private static extern int mciSendString(string command, System.Text.StringBuilder result, int resultLength, IntPtr callback);

            internal static void Play(string fileName)
            {
                try
                {
                    string path = Resolve(fileName);
                    if (!File.Exists(path)) { System.Media.SystemSounds.Asterisk.Play(); return; }
                    lock (Sync)
                    {
                        int generation = ++_playGeneration;
                        StopCore();
                        mciSendString("open \"" + path + "\" type mpegvideo alias " + Alias, null, 0, IntPtr.Zero);
                        mciSendString("play " + Alias + " from 0", null, 0, IntPtr.Zero);
                        _autoStopTimer?.Dispose();
                        // Alarm assets are previews/notifications, not background audio.
                        // Bound playback so a long MP3 can never keep sounding at 00:00.
                        _autoStopTimer = new System.Threading.Timer(_ =>
                        {
                            lock (Sync)
                            {
                                if (generation != _playGeneration) return;
                                StopCore();
                            }
                        }, null, 4000, System.Threading.Timeout.Infinite);
                    }
                }
                catch { System.Media.SystemSounds.Asterisk.Play(); }
            }

            internal static void Stop()
            {
                lock (Sync)
                {
                    _playGeneration++;
                    StopCore();
                }
            }

            private static void StopCore()
            {
                try
                {
                    _autoStopTimer?.Dispose();
                    _autoStopTimer = null;
                    mciSendString("stop " + Alias, null, 0, IntPtr.Zero);
                    mciSendString("close " + Alias, null, 0, IntPtr.Zero);
                }
                catch { }
            }

            private static string Resolve(string fileName)
            {
                string deployed = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds", fileName);
                if (File.Exists(deployed)) return deployed;
                return Path.Combine(@"C:\xampp\htdocs\LOKAL-ThesisSys\assets\sounds", fileName);
            }
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
