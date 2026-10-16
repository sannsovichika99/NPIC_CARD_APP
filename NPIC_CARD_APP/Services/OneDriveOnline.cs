using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NPIC_CARD_APP.Services
{
    /// <summary>The code the user types at microsoft.com/devicelogin to sign the app in.</summary>
    public sealed class DeviceCodeInfo
    {
        public string UserCode { get; set; }
        public string VerificationUri { get; set; }
        internal string DeviceCode { get; set; }
        internal int IntervalSeconds { get; set; }
        internal DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// A Microsoft sign-in for the Graph API: device-code sign-in (no password typed into the app), then an access token
    /// renewed from the refresh token. Works for personal and work / school accounts. Thread-safe for parallel downloads.
    /// </summary>
    public sealed class OneDriveSession
    {
        public const string Scopes = "Files.Read.All User.Read offline_access";
        private const string Authority = "https://login.microsoftonline.com/common/oauth2/v2.0/";

        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private string _accessToken;
        private DateTime _accessExpires;

        public OneDriveSession(string clientId, string refreshToken)
        {
            ClientId = (clientId ?? "").Trim();
            RefreshToken = refreshToken ?? "";
        }

        /// <summary>The App ID the refresh token belongs to.</summary>
        public string ClientId { get; private set; }
        public string RefreshToken { get; private set; }
        public bool SignedIn { get { return RefreshToken.Length > 0 && ClientId.Length > 0; } }

        /// <summary>Raised (on any thread) when the refresh token changes: save it.</summary>
        public event Action<OneDriveSession> TokensChanged;

        // ===================== Sign in =====================
        /// <summary>Asks Microsoft for a sign-in code for <paramref name="clientId"/>.</summary>
        public async Task<DeviceCodeInfo> StartSignInAsync(string clientId, CancellationToken cancel)
        {
            clientId = OneDriveOnlineSync.CheckClientId(clientId);
            var answer = await OneDriveOnlineSync.PostFormAsync(Authority + "devicecode", new Dictionary<string, string>
            {
                { "client_id", clientId }, { "scope", Scopes }
            }, cancel).ConfigureAwait(false);

            int interval, expires;
            return new DeviceCodeInfo
            {
                UserCode = OneDriveOnlineSync.Str(answer, "user_code"),
                VerificationUri = OneDriveOnlineSync.Str(answer, "verification_uri"),
                DeviceCode = OneDriveOnlineSync.Str(answer, "device_code"),
                IntervalSeconds = int.TryParse(OneDriveOnlineSync.Str(answer, "interval"), out interval) && interval > 0 ? interval : 5,
                ExpiresAt = DateTime.UtcNow.AddSeconds(int.TryParse(OneDriveOnlineSync.Str(answer, "expires_in"), out expires) ? expires : 900)
            };
        }

        /// <summary>Waits until the code was entered in the browser; then the session is signed in to <paramref name="clientId"/>.</summary>
        public async Task CompleteSignInAsync(string clientId, DeviceCodeInfo code, CancellationToken cancel)
        {
            clientId = OneDriveOnlineSync.CheckClientId(clientId);
            int interval = code.IntervalSeconds;
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), cancel).ConfigureAwait(false);
                if (DateTime.UtcNow > code.ExpiresAt) throw new TimeoutException("The sign-in code expired. Click Sign In… again.");
                try
                {
                    var answer = await OneDriveOnlineSync.PostFormAsync(Authority + "token", new Dictionary<string, string>
                    {
                        { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" },
                        { "client_id", clientId }, { "device_code", code.DeviceCode }
                    }, cancel).ConfigureAwait(false);
                    ClientId = clientId;
                    Accept(answer);
                    return;
                }
                catch (OneDriveAuthException ex) when (ex.Code == "authorization_pending") { /* not entered yet */ }
                catch (OneDriveAuthException ex) when (ex.Code == "slow_down") { interval += 5; }
            }
        }

        public void SignOut()
        {
            _accessToken = null;
            RefreshToken = "";
            RaiseChanged();
        }

        // ===================== Access token =====================
        /// <summary>A valid access token, renewed from the refresh token when it is about to expire.</summary>
        public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancel)
        {
            if (!SignedIn) throw new InvalidOperationException("Sign in to OneDrive first: click Sign In… in OneDrive Settings.");
            await _lock.WaitAsync(cancel).ConfigureAwait(false);
            try
            {
                if (!forceRefresh && _accessToken != null && DateTime.UtcNow < _accessExpires) return _accessToken;
                try
                {
                    var answer = await OneDriveOnlineSync.PostFormAsync(Authority + "token", new Dictionary<string, string>
                    {
                        { "grant_type", "refresh_token" }, { "client_id", ClientId },
                        { "refresh_token", RefreshToken }, { "scope", Scopes }
                    }, cancel).ConfigureAwait(false);
                    Accept(answer);
                    return _accessToken;
                }
                catch (OneDriveAuthException ex) when (ex.Code == "invalid_grant" || ex.Code == "interaction_required")
                {
                    SignOut();   // expired, revoked or password changed
                    throw new InvalidOperationException("The OneDrive sign-in has expired. Click Sign In… in OneDrive Settings again.");
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private void Accept(Dictionary<string, object> answer)
        {
            _accessToken = OneDriveOnlineSync.Str(answer, "access_token");
            int seconds;
            if (!int.TryParse(OneDriveOnlineSync.Str(answer, "expires_in"), out seconds)) seconds = 3600;
            _accessExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 120));   // renew a little early
            string refresh = OneDriveOnlineSync.Str(answer, "refresh_token");
            if (refresh.Length > 0) RefreshToken = refresh;
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            Action<OneDriveSession> handler = TokensChanged;
            if (handler != null) handler(this);
        }
    }

    /// <summary>An error from the Microsoft sign-in service ("authorization_pending", "invalid_grant", …).</summary>
    public sealed class OneDriveAuthException : Exception
    {
        public OneDriveAuthException(string code, string message) : base(message) { Code = code; }
        public string Code { get; private set; }
    }

    /// <summary>
    /// Reads student photos from OneDrive online with Microsoft Graph: a share link ("Anyone with the link", or shared with the
    /// signed-in account), or a path in the signed-in account's own OneDrive ("/Photos/2025").
    /// </summary>
    public static class OneDriveOnlineSync
    {
        public const int TimeoutSeconds = 120;
        public const string RegisterAppUrl = "https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade";

        private const string Graph = "https://graph.microsoft.com/v1.0/";
        private const int MaxDepth = 5;
        private const int MaxFiles = 20000;

        private static readonly Regex ClientIdPattern = new Regex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".webp", ".heic", ".heif", ".tif", ".tiff"
        };

        // Graph answers /content with a redirect to a pre-signed address: follow it by hand, without the access token.
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NPIC_CARD_APP/1.0");
            return client;
        }

        // ===================== Link =====================
        /// <summary>True for a web link or a "/path" in OneDrive; false for a folder on this PC.</summary>
        public static bool IsOnline(string folder)
        {
            folder = (folder ?? "").Trim();
            return folder.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   folder.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                   (folder.StartsWith("/") && !folder.StartsWith("//"));
        }

        /// <exception cref="ArgumentException">Not a OneDrive / SharePoint link or path.</exception>
        public static void CheckLink(string link)
        {
            link = (link ?? "").Trim();
            if (link.StartsWith("/")) return;
            Uri uri;
            if (!Uri.TryCreate(link, UriKind.Absolute, out uri) ||
                !(uri.Host.EndsWith("1drv.ms", StringComparison.OrdinalIgnoreCase) ||
                  uri.Host.IndexOf("onedrive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  uri.Host.EndsWith("sharepoint.com", StringComparison.OrdinalIgnoreCase) ||
                  uri.Host.EndsWith("live.com", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("This is not a OneDrive folder link.\n\n" +
                    "In OneDrive right-click the folder → Share → Copy link, e.g. https://1drv.ms/f/… or https://…sharepoint.com/…");
        }

        internal static string CheckClientId(string clientId)
        {
            clientId = (clientId ?? "").Trim();
            if (clientId.Length == 0)
                throw new ArgumentException("Enter the App ID (Application client ID) of your app registration. Click “Register App…”.");
            if (!ClientIdPattern.IsMatch(clientId))
                throw new ArgumentException("The App ID looks like 12345678-abcd-…: copy the “Application (client) ID” from the app registration.");
            return clientId;
        }

        /// <summary>Graph address of the folder: a share link is encoded as "u!base64url".</summary>
        private static string FolderAddress(string link)
        {
            link = link.Trim();
            if (link.StartsWith("/"))
            {
                string path = string.Join("/", Array.ConvertAll(link.Trim('/').Split('/'), Uri.EscapeDataString));
                return Graph + "me/drive/root" + (path.Length > 0 ? ":/" + path + ":" : "");
            }
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(link)).TrimEnd('=').Replace('/', '_').Replace('+', '-');
            return Graph + "shares/u!" + encoded + "/driveItem";
        }

        // ===================== Account =====================
        /// <summary>"Sok Dara (dara@school.edu.kh)".</summary>
        public static async Task<string> GetAccountNameAsync(OneDriveSession session, CancellationToken cancel)
        {
            var me = await GetJsonAsync(session, Graph + "me?$select=displayName,userPrincipalName,mail", cancel).ConfigureAwait(false);
            string name = Str(me, "displayName"), mail = Str(me, "mail");
            if (mail.Length == 0) mail = Str(me, "userPrincipalName");
            return name.Length == 0 ? mail : mail.Length == 0 ? name : name + " (" + mail + ")";
        }

        // ===================== List =====================
        /// <summary>Lists every picture in the folder and its sub-folders. <see cref="DriveFile.Id"/> is "driveId/itemId".</summary>
        public static async Task<List<DriveFile>> ListImagesAsync(OneDriveSession session, string link, CancellationToken cancel)
        {
            CheckLink(link);
            var root = await GetJsonAsync(session, FolderAddress(link), cancel).ConfigureAwait(false);
            root = Target(root);
            if (!root.ContainsKey("folder"))
                throw new ArgumentException("This link opens a file, not a folder. Share the folder that holds the photos.");

            var files = new List<DriveFile>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<Tuple<string, string, string, int>>();   // drive id, item id, path, depth
            queue.Enqueue(Tuple.Create(DriveId(root), Str(root, "id"), "", 0));

            while (queue.Count > 0)
            {
                var folder = queue.Dequeue();
                if (!visited.Add(folder.Item1 + "/" + folder.Item2)) continue;
                string url = ItemUrl(folder.Item1, folder.Item2) + "/children?$top=999&$select=id,name,size,lastModifiedDateTime,file,folder,remoteItem,parentReference";
                while (url != null)
                {
                    var page = await GetJsonAsync(session, url, cancel).ConfigureAwait(false);
                    url = page.ContainsKey("@odata.nextLink") ? page["@odata.nextLink"] as string : null;

                    foreach (object o in (page.ContainsKey("value") ? page["value"] as IList : null) ?? new object[0])
                    {
                        var item = o as Dictionary<string, object>;
                        if (item == null) continue;
                        string name = Str(item, "name");
                        item = Target(item);   // a shortcut to a shared folder points elsewhere
                        string driveId = DriveId(item);
                        if (driveId.Length == 0) driveId = folder.Item1;

                        if (item.ContainsKey("folder"))
                        {
                            if (folder.Item4 < MaxDepth)
                                queue.Enqueue(Tuple.Create(driveId, Str(item, "id"), folder.Item3.Length == 0 ? name : folder.Item3 + "/" + name, folder.Item4 + 1));
                            continue;
                        }
                        if (!item.ContainsKey("file") || !ImageExtensions.Contains(Path.GetExtension(name) ?? "")) continue;

                        long size;
                        DateTime modified;
                        files.Add(new DriveFile
                        {
                            Id = driveId + "/" + Str(item, "id"), Name = name, Folder = folder.Item3,
                            Size = long.TryParse(Str(item, "size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out size) ? size : 0,
                            Modified = DateTime.TryParse(Str(item, "lastModifiedDateTime"), CultureInfo.InvariantCulture,
                                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out modified) ? modified.ToLocalTime() : (DateTime?)null
                        });
                        if (files.Count > MaxFiles)
                            throw new InvalidDataException("The folder holds more than " + MaxFiles + " pictures. Share the folder with the photos only.");
                    }
                }
            }
            return files;
        }

        /// <summary>For a shortcut / shared item, the item it points to (which carries its own drive id).</summary>
        private static Dictionary<string, object> Target(Dictionary<string, object> item)
        {
            var remote = item.ContainsKey("remoteItem") ? item["remoteItem"] as Dictionary<string, object> : null;
            return remote ?? item;
        }

        /// <summary>Graph address of an item; an empty drive id means the signed-in account's own OneDrive.</summary>
        private static string ItemUrl(string driveId, string itemId)
        {
            return Graph + (driveId.Length == 0 ? "me/drive" : "drives/" + Uri.EscapeDataString(driveId)) +
                   "/items/" + Uri.EscapeDataString(itemId);
        }

        private static string DriveId(Dictionary<string, object> item)
        {
            var parent = item.ContainsKey("parentReference") ? item["parentReference"] as Dictionary<string, object> : null;
            return Str(parent, "driveId");
        }

        // ===================== Download =====================
        /// <summary>Downloads the planned photos into the Photo Folder. Failures are recorded per item.</summary>
        public static Task DownloadAsync(OneDriveSession session, IList<PhotoSyncItem> items, string photoFolder,
            IProgress<int> progress, CancellationToken cancel)
        {
            return GoogleDrivePhotoSync.SaveAllAsync(items, photoFolder, async (f, ct) =>
            {
                int slash = f.Id.IndexOf('/');
                string url = ItemUrl(f.Id.Substring(0, slash), f.Id.Substring(slash + 1)) + "/content";
                byte[] bytes = await GetBytesAsync(session, url, ct).ConfigureAwait(false);
                if (f.Size > 0 && bytes.Length != f.Size) throw new InvalidDataException("The download was incomplete. Try again.");
                return bytes;
            }, progress, cancel);
        }

        // ===================== HTTP =====================
        private static async Task<Dictionary<string, object>> GetJsonAsync(OneDriveSession session, string url, CancellationToken cancel)
        {
            byte[] body = await GetBytesAsync(session, url, cancel).ConfigureAwait(false);
            return Json(Encoding.UTF8.GetString(body));
        }

        /// <summary>GET with the access token; renews the token once on 401 and follows a download redirect without it.</summary>
        private static async Task<byte[]> GetBytesAsync(OneDriveSession session, string url, CancellationToken cancel)
        {
            for (int attempt = 0; ; attempt++)
            {
                string token = await session.GetAccessTokenAsync(attempt > 0, cancel).ConfigureAwait(false);
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    // Lets a link shared with "people in your organisation" be opened the first time.
                    request.Headers.TryAddWithoutValidation("Prefer", "redeemSharingLinkIfNecessary");
                    using (HttpResponseMessage response = await SendAsync(request, cancel).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400 && response.Headers.Location != null)
                            return await DownloadPresignedAsync(response.Headers.Location, cancel).ConfigureAwait(false);
                        byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode) throw new HttpRequestException(GraphError(response, bytes));
                        return bytes;
                    }
                }
            }
        }

        private static async Task<byte[]> DownloadPresignedAsync(Uri location, CancellationToken cancel)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, location))
            using (HttpResponseMessage response = await SendAsync(request, cancel).ConfigureAwait(false))
            {
                byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException(string.Format("OneDrive answered {0} while downloading the photo.", (int)response.StatusCode));
                return bytes;
            }
        }

        internal static async Task<Dictionary<string, object>> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken cancel)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) })
            using (HttpResponseMessage response = await SendAsync(request, cancel).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Dictionary<string, object> json;
                try { json = Json(body); }
                catch (Exception) { throw new HttpRequestException(string.Format("Microsoft sign-in answered {0}.", (int)response.StatusCode)); }
                if (response.IsSuccessStatusCode) return json;
                string code = Str(json, "error");
                throw new OneDriveAuthException(code, AuthError(code, Str(json, "error_description")));
            }
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            try
            {
                return await Http.SendAsync(request, cancel).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
            {
                throw new TimeoutException(string.Format("OneDrive did not answer within {0} seconds.", TimeoutSeconds));
            }
        }

        /// <summary>Turns a sign-in error into a sentence that says what to fix in the app registration.</summary>
        private static string AuthError(string code, string description)
        {
            string d = description ?? "";
            if (d.Contains("AADSTS700016") || d.Contains("AADSTS90002") || d.Contains("AADSTS50059"))
                return "Microsoft does not know this App ID. Copy the “Application (client) ID” from the app registration again.";
            if (d.Contains("AADSTS7000218") || d.Contains("AADSTS7000215"))
                return "In the app registration open Authentication → “Allow public client flows” → Yes → Save, then sign in again.";
            if (d.Contains("AADSTS50194") || d.Contains("AADSTS50020") || d.Contains("AADSTS9002331"))
                return "The app registration does not allow this kind of account. In Authentication set Supported account types to " +
                       "“Accounts in any organizational directory and personal Microsoft accounts”.";
            if (d.Contains("AADSTS65001") || d.Contains("AADSTS90094"))
                return "Your school's Microsoft 365 needs an administrator to approve the app (Files.Read.All). Ask the IT admin to grant consent.";
            if (code == "authorization_declined") return "Sign-in was cancelled in the browser.";
            if (code == "expired_token") return "The sign-in code expired. Click Sign In… again.";
            string first = d.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length > 0
                ? d.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0] : code;
            return "Microsoft sign-in failed: " + first;
        }

        /// <summary>Turns a Graph error into a sentence that says what to fix.</summary>
        private static string GraphError(HttpResponseMessage response, byte[] body)
        {
            int status = (int)response.StatusCode;
            string code = "", message = "";
            try
            {
                var error = Json(Encoding.UTF8.GetString(body))["error"] as Dictionary<string, object>;
                code = Str(error, "code");
                message = Str(error, "message");
            }
            catch (Exception) { /* not JSON: use the status code only */ }

            if (status == 404 || code == "itemNotFound")
                return "OneDrive could not find the folder or the photo. Check the link, and that it is shared with this account " +
                       "or as “Anyone with the link”.";
            if (status == 403 || code == "accessDenied")
                return "OneDrive refused access. Share the folder with the signed-in account, or create an “Anyone with the link” link." +
                       (message.Length > 0 ? "  OneDrive says: " + message : "");
            if (status == 401)
                return "OneDrive did not accept the sign-in. Click Sign In… in OneDrive Settings again.";
            if (status == 429 || status == 503)
                return "OneDrive is limiting requests. Wait a minute and try again.";
            return string.Format("OneDrive answered {0}.{1}", status, message.Length > 0 ? " " + message : "");
        }

        // ===================== JSON =====================
        private static Dictionary<string, object> Json(string text)
        {
            var obj = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text) as Dictionary<string, object>;
            if (obj == null) throw new InvalidDataException("OneDrive returned an unexpected answer.");
            return obj;
        }

        internal static string Str(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }
    }
}
