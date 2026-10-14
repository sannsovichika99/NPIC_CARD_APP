using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// Every font used on the card, in one place.
    ///   Khmer OS Muol Light  → ព្រះរាជាណាចក្រកម្ពុជា, ជាតិ សាសនា ព្រះមហាក្សត្រ
    ///   Khmer OS Muol        → ប័ណ្ណសម្គាល់ខ្លួននិស្សិត, នាយកវិទ្យាស្ថាន, director name
    ///   Tacteing             → ornament under the motto
    ///   Times New Roman Bold → STUDENT IDENTIFICATION CARD
    ///   Times New Roman      → every Latin letter/digit (name in Latin, NPIC24836, email, website)
    ///   Khmer OS Siemreap    → front field labels and values, អត្តលេខ,  address, issue-date lines, expiry line
    ///   Siemreap             → ស្កេនទីនេះសម្រាប់ព័ត៌មាន
    /// </summary>
    public static class CardFonts
    {
        public const string KhmerOSMuol = "Khmer OS Muol";
        public const string KhmerOSMuolLight = "Khmer OS Muol Light";
        public const string Tacteing = "Tacteing";
        public const string TimesNewRoman = "Times New Roman";
        public const string KhmerOSSiemreap = "Khmer OS Siemreap";
        public const string Siemreap = "Siemreap";

        public static readonly string[] Required =
        {
            KhmerOSMuol, KhmerOSMuolLight, Tacteing, TimesNewRoman, KhmerOSSiemreap, Siemreap
        };

        private static HashSet<string> _installed;

        // ----- Roles (with fallbacks so the app still runs if a font is missing) -----
        public static string Muol { get { return Resolve(KhmerOSMuol, KhmerOSMuolLight, "Khmer UI"); } }
        public static string MuolLight { get { return Resolve(KhmerOSMuolLight, KhmerOSMuol, "Khmer UI"); } }
        public static string Serif { get { return Resolve(TimesNewRoman, "Liberation Serif"); } }
        public static string KhmerSiemreap { get { return Resolve(KhmerOSSiemreap, "Khmer UI"); } }
        public static string SiemreapGoogle { get { return Resolve(Siemreap, KhmerOSSiemreap, "Khmer UI"); } }

        /// <summary>Font for all application UI text (forms, buttons, grids, labels).</summary>
        public static string UI { get { return Resolve(KhmerOSSiemreap, "Segoe UI"); } }

        /// <summary>Null when Tacteing is not installed (the renderer then draws the ornament itself).</summary>
        public static string Ornament { get { return IsInstalled(Tacteing) ? Tacteing : null; } }

        public static bool IsInstalled(string family)
        {
            if (_installed == null)
            {
                using (var fonts = new InstalledFontCollection())
                    _installed = new HashSet<string>(fonts.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            }
            return _installed.Contains(family);
        }

        public static string[] Missing()
        {
            return Required.Where(f => !IsInstalled(f)).ToArray();
        }

        /// <summary>Call once at start-up (MainForm.OnLoad).</summary>
        public static void WarnIfMissing(IWin32Window owner)
        {
            string[] missing = Missing();
            if (missing.Length == 0) return;

            MessageBox.Show(owner,
                "These fonts are not installed, so the card will not match the design:\n\n  • " +
                string.Join("\n  • ", missing) +
                "\n\nInstall them (right-click the .ttf → Install for all users), then restart the app.",
                "Missing fonts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string Resolve(params string[] families)
        {
            foreach (string f in families)
                if (IsInstalled(f)) return f;
            return "Microsoft Sans Serif";
        }

        // ----- Khmer / Latin split -----

        public static bool IsKhmer(char c)
        {
            return (c >= '\u1780' && c <= '\u17FF') ||   // Khmer
                   (c >= '\u19E0' && c <= '\u19FF') ||   // Khmer symbols
                   (c >= '\u200B' && c <= '\u200D');     // ZWSP / ZWNJ / ZWJ used inside Khmer words
        }

        /// <summary>
        /// Splits mixed text into runs: Khmer characters → khmerFamily, Latin letters/digits → Times New Roman.
        /// Punctuation stays with the run before it; spaces go to the run after them
        /// (so no run ends with a space, which keeps width measurement exact).
        /// All runs are drawn at the SAME point size by the renderer.
        /// </summary>
        public static List<TextRun> Split(string text, string khmerFamily, FontStyle latinStyle, Color color)
        {
            var runs = new List<TextRun>();
            if (string.IsNullOrEmpty(text)) return runs;

            bool hasKhmer = text.Any(IsKhmer);
            bool? current = null;              // true = Khmer run, false = Latin run
            var buffer = new StringBuilder();
            var spaces = new StringBuilder();

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c)) { spaces.Append(c); continue; }

                bool khmer;
                if (IsKhmer(c)) khmer = true;
                else if (char.IsLetterOrDigit(c)) khmer = false;
                else khmer = current ?? hasKhmer;   // neutral: ":", "-", ".", "@", "/" …

                if (current.HasValue && current.Value != khmer)
                {
                    runs.Add(MakeRun(buffer.ToString(), current.Value, khmerFamily, latinStyle, color));
                    buffer.Clear();
                }
                current = khmer;
                buffer.Append(spaces).Append(c);
                spaces.Clear();
            }

            buffer.Append(spaces);
            if (buffer.Length > 0)
                runs.Add(MakeRun(buffer.ToString(), current ?? hasKhmer, khmerFamily, latinStyle, color));
            return runs;
        }

        private static TextRun MakeRun(string text, bool khmer, string khmerFamily, FontStyle latinStyle, Color color)
        {
            return khmer
                ? new TextRun(text, khmerFamily, FontStyle.Regular, color)
                : new TextRun(text, Serif, latinStyle, color);
        }
    }

    public class TextRun
    {
        public string Text { get; private set; }
        public string Family { get; private set; }
        public FontStyle Style { get; private set; }
        public Color Color { get; private set; }

        public TextRun(string text, string family, FontStyle style, Color color)
        {
            Text = text;
            Family = family;
            Style = style;
            Color = color;
        }
    }
}
