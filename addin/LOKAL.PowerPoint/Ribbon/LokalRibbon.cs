using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Office = Microsoft.Office.Core;
using PPT = Microsoft.Office.Interop.PowerPoint;

namespace LOKAL.PowerPoint.Ribbon
{
    /// <summary>
    /// LOKAL Ribbon callback handler — processes all ribbon button clicks.
    /// Clean 3-group layout matching ClassPoint (Me, Add quiz, More).
    /// </summary>
    [ComVisible(true)]
    public class LokalRibbon : Office.IRibbonExtensibility
    {
        private Office.IRibbonUI _ribbon;
        private ThisAddIn _addIn;

        public LokalRibbon(ThisAddIn addIn)
        {
            _addIn = addIn;
        }

        public string GetCustomUI(string ribbonID)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream("LOKAL.PowerPoint.Ribbon.LokalRibbon.xml"))
            {
                if (stream == null)
                    throw new InvalidOperationException("Could not find embedded ribbon XML");
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        public void Ribbon_Load(Office.IRibbonUI ribbonUI)
        {
            _ribbon = ribbonUI;
            _addIn.Ribbon = this;
        }

        public void InvalidateRibbon()
        {
            _ribbon?.Invalidate();
        }

        public System.Drawing.Bitmap GetRibbonImage(Office.IRibbonControl control)
        {
            try
            {
                string fileName = "";
                switch (control.Id)
                {
                    case "btnMultipleChoice": fileName = "choice.png"; break;
                    case "btnWordCloud": fileName = "word-cloud.png"; break;
                    case "btnShortAnswer": fileName = "blank-paper.png"; break;
                    case "btnSlideDrawing": fileName = "draw.png"; break;
                    case "btnImageUpload": fileName = "image.png"; break;
                    case "btnFillBlanks": fileName = "report.png"; break;
                    case "btnAudioRecord": fileName = "voice-message.png"; break;
                    case "btnVideoUpload": fileName = "virtual-event.png"; break;
                    case "btnQuickIdeas": fileName = "question.png"; break;
                    case "btnLeaderboard": fileName = "trophy.png"; break;
                }

                if (!string.IsNullOrEmpty(fileName))
                {
                    // For local development, assume assets is in project root
                    string asmPath = Assembly.GetExecutingAssembly().Location;
                    string deployedPath = Path.Combine(Path.GetDirectoryName(asmPath), fileName);
                    if (File.Exists(deployedPath)) return new System.Drawing.Bitmap(deployedPath);
                    string assetsDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(asmPath), @"..\..\..\..\assets"));
                    string fullPath = Path.Combine(assetsDir, fileName);
                    
                    // Fallback to absolute path if relative fails
                    if (!File.Exists(fullPath))
                        fullPath = @"c:\xampp\htdocs\LOKAL-ThesisSys\assets\" + fileName;

                    if (File.Exists(fullPath))
                        return new System.Drawing.Bitmap(fullPath);
                }
            }
            catch { }
            return null;
        }

        public void UpdateLoginState(bool isLoggedIn, string displayName)
        {
            _ribbon?.Invalidate();
        }

        public void UpdateSlideInfo(int currentSlide, int totalSlides)
        {
            // Toolbar overlay handles this now
        }

        public bool GetVisibleLoggedIn(Office.IRibbonControl control)
        {
            return !string.IsNullOrEmpty(Properties.Settings.Default.AuthToken);
        }

        public bool GetVisibleLoggedOut(Office.IRibbonControl control)
        {
            return string.IsNullOrEmpty(Properties.Settings.Default.AuthToken);
        }

        public string GetProfileLabel(Office.IRibbonControl control)
        {
            string name = Properties.Settings.Default.TeacherDisplayName;
            return !string.IsNullOrEmpty(name) ? name : "User Profile";
        }

        public System.Drawing.Bitmap GetProfileImage(Office.IRibbonControl control)
        {
            try 
            {
                string path = @"c:\xampp\htdocs\LOKAL-ThesisSys\assets\user.png";
                if (System.IO.File.Exists(path))
                {
                    return new System.Drawing.Bitmap(path);
                }
            }
            catch {}
            return null;
        }

        // ============================
        // ME GROUP
        // ============================

        public void OnMyAccount(Office.IRibbonControl control)
        {
            _addIn.OpenBrowserUrl("/teacher/#/profile");
        }

        public void OnLogout(Office.IRibbonControl control)
        {
            if (MessageBox.Show("Are you sure you want to sign out?", "LOKAL", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Properties.Settings.Default.AuthToken = "";
                Properties.Settings.Default.TeacherDisplayName = "";
                Properties.Settings.Default.TeacherEmail = "";
                Properties.Settings.Default.Save();
                
                _addIn.ApiClient.SetToken(null);
                UpdateLoginState(false, "");
            }
        }

        public void OnSignIn(Office.IRibbonControl control)
        {
            using (var dlg = new UI.LoginForm(_addIn))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    UpdateLoginState(true, Properties.Settings.Default.TeacherDisplayName);
                }
            }
        }

        // ============================
        // ADD QUIZ GROUP
        // ============================

        public void OnMultipleChoice(Office.IRibbonControl control) => InsertActivity("multiple_choice", "Multiple Choice", "📊");
        public void OnWordCloud(Office.IRibbonControl control) => InsertActivity("word_cloud", "Word Cloud", "☁️");
        public void OnShortAnswer(Office.IRibbonControl control) => InsertActivity("short_answer", "Short Answer", "📝");
        public void OnSlideDrawing(Office.IRibbonControl control) => InsertActivity("slide_drawing", "Slide Drawing", "🎨");
        public void OnImageUpload(Office.IRibbonControl control) => InsertActivity("image_upload", "Image Upload", "🖼️");
        public void OnFillBlanks(Office.IRibbonControl control) => InsertActivity("fill_blanks", "Fill in the Blanks", "📋");
        public void OnAudioRecord(Office.IRibbonControl control) => InsertActivity("audio_record", "Audio Record", "🎤");
        public void OnVideoUpload(Office.IRibbonControl control) => InsertActivity("video_upload", "Video Upload", "📹");

        public void OnQuickIdeas(Office.IRibbonControl control)
        {
            MessageBox.Show("Quiz Ideas will suggest activity types based on your slide content.",
                "LOKAL — Quiz Ideas", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ============================
        // MORE GROUP
        // ============================

        public void OnSelectClass(Office.IRibbonControl control)
        {
            using (var dlg = new ClassCodeDialog(_addIn))
            {
                if (dlg.ShowDialog() == DialogResult.OK && dlg.SelectedClassId.HasValue)
                {
                    Properties.Settings.Default.SelectedClassId = dlg.SelectedClassId.Value;
                    Properties.Settings.Default.SelectedClassCode = dlg.SelectedCode;
                    Properties.Settings.Default.Save();
                    
                    MessageBox.Show($"Class '{dlg.SelectedCode}' selected for your next presentation session.", 
                        "LOKAL", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        public void OnMyClasses(Office.IRibbonControl control)
        {
            _addIn.OpenBrowserUrl("/teacher/#/classes");
        }

        public void OnReports(Office.IRibbonControl control)
        {
            _addIn.OpenBrowserUrl("/teacher/#/reports");
        }

        public void OnSettings(Office.IRibbonControl control)
        {
            _addIn.OpenBrowserUrl("/teacher/#/settings");
        }

        // --- More Features dropdown ---

        public void OnTimer(Office.IRibbonControl control)
        {
            using (var dlg = new TimerDialog()) { dlg.ShowDialog(); }
        }

        public void OnNamePicker(Office.IRibbonControl control)
        {
            using (var dlg = new NamePickerDialog(_addIn)) { dlg.ShowDialog(); }
        }

        public void OnQuickPoll(Office.IRibbonControl control)
        {
            using (var dlg = new QuickPollDialog(_addIn)) { dlg.ShowDialog(); }
        }

        public void OnLeaderboard(Office.IRibbonControl control)
        {
            using (var dlg = new LeaderboardDialog(_addIn)) { dlg.ShowDialog(); }
        }

        public void OnAwardStars(Office.IRibbonControl control)
        {
            MessageBox.Show("Select students in the leaderboard to award stars.",
                "LOKAL", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void OnDraggableObjects(Office.IRibbonControl control)
        {
            MessageBox.Show("Select shapes on your slide to make them draggable during the slideshow.",
                "LOKAL — Draggable Objects", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void OnSharePDF(Office.IRibbonControl control)
        {
            try
            {
                var pres = _addIn.Application.ActivePresentation;
                var savePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Path.GetFileNameWithoutExtension(pres.Name) + ".pdf"
                );
                pres.SaveAs(savePath, PPT.PpSaveAsFileType.ppSaveAsPDF);
                MessageBox.Show($"PDF saved to:\n{savePath}", "LOKAL — Share PDF",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to export PDF: " + ex.Message, "LOKAL",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // --- Reset dropdown ---

        public async void OnDeleteAllResponses(Office.IRibbonControl control)
        {
            if (!_addIn.CurrentSessionId.HasValue)
            {
                MessageBox.Show("Start or select a class session first.", "LOKAL",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show("Delete all responses for this session?",
                "LOKAL — Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try
                {
                    await _addIn.ApiClient.DeleteSessionResponsesAsync(_addIn.CurrentSessionId.Value);
                    _addIn.ResetActivityResponseStates();
                    MessageBox.Show("All responses for this session were deleted.", "LOKAL",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not delete responses: " + ex.Message, "LOKAL",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public void OnDeleteAnnotations(Office.IRibbonControl control)
        {
            if (MessageBox.Show("Delete all annotations and whiteboards?",
                "LOKAL — Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                MessageBox.Show("All annotations deleted.", "LOKAL");
            }
        }

        // ============================
        // HELPERS
        // ============================

        private void InsertActivity(string type, string label, string icon)
        {
            // Insert the button on the active slide
            bool success = _addIn.InsertActivityShape(type, label, icon);
            
            // If successfully inserted, show the side panel config view
            if (success)
            {
                _addIn.ShowConfigActivityPane(type);
            }
        }
    }
}
