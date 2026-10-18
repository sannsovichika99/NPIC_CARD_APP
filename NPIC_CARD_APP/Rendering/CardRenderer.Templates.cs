using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// The extra card designs chosen with Card Template… (Modern, Minimal, Ribbon). They print the same data as the Classic card
    /// with the same drawing helpers (Khmer/Latin runs, QR, stamp, signature), only arranged differently. All positions in mm on
    /// the 84.6 x 54 mm card. The photo box keeps <see cref="PhotoWidthMm"/> x <see cref="PhotoHeightMm"/> in every design, so photos
    /// cropped with Remove Background… fit all of them.
    /// </summary>
    public static partial class CardRenderer
    {
        private static readonly Color Muted = Color.FromArgb(104, 110, 138);
        private static readonly Color Hairline = Color.FromArgb(206, 211, 228);
        private static readonly Color BandSubtext = Color.FromArgb(214, 220, 250);

        /// <summary>Field size of the new designs: a little smaller than Classic, so six rows fit beside the photo.</summary>
        private const float TplFieldPt = 6.8f;
        private const float TplLabelPt = 6.4f;

        /// <summary>Bar along the bottom edge of Modern (front and back).</summary>
        private const float FooterTopMm = 52.6f;

        private static string[] FieldLabels()
        {
            return new[] { "គោត្តនាម-នាម", "អក្សរឡាតាំង", "ភេទ", "ថ្ងៃខែឆ្នាំកំណើត", "មហាវិទ្យាល័យ", "កម្រិត" };
        }

        private static string[] FieldValues(Student s)
        {
            return new[]
            {
                s.NameKh, (s.NameEn ?? "").ToUpperInvariant(), KhmerText.Gender(s.Gender),
                KhmerText.Date(s.DateOfBirth), s.Faculty, s.Level
            };
        }

        // =====================================================================
        // MODERN — navy header band, framed photo on the left, footer bar
        // =====================================================================
        private static Bitmap RenderModernFront(Student s, AppSettings settings)
        {
            CardText t = settings.CardText ?? new CardText();
            Bitmap bmp = CreateCanvas();
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Prepare(g);

                // Header band + red accent line
                FillMm(g, Navy, 0f, 0f, CardWidthMm, 10.4f);
                FillMm(g, Red, 0f, 10.4f, CardWidthMm, 0.6f);
                FillEllipseMm(g, Color.White, 5.9f, 5.2f, 4.1f);
                DrawAsset(g, AssetStore.Get("npic_png.png", true, true), 3.1f, 1.7f, 5.6f, 7.0f, 1f, false);
                DrawText(g, "ប័ណ្ណសម្គាល់ខ្លួននិស្សិត", CardFonts.Muol, 9f, FontStyle.Regular, Color.White,
                    11.6f, 0.7f, 48f, 5.4f, TextFormatFlags.Left);
                DrawText(g, "STUDENT IDENTIFICATION CARD", CardFonts.Muol, 5.6f, FontStyle.Bold, BandSubtext,
                    11.6f, 5.8f, 48f, 3.4f, TextFormatFlags.Left);

                // Generation pill on the right of the band
                FrameRoundMm(g, Color.White, 64.6f, 2.6f, 17.4f, 5.2f, 2.6f, 0.2f);
                DrawText(g, "ជំនាន់ទី " + KhmerText.Generation(s.Generation), CardFonts.KhmerSiemreap, 7.2f, FontStyle.Regular,
                    Color.White, 64.6f, 2.6f, 17.4f, 5.2f, TextFormatFlags.HorizontalCenter);

                // Faded shield behind the fields
                DrawAsset(g, AssetStore.Get("npic_png.png", true, true), 46f, 13.5f, 22f, 36f, 0.07f, false);

                // Left column: photo, ID, expiry
                DrawPhoto(g, FindPhoto(settings.PhotoFolder, s.StudentId), 4.4f, 12.8f, PhotoWidthMm, PhotoHeightMm);
                FrameMm(g, Navy, 4.4f, 12.8f, PhotoWidthMm, PhotoHeightMm, 0.3f);
                DrawIdLine(g, s.StudentId, 2.0f, 35.4f, 23.1f, 3.4f, TextFormatFlags.HorizontalCenter);
                DrawText(g, KhmerText.Expiry(s.ValidUntil), CardFonts.KhmerSiemreap, ExpiryPt, FontStyle.Regular, IdRed,
                    2.0f, 38.8f, 23.1f, 3.2f, TextFormatFlags.HorizontalCenter);

                // Fields
                float[] rows = RowTops(12.6f, 3.6f, 6);
                DrawFieldRows(g, FieldLabels(), FieldValues(s), rows, 3.6f, 26.6f, 40.4f, 41.7f, 83f, Navy, Navy);

                // Issue date (right-aligned under the fields)
                DrawText(g, t.IssueLine1, CardFonts.KhmerSiemreap, IssuePt, FontStyle.Regular, Blue, 26.6f, 34.4f, 56.4f, 2.9f, TextFormatFlags.Right);
                DrawText(g, t.IssueLine2, CardFonts.KhmerSiemreap, IssuePt, FontStyle.Regular, Blue, 26.6f, 37.2f, 56.4f, 2.9f, TextFormatFlags.Right);

                // QR (middle) and director block (right)
                DrawQr(g, BuildQrText(s, t.QrTemplate), 27.0f, 39.6f, 12.6f);
                DrawDirectorBlock(g, t, 58.6f, 40.6f, 0.85f);

                // Footer bar
                FillMm(g, Navy, 0f, FooterTopMm, CardWidthMm, CardHeightMm - FooterTopMm);
                FillMm(g, Red, 0f, FooterTopMm, 22f, CardHeightMm - FooterTopMm);
            }
            return bmp;
        }

        private static Bitmap RenderModernBack(AppSettings settings)
        {
            CardText t = settings.CardText ?? new CardText();
            Bitmap bmp = CreateCanvas();
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Prepare(g);

                // Band with both logos on white discs, kingdom + motto in white
                FillMm(g, Navy, 0f, 0f, CardWidthMm, 13.2f);
                FillMm(g, Red, 0f, 13.2f, CardWidthMm, 0.6f);
                FillEllipseMm(g, Color.White, 8.0f, 6.6f, 5.4f);
                DrawAsset(g, AssetStore.Get("ministry_of_labour_logo.png", true, true), 3.7f, 2.3f, 8.6f, 8.6f, 1f, false);
                FillEllipseMm(g, Color.White, 76.6f, 6.6f, 5.4f);
                DrawAsset(g, AssetStore.Get("npic_png.png", true, true), 73.2f, 2.0f, 6.8f, 9.2f, 1f, false);
                DrawText(g, "ព្រះរាជាណាចក្រកម្ពុជា", CardFonts.MuolLight, 10f, FontStyle.Regular, Color.White,
                    15f, 1.0f, 54.6f, 5.4f, TextFormatFlags.HorizontalCenter);
                DrawText(g, "ជាតិ សាសនា ព្រះមហាក្សត្រ", CardFonts.MuolLight, 9f, FontStyle.Regular, BandSubtext,
                    15f, 6.6f, 54.6f, 5.0f, TextFormatFlags.HorizontalCenter);

                // Titles
                DrawText(g, "ប័ណ្ណសម្គាល់ខ្លួននិស្សិត", CardFonts.Muol, 15f, FontStyle.Regular, Red,
                    8f, 15.6f, 68.6f, 8.2f, TextFormatFlags.HorizontalCenter);
                DrawText(g, "STUDENT IDENTIFICATION CARD", CardFonts.Muol, 8.5f, FontStyle.Bold, Red,
                    8f, 24.2f, 68.6f, 3.8f, TextFormatFlags.HorizontalCenter);
                DrawOrnament(g, t.OrnamentText, 42.3f, 30.0f, 8.6f);

                DrawContacts(g, t, 6.3f, 9.4f, 34.0f, 4.3f, 56f);
                DrawBackQrBlock(g, t, 69.3f, 32.0f, true);

                FillMm(g, Navy, 0f, FooterTopMm, CardWidthMm, CardHeightMm - FooterTopMm);
                FillMm(g, Red, CardWidthMm - 22f, FooterTopMm, 22f, CardHeightMm - FooterTopMm);
            }
            return bmp;
        }

        // =====================================================================
        // MINIMAL — white card, large name, photo on the right, hairline divider
        // =====================================================================
        private static Bitmap RenderMinimalFront(Student s, AppSettings settings)
        {
            CardText t = settings.CardText ?? new CardText();
            Bitmap bmp = CreateCanvas();
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Prepare(g);

                // Header: small logo + title, generation on the right, hairline underneath
                DrawAsset(g, AssetStore.Get("npic_png.png", true, true), 3.6f, 2.0f, 5.2f, 7.0f, 1f, false);
                DrawText(g, "ប័ណ្ណសម្គាល់ខ្លួននិស្សិត", CardFonts.Muol, 8.4f, FontStyle.Regular, Navy,
                    10.2f, 1.4f, 46f, 4.8f, TextFormatFlags.Left);
                DrawText(g, "STUDENT IDENTIFICATION CARD", CardFonts.Muol, 5.2f, FontStyle.Bold, Muted,
                    10.2f, 6.0f, 46f, 3.0f, TextFormatFlags.Left);
                DrawText(g, "ជំនាន់ទី: " + KhmerText.Generation(s.Generation), CardFonts.KhmerSiemreap, TplFieldPt, FontStyle.Regular, Navy,
                    58f, 3.0f, 23.4f, 3.6f, TextFormatFlags.Right);
                LineMm(g, Hairline, 3.6f, 10.0f, 81.4f, 10.0f, 0.2f);

                // Photo on the right with the ID under it
                DrawPhoto(g, FindPhoto(settings.PhotoFolder, s.StudentId), 62.7f, 12.0f, PhotoWidthMm, PhotoHeightMm);
                FrameMm(g, Hairline, 62.7f, 12.0f, PhotoWidthMm, PhotoHeightMm, 0.2f);
                DrawIdLine(g, s.StudentId, 59.6f, 34.4f, 24.8f, 3.4f, TextFormatFlags.HorizontalCenter);

                // Large name
                DrawText(g, s.NameKh, CardFonts.MuolLight, 10.5f, FontStyle.Regular, Navy, 3.6f, 11.4f, 56f, 6.0f, TextFormatFlags.Left);
                DrawText(g, (s.NameEn ?? "").ToUpperInvariant(), CardFonts.Serif, 8f, FontStyle.Bold, IdRed,
                    3.6f, 17.4f, 56f, 4.0f, TextFormatFlags.Left);

                // Remaining fields: small muted labels
                string[] labels = { "ភេទ", "ថ្ងៃខែឆ្នាំកំណើត", "មហាវិទ្យាល័យ", "កម្រិត" };
                string[] values = { KhmerText.Gender(s.Gender), KhmerText.Date(s.DateOfBirth), s.Faculty, s.Level };
                DrawFieldRows(g, labels, values, RowTops(22.2f, 3.7f, 4), 3.7f, 3.6f, 18.4f, 19.7f, 59f, Muted, Navy);

                // Bottom: QR, expiry + issue date, director block
                LineMm(g, Hairline, 3.6f, 37.6f, 57f, 37.6f, 0.2f);
                DrawQr(g, BuildQrText(s, t.QrTemplate), 3.0f, 39.4f, 12.6f);
                DrawText(g, KhmerText.Expiry(s.ValidUntil), CardFonts.KhmerSiemreap, ExpiryPt, FontStyle.Regular, IdRed,
                    16.6f, 39.6f, 40f, 3.4f, TextFormatFlags.Left);
                DrawText(g, t.IssueLine1, CardFonts.KhmerSiemreap, IssuePt, FontStyle.Regular, Blue, 16.6f, 44.2f, 40.4f, 2.9f, TextFormatFlags.Left);
                DrawText(g, t.IssueLine2, CardFonts.KhmerSiemreap, IssuePt, FontStyle.Regular, Blue, 16.6f, 47.2f, 40.4f, 2.9f, TextFormatFlags.Left);
                DrawDirectorBlock(g, t, 58.4f, 39.0f, 0.9f);
            }
            return bmp;
        }

        private static Bitmap RenderMinimalBack(AppSettings settings)
        {
            CardText t = settings.CardText ?? new CardText();
            Bitmap bmp = CreateCanvas();
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Prepare(g);

                DrawAsset(g, AssetStore.Get("ministry_of_labour_logo.png", true, true), 4.4f, 3.6f, 13.4f, 13.4f, 1f, false);
                DrawAsset(g, AssetStore.Get("npic_png.png", true, true), 69.8f, 3.2f, 10.4f, 14.4f, 1f, false);
                DrawText(g, "ព្រះរាជាណាចក្រកម្ពុជា", CardFonts.MuolLight, 10.5f, FontStyle.Regular, Navy,
                    20f, 2.0f, 44.6f, 5.2f, TextFormatFlags.HorizontalCenter);
                DrawText(g, "ជាតិ សាសនា ព្រះមហាក្សត្រ", CardFonts.MuolLight, 9.5f, FontStyle.Regular, Navy,
                    20f, 7.0f, 44.6f, 4.6f, TextFormatFlags.HorizontalCenter);
                DrawOrnament(g, t.OrnamentText, 42.3f, 13.2f, 8.6f);

                DrawText(g, "ប័ណ្ណសម្គាល់ខ្លួននិស្សិត", CardFonts.Muol, 14f, FontStyle.Regular, Red,
                    10f, 17.0f, 64.6f, 7.6f, TextFormatFlags.HorizontalCenter);
                DrawText(g, "STUDENT IDENTIFICATION CARD", CardFonts.Muol, 8f, FontStyle.Bold, Muted,
                    10f, 24.8f, 64.6f, 3.6f, TextFormatFlags.HorizontalCenter);
                LineMm(g, Hairline, 8f, 30.2f, 76.6f, 30.2f, 0.2f);

                DrawContacts(g, t, 6.3f, 9.4f, 34.4f, 4.6f, 56f);
                DrawBackQrBlock(g, t, 69.3f, 32.8f, true);
            }
            return bmp;
        }

        // =====================================================================
        // RIBBON — navy ribbon down the left edge (logo, QR, generation)
        // =====================================================================
        private const float RibbonWidthMm = 14f;

        private static Bitmap RenderRibbonFront(Student s, AppSettings settings)
        {
            CardText t = settings.CardText ?? new CardText();
            Bitmap bmp = CreateCanvas();
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Prepare(g);
                DrawRibbon(g);

                // Ribbon: logo, QR on a white tile, generation
                FillEllipseMm(g, Color.White, 7f, 7.4f, 5.0f);
                DrawAsset(g, AssetStore.Get("npic_png.png", true, true), 3.7f, 3.4f, 6.6f, 8.0f, 1f, false);
                FillRoundMm(g, Color.White, 1.4f, 15.6f, 11.2f, 11.2f, 0.8f);
                DrawQr(g, BuildQrText(s, t.QrTemplate), 1.7f, 15.9f, 10.6f);
                DrawText(g, "ជំនាន់ទី", CardFonts.KhmerSiemreap, 6.4f, FontStyle.Regular, BandSubtext,
                    0f, 37.4f, RibbonWidthMm, 3.4f, TextFormatFlags.HorizontalCenter);
                DrawText(g, KhmerText.Generation(s.Generation), CardFonts.KhmerSiemreap, 15f, FontStyle.Bold, Color.White,
                    0f, 41.2f, RibbonWidthMm, 7.6f, TextFormatFlags.HorizontalCenter);

                // Title
                DrawText(g, "ប័ណ្ណសម្គាល់ខ្លួននិស្សិត", CardFonts.Muol, 9f, FontStyle.Regular, Navy,
                    17f, 1.2f, 44f, 5.2f, TextFormatFlags.Left);
                DrawText(g, "STUDENT IDENTIFICATION CARD", CardFonts.Muol, 5.6f, FontStyle.Bold, Red,
                    17f, 6.2f, 44f, 3.2f, TextFormatFlags.Left);

                // Photo top-right, ID + expiry under it
                DrawPhoto(g, FindPhoto(settings.PhotoFolder, s.StudentId), 63.4f, 2.0f, PhotoWidthMm, PhotoHeightMm);
                FrameMm(g, Navy, 63.4f, 2.0f, PhotoWidthMm, PhotoHeightMm, 0.3f);
                DrawIdLine(g, s.StudentId, 61.2f, 24.4f, 22.4f, 3.4f, TextFormatFlags.HorizontalCenter);
                DrawText(g, KhmerText.Expiry(s.ValidUntil), CardFonts.KhmerSiemreap, ExpiryPt, FontStyle.Regular, IdRed,
                    61.2f, 27.8f, 22.4f, 3.2f, TextFormatFlags.HorizontalCenter);

                // Fields
                DrawFieldRows(g, FieldLabels(), FieldValues(s), RowTops(11.6f, 4.1f, 6), 4.1f, 17f, 30.8f, 32.1f, 60.2f, Navy, Navy);

                // Issue date + director
                DrawText(g, t.IssueLine1, CardFonts.KhmerSiemreap, IssuePt, FontStyle.Regular, Blue, 17f, 41.6f, 40f, 2.9f, TextFormatFlags.Left);
                DrawText(g, t.IssueLine2, CardFonts.KhmerSiemreap, IssuePt, FontStyle.Regular, Blue, 17f, 44.6f, 40f, 2.9f, TextFormatFlags.Left);
                DrawDirectorBlock(g, t, 58.4f, 38.0f, 0.9f);
            }
            return bmp;
        }

        private static Bitmap RenderRibbonBack(AppSettings settings)
        {
            CardText t = settings.CardText ?? new CardText();
            Bitmap bmp = CreateCanvas();
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Prepare(g);
                DrawRibbon(g);

                // Ribbon: both logos on white discs, social icons stacked underneath
                FillEllipseMm(g, Color.White, 7f, 8.0f, 5.2f);
                DrawAsset(g, AssetStore.Get("ministry_of_labour_logo.png", true, true), 2.9f, 3.9f, 8.2f, 8.2f, 1f, false);
                FillEllipseMm(g, Color.White, 7f, 21.0f, 5.2f);
                DrawAsset(g, AssetStore.Get("npic_png.png", true, true), 3.6f, 16.6f, 6.8f, 8.8f, 1f, false);
                string[] icons = { "facebook.png", "telegram.png", "youtube.png", "tiktok.png" };
                for (int i = 0; i < icons.Length; i++)
                {
                    FillEllipseMm(g, Color.White, 7f, 32.4f + i * 4.6f, 1.8f);
                    DrawAsset(g, AssetStore.Get(icons[i], true, true), 5.75f, 31.15f + i * 4.6f, 2.5f, 2.5f, 1f, false);
                }

                // Content
                DrawText(g, "ព្រះរាជាណាចក្រកម្ពុជា", CardFonts.MuolLight, 10f, FontStyle.Regular, Blue,
                    17f, 1.6f, 65.6f, 5.2f, TextFormatFlags.HorizontalCenter);
                DrawText(g, "ជាតិ សាសនា ព្រះមហាក្សត្រ", CardFonts.MuolLight, 9f, FontStyle.Regular, Blue,
                    17f, 6.6f, 65.6f, 4.6f, TextFormatFlags.HorizontalCenter);
                DrawOrnament(g, t.OrnamentText, 49.8f, 12.8f, 8.6f);
                DrawText(g, "ប័ណ្ណសម្គាល់ខ្លួននិស្សិត", CardFonts.Muol, 14f, FontStyle.Regular, Red,
                    17f, 16.4f, 65.6f, 7.6f, TextFormatFlags.HorizontalCenter);
                DrawText(g, "STUDENT IDENTIFICATION CARD", CardFonts.Muol, 8f, FontStyle.Bold, Red,
                    17f, 24.2f, 65.6f, 3.6f, TextFormatFlags.HorizontalCenter);

                DrawContacts(g, t, 19.4f, 22.4f, 33.4f, 4.6f, 44.6f);
                DrawBackQrBlock(g, t, 70.0f, 32.4f, false);
            }
            return bmp;
        }

        /// <summary>Navy ribbon down the left edge with a thin red line on its right.</summary>
        private static void DrawRibbon(Graphics g)
        {
            FillMm(g, Navy, 0f, 0f, RibbonWidthMm, CardHeightMm);
            FillMm(g, Red, RibbonWidthMm, 0f, 0.6f, CardHeightMm);
        }

        // =====================================================================
        // SHARED TEMPLATE PIECES
        // =====================================================================
        private static float[] RowTops(float first, float step, int count)
        {
            var rows = new float[count];
            for (int i = 0; i < count; i++) rows[i] = first + i * step;
            return rows;
        }

        /// <summary>Label : value rows. Values shrink (with their Latin parts) when they are wider than valueRight.</summary>
        private static void DrawFieldRows(Graphics g, string[] labels, string[] values, float[] rowTop, float rowH,
            float labelX, float colonX, float valueX, float valueRight, Color labelColor, Color valueColor)
        {
            float labelPt = labelColor == valueColor ? TplFieldPt : TplLabelPt;
            for (int i = 0; i < labels.Length; i++)
            {
                float y = rowTop[i];
                DrawText(g, labels[i], CardFonts.KhmerSiemreap, labelPt, FontStyle.Regular, labelColor, labelX, y, colonX - labelX, rowH, TextFormatFlags.Left);
                DrawText(g, ":", CardFonts.KhmerSiemreap, labelPt, FontStyle.Regular, labelColor, colonX, y, 1.4f, rowH, TextFormatFlags.Left);
                DrawText(g, values[i], CardFonts.KhmerSiemreap, TplFieldPt, FontStyle.Regular, valueColor, valueX, y, valueRight - valueX, rowH, TextFormatFlags.Left);
            }
        }

        /// <summary>
        /// Director title, stamp, signature and name, laid out like the Classic card and scaled by <paramref name="scale"/>.
        /// At scale 1 the block is 27.9 x 14.2 mm, its top-left corner at (x, y).
        /// </summary>
        private static void DrawDirectorBlock(Graphics g, CardText t, float x, float y, float scale)
        {
            float k = scale;
            DrawText(g, t.DirectorTitle, CardFonts.Muol, DirectorTitlePt * k, FontStyle.Regular, Blue,
                x, y, 24.8f * k, 3.8f * k, TextFormatFlags.HorizontalCenter);
            DrawAsset(g, AssetStore.Get("stamp.png", true, true), x + 1.7f * k, y + 0.8f * k, 12.2f * k, 12.2f * k, 1f, false);
            DrawAsset(g, AssetStore.GetFile(t.SignatureFile, true, true), x + 6.5f * k, y + (4.1f + SignatureOffsetYMm) * k,
                13f * k, 5.6f * k, 1f, false);
            DrawText(g, t.DirectorName, CardFonts.Muol, DirectorNamePt * k, FontStyle.Regular, Navy,
                x + 7.9f * k, y + (9.1f + SignatureOffsetYMm) * k, 20f * k, 3.6f * k, TextFormatFlags.HorizontalCenter);
        }

        /// <summary>Address, email and website lines with their icons; iconX is the icon centre.</summary>
        private static void DrawContacts(Graphics g, CardText t, float iconX, float textX, float firstTop, float step, float textW)
        {
            DrawPinIcon(g, iconX, firstTop + 2.0f);
            DrawText(g, t.AddressKh, CardFonts.KhmerSiemreap, ContactPt, FontStyle.Regular, Navy, textX, firstTop, textW, 4.0f, TextFormatFlags.Left);
            DrawMailIcon(g, iconX, firstTop + step + 1.9f);
            DrawText(g, t.Email, CardFonts.KhmerSiemreap, ContactPt, FontStyle.Regular, Navy, textX, firstTop + step, textW, 3.8f, TextFormatFlags.Left);
            DrawGlobeIcon(g, iconX, firstTop + 2 * step + 1.9f);
            DrawText(g, t.Website, CardFonts.KhmerSiemreap, ContactPt, FontStyle.Regular, Navy, textX, firstTop + 2 * step, textW, 3.8f, TextFormatFlags.Left);
        }

        /// <summary>Back QR (generated or the fixed image, as set in Card Text…), "scan here" line and optionally the social icons.</summary>
        private static void DrawBackQrBlock(Graphics g, CardText t, float x, float y, bool socialIcons)
        {
            const float size = 11.3f;
            FillMm(g, Color.White, x, y, size, size);
            string backQr = t.BackQrSource == BackQrSource.Generate ? BuildBackQrText(t) : null;
            Bitmap backQrLogo = backQr != null ? AssetStore.GetFile(t.BackQrLogoFile, false, true) : null;
            if (!TryDrawQr(g, backQr, x, y, size, backQrLogo, t.BackQrLogoPercent))
                DrawAsset(g, AssetStore.Get("qrcode_for_back_card.png", false, true), x + 0.3f, y + 0.3f, size - 0.6f, size - 0.6f, 1f, true);
            DrawText(g, "ស្កេនទីនេះសម្រាប់ព័ត៌មាន", CardFonts.SiemreapGoogle, ScanPt, FontStyle.Regular, Red,
                x - 2.8f, y + size + 0.3f, 16.8f, 2.8f, TextFormatFlags.HorizontalCenter);

            if (!socialIcons) return;
            string[] icons = { "facebook.png", "telegram.png", "youtube.png", "tiktok.png" };
            const float iconSize = 2.5f, iconStep = 2.73f;
            float left = x + (size - (3 * iconStep + iconSize)) / 2f;
            for (int i = 0; i < icons.Length; i++)
                DrawAsset(g, AssetStore.Get(icons[i], true, true), left + i * iconStep, y + size + 3.4f, iconSize, iconSize, 1f, false);
        }

        private static void FillEllipseMm(Graphics g, Color color, float cxMm, float cyMm, float rMm)
        {
            using (var brush = new SolidBrush(color))
                g.FillEllipse(brush, MmToPx(cxMm - rMm), MmToPx(cyMm - rMm), MmToPx(2 * rMm), MmToPx(2 * rMm));
        }

        private static void FillRoundMm(Graphics g, Color color, float x, float y, float w, float h, float rMm)
        {
            using (GraphicsPath path = RoundPathPx(x, y, w, h, rMm))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        private static void FrameRoundMm(Graphics g, Color color, float x, float y, float w, float h, float rMm, float thickMm)
        {
            using (GraphicsPath path = RoundPathPx(x, y, w, h, rMm))
            using (var pen = new Pen(color, MmToPx(thickMm)))
                g.DrawPath(pen, path);
        }

        /// <summary>Frame drawn just outside the box, so it never covers the photo.</summary>
        private static void FrameMm(Graphics g, Color color, float x, float y, float w, float h, float thickMm)
        {
            float half = thickMm / 2f;
            using (var pen = new Pen(color, MmToPx(thickMm)))
                g.DrawRectangle(pen, MmToPx(x - half), MmToPx(y - half), MmToPx(w + thickMm), MmToPx(h + thickMm));
        }

        private static void LineMm(Graphics g, Color color, float x1, float y1, float x2, float y2, float thickMm)
        {
            using (var pen = new Pen(color, MmToPx(thickMm)))
                g.DrawLine(pen, MmToPx(x1), MmToPx(y1), MmToPx(x2), MmToPx(y2));
        }

        private static GraphicsPath RoundPathPx(float x, float y, float w, float h, float rMm)
        {
            float px = MmToPx(x), py = MmToPx(y), pw = MmToPx(w), ph = MmToPx(h);
            float d = Math.Min(MmToPx(2 * rMm), Math.Min(pw, ph));
            var path = new GraphicsPath();
            path.AddArc(px, py, d, d, 180f, 90f);
            path.AddArc(px + pw - d, py, d, d, 270f, 90f);
            path.AddArc(px + pw - d, py + ph - d, d, d, 0f, 90f);
            path.AddArc(px, py + ph - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }
}
