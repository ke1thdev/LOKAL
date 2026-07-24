using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LOKAL.Tray
{
    internal sealed class TrayApplicationContext : ApplicationContext, IDisposable
    {
        private readonly NotifyIcon trayIcon;
        private readonly StatusForm statusForm;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private readonly CancellationTokenSource showListenerCancellation = new CancellationTokenSource();
        private readonly EventWaitHandle showEvent;
        private readonly ToolStripMenuItem serviceItem;
        private readonly ToolStripMenuItem modeItem;
        private readonly ToolStripMenuItem teacherItem;
        private readonly ToolStripMenuItem studentItem;
        private readonly ToolStripMenuItem copyStudentItem;
        private readonly ToolStripMenuItem startItem;
        private readonly ToolStripMenuItem restartItem;
        private readonly ToolStripMenuItem stopItem;
        private readonly ToolStripMenuItem startupItem;
        private StatusSnapshot currentSnapshot;
        private bool exiting;

        public TrayApplicationContext(EventWaitHandle showEvent)
        {
            this.showEvent = showEvent;
            statusForm = new StatusForm();
            statusForm.HideRequested += (_, __) => statusForm.Hide();
            statusForm.ServiceCommandRequested += ExecuteServiceCommand;
            statusForm.RefreshRequested += async (_, __) => await RefreshStatusAsync();
            statusForm.OpenTeacherRequested += (_, __) => ServerStatusClient.OpenUrl(currentSnapshot?.TeacherUrl);
            statusForm.OpenStudentRequested += (_, __) => ServerStatusClient.OpenUrl(currentSnapshot?.StudentUrl);
            _ = statusForm.Handle;

            serviceItem = HeaderItem("Server: Checking…");
            modeItem = HeaderItem("Mode: Checking…");
            teacherItem = MenuItem("Open Teacher Dashboard", (_, __) => ServerStatusClient.OpenUrl(currentSnapshot?.TeacherUrl));
            studentItem = MenuItem("Open Student Join Page", (_, __) => ServerStatusClient.OpenUrl(currentSnapshot?.StudentUrl));
            copyStudentItem = MenuItem("Copy Student Link", (_, __) => CopyStudentLink());
            startItem = MenuItem("Start Server", (_, __) => ExecuteServiceCommand("start"));
            restartItem = MenuItem("Restart Server", (_, __) => ExecuteServiceCommand("restart"));
            stopItem = MenuItem("Stop Server", (_, __) => ExecuteServiceCommand("stop"));
            startupItem = MenuItem("Start Status App with Windows", (_, __) => ToggleStartup());
            startupItem.Checked = ServerStatusClient.IsStartupEnabled();

            var menu = new ContextMenuStrip { ShowImageMargin = false, Font = new Font("Segoe UI", 10F) };
            menu.Items.Add(serviceItem);
            menu.Items.Add(modeItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("Show Server Status", (_, __) => ShowStatus()));
            menu.Items.Add(teacherItem);
            menu.Items.Add(studentItem);
            menu.Items.Add(copyStudentItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(startItem);
            menu.Items.Add(restartItem);
            menu.Items.Add(stopItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("Open Server Logs", (_, __) => ServerStatusClient.OpenLogs()));
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("Exit Status App", (_, __) => ExitApplication()));

            trayIcon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "LOKAL Server — checking status",
                Visible = true,
                ContextMenuStrip = menu
            };
            trayIcon.DoubleClick += (_, __) => ShowStatus();
            trayIcon.MouseClick += (_, eventArgs) => { if (eventArgs.Button == MouseButtons.Left) ShowStatus(); };

            refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            refreshTimer.Tick += async (_, __) => await RefreshStatusAsync();
            refreshTimer.Start();
            _ = ListenForShowRequestsAsync(showListenerCancellation.Token);
            _ = RefreshStatusAsync();
        }

        private static ToolStripMenuItem HeaderItem(string text) => new ToolStripMenuItem(text) { Enabled = false, Font = new Font("Segoe UI Semibold", 10F) };
        private static ToolStripMenuItem MenuItem(string text, EventHandler click)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += click;
            return item;
        }

        private async Task RefreshStatusAsync()
        {
            try
            {
                currentSnapshot = await ServerStatusClient.GetSnapshotAsync();
                serviceItem.Text = "Server: " + currentSnapshot.DisplayState;
                modeItem.Text = "Mode: " + currentSnapshot.ModeLabel + (currentSnapshot.RestartRequired ? " — restart required" : string.Empty);
                teacherItem.Enabled = currentSnapshot.ServerReachable;
                studentItem.Enabled = currentSnapshot.ServerReachable;
                copyStudentItem.Enabled = currentSnapshot.ServerReachable;
                startItem.Enabled = currentSnapshot.ServiceInstalled && !currentSnapshot.ServerReachable;
                restartItem.Enabled = currentSnapshot.ServiceInstalled;
                stopItem.Enabled = currentSnapshot.ServiceInstalled && currentSnapshot.ServerReachable;
                var tooltip = "LOKAL — " + currentSnapshot.DisplayState + " — " + currentSnapshot.ModeLabel;
                trayIcon.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
                statusForm.ApplyStatus(currentSnapshot);
            }
            catch (Exception exception)
            {
                serviceItem.Text = "Server: Status unavailable";
                statusForm.ShowError(exception.Message);
            }
        }

        private async Task ListenForShowRequestsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var signaled = await Task.Run(() => showEvent.WaitOne(1000), cancellationToken).ConfigureAwait(false);
                if (signaled && !cancellationToken.IsCancellationRequested)
                    statusForm.BeginInvoke(new Action(ShowStatus));
            }
        }

        private void ShowStatus()
        {
            if (statusForm.WindowState == FormWindowState.Minimized) statusForm.WindowState = FormWindowState.Normal;
            statusForm.Show();
            statusForm.Activate();
            statusForm.BringToFront();
            _ = RefreshStatusAsync();
        }

        private void CopyStudentLink()
        {
            if (!string.IsNullOrWhiteSpace(currentSnapshot?.StudentUrl))
            {
                Clipboard.SetText(currentSnapshot.StudentUrl);
                trayIcon.ShowBalloonTip(2200, "Student link copied", currentSnapshot.StudentUrl, ToolTipIcon.Info);
            }
        }

        private void ToggleStartup()
        {
            try
            {
                ServerStatusClient.SetStartupEnabled(!startupItem.Checked);
                startupItem.Checked = ServerStatusClient.IsStartupEnabled();
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "LOKAL Server Status", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExecuteServiceCommand(string command)
        {
            try
            {
                ServerStatusClient.RunServiceCommand(command);
                trayIcon.ShowBalloonTip(2000, "LOKAL Server", char.ToUpper(command[0]) + command.Substring(1) + " requested.", ToolTipIcon.Info);
                Task.Delay(1500).ContinueWith(_ => statusForm.BeginInvoke(new Action(async () => await RefreshStatusAsync())));
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "LOKAL Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static Icon LoadIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LOKAL.Tray.Resources.lokal-tray-icon.ico"))
            {
                if (stream == null) return SystemIcons.Application;
                using (var icon = new Icon(stream)) return (Icon)icon.Clone();
            }
        }

        private void ExitApplication()
        {
            exiting = true;
            refreshTimer.Stop();
            showListenerCancellation.Cancel();
            trayIcon.Visible = false;
            statusForm.CloseForExit();
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            if (!exiting) ExitApplication();
            else base.ExitThreadCore();
        }

        public new void Dispose()
        {
            refreshTimer?.Dispose();
            trayIcon?.Dispose();
            statusForm?.Dispose();
            showListenerCancellation.Dispose();
            base.Dispose();
        }
    }
}
