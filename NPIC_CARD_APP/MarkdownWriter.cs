using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Reads Markdown — the release notes written on GitHub — and writes it with a <see cref="GuideWriter"/>, so it looks like
    /// the user guide: headings, paragraphs, **bold**, bullet and numbered lists, task lists, tables, quotes, code blocks and
    /// dividers. Links show their text, images their alt text, and HTML tags are dropped. Italic, strike-through and inline
    /// code keep their text without the markers (the guide has no italic or inline-code style).
    ///
    /// Tables become two-column rows like the guide's shortcut list: first cell in bold, the other cells after it
    /// ("| **Photos** | Matches photos by ID |" → Photos   Matches photos by ID). The header row is left out.
    /// </summary>
    internal static class MarkdownWriter
    {
        private static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*?)\s*#*\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex RuleLine = new Regex(@"^(\*\s*){3,}$|^(-\s*){3,}$|^(_\s*){3,}$", RegexOptions.CultureInvariant);
        private static readonly Regex BulletItem = new Regex(@"^(\s*)[-*+]\s+(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex NumberItem = new Regex(@"^(\s*)(\d{1,9})[.)]\s+(.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex TaskBox = new Regex(@"^\[([ xX])\]\s+", RegexOptions.CultureInvariant);
        private static readonly Regex TableRule = new Regex(@"^\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)*\|?\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex Fence = new Regex(@"^(```|~~~)", RegexOptions.CultureInvariant);

        private static readonly Regex Escape = new Regex(@"\\([\\`*_{}\[\]()#+\-.!|~<>])", RegexOptions.CultureInvariant);
        private static readonly Regex Image = new Regex(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant);
        private static readonly Regex Link = new Regex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.CultureInvariant);
        private static readonly Regex AutoLink = new Regex(@"<((?:https?|mailto):[^>\s]+)>", RegexOptions.CultureInvariant);
        private static readonly Regex LineBreakTag = new Regex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex HtmlTag = new Regex(@"</?[A-Za-z][^>]*>", RegexOptions.CultureInvariant);
        private static readonly Regex InlineCode = new Regex(@"`+([^`]+?)`+", RegexOptions.CultureInvariant);
        private static readonly Regex UnderBold = new Regex(@"__(.+?)__", RegexOptions.CultureInvariant);
        private static readonly Regex StarItalic = new Regex(@"(?<!\*)\*(?![\s*])(.+?)(?<![\s*])\*(?!\*)", RegexOptions.CultureInvariant);
        private static readonly Regex UnderItalic = new Regex(@"(?<![\w_])_(?![\s_])(.+?)(?<![\s_])_(?![\w_])", RegexOptions.CultureInvariant);
        private static readonly Regex Strike = new Regex(@"~~(.+?)~~", RegexOptions.CultureInvariant);

        // Backslash-escaped characters are hidden in the private-use area while the text is parsed, so "\*" or "\|" never
        // start emphasis or split a table cell, then shown again as the plain character.
        private const char MaskBase = '';

        /// <summary>True when the text contains Khmer letters (then the Khmer guide fonts are the better choice).</summary>
        public static bool HasKhmer(string text)
        {
            return (text ?? "").Any(c => c >= 'ក' && c <= '៿');
        }

        public static void Write(string markdown, GuideWriter w)
        {
            string[] lines = MaskLines((markdown ?? "").Replace("\r\n", "\n").Replace('\t', ' ').Split('\n'));
            var paragraph = new List<string>();

            Action flush = () =>
            {
                if (paragraph.Count == 0) return;
                w.Para(Inline(string.Join(" ", paragraph)));
                paragraph.Clear();
            };

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string line = raw.Trim();
                Match m;

                if (line.Length == 0) { flush(); continue; }

                // ```code block```
                m = Fence.Match(line);
                if (m.Success)
                {
                    flush();
                    var code = new List<string>();
                    for (i++; i < lines.Length && !lines[i].Trim().StartsWith(m.Groups[1].Value, StringComparison.Ordinal); i++)
                        code.Add(lines[i].TrimEnd());   // left unmasked by MaskLines: shown exactly as written
                    w.Code(code);
                    continue;
                }

                // # Heading
                m = Heading.Match(line);
                if (m.Success)
                {
                    flush();
                    string title = Inline(m.Groups[2].Value).Replace("**", "");
                    if (m.Groups[1].Value.Length <= 2) w.Heading(title);
                    else w.Subheading(title);
                    continue;
                }

                // Heading underlined with === or --- (setext)
                if (paragraph.Count == 1 && (Regex.IsMatch(line, "^=+$") || Regex.IsMatch(line, "^-+$")))
                {
                    string title = Inline(paragraph[0]).Replace("**", "");
                    paragraph.Clear();
                    if (line[0] == '=') w.Heading(title);
                    else w.Subheading(title);
                    continue;
                }

                // --- divider
                if (RuleLine.IsMatch(line)) { flush(); w.Rule(); continue; }

                // | table |
                if (line.Contains("|") && i + 1 < lines.Length && TableRule.IsMatch(lines[i + 1].Trim()) && lines[i + 1].Contains("-"))
                {
                    flush();
                    i++;   // header row (line) and the |---| rule are skipped
                    for (; i + 1 < lines.Length && lines[i + 1].Trim().Contains("|"); i++)
                        WriteTableRow(Cells(lines[i + 1]), w);
                    w.Space();   // rows only have a tiny gap between them
                    continue;
                }

                // > quote (consecutive lines form one note)
                if (line.StartsWith(">"))
                {
                    flush();
                    var quote = new List<string> { line.TrimStart('>').Trim() };
                    while (i + 1 < lines.Length && lines[i + 1].Trim().StartsWith(">"))
                        quote.Add(lines[++i].Trim().TrimStart('>').Trim());
                    string note = Inline(string.Join(" ", quote.Where(q => q.Length > 0)));
                    if (note.Length > 0) { w.Note(note); w.Space(); }
                    continue;
                }

                // - bullet  /  1. step   (indented lines that follow continue the item)
                m = BulletItem.Match(raw);
                if (m.Success && !RuleLine.IsMatch(line))
                {
                    flush();
                    string item = ReadItem(m.Groups[2].Value, lines, ref i);
                    Match task = TaskBox.Match(item);
                    if (task.Success) item = (task.Groups[1].Value == " " ? "☐  " : "☑  ") + item.Substring(task.Length);
                    w.Bullet(Inline(item));
                    continue;
                }
                m = NumberItem.Match(raw);
                if (m.Success)
                {
                    flush();
                    int number;
                    int.TryParse(m.Groups[2].Value, out number);
                    w.Step(number, Inline(ReadItem(m.Groups[3].Value, lines, ref i)));
                    continue;
                }

                paragraph.Add(line);
            }
            flush();
        }

        /// <summary>
        /// Hides characters that must not be read as Markdown, line by line outside ``` blocks (whose lines stay as written):
        /// first the content of `inline code` (where a backslash is just a backslash), then backslash escapes elsewhere.
        /// </summary>
        private static string[] MaskLines(string[] lines)
        {
            string fence = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                Match f = Fence.Match(trimmed);
                if (fence != null)
                {
                    if (f.Success && trimmed.StartsWith(fence, StringComparison.Ordinal)) fence = null;
                    continue;
                }
                if (f.Success) { fence = f.Groups[1].Value; continue; }

                string s = InlineCode.Replace(lines[i], m => "`" + Protect(m.Groups[1].Value) + "`");
                lines[i] = Escape.Replace(s, m => ((char)(MaskBase + m.Groups[1].Value[0])).ToString());
            }
            return lines;
        }

        /// <summary>The text of a list item plus its continuation lines (indented, not a new block).</summary>
        private static string ReadItem(string first, string[] lines, ref int i)
        {
            var text = new StringBuilder(first.Trim());
            while (i + 1 < lines.Length)
            {
                string next = lines[i + 1];
                string trimmed = next.Trim();
                bool continues = trimmed.Length > 0 && next.StartsWith("  ") &&
                                 !BulletItem.IsMatch(next) && !NumberItem.IsMatch(next) && !Fence.IsMatch(trimmed);
                if (!continues) break;
                text.Append(' ').Append(trimmed);
                i++;
            }
            return text.ToString();
        }

        private static List<string> Cells(string row)
        {
            string r = row.Trim();
            if (r.StartsWith("|")) r = r.Substring(1);
            if (r.EndsWith("|")) r = r.Substring(0, r.Length - 1);
            return r.Split('|').Select(c => c.Trim()).ToList();
        }

        private static void WriteTableRow(List<string> cells, GuideWriter w)
        {
            List<string> filled = cells.Where(c => c.Length > 0).ToList();
            if (filled.Count == 0) return;
            if (filled.Count == 1) { w.Bullet(Inline(filled[0])); return; }
            w.Key(Inline(cells[0]).Replace("**", ""), Inline(string.Join("  ·  ", cells.Skip(1).Where(c => c.Length > 0))));
        }

        /// <summary>Inline Markdown → the guide's text, where only **bold** has a style.</summary>
        private static string Inline(string text)
        {
            string s = text;
            s = InlineCode.Replace(s, m => Protect(m.Groups[1].Value));   // code: its text is never read as Markdown
            s = Image.Replace(s, "$1");
            s = Link.Replace(s, "$1");
            s = AutoLink.Replace(s, "$1");
            s = LineBreakTag.Replace(s, " ");
            s = HtmlTag.Replace(s, "");
            s = UnderBold.Replace(s, "**$1**");
            s = Strike.Replace(s, "$1");
            s = StarItalic.Replace(s, "$1");
            s = UnderItalic.Replace(s, "$1");
            s = s.Replace("&nbsp;", " ").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&");
            return Unmask(s).Trim();
        }

        /// <summary>Hides the Markdown characters of inline code so the later steps leave them alone.</summary>
        private static string Protect(string code)
        {
            var sb = new StringBuilder(code.Length);
            foreach (char c in code) sb.Append("*_~[]()<>|`\\".IndexOf(c) >= 0 ? (char)(MaskBase + c) : c);
            return sb.ToString();
        }

        /// <summary>Shows masked characters again as the plain characters.</summary>
        private static string Unmask(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text) sb.Append(c >= MaskBase && c < MaskBase + 128 ? (char)(c - MaskBase) : c);
            return sb.ToString();
        }
    }
}
