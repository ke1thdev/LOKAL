using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LOKAL.Tray
{
    internal static class Program
    {
        private const string MutexName = "Local\\LOKAL.Tray.SingleInstance";
        private const string ShowEventName = "Local\\LOKAL.Tray.ShowStatus";

        [STAThread]
        private static void Main(string[] args)
        {
            if (TryRunDiagnostic(args)) return;

            bool ownsMutex;
            using (var mutex = new Mutex(true, MutexName, out ownsMutex))
            {
                if (!ownsMutex)
                {
                    try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
                    catch (WaitHandleCannotBeOpenedException) { }
                    return;
                }

                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var context = new TrayApplicationContext(showEvent))
                    {
                        Application.Run(context);
                    }
                }
            }
        }

        private static bool TryRunDiagnostic(string[] args)
        {
            var index = Array.FindIndex(args, value => string.Equals(value, "--diagnose", StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) return true;

            var snapshot = ServerStatusClient.GetSnapshotAsync().GetAwaiter().GetResult();
            File.WriteAllText(args[index + 1], snapshot.ToDiagnosticJson());
            return true;
        }
    }
}
