using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace CardStudio.Setup
{
    /// <summary>Removes what InstallEngine created. No UI.</summary>
    internal static class UninstallEngine
    {
        /// <summary>Install folder from the registry, else the folder this uninstall.exe runs from. Null if not installed.</summary>
        public static string FindInstallDir()
        {
            string dir = InstallEngine.ExistingInstallDir();
            if (dir != null && Directory.Exists(dir)) return dir;

            string own = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            return File.Exists(Path.Combine(own, ProductInfo.ExeName)) ? own : null;
        }

        /// <returns>Files or folders that could not be removed (empty when everything went).</returns>
        public static List<string> Uninstall(string dir, bool purgeUserData, IProgress<InstallProgress> progress)
        {
            Log.Write("Uninstalling from " + dir + (purgeUserData ? " (with user data)" : ""));
            var leftovers = new List<string>();

            Report(progress, 5, "Removing shortcuts");
            RemoveShortcuts();

            List<string> files = InstallEngine.ReadManifest(dir);
            if (files.Count == 0)                       // manifest lost: remove at least the program itself
                files = new List<string> { ProductInfo.ExeName, ProductInfo.ExeName + ".config" };

            for (int i = 0; i < files.Count; i++)
            {
                Report(progress, 10 + 70 * i / files.Count, "Removing " + files[i]);
                string path;
                try { path = InstallEngine.SafeCombine(dir, files[i]); }
                catch (InvalidDataException) { continue; }   // never delete outside the install folder
                if (!InstallEngine.TryDeleteFile(path)) leftovers.Add(path);
            }
            InstallEngine.TryDeleteFile(Path.Combine(dir, ProductInfo.ManifestName));
            InstallEngine.DeleteEmptyFolders(dir, false);

            Report(progress, 85, "Removing the Settings > Apps entry");
            RemoveUninstallEntry();

            if (purgeUserData)
            {
                Report(progress, 90, ProductInfo.PurgeProgressText);
                string data = ProductInfo.UserDataFolder;
                try { if (Directory.Exists(data)) Directory.Delete(data, true); }
                catch (Exception ex) { Log.Write("Could not delete " + data + ": " + ex.Message); leftovers.Add(data); }
            }

            Report(progress, 95, "Removing the uninstaller");
            string uninstaller = Path.Combine(dir, ProductInfo.UninstallerName);
            if (InstallEngine.PathsEqual(Assembly.GetEntryAssembly().Location, uninstaller))
            {
                ScheduleSelfDelete(uninstaller, dir);     // a running exe cannot delete itself
            }
            else
            {
                InstallEngine.TryDeleteFile(uninstaller);
                InstallEngine.DeleteEmptyFolders(dir, true);
            }

            Report(progress, 100, "Done");
            Log.Write("Uninstall finished, " + leftovers.Count + " leftovers");
            return leftovers;
        }

        public static void RemoveShortcuts()
        {
            InstallEngine.TryDeleteFile(ProductInfo.DesktopShortcut);
            string folder = ProductInfo.StartMenuFolder;
            if (!Directory.Exists(folder)) return;
            InstallEngine.TryDeleteFile(Path.Combine(folder, ProductInfo.Name + ".lnk"));
            InstallEngine.TryDeleteFile(Path.Combine(folder, "Uninstall " + ProductInfo.Name + ".lnk"));
            InstallEngine.DeleteEmptyFolders(folder, true);
        }

        public static void RemoveUninstallEntry()
        {
            using (var hklm = InstallEngine.Hklm())
                hklm.DeleteSubKeyTree(ProductInfo.UninstallKey, false);
        }

        /// <summary>
        /// A hidden cmd.exe waits ~2 s (this process has exited by then), deletes uninstall.exe and removes the
        /// install folder if nothing else is left in it. rmdir without /s never deletes files the user added.
        /// </summary>
        private static void ScheduleSelfDelete(string exe, string dir)
        {
            string cmd = "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + exe + "\" & rmdir \"" + dir.TrimEnd('\\') + "\"";
            var psi = new ProcessStartInfo("cmd.exe", cmd)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath()
            };
            Process.Start(psi).Dispose();
        }

        private static void Report(IProgress<InstallProgress> progress, int percent, string status)
        {
            if (progress != null) progress.Report(new InstallProgress(percent, status));
        }
    }
}
