using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// Responsive, presentation-first Name Picker. Card and wheel views share one
    /// participant pool, support real online-presence filtering, auto-pick, picked
    /// history, put-back, reset, and dashboard star awards.
    /// </summary>
    public sealed class NamePickerDialog : Form
    {
        private const string SpinnerSoundAlias = "lokal_name_picker_spinner";
        private readonly ThisAddIn _addIn;
        private readonly Random _random = new Random();
        private readonly List<Participant> _all = new List<Participant>();
        private readonly List<Participant> _pool = new List<Participant>();
        private readonly List<Participant> _picked = new List<Participant>();
        private readonly HashSet<long> _onlineIds = new HashSet<long>();
        private readonly HashSet<long> _revealedCards = new HashSet<long>();
        private readonly Dictionary<long, string> _cardSymbols = new Dictionary<long, string>();

        private PickerHeader _header;
        private PickerSurface _surface;
        private PickerFooter _footer;
        private PickerOverlay _overlay;
        private Timer _spinTimer;
        private Timer _presenceTimer;
        private bool _refreshing;
        private bool _isWheelView = true;
        private bool _onlineOnly;
        private bool _isSpinning;
        private double _wheelAngle;
        private double _wheelVelocity;

        private static readonly string[] CardSymbols =
        {
            "🍎", "🍪", "🍭", "🎱", "⚽", "🍇", "🏀", "🍔", "🍿", "🍕",
            "🥝", "🎾", "🍩", "🌮", "🧁", "🍓", "🥐", "🎯", "🍌", "🥥"
        };

        internal static readonly Color Ink = Color.FromArgb(48, 52, 72);
        internal static readonly Color Muted = Color.FromArgb(113, 119, 145);
        internal static readonly Color Indigo = LokalUi.Primary;
        internal static readonly Color IndigoSoft = LokalUi.PrimaryLight;
        internal static readonly Color Canvas = LokalUi.PrimaryPale;
        internal static readonly Color FooterBg = Color.FromArgb(253, 253, 255);
        internal static readonly Color Teal = Color.FromArgb(13, 205, 164);
        internal static readonly Color Reset = Color.FromArgb(222, 96, 103);

        internal static readonly Color[] WheelColors =
        {
            LokalUi.Primary, Color.FromArgb(239, 62, 105),
            Color.FromArgb(255, 186, 9), Color.FromArgb(10, 204, 158),
            Color.FromArgb(48, 99, 224), Color.FromArgb(245, 117, 53),
            LokalUi.Primary, Color.FromArgb(32, 174, 202)
        };

        public NamePickerDialog(ThisAddIn addIn)
        {
            _addIn = addIn;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BuildUi();
            Shown += async (s, e) => await RefreshParticipantsAsync(true);
        }

        internal IReadOnlyList<Participant> Pool { get { return _pool; } }
        internal IReadOnlyList<Participant> Picked { get { return _picked; } }
        internal bool IsWheelView { get { return _isWheelView; } }
        internal bool OnlineOnly { get { return _onlineOnly; } }
        internal bool IsSpinning { get { return _isSpinning; } }
        internal double WheelAngle { get { return _wheelAngle; } }

        private void BuildUi()
        {
            Text = "LOKAL — Name Picker";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(900, 620);
            BackColor = Color.White;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            LokalUi.ApplyBrandIcon(this);

            Rectangle working = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Max(1000, (int)(working.Width * 0.75)),
                Math.Max(680, (int)(working.Height * 0.82)));

            _surface = new PickerSurface(this) { Dock = DockStyle.Fill, BackColor = Canvas };
            _footer = new PickerFooter(this) { Dock = DockStyle.Bottom, Height = 88, BackColor = FooterBg };
            _header = new PickerHeader(this) { Dock = DockStyle.Top, Height = 96, BackColor = Color.White };
            Controls.Add(_surface);
            Controls.Add(_footer);
            Controls.Add(_header);

            _spinTimer = new Timer { Interval = 16 };
            _spinTimer.Tick += SpinTick;
            _presenceTimer = new Timer { Interval = 1800 };
            _presenceTimer.Tick += async (s, e) => await RefreshParticipantsAsync(false);
            _presenceTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _spinTimer?.Stop();
                _spinTimer?.Dispose();
                _presenceTimer?.Stop();
                _presenceTimer?.Dispose();
                LokalUi.StopSoundAsset(SpinnerSoundAlias);
            }
            base.Dispose(disposing);
        }

        internal void ToggleView()
        {
            if (_isSpinning || _overlay != null) return;
            _isWheelView = !_isWheelView;
            _header.Invalidate();
            _surface.Invalidate();
        }

        internal void ToggleOnlineOnly()
        {
            if (_isSpinning || _overlay != null) return;
            _onlineOnly = !_onlineOnly;
            RebuildPool(true);
            _header.Invalidate();
        }

        internal void StartSpin()
        {
            if (!_isWheelView || _isSpinning || _overlay != null || _pool.Count == 0) return;
            _isSpinning = true;
            _wheelVelocity = 19d + _random.NextDouble() * 8d;
            LokalUi.PlaySoundAsset("spinner.mp3", SpinnerSoundAlias, true);
            _spinTimer.Start();
            _footer.Invalidate();
        }

        private void SpinTick(object sender, EventArgs e)
        {
            _wheelAngle = (_wheelAngle + _wheelVelocity) % 360d;
            _wheelVelocity *= 0.985d;
            _surface.Invalidate();
            if (_wheelVelocity >= .14d) return;

            _spinTimer.Stop();
            LokalUi.StopSoundAsset(SpinnerSoundAlias);
            _isSpinning = false;
            int index = WinnerIndex();
            if (index >= 0)
            {
                Participant winner = _pool[index];
                _pool.RemoveAt(index);
                AddPicked(winner);
                ShowResult(winner);
                System.Media.SystemSounds.Exclamation.Play();
            }
            _footer.Invalidate();
        }

        private int WinnerIndex()
        {
            if (_pool.Count == 0) return -1;
            double sweep = 360d / _pool.Count;
            double normalized = ((-_wheelAngle % 360d) + 360d) % 360d;
            return Math.Min(_pool.Count - 1, (int)(normalized / sweep));
        }

        internal void RevealCard(Participant participant)
        {
            if (participant == null) return;
            _revealedCards.Add(participant.Id);
            _surface.Invalidate();
            System.Media.SystemSounds.Asterisk.Play();
        }

        internal void ShowAutoPick()
        {
            if (_isSpinning || _pool.Count == 0 || _overlay != null) return;
            ShowOverlay(PickerOverlay.ForCount(this));
        }

        internal void AutoPick(int count)
        {
            if (_pool.Count == 0) return;
            count = Math.Max(1, Math.Min(count, _pool.Count));
            var selected = new List<Participant>();
            for (int i = 0; i < count; i++)
            {
                int index = _random.Next(_pool.Count);
                Participant participant = _pool[index];
                _pool.RemoveAt(index);
                AddPicked(participant);
                selected.Add(participant);
            }
            HideOverlay();
            if (selected.Count == 1) ShowResult(selected[0]);
            else ShowOverlay(PickerOverlay.ForMultiple(this, selected));
            _surface.Invalidate();
        }

        private void AddPicked(Participant participant)
        {
            if (_picked.All(p => p.Id != participant.Id)) _picked.Add(participant);
        }

        internal void PutBack(Participant participant)
        {
            if (participant == null) return;
            _picked.RemoveAll(p => p.Id == participant.Id);
            if (EligibleParticipants().Any(p => p.Id == participant.Id) && _pool.All(p => p.Id != participant.Id))
                _pool.Add(participant);
            HideOverlay();
            _surface.Invalidate();
        }

        internal async void AwardStar(Participant participant)
        {
            if (participant == null || _addIn == null || !_addIn.CurrentClassId.HasValue) return;
            try
            {
                Participant updated = await _addIn.ApiClient.AdjustParticipantStarsAsync(
                    _addIn.CurrentClassId.Value, participant.Id, 1);
                if (updated != null)
                {
                    participant.TotalStars = updated.TotalStars;
                    participant.Level = updated.Level;
                }
                else participant.TotalStars++;
                LokalUi.PlayAddStarSound();
                _overlay?.MarkStarAwarded();
                _surface.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not award the star: " + ex.Message, "LOKAL",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowResult(Participant participant)
        {
            ShowOverlay(PickerOverlay.ForResult(this, participant));
        }

        internal void ResetAll()
        {
            if (_isSpinning) return;
            HideOverlay();
            _picked.Clear();
            _revealedCards.Clear();
            _wheelAngle = 0;
            RebuildPool(false);
        }

        internal void ShowOverlay(PickerOverlay overlay)
        {
            HideOverlay();
            _overlay = overlay;
            _overlay.Dock = DockStyle.Fill;
            _surface.Controls.Add(_overlay);
            _overlay.BringToFront();
            _surface.Invalidate();
        }

        internal void HideOverlay()
        {
            if (_overlay == null) return;
            _surface.Controls.Remove(_overlay);
            _overlay.Dispose();
            _overlay = null;
            _surface.Invalidate();
        }

        private async System.Threading.Tasks.Task RefreshParticipantsAsync(bool force)
        {
            if (_refreshing || _addIn == null || !_addIn.CurrentClassId.HasValue) return;
            if (!force && (_isSpinning || _overlay != null)) return;
            _refreshing = true;
            try
            {
                long classId = _addIn.CurrentClassId.Value;
                List<Participant> participants = await _addIn.ApiClient.GetParticipantsAsync(classId)
                    ?? new List<Participant>();
                List<long> online = await _addIn.ApiClient.GetOnlineParticipantIdsAsync(classId)
                    ?? new List<long>();

                string before = string.Join("|", _all.Select(p => p.Id + ":" + p.Name)) + "/" +
                    string.Join(",", _onlineIds.OrderBy(id => id));
                string after = string.Join("|", participants.Select(p => p.Id + ":" + p.Name)) + "/" +
                    string.Join(",", online.OrderBy(id => id));
                if (force || !string.Equals(before, after, StringComparison.Ordinal))
                {
                    _all.Clear();
                    _all.AddRange(participants.OrderBy(p => p.Name));
                    _onlineIds.Clear();
                    foreach (long id in online) _onlineIds.Add(id);
                    AssignCardSymbols();
                    RebuildPool(false);
                }
            }
            catch
            {
                // Keep the last successful participant snapshot during a brief API outage.
            }
            finally { _refreshing = false; }
        }

        private IEnumerable<Participant> EligibleParticipants()
        {
            return _onlineOnly ? _all.Where(p => _onlineIds.Contains(p.Id)) : _all;
        }

        private void RebuildPool(bool clearSelections)
        {
            if (clearSelections)
            {
                _picked.Clear();
                _revealedCards.Clear();
            }
            var pickedIds = new HashSet<long>(_picked.Select(p => p.Id));
            _pool.Clear();
            _pool.AddRange(EligibleParticipants().Where(p => !pickedIds.Contains(p.Id)));
            _surface?.Invalidate();
            _footer?.Invalidate();
        }

        private void AssignCardSymbols()
        {
            foreach (Participant participant in _all)
            {
                if (!_cardSymbols.ContainsKey(participant.Id))
                    _cardSymbols[participant.Id] = CardSymbols[_random.Next(CardSymbols.Length)];
            }
        }

        internal string CardSymbol(Participant participant)
        {
            string symbol;
            return participant != null && _cardSymbols.TryGetValue(participant.Id, out symbol) ? symbol : "⭐";
        }

        internal bool IsCardRevealed(long id) { return _revealedCards.Contains(id); }

        internal sealed class PickerHeader : Control
        {
            private readonly NamePickerDialog _owner;
            private Rectangle _toggleRect;
            private Rectangle _viewRect;
            private Image _logo;

            public PickerHeader(NamePickerDialog owner)
            {
                _owner = owner;
                DoubleBuffered = true;
                Cursor = Cursors.Default;
                TryLoadLogo();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _logo?.Dispose();
                base.Dispose(disposing);
            }

            private void TryLoadLogo()
            {
                string[] candidates =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "android-chrome-192x192.png"),
                    @"C:\xampp\htdocs\LOKAL-ThesisSys\assets\android-chrome-192x192.png"
                };
                foreach (string path in candidates)
                {
                    try { if (File.Exists(path)) { using (var source = Image.FromFile(path)) _logo = new Bitmap(source); break; } }
                    catch { }
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Color.White);
                using (var pen = new Pen(Color.FromArgb(233, 235, 244))) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

                int logo = Math.Max(38, Math.Min(48, Height - 40));
                Rectangle logoRect = new Rectangle(34, (Height - logo) / 2, logo, logo);
                if (_logo != null) g.DrawImage(_logo, logoRect);
                else
                {
                    using (var brush = new SolidBrush(LokalUi.Primary)) g.FillEllipse(brush, logoRect);
                    using (var font = new Font("Segoe UI", 17f, FontStyle.Bold))
                    using (var sf = CenterFormat()) g.DrawString("L", font, Brushes.White, logoRect, sf);
                }

                Rectangle titleRect = new Rectangle(logoRect.Right + 14, 0, 280, Height);
                using (var font = new Font("Segoe UI", 19f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "Name Picker", font, titleRect, Ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                int switchW = 58, switchH = 32;
                int labelW = Math.Min(340, Math.Max(220, Width / 4));
                int groupW = switchW + 16 + labelW;
                int groupX = Math.Max(titleRect.Right + 20, (Width - groupW) / 2);
                _toggleRect = new Rectangle(groupX, (Height - switchH) / 2, switchW, switchH);
                PaintSwitch(g, _toggleRect, _owner.OnlineOnly);
                Rectangle onlineText = new Rectangle(_toggleRect.Right + 16, 0, labelW, Height);
                using (var font = new Font("Segoe UI", 12.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "Show online participants only", font, onlineText, Ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                string viewText = _owner.IsWheelView ? "Change to Card view" : "Change to Wheel view";
                using (var font = new Font("Segoe UI", 12.5f, FontStyle.Bold))
                {
                    Size measured = TextRenderer.MeasureText(viewText, font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding);
                    _viewRect = new Rectangle(Width - measured.Width - 34, 0, measured.Width, Height);
                    TextRenderer.DrawText(g, viewText, font, _viewRect, Indigo,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                Cursor = (_toggleRect.Contains(e.Location) || _viewRect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                if (_toggleRect.Contains(e.Location)) _owner.ToggleOnlineOnly();
                else if (_viewRect.Contains(e.Location)) _owner.ToggleView();
            }

            private static void PaintSwitch(Graphics g, Rectangle rect, bool on)
            {
                using (var path = RoundRect(rect, rect.Height / 2))
                using (var brush = new SolidBrush(on ? Teal : LokalUi.PrimaryLight)) g.FillPath(brush, path);
                int d = rect.Height - 6;
                int x = on ? rect.Right - d - 3 : rect.Left + 3;
                Rectangle knob = new Rectangle(x, rect.Top + 3, d, d);
                using (var shadow = new SolidBrush(Color.FromArgb(35, 50, 56, 100))) g.FillEllipse(shadow, knob.Left + 1, knob.Top + 2, d, d);
                g.FillEllipse(Brushes.White, knob);
            }
        }

        internal sealed class PickerFooter : Control
        {
            private readonly NamePickerDialog _owner;
            private Rectangle _autoRect;
            private Rectangle _resetRect;
            public PickerFooter(NamePickerDialog owner) { _owner = owner; DoubleBuffered = true; }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(FooterBg);
                using (var pen = new Pen(Color.FromArgb(235, 237, 246))) g.DrawLine(pen, 0, 0, Width, 0);
                int buttonW = Math.Max(190, Math.Min(270, Width / 5));
                int buttonH = 54;
                int gap = Math.Max(22, Math.Min(36, Width / 30));
                int x = (Width - buttonW * 2 - gap) / 2;
                int y = (Height - buttonH) / 2;
                _autoRect = new Rectangle(x, y, buttonW, buttonH);
                _resetRect = new Rectangle(x + buttonW + gap, y, buttonW, buttonH);
                DrawButton(g, _autoRect, "Auto pick", IndigoSoft, Indigo, _owner.Pool.Count > 0 && !_owner.IsSpinning);
                DrawButton(g, _resetRect, "Reset", Reset, Color.White, !_owner.IsSpinning);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                Cursor = (_autoRect.Contains(e.Location) || _resetRect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                if (_autoRect.Contains(e.Location)) _owner.ShowAutoPick();
                else if (_resetRect.Contains(e.Location)) _owner.ResetAll();
            }

            private static void DrawButton(Graphics g, Rectangle rect, string text, Color fill, Color textColor, bool enabled)
            {
                if (!enabled) { fill = Color.FromArgb(240, 242, 250); textColor = Color.FromArgb(171, 177, 202); }
                using (var path = RoundRect(rect, rect.Height / 2))
                using (var brush = new LinearGradientBrush(rect, ControlPaint.Light(fill, .08f), fill, 90f)) g.FillPath(brush, path);
                using (var font = new Font("Segoe UI", 11.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, text, font, rect, textColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        internal sealed class PickerSurface : Control
        {
            private readonly NamePickerDialog _owner;
            private readonly List<Tuple<Rectangle, Participant>> _cardHits = new List<Tuple<Rectangle, Participant>>();
            private Rectangle _wheelHit;
            private Image _iconSprite;

            public PickerSurface(NamePickerDialog owner)
            {
                _owner = owner;
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                LoadIconSprite();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _iconSprite?.Dispose();
                base.Dispose(disposing);
            }

            private void LoadIconSprite()
            {
                string[] candidates =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "name-picker-icons.png"),
                    @"C:\xampp\htdocs\LOKAL-ThesisSys\assets\name-picker-icons.png"
                };
                foreach (string path in candidates)
                {
                    try { if (File.Exists(path)) { using (var source = Image.FromFile(path)) _iconSprite = new Bitmap(source); break; } }
                    catch { }
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                e.Graphics.Clear(Canvas);
                _cardHits.Clear();
                _wheelHit = Rectangle.Empty;
                if (_owner.IsWheelView) DrawWheelView(e.Graphics);
                else DrawCardView(e.Graphics);
            }

            private void DrawCardView(Graphics g)
            {
                int top = 28;
                using (var font = new Font("Segoe UI", 13f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "Click on any card to reveal a participant's name", font,
                        new Rectangle(30, top, Width - 60, 40), Ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                IReadOnlyList<Participant> list = _owner.Pool;
                if (list.Count == 0) { DrawEmpty(g); return; }
                int maxCardW = 250;
                int minCardW = 150;
                int gap = 18;
                int available = Width - 110;
                int columns = Math.Max(1, Math.Min(5, (available + gap) / (minCardW + gap)));
                int cardW = Math.Min(maxCardW, (available - gap * (columns - 1)) / columns);
                int cardH = Math.Max(118, Math.Min(148, (Height - 130) / Math.Max(1, (int)Math.Ceiling(list.Count / (double)columns)) - 16));
                int totalW = cardW * columns + gap * (columns - 1);
                int startX = (Width - totalW) / 2;
                int startY = top + 58;

                for (int i = 0; i < list.Count; i++)
                {
                    int row = i / columns, col = i % columns;
                    Rectangle card = new Rectangle(startX + col * (cardW + gap),
                        startY + row * (cardH + gap), cardW, cardH);
                    if (card.Bottom > Height - 18) break;
                    Participant participant = list[i];
                    DrawParticipantCard(g, card, participant, _owner.IsCardRevealed(participant.Id));
                    _cardHits.Add(Tuple.Create(card, participant));
                }
            }

            private void DrawParticipantCard(Graphics g, Rectangle card, Participant participant, bool revealed)
            {
                Rectangle shadow = card; shadow.Offset(0, 5);
                using (var path = RoundRect(shadow, 16))
                using (var brush = new SolidBrush(Color.FromArgb(30, 85, 93, 146))) g.FillPath(brush, path);
                using (var path = RoundRect(card, 16))
                using (var brush = new LinearGradientBrush(card,
                    revealed ? Color.White : LokalUi.PrimaryLight,
                    revealed ? Color.FromArgb(248, 250, 255) : LokalUi.PrimaryPale, 90f))
                using (var pen = new Pen(revealed ? Teal : LokalUi.PrimaryMedium, revealed ? 2f : 1f))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }

                if (!revealed)
                {
                    string symbol = _owner.CardSymbol(participant);
                    int iconIndex = Array.IndexOf(CardSymbols, symbol);
                    if (_iconSprite != null && iconIndex >= 0)
                    {
                        int size = Math.Min(112, card.Height - 14);
                        Rectangle destination = new Rectangle(card.Left + (card.Width - size) / 2,
                            card.Top + (card.Height - size) / 2, size, size);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(_iconSprite, destination, new Rectangle(iconIndex * 128, 0, 128, 128), GraphicsUnit.Pixel);
                    }
                    else
                    {
                        using (var font = new Font("Segoe UI Emoji", Math.Max(30f, card.Height * .36f)))
                            TextRenderer.DrawText(g, symbol, font, card, Color.Black,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    }
                    return;
                }

                int avatar = Math.Min(58, card.Height / 2);
                Rectangle avatarRect = new Rectangle(card.Left + (card.Width - avatar) / 2, card.Top + 17, avatar, avatar);
                DrawAvatar(g, avatarRect, participant);
                Rectangle nameRect = new Rectangle(card.Left + 10, avatarRect.Bottom + 10, card.Width - 20, card.Bottom - avatarRect.Bottom - 15);
                using (var font = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, participant.Name ?? "Student", font, nameRect, Ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            private void DrawWheelView(Graphics g)
            {
                if (_owner.Pool.Count == 0) { DrawEmpty(g); return; }
                bool showHistory = _owner.Picked.Count > 0 && Width >= 980;
                int historyW = showHistory ? Math.Max(285, Math.Min(390, Width / 4)) : 0;
                Rectangle wheelArea = new Rectangle(18, 14, Width - historyW - 36, Height - 28);
                int wheelSize = Math.Min(wheelArea.Width - 70, wheelArea.Height - 35);
                wheelSize = Math.Max(220, wheelSize);
                int cx = wheelArea.Left + wheelArea.Width / 2;
                int cy = wheelArea.Top + wheelArea.Height / 2;
                Rectangle wheel = new Rectangle(cx - wheelSize / 2, cy - wheelSize / 2, wheelSize, wheelSize);
                _wheelHit = wheel;
                DrawWheel(g, wheel);
                if (showHistory) DrawPickedHistory(g, new Rectangle(Width - historyW, 30, historyW - 24, Height - 60));
            }

            private void DrawWheel(Graphics g, Rectangle wheel)
            {
                Rectangle shadow = wheel; shadow.Inflate(10, 10); shadow.Offset(0, 7);
                using (var brush = new SolidBrush(Color.FromArgb(24, 54, 63, 128))) g.FillEllipse(brush, shadow);
                Rectangle ring = wheel; ring.Inflate(8, 8);
                using (var brush = new SolidBrush(LokalUi.PrimaryLight)) g.FillEllipse(brush, ring);
                g.FillEllipse(Brushes.White, wheel);

                int n = _owner.Pool.Count;
                float sweep = 360f / n;
                for (int i = 0; i < n; i++)
                {
                    float start = (float)(_owner.WheelAngle + i * sweep);
                    using (var brush = new SolidBrush(WheelColors[i % WheelColors.Length])) g.FillPie(brush, wheel, start, sweep);
                    float rad = start * (float)Math.PI / 180f;
                    using (var pen = new Pen(Color.FromArgb(115, 255, 255, 255), 1.2f))
                        g.DrawLine(pen, wheel.Left + wheel.Width / 2, wheel.Top + wheel.Height / 2,
                            wheel.Left + wheel.Width / 2 + (float)Math.Cos(rad) * wheel.Width / 2,
                            wheel.Top + wheel.Height / 2 + (float)Math.Sin(rad) * wheel.Height / 2);
                }

                float fontSize = Math.Max(8f, Math.Min(15f, sweep * .34f));
                using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold))
                {
                    int cx = wheel.Left + wheel.Width / 2, cy = wheel.Top + wheel.Height / 2;
                    for (int i = 0; i < n; i++)
                    {
                        float angle = (float)(_owner.WheelAngle + (i + .5) * sweep);
                        GraphicsState state = g.Save();
                        g.TranslateTransform(cx, cy);
                        g.RotateTransform(angle);
                        string name = Ellipsize(g, _owner.Pool[i].Name, font, wheel.Width * .32f);
                        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString(name, font, Brushes.White,
                                new RectangleF(wheel.Width * .18f, -18, wheel.Width * .32f, 36), format);
                        g.Restore(state);
                    }
                }

                int center = Math.Max(22, wheel.Width / 20);
                g.FillEllipse(Brushes.White, wheel.Left + wheel.Width / 2 - center, wheel.Top + wheel.Height / 2 - center, center * 2, center * 2);
                using (var brush = new SolidBrush(Indigo)) g.FillEllipse(brush,
                    wheel.Left + wheel.Width / 2 - 5, wheel.Top + wheel.Height / 2 - 5, 10, 10);

                int cyPointer = wheel.Top + wheel.Height / 2;
                int tip = wheel.Right - 5;
                Point[] pointer = { new Point(tip, cyPointer), new Point(tip + 55, cyPointer - 20), new Point(tip + 55, cyPointer + 20) };
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(pointer);
                    using (var brush = new LinearGradientBrush(new Rectangle(tip, cyPointer - 20, 56, 40),
                        LokalUi.PrimaryMedium, LokalUi.PrimaryHover, 0f)) g.FillPath(brush, path);
                }
            }

            private void DrawPickedHistory(Graphics g, Rectangle panel)
            {
                using (var pen = new Pen(LokalUi.PrimaryLight, 1.5f)) g.DrawLine(pen, panel.Left, panel.Top + 62, panel.Left, panel.Bottom);
                using (var font = new Font("Segoe UI", 13f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "Picked names (" + _owner.Picked.Count + ")", font,
                        new Rectangle(panel.Left + 34, panel.Top, panel.Width - 38, 45), Ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int y = panel.Top + 72;
                foreach (Participant participant in _owner.Picked.Take(8))
                {
                    Rectangle chip = new Rectangle(panel.Left + 34, y, panel.Width - 54, 50);
                    using (var path = RoundRect(chip, 25))
                    using (var brush = new SolidBrush(Color.FromArgb(248, 250, 255)))
                    using (var pen = new Pen(Teal, 1.5f)) { g.FillPath(brush, path); g.DrawPath(pen, path); }
                    Rectangle avatar = new Rectangle(chip.Left + 6, chip.Top + 6, 38, 38);
                    DrawAvatar(g, avatar, participant);
                    using (var font = new Font("Segoe UI", 10.2f, FontStyle.Bold))
                        TextRenderer.DrawText(g, participant.Name, font,
                            new Rectangle(avatar.Right + 10, chip.Top, chip.Width - 62, chip.Height), Ink,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    y += 62;
                }
            }

            private void DrawEmpty(Graphics g)
            {
                string title = _owner.OnlineOnly ? "No participants are online" : "No participants available";
                string detail = _owner.OnlineOnly ? "Students will appear here as soon as they connect." : "Ask students to join using the class code.";
                Rectangle card = new Rectangle(Math.Max(20, Width / 2 - 260), Math.Max(30, Height / 2 - 95), Math.Min(520, Width - 40), 190);
                using (var path = RoundRect(card, 28))
                using (var brush = new SolidBrush(Color.FromArgb(235, 255, 255, 255))) g.FillPath(brush, path);
                DrawPeople(g, card.Left + 48, card.Top + 54);
                using (var font = new Font("Segoe UI", 15f, FontStyle.Bold))
                    TextRenderer.DrawText(g, title, font, new Rectangle(card.Left + 110, card.Top + 42, card.Width - 145, 38), Ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                using (var font = new Font("Segoe UI", 10.5f))
                    TextRenderer.DrawText(g, detail, font, new Rectangle(card.Left + 110, card.Top + 84, card.Width - 145, 58), Muted,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                bool hit = _wheelHit.Contains(e.Location) || _cardHits.Any(item => item.Item1.Contains(e.Location));
                Cursor = hit ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                if (_wheelHit.Contains(e.Location)) { _owner.StartSpin(); return; }
                Tuple<Rectangle, Participant> hit = _cardHits.FirstOrDefault(item => item.Item1.Contains(e.Location));
                if (hit != null) _owner.RevealCard(hit.Item2);
            }
        }

        internal sealed class PickerOverlay : Control
        {
            private enum OverlayMode { Count, Result, Multiple }
            private readonly NamePickerDialog _owner;
            private readonly OverlayMode _mode;
            private readonly Participant _participant;
            private readonly List<Participant> _multiple;
            private int _count = 1;
            private bool _starAwarded;
            private Rectangle _modal;
            private Rectangle _minus;
            private Rectangle _plus;
            private Rectangle _primary;
            private Rectangle _putBack;
            private Rectangle _star;

            private PickerOverlay(NamePickerDialog owner, OverlayMode mode, Participant participant, List<Participant> multiple)
            {
                _owner = owner; _mode = mode; _participant = participant; _multiple = multiple;
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            public static PickerOverlay ForCount(NamePickerDialog owner) { return new PickerOverlay(owner, OverlayMode.Count, null, null); }
            public static PickerOverlay ForResult(NamePickerDialog owner, Participant participant) { return new PickerOverlay(owner, OverlayMode.Result, participant, null); }
            public static PickerOverlay ForMultiple(NamePickerDialog owner, List<Participant> participants) { return new PickerOverlay(owner, OverlayMode.Multiple, null, participants); }
            public void MarkStarAwarded() { _starAwarded = true; Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                using (var dim = new SolidBrush(Color.FromArgb(178, 24, 27, 39))) g.FillRectangle(dim, ClientRectangle);
                if (_mode == OverlayMode.Count) DrawCount(g);
                else if (_mode == OverlayMode.Result) DrawResult(g);
                else DrawMultiple(g);
            }

            private void DrawCount(Graphics g)
            {
                int w = Math.Min(500, Width - 60), h = 330;
                _modal = Centered(w, h);
                DrawModal(g, _modal);
                Rectangle header = new Rectangle(_modal.Left, _modal.Top, _modal.Width, 72);
                using (var brush = new SolidBrush(LokalUi.PrimaryPale)) FillTopRound(g, header, 22, brush);
                using (var font = new Font("Segoe UI", 17f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "Auto-pick names", font, header, Indigo,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int y = _modal.Top + 103;
                _minus = new Rectangle(_modal.Left + 92, y, 58, 58);
                _plus = new Rectangle(_modal.Right - 150, y, 58, 58);
                DrawRoundIcon(g, _minus, false); DrawRoundIcon(g, _plus, true);
                using (var font = new Font("Segoe UI", 38f, FontStyle.Bold))
                    TextRenderer.DrawText(g, _count.ToString(), font,
                        new Rectangle(_modal.Left + 170, y - 12, _modal.Width - 340, 82), Ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                _primary = new Rectangle(_modal.Left + 120, _modal.Bottom - 86, _modal.Width - 240, 54);
                DrawPill(g, _primary, "Pick " + _count + " name" + (_count == 1 ? "" : "s"), Indigo, Color.White);
            }

            private void DrawResult(Graphics g)
            {
                int w = Math.Min(480, Width - 60), h = 320;
                _modal = Centered(w, h);
                DrawModal(g, _modal);
                Rectangle header = new Rectangle(_modal.Left, _modal.Top, _modal.Width, 72);
                using (var brush = new LinearGradientBrush(header, Color.FromArgb(14, 211, 171), Color.FromArgb(9, 191, 157), 0f))
                    FillTopRound(g, header, 22, brush);
                using (var font = new Font("Segoe UI", 17f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "We have a name", font, header, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                Rectangle avatar = new Rectangle(_modal.Left + _modal.Width / 2 - 44, _modal.Top + 96, 88, 88);
                DrawAvatar(g, avatar, _participant);
                using (var font = new Font("Segoe UI", 15f, FontStyle.Bold))
                    TextRenderer.DrawText(g, _participant.Name, font,
                        new Rectangle(_modal.Left + 35, avatar.Bottom + 10, _modal.Width - 70, 40), Ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                _putBack = new Rectangle(_modal.Left + 24, _modal.Bottom - 62, 110, 38);
                using (var font = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "Put back", font, _putBack, Indigo,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                _star = new Rectangle(_modal.Right - 72, _modal.Bottom - 70, 48, 48);
                using (var brush = new SolidBrush(_starAwarded ? Teal : Indigo)) g.FillEllipse(brush, _star);
                DrawingUtil.DrawStar(g, _star.Left + 11, _star.Top + 11, 26, Color.FromArgb(251, 191, 36));
                string total = Math.Max(0, _participant.TotalStars).ToString();
                Size totalSize;
                using (var totalFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                {
                    totalSize = TextRenderer.MeasureText(total, totalFont, Size.Empty, TextFormatFlags.NoPadding);
                    Rectangle badge = new Rectangle(_star.Right - Math.Max(28, totalSize.Width + 14), _star.Top - 22,
                        Math.Max(28, totalSize.Width + 14), 22);
                    using (var badgePath = RoundRect(badge, 11))
                    using (var badgeBrush = new SolidBrush(Color.FromArgb(42, 190, 91))) g.FillPath(badgeBrush, badgePath);
                    TextRenderer.DrawText(g, total, totalFont, badge, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                if (_starAwarded)
                {
                    using (var font = new Font("Segoe UI", 8f, FontStyle.Bold))
                        TextRenderer.DrawText(g, "✓", font, new Rectangle(_star.Right - 17, _star.Top - 1, 18, 18), Color.White,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }

            private void DrawMultiple(Graphics g)
            {
                int count = _multiple == null ? 0 : _multiple.Count;
                int columns = Math.Min(5, Math.Max(1, count));
                int rows = (int)Math.Ceiling(count / (double)columns);
                int w = Math.Min(760, Width - 50);
                int h = Math.Min(560, 110 + rows * 125);
                _modal = Centered(w, h);
                DrawModal(g, _modal);
                using (var font = new Font("Segoe UI", 17f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "Auto-picked names", font,
                        new Rectangle(_modal.Left, _modal.Top + 12, _modal.Width, 50), Indigo,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int gap = 12, itemW = Math.Min(125, (_modal.Width - 50 - gap * (columns - 1)) / columns), itemH = 105;
                int total = itemW * columns + gap * (columns - 1);
                int startX = _modal.Left + (_modal.Width - total) / 2;
                for (int i = 0; i < count; i++)
                {
                    int col = i % columns, row = i / columns;
                    Rectangle item = new Rectangle(startX + col * (itemW + gap), _modal.Top + 76 + row * (itemH + gap), itemW, itemH);
                    using (var path = RoundRect(item, 14))
                    using (var brush = new SolidBrush(Color.FromArgb(247, 249, 255)))
                    using (var pen = new Pen(Teal, 1.5f)) { g.FillPath(brush, path); g.DrawPath(pen, path); }
                    Rectangle avatar = new Rectangle(item.Left + item.Width / 2 - 23, item.Top + 10, 46, 46);
                    DrawAvatar(g, avatar, _multiple[i]);
                    using (var font = new Font("Segoe UI", 9f, FontStyle.Bold))
                        TextRenderer.DrawText(g, _multiple[i].Name, font,
                            new Rectangle(item.Left + 6, avatar.Bottom + 7, item.Width - 12, 33), Ink,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                bool actionable = _minus.Contains(e.Location) || _plus.Contains(e.Location) || _primary.Contains(e.Location) ||
                    _putBack.Contains(e.Location) || _star.Contains(e.Location);
                Cursor = actionable ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                if (_mode == OverlayMode.Count)
                {
                    if (_minus.Contains(e.Location)) { _count = Math.Max(1, _count - 1); Invalidate(); }
                    else if (_plus.Contains(e.Location)) { _count = Math.Min(_owner.Pool.Count, _count + 1); Invalidate(); }
                    else if (_primary.Contains(e.Location)) _owner.AutoPick(_count);
                }
                else if (_mode == OverlayMode.Result)
                {
                    if (_putBack.Contains(e.Location)) _owner.PutBack(_participant);
                    else if (_star.Contains(e.Location)) _owner.AwardStar(_participant);
                    else if (!_modal.Contains(e.Location)) _owner.HideOverlay();
                }
                else if (!_modal.Contains(e.Location)) _owner.HideOverlay();
            }

            private Rectangle Centered(int w, int h) { return new Rectangle((Width - w) / 2, (Height - h) / 2, w, h); }

            private static void DrawModal(Graphics g, Rectangle rect)
            {
                Rectangle shadow = rect; shadow.Offset(0, 10);
                using (var path = RoundRect(shadow, 22))
                using (var brush = new SolidBrush(Color.FromArgb(55, 0, 0, 0))) g.FillPath(brush, path);
                using (var path = RoundRect(rect, 22))
                using (var brush = new SolidBrush(Color.White)) g.FillPath(brush, path);
            }

            private static void DrawRoundIcon(Graphics g, Rectangle rect, bool plus)
            {
                using (var pen = new Pen(Indigo, 1.5f)) g.DrawEllipse(pen, rect);
                using (var pen = new Pen(Indigo, 2.4f))
                {
                    g.DrawLine(pen, rect.Left + 16, rect.Top + rect.Height / 2, rect.Right - 16, rect.Top + rect.Height / 2);
                    if (plus) g.DrawLine(pen, rect.Left + rect.Width / 2, rect.Top + 16, rect.Left + rect.Width / 2, rect.Bottom - 16);
                }
            }
        }

        internal static GraphicsPath RoundRect(Rectangle rect, int radius)
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

        internal static StringFormat CenterFormat()
        {
            return new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        }

        internal static void DrawAvatar(Graphics g, Rectangle rect, Participant participant)
        {
            Color a = DrawingUtil.AvatarColor(participant == null ? "" : participant.Name, 0);
            Color b = DrawingUtil.AvatarColor(participant == null ? "" : participant.Name, 1);
            using (var brush = new LinearGradientBrush(rect, a, b, 45f)) g.FillEllipse(brush, rect);
            using (var pen = new Pen(Color.White, 3f)) g.DrawEllipse(pen, rect);
            using (var font = new Font("Segoe UI", Math.Max(10f, rect.Height * .30f), FontStyle.Bold))
            using (var sf = CenterFormat()) g.DrawString(DrawingUtil.Initials(participant == null ? "" : participant.Name), font, Brushes.White, rect, sf);
        }

        internal static void DrawPill(Graphics g, Rectangle rect, string text, Color fill, Color foreground)
        {
            using (var path = RoundRect(rect, rect.Height / 2))
            using (var brush = new LinearGradientBrush(rect, ControlPaint.Light(fill, .08f), fill, 90f)) g.FillPath(brush, path);
            using (var font = new Font("Segoe UI", 10.8f, FontStyle.Bold))
                TextRenderer.DrawText(g, text, font, rect, foreground,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        internal static void FillTopRound(Graphics g, Rectangle rect, int radius, Brush brush)
        {
            using (var path = new GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
                path.AddLine(rect.Right, rect.Bottom, rect.Left, rect.Bottom);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        internal static string Ellipsize(Graphics g, string value, Font font, float width)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "Student" : value;
            while (text.Length > 3 && g.MeasureString(text, font).Width > width) text = text.Substring(0, text.Length - 1);
            return text == value ? text : text.TrimEnd() + "…";
        }

        internal static void DrawPeople(Graphics g, int x, int y)
        {
            using (var brush = new SolidBrush(IndigoSoft)) g.FillEllipse(brush, x - 8, y - 12, 72, 72);
            using (var pen = new Pen(Indigo, 4f))
            {
                g.DrawEllipse(pen, x + 9, y, 20, 20);
                g.DrawArc(pen, x, y + 23, 38, 30, 190, 160);
                g.DrawEllipse(pen, x + 34, y + 8, 15, 15);
                g.DrawArc(pen, x + 29, y + 27, 30, 23, 190, 145);
            }
        }
    }
}
