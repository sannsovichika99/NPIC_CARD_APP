using System;
using System.Windows.Forms;

namespace NPIC_CARD_APP
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // A serial number valid for this PC is required to open Card Studio (see LicenseService).
            if (!ActivationForm.EnsureActivated()) return;

            Application.Run(new MainForm());
        }
    }
}
