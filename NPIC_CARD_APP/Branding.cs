using System;
using System.Drawing;
using System.IO;
using System.Reflection;

// Name Windows shows for the exe (Task Manager, file properties, taskbar tooltips).
[assembly: AssemblyTitle("Card Studio")]
[assembly: AssemblyProduct("Card Studio")]

namespace NPIC_CARD_APP
{
    /// <summary>
    /// App identity (logo + window icon), embedded in the exe so it never depends on the user-editable Assets folder.
    /// Not to be confused with npic_png.png, the school logo printed on the cards.
    /// </summary>
    internal static class Branding
    {
        private static Icon _icon;
        private static Image _logo;
        private static bool _iconTried, _logoTried;

        /// <summary>Multi-size window / taskbar icon, or null if the resource cannot be read.</summary>
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

        /// <summary>Transparent 512px logo for the sidebar badge, or null if the resource cannot be read.</summary>
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
