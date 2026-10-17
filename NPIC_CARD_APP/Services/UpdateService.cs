using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NPIC_CARD_APP.Services
{
    /// <summary>A published version of Card Studio, read from the update source by <see cref="UpdateService.CheckAsync"/>.</summary>
    public sealed class UpdateInfo
    {
        /// <summary>Always four parts (1.0.3 → 1.0.3.0), so it compares correctly with the running version.</summary>
        public Version Version { get; internal set; }
        /// <summary>"1.0.3" (four parts only when the revision is not 0).</summary>
        public string VersionText { get; internal set; }
        /// <summary>Absolute https:// address or file path of the setup exe.</summary>
        public string DownloadUrl { get; internal set; }
        /// <summary>Upper-case hex SHA-256 of the setup, or null when the source does not give one.</summary>
        public string Sha256 { get; internal set; }
        /// <summary>Release notes (plain text or Markdown), or "".</summary>
        public string Notes { get; internal set; }
        public DateTime? ReleaseDate { get; internal set; }
        /// <summary>Web page of the release (GitHub), or null.</summary>
        public string PageUrl { get; internal set; }
        /// <summary>Size of the setup in bytes, 0 = unknown.</summary>
        public long Size { get; internal set; }

        public bool IsNewerThanRunning { get { return Version > UpdateService.CurrentVersion; } }
    }

    /// <summary>Bytes received so far while downloading an update (<see cref="Total"/> = 0 when unknown).</summary>
    public struct DownloadProgress
    {
        public long Received, Total;
        public DownloadProgress(long received, long total) { Received = received; Total = total; }
    }

    /// <summary>
    /// Check for Updates: reads where the newest Card Studio is published, compares it with the running version, downloads the
    /// setup, checks it and starts it. The setup (CardStudio-Setup.exe /WaitForApp) waits for the app to close, then updates
    /// it in place like any manual upgrade: settings, print history and card images are kept.
    ///
    /// The update source (<see cref="AppSettings.UpdateUrl"/>) is one of:
    ///   • a folder — network share or local — holding update.json and CardStudio-Setup.exe, as written to dist\ by
    ///     Installer\build.ps1:  \\server\share\CardStudio   or   D:\Updates\CardStudio
    ///   • an https:// address of such an update.json (or of the folder holding it)
    ///   • a GitHub repository with releases (public, or reachable without sign-in):
    ///     https://github.com/owner/repo  →  the latest release, whose setup .exe is attached as an asset
    ///
    /// update.json:
    ///   { "version": "1.0.3", "url": "CardStudio-Setup.exe", "sha256": "…64 hex…", "size": 1468006,
    ///     "date": "2026-10-17", "notes": "What changed…" }
    /// "url" may be relative to update.json. Plain http:// is refused: the setup runs as administrator, so it must not be
    /// replaceable on the way. When a SHA-256 is given, a download that does not match it is deleted and never started.
    /// </summary>
    public static class UpdateService
    {
        /// <summary>
        /// Update source used when <see cref="AppSettings.UpdateUrl"/> is empty. Set it to the school's share or web address
        /// before building, so every installed PC checks there without any setup; "" = each PC must enter one.
        /// </summary>
        public const string DefaultSource = "";

        public const string ManifestName = "update.json";

        /// <summary>The setup waits for this app to exit (see CardStudio.Setup SetupOptions).</summary>
        public const string SetupArguments = "/WaitForApp";

        public const int CheckTimeoutSeconds = 20;
        public const int DownloadTimeoutMinutes = 15;

        /// <summary>The startup check runs at most this often.</summary>
        public static readonly TimeSpan StartupCheckInterval = TimeSpan.FromHours(20);

        private static readonly Regex GitHubRepo = new Regex(
            @"^https://github\.com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(?:\.git)?(?:/releases(?:/latest)?)?/?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Hex64 = new Regex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant);

        // Deadlines are set per call with a CancellationTokenSource (check: 20 s, download: 15 min).
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CardStudio/" + CurrentVersionText);   // GitHub requires one
            return client;
        }

        // ===================== Versions =====================

        /// <summary>Version of the running app, from &lt;repo&gt;\Version.cs.</summary>
        public static Version CurrentVersion
        {
            get { return Normalize(typeof(UpdateService).Assembly.GetName().Version); }
        }

        public static string CurrentVersionText { get { return Display(CurrentVersion); } }

        private static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        private static string Display(Version v)
        {
            return v.ToString(v.Revision > 0 ? 4 : 3);
        }

        // ===================== Settings helpers =====================

        /// <summary>The update source to use: the saved one, or <see cref="DefaultSource"/>.</summary>
        public static string SourceOf(AppSettings settings)
        {
            string saved = (settings.UpdateUrl ?? "").Trim();
            return saved.Length > 0 ? saved : DefaultSource;
        }

        /// <summary>True when the startup check should run now (switched on, a source is known, not checked recently).</summary>
        public static bool IsStartupCheckDue(AppSettings settings)
        {
            if (!settings.CheckUpdatesOnStartup || SourceOf(settings).Length == 0) return false;
            DateTime last = settings.LastUpdateCheck;
            return last > DateTime.Now || DateTime.Now - last >= StartupCheckInterval;   // clock turned back: check
        }

        // ===================== Check =====================

        /// <summary>Reads the newest published version from <paramref name="source"/>. Does not compare or download.</summary>
        /// <exception cref="ArgumentException">The source is empty or not usable (e.g. plain http://).</exception>
        /// <exception cref="TimeoutException">No answer within <see cref="CheckTimeoutSeconds"/>.</exception>
        /// <exception cref="InvalidDataException">The answer is not valid update information.</exception>
        public static async Task<UpdateInfo> CheckAsync(string source, CancellationToken cancel)
        {
            bool web;
            string location = ResolveManifest(source, out web);

            string json;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(CheckTimeoutSeconds));
                try
                {
                    json = web
                        ? await ReadWebAsync(location, deadline.Token).ConfigureAwait(false)
                        : await Task.Run(() => ReadManifestFile(location), deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                {
                    throw new TimeoutException(string.Format(
                        "The update source did not answer within {0} seconds: {1}", CheckTimeoutSeconds, location));
                }
            }
            return Parse(json, location, web);
        }

        /// <summary>Turns what the user typed into the address of the update information; web = it is an https:// address.</summary>
        private static string ResolveManifest(string source, out bool web)
        {
            source = (source ?? "").Trim().Trim('"');
            if (source.Length == 0)
                throw new ArgumentException("No update address is set. Enter the folder or https:// address where new versions are published.");

            Uri uri;
            if (Uri.TryCreate(source, UriKind.Absolute, out uri) && uri.Scheme != Uri.UriSchemeFile)
            {
                CheckWebScheme(uri);
                web = true;

                Match gh = GitHubRepo.Match(source);
                if (gh.Success)
                    return "https://api.github.com/repos/" + gh.Groups[1].Value + "/" + gh.Groups[2].Value + "/releases/latest";

                // https://host/folder or https://host/folder/ → its update.json; a file name or an API address is kept.
                string lastSegment = uri.Segments.Length == 0 ? "" : uri.Segments[uri.Segments.Length - 1];
                bool isFile = lastSegment.Contains(".") || uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase);
                if (isFile || uri.Query.Length > 0) return uri.AbsoluteUri;
                return new Uri(new Uri(uri.AbsoluteUri.TrimEnd('/') + "/"), ManifestName).AbsoluteUri;
            }

            web = false;
            string path = uri != null && uri.IsFile ? uri.LocalPath : source;
            try
            {
                path = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw new ArgumentException("This is neither an https:// address nor a folder path: " + source, ex);
            }
            return Directory.Exists(path) ? Path.Combine(path, ManifestName) : path;
        }

        private static void CheckWebScheme(Uri uri)
        {
            if (uri.Scheme == Uri.UriSchemeHttps) return;
            if (uri.Scheme == Uri.UriSchemeHttp)
                throw new ArgumentException("Plain http:// addresses are not allowed for updates: the setup runs as administrator, " +
                                            "so it must come over https:// or from a network folder.\n" + uri.AbsoluteUri);
            throw new ArgumentException("Use an https:// address or a folder path for updates, not " + uri.Scheme + ":");
        }

        private static async Task<string> ReadWebAsync(string url, CancellationToken cancel)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };   // never an old update.json
                using (HttpResponseMessage response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancel)
                           .ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        throw new FileNotFoundException(url.Contains("api.github.com")
                            ? "No published release was found on GitHub (404). The repository may be private, or it has no release yet:\n" + url
                            : "No update information was found at this address (404):\n" + url);
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException(string.Format("The update server answered {0} {1}:\n{2}",
                            (int)response.StatusCode, response.ReasonPhrase, url));
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>SendAsync with the real network error ("remote name could not be resolved") instead of a generic one.</summary>
        private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion,
            CancellationToken cancel)
        {
            try
            {
                return await Http.SendAsync(request, completion, cancel).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.InnerException is WebException)
            {
                throw new HttpRequestException("Could not reach " + request.RequestUri.Host + ": " + ex.InnerException.Message, ex);
            }
        }

        private static string ReadManifestFile(string path)
        {
            if (!File.Exists(path))
            {
                // No extension = the user meant a folder (that does not exist / cannot be reached).
                string folder = Path.GetExtension(path).Length == 0 ? path : Path.GetDirectoryName(path);
                throw new FileNotFoundException(folder != path && folder != null && Directory.Exists(folder)
                    ? "There is no " + Path.GetFileName(path) + " in " + folder + ".\nCopy the update.json and CardStudio-Setup.exe from dist\\ there."
                    : "The update folder cannot be reached: " + (folder ?? path) + "\nCheck the network connection and the address.", path);
            }
            return File.ReadAllText(path, Encoding.UTF8);
        }

        // ===================== Parse =====================

        private static UpdateInfo Parse(string json, string location, bool web)
        {
            Dictionary<string, object> obj;
            try
            {
                obj = new JavaScriptSerializer().DeserializeObject(json ?? "") as Dictionary<string, object>;
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException("The update information is not valid JSON:\n" + location, ex);
            }
            if (obj == null) throw new InvalidDataException("The update information is empty or not a JSON object:\n" + location);

            var info = new UpdateInfo();
            string versionText;
            string download;
            if (obj.ContainsKey("tag_name"))
            {
                // GitHub "latest release": version from the tag (v1.0.3), the setup from the attached .exe asset.
                versionText = Str(obj, "tag_name");
                info.Notes = Str(obj, "body");
                info.PageUrl = Str(obj, "html_url");
                info.ReleaseDate = Date(Str(obj, "published_at"));
                Dictionary<string, object> asset = PickSetupAsset(obj);
                if (asset == null)
                    throw new InvalidDataException("The latest GitHub release (" + versionText + ") has no setup .exe attached.");
                download = Str(asset, "browser_download_url");
                info.Size = Long(asset, "size");
                string digest = Str(asset, "digest");   // "sha256:…", given by GitHub for newer uploads
                if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) info.Sha256 = digest.Substring(7);
            }
            else
            {
                versionText = Str(obj, "version");
                download = Str(obj, "url");
                info.Sha256 = Str(obj, "sha256");
                info.Notes = Str(obj, "notes");
                info.ReleaseDate = Date(Str(obj, "date"));
                info.Size = Long(obj, "size");
            }

            Version version;
            versionText = versionText.Trim().TrimStart('v', 'V');
            if (versionText.Length == 0) throw new InvalidDataException("The update information has no \"version\":\n" + location);
            if (versionText.IndexOf('.') < 0) versionText += ".0";   // "2" → 2.0
            if (!Version.TryParse(versionText, out version))
                throw new InvalidDataException("\"" + versionText + "\" is not a version number (expected e.g. 1.0.3):\n" + location);
            info.Version = Normalize(version);
            info.VersionText = Display(info.Version);

            if (download.Length == 0) throw new InvalidDataException("The update information has no \"url\" of the setup:\n" + location);
            info.DownloadUrl = ResolveDownload(download, location, web);

            if (!string.IsNullOrEmpty(info.Sha256))
            {
                info.Sha256 = info.Sha256.Replace(" ", "").ToUpperInvariant();
                if (!Hex64.IsMatch(info.Sha256))
                    throw new InvalidDataException("\"sha256\" must be 64 hexadecimal characters:\n" + location);
            }
            else info.Sha256 = null;

            return info;
        }

        private static Dictionary<string, object> PickSetupAsset(Dictionary<string, object> release)
        {
            object list;
            var assets = release.TryGetValue("assets", out list) ? list as object[] : null;
            if (assets == null) return null;
            Dictionary<string, object> anyExe = null;
            foreach (object item in assets)
            {
                var asset = item as Dictionary<string, object>;
                string name = asset == null ? "" : Str(asset, "name");
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0) return asset;
                if (anyExe == null) anyExe = asset;
            }
            return anyExe;
        }

        /// <summary>"url" of update.json: absolute, or relative to update.json itself.</summary>
        private static string ResolveDownload(string url, string location, bool web)
        {
            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                if (uri.IsFile) return uri.LocalPath;   // C:\…, \\server\share\…, file://…
                CheckWebScheme(uri);
                return uri.AbsoluteUri;
            }
            if (web) return new Uri(new Uri(location), url).AbsoluteUri;
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(location) ?? "", url));
        }

        private static string Str(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }

        private static long Long(Dictionary<string, object> obj, string key)
        {
            object value;
            if (!obj.TryGetValue(key, out value) || value == null) return 0;
            try { return Math.Max(0, Convert.ToInt64(value, CultureInfo.InvariantCulture)); }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException) { return 0; }
        }

        private static DateTime? Date(string text)
        {
            DateTime d;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d)
                ? d.ToLocalTime() : (DateTime?)null;
        }

        // ===================== Download =====================

        /// <summary>%TEMP%\CardStudio-Update: downloaded setups (old ones are removed by the next download).</summary>
        public static string DownloadFolder { get { return Path.Combine(Path.GetTempPath(), "CardStudio-Update"); } }

        /// <summary>
        /// Downloads (or copies, for a folder source) the setup of <paramref name="info"/> to <see cref="DownloadFolder"/> and
        /// checks it. Returns the path of the checked setup.
        /// </summary>
        /// <exception cref="InvalidDataException">The file does not match its SHA-256, or is not a Windows program.</exception>
        public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<DownloadProgress> progress, CancellationToken cancel)
        {
            Directory.CreateDirectory(DownloadFolder);
            foreach (string old in Directory.GetFiles(DownloadFolder))
                TryDelete(old);   // earlier downloads; one still running is locked and simply stays

            string target = Path.Combine(DownloadFolder, "CardStudio-Setup-" + info.VersionText + ".exe");
            string partial = target + ".part";

            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(DownloadTimeoutMinutes));
                try
                {
                    Uri uri;
                    bool web = Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out uri) && !uri.IsFile;
                    if (web) await DownloadWebAsync(info, partial, progress, deadline.Token).ConfigureAwait(false);
                    else await CopyFileAsync(info.DownloadUrl, partial, progress, deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                {
                    TryDelete(partial);
                    throw new TimeoutException(string.Format("The download did not finish within {0} minutes.", DownloadTimeoutMinutes));
                }
                catch
                {
                    TryDelete(partial);
                    throw;
                }
            }

            try
            {
                VerifySetup(partial, info);
                TryDelete(target);
                File.Move(partial, target);
            }
            catch
            {
                TryDelete(partial);
                throw;
            }
            return target;
        }

        private static async Task DownloadWebAsync(UpdateInfo info, string partial, IProgress<DownloadProgress> progress,
            CancellationToken cancel)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl))
            using (HttpResponseMessage response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel)
                       .ConfigureAwait(false))
            {
                // Redirects (GitHub → its file host) are followed by HttpClient; make sure they stayed on https.
                if (response.RequestMessage != null && response.RequestMessage.RequestUri != null)
                    CheckWebScheme(response.RequestMessage.RequestUri);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException(string.Format("The setup could not be downloaded: the server answered {0} {1}.\n{2}",
                        (int)response.StatusCode, response.ReasonPhrase, info.DownloadUrl));

                long total = response.Content.Headers.ContentLength ?? info.Size;
                using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    await CopyAsync(source, target, total, progress, cancel).ConfigureAwait(false);
            }
        }

        private static async Task CopyFileAsync(string path, string partial, IProgress<DownloadProgress> progress, CancellationToken cancel)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("The setup named in update.json was not found:\n" + path, path);
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                await CopyAsync(source, target, source.Length, progress, cancel).ConfigureAwait(false);
        }

        private static async Task CopyAsync(Stream source, Stream target, long total, IProgress<DownloadProgress> progress,
            CancellationToken cancel)
        {
            var buffer = new byte[81920];
            long received = 0;
            var lastReport = Stopwatch.StartNew();
            if (progress != null) progress.Report(new DownloadProgress(0, total));
            int read;
            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer, 0, read, cancel).ConfigureAwait(false);
                received += read;
                if (progress != null && lastReport.ElapsedMilliseconds >= 100)
                {
                    progress.Report(new DownloadProgress(received, total));
                    lastReport.Restart();
                }
            }
            if (progress != null) progress.Report(new DownloadProgress(received, Math.Max(total, received)));
        }

        /// <summary>The download must be a Windows program and, when the source gives a SHA-256, exactly the published file.</summary>
        private static void VerifySetup(string path, UpdateInfo info)
        {
            using (FileStream fs = File.OpenRead(path))
            {
                if (fs.Length < 2 || fs.ReadByte() != 'M' || fs.ReadByte() != 'Z')
                    throw new InvalidDataException("The downloaded file is not a Windows program (the server may have sent an error page). " +
                                                   "Nothing was installed.");
                if (info.Sha256 == null) return;

                fs.Position = 0;
                string actual;
                using (SHA256 sha = SHA256.Create())
                    actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
                if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded setup does not match the published SHA-256 checksum: it is damaged or " +
                                                   "not the published file. It was deleted and nothing was installed.\n\n" +
                                                   "Expected " + info.Sha256 + "\nReceived " + actual);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // ===================== Install =====================

        /// <summary>
        /// Starts the downloaded setup (Windows asks for administrator approval). The caller must then close the app: the setup
        /// waits up to 30 s for it. Returns false when the user answered No to the approval prompt.
        /// </summary>
        public static bool StartSetup(string setupPath)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(setupPath, SetupArguments)
                {
                    UseShellExecute = true,   // needed for the setup's requireAdministrator manifest (UAC prompt)
                    WorkingDirectory = Path.GetDirectoryName(setupPath)
                }))
                {
                    return true;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)   // ERROR_CANCELLED: No on the UAC prompt
            {
                return false;
            }
        }
    }
}
