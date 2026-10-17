using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace CardStudio.Setup
{
    internal sealed class InstallProgress
    {
        public readonly int Percent;
        public readonly string Status;

        public InstallProgress(int percent, string status)
        {
            Percent = Math.Max(0, Math.Min(100, percent));
            Status = status;
        }
    }

    /// <summary>Copies the embedded payload, writes the uninstaller, shortcuts and the Settings > Apps entry. No UI.</summary>
    internal static class InstallEngine
    {
        // ---------- Checks ----------

        public static bool IsNetFrameworkInstalled()
        {
            using (RegistryKey hklm = Hklm())
            using (RegistryKey key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            {
                object release = key == null ? null : key.GetValue("Release");
                return release is int && (int)release >= ProductInfo.MinNetFrameworkRelease;
            }
        }

        /// <summary>Install folder of an existing installation, or null when the product is not installed.</summary>
        public static string ExistingInstallDir()
        {
            using (RegistryKey hklm = Hklm())
            using (RegistryKey key = hklm.OpenSubKey(ProductInfo.UninstallKey))
            {
                string dir = key == null ? null : key.GetValue("InstallLocation") as string;
                return string.IsNullOrWhiteSpace(dir) ? null : dir;
            }
        }

        public static string ExistingVersion()
        {
            using (RegistryKey hklm = Hklm())
            using (RegistryKey key = hklm.OpenSubKey(ProductInfo.UninstallKey))
                return key == null ? null : key.GetValue("DisplayVersion") as string;
        }

        public static bool IsAppRunning()
        {
            Process[] running = Process.GetProcessesByName(ProductInfo.ProcessName);
            foreach (Process p in running) p.Dispose();
            return running.Length > 0;
        }

        /// <summary>Uncompressed size of the payload, in bytes.</summary>
        public static long PayloadSize()
        {
            using (Stream s = OpenPayload())
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                return zip.Entries.Sum(e => e.Length);
        }

        // ---------- Install ----------

        public static void Install(SetupOptions o, IProgress<InstallProgress> progress)
        {
            string dir = Path.GetFullPath(o.InstallDir).TrimEnd('\\');
            string existing = ExistingInstallDir();
            if (existing != null && !PathsEqual(existing, dir))
                throw new InvalidOperationException(ProductInfo.Name + " is already installed in " + existing +
                                                    ". Uninstall it first, or install into the same folder.");
            bool upgrade = existing != null;
            Log.Write((upgrade ? "Upgrading" : "Installing") + " " + ProductInfo.Version + " into " + dir);

            var created = new List<string>();     // new files, removed again if a fresh install fails
            try
            {
                Report(progress, 0, "Preparing " + dir);
                Directory.CreateDirectory(dir);

                // Staff replace card images in Assets\ without admin rights, so BUILTIN\Users get Modify there.
                // Set before extracting so every file inherits it.
                string assets = Path.Combine(dir, ProductInfo.UserEditableFolder);
                Directory.CreateDirectory(assets);
                GrantUsersModify(assets);

                List<string> oldFiles = upgrade ? ReadManifest(dir) : new List<string>();
                var newFiles = new List<string>();
                long totalBytes;

                using (Stream s = OpenPayload())
                using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    List<ZipArchiveEntry> entries = zip.Entries.Where(e => e.Name.Length > 0).ToList();
                    totalBytes = entries.Sum(e => e.Length);
                    long doneBytes = 0;

                    foreach (ZipArchiveEntry entry in entries)
                    {
                        string rel = entry.FullName.Replace('/', '\\');
                        string target = SafeCombine(dir, rel);
                        newFiles.Add(rel);

                        if (upgrade && IsUserEditable(rel) && File.Exists(target))
                        {
                            Log.Write("Kept user file " + rel);
                        }
                        else
                        {
                            Report(progress, (int)(85 * doneBytes / Math.Max(1, totalBytes)), "Copying " + rel);
                            bool existed = File.Exists(target);
                            Extract(entry, target);
                            if (!existed) created.Add(target);
                        }
                        doneBytes += entry.Length;
                    }
                }

                // Files shipped by the previous version but not by this one.
                foreach (string rel in oldFiles.Except(newFiles, StringComparer.OrdinalIgnoreCase))
                    if (!IsUserEditable(rel)) TryDeleteFile(Path.Combine(dir, rel));

                string manifest = Path.Combine(dir, ProductInfo.ManifestName);
                if (!File.Exists(manifest)) created.Add(manifest);
                File.WriteAllLines(manifest, newFiles);

                Report(progress, 88, "Writing the uninstaller");
                string uninstaller = Path.Combine(dir, ProductInfo.UninstallerName);
                string self = Assembly.GetEntryAssembly().Location;
                if (!PathsEqual(self, uninstaller))
                {
                    if (!File.Exists(uninstaller)) created.Add(uninstaller);
                    File.Copy(self, uninstaller, true);
                }

                Report(progress, 92, "Creating shortcuts");
                string exe = Path.Combine(dir, ProductInfo.ExeName);
                UninstallEngine.RemoveShortcuts();     // a reinstall may turn a shortcut off
                if (o.DesktopShortcut)
                    Shortcut.Create(ProductInfo.DesktopShortcut, exe, dir, "Design and print student ID cards");
                if (o.StartMenuShortcut)
                {
                    Shortcut.Create(Path.Combine(ProductInfo.StartMenuFolder, ProductInfo.Name + ".lnk"),
                                    exe, dir, "Design and print student ID cards");
                    Shortcut.Create(Path.Combine(ProductInfo.StartMenuFolder, "Uninstall " + ProductInfo.Name + ".lnk"),
                                    uninstaller, dir, "Remove " + ProductInfo.Name, "/uninstall");
                }

                Report(progress, 96, "Registering with Windows");
                WriteUninstallEntry(dir, exe, uninstaller, totalBytes);
                Shortcut.NotifyShell();

                Report(progress, 100, "Done");
                Log.Write("Install finished");
            }
            catch (Exception ex)
            {
                Log.Write("Install failed: " + ex);
                if (!upgrade) Rollback(dir, created);
                throw;
            }
        }

        /// <summary>
        /// Starts the app as the signed-in user. Setup runs elevated; starting the exe directly would run the
        /// app as administrator too. explorer.exe hands the launch to the (unelevated) shell.
        /// </summary>
        public static void LaunchApp(string dir)
        {
            string exe = Path.Combine(dir, ProductInfo.ExeName);
            try { Process.Start("explorer.exe", "\"" + exe + "\""); }
            catch (Exception ex) { Log.Write("Launch failed: " + ex.Message); }
        }

        // ---------- Helpers (also used by UninstallEngine) ----------

        internal static RegistryKey Hklm()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
        }

        internal static List<string> ReadManifest(string dir)
        {
            string path = Path.Combine(dir, ProductInfo.ManifestName);
            if (!File.Exists(path)) return new List<string>();
            return File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }

        internal static bool IsUserEditable(string relativePath)
        {
            return relativePath.StartsWith(ProductInfo.UserEditableFolder + "\\", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool PathsEqual(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                                 StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>dir + relative path, refusing anything that escapes dir (e.g. "..\" in a zip entry).</summary>
        internal static string SafeCombine(string dir, string rel)
        {
            string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            string full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Path outside the install folder: " + rel);
            return full;
        }

        internal static bool TryDeleteFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return true;
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("Could not delete " + path + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>Deletes empty sub-folders of dir (deepest first), then dir itself if it is empty.</summary>
        internal static void DeleteEmptyFolders(string dir, bool includeRoot)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string sub in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                TryDeleteEmptyFolder(sub);
            if (includeRoot) TryDeleteEmptyFolder(dir);
        }

        private static void TryDeleteEmptyFolder(string dir)
        {
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
            catch (Exception ex) { Log.Write("Could not delete folder " + dir + ": " + ex.Message); }
        }

        private static Stream OpenPayload()
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ProductInfo.PayloadResource);
            if (s == null) throw new InvalidOperationException("This setup file is damaged: the application payload is missing.");
            return s;
        }

        private static void Extract(ZipArchiveEntry entry, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
            using (Stream src = entry.Open())
            using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                src.CopyTo(dst);
            File.SetLastWriteTime(target, entry.LastWriteTime.LocalDateTime);
        }

        private static void GrantUsersModify(string folder)
        {
            var info = new DirectoryInfo(folder);
            DirectorySecurity acl = info.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            info.SetAccessControl(acl);
        }

        private static void WriteUninstallEntry(string dir, string exe, string uninstaller, long totalBytes)
        {
            using (RegistryKey hklm = Hklm())
            using (RegistryKey key = hklm.CreateSubKey(ProductInfo.UninstallKey))
            {
                key.SetValue("DisplayName", ProductInfo.Name);
                key.SetValue("DisplayVersion", ProductInfo.Version);
                key.SetValue("Publisher", ProductInfo.Publisher);
                key.SetValue("DisplayIcon", exe + ",0");
                key.SetValue("InstallLocation", dir);
                key.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                key.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /S");
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("EstimatedSize", (int)((totalBytes + new FileInfo(uninstaller).Length) / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        private static void Rollback(string dir, List<string> created)
        {
            Log.Write("Rolling back " + created.Count + " files");
            UninstallEngine.RemoveShortcuts();
            UninstallEngine.RemoveUninstallEntry();
            foreach (string f in created) TryDeleteFile(f);
            DeleteEmptyFolders(dir, true);
        }

        private static void Report(IProgress<InstallProgress> progress, int percent, string status)
        {
            if (progress != null) progress.Report(new InstallProgress(percent, status));
        }
    }

    /// <summary>Plain-text log in %TEMP%\CardStudio-Setup.log (useful for silent installs).</summary>
    internal static class Log
    {
        public static readonly string FilePath = Path.Combine(Path.GetTempPath(), "CardStudio-Setup.log");
        private static readonly object Gate = new object();

        public static void Write(string line)
        {
            try
            {
                lock (Gate)
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
