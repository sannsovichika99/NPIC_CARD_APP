using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CardStudio.Setup
{
    /// <summary>Creates .lnk files through the Windows Shell's IShellLink (no WScript.Shell dependency).</summary>
    internal static class Shortcut
    {
        public static void Create(string lnkPath, string target, string workingDir, string description, string arguments = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath));

            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(target);
                link.SetWorkingDirectory(workingDir);
                link.SetDescription(description);
                link.SetIconLocation(target, 0);
                if (!string.IsNullOrEmpty(arguments)) link.SetArguments(arguments);
                ((IPersistFile)link).Save(lnkPath, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }

        /// <summary>Tells Explorer to refresh icons, so new shortcuts show the app icon straight away.</summary>
        public static void NotifyShell()
        {
            const int SHCNE_ASSOCCHANGED = 0x08000000;
            SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
            void GetIDList(out IntPtr idl);
            void SetIDList(IntPtr idl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int cch, out int icon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid classId);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
        }
    }
}
