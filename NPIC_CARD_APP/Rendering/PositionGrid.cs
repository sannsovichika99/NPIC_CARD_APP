using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// The "Find Position" pattern: 10 mm squares, each labelled with its page position
    /// (red X on top, blue Y below), and a 1 mm tick on every 10 mm line (long tick = 5 mm).
    /// Whatever part of the page lands on a card tells where the card sits:
    /// Slot X = square X − mm from the card's left edge, Slot Y = square Y − mm from the top edge.
    /// Drawn in world units of millimetres, so the same code prints the card and draws the on-screen example.
    /// </summary>
    public static class PositionGrid
    {
        public const int Cell = 10;

        private static readonly Color CellA = Color.FromArgb(255, 238, 160);   // light yellow
        private static readonly Color CellB = Color.FromArgb(185, 225, 255);   // light blue
        public static readonly Color XColor = Color.FromArgb(190, 0, 0);
        public static readonly Color YColor = Color.FromArgb(0, 50, 165);

        private const float LineMm = 0.25f;      // 10 mm grid line
        private const float TickMm = 0.1f;       // 1 mm tick
        private const float ShortTick = 1.0f;
        private const float LongTick = 2.0f;     // the 5 mm tick
        private const float LabelMm = 1.9f;      // ≈ 5.4 pt

        /// <summary>Draws the pattern over areaMm (world units must be millimetres).</summary>
        public static void Draw(Graphics g, RectangleF areaMm)
        {
            int left = Floor10(areaMm.Left), top = Floor10(areaMm.Top);
            int right = (int)Math.Ceiling(areaMm.Right), bottom = (int)Math.Ceiling(areaMm.Bottom);

            TextRenderingHint oldHint = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using (var cellA = new SolidBrush(CellA))
            using (var cellB = new SolidBrush(CellB))
            using (var line = new Pen(Color.Black, LineMm))
            using (var tick = new Pen(Color.Black, TickMm))
            using (var font = new Font("Arial", LabelMm, FontStyle.Bold, GraphicsUnit.World))
            using (var xBrush = new SolidBrush(XColor))
            using (var yBrush = new SolidBrush(YColor))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int y = top; y < bottom; y += Cell)
                    for (int x = left; x < right; x += Cell)
                        g.FillRectangle(((x + y) / Cell) % 2 == 0 ? cellA : cellB, x, y, Cell, Cell);

                // 1 mm ticks: down from every horizontal line, right from every vertical line
                for (int y = top; y <= bottom; y += Cell)
                    for (int x = left; x < right; x += Cell)
                        for (int i = 1; i < Cell; i++)
                        {
                            float len = i == 5 ? LongTick : ShortTick;
                            g.DrawLine(tick, x + i, y, x + i, y + len);
                        }
                for (int x = left; x <= right; x += Cell)
                    for (int y = top; y < bottom; y += Cell)
                        for (int i = 1; i < Cell; i++)
                        {
                            float len = i == 5 ? LongTick : ShortTick;
                            g.DrawLine(tick, x, y + i, x + len, y + i);
                        }

                for (int x = left; x <= right; x += Cell) g.DrawLine(line, x, top, x, bottom);
                for (int y = top; y <= bottom; y += Cell) g.DrawLine(line, left, y, right, y);

                // Labels in the free part of the square (ticks use the top 2 mm and left 2 mm)
                for (int y = top; y < bottom; y += Cell)
                    for (int x = left; x < right; x += Cell)
                    {
                        g.DrawString("X " + x.ToString(CultureInfo.InvariantCulture), font, xBrush,
                            new RectangleF(x + 1.5f, y + 2.4f, Cell - 1.5f, 3.2f), center);
                        g.DrawString("Y " + y.ToString(CultureInfo.InvariantCulture), font, yBrush,
                            new RectangleF(x + 1.5f, y + 5.6f, Cell - 1.5f, 3.2f), center);
                    }
            }
            g.TextRenderingHint = oldHint;
        }

        /// <summary>The square line at or after positionMm (where a reading of the slot position should start).</summary>
        public static int Ceil10(float positionMm)
        {
            return (int)Math.Ceiling(Math.Round(positionMm, 2) / Cell) * Cell;
        }

        private static int Floor10(float positionMm)
        {
            return (int)Math.Floor(positionMm / Cell) * Cell;
        }
    }
}
