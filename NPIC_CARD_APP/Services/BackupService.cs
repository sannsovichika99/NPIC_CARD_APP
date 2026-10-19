using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Serialization;

namespace NPIC_CARD_APP.Services
{
    /// <summary>Why a backup was made; also decides the start of its file name.</summary>
    public enum BackupKind { Manual, Auto, BeforeRestore }

    /// <summary>
    /// backup-info.xml inside every backup zip: what is in it and where it came from. Its presence is what makes a zip a
    /// Card Studio backup.
    /// </summary>
    [XmlRoot("CardStudioBackup")]
    public class BackupInfo
    {
        public int Format { get; set; }
        public string AppVersion { get; set; }
        public DateTime Created { get; set; }
        public string Kind { get; set; }
        public string MachineName { get; set; }
        public string UserName { get; set; }

        /// <summary>Photo Folder / CSV folder / RFID file of the PC the backup was made on.</summary>
        public string PhotoFolder { get; set; }
        public bool PhotoFolderIsDefault { get; set; }
        public string CsvFolder { get; set; }
        public bool CsvFolderIsDefault { get; set; }
        public string RfidFile { get; set; }

        public int DataFiles { get; set; }
        public int PhotoFiles { get; set; }
        public int CsvFiles { get; set; }
        public bool HasRfid { get; set; }
        public long Bytes { get; set; }

        /// <summary>Files that could not be read when the backup was made (in use, no permission).</summary>
        public List<string> Skipped { get; set; }

        public BackupInfo() { Skipped = new List<string>(); }

        [XmlIgnore]
        public BackupKind KindValue
        {
            get
            {
                BackupKind k;
                return Enum.TryParse(Kind, out k) ? k : BackupKind.Manual;
            }
        }
    }

    /// <summary>One backup zip on disk.</summary>
    public sealed class BackupFile
    {
        public string Path;
        public long Size;
        public DateTime Modified;

        /// <summary>Null when the zip is damaged or not a Card Studio backup (see <see cref="Error"/>).</summary>
        public BackupInfo Info;
        public string Error;

        public DateTime Created { get { return Info != null ? Info.Created : Modified; } }

        public BackupKind Kind
        {
            get
            {
                if (Info != null) return Info.KindValue;
                string name = System.IO.Path.GetFileName(Path) ?? "";
                return name.StartsWith(BackupService.AutoPrefix, StringComparison.OrdinalIgnoreCase) ? BackupKind.Auto
                     : name.StartsWith(BackupService.BeforeRestorePrefix, StringComparison.OrdinalIgnoreCase) ? BackupKind.BeforeRestore
                     : BackupKind.Manual;
            }
        }
    }

    /// <summary>What a backup reads, taken from the settings on the UI thread so the work can run in the background.</summary>
    public sealed class BackupSources
    {
        public string PhotoFolder, CsvFolder, RfidFile;
        public bool PhotoFolderIsDefault, CsvFolderIsDefault;
        public bool IncludePhotos, IncludeCsv;

        public static BackupSources From(AppSettings s, bool includePhotos, bool includeCsv)
        {
            return new BackupSources
            {
                PhotoFolder = (s.PhotoFolder ?? "").Trim(),
                PhotoFolderIsDefault = AppSettings.IsDefaultPhotoFolder(s.PhotoFolder),
                CsvFolder = (s.CsvFolder ?? "").Trim(),
                CsvFolderIsDefault = AppSettings.IsDefaultCsvFolder(s.CsvFolder),
                RfidFile = RfidCardWriter.FilePath(s),
                IncludePhotos = includePhotos,
                IncludeCsv = includeCsv
            };
        }
    }

    /// <summary>Progress of a backup or restore: <see cref="Done"/> of <see cref="Total"/> files.</summary>
    public struct BackupProgress
    {
        public bool Restoring;
        public int Done, Total;
    }

    public sealed class RestoreResult
    {
        /// <summary>Backup of the data as it was just before the restore.</summary>
        public string SafetyBackup;
        public int Restored, Failed;
        public readonly List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Backup &amp; Restore: one .zip with everything Card Studio keeps for this Windows user —
    /// <list type="bullet">
    /// <item><c>data/</c>   %AppData%\NPIC_CARD_APP: settings, print history, RFID cards, QR logos, signatures, card designs…</item>
    /// <item><c>photos/</c> the student photos of the Photo Folder and their _original copies (optional)</item>
    /// <item><c>csv/</c>    the student CSV files of the CSV folder (optional)</item>
    /// <item><c>rfid/</c>   the RFID file when it is kept outside the app data folder</item>
    /// </list>
    /// plus <c>backup-info.xml</c>. The licence (license.txt) is never backed up: it is entered with License….
    /// Backups are kept in %AppData%\NPIC_CARD_APP\Backups; the daily automatic ones are pruned to <see cref="AutoKeep"/>.
    /// A restore first saves the current data as a "before-restore" backup, then puts the files back (files added since the
    /// backup are kept). The app must restart afterwards, without saving the settings it holds in memory.
    /// </summary>
    public static class BackupService
    {
        public const string ManualPrefix = "backup-", AutoPrefix = "auto-", BeforeRestorePrefix = "before-restore-";
        public const int AutoKeep = 10, BeforeRestoreKeep = 5;

        private const int FormatVersion = 1;
        private const string InfoEntry = "backup-info.xml";
        private const string DataPrefix = "data/", PhotosPrefix = "photos/", CsvPrefix = "csv/", RfidPrefix = "rfid/";
        private const string OriginalFolderName = "_original";   // Remove Background keeps the untouched photos here

        private static readonly string[] PhotoExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };
        private static readonly string[] StoredExtensions = { ".jpg", ".jpeg", ".png", ".zip", ".xlsx", ".mp4" };   // already compressed

        /// <summary>Files of the app data folder that never go into a backup (relative paths).</summary>
        private static readonly string[] ExcludedDataFiles = { "license.txt", "lang-missing-km.txt" };

        private static readonly XmlSerializer InfoSerializer = new XmlSerializer(typeof(BackupInfo));
        private static int _busy;

        /// <summary>%AppData%\NPIC_CARD_APP</summary>
        public static string DataFolder { get { return Path.GetDirectoryName(AppSettings.FilePath); } }

        /// <summary>%AppData%\NPIC_CARD_APP\Backups</summary>
        public static string BackupFolder { get { return Path.Combine(DataFolder, "Backups"); } }

        /// <summary>True while a backup or restore is running (the daily backup runs in the background).</summary>
        public static bool IsBusy { get { return Volatile.Read(ref _busy) != 0; } }

        /// <summary>The daily automatic backup is switched on and has not run today.</summary>
        public static bool IsAutoBackupDue(AppSettings s)
        {
            return s.AutoBackup && s.LastAutoBackup.Date != DateTime.Today && !IsBusy;
        }

        // ===================== Backup =====================

        /// <summary>Makes a backup in <see cref="BackupFolder"/> and returns its path. Safe to run on a background thread.</summary>
        /// <exception cref="InvalidOperationException">Another backup or restore is running.</exception>
        public static string Create(BackupSources src, BackupKind kind, IProgress<BackupProgress> progress, CancellationToken ct)
        {
            Enter();
            try
            {
                string path = CreateCore(src, kind, progress, ct);
                if (kind == BackupKind.Auto) Prune(AutoPrefix, AutoKeep);
                return path;
            }
            finally { Leave(); }
        }

        private struct Item
        {
            public string Source, Entry;
            public Item(string source, string entry) { Source = source; Entry = entry; }
        }

        private static string CreateCore(BackupSources src, BackupKind kind, IProgress<BackupProgress> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(BackupFolder);
            CleanPartials();

            var info = new BackupInfo
            {
                Format = FormatVersion,
                AppVersion = UpdateService.CurrentVersionText,
                Created = DateTime.Now,
                Kind = kind.ToString(),
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                PhotoFolder = src.PhotoFolder,
                PhotoFolderIsDefault = src.PhotoFolderIsDefault,
                CsvFolder = src.CsvFolder,
                CsvFolderIsDefault = src.CsvFolderIsDefault,
                RfidFile = src.RfidFile
            };

            // ---- What goes in ----
            var items = new List<Item>();
            string data = DataFolder;
            if (Directory.Exists(data))
            {
                // The photo / CSV folders get their own part (and their own option), even when they are inside the data folder.
                var skipDirs = new List<string> { BackupFolder };
                if (IsUnder(src.PhotoFolder, data)) skipDirs.Add(src.PhotoFolder);
                if (IsUnder(src.CsvFolder, data)) skipDirs.Add(src.CsvFolder);
                foreach (string file in FilesUnder(data, skipDirs))
                {
                    string rel = Relative(data, file);
                    if (ExcludedDataFiles.Any(x => string.Equals(x, rel, StringComparison.OrdinalIgnoreCase))) continue;
                    if (file.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) ||
                        file.EndsWith(".restoring", StringComparison.OrdinalIgnoreCase)) continue;
                    items.Add(new Item(file, DataPrefix + rel));
                }
            }
            if (src.IncludePhotos && SafeDirExists(src.PhotoFolder))
            {
                foreach (string f in TopFiles(src.PhotoFolder, IsPhoto)) items.Add(new Item(f, PhotosPrefix + Path.GetFileName(f)));
                string originals = Path.Combine(src.PhotoFolder, OriginalFolderName);
                if (SafeDirExists(originals))
                    foreach (string f in TopFiles(originals, IsPhoto))
                        items.Add(new Item(f, PhotosPrefix + OriginalFolderName + "/" + Path.GetFileName(f)));
            }
            if (src.IncludeCsv && SafeDirExists(src.CsvFolder))
            {
                foreach (string f in TopFiles(src.CsvFolder, p => HasExtension(p, ".csv")))
                    items.Add(new Item(f, CsvPrefix + Path.GetFileName(f)));
            }
            if (!string.IsNullOrEmpty(src.RfidFile) && SafeFileExists(src.RfidFile) && !IsUnder(src.RfidFile, data))
                items.Add(new Item(src.RfidFile, RfidPrefix + Path.GetFileName(src.RfidFile)));

            // ---- Write: to a .partial file first, so a stopped backup never looks like a good one ----
            string final = UniqueName(kind);
            string partial = final + ".partial";
            try
            {
                using (var fs = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (progress != null && (i % 8 == 0 || i == items.Count - 1))
                            progress.Report(new BackupProgress { Done = i + 1, Total = items.Count });

                        Item item = items[i];
                        try
                        {
                            using (var input = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            {
                                ZipArchiveEntry entry = zip.CreateEntry(item.Entry,
                                    StoredExtensions.Any(x => HasExtension(item.Source, x)) ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                                DateTime modified = File.GetLastWriteTime(item.Source);
                                if (modified.Year >= 1980 && modified.Year <= 2107) entry.LastWriteTime = modified;
                                using (Stream output = entry.Open()) input.CopyTo(output);
                                info.Bytes += input.Length;
                            }
                        }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                        {
                            info.Skipped.Add(item.Entry + " — " + ex.Message);
                            continue;
                        }
                        if (item.Entry.StartsWith(DataPrefix)) info.DataFiles++;
                        else if (item.Entry.StartsWith(PhotosPrefix)) info.PhotoFiles++;
                        else if (item.Entry.StartsWith(CsvPrefix)) info.CsvFiles++;
                        else if (item.Entry.StartsWith(RfidPrefix)) info.HasRfid = true;
                    }

                    ZipArchiveEntry infoEntry = zip.CreateEntry(InfoEntry, CompressionLevel.Optimal);
                    using (Stream s = infoEntry.Open()) InfoSerializer.Serialize(s, info);
                }
                File.Move(partial, final);
            }
            catch
            {
                TryDelete(partial);
                throw;
            }
            return final;
        }

        // ===================== List / read =====================

        /// <summary>The backups in <see cref="BackupFolder"/>, newest first.</summary>
        public static List<BackupFile> List()
        {
            var list = new List<BackupFile>();
            if (!Directory.Exists(BackupFolder)) return list;
            FileInfo[] files;
            try { files = new DirectoryInfo(BackupFolder).GetFiles("*.zip"); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return list; }

            foreach (FileInfo f in files)
            {
                if (!string.Equals(f.Extension, ".zip", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(Describe(f.FullName));
            }
            return list.OrderByDescending(b => b.Created).ToList();
        }

        /// <summary>A backup file anywhere (Restore From File…); <see cref="BackupFile.Info"/> is null when it is not a backup.</summary>
        public static BackupFile Describe(string path)
        {
            var b = new BackupFile { Path = path };
            try
            {
                var f = new FileInfo(path);
                b.Size = f.Length;
                b.Modified = f.LastWriteTime;
                b.Info = ReadInfo(path);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
            {
                b.Error = ex.Message;
            }
            return b;
        }

        /// <exception cref="InvalidDataException">Not a zip, damaged, or not a Card Studio backup.</exception>
        public static BackupInfo ReadInfo(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry entry = zip.GetEntry(InfoEntry);
                    if (entry == null) throw new InvalidDataException("This is not a Card Studio backup.");
                    using (Stream s = entry.Open())
                    {
                        var info = (BackupInfo)InfoSerializer.Deserialize(s);
                        if (info.Skipped == null) info.Skipped = new List<string>();
                        return info;
                    }
                }
            }
            catch (InvalidOperationException ex)   // XmlSerializer: backup-info.xml is damaged
            {
                throw new InvalidDataException("The backup information in this file is damaged.", ex);
            }
        }

        // ===================== Restore =====================

        /// <summary>
        /// Puts the files of <paramref name="zipPath"/> back. The current data (with photos / CSV files when the backup has
        /// them) is first saved as a "before-restore" backup; nothing is restored when that fails. Afterwards the app must
        /// restart without saving the settings it holds in memory.
        /// </summary>
        /// <param name="current">What the app uses now (for the safety backup).</param>
        /// <param name="ct">Stops the safety backup only; once files are being replaced the restore runs to the end.</param>
        /// <exception cref="InvalidDataException">Not a Card Studio backup.</exception>
        /// <exception cref="InvalidOperationException">Another backup or restore is running.</exception>
        public static RestoreResult Restore(string zipPath, BackupSources current, IProgress<BackupProgress> progress, CancellationToken ct)
        {
            Enter();
            try
            {
                BackupInfo info = ReadInfo(zipPath);
                var result = new RestoreResult();

                // 1. Safety copy of everything the restore may overwrite.
                current.IncludePhotos = info.PhotoFiles > 0;
                current.IncludeCsv = info.CsvFiles > 0;
                result.SafetyBackup = CreateCore(current, BackupKind.BeforeRestore, progress, ct);
                Prune(BeforeRestorePrefix, BeforeRestoreKeep);

                using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    List<ZipArchiveEntry> entries = zip.Entries.Where(e => e.Name.Length > 0 && e.FullName != InfoEntry).ToList();
                    // From here on the restore is not stopped by ct: half the files of one backup and half of another
                    // would be worse than either. Only the safety backup above can be cancelled.
                    int done = 0;
                    Action<ZipArchiveEntry, string> put = (entry, target) =>
                    {
                        done++;
                        if (progress != null && (done % 8 == 0 || done == entries.Count))
                            progress.Report(new BackupProgress { Restoring = true, Done = done, Total = entries.Count });
                        Extract(entry, target, result);
                    };

                    // 2. App data: settings, print history, RFID cards, logos, signatures, card designs…
                    string data = DataFolder;
                    foreach (ZipArchiveEntry e in Under(entries, DataPrefix))
                    {
                        string rel = e.FullName.Substring(DataPrefix.Length);
                        if (ExcludedDataFiles.Any(x => string.Equals(x, rel, StringComparison.OrdinalIgnoreCase))) continue;
                        put(e, SafeTarget(data, rel, result));
                    }

                    // 3. The restored settings say where photos, CSV files and the RFID file belong on this PC.
                    AppSettings restored = AppSettings.Load();
                    bool changed = false;

                    List<ZipArchiveEntry> photos = Under(entries, PhotosPrefix).ToList();
                    if (photos.Count > 0)
                    {
                        string folder = PlaceFolder(restored.PhotoFolder, info.PhotoFolderIsDefault, AppSettings.DefaultPhotoFolder,
                            "Photos", info.PhotoFolder, result);
                        if (!AppSettings.SameFolderPath(folder, restored.PhotoFolder)) { restored.PhotoFolder = folder; changed = true; }
                        foreach (ZipArchiveEntry e in photos) put(e, SafeTarget(folder, e.FullName.Substring(PhotosPrefix.Length), result));
                    }

                    List<ZipArchiveEntry> csv = Under(entries, CsvPrefix).ToList();
                    if (csv.Count > 0)
                    {
                        string folder = PlaceFolder(restored.CsvFolder, info.CsvFolderIsDefault, AppSettings.DefaultCsvFolder,
                            "CSV files", info.CsvFolder, result);
                        if (!AppSettings.SameFolderPath(folder, restored.CsvFolder)) { restored.CsvFolder = folder; changed = true; }
                        foreach (ZipArchiveEntry e in csv) put(e, SafeTarget(folder, e.FullName.Substring(CsvPrefix.Length), result));
                    }

                    ZipArchiveEntry rfid = Under(entries, RfidPrefix).FirstOrDefault();
                    if (rfid != null)
                    {
                        string target = RfidCardWriter.FilePath(restored);
                        if (!TryCreateFolder(Path.GetDirectoryName(target)))
                        {
                            result.Notes.Add("The RFID file was restored to the app data folder: " + RfidCardWriter.DefaultPath +
                                " (" + target + " is not available on this PC).");
                            restored.RfidCsvPath = "";
                            target = RfidCardWriter.DefaultPath;
                            changed = true;
                        }
                        put(rfid, target);
                    }

                    if (changed) restored.Save();
                }

                if (info.Skipped.Count > 0)
                    result.Notes.Add(info.Skipped.Count + " file(s) could not be read when this backup was made and are not in it.");
                return result;
            }
            finally { Leave(); }
        }

        /// <summary>
        /// Where restored photos / CSV files go: the folder of the restored settings when it can be used on this PC. A
        /// default folder of the backup's PC (another Windows user's AppData) becomes this PC's default folder.
        /// </summary>
        private static string PlaceFolder(string wanted, bool wasDefault, string fallback, string what, string original, RestoreResult result)
        {
            if (wasDefault || AppSettings.SameFolderPath(wanted, fallback))
            {
                TryCreateFolder(fallback);
                return fallback;
            }
            if (TryCreateFolder(wanted)) return wanted;
            TryCreateFolder(fallback);
            result.Notes.Add(what + " were restored to the default folder " + fallback + " (" +
                (string.IsNullOrEmpty(wanted) ? original : wanted) + " is not available on this PC).");
            return fallback;
        }

        /// <summary>Full path of <paramref name="rel"/> under <paramref name="root"/>; null for a path that would leave it ("..\").</summary>
        private static string SafeTarget(string root, string rel, RestoreResult result)
        {
            try
            {
                string target = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (IsUnder(target, root)) return target;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { }
            result.Notes.Add("Skipped an unsafe file name in the backup: " + rel);
            result.Failed++;
            return null;
        }

        private static void Extract(ZipArchiveEntry entry, string target, RestoreResult result)
        {
            if (target == null) return;
            string temp = target + ".restoring";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (Stream input = entry.Open())
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    input.CopyTo(output);

                if (File.Exists(target))
                {
                    File.SetAttributes(target, FileAttributes.Normal);   // a read-only file would refuse the delete
                    File.Delete(target);
                }
                File.Move(temp, target);
                try { File.SetLastWriteTime(target, entry.LastWriteTime.LocalDateTime); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { }
                result.Restored++;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                TryDelete(temp);
                result.Failed++;
                result.Notes.Add("Could not restore " + target + ": " + ex.Message);
            }
        }

        // ===================== Housekeeping =====================

        /// <summary>Deletes the oldest backups whose name starts with <paramref name="prefix"/>, keeping <paramref name="keep"/>.</summary>
        private static void Prune(string prefix, int keep)
        {
            try
            {
                IEnumerable<FileInfo> old = new DirectoryInfo(BackupFolder).GetFiles(prefix + "*.zip")
                    .Where(f => string.Equals(f.Extension, ".zip", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)   // names hold the date: yyyyMMdd-HHmmss
                    .Skip(keep);
                foreach (FileInfo f in old) TryDelete(f.FullName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        /// <summary>Leftovers of a backup stopped by a crash or power cut (older than a day, so never one being written).</summary>
        private static void CleanPartials()
        {
            try
            {
                foreach (FileInfo f in new DirectoryInfo(BackupFolder).GetFiles("*.partial"))
                    if (f.LastWriteTime < DateTime.Now.AddDays(-1)) TryDelete(f.FullName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        private static string UniqueName(BackupKind kind)
        {
            string prefix = kind == BackupKind.Auto ? AutoPrefix : kind == BackupKind.BeforeRestore ? BeforeRestorePrefix : ManualPrefix;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(BackupFolder, prefix + stamp + ".zip");
            for (int n = 2; File.Exists(path) || File.Exists(path + ".partial"); n++)
                path = Path.Combine(BackupFolder, prefix + stamp + "-" + n + ".zip");
            return path;
        }

        private static void Enter()
        {
            if (Interlocked.Exchange(ref _busy, 1) != 0)
                throw new InvalidOperationException("Another backup or restore is running. Try again in a moment.");
        }

        private static void Leave() { Volatile.Write(ref _busy, 0); }

        // ===================== Helpers =====================

        private static IEnumerable<ZipArchiveEntry> Under(IEnumerable<ZipArchiveEntry> entries, string prefix)
        {
            return entries.Where(e => e.FullName.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                      e.FullName.Length > prefix.Length);
        }

        /// <summary>Every file under <paramref name="root"/>, skipping <paramref name="skipDirs"/> and unreadable folders.</summary>
        private static IEnumerable<string> FilesUnder(string root, List<string> skipDirs)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                string[] files, dirs;
                try
                {
                    files = Directory.GetFiles(dir);
                    dirs = Directory.GetDirectories(dir);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { continue; }
                foreach (string f in files) yield return f;
                foreach (string d in dirs)
                    if (!skipDirs.Any(s => AppSettings.SameFolderPath(s, d))) pending.Push(d);
            }
        }

        private static IEnumerable<string> TopFiles(string folder, Func<string, bool> keep)
        {
            try { return Directory.GetFiles(folder).Where(keep).ToList(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return new string[0]; }
        }

        private static bool IsPhoto(string path) { return PhotoExtensions.Any(x => HasExtension(path, x)); }

        private static bool HasExtension(string path, string ext)
        {
            return string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"Logos/qr.png" for <c>%AppData%\NPIC_CARD_APP\Logos\qr.png</c>.</summary>
        private static string Relative(string root, string file)
        {
            string r = root.TrimEnd('\\') + "\\";
            return (file.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? file.Substring(r.Length) : Path.GetFileName(file))
                .Replace('\\', '/');
        }

        /// <summary>True when <paramref name="path"/> is inside <paramref name="root"/> (not the root itself).</summary>
        private static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
            try
            {
                string p = Path.GetFullPath(path.Trim()).TrimEnd('\\');
                string r = Path.GetFullPath(root.Trim()).TrimEnd('\\') + "\\";
                return p.StartsWith(r, StringComparison.OrdinalIgnoreCase) && p.Length > r.Length;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }
        }

        private static bool TryCreateFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return false;
            try
            {
                if (!Path.IsPathRooted(folder)) return false;
                Directory.CreateDirectory(folder);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                                       ex is NotSupportedException)
            {
                return false;
            }
        }

        private static bool SafeDirExists(string folder)
        {
            try { return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder); }
            catch (ArgumentException) { return false; }
        }

        private static bool SafeFileExists(string file)
        {
            try { return File.Exists(file); }
            catch (ArgumentException) { return false; }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }
}
