using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// Small PDF 1.4 writer with no outside library: pages of any size in millimetres, JPEG images, hairlines and
    /// Helvetica / Times text. Used by Design Preview (PdfExportForm) to export card sheets and the student list.
    ///   - positions are millimetres from the TOP-LEFT corner of the page, like the screen preview;
    ///   - an image is stored once (<see cref="AddImage"/>) and can be placed on many pages (the shared card back);
    ///   - text Helvetica cannot show (Khmer…) is drawn by GDI+ into a small image instead (<see cref="DrawText"/>).
    /// Write the pages in order: BeginPage → Draw… → EndPage, then <see cref="Finish"/> (also done by Dispose).
    /// </summary>
    public sealed class PdfWriter : IDisposable
    {
        private const float PtPerMm = 72f / 25.4f;
        private const int CatalogId = 1, PagesId = 2, FontId = 3, BoldFontId = 4, InfoId = 5, TimesId = 6, TimesBoldId = 7;

        private static readonly Encoding Ansi = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);

        private readonly Stream _out;
        private readonly bool _ownsStream;
        private readonly Dictionary<int, long> _offsets = new Dictionary<int, long>();
        private readonly List<int> _pageIds = new List<int>();
        private readonly string _title;
        private readonly Dictionary<string, TextImage> _textImages = new Dictionary<string, TextImage>();   // DrawTextOnBaseline
        private int _nextId = TimesBoldId + 1;
        private bool _finished;

        // Page being written
        private StringBuilder _content;
        private HashSet<int> _pageImages;
        private float _pageW, _pageH;

        /// <summary>JPEG quality of the images, 1 … 100.</summary>
        public int JpegQuality { get; set; }

        public PdfWriter(string path, string title)
            : this(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None), title, true) { }

        public PdfWriter(Stream output, string title, bool ownsStream)
        {
            _out = output;
            _ownsStream = ownsStream;
            _title = title ?? "";
            JpegQuality = 92;
            WriteAscii("%PDF-1.4\n%âãÏÓ\n");
        }

        public int PageCount { get { return _pageIds.Count; } }

        // ===================== Images =====================

        /// <summary>Stores a picture (flattened on white, JPEG) and returns its id for <see cref="DrawImage"/>.</summary>
        public int AddImage(Image image)
        {
            if (image == null) throw new ArgumentNullException("image");
            byte[] jpeg;
            using (var flat = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
            {
                flat.SetResolution(image.HorizontalResolution, image.VerticalResolution);
                using (Graphics g = Graphics.FromImage(flat))
                {
                    g.Clear(Color.White);
                    g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
                }
                jpeg = EncodeJpeg(flat, JpegQuality);
            }

            int id = _nextId++;
            BeginObject(id);
            WriteAscii(string.Format(CultureInfo.InvariantCulture,
                "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 " +
                "/Filter /DCTDecode /Length {2} >>\nstream\n", image.Width, image.Height, jpeg.Length));
            _out.Write(jpeg, 0, jpeg.Length);
            WriteAscii("\nendstream\nendobj\n");
            return id;
        }

        private static byte[] EncodeJpeg(Bitmap bmp, int quality)
        {
            ImageCodecInfo codec = null;
            foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == ImageFormat.Jpeg.Guid) { codec = c; break; }
            using (var ms = new MemoryStream())
            using (var args = new EncoderParameters(1))
            {
                args.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Max(1, Math.Min(100, quality)));
                bmp.Save(ms, codec, args);
                return ms.ToArray();
            }
        }

        // ===================== Pages =====================

        public void BeginPage(float widthMm, float heightMm)
        {
            if (_content != null) throw new InvalidOperationException("EndPage was not called.");
            if (_finished) throw new ObjectDisposedException("PdfWriter");
            _pageW = widthMm * PtPerMm;
            _pageH = heightMm * PtPerMm;
            _content = new StringBuilder();
            _pageImages = new HashSet<int>();
        }

        /// <summary>Places a stored image in a box (mm from the top-left corner), stretched to fill it.</summary>
        public void DrawImage(int imageId, RectangleF boxMm)
        {
            RequirePage();
            _pageImages.Add(imageId);
            _content.Append("q ")
                .Append(N(boxMm.Width * PtPerMm)).Append(" 0 0 ").Append(N(boxMm.Height * PtPerMm)).Append(' ')
                .Append(N(boxMm.X * PtPerMm)).Append(' ').Append(N(_pageH - boxMm.Bottom * PtPerMm))
                .Append(" cm /Im").Append(imageId).Append(" Do Q\n");
        }

        /// <summary>Straight line, <paramref name="widthPt"/> points thick.</summary>
        public void DrawLine(float x1Mm, float y1Mm, float x2Mm, float y2Mm, float widthPt, Color color)
        {
            RequirePage();
            _content.Append("q ").Append(Rgb(color)).Append(" RG ").Append(N(widthPt)).Append(" w ")
                .Append(N(x1Mm * PtPerMm)).Append(' ').Append(N(_pageH - y1Mm * PtPerMm)).Append(" m ")
                .Append(N(x2Mm * PtPerMm)).Append(' ').Append(N(_pageH - y2Mm * PtPerMm)).Append(" l S Q\n");
        }

        /// <summary>Filled rectangle (table header, row stripes).</summary>
        public void FillRectangle(RectangleF boxMm, Color color)
        {
            RequirePage();
            _content.Append("q ").Append(Rgb(color)).Append(" rg ")
                .Append(N(boxMm.X * PtPerMm)).Append(' ').Append(N(_pageH - boxMm.Bottom * PtPerMm)).Append(' ')
                .Append(N(boxMm.Width * PtPerMm)).Append(' ').Append(N(boxMm.Height * PtPerMm)).Append(" re f Q\n");
        }

        /// <summary>Rectangle outline.</summary>
        public void DrawRectangle(RectangleF boxMm, float widthPt, Color color)
        {
            RequirePage();
            _content.Append("q ").Append(Rgb(color)).Append(" RG ").Append(N(widthPt)).Append(" w ")
                .Append(N(boxMm.X * PtPerMm)).Append(' ').Append(N(_pageH - boxMm.Bottom * PtPerMm)).Append(' ')
                .Append(N(boxMm.Width * PtPerMm)).Append(' ').Append(N(boxMm.Height * PtPerMm)).Append(" re S Q\n");
        }

        /// <summary>
        /// One line of text; <paramref name="topMm"/> is the top of the line. Helvetica when every character exists in it,
        /// otherwise the line is drawn with <paramref name="fallbackFont"/> into an image (Khmer names, for example) filled
        /// with <paramref name="background"/> (white when null) — pass the colour the text sits on.
        /// </summary>
        public void DrawText(string text, float xMm, float topMm, float sizePt, Color color, bool bold, string fallbackFont,
            Color? background = null)
        {
            RequirePage();
            if (string.IsNullOrEmpty(text)) return;

            byte[] ansi = null;
            try { ansi = Ansi.GetBytes(text); }
            catch (EncoderFallbackException) { }

            if (ansi != null)
            {
                float baseline = _pageH - topMm * PtPerMm - sizePt * 0.8f;
                _content.Append("q BT ").Append(Rgb(color)).Append(" rg /").Append(bold ? "F2" : "F1").Append(' ')
                    .Append(N(sizePt)).Append(" Tf ").Append(N(xMm * PtPerMm)).Append(' ').Append(N(baseline))
                    .Append(" Td (").Append(Escape(ansi)).Append(") Tj ET Q\n");
                return;
            }

            // GDI+ text image at 400 dpi, centred on the same line as Helvetica text would be
            // (Khmer fonts are taller: marks above and below), so mixed rows of a table line up.
            const float dpi = 400f;
            using (var font = new Font(fallbackFont ?? "Segoe UI", sizePt * dpi / 72f, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
            {
                Size measured;
                using (var probe = new Bitmap(1, 1))
                using (Graphics pg = Graphics.FromImage(probe))
                    measured = Size.Ceiling(pg.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic));
                int w = Math.Max(1, measured.Width + 4), h = Math.Max(1, measured.Height + 2);
                using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    using (var brush = new SolidBrush(color))
                    {
                        g.Clear(background ?? Color.White);
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        g.DrawString(text, font, brush, 0, 0, StringFormat.GenericTypographic);
                    }
                    int id = AddImage(bmp);
                    float hMm = h / dpi * 25.4f;
                    DrawImage(id, new RectangleF(xMm, TextImageTop(topMm, sizePt, hMm), w / dpi * 25.4f, hMm));
                }
            }
        }

        /// <summary>
        /// One piece of text standing on <paramref name="baselineMm"/>. With <paramref name="times"/>, text that fits the
        /// PDF's Times-Roman / Times-Bold is written as real text; anything else (Khmer…) is drawn with
        /// <paramref name="imageFont"/> into a picture whose baseline sits on the same line, filled with
        /// <paramref name="background"/> (white when null).
        /// </summary>
        public void DrawTextOnBaseline(string text, float xMm, float baselineMm, float sizePt, Color color, bool bold,
            bool times, string imageFont, Color? background = null)
        {
            RequirePage();
            if (string.IsNullOrEmpty(text)) return;

            byte[] ansi = null;
            if (times)
            {
                try { ansi = Ansi.GetBytes(text); }
                catch (EncoderFallbackException) { }
            }
            if (ansi != null)
            {
                _content.Append("q BT ").Append(Rgb(color)).Append(" rg /").Append(bold ? "F4" : "F3").Append(' ')
                    .Append(N(sizePt)).Append(" Tf ").Append(N(xMm * PtPerMm)).Append(' ').Append(N(_pageH - baselineMm * PtPerMm))
                    .Append(" Td (").Append(Escape(ansi)).Append(") Tj ET Q\n");
                return;
            }

            // The same word in the same style is stored once and reused (ប្រុស / ស្រី, faculty names…)
            string family = imageFont ?? "Segoe UI";
            string key = string.Join("\u0001", text, family, N(sizePt), bold ? "b" : "r", Rgb(color),
                background.HasValue ? Rgb(background.Value) : "w");
            TextImage cached;
            if (!_textImages.TryGetValue(key, out cached))
            {
                cached = RenderTextImage(text, family, sizePt, color, bold, background);
                _textImages[key] = cached;
            }
            DrawImage(cached.Id, new RectangleF(xMm, baselineMm - cached.AscentMm, cached.WidthMm, cached.HeightMm));
        }

        private sealed class TextImage
        {
            public int Id;
            public float WidthMm, HeightMm, AscentMm;
        }

        /// <summary>Text drawn by GDI+ at 400 dpi; <see cref="TextImage.AscentMm"/> is the baseline from the top.</summary>
        private TextImage RenderTextImage(string text, string family, float sizePt, Color color, bool bold, Color? background)
        {
            const float dpi = 400f;
            float ascentEm;
            using (var fam = new FontFamily(family))
                ascentEm = fam.GetCellAscent(FontStyle.Regular) / (float)fam.GetEmHeight(FontStyle.Regular);
            float px = sizePt * dpi / 72f;
            using (var font = new Font(family, px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
            {
                Size measured;
                using (var probe = new Bitmap(1, 1))
                using (Graphics pg = Graphics.FromImage(probe))
                    measured = Size.Ceiling(pg.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic));
                int w = Math.Max(1, measured.Width + 4), h = Math.Max(1, measured.Height + 2);
                using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    using (var brush = new SolidBrush(color))
                    {
                        g.Clear(background ?? Color.White);
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        g.DrawString(text, font, brush, 0, 0, StringFormat.GenericTypographic);
                    }
                    return new TextImage
                    {
                        Id = AddImage(bmp), WidthMm = w / dpi * 25.4f, HeightMm = h / dpi * 25.4f,
                        AscentMm = ascentEm * px / dpi * 25.4f
                    };
                }
            }
        }

        /// <summary>Top of a text drawn as a picture <paramref name="heightMm"/> high, centred on the Helvetica line.</summary>
        public static float TextImageTop(float topMm, float sizePt, float heightMm)
        {
            float sizeMm = sizePt / 72f * 25.4f;
            return topMm + sizeMm * 0.45f - heightMm / 2f;
        }

        public void EndPage()
        {
            RequirePage();
            byte[] content = Encoding.ASCII.GetBytes(_content.ToString());
            int contentId = _nextId++;
            BeginObject(contentId);
            WriteAscii("<< /Length " + content.Length + " >>\nstream\n");
            _out.Write(content, 0, content.Length);
            WriteAscii("\nendstream\nendobj\n");

            var images = new StringBuilder();
            foreach (int id in _pageImages) images.Append("/Im").Append(id).Append(' ').Append(id).Append(" 0 R ");

            int pageId = _nextId++;
            BeginObject(pageId);
            WriteAscii("<< /Type /Page /Parent " + PagesId + " 0 R /MediaBox [0 0 " + N(_pageW) + " " + N(_pageH) + "] " +
                       "/Resources << /Font << /F1 " + FontId + " 0 R /F2 " + BoldFontId + " 0 R /F3 " + TimesId + " 0 R /F4 " +
                       TimesBoldId + " 0 R >> " +
                       "/XObject << " + images + ">> /ProcSet [/PDF /Text /ImageC] >> " +
                       "/Contents " + contentId + " 0 R >>\nendobj\n");
            _pageIds.Add(pageId);
            _content = null;
            _pageImages = null;
        }

        // ===================== Document =====================

        /// <summary>Writes the page tree, fonts, info and cross-reference table. The file is complete afterwards.</summary>
        public void Finish()
        {
            if (_finished) return;
            if (_content != null) EndPage();
            if (_pageIds.Count == 0)
            {
                BeginPage(210f, 297f);   // a PDF needs at least one page
                EndPage();
            }

            var kids = new StringBuilder();
            foreach (int id in _pageIds) kids.Append(id).Append(" 0 R ");
            BeginObject(PagesId);
            WriteAscii("<< /Type /Pages /Kids [" + kids + "] /Count " + _pageIds.Count + " >>\nendobj\n");

            BeginObject(FontId);
            WriteAscii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");
            BeginObject(BoldFontId);
            WriteAscii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

            BeginObject(TimesId);
            WriteAscii("<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>\nendobj\n");
            BeginObject(TimesBoldId);
            WriteAscii("<< /Type /Font /Subtype /Type1 /BaseFont /Times-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

            BeginObject(InfoId);
            WriteAscii("<< /Title " + TextString(_title) + " /Creator " + TextString("NPIC Card Studio") +
                       " /Producer " + TextString("NPIC Card Studio") +
                       " /CreationDate (D:" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ") >>\nendobj\n");

            BeginObject(CatalogId);
            WriteAscii("<< /Type /Catalog /Pages " + PagesId + " 0 R >>\nendobj\n");

            long xref = _out.Position;
            int count = _nextId;
            var sb = new StringBuilder();
            sb.Append("xref\n0 ").Append(count).Append('\n').Append("0000000000 65535 f \n");
            for (int id = 1; id < count; id++)
            {
                long offset;
                if (_offsets.TryGetValue(id, out offset)) sb.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
                else sb.Append("0000000000 65535 f \n");
            }
            sb.Append("trailer\n<< /Size ").Append(count).Append(" /Root ").Append(CatalogId).Append(" 0 R /Info ")
              .Append(InfoId).Append(" 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            WriteAscii(sb.ToString());
            _out.Flush();
            _finished = true;
        }

        public void Dispose()
        {
            try { if (!_finished) Finish(); }
            finally { if (_ownsStream) _out.Dispose(); }
        }

        // ===================== Helpers =====================

        private void RequirePage()
        {
            if (_content == null) throw new InvalidOperationException("BeginPage was not called.");
        }

        private void BeginObject(int id)
        {
            _offsets[id] = _out.Position;
            WriteAscii(id + " 0 obj\n");
        }

        private void WriteAscii(string text)
        {
            byte[] bytes = Encoding.GetEncoding(28591).GetBytes(text);   // Latin-1: one byte per char (header comment)
            _out.Write(bytes, 0, bytes.Length);
        }

        private static string N(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Rgb(Color c)
        {
            return N(c.R / 255f) + " " + N(c.G / 255f) + " " + N(c.B / 255f);
        }

        /// <summary>PDF literal string body: ( ) \ escaped, bytes above 126 as octal.</summary>
        private static string Escape(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length + 8);
            foreach (byte b in bytes)
            {
                if (b == '(' || b == ')' || b == '\\') sb.Append('\\').Append((char)b);
                else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                else sb.Append((char)b);
            }
            return sb.ToString();
        }

        /// <summary>Document-info text: UTF-16BE hex string, so any language works.</summary>
        private static string TextString(string text)
        {
            var sb = new StringBuilder("<FEFF");
            foreach (byte b in Encoding.BigEndianUnicode.GetBytes(text ?? "")) sb.Append(b.ToString("X2"));
            return sb.Append('>').ToString();
        }
    }
}
