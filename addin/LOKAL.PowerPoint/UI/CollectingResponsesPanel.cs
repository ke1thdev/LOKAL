using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Linq;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// "Collecting Responses" panel — auto-opens when an activity starts.
    /// Shows animated dots, response count, and real-time response list.
    /// Matches ClassPoint's collecting responses overlay exactly.
    /// </summary>
    public class CollectingResponsesPanel : UserControl
    {
        private readonly ThisAddIn _addIn;
        private Activity _currentActivity;
        private readonly List<Response> _responses = new List<Response>();

        // UI Elements
        private Label _titleLabel;
        private Label _typeLabel;
        private Label _countLabel;
        private Panel _dotsPanel;
        private FlowLayoutPanel _responsesList;
        private Button _closeBtn;

        private Timer _animTimer;
        private int _dotFrame = 0;

        // Colors
        private readonly Color _bgDark = Color.FromArgb(30, 27, 75);
        private readonly Color _bgCard = Color.FromArgb(49, 46, 129);
        private readonly Color _accent = LokalUi.Primary;
        private readonly Color _accentLight = Color.FromArgb(129, 140, 248);
        private readonly Color _success = Color.FromArgb(34, 197, 94);

        public CollectingResponsesPanel(ThisAddIn addIn)
        {
            _addIn = addIn;
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.BackColor = _bgDark;
            this.Dock = DockStyle.Fill;
            this.Padding = new Padding(16);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // Title
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));   // Type
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));   // Animated dots
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));   // Count
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Responses list
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));   // Close button

            // Title
            _titleLabel = new Label
            {
                Text = "Collecting responses...",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.White,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(_titleLabel, 0, 0);

            // Activity type badge
            _typeLabel = new Label
            {
                Text = "Multiple Choice",
                Font = new Font("Segoe UI", 9f),
                ForeColor = _accentLight,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopCenter,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(_typeLabel, 0, 1);

            // Animated dots panel
            _dotsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            _dotsPanel.Paint += DotsPanel_Paint;
            layout.Controls.Add(_dotsPanel, 0, 2);

            // Response count
            _countLabel = new Label
            {
                Text = "0 responses",
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.White,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(_countLabel, 0, 3);

            // Responses list
            _responsesList = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(4)
            };
            layout.Controls.Add(_responsesList, 0, 4);

            // Close button
            _closeBtn = new Button
            {
                Text = "Close submissions",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(239, 68, 68), // red
                FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Fill,
                Cursor = Cursors.Hand,
                Height = 40,
                Margin = new Padding(0, 4, 0, 0)
            };
            _closeBtn.FlatAppearance.BorderSize = 0;
            _closeBtn.Click += async (s, e) =>
            {
                await _addIn.SessionManager.CloseActivityAsync();
            };
            layout.Controls.Add(_closeBtn, 0, 5);

            this.Controls.Add(layout);

            // Animation timer for bouncing dots
            _animTimer = new Timer { Interval = 400 };
            _animTimer.Tick += (s, e) =>
            {
                _dotFrame = (_dotFrame + 1) % 4;
                _dotsPanel.Invalidate();
            };
            _animTimer.Start();
        }

        private void DotsPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int dotSize = 14;
            int gap = 8;
            int totalWidth = (dotSize * 3) + (gap * 2);
            int startX = (_dotsPanel.Width - totalWidth) / 2;
            int baseY = _dotsPanel.Height / 2;

            for (int i = 0; i < 3; i++)
            {
                int x = startX + (i * (dotSize + gap));
                int bounceOffset = (i == _dotFrame % 3) ? -8 : 0;
                int y = baseY + bounceOffset;

                using (var brush = new SolidBrush(_accentLight))
                {
                    g.FillEllipse(brush, x, y, dotSize, dotSize);
                }
            }
        }

        public void SetActivity(Activity activity)
        {
            _currentActivity = activity;
            _responses.Clear();

            if (this.InvokeRequired)
            {
                this.Invoke((Action)(() => UpdateUI()));
            }
            else
            {
                UpdateUI();
            }
        }

        private void UpdateUI()
        {
            string typeLabel = _currentActivity?.Type switch
            {
                "multiple_choice" => "📊 Multiple Choice",
                "word_cloud" => "☁️ Word Cloud",
                "short_answer" => "📝 Short Answer",
                "fill_blanks" => "📋 Fill in the Blanks",
                "slide_drawing" => "🎨 Slide Drawing",
                "image_upload" => "🖼️ Image Upload",
                "audio_record" => "🎤 Audio Record",
                "video_upload" => "📹 Video Upload",
                _ => "Activity"
            };

            _typeLabel.Text = typeLabel;
            _countLabel.Text = "0 responses";
            _responsesList.Controls.Clear();
            _titleLabel.Text = "Collecting responses...";
            _animTimer.Start();
        }

        public void AddResponse(Response response)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((Action)(() => AddResponse(response)));
                return;
            }

            var existing = _responses.FirstOrDefault(r => r.ParticipantId == response.ParticipantId);
            if (existing != null)
            {
                existing.Answer = response.Answer;
                existing.ResponseTimeMs = response.ResponseTimeMs;
                
                foreach (Control c in _responsesList.Controls)
                {
                    if (c is Panel p && p.Tag is long pid && pid == response.ParticipantId)
                    {
                        foreach (Control child in p.Controls)
                        {
                            if (child.Name == "timeLabel")
                            {
                                child.Text = $"{response.ResponseTimeMs / 1000.0:F1}s";
                                break;
                            }
                        }
                        break;
                    }
                }
            }
            else
            {
                _responses.Add(response);
                RenderResponse(response);
            }
        }

        private void RenderResponse(Response response)
        {
            _countLabel.Text = $"{_responses.Count} response{(_responses.Count != 1 ? "s" : "")}";

            // Add response row
            var row = new Panel
            {
                Width = _responsesList.Width - 24,
                Height = 36,
                BackColor = _bgCard,
                Margin = new Padding(0, 2, 0, 2),
                Padding = new Padding(10, 6, 10, 6),
                Tag = response.ParticipantId
            };

            // Checkmark
            var check = new Label
            {
                Text = "✓",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = _success,
                AutoSize = true,
                Location = new Point(8, 6)
            };
            row.Controls.Add(check);

            // Name
            var name = new Label
            {
                Text = response.ParticipantName ?? $"Student {response.ParticipantId}",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(30, 8)
            };
            row.Controls.Add(name);

            // Time
            var time = new Label
            {
                Name = "timeLabel",
                Text = $"{response.ResponseTimeMs / 1000.0:F1}s",
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(150, 255, 255, 255),
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Location = new Point(row.Width - 50, 9)
            };
            row.Controls.Add(time);

            _responsesList.Controls.Add(row);
            _responsesList.ScrollControlIntoView(row);
        }

        public void UpdateParticipantCount()
        {
            // Could update connected students count
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer?.Stop();
                _animTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
