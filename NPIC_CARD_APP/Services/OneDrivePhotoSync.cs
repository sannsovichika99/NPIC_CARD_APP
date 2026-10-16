using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace NPIC_CARD_APP.Services
{
    /// <summary>A OneDrive account synced to this PC.</summary>
    public sealed class OneDriveRoot
    {
        /// <summary>"Personal" or the work / school name, e.g. "NPIC".</summary>
        public string Name { get; set; }
        /// <summary>The account's local folder, e.g. C:\Users\Dara\OneDrive - NPIC.</summary>
        public string Path { get; set; }
    }

    /// <summary>
    /// Copies student photos from a OneDrive folder that the OneDrive app syncs to this PC into the Photo Folder.
    /// Nothing goes through the internet here: OneDrive itself downloads online-only files when they are read.
    /// <para>Matching, the comparison with the Photo Folder and saving are the same as for Google Drive
    /// (<see cref="GoogleDrivePhotoSync.Plan"/>). Local files have no MD5, so a photo counts as changed when its size differs.</para>
    /// </summary>
    public static class OneDrivePhotoSync
    {
        /// <summary>Files listed; those that are not JPG / PNG / BMP show as "not JPG/PNG" so the user sees why they are skipped.</summary>
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".webp", ".heic", ".heif", ".tif", ".tiff"
        };

        private const int MaxDepth = 5;
        private const int MaxFiles = 20000;

        // ===================== OneDrive on this PC =====================
        /// <summary>The OneDrive accounts synced to this PC (none when the OneDrive app is not set up).</summary>
        public static List<OneDriveRoot> FindRoots()
        {
            var roots = new List<OneDriveRoot>();
            Action<string, string> add = (name, path) =>
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                path = path.Trim().TrimEnd('\\');
                if (!Directory.Exists(path) || roots.Any(r => SamePath(r.Path, path))) return;
                roots.Add(new OneDriveRoot { Name = name, Path = path });
            };

            // The OneDrive app lists every signed-in account here, with its name.
            try
            {
                using (RegistryKey accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts"))
                {
                    if (accounts != null)
                        foreach (string sub in accounts.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                            using (RegistryKey account = accounts.OpenSubKey(sub))
                            {
                                if (account == null) continue;
                                string name = account.GetValue("DisplayName") as string;
                                if (string.IsNullOrWhiteSpace(name)) name = sub == "Personal" ? "Personal" : "Work or school";
                                add(name, account.GetValue("UserFolder") as string);
                            }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                /* fall back to the environment variables */
            }

            add("Personal", Environment.GetEnvironmentVariable("OneDriveConsumer"));
            add("Work or school", Environment.GetEnvironmentVariable("OneDriveCommercial"));
            add("OneDrive", Environment.GetEnvironmentVariable("OneDrive"));
            return roots;
        }

        /// <summary>The account whose folder holds <paramref name="folder"/>, or null when it is outside OneDrive.</summary>
        public static OneDriveRoot RootOf(string folder, IEnumerable<OneDriveRoot> roots)
        {
            if (string.IsNullOrWhiteSpace(folder)) return null;
            return roots.Where(r => IsSameOrInside(folder, r.Path)).OrderByDescending(r => r.Path.Length).FirstOrDefault();
        }

        // ===================== Folder =====================
        /// <summary>Checks the folder before scanning.</summary>
        /// <exception cref="ArgumentException">No folder, a missing folder, or the Photo Folder itself.</exception>
        public static string CheckFolder(string folder, string photoFolder)
        {
            folder = (folder ?? "").Trim().TrimEnd('\\');
            if (folder.Length == 0) throw new ArgumentException("Choose the OneDrive folder with the photos: click Browse….");
            if (folder.EndsWith(":")) folder += "\\";   // a whole drive, e.g. "D:"
            string full;
            try { full = Path.GetFullPath(folder); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw new ArgumentException("This is not a valid folder path: " + folder);
            }
            if (!Directory.Exists(full))
                throw new ArgumentException("The folder was not found: " + full + "\n\n" +
                    "Check that OneDrive is running and signed in on this PC, then choose the folder again.");
            if (!string.IsNullOrEmpty(photoFolder) && SamePath(full, photoFolder))
                throw new ArgumentException("This is the Photo Folder itself. Choose the OneDrive folder the photos come from.");
            return full;
        }

        // ===================== List =====================
        /// <summary>
        /// Lists every picture in the folder and its sub-folders (reads names and sizes only: online-only files stay online).
        /// The Photo Folder is skipped when it lies inside the OneDrive folder.
        /// </summary>
        public static Task<List<DriveFile>> ListImagesAsync(string folder, string photoFolder, CancellationToken cancel)
        {
            string root = CheckFolder(folder, photoFolder);
            return Task.Run(() =>
            {
                var files = new List<DriveFile>();
                var queue = new Queue<Tuple<DirectoryInfo, string, int>>();   // folder, path below the root, depth
                queue.Enqueue(Tuple.Create(new DirectoryInfo(root), "", 0));

                while (queue.Count > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    var dir = queue.Dequeue();
                    FileSystemInfo[] entries;
                    try { entries = dir.Item1.GetFileSystemInfos(); }
                    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                    {
                        if (dir.Item3 == 0) throw new IOException("Could not read the folder: " + ex.Message, ex);
                        continue;   // an unreadable sub-folder: list the rest
                    }

                    foreach (FileSystemInfo entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        string path = dir.Item2.Length == 0 ? entry.Name : dir.Item2 + "/" + entry.Name;

                        var sub = entry as DirectoryInfo;
                        if (sub != null)
                        {
                            // OneDrive folders are reparse points too, so the depth limit is what stops loops.
                            if (dir.Item3 < MaxDepth && !(photoFolder != null && SamePath(sub.FullName, photoFolder)))
                                queue.Enqueue(Tuple.Create(sub, path, dir.Item3 + 1));
                            continue;
                        }

                        var file = (FileInfo)entry;
                        if (!ImageExtensions.Contains(file.Extension)) continue;
                        files.Add(new DriveFile
                        {
                            Id = file.FullName, Name = file.Name, Folder = dir.Item2,
                            Size = file.Length,   // the real size, even for an online-only file
                            Modified = file.LastWriteTime
                        });
                        if (files.Count > MaxFiles)
                            throw new InvalidDataException("The folder holds more than " + MaxFiles + " pictures. Choose the folder with the photos only.");
                    }
                }
                return files;
            }, cancel);
        }

        // ===================== Copy =====================
        /// <summary>Copies the planned photos into the Photo Folder. Failures are recorded per item.</summary>
        public static Task CopyAsync(IList<PhotoSyncItem> items, string photoFolder, IProgress<int> progress, CancellationToken cancel)
        {
            return GoogleDrivePhotoSync.SaveAllAsync(items, photoFolder, ReadAsync, progress, cancel);
        }

        private static async Task<byte[]> ReadAsync(DriveFile f, CancellationToken cancel)
        {
            try
            {
                // Reading an online-only file makes OneDrive download it first.
                using (var source = new FileStream(f.Id, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true))
                using (var copy = new MemoryStream(f.Size > 0 && f.Size < int.MaxValue ? (int)f.Size : 0))
                {
                    await source.CopyToAsync(copy, 81920, cancel).ConfigureAwait(false);
                    return copy.ToArray();
                }
            }
            catch (FileNotFoundException)
            {
                throw new FileNotFoundException("The file is no longer in the OneDrive folder. Scan again.");
            }
            catch (DirectoryNotFoundException)
            {
                throw new FileNotFoundException("The file is no longer in the OneDrive folder. Scan again.");
            }
            catch (IOException ex)
            {
                // Typically "The cloud file provider is not running" / offline while the file is online-only.
                throw new IOException("OneDrive could not provide the file. Check that OneDrive is running, signed in and online. (" +
                                      ex.Message.Trim() + ")", ex);
            }
        }

        // ===================== Paths =====================
        private static bool SamePath(string a, string b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameOrInside(string path, string folder)
        {
            string p = Normalize(path), f = Normalize(folder);
            return p.Length > 0 && f.Length > 0 &&
                   (string.Equals(p, f, StringComparison.OrdinalIgnoreCase) || p.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase));
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path.Trim()).TrimEnd('\\'); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return path.Trim().TrimEnd('\\');
            }
        }
    }
}
