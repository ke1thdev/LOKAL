using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using PPT = Microsoft.Office.Interop.PowerPoint;
using Office = Microsoft.Office.Core;

namespace LOKAL.PowerPoint
{
    public class AddActivityPanel : UserControl
    {
        private readonly ThisAddIn _addIn;

        private readonly (string type, string fileName, string label)[] _activityTypes = new[]
        {
            ("multiple_choice", "choice.png", "Multiple Choice"),
            ("word_cloud", "word-cloud.png", "Word Cloud"),
            ("short_answer", "blank-paper.png", "Short Answer"),
            ("slide_drawing", "draw.png", "Slide Drawing"),
            ("image_upload", "image.png", "Image Upload"),
            ("fill_blanks", "report.png", "Fill in the Blanks"),
            ("audio_record", "voice-message.png", "Audio Record"),
            ("video_upload", "virtual-event.png", "Video Upload"),
        };

        private readonly Color _bgWhite = Color.White;
        private readonly Color _primaryBlue = LokalUi.Primary;
        private readonly Color _cardBorder = Color.FromArgb(220, 225, 235);
        private readonly Color _cardHover = LokalUi.PrimaryPale;
        private readonly Color _textDark = Color.FromArgb(60, 60, 60);

        public AddActivityPanel(ThisAddIn addIn)
        {
            _addIn = addIn;
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.BackColor = _bgWhite;
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;
            this.Padding = new Padding(20);

            var header = new Label
            {
                Text = "Add Activity",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = _primaryBlue,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4),
                Location = new Point(20, 20)
            };
            this.Controls.Add(header);

            var subtitle = new Label
            {
                Text = "Choose an activity to start with",
                Font = new Font("Segoe UI", 10f),
                ForeColor = _textDark,
                AutoSize = true,
                Location = new Point(20, 60)
            };
            this.Controls.Add(subtitle);

            int startX = 20;
            int startY = 100;
            int colCount = 3;
            int cardSize = 90;
            int spacingX = 12;
            int spacingY = 12;

            for (int i = 0; i < _activityTypes.Length; i++)
            {
                var type = _activityTypes[i];
                int row = i / colCount;
                int col = i % colCount;

                var card = CreateActivityCard(type.type, type.fileName, type.label);
                card.Location = new Point(startX + col * (cardSize + spacingX), startY + row * (cardSize + spacingY));
                this.Controls.Add(card);
            }

            var hintLabel = new Label
            {
                Text = "💡 Hint: See our activity examples here",
                Font = new Font("Segoe UI", 9f),
                ForeColor = _primaryBlue,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Location = new Point(20, startY + 3 * (cardSize + spacingY) + 20)
            };
            this.Controls.Add(hintLabel);
        }

        private Panel CreateActivityCard(string type, string fileName, string label)
        {
            var card = new Panel
            {
                Width = 90,
                Height = 90,
                BackColor = _bgWhite,
                Cursor = Cursors.Hand
            };

            card.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(_cardBorder, 1.5f))
                {
                    int radius = 8;
                    var rect = new Rectangle(1, 1, card.Width - 3, card.Height - 3);
                    var path = GetRoundedRectPath(rect, radius);
                    e.Graphics.DrawPath(pen, path);
                }
            };

            var iconBox = new PictureBox
            {
                Width = 36,
                Height = 36,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Location = new Point((card.Width - 36) / 2, 12),
                Enabled = false // let clicks pass through to panel
            };

            string asmPath = Assembly.GetExecutingAssembly().Location;
            string assetsDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(asmPath), @"..\..\..\..\assets"));
            string fullPath = Path.Combine(assetsDir, fileName);
            if (!File.Exists(fullPath))
                fullPath = @"c:\xampp\htdocs\LOKAL-ThesisSys\assets\" + fileName;
            
            if (File.Exists(fullPath))
            {
                try { iconBox.Image = Image.FromFile(fullPath); } catch { }
            }
            
            card.Controls.Add(iconBox);

            var nameLabel = new Label
            {
                Text = label.Replace(" ", "\n"),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = _textDark,
                TextAlign = ContentAlignment.TopCenter,
                Size = new Size(card.Width - 4, 30),
                BackColor = Color.Transparent,
                Location = new Point(2, 52),
                Enabled = false // let clicks pass through
            };
            card.Controls.Add(nameLabel);

            Action<bool> setHover = (hover) =>
            {
                card.BackColor = hover ? _cardHover : _bgWhite;
                card.Invalidate();
            };

            card.MouseEnter += (s, e) => setHover(true);
            card.MouseLeave += (s, e) => setHover(false);

            card.Click += (s, e) =>
            {
                // To keep backward compatibility with the InsertActivityShape call which takes a text icon
                // We'll just pass a relevant emoji or empty string. The ribbon now uses images, but the slide shape still uses text for now unless we change it.
                // But wait, the user's slide button screenshot shows "📊 Multiple Choice". So we still need emojis for the shape text!
                string emoji = GetEmojiForType(type);
                _addIn.InsertActivityShape(type, label, emoji);
            };

            return card;
        }

        private string GetEmojiForType(string type)
        {
            switch (type)
            {
                case "multiple_choice": return "📊";
                case "word_cloud": return "☁️";
                case "short_answer": return "📝";
                case "slide_drawing": return "🎨";
                case "image_upload": return "🖼️";
                case "fill_blanks": return "📋";
                case "audio_record": return "🎤";
                case "video_upload": return "📹";
                default: return "";
            }
        }

        private System.Drawing.Drawing2D.GraphicsPath GetRoundedRectPath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            Size size = new Size(diameter, diameter);
            Rectangle arc = new Rectangle(bounds.Location, size);
            var path = new System.Drawing.Drawing2D.GraphicsPath();

            if (radius == 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
