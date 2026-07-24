using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// Quick Poll dialog — instant A/B/C/D or Yes/No poll during slideshow.
    /// </summary>
    public class QuickPollDialog : Form
    {
        private readonly ThisAddIn _addIn;

        public QuickPollDialog(ThisAddIn addIn)
        {
            _addIn = addIn;
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = "LOKAL — Quick Poll";
            this.Size = new Size(400, 360);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 10f);

            var header = new Label
            {
                Text = "Quick Poll",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59),
                AutoSize = true,
                Location = new Point(20, 16)
            };
            this.Controls.Add(header);

            var subtitle = new Label
            {
                Text = "Start an instant poll with your students.",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(100, 116, 139),
                AutoSize = true,
                Location = new Point(20, 44)
            };
            this.Controls.Add(subtitle);

            // Poll type buttons
            int y = 80;
            var types = new[]
            {
                ("Yes / No", new[] { "Yes", "No" }),
                ("True / False", new[] { "True", "False" }),
                ("A / B", new[] { "A", "B" }),
                ("A / B / C / D", new[] { "A", "B", "C", "D" }),
                ("👍 / 👎", new[] { "👍", "👎" }),
            };

            foreach (var (label, choices) in types)
            {
                var btn = new Button
                {
                    Text = label,
                    Location = new Point(20, y),
                    Size = new Size(340, 44),
                    Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = LokalUi.Primary,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                btn.FlatAppearance.BorderSize = 0;
                var capturedChoices = choices;
                btn.Click += async (s, e) =>
                {
                    await StartQuickPoll(label, capturedChoices);
                };
                this.Controls.Add(btn);
                y += 52;
            }
        }

        private async System.Threading.Tasks.Task StartQuickPoll(string label, string[] choices)
        {
            if (!_addIn.CurrentSessionId.HasValue || !_addIn.CurrentClassId.HasValue)
            {
                MessageBox.Show("Please start a slideshow with a class selected first.",
                    "LOKAL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var config = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    choices,
                    correct_answer = new int[] { },
                    allow_multiple = false
                });

                await _addIn.SessionManager.StartActivityAsync(
                    "multiple_choice", $"Quick Poll: {label}", config);

                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start poll: " + ex.Message,
                    "LOKAL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
