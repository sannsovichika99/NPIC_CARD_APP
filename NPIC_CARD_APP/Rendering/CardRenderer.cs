using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// Draws the NPIC student card (CR80, 85.6 x 54 mm) into a 600-dpi bitmap, following the
    /// card_front.png / card_back.png templates. The same bitmap is used for preview and print.
    /// Khmer text uses TextRenderer (GDI/Uniscribe) so subscript consonants render correctly.
    /// All positions are in millimetres.
    /// </summary>
    public static class CardRenderer
    {
        public const float CardWidthMm = 85.6f;
        public const float CardHeightMm = 54f;
        public const int Dpi = 600;

        /// <summary>Size of the student photo box on the front (the photo editor crops to this shape).</summary>
        public const float PhotoWidthMm = 18.7f;
        public const float PhotoHeightMm = 22f;

        private static readonly Color Navy = Color.FromArgb(46, 49, 146);
        private static readonly Color Blue = Color.FromArgb(47, 75, 159);
        private static readonly Color Red = Color.FromArgb(172, 30, 37);
        private static readonly Color IdRed = Color.FromArgb(190, 30, 45);
        private static readonly Color WatermarkText = Color.FromArgb(249, 243, 228);

        // ---- Font sizes (pt). Khmer and Latin text on the same line ALWAYS share one size. ----
        private const float KingdomPt = 12f;     // ព្រះរាជាណាចក្រកម្ពុជា          (Khmer OS Muol Light)
        private const float MottoPt = 11.5f;      // ជាតិ សាសនា ព្រះមហាក្សត្រ       (Khmer OS Muol Light)
        private const float OrnamentPt = 16f;    // ornament                        (Tacteing)
        private const float CardTitlePt = 17f;   // ប័ណ្ណសម្គាល់ខ្លួននិស្សិត       (Khmer OS Muol)
        private const float CardTitleEnPt = 9.5f;// STUDENT IDENTIFICATION CARD     (Times New Roman Bold)
        private const float ContactPt = 7.2f;    // address / email / website       (Khmer OS Siemreap + Times New Roman)
        private const float ScanPt = 3.6f;       // ស្កេនទីនេះសម្រាប់ព័ត៌មាន        (Siemreap)
        private const float FieldPt = 7.6f;      // front rows, អត្តលេខ + ID, ជំនាន់ទី (Khmer OS Siemreap + Times New Roman)
        private const float ExpiryPt = 6.4f;     // ផុតកំណត់...                     (Khmer OS Siemreap)
        private const float IssuePt = 6.0f;      // ថ្ងៃចេញប័ណ្ណ... / រាជធានីភ្នំពេញ... (Khmer OS Siemreap)
        private const float DirectorTitlePt = 6.6f; // នាយកវិទ្យាស្ថាន              (Khmer OS Muol)
        private const float DirectorNamePt = 6.2f;  // director name                (Khmer OS Muol)

        // ---- Positions (mm) ----
        /// <summary>Moves the signature and director name down (positive = towards the bottom edge).
        /// Keep at 1.5 mm or less, or the director name runs off the 54 mm card.</summary>
        private const float SignatureOffsetYMm = 1.5f;

        /// <summary>Kept for MainForm (grid / text boxes).</summary>
        public static string KhmerFont { get { return CardFonts.KhmerSiemreap; } }
        public static string MuolFont { get { return CardFonts.Muol; } }

        // =====================================================================
        // FRONT — student information (card_front.png)
        // =====================================================================
        public static Bitmap RenderFront(Student s, AppSettings settings)
        {
            CardText t = settings.CardText ?? new CardText();
            Bitmap bmp = CreateCanvas();
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Prepare(g);

                // Faded NPIC shield in the middle
                DrawAsset(g, AssetStore.Get("npic_png.png", true, true), 28.8f, 6.1f, 24.9f, 40.2f, 0.13f, false);

                // Photo, ID, QR, expiry (left column)
                DrawPhoto(g, FindPhoto(settings.PhotoFolder, s.StudentId), 4.2f, 1.8f, PhotoWidthMm, PhotoHeightMm);
                DrawIdLine(g, s.StudentId, 3.5f, 24.9f, 21f, 3.6f);
                DrawQr(g, BuildQrText(s, t.QrTemplate), 4.85f, 28.9f, 17.3f);
                DrawText(g, KhmerText.Expiry(s.ValidUntil), CardFonts.KhmerSiemreap, ExpiryPt, FontStyle.Regular, IdRed,
                    3.5f, 47.4f, 23f, 3.6f, TextFormatFlags.Left);

                // Fields (right column)
                string[] labels = { "គោត្តនាម-នាម", "អក្សរឡាតាំង", "ភេទ", "ថ្ងៃខែឆ្នាំកំណើត", "មហាវិទ្យាល័យ", "កម្រិត" };
                string[] values =
                {
                    s.NameKh, (s.NameEn ?? "").ToUpperInvariant(), KhmerText.Gender(s.Gender),
                    KhmerText.Date(s.DateOfBirth), s.Faculty, s.Level
                };
                float[] rowTop = { 2f, 6.5f, 11f, 15.5f, 20f, 24.5f };   // even 4.5 mm spacing
                //float[] rowTop = { 2.3f, 6.6f, 10.9f, 15.1f, 19.4f, 24.1f };
                const float rowH = 4.2f, labelX = 26.2f, colonX = 41.0f, valueX = 42.4f;

                for (int i = 0; i < labels.Length; i++)
                {
                    float y = rowTop[i];
                    float valueW = (i == 5 ? 68.5f : 83f) - valueX;
                    // Label, colon and value share FieldPt; Latin parts of a value switch to Times New Roman automatically
                    DrawText(g, labels[i], CardFonts.KhmerSiemreap, FieldPt, FontStyle.Regular, Navy, labelX, y, colonX - labelX, rowH, TextFormatFlags.Left);
                    DrawText(g, ":", CardFonts.KhmerSiemreap, FieldPt, FontStyle.Regular, Navy, colonX, y, 1.4f, rowH, TextFormatFlags.Left);
                    DrawText(g, values[i], CardFonts.KhmerSiemreap, FieldPt, FontStyle.Regular, Navy, valueX, y, valueW, rowH, TextFormatFlags.Left);
                }
                DrawText(g, "ជំនាន់ទី: " + KhmerText.Generation(s.Generation), CardFonts.KhmerSiemreap, FieldPt, FontStyle.Regular, Navy,
                    69f, rowTop[5], 15f, rowH, TextFormatFlags.Left);

                // Issue date
                DrawText(g, t.IssueLine1, CardFonts.KhmerSiemreap, IssuePt, FontStyle.Regular, Blue, 34f, 31.3f, 48f, 3.6f, TextFormatFlags.Right);
                DrawText(g, t.IssueLine2, CardFonts.KhmerSiemreap, IssuePt, FontStyle.Regular, Blue, 34f, 34.3f, 44f, 3.6f, TextFormatFlags.Right);

                // Director, stamp, signature
                DrawText(g, t.DirectorTitle, CardFonts.Muol, DirectorTitlePt, FontStyle.Regular, Blue, 55f, 37.3f, 24.8f, 3.8f, TextFormatFlags.HorizontalCenter);
                DrawAsset(g, AssetStore.Get("stamp.png", true, true), 56.7f, 39.1f, 12.2f, 12.2f, 1f, false);
                DrawAsset(g, AssetStore.GetFile(settings.SignaturePath, true, true), 61.5f, 43.4f + SignatureOffsetYMm, 13f, 5.6f, 1f, false);
                DrawText(g, t.DirectorName, CardFonts.Muol, DirectorNamePt, FontStyle.Regular, Navy, 62.9f, 48.4f + SignatureOffsetYMm, 20f, 3.6f, TextFormatFlags.HorizontalCenter);
            }
            return bmp;
        }

        // =====================================================================
        // BACK — institution information (card_back.png), same for every student
        // =====================================================================
        public static Bitmap RenderBack(AppSettings settings)
        {
            CardText t = settings.CardText ?? new CardText();
            Bitmap bmp = CreateCanvas();
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Prepare(g);
                DrawWatermarkText(g);

                // Logos
                DrawAsset(g, AssetStore.Get("ministry_of_labour_logo.png", true, true), 4.4f, 8.5f, 16.4f, 16.4f, 1f, false);
                DrawAsset(g, AssetStore.Get("npic_png.png", true, true), 68.3f, 8.5f, 11.6f, 17.4f, 1f, false);

                // Titles (box widths match the template; text shrinks to fit)
                DrawText(g, "ព្រះរាជាណាចក្រកម្ពុជា", CardFonts.MuolLight, KingdomPt, FontStyle.Regular, Blue,
                    26.2f, 2.4f, 33.2f, 5.4f, TextFormatFlags.HorizontalCenter);
                DrawText(g, "ជាតិ សាសនា ព្រះមហាក្សត្រ", CardFonts.MuolLight, MottoPt, FontStyle.Regular, Blue,
                    26.6f, 7.1f, 32.1f, 4.8f, TextFormatFlags.HorizontalCenter);
                DrawOrnament(g, t.OrnamentText, 42.8f, 13.3f, 9.8f);
                DrawText(g, "ប័ណ្ណសម្គាល់ខ្លួននិស្សិត", CardFonts.Muol, CardTitlePt, FontStyle.Regular, Red,
                    20.8f, 18.6f, 43.6f, 9f, TextFormatFlags.HorizontalCenter);
                DrawText(g, "STUDENT IDENTIFICATION CARD", CardFonts.Muol, CardTitleEnPt, FontStyle.Bold, Red,
                    20.8f, 28.7f, 43.6f, 3.8f, TextFormatFlags.HorizontalCenter);

                // Contact lines — Khmer and Latin all at ContactPt
                DrawPinIcon(g, 6.3f, 41.0f);
                DrawText(g, t.AddressKh, CardFonts.KhmerSiemreap, ContactPt, FontStyle.Regular, Navy, 9.4f, 39.0f, 54f, 4.0f, TextFormatFlags.Left);
                DrawMailIcon(g, 6.3f, 45.1f);
                DrawText(g, t.Email, CardFonts.KhmerSiemreap, ContactPt, FontStyle.Regular, Navy, 9.4f, 43.2f, 50f, 3.8f, TextFormatFlags.Left);
                DrawGlobeIcon(g, 6.3f, 49.3f);
                DrawText(g, t.Website, CardFonts.KhmerSiemreap, ContactPt, FontStyle.Regular, Navy, 9.4f, 47.4f, 50f, 3.8f, TextFormatFlags.Left);

                // QR + social icons
                FillMm(g, Color.White, 68.3f, 33.8f, 11.3f, 11.3f);
                DrawAsset(g, AssetStore.Get("qrcode_for_back_card.png", false, true), 68.6f, 34.1f, 10.7f, 10.7f, 1f, true);
                DrawText(g, "ស្កេនទីនេះសម្រាប់ព័ត៌មាន", CardFonts.SiemreapGoogle, ScanPt, FontStyle.Regular, Red,
                    65.5f, 45.4f, 16.8f, 2.8f, TextFormatFlags.HorizontalCenter);

                string[] icons = { "facebook.png", "telegram.png", "youtube.png", "tiktok.png" };
                const float iconSize = 2.5f, iconStep = 2.73f;
                for (int i = 0; i < icons.Length; i++)
                    DrawAsset(g, AssetStore.Get(icons[i], true, true), 68.6f + i * iconStep, 48.4f, iconSize, iconSize, 1f, false);
            }
            return bmp;
        }

        // =====================================================================
        // CALIBRATION
        // =====================================================================
        /// <summary>Red line = card edge; each black line is 1 mm further inside.</summary>
        public static Bitmap RenderCalibration(int slot, float offsetXmm, float offsetYmm)
        {
            Bitmap bmp = CreateCanvas();
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Prepare(g);
                using (var black = new Pen(Color.Black, MmToPx(0.12f)))
                using (var red = new Pen(Red, MmToPx(0.25f)))
                {
                    for (int i = 0; i <= 5; i++)
                    {
                        float inset = MmToPx(i) + (i == 0 ? MmToPx(0.15f) : 0f);
                        g.DrawRectangle(i == 0 ? red : black, inset, inset, bmp.Width - 2 * inset, bmp.Height - 2 * inset);
                    }
                    float cx = bmp.Width / 2f, cy = bmp.Height / 2f, arm = MmToPx(3f);
                    g.DrawLine(red, cx - arm, cy, cx + arm, cy);
                    g.DrawLine(red, cx, cy - arm, cx, cy + arm);
                }
                DrawText(g, "SLOT " + slot, CardFonts.Serif, 12f, FontStyle.Bold, Navy, 0f, 10f, CardWidthMm, 8f, TextFormatFlags.HorizontalCenter);
                DrawText(g, string.Format(CultureInfo.InvariantCulture, "X = {0:0.0} mm    Y = {1:0.0} mm", offsetXmm, offsetYmm),
                    CardFonts.Serif, 8f, FontStyle.Regular, Color.Black, 0f, 32f, CardWidthMm, 5f, TextFormatFlags.HorizontalCenter);
                DrawText(g, "1 line = 1 mm", CardFonts.Serif, 6f, FontStyle.Italic, Color.DimGray, 0f, 38f, CardWidthMm, 4f, TextFormatFlags.HorizontalCenter);
            }
            return bmp;
        }

        // =====================================================================
        // DRAWING HELPERS
        // =====================================================================
        private static string BuildQrText(Student s, string template)
        {
            string text = !string.IsNullOrWhiteSpace(s.QrData) ? s.QrData : (template ?? "{StudentId}");
            text = text.Replace("{StudentId}", s.StudentId ?? "")
                       .Replace("{NameEn}", s.NameEn ?? "")
                       .Replace("{NameKh}", s.NameKh ?? "");
            return string.IsNullOrWhiteSpace(text) ? (s.StudentId ?? "") : text;
        }

        private static void DrawQr(Graphics g, string text, float xMm, float yMm, float sizeMm)
        {
            if (string.IsNullOrEmpty(text)) return;
            QrEncoder qr = QrEncoder.Encode(text);
            Rectangle box = RectMm(xMm, yMm, sizeMm, sizeMm);
            g.FillRectangle(Brushes.White, box);

            const int quiet = 1;
            float module = (float)box.Width / (qr.Size + 2 * quiet);
            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            for (int y = 0; y < qr.Size; y++)
                for (int x = 0; x < qr.Size; x++)
                {
                    if (!qr.IsDark(x, y)) continue;
                    int left = box.X + (int)Math.Round((x + quiet) * module);
                    int top = box.Y + (int)Math.Round((y + quiet) * module);
                    int right = box.X + (int)Math.Round((x + quiet + 1) * module);
                    int bottom = box.Y + (int)Math.Round((y + quiet + 1) * module);
                    g.FillRectangle(Brushes.Black, left, top, right - left, bottom - top);
                }
            g.SmoothingMode = oldMode;
        }

        /// <summary>"អត្តលេខ:" (Khmer OS Siemreap, navy) + ID (Times New Roman, red) — one size, one baseline.</summary>
        private static void DrawIdLine(Graphics g, string id, float xMm, float yMm, float wMm, float hMm)
        {
            var runs = CardFonts.Split("អត្តលេខ: ", CardFonts.KhmerSiemreap, FontStyle.Regular, Navy);
            runs.AddRange(CardFonts.Split(id, CardFonts.KhmerSiemreap, FontStyle.Regular, IdRed));
            DrawRuns(g, runs, FieldPt, xMm, yMm, wMm, hMm, TextFormatFlags.Left);
        }

        private static void DrawWatermarkText(Graphics g)
        {
            using (var font = new Font(CardFonts.Serif, PtToPx(7f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(WatermarkText))
            {
                int row = 0;
                for (float y = -4f; y < CardHeightMm + 6f; y += 10f, row++)
                {
                    for (float x = -6f + (row % 2) * 7.5f; x < CardWidthMm + 6f; x += 15f)
                    {
                        GraphicsState state = g.Save();
                        g.TranslateTransform(MmToPx(x), MmToPx(y));
                        g.RotateTransform(-30f);
                        g.DrawString("NPIC", font, brush, 0f, 0f);
                        g.Restore(state);
                    }
                }
            }
        }

        /// <summary>Tacteing glyph(s) if the font is installed, otherwise a drawn ornament.</summary>
        private static void DrawOrnament(Graphics g, string tacteingText, float cxMm, float cyMm, float halfLengthMm)
        {
            if (CardFonts.Ornament != null && !string.IsNullOrEmpty(tacteingText))
            {
                var run = new TextRun(tacteingText, CardFonts.Ornament, FontStyle.Regular, Navy);
                DrawRuns(g, new List<TextRun> { run }, OrnamentPt,
                    cxMm - halfLengthMm, cyMm - 2.5f, 2 * halfLengthMm, 5f, TextFormatFlags.HorizontalCenter);
                return;
            }

            float cx = MmToPx(cxMm), cy = MmToPx(cyMm);
            float half = MmToPx(halfLengthMm), inner = MmToPx(2.6f), thick = MmToPx(0.2f);
            using (var brush = new SolidBrush(Navy))
            using (var pen = new Pen(Navy, MmToPx(0.15f)))
            {
                foreach (int side in new[] { -1, 1 })
                {
                    g.FillPolygon(brush, new[]
                    {
                        new PointF(cx + side * half, cy),
                        new PointF(cx + side * inner, cy - thick),
                        new PointF(cx + side * inner, cy + thick)
                    });
                    float dx = cx + side * MmToPx(2.0f), d = MmToPx(0.4f);
                    g.FillPolygon(brush, new[]
                    {
                        new PointF(dx - d, cy), new PointF(dx, cy - d * 0.7f), new PointF(dx + d, cy), new PointF(dx, cy + d * 0.7f)
                    });
                    float dot = MmToPx(0.18f), ox = cx + side * MmToPx(1.15f);
                    g.FillEllipse(brush, ox - dot, cy - dot, 2 * dot, 2 * dot);
                }
                float r = MmToPx(0.55f), c = MmToPx(0.2f);
                g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
                g.FillEllipse(brush, cx - c, cy - c, 2 * c, 2 * c);
            }
        }

        private static void DrawPinIcon(Graphics g, float cxMm, float cyMm)
        {
            float r = MmToPx(0.85f), cx = MmToPx(cxMm), cy = MmToPx(cyMm - 0.35f);
            using (var path = new GraphicsPath())
            using (var brush = new SolidBrush(Navy))
            {
                path.AddArc(cx - r, cy - r, 2 * r, 2 * r, 135f, 270f);
                path.AddLine(cx + r * 0.707f, cy + r * 0.707f, cx, cy + r * 2.1f);
                path.CloseFigure();
                g.FillPath(brush, path);
                float hole = r * 0.42f;
                g.FillEllipse(Brushes.White, cx - hole, cy - hole, 2 * hole, 2 * hole);
            }
        }

        private static void DrawMailIcon(Graphics g, float cxMm, float cyMm)
        {
            float w = MmToPx(2.0f), h = MmToPx(1.4f), x = MmToPx(cxMm) - w / 2, y = MmToPx(cyMm) - h / 2;
            using (var pen = new Pen(Navy, MmToPx(0.17f)) { LineJoin = LineJoin.Round })
            {
                g.DrawRectangle(pen, x, y, w, h);
                g.DrawLines(pen, new[] { new PointF(x, y), new PointF(x + w / 2, y + h * 0.58f), new PointF(x + w, y) });
            }
        }

        private static void DrawGlobeIcon(Graphics g, float cxMm, float cyMm)
        {
            float r = MmToPx(0.95f), cx = MmToPx(cxMm), cy = MmToPx(cyMm);
            using (var pen = new Pen(Navy, MmToPx(0.15f)))
            {
                g.DrawEllipse(pen, cx - r, cy - r, 2 * r, 2 * r);
                g.DrawEllipse(pen, cx - r * 0.45f, cy - r, r * 0.9f, 2 * r);
                g.DrawLine(pen, cx - r, cy, cx + r, cy);
                g.DrawLine(pen, cx, cy - r, cx, cy + r);
                float k = r * 0.5f, half = (float)Math.Sqrt(r * r - k * k);
                g.DrawLine(pen, cx - half, cy - k, cx + half, cy - k);
                g.DrawLine(pen, cx - half, cy + k, cx + half, cy + k);
            }
        }

        /// <summary>
        /// Single-line text. Khmer characters use <paramref name="khmerFamily"/>, Latin letters/digits use
        /// Times New Roman (<paramref name="latinStyle"/>) — both at the SAME size and on one baseline.
        /// </summary>
        private static void DrawText(Graphics g, string text, string khmerFamily, float sizePt, FontStyle latinStyle, Color color,
            float xMm, float yMm, float wMm, float hMm, TextFormatFlags align)
        {
            DrawRuns(g, CardFonts.Split(text, khmerFamily, latinStyle, color), sizePt, xMm, yMm, wMm, hMm, align);
        }

        /// <summary>
        /// Draws runs side by side at one size. If the line is too wide, ALL runs shrink together
        /// (down to 60%), so Khmer and Latin always stay equal. The box is centred vertically on the
        /// tallest font's cell, and every run sits on the same baseline.
        /// </summary>
        private static void DrawRuns(Graphics g, List<TextRun> runs, float sizePt,
            float xMm, float yMm, float wMm, float hMm, TextFormatFlags align)
        {
            if (runs == null || runs.Count == 0) return;

            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine |
                                          TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping;
            Rectangle box = RectMm(xMm, yMm, wMm, hMm);
            float size = sizePt;
            float minSize = sizePt * 0.6f;

            while (true)
            {
                var fonts = new Font[runs.Count];
                try
                {
                    // Spaces at the edges of a run are measured separately (renderers often ignore them).
                    string[] texts = new string[runs.Count];
                    int[] gaps = new int[runs.Count];      // space before the run
                    int[] widths = new int[runs.Count];
                    int total = 0, carry = 0;
                    for (int i = 0; i < runs.Count; i++)
                    {
                        fonts[i] = new Font(runs[i].Family, PtToPx(size), runs[i].Style, GraphicsUnit.Pixel);
                        string raw = runs[i].Text;
                        texts[i] = raw.Trim();
                        int lead = raw.Length - raw.TrimStart().Length;
                        int trail = texts[i].Length == 0 ? 0 : raw.Length - raw.TrimEnd().Length;
                        float space = (lead + trail) == 0 ? 0f : SpaceWidth(g, fonts[i], flags);

                        gaps[i] = carry + (int)Math.Round(lead * space);
                        carry = (int)Math.Round(trail * space);   // goes in front of the next run
                        widths[i] = texts[i].Length == 0 ? 0
                            : TextRenderer.MeasureText(g, texts[i], fonts[i], new Size(int.MaxValue, int.MaxValue), flags).Width;
                        total += gaps[i] + widths[i];
                    }

                    if (total <= box.Width || size <= minSize)
                    {
                        float maxAscent = 0f, maxDescent = 0f;
                        float[] ascents = new float[runs.Count];
                        for (int i = 0; i < runs.Count; i++)
                        {
                            float descent;
                            ascents[i] = CellAscent(fonts[i], out descent);
                            maxAscent = Math.Max(maxAscent, ascents[i]);
                            maxDescent = Math.Max(maxDescent, descent);
                        }
                        float baseline = box.Y + (box.Height - (maxAscent + maxDescent)) / 2f + maxAscent;

                        int x = box.X;
                        if ((align & TextFormatFlags.HorizontalCenter) != 0) x = box.X + (box.Width - total) / 2;
                        else if ((align & TextFormatFlags.Right) != 0) x = box.Right - total;

                        for (int i = 0; i < runs.Count; i++)
                        {
                            x += gaps[i];
                            if (widths[i] == 0) continue;
                            int top = (int)Math.Round(baseline - ascents[i]);
                            var rect = new Rectangle(x, top, widths[i] + 2, (int)Math.Ceiling(maxAscent + maxDescent) + 2);
                            TextRenderer.DrawText(g, texts[i], fonts[i], rect, runs[i].Color, flags);
                            x += widths[i];
                        }
                        return;
                    }
                }
                finally
                {
                    foreach (Font f in fonts)
                        if (f != null) f.Dispose();
                }
                size -= 0.25f;
            }
        }

        private static float SpaceWidth(Graphics g, Font font, TextFormatFlags flags)
        {
            var max = new Size(int.MaxValue, int.MaxValue);
            return TextRenderer.MeasureText(g, "x x", font, max, flags).Width - TextRenderer.MeasureText(g, "xx", font, max, flags).Width;
        }

        /// <summary>Cell ascent/descent in pixels — what GDI uses to place the text top, so baselines line up.</summary>
        private static float CellAscent(Font font, out float descent)
        {
            FontFamily family = font.FontFamily;
            FontStyle style = family.IsStyleAvailable(font.Style) ? font.Style : FontStyle.Regular;
            float em = family.GetEmHeight(style);
            descent = font.Size * family.GetCellDescent(style) / em;
            return font.Size * family.GetCellAscent(style) / em;
        }

        /// <summary>Draws an image scaled to fit (centered) inside the box.</summary>
        private static void DrawAsset(Graphics g, Image img, float xMm, float yMm, float wMm, float hMm, float opacity, bool nearest)
        {
            if (img == null) return;
            Rectangle box = RectMm(xMm, yMm, wMm, hMm);
            float scale = Math.Min((float)box.Width / img.Width, (float)box.Height / img.Height);
            int dw = (int)Math.Round(img.Width * scale), dh = (int)Math.Round(img.Height * scale);
            var dest = new Rectangle(box.X + (box.Width - dw) / 2, box.Y + (box.Height - dh) / 2, dw, dh);

            InterpolationMode oldMode = g.InterpolationMode;
            if (nearest) g.InterpolationMode = InterpolationMode.NearestNeighbor;
            using (var attr = new ImageAttributes())
            {
                attr.SetWrapMode(WrapMode.TileFlipXY); // avoids faint borders when scaling
                if (opacity < 1f) attr.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
                g.DrawImage(img, dest, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attr);
            }
            g.InterpolationMode = oldMode;
        }

        private static void DrawPhoto(Graphics g, string path, float x, float y, float w, float h)
        {
            Rectangle dest = RectMm(x, y, w, h);
            if (path == null)
            {
                using (var brush = new SolidBrush(Color.FromArgb(235, 235, 235)))
                    g.FillRectangle(brush, dest);
                DrawText(g, "PHOTO", CardFonts.Serif, 7f, FontStyle.Bold, Color.Gray, x, y, w, h, TextFormatFlags.HorizontalCenter);
                return;
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image img = Image.FromStream(fs))
            using (var attr = new ImageAttributes())
            {
                ApplyExifOrientation(img);
                attr.SetWrapMode(WrapMode.TileFlipXY);
                Rectangle src = CoverCrop(img.Size, dest.Size);
                g.DrawImage(img, dest, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attr);
            }
        }

        /// <summary>Looks for {StudentId}.jpg/.jpeg/.png/.bmp in the photo folder.</summary>
        public static string FindPhoto(string folder, string studentId)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(studentId) || !Directory.Exists(folder)) return null;
            if (studentId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;

            foreach (string ext in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
            {
                string path = Path.Combine(folder, studentId + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>Crops the source to the destination ratio (keeps the top third for faces).</summary>
        private static Rectangle CoverCrop(Size src, Size dst)
        {
            float srcRatio = (float)src.Width / src.Height;
            float dstRatio = (float)dst.Width / dst.Height;
            if (srcRatio > dstRatio)
            {
                int w = (int)(src.Height * dstRatio);
                return new Rectangle((src.Width - w) / 2, 0, w, src.Height);
            }
            int h = (int)(src.Width / dstRatio);
            return new Rectangle(0, (src.Height - h) / 3, src.Width, h);
        }

        /// <summary>Phone photos are often stored sideways with an EXIF rotation flag.</summary>
        internal static void ApplyExifOrientation(Image img)
        {
            const int OrientationId = 0x0112;
            if (!img.PropertyIdList.Contains(OrientationId)) return;
            byte[] value = img.GetPropertyItem(OrientationId).Value;
            if (value == null || value.Length == 0) return;

            // SHORT tag: the value is in the first byte (Intel) or the second byte (Motorola byte order)
            int orientation = value.Length >= 2 && value[0] == 0 ? value[1] : value[0];
            switch (orientation)
            {
                case 2: img.RotateFlip(RotateFlipType.RotateNoneFlipX); break;   // mirrored (front camera)
                case 3: img.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                case 4: img.RotateFlip(RotateFlipType.RotateNoneFlipY); break;
                case 5: img.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                case 6: img.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                case 7: img.RotateFlip(RotateFlipType.Rotate270FlipX); break;
                case 8: img.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
            }
        }

        private static Bitmap CreateCanvas()
        {
            int w = (int)Math.Round(MmToPx(CardWidthMm));
            int h = (int)Math.Round(MmToPx(CardHeightMm));
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb); // 24bpp: GDI text needs no alpha
            bmp.SetResolution(Dpi, Dpi);
            using (Graphics g = Graphics.FromImage(bmp))
                g.Clear(Color.White);
            return bmp;
        }

        private static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // grayscale AA, no ClearType fringes
        }

        private static void FillMm(Graphics g, Color color, float x, float y, float w, float h)
        {
            using (var brush = new SolidBrush(color))
                g.FillRectangle(brush, RectMm(x, y, w, h));
        }

        private static Rectangle RectMm(float x, float y, float w, float h)
        {
            return Rectangle.Round(new RectangleF(MmToPx(x), MmToPx(y), MmToPx(w), MmToPx(h)));
        }

        private static float MmToPx(float mm) { return mm * Dpi / 25.4f; }

        private static float PtToPx(float pt) { return pt * Dpi / 72f; }
    }
}
