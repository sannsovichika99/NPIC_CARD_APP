using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// One line of mixed Khmer / Latin text set like a Word document: Latin letters and digits in one font (Times New
    /// Roman by default), Khmer (complex script) in another (Khmer OS Siemreap), every piece on the same baseline and at
    /// the same point size.
    /// Used by the Student list report so the screen preview and the PDF place each piece at the same spot.
    /// All results are in millimetres.
    /// </summary>
    internal static class MixedText
    {
        public const float MmPerPt = 25.4f / 72f;
        private const float MeasurePx = 100f;   // fonts are measured at 100 px and scaled (widths are linear)

        /// <summary>The two fonts of a line: Latin letters and digits, and complex script (Khmer).</summary>
        public sealed class FontPair
        {
            public readonly string Latin, Khmer;
            public FontPair(string latin, string khmer) { Latin = latin; Khmer = khmer; }

            /// <summary>Times New Roman + Khmer OS Siemreap (or their fallbacks).</summary>
            public static FontPair Default { get { return new FontPair(CardFonts.Serif, CardFonts.KhmerSiemreap); } }
        }

        /// <summary>A piece of the line in one font; <see cref="X"/> is from the start of the line.</summary>
        public sealed class Piece
        {
            public string Text;
            public bool Khmer;
            public float X, Width;
        }

        private static readonly Bitmap Surface = new Bitmap(1, 1);
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();
        private static readonly StringFormat Typographic = MakeFormat();

        private static StringFormat MakeFormat()
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
            return f;
        }

        /// <summary>Splits <paramref name="text"/> into Khmer and Latin pieces and places them side by side.</summary>
        public static List<Piece> Layout(string text, float sizePt, bool bold, FontPair fonts)
        {
            var pieces = new List<Piece>();
            float x = 0f;
            foreach (TextRun run in CardFonts.Split(text ?? "", fonts.Khmer, FontStyle.Regular, Color.Black))
            {
                bool khmer = run.Text.Any(CardFonts.IsKhmer);
                float w = MeasureEm(run.Text, khmer ? fonts.Khmer : fonts.Latin, bold) * sizePt * MmPerPt;
                pieces.Add(new Piece { Text = run.Text, Khmer = khmer, X = x, Width = w });
                x += w;
            }
            return pieces;
        }

        /// <summary>Width of the whole line.</summary>
        public static float Width(string text, float sizePt, bool bold, FontPair fonts)
        {
            List<Piece> pieces = Layout(text, sizePt, bold, fonts);
            return pieces.Count == 0 ? 0f : pieces[pieces.Count - 1].X + pieces[pieces.Count - 1].Width;
        }

        /// <summary>Shortens <paramref name="text"/> with "…" so it fits <paramref name="widthMm"/>.</summary>
        public static string Fit(string text, float widthMm, float sizePt, bool bold, FontPair fonts)
        {
            if (string.IsNullOrEmpty(text) || Width(text, sizePt, bold, fonts) <= widthMm) return text;
            for (int len = text.Length - 1; len > 0; len--)
            {
                string cut = text.Substring(0, len).TrimEnd() + "…";
                if (Width(cut, sizePt, bold, fonts) <= widthMm) return cut;
            }
            return "…";
        }

        /// <summary>Distance from the top of a line of <paramref name="family"/> to its baseline, in ems.</summary>
        public static float AscentEm(string family)
        {
            using (var f = new FontFamily(family))
                return f.GetCellAscent(FontStyle.Regular) / (float)f.GetEmHeight(FontStyle.Regular);
        }

        /// <summary>Height of one line of <paramref name="family"/>, in ems (Khmer OS Siemreap ≈ 1.8, Times ≈ 1.15).</summary>
        public static float LineEm(string family)
        {
            using (var f = new FontFamily(family))
                return f.GetLineSpacing(FontStyle.Regular) / (float)f.GetEmHeight(FontStyle.Regular);
        }

        private static float MeasureEm(string text, string family, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            string key = family + (bold ? "|b" : "|r");
            Font font;
            if (!Fonts.TryGetValue(key, out font))
            {
                font = new Font(family, MeasurePx, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
                Fonts[key] = font;
            }
            using (Graphics g = Graphics.FromImage(Surface))
            {
                g.PageUnit = GraphicsUnit.Pixel;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;   // no grid fitting: widths scale with the size
                return g.MeasureString(text, font, PointF.Empty, Typographic).Width / MeasurePx;
            }
        }
    }
}
