using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Language of the app windows: English or Khmer (<see cref="AppSettings.UiLanguage"/>, language menu in the top bar,
    /// Settings &amp; Help → Language, or Ctrl+Shift+L). Read once at start-up like Dark Mode: switching restarts the app.
    /// Printed cards are never affected (their text comes from Card Text… and the card designs).
    ///
    /// How text is translated:
    ///   - the English text written in the code is the key of the Khmer table (<see cref="KhmerStrings"/>, Localization\Khmer.*.cs);
    ///     text that is not in the table simply stays English, so a missing entry never breaks anything;
    ///   - <see cref="Apply"/> translates every label, button, check box, tab, group box, grid / list column, fixed drop-down
    ///     choice and menu of a window, also the ones added or changed later; popups (<see cref="ModalForm"/>) and the main
    ///     window call it themselves. <see cref="Keep"/> leaves data (a CSV preview with the file's own column names) alone;
    ///   - the popup title and subtitle, message boxes (<see cref="AppDialog"/>), tool tips (<see cref="SetTip"/>), search hints
    ///     (<see cref="SetCueBanner"/>) and the app's own controls (pill buttons, sidebar items, tiles…) translate their text too;
    ///     property lists use <see cref="LocalizedProperties"/>;
    ///   - text built from parts ("Loaded " + n + " student(s) from " + file) matches an entry with placeholders
    ///     ("Loaded {0} student(s) from {1}"), so the code can keep building English text; <see cref="F"/> does the same
    ///     with an explicit format. The parts (numbers, names, paths) are kept; fixed words among them are translated too.
    ///     An entry of one line never matches a whole message of several lines;
    ///   - a window with its own language switch (About &amp; User Guide) builds its texts inside <see cref="Suspend"/>.
    /// Multi-line text is also looked up line by line, so a message made of several fixed paragraphs needs one entry per paragraph.
    /// Longer Khmer text gets invisible break points between words (<see cref="KhmerBreaks"/>) and wrapping labels draw with
    /// GDI+, so lines break between words.
    /// </summary>
    internal static class Lang
    {
        /// <summary>Saved values of <see cref="AppSettings.UiLanguage"/>.</summary>
        public const string EnglishCode = "en", KhmerCode = "km";

        private static bool? _khmer;
        private static Dictionary<string, string> _table;

        /// <summary>True when the windows are shown in Khmer. Read from the settings the first time it is needed.</summary>
        public static bool Khmer
        {
            get
            {
                if (!_khmer.HasValue) _khmer = LoadSetting();
                return _khmer.Value;
            }
        }

        /// <summary>"en" or "km", the language in use.</summary>
        public static string Code { get { return Khmer ? KhmerCode : EnglishCode; } }

        /// <summary>True when a saved <see cref="AppSettings.UiLanguage"/> value means Khmer ("km", "kh", "khmer"…).</summary>
        public static bool IsKhmerCode(string code)
        {
            string c = (code ?? "").Trim().ToLowerInvariant();
            return c == KhmerCode || c == "kh" || c == "khm" || c == "khmer" || c.StartsWith("km-", StringComparison.Ordinal);
        }

        private static bool LoadSetting()
        {
            try { return IsKhmerCode(AppSettings.Load().UiLanguage); }
            catch (Exception) { return false; }
        }

        private static Dictionary<string, string> Table
        {
            get { return _table ?? (_table = KhmerStrings.Build()); }
        }

        // ===================== Text =====================

        /// <summary>
        /// The Khmer text for <paramref name="english"/> when the app is in Khmer and the table has it, else the English.
        /// Text built from parts ("Loaded 12 student(s) from list.xlsx") matches a table entry with placeholders
        /// ("Loaded {0} student(s) from {1}"): the parts are put into the Khmer text in their places.
        /// </summary>
        public static string T(string english)
        {
            if (_suspended > 0) return english;
            string khmer = Translate(english);
            // Longer Khmer text gets invisible break points between words, so it wraps between words (see KhmerBreaks).
            return ReferenceEquals(khmer, english) ? english : KhmerBreaks.Insert(khmer);
        }

        [ThreadStatic] private static int _suspended;

        /// <summary>
        /// Inside the using block <see cref="T(string)"/> returns the English text as it is: for a window that shows its own
        /// language choice (the About &amp; User Guide page has its own English | ខ្មែរ switch). Call <see cref="Keep"/> on that
        /// window too, so its controls are not translated later.
        /// </summary>
        public static IDisposable Suspend()
        {
            _suspended++;
            return new Resume();
        }

        private sealed class Resume : IDisposable
        {
            private bool _done;

            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _suspended--;
            }
        }

        /// <summary><see cref="T(string)"/>, also inside <see cref="Suspend"/> (for the guide naming buttons as the app shows them).</summary>
        public static string Translate(string english)
        {
            if (!Khmer || string.IsNullOrEmpty(english)) return english;
            string khmer;
            if (Table.TryGetValue(english, out khmer)) return khmer;
            if (TryTemplate(english, out khmer)) return khmer;

            if (english.IndexOf('\n') >= 0)
            {
                // Several paragraphs or lines: translate the ones the table knows, keep the others (names, paths, numbers).
                string[] lines = english.Split('\n');
                bool changed = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    bool cr = line.EndsWith("\r", StringComparison.Ordinal);
                    if (cr) line = line.Substring(0, line.Length - 1);
                    string tr = Line(line);
                    if (!ReferenceEquals(tr, line)) { lines[i] = cr ? tr + "\r" : tr; changed = true; }
                }
                if (changed) return string.Join("\n", lines);
                Missing(english);
                return english;
            }

            string one = Line(english);
            if (ReferenceEquals(one, english)) Missing(english);
            return one;
        }

        /// <summary>English or Khmer text written side by side in the code (for text that only one place uses).</summary>
        public static string T(string english, string khmer)
        {
            return Khmer ? khmer : english;
        }

        /// <summary>
        /// <see cref="string.Format(string, object[])"/> with the translated format: <c>Lang.F("{0} students", n)</c>.
        /// The table entry keeps the same {0} {1} … placeholders.
        /// </summary>
        public static string F(string englishFormat, params object[] args)
        {
            string format = T(englishFormat);
            try { return string.Format(CultureInfo.CurrentCulture, format, args); }
            catch (FormatException)
            {
                // A broken table entry must never stop the app: fall back to the English format.
                return string.Format(CultureInfo.CurrentCulture, englishFormat, args);
            }
        }

        // Leading spaces / bullet and trailing spaces or colon around a line: "  • Photo folder:" → "Photo folder".
        private static readonly Regex Around = new Regex(@"^(\s*(?:[•·\-–]\s+)?)(.*?)(:?\s*)$", RegexOptions.CultureInvariant);

        private static string Line(string line)
        {
            if (line.Length == 0) return line;
            string khmer;
            if (Table.TryGetValue(line, out khmer)) return khmer;
            if (TryTemplate(line, out khmer)) return khmer;
            Match m = Around.Match(line);
            if (!m.Success) return line;
            string core = m.Groups[2].Value;
            if (core.Length == 0 || core.Length == line.Length) return line;
            if (Table.TryGetValue(core, out khmer) || TryTemplate(core, out khmer))
            {
                string end = m.Groups[3].Value;
                if (end.StartsWith(":", StringComparison.Ordinal)) end = "៖" + end.Substring(1);   // Khmer colon
                return m.Groups[1].Value + khmer + end;
            }
            return line;
        }

        // ===================== Entries with placeholders =====================

        private sealed class Template
        {
            public Regex Pattern;
            public string Khmer, Prefix, Suffix;
            public int LiteralLength;
            public bool MultiLine;   // the English entry has line breaks
        }

        private static readonly Regex Placeholder = new Regex(@"\{(\d+)\}", RegexOptions.CultureInvariant);
        private static List<Template> _templates;

        /// <summary>Table entries with {0} placeholders, most specific (longest fixed text) first.</summary>
        private static List<Template> Templates
        {
            get
            {
                if (_templates != null) return _templates;
                var list = new List<Template>();
                foreach (KeyValuePair<string, string> entry in Table)
                {
                    MatchCollection holes = Placeholder.Matches(entry.Key);
                    if (holes.Count == 0) continue;
                    string literal = Placeholder.Replace(entry.Key, "");
                    if (literal.Count(char.IsLetter) < 2) continue;   // "{0} {1}" would match anything

                    var pattern = new System.Text.StringBuilder("^");
                    var seen = new HashSet<string>();
                    int at = 0;
                    foreach (Match hole in holes)
                    {
                        pattern.Append(Regex.Escape(entry.Key.Substring(at, hole.Index - at)));
                        string name = "p" + hole.Groups[1].Value;
                        pattern.Append(seen.Add(name) ? "(?<" + name + ">.+?)" : @"\k<" + name + ">");
                        at = hole.Index + hole.Length;
                    }
                    pattern.Append(Regex.Escape(entry.Key.Substring(at))).Append('$');

                    list.Add(new Template
                    {
                        Pattern = new Regex(pattern.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant),
                        Khmer = entry.Value,
                        Prefix = entry.Key.Substring(0, holes[0].Index),
                        Suffix = entry.Key.Substring(holes[holes.Count - 1].Index + holes[holes.Count - 1].Length),
                        LiteralLength = literal.Length,
                        MultiLine = entry.Key.IndexOf('\n') >= 0
                    });
                }
                list.Sort((a, b) => b.LiteralLength.CompareTo(a.LiteralLength));
                return _templates = list;
            }
        }

        private static bool TryTemplate(string text, out string khmer)
        {
            khmer = null;
            bool multiLine = text.IndexOf('\n') >= 0;
            foreach (Template t in Templates)
            {
                // A one-line entry ("Printer: {0}") must not swallow a whole message: multi-line text is matched only by
                // multi-line entries, else translated line by line.
                if (t.MultiLine != multiLine) continue;
                if (text.Length <= t.LiteralLength) continue;
                if (!text.StartsWith(t.Prefix, StringComparison.Ordinal) || !text.EndsWith(t.Suffix, StringComparison.Ordinal)) continue;
                Match m = t.Pattern.Match(text);
                if (!m.Success) continue;
                khmer = Placeholder.Replace(t.Khmer, hole =>
                {
                    Group g = m.Groups["p" + hole.Groups[1].Value];
                    return g.Success ? Part(g.Value) : hole.Value;
                });
                return true;
            }
            return false;
        }

        /// <summary>
        /// A part put into a Khmer text: translated when it is a fixed word ("front", "Auto") or itself matches an entry
        /// ("in 12 days"), else as it is (names, paths, numbers). Always shorter than the text it came from, so this ends.
        /// </summary>
        private static string Part(string value)
        {
            string khmer;
            if (Table.TryGetValue(value, out khmer)) return khmer;
            return TryTemplate(value, out khmer) ? khmer : value;
        }

        // ===================== Windows =====================

        private static readonly ConditionalWeakTable<Control, object> Skipped = new ConditionalWeakTable<Control, object>();
        private static readonly object Mark = new object();

        /// <summary>
        /// Leaves <paramref name="control"/> and everything inside it as it is: data shown as-is (CSV previews with the
        /// file's own column names, student names, file names…). Call before <see cref="Apply"/> runs (in the constructor).
        /// </summary>
        public static TControl Keep<TControl>(TControl control) where TControl : Control
        {
            if (control != null && !IsKept(control)) Skipped.Add(control, Mark);
            return control;
        }

        private static bool IsKept(Control c)
        {
            object o;
            return Skipped.TryGetValue(c, out o);
        }

        /// <summary>
        /// Translates every fixed text of <paramref name="root"/> and its children, and keeps doing so for children added and
        /// text changed later. Does nothing in English. Safe to call more than once.
        /// </summary>
        public static void Apply(Control root)
        {
            if (!Khmer || root == null) return;
            Walk(root);
        }

        private static void Walk(Control c)
        {
            if (IsKept(c)) return;
            // Khmer wraps only at the zero-width spaces between words (KhmerBreaks): GDI+ honours them, GDI does not.
            // Labels that cut their text with "…" never wrap, and keep GDI's tighter line spacing.
            var label = c as Label;
            if (label != null && !label.AutoEllipsis && KhmerBreaks.Available) label.UseCompatibleTextRendering = true;
            Translate(c);

            if (TakesText(c))
            {
                c.TextChanged -= OnTextChanged;   // -= first: Apply may run again on the same tree
                c.TextChanged += OnTextChanged;
            }

            var grid = c as DataGridView;
            if (grid != null)
            {
                foreach (DataGridViewColumn col in grid.Columns) TranslateColumn(col);
                grid.ColumnAdded -= OnColumnAdded;
                grid.ColumnAdded += OnColumnAdded;
            }

            var list = c as ListView;
            if (list != null)
                foreach (ColumnHeader h in list.Columns) h.Text = T(h.Text);

            // Fixed choices ("All students", "Horizontal"…). The code reads them by SelectedIndex, never by their text;
            // items that are data (printer names) have no entry and stay as they are.
            var combo = c as ComboBox;
            if (combo != null && combo.DropDownStyle == ComboBoxStyle.DropDownList && combo.DataSource == null)
                TranslateItems(combo);

            if (c.ContextMenuStrip != null) Hook(c.ContextMenuStrip);
            var strip = c as ToolStrip;
            if (strip != null) Hook(strip);

            c.ControlAdded -= OnControlAdded;
            c.ControlAdded += OnControlAdded;
            foreach (Control child in c.Controls) Walk(child);
        }

        /// <summary>Controls whose Text is a fixed caption (never what the user types or picks).</summary>
        private static bool TakesText(Control c)
        {
            if (c is TextBoxBase || c is ComboBox || c is UpDownBase || c is DateTimePicker || c is ListControl ||
                c is WebBrowser || c is Form || c is ToolStrip)
                return false;
            return c is Label || c is ButtonBase || c is GroupBox || c is TabPage;
        }

        private static void Translate(Control c)
        {
            if (!TakesText(c)) return;
            string text = c.Text;
            if (string.IsNullOrEmpty(text)) return;
            string tr = T(text);
            if (!ReferenceEquals(tr, text) && tr != text) c.Text = tr;
        }

        private static void OnTextChanged(object sender, EventArgs e)
        {
            // Setting the Khmer text raises TextChanged again; the Khmer text is no key, so it stops there.
            Translate((Control)sender);
        }

        private static void OnControlAdded(object sender, ControlEventArgs e)
        {
            Walk(e.Control);
        }

        private static void OnColumnAdded(object sender, DataGridViewColumnEventArgs e)
        {
            TranslateColumn(e.Column);
        }

        private static void TranslateColumn(DataGridViewColumn col)
        {
            if (!string.IsNullOrEmpty(col.HeaderText)) col.HeaderText = T(col.HeaderText);
            if (!string.IsNullOrEmpty(col.ToolTipText)) col.ToolTipText = T(col.ToolTipText);
        }

        private static void TranslateItems(ComboBox combo)
        {
            int selected = combo.SelectedIndex;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                var text = combo.Items[i] as string;
                if (string.IsNullOrEmpty(text)) continue;
                string tr = T(text);
                if (tr != text) combo.Items[i] = tr;
            }
            if (combo.SelectedIndex != selected && selected < combo.Items.Count) combo.SelectedIndex = selected;
        }

        /// <summary>Menus: translated now and again each time they open (their items are often made on the fly).</summary>
        public static void Hook(ToolStrip strip)
        {
            if (!Khmer || strip == null) return;
            TranslateItems(strip.Items);
            var drop = strip as ToolStripDropDown;
            if (drop != null)
            {
                drop.Opening -= OnMenuOpening;
                drop.Opening += OnMenuOpening;
            }
        }

        private static void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            TranslateItems(((ToolStrip)sender).Items);
        }

        private static void TranslateItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                if (item is ToolStripTextBox || item is ToolStripComboBox) continue;
                if (!string.IsNullOrEmpty(item.Text)) item.Text = T(item.Text);
                if (!string.IsNullOrEmpty(item.ToolTipText)) item.ToolTipText = T(item.ToolTipText);
                var menu = item as ToolStripDropDownItem;
                if (menu != null && menu.HasDropDownItems) TranslateItems(menu.DropDownItems);
            }
        }

        // ===================== Helpers =====================

        /// <summary>
        /// Height of <paramref name="text"/> wrapped at <paramref name="width"/>, measured the way it is drawn: with GDI+ for
        /// Khmer (word breaks, see <see cref="KhmerBreaks"/>), with GDI (TextRenderer) otherwise.
        /// </summary>
        public static int WrappedHeight(Control control, string text, System.Drawing.Font font, int width)
        {
            if (!Khmer || !KhmerBreaks.Available)
                return TextRenderer.MeasureText(text, font, new System.Drawing.Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height;
            using (System.Drawing.Graphics g = control.CreateGraphics())
                return (int)Math.Ceiling(g.MeasureString(text, font, width).Height);
        }

        /// <summary><see cref="ToolTip.SetToolTip"/> with the tip in the app's language.</summary>
        public static void SetTip(this ToolTip tip, Control control, string text)
        {
            tip.SetToolTip(control, T(text));
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// <summary>Grey hint shown in an empty text box ("Search…"), in the app's language. Needs the box's handle.</summary>
        public static void SetCueBanner(TextBox box, string text)
        {
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, T(text));
        }

        // ===================== Missing entries (Debug builds) =====================

        private static HashSet<string> _missing;

        /// <summary>
        /// Debug builds only: English text shown in Khmer mode that has no entry yet is listed in
        /// %AppData%\NPIC_CARD_APP\lang-missing-km.txt when the app closes, to help complete the table.
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private static void Missing(string english)
        {
            if (english.Length < 3 || english.Any(ch => ch >= 'ក' && ch <= '៿')) return;   // already Khmer
            if (english.Count(char.IsLetter) < 3) return;
            if (_missing == null)
            {
                _missing = new HashSet<string>(StringComparer.Ordinal);
                Application.ApplicationExit += (s, e) => WriteMissing();
            }
            _missing.Add(english);
        }

        private static void WriteMissing()
        {
            try
            {
                if (_missing == null || _missing.Count == 0) return;
                string path = Path.Combine(Path.GetDirectoryName(AppSettings.FilePath), "lang-missing-km.txt");
                var all = new SortedSet<string>(StringComparer.Ordinal);
                if (File.Exists(path))
                    foreach (string line in File.ReadAllLines(path)) if (line.Length > 0) all.Add(line);
                foreach (string s in _missing) all.Add(s.Replace("\r", "").Replace("\n", "\\n"));
                File.WriteAllLines(path, all);
            }
            catch (Exception)
            {
                // A diagnostic file only: never fail on exit.
            }
        }
    }
}
