using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace LOKAL.PowerPoint
{
    /// <summary>Applies the LOKAL favicon to every top-level add-in window.</summary>
    internal static class LokalUi
    {
        public static readonly Color Brand950 = Color.FromArgb(11, 31, 28);
        public static readonly Color Brand900 = Color.FromArgb(11, 31, 28);
        public static readonly Color Brand800 = Color.FromArgb(11, 31, 28);
        public static readonly Color Primary = Color.FromArgb(11, 31, 28);
        public static readonly Color PrimaryHover = Color.FromArgb(11, 31, 28);
        public static readonly Color PrimaryMedium = Color.FromArgb(11, 31, 28);
        public static readonly Color PrimaryLight = Color.FromArgb(217, 226, 224);
        public static readonly Color PrimaryPale = Color.FromArgb(242, 246, 245);

        private static readonly HashSet<IntPtr> BrandedWindows = new HashSet<IntPtr>();
        private static Icon _appIcon;
        private static Image _trophyImage;
        private static Image _bellImage;
        private static bool _initialized;
        private static readonly object AddStarSoundSync = new object();
        private const string AddStarSoundAlias = "lokal_add_star_sound";

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, StringBuilder returnValue, int returnLength, IntPtr callback);

        public static void EnableGlobalFormBranding()
        {
            if (_initialized) return;
            _initialized = true;
            LoadIcon();
            Application.Idle += (s, e) => ApplyToOpenForms();
            ApplyToOpenForms();
        }

        public static void ApplyBrandIcon(Form form)
        {
            if (form == null) return;
            LoadIcon();
            if (_appIcon == null) return;
            try
            {
                form.Icon = _appIcon;
                if (form.IsHandleCreated) BrandedWindows.Add(form.Handle);
            }
            catch { }
        }

        private static void ApplyToOpenForms()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form == null || form.IsDisposed) continue;
                if (form.IsHandleCreated && BrandedWindows.Contains(form.Handle)) continue;
                ApplyBrandIcon(form);
            }
        }

        private static void LoadIcon()
        {
            if (_appIcon != null) return;
            string[] candidates =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico"),
                @"C:\xampp\htdocs\LOKAL-ThesisSys\assets\favicon.ico"
            };
            foreach (string path in candidates)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    using (var icon = new Icon(path)) _appIcon = (Icon)icon.Clone();
                    return;
                }
                catch { }
            }
        }

        public static Image TrophyImage
        {
            get
            {
                if (_trophyImage != null) return _trophyImage;
                string[] candidates =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trophy.png"),
                    @"C:\xampp\htdocs\LOKAL-ThesisSys\assets\trophy.png"
                };
                foreach (string path in candidates)
                {
                    try
                    {
                        if (!File.Exists(path)) continue;
                        using (var source = Image.FromFile(path)) _trophyImage = new Bitmap(source);
                        break;
                    }
                    catch { }
                }
                return _trophyImage;
            }
        }

        public static bool DrawTrophyImage(Graphics graphics, Rectangle bounds)
        {
            return DrawAssetImage(graphics, bounds, TrophyImage);
        }

        public static Image BellImage
        {
            get
            {
                if (_bellImage != null) return _bellImage;
                string[] candidates =
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bell.png"),
                    @"C:\xampp\htdocs\LOKAL-ThesisSys\assets\bell.png"
                };
                foreach (string path in candidates)
                {
                    try
                    {
                        if (!File.Exists(path)) continue;
                        using (var source = Image.FromFile(path)) _bellImage = new Bitmap(source);
                        break;
                    }
                    catch { }
                }
                return _bellImage;
            }
        }

        public static bool DrawBellImage(Graphics graphics, Rectangle bounds)
        {
            return DrawAssetImage(graphics, bounds, BellImage);
        }

        public static void PlayAddStarSound()
        {
            PlaySoundAsset("add-star-sound.mp3", AddStarSoundAlias);
        }

        public static void PlaySoundAsset(string fileName, string alias)
        {
            PlaySoundAsset(fileName, alias, false);
        }

        public static void PlaySoundAsset(string fileName, string alias, bool repeat)
        {
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(alias)) return;

            string[] candidates =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds", fileName),
                Path.Combine(@"C:\xampp\htdocs\LOKAL-ThesisSys\assets\sounds", fileName)
            };
            string path = null;
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate)) { path = candidate; break; }
            }
            if (string.IsNullOrEmpty(path)) return;

            lock (AddStarSoundSync)
            {
                try
                {
                    mciSendString("close " + alias, null, 0, IntPtr.Zero);
                    int opened = mciSendString("open \"" + path + "\" type mpegvideo alias " + alias,
                        null, 0, IntPtr.Zero);
                    if (opened == 0)
                        mciSendString("play " + alias + " from 0" + (repeat ? " repeat" : ""), null, 0, IntPtr.Zero);
                }
                catch { }
            }
        }

        public static void StopSoundAsset(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias)) return;
            lock (AddStarSoundSync)
            {
                try
                {
                    mciSendString("stop " + alias, null, 0, IntPtr.Zero);
                    mciSendString("close " + alias, null, 0, IntPtr.Zero);
                }
                catch { }
            }
        }

        private static bool DrawAssetImage(Graphics graphics, Rectangle bounds, Image image)
        {
            if (graphics == null || image == null || bounds.Width <= 0 || bounds.Height <= 0) return false;
            System.Drawing.Drawing2D.InterpolationMode previous = graphics.InterpolationMode;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            float scale = Math.Min(bounds.Width / (float)image.Width, bounds.Height / (float)image.Height);
            int width = Math.Max(1, (int)Math.Round(image.Width * scale));
            int height = Math.Max(1, (int)Math.Round(image.Height * scale));
            Rectangle destination = new Rectangle(bounds.Left + (bounds.Width - width) / 2,
                bounds.Top + (bounds.Height - height) / 2, width, height);
            graphics.DrawImage(image, destination);
            graphics.InterpolationMode = previous;
            return true;
        }
    }
}
