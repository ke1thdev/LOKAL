using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LOKAL.PowerPoint
{
    public class CollectingResponsesForm : Form
    {
        private readonly ThisAddIn _addIn;
        private Activity _currentActivity;
        private readonly List<Response> _responses = new List<Response>();
        private List<Participant> _classParticipants = new List<Participant>();

        // UI Elements
        private Label _activityTypeLabel;
        private FlowLayoutPanel _joinInstructionsFlow;
        private Label _statusLabel;
        private Label _participantCountLabel;
        private Label _timerLabel;
        private Label _noParticipantsLabel;
        private Label _joinLink;
        private Label _responsesLink;
        private Label _revealAnswerLink;
        private Label _insertResultsLink;
        private Label _quizSummaryLink;
        private Panel _invitePopup;
        private DoubleBufferedPanel _animPanel;
        private Button _closeSubmissionBtn;
        private Timer _animTimer;
        private Timer _elapsedTimer;
        private double _animPhase = 0;
        private int _elapsedSeconds = 0;
        private bool _showCorrectAnswer;

        // Colors
        private readonly Color _bgLightBlue = LokalUi.PrimaryPale;
        private readonly Color _primaryBlue = LokalUi.Primary;
        private readonly Color _btnRed = Color.FromArgb(217, 83, 79);
        private readonly Color _textDark = Color.FromArgb(51, 51, 51);

        private class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
        }

        public CollectingResponsesForm(ThisAddIn addIn)
        {
            _addIn = addIn;
            InitializeUI();
        }

        internal bool HasResponses => _responses.Count > 0;

        private void InitializeUI()
        {
            this.Text = "LOKAL — Multiple Choice";
            this.Size = new Size(1300, 800); // Increased size further to prevent clipping
            this.MinimumSize = new Size(1000, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable; 
            this.TopMost = true;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 10f);
            this.ShowIcon = true;
            
            // ====== Header Bar ======
            var headerPanel = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Color.White };
            
            var headerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Left: Activity Type
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); // Center: Join Instructions gets maximum space
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // Right: Live Status
            headerPanel.Controls.Add(headerLayout);

            var leftFlow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.Left, WrapContents = false, Padding = new Padding(15, 20, 0, 0) };
            
            // Using actual icon instead of label
            PictureBox logoIcon = null;
            try {
                string logoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "android-chrome-192x192.png");
                if (!System.IO.File.Exists(logoPath))
                    logoPath = @"c:\xampp\htdocs\LOKAL-ThesisSys\assets\android-chrome-192x192.png";
                logoIcon = new PictureBox
                {
                    Image = Image.FromFile(logoPath),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Size = new Size(40, 40),
                    BackColor = Color.Transparent
                };
            } catch {
                // Fallback if image fails
                var fallback = new Label { Text = "L", Font = new Font("Segoe UI", 18f, FontStyle.Bold), ForeColor = Color.White, BackColor = _primaryBlue, TextAlign = ContentAlignment.MiddleCenter, Size = new Size(40, 40) };
                var logoPath = new GraphicsPath(); logoPath.AddEllipse(0, 0, 40, 40); fallback.Region = new Region(logoPath);
                leftFlow.Controls.Add(fallback);
            }
            if (logoIcon != null) leftFlow.Controls.Add(logoIcon);

            _activityTypeLabel = new Label
            {
                Text = "Multiple Choice",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = _textDark,
                AutoSize = true,
                Margin = new Padding(10, 5, 0, 0)
            };
            leftFlow.Controls.Add(_activityTypeLabel);
            headerLayout.Controls.Add(leftFlow, 0, 0);

            _joinInstructionsFlow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.None, WrapContents = false, Padding = new Padding(0, 25, 0, 0) };
            headerLayout.Controls.Add(_joinInstructionsFlow, 1, 0);

            var rightFlow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right, WrapContents = false, Padding = new Padding(0, 25, 25, 0) };
            var liveStatusLink = new Label
            {
                Text = "Live status",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = _primaryBlue,
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            liveStatusLink.Click += (s, e) => ToggleLiveStatusPopup();
            rightFlow.Controls.Add(liveStatusLink);
            headerLayout.Controls.Add(rightFlow, 2, 0);

            this.Controls.Add(headerPanel);

            var headerSep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(220, 220, 220) };
            this.Controls.Add(headerSep);

            // ====== Bottom Bar ======
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 100, BackColor = Color.White };
            var bottomSep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(220, 220, 220) };
            bottomPanel.Controls.Add(bottomSep);

            var bottomLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(20, 0, 20, 0)
            };
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            bottomPanel.Controls.Add(bottomLayout);

            Image LoadIcon(string name) {
                try {
                    string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
                    if (!System.IO.File.Exists(iconPath))
                        iconPath = System.IO.Path.Combine(@"c:\xampp\htdocs\LOKAL-ThesisSys\assets", name);
                    using (var source = Image.FromFile(iconPath))
                        return new Bitmap(source, new Size(24, 24));
                } catch { return null; }
            }

            var leftFlowBottom = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Anchor = AnchorStyles.Left };
            
            _participantCountLabel = new Label
            {
                Text = "      0",
                Image = LoadIcon("people.png"),
                ImageAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = _primaryBlue,
                AutoSize = true,
                Margin = new Padding(0, 0, 30, 0)
            };
            
            _timerLabel = new Label
            {
                Text = "      00:00",
                Image = LoadIcon("clock.png"),
                ImageAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = _primaryBlue,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 0)
            };

            leftFlowBottom.Controls.Add(_participantCountLabel);
            leftFlowBottom.Controls.Add(_timerLabel);
            bottomLayout.Controls.Add(leftFlowBottom, 0, 0);

            _closeSubmissionBtn = new Button
            {
                Text = "Close submission",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = _btnRed,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(240, 50),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.None
            };
            _closeSubmissionBtn.FlatAppearance.BorderSize = 0;
            
            _closeSubmissionBtn.Click += async (s, e) =>
            {
                _closeSubmissionBtn.Enabled = false;
                _closeSubmissionBtn.Text = "Closing...";
                try
                {
                    await _addIn.SessionManager.CloseActivityAsync(true);
                }
                catch
                {
                    if (!IsDisposed)
                    {
                        _closeSubmissionBtn.Enabled = true;
                        _closeSubmissionBtn.Text = "Close submission";
                    }
                }
            };
            
            _closeSubmissionBtn.Paint += (s, e) =>
            {
                using (var path = GetRoundedRectPath(new Rectangle(0, 0, _closeSubmissionBtn.Width, _closeSubmissionBtn.Height), 25))
                {
                    _closeSubmissionBtn.Region = new Region(path);
                }
            };
            bottomLayout.Controls.Add(_closeSubmissionBtn, 1, 0);

            var rightFlowBottom = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Anchor = AnchorStyles.Right };
            
            var musicLink = new Label
            {
                Text = "      Music",
                Image = LoadIcon("musical-note.png"),
                ImageAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = _primaryBlue,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 0)
            };

            _responsesLink = new Label
            {
                Text = "      Responses",
                Image = LoadIcon("eye.png"),
                ImageAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                AutoSize = true,
                Cursor = Cursors.Default,
                Margin = new Padding(0, 0, 30, 0)
            };
            _responsesLink.Click += (s, e) =>
            {
                if (_responses.Count > 0) ShowResponseChart();
            };

            _revealAnswerLink = new Label
            {
                Text = "✓ Reveal answer",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = _primaryBlue,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 30, 0)
            };
            _revealAnswerLink.Click += (s, e) =>
            {
                if (_responses.Count == 0) return;
                _showCorrectAnswer = !_showCorrectAnswer;
                _revealAnswerLink.Text = _showCorrectAnswer
                    ? "✓ Answer revealed"
                    : "✓ Reveal answer";
                _animPanel?.Invalidate();
            };

            _insertResultsLink = new Label
            {
                Text = "▣ Insert as slide",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(148, 163, 184),
                AutoSize = true,
                Cursor = Cursors.Default,
                Margin = new Padding(0, 0, 30, 0)
            };
            _insertResultsLink.Click += (s, e) =>
            {
                if (_responses.Count == 0 || _currentActivity == null) return;
                _addIn.InsertMultipleChoiceResultsSlide(_currentActivity, _responses);
            };

            _quizSummaryLink = new Label
            {
                Text = "Quiz summary",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = _primaryBlue,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 24, 0),
                Visible = false
            };
            _quizSummaryLink.Click += async (s, e) =>
            {
                if (_currentActivity == null || !_currentActivity.IsQuizMode || _currentActivity.SessionId <= 0) return;
                _quizSummaryLink.Enabled = false;
                try
                {
                    var summary = await _addIn.ApiClient.GetQuizSummaryAsync(_currentActivity.SessionId);
                    using (var dialog = new QuizSummaryForm(summary))
                    {
                        dialog.TopMost = true;
                        dialog.ShowDialog(this);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Unable to open Quiz Summary: " + ex.Message,
                        "LOKAL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally
                {
                    if (!_quizSummaryLink.IsDisposed) _quizSummaryLink.Enabled = true;
                }
            };

            rightFlowBottom.Controls.Add(musicLink);
            rightFlowBottom.Controls.Add(_quizSummaryLink);
            rightFlowBottom.Controls.Add(_insertResultsLink);
            rightFlowBottom.Controls.Add(_revealAnswerLink);
            rightFlowBottom.Controls.Add(_responsesLink);
            bottomLayout.Controls.Add(rightFlowBottom, 2, 0);

            this.Controls.Add(bottomPanel);

            // ====== Main Content Area ======
            _animPanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = _bgLightBlue
            };
            _animPanel.Paint += AnimPanel_Paint;
            _animPanel.Resize += (s, e) => _animPanel.Invalidate();
            this.Controls.Add(_animPanel);

            _statusLabel = new Label
            {
                Text = "Collecting responses...",
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = _textDark,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(500, 40)
            };
            _animPanel.Controls.Add(_statusLabel);
            
            _noParticipantsLabel = new Label
            {
                Text = "There are no participants yet.",
                Font = new Font("Segoe UI", 14f),
                ForeColor = Color.Gray,
                BackColor = Color.Transparent,
                AutoSize = true
            };
            _animPanel.Controls.Add(_noParticipantsLabel);

            _joinLink = new Label
            {
                Text = "Here's how they can join",
                Font = new Font("Segoe UI", 14f),
                ForeColor = _primaryBlue,
                BackColor = Color.Transparent,
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            _joinLink.Click += (s, e) => ToggleInvitePopup();
            _animPanel.Controls.Add(_joinLink);

            Action positionLabels = () => {
                if (_noParticipantsLabel == null || _joinLink == null) return;
                _statusLabel.Location = new Point((_animPanel.Width - 500) / 2, _animPanel.Height / 2 + 120);
                
                int totalWidth = _noParticipantsLabel.PreferredWidth + 5 + _joinLink.PreferredWidth;
                int startX = (_animPanel.Width - totalWidth) / 2;
                int yPos = _animPanel.Height / 2 + 170; 
                _noParticipantsLabel.Location = new Point(startX, yPos);
                _joinLink.Location = new Point(startX + _noParticipantsLabel.PreferredWidth + 5, yPos);
                _noParticipantsLabel.BringToFront();
                _joinLink.BringToFront();
            };

            _animPanel.Resize += (s, e) => positionLabels();
            positionLabels(); 
            
            CreateLiveStatusPopup();
            CreateInvitePopup();
            CreateWhoRespondedPopup();

            _animPanel.MouseClick += AnimPanel_MouseClick;

            _animTimer = new Timer { Interval = 33 };
            _animTimer.Tick += (s, e) =>
            {
                _animPhase += 0.05;
                _animPanel.Invalidate();
            };
            _animTimer.Start();

            _elapsedTimer = new Timer { Interval = 1000 };
            _elapsedTimer.Tick += (s, e) =>
            {
                _elapsedSeconds++;
                if (_currentActivity != null && _currentActivity.AutoCloseSeconds > 0)
                {
                    int remaining = Math.Max(0, _currentActivity.AutoCloseSeconds - _elapsedSeconds);
                    int min = remaining / 60;
                    int sec = remaining % 60;
                    _timerLabel.Text = $"      {min:D2}:{sec:D2}";
                    
                    if (remaining <= 10) {
                        _timerLabel.ForeColor = _btnRed;
                    } else {
                        _timerLabel.ForeColor = _primaryBlue;
                    }

                    if (remaining <= 0)
                    {
                        _elapsedTimer.Stop();
                    }
                }
                else
                {
                    int min = _elapsedSeconds / 60;
                    int sec = _elapsedSeconds % 60;
                    _timerLabel.Text = $"      {min:D2}:{sec:D2}";
                    _timerLabel.ForeColor = _primaryBlue;
                }
            };
            _elapsedTimer.Start();
        }

        private void ShowNoResponsesCollectedScreen()
        {
            _animTimer.Stop();
            _animPanel.Controls.Clear();
            _animPanel.Paint -= AnimPanel_Paint;

            var pnl = new Panel { Dock = DockStyle.Fill, BackColor = _bgLightBlue };
            
            var lblIcon = new Label 
            { 
                Text = "No responses", 
                Font = new Font("Segoe UI", 22f, FontStyle.Bold), 
                AutoSize = true, 
                BackColor = Color.Transparent,
                ForeColor = _textDark
            };
            
            var lblMsg = new Label 
            { 
                Text = "There are no responses collected", 
                Font = new Font("Segoe UI", 20f, FontStyle.Bold), 
                ForeColor = _textDark, 
                AutoSize = true,
                BackColor = Color.Transparent
            };
            
            var btnRestart = new Label
            { 
                Text = "Restart activity", 
                Font = new Font("Segoe UI", 13f, FontStyle.Bold), 
                BackColor = _primaryBlue,
                ForeColor = Color.White,
                Size = new Size(220, 50), 
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Action updateRestartRegion = () =>
            {
                using (var path = GetRoundedRectPath(new Rectangle(0, 0, btnRestart.Width, btnRestart.Height), 25))
                {
                    Region old = btnRestart.Region;
                    btnRestart.Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            };
            updateRestartRegion();
            btnRestart.Resize += (s, e) => updateRestartRegion();
            btnRestart.MouseEnter += (s, e) =>
                btnRestart.BackColor = Color.FromArgb(25, 103, 92);
            btnRestart.MouseLeave += (s, e) =>
                btnRestart.BackColor = _primaryBlue;
            btnRestart.Click += (s, e) =>
            {
                Close();
                _addIn.TryAutoStartActivityForCurrentSlide(true);
            };
            
            pnl.Resize += (s, e) => {
                lblIcon.Location = new Point((pnl.Width - lblIcon.Width) / 2, pnl.Height / 2 - 115);
                lblMsg.Location = new Point((pnl.Width - lblMsg.Width) / 2, pnl.Height / 2 - 20);
                btnRestart.Location = new Point((pnl.Width - btnRestart.Width) / 2, pnl.Height / 2 + 50);
            };
            
            pnl.Controls.Add(lblIcon);
            pnl.Controls.Add(lblMsg);
            pnl.Controls.Add(btnRestart);
            _animPanel.Controls.Add(pnl);
        }

        private Panel _liveStatusPopup;
        private Label _submittedLabel;
        private Label _pendingLabel;
        private FlowLayoutPanel _submittedList;
        private FlowLayoutPanel _pendingList;

        private void CreateLiveStatusPopup()
        {
            _liveStatusPopup = new Panel { Size = new Size(500, 350), BackColor = Color.White, Visible = false };
            _liveStatusPopup.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = GetRoundedRectPath(new Rectangle(0, 0, _liveStatusPopup.Width - 1, _liveStatusPopup.Height - 1), 16))
                using (var pen = new Pen(Color.LightGray, 1)) { e.Graphics.DrawPath(pen, path); }
            };

            var tabsPanel = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.White };
            var activeIndicator = new Panel { BackColor = _primaryBlue, Size = new Size(250, 2), Location = new Point(0, 48) };
            
            _submittedLabel = new Label { Text = "Submitted (0)", Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = _primaryBlue, TextAlign = ContentAlignment.MiddleCenter, Size = new Size(250, 48), Location = new Point(0, 0), Cursor = Cursors.Hand };
            _pendingLabel = new Label { Text = "Pending (0)", Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleCenter, Size = new Size(250, 48), Location = new Point(250, 0), Cursor = Cursors.Hand };

            _submittedLabel.Click += (s, e) => { _submittedLabel.ForeColor = _primaryBlue; _pendingLabel.ForeColor = Color.Gray; activeIndicator.Location = new Point(0, 48); _submittedList.Visible = true; _pendingList.Visible = false; };
            _pendingLabel.Click += (s, e) => { _submittedLabel.ForeColor = Color.Gray; _pendingLabel.ForeColor = _primaryBlue; activeIndicator.Location = new Point(250, 48); _submittedList.Visible = false; _pendingList.Visible = true; };

            tabsPanel.Controls.Add(activeIndicator); tabsPanel.Controls.Add(_submittedLabel); tabsPanel.Controls.Add(_pendingLabel);
            var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.LightGray };

            _submittedList = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20), Visible = true };
            _pendingList = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20), Visible = false };

            _liveStatusPopup.Controls.Add(_submittedList); _liveStatusPopup.Controls.Add(_pendingList); _liveStatusPopup.Controls.Add(sep); _liveStatusPopup.Controls.Add(tabsPanel);
            _animPanel.Controls.Add(_liveStatusPopup);
        }

        private void ToggleLiveStatusPopup()
        {
            if (_liveStatusPopup == null) return;
            if (!_liveStatusPopup.Visible)
            {
                _liveStatusPopup.Location = new Point((_animPanel.Width - _liveStatusPopup.Width) / 2, (_animPanel.Height - _liveStatusPopup.Height) / 2 - 30);
                var submittedIds = new HashSet<long>(_responses.Select(r => r.ParticipantId));
                var submittedUsers = _responses
                    .Select(r => !string.IsNullOrWhiteSpace(r.ParticipantName)
                        ? r.ParticipantName
                        : _classParticipants.FirstOrDefault(p => p.Id == r.ParticipantId)?.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct()
                    .ToList();
                var pendingUsers = _classParticipants.Where(p => !submittedIds.Contains(p.Id)).ToList();
                _submittedLabel.Text = $"Submitted ({submittedUsers.Count})";
                _pendingLabel.Text = $"Pending ({pendingUsers.Count})";
                PopulateParticipantList(_submittedList, submittedUsers);
                PopulateParticipantList(_pendingList, pendingUsers.Select(p => p.Name).ToList());
                _liveStatusPopup.BringToFront();
            }
            _liveStatusPopup.Visible = !_liveStatusPopup.Visible; TogglePopupBackgroundContent(_liveStatusPopup.Visible); _animPanel.Invalidate();
        }

        private void TogglePopupBackgroundContent(bool isVisible)
        {
            if (_statusLabel != null) _statusLabel.Visible = !isVisible;
            if (_responses.Count == 0)
            {
                if (_noParticipantsLabel != null) _noParticipantsLabel.Visible = !isVisible;
                if (_joinLink != null) _joinLink.Visible = !isVisible;
            }
        }

        private void PopulateParticipantList(FlowLayoutPanel panel, List<string> names)
        {
            panel.Controls.Clear();
            foreach (var name in names)
            {
                var pnl = new Panel { Width = panel.Width - 40, Height = 40 };
                var initialLabel = new Label { Text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpper(), Size = new Size(32, 32), Location = new Point(0, 4), BackColor = Color.LightGray, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
                initialLabel.Paint += (s, e) => { var path = new GraphicsPath(); path.AddEllipse(0, 0, 32, 32); initialLabel.Region = new Region(path); };
                var nameLabel = new Label { Text = name, Location = new Point(40, 10), AutoSize = true, Font = new Font("Segoe UI", 10f) };
                pnl.Controls.Add(initialLabel); pnl.Controls.Add(nameLabel); panel.Controls.Add(pnl);
            }
        }

        private void CreateInvitePopup()
        {
            _invitePopup = new Panel { Size = new Size(680, 420), BackColor = Color.White, Visible = false };
            
            int radius = 30;
            // Set the Region immediately to guarantee rounded corners and perfectly clip the header
            _invitePopup.Region = new Region(GetRoundedRectPath(new Rectangle(0, 0, _invitePopup.Width, _invitePopup.Height), radius));

            var headerPanel = new Panel { Dock = DockStyle.Top, Height = 65, BackColor = LokalUi.PrimaryPale };
            
            // Apply a region to the header panel directly to strictly enforce the top corners
            var headerPath = new GraphicsPath();
            headerPath.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
            headerPath.AddArc(headerPanel.Width - radius * 2, 0, radius * 2, radius * 2, 270, 90);
            headerPath.AddLine(headerPanel.Width, headerPanel.Height, 0, headerPanel.Height);
            headerPath.CloseFigure();
            headerPanel.Region = new Region(headerPath);

            var titleLabel = new Label { Text = "Invite participants to your class", Font = new Font("Segoe UI", 16f, FontStyle.Bold), ForeColor = _primaryBlue, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
            headerPanel.Controls.Add(titleLabel);
            _invitePopup.Controls.Add(headerPanel);

            var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f)); 
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f)); 
            _invitePopup.Controls.Add(mainLayout);

            var qrPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30) };
            var qrImg = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White, SizeMode = PictureBoxSizeMode.Zoom };
            
            qrImg.Paint += (s, e) => {
                try {
                    using (var logo = Image.FromFile(@"c:\xampp\htdocs\LOKAL-ThesisSys\assets\android-chrome-512x512.png")) {
                        int logoSize = 56;
                        int x = (qrImg.Width - logoSize) / 2;
                        int y = (qrImg.Height - logoSize) / 2;
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.FillEllipse(Brushes.White, x - 5, y - 5, logoSize + 10, logoSize + 10);
                        e.Graphics.DrawImage(logo, x, y, logoSize, logoSize);
                    }
                } catch { }
            };

            qrPanel.Controls.Add(qrImg);
            mainLayout.Controls.Add(qrPanel, 0, 0);

            var joinUrl = _addIn.CurrentJoinUrl ?? "http://192.168.100.143:8080/student";
            string classCode = _addIn.CurrentClassCode ?? "UTOT";
            
            string cleanJoinUrl = joinUrl.StartsWith("http") ? joinUrl : "http://" + joinUrl;
            string fullQrUrl = $"{cleanJoinUrl}?code={classCode}";
            
            try { qrImg.LoadAsync($"http://localhost:8080/api/v1/qrcode?data={Uri.EscapeDataString(fullQrUrl)}"); } catch { }

            var rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
            rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f)); // Top spacer
            rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // URL Title
            rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // URL Value
            rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Separator
            rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Code Title
            rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Code Value
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f)); // Bottom spacer
            
            var urlTitle = new Label { Text = "URL", Font = new Font("Segoe UI", 9f), ForeColor = Color.Gray, AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, 5) };
            
            var displayUrl = joinUrl.Replace("http://", "").Replace("https://", "");
            if (displayUrl.Length > 24) displayUrl = displayUrl.Substring(0, 21) + "...";
            
            // FlowLayoutPanel is buggy with AutoSize centering, use a nested TableLayoutPanel to guarantee perfection
            var urlFlow = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, 15) };
            urlFlow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            urlFlow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            
            var urlValue = new Label { Text = displayUrl, Font = new Font("Segoe UI", 15f), ForeColor = _primaryBlue, AutoSize = true, Margin = new Padding(0), Anchor = AnchorStyles.Left | AnchorStyles.Top };
            var copyIconText = new Label { Text = "📋", Font = new Font("Segoe UI", 14f), ForeColor = _primaryBlue, AutoSize = true, Cursor = Cursors.Hand, Margin = new Padding(5, 2, 0, 0), Anchor = AnchorStyles.Left | AnchorStyles.Top };
            
            var clickHandler = new EventHandler((s, e) => { try { Clipboard.SetText(joinUrl); MessageBox.Show("URL Copied to clipboard!"); } catch {} });
            copyIconText.Click += clickHandler;
            
            urlFlow.Controls.Add(urlValue, 0, 0); 
            urlFlow.Controls.Add(copyIconText, 1, 0);

            var sepContainer = new Panel { Size = new Size(220, 1), Anchor = AnchorStyles.None, Margin = new Padding(0, 10, 0, 25) };
            sepContainer.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(230, 230, 230) });

            var codeTitle = new Label { Text = "Class code", Font = new Font("Segoe UI", 9f), ForeColor = Color.Gray, AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, 5) };
            var codeValue = new Label { Text = classCode, Font = new Font("Segoe UI", 26f), ForeColor = _primaryBlue, AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(0) };

            rightPanel.Controls.Add(urlTitle, 0, 1);
            rightPanel.Controls.Add(urlFlow, 0, 2);
            rightPanel.Controls.Add(sepContainer, 0, 3);
            rightPanel.Controls.Add(codeTitle, 0, 4);
            rightPanel.Controls.Add(codeValue, 0, 5);

            mainLayout.Controls.Add(rightPanel, 1, 0);
            _animPanel.Controls.Add(_invitePopup);
        }

        private Panel _whoRespondedPopup;
        private Label _whoRespondedTitle;
        private FlowLayoutPanel _whoRespondedList;
        private int _selectedOptionIndex = -1;

        private void CreateWhoRespondedPopup()
        {
            _whoRespondedPopup = new Panel { Size = new Size(400, 300), BackColor = Color.White, Visible = false };
            _whoRespondedPopup.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = GetRoundedRectPath(new Rectangle(0, 0, _whoRespondedPopup.Width - 1, _whoRespondedPopup.Height - 1), 16))
                using (var pen = new Pen(Color.LightGray, 1)) { e.Graphics.DrawPath(pen, path); }
            };

            var header = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = LokalUi.PrimaryPale };
            _whoRespondedTitle = new Label { Text = "Who chose 'A'?", Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(51, 51, 51), TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
            header.Controls.Add(_whoRespondedTitle);
            
            _whoRespondedList = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, BackColor = Color.White };
            var awardBtn = new Button { Text = "★ Award 1 star to this answer", Font = new Font("Segoe UI", 10f, FontStyle.Bold), BackColor = LokalUi.Primary, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Size = new Size(245, 40), Cursor = Cursors.Hand };
            awardBtn.FlatAppearance.BorderSize = 0;
            awardBtn.Location = new Point((_whoRespondedPopup.Width - awardBtn.Width) / 2, 10);
            awardBtn.Click += async (s, e) =>
            {
                try
                {
                    if (!_addIn.CurrentClassId.HasValue || _selectedOptionIndex < 0)
                        return;

                    var participantIds = _responses
                        .Where(r => ResponseIncludesOption(r, _selectedOptionIndex))
                        .Select(r => r.ParticipantId)
                        .Distinct()
                        .ToList();

                    foreach (var participantId in participantIds)
                        await _addIn.ApiClient.AdjustParticipantStarsAsync(
                            _addIn.CurrentClassId.Value, participantId, 1);

                    if (participantIds.Count == 0)
                    {
                        MessageBox.Show("No students selected this answer.", "LOKAL");
                        return;
                    }

                    LokalUi.PlayAddStarSound();
                    MessageBox.Show(
                        $"Awarded 1 star to {participantIds.Count} student{(participantIds.Count == 1 ? "" : "s")}.",
                        "LOKAL");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not award stars: " + ex.Message, "LOKAL",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            awardBtn.Paint += (s, e) => { var path = GetRoundedRectPath(new Rectangle(0, 0, awardBtn.Width, awardBtn.Height), 20); awardBtn.Region = new Region(path); };
            footer.Controls.Add(awardBtn);

            _whoRespondedPopup.Controls.Add(_whoRespondedList); _whoRespondedPopup.Controls.Add(header); _whoRespondedPopup.Controls.Add(footer);
            _animPanel.Controls.Add(_whoRespondedPopup);
        }

        private void ToggleWhoRespondedPopup(string option)
        {
            if (_whoRespondedPopup == null) return;
            if (!_whoRespondedPopup.Visible)
            {
                _whoRespondedPopup.Location = new Point((_animPanel.Width - _whoRespondedPopup.Width) / 2, (_animPanel.Height - _whoRespondedPopup.Height) / 2 - 30);
                _whoRespondedTitle.Text = $"Who chose '{option}'?";
                int optionIndex = string.IsNullOrEmpty(option) ? -1 : char.ToUpperInvariant(option[0]) - 'A';
                _selectedOptionIndex = optionIndex;
                var names = _responses
                    .Where(r => ResponseIncludesOption(r, optionIndex))
                    .Select(r => !string.IsNullOrWhiteSpace(r.ParticipantName)
                        ? r.ParticipantName
                        : _classParticipants?.FirstOrDefault(p => p.Id == r.ParticipantId)?.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();
                PopulateParticipantList(_whoRespondedList, names);
                _whoRespondedPopup.BringToFront();
            }
            else
            {
                _selectedOptionIndex = -1;
            }
            _whoRespondedPopup.Visible = !_whoRespondedPopup.Visible; TogglePopupBackgroundContent(_whoRespondedPopup.Visible); _animPanel.Invalidate();
        }

        private void AnimPanel_MouseClick(object sender, MouseEventArgs e)
        {
            if (_liveStatusPopup != null && _liveStatusPopup.Visible) { ToggleLiveStatusPopup(); return; }
            if (_invitePopup != null && _invitePopup.Visible) { ToggleInvitePopup(); return; }
            if (_whoRespondedPopup != null && _whoRespondedPopup.Visible) { ToggleWhoRespondedPopup(""); return; }

            if (_currentActivity?.Type != "multiple_choice" || _responses.Count == 0) return;
            if (_barRects == null || _barLabels == null) return;

            for (int i = 0; i < _barRects.Length; i++)
            {
                if (_barRects[i].Contains(e.Location))
                {
                    ToggleWhoRespondedPopup(_barLabels[i]);
                    return;
                }
            }
        }

        private void ToggleInvitePopup()
        {
            if (_invitePopup == null) return;
            if (!_invitePopup.Visible)
            {
                _invitePopup.Location = new Point((_animPanel.Width - _invitePopup.Width) / 2, (_animPanel.Height - _invitePopup.Height) / 2 - 30);
                _invitePopup.BringToFront();
            }
            _invitePopup.Visible = !_invitePopup.Visible; TogglePopupBackgroundContent(_invitePopup.Visible); _animPanel.Invalidate();
        }

        private Rectangle[] _barRects = new Rectangle[0];
        private string[] _barLabels = new string[0];

        private void AnimPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_currentActivity?.Type == "multiple_choice" && _responses.Count > 0)
                DrawBarChart(g);
            else
                DrawPulseAnimation(g);

            bool anyPopupOpen = (_liveStatusPopup != null && _liveStatusPopup.Visible) ||
                                (_invitePopup != null && _invitePopup.Visible) ||
                                (_whoRespondedPopup != null && _whoRespondedPopup.Visible);
            
            if (anyPopupOpen)
                using (var brush = new SolidBrush(Color.FromArgb(180, 50, 50, 50)))
                    g.FillRectangle(brush, _animPanel.ClientRectangle);
        }

        private readonly Color[] _barColors = new Color[] { Color.FromArgb(0, 208, 132), Color.FromArgb(235, 76, 112), Color.FromArgb(66, 153, 225), Color.FromArgb(246, 173, 85) };

        private static bool ResponseIncludesOption(Response response, int optionIndex)
        {
            if (response?.Answer == null || optionIndex < 0) return false;
            try
            {
                var token = response.Answer as Newtonsoft.Json.Linq.JToken;
                if (token == null)
                {
                    string raw = response.Answer.ToString();
                    try { token = Newtonsoft.Json.Linq.JToken.Parse(raw); }
                    catch { token = new Newtonsoft.Json.Linq.JValue(raw); }
                }
                return AnswerTokenIncludesOption(token, optionIndex);
            }
            catch { return false; }
        }

        private static bool AnswerTokenIncludesOption(Newtonsoft.Json.Linq.JToken token, int optionIndex)
        {
            if (token == null) return false;
            if (token.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                return token.Children().Any(child => AnswerTokenIncludesOption(child, optionIndex));
            if (token.Type == Newtonsoft.Json.Linq.JTokenType.Object)
            {
                var obj = (Newtonsoft.Json.Linq.JObject)token;
                foreach (string propertyName in new[] { "selected_options", "selectedAnswers", "answers", "answer" })
                {
                    Newtonsoft.Json.Linq.JToken nested;
                    if (obj.TryGetValue(propertyName, StringComparison.OrdinalIgnoreCase, out nested) &&
                        AnswerTokenIncludesOption(nested, optionIndex))
                        return true;
                }
                return false;
            }
            if (token.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                return Convert.ToInt32(((Newtonsoft.Json.Linq.JValue)token).Value) == optionIndex;

            string text = token.ToString().Trim().Trim('"');
            if (int.TryParse(text, out int numeric)) return numeric == optionIndex;
            return text.Length == 1 && char.ToUpperInvariant(text[0]) == (char)('A' + optionIndex);
        }

        private void ShowResponseChart()
        {
            if (_responses.Count == 0) return;
            if (_liveStatusPopup != null) _liveStatusPopup.Visible = false;
            if (_invitePopup != null) _invitePopup.Visible = false;
            if (_whoRespondedPopup != null) _whoRespondedPopup.Visible = false;
            TogglePopupBackgroundContent(false);
            if (_statusLabel != null) _statusLabel.Visible = false;
            if (_noParticipantsLabel != null) _noParticipantsLabel.Visible = false;
            if (_joinLink != null) _joinLink.Visible = false;
            _animPanel?.Invalidate();
        }

        private void DrawBarChart(Graphics g)
        {
            int optionsCount = 4;
            var correctAnswers = new HashSet<int>();
            try {
                var cfg = Newtonsoft.Json.Linq.JObject.Parse(_currentActivity.Config);
                var opts = cfg["options"] as Newtonsoft.Json.Linq.JArray;
                if (opts != null) optionsCount = opts.Count;
                else optionsCount = Math.Max(2, Math.Min(8, cfg.Value<int?>("num_choices") ?? 4));

                var correct = cfg["correct_answer"];
                if (correct is Newtonsoft.Json.Linq.JArray correctArray)
                {
                    foreach (var token in correctArray)
                    {
                        if (token.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                            correctAnswers.Add(token.ToObject<int>());
                        else if (token.Type == Newtonsoft.Json.Linq.JTokenType.String &&
                                 !string.IsNullOrWhiteSpace(token.ToObject<string>()))
                            correctAnswers.Add(char.ToUpperInvariant(token.ToObject<string>()[0]) - 'A');
                    }
                }
            } catch { }

            _barRects = new Rectangle[optionsCount];
            _barLabels = new string[optionsCount];

            int cx = _animPanel.Width / 2;
            int cy = Math.Max(260, _animPanel.Height - 100);
            int maxHeight = Math.Max(120, Math.Min(300, cy - 115));
            int spacing = Math.Max(10, Math.Min(26, _animPanel.Width / 70));
            int barWidth = Math.Max(48, Math.Min(100,
                (_animPanel.Width - 160 - ((optionsCount - 1) * spacing)) / optionsCount));

            int totalWidth = (optionsCount * barWidth) + ((optionsCount - 1) * spacing);
            int startX = cx - (totalWidth / 2);
            int maxVotes = _responses.Count == 0 ? 1 : _responses.Count;

            using (var font = new Font("Segoe UI", 16f, FontStyle.Bold))
            using (var labelBrush = new SolidBrush(Color.FromArgb(51, 51, 51)))
            {
                for (int i = 0; i < optionsCount; i++)
                {
                    string optionLetter = ((char)('A' + i)).ToString();
                    _barLabels[i] = optionLetter;
                    
                    int votes = _responses.Count(r => ResponseIncludesOption(r, i));
                    int h = (int)((double)votes / maxVotes * maxHeight);
                    if (h < 10) h = 10; 

                    int x = startX + i * (barWidth + spacing);
                    int y = cy - h;
                    _barRects[i] = new Rectangle(x, y, barWidth, h);

                    using (var brush = new SolidBrush(_barColors[i % _barColors.Length]))
                    using (var path = GetRoundedRectPath(_barRects[i], 10))
                        g.FillPath(brush, path);

                    if (_showCorrectAnswer && correctAnswers.Contains(i))
                    {
                        using (var answerPen = new Pen(Color.FromArgb(22, 163, 74), 5f))
                        using (var path = GetRoundedRectPath(_barRects[i], 10))
                            g.DrawPath(answerPen, path);
                    }

                    var strSize = g.MeasureString(optionLetter, font);
                    g.DrawString(optionLetter, font, labelBrush, x + (barWidth - strSize.Width) / 2, cy + 10);

                    if (_showCorrectAnswer && correctAnswers.Contains(i))
                    {
                        using (var checkFont = new Font("Segoe UI Symbol", 15f, FontStyle.Bold))
                        using (var checkBrush = new SolidBrush(Color.FromArgb(22, 163, 74)))
                            g.DrawString("✓", checkFont, checkBrush, x + barWidth - 18, cy + 7);
                    }

                    if (votes > 0)
                    {
                        string pct = Math.Round((double)votes / _responses.Count * 100).ToString();
                        string bubbleTxt = $"{votes} ({pct}%)";
                        using (var bubbleFont = new Font("Segoe UI", 10f, FontStyle.Bold))
                        {
                            var bSize = g.MeasureString(bubbleTxt, bubbleFont);
                            int bx = x + (barWidth - (int)bSize.Width) / 2;
                            int by = y - 30;
                            using (var bBrush = new SolidBrush(Color.FromArgb(60, 60, 60)))
                            using (var textBrush = new SolidBrush(Color.White))
                            {
                                g.FillPath(bBrush, GetRoundedRectPath(new Rectangle(bx - 10, by - 5, (int)bSize.Width + 20, (int)bSize.Height + 10), 12));
                                g.DrawString(bubbleTxt, bubbleFont, textBrush, bx, by);
                                Point[] tri = { new Point(x + barWidth/2 - 6, by + (int)bSize.Height + 5), new Point(x + barWidth/2 + 6, by + (int)bSize.Height + 5), new Point(x + barWidth/2, by + (int)bSize.Height + 12) };
                                g.FillPolygon(bBrush, tri);
                            }
                        }
                    }
                }
            }
        }

        private void DrawPulseAnimation(Graphics g)
        {
            int cx = _animPanel.Width / 2;
            int cy = _animPanel.Height / 2 - 20;
            
            Color[] colors = { Color.FromArgb(66, 153, 225), Color.FromArgb(235, 76, 112), Color.FromArgb(246, 173, 85), LokalUi.PrimaryMedium, Color.FromArgb(0, 208, 132) };
            
            int barWidth = 12;
            int spacing = 10;
            int numBars = 5;
            int totalWidth = (numBars * barWidth) + ((numBars - 1) * spacing);
            int startX = cx - (totalWidth / 2);
            
            for (int i = 0; i < numBars; i++)
            {
                double phaseOffset = i * (Math.PI / 3);
                double sine = Math.Sin(_animPhase * 2 + phaseOffset);
                int h = (int)(30 + (20 * sine));
                if (i == 2) h = (int)(40 + (25 * sine));
                if (i == 0 || i == 4) h = (int)(20 + (15 * sine));

                int x = startX + i * (barWidth + spacing);
                int y = cy - (h / 2);
                
                using (var brush = new SolidBrush(colors[i % colors.Length]))
                using (var path = GetRoundedRectPath(new Rectangle(x, y, barWidth, h), barWidth / 2))
                    g.FillPath(brush, path);
            }
        }

        private GraphicsPath GetRoundedRectPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0) { path.AddRectangle(r); return path; }
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.X + r.Width - d, r.Y, d, d, 270, 90);
            path.AddArc(r.X + r.Width - d, r.Y + r.Height - d, d, d, 0, 90);
            path.AddArc(r.X, r.Y + r.Height - d, d, d, 90, 90);
            path.CloseAllFigures();
            return path;
        }

        public void SetActivity(Activity activity, string classCode, string joinUrl = null, List<Participant> participants = null)
        {
            if (participants != null) _classParticipants = participants;
            string joinDisplay = string.IsNullOrEmpty(joinUrl) ? "localhost:8080/student" : joinUrl.Replace("http://", "").Replace("https://", "");
            _currentActivity = activity;
            _responses.Clear();
            _elapsedSeconds = 0;
            _showCorrectAnswer = false;
            
            Action update = () =>
            {
                string typeText = activity?.Type switch
                {
                    "multiple_choice" => "Multiple Choice",
                    _                 => "Activity"
                };

                _activityTypeLabel.Text = typeText;
                
                // Build dynamic multi-color join instructions (slightly smaller font to guarantee fit)
                _joinInstructionsFlow.Controls.Clear();
                _joinInstructionsFlow.Controls.Add(new Label { Text = "Visit", Font = new Font("Segoe UI", 12f), ForeColor = _textDark, AutoSize = true, Margin = new Padding(0) });
                _joinInstructionsFlow.Controls.Add(new Label { Text = $" {joinDisplay} ", Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = _primaryBlue, AutoSize = true, Margin = new Padding(0) });
                _joinInstructionsFlow.Controls.Add(new Label { Text = "and use code", Font = new Font("Segoe UI", 12f), ForeColor = _textDark, AutoSize = true, Margin = new Padding(0) });
                _joinInstructionsFlow.Controls.Add(new Label { Text = $" {classCode} ", Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = _primaryBlue, AutoSize = true, Margin = new Padding(0) });
                _joinInstructionsFlow.Controls.Add(new Label { Text = "to join", Font = new Font("Segoe UI", 12f), ForeColor = _textDark, AutoSize = true, Margin = new Padding(0) });

                _statusLabel.Text = "Collecting responses...";
                _statusLabel.Visible = true;
                int participantCount = _classParticipants == null ? 0 : _classParticipants.Count;
                _participantCountLabel.Text = $"      {participantCount}";
                
                if (activity != null && activity.AutoCloseSeconds > 0)
                {
                    int min = activity.AutoCloseSeconds / 60;
                    int sec = activity.AutoCloseSeconds % 60;
                    _timerLabel.Text = $"      {min:D2}:{sec:D2}";
                }
                else
                {
                    _timerLabel.Text = "      00:00";
                }
                _timerLabel.ForeColor = _primaryBlue;
                
                _closeSubmissionBtn.Enabled = true;
                _closeSubmissionBtn.Text = "Close submission";
                SetResponsesLinkAvailability(false);
                if (_revealAnswerLink != null)
                {
                    _revealAnswerLink.Text = "✓ Reveal answer";
                    _revealAnswerLink.Visible = activity?.Type == "multiple_choice";
                }
                if (_quizSummaryLink != null)
                    _quizSummaryLink.Visible = activity != null && activity.IsQuizMode;
                
                if (_noParticipantsLabel != null) _noParticipantsLabel.Visible = participantCount == 0;
                if (_joinLink != null) _joinLink.Visible = participantCount == 0;
                _animPanel?.Invalidate();
                if (_elapsedTimer != null && !_elapsedTimer.Enabled) _elapsedTimer.Start();
            };

            if (this.InvokeRequired) this.Invoke(update); else update();
        }

        public void UpdateParticipantCount(int count)
        {
            Action update = () => { 
                _participantCountLabel.Text = $"      {count}";
                if (_noParticipantsLabel != null) _noParticipantsLabel.Visible = count == 0;
                if (_joinLink != null) _joinLink.Visible = count == 0;
            };
            if (this.InvokeRequired) this.Invoke(update); else update();
        }

        public void AddResponse(Response response)
        {
            Action update = () => {
                if (response == null || _responses.Any(r => r.ParticipantId == response.ParticipantId)) return;
                _responses.Add(response);
                SetResponsesLinkAvailability(true);
                _statusLabel.Text = $"Collecting responses... ({_responses.Count})";
                _statusLabel.Visible = false;
                if (_noParticipantsLabel != null) _noParticipantsLabel.Visible = false;
                if (_joinLink != null) _joinLink.Visible = false;
                _animPanel?.Invalidate();
            };
            if (this.InvokeRequired) this.Invoke(update); else update();
        }

        public void ShowStoredResponses()
        {
            Action update = () =>
            {
                if (_responses.Count == 0) return;
                _elapsedTimer?.Stop();
                _closeSubmissionBtn.Enabled = false;
                _closeSubmissionBtn.Text = "Activity closed";
                SetResponsesLinkAvailability(true);
                ShowResponseChart();
            };
            if (InvokeRequired) BeginInvoke(update); else update();
        }

        internal void CompleteActivityAndShowResults()
        {
            Action update = () =>
            {
                if (IsDisposed) return;
                _elapsedTimer?.Stop();
                _animTimer?.Stop();
                _closeSubmissionBtn.Enabled = false;
                _closeSubmissionBtn.Text = "Activity closed";
                SetResponsesLinkAvailability(_responses.Count > 0);

                if (_responses.Count == 0)
                    ShowNoResponsesCollectedScreen();
                else
                    ShowResponseChart();
            };

            if (InvokeRequired) BeginInvoke(update); else update();
        }

        private void SetResponsesLinkAvailability(bool available)
        {
            if (_responsesLink == null || _responsesLink.IsDisposed) return;
            _responsesLink.ForeColor = available ? _primaryBlue : Color.FromArgb(148, 163, 184);
            _responsesLink.Cursor = available ? Cursors.Hand : Cursors.Default;
            if (_insertResultsLink != null && !_insertResultsLink.IsDisposed)
            {
                _insertResultsLink.ForeColor = available ? _primaryBlue : Color.FromArgb(148, 163, 184);
                _insertResultsLink.Cursor = available ? Cursors.Hand : Cursors.Default;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _animTimer?.Stop(); _animTimer?.Dispose();
            _elapsedTimer?.Stop(); _elapsedTimer?.Dispose();
            _addIn.RestoreActivityCountdownZOrder();
            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!Visible) return;
            if (WindowState == FormWindowState.Minimized)
                _addIn.RestoreActivityCountdownZOrder();
            else
                _addIn.KeepCollectingResponsesAboveCountdown();
        }
    }
}
