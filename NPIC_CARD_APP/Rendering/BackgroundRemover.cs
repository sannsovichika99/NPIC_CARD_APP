using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// Removes a plain backdrop (white wall, blue/red ID-photo screen, paper) from an image.
    /// The background colour is taken from the image border, then flood-filled inwards from the edges,
    /// so similar colours inside the subject (a white shirt surrounded by a face) are kept.
    /// </summary>
    public static class BackgroundRemover
    {
        /// <summary>Width of the soft edge, in colour distance units, beyond the tolerance.</summary>
        private const int SoftBand = 36;

        /// <summary>Loads an image as 32bpp ARGB, applying the EXIF rotation of phone photos.</summary>
        public static Bitmap Load(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image src = Image.FromStream(fs))
            {
                CardRenderer.ApplyExifOrientation(src);
                var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                bmp.SetResolution(96f, 96f);
                using (Graphics g = Graphics.FromImage(bmp))
                    g.DrawImage(src, 0, 0, src.Width, src.Height);
                return bmp;
            }
        }

        /// <summary>Returns a scaled copy whose longest side is at most <paramref name="maxSide"/> px.</summary>
        public static Bitmap Downscale(Bitmap src, int maxSide)
        {
            float scale = Math.Min(1f, (float)maxSide / Math.Max(src.Width, src.Height));
            int w = Math.Max(1, (int)Math.Round(src.Width * scale));
            int h = Math.Max(1, (int)Math.Round(src.Height * scale));
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }
            return bmp;
        }

        /// <summary>
        /// Returns a new bitmap with the background removed.
        /// </summary>
        /// <param name="tolerance">0–200: how far (RGB distance) a colour may be from the background colour.</param>
        /// <param name="fill">Colour put behind the subject; <see cref="Color.Transparent"/> keeps it see-through.</param>
        public static Bitmap Remove(Bitmap src, int tolerance, Color fill)
        {
            int w = src.Width, h = src.Height;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
                g.DrawImage(src, 0, 0, w, h);

            var rect = new Rectangle(0, 0, w, h);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int stride = data.Stride;
            byte[] px = new byte[stride * h];
            Marshal.Copy(data.Scan0, px, 0, px.Length);

            Color bg = BorderColor(px, w, h, stride);
            int tol2 = tolerance * tolerance;

            // 1. Flood fill from every border pixel close to the background colour.
            var isBg = new bool[w * h];
            var queue = new Queue<int>();
            Action<int, int> seed = (x, y) =>
            {
                int p = y * w + x;
                if (!isBg[p] && Dist2(px, y * stride + x * 4, bg) <= tol2) { isBg[p] = true; queue.Enqueue(p); }
            };
            for (int x = 0; x < w; x++) { seed(x, 0); seed(x, h - 1); }
            for (int y = 0; y < h; y++) { seed(0, y); seed(w - 1, y); }

            while (queue.Count > 0)
            {
                int p = queue.Dequeue();
                int x = p % w, y = p / w;
                if (x > 0) seed(x - 1, y);
                if (x < w - 1) seed(x + 1, y);
                if (y > 0) seed(x, y - 1);
                if (y < h - 1) seed(x, y + 1);
            }

            // 2. Soft edge: pixels within 2 px of the removed area get partial alpha by colour distance,
            //    and the background colour that was blended into them is taken out (no halo).
            bool[] ring = Dilate(Dilate(isBg, w, h), w, h);
            for (int p = 0; p < isBg.Length; p++)
            {
                int i = (p / w) * stride + (p % w) * 4;
                if (isBg[p])
                {
                    px[i + 3] = 0;
                    continue;
                }
                if (!ring[p]) continue;

                double d = Math.Sqrt(Dist2(px, i, bg));
                double a = Math.Min(1.0, Math.Max(0.0, (d - tolerance) / SoftBand));
                if (a >= 1.0) continue;
                if (a <= 0.02) { px[i + 3] = 0; continue; }
                px[i] = Unblend(px[i], bg.B, a);
                px[i + 1] = Unblend(px[i + 1], bg.G, a);
                px[i + 2] = Unblend(px[i + 2], bg.R, a);
                px[i + 3] = (byte)(px[i + 3] * a);
            }

            Marshal.Copy(px, 0, data.Scan0, px.Length);
            bmp.UnlockBits(data);

            if (fill.A == 0) return bmp;

            // Flatten onto a solid colour (e.g. white or ID-photo blue).
            var flat = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(flat))
            {
                g.Clear(fill);
                g.DrawImage(bmp, 0, 0, w, h);
            }
            bmp.Dispose();
            return flat;
        }

        /// <summary>Checkerboard tile used behind transparent previews.</summary>
        public static Bitmap Checkerboard(int cell = 8)
        {
            var bmp = new Bitmap(cell * 2, cell * 2);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var dark = new SolidBrush(Color.FromArgb(220, 222, 228)))
            {
                g.Clear(Color.White);
                g.FillRectangle(dark, 0, 0, cell, cell);
                g.FillRectangle(dark, cell, cell, cell, cell);
            }
            return bmp;
        }

        /// <summary>Most common colour along the image border (quantised), averaged within its bucket.</summary>
        private static Color BorderColor(byte[] px, int w, int h, int stride)
        {
            var count = new Dictionary<int, int>();
            var sum = new Dictionary<int, long[]>();
            Action<int, int> add = (x, y) =>
            {
                int i = y * stride + x * 4;
                int b = px[i], g = px[i + 1], r = px[i + 2];
                int key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                int c;
                count.TryGetValue(key, out c);
                count[key] = c + 1;
                long[] s;
                if (!sum.TryGetValue(key, out s)) sum[key] = s = new long[3];
                s[0] += r; s[1] += g; s[2] += b;
            };
            for (int x = 0; x < w; x++) { add(x, 0); add(x, h - 1); }
            for (int y = 1; y < h - 1; y++) { add(0, y); add(w - 1, y); }

            int best = -1, bestCount = -1;
            foreach (KeyValuePair<int, int> kv in count)
                if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }

            long[] t = sum[best];
            return Color.FromArgb((int)(t[0] / bestCount), (int)(t[1] / bestCount), (int)(t[2] / bestCount));
        }

        private static int Dist2(byte[] px, int i, Color c)
        {
            int db = px[i] - c.B, dg = px[i + 1] - c.G, dr = px[i + 2] - c.R;
            return dr * dr + dg * dg + db * db;
        }

        private static bool[] Dilate(bool[] mask, int w, int h)
        {
            var result = (bool[])mask.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (mask[y * w + x]) continue;
                    if ((x > 0 && mask[y * w + x - 1]) || (x < w - 1 && mask[y * w + x + 1]) ||
                        (y > 0 && mask[(y - 1) * w + x]) || (y < h - 1 && mask[(y + 1) * w + x]))
                        result[y * w + x] = true;
                }
            return result;
        }

        private static byte Unblend(int channel, int bgChannel, double alpha)
        {
            double v = (channel - bgChannel * (1 - alpha)) / alpha;
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v)));
        }
    }
}
