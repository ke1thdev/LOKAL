using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// Premium responsive class workspace shown from the slideshow class-code badge.
    /// It combines join QR/code, live participant management, stars, sorting, class
    /// locking, launcher shortcuts, and leaderboard navigation in one surface.
    /// </summary>
    public sealed class MyClassForm : Form
    {
        private readonly ThisAddIn _addIn;
        private readonly List<Participant> _participants = new List<Participant>();
        private readonly HashSet<long> _onlineIds = new HashSet<long>();
        private Class _currentClass;
        private ClassSurface _surface;
        private ClassFooter _footer;
        private Timer _refreshTimer;
        private Timer _confettiTimer;
        private bool _refreshing;
        private Image _qrImage;
        private Image _logoImage;
        private string _search = "";
        private SortMode _sortMode = SortMode.JoinOrder;
        private bool _onlineOnly;
        private bool _menuOpen;
        private bool _sortOpen;
        private OverlayMode _overlayMode;
        private Participant _pendingParticipant;
        private readonly List<ConfettiParticle> _confetti = new List<ConfettiParticle>();
        private readonly Random _random = new Random();
        private DateTime _confettiStartedUtc;
        private DateTime _confettiFrameUtc;
        private const double ConfettiMaximumSeconds = 1.65;

        internal static readonly Color Ink = Color.FromArgb(48, 52, 72);
        internal static readonly Color Muted = Color.FromArgb(126, 132, 154);
        internal static readonly Color Canvas = LokalUi.PrimaryPale;
        internal static readonly Color Indigo = LokalUi.Primary;
        internal static readonly Color Teal = Color.FromArgb(9, 121, 107);
        internal static readonly Color Red = Color.FromArgb(222, 96, 103);
        internal static readonly Color Green = Color.FromArgb(16, 200, 91);

        internal enum SortMode { JoinOrder, Name }
        internal enum OverlayMode { None, Qr, ConfirmNewClass, ConfirmDelete }

        public MyClassForm(ThisAddIn addIn)
        {
            _addIn = addIn;
            LoadLogo();
            BuildUi();
            Shown += async (s, e) =>
            {
                if (_addIn != null && _addIn.CurrentClassId.HasValue)
                    await LoadParticipantsAsync(_addIn.CurrentClassId.Value);
                await LoadQrAsync();
            };
        }

        internal IReadOnlyList<Participant> VisibleParticipants
        {
            get
            {
                IEnumerable<Participant> query = _participants;
                if (_onlineOnly) query = query.Where(p => _onlineIds.Contains(p.Id));
                if (!string.IsNullOrWhiteSpace(_search))
                    query = query.Where(p => (p.Name ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
                if (_sortMode == SortMode.Name) query = query.OrderBy(p => p.Name);
                return query.ToList();
            }
        }

        internal IReadOnlyList<Participant> Participants { get { return _participants; } }
        internal HashSet<long> OnlineIds { get { return _onlineIds; } }
        internal Class CurrentClass { get { return _currentClass; } }
        internal Image QrImage { get { return _qrImage; } }
        internal Image LogoImage { get { return _logoImage; } }
        internal SortMode CurrentSort { get { return _sortMode; } }
        internal bool OnlineOnly { get { return _onlineOnly; } }
        internal bool MenuOpen { get { return _menuOpen; } }
        internal bool SortOpen { get { return _sortOpen; } }
        internal OverlayMode CurrentOverlay { get { return _overlayMode; } }
        internal Participant PendingParticipant { get { return _pendingParticipant; } }
        internal IReadOnlyList<ConfettiParticle> Confetti { get { return _confetti; } }

        internal string InstructorTitle
        {
            get
            {
                string displayName = Properties.Settings.Default.TeacherDisplayName;
                string firstName = string.IsNullOrWhiteSpace(displayName) ? "Teacher" :
                    displayName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Teacher";
                return firstName.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? firstName + "' Class" : firstName + "'s Class";
            }
        }

        internal string ClassCode { get { return _addIn == null ? "-----" : (_addIn.CurrentClassCode ?? "-----"); } }

        internal string JoinLink
        {
            get
            {
                string baseUrl = _addIn == null ? "http://localhost:8080/student" : _addIn.CurrentJoinUrl;
                if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "http://localhost:8080/student";
                string separator = baseUrl.Contains("?") ? "&" : "?";
                return baseUrl + separator + "code=" + Uri.EscapeDataString(ClassCode);
            }
        }

        private void BuildUi()
        {
            Text = "LOKAL — My Class";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(1000, 680);
            TopMost = true;
            BackColor = Color.White;
            AutoScaleMode = AutoScaleMode.Dpi;
            LokalUi.ApplyBrandIcon(this);
            Rectangle working = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Max(1100, (int)(working.Width * .75)), Math.Max(720, (int)(working.Height * .82)));

            _surface = new ClassSurface(this) { Dock = DockStyle.Fill, BackColor = Canvas };
            _footer = new ClassFooter(this) { Dock = DockStyle.Bottom, Height = 96, BackColor = Color.White };
            Controls.Add(_surface);
            Controls.Add(_footer);

            _refreshTimer = new Timer { Interval = 1500 };
            _refreshTimer.Tick += async (s, e) =>
            {
                if (_addIn != null && _addIn.CurrentClassId.HasValue)
                    await LoadParticipantsAsync(_addIn.CurrentClassId.Value);
            };
            _refreshTimer.Start();

            _confettiTimer = new Timer { Interval = 16 };
            _confettiTimer.Tick += (s, e) =>
            {
                DateTime now = DateTime.UtcNow;
                double totalSeconds = (now - _confettiStartedUtc).TotalSeconds;
                float deltaSeconds = (float)Math.Max(.001,
                    Math.Min(.05, (now - _confettiFrameUtc).TotalSeconds));
                _confettiFrameUtc = now;

                // Always terminate the effect even when Windows coalesces timer
                // messages while an API request or repaint is in progress.
                if (totalSeconds >= ConfettiMaximumSeconds)
                {
                    StopAndClearConfetti();
                    return;
                }

                for (int i = _confetti.Count - 1; i >= 0; i--)
                {
                    ConfettiParticle p = _confetti[i];
                    p.AgeSeconds += deltaSeconds;
                    p.X += p.Vx * deltaSeconds;
                    p.Y += p.Vy * deltaSeconds;
                    p.Vx *= (float)Math.Pow(.965, deltaSeconds * 60f);
                    p.Vy += 570f * deltaSeconds;
                    p.Rotation += p.Spin * deltaSeconds;
                    if (p.AgeSeconds >= p.LifetimeSeconds ||
                        p.Y > _surface.Height + 40)
                        _confetti.RemoveAt(i);
                }
                _surface.Invalidate();
                if (_confetti.Count == 0) StopAndClearConfetti();
            };
        }

        private void StopAndClearConfetti()
        {
            _confettiTimer?.Stop();
            _confetti.Clear();
            _surface?.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshTimer?.Stop(); _refreshTimer?.Dispose();
                _confettiTimer?.Stop(); _confettiTimer?.Dispose();
                _confetti.Clear();
                _qrImage?.Dispose(); _logoImage?.Dispose();
            }
            base.Dispose(disposing);
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
                try { if (File.Exists(path)) { using (var source = Image.FromFile(path)) _logoImage = new Bitmap(source); break; } }
                catch { }
            }
        }

        public async Task LoadParticipantsAsync(long classId)
        {
            if (_refreshing || _addIn == null) return;
            _refreshing = true;
            try
            {
                var participantsTask = _addIn.ApiClient.GetParticipantsAsync(classId);
                var onlineTask = _addIn.ApiClient.GetOnlineParticipantIdsAsync(classId);
                var classTask = _addIn.ApiClient.GetClassAsync(classId);
                await Task.WhenAll(participantsTask, onlineTask, classTask);
                List<Participant> fresh = participantsTask.Result ?? new List<Participant>();
                List<long> online = onlineTask.Result ?? new List<long>();

                _participants.Clear(); _participants.AddRange(fresh);
                _onlineIds.Clear(); foreach (long id in online) _onlineIds.Add(id);
                _currentClass = classTask.Result;
                _surface.Invalidate(); _footer.Invalidate();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("My Class refresh failed: " + ex.Message); }
            finally { _refreshing = false; }
        }

        private async Task LoadQrAsync()
        {
            try
            {
                string server = Properties.Settings.Default.ServerUrl ?? "http://localhost:8080";
                string endpoint = server.TrimEnd('/') + "/api/v1/qrcode?data=" + Uri.EscapeDataString(JoinLink);
                byte[] data;
                using (var client = new WebClient()) data = await client.DownloadDataTaskAsync(endpoint);
                using (var stream = new MemoryStream(data))
                using (var source = Image.FromStream(stream))
                {
                    Image old = _qrImage;
                    _qrImage = new Bitmap(source);
                    old?.Dispose();
                }
                _surface.Invalidate();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("QR load failed: " + ex.Message); }
        }

        internal void SetSearch(string value)
        {
            _search = value ?? "";
            _surface.Invalidate();
        }

        internal void ToggleSortMenu() { _sortOpen = !_sortOpen; _menuOpen = false; _surface.Invalidate(); }
        internal void ToggleMenu() { _menuOpen = !_menuOpen; _sortOpen = false; _surface.Invalidate(); }
        internal void SetSort(SortMode mode) { _sortMode = mode; _sortOpen = false; _surface.Invalidate(); }
        internal void ToggleOnlineFilter() { _onlineOnly = !_onlineOnly; _surface.Invalidate(); }

        internal void ShowQr() { _overlayMode = OverlayMode.Qr; _menuOpen = _sortOpen = false; _surface.Focus(); _surface.Invalidate(); }
        internal void RequestNewClass() { _overlayMode = OverlayMode.ConfirmNewClass; _surface.Invalidate(); }
        internal void RequestDelete(Participant participant) { _pendingParticipant = participant; _overlayMode = OverlayMode.ConfirmDelete; _surface.Invalidate(); }
        internal void CloseOverlay() { _overlayMode = OverlayMode.None; _pendingParticipant = null; _surface.Invalidate(); }

        internal async void ConfirmOverlay()
        {
            if (_overlayMode == OverlayMode.ConfirmDelete && _pendingParticipant != null)
                await DeleteParticipantAsync(_pendingParticipant);
            else if (_overlayMode == OverlayMode.ConfirmNewClass)
                await StartNewClassAsync();
            CloseOverlay();
        }

        internal async void AdjustStars(Participant participant, int delta)
        {
            if (participant == null || _addIn == null || !_addIn.CurrentClassId.HasValue) return;
            try
            {
                await _addIn.ApiClient.AdjustParticipantStarsAsync(_addIn.CurrentClassId.Value, participant.Id, delta);
                participant.TotalStars = Math.Max(0, participant.TotalStars + delta);
                if (delta > 0)
                {
                    LokalUi.PlayAddStarSound();
                    StartConfetti(30, _surface.LastPointer.X, _surface.LastPointer.Y);
                }
                await LoadParticipantsAsync(_addIn.CurrentClassId.Value);
            }
            catch (Exception ex) { ShowError("Could not update stars", ex); }
        }

        internal async void AwardStarsToAll()
        {
            if (_addIn == null || !_addIn.CurrentClassId.HasValue || _participants.Count == 0) return;
            try
            {
                long classId = _addIn.CurrentClassId.Value;
                foreach (Participant participant in _participants)
                    await _addIn.ApiClient.AdjustParticipantStarsAsync(classId, participant.Id, 1);
                foreach (Participant participant in _participants) participant.TotalStars++;
                LokalUi.PlayAddStarSound();
                StartConfetti(110, _surface.Width / 2, _surface.Height / 3);
                await LoadParticipantsAsync(classId);
            }
            catch (Exception ex) { ShowError("Could not award stars", ex); }
        }

        internal async void ToggleClassLock()
        {
            if (_addIn == null || !_addIn.CurrentClassId.HasValue) return;
            bool locked = !(_currentClass?.IsLocked ?? false);
            try
            {
                await _addIn.ApiClient.SetClassLockedAsync(_addIn.CurrentClassId.Value, locked);
                if (_currentClass != null) _currentClass.IsLocked = locked;
                _surface.Invalidate();
            }
            catch (Exception ex) { ShowError("Could not change class lock", ex); }
        }

        private async Task DeleteParticipantAsync(Participant participant)
        {
            if (_addIn == null || !_addIn.CurrentClassId.HasValue) return;
            try
            {
                await _addIn.ApiClient.DeleteParticipantAsync(_addIn.CurrentClassId.Value, participant.Id);
                _participants.RemoveAll(p => p.Id == participant.Id);
                _surface.Invalidate(); _footer.Invalidate();
            }
            catch (Exception ex) { ShowError("Could not remove participant", ex); }
        }

        private async Task StartNewClassAsync()
        {
            if (_addIn == null) return;
            try
            {
                if (_addIn.CurrentSessionId.HasValue && _addIn.CurrentClassId.HasValue)
                    await _addIn.SessionManager.StopSessionAsync(_addIn.CurrentSessionId.Value, _addIn.CurrentClassId.Value);
                AutoSessionResponse result = await _addIn.SessionManager.AutoStartSessionAsync();
                if (result == null) throw new Exception("The new class session could not be started.");
                _participants.Clear(); _onlineIds.Clear(); _currentClass = null;
                _addIn.ClassCodeBadge?.SetCode(_addIn.CurrentClassCode);
                _addIn.ClassCodeBadge?.SetParticipantCount(0);
                await LoadQrAsync();
                if (_addIn.CurrentClassId.HasValue) await LoadParticipantsAsync(_addIn.CurrentClassId.Value);
            }
            catch (Exception ex) { ShowError("Could not start a new class", ex); }
        }

        internal void OpenLeaderboard()
        {
            Hide();
            try { using (var dialog = new LeaderboardDialog(_addIn)) dialog.ShowDialog(); }
            finally { Close(); }
        }

        internal void OpenNamePicker()
        {
            _menuOpen = false; Hide();
            try { using (var dialog = new NamePickerDialog(_addIn)) dialog.ShowDialog(); }
            finally { Show(); BringToFront(); }
        }

        internal void OpenQuickPoll()
        {
            _menuOpen = false; Hide();
            try { using (var dialog = new QuickPollDialog(_addIn)) dialog.ShowDialog(); }
            finally { Show(); BringToFront(); }
        }

        internal void CopyJoinLink()
        {
            try { Clipboard.SetText(JoinLink); }
            catch { }
        }

        internal void StartConfetti(int count, int originX, int originY)
        {
            Color[] colors = { Color.FromArgb(255, 184, 0), Color.FromArgb(255, 48, 137), Color.FromArgb(14, 201, 152), LokalUi.PrimaryMedium, Color.FromArgb(255, 226, 15) };
            // Each click starts one fresh, short burst. Reusing particles caused
            // the opaque stationary pile seen after several star clicks.
            _confettiTimer.Stop();
            _confetti.Clear();
            _confettiStartedUtc = _confettiFrameUtc = DateTime.UtcNow;

            int particleCount = Math.Max(12, Math.Min(72, count));
            for (int i = 0; i < particleCount; i++)
            {
                _confetti.Add(new ConfettiParticle
                {
                    X = originX + _random.Next(-22, 23), Y = originY + _random.Next(-8, 9),
                    Vx = -230f + (float)_random.NextDouble() * 460f,
                    Vy = -470f + (float)_random.NextDouble() * 205f,
                    Rotation = _random.Next(360),
                    Spin = -420f + (float)_random.NextDouble() * 840f,
                    Size = _random.Next(5, 11),
                    AgeSeconds = 0f,
                    LifetimeSeconds = .92f + (float)_random.NextDouble() * .55f,
                    Color = colors[_random.Next(colors.Length)]
                });
            }
            _surface.Invalidate();
            _confettiTimer.Start();
        }

        private void ShowError(string title, Exception ex)
        {
            MessageBox.Show(title + ": " + ex.Message, "LOKAL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        internal sealed class ClassSurface : Control
        {
            private readonly MyClassForm _owner;
            private readonly TextBox _searchBox;
            private readonly List<CardHit> _cards = new List<CardHit>();
            private Rectangle _qrRect, _urlRect, _sortRect, _lockRect, _quickPollRect, _namePickerRect;
            private Rectangle _sortJoinRect, _sortNameRect, _sortOnlineRect;
            private Rectangle _overlayModal, _confirmRect, _cancelRect;
            private long _hoveredId;
            public Point LastPointer { get; private set; }

            public ClassSurface(MyClassForm owner)
            {
                _owner = owner;
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                _searchBox = new TextBox
                {
                    BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Ink,
                    Font = new Font("Segoe UI", 11f), Text = "Search name"
                };
                _searchBox.ForeColor = Muted;
                _searchBox.Enter += (s, e) =>
                {
                    if (_searchBox.Text == "Search name")
                    {
                        _searchBox.Text = string.Empty;
                        _searchBox.ForeColor = Ink;
                    }
                };
                _searchBox.Leave += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(_searchBox.Text))
                    {
                        _searchBox.Text = "Search name";
                        _searchBox.ForeColor = Muted;
                    }
                };
                _searchBox.TextChanged += (s, e) =>
                    _owner.SetSearch(_searchBox.Text == "Search name" ? string.Empty : _searchBox.Text);
                Controls.Add(_searchBox);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Canvas);
                _cards.Clear();
                int headerH = 102;
                DrawHeader(g, headerH);
                DrawWorkspace(g, headerH);
                if (_owner.SortOpen) DrawSortMenu(g);
                if (_owner.MenuOpen) DrawLauncherMenu(g);
                if (_owner.CurrentOverlay != OverlayMode.None) DrawOverlay(g);
                DrawConfetti(g);
            }

            private void DrawHeader(Graphics g, int headerH)
            {
                g.FillRectangle(Brushes.White, 0, 0, Width, headerH);
                using (var pen = new Pen(Color.FromArgb(235, 237, 244))) g.DrawLine(pen, 0, headerH - 1, Width, headerH - 1);
                int logoSize = 46;
                Rectangle logo = new Rectangle(Width / 2 - 150, (headerH - logoSize) / 2, logoSize, logoSize);
                DrawLogo(g, logo, _owner.LogoImage);
                using (var font = new Font("Segoe UI", 18f, FontStyle.Bold))
                    TextRenderer.DrawText(g, _owner.InstructorTitle, font,
                        new Rectangle(logo.Right + 12, 0, 290, headerH), Ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            private void DrawWorkspace(Graphics g, int top)
            {
                int toolbarH = 92;
                int leftWidth = Math.Max(330, Math.Min(440, Width * 31 / 100));
                int rightLeft = leftWidth + 1;
                using (var pen = new Pen(LokalUi.PrimaryLight, 1.5f))
                    g.DrawLine(pen, leftWidth, top + toolbarH + 14, leftWidth, Height - 42);

                Rectangle searchPill = new Rectangle(rightLeft + 30, top + 22, 270, 48);
                using (var path = RoundRect(searchPill, 24))
                using (var brush = new SolidBrush(Color.White))
                using (var pen = new Pen(Color.FromArgb(205, 209, 222))) { g.FillPath(brush, path); g.DrawPath(pen, path); }
                _searchBox.SetBounds(searchPill.Left + 18, searchPill.Top + 14, searchPill.Width - 58, 24);
                _searchBox.Visible = _owner.CurrentOverlay == OverlayMode.None;
                DrawSearch(g, searchPill.Right - 37, searchPill.Top + 14);

                string countText = _owner.VisibleParticipants.Count + " participant" + (_owner.VisibleParticipants.Count == 1 ? "" : "s") + " joined";
                using (var font = new Font("Segoe UI", 16f, FontStyle.Bold))
                    TextRenderer.DrawText(g, countText, font,
                        new Rectangle(rightLeft + 310, top + 15, Math.Max(150, Width - rightLeft - 500), 60), Ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                _sortRect = new Rectangle(Width - 155, top + 22, 125, 48);
                DrawPill(g, _sortRect, "☰   Sort by", Color.White, Ink);
                DrawLeftPanel(g, new Rectangle(0, top + toolbarH, leftWidth, Height - top - toolbarH));
                DrawParticipants(g, new Rectangle(rightLeft + 24, top + toolbarH, Width - rightLeft - 48, Height - top - toolbarH));
            }

            private void DrawLeftPanel(Graphics g, Rectangle area)
            {
                int qrSize = Math.Min(area.Width - 76, Math.Min(340, area.Height - 180));
                qrSize = Math.Max(220, qrSize);
                _qrRect = new Rectangle(area.Left + (area.Width - qrSize) / 2, area.Top + 10, qrSize, qrSize);
                Rectangle shadow = _qrRect; shadow.Offset(0, 5);
                using (var path = RoundRect(shadow, 22))
                using (var brush = new SolidBrush(Color.FromArgb(28, 50, 58, 120))) g.FillPath(brush, path);
                using (var path = RoundRect(_qrRect, 22))
                using (var brush = new SolidBrush(Color.White)) g.FillPath(brush, path);
                Rectangle imageRect = _qrRect; imageRect.Inflate(-22, -22);
                DrawQr(g, imageRect);

                int gap = 14;
                int infoW = (_qrRect.Width - gap) * 58 / 100;
                int infoH = 92;
                _urlRect = new Rectangle(_qrRect.Left, _qrRect.Bottom + 16, infoW, infoH);
                Rectangle code = new Rectangle(_urlRect.Right + gap, _urlRect.Top, _qrRect.Right - _urlRect.Right - gap, infoH);
                DrawInfoCard(g, _urlRect, "URL  ▣", ShortJoinLink(), true);
                DrawInfoCard(g, code, "Class code", _owner.ClassCode, false);
            }

            private string ShortJoinLink()
            {
                try { return new Uri(_owner.JoinLink).Authority; }
                catch { return _owner.JoinLink; }
            }

            private void DrawInfoCard(Graphics g, Rectangle rect, string title, string value, bool smaller)
            {
                using (var path = RoundRect(rect, 16))
                using (var brush = new SolidBrush(Color.White)) g.FillPath(brush, path);
                using (var font = new Font("Segoe UI", 9.5f))
                    TextRenderer.DrawText(g, title, font, new Rectangle(rect.Left + 14, rect.Top + 10, rect.Width - 28, 25), Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                using (var font = new Font("Segoe UI", smaller ? 11f : 14f, FontStyle.Bold))
                    TextRenderer.DrawText(g, value, font, new Rectangle(rect.Left + 10, rect.Top + 38, rect.Width - 20, 40), Indigo,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            private void DrawParticipants(Graphics g, Rectangle area)
            {
                IReadOnlyList<Participant> list = _owner.VisibleParticipants;
                if (list.Count == 0)
                {
                    DrawWaiting(g, area); return;
                }
                int gap = 18;
                int minCard = 150;
                int columns = Math.Max(1, Math.Min(5, (area.Width + gap) / (minCard + gap)));
                int cardW = Math.Min(180, (area.Width - gap * (columns - 1)) / columns);
                int cardH = 160;
                int totalW = cardW * columns + gap * (columns - 1);
                int startX = area.Left + Math.Max(0, (area.Width - totalW) / 2);
                int y = area.Top + 20;
                for (int i = 0; i < list.Count; i++)
                {
                    int row = i / columns, col = i % columns;
                    Rectangle rect = new Rectangle(startX + col * (cardW + gap), y + row * (cardH + 22), cardW, cardH);
                    if (rect.Bottom > Height - 15) break;
                    DrawParticipant(g, rect, list[i], list[i].Id == _hoveredId);
                }
            }

            private void DrawParticipant(Graphics g, Rectangle rect, Participant participant, bool hover)
            {
                Rectangle info = new Rectangle(rect.Left, rect.Top + 48, rect.Width, rect.Height - 48);
                Rectangle shadow = info; shadow.Offset(0, 4);
                using (var path = RoundRect(shadow, 14))
                using (var brush = new SolidBrush(Color.FromArgb(25, 60, 68, 125))) g.FillPath(brush, path);
                using (var path = RoundRect(info, 14))
                using (var brush = new SolidBrush(hover ? Color.FromArgb(255, 244, 196) : Color.White))
                using (var pen = new Pen(hover ? Color.FromArgb(255, 210, 76) : Color.FromArgb(223, 226, 242), hover ? 1.5f : 1f))
                { g.FillPath(brush, path); g.DrawPath(pen, path); }

                Rectangle avatar = new Rectangle(rect.Left + rect.Width / 2 - 37, rect.Top + 2, 74, 74);
                DrawAvatar(g, avatar, participant);
                DrawingUtil.DrawLevelBadge(g, new Rectangle(rect.Left + 8, rect.Top + 8, 40, 40), participant.Level);
                if (_owner.OnlineIds.Contains(participant.Id))
                {
                    using (var brush = new SolidBrush(Green)) g.FillEllipse(brush, avatar.Right - 9, avatar.Top + 4, 17, 17);
                    using (var pen = new Pen(Color.White, 2f)) g.DrawEllipse(pen, avatar.Right - 9, avatar.Top + 4, 17, 17);
                }

                if (hover)
                {
                    Rectangle delete = new Rectangle(rect.Right - 29, rect.Top + 4, 25, 25);
                    using (var brush = new SolidBrush(Color.FromArgb(225, 255, 255, 255))) g.FillEllipse(brush, delete);
                    using (var pen = new Pen(Color.FromArgb(240, 112, 119), 1.8f))
                    { g.DrawLine(pen, delete.Left + 7, delete.Top + 7, delete.Right - 7, delete.Bottom - 7); g.DrawLine(pen, delete.Right - 7, delete.Top + 7, delete.Left + 7, delete.Bottom - 7); }

                    Rectangle actions = new Rectangle(info.Left + 10, info.Bottom - 42, info.Width - 20, 34);
                    Rectangle plus = new Rectangle(actions.Left, actions.Top, actions.Width * 2 / 3, actions.Height);
                    Rectangle minus = new Rectangle(plus.Right, actions.Top, actions.Right - plus.Right, actions.Height);
                    using (var path = RoundRect(actions, 17))
                    using (var brush = new SolidBrush(LokalUi.PrimaryMedium)) g.FillPath(brush, path);
                    using (var brush = new SolidBrush(LokalUi.PrimaryPale)) g.FillRectangle(brush, minus);
                    DrawingUtil.DrawStar(g, plus.Left + 13, plus.Top + 7, 20, Color.FromArgb(251, 191, 36));
                    using (var font = new Font("Segoe UI", 10f, FontStyle.Bold))
                        TextRenderer.DrawText(g, "+1", font, new Rectangle(plus.Left + 40, plus.Top, plus.Width - 45, plus.Height), Color.White,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    DrawDownArrow(g, minus);
                    _cards.Add(new CardHit(participant, rect, plus, minus, delete));
                }
                else _cards.Add(new CardHit(participant, rect, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty));

                using (var font = new Font("Segoe UI", 10.3f, FontStyle.Bold))
                    TextRenderer.DrawText(g, participant.Name ?? "Student", font,
                        new Rectangle(info.Left + 8, info.Top + 30, info.Width - 16, 31), Ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                if (!hover)
                {
                    DrawingUtil.DrawStar(g, info.Left + info.Width / 2 - 25, info.Top + 68, 20, Color.FromArgb(251, 191, 36));
                    using (var font = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                        TextRenderer.DrawText(g, participant.TotalStars.ToString(), font,
                            new Rectangle(info.Left + info.Width / 2 + 2, info.Top + 64, 42, 28), Ink,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }

            private void DrawWaiting(Graphics g, Rectangle area)
            {
                int cx = area.Left + area.Width / 2, cy = area.Top + area.Height / 2 - 30;
                DrawPaperPlane(g, cx, cy - 45);
                string message = _owner.OnlineOnly ? "Waiting for online participants…" : "Waiting for participants to join…";
                using (var font = new Font("Segoe UI", 16f))
                    TextRenderer.DrawText(g, message, font, new Rectangle(area.Left, cy + 35, area.Width, 50), Indigo,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            private void DrawSortMenu(Graphics g)
            {
                Rectangle menu = new Rectangle(_sortRect.Right - 280, _sortRect.Bottom + 14, 280, 168);
                DrawPopup(g, menu);
                _sortJoinRect = new Rectangle(menu.Left, menu.Top, menu.Width, 53);
                _sortNameRect = new Rectangle(menu.Left, _sortJoinRect.Bottom, menu.Width, 53);
                _sortOnlineRect = new Rectangle(menu.Left, _sortNameRect.Bottom, menu.Width, menu.Bottom - _sortNameRect.Bottom);
                DrawMenuRow(g, _sortJoinRect, "Join order", _owner.CurrentSort == SortMode.JoinOrder);
                DrawMenuRow(g, _sortNameRect, "Name", _owner.CurrentSort == SortMode.Name);
                using (var pen = new Pen(Color.FromArgb(224, 227, 241))) g.DrawLine(pen, menu.Left, _sortOnlineRect.Top, menu.Right, _sortOnlineRect.Top);
                DrawSwitch(g, new Rectangle(menu.Left + 18, _sortOnlineRect.Top + 14, 42, 24), _owner.OnlineOnly);
                using (var font = new Font("Segoe UI", 10.5f))
                    TextRenderer.DrawText(g, "Online participants only", font,
                        new Rectangle(menu.Left + 72, _sortOnlineRect.Top, menu.Width - 84, _sortOnlineRect.Height), Ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            private void DrawLauncherMenu(Graphics g)
            {
                Rectangle menu = new Rectangle(Width - 330, Height - 220, 300, 190);
                DrawPopup(g, menu);
                _lockRect = new Rectangle(menu.Left + 16, menu.Top + 10, menu.Width - 32, 54);
                DrawSwitch(g, new Rectangle(_lockRect.Left, _lockRect.Top + 15, 42, 24), _owner.CurrentClass?.IsLocked ?? false);
                using (var font = new Font("Segoe UI", 10.5f))
                    TextRenderer.DrawText(g, "Lock class", font, new Rectangle(_lockRect.Left + 56, _lockRect.Top, 180, _lockRect.Height), Ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                using (var pen = new Pen(Color.FromArgb(224, 227, 241))) g.DrawLine(pen, menu.Left, _lockRect.Bottom + 4, menu.Right, _lockRect.Bottom + 4);
                _quickPollRect = new Rectangle(menu.Left + 16, _lockRect.Bottom + 18, 126, 92);
                _namePickerRect = new Rectangle(_quickPollRect.Right + 14, _quickPollRect.Top, 126, 92);
                DrawToolTile(g, _quickPollRect, "▥", "Quick Poll");
                DrawToolTile(g, _namePickerRect, "?", "Name Picker");
            }

            private void DrawOverlay(Graphics g)
            {
                _searchBox.Visible = false;
                using (var brush = new SolidBrush(Color.FromArgb(180, 24, 27, 39))) g.FillRectangle(brush, ClientRectangle);
                if (_owner.CurrentOverlay == OverlayMode.Qr)
                {
                    int size = Math.Min(680, Math.Min(Width - 90, Height - 90));
                    _overlayModal = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
                    DrawPopup(g, _overlayModal);
                    Rectangle qr = _overlayModal; qr.Inflate(-42, -42); DrawQr(g, qr);
                    return;
                }

                _overlayModal = new Rectangle(Width / 2 - 260, Height / 2 - 145, 520, 290);
                DrawPopup(g, _overlayModal);
                DrawWarning(g, _overlayModal.Left + 36, _overlayModal.Top + 46);
                string text = _owner.CurrentOverlay == OverlayMode.ConfirmDelete ?
                    "Remove " + (_owner.PendingParticipant?.Name ?? "this participant") + " from the class?" :
                    "Start a new class? This will end the current session and create a new class code.";
                using (var font = new Font("Segoe UI", 12f))
                    TextRenderer.DrawText(g, text, font,
                        new Rectangle(_overlayModal.Left + 100, _overlayModal.Top + 38, _overlayModal.Width - 135, 110), Ink,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
                Rectangle footer = new Rectangle(_overlayModal.Left, _overlayModal.Bottom - 82, _overlayModal.Width, 82);
                using (var brush = new SolidBrush(Color.FromArgb(246, 247, 255))) g.FillRectangle(brush, footer);
                _confirmRect = new Rectangle(_overlayModal.Right - 255, footer.Top + 18, 130, 48);
                _cancelRect = new Rectangle(_confirmRect.Right + 14, footer.Top + 18, 92, 48);
                DrawPill(g, _confirmRect, "Confirm", Indigo, Color.White);
                using (var font = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "Cancel", font, _cancelRect, Indigo,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            private void DrawConfetti(Graphics g)
            {
                foreach (ConfettiParticle p in _owner.Confetti)
                {
                    float progress = Math.Max(0f, Math.Min(1f,
                        p.AgeSeconds / Math.Max(.01f, p.LifetimeSeconds)));
                    int alpha = Math.Max(0, Math.Min(235,
                        (int)(235f * Math.Pow(1f - progress, .72))));
                    GraphicsState state = g.Save();
                    g.TranslateTransform(p.X, p.Y); g.RotateTransform(p.Rotation);
                    using (var brush = new SolidBrush(Color.FromArgb(alpha, p.Color)))
                    {
                        if (p.Size % 3 == 0) g.FillEllipse(brush, -p.Size / 2, -p.Size / 2, p.Size, p.Size);
                        else g.FillRectangle(brush, -p.Size / 2, -p.Size / 3, p.Size, p.Size * 2 / 3);
                    }
                    g.Restore(state);
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                LastPointer = e.Location;
                long nextHover = 0;
                foreach (CardHit card in _cards) if (card.Bounds.Contains(e.Location)) { nextHover = card.Participant.Id; break; }
                if (nextHover != _hoveredId) { _hoveredId = nextHover; Invalidate(); }
                bool hand = _qrRect.Contains(e.Location) || _urlRect.Contains(e.Location) || _sortRect.Contains(e.Location) ||
                    _cards.Any(c => c.Bounds.Contains(e.Location)) || _lockRect.Contains(e.Location) ||
                    _quickPollRect.Contains(e.Location) || _namePickerRect.Contains(e.Location) ||
                    _sortJoinRect.Contains(e.Location) || _sortNameRect.Contains(e.Location) || _sortOnlineRect.Contains(e.Location) ||
                    _confirmRect.Contains(e.Location) || _cancelRect.Contains(e.Location);
                Cursor = hand ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                LastPointer = e.Location;
                if (_owner.CurrentOverlay != OverlayMode.None)
                {
                    if (_owner.CurrentOverlay == OverlayMode.Qr && !_overlayModal.Contains(e.Location)) _owner.CloseOverlay();
                    else if (_confirmRect.Contains(e.Location)) _owner.ConfirmOverlay();
                    else if (_cancelRect.Contains(e.Location)) _owner.CloseOverlay();
                    return;
                }
                if (_qrRect.Contains(e.Location)) { _owner.ShowQr(); return; }
                if (_urlRect.Contains(e.Location)) { _owner.CopyJoinLink(); return; }
                if (_sortRect.Contains(e.Location)) { _owner.ToggleSortMenu(); return; }
                if (_owner.SortOpen)
                {
                    if (_sortJoinRect.Contains(e.Location)) _owner.SetSort(SortMode.JoinOrder);
                    else if (_sortNameRect.Contains(e.Location)) _owner.SetSort(SortMode.Name);
                    else if (_sortOnlineRect.Contains(e.Location)) _owner.ToggleOnlineFilter();
                    return;
                }
                if (_owner.MenuOpen)
                {
                    if (_lockRect.Contains(e.Location)) _owner.ToggleClassLock();
                    else if (_quickPollRect.Contains(e.Location)) _owner.OpenQuickPoll();
                    else if (_namePickerRect.Contains(e.Location)) _owner.OpenNamePicker();
                    return;
                }
                CardHit hit = _cards.FirstOrDefault(c => c.Bounds.Contains(e.Location));
                if (hit != null)
                {
                    if (hit.Plus.Contains(e.Location)) _owner.AdjustStars(hit.Participant, 1);
                    else if (hit.Minus.Contains(e.Location)) _owner.AdjustStars(hit.Participant, -1);
                    else if (hit.Delete.Contains(e.Location)) _owner.RequestDelete(hit.Participant);
                }
            }

            private void DrawQr(Graphics g, Rectangle rect)
            {
                using (var brush = new SolidBrush(Color.White)) g.FillRectangle(brush, rect);
                if (_owner.QrImage != null)
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(_owner.QrImage, rect);
                }
                else
                {
                    using (var font = new Font("Segoe UI", 12f))
                        TextRenderer.DrawText(g, "Generating QR code…", font, rect, Muted,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                int logoSize = Math.Max(42, rect.Width / 7);
                Rectangle logo = new Rectangle(rect.Left + (rect.Width - logoSize) / 2, rect.Top + (rect.Height - logoSize) / 2, logoSize, logoSize);
                g.FillEllipse(Brushes.White, logo.Left - 6, logo.Top - 6, logo.Width + 12, logo.Height + 12);
                DrawLogo(g, logo, _owner.LogoImage);
            }

            private static void DrawLogo(Graphics g, Rectangle rect, Image logo)
            {
                if (logo != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(logo, rect); }
                else
                {
                    using (var brush = new SolidBrush(Teal)) g.FillEllipse(brush, rect);
                    using (var font = new Font("Segoe UI", rect.Height * .38f, FontStyle.Bold))
                    using (var sf = CenterFormat()) g.DrawString("L", font, Brushes.White, rect, sf);
                }
            }

            private static void DrawAvatar(Graphics g, Rectangle rect, Participant participant)
            {
                Color a = DrawingUtil.AvatarColor(participant.Name, 0), b = DrawingUtil.AvatarColor(participant.Name, 1);
                using (var brush = new LinearGradientBrush(rect, a, b, 45f)) g.FillEllipse(brush, rect);
                using (var pen = new Pen(Color.White, 3f)) g.DrawEllipse(pen, rect);
                using (var font = new Font("Segoe UI", 17f, FontStyle.Bold))
                using (var sf = CenterFormat()) g.DrawString(DrawingUtil.Initials(participant.Name), font, Brushes.White, rect, sf);
            }

            private static void DrawSearch(Graphics g, int x, int y)
            {
                using (var pen = new Pen(Ink, 2.4f)) { g.DrawEllipse(pen, x, y, 15, 15); g.DrawLine(pen, x + 12, y + 13, x + 21, y + 22); }
            }

            private static void DrawDownArrow(Graphics g, Rectangle rect)
            {
                using (var pen = new Pen(Color.FromArgb(239, 110, 118), 2f))
                { g.DrawLine(pen, rect.Left + rect.Width / 2, rect.Top + 8, rect.Left + rect.Width / 2, rect.Bottom - 11); g.DrawLine(pen, rect.Left + rect.Width / 2, rect.Bottom - 11, rect.Left + rect.Width / 2 - 6, rect.Bottom - 17); g.DrawLine(pen, rect.Left + rect.Width / 2, rect.Bottom - 11, rect.Left + rect.Width / 2 + 6, rect.Bottom - 17); }
            }

            private static void DrawPaperPlane(Graphics g, int cx, int cy)
            {
                Point[] outer = { new Point(cx - 54, cy - 27), new Point(cx + 62, cy), new Point(cx - 30, cy + 45), new Point(cx - 9, cy + 8) };
                using (var brush = new SolidBrush(Color.FromArgb(255, 73, 74))) g.FillPolygon(brush, outer);
                Point[] inner = { new Point(cx - 9, cy + 8), new Point(cx + 62, cy), new Point(cx - 42, cy - 1) };
                using (var brush = new SolidBrush(Color.FromArgb(203, 15, 31))) g.FillPolygon(brush, inner);
            }

            private static void DrawWarning(Graphics g, int x, int y)
            {
                Point[] triangle = { new Point(x + 20, y), new Point(x + 42, y + 40), new Point(x - 2, y + 40) };
                using (var brush = new SolidBrush(Color.FromArgb(255, 184, 0))) g.FillPolygon(brush, triangle);
                using (var font = new Font("Segoe UI", 14f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "!", font, new Rectangle(x + 9, y + 9, 22, 28), Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            private static void DrawPopup(Graphics g, Rectangle rect)
            {
                Rectangle shadow = rect; shadow.Offset(0, 8);
                using (var path = RoundRect(shadow, 18))
                using (var brush = new SolidBrush(Color.FromArgb(38, 0, 0, 0))) g.FillPath(brush, path);
                using (var path = RoundRect(rect, 18))
                using (var brush = new SolidBrush(Color.White)) g.FillPath(brush, path);
            }

            private static void DrawMenuRow(Graphics g, Rectangle rect, string text, bool selected)
            {
                if (selected) using (var brush = new SolidBrush(Color.FromArgb(235, 238, 255))) g.FillRectangle(brush, rect);
                using (var font = new Font("Segoe UI", 10.5f))
                    TextRenderer.DrawText(g, (selected ? "✓   " : "      ") + text, font,
                        new Rectangle(rect.Left + 16, rect.Top, rect.Width - 32, rect.Height), selected ? Indigo : Ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            private static void DrawToolTile(Graphics g, Rectangle rect, string icon, string text)
            {
                using (var path = RoundRect(rect, 12))
                using (var brush = new SolidBrush(Color.FromArgb(247, 248, 255))) g.FillPath(brush, path);
                using (var font = new Font("Segoe UI", 20f, FontStyle.Bold))
                    TextRenderer.DrawText(g, icon, font, new Rectangle(rect.Left, rect.Top + 7, rect.Width, 42), Teal,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                using (var font = new Font("Segoe UI", 9.3f))
                    TextRenderer.DrawText(g, text, font, new Rectangle(rect.Left, rect.Top + 50, rect.Width, 34), Ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        internal sealed class ClassFooter : Control
        {
            private readonly MyClassForm _owner;
            private Rectangle _newRect, _trophyRect, _awardRect, _menuRect;
            public ClassFooter(MyClassForm owner) { _owner = owner; DoubleBuffered = true; }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.White);
                using (var pen = new Pen(Color.FromArgb(235, 237, 244))) g.DrawLine(pen, 0, 0, Width, 0);
                int y = 20, h = 56;
                int newW = Math.Max(210, Math.Min(275, Width / 5));
                int awardW = Math.Max(235, Math.Min(290, Width / 5));
                int trophy = 64, gap = 28;
                int total = newW + awardW + trophy + gap * 2;
                int x = (Width - total) / 2;
                _newRect = new Rectangle(x, y, newW, h);
                _trophyRect = new Rectangle(_newRect.Right + gap, 14, trophy, 68);
                _awardRect = new Rectangle(_trophyRect.Right + gap, y, awardW, h);
                _menuRect = new Rectangle(Width - 76, 20, 54, 54);
                DrawPill(g, _newRect, "Start new class", Red, Color.White);
                using (var brush = new SolidBrush(Color.FromArgb(248, 249, 255))) g.FillEllipse(brush, _trophyRect);
                using (var pen = new Pen(Indigo, 2f)) g.DrawEllipse(pen, _trophyRect);
                DrawingUtil.DrawTrophy(g, new Rectangle(_trophyRect.Left + 16, _trophyRect.Top + 13, 36, 39), Color.FromArgb(242, 177, 18));
                DrawPill(g, _awardRect, "★  Award stars to all", Indigo, Color.White);
                using (var font = new Font("Segoe UI", 22f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "≡", font, _menuRect, Ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (_owner.CurrentOverlay != OverlayMode.None)
                    using (var dim = new SolidBrush(Color.FromArgb(155, 19, 22, 34)))
                        g.FillRectangle(dim, ClientRectangle);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                Cursor = (_newRect.Contains(e.Location) || _trophyRect.Contains(e.Location) || _awardRect.Contains(e.Location) || _menuRect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                if (_owner.CurrentOverlay != OverlayMode.None) return;
                if (_newRect.Contains(e.Location)) _owner.RequestNewClass();
                else if (_trophyRect.Contains(e.Location)) _owner.OpenLeaderboard();
                else if (_awardRect.Contains(e.Location)) _owner.AwardStarsToAll();
                else if (_menuRect.Contains(e.Location)) _owner.ToggleMenu();
            }
        }

        internal sealed class CardHit
        {
            public Participant Participant; public Rectangle Bounds, Plus, Minus, Delete;
            public CardHit(Participant participant, Rectangle bounds, Rectangle plus, Rectangle minus, Rectangle delete)
            { Participant = participant; Bounds = bounds; Plus = plus; Minus = minus; Delete = delete; }
        }

        internal sealed class ConfettiParticle
        {
            public float X, Y, Vx, Vy, Rotation, Spin;
            public float AgeSeconds, LifetimeSeconds;
            public int Size;
            public Color Color;
        }

        internal static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }

        internal static StringFormat CenterFormat() { return new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }; }

        internal static void DrawPill(Graphics g, Rectangle rect, string text, Color fill, Color foreground)
        {
            using (var path = RoundRect(rect, rect.Height / 2))
            using (var brush = new LinearGradientBrush(rect, ControlPaint.Light(fill, .08f), fill, 90f)) g.FillPath(brush, path);
            using (var font = new Font("Segoe UI", 11f, FontStyle.Bold))
                TextRenderer.DrawText(g, text, font, rect, foreground,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        internal static void DrawSwitch(Graphics g, Rectangle rect, bool on)
        {
            using (var path = RoundRect(rect, rect.Height / 2))
            using (var brush = new SolidBrush(on ? Green : Color.FromArgb(229, 232, 248))) g.FillPath(brush, path);
            int d = rect.Height - 6, x = on ? rect.Right - d - 3 : rect.Left + 3;
            g.FillEllipse(Brushes.White, x, rect.Top + 3, d, d);
        }
    }
}
