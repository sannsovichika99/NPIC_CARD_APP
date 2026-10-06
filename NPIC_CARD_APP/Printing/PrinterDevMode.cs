using System;
using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace NPIC_CARD_APP.Printing
{
    /// <summary>
    /// Opens the real Epson driver dialog and saves/restores the full DEVMODE,
    /// including driver-private options such as Paper Type = "PVC ID Card".
    /// (.NET's PrinterSettings cannot set Paper Type by itself.)
    /// </summary>
    internal static class PrinterDevMode
    {
        private const int DM_OUT_BUFFER = 2;
        private const int DM_IN_PROMPT = 4;
        private const int DM_IN_BUFFER = 8;
        private const int IDOK = 1;
        private const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "DocumentPropertiesW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DocumentProperties(IntPtr hWnd, IntPtr hPrinter, string pDeviceName,
            IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        /// <summary>Shows the driver's Printing Preferences. Returns null if the user cancels.</summary>
        public static byte[] ShowDriverDialog(IntPtr owner, PrinterSettings ps)
        {
            IntPtr hPrinter;
            if (!OpenPrinter(ps.PrinterName, out hPrinter, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            IntPtr hDevModeIn = IntPtr.Zero;
            IntPtr pDevModeOut = IntPtr.Zero;
            try
            {
                int size = DocumentProperties(owner, hPrinter, ps.PrinterName, IntPtr.Zero, IntPtr.Zero, 0);
                if (size <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());

                hDevModeIn = ps.GetHdevmode(ps.DefaultPageSettings);
                pDevModeOut = Marshal.AllocHGlobal(size);

                int result;
                IntPtr pDevModeIn = GlobalLock(hDevModeIn);
                try
                {
                    result = DocumentProperties(owner, hPrinter, ps.PrinterName, pDevModeOut, pDevModeIn,
                        DM_IN_BUFFER | DM_IN_PROMPT | DM_OUT_BUFFER);
                }
                finally
                {
                    GlobalUnlock(hDevModeIn);
                }

                if (result != IDOK) return null;

                var data = new byte[size];
                Marshal.Copy(pDevModeOut, data, 0, size);
                return data;
            }
            finally
            {
                if (hDevModeIn != IntPtr.Zero) GlobalFree(hDevModeIn);
                if (pDevModeOut != IntPtr.Zero) Marshal.FreeHGlobal(pDevModeOut);
                ClosePrinter(hPrinter);
            }
        }

        /// <summary>Applies a saved DEVMODE to PrinterSettings (copies, driver-private options such as Paper Type).</summary>
        public static void Apply(PrinterSettings ps, byte[] devMode)
        {
            WithHdevmode(devMode, ps.SetHdevmode);
        }

        /// <summary>Applies a saved DEVMODE to PageSettings (paper size, paper source, orientation).</summary>
        public static void Apply(PageSettings page, byte[] devMode)
        {
            WithHdevmode(devMode, page.SetHdevmode);
        }

        private static void WithHdevmode(byte[] devMode, Action<IntPtr> apply)
        {
            IntPtr h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(uint)devMode.Length);
            if (h == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                IntPtr p = GlobalLock(h);
                Marshal.Copy(devMode, 0, p, devMode.Length);
                GlobalUnlock(h);
                apply(h);
            }
            finally
            {
                GlobalFree(h);
            }
        }
    }
}
