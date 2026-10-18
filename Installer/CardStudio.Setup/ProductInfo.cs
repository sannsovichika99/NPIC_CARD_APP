using System;
using System.IO;
using System.Reflection;

// The version (shown in Settings > Apps) is in <repo>\Version.cs, shared with the app.
[assembly: AssemblyTitle("Card Studio Setup")]
[assembly: AssemblyProduct("Card Studio")]
[assembly: AssemblyCompany("NPIC")]

namespace CardStudio.Setup
{
    /// <summary>
    /// Everything that identifies the product being installed. Change values here, not in the engines.
    /// The Serial Generator setup (Installer\SerialGenerator.Setup) compiles the same engine files with its own ProductInfo.cs:
    /// keep the two classes' members the same.
    /// </summary>
    internal static class ProductInfo
    {
        public const string Name = "Card Studio";
        public const string Publisher = "NPIC";

        /// <summary>File name of this setup, for the command-line help.</summary>
        public const string SetupExeName = "CardStudio-Setup.exe";

        /// <summary>Only one copy of this setup at a time.</summary>
        public const string MutexName = @"Global\CardStudio.Setup";

        /// <summary>Setup log in %TEMP%.</summary>
        public const string LogFileName = "CardStudio-Setup.log";

        /// <summary>Tooltip of the desktop and Start Menu shortcuts.</summary>
        public const string Description = "Design and print student ID cards";

        /// <summary>Install the fonts in &lt;install dir&gt;\Assets\Fonts for all users.</summary>
        public static readonly bool InstallFonts = true;

        /// <summary>The desktop shortcut option starts ticked (all users' desktop).</summary>
        public static readonly bool DesktopShortcutByDefault = true;

        /// <summary>Extra line on the "installed" page ("" = none).</summary>
        public const string FinishedNote = "";

        /// <summary>Shown when updating an existing installation.</summary>
        public const string UpdateKeepsText = "Your settings, print history and card images in Assets are kept.";

        /// <summary>Uninstall: option to delete <see cref="UserDataFolder"/>, and what happened to it.</summary>
        public const string PurgeOptionText = "Also delete my settings, print history and uploaded images";
        public const string PurgeProgressText = "Removing settings and print history";
        public const string PurgeDoneText = "Your settings and print history were deleted too.";
        /// <summary>{0} = <see cref="UserDataFolder"/>.</summary>
        public const string PurgeKeptText = "Your settings and print history in {0} were kept.";

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

        /// <summary>
        /// Card images the user may replace: BUILTIN\Users get Modify, and existing files here are kept when upgrading.
        /// Null = the product has no such folder.
        /// </summary>
        public static readonly string UserEditableFolder = "Assets";

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
