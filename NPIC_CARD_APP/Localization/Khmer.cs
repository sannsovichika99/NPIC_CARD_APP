using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// English → Khmer table of the app windows (see <see cref="Lang"/>). Split by area over Localization\Khmer.*.cs.
    ///
    /// Rules for entries:
    ///   - the key is the English text exactly as the code shows it (same capitals, punctuation, "…" and spaces);
    ///   - keep {0}, {1} … placeholders, keyboard shortcuts (Ctrl+L, F1), file names and product names
    ///     (Card Studio, Excel, Google Drive, Epson…) as they are;
    ///   - a missing entry is no error: the English text is shown. Debug builds list the missing ones in
    ///     %AppData%\NPIC_CARD_APP\lang-missing-km.txt.
    /// </summary>
    internal static partial class KhmerStrings
    {
        private static Dictionary<string, string> _d;

        public static Dictionary<string, string> Build()
        {
            _d = new Dictionary<string, string>(4096, StringComparer.Ordinal);
            Common();
            MainWindow();
            MainWindowMessages();
            Hub();
            Import();
            Students();
            Photos();
            Cards();
            Designer();
            PdfDesigner();
            Printing();
            Settings();
            Activation();
            Services();
            Mobile();
            Dictionary<string, string> d = _d;
            _d = null;
            return d;
        }

        private static void A(string english, string khmer)
        {
            string old;
            if (_d.TryGetValue(english, out old) && old != khmer)
                Debug.WriteLine("Khmer table: two entries for \"" + english + "\"");
            _d[english] = khmer;
        }
    }
}
