using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
namespace LOKAL.PowerPoint
{
    /// <summary>
    /// Manages live class sessions — auto-starting sessions with generated codes,
    /// starting/closing activities, and coordinating WebSocket connections.
    /// </summary>
    public class SessionManager
    {
        private readonly ThisAddIn _addIn;
        private Activity _currentActivity;
        private readonly object _participantSync = new object();
        private readonly HashSet<long> _knownParticipantIds = new HashSet<long>();
        private int _participantCount;

        internal Activity CurrentActivity => _currentActivity;
        internal int ParticipantCount
        {
            get { lock (_participantSync) return _participantCount; }
        }

        public SessionManager(ThisAddIn addIn)
        {
            _addIn = addIn;
        }

        /// <summary>
        /// Auto-starts a session by creating a temporary class with a random 5-digit code.
        /// Called automatically when the slideshow begins.
        /// </summary>
        public async Task<AutoSessionResponse> AutoStartSessionAsync()
        {
            _addIn.CurrentSessionId = null;
            try
            {
                var result = await _addIn.ApiClient.AutoStartSessionAsync();
                if (result == null)
                    return null;

                _addIn.CurrentClassCode = result.ClassCode;
                _addIn.CurrentClassId = result.ClassId;
                _addIn.CurrentSessionId = result.SessionId;
                
                var port = new Uri(Properties.Settings.Default.ServerUrl ?? "http://localhost:8080").Port;
                _addIn.CurrentJoinUrl = $"http://{GetLocalIPAddress()}:{port}/student";
                SetParticipants(null);
                PersistSelectedClass(result.ClassId, result.ClassCode);

                // Auth for the activity/session endpoints
                if (!string.IsNullOrEmpty(result.Token))
                    _addIn.ApiClient.SetToken(result.Token);

                // Connect WebSocket for real-time events.
                // WS failure must not kill the session — only live counters suffer.
                try
                {
                    var baseUrl = Properties.Settings.Default.ServerUrl ?? "http://localhost:8080";
                    // Listen for student responses (unsubscribe first —
                    // every slideshow begin passes through here)
                    _addIn.WsClient.OnStudentResponse -= OnStudentResponse;
                    _addIn.WsClient.OnStudentResponse += OnStudentResponse;
                    _addIn.WsClient.OnStudentJoin -= OnStudentJoin;
                    _addIn.WsClient.OnStudentJoin += OnStudentJoin;
                    await _addIn.WsClient.ConnectAsync(baseUrl,
                        "class:" + result.ClassCode, _addIn.ApiClient.GetToken());
                }
                catch (Exception wsEx)
                {
                    System.Diagnostics.Debug.WriteLine("WebSocket connect failed: " + wsEx.Message);
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Auto-start session failed: " + ex.Message);
                return null;
            }
        }

        public async Task<bool> StartSessionAsync(long classId)
        {
            _addIn.CurrentSessionId = null;
            try
            {
                var session = await _addIn.ApiClient.StartSessionAsync(classId);
                var cls = await _addIn.ApiClient.GetClassAsync(classId);
                if (session == null || cls == null)
                    throw new InvalidOperationException("The selected class could not be loaded.");

                var baseUrl = Properties.Settings.Default.ServerUrl ?? "http://localhost:8080";

                _addIn.CurrentSessionId = session.Id;
                _addIn.CurrentClassCode = cls.Code;
                _addIn.CurrentClassId = cls.Id;
                
                // Ensure JoinUrl is set for the ClassCodeBadge
                var port = new Uri(baseUrl).Port;
                _addIn.CurrentJoinUrl = $"http://{GetLocalIPAddress()}:{port}/student";
                SetParticipants(null);
                PersistSelectedClass(cls.Id, cls.Code);

                _addIn.WsClient.OnStudentResponse -= OnStudentResponse;
                _addIn.WsClient.OnStudentResponse += OnStudentResponse;
                _addIn.WsClient.OnStudentJoin -= OnStudentJoin;
                _addIn.WsClient.OnStudentJoin += OnStudentJoin;

                try
                {
                    await _addIn.WsClient.ConnectAsync(baseUrl,
                        "class:" + cls.Code, _addIn.ApiClient.GetToken());
                }
                catch (Exception wsEx)
                {
                    System.Diagnostics.Debug.WriteLine("WebSocket connect failed: " + wsEx.Message);
                }

                // Load the authoritative roster after subscribing, so a student
                // cannot be missed between the REST snapshot and live updates.
                try
                {
                    SetParticipants(await _addIn.ApiClient.GetParticipantsAsync(classId));
                }
                catch
                {
                    // Keep any members already observed through the live room.
                }
                return true;
            }
            catch (Exception ex)
            {
                if (ex.Message.ToLower().Contains("invalid token"))
                {
                    Properties.Settings.Default.AuthToken = "";
                    Properties.Settings.Default.Save();
                    _addIn.ApiClient.SetToken(null);
                    
                    System.Windows.Forms.MessageBox.Show(
                        "Your session has expired. Please sign in again from the ribbon.",
                        "LOKAL", System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show(
                        "Failed to start session: " + ex.Message,
                        "LOKAL", System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                }
                return false;
            }
        }

        public async Task StopSessionAsync(long sessionId, long classId)
        {
            try
            {
                await _addIn.ApiClient.StopSessionAsync(sessionId, classId);
                _addIn.CurrentSessionId = null;
                _addIn.CurrentClassCode = null;
                _addIn.WsClient.Disconnect();
                SetParticipants(null);
            }
            catch { }
        }

        public async Task<Activity> StartActivityAsync(string type,
            string questionText, string config, bool isQuizMode = false,
            int autoCloseSeconds = 0, bool showResponses = true)
        {
            if (!_addIn.CurrentSessionId.HasValue || !_addIn.CurrentClassId.HasValue)
                throw new InvalidOperationException("No active session");

            var req = new StartActivityRequest
            {
                SessionId = _addIn.CurrentSessionId.Value,
                ClassId = _addIn.CurrentClassId.Value,
                Type = type,
                QuestionText = questionText,
                Config = config,
                IsQuizMode = isQuizMode,
                AutoCloseSeconds = autoCloseSeconds
            };

            var activity = await _addIn.ApiClient.StartActivityAsync(req);
            _addIn.CurrentActivityId = activity.Id;
            _currentActivity = activity;
            _addIn.NotifyActivityResponseAvailability(false);

            // Fetch participants so the UI knows who is pending
            List<Participant> participants = new List<Participant>();
            try {
                participants = await _addIn.ApiClient.GetParticipantsAsync(_addIn.CurrentClassId.Value);
            } catch { }

            // Show the small presenter countdown first. When the response window
            // is enabled it is shown last so its z-order stays above the timer.
            _addIn.ShowActivityCountdown(activity);
            if (showResponses)
                _addIn.ShowCollectingResponses(activity, participants);
            else
                _addIn.HideCollectingResponses();

            return activity;
        }

        public async Task CloseActivityAsync(bool showResults = false)
        {
            if (!_addIn.CurrentActivityId.HasValue || !_addIn.CurrentClassId.HasValue)
                return;

            try
            {
                long activityId = _addIn.CurrentActivityId.Value;
                var responseWindow = _addIn.CollectingResponsesOverlay;
                bool canKeepResponseWindow = showResults && responseWindow != null &&
                    !responseWindow.IsDisposed && responseWindow.Visible;

                // Quiz mode is the only mode that automatically awards stars.
                // A normal multiple-choice poll may still have a correct answer
                // for review, but it must never change participant totals.
                if (_currentActivity != null)
                {
                    try
                    {
                        if (_currentActivity.IsQuizMode)
                        {
                            int diff = 1;
                            try {
                                var cfg = Newtonsoft.Json.Linq.JObject.Parse(_currentActivity.Config);
                                diff = cfg.Value<int>("difficulty");
                            } catch { }
                            diff = Math.Max(1, Math.Min(3, diff));
                            await _addIn.ApiClient.AwardStarsToCorrectAsync(activityId, diff);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Auto-reward failed: " + ex.Message);
                    }
                }

                await _addIn.ApiClient.CloseActivityAsync(
                    activityId,
                    _addIn.CurrentClassId.Value);

                // WebSocket delivery is best-effort. Reload the authoritative
                // response list so the result chart is complete even after a
                // brief connection drop.
                if (responseWindow != null && !responseWindow.IsDisposed)
                {
                    try
                    {
                        var persistedResponses = await _addIn.ApiClient.GetResponsesAsync(activityId);
                        if (persistedResponses != null)
                        {
                            foreach (var response in persistedResponses)
                                responseWindow.AddResponse(response);
                        }
                    }
                    catch { }
                }

                bool hasResponses = responseWindow != null && !responseWindow.IsDisposed &&
                    responseWindow.HasResponses;
                _addIn.MarkActivityButtonClosed(activityId, hasResponses);
                _addIn.CurrentActivityId = null;
                _currentActivity = null;
                _addIn.HideActivityCountdown();
                _addIn.NotifyActivityResponseAvailability(hasResponses);

                if (canKeepResponseWindow)
                    responseWindow.CompleteActivityAndShowResults();
                else
                    _addIn.HideCollectingResponses();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Failed to close activity: " + ex.Message,
                    "LOKAL", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        // ===== WebSocket Event Handlers =====

        private void OnStudentResponse(Response response)
        {
            // WS events arrive on a threadpool thread — marshal to UI
            _addIn.RunOnUi(() =>
            {
                _addIn.CollectingResponsesOverlay?.AddResponse(response);
                _addIn.NotifyActivityResponseAvailability(true);
            });
        }

        private void OnStudentJoin(Participant participant)
        {
            int count;
            lock (_participantSync)
            {
                if (participant != null && participant.Id > 0)
                    _knownParticipantIds.Add(participant.Id);
                _participantCount = _knownParticipantIds.Count;
                count = _participantCount;
            }
            _addIn.RunOnUi(() => 
            {
                _addIn.ClassCodeBadge?.SetParticipantCount(count);
                _addIn.CollectingResponsesOverlay?.UpdateParticipantCount(count);
            });
        }

        private void SetParticipants(IEnumerable<Participant> participants)
        {
            int count;
            lock (_participantSync)
            {
                _knownParticipantIds.Clear();
                if (participants != null)
                {
                    foreach (Participant participant in participants)
                    {
                        if (participant != null && participant.Id > 0)
                            _knownParticipantIds.Add(participant.Id);
                    }
                }
                _participantCount = _knownParticipantIds.Count;
                count = _participantCount;
            }

            _addIn.RunOnUi(() =>
            {
                _addIn.ClassCodeBadge?.SetParticipantCount(count);
                _addIn.CollectingResponsesOverlay?.UpdateParticipantCount(count);
            });
        }

        private static void PersistSelectedClass(long classId, string classCode)
        {
            Properties.Settings.Default.SelectedClassId = classId;
            Properties.Settings.Default.SelectedClassCode = classCode ?? string.Empty;
            Properties.Settings.Default.Save();
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "localhost";
        }
    }
}
