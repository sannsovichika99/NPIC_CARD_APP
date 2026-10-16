using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;

namespace NPIC_CARD_APP.Services
{
    /// <summary>One image file in the shared Google Drive folder.</summary>
    public sealed class DriveFile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        /// <summary>Sub-folder path inside the shared folder ("" for the folder itself).</summary>
        public string Folder { get; set; }
        public long Size { get; set; }
        public string Md5 { get; set; }
        public DateTime? Modified { get; set; }
        public string ResourceKey { get; set; }
    }

    public enum PhotoSyncStatus { New, Changed, UpToDate, KeptEdited, NotMatched, Unsupported, Duplicate, Downloaded, Failed }

    /// <summary>A Drive file and what the sync does with it.</summary>
    public sealed class PhotoSyncItem
    {
        public DriveFile File { get; set; }
        /// <summary>Student the file belongs to, or null when the name matches none.</summary>
        public string StudentId { get; set; }
        public string StudentName { get; set; }
        public PhotoSyncStatus Status { get; set; }
        /// <summary>Error text when <see cref="Status"/> is Failed.</summary>
        public string Error { get; set; }

        public bool WillDownload(bool replaceChanged)
        {
            return Status == PhotoSyncStatus.New || (replaceChanged && Status == PhotoSyncStatus.Changed);
        }
    }

    /// <summary>
    /// Copies student photos from a Google Drive folder shared as "Anyone with the link" into the Photo Folder,
    /// using the Google Drive API v3 with an API key (no Google sign-in).
    /// <para>A file belongs to a student when its name is the StudentId ("NPIC24001.jpg") or starts with it followed by a
    /// space, "-" or "_" — Google Forms names uploads "NPIC24001 - Sok Dara.jpg". Sub-folders are searched too.</para>
    /// <para>Photos edited with Remove Background are never replaced: their original lives in the "_original" backup folder.</para>
    /// </summary>
    public static class GoogleDrivePhotoSync
    {
        public const int TimeoutSeconds = 120;
        public const string ApiKeyHelpUrl = "https://console.cloud.google.com/apis/credentials";
        public static readonly string[] PhotoExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        private const string Api = "https://www.googleapis.com/drive/v3/files";
        private const string FolderMime = "application/vnd.google-apps.folder";
        private const int MaxDepth = 5;
        private const int MaxFiles = 20000;
        private const int ParallelDownloads = 4;

        private static readonly Regex FolderInPath = new Regex(@"/folders/([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant);
        private static readonly Regex IdParam = new Regex(@"[?&]id=([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant);
        private static readonly Regex ResourceKeyParam = new Regex(@"[?&]resourcekey=([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex BareId = new Regex(@"^[A-Za-z0-9_-]{19,}$", RegexOptions.CultureInvariant);
        private static readonly Regex LeadingId = new Regex(@"^[\p{L}\p{N}_.-]+", RegexOptions.CultureInvariant);

        // One shared client for the app's lifetime (creating one per request leaks sockets).
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NPIC_CARD_APP/1.0");
            return client;
        }

        // ===================== Link =====================
        /// <summary>Reads the folder id (and the resource key of older shared links) from a Drive folder link or bare id.</summary>
        /// <exception cref="ArgumentException">Not a Google Drive folder link.</exception>
        public static void ParseFolderLink(string link, out string folderId, out string resourceKey)
        {
            link = (link ?? "").Trim();
            if (link.Length == 0) throw new ArgumentException("Paste the link of the Google Drive folder with the photos.");

            folderId = null;
            if (BareId.IsMatch(link)) folderId = link;
            else if (link.IndexOf("drive.google.com/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Match m = FolderInPath.Match(link);
                if (!m.Success) m = IdParam.Match(link);
                if (m.Success) folderId = m.Groups[1].Value;
            }
            if (folderId == null)
                throw new ArgumentException("This is not a Google Drive folder link.\n\n" +
                    "In Google Drive right-click the folder → Share → Copy link, e.g.\nhttps://drive.google.com/drive/folders/…");

            Match key = ResourceKeyParam.Match(link);
            resourceKey = key.Success ? key.Groups[1].Value : null;
        }

        /// <summary>Address that opens the folder in the browser.</summary>
        public static string FolderWebUrl(string link)
        {
            string id, key;
            ParseFolderLink(link, out id, out key);
            return "https://drive.google.com/drive/folders/" + id + (key != null ? "?resourcekey=" + key : "");
        }

        // ===================== List =====================
        /// <summary>Lists every image in the shared folder and its sub-folders.</summary>
        public static async Task<List<DriveFile>> ListImagesAsync(string folderLink, string apiKey, CancellationToken cancel)
        {
            string folderId, resourceKey;
            ParseFolderLink(folderLink, out folderId, out resourceKey);
            apiKey = CheckKey(apiKey);

            var files = new List<DriveFile>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<Tuple<string, string, string, int>>();   // id, resource key, path, depth
            queue.Enqueue(Tuple.Create(folderId, resourceKey, "", 0));

            while (queue.Count > 0)
            {
                var folder = queue.Dequeue();
                if (!visited.Add(folder.Item1)) continue;
                string pageToken = null;
                do
                {
                    string url = Api + "?q=" + Uri.EscapeDataString("'" + folder.Item1 + "' in parents and trashed = false") +
                                 "&fields=" + Uri.EscapeDataString("nextPageToken,files(id,name,mimeType,size,md5Checksum,modifiedTime,resourceKey)") +
                                 "&pageSize=1000&supportsAllDrives=true&includeItemsFromAllDrives=true&key=" + Uri.EscapeDataString(apiKey) +
                                 (pageToken != null ? "&pageToken=" + Uri.EscapeDataString(pageToken) : "");
                    string body = Encoding.UTF8.GetString(await GetAsync(url, folder.Item1, folder.Item2, cancel).ConfigureAwait(false));
                    var page = Json(body);
                    pageToken = page.ContainsKey("nextPageToken") ? page["nextPageToken"] as string : null;

                    foreach (object o in (page.ContainsKey("files") ? page["files"] as IList : null) ?? new object[0])
                    {
                        var f = o as Dictionary<string, object>;
                        if (f == null) continue;
                        string id = Str(f, "id"), name = Str(f, "name"), mime = Str(f, "mimeType");
                        if (mime == FolderMime)
                        {
                            if (folder.Item4 < MaxDepth)
                                queue.Enqueue(Tuple.Create(id, Str(f, "resourceKey"), folder.Item3.Length == 0 ? name : folder.Item3 + "/" + name, folder.Item4 + 1));
                            continue;
                        }
                        if (!mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;

                        long size;
                        DateTime modified;
                        files.Add(new DriveFile
                        {
                            Id = id, Name = name, Folder = folder.Item3, Md5 = Str(f, "md5Checksum"), ResourceKey = Str(f, "resourceKey"),
                            Size = long.TryParse(Str(f, "size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out size) ? size : 0,
                            Modified = DateTime.TryParse(Str(f, "modifiedTime"), CultureInfo.InvariantCulture,
                                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out modified) ? modified.ToLocalTime() : (DateTime?)null
                        });
                        if (files.Count > MaxFiles)
                            throw new InvalidDataException("The folder holds more than " + MaxFiles + " images. Share the folder with the photos only.");
                    }
                } while (!string.IsNullOrEmpty(pageToken));
            }
            return files;
        }

        // ===================== Plan =====================
        /// <summary>
        /// Matches the Drive files to students and compares them with the Photo Folder (by MD5, so renamed or
        /// re-saved files are not downloaded again). Reads the local photos: run it off the UI thread.
        /// </summary>
        /// <param name="students">The loaded student list; when empty, the file name itself is taken as the StudentId.</param>
        public static List<PhotoSyncItem> Plan(IList<DriveFile> files, string photoFolder, IList<Student> students)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // id → name
            foreach (Student s in students ?? new Student[0])
                if (!string.IsNullOrEmpty(s.StudentId) && !names.ContainsKey(s.StudentId))
                    names[s.StudentId] = string.IsNullOrEmpty(s.NameEn) ? s.NameKh : s.NameEn;

            var items = new List<PhotoSyncItem>(files.Count);
            foreach (DriveFile f in files)
            {
                var item = new PhotoSyncItem { File = f, StudentId = MatchStudent(f.Name, names) };
                string name;
                if (item.StudentId != null && names.TryGetValue(item.StudentId, out name)) item.StudentName = name;

                if (Array.IndexOf(PhotoExtensions, Extension(f.Name)) < 0) item.Status = PhotoSyncStatus.Unsupported;
                else if (item.StudentId == null) item.Status = PhotoSyncStatus.NotMatched;
                else item.Status = CompareWithLocal(f, photoFolder, item.StudentId);
                items.Add(item);
            }

            // Two files for one student (e.g. sent twice through the form): the newest one counts.
            var newest = new Dictionary<string, PhotoSyncItem>(StringComparer.OrdinalIgnoreCase);
            foreach (PhotoSyncItem item in items)
            {
                if (item.StudentId == null || item.Status == PhotoSyncStatus.Unsupported) continue;
                PhotoSyncItem other;
                if (!newest.TryGetValue(item.StudentId, out other)) { newest[item.StudentId] = item; continue; }
                if ((item.File.Modified ?? DateTime.MinValue) > (other.File.Modified ?? DateTime.MinValue))
                {
                    other.Status = PhotoSyncStatus.Duplicate;
                    newest[item.StudentId] = item;
                }
                else item.Status = PhotoSyncStatus.Duplicate;
            }
            return items;
        }

        /// <summary>"NPIC24001.jpg" or "NPIC24001 - Sok Dara.jpg" → "NPIC24001" (the loaded list's spelling), or null.</summary>
        internal static string MatchStudent(string fileName, Dictionary<string, string> students)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName ?? "").Trim();
            if (stem.Length == 0) return null;

            if (students.Count == 0)
            {
                Match m = LeadingId.Match(stem);
                string id = m.Success ? m.Value.Trim('.', '-', '_') : "";
                return id.Length == 0 || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? null : id;
            }

            string best = null;
            foreach (string id in students.Keys)
            {
                if (id.Length > stem.Length || !stem.StartsWith(id, StringComparison.OrdinalIgnoreCase)) continue;
                if (id.Length < stem.Length && char.IsLetterOrDigit(stem[id.Length])) continue;   // NPIC2400 must not match NPIC24001
                if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                if (best == null || id.Length > best.Length) best = id;
            }
            return best;
        }

        private static PhotoSyncStatus CompareWithLocal(DriveFile f, string photoFolder, string studentId)
        {
            string original = BackgroundRemovalForm.FindOriginal(photoFolder, studentId);
            string local = original ?? CardRenderer.FindPhoto(photoFolder, studentId);
            if (local == null) return PhotoSyncStatus.New;
            bool same = SameContent(f, local);
            if (original != null) return same ? PhotoSyncStatus.UpToDate : PhotoSyncStatus.KeptEdited;
            return same ? PhotoSyncStatus.UpToDate : PhotoSyncStatus.Changed;
        }

        private static bool SameContent(DriveFile f, string path)
        {
            try
            {
                if (string.IsNullOrEmpty(f.Md5)) return new FileInfo(path).Length == f.Size;
                return string.Equals(Md5Of(File.ReadAllBytes(path)), f.Md5, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // ===================== Download =====================
        /// <summary>Downloads the planned photos, <see cref="ParallelDownloads"/> at a time. Failures are recorded per item.</summary>
        /// <param name="progress">Called with the number of photos finished so far.</param>
        public static async Task DownloadAsync(IList<PhotoSyncItem> items, string photoFolder, string apiKey,
            IProgress<int> progress, CancellationToken cancel)
        {
            apiKey = CheckKey(apiKey);
            Directory.CreateDirectory(photoFolder);
            int done = 0;
            using (var gate = new SemaphoreSlim(ParallelDownloads))
            {
                var tasks = new List<Task>(items.Count);
                foreach (PhotoSyncItem item in items)
                {
                    try { await gate.WaitAsync(cancel).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }   // stop starting new ones; let the running ones finish
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await DownloadOneAsync(item, photoFolder, apiKey, cancel).ConfigureAwait(false);
                            item.Status = PhotoSyncStatus.Downloaded;
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            item.Status = PhotoSyncStatus.Failed;
                            item.Error = ex.Message;
                        }
                        finally
                        {
                            gate.Release();
                            if (progress != null) progress.Report(Interlocked.Increment(ref done));
                        }
                    }));
                }
                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* reported below */ }
            }
            cancel.ThrowIfCancellationRequested();
        }

        private static async Task DownloadOneAsync(PhotoSyncItem item, string photoFolder, string apiKey, CancellationToken cancel)
        {
            DriveFile f = item.File;
            byte[] bytes = await GetAsync(Api + "/" + Uri.EscapeDataString(f.Id) + "?alt=media&supportsAllDrives=true&key=" +
                                          Uri.EscapeDataString(apiKey), f.Id, f.ResourceKey, cancel).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(f.Md5) && !string.Equals(Md5Of(bytes), f.Md5, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download was incomplete. Try again.");
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (Image.FromStream(ms, false, true)) { }   // make sure it is a picture the cards can use
            }
            catch (ArgumentException)
            {
                throw new InvalidDataException("Not a readable JPG/PNG picture.");
            }

            string ext = Extension(f.Name);
            string target = Path.Combine(photoFolder, item.StudentId + ext);
            string temp = target + ".part";
            File.WriteAllBytes(temp, bytes);
            try
            {
                // Replace the student's old photo, whatever its extension (FindPhoto would otherwise still pick a stale .jpg).
                if (File.Exists(target)) File.Replace(temp, target, null);
                else File.Move(temp, target);
                foreach (string e in PhotoExtensions)
                {
                    string old = Path.Combine(photoFolder, item.StudentId + e);
                    if (e != ext && File.Exists(old)) File.Delete(old);
                }
                if (f.Modified.HasValue) File.SetLastWriteTime(target, f.Modified.Value);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        // ===================== HTTP / JSON =====================
        private static async Task<byte[]> GetAsync(string url, string fileId, string resourceKey, CancellationToken cancel)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                // Links shared before 2021 need their resource key to be readable.
                if (!string.IsNullOrEmpty(resourceKey))
                    request.Headers.TryAddWithoutValidation("X-Goog-Drive-Resource-Keys", fileId + "/" + resourceKey);

                HttpResponseMessage response;
                try
                {
                    response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
                {
                    throw new TimeoutException(string.Format("Google Drive did not answer within {0} seconds.", TimeoutSeconds));
                }

                using (response)
                {
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) throw new HttpRequestException(ErrorMessage((int)response.StatusCode, bytes));
                    return bytes;
                }
            }
        }

        /// <summary>Turns a Drive API error into a sentence that says what to fix.</summary>
        private static string ErrorMessage(int status, byte[] body)
        {
            string message = "", reason = "";
            try
            {
                var error = Json(Encoding.UTF8.GetString(body))["error"] as Dictionary<string, object>;
                if (error != null)
                {
                    message = Str(error, "message");
                    var errors = error.ContainsKey("errors") ? error["errors"] as IList : null;
                    if (errors != null && errors.Count > 0) reason = Str(errors[0] as Dictionary<string, object>, "reason");
                    if (reason.Length == 0) reason = Str(error, "status");
                }
            }
            catch (Exception) { /* not JSON: use the status code only */ }

            if (message.IndexOf("API key not valid", StringComparison.OrdinalIgnoreCase) >= 0 || reason == "keyInvalid")
                return "The API key is not valid. Copy it again from Google Cloud Console → APIs & Services → Credentials.";
            if (reason == "accessNotConfigured" || reason == "SERVICE_DISABLED" || message.IndexOf("has not been used", StringComparison.OrdinalIgnoreCase) >= 0)
                return "The Google Drive API is not turned on for this API key.\n\n" +
                       "In Google Cloud Console open APIs & Services → Library → Google Drive API → Enable, wait a minute, then try again.";
            if (reason == "ipRefererBlocked" || reason == "API_KEY_SERVICE_BLOCKED" || message.IndexOf("blocked", StringComparison.OrdinalIgnoreCase) >= 0)
                return "This API key is restricted and may not call the Google Drive API. Allow the Drive API in the key's restrictions.";
            if (status == 404)
                return "Google Drive could not find the folder or the photo.\n\n" +
                       "Check the link, and share the folder as “Anyone with the link” (Viewer).";
            if (status == 403 && (reason.IndexOf("rateLimit", StringComparison.OrdinalIgnoreCase) >= 0 || reason == "userRateLimitExceeded"))
                return "Google Drive is limiting requests. Wait a minute and try again.";
            if (status == 403 || status == 401)
                return "Google Drive refused access. Share the folder as “Anyone with the link” (Viewer)." +
                       (message.Length > 0 ? "\n\nGoogle says: " + message : "");
            return string.Format("Google Drive answered {0}.{1}", status, message.Length > 0 ? " " + message : "");
        }

        private static string CheckKey(string apiKey)
        {
            apiKey = (apiKey ?? "").Trim();
            if (apiKey.Length == 0)
                throw new ArgumentException("Enter a Google API key. Click “Get a key” to create one (free) in Google Cloud Console.");
            return apiKey;
        }

        private static Dictionary<string, object> Json(string text)
        {
            var obj = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text) as Dictionary<string, object>;
            if (obj == null) throw new InvalidDataException("Google Drive returned an unexpected answer.");
            return obj;
        }

        private static string Str(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }

        private static string Extension(string name)
        {
            return (Path.GetExtension(name ?? "") ?? "").ToLowerInvariant();
        }

        private static string Md5Of(byte[] bytes)
        {
            using (MD5 md5 = MD5.Create())
            {
                var sb = new StringBuilder(32);
                foreach (byte b in md5.ComputeHash(bytes)) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
