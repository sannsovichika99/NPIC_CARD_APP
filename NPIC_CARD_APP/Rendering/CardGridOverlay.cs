using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// Millimetre layout grid drawn over an on-screen card preview (never printed):
    ///   fine line every 1 mm (only when the card is big enough to read it),
    ///   medium line every 5 mm, strong line every 10 mm with its mm label on the top / left edge,
    ///   dashed centre lines.
    /// The card is assumed to be CR80 (85.6 × 54 mm); portrait or landscape is taken from the rectangle.
    /// </summary>
    public static class CardGridOverlay
    {
        private static readonly Color Fine   = Color.FromArgb(45, 0, 140, 255);
        private static readonly Color Medium = Color.FromArgb(95, 0, 120, 235);
        private static readonly Color Major  = Color.FromArgb(150, 0, 95, 210);
        private static readonly Color Centre = Color.FromArgb(190, 230, 40, 90);
        private static readonly Color LabelText = Color.FromArgb(230, 0, 70, 170);
        private static readonly Color LabelBack = Color.FromArgb(185, 255, 255, 255);

        private const float MinFinePxPerMm = 6f;    // below this, 1 mm lines turn into noise
        private const float MinMediumPxPerMm = 2.5f;
        private const float MinLabelPxPerMm = 4f;

        /// <summary>Draws the grid over <paramref name="card"/> (pixels). Clip to the card's rounded corners first if wanted.</summary>
        public static void Draw(Graphics g, RectangleF card)
        {
            if (card.Width < 8 || card.Height < 8) return;

            bool portrait = card.Height > card.Width;
            float widthMm = portrait ? CardRenderer.CardHeightMm : CardRenderer.CardWidthMm;
            float heightMm = portrait ? CardRenderer.CardWidthMm : CardRenderer.CardHeightMm;
            float pxPerMm = card.Width / widthMm;

            SmoothingMode oldSmoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;   // crisp 1 px lines

            using (var fine = new Pen(Fine, 1f))
            using (var medium = new Pen(Medium, 1f))
            using (var major = new Pen(Major, pxPerMm >= MinFinePxPerMm ? 1.5f : 1f))
            {
                for (int mm = 1; mm < widthMm; mm++)
                {
                    Pen pen = PenFor(mm, pxPerMm, fine, medium, major);
                    if (pen == null) continue;
                    float x = card.X + mm * pxPerMm;
                    g.DrawLine(pen, x, card.Top, x, card.Bottom);
                }
                for (int mm = 1; mm < heightMm; mm++)
                {
                    Pen pen = PenFor(mm, pxPerMm, fine, medium, major);
                    if (pen == null) continue;
                    float y = card.Y + mm * pxPerMm;
                    g.DrawLine(pen, card.Left, y, card.Right, y);
                }
            }

            using (var centre = new Pen(Centre, 1f) { DashStyle = DashStyle.Dash })
            {
                float cx = card.X + card.Width / 2f, cy = card.Y + card.Height / 2f;
                g.DrawLine(centre, cx, card.Top, cx, card.Bottom);
                g.DrawLine(centre, card.Left, cy, card.Right, cy);
            }

            g.SmoothingMode = oldSmoothing;

            if (pxPerMm >= MinLabelPxPerMm) DrawLabels(g, card, widthMm, heightMm, pxPerMm);
        }

        private static Pen PenFor(int mm, float pxPerMm, Pen fine, Pen medium, Pen major)
        {
            if (mm % 10 == 0) return major;
            if (mm % 5 == 0) return pxPerMm >= MinMediumPxPerMm ? medium : null;
            return pxPerMm >= MinFinePxPerMm ? fine : null;
        }

        /// <summary>mm value of every 10 mm line: along the top edge for X, along the left edge for Y.</summary>
        private static void DrawLabels(Graphics g, RectangleF card, float widthMm, float heightMm, float pxPerMm)
        {
            float fontPx = Math.Max(8f, Math.Min(14f, pxPerMm * 1.6f));
            TextRenderingHint oldHint = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using (var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var text = new SolidBrush(LabelText))
            using (var back = new SolidBrush(LabelBack))
            {
                for (int mm = 10; mm < widthMm; mm += 10)
                    Label(g, mm, font, text, back, new PointF(card.X + mm * pxPerMm + 2f, card.Y + 2f));
                for (int mm = 10; mm < heightMm; mm += 10)
                    Label(g, mm, font, text, back, new PointF(card.X + 2f, card.Y + mm * pxPerMm + 1f));
            }
            g.TextRenderingHint = oldHint;
        }

        private static void Label(Graphics g, int mm, Font font, Brush text, Brush back, PointF at)
        {
            string s = mm.ToString(CultureInfo.InvariantCulture);
            SizeF size = g.MeasureString(s, font, PointF.Empty, StringFormat.GenericTypographic);
            g.FillRectangle(back, at.X - 1f, at.Y, size.Width + 2f, size.Height);
            g.DrawString(s, font, text, at, StringFormat.GenericTypographic);
        }
    }
}
