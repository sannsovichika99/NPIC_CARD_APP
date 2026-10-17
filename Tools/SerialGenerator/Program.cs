using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Card Studio Serial Generator")]
[assembly: AssemblyProduct("Card Studio")]
[assembly: AssemblyVersion("1.0.0.0")]

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
