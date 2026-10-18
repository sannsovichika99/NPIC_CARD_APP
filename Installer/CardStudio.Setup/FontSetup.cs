using System;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CardStudio.Setup
{
    /// <summary>
    /// Installs the card fonts shipped in &lt;install dir&gt;\Assets\Fonts for all users (C:\Windows\Fonts + HKLM),
    /// skipping any family Windows already has. Fonts are shared with other programs, so uninstall leaves them.
    /// A font that cannot be installed is only logged: the app installs it per user at start-up instead.
    /// </summary>
    internal static class FontSetup
    {
        private const string FontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

        public static void InstallMissing(string installDir)
        {
            string source = Path.Combine(installDir, ProductInfo.UserEditableFolder, "Fonts");
            if (!Directory.Exists(source)) return;

            string winFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            bool changed = false;

            foreach (string file in Directory.GetFiles(source))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".ttf" && ext != ".otf") continue;

                try
                {
                    string family = FamilyName(file);
                    if (family == null) { Log.Write("Font skipped (unreadable): " + file); continue; }

                    using (RegistryKey hklm = InstallEngine.Hklm())
                    using (RegistryKey key = hklm.CreateSubKey(FontsKey))
                    {
                        if (key.GetValueNames().Any(n => n.StartsWith(family + " (", StringComparison.OrdinalIgnoreCase)))
                        {
                            Log.Write("Font already installed: " + family);
                            continue;
                        }

                        string target = Path.Combine(winFonts, Path.GetFileName(file));
                        if (File.Exists(target))     // same file name, other font: never overwrite a system font
                            target = Path.Combine(winFonts, Path.GetFileNameWithoutExtension(file) + "_CardStudio" + Path.GetExtension(file));
                        if (!File.Exists(target)) File.Copy(file, target);

                        string kind = ext == ".otf" ? "OpenType" : "TrueType";
                        key.SetValue(family + " (" + kind + ")", Path.GetFileName(target));
                        AddFontResource(target);
                        changed = true;
                        Log.Write("Font installed: " + family + " -> " + target);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Font install failed for " + file + ": " + ex.Message);
                }
            }

            if (changed)
            {
                IntPtr ignored;
                SendMessageTimeout(new IntPtr(0xFFFF), 0x001D /* WM_FONTCHANGE */, IntPtr.Zero, IntPtr.Zero,
                                   0x0002 /* SMTO_ABORTIFHUNG */, 1000, out ignored);
            }
        }

        private static string FamilyName(string file)
        {
            using (var pfc = new PrivateFontCollection())
            {
                pfc.AddFontFile(file);
                return pfc.Families.Length > 0 ? pfc.Families[0].Name : null;
            }
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int AddFontResource(string lpFileName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                                                        uint flags, uint timeout, out IntPtr result);
    }
}
