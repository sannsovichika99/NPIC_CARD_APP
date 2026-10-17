using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CardStudio.Setup
{
    internal static class ExitCodes
    {
        public const int Success = 0;
        public const int Failed = 1;
        public const int BadArguments = 2;
        public const int Cancelled = 3;
        public const int MissingNetFramework = 4;
        public const int AppRunning = 5;
        public const int NotInstalled = 6;
        public const int AlreadyRunning = 7;
    }

    /// <summary>
    /// One exe, two roles: CardStudio-Setup.exe installs; the copy saved as uninstall.exe in the install
    /// folder is started with /uninstall by Settings > Apps or the Start Menu.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            SetupOptions options;
            try { options = SetupOptions.Parse(args); }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message + Environment.NewLine + Environment.NewLine +
                                "CardStudio-Setup.exe [/S] [/D=folder] [/NoDesktop] [/NoStartMenu] [/WaitForApp]" + Environment.NewLine +
                                "uninstall.exe /uninstall [/S] [/purge]",
                                ProductInfo.Name + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return ExitCodes.BadArguments;
            }

            // Never hold the install folder open as our current directory (it blocks deleting it).
            Environment.CurrentDirectory = Path.GetTempPath();

            bool firstInstance;
            using (new Mutex(true, @"Global\CardStudio.Setup", out firstInstance))
            {
                if (!firstInstance)
                {
                    if (!options.Silent)
                        MessageBox.Show("Card Studio Setup is already running.", ProductInfo.Name + " Setup",
                                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return ExitCodes.AlreadyRunning;
                }

                try
                {
                    return options.Uninstall ? RunUninstall(options) : RunInstall(options);
                }
                catch (Exception ex)
                {
                    Log.Write("Unexpected error: " + ex);
                    if (!options.Silent)
                        MessageBox.Show(ex.Message, ProductInfo.Name + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return ExitCodes.Failed;
                }
            }
        }

        private static int RunInstall(SetupOptions o)
        {
            if (!InstallEngine.IsNetFrameworkInstalled())
            {
                Log.Write(".NET Framework 4.8.1 not found");
                if (!o.Silent &&
                    MessageBox.Show(ProductInfo.Name + " needs Microsoft .NET Framework 4.8.1, which is not installed on this PC." +
                                    Environment.NewLine + Environment.NewLine +
                                    "Open the download page now? Run this setup again after installing it.",
                                    ProductInfo.Name + " Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    Process.Start(ProductInfo.NetFrameworkDownloadUrl);
                return ExitCodes.MissingNetFramework;
            }

            // Started by the app's Check for Updates, which closes right after: give it time to exit before
            // the "Card Studio is running" check below.
            if (o.WaitForApp) InstallEngine.WaitForAppToClose(TimeSpan.FromSeconds(30));

            if (!o.Silent)
            {
                using (var form = new SetupForm(o))
                {
                    Application.Run(form);
                    return form.ExitCode;
                }
            }

            o.InstallDir = o.InstallDir ?? InstallEngine.ExistingInstallDir() ?? ProductInfo.DefaultInstallDir;
            if (InstallEngine.IsAppRunning()) { Log.Write("App is running, silent install stopped"); return ExitCodes.AppRunning; }
            InstallEngine.Install(o, null);
            return ExitCodes.Success;
        }

        private static int RunUninstall(SetupOptions o)
        {
            string dir = UninstallEngine.FindInstallDir();
            if (dir == null)
            {
                if (!o.Silent)
                    MessageBox.Show(ProductInfo.Name + " is not installed on this PC.", "Uninstall " + ProductInfo.Name,
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return ExitCodes.NotInstalled;
            }

            if (!o.Silent)
            {
                using (var form = new UninstallForm(dir, o.Purge))
                {
                    Application.Run(form);
                    return form.ExitCode;
                }
            }

            if (InstallEngine.IsAppRunning()) { Log.Write("App is running, silent uninstall stopped"); return ExitCodes.AppRunning; }
            UninstallEngine.Uninstall(dir, o.Purge, null);
            return ExitCodes.Success;
        }
    }
}
