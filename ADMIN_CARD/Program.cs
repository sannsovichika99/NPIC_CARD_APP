using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Card Studio Admin")]
[assembly: AssemblyProduct("Card Studio")]
[assembly: AssemblyCompany("NPIC")]
// Version: AdminVersion.cs.

namespace CardStudio.Admin
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new AdminForm());
        }
    }
}
