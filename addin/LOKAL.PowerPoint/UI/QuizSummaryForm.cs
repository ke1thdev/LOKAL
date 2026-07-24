using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// In-PowerPoint Quiz Mode summary: participation, correct answers, stars
    /// and answer speed, with a CSV export for after-class review.
    /// </summary>
    public sealed class QuizSummaryForm : Form
    {
        private readonly QuizSessionSummary _summary;
        private readonly DataGridView _grid;

        public QuizSummaryForm(QuizSessionSummary summary)
        {
            _summary = summary ?? new QuizSessionSummary();
            Text = "LOKAL — Quiz Summary";
            Size = new Size(900, 620);
            MinimumSize = new Size(720, 480);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 10f);

            var title = new Label
            {
                Dock = DockStyle.Top,
                Height = 72,
                Text = "Quiz Mode summary",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            Controls.Add(title);

            var participation = (_summary.Rows ?? new System.Collections.Generic.List<QuizSummaryRow>())
                .Count(r => r.SubmittedCount > 0);
            var subtitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 42,
                Text = string.Format("{0} questions  •  {1}/{2} students participated",
                    _summary.QuestionCount, participation, (_summary.Rows ?? new System.Collections.Generic.List<QuizSummaryRow>()).Count),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(100, 116, 139)
            };
            Controls.Add(subtitle);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Padding = new Padding(6)
            };
            _grid.EnableHeadersVisualStyles = false;
            _grid.RowTemplate.Height = 42;
            _grid.Columns.Add("rank", "#");
            _grid.Columns.Add("name", "Student");
            _grid.Columns.Add("participation", "Participation");
            _grid.Columns.Add("correct", "Correct");
            _grid.Columns.Add("stars", "Stars");
            _grid.Columns.Add("speed", "Avg. speed");
            Controls.Add(_grid);

            int rank = 1;
            foreach (var row in _summary.Rows ?? new System.Collections.Generic.List<QuizSummaryRow>())
            {
                string participationText = _summary.QuestionCount <= 0
                    ? "0%"
                    : Math.Round(row.SubmittedCount * 100d / _summary.QuestionCount) + "%";
                string speed = row.AverageTimeMs <= 0 ? "—" : (row.AverageTimeMs / 1000d).ToString("0.00") + " s";
                _grid.Rows.Add(rank++, row.Name, participationText,
                    row.CorrectCount + "/" + _summary.QuestionCount, row.StarsEarned, speed);
            }

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 78, BackColor = Color.White };
            var export = new Button
            {
                Text = "Export CSV",
                Size = new Size(180, 44),
                Location = new Point((ClientSize.Width - 180) / 2, 16),
                Anchor = AnchorStyles.Top,
                FlatStyle = FlatStyle.Flat,
                BackColor = LokalUi.Primary,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            export.FlatAppearance.BorderSize = 0;
            export.Click += (s, e) => ExportCsv();
            footer.Controls.Add(export);
            Controls.Add(footer);
        }

        private void ExportCsv()
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = "LOKAL-quiz-summary.csv",
                Title = "Export Quiz Summary"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var csv = new StringBuilder();
                csv.AppendLine("Rank,Student,Questions,Submitted,Correct,Stars,Average response (ms)");
                int rank = 1;
                foreach (var row in _summary.Rows ?? new System.Collections.Generic.List<QuizSummaryRow>())
                {
                    csv.Append(rank++).Append(',')
                        .Append(Escape(row.Name)).Append(',')
                        .Append(_summary.QuestionCount).Append(',')
                        .Append(row.SubmittedCount).Append(',')
                        .Append(row.CorrectCount).Append(',')
                        .Append(row.StarsEarned).Append(',')
                        .Append(Math.Round(row.AverageTimeMs)).AppendLine();
                }
                File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
            }
        }

        private static string Escape(string value)
        {
            value = value ?? string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
