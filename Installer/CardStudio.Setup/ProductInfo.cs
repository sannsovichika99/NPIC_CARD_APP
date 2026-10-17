using System;
using System.IO;
using System.Reflection;

// The version (shown in Settings > Apps) is in <repo>\Version.cs, shared with the app.
[assembly: AssemblyTitle("Card Studio Setup")]
[assembly: AssemblyProduct("Card Studio")]
[assembly: AssemblyCompany("NPIC")]

namespace CardStudio.Setup
{
    /// <summary>Everything that identifies the product being installed. Change values here, not in the engines.</summary>
    internal static class ProductInfo
    {
        public const string Name = "Card Studio";
        public const string Publisher = "NPIC";

        /// <summary>Main executable inside the payload.</summary>
        public const string ExeName = "NPIC_CARD_APP.exe";
        public const string ProcessName = "NPIC_CARD_APP";

        /// <summary>Copy of this setup exe placed in the install folder; it runs the uninstall.</summary>
        public const string UninstallerName = "uninstall.exe";

        /// <summary>List of installed files (relative paths), used by upgrade and uninstall.</summary>
        public const string ManifestName = "install.manifest";

        /// <summary>HKLM key read by Settings > Apps.</summary>
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CardStudio";

        /// <summary>The app keeps settings, print history and uploads in %AppData%\NPIC_CARD_APP.</summary>
        public const string AppDataFolderName = "NPIC_CARD_APP";

        /// <summary>Card images the user may replace. Existing files here are kept when upgrading.</summary>
        public const string UserEditableFolder = "Assets";

        /// <summary>.NET Framework 4.8.1 "Release" registry value (any OS).</summary>
        public const int MinNetFrameworkRelease = 533320;
        public const string NetFrameworkDownloadUrl = "https://dotnet.microsoft.com/download/dotnet-framework/net481";

        public const string PayloadResource = "CardStudio.Setup.payload.zip";
        public const string LogoResource = "CardStudio.Setup.logo.png";

        public static string Version
        {
            get
            {
                // 1.2.0.0 → "1.2.0", 1.0.0.1 → "1.0.0.1": the same text the app shows and the GitHub release tag uses.
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.ToString(v.Revision > 0 ? 4 : 3);
            }
        }

        public static string DefaultInstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Name); }
        }

        public static string DesktopShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), Name + ".lnk"); }
        }

        /// <summary>Start Menu folder holding the app and uninstall shortcuts.</summary>
        public static string StartMenuFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Name); }
        }

        public static string UserDataFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDataFolderName); }
        }
    }
}
