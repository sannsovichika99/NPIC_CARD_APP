using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>
    /// Installs the card fonts that are missing on this PC from Assets\Fonts (next to the .exe).
    /// Per-user install (Windows 10 1809+): copies the file to %LOCALAPPDATA%\Microsoft\Windows\Fonts and registers
    /// it under HKCU, so no administrator rights are needed. The setup normally installs them for all users already;
    /// this covers PCs where that failed, the fonts were removed later, or the app was copied without the setup.
    /// </summary>
    public static class FontInstaller
    {
        /// <summary>Passed to the relaunched process, so a font that still does not show up cannot cause a restart loop.</summary>
        public const string RelaunchedArg = "--fonts-installed";

        private const string UserFontsKey = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

        public static string FontsFolder
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Fonts"); }
        }

        /// <summary>
        /// Call first thing in Main, before any window or Font is created.
        /// Returns true when fonts were installed but this process cannot see them yet: the caller should
        /// exit, because the app has been started again (in a new process the fonts are always visible).
        /// </summary>
        public static bool InstallMissing(string[] args)
        {
            try
            {
                string[] missing = CardFonts.Missing();
                if (missing.Length == 0 || !Directory.Exists(FontsFolder)) return false;

                var installed = new List<string>();
                foreach (KeyValuePair<string, string> font in FontFiles())
                {
                    if (!missing.Contains(font.Key, StringComparer.OrdinalIgnoreCase)) continue;
                    if (InstallForCurrentUser(font.Value, font.Key)) installed.Add(font.Key);
                }
                if (installed.Count == 0) return false;

                IntPtr ignored;
                SendMessageTimeout(HWND_BROADCAST, WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out ignored);

                // GDI+ may keep the font list it read before the install; then only a new process sees the fonts.
                CardFonts.Refresh();
                if (!installed.Any(f => !CardFonts.IsInstalled(f))) return false;
                if (args != null && args.Contains(RelaunchedArg)) return false;   // restarted once already: carry on

                Process.Start(System.Windows.Forms.Application.ExecutablePath, RelaunchedArg);
                return true;
            }
            catch (Exception ex)
            {
                // Never block start-up over fonts: the app falls back to other fonts and MainForm shows a warning.
                Debug.WriteLine("Font install failed: " + ex);
                return false;
            }
        }

        /// <summary>Family name → font file, for every .ttf/.otf in Assets\Fonts (the name is read from the file).</summary>
        private static IEnumerable<KeyValuePair<string, string>> FontFiles()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(FontsFolder))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".ttf" && ext != ".otf") continue;

                string family = null;
                try
                {
                    using (var pfc = new PrivateFontCollection())
                    {
                        pfc.AddFontFile(file);
                        if (pfc.Families.Length > 0) family = pfc.Families[0].Name;
                    }
                }
                catch (Exception ex) { Debug.WriteLine("Unreadable font " + file + ": " + ex.Message); }

                if (family != null && seen.Add(family))
                    yield return new KeyValuePair<string, string>(family, file);
            }
        }

        private static bool InstallForCurrentUser(string source, string family)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                          "Microsoft", "Windows", "Fonts");
                Directory.CreateDirectory(dir);

                string target = Path.Combine(dir, Path.GetFileName(source));
                if (File.Exists(target) && new FileInfo(target).Length != new FileInfo(source).Length)
                    target = Path.Combine(dir, Path.GetFileNameWithoutExtension(source) + "_" +
                                               Guid.NewGuid().ToString("N").Substring(0, 8) + Path.GetExtension(source));
                if (!File.Exists(target)) File.Copy(source, target);

                string kind = Path.GetExtension(source).Equals(".otf", StringComparison.OrdinalIgnoreCase) ? "OpenType" : "TrueType";
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UserFontsKey))
                    key.SetValue(family + " (" + kind + ")", target);

                // Makes the font usable right away (until sign-out; the registry entry loads it at every sign-in).
                if (AddFontResource(target) == 0) return false;

                Debug.WriteLine("Installed font " + family + " → " + target);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Could not install " + family + ": " + ex.Message);
                return false;
            }
        }

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        private const uint WM_FONTCHANGE = 0x001D;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int AddFontResource(string lpFileName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                                                        uint flags, uint timeout, out IntPtr result);
    }
}
