using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace LOKAL.Tray
{
    [DataContract]
    internal sealed class ServerConfiguration
    {
        [DataMember(Name = "mode")] public string Mode { get; set; }
        [DataMember(Name = "port")] public int Port { get; set; }
        [DataMember(Name = "public_url")] public string PublicUrl { get; set; }
    }

    [DataContract]
    internal sealed class StatusEnvelope
    {
        [DataMember(Name = "success")] public bool Success { get; set; }
        [DataMember(Name = "data")] public ServerStatus Status { get; set; }
    }

    [DataContract]
    internal sealed class ServerStatus
    {
        [DataMember(Name = "mode")] public string Mode { get; set; }
        [DataMember(Name = "mode_label")] public string ModeLabel { get; set; }
        [DataMember(Name = "running")] public bool Running { get; set; }
        [DataMember(Name = "listen_address")] public string ListenAddress { get; set; }
        [DataMember(Name = "teacher_url")] public string TeacherUrl { get; set; }
        [DataMember(Name = "student_url")] public string StudentUrl { get; set; }
        [DataMember(Name = "public_url")] public string PublicUrl { get; set; }
        [DataMember(Name = "configuration_message")] public string ConfigurationMessage { get; set; }
        [DataMember(Name = "restart_required")] public bool RestartRequired { get; set; }
    }

    internal sealed class StatusSnapshot
    {
        public string ServiceState { get; set; }
        public bool ServiceInstalled { get; set; }
        public bool ServerReachable { get; set; }
        public string ModeLabel { get; set; }
        public string TeacherUrl { get; set; }
        public string StudentUrl { get; set; }
        public string ListenAddress { get; set; }
        public bool RestartRequired { get; set; }
        public string Error { get; set; }

        public string DisplayState => ServerReachable
            ? (ServiceInstalled ? "Running" : "Running (development)")
            : ServiceState;

        public string ToDiagnosticJson()
        {
            return "{" +
                "\"serviceInstalled\":" + ServiceInstalled.ToString().ToLowerInvariant() + "," +
                "\"serviceState\":\"" + Escape(ServiceState) + "\"," +
                "\"serverReachable\":" + ServerReachable.ToString().ToLowerInvariant() + "," +
                "\"modeLabel\":\"" + Escape(ModeLabel) + "\"," +
                "\"teacherUrl\":\"" + Escape(TeacherUrl) + "\"," +
                "\"studentUrl\":\"" + Escape(StudentUrl) + "\"," +
                "\"configPath\":\"" + Escape(ServerStatusClient.ConfigPath) + "\"," +
                "\"logPath\":\"" + Escape(ServerStatusClient.LogPath) + "\"}";
        }

        private static string Escape(string value) => (value ?? string.Empty)
            .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    internal static class ServerStatusClient
    {
        private const string ServiceName = "LOKALServer";
        private const string StartupValueName = "LOKAL Server Status";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        public static readonly string ProgramDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LOKAL");
        public static readonly string ConfigPath = Path.Combine(ProgramDataRoot, "config", "server.json");
        public static readonly string LogPath = Path.Combine(ProgramDataRoot, "logs", "lokal.log");

        public static async Task<StatusSnapshot> GetSnapshotAsync()
        {
            var snapshot = ReadServiceState();
            var configuration = ReadConfiguration();
            var port = configuration?.Port > 0 ? configuration.Port : 8080;
            snapshot.ModeLabel = FriendlyMode(configuration?.Mode);
            snapshot.TeacherUrl = $"http://127.0.0.1:{port}/teacher/#/server";
            snapshot.StudentUrl = configuration?.Mode == "online" && !string.IsNullOrWhiteSpace(configuration.PublicUrl)
                ? configuration.PublicUrl.TrimEnd('/') + "/student/"
                : $"http://127.0.0.1:{port}/student/";

            try
            {
                var json = await Http.GetStringAsync($"http://127.0.0.1:{port}/api/v1/server/status").ConfigureAwait(false);
                var envelope = Deserialize<StatusEnvelope>(json);
                if (envelope?.Success == true && envelope.Status != null)
                {
                    snapshot.ServerReachable = envelope.Status.Running;
                    snapshot.ModeLabel = envelope.Status.ModeLabel;
                    snapshot.TeacherUrl = $"http://127.0.0.1:{port}/teacher/#/server";
                    snapshot.StudentUrl = envelope.Status.StudentUrl;
                    snapshot.ListenAddress = envelope.Status.ListenAddress;
                    snapshot.RestartRequired = envelope.Status.RestartRequired;
                    snapshot.Error = null;
                }
            }
            catch (Exception exception)
            {
                snapshot.Error = exception.Message;
            }
            return snapshot;
        }

        public static void RunServiceCommand(string command)
        {
            var executable = FindServerExecutable();
            if (executable == null)
                throw new FileNotFoundException("lokal.exe was not found beside the tray application or in the LOKAL installation folder.");
            Process.Start(new ProcessStartInfo(executable, "service " + command)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(executable)
            });
        }

        public static void OpenUrl(string url)
        {
            if (!string.IsNullOrWhiteSpace(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        public static void OpenLogs()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            if (File.Exists(LogPath))
                Process.Start("explorer.exe", "/select,\"" + LogPath + "\"");
            else
                Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(LogPath) + "\"");
        }

        public static bool IsStartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                return key?.GetValue(StartupValueName) != null;
        }

        public static void SetStartupEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled) key.SetValue(StartupValueName, "\"" + Process.GetCurrentProcess().MainModule.FileName + "\" --background");
                else key.DeleteValue(StartupValueName, false);
            }
        }

        private static StatusSnapshot ReadServiceState()
        {
            try
            {
                using (var service = new ServiceController(ServiceName))
                {
                    var state = service.Status.ToString();
                    return new StatusSnapshot { ServiceInstalled = true, ServiceState = state };
                }
            }
            catch (InvalidOperationException)
            {
                return new StatusSnapshot { ServiceInstalled = false, ServiceState = "Not installed" };
            }
        }

        private static ServerConfiguration ReadConfiguration()
        {
            try { return File.Exists(ConfigPath) ? Deserialize<ServerConfiguration>(File.ReadAllText(ConfigPath)) : null; }
            catch { return null; }
        }

        private static T Deserialize<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        private static string FriendlyMode(string mode)
        {
            if (string.Equals(mode, "offline", StringComparison.OrdinalIgnoreCase)) return "Offline";
            if (string.Equals(mode, "online", StringComparison.OrdinalIgnoreCase)) return "Online";
            return "Local Network";
        }

        private static string FindServerExecutable()
        {
            var current = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(current, "lokal.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LOKAL", "lokal.exe"),
                ReadInstalledServiceExecutable(),
                Path.GetFullPath(Path.Combine(current, "..", "..", "..", "lokal.exe"))
            };
            foreach (var candidate in candidates) if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return candidate;
            return null;
        }

        private static string ReadInstalledServiceExecutable()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\LOKALServer"))
                {
                    var imagePath = (key?.GetValue("ImagePath") as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(imagePath)) return null;
                    if (imagePath.StartsWith("\"", StringComparison.Ordinal))
                    {
                        var closingQuote = imagePath.IndexOf('"', 1);
                        return closingQuote > 1 ? imagePath.Substring(1, closingQuote - 1) : null;
                    }
                    var serviceArgument = imagePath.IndexOf(" service ", StringComparison.OrdinalIgnoreCase);
                    return serviceArgument > 0 ? imagePath.Substring(0, serviceArgument).Trim() : imagePath;
                }
            }
            catch { return null; }
        }
    }
}
