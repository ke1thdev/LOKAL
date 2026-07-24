using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Office = Microsoft.Office.Core;
using PPT = Microsoft.Office.Interop.PowerPoint;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// Premium LOKAL leaderboard. The visual components are custom drawn so the
    /// layout remains stable in PowerPoint at different resolutions and DPI scales.
    /// </summary>
    public sealed class LeaderboardDialog : Form
    {
        private readonly ThisAddIn _addIn;
        private readonly List<Participant> _participants = new List<Participant>();
        private readonly Color _ink = Color.FromArgb(38, 46, 70);
        private readonly Color _primary = LokalUi.Primary;

        private Panel _header;
        private GradientPanel _body;
        private Panel _viewHost;
        private LeaderboardTabs _tabs;
        private Button _insertButton;
        private Image _logoImage;
        private bool _currentClassActive = true;
        private int _currentVisibleCount = 7;
        private readonly Timer _leaderboardRefreshTimer;
        private bool _refreshInProgress;
        private string _leaderboardVersion = string.Empty;

        public LeaderboardDialog(ThisAddIn addIn)
        {
            _addIn = addIn;
            LokalUi.ApplyBrandIcon(this);
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            LoadLogo();
            BuildWindow();
            _leaderboardRefreshTimer = new Timer { Interval = 1500 };
            _leaderboardRefreshTimer.Tick += async (s, e) => await RefreshLeaderboardAsync(false);
            LoadData();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_leaderboardRefreshTimer != null)
                {
                    _leaderboardRefreshTimer.Stop();
                    _leaderboardRefreshTimer.Dispose();
                }
                if (_logoImage != null) _logoImage.Dispose();
            }
            base.Dispose(disposing);
        }

        private void BuildWindow()
        {
            Text = "LOKAL — Leaderboard";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(980, 620);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.White;
            TopMost = true;

            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(
                Math.Max(MinimumSize.Width, (int)(work.Width * 0.78)),
                Math.Max(MinimumSize.Height, (int)(work.Height * 0.82)));

            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 96,
                BackColor = Color.White
            };
            _header.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(232, 234, 244)))
                    e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
            };

            var logo = new LogoControl(_logoImage)
            {
                Size = new Size(48, 48),
                BackColor = Color.Transparent
            };
            var title = new Label
            {
                Text = "Leader Board",
                Font = new Font("Segoe UI", 21f, FontStyle.Bold),
                ForeColor = _ink,
                AutoSize = true,
                BackColor = Color.Transparent
            };

            _tabs = new LeaderboardTabs
            {
                Size = new Size(560, 56),
                CurrentClassActive = true,
                Cursor = Cursors.Hand
            };
            _tabs.ModeChanged += current => SetActiveTab(current);

            _insertButton = MakeHeaderButton("Insert as slide");
            _insertButton.Click += (s, e) => InsertAsSlide();

            _header.Controls.Add(logo);
            _header.Controls.Add(title);
            _header.Controls.Add(_tabs);
            _header.Controls.Add(_insertButton);

            Action layoutHeader = () =>
            {
                int cy = (_header.ClientSize.Height - logo.Height) / 2;
                logo.Location = new Point(30, cy);
                title.Location = new Point(90, (_header.ClientSize.Height - title.Height) / 2 - 1);
                _tabs.Location = new Point(
                    Math.Max(340, (_header.ClientSize.Width - _tabs.Width) / 2),
                    (_header.ClientSize.Height - _tabs.Height) / 2);
                _insertButton.Location = new Point(
                    _header.ClientSize.Width - _insertButton.Width - 30,
                    (_header.ClientSize.Height - _insertButton.Height) / 2);

                bool enoughRoom = _tabs.Right + 18 < _insertButton.Left;
                _insertButton.Visible = enoughRoom;
            };
            _header.Resize += (s, e) => layoutHeader();
            layoutHeader();

            _body = new GradientPanel
            {
                Dock = DockStyle.Fill,
                GradientTop = LokalUi.PrimaryLight,
                GradientBottom = LokalUi.PrimaryPale
            };
            _viewHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            _body.Controls.Add(_viewHost);

            Controls.Add(_body);
            Controls.Add(_header);
            ShowStatus("Loading leaderboard…", "Fetching the latest class rankings.");
        }

        private Button MakeHeaderButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(170, 44),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = _primary,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = LokalUi.PrimaryPale;
            return button;
        }

        private async void LoadData()
        {
            if (!_addIn.CurrentClassId.HasValue)
            {
                ShowStatus("No class selected", "Start or select a class to view its leaderboard.");
                return;
            }

            try
            {
                await RefreshLeaderboardAsync(true);
                if (!IsDisposed) _leaderboardRefreshTimer.Start();
            }
            catch (Exception ex)
            {
                ShowStatus("Couldn’t load the leaderboard", ex.Message);
            }
        }

        private async Task RefreshLeaderboardAsync(bool showErrors)
        {
            if (_refreshInProgress || IsDisposed || !_addIn.CurrentClassId.HasValue) return;
            _refreshInProgress = true;
            try
            {
                List<Participant> data = await _addIn.ApiClient.GetLeaderboardAsync(
                    _addIn.CurrentClassId.Value, _addIn.CurrentSessionId)
                    ?? new List<Participant>();
                string version = string.Join("|", data
                    .OrderBy(p => p.Id)
                    .Select(p => string.Format("{0}:{1}:{2}:{3}:{4}:{5}", p.Id, p.TotalStars,
                        p.SessionStars, p.SessionResponseTimeMs, p.Level, p.Name)));

                if (version == _leaderboardVersion) return;
                _leaderboardVersion = version;
                _participants.Clear();
                _participants.AddRange(data);
                if (!IsDisposed) RenderActiveView();
            }
            catch (Exception ex)
            {
                if (showErrors && !IsDisposed)
                    ShowStatus("Unable to load the leaderboard", ex.Message);
            }
            finally
            {
                _refreshInProgress = false;
            }
        }

        private List<Participant> SortedParticipants(bool currentSession = false)
        {
            IEnumerable<Participant> ranked = _participants
                .OrderByDescending(p => currentSession ? p.SessionStars : p.TotalStars);
            if (currentSession)
            {
                // ClassPoint documents answer speed as the Quiz Mode tiebreaker.
                // Students without a correct timed answer sort after timed peers.
                ranked = ((IOrderedEnumerable<Participant>)ranked)
                    .ThenBy(p => p.SessionResponseTimeMs > 0
                        ? p.SessionResponseTimeMs
                        : long.MaxValue);
            }
            return ((IOrderedEnumerable<Participant>)ranked)
                .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private void SetActiveTab(bool currentClass)
        {
            _currentClassActive = currentClass;
            _tabs.CurrentClassActive = currentClass;
            _tabs.Invalidate();
            RenderActiveView();
        }

        private void RenderActiveView()
        {
            if (_participants.Count == 0)
            {
                ShowStatus("No students yet", "Joined students will appear here automatically.");
                return;
            }

            if (_currentClassActive) BuildCurrentClassView();
            else BuildTotalStarsView();
        }

        private void BuildCurrentClassView()
        {
            _viewHost.Controls.Clear();
            List<Participant> sorted = SortedParticipants(true);
            int count = Math.Min(_currentVisibleCount, sorted.Count);

            var content = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 100, BackColor = Color.White };
            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.Transparent
            };
            var stack = new Panel { BackColor = Color.Transparent };
            scroll.Controls.Add(stack);
            content.Controls.Add(scroll);
            content.Controls.Add(footer);

            var rows = new List<SessionRankRowControl>();
            for (int i = 0; i < count; i++)
            {
                var row = new SessionRankRowControl(i + 1, sorted[i]);
                rows.Add(row);
                stack.Controls.Add(row);
            }

            PillButton more = MakeShowMoreButton("Show more");
            if (count < sorted.Count)
            {
                more.Click += (s, e) =>
                {
                    _currentVisibleCount = sorted.Count;
                    BuildCurrentClassView();
                };
            }
            else more.Enabled = false;
            footer.Controls.Add(more);

            Action layout = () =>
            {
                int viewport = Math.Max(1, scroll.ClientSize.Width);
                int rowWidth = Math.Min(960, Math.Max(640, viewport - 150));
                int left = Math.Max(28, (viewport - rowWidth) / 2);
                int y = 48;
                int maximumStars = Math.Max(1, sorted.Max(p => p.SessionStars));
                int minimumWidth = Math.Min(rowWidth, Math.Max(370, (int)(rowWidth * 0.38)));
                foreach (SessionRankRowControl row in rows)
                {
                    double scoreRatio = Math.Max(0d, Math.Min(1d,
                        row.Stars / (double)maximumStars));
                    int scoreWidth = minimumWidth + (int)((rowWidth - minimumWidth) * scoreRatio);
                    row.SetBounds(left, y, scoreWidth, 74);
                    y += 88;
                }
                stack.SetBounds(0, 0, Math.Max(viewport - 18, 1), Math.Max(y + 28, scroll.ClientSize.Height));
                more.Location = new Point((footer.ClientSize.Width - more.Width) / 2,
                    (footer.ClientSize.Height - more.Height) / 2);
            };
            scroll.Resize += (s, e) => layout();
            footer.Resize += (s, e) => layout();
            _viewHost.Controls.Add(content);
            layout();
        }

        private void BuildTotalStarsView()
        {
            _viewHost.Controls.Clear();
            List<Participant> sorted = SortedParticipants(false);

            var root = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 100, BackColor = Color.White };
            var stage = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var podium = new PodiumControl(sorted) { BackColor = Color.Transparent };
            var scroll = new Panel { AutoScroll = true, BackColor = Color.Transparent };
            var stack = new Panel { BackColor = Color.Transparent };
            scroll.Controls.Add(stack);

            var rows = new List<RankRowControl>();
            for (int i = 0; i < sorted.Count; i++)
            {
                var row = new RankRowControl(i + 1, sorted[i]) { Compact = true };
                rows.Add(row);
                stack.Controls.Add(row);
            }

            var more = MakeShowMoreButton("Show more");
            more.Enabled = false;
            footer.Controls.Add(more);
            stage.Controls.Add(podium);
            stage.Controls.Add(scroll);
            root.Controls.Add(stage);
            root.Controls.Add(footer);
            _viewHost.Controls.Add(root);

            Action layout = () =>
            {
                int stageW = Math.Max(900, stage.ClientSize.Width);
                int stageH = Math.Max(410, stage.ClientSize.Height);
                int podiumW = Math.Max(470, Math.Min(650, (int)(stageW * 0.52)));
                int rightLeft = Math.Max(podiumW, (int)(stageW * 0.54));
                int rightW = Math.Max(430, stageW - rightLeft - 44);

                podium.SetBounds(28, 12, podiumW - 40, stageH - 22);
                scroll.SetBounds(rightLeft, 30, rightW, stageH - 48);

                int rowW = Math.Max(390, scroll.ClientSize.Width - 22);
                int y = 0;
                foreach (RankRowControl row in rows)
                {
                    row.SetBounds(0, y, rowW, 74);
                    y += 88;
                }
                stack.SetBounds(0, 0, rowW, Math.Max(y + 8, scroll.ClientSize.Height));
                more.Location = new Point((footer.ClientSize.Width - more.Width) / 2,
                    (footer.ClientSize.Height - more.Height) / 2);
            };
            stage.Resize += (s, e) => layout();
            scroll.Resize += (s, e) => layout();
            footer.Resize += (s, e) => layout();
            layout();
        }

        private PillButton MakeShowMoreButton(string text)
        {
            var button = new PillButton
            {
                Text = text,
                Size = new Size(250, 48),
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            return button;
        }

        private void ShowStatus(string title, string detail)
        {
            _viewHost.Controls.Clear();
            var status = new StatusControl(title, detail) { Dock = DockStyle.Fill };
            _viewHost.Controls.Add(status);
        }

        private void InsertAsSlide()
        {
            string path = null;
            try
            {
                if (_addIn.Application == null || _addIn.Application.ActivePresentation == null)
                    throw new InvalidOperationException("Open a presentation before inserting the leaderboard.");

                // Render a dedicated 16:9 leaderboard canvas. Capturing the dialog
                // itself also captured window chrome/header controls and produced a
                // presentation slide that looked like a desktop screenshot.
                using (Control exportCanvas = BuildSlideExportCanvas(new Size(1600, 900)))
                using (var bitmap = new Bitmap(exportCanvas.Width, exportCanvas.Height))
                {
                    exportCanvas.CreateControl();
                    exportCanvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, exportCanvas.Size));
                    path = Path.Combine(Path.GetTempPath(), "lokal_leaderboard_" + Guid.NewGuid().ToString("N") + ".png");
                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }

                PPT.Presentation presentation = _addIn.Application.ActivePresentation;
                PPT.Slide slide = presentation.Slides.Add(
                    presentation.Slides.Count + 1,
                    PPT.PpSlideLayout.ppLayoutBlank);
                float slideW = presentation.PageSetup.SlideWidth;
                float slideH = presentation.PageSetup.SlideHeight;
                slide.Shapes.AddPicture(path, Office.MsoTriState.msoFalse, Office.MsoTriState.msoTrue,
                    0, 0, slideW, slideH);
                MessageBox.Show("Leaderboard inserted as a new slide.", "LOKAL",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Unable to insert leaderboard",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (!string.IsNullOrEmpty(path))
                {
                    try { File.Delete(path); } catch { }
                }
            }
        }

        private Control BuildSlideExportCanvas(Size size)
        {
            var canvas = new GradientPanel
            {
                Size = size,
                GradientTop = LokalUi.PrimaryLight,
                GradientBottom = LokalUi.PrimaryPale
            };
            var title = new Label
            {
                Text = _currentClassActive ? "Current class rank" : "Total stars rank",
                Font = new Font("Segoe UI", 30f, FontStyle.Bold), ForeColor = _ink,
                BackColor = Color.Transparent, AutoSize = true, Location = new Point(58, 38)
            };
            var subtitle = new Label
            {
                Text = _currentClassActive ? "Live rankings for this class session" : "Top performers across all class sessions",
                Font = new Font("Segoe UI", 14f), ForeColor = Color.FromArgb(92, 99, 126),
                BackColor = Color.Transparent, AutoSize = true, Location = new Point(62, 93)
            };
            canvas.Controls.Add(title); canvas.Controls.Add(subtitle);
            List<Participant> sorted = SortedParticipants(_currentClassActive);
            if (!_currentClassActive)
            {
                var podium = new PodiumControl(sorted) { BackColor = Color.Transparent };
                podium.SetBounds(45, 138, 710, 715); canvas.Controls.Add(podium);
                int y = 152;
                for (int i = 0; i < Math.Min(7, sorted.Count); i++)
                {
                    var row = new RankRowControl(i + 1, sorted[i]) { Compact = true };
                    row.SetBounds(820, y, 720, 76); canvas.Controls.Add(row); y += 92;
                }
            }
            else
            {
                int maximum = Math.Max(1, sorted.Count == 0 ? 1 : sorted.Max(p => p.SessionStars));
                int y = 150, fullWidth = 1420, minimum = 620;
                for (int i = 0; i < Math.Min(8, sorted.Count); i++)
                {
                    var row = new SessionRankRowControl(i + 1, sorted[i]);
                    double ratio = Math.Max(0d, Math.Min(1d, sorted[i].SessionStars / (double)maximum));
                    int width = minimum + (int)((fullWidth - minimum) * ratio);
                    row.SetBounds(90, y, width, 76); canvas.Controls.Add(row); y += 90;
                }
            }
            return canvas;
        }

        private void LoadLogo()
        {
            string[] candidates =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "android-chrome-192x192.png"),
                @"C:\xampp\htdocs\LOKAL-ThesisSys\assets\android-chrome-192x192.png"
            };
            foreach (string path in candidates)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    using (var source = Image.FromFile(path)) _logoImage = new Bitmap(source);
                    break;
                }
                catch { }
            }
        }
    }

    internal sealed class GradientPanel : Panel
    {
        public Color GradientTop { get; set; }
        public Color GradientBottom { get; set; }

        public GradientPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            using (var brush = new LinearGradientBrush(ClientRectangle, GradientTop, GradientBottom, 90f))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }

    internal sealed class PillButton : Control
    {
        public PillButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(1, 1, Width - 3, Height - 3);
            Color fill = Enabled ? LokalUi.PrimaryLight : Color.FromArgb(247, 249, 249);
            Color text = Enabled ? LokalUi.Primary : Color.FromArgb(145, 157, 157);
            using (var path = DrawingUtil.RoundRect(rect, rect.Height / 2))
            using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, rect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class LogoControl : Control
    {
        private readonly Image _image;
        public LogoControl(Image image)
        {
            _image = image;
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (_image != null)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(1, 1, Width - 2, Height - 2);
                    e.Graphics.SetClip(path);
                    e.Graphics.DrawImage(_image, ClientRectangle);
                    e.Graphics.ResetClip();
                }
                return;
            }
            using (var brush = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(23, 146, 135), Color.FromArgb(41, 85, 194), 45f))
                e.Graphics.FillEllipse(brush, 1, 1, Width - 2, Height - 2);
            using (var font = new Font("Segoe UI", 21f, FontStyle.Bold))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                e.Graphics.DrawString("L", font, Brushes.White, ClientRectangle, sf);
        }
    }

    internal sealed class LeaderboardTabs : Control
    {
        public bool CurrentClassActive { get; set; }
        public event Action<bool> ModeChanged;

        public LeaderboardTabs()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            bool current = e.X >= Width / 2;
            if (current == CurrentClassActive) return;
            CurrentClassActive = current;
            Invalidate();
            if (ModeChanged != null) ModeChanged(current);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle outer = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var path = DrawingUtil.RoundRect(outer, outer.Height / 2))
            using (var brush = new SolidBrush(LokalUi.PrimaryPale))
                g.FillPath(brush, path);

            int half = Width / 2;
            Rectangle active = CurrentClassActive
                ? new Rectangle(half + 2, 3, Width - half - 6, Height - 7)
                : new Rectangle(3, 3, half - 5, Height - 7);
            Rectangle shadow = active; shadow.Offset(0, 2);
            using (var path = DrawingUtil.RoundRect(shadow, shadow.Height / 2))
            using (var brush = new SolidBrush(Color.FromArgb(24, 56, 63, 130)))
                g.FillPath(brush, path);
            using (var path = DrawingUtil.RoundRect(active, active.Height / 2))
            using (var brush = new SolidBrush(Color.White))
                g.FillPath(brush, path);

            using (var activeFont = new Font("Segoe UI", 11.2f, FontStyle.Bold))
            using (var inactiveFont = new Font("Segoe UI", 11.2f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "Total stars rank", CurrentClassActive ? inactiveFont : activeFont,
                    new Rectangle(0, 0, half, Height),
                    CurrentClassActive ? Color.FromArgb(113, 118, 138) : LokalUi.Primary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "Current class rank", CurrentClassActive ? activeFont : inactiveFont,
                    new Rectangle(half, 0, Width - half, Height),
                    CurrentClassActive ? LokalUi.Primary : Color.FromArgb(113, 118, 138),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class SectionHeading : Control
    {
        private readonly string _title;
        private readonly string _subtitle;
        public SectionHeading(string title, string subtitle)
        {
            _title = title;
            _subtitle = subtitle;
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var titleFont = new Font("Segoe UI", 16f, FontStyle.Bold))
            using (var subFont = new Font("Segoe UI", 9.5f, FontStyle.Regular))
            {
                TextRenderer.DrawText(e.Graphics, _title, titleFont, new Rectangle(0, 0, Width, 31),
                    Color.FromArgb(43, 50, 75), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, _subtitle, subFont, new Rectangle(0, 34, Width, 24),
                    Color.FromArgb(118, 124, 147), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class RoundedContainer : Panel
    {
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public int Radius { get; set; }

        public RoundedContainer()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle shadow = new Rectangle(5, 8, Width - 12, Height - 14);
            using (var path = DrawingUtil.RoundRect(shadow, Radius))
            using (var brush = new SolidBrush(Color.FromArgb(20, 50, 55, 100)))
                e.Graphics.FillPath(brush, path);
            Rectangle card = new Rectangle(2, 2, Width - 8, Height - 10);
            using (var path = DrawingUtil.RoundRect(card, Radius))
            using (var brush = new SolidBrush(FillColor))
            using (var pen = new Pen(BorderColor))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class SessionRankRowControl : Control
    {
        private readonly int _rank;
        private readonly Participant _participant;
        public int Stars { get { return Math.Max(0, _participant.SessionStars); } }

        public SessionRankRowControl(int rank, Participant participant)
        {
            _rank = rank;
            _participant = participant;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle card = new Rectangle(2, 2, Width - 9, Height - 11);
            Rectangle shadow = card; shadow.Offset(0, 4);
            using (var path = DrawingUtil.RoundRect(shadow, card.Height / 2))
            using (var brush = new SolidBrush(LokalUi.PrimaryLight))
                g.FillPath(brush, path);

            using (var path = DrawingUtil.RoundRect(card, card.Height / 2))
            {
                if (_rank == 1)
                {
                    using (var brush = new LinearGradientBrush(card,
                        Color.FromArgb(248, 198, 20), LokalUi.PrimaryPale, 0f))
                        g.FillPath(brush, path);
                }
                else
                {
                    using (var brush = new SolidBrush(LokalUi.PrimaryPale))
                        g.FillPath(brush, path);
                }
            }

            int cy = card.Top + card.Height / 2;
            Rectangle rankCircle = new Rectangle(card.Left + 10, cy - 21, 42, 42);
            using (var brush = new SolidBrush(Color.White)) g.FillEllipse(brush, rankCircle);
            using (var font = new Font("Segoe UI", 12f, FontStyle.Bold))
                TextRenderer.DrawText(g, _rank.ToString(), font, rankCircle, Color.FromArgb(45, 50, 70),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            Rectangle levelBadge = new Rectangle(card.Right - 57, cy - 23, 46, 46);
            DrawingUtil.DrawLevelBadge(g, levelBadge, _participant.Level);

            Rectangle score = new Rectangle(levelBadge.Left - 94, cy - 20, 82, 40);
            using (var path = DrawingUtil.RoundRect(score, 20))
            using (var brush = new SolidBrush(LokalUi.PrimaryLight)) g.FillPath(brush, path);
            DrawingUtil.DrawStar(g, score.Left + 11, score.Top + 9, 21, Color.FromArgb(255, 190, 47));
            using (var font = new Font("Segoe UI", 11f, FontStyle.Bold))
                TextRenderer.DrawText(g, _participant.SessionStars.ToString(), font,
                    new Rectangle(score.Left + 40, score.Top, score.Width - 43, score.Height),
                    Color.FromArgb(45, 50, 70), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // Reserve independent columns for rank, avatar, name, score, and level.
            // The old name rectangle had a forced 90px minimum and was positioned
            // relative to the avatar, so it crossed into the avatar on short bars.
            int avatarSize = Math.Min(48, card.Height - 14);
            Rectangle avatar = new Rectangle(card.Left + 64, cy - avatarSize / 2, avatarSize, avatarSize);
            using (var brush = new SolidBrush(Color.FromArgb(194, 193, 203))) g.FillEllipse(brush, avatar);
            using (var font = new Font("Segoe UI", 14f))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(DrawingUtil.Initials(_participant.Name), font, Brushes.White, avatar, sf);

            int nameLeft = avatar.Right + 12;
            int nameRight = score.Left - 12;
            if (nameRight > nameLeft)
            {
                Rectangle name = new Rectangle(nameLeft, card.Top, nameRight - nameLeft, card.Height);
                using (var font = new Font("Segoe UI", 11.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, _participant.Name ?? "Student", font, name,
                        Color.FromArgb(45, 49, 67), TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class RankRowControl : Control
    {
        private readonly int _rank;
        private readonly Participant _participant;
        public bool Compact { get; set; }

        public RankRowControl(int rank, Participant participant)
        {
            _rank = rank;
            _participant = participant;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int h = Height - 9;
            Rectangle shadow = new Rectangle(5, 6, Width - 11, h);
            using (var path = DrawingUtil.RoundRect(shadow, 22))
            using (var brush = new SolidBrush(LokalUi.PrimaryLight))
                g.FillPath(brush, path);
            Rectangle card = new Rectangle(2, 2, Width - 9, h);
            using (var path = DrawingUtil.RoundRect(card, 22))
            using (var brush = new SolidBrush(LokalUi.PrimaryPale))
            using (var pen = new Pen(Color.FromArgb(224, 227, 241)))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            int avatarSize = Compact ? 42 : 46;
            int centerY = card.Top + card.Height / 2;
            DrawRank(g, new Rectangle(18, centerY - 16, 34, 32));

            int avatarX = 63;
            Rectangle avatar = new Rectangle(avatarX, centerY - avatarSize / 2, avatarSize, avatarSize);
            Color avatarA = DrawingUtil.AvatarColor(_participant.Name, 0);
            Color avatarB = DrawingUtil.AvatarColor(_participant.Name, 1);
            using (var brush = new LinearGradientBrush(avatar, avatarA, avatarB, 45f))
                g.FillEllipse(brush, avatar);
            using (var pen = new Pen(Color.White, 2f)) g.DrawEllipse(pen, avatar);
            using (var font = new Font("Segoe UI", Compact ? 11.5f : 12.5f, FontStyle.Bold))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(DrawingUtil.Initials(_participant.Name), font, Brushes.White, avatar, sf);

            int rightReserved = Compact ? 220 : 235;
            Rectangle nameRect = new Rectangle(avatar.Right + 14, card.Top, Math.Max(80, card.Width - avatar.Right - rightReserved), card.Height);
            using (var font = new Font("Segoe UI", Compact ? 10.8f : 11.8f, FontStyle.Bold))
                TextRenderer.DrawText(g, _participant.Name ?? "Student", font, nameRect,
                    Color.FromArgb(38, 43, 64), TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            int pillW = 92;
            Rectangle levelBadge = new Rectangle(card.Right - 57, centerY - 23, 46, 46);
            DrawingUtil.DrawLevelBadge(g, levelBadge, _participant.Level);
            Rectangle score = new Rectangle(levelBadge.Left - pillW - 12, centerY - 18, pillW, 36);
            using (var path = DrawingUtil.RoundRect(score, 18))
            using (var brush = new SolidBrush(Color.FromArgb(255, 248, 225)))
                g.FillPath(brush, path);
            DrawingUtil.DrawStar(g, score.Left + 11, score.Top + 8, 20, Color.FromArgb(255, 190, 47));
            using (var font = new Font("Segoe UI", 11f, FontStyle.Bold))
                TextRenderer.DrawText(g, _participant.TotalStars.ToString(), font,
                    new Rectangle(score.Left + 39, score.Top, score.Width - 44, score.Height),
                    Color.FromArgb(201, 126, 8), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        }

        private void DrawRank(Graphics g, Rectangle rect)
        {
            Color fill;
            Color text;
            if (_rank == 1) { fill = Color.FromArgb(255, 232, 151); text = Color.FromArgb(165, 103, 0); }
            else if (_rank == 2) { fill = Color.FromArgb(226, 231, 241); text = Color.FromArgb(92, 100, 120); }
            else if (_rank == 3) { fill = Color.FromArgb(255, 214, 191); text = Color.FromArgb(169, 79, 28); }
            else { fill = Color.Transparent; text = Color.FromArgb(139, 145, 166); }

            if (_rank <= 3)
            {
                using (var brush = new SolidBrush(fill)) g.FillEllipse(brush, rect);
            }
            using (var font = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, _rank.ToString(), font, rect, text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class PodiumControl : Control
    {
        private readonly List<Participant> _sorted;
        public PodiumControl(List<Participant> sorted)
        {
            _sorted = sorted;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            if (_sorted.Count == 0) return;

            int gap = 14;
            int blockW = Math.Max(100, Math.Min(175, (Width - 56 - gap * 2) / 3));
            int baseY = Height - 48;
            int firstH = Math.Min(255, Math.Max(175, (int)(Height * 0.34)));
            int secondH = firstH - 54;
            int thirdH = firstH - 76;
            int totalW = blockW * 3 + gap * 2;
            int startX = (Width - totalW) / 2;

            DrawPlace(g, 2, _sorted.Count > 1 ? _sorted[1] : null,
                new Rectangle(startX, baseY - secondH, blockW, secondH),
                Color.FromArgb(239, 240, 242), Color.FromArgb(128, 128, 132));
            DrawPlace(g, 1, _sorted[0],
                new Rectangle(startX + blockW + gap, baseY - firstH, blockW, firstH),
                Color.FromArgb(255, 220, 99), Color.FromArgb(235, 137, 0));
            DrawPlace(g, 3, _sorted.Count > 2 ? _sorted[2] : null,
                new Rectangle(startX + (blockW + gap) * 2, baseY - thirdH, blockW, thirdH),
                Color.FromArgb(255, 200, 169), Color.FromArgb(206, 86, 21));
        }

        private void DrawPlace(Graphics g, int rank, Participant participant, Rectangle block, Color fill, Color accent)
        {
            Rectangle shadow = block; shadow.Offset(4, 5);
            using (var path = DrawingUtil.RoundRect(shadow, 17))
            using (var brush = new SolidBrush(Color.FromArgb(190, 191, 213))) g.FillPath(brush, path);

            int depth = Math.Max(8, Math.Min(13, block.Width / 12));
            Rectangle front = new Rectangle(block.Left, block.Top + depth, block.Width - depth, block.Height - depth);
            Point[] rightFace =
            {
                new Point(front.Right, front.Top), new Point(block.Right, block.Top),
                new Point(block.Right, block.Bottom - depth), new Point(front.Right, block.Bottom)
            };
            Point[] topFace =
            {
                new Point(front.Left + depth, block.Top), new Point(block.Right, block.Top),
                new Point(front.Right, front.Top), new Point(front.Left, front.Top)
            };
            using (var brush = new SolidBrush(ControlPaint.Dark(fill, 0.12f))) g.FillPolygon(brush, rightFace);
            using (var brush = new SolidBrush(ControlPaint.LightLight(fill))) g.FillPolygon(brush, topFace);
            using (var path = DrawingUtil.RoundRect(front, 15))
            using (var brush = new LinearGradientBrush(front, ControlPaint.Light(fill, 0.12f), fill, 0f))
            using (var pen = new Pen(Color.FromArgb(120, Color.White), 1.2f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            DrawingUtil.DrawTrophy(g,
                new Rectangle(front.Left + front.Width / 2 - 30, front.Bottom - 72, 60, 54),
                Color.FromArgb(72, accent));

            using (var font = new Font("Segoe UI", 28f, FontStyle.Bold))
                TextRenderer.DrawText(g, rank.ToString(), font,
                    new Rectangle(front.Left, front.Top + front.Height / 2, front.Width, front.Height / 2 - 8),
                    Color.FromArgb(150, accent), TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.NoPadding);

            if (participant == null) return;
            int avatarSize = Math.Min(58, Math.Max(46, block.Width / 2));
            Rectangle avatar = new Rectangle(block.Left + (block.Width - avatarSize) / 2,
                block.Top - avatarSize - 58, avatarSize, avatarSize);
            using (var brush = new SolidBrush(Color.FromArgb(194, 193, 203))) g.FillEllipse(brush, avatar);
            using (var pen = new Pen(rank == 1 ? Color.FromArgb(255, 205, 55) : Color.White, 3f))
                g.DrawEllipse(pen, avatar);
            using (var font = new Font("Segoe UI", 13f, FontStyle.Bold))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(DrawingUtil.Initials(participant.Name), font, Brushes.White, avatar, sf);

            if (rank == 1) DrawingUtil.DrawCrown(g, avatar.Left + avatar.Width / 2, avatar.Top - 14);
            Rectangle nameRect = new Rectangle(block.Left - 4, avatar.Bottom + 3, block.Width + 8, 23);
            using (var font = new Font("Segoe UI", 9.2f, FontStyle.Bold))
                TextRenderer.DrawText(g, participant.Name ?? "Student", font, nameRect, Color.FromArgb(45, 49, 67),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            int scoreW = 58;
            Rectangle score = new Rectangle(block.Left + (block.Width - scoreW) / 2, avatar.Bottom + 28, scoreW, 22);
            using (var path = DrawingUtil.RoundRect(score, 11))
            using (var brush = new SolidBrush(Color.FromArgb(215, 255, 255, 255))) g.FillPath(brush, path);
            DrawingUtil.DrawStar(g, score.Left + 7, score.Top + 5, 12, Color.FromArgb(255, 188, 35));
            using (var font = new Font("Segoe UI", 8f, FontStyle.Bold))
                TextRenderer.DrawText(g, participant.TotalStars.ToString(), font,
                    new Rectangle(score.Left + 22, score.Top, score.Width - 25, score.Height), accent,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            Rectangle level = new Rectangle(front.Left + (front.Width - 94) / 2, front.Top + 14, 94, 40);
            using (var path = DrawingUtil.RoundRect(level, 18))
            using (var brush = new SolidBrush(Color.FromArgb(225, 255, 255, 255))) g.FillPath(brush, path);
            DrawingUtil.DrawLevelBadge(g, new Rectangle(level.Left + 5, level.Top + 3, 34, 34), participant.Level);
            using (var font = new Font("Segoe UI", 10f, FontStyle.Bold))
                TextRenderer.DrawText(g, "Lv " + Math.Max(1, participant.Level), font,
                    new Rectangle(level.Left + 38, level.Top, level.Width - 42, level.Height),
                    Color.FromArgb(52, 55, 72), TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class StatusControl : Control
    {
        private readonly string _title;
        private readonly string _detail;
        public StatusControl(string title, string detail)
        {
            _title = title;
            _detail = detail;
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int w = Math.Min(520, Width - 60);
            Rectangle card = new Rectangle((Width - w) / 2, (Height - 180) / 2, w, 180);
            using (var path = DrawingUtil.RoundRect(card, 26))
            using (var brush = new SolidBrush(Color.FromArgb(238, 255, 255, 255)))
                e.Graphics.FillPath(brush, path);
            DrawingUtil.DrawStar(e.Graphics, card.Left + 36, card.Top + 36, 34, Color.FromArgb(255, 190, 47));
            using (var titleFont = new Font("Segoe UI", 15f, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, _title, titleFont,
                    new Rectangle(card.Left + 86, card.Top + 31, card.Width - 115, 38),
                    Color.FromArgb(43, 50, 75), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (var detailFont = new Font("Segoe UI", 10f))
                TextRenderer.DrawText(e.Graphics, _detail, detailFont,
                    new Rectangle(card.Left + 36, card.Top + 91, card.Width - 72, 58),
                    Color.FromArgb(112, 118, 140), TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }
    }

    internal static class DrawingUtil
    {
        private static Image _levelBadgeSprite;

        public static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static string Initials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";
            string[] parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
                return (parts[0][0].ToString() + parts[parts.Length - 1][0]).ToUpperInvariant();
            return name.Substring(0, Math.Min(2, name.Length)).ToUpperInvariant();
        }

        public static Color AvatarColor(string name, int variant)
        {
            Color[] palette =
            {
                LokalUi.PrimaryMedium, LokalUi.PrimaryHover,
                Color.FromArgb(31, 164, 151), Color.FromArgb(36, 125, 189),
                Color.FromArgb(235, 126, 68), Color.FromArgb(211, 83, 137)
            };
            int hash = string.IsNullOrEmpty(name) ? 0 : name.Aggregate(17, (value, c) => value * 31 + c);
            int index = (hash & int.MaxValue) % palette.Length;
            if (variant == 0) return palette[index];
            Color c1 = palette[index];
            return Color.FromArgb(Math.Max(0, c1.R - 32), Math.Max(0, c1.G - 32), Math.Max(0, c1.B - 16));
        }

        public static void DrawStar(Graphics g, float x, float y, float size, Color color)
        {
            // Exact dashboard SVG geometry:
            // viewBox="0 0 24 24", fill="#FBBF24", stroke="#F59E0B".
            float[,] svg =
            {
                {12f, 2f}, {15.09f, 8.26f}, {22f, 9.27f}, {17f, 14.14f},
                {18.18f, 21.02f}, {12f, 17.77f}, {5.82f, 21.02f},
                {7f, 14.14f}, {2f, 9.27f}, {8.91f, 8.26f}
            };
            var points = new PointF[10];
            for (int i = 0; i < points.Length; i++)
                points[i] = new PointF(x + svg[i, 0] / 24f * size, y + svg[i, 1] / 24f * size);

            using (var brush = new SolidBrush(Color.FromArgb(251, 191, 36)))
                g.FillPolygon(brush, points);
            using (var pen = new Pen(Color.FromArgb(245, 158, 11), Math.Max(1f, size * 1.5f / 24f))
            {
                LineJoin = LineJoin.Round
            })
                g.DrawPolygon(pen, points);
        }

        public static void DrawLevelBadge(Graphics g, Rectangle destination, int level)
        {
            if (_levelBadgeSprite == null)
            {
                string[] candidates =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "level-badges.png"),
                    @"C:\xampp\htdocs\LOKAL-ThesisSys\assets\level-badges.png"
                };
                foreach (string path in candidates)
                {
                    try
                    {
                        if (!File.Exists(path)) continue;
                        using (var source = Image.FromFile(path)) _levelBadgeSprite = new Bitmap(source);
                        break;
                    }
                    catch { }
                }
            }

            int safeLevel = Math.Max(1, Math.Min(10, level));
            if (_levelBadgeSprite != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_levelBadgeSprite, destination,
                    new Rectangle((safeLevel - 1) * 128, 0, 128, 128), GraphicsUnit.Pixel);
                return;
            }

            Point[] hex =
            {
                new Point(destination.Left + destination.Width / 2, destination.Top),
                new Point(destination.Right - 1, destination.Top + destination.Height / 4),
                new Point(destination.Right - 1, destination.Bottom - destination.Height / 4),
                new Point(destination.Left + destination.Width / 2, destination.Bottom - 1),
                new Point(destination.Left, destination.Bottom - destination.Height / 4),
                new Point(destination.Left, destination.Top + destination.Height / 4)
            };
            using (var brush = new SolidBrush(LokalUi.PrimaryMedium)) g.FillPolygon(brush, hex);
            using (var font = new Font("Segoe UI", destination.Height * 0.36f, FontStyle.Bold))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(safeLevel.ToString(), font, Brushes.White, destination, sf);
        }

        public static void DrawTrophy(Graphics g, Rectangle rect, Color color)
        {
            if (LokalUi.DrawTrophyImage(g, rect)) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, Math.Max(2f, rect.Width / 18f)))
            {
                pen.LineJoin = LineJoin.Round;
                Rectangle cup = new Rectangle(rect.Left + rect.Width / 4, rect.Top,
                    rect.Width / 2, rect.Height / 2);
                g.DrawArc(pen, cup, 0, 180);
                g.DrawLine(pen, cup.Left, cup.Top + 4, cup.Left + 5, cup.Bottom - 2);
                g.DrawLine(pen, cup.Right, cup.Top + 4, cup.Right - 5, cup.Bottom - 2);
                g.DrawArc(pen, new Rectangle(rect.Left + 2, rect.Top + 4, rect.Width / 3, rect.Height / 3), 90, 180);
                g.DrawArc(pen, new Rectangle(rect.Right - rect.Width / 3 - 2, rect.Top + 4,
                    rect.Width / 3, rect.Height / 3), 270, 180);
                int stemX = rect.Left + rect.Width / 2;
                g.DrawLine(pen, stemX, cup.Bottom - 1, stemX, rect.Bottom - 10);
                g.DrawLine(pen, rect.Left + rect.Width / 3, rect.Bottom - 7,
                    rect.Right - rect.Width / 3, rect.Bottom - 7);
            }
        }

        public static void DrawCrown(Graphics g, int cx, int y)
        {
            Point[] crown =
            {
                new Point(cx - 15, y + 14), new Point(cx - 17, y),
                new Point(cx - 8, y + 8), new Point(cx, y - 4),
                new Point(cx + 8, y + 8), new Point(cx + 17, y),
                new Point(cx + 15, y + 14)
            };
            using (var brush = new SolidBrush(Color.FromArgb(255, 197, 45))) g.FillPolygon(brush, crown);
        }
    }
}
