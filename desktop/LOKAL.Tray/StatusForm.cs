using System;
using System.Drawing;
using System.Windows.Forms;

namespace LOKAL.Tray
{
    internal sealed class StatusForm : Form
    {
        private static readonly Color Forest = Color.FromArgb(11, 31, 28);
        private static readonly Color Teal = Color.FromArgb(14, 116, 103);
        private static readonly Color Canvas = Color.FromArgb(245, 248, 247);
        private static readonly Color Muted = Color.FromArgb(99, 115, 112);
        private readonly Label stateLabel;
        private readonly Label stateDetail;
        private readonly Label modeLabel;
        private readonly Label listenLabel;
        private readonly LinkLabel teacherLink;
        private readonly LinkLabel studentLink;
        private readonly Button startButton;
        private readonly Button restartButton;
        private readonly Button stopButton;
        private bool closeForExit;
        private StatusSnapshot snapshot;

        public event EventHandler HideRequested;
        public event Action<string> ServiceCommandRequested;
        public event EventHandler RefreshRequested;
        public event EventHandler OpenTeacherRequested;
        public event EventHandler OpenStudentRequested;

        public StatusForm()
        {
            Text = "LOKAL — Server Status";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(720, 500);
            Size = new Size(820, 560);
            BackColor = Canvas;
            Font = new Font("Segoe UI", 10F);

            var header = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Forest, Padding = new Padding(30, 22, 30, 18) };
            var title = new Label { Text = "LOKAL", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 22F), AutoSize = true, Location = new Point(30, 18) };
            var subtitle = new Label { Text = "Hybrid classroom server status", ForeColor = Color.FromArgb(190, 220, 214), Font = new Font("Segoe UI", 10F), AutoSize = true, Location = new Point(33, 62) };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 28, 28, 22), ColumnCount = 2, RowCount = 2, BackColor = Canvas };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            var healthCard = Card("SERVER HEALTH");
            stateLabel = new Label { Text = "Checking…", ForeColor = Forest, Font = new Font("Segoe UI Semibold", 20F), AutoSize = true, Location = new Point(22, 54) };
            stateDetail = new Label { Text = "Reading the Windows service and local endpoint", ForeColor = Muted, AutoSize = false, Location = new Point(24, 96), Size = new Size(300, 48) };
            healthCard.Controls.Add(stateLabel);
            healthCard.Controls.Add(stateDetail);

            var modeCard = Card("OPERATING MODE");
            modeLabel = new Label { Text = "Local Network", ForeColor = Forest, Font = new Font("Segoe UI Semibold", 18F), AutoSize = true, Location = new Point(22, 54) };
            listenLabel = new Label { Text = "Listener: checking…", ForeColor = Muted, AutoSize = false, Location = new Point(24, 95), Size = new Size(330, 44) };
            modeCard.Controls.Add(modeLabel);
            modeCard.Controls.Add(listenLabel);

            var accessCard = Card("QUICK ACCESS");
            teacherLink = Link("Teacher Dashboard", 52, (_, __) => OpenTeacherRequested?.Invoke(this, EventArgs.Empty));
            studentLink = Link("Student Join Page", 96, (_, __) => OpenStudentRequested?.Invoke(this, EventArgs.Empty));
            accessCard.Controls.Add(teacherLink);
            accessCard.Controls.Add(studentLink);

            var controlCard = Card("SERVER CONTROLS");
            startButton = ActionButton("Start", new Point(22, 58), false, (_, __) => ServiceCommandRequested?.Invoke("start"));
            restartButton = ActionButton("Restart", new Point(118, 58), true, (_, __) => ServiceCommandRequested?.Invoke("restart"));
            stopButton = ActionButton("Stop", new Point(232, 58), false, (_, __) => ServiceCommandRequested?.Invoke("stop"));
            var logsButton = ActionButton("Open logs", new Point(22, 105), false, (_, __) => ServerStatusClient.OpenLogs());
            logsButton.Width = 112;
            var refreshButton = ActionButton("Refresh", new Point(146, 105), false, (_, __) => RefreshRequested?.Invoke(this, EventArgs.Empty));
            controlCard.Controls.Add(startButton);
            controlCard.Controls.Add(restartButton);
            controlCard.Controls.Add(stopButton);
            controlCard.Controls.Add(logsButton);
            controlCard.Controls.Add(refreshButton);

            body.Controls.Add(healthCard, 0, 0);
            body.Controls.Add(modeCard, 1, 0);
            body.Controls.Add(accessCard, 0, 1);
            body.Controls.Add(controlCard, 1, 1);
            Controls.Add(body);
            Controls.Add(header);

            FormClosing += (_, eventArgs) =>
            {
                if (!closeForExit && eventArgs.CloseReason == CloseReason.UserClosing)
                {
                    eventArgs.Cancel = true;
                    HideRequested?.Invoke(this, EventArgs.Empty);
                }
            };
        }

        public void ApplyStatus(StatusSnapshot value)
        {
            snapshot = value;
            stateLabel.Text = value.DisplayState;
            stateLabel.ForeColor = value.ServerReachable ? Teal : Color.FromArgb(194, 65, 65);
            stateDetail.Text = value.ServerReachable
                ? "The LOKAL web server is accepting teacher and student connections."
                : (value.ServiceInstalled ? "The Windows service is installed but the server is not reachable." : "The Windows service is not installed. Development console mode is also offline.");
            modeLabel.Text = value.ModeLabel + (value.RestartRequired ? "  •  Restart required" : string.Empty);
            listenLabel.Text = "Listener: " + (string.IsNullOrWhiteSpace(value.ListenAddress) ? "not available" : value.ListenAddress);
            teacherLink.Text = "Teacher Dashboard\n" + value.TeacherUrl;
            studentLink.Text = "Student Join Page\n" + value.StudentUrl;
            teacherLink.Enabled = value.ServerReachable;
            studentLink.Enabled = value.ServerReachable;
            startButton.Enabled = value.ServiceInstalled && !value.ServerReachable;
            restartButton.Enabled = value.ServiceInstalled;
            stopButton.Enabled = value.ServiceInstalled && value.ServerReachable;
        }

        public void ShowError(string message)
        {
            stateLabel.Text = "Status unavailable";
            stateLabel.ForeColor = Color.FromArgb(194, 65, 65);
            stateDetail.Text = message;
        }

        public void CloseForExit()
        {
            closeForExit = true;
            Close();
        }

        private static Panel Card(string heading)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(8), BackColor = Color.White, Padding = new Padding(22) };
            panel.Paint += (_, eventArgs) =>
            {
                using (var pen = new Pen(Color.FromArgb(220, 230, 227)))
                    eventArgs.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
            };
            panel.Controls.Add(new Label { Text = heading, ForeColor = Muted, Font = new Font("Segoe UI Semibold", 9F), AutoSize = true, Location = new Point(22, 20) });
            return panel;
        }

        private static LinkLabel Link(string text, int top, EventHandler click)
        {
            var link = new LinkLabel { Text = text, Font = new Font("Segoe UI Semibold", 11F), LinkColor = Teal, ActiveLinkColor = Forest, AutoSize = false, Location = new Point(22, top), Size = new Size(310, 42) };
            link.LinkClicked += (sender, args) => click(sender, args);
            return link;
        }

        private static Button ActionButton(string text, Point location, bool primary, EventHandler click)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = new Size(text.Length > 7 ? 108 : 86, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Teal : Color.White,
                ForeColor = primary ? Color.White : Forest,
                Font = new Font("Segoe UI Semibold", 9.5F),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = primary ? Teal : Color.FromArgb(190, 205, 201);
            button.FlatAppearance.BorderSize = 1;
            button.Click += click;
            return button;
        }
    }
}
