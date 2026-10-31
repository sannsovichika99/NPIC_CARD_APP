using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml;
using CardStudio.Admin;

// ADMIN_CARD compiles Card Studio's own UI kit (UiKit.cs, ModalKit.cs, AccentPalette.cs, SlimScroll.cs, Notifications.cs, linked
// from NPIC_CARD_APP), so the admin tool looks exactly like the app and follows its changes. The kit needs a few app classes that
// are too big to link (translations, card fonts, settings, branding): these are their small admin-tool versions, with the same
// names and members the kit uses. English only: ADMIN_CARD is an administrator tool, like the Serial Generator.

namespace NPIC_CARD_APP
{
    /// <summary>Card Studio's translation helper, English only.</summary>
    internal static class Lang
    {
        public static bool Khmer { get { return false; } }
        public static string T(string english) { return english; }
        public static string T(string english, string khmer) { return english; }
        public static string F(string englishFormat, params object[] args) { return string.Format(englishFormat, args); }
        public static void Apply(Control root) { }
        public static TControl Keep<TControl>(TControl control) where TControl : Control { return control; }

        public static void SetTip(this ToolTip tip, Control control, string text)
        {
            tip.SetToolTip(control, text);
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// <summary>Grey hint shown in an empty text box. Needs the box's handle.</summary>
        public static void SetCueBanner(TextBox box, string text)
        {
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }
    }

    /// <summary>Khmer line breaking is only needed for Khmer text in the app.</summary>
    internal static class KhmerBreaks
    {
        public static bool Available { get { return false; } }
    }

    /// <summary>The app logo and window icon, embedded in ADMIN_CARD.exe from NPIC_CARD_APP\Assets (see ADMIN_CARD.csproj).</summary>
    internal static class Branding
    {
        private static Icon _icon;
        private static Image _logo;
        private static bool _iconTried, _logoTried;

        public static Icon Icon
        {
            get
            {
                if (!_iconTried)
                {
                    _iconTried = true;
                    try { using (Stream s = Open("NPIC_CARD_APP.app.ico")) if (s != null) _icon = new Icon(s); }
                    catch (Exception) { _icon = null; }
                }
                return _icon;
            }
        }

        public static Image Logo
        {
            get
            {
                if (!_logoTried)
                {
                    _logoTried = true;
                    try
                    {
                        using (Stream s = Open("NPIC_CARD_APP.app_logo.png"))
                            if (s != null) using (var img = Image.FromStream(s)) _logo = new Bitmap(img);
                    }
                    catch (Exception) { _logo = null; }
                }
                return _logo;
            }
        }

        private static Stream Open(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        }
    }
}

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>The UI font roles of Card Studio's CardFonts (Khmer OS Siemreap when installed, as in the app).</summary>
    internal static class CardFonts
    {
        public const string KhmerOSSiemreap = "Khmer OS Siemreap";
        private static HashSet<string> _installed;

        public static string UI { get { return IsInstalled(KhmerOSSiemreap) ? KhmerOSSiemreap : "Segoe UI"; } }
        public static string KhmerSiemreap { get { return IsInstalled(KhmerOSSiemreap) ? KhmerOSSiemreap : "Khmer UI"; } }

        public static bool IsInstalled(string family)
        {
            if (_installed == null)
            {
                using (var fonts = new InstalledFontCollection())
                    _installed = new HashSet<string>(fonts.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            }
            return _installed.Contains(family);
        }
    }

    /// <summary>The card preview grid is not used in ADMIN_CARD.</summary>
    internal static class CardGridOverlay
    {
        public static void Draw(Graphics g, RectangleF card) { }
    }
}

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// The two settings the UI kit reads (Theme): dark mode and the primary colour. Taken from Card Studio's own settings on
    /// this PC (%AppData%\NPIC_CARD_APP\settings.xml), so the admin tool matches the app; Dark Mode in ADMIN_CARD overrides it.
    /// </summary>
    internal sealed class AppSettings
    {
        public bool DarkMode { get; set; }
        public string AccentColor { get; set; }

        public static string CardStudioSettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NPIC_CARD_APP", "settings.xml"); }
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                if (File.Exists(CardStudioSettingsPath))
                {
                    var doc = new XmlDocument();
                    doc.Load(CardStudioSettingsPath);
                    XmlNode dark = doc.DocumentElement == null ? null : doc.DocumentElement["DarkMode"];
                    XmlNode accent = doc.DocumentElement == null ? null : doc.DocumentElement["AccentColor"];
                    s.DarkMode = dark != null && string.Equals(dark.InnerText.Trim(), "true", StringComparison.OrdinalIgnoreCase);
                    s.AccentColor = accent == null ? null : accent.InnerText.Trim();
                }
            }
            catch (Exception) { /* unreadable: the default light violet look */ }

            AdminSettings admin = AdminSettings.Current;
            if (admin.DarkMode != null) s.DarkMode = admin.DarkMode.Value;
            return s;
        }
    }
}
