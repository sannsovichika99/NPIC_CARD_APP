using System;

namespace CardStudio.Setup
{
    /// <summary>
    /// Command line:
    ///   CardStudio-Setup.exe                         interactive install
    ///   CardStudio-Setup.exe /S [/D=path]            silent install (both shortcuts unless /NoDesktop, /NoStartMenu)
    ///   uninstall.exe /uninstall [/S] [/purge]       uninstall; /purge also deletes %AppData%\NPIC_CARD_APP
    /// </summary>
    internal sealed class SetupOptions
    {
        public bool Uninstall;
        public bool Silent;
        public bool Purge;
        public string InstallDir;          // null = existing install location or Program Files\Card Studio
        public bool DesktopShortcut = true;
        public bool StartMenuShortcut = true;

        public static SetupOptions Parse(string[] args)
        {
            var o = new SetupOptions();
            foreach (string raw in args)
            {
                string a = raw.Trim();
                if (a.StartsWith("-")) a = "/" + a.Substring(1);

                if (Is(a, "/uninstall") || Is(a, "/u")) o.Uninstall = true;
                else if (Is(a, "/S") || Is(a, "/silent") || Is(a, "/quiet")) o.Silent = true;
                else if (Is(a, "/purge")) o.Purge = true;
                else if (Is(a, "/NoDesktop")) o.DesktopShortcut = false;
                else if (Is(a, "/NoStartMenu")) o.StartMenuShortcut = false;
                else if (a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase)) o.InstallDir = a.Substring(3).Trim('"');
                else throw new ArgumentException("Unknown argument: " + raw);
            }
            return o;
        }

        private static bool Is(string a, string name)
        {
            return string.Equals(a, name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
