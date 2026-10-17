using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CardStudio.Setup
{
    /// <summary>Shared look for the setup and uninstall windows (same accent colour as Card Studio).</summary>
    internal static class SetupUi
    {
        public static readonly Color Accent = Color.FromArgb(108, 99, 245);
        public static readonly Color TextMain = Color.FromArgb(43, 45, 66);
        public static readonly Color TextSoft = Color.FromArgb(104, 107, 130);
        public static readonly Color Border = Color.FromArgb(228, 229, 238);
        public static readonly Color Danger = Color.FromArgb(214, 48, 72);

        /// <summary>Fixed 560×400 dialog, laid out at 96 DPI and scaled by WinForms on high-DPI screens.</summary>
        public static void InitDialog(Form f, string title)
        {
            f.AutoScaleDimensions = new SizeF(96F, 96F);
            f.AutoScaleMode = AutoScaleMode.Dpi;
            f.Text = title;
            f.Font = new Font("Segoe UI", 9F);
            f.ClientSize = new Size(560, 400);
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = true;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.BackColor = Color.White;
            f.ForeColor = TextMain;
            try { f.Icon = Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly().Location); }
            catch (Exception) { }
        }

        public static Panel Header(string title, string subtitle)
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Accent };

            Image logo = LoadLogo();
            if (logo != null)
                header.Controls.Add(new PictureBox
                {
                    Image = logo, SizeMode = PictureBoxSizeMode.Zoom,
                    Bounds = new Rectangle(18, 14, 48, 48), BackColor = Color.Transparent
                });

            header.Controls.Add(new Label
            {
                Text = title, AutoSize = true, ForeColor = Color.White, BackColor = Color.Transparent,
                Font = new Font("Segoe UI Semibold", 14F), Location = new Point(78, 12)
            });
            header.Controls.Add(new Label
            {
                Text = subtitle, AutoSize = true, ForeColor = Color.FromArgb(226, 224, 252),
                BackColor = Color.Transparent, Location = new Point(80, 44)
            });
            return header;
        }

        /// <summary>Bottom bar; buttons are added right-to-left with <see cref="FooterButton"/>.</summary>
        public static Panel Footer()
        {
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.FromArgb(250, 250, 253) };
            footer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Border });
            return footer;
        }

        /// <param name="slot">0 = right-most button, 1 = the one left of it.</param>
        public static Button FooterButton(Panel footer, string text, int slot, bool primary)
        {
            var b = new Button
            {
                Text = text, Size = new Size(100, 30), Location = new Point(560 - 16 - 100 - slot * 108, 14),
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : TextMain
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            footer.Controls.Add(b);
            return b;
        }

        public static Panel Page()
        {
            return new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 0), Visible = false };
        }

        public static Label Text(Control parent, string text, int x, int y, int width = 512, bool soft = false)
        {
            var l = new Label
            {
                Text = text, Location = new Point(x, y), MaximumSize = new Size(width, 0), AutoSize = true,
                ForeColor = soft ? TextSoft : TextMain
            };
            parent.Controls.Add(l);
            return l;
        }

        public static string FormatSize(long bytes)
        {
            return bytes >= 1024 * 1024
                ? (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB"
                : Math.Max(1, bytes / 1024) + " KB";
        }

        private static Image LoadLogo()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ProductInfo.LogoResource))
                    if (s != null) using (Image img = Image.FromStream(s)) return new Bitmap(img);
            }
            catch (Exception) { }
            return null;
        }
    }
}
