using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>Draws a side of the custom design made with Card Designer… (the "Custom" card template).</summary>
    public static partial class CardRenderer
    {
        /// <summary>
        /// Draws one side of a custom design. <paramref name="s"/> = null for the back (student fields then print empty).
        /// <paramref name="dpi"/> below <see cref="Dpi"/> gives a faster, smaller bitmap for the designer's live canvas.
        /// </summary>
        public static Bitmap RenderCustomSide(CardSideDesign side, Student s, AppSettings settings, float dpi = Dpi)
        {
            float old = _dpiOverride;
            _dpiOverride = dpi;
            try
            {
                CardText t = settings.CardText ?? new CardText();
                Bitmap bmp = CreateCanvas();
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    Prepare(g);
                    if (side == null) return bmp;
                    if (!side.BackgroundColor.IsEmpty && side.BackgroundColor.ToArgb() != Color.White.ToArgb())
                        g.Clear(Color.FromArgb(255, side.BackgroundColor));

                    foreach (CardElement e in side.Elements)
                    {
                        if (e == null || !e.Visible || e.Width <= 0f || e.Height <= 0f) continue;
                        try
                        {
                            DrawElement(g, e, s, settings, t);
                        }
                        catch (Exception ex) when (ex is ArgumentException || ex is System.IO.IOException || ex is OutOfMemoryException)
                        {
                            // A broken image file or a QR text that is too long must not stop the rest of the card.
                        }
                    }
                }
                return bmp;
            }
            finally
            {
                _dpiOverride = old;
            }
        }

        private static void DrawElement(Graphics g, CardElement e, Student s, AppSettings settings, CardText t)
        {
            var shape = e as ShapeElement;
            if (shape != null) { DrawShape(g, shape); return; }

            var text = e as TextElement;
            if (text != null)
            {
                DrawRuns(g, Runs(text.Text, text, text.Color), text.SizePt, e.X, e.Y, e.Width, e.Height, Flags(text.Align));
                return;
            }

            var field = e as FieldElement;
            if (field != null) { DrawField(g, field, s, t); return; }

            var image = e as ImageElement;
            if (image != null) { DrawImageElement(g, image, t); return; }

            var photo = e as PhotoElement;
            if (photo != null)
            {
                DrawPhoto(g, s == null ? null : FindPhoto(settings.PhotoFolder, s.StudentId), e.X, e.Y, e.Width, e.Height);
                if (photo.FrameMm > 0f && !photo.FrameColor.IsEmpty)
                    FrameMm(g, photo.FrameColor, e.X, e.Y, e.Width, e.Height, photo.FrameMm);
                return;
            }

            var qr = e as QrElement;
            if (qr != null) DrawQrElement(g, qr, s, t);
        }

        private static void DrawShape(Graphics g, ShapeElement e)
        {
            switch (e.Shape)
            {
                case CardShapeKind.Line:
                {
                    Color c = !e.BorderColor.IsEmpty ? e.BorderColor : e.FillColor;
                    if (c.IsEmpty) return;
                    float thick = e.BorderMm > 0f ? e.BorderMm : Math.Max(0.1f, Math.Min(e.Width, e.Height));
                    if (e.Width >= e.Height) LineMm(g, c, e.X, e.Y + e.Height / 2f, e.X + e.Width, e.Y + e.Height / 2f, thick);
                    else LineMm(g, c, e.X + e.Width / 2f, e.Y, e.X + e.Width / 2f, e.Y + e.Height, thick);
                    return;
                }
                case CardShapeKind.Ellipse:
                    if (!e.FillColor.IsEmpty)
                        using (var b = new SolidBrush(e.FillColor))
                            g.FillEllipse(b, MmToPx(e.X), MmToPx(e.Y), MmToPx(e.Width), MmToPx(e.Height));
                    if (!e.BorderColor.IsEmpty && e.BorderMm > 0f)
                        using (var p = new Pen(e.BorderColor, MmToPx(e.BorderMm)))
                            g.DrawEllipse(p, MmToPx(e.X), MmToPx(e.Y), MmToPx(e.Width), MmToPx(e.Height));
                    return;
                default:
                    if (!e.FillColor.IsEmpty)
                    {
                        if (e.CornerMm > 0f) FillRoundMm(g, e.FillColor, e.X, e.Y, e.Width, e.Height, e.CornerMm);
                        else FillMm(g, e.FillColor, e.X, e.Y, e.Width, e.Height);
                    }
                    if (!e.BorderColor.IsEmpty && e.BorderMm > 0f)
                    {
                        if (e.CornerMm > 0f) FrameRoundMm(g, e.BorderColor, e.X, e.Y, e.Width, e.Height, e.CornerMm, e.BorderMm);
                        else
                            using (var p = new Pen(e.BorderColor, MmToPx(e.BorderMm)))
                                g.DrawRectangle(p, MmToPx(e.X), MmToPx(e.Y), MmToPx(e.Width), MmToPx(e.Height));
                    }
                    return;
            }
        }

        private static void DrawField(Graphics g, FieldElement e, Student s, CardText t)
        {
            string value = FieldValue(e.Field, s, t);
            TextFormatFlags align = Flags(e.Align);
            bool hasLabel = !string.IsNullOrEmpty(e.Label);

            if (hasLabel && e.LabelWidth > 0f)
            {
                // Label column, then ": value" — rows with the same label width line up like the built-in cards.
                float colonX = e.X + e.LabelWidth;
                DrawRuns(g, Runs(e.Label, e, e.LabelColor), e.SizePt, e.X, e.Y, e.LabelWidth, e.Height, TextFormatFlags.Left);
                DrawRuns(g, Runs(":", e, e.LabelColor), e.SizePt, colonX, e.Y, 1.4f, e.Height, TextFormatFlags.Left);
                float valueX = colonX + 1.3f;
                DrawRuns(g, Runs(value, e, e.Color), e.SizePt, valueX, e.Y, Math.Max(0.5f, e.X + e.Width - valueX), e.Height, align);
                return;
            }

            List<TextRun> runs = hasLabel ? Runs(e.Label, e, e.LabelColor) : new List<TextRun>();
            runs.AddRange(Runs(value, e, e.Color));
            DrawRuns(g, runs, e.SizePt, e.X, e.Y, e.Width, e.Height, align);
        }

        /// <summary>Text of a field. Student fields are "" when there is no student (back side).</summary>
        public static string FieldValue(CardField field, Student s, CardText t)
        {
            switch (field)
            {
                case CardField.IssueLine1: return t.IssueLine1;
                case CardField.IssueLine2: return t.IssueLine2;
                case CardField.DirectorTitle: return t.DirectorTitle;
                case CardField.DirectorName: return t.DirectorName;
                case CardField.Address: return t.AddressKh;
                case CardField.Email: return t.Email;
                case CardField.Website: return t.Website;
            }
            if (s == null) return "";
            switch (field)
            {
                case CardField.StudentId: return s.StudentId;
                case CardField.NameKhmer: return s.NameKh;
                case CardField.NameLatin: return (s.NameEn ?? "").ToUpperInvariant();
                case CardField.Gender: return KhmerText.Gender(s.Gender);
                case CardField.DateOfBirth: return KhmerText.Date(s.DateOfBirth);
                case CardField.Faculty: return s.Faculty;
                case CardField.Level: return s.Level;
                case CardField.Generation: return KhmerText.Generation(s.Generation);
                case CardField.ExpiryDate: return KhmerText.Expiry(s.ValidUntil);
                default: return "";
            }
        }

        private static void DrawImageElement(Graphics g, ImageElement e, CardText t)
        {
            float cx = e.X + e.Width / 2f, cy = e.Y + e.Height / 2f;
            switch (e.Source)
            {
                case CardImageSource.PinIcon: DrawPinIcon(g, cx, cy + 0.35f); return;
                case CardImageSource.MailIcon: DrawMailIcon(g, cx, cy); return;
                case CardImageSource.GlobeIcon: DrawGlobeIcon(g, cx, cy); return;
            }

            Bitmap img;
            bool nearest = false;
            switch (e.Source)
            {
                case CardImageSource.Signature: img = AssetStore.GetFile(t.SignatureFile, e.RemoveWhite, true); break;
                case CardImageSource.CustomFile: img = AssetStore.GetFile(CardText.ResolveAppPath(e.FilePath), e.RemoveWhite, true); break;
                case CardImageSource.BackQrPicture:
                    img = AssetStore.Get("qrcode_for_back_card.png", false, true);
                    nearest = true;
                    break;
                default: img = AssetStore.Get(AssetFileName(e.Source), e.RemoveWhite, true); break;
            }
            float opacity = Math.Max(0, Math.Min(100, e.Opacity)) / 100f;
            if (opacity > 0f) DrawAsset(g, img, e.X, e.Y, e.Width, e.Height, opacity, nearest);
        }

        private static string AssetFileName(CardImageSource source)
        {
            switch (source)
            {
                case CardImageSource.MinistryLogo: return "ministry_of_labour_logo.png";
                case CardImageSource.Stamp: return "stamp.png";
                case CardImageSource.Facebook: return "facebook.png";
                case CardImageSource.Telegram: return "telegram.png";
                case CardImageSource.YouTube: return "youtube.png";
                case CardImageSource.TikTok: return "tiktok.png";
                default: return "npic_png.png";
            }
        }

        private static void DrawQrElement(Graphics g, QrElement e, Student s, CardText t)
        {
            float size = Math.Min(e.Width, e.Height);
            float x = e.X + (e.Width - size) / 2f, y = e.Y + (e.Height - size) / 2f;
            switch (e.Content)
            {
                case CardQrContent.StudentQr:
                    if (s != null) TryDrawQr(g, BuildQrText(s, t.QrTemplate), x, y, size, null, 0);
                    break;
                case CardQrContent.BackQr:
                    FillMm(g, Color.White, x, y, size, size);
                    string backQr = t.BackQrSource == BackQrSource.Generate ? BuildBackQrText(t) : null;
                    Bitmap logo = backQr != null ? AssetStore.GetFile(t.BackQrLogoFile, false, true) : null;
                    if (!TryDrawQr(g, backQr, x, y, size, logo, t.BackQrLogoPercent))
                        DrawAsset(g, AssetStore.Get("qrcode_for_back_card.png", false, true), x + 0.3f, y + 0.3f, size - 0.6f, size - 0.6f, 1f, true);
                    break;
                default:
                    TryDrawQr(g, (e.CustomText ?? "").Trim(), x, y, size, null, 0);
                    break;
            }
        }

        /// <summary>Text runs in the element's font. The ornament font is used as-is (its "letters" are the ornament shapes).</summary>
        private static List<TextRun> Runs(string text, TextElementBase e, Color color)
        {
            if (string.IsNullOrEmpty(text)) return new List<TextRun>();
            if (color.IsEmpty) color = Color.Black;
            if (e.Font == CardFontFamily.Tacteing)
            {
                string family = CardFonts.Ornament ?? CardFonts.Serif;
                return new List<TextRun> { new TextRun(text, family, FontStyle.Regular, color) };
            }
            return CardFonts.Split(text, FontFamilyName(e.Font), e.Bold ? FontStyle.Bold : FontStyle.Regular, color);
        }

        private static string FontFamilyName(CardFontFamily font)
        {
            switch (font)
            {
                case CardFontFamily.KhmerOSMuol: return CardFonts.Muol;
                case CardFontFamily.KhmerOSMuolLight: return CardFonts.MuolLight;
                case CardFontFamily.Siemreap: return CardFonts.SiemreapGoogle;
                default: return CardFonts.KhmerSiemreap;
            }
        }

        private static TextFormatFlags Flags(CardTextAlign align)
        {
            switch (align)
            {
                case CardTextAlign.Center: return TextFormatFlags.HorizontalCenter;
                case CardTextAlign.Right: return TextFormatFlags.Right;
                default: return TextFormatFlags.Left;
            }
        }
    }
}
