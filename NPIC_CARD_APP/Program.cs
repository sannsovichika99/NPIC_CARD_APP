using System;
using System.Windows.Forms;

namespace NPIC_CARD_APP
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Card fonts missing on this PC are installed from Assets\Fonts before any Font is created.
            // True = installed, but only a new process can use them: the app has been restarted, so stop here.
            if (Rendering.FontInstaller.InstallMissing(args)) return;

            // A serial number valid for this PC is required to open Card Studio (see LicenseService).
            if (!ActivationForm.EnsureActivated()) return;

            Application.Run(new MainForm());
        }
    }
}
