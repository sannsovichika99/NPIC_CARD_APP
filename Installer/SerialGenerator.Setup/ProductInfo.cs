using System;
using System.IO;
using System.Reflection;

// The version (shown in Settings > Apps) is in Tools\SerialGenerator\GeneratorVersion.cs, shared with the generator.
[assembly: AssemblyTitle("Card Studio Serial Generator Setup")]
[assembly: AssemblyProduct("Card Studio Serial Generator")]
[assembly: AssemblyCompany("NPIC")]

namespace CardStudio.Setup
{
    /// <summary>
    /// The Serial Generator's identity for the shared setup engine (the .cs files of Installer\CardStudio.Setup, compiled
    /// into SerialGenerator-Setup.exe). Same members as Installer\CardStudio.Setup\ProductInfo.cs; only the values differ.
    ///
    /// The private key (serial-private.key) is NOT in this setup: the setup file may be copied around, the key must not be.
    /// On first start the generator asks for it (Open Key…) and remembers where it is.
    /// </summary>
    internal static class ProductInfo
    {
        public const string Name = "Card Studio Serial Generator";
        public const string Publisher = "NPIC";

        /// <summary>File name of this setup, for the command-line help.</summary>
        public const string SetupExeName = "SerialGenerator-Setup.exe";

        /// <summary>Only one copy of this setup at a time (independent of Card Studio's setup).</summary>
        public const string MutexName = @"Global\CardStudio.SerialGenerator.Setup";

        /// <summary>Setup log in %TEMP%.</summary>
        public const string LogFileName = "SerialGenerator-Setup.log";

        /// <summary>Tooltip of the desktop and Start Menu shortcuts.</summary>
        public const string Description = "Make Card Studio serial numbers (administrator tool)";

        /// <summary>The generator needs no card fonts.</summary>
        public static readonly bool InstallFonts = false;

        /// <summary>
        /// Administrator tool: no shortcut on every user's desktop unless ticked (or /Desktop). The Start Menu shortcut is
        /// still made by default.
        /// </summary>
        public static readonly bool DesktopShortcutByDefault = false;

        /// <summary>Extra line on the "installed" page.</summary>
        public const string FinishedNote =
            "First start: click Open Key… and choose your serial-private.key (the backup of Installer\\serial-private.key). " +
            "The generator remembers it. The key is not part of this setup.";

        /// <summary>Shown when updating an existing installation.</summary>
        public const string UpdateKeepsText = "The remembered key location is kept; your key file and serials.csv are not touched.";

        /// <summary>Uninstall: option to delete <see cref="UserDataFolder"/>, and what happened to it.</summary>
        public const string PurgeOptionText = "Also forget the remembered key location";
        public const string PurgeProgressText = "Removing the remembered key location";
        public const string PurgeDoneText =
            "The remembered key location was removed too. Your key file and serials.csv were not touched.";
        /// <summary>{0} = <see cref="UserDataFolder"/>.</summary>
        public const string PurgeKeptText =
            "The remembered key location in {0} was kept. Your key file and serials.csv were not touched.";

        /// <summary>Main executable inside the payload.</summary>
        public const string ExeName = "CardStudio.SerialGenerator.exe";
        public const string ProcessName = "CardStudio.SerialGenerator";

        /// <summary>Copy of this setup exe placed in the install folder; it runs the uninstall.</summary>
        public const string UninstallerName = "uninstall.exe";

        /// <summary>List of installed files (relative paths), used by upgrade and uninstall.</summary>
        public const string ManifestName = "install.manifest";

        /// <summary>HKLM key read by Settings > Apps.</summary>
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CardStudioSerialGenerator";

        /// <summary>The generator keeps only the path of the key file there (key-path.txt).</summary>
        public const string AppDataFolderName = "CardStudio.SerialGenerator";

        /// <summary>No folder users replace files in.</summary>
        public static readonly string UserEditableFolder = null;

        /// <summary>.NET Framework 4.8.1 "Release" registry value (any OS).</summary>
        public const int MinNetFrameworkRelease = 533320;
        public const string NetFrameworkDownloadUrl = "https://dotnet.microsoft.com/download/dotnet-framework/net481";

        public const string PayloadResource = "CardStudio.Setup.payload.zip";
        public const string LogoResource = "CardStudio.Setup.logo.png";

        public static string Version
        {
            get
            {
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

        /// <summary>Own Start Menu folder: uninstalling Card Studio never removes these shortcuts, and the other way round.</summary>
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
