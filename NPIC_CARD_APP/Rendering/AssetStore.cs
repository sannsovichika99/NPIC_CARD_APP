using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// Loads images from the Assets folder (next to the .exe) and caches them.
    /// The PNGs have white backgrounds, so they can be made transparent and trimmed.
    /// Returned bitmaps are shared: do not dispose them.
    /// </summary>
    public static class AssetStore
    {
        public static readonly string[] RequiredFiles =
        {
            "npic_png.png", "ministry_of_labour_logo.png", "stamp.png", "qrcode_for_back_card.png",
            "facebook.png", "telegram.png", "youtube.png", "tiktok.png", "maps.png"
        };

        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static string Folder
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets"); }
        }

        public static string[] MissingFiles()
        {
            return RequiredFiles.Where(f => !File.Exists(Path.Combine(Folder, f))).ToArray();
        }

        public static Bitmap Get(string fileName, bool whiteToTransparent, bool trim)
        {
            return GetFile(Path.Combine(Folder, fileName), whiteToTransparent, trim);
        }

        public static Bitmap GetFile(string path, bool whiteToTransparent, bool trim)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            string key = path + "|" + whiteToTransparent + "|" + trim + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            Bitmap cached;
            if (Cache.TryGetValue(key, out cached)) return cached;

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image src = Image.FromStream(fs))
            {
                Bitmap result = Process(src, whiteToTransparent, trim);
                Cache[key] = result;
                return result;
            }
        }

        private static Bitmap Process(Image src, bool whiteToTransparent, bool trim)
        {
            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
                g.DrawImage(src, 0, 0, src.Width, src.Height);

            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int length = data.Stride * bmp.Height;
            byte[] px = new byte[length];
            Marshal.Copy(data.Scan0, px, 0, length);

            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    int i = y * data.Stride + x * 4;
                    int b = px[i], gr = px[i + 1], r = px[i + 2], a = px[i + 3];
                    int lightest = Math.Min(r, Math.Min(gr, b));

                    if (whiteToTransparent)
                    {
                        // 245+ = fully transparent, 205- = opaque, soft edge in between
                        int alpha = lightest >= 245 ? 0 : lightest <= 205 ? 255 : (245 - lightest) * 255 / 40;
                        if (alpha > 0 && alpha < 255)
                        {
                            // remove the white that was blended into anti-aliased edges
                            float f = alpha / 255f;
                            px[i] = Unblend(b, f);
                            px[i + 1] = Unblend(gr, f);
                            px[i + 2] = Unblend(r, f);
                        }
                        a = a * alpha / 255;
                        px[i + 3] = (byte)a;
                    }

                    if (a > 0 && lightest < 240)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            Marshal.Copy(px, 0, data.Scan0, length);
            bmp.UnlockBits(data);

            if (trim && maxX >= minX && maxY >= minY)
            {
                Bitmap cropped = bmp.Clone(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1), PixelFormat.Format32bppArgb);
                bmp.Dispose();
                return cropped;
            }
            return bmp;
        }

        private static byte Unblend(int channel, float alpha)
        {
            float v = (channel - 255f * (1f - alpha)) / alpha;
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v)));
        }
    }
}
