using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// Class code dialog — lets teacher select a class before starting a session.
    /// Shows list of their classes or lets them create a new one.
    /// </summary>
    public class ClassCodeDialog : Form
    {
        private readonly ThisAddIn _addIn;
        public string SelectedCode { get; private set; }
        public long? SelectedClassId { get; private set; }

        private ListBox _classList;
        private List<Class> _classes;

        public ClassCodeDialog(ThisAddIn addIn)
        {
            _addIn = addIn;
            InitializeUI();
            LoadClasses();
        }

        private void InitializeUI()
        {
            this.Text = "LOKAL — Select Class";
            this.Size = new Size(420, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 10f);

            // Header
            var header = new Label
            {
                Text = "Select a class",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                AutoSize = true,
                Location = new Point(20, 16)
            };
            this.Controls.Add(header);

            var subtitle = new Label
            {
                Text = "Choose which class will join this session.",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Location = new Point(20, 44)
            };
            this.Controls.Add(subtitle);

            // Class list
            _classList = new ListBox
            {
                Location = new Point(20, 72),
                Size = new Size(360, 320),
                Font = new Font("Segoe UI", 11f),
                BorderStyle = BorderStyle.FixedSingle,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 52
            };
            _classList.DrawItem += ClassList_DrawItem;
            _classList.DoubleClick += (s, e) => SelectClass();
            this.Controls.Add(_classList);

            // Buttons
            var selectBtn = new Button
            {
                Text = "Select",
                Location = new Point(200, 410),
                Size = new Size(90, 36),
                BackColor = LokalUi.Primary,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            selectBtn.FlatAppearance.BorderSize = 0;
            selectBtn.Click += (s, e) => SelectClass();
            this.Controls.Add(selectBtn);

            var cancelBtn = new Button
            {
                Text = "Cancel",
                Location = new Point(296, 410),
                Size = new Size(84, 36),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f),
                Cursor = Cursors.Hand
            };
            cancelBtn.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(cancelBtn);
        }

        private void ClassList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || _classes == null) return;

            e.DrawBackground();
            var cls = _classes[e.Index];
            bool isSelected = (e.State & DrawItemState.Selected) != 0;

            // Background
            using (var bg = new SolidBrush(isSelected ?
                Color.FromArgb(224, 231, 255) : Color.White))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }

            // Avatar circle
            var avatarColor = ColorTranslator.FromHtml(cls.AvatarColor ?? "#F97316");
            using (var brush = new SolidBrush(avatarColor))
            {
                e.Graphics.FillEllipse(brush, e.Bounds.X + 8, e.Bounds.Y + 10, 32, 32);
            }
            // Avatar letter
            var letter = cls.Name.Substring(0, 1).ToUpper();
            using (var font = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.White))
            {
                var size = e.Graphics.MeasureString(letter, font);
                e.Graphics.DrawString(letter, font, brush,
                    e.Bounds.X + 8 + (32 - size.Width) / 2,
                    e.Bounds.Y + 10 + (32 - size.Height) / 2);
            }

            // Class name
            using (var font = new Font("Segoe UI", 10f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(30, 41, 59)))
            {
                e.Graphics.DrawString(cls.Name, font, brush, e.Bounds.X + 48, e.Bounds.Y + 8);
            }

            // Class code
            using (var font = new Font("Segoe UI", 9f))
            using (var brush = new SolidBrush(Color.FromArgb(20, 184, 166)))
            {
                e.Graphics.DrawString($"Code: {cls.Code}  ·  {cls.ParticipantCount} students",
                    font, brush, e.Bounds.X + 48, e.Bounds.Y + 28);
            }
        }

        private async void LoadClasses()
        {
            try
            {
                _classes = await _addIn.ApiClient.GetClassesAsync();
                _classList.Items.Clear();
                foreach (var cls in _classes)
                {
                    _classList.Items.Add(cls.Name);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load classes: " + ex.Message +
                    "\n\nMake sure the LOKAL server is running.",
                    "LOKAL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SelectClass()
        {
            if (_classList.SelectedIndex < 0 || _classes == null) return;

            var selected = _classes[_classList.SelectedIndex];
            SelectedCode = selected.Code;
            SelectedClassId = selected.Id;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
