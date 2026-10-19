using System.Collections.Generic;
using System.Drawing;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// The built-in templates (Classic, Modern, Minimal, Ribbon) rebuilt from Card Designer elements, so one can be copied into
    /// My designs and changed freely. Every position, size, font and colour matches the hard-coded drawing in CardRenderer.cs /
    /// CardRenderer.Templates.cs; data (student fields, Card Text, QR codes, signature, ornament) stays live, exactly as in the
    /// built-in template. Background pieces (bands, ribbons, watermarks) are locked, like in the starter design.
    /// </summary>
    public static class BuiltInDesigns
    {
        private static readonly Color Navy = CardColors.Navy, Blue = CardColors.Blue, Red = CardColors.Red, IdRed = CardColors.IdRed;
        private static readonly Color BandText = CardColors.BandText;
        private static readonly Color Muted = Color.FromArgb(104, 110, 138);
        private static readonly Color Hairline = Color.FromArgb(206, 211, 228);

        // Sizes of the built-in templates (pt)
        private const float FieldPt = 7.1f, TplFieldPt = 6.8f, ExpiryPt = 6.4f, IssuePt = 6.0f, ContactPt = 7.2f, ScanPt = 3.6f;
        private const float PhotoW = CardRenderer.PhotoWidthMm, PhotoH = CardRenderer.PhotoHeightMm;
        private const float W = CardRenderer.CardWidthMm, H = CardRenderer.CardHeightMm;
        private const float FooterTop = 52.6f, RibbonW = 14f;

        private const string Title = "ប័ណ្ណសម្គាល់ខ្លួននិស្សិត";
        private const string TitleEn = "STUDENT IDENTIFICATION CARD";
        private const string Kingdom = "ព្រះរាជាណាចក្រកម្ពុជា";
        private const string Motto = "ជាតិ សាសនា ព្រះមហាក្សត្រ";
        private const string ScanHere = "ស្កេនទីនេះសម្រាប់ព័ត៌មាន";
        private static readonly string[] Labels = { "គោត្តនាម-នាម", "អក្សរឡាតាំង", "ភេទ", "ថ្ងៃខែឆ្នាំកំណើត", "មហាវិទ្យាល័យ", "កម្រិត" };
        private static readonly CardField[] Fields =
            { CardField.NameKhmer, CardField.NameLatin, CardField.Gender, CardField.DateOfBirth, CardField.Faculty, CardField.Level };

        /// <summary>True for the templates that can be copied (every built-in one; not Custom).</summary>
        public static bool CanCopy(CardTemplate template)
        {
            return template != CardTemplate.Custom;
        }

        /// <summary>A new, unsaved design (no id, no name) that looks like the built-in template.</summary>
        public static CustomCardDesign Create(CardTemplate template)
        {
            var d = new CustomCardDesign();
            switch (template)
            {
                case CardTemplate.Modern: ModernFront(d.Front.Elements); ModernBack(d.Back.Elements); break;
                case CardTemplate.Minimal: MinimalFront(d.Front.Elements); MinimalBack(d.Back.Elements); break;
                case CardTemplate.Ribbon: RibbonFront(d.Front.Elements); RibbonBack(d.Back.Elements); break;
                default: ClassicFront(d.Front.Elements); ClassicBack(d.Back.Elements); break;
            }
            return d;
        }

        // =====================================================================
        // CLASSIC
        // =====================================================================
        private static void ClassicFront(List<CardElement> f)
        {
            f.Add(Lock(Img("Shield watermark", CardImageSource.NpicLogo, 28.8f, 6.1f, 24.9f, 40.2f, 13)));

            f.Add(Photo(4.2f, 1.8f, Color.Empty, 0f));
            f.Add(Id(3.5f, 24.9f, 21f, 3.6f, CardTextAlign.Left));
            f.Add(new QrElement { X = 4.85f, Y = 28.9f, Width = 17.3f, Height = 17.3f });
            f.Add(Fld(null, CardField.ExpiryDate, "", 0f, ExpiryPt, IdRed, IdRed, 3.5f, 47.4f, 23f, 3.6f, CardTextAlign.Left));

            float[] rows = { 2f, 6.5f, 11f, 15.5f, 20f, 24.5f };
            for (int i = 0; i < Labels.Length; i++)
            {
                float right = i == 5 ? 68.5f : 83f;   // the last row leaves room for the generation
                FieldElement row = Fld(null, Fields[i], Labels[i], 15.5f, FieldPt, Navy, Navy, 26.2f, rows[i], right - 26.2f, 4.2f, CardTextAlign.Left);
                row.SpaceLeft = 0.6f;
                f.Add(row);
            }
            f.Add(Fld(null, CardField.Generation, "ជំនាន់ទី", 8f, FieldPt, Navy, Navy, 69f, rows[5], 15f, 4.2f, CardTextAlign.Left));

            f.Add(Fld(null, CardField.IssueLine1, "", 0f, IssuePt, Blue, Blue, 34f, 31.3f, 48f, 3.6f, CardTextAlign.Right));
            f.Add(Fld(null, CardField.IssueLine2, "", 0f, IssuePt, Blue, Blue, 34f, 34.3f, 44f, 3.6f, CardTextAlign.Right));

            // Director block: stamp, signature and name sit further left than in the other templates
            f.Add(Fld(null, CardField.DirectorTitle, "", 0f, 6.6f, Blue, Blue, 55f, 37.3f, 24.8f, 3.8f, CardTextAlign.Center, CardFontFamily.KhmerOSMuol));
            f.Add(Img("Stamp", CardImageSource.Stamp, 51.3f, 37.5f, 12.2f, 12.2f));
            f.Add(Img("Signature", CardImageSource.Signature, 57.5f, 41.5f, 13f, 5.6f));
            f.Add(Fld(null, CardField.DirectorName, "", 0f, 6.2f, Navy, Navy, 59f, 46.5f, 20f, 3.6f, CardTextAlign.Center, CardFontFamily.KhmerOSMuol));
        }

        private static void ClassicBack(List<CardElement> b)
        {
            b.Add(Lock(Img("NPIC watermark", CardImageSource.WatermarkPattern, 0f, 0f, W, H)));
            b.Add(Img("Ministry logo", CardImageSource.MinistryLogo, 4.4f, 5.5f, 16.4f, 16.4f));
            b.Add(Img("NPIC logo", CardImageSource.NpicLogo, 68.3f, 5.5f, 11.6f, 17.4f));

            b.Add(Txt("Kingdom", Kingdom, CardFontFamily.KhmerOSMuolLight, 12f, Blue, 26.2f, 2.4f, 33.2f, 5.4f, align: CardTextAlign.Center));
            b.Add(Txt("Motto", Motto, CardFontFamily.KhmerOSMuolLight, 11.5f, Blue, 26.6f, 7.1f, 32.1f, 4.8f, align: CardTextAlign.Center));
            b.Add(Ornament(42.8f, 13.3f, 9.8f));
            b.Add(Txt("Title", Title, CardFontFamily.KhmerOSMuol, 17f, Red, 20.8f, 18.6f, 43.6f, 9f, align: CardTextAlign.Center));
            b.Add(Txt("Title (English)", TitleEn, CardFontFamily.KhmerOSMuol, 9.5f, Red, 20.8f, 28.7f, 43.6f, 3.8f, true, CardTextAlign.Center));

            // Contact lines (the email and website boxes are narrower than the address in Classic)
            b.Add(Icon(CardImageSource.PinIcon, 6.3f, 41.0f - 0.35f));
            b.Add(Fld(null, CardField.Address, "", 0f, ContactPt, Navy, Navy, 9.4f, 39.0f, 54f, 4.0f, CardTextAlign.Left));
            b.Add(Icon(CardImageSource.MailIcon, 6.3f, 45.1f));
            b.Add(Fld(null, CardField.Email, "", 0f, ContactPt, Navy, Navy, 9.4f, 43.2f, 50f, 3.8f, CardTextAlign.Left));
            b.Add(Icon(CardImageSource.GlobeIcon, 6.3f, 49.3f));
            b.Add(Fld(null, CardField.Website, "", 0f, ContactPt, Navy, Navy, 9.4f, 47.4f, 50f, 3.8f, CardTextAlign.Left));

            b.Add(new QrElement { Content = CardQrContent.BackQr, X = 68.25f, Y = 33.8f, Width = 11.3f, Height = 11.3f });
            b.Add(Txt("Scan here", ScanHere, CardFontFamily.Siemreap, ScanPt, Red, 65.5f, 45.4f, 16.8f, 2.8f, align: CardTextAlign.Center));

            // Social icons + map pin, grouped so they move together in Card Designer
            CardImageSource[] icons = { CardImageSource.Facebook, CardImageSource.Telegram, CardImageSource.YouTube, CardImageSource.TikTok, CardImageSource.Maps };
            for (int i = 0; i < icons.Length; i++)
            {
                ImageElement icon = Img(icons[i] == CardImageSource.Maps ? "Map" : null, icons[i], 66.5f + i * 3f, 48f, 2.5f, 2.5f);
                icon.GroupId = "gclassicsocial";
                if (icons[i] == CardImageSource.Maps) icon.RemoveWhite = false;
                b.Add(icon);
            }
        }

        // =====================================================================
        // MODERN
        // =====================================================================
        private static void ModernFront(List<CardElement> f)
        {
            f.Add(Lock(Rect("Header band", 0f, 0f, W, 10.4f, Navy)));
            f.Add(Lock(Rect("Header line", 0f, 10.4f, W, 0.6f, Red)));
            f.Add(Disc("Logo disc", 5.9f, 5.2f, 4.1f));
            f.Add(Img("Logo", CardImageSource.NpicLogo, 3.1f, 1.7f, 5.6f, 7.0f));
            f.Add(Txt("Title", Title, CardFontFamily.KhmerOSMuol, 9f, Color.White, 11.6f, 0.7f, 48f, 5.4f));
            f.Add(Txt("Title (English)", TitleEn, CardFontFamily.KhmerOSMuol, 5.6f, BandText, 11.6f, 5.8f, 48f, 3.4f, true));
            f.Add(new ShapeElement
            {
                Name = "Generation pill", FillColor = Color.Empty, BorderColor = Color.White, BorderMm = 0.2f, CornerMm = 2.6f,
                X = 64.6f, Y = 2.6f, Width = 17.4f, Height = 5.2f
            });
            f.Add(Fld(null, CardField.Generation, "ជំនាន់ទី ", 0f, 7.2f, Color.White, Color.White, 64.6f, 2.6f, 17.4f, 5.2f, CardTextAlign.Center));
            f.Add(Lock(Img("Shield watermark", CardImageSource.NpicLogo, 46f, 13.5f, 22f, 36f, 7)));

            f.Add(Photo(4.4f, 12.8f, Navy, 0.3f));
            f.Add(Id(2.0f, 35.4f, 23.1f, 3.4f, CardTextAlign.Center));
            f.Add(Fld(null, CardField.ExpiryDate, "", 0f, ExpiryPt, IdRed, IdRed, 2.0f, 38.8f, 23.1f, 3.2f, CardTextAlign.Center));

            FieldRows(f, Labels, Fields, 12.6f, 3.6f, 26.6f, 40.4f, 83f, Navy, Navy);

            f.Add(Fld(null, CardField.IssueLine1, "", 0f, IssuePt, Blue, Blue, 26.6f, 34.4f, 56.4f, 2.9f, CardTextAlign.Right));
            f.Add(Fld(null, CardField.IssueLine2, "", 0f, IssuePt, Blue, Blue, 26.6f, 37.2f, 56.4f, 2.9f, CardTextAlign.Right));
            f.Add(new QrElement { X = 27.0f, Y = 39.6f, Width = 12.6f, Height = 12.6f });
            Director(f, 58.6f, 40.6f, 0.85f);

            f.Add(Lock(Rect("Footer", 0f, FooterTop, W, H - FooterTop, Navy)));
            f.Add(Lock(Rect("Footer accent", 0f, FooterTop, 22f, H - FooterTop, Red)));
        }

        private static void ModernBack(List<CardElement> b)
        {
            b.Add(Lock(Rect("Header band", 0f, 0f, W, 13.2f, Navy)));
            b.Add(Lock(Rect("Header line", 0f, 13.2f, W, 0.6f, Red)));
            b.Add(Disc("Ministry disc", 8.0f, 6.6f, 5.4f));
            b.Add(Img("Ministry logo", CardImageSource.MinistryLogo, 3.7f, 2.3f, 8.6f, 8.6f));
            b.Add(Disc("NPIC disc", 76.6f, 6.6f, 5.4f));
            b.Add(Img("NPIC logo", CardImageSource.NpicLogo, 73.2f, 2.0f, 6.8f, 9.2f));
            b.Add(Txt("Kingdom", Kingdom, CardFontFamily.KhmerOSMuolLight, 10f, Color.White, 15f, 1.0f, 54.6f, 5.4f, align: CardTextAlign.Center));
            b.Add(Txt("Motto", Motto, CardFontFamily.KhmerOSMuolLight, 9f, BandText, 15f, 6.6f, 54.6f, 5.0f, align: CardTextAlign.Center));

            b.Add(Txt("Title", Title, CardFontFamily.KhmerOSMuol, 15f, Red, 8f, 15.6f, 68.6f, 8.2f, align: CardTextAlign.Center));
            b.Add(Txt("Title (English)", TitleEn, CardFontFamily.KhmerOSMuol, 8.5f, Red, 8f, 24.2f, 68.6f, 3.8f, true, CardTextAlign.Center));
            b.Add(Ornament(42.3f, 30.0f, 8.6f));

            Contacts(b, 6.3f, 9.4f, 34.0f, 4.3f, 56f);
            BackQrBlock(b, 69.3f, 32.0f, true);

            b.Add(Lock(Rect("Footer", 0f, FooterTop, W, H - FooterTop, Navy)));
            b.Add(Lock(Rect("Footer accent", W - 22f, FooterTop, 22f, H - FooterTop, Red)));
        }

        // =====================================================================
        // MINIMAL
        // =====================================================================
        private static void MinimalFront(List<CardElement> f)
        {
            f.Add(Img("Logo", CardImageSource.NpicLogo, 3.6f, 2.0f, 5.2f, 7.0f));
            f.Add(Txt("Title", Title, CardFontFamily.KhmerOSMuol, 8.4f, Navy, 10.2f, 1.4f, 46f, 4.8f));
            f.Add(Txt("Title (English)", TitleEn, CardFontFamily.KhmerOSMuol, 5.2f, Muted, 10.2f, 6.0f, 46f, 3.0f, true));
            f.Add(Fld(null, CardField.Generation, "ជំនាន់ទី: ", 0f, TplFieldPt, Navy, Navy, 58f, 3.0f, 23.4f, 3.6f, CardTextAlign.Right));
            f.Add(Line("Header line", 3.6f, 81.4f, 10.0f, Hairline));

            f.Add(Photo(62.7f, 12.0f, Hairline, 0.2f));
            f.Add(Id(59.6f, 34.4f, 24.8f, 3.4f, CardTextAlign.Center));

            f.Add(Fld("Name", CardField.NameKhmer, "", 0f, 10.5f, Navy, Navy, 3.6f, 11.4f, 56f, 6.0f, CardTextAlign.Left, CardFontFamily.KhmerOSMuolLight));
            FieldElement latin = Fld("Name (Latin)", CardField.NameLatin, "", 0f, 8f, IdRed, IdRed, 3.6f, 17.4f, 56f, 4.0f, CardTextAlign.Left);
            latin.Bold = true;
            f.Add(latin);

            FieldRows(f, new[] { Labels[2], Labels[3], Labels[4], Labels[5] }, new[] { Fields[2], Fields[3], Fields[4], Fields[5] },
                22.2f, 3.7f, 3.6f, 18.4f, 59f, Muted, Navy);

            f.Add(Line("Divider", 3.6f, 57f, 37.6f, Hairline));
            f.Add(new QrElement { X = 3.0f, Y = 39.4f, Width = 12.6f, Height = 12.6f });
            f.Add(Fld(null, CardField.ExpiryDate, "", 0f, ExpiryPt, IdRed, IdRed, 16.6f, 39.6f, 40f, 3.4f, CardTextAlign.Left));
            f.Add(Fld(null, CardField.IssueLine1, "", 0f, IssuePt, Blue, Blue, 16.6f, 44.2f, 40.4f, 2.9f, CardTextAlign.Left));
            f.Add(Fld(null, CardField.IssueLine2, "", 0f, IssuePt, Blue, Blue, 16.6f, 47.2f, 40.4f, 2.9f, CardTextAlign.Left));
            Director(f, 58.4f, 39.0f, 0.9f);
        }

        private static void MinimalBack(List<CardElement> b)
        {
            b.Add(Img("Ministry logo", CardImageSource.MinistryLogo, 4.4f, 3.6f, 13.4f, 13.4f));
            b.Add(Img("NPIC logo", CardImageSource.NpicLogo, 69.8f, 3.2f, 10.4f, 14.4f));
            b.Add(Txt("Kingdom", Kingdom, CardFontFamily.KhmerOSMuolLight, 10.5f, Navy, 20f, 2.0f, 44.6f, 5.2f, align: CardTextAlign.Center));
            b.Add(Txt("Motto", Motto, CardFontFamily.KhmerOSMuolLight, 9.5f, Navy, 20f, 7.0f, 44.6f, 4.6f, align: CardTextAlign.Center));
            b.Add(Ornament(42.3f, 13.2f, 8.6f));

            b.Add(Txt("Title", Title, CardFontFamily.KhmerOSMuol, 14f, Red, 10f, 17.0f, 64.6f, 7.6f, align: CardTextAlign.Center));
            b.Add(Txt("Title (English)", TitleEn, CardFontFamily.KhmerOSMuol, 8f, Muted, 10f, 24.8f, 64.6f, 3.6f, true, CardTextAlign.Center));
            b.Add(Line("Divider", 8f, 76.6f, 30.2f, Hairline));

            Contacts(b, 6.3f, 9.4f, 34.4f, 4.6f, 56f);
            BackQrBlock(b, 69.3f, 32.8f, true);
        }

        // =====================================================================
        // RIBBON
        // =====================================================================
        private static void RibbonFront(List<CardElement> f)
        {
            Ribbon(f);
            f.Add(Disc("Logo disc", 7f, 7.4f, 5.0f));
            f.Add(Img("Logo", CardImageSource.NpicLogo, 3.7f, 3.4f, 6.6f, 8.0f));
            f.Add(new ShapeElement { Name = "QR tile", FillColor = Color.White, CornerMm = 0.8f, X = 1.4f, Y = 15.6f, Width = 11.2f, Height = 11.2f });
            f.Add(new QrElement { X = 1.7f, Y = 15.9f, Width = 10.6f, Height = 10.6f });
            f.Add(Txt("Generation label", "ជំនាន់ទី", CardFontFamily.KhmerOSSiemreap, 6.4f, BandText, 0f, 37.4f, RibbonW, 3.4f, align: CardTextAlign.Center));
            FieldElement gen = Fld(null, CardField.Generation, "", 0f, 15f, Color.White, Color.White, 0f, 41.2f, RibbonW, 7.6f, CardTextAlign.Center);
            gen.Bold = true;
            f.Add(gen);

            f.Add(Txt("Title", Title, CardFontFamily.KhmerOSMuol, 9f, Navy, 17f, 1.2f, 44f, 5.2f));
            f.Add(Txt("Title (English)", TitleEn, CardFontFamily.KhmerOSMuol, 5.6f, Red, 17f, 6.2f, 44f, 3.2f, true));

            f.Add(Photo(63.4f, 2.0f, Navy, 0.3f));
            f.Add(Id(61.2f, 24.4f, 22.4f, 3.4f, CardTextAlign.Center));
            f.Add(Fld(null, CardField.ExpiryDate, "", 0f, ExpiryPt, IdRed, IdRed, 61.2f, 27.8f, 22.4f, 3.2f, CardTextAlign.Center));

            FieldRows(f, Labels, Fields, 11.6f, 4.1f, 17f, 30.8f, 60.2f, Navy, Navy);

            f.Add(Fld(null, CardField.IssueLine1, "", 0f, IssuePt, Blue, Blue, 17f, 41.6f, 40f, 2.9f, CardTextAlign.Left));
            f.Add(Fld(null, CardField.IssueLine2, "", 0f, IssuePt, Blue, Blue, 17f, 44.6f, 40f, 2.9f, CardTextAlign.Left));
            Director(f, 58.4f, 38.0f, 0.9f);
        }

        private static void RibbonBack(List<CardElement> b)
        {
            Ribbon(b);
            b.Add(Disc("Ministry disc", 7f, 8.0f, 5.2f));
            b.Add(Img("Ministry logo", CardImageSource.MinistryLogo, 2.9f, 3.9f, 8.2f, 8.2f));
            b.Add(Disc("NPIC disc", 7f, 21.0f, 5.2f));
            b.Add(Img("NPIC logo", CardImageSource.NpicLogo, 3.6f, 16.6f, 6.8f, 8.8f));
            CardImageSource[] icons = { CardImageSource.Facebook, CardImageSource.Telegram, CardImageSource.YouTube, CardImageSource.TikTok };
            for (int i = 0; i < icons.Length; i++)
            {
                b.Add(Disc(icons[i] + " disc", 7f, 32.4f + i * 4.6f, 1.8f));
                b.Add(Img(null, icons[i], 5.75f, 31.15f + i * 4.6f, 2.5f, 2.5f));
            }

            b.Add(Txt("Kingdom", Kingdom, CardFontFamily.KhmerOSMuolLight, 10f, Blue, 17f, 1.6f, 65.6f, 5.2f, align: CardTextAlign.Center));
            b.Add(Txt("Motto", Motto, CardFontFamily.KhmerOSMuolLight, 9f, Blue, 17f, 6.6f, 65.6f, 4.6f, align: CardTextAlign.Center));
            b.Add(Ornament(49.8f, 12.8f, 8.6f));
            b.Add(Txt("Title", Title, CardFontFamily.KhmerOSMuol, 14f, Red, 17f, 16.4f, 65.6f, 7.6f, align: CardTextAlign.Center));
            b.Add(Txt("Title (English)", TitleEn, CardFontFamily.KhmerOSMuol, 8f, Red, 17f, 24.2f, 65.6f, 3.6f, true, CardTextAlign.Center));

            Contacts(b, 19.4f, 22.4f, 33.4f, 4.6f, 44.6f);
            BackQrBlock(b, 70.0f, 32.4f, false);
        }

        private static void Ribbon(List<CardElement> list)
        {
            list.Add(Lock(Rect("Ribbon", 0f, 0f, RibbonW, H, Navy)));
            list.Add(Lock(Rect("Ribbon line", RibbonW, 0f, 0.6f, H, Red)));
        }

        // =====================================================================
        // SHARED PIECES (same arrangement as the helpers in CardRenderer.Templates.cs)
        // =====================================================================

        /// <summary>Label : value rows; the label gets a column (colonX - labelX) so the colons line up.</summary>
        private static void FieldRows(List<CardElement> list, string[] labels, CardField[] fields, float firstTop, float rowH,
            float labelX, float colonX, float valueRight, Color labelColor, Color valueColor)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                FieldElement row = Fld(null, fields[i], labels[i], colonX - labelX, TplFieldPt, labelColor, valueColor,
                    labelX, firstTop + i * rowH, valueRight - labelX, rowH, CardTextAlign.Left);
                if (labelColor != valueColor) row.LabelSizePt = 6.4f;   // muted labels are a little smaller (Minimal)
                list.Add(row);
            }
        }

        /// <summary>Director title, stamp, signature and name; at scale 1 the block's top-left corner is (x, y).</summary>
        private static void Director(List<CardElement> list, float x, float y, float k)
        {
            list.Add(Fld(null, CardField.DirectorTitle, "", 0f, 6.6f * k, Blue, Blue, x, y, 24.8f * k, 3.8f * k, CardTextAlign.Center, CardFontFamily.KhmerOSMuol));
            list.Add(Img("Stamp", CardImageSource.Stamp, x + 1.7f * k, y + 0.8f * k, 12.2f * k, 12.2f * k));
            list.Add(Img("Signature", CardImageSource.Signature, x + 6.5f * k, y + 5.6f * k, 13f * k, 5.6f * k));
            list.Add(Fld(null, CardField.DirectorName, "", 0f, 6.2f * k, Navy, Navy, x + 7.9f * k, y + 10.6f * k, 20f * k, 3.6f * k, CardTextAlign.Center, CardFontFamily.KhmerOSMuol));
        }

        /// <summary>Address, email and website with their icons; iconX is the icon centre.</summary>
        private static void Contacts(List<CardElement> list, float iconX, float textX, float top, float step, float textW)
        {
            list.Add(Icon(CardImageSource.PinIcon, iconX, top + 2.0f - 0.35f));   // the pin is drawn 0.35 mm below the box centre
            list.Add(Fld(null, CardField.Address, "", 0f, ContactPt, Navy, Navy, textX, top, textW, 4.0f, CardTextAlign.Left));
            list.Add(Icon(CardImageSource.MailIcon, iconX, top + step + 1.9f));
            list.Add(Fld(null, CardField.Email, "", 0f, ContactPt, Navy, Navy, textX, top + step, textW, 3.8f, CardTextAlign.Left));
            list.Add(Icon(CardImageSource.GlobeIcon, iconX, top + 2 * step + 1.9f));
            list.Add(Fld(null, CardField.Website, "", 0f, ContactPt, Navy, Navy, textX, top + 2 * step, textW, 3.8f, CardTextAlign.Left));
        }

        /// <summary>Back QR (Card Text), "scan here" line and optionally the social icons under it.</summary>
        private static void BackQrBlock(List<CardElement> list, float x, float y, bool socialIcons)
        {
            const float size = 11.3f;
            list.Add(new QrElement { Content = CardQrContent.BackQr, X = x, Y = y, Width = size, Height = size });
            list.Add(Txt("Scan here", ScanHere, CardFontFamily.Siemreap, ScanPt, Red, x - 2.8f, y + size + 0.3f, 16.8f, 2.8f, align: CardTextAlign.Center));
            if (socialIcons) SocialRow(list, x + (size - (3 * 2.73f + 2.5f)) / 2f, y + size + 3.4f);
        }

        private static void SocialRow(List<CardElement> list, float left, float top)
        {
            CardImageSource[] icons = { CardImageSource.Facebook, CardImageSource.Telegram, CardImageSource.YouTube, CardImageSource.TikTok };
            for (int i = 0; i < icons.Length; i++) list.Add(Img(null, icons[i], left + i * 2.73f, top, 2.5f, 2.5f));
        }

        // ---- Element factories ----

        private static T Lock<T>(T e) where T : CardElement
        {
            e.Locked = true;
            return e;
        }

        private static ShapeElement Rect(string name, float x, float y, float w, float h, Color fill)
        {
            return new ShapeElement { Name = name, FillColor = fill, X = x, Y = y, Width = w, Height = h };
        }

        /// <summary>White disc behind a logo, given by its centre and radius.</summary>
        private static ShapeElement Disc(string name, float cx, float cy, float r)
        {
            return new ShapeElement { Name = name, Shape = CardShapeKind.Ellipse, FillColor = Color.White, X = cx - r, Y = cy - r, Width = 2 * r, Height = 2 * r };
        }

        /// <summary>Horizontal hairline from x1 to x2 at height y, 0.2 mm thick.</summary>
        private static ShapeElement Line(string name, float x1, float x2, float y, Color color)
        {
            return new ShapeElement
            {
                Name = name, Shape = CardShapeKind.Line, FillColor = Color.Empty, BorderColor = color, BorderMm = 0.2f,
                X = x1, Y = y - 0.5f, Width = x2 - x1, Height = 1f
            };
        }

        private static PhotoElement Photo(float x, float y, Color frame, float frameMm)
        {
            return new PhotoElement { X = x, Y = y, Width = PhotoW, Height = PhotoH, FrameColor = frame, FrameMm = frameMm };
        }

        /// <summary>"អត្តលេខ:" in navy + the ID in red.</summary>
        private static FieldElement Id(float x, float y, float w, float h, CardTextAlign align)
        {
            return Fld("ID", CardField.StudentId, "អត្តលេខ: ", 0f, FieldPt, Navy, IdRed, x, y, w, h, align);
        }

        /// <summary>Drawn icon (pin, mail, globe) given by its centre.</summary>
        private static ImageElement Icon(CardImageSource source, float cx, float cy)
        {
            return Img(null, source, cx - 1f, cy - 1f, 2f, 2f);
        }

        /// <summary>Ornament from Card Text (Tacteing, or the drawn one), given by its centre and half length.</summary>
        private static ImageElement Ornament(float cx, float cy, float halfLength)
        {
            return Img("Ornament", CardImageSource.Ornament, cx - halfLength, cy - 2.5f, 2 * halfLength, 5f);
        }

        private static ImageElement Img(string name, CardImageSource source, float x, float y, float w, float h, int opacity = 100)
        {
            return new ImageElement { Name = name ?? "", Source = source, Opacity = opacity, X = x, Y = y, Width = w, Height = h };
        }

        private static TextElement Txt(string name, string text, CardFontFamily font, float pt, Color color,
            float x, float y, float w, float h, bool bold = false, CardTextAlign align = CardTextAlign.Left)
        {
            return new TextElement { Name = name, Text = text, Font = font, SizePt = pt, Color = color, Bold = bold, Align = align, X = x, Y = y, Width = w, Height = h };
        }

        private static FieldElement Fld(string name, CardField field, string label, float labelWidth, float pt, Color labelColor, Color color,
            float x, float y, float w, float h, CardTextAlign align, CardFontFamily font = CardFontFamily.KhmerOSSiemreap)
        {
            return new FieldElement
            {
                Name = name ?? "", Field = field, Label = label, LabelWidth = labelWidth, SizePt = pt, LabelColor = labelColor, Color = color,
                Font = font, Align = align, X = x, Y = y, Width = w, Height = h
            };
        }
    }
}
