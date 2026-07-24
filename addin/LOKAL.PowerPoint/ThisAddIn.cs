using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Office.Tools;
using Newtonsoft.Json.Linq;
using PPT = Microsoft.Office.Interop.PowerPoint;
using Office = Microsoft.Office.Core;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// LOKAL PowerPoint Add-In — Main entry point.
    /// Auto-starts a session with a generated join code when slideshow begins.
    /// Uses floating overlay forms during slideshow (not CustomTaskPanes).
    /// </summary>
    public partial class ThisAddIn
    {
        // === Core services ===
        internal LokalApiClient ApiClient { get; private set; }
        internal WebSocketClient WsClient { get; private set; }
        internal SessionManager SessionManager { get; private set; }

        // === Ribbon reference ===
        internal Ribbon.LokalRibbon Ribbon { get; set; }

        // === Slideshow overlay forms ===
        internal SlideshowToolbarForm ToolbarOverlay { get; private set; }
        internal ClassCodeBadgeForm ClassCodeBadge { get; private set; }
        internal CollectingResponsesForm CollectingResponsesOverlay { get; private set; }
        internal ActivityCountdownOverlayForm ActivityCountdownOverlay { get; private set; }
        internal ActivityLaunchOverlayForm ActivityLaunchOverlay { get; private set; }
        internal MyClassForm MyClassOverlay { get; private set; }

        // === Edit-mode task pane ===
        internal CustomTaskPane AddActivityPane { get; private set; }

        // === State ===
        internal string CurrentClassCode { get; set; }
        internal long? CurrentSessionId { get; set; }
        internal long? CurrentClassId { get; set; }
        internal long? CurrentActivityId { get; set; }
        internal string CurrentJoinUrl { get; set; }
        internal PPT.Shape CurrentActivityShape { get; set; }

        // UI-thread marshaling: VSTO COM events resume on threadpool threads
        // after an await, and WinForms shown there never get a message pump.
        private System.Windows.Forms.Control _uiInvoker;
        private readonly System.Collections.Generic.HashSet<int> _startedSlideIds
            = new System.Collections.Generic.HashSet<int>();
        private bool _sessionReady;

        /// <summary>
        /// Writes add-in diagnostics beside the LOKAL server log. Runtime data
        /// must never be written into the source or installation directory,
        /// which may be read-only after LOKAL is installed.
        /// </summary>
        private static void AppendDiagnosticLog(string message)
        {
            try
            {
                string programData = Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData);
                string logDirectory = System.IO.Path.Combine(programData, "LOKAL", "logs");
                System.IO.Directory.CreateDirectory(logDirectory);
                string logPath = System.IO.Path.Combine(logDirectory, "slide-error.log");
                string entry = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                    + " " + message + Environment.NewLine;
                System.IO.File.AppendAllText(logPath, entry);
            }
            catch (Exception logException)
            {
                System.Diagnostics.Debug.WriteLine(
                    "Unable to write LOKAL diagnostic log: " + logException.Message);
            }
        }

        internal void RunOnUi(Action action)
        {
            try
            {
                if (_uiInvoker != null && _uiInvoker.IsHandleCreated && _uiInvoker.InvokeRequired)
                    _uiInvoker.BeginInvoke(action);
                else
                    action();
            }
            catch { }
        }

        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            LokalUi.EnableGlobalFormBranding();
            // Capture the main UI thread for later marshaling
            _uiInvoker = new System.Windows.Forms.Control();
            var _ = _uiInvoker.Handle; // force handle creation on this thread

            // Initialize API client
            var baseUrl = Properties.Settings.Default.ServerUrl;
            if (string.IsNullOrEmpty(baseUrl))
                baseUrl = "http://localhost:8080";

            ApiClient = new LokalApiClient(baseUrl);
            
            string savedToken = Properties.Settings.Default.AuthToken;
            if (!string.IsNullOrEmpty(savedToken))
            {
                ApiClient.SetToken(savedToken);
                // Optionally fire off a background task to refresh the profile if needed
            }
            
            WsClient = new WebSocketClient();
            SessionManager = new SessionManager(this);

            // Hook into SlideShow events
            Application.SlideShowBegin += OnSlideShowBegin;
            Application.SlideShowEnd += OnSlideShowEnd;
            Application.SlideShowNextSlide += OnSlideShowNextSlide;
            Application.WindowSelectionChange += OnWindowSelectionChange;
            Application.PresentationBeforeSave += OnPresentationBeforeSave;
        }

        private void ThisAddIn_Shutdown(object sender, EventArgs e)
        {
            FlushActivityOptions();
            Application.PresentationBeforeSave -= OnPresentationBeforeSave;
            WsClient?.Disconnect();
            CloseAllOverlays();
        }

        private void OnPresentationBeforeSave(PPT.Presentation presentation, ref bool cancel)
        {
            if (cancel) return;
            FlushActivityOptions();
        }

        private void FlushActivityOptions()
        {
            try
            {
                var panel = ConfigActivityPane?.Control as ConfigActivityPanel;
                panel?.SaveConfigToShape();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not flush activity options before save: " + ex.Message);
            }
        }

        /// <summary>
        /// Writes the per-slide activity configuration to the activity shape and a
        /// slide-level recovery tag.  Shape tags are the primary source; the slide
        /// tag protects against a damaged/recreated group shape during editing.
        /// </summary>
        internal void PersistActivityConfig(PPT.Shape shape, string json)
        {
            if (shape == null || string.IsNullOrWhiteSpace(json)) return;

            string existing = "";
            try { existing = shape.Tags["LOKAL_CONFIG"]; } catch { }
            bool changed = !string.Equals(existing, json, StringComparison.Ordinal);

            if (changed)
                shape.Tags.Add("LOKAL_CONFIG", json);

            PPT.Slide slide = ResolveSlideForShape(shape);
            if (slide != null)
            {
                string recoveryTag = GetActivityRecoveryTag(shape);
                string slideExisting = "";
                try { slideExisting = slide.Tags[recoveryTag]; } catch { }
                if (!string.Equals(slideExisting, json, StringComparison.Ordinal))
                {
                    slide.Tags.Add(recoveryTag, json);
                    changed = true;
                }
            }

            // COM tag changes are not guaranteed to flip PowerPoint's Saved flag.
            // Explicitly mark the deck dirty so Ctrl+S and the close prompt include
            // the updated activity options.
            if (changed)
            {
                try
                {
                    var presentation = Application.ActivePresentation;
                    if (presentation != null)
                        presentation.Saved = Office.MsoTriState.msoFalse;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Could not mark presentation dirty: " + ex.Message);
                }
            }
        }

        internal string ReadActivityConfig(PPT.Shape shape)
        {
            if (shape == null) return null;

            string json = "";
            try { json = shape.Tags["LOKAL_CONFIG"]; } catch { }
            if (!string.IsNullOrWhiteSpace(json)) return json;

            PPT.Slide slide = ResolveSlideForShape(shape);
            if (slide == null) return null;

            try { json = slide.Tags[GetActivityRecoveryTag(shape)]; } catch { }
            if (!string.IsNullOrWhiteSpace(json))
            {
                // Repair the primary copy without routing through the panel state.
                try { shape.Tags.Add("LOKAL_CONFIG", json); } catch { }
            }
            return json;
        }

        private PPT.Slide ResolveSlideForShape(PPT.Shape shape)
        {
            try
            {
                var parentSlide = shape.Parent as PPT.Slide;
                if (parentSlide != null) return parentSlide;
            }
            catch { }

            try
            {
                return Application.ActiveWindow?.View?.Slide;
            }
            catch
            {
                return null;
            }
        }

        private static string GetActivityRecoveryTag(PPT.Shape shape)
        {
            int id = 0;
            try { id = shape.Id; } catch { }
            return "LOKAL_CONFIG_" + id;
        }

        // ===== SLIDESHOW EVENTS =====

        private async void OnSlideShowBegin(PPT.SlideShowWindow wn)
        {
            _startedSlideIds.Clear();
            _sessionReady = false;

            // Show toolbar immediately — still on the main thread, before any await
            ShowToolbarOverlay();

            long selectedClassId = Properties.Settings.Default.SelectedClassId;

            if (selectedClassId != 0)
            {
                await SessionManager.StartSessionAsync(selectedClassId);
            }
            else
            {
                await SessionManager.AutoStartSessionAsync();
            }

            if (CurrentSessionId.HasValue)
            {
                _sessionReady = true;
                RunOnUi(() =>
                {
                    ShowClassCodeBadge(CurrentClassCode);
                    ToolbarOverlay?.PositionOnSlideshow();
                });
                
                if (wn != null && wn.View != null && wn.View.Slide != null)
                {
                    _ = SyncCurrentSlideAsync(wn.View.Slide);
                }
                
                TryAutoStartActivityForCurrentSlide();
            }
        }

        private void OnSlideShowEnd(PPT.Presentation pres)
        {
            _sessionReady = false;
            _startedSlideIds.Clear();

            // Close all overlay forms
            CloseAllOverlays();

            // Stop the session - commented out to support persistent sessions across presentations
            // if (CurrentSessionId.HasValue && CurrentClassId.HasValue)
            // {
            //     _ = SessionManager.StopSessionAsync(CurrentSessionId.Value, CurrentClassId.Value);
            // }
        }

        private void OnSlideShowNextSlide(PPT.SlideShowWindow wn)
        {
            HideActivityLaunchOverlay();
            if (CurrentActivityId.HasValue)
            {
                _ = SessionManager.CloseActivityAsync();
            }
            
            _ = SyncCurrentSlideAsync(wn.View.Slide);
            TryAutoStartActivityForCurrentSlide();
        }

        private async Task SyncCurrentSlideAsync(PPT.Slide slide)
        {
            try
            {
                if (!_sessionReady || !CurrentClassId.HasValue) return;

                string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lokal_class_slide.png");
                slide.Export(tempPath, "PNG", 1920, 1080);
                byte[] imgBytes = System.IO.File.ReadAllBytes(tempPath);
                string base64Image = Convert.ToBase64String(imgBytes);

                await ApiClient.UploadClassSlideAsync(CurrentClassId.Value, base64Image);
            }
            catch { }
        }

        /// <summary>
        /// If the current slideshow slide has a LOKAL activity shape, start its
        /// activity using the config persisted in the shape's tags.
        /// manual=true (toolbar button) bypasses the start_with_slide option and,
        /// if the activity is already running, just re-shows the responses window.
        /// </summary>
        internal void TryAutoStartActivityForCurrentSlide(bool manual = false)
        {
            try
            {
                if (!_sessionReady) return;

                var ssw = Application.SlideShowWindows;
                if (ssw.Count == 0) return;
                var slide = ssw[1].View.Slide;

                PPT.Shape activityShape = null;
                foreach (PPT.Shape s in slide.Shapes)
                {
                    if (s.Tags["LOKAL_ACTIVITY"] != "")
                    {
                        activityShape = s;
                        break;
                    }
                }
                if (activityShape == null)
                {
                    HideActivityLaunchOverlay();
                    return;
                }

                if (_startedSlideIds.Contains(slide.SlideID))
                {
                    if (manual)
                    {
                        if (CurrentActivityId.HasValue &&
                            CollectingResponsesOverlay != null && !CollectingResponsesOverlay.IsDisposed)
                        {
                            RunOnUi(() => CollectingResponsesOverlay.Show());
                            return;
                        }
                        else
                        {
                            // Allow starting a new activity if the previous one was closed
                            _startedSlideIds.Remove(slide.SlideID);
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                string type = activityShape.Tags["LOKAL_ACTIVITY"];
                string configJson = activityShape.Tags["LOKAL_CONFIG"];

                bool startWithSlide = true;
                bool quizMode = false;
                bool minimizeAfterStart = false;
                int autoClose = 0;
                if (!string.IsNullOrEmpty(configJson))
                {
                    try
                    {
                        var cfg = Newtonsoft.Json.Linq.JObject.Parse(configJson);
                        startWithSlide = cfg.Value<bool?>("start_with_slide") ?? true;
                        quizMode = cfg.Value<bool?>("quiz_mode") ?? false;
                        minimizeAfterStart = cfg.Value<bool?>("minimize_after_start") ?? false;
                        bool autoCloseEnabled = cfg.Value<bool?>("auto_close_enabled") ?? false;
                        autoClose = autoCloseEnabled ? (cfg.Value<int?>("auto_close_seconds") ?? 0) : 0;
                    }
                    catch { }
                }
                else
                {
                    configJson = "{\"num_choices\":4,\"allow_multiple\":false,\"correct_answer\":\"\",\"quiz_mode\":false,\"difficulty\":1,\"start_with_slide\":true,\"minimize_after_start\":false,\"auto_close_enabled\":false,\"auto_close_seconds\":0}";
                }

                if (!manual && !startWithSlide)
                {
                    ShowActivityLaunchOverlay(activityShape, type, configJson);
                    return;
                }

                HideActivityLaunchOverlay();
                _startedSlideIds.Add(slide.SlideID);
                string question = GetSlideQuestionText(slide);

                // Start activity, then snapshot slide after a delay to avoid hanging PowerPoint
                StartActivitySafe(type, question, configJson, quizMode, autoClose, minimizeAfterStart, slide, activityShape);
            }
            catch { }
        }

        /// <summary>Exports the slide to a temp PNG and returns the path ("" on failure).</summary>
        internal string ExportSlidePng(PPT.Slide slide)
        {
            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), $"lokal_slide_{slide.SlideID}.png");
                slide.Export(path, "PNG", 1280, 720);
                return path;
            }
            catch (Exception ex)
            {
                AppendDiagnosticLog("Export error: " + ex.Message);
                System.Diagnostics.Debug.WriteLine("ExportSlidePng failed: " + ex.Message);
                return "";
            }
        }

        internal Task<string> ExportSlidePngAsync(PPT.Slide slide)
        {
            var tcs = new TaskCompletionSource<string>();
            RunOnUi(() =>
            {
                try
                {
                    string path = ExportSlidePng(slide);
                    tcs.SetResult(path);
                }
                catch (Exception)
                {
                    tcs.SetResult("");
                }
            });
            return tcs.Task;
        }

        private async void StartActivitySafe(string type, string question,
            string configJson, bool quizMode, int autoClose, bool minimizeAfterStart,
            PPT.Slide slide, PPT.Shape activityShape)
        {
            try
            {
                var activity = await SessionManager.StartActivityAsync(type, question, configJson,
                    quizMode, autoClose, !minimizeAfterStart);

                if (activity != null && activityShape != null)
                {
                    RunOnUi(() =>
                    {
                        try
                        {
                            activityShape.Tags.Add("LOKAL_LAST_ACTIVITY_ID", activity.Id.ToString());
                            activityShape.Tags.Add("LOKAL_LAST_ACTIVITY_QUESTION", question ?? string.Empty);
                            SetActivityButtonState(activityShape, "active");
                        }
                        catch { }
                    });
                }

                string slidePngPath = "";
                
                // Retry loop to handle PowerPoint COM exceptions during transitions
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(1000); // Wait for transition or retry delay
                    slidePngPath = await ExportSlidePngAsync(slide);
                    if (!string.IsNullOrEmpty(slidePngPath))
                    {
                        break;
                    }
                }

                // Ship the slide snapshot so students see the real slide
                if (activity != null && !string.IsNullOrEmpty(slidePngPath) && System.IO.File.Exists(slidePngPath))
                {
                    try
                    {
                        var bytes = System.IO.File.ReadAllBytes(slidePngPath);
                        await ApiClient.UploadActivitySlideAsync(activity.Id, Convert.ToBase64String(bytes));
                    }
                    catch (Exception upEx)
                    {
                        AppendDiagnosticLog("Upload error: " + upEx.Message);
                        System.Diagnostics.Debug.WriteLine("Slide upload failed: " + upEx.Message);
                    }
                }
                else
                {
                    AppendDiagnosticLog(
                        "Skipped upload. activity!=null? " + (activity != null)
                        + ", path=" + slidePngPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("StartActivity failed: " + ex.Message);
            }
        }

        // ===== OVERLAY FORM MANAGEMENT =====

        private void ShowToolbarOverlay()
        {
            RunOnUi(() =>
            {
                try
                {
                    if (ToolbarOverlay == null || ToolbarOverlay.IsDisposed)
                    {
                        ToolbarOverlay = new SlideshowToolbarForm(this);
                    }
                    ToolbarOverlay.PositionOnSlideshow();
                    ToolbarOverlay.Show();
                }
                catch { }
            });
        }

        private void ShowClassCodeBadge(string code)
        {
            RunOnUi(() =>
            {
                try
                {
                    if (ClassCodeBadge == null || ClassCodeBadge.IsDisposed)
                    {
                        ClassCodeBadge = new ClassCodeBadgeForm(this);
                    }
                    ClassCodeBadge.SetCode(code);
                    ClassCodeBadge.SetParticipantCount(SessionManager?.ParticipantCount ?? 0);
                    ClassCodeBadge.PositionOnSlideshow();
                    ClassCodeBadge.Show();
                }
                catch { }
            });
        }

        internal void ShowCollectingResponses(Activity activity, System.Collections.Generic.List<Participant> participants = null)
        {
            RunOnUi(() =>
            {
                try
                {
                    if (CollectingResponsesOverlay == null || CollectingResponsesOverlay.IsDisposed)
                    {
                        CollectingResponsesOverlay = new CollectingResponsesForm(this);
                    }
                    CollectingResponsesOverlay.SetActivity(activity, CurrentClassCode ?? "-----", CurrentJoinUrl, participants);
                    CollectingResponsesOverlay.Show();
                    KeepCollectingResponsesAboveCountdown();
                    // Showing two independent TopMost forms can otherwise leave
                    // the small countdown above the activity window. Repeat the
                    // ordering after the current UI message has completed so the
                    // WinForms/native window activation sequence cannot undo it.
                    CollectingResponsesOverlay.BeginInvoke((Action)(() =>
                    {
                        KeepCollectingResponsesAboveCountdown();
                    }));
                }
                catch { }
            });
        }

        internal void KeepCollectingResponsesAboveCountdown()
        {
            try
            {
                if (ActivityCountdownOverlay != null &&
                    !ActivityCountdownOverlay.IsDisposed &&
                    ActivityCountdownOverlay.Visible)
                {
                    ActivityCountdownOverlay.TopMost = false;
                    ActivityCountdownOverlay.SendToBack();
                }

                if (CollectingResponsesOverlay != null &&
                    !CollectingResponsesOverlay.IsDisposed &&
                    CollectingResponsesOverlay.Visible &&
                    CollectingResponsesOverlay.WindowState !=
                        System.Windows.Forms.FormWindowState.Minimized)
                {
                    CollectingResponsesOverlay.TopMost = true;
                    CollectingResponsesOverlay.BringToFront();
                    CollectingResponsesOverlay.Activate();
                }
            }
            catch { }
        }

        internal void RestoreActivityCountdownZOrder()
        {
            try
            {
                if (ActivityCountdownOverlay != null &&
                    !ActivityCountdownOverlay.IsDisposed &&
                    ActivityCountdownOverlay.Visible)
                {
                    ActivityCountdownOverlay.TopMost = true;
                    ActivityCountdownOverlay.BringToFront();
                }
            }
            catch { }
        }

        private void ShowActivityLaunchOverlay(PPT.Shape shape, string type, string configJson)
        {
            RunOnUi(() =>
            {
                try
                {
                    if (ActivityLaunchOverlay == null || ActivityLaunchOverlay.IsDisposed)
                        ActivityLaunchOverlay = new ActivityLaunchOverlayForm(this);
                    ActivityLaunchOverlay.SetActivity(shape, type, configJson);
                    ActivityLaunchOverlay.Show();
                    ActivityLaunchOverlay.BringToFront();
                }
                catch { }
            });
        }

        internal void HideActivityLaunchOverlay()
        {
            RunOnUi(() =>
            {
                try
                {
                    if (ActivityLaunchOverlay != null && !ActivityLaunchOverlay.IsDisposed)
                        ActivityLaunchOverlay.Close();
                }
                catch { }
                ActivityLaunchOverlay = null;
            });
        }

        internal void NotifyActivityResponseAvailability(bool available)
        {
            RunOnUi(() =>
            {
                try
                {
                    (ConfigActivityPane?.Control as ConfigActivityPanel)
                        ?.SetResponseAvailability(available);
                }
                catch { }
            });
        }

        /// <summary>
        /// Opens the response window only when the running activity already has
        /// responses. A completed activity id is persisted on its question shape,
        /// so results can also be reviewed later from edit mode. View Responses
        /// therefore never starts a new activity.
        /// </summary>
        internal async Task<bool> ShowCurrentResponsesAsync()
        {
            long? responseActivityId = GetResponseActivityIdForSelectedShape();
            if (!responseActivityId.HasValue)
            {
                NotifyActivityResponseAvailability(false);
                return false;
            }

            var responses = await ApiClient.GetResponsesAsync(responseActivityId.Value);
            if (responses == null || responses.Count == 0)
            {
                NotifyActivityResponseAvailability(false);
                return false;
            }

            System.Collections.Generic.List<Participant> participants = null;
            try
            {
                if (CurrentClassId.HasValue)
                    participants = await ApiClient.GetParticipantsAsync(CurrentClassId.Value);
            }
            catch { }

            Activity activity = SessionManager?.CurrentActivity;
            if (activity == null || activity.Id != responseActivityId.Value)
                activity = BuildStoredActivity(responseActivityId.Value);

            RunOnUi(() =>
            {
                try
                {
                    ShowCollectingResponses(activity, participants);
                    foreach (var response in responses)
                        CollectingResponsesOverlay?.AddResponse(response);
                    CollectingResponsesOverlay?.ShowStoredResponses();
                }
                catch { }
            });
            NotifyActivityResponseAvailability(true);
            return true;
        }

        internal long? GetResponseActivityIdForSelectedShape()
        {
            try
            {
                string value = CurrentActivityShape?.Tags["LOKAL_LAST_ACTIVITY_ID"];
                if (long.TryParse(value, out long activityId) && activityId > 0)
                    return activityId;
            }
            catch { }
            if (CurrentActivityId.HasValue) return CurrentActivityId.Value;
            return null;
        }

        internal void MarkActivityButtonClosed(long activityId, bool hasResponses)
        {
            RunOnUi(() =>
            {
                try
                {
                    foreach (PPT.Slide slide in Application.ActivePresentation.Slides)
                    {
                        foreach (PPT.Shape shape in slide.Shapes)
                        {
                            if (shape.Tags["LOKAL_LAST_ACTIVITY_ID"] == activityId.ToString())
                                SetActivityButtonState(shape, hasResponses ? "results" : "ready");
                        }
                    }
                }
                catch { }
            });
        }

        internal void ResetActivityResponseStates()
        {
            RunOnUi(() =>
            {
                try
                {
                    foreach (PPT.Slide slide in Application.ActivePresentation.Slides)
                    {
                        foreach (PPT.Shape shape in slide.Shapes)
                        {
                            if (string.IsNullOrEmpty(shape.Tags["LOKAL_ACTIVITY"])) continue;
                            try { shape.Tags.Delete("LOKAL_LAST_ACTIVITY_ID"); } catch { }
                            try { shape.Tags.Delete("LOKAL_LAST_ACTIVITY_QUESTION"); } catch { }
                            SetActivityButtonState(shape, "ready");
                        }
                    }
                }
                catch { }

                CurrentActivityId = null;
                NotifyActivityResponseAvailability(false);
                HideActivityCountdown();
                HideCollectingResponses();
            });
        }

        private static void SetActivityButtonState(PPT.Shape activityShape, string state)
        {
            if (activityShape == null) return;

            System.Drawing.Color color;
            switch (state)
            {
                case "active":
                    color = System.Drawing.Color.FromArgb(255, 183, 3);
                    break;
                case "results":
                    color = System.Drawing.Color.FromArgb(22, 163, 74);
                    break;
                default:
                    color = LokalUi.Primary;
                    break;
            }

            try
            {
                PPT.Shape visualShape = activityShape;
                if (activityShape.Type == Office.MsoShapeType.msoGroup &&
                    activityShape.GroupItems.Count > 0)
                {
                    visualShape = activityShape.GroupItems[1];
                }

                visualShape.Fill.Visible = Office.MsoTriState.msoTrue;
                visualShape.Fill.Solid();
                visualShape.Fill.ForeColor.RGB =
                    System.Drawing.ColorTranslator.ToOle(color);
            }
            catch { }
        }

        internal void InsertMultipleChoiceResultsSlide(Activity activity, IEnumerable<Response> responseSource)
        {
            RunOnUi(() =>
            {
                try
                {
                    if (Application.ActivePresentation == null || activity == null) return;

                    var responses = (responseSource ?? Enumerable.Empty<Response>()).ToList();
                    var config = string.IsNullOrWhiteSpace(activity.Config)
                        ? new JObject()
                        : JObject.Parse(activity.Config);
                    int optionCount = Math.Max(2, Math.Min(8,
                        config.Value<int?>("num_choices") ??
                        (config["options"] as JArray)?.Count ?? 4));
                    var optionLabels = new List<string>();
                    var configuredOptions = config["options"] as JArray;
                    for (int i = 0; i < optionCount; i++)
                    {
                        string label = configuredOptions != null && i < configuredOptions.Count
                            ? configuredOptions[i]?.ToString()
                            : null;
                        optionLabels.Add(string.IsNullOrWhiteSpace(label)
                            ? ((char)('A' + i)).ToString()
                            : label);
                    }

                    var correctAnswers = ParseCorrectAnswerIndexes(config["correct_answer"]);
                    var counts = Enumerable.Range(0, optionCount)
                        .Select(i => responses.Count(r => ResponseAnswerIncludesOption(r, i)))
                        .ToArray();

                    PPT.Presentation presentation = Application.ActivePresentation;
                    PPT.Slide slide = presentation.Slides.Add(
                        presentation.Slides.Count + 1, PPT.PpSlideLayout.ppLayoutBlank);
                    float slideWidth = presentation.PageSetup.SlideWidth;
                    float slideHeight = presentation.PageSetup.SlideHeight;

                    slide.FollowMasterBackground = Office.MsoTriState.msoFalse;
                    slide.Background.Fill.Solid();
                    slide.Background.Fill.ForeColor.RGB =
                        System.Drawing.ColorTranslator.ToOle(
                            System.Drawing.Color.FromArgb(244, 244, 255));

                    AddResultsText(slide, "Multiple Choice Results",
                        36, 22, slideWidth - 72, 44, 24, true,
                        System.Drawing.Color.FromArgb(31, 41, 55));
                    AddResultsText(slide,
                        string.IsNullOrWhiteSpace(activity.QuestionText)
                            ? "Question results"
                            : activity.QuestionText,
                        36, 70, slideWidth - 72, 48, 17, false,
                        System.Drawing.Color.FromArgb(71, 85, 105));

                    float chartLeft = 55;
                    float chartTop = 150;
                    float chartWidth = slideWidth - 110;
                    float chartHeight = Math.Max(180, slideHeight - 245);
                    float gap = Math.Max(8, Math.Min(18, chartWidth / 60));
                    float barWidth = (chartWidth - gap * (optionCount - 1)) / optionCount;
                    int maxVotes = Math.Max(1, counts.Length == 0 ? 1 : counts.Max());
                    var barColors = new[]
                    {
                        System.Drawing.Color.FromArgb(16, 185, 129),
                        System.Drawing.Color.FromArgb(244, 63, 94),
                        System.Drawing.Color.FromArgb(56, 189, 248),
                        System.Drawing.Color.FromArgb(249, 115, 22),
                        LokalUi.Primary,
                        System.Drawing.Color.FromArgb(168, 85, 247),
                        System.Drawing.Color.FromArgb(234, 179, 8),
                        System.Drawing.Color.FromArgb(20, 184, 166)
                    };

                    for (int i = 0; i < optionCount; i++)
                    {
                        float height = Math.Max(12, chartHeight * counts[i] / maxVotes);
                        float left = chartLeft + i * (barWidth + gap);
                        float top = chartTop + chartHeight - height;
                        var bar = slide.Shapes.AddShape(
                            Office.MsoAutoShapeType.msoShapeRoundedRectangle,
                            left, top, barWidth, height);
                        bar.Fill.Solid();
                        bar.Fill.ForeColor.RGB =
                            System.Drawing.ColorTranslator.ToOle(barColors[i]);
                        bar.Line.Visible = correctAnswers.Contains(i)
                            ? Office.MsoTriState.msoTrue
                            : Office.MsoTriState.msoFalse;
                        if (correctAnswers.Contains(i))
                        {
                            bar.Line.ForeColor.RGB =
                                System.Drawing.ColorTranslator.ToOle(
                                    System.Drawing.Color.FromArgb(22, 163, 74));
                            bar.Line.Weight = 3f;
                        }

                        string answerMarker = correctAnswers.Contains(i) ? "  ✓" : "";
                        AddResultsText(slide,
                            $"{(char)('A' + i)}{answerMarker}\n{counts[i]} response{(counts[i] == 1 ? "" : "s")}",
                            left, chartTop + chartHeight + 8, barWidth, 52, 11, true,
                            System.Drawing.Color.FromArgb(31, 41, 55));
                    }

                    int participantCount = responses.Select(r => r.ParticipantId).Distinct().Count();
                    AddResultsText(slide,
                        $"{participantCount} participant{(participantCount == 1 ? "" : "s")} • {responses.Count} response{(responses.Count == 1 ? "" : "s")}",
                        36, slideHeight - 42, slideWidth - 72, 26, 11, false,
                        System.Drawing.Color.FromArgb(100, 116, 139));

                    try
                    {
                        Application.ActiveWindow.View.GotoSlide(slide.SlideIndex);
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(
                        "Could not insert the results slide: " + ex.Message,
                        "LOKAL", System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                }
            });
        }

        private static void AddResultsText(
            PPT.Slide slide, string text, float left, float top, float width,
            float height, float fontSize, bool bold, System.Drawing.Color color)
        {
            var shape = slide.Shapes.AddTextbox(
                Office.MsoTextOrientation.msoTextOrientationHorizontal,
                left, top, width, height);
            shape.Line.Visible = Office.MsoTriState.msoFalse;
            shape.Fill.Visible = Office.MsoTriState.msoFalse;
            shape.TextFrame.TextRange.Text = text ?? "";
            shape.TextFrame.TextRange.Font.Name = "Segoe UI";
            shape.TextFrame.TextRange.Font.Size = fontSize;
            shape.TextFrame.TextRange.Font.Bold = bold
                ? Office.MsoTriState.msoTrue
                : Office.MsoTriState.msoFalse;
            shape.TextFrame.TextRange.Font.Color.RGB =
                System.Drawing.ColorTranslator.ToOle(color);
            shape.TextFrame.TextRange.ParagraphFormat.Alignment =
                PPT.PpParagraphAlignment.ppAlignCenter;
            shape.TextFrame.VerticalAnchor = Office.MsoVerticalAnchor.msoAnchorMiddle;
        }

        private static HashSet<int> ParseCorrectAnswerIndexes(JToken correctAnswer)
        {
            var result = new HashSet<int>();
            if (correctAnswer == null) return result;
            IEnumerable<JToken> tokens = correctAnswer.Type == JTokenType.Array
                ? correctAnswer.Children()
                : new[] { correctAnswer };
            foreach (var token in tokens)
            {
                if (token.Type == JTokenType.Integer)
                    result.Add(token.Value<int>());
                else
                {
                    string value = token.ToString().Trim();
                    if (int.TryParse(value, out int numeric))
                        result.Add(numeric);
                    else if (value.Length == 1 && char.IsLetter(value[0]))
                        result.Add(char.ToUpperInvariant(value[0]) - 'A');
                }
            }
            return result;
        }

        private static bool ResponseAnswerIncludesOption(Response response, int optionIndex)
        {
            if (response?.Answer == null) return false;
            try
            {
                JToken token = response.Answer as JToken;
                if (token == null)
                {
                    string raw = response.Answer.ToString();
                    try { token = JToken.Parse(raw); }
                    catch { token = new JValue(raw); }
                }
                return AnswerIncludesOption(token, optionIndex);
            }
            catch { return false; }
        }

        private static bool AnswerIncludesOption(JToken token, int optionIndex)
        {
            if (token == null) return false;
            if (token.Type == JTokenType.Array)
                return token.Children().Any(child => AnswerIncludesOption(child, optionIndex));
            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;
                foreach (string name in new[] { "selected_options", "selectedAnswers", "answers", "answer" })
                {
                    JToken nested;
                    if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out nested) &&
                        AnswerIncludesOption(nested, optionIndex))
                        return true;
                }
                return false;
            }
            if (token.Type == JTokenType.Integer)
                return token.Value<int>() == optionIndex;
            string value = token.ToString().Trim().Trim('"');
            if (int.TryParse(value, out int numeric)) return numeric == optionIndex;
            return value.Length == 1 &&
                   char.ToUpperInvariant(value[0]) == (char)('A' + optionIndex);
        }

        private Activity BuildStoredActivity(long activityId)
        {
            string type = "multiple_choice";
            string config = "{\"num_choices\":4,\"allow_multiple\":false,\"correct_answer\":[]}";
            string question = "Activity responses";
            try
            {
                if (CurrentActivityShape != null)
                {
                    type = CurrentActivityShape.Tags["LOKAL_ACTIVITY"] ?? type;
                    config = CurrentActivityShape.Tags["LOKAL_CONFIG"] ?? config;
                    question = CurrentActivityShape.Tags["LOKAL_LAST_ACTIVITY_QUESTION"] ?? question;
                }
            }
            catch { }

            return new Activity
            {
                Id = activityId,
                ClassId = CurrentClassId ?? 0,
                SessionId = CurrentSessionId ?? 0,
                Type = string.IsNullOrWhiteSpace(type) ? "multiple_choice" : type,
                QuestionText = question,
                Config = config,
                StartedAt = DateTime.Now,
                ClosedAt = DateTime.Now
            };
        }

        internal void HideCollectingResponses()
        {
            RunOnUi(() =>
            {
                try
                {
                    if (CollectingResponsesOverlay != null && !CollectingResponsesOverlay.IsDisposed)
                        CollectingResponsesOverlay.Close();
                }
                catch { }
            });
        }

        internal void ShowActivityCountdown(Activity activity)
        {
            RunOnUi(() =>
            {
                try
                {
                    if (activity == null || activity.AutoCloseSeconds <= 0)
                    {
                        HideActivityCountdown();
                        return;
                    }
                    if (ActivityCountdownOverlay == null || ActivityCountdownOverlay.IsDisposed)
                        ActivityCountdownOverlay = new ActivityCountdownOverlayForm(this);
                    ActivityCountdownOverlay.SetActivity(activity);
                    ActivityCountdownOverlay.Show();
                    ActivityCountdownOverlay.BringToFront();
                }
                catch { }
            });
        }

        internal void HideActivityCountdown()
        {
            RunOnUi(() =>
            {
                try
                {
                    if (ActivityCountdownOverlay != null && !ActivityCountdownOverlay.IsDisposed)
                        ActivityCountdownOverlay.Close();
                    if (ActivityLaunchOverlay != null && !ActivityLaunchOverlay.IsDisposed)
                        ActivityLaunchOverlay.Close();
                }
                catch { }
                ActivityCountdownOverlay = null;
                ActivityLaunchOverlay = null;
            });
        }

        private void CloseAllOverlays()
        {
            RunOnUi(() =>
            {
                try
                {
                    if (ToolbarOverlay != null && !ToolbarOverlay.IsDisposed)
                        ToolbarOverlay.Close();
                    if (ClassCodeBadge != null && !ClassCodeBadge.IsDisposed)
                        ClassCodeBadge.Close();
                    if (CollectingResponsesOverlay != null && !CollectingResponsesOverlay.IsDisposed)
                        CollectingResponsesOverlay.Close();
                    if (ActivityCountdownOverlay != null && !ActivityCountdownOverlay.IsDisposed)
                        ActivityCountdownOverlay.Close();
                    if (ActivityLaunchOverlay != null && !ActivityLaunchOverlay.IsDisposed)
                        ActivityLaunchOverlay.Close();
                    if (MyClassOverlay != null && !MyClassOverlay.IsDisposed)
                        MyClassOverlay.Close();
                }
                catch { }

                ToolbarOverlay = null;
                ClassCodeBadge = null;
                CollectingResponsesOverlay = null;
                ActivityCountdownOverlay = null;
                ActivityLaunchOverlay = null;
                MyClassOverlay = null;
            });
        }

        internal void ShowMyClassForm()
        {
            RunOnUi(async () =>
            {
                try
                {
                    if (MyClassOverlay == null || MyClassOverlay.IsDisposed)
                    {
                        MyClassOverlay = new MyClassForm(this);
                    }
                    MyClassOverlay.Show();
                    MyClassOverlay.BringToFront();

                    if (CurrentClassId.HasValue)
                    {
                        await MyClassOverlay.LoadParticipantsAsync(CurrentClassId.Value);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to show My Class form: {ex.Message}");
                }
            });
        }

        // ===== EDIT-MODE TASK PANE (for Add Activity panel) =====

        internal void ShowAddActivityPane()
        {
            if (AddActivityPane == null)
            {
                var panel = new AddActivityPanel(this);
                AddActivityPane = CustomTaskPanes.Add(panel, "LOKAL — Add Activity");
                AddActivityPane.DockPosition = Office.MsoCTPDockPosition.msoCTPDockPositionRight;
                AddActivityPane.Width = 420;
            }
            AddActivityPane.Visible = true;
        }

        internal void HideAddActivityPane()
        {
            if (AddActivityPane != null)
                AddActivityPane.Visible = false;
        }

        // ===== HELPERS =====

        internal bool InsertActivityShape(string type, string label, string icon)
        {
            try
            {
                if (Application.ActiveWindow == null || Application.ActiveWindow.View.Slide == null)
                {
                    System.Windows.Forms.MessageBox.Show("Please select a slide to add the activity.", "LOKAL", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                    return false;
                }

                PPT.Slide slide = Application.ActiveWindow.View.Slide;

                // Check for existing quiz button
                foreach (PPT.Shape existingShape in slide.Shapes)
                {
                    if (existingShape.Tags["LOKAL_ACTIVITY"] != "")
                    {
                        System.Windows.Forms.MessageBox.Show("There is already a quiz button on this slide. Please delete it before adding a new one.",
                            "LOKAL", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                        return false;
                    }
                }

                // Center bottom positioning
                float slideWidth = Application.ActivePresentation.PageSetup.SlideWidth;
                float slideHeight = Application.ActivePresentation.PageSetup.SlideHeight;
                float width = 180;
                float height = 45;
                float left = (slideWidth - width) / 2;
                float top = slideHeight - height - 20;

                // Create main button shape
                var shape = slide.Shapes.AddShape(
                    Office.MsoAutoShapeType.msoShapeRoundedRectangle,
                    left, top, width, height);

                // Styling
                var primaryBlue = LokalUi.Primary;
                shape.Fill.ForeColor.RGB = System.Drawing.ColorTranslator.ToOle(primaryBlue);
                shape.Line.Visible = Office.MsoTriState.msoFalse;
                shape.TextFrame.TextRange.Text = $"{icon}  {label}";
                shape.TextFrame.TextRange.Font.Name = "Segoe UI";
                shape.TextFrame.TextRange.Font.Size = 16;
                shape.TextFrame.TextRange.Font.Color.RGB = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.White);
                shape.TextFrame.TextRange.Font.Bold = Office.MsoTriState.msoTrue;

                shape.Shadow.Visible = Office.MsoTriState.msoTrue;
                shape.Shadow.Style = Office.MsoShadowStyle.msoShadowStyleOuterShadow;
                shape.Shadow.Blur = 8;
                shape.Shadow.Transparency = 0.5f;

                // Create invisible overlay to prevent text editing
                var overlay = slide.Shapes.AddShape(
                    Office.MsoAutoShapeType.msoShapeRectangle,
                    left, top, width, height);
                overlay.Fill.Transparency = 1.0f; // Fully transparent
                overlay.Line.Visible = Office.MsoTriState.msoFalse;

                // Group them together
                var shapesArray = new string[] { shape.Name, overlay.Name };
                var groupShape = slide.Shapes.Range(shapesArray).Group();

                // Tag the group so we know it's our activity
                groupShape.Tags.Add("LOKAL_ACTIVITY", type);
                // Default config
                string configStr = "{\"num_choices\":4,\"allow_multiple\":false,\"correct_answer\":[],\"quiz_mode\":false,\"difficulty\":1,\"start_with_slide\":true,\"minimize_after_start\":false,\"auto_close_enabled\":false,\"auto_close_seconds\":0}";
                try
                {
                    string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LOKAL");
                    string path = System.IO.Path.Combine(dir, "DefaultActivityConfig.json");
                    if (System.IO.File.Exists(path))
                    {
                        configStr = System.IO.File.ReadAllText(path);
                    }
                }
                catch { }

                groupShape.Tags.Add("LOKAL_CONFIG", configStr);

                groupShape.Select();
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Failed to add activity to slide: " + ex.Message, "LOKAL", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return false;
            }
        }

        private void OnWindowSelectionChange(PPT.Selection Sel)
        {
            try
            {
                if (Sel.Type == PPT.PpSelectionType.ppSelectionShapes)
                {
                    PPT.ShapeRange shapeRange = Sel.ShapeRange;
                    if (shapeRange.Count == 1)
                    {
                        PPT.Shape shape = shapeRange[1];
                        string activityType = shape.Tags["LOKAL_ACTIVITY"];
                        if (!string.IsNullOrEmpty(activityType))
                        {
                            CurrentActivityShape = shape;
                            ShowConfigActivityPane(activityType);
                            return;
                        }
                    }
                }
                
                // If not an activity shape, close config pane if it's open.
                // We do NOT want to force open AddActivityPane.
                HideConfigActivityPane();
            }
            catch { }
        }

        internal void HideConfigActivityPane()
        {
            if (ConfigActivityPane != null)
                ConfigActivityPane.Visible = false;
        }

        internal CustomTaskPane ConfigActivityPane { get; private set; }

        internal void ShowConfigActivityPane(string activityType)
        {
            HideAddActivityPane();

            if (ConfigActivityPane == null)
            {
                var panel = new ConfigActivityPanel(this);
                ConfigActivityPane = CustomTaskPanes.Add(panel, "LOKAL — Activity Options");
                ConfigActivityPane.DockPosition = Office.MsoCTPDockPosition.msoCTPDockPositionRight;
                ConfigActivityPane.Width = 420;
            }

            var ctrl = ConfigActivityPane.Control as ConfigActivityPanel;
            if (ctrl != null)
            {
                ctrl.SetActivityType(activityType);
            }

            ConfigActivityPane.Visible = true;
        }

        internal void OpenBrowserUrl(string path)
        {
            var baseUrl = Properties.Settings.Default.ServerUrl ?? "http://localhost:8080";
            System.Diagnostics.Process.Start(baseUrl + path);
        }

        /// <summary>
        /// Reads question text from the active slide by extracting all text content.
        /// Used when starting an activity to auto-populate the question.
        /// </summary>
        internal string GetSlideQuestionText()
        {
            try
            {
                // In slideshow mode ActiveWindow can throw — prefer the show's slide
                var ssw = Application.SlideShowWindows;
                if (ssw.Count > 0)
                    return GetSlideQuestionText(ssw[1].View.Slide);

                if (Application.ActiveWindow?.View?.Slide == null)
                    return "";

                return GetSlideQuestionText(Application.ActiveWindow.View.Slide);
            }
            catch
            {
                return "";
            }
        }

        internal string GetSlideQuestionText(PPT.Slide slide)
        {
            try
            {
                var texts = new System.Collections.Generic.List<string>();

                foreach (PPT.Shape shape in slide.Shapes)
                {
                    // Skip our own activity button shapes
                    if (shape.Tags["LOKAL_ACTIVITY"] != "")
                        continue;

                    if (shape.HasTextFrame == Office.MsoTriState.msoTrue)
                    {
                        string text = shape.TextFrame.TextRange.Text?.Trim();
                        if (!string.IsNullOrEmpty(text))
                            texts.Add(text);
                    }
                }

                return string.Join(" | ", texts);
            }
            catch
            {
                return "";
            }
        }

        // ===== RIBBON HOOK =====

        protected override Microsoft.Office.Core.IRibbonExtensibility CreateRibbonExtensibilityObject()
        {
            return new Ribbon.LokalRibbon(this);
        }

        #region VSTO generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new EventHandler(ThisAddIn_Startup);
            this.Shutdown += new EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}
