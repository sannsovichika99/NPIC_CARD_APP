using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Card Studio Serial Generator")]
[assembly: AssemblyProduct("Card Studio")]
// Version: GeneratorVersion.cs (shared with its setup, Installer\SerialGenerator.Setup).

namespace CardStudio.SerialGenerator
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new GeneratorForm());
        }
    }
}
