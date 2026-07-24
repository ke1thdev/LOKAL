using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace LOKAL.PowerPoint
{
    public class ConfigActivityPanel : UserControl
    {
        private readonly ThisAddIn _addIn;
        private string _currentActivityType = "multiple_choice";
        private Label _lblHeader;
        // Start in restore mode.  The WinForms controls fire CheckedChanged /
        // SelectedIndexChanged while InitializeUI builds the panel.  If writes are
        // allowed during that phase, those default values overwrite the activity
        // shape's saved LOKAL_CONFIG before SetActivityType can restore it.
        private bool _isRestoringConfig = true;

        // === State ===
        private int _numChoices = 4;
        private bool _allowMultiple = false;
        private bool _hasCorrectAnswer = false;
        private string _correctAnswer = "A";
        private bool _quizModeEnabled = false;
        private int _starDifficulty = 1;
        private bool _startWithSlide = true;
        private bool _minimizeAfterStart = false;
        private bool _autoCloseEnabled = false;
        private int _autoCloseSeconds = 15;

        // === UI Controls ===
        private Label[] _choiceBtns;
        private CheckBox _chkMultiple;
        private CheckBox _chkCorrect;
        private ComboBox _cmbCorrect;
        private Button _btnCorrectMulti;
        private Button _btnQuizToggle;
        private Label[] _starLabels;
        private Label _lblDifficulty;
        private CheckBox _chkStart;
        private CheckBox _chkMinimize;
        private CheckBox _chkAutoClose;
        private NumericUpDown _nudSeconds;
        private ComboBox _cmbTimeUnit;
        private Button _btnViewResponses;

        // === Colors ===
        private readonly Color _bgWhite = Color.White;
        private readonly Color _primaryBlue = LokalUi.Primary;
        private readonly Color _primaryBlueLight = LokalUi.PrimaryLight;
        private readonly Color _primaryBluePale = LokalUi.PrimaryPale;
        private readonly Color _textDark = Color.FromArgb(60, 60, 60);
        private readonly Color _textGray = Color.FromArgb(120, 120, 120);
        private readonly Color _borderGray = Color.FromArgb(230, 230, 230);
        private readonly Color _segmentBg = Color.FromArgb(242, 242, 242);
        private readonly Color _successGreen = LokalUi.Primary;

        public ConfigActivityPanel(ThisAddIn addIn)
        {
            _addIn = addIn;
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.BackColor = _bgWhite;
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;
            this.Padding = new Padding(24, 20, 24, 20);

            int y = 20;
            int contentWidth = 360;

            // ====== Header Section ======
            _lblHeader = new Label
            {
                Text = "Multiple Choice",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = _primaryBlue,
                AutoSize = true,
                Location = new Point(24, y)
            };
            this.Controls.Add(_lblHeader);

            y += 40;

            // Divider
            var divider = new Panel
            {
                Height = 1,
                BackColor = _borderGray,
                Width = contentWidth,
                Location = new Point(24, y)
            };
            this.Controls.Add(divider);
            y += 20;

            // ====== Number of Choices ======
            var lblNumChoices = new Label
            {
                Text = "Number of choices",
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                ForeColor = _textDark,
                AutoSize = true,
                Location = new Point(24, y)
            };
            this.Controls.Add(lblNumChoices);
            y += 28;

            // Segmented control
            var flpChoices = new Panel
            {
                Location = new Point(24, y),
                Width = contentWidth,
                Height = 42,
                BackColor = _segmentBg
            };
            flpChoices.Paint += (s, e) =>
            {
                using (var path = GetRoundedRectPath(new Rectangle(0, 0, flpChoices.Width, flpChoices.Height), 6))
                {
                    flpChoices.Region = new Region(path);
                }
            };
            this.Controls.Add(flpChoices);

            _choiceBtns = new Label[7]; // 2-8
            int segWidth = contentWidth / 7;
            for (int i = 0; i < 7; i++)
            {
                int num = i + 2;
                var btn = new Label
                {
                    Text = num.ToString(),
                    Width = segWidth,
                    Height = 42,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 10.5f, (num == _numChoices) ? FontStyle.Bold : FontStyle.Regular),
                    Cursor = Cursors.Hand,
                    Location = new Point(i * segWidth, 0),
                    Tag = num
                };

                if (num == _numChoices)
                {
                    btn.BackColor = _primaryBlue;
                    btn.ForeColor = Color.White;
                }
                else
                {
                    btn.BackColor = Color.Transparent;
                    btn.ForeColor = _textGray;
                }

                int capturedNum = num;
                int capturedIdx = i;
                btn.Click += (s, e) => SelectNumChoices(capturedNum);
                btn.MouseEnter += (s, e) => { if (capturedNum != _numChoices) btn.BackColor = Color.FromArgb(230, 230, 230); };
                btn.MouseLeave += (s, e) => { if (capturedNum != _numChoices) btn.BackColor = Color.Transparent; };

                _choiceBtns[i] = btn;
                flpChoices.Controls.Add(btn);
            }
            y += 58;

            // ====== Checkboxes ======
            _chkMultiple = new CheckBox
            {
                Text = "Allow selecting multiple choices",
                Font = new Font("Segoe UI", 10f),
                ForeColor = _textDark,
                AutoSize = true,
                Location = new Point(24, y),
                Checked = _allowMultiple
            };
            _chkMultiple.CheckedChanged += (s, e) =>
            {
                _allowMultiple = _chkMultiple.Checked;
                if (_hasCorrectAnswer)
                {
                    _btnCorrectMulti.Visible = _allowMultiple;
                    _cmbCorrect.Visible = !_allowMultiple;
                }
                
                if (!_allowMultiple && _correctAnswer.Contains(","))
                {
                    _correctAnswer = _correctAnswer.Split(',')[0];
                    if (_cmbCorrect.Items.Contains(_correctAnswer))
                        _cmbCorrect.SelectedItem = _correctAnswer;
                }
                else if (_allowMultiple)
                {
                    _btnCorrectMulti.Text = string.IsNullOrEmpty(_correctAnswer) ? "None" : _correctAnswer;
                }
                
                SaveConfigToShape();
            };
            this.Controls.Add(_chkMultiple);
            y += 32;

            _chkCorrect = new CheckBox
            {
                Text = "Has correct answer(s)",
                Font = new Font("Segoe UI", 10f),
                ForeColor = _textDark,
                AutoSize = true,
                Location = new Point(24, y),
                Checked = _hasCorrectAnswer
            };
            _chkCorrect.CheckedChanged += (s, e) =>
            {
                _hasCorrectAnswer = _chkCorrect.Checked;
                if (_allowMultiple)
                {
                    _btnCorrectMulti.Visible = _hasCorrectAnswer;
                    _cmbCorrect.Visible = false;
                }
                else
                {
                    _cmbCorrect.Visible = _hasCorrectAnswer;
                    _cmbCorrect.Enabled = _hasCorrectAnswer;
                    _btnCorrectMulti.Visible = false;
                }
                SaveConfigToShape();
            };
            this.Controls.Add(_chkCorrect);

            _cmbCorrect = new ComboBox
            {
                Location = new Point(contentWidth - 60 + 24, y - 2),
                Width = 60,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5f),
                Enabled = false,
                Visible = false
            };
            _cmbCorrect.SelectedIndexChanged += (s, e) =>
            {
                if (_cmbCorrect.SelectedItem != null && !_allowMultiple)
                    _correctAnswer = _cmbCorrect.SelectedItem.ToString();
                SaveConfigToShape();
            };
            this.Controls.Add(_cmbCorrect);

            _btnCorrectMulti = new Button
            {
                Location = new Point(contentWidth - 100 + 24, y - 2),
                Width = 100,
                Height = 25,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "A",
                Visible = false
            };
            _btnCorrectMulti.FlatAppearance.BorderColor = _borderGray;
            _btnCorrectMulti.Click += (s, e) =>
            {
                var menu = new ContextMenuStrip();
                for (int i = 0; i < _numChoices; i++)
                {
                    string letter = ((char)('A' + i)).ToString();
                    var item = new ToolStripMenuItem(letter)
                    {
                        CheckOnClick = true,
                        Checked = _correctAnswer.Split(',').Contains(letter)
                    };
                    item.CheckedChanged += (s2, e2) =>
                    {
                        var selected = new System.Collections.Generic.List<string>();
                        foreach (ToolStripMenuItem mi in menu.Items)
                        {
                            if (mi.Checked) selected.Add(mi.Text);
                        }
                        _correctAnswer = string.Join(",", selected);
                        _btnCorrectMulti.Text = string.IsNullOrEmpty(_correctAnswer) ? "None" : _correctAnswer;
                        SaveConfigToShape();
                    };
                    menu.Items.Add(item);
                }
                menu.Closing += (s2, e2) =>
                {
                    if (e2.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
                        e2.Cancel = true;
                };
                menu.Show(_btnCorrectMulti, new Point(0, _btnCorrectMulti.Height));
            };
            this.Controls.Add(_btnCorrectMulti);

            UpdateCorrectAnswerOptions();
            y += 40;

            // ====== Quiz Mode Panel ======
            var pnlQuizMode = new Panel
            {
                Location = new Point(24, y),
                Width = contentWidth,
                Height = 90,
                BackColor = _primaryBluePale
            };
            pnlQuizMode.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(_primaryBlueLight, 1.5f))
                using (var path = GetRoundedRectPath(new Rectangle(1, 1, pnlQuizMode.Width - 3, pnlQuizMode.Height - 3), 10))
                {
                    e.Graphics.DrawPath(pen, path);
                }
                using (var pen = new Pen(_primaryBlueLight, 1f))
                {
                    e.Graphics.DrawLine(pen, 16, 44, pnlQuizMode.Width - 16, 44);
                }
            };
            this.Controls.Add(pnlQuizMode);

            var lblQuizMode = new Label
            {
                Text = "Quiz mode",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = _primaryBlue,
                AutoSize = true,
                Location = new Point(16, 12),
                BackColor = Color.Transparent
            };
            pnlQuizMode.Controls.Add(lblQuizMode);

            var lblQuizModeInfo = new Label
            {
                Text = "i",
                Font = new Font("Georgia", 9f, FontStyle.Italic | FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = _primaryBlue,
                AutoSize = false,
                Size = new Size(16, 16),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(lblQuizMode.Right + 8, 14),
                Cursor = Cursors.Help
            };
            var pathInfo = new System.Drawing.Drawing2D.GraphicsPath();
            pathInfo.AddEllipse(0, 0, 16, 16);
            lblQuizModeInfo.Region = new Region(pathInfo);
            
            var toolTip = new ToolTip();
            toolTip.SetToolTip(lblQuizModeInfo, "Enable Quiz mode to automatically grade students and award stars only for correct answers.\n\nDifficulty Scaling:\n★ = Easy (1 star)\n★★ = Medium (2 stars)\n★★★ = Hard (3 stars)");
            
            pnlQuizMode.Controls.Add(lblQuizModeInfo);

            // Toggle button
            _btnQuizToggle = new Button
            {
                Text = "",
                BackColor = _borderGray,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(48, 24),
                Location = new Point(contentWidth - 68, 12),
                Cursor = Cursors.Hand
            };
            _btnQuizToggle.FlatAppearance.BorderSize = 0;
            _btnQuizToggle.Paint += PaintToggleSwitch;
            _btnQuizToggle.Click += (s, e) =>
            {
                _quizModeEnabled = !_quizModeEnabled;
                // Quiz Mode has no meaningful scoring without a correct answer.
                // Keep the ClassPoint-style one-click workflow deterministic by
                // enabling the existing default answer (A) when Quiz Mode turns on.
                if (_quizModeEnabled && !_hasCorrectAnswer)
                {
                    _chkCorrect.Checked = true;
                    if (string.IsNullOrWhiteSpace(_correctAnswer))
                        _correctAnswer = "A";
                    if (!_allowMultiple && _cmbCorrect.Items.Contains(_correctAnswer))
                        _cmbCorrect.SelectedItem = _correctAnswer;
                }
                _btnQuizToggle.Invalidate();
                UpdateStarLabels();
                SaveConfigToShape();
            };
            pnlQuizMode.Controls.Add(_btnQuizToggle);

            // Star difficulty labels
            _starLabels = new Label[3];
            for (int i = 0; i < 3; i++)
            {
                int starIdx = i;
                _starLabels[i] = new Label
                {
                    Text = "★",
                    Font = new Font("Segoe UI", 16f),
                    ForeColor = _primaryBlueLight,
                    AutoSize = true,
                    Location = new Point(16 + i * 28, 52),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                _starLabels[i].Click += (s, e) =>
                {
                    if (!_quizModeEnabled) return;
                    _starDifficulty = starIdx + 1;
                    UpdateStarLabels();
                    SaveConfigToShape();
                };
                pnlQuizMode.Controls.Add(_starLabels[i]);
            }

            _lblDifficulty = new Label
            {
                Text = "Easy · awards 1 star",
                Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
                ForeColor = _textGray,
                AutoSize = true,
                Location = new Point(112, 57),
                BackColor = Color.Transparent
            };
            pnlQuizMode.Controls.Add(_lblDifficulty);
            UpdateStarLabels();

            y += 110;

            // ====== Play Options ======
            var lblPlayOptions = new Label
            {
                Text = "Play Options",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = _textDark,
                AutoSize = true,
                Location = new Point(24, y)
            };
            this.Controls.Add(lblPlayOptions);

            var lblSaveDefault = new Label
            {
                Text = "Save as default",
                Font = new Font("Segoe UI", 9f),
                ForeColor = _primaryBlue,
                AutoSize = true,
                Location = new Point(160, y + 2),
                Cursor = Cursors.Hand
            };
            lblSaveDefault.Click += (s, e) =>
            {
                try
                {
                    var correctArr = new System.Collections.Generic.List<int>();
                    if (_hasCorrectAnswer && !string.IsNullOrEmpty(_correctAnswer))
                    {
                        foreach (var part in _correctAnswer.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (part.Length > 0 && part[0] >= 'A' && part[0] <= 'Z')
                                correctArr.Add(part[0] - 'A');
                        }
                    }

                    var config = new
                    {
                        num_choices = _numChoices,
                        allow_multiple = _allowMultiple,
                        correct_answer = correctArr.ToArray(),
                        quiz_mode = _quizModeEnabled,
                        difficulty = _starDifficulty,
                        start_with_slide = _startWithSlide,
                        minimize_after_start = _minimizeAfterStart,
                        auto_close_enabled = _autoCloseEnabled,
                        auto_close_seconds = _autoCloseEnabled ? (_cmbTimeUnit.SelectedIndex == 0 ? (int)_nudSeconds.Value : (int)_nudSeconds.Value * 60) : 0
                    };
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(config);
                    string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LOKAL");
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "DefaultActivityConfig.json"), json);
                    
                    var oldText = lblSaveDefault.Text;
                    lblSaveDefault.Text = "Saved!";
                    lblSaveDefault.ForeColor = Color.Green;
                    var timer = new System.Windows.Forms.Timer { Interval = 2000 };
                    timer.Tick += (ts, te) =>
                    {
                        lblSaveDefault.Text = oldText;
                        lblSaveDefault.ForeColor = _primaryBlue;
                        timer.Stop();
                        timer.Dispose();
                    };
                    timer.Start();
                }
                catch { }
            };
            this.Controls.Add(lblSaveDefault);
            y += 32;

            _chkStart = new CheckBox
            {
                Text = "Start activity with slide",
                Font = new Font("Segoe UI", 10f),
                ForeColor = _textDark,
                AutoSize = true,
                Location = new Point(24, y),
                Checked = _startWithSlide
            };
            _chkStart.CheckedChanged += (s, e) => { _startWithSlide = _chkStart.Checked; SaveConfigToShape(); };
            this.Controls.Add(_chkStart);
            y += 30;

            _chkMinimize = new CheckBox
            {
                Text = "Minimize activity window after activity starts",
                Font = new Font("Segoe UI", 10f),
                ForeColor = _textDark,
                AutoSize = true,
                Location = new Point(24, y)
            };
            _chkMinimize.CheckedChanged += (s, e) => { _minimizeAfterStart = _chkMinimize.Checked; SaveConfigToShape(); };
            this.Controls.Add(_chkMinimize);
            y += 30;

            _chkAutoClose = new CheckBox
            {
                Text = "Auto-close submission after",
                Font = new Font("Segoe UI", 10f),
                ForeColor = _textDark,
                AutoSize = true,
                Location = new Point(24, y)
            };
            _chkAutoClose.CheckedChanged += (s, e) =>
            {
                _autoCloseEnabled = _chkAutoClose.Checked;
                _nudSeconds.Enabled = _autoCloseEnabled;
                _cmbTimeUnit.Enabled = _autoCloseEnabled;
                SaveConfigToShape();
            };
            this.Controls.Add(_chkAutoClose);

            _nudSeconds = new NumericUpDown
            {
                Location = new Point(contentWidth - 100 + 24, y - 2),
                Width = 50,
                Minimum = 5,
                Maximum = 300,
                Value = 15,
                Font = new Font("Segoe UI", 9f),
                Enabled = false
            };
            _nudSeconds.ValueChanged += (s, e) => { _autoCloseSeconds = (int)_nudSeconds.Value; SaveConfigToShape(); };
            this.Controls.Add(_nudSeconds);

            _cmbTimeUnit = new ComboBox
            {
                Location = new Point(contentWidth - 44 + 24, y - 2),
                Width = 48,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f),
                Enabled = false
            };
            _cmbTimeUnit.Items.AddRange(new object[] { "sec", "min" });
            _cmbTimeUnit.SelectedIndex = 0;
            _cmbTimeUnit.SelectedIndexChanged += (s, e) =>
            {
                bool minutes = _cmbTimeUnit.SelectedIndex == 1;
                decimal value = _nudSeconds.Value;
                _nudSeconds.Minimum = minutes ? 1 : 5;
                _nudSeconds.Maximum = minutes ? 60 : 300;
                _nudSeconds.Value = Math.Max(_nudSeconds.Minimum,
                    Math.Min(_nudSeconds.Maximum, value));
                SaveConfigToShape();
            };
            this.Controls.Add(_cmbTimeUnit);
            y += 50;

            // ====== View Responses Button ======
            _btnViewResponses = new Button
            {
                Text = "View Responses",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = _primaryBlue,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(contentWidth - 60, 44),
                Location = new Point(24 + 30, y),
                Cursor = Cursors.Default,
                Enabled = false
            };
            _btnViewResponses.FlatAppearance.BorderSize = 0;
            _btnViewResponses.Click += BtnViewResponses_Click;

            // Rounded corners for view responses button
            _btnViewResponses.Paint += (s, e) =>
            {
                using (var path = GetRoundedRectPath(new Rectangle(0, 0, _btnViewResponses.Width, _btnViewResponses.Height), 8))
                {
                    _btnViewResponses.Region = new Region(path);
                }
            };
            this.Controls.Add(_btnViewResponses);
        }

        // ====== Event Handlers ======

        /// <summary>
        /// Persists the current panel config into the activity shape's tags so the
        /// slideshow can auto-start the activity with the right settings.
        /// </summary>
        internal void SaveConfigToShape()
        {
            if (_isRestoringConfig) return;
            try
            {
                var shape = _addIn.CurrentActivityShape;
                if (shape == null) return;

                int autoClose = _autoCloseEnabled ? _autoCloseSeconds : 0;
                if (_cmbTimeUnit != null && _cmbTimeUnit.SelectedIndex == 1) // "min"
                    autoClose *= 60;

                var correctArr = new System.Collections.Generic.List<int>();
                if (_hasCorrectAnswer && !string.IsNullOrEmpty(_correctAnswer))
                {
                    foreach (var part in _correctAnswer.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (part.Length > 0 && part[0] >= 'A' && part[0] <= 'Z')
                            correctArr.Add(part[0] - 'A');
                    }
                }

                var config = new
                {
                    num_choices = _numChoices,
                    allow_multiple = _allowMultiple,
                    correct_answer = correctArr.ToArray(),
                    quiz_mode = _quizModeEnabled,
                    difficulty = _starDifficulty,
                    start_with_slide = _startWithSlide,
                    minimize_after_start = _minimizeAfterStart,
                    auto_close_enabled = _autoCloseEnabled,
                    auto_close_seconds = autoClose
                };

                _addIn.PersistActivityConfig(shape, JsonConvert.SerializeObject(config));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not persist activity options: " + ex.Message);
            }
        }

        private void SelectNumChoices(int num)
        {
            _numChoices = num;
            for (int i = 0; i < _choiceBtns.Length; i++)
            {
                int btnNum = i + 2;
                if (btnNum == _numChoices)
                {
                    _choiceBtns[i].BackColor = _primaryBlue;
                    _choiceBtns[i].ForeColor = Color.White;
                    _choiceBtns[i].Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
                }
                else
                {
                    _choiceBtns[i].BackColor = Color.Transparent;
                    _choiceBtns[i].ForeColor = _textGray;
                    _choiceBtns[i].Font = new Font("Segoe UI", 10.5f, FontStyle.Regular);
                }
            }
            UpdateCorrectAnswerOptions();
            SaveConfigToShape();
        }

        private void UpdateCorrectAnswerOptions()
        {
            _cmbCorrect.Items.Clear();
            for (int i = 0; i < _numChoices; i++)
            {
                _cmbCorrect.Items.Add(((char)('A' + i)).ToString());
            }
            if (_cmbCorrect.Items.Count > 0)
            {
                var parts = _correctAnswer.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var validParts = parts.Where(p => _cmbCorrect.Items.Contains(p)).ToList();
                if (validParts.Count == 0)
                {
                    _correctAnswer = "A";
                    _cmbCorrect.SelectedIndex = 0;
                }
                else
                {
                    _correctAnswer = string.Join(",", validParts);
                    _cmbCorrect.SelectedItem = validParts[0];
                }
                
                if (_btnCorrectMulti != null)
                    _btnCorrectMulti.Text = string.IsNullOrEmpty(_correctAnswer) ? "None" : _correctAnswer;
            }
        }

        private void UpdateStarLabels()
        {
            for (int i = 0; i < 3; i++)
            {
                if (_quizModeEnabled && i < _starDifficulty)
                    _starLabels[i].ForeColor = Color.FromArgb(250, 200, 50); // Gold
                else
                    _starLabels[i].ForeColor = _primaryBlueLight;

                _starLabels[i].Cursor = _quizModeEnabled ? Cursors.Hand : Cursors.Default;
            }

            if (_lblDifficulty != null)
            {
                string[] names = { "Easy", "Intermediate", "Difficult" };
                _lblDifficulty.Text = names[Math.Max(1, Math.Min(3, _starDifficulty)) - 1]
                    + " · awards " + _starDifficulty + (_starDifficulty == 1 ? " star" : " stars");
                _lblDifficulty.ForeColor = _quizModeEnabled ? _textDark : _textGray;
            }
        }

        private void PaintToggleSwitch(object sender, PaintEventArgs e)
        {
            var btn = (Button)sender;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Color trackColor = _quizModeEnabled ? _primaryBlue : Color.FromArgb(200, 200, 200);
            int w = btn.Width;
            int h = btn.Height;
            int radius = h / 2;

            // Track
            using (var brush = new SolidBrush(trackColor))
            using (var path = GetRoundedRectPath(new Rectangle(0, 0, w, h), radius))
            {
                e.Graphics.FillPath(brush, path);
            }

            // Thumb
            int thumbD = h - 6;
            int thumbX = _quizModeEnabled ? (w - thumbD - 3) : 3;
            using (var brush = new SolidBrush(Color.White))
            {
                e.Graphics.FillEllipse(brush, thumbX, 3, thumbD, thumbD);
            }
        }

        private async void BtnViewResponses_Click(object sender, EventArgs e)
        {
            if (!_addIn.GetResponseActivityIdForSelectedShape().HasValue) return;
            try
            {
                _btnViewResponses.Enabled = false;
                _btnViewResponses.Text = "Opening...";
                bool opened = await _addIn.ShowCurrentResponsesAsync();
                SetResponseAvailability(opened);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not open activity responses: " + ex.Message);
                SetResponseAvailability(false);
            }
        }

        internal void SetResponseAvailability(bool available)
        {
            Action update = () =>
            {
                if (_btnViewResponses == null || _btnViewResponses.IsDisposed) return;
                _btnViewResponses.Enabled = available;
                _btnViewResponses.Text = "View Responses";
                _btnViewResponses.BackColor = available ? _primaryBlue : Color.FromArgb(203, 213, 225);
                _btnViewResponses.Cursor = available ? Cursors.Hand : Cursors.Default;
            };
            if (InvokeRequired) BeginInvoke(update); else update();
        }

        private async Task RefreshResponseAvailabilityAsync()
        {
            bool available = false;
            try
            {
                long? activityId = _addIn.GetResponseActivityIdForSelectedShape();
                if (activityId.HasValue)
                {
                    var responses = await _addIn.ApiClient.GetResponsesAsync(activityId.Value);
                    available = responses != null && responses.Count > 0;
                }
            }
            catch { }
            SetResponseAvailability(available);
        }

        /// <summary>Restores panel state from the shape's LOKAL_CONFIG tag.</summary>
        private void LoadConfigFromShape()
        {
            try
            {
                _isRestoringConfig = true;
                var shape = _addIn.CurrentActivityShape;
                if (shape == null) return;

                string json = _addIn.ReadActivityConfig(shape);
                if (string.IsNullOrEmpty(json)) return;

                var cfg = Newtonsoft.Json.Linq.JObject.Parse(json);

                int numChoices = cfg.Value<int?>("num_choices") ?? 4;
                if (numChoices >= 2 && numChoices <= 8 && numChoices != _numChoices)
                    SelectNumChoices(numChoices);

                _chkMultiple.Checked = cfg.Value<bool?>("allow_multiple") ?? false;

                var correctToken = cfg["correct_answer"];
                string correctStr = "";
                if (correctToken != null && correctToken.Type == Newtonsoft.Json.Linq.JTokenType.Array)
                {
                    var cArr = correctToken as Newtonsoft.Json.Linq.JArray;
                    if (cArr != null && cArr.Count > 0)
                        correctStr = string.Join(",", cArr.Select(t => ((char)('A' + (int)t)).ToString()));
                }
                else if (correctToken != null && correctToken.Type == Newtonsoft.Json.Linq.JTokenType.String)
                {
                    correctStr = correctToken.ToString();
                }

                _chkCorrect.Checked = !string.IsNullOrEmpty(correctStr);
                if (!string.IsNullOrEmpty(correctStr))
                {
                    _correctAnswer = correctStr;
                    if (_allowMultiple)
                    {
                        if (_btnCorrectMulti != null)
                            _btnCorrectMulti.Text = _correctAnswer;
                    }
                    else if (_cmbCorrect.Items.Contains(correctStr))
                    {
                        _cmbCorrect.SelectedItem = correctStr;
                    }
                }

                _quizModeEnabled = cfg.Value<bool?>("quiz_mode") ?? false;
                _btnQuizToggle.Invalidate();
                _starDifficulty = Math.Max(1, Math.Min(3, cfg.Value<int?>("difficulty") ?? 1));
                UpdateStarLabels();

                _chkStart.Checked = cfg.Value<bool?>("start_with_slide") ?? true;
                _chkMinimize.Checked = cfg.Value<bool?>("minimize_after_start") ?? false;

                // The enabled flag is deliberately separate from the last-entered
                // duration.  Old configs did not carry this flag, which could make
                // a stale value such as 15 seconds launch an unwanted countdown.
                bool autoCloseEnabled = cfg.Value<bool?>("auto_close_enabled") ?? false;
                int autoClose = autoCloseEnabled ? (cfg.Value<int?>("auto_close_seconds") ?? 0) : 0;
                _chkAutoClose.Checked = autoCloseEnabled && autoClose > 0;
                if (_chkAutoClose.Checked)
                {
                    if (autoClose % 60 == 0 && autoClose >= 60)
                    {
                        _cmbTimeUnit.SelectedIndex = 1;
                        _nudSeconds.Value = Math.Min(_nudSeconds.Maximum, autoClose / 60);
                    }
                    else
                    {
                        _cmbTimeUnit.SelectedIndex = 0;
                        _nudSeconds.Value = Math.Min(_nudSeconds.Maximum, Math.Max(_nudSeconds.Minimum, autoClose));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not restore activity options: " + ex.Message);
            }
            finally
            {
                _isRestoringConfig = false;
            }
        }

        public void SetActivityType(string type)
        {
            _currentActivityType = type;
            LoadConfigFromShape();
            switch (type)
            {
                case "multiple_choice": _lblHeader.Text = "Multiple Choice"; break;
                case "word_cloud": _lblHeader.Text = "Word Cloud"; break;
                case "short_answer": _lblHeader.Text = "Short Answer"; break;
                case "slide_drawing": _lblHeader.Text = "Slide Drawing"; break;
                case "image_upload": _lblHeader.Text = "Image Upload"; break;
                case "fill_blanks": _lblHeader.Text = "Fill in the Blanks"; break;
                case "audio_record": _lblHeader.Text = "Audio Record"; break;
                case "video_upload": _lblHeader.Text = "Video Upload"; break;
                default: _lblHeader.Text = "Activity Options"; break;
            }

            // ClassPoint-style behavior: this is a viewer, not an activity-start
            // button. It becomes available only after a response exists.
            SetResponseAvailability(false);
            Task responseRefresh = RefreshResponseAvailabilityAsync();
        }

        private GraphicsPath GetRoundedRectPath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            Size size = new Size(diameter, diameter);
            Rectangle arc = new Rectangle(bounds.Location, size);
            var path = new GraphicsPath();

            if (radius == 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
