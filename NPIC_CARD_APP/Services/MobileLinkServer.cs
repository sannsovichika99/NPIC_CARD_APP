using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// What the Card Studio mobile app needs from the main window. Every member is called on a server thread:
    /// the main window marshals to its UI thread itself.
    /// </summary>
    public interface IMobileLinkHost
    {
        /// <summary>A copy of the loaded student list (the list itself only changes on the UI thread).</summary>
        List<Student> Students();
        /// <summary>Where the list came from (file name, API host…), "" when nothing is loaded.</summary>
        string Source();
        AppSettings Settings();
        /// <summary>StudentId → last time a side of the card was printed.</summary>
        Dictionary<string, DateTime> LastPrinted();
        /// <summary>The card side drawn with the design in use, as a JPEG no wider than <paramref name="maxWidth"/> px.</summary>
        byte[] RenderCard(Student s, bool front, int maxWidth);
        /// <summary>A photo for <paramref name="s"/> was saved to <paramref name="path"/> from the phone.</summary>
        void PhotoReceived(Student s, string path, string device);
        /// <summary>
        /// The phone asked to print one side of <paramref name="s"/>'s card: Card Studio selects the student and side and
        /// starts Print Selected, so the person at the PC confirms the tray load and inserts the tray as usual.
        /// Returns null when the print was started, or why it cannot start now (another window is open on the PC…).
        /// </summary>
        string RequestPrint(Student s, bool front, string device);
    }

    /// <summary>One line of the Mobile App page's activity list.</summary>
    public class MobileLinkEvent
    {
        public DateTime At { get; set; }
        public string Device { get; set; }
        public string Text { get; set; }
        public bool Problem { get; set; }
    }

    /// <summary>
    /// Small HTTP/1.1 + JSON server on the local network for the Card Studio mobile app (folder "mobile" of the repository).
    /// Built on <see cref="TcpListener"/> instead of HttpListener, so it needs no admin rights or URL reservation; Windows
    /// Firewall asks once whether Card Studio may accept connections (or use Allow in Firewall on the Mobile App page).
    /// <para>Every /api call except /api/ping needs the pairing code, sent as "Authorization: Bearer code" (or ?token=code
    /// for images). The phone gets the code by scanning the QR code on the Mobile App page. After 10 wrong codes from one
    /// address that address is refused for a minute.</para>
    /// <list type="table">
    /// <item><term>GET /api/ping</term><description>App name, version, PC name (no code needed)</description></item>
    /// <item><term>GET /api/heartbeat</term><description>Keeps the phone listed as connected on the PC (sent every 30 s)</description></item>
    /// <item><term>GET /api/info</term><description>Counts, list source, card design, accent colour</description></item>
    /// <item><term>GET /api/students?q=&amp;offset=&amp;limit=&amp;filter=nophoto|notprinted|nocard</term><description>Loaded students</description></item>
    /// <item><term>GET /api/students/{id}</term><description>One student with every column</description></item>
    /// <item><term>GET|PUT /api/students/{id}/photo</term><description>Read or replace the photo (JPEG / PNG body)</description></item>
    /// <item><term>GET /api/students/{id}/card?side=front|back</term><description>The card as a JPEG</description></item>
    /// <item><term>POST /api/students/{id}/print?side=front|back</term><description>Starts Print Selected for that card side on the PC</description></item>
    /// <item><term>GET /api/lookup?code=</term><description>Student from a scanned card QR / ID</description></item>
    /// <item><term>GET /api/history?limit=</term><description>Latest printed card sides</description></item>
    /// </list>
    /// </summary>
    public sealed class MobileLinkServer : IDisposable
    {
        public const int DefaultPort = 8765;
        public const string ApiVersion = "1";

        private const int MaxHeaderBytes = 16 * 1024;
        private const int MaxBodyBytes = 15 * 1024 * 1024;     // a 12 MP phone photo is 3–8 MB
        private const int MaxConnections = 24;
        private const int IoTimeoutMs = 20000;
        private const int MaxBadTokens = 10;
        private static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(1);
        private const int MaxEvents = 200;
        /// <summary>A phone that sent nothing for this long no longer counts as connected (it sends a heartbeat every 30 s).</summary>
        public static readonly TimeSpan PhoneTimeout = TimeSpan.FromSeconds(90);
        public const string ReplacedFolderName = "_replaced";

        private readonly IMobileLinkHost _host;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
        private readonly SemaphoreSlim _slots = new SemaphoreSlim(MaxConnections);
        private readonly Dictionary<string, BadTokens> _bad = new Dictionary<string, BadTokens>();
        private readonly List<MobileLinkEvent> _events = new List<MobileLinkEvent>();
        private readonly Dictionary<string, SeenPhone> _phones = new Dictionary<string, SeenPhone>();   // Request.Device → last request
        private readonly object _lock = new object();
        private TcpListener _listener;
        private CancellationTokenSource _stop;
        private string _token;

        private sealed class BadTokens { public int Count; public DateTime Since; }
        private sealed class SeenPhone { public string Name; public DateTime At; }

        /// <summary>Raised on a server thread for every activity line (a phone connected, a photo arrived, a wrong code…).</summary>
        public event Action<MobileLinkEvent> Activity;

        /// <summary>
        /// Raised on a server thread when a paired phone shows up, or when the server stops / the pairing code changes.
        /// A phone that goes quiet is not announced: read <see cref="ConnectedPhones"/> again from time to time.
        /// </summary>
        public event Action PhonesChanged;

        public MobileLinkServer(IMobileLinkHost host)
        {
            if (host == null) throw new ArgumentNullException("host");
            _host = host;
        }

        public bool Running { get { return _listener != null; } }
        public int Port { get; private set; }

        /// <summary>Names of the paired phones heard from within <see cref="PhoneTimeout"/>, sorted.</summary>
        public List<string> ConnectedPhones()
        {
            DateTime now = DateTime.UtcNow;
            lock (_lock)
            {
                foreach (string key in _phones.Where(p => now - p.Value.At > PhoneTimeout).Select(p => p.Key).ToList())
                    _phones.Remove(key);
                return _phones.Values.Select(p => p.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
        }

        /// <summary>The latest activity lines, oldest first.</summary>
        public List<MobileLinkEvent> RecentEvents()
        {
            lock (_lock) return _events.ToList();
        }

        /// <summary>Starts listening on every network of this PC. Throws <see cref="SocketException"/> when the port is taken.</summary>
        public void Start(int port, string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length < 16) throw new ArgumentException("The pairing code is too short.");
            Stop();
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start(64);
            _listener = listener;
            _token = token;
            Port = port;
            _stop = new CancellationTokenSource();
            CancellationToken cancel = _stop.Token;
            Task.Run(() => AcceptLoop(listener, cancel));
            Log(null, "Started on port " + port, false);
        }

        public void Stop()
        {
            TcpListener listener = _listener;
            if (listener == null) return;
            _listener = null;
            try { _stop.Cancel(); } catch (ObjectDisposedException) { }
            try { listener.Stop(); } catch (SocketException) { }
            Log(null, "Stopped", false);
            ForgetPhones();
        }

        /// <summary>A new pairing code: phones paired with the old one must scan the QR code again.</summary>
        public void ChangeToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length < 16) throw new ArgumentException("The pairing code is too short.");
            _token = token;
            lock (_lock) _bad.Clear();
            Log(null, "New pairing code: phones must scan the QR code again", false);
            ForgetPhones();   // they cannot connect with the old code any more
        }

        public void Dispose()
        {
            Stop();
        }

        // ===================== Pairing =====================

        /// <summary>A new random pairing code: 26 characters from an alphabet without look-alikes (≈ 130 bits).</summary>
        public static string NewToken()
        {
            const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            var bytes = new byte[26];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(bytes.Length);
            foreach (byte b in bytes) sb.Append(alphabet[b % alphabet.Length]);
            return sb.ToString();
        }

        /// <summary>
        /// IPv4 addresses of this PC that a phone on the same Wi-Fi can reach: networks that are up, not loopback or tunnels,
        /// those with a default gateway (the real LAN) first.
        /// </summary>
        public static List<string> LocalAddresses()
        {
            var ranked = new List<KeyValuePair<int, string>>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    IPInterfaceProperties props = nic.GetIPProperties();
                    bool gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork &&
                                                                  !g.Address.Equals(IPAddress.Any));
                    string desc = (nic.Description + " " + nic.Name).ToLowerInvariant();
                    bool virtualNic = desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("hyper-v") ||
                                      desc.Contains("vethernet") || desc.Contains("wsl") || desc.Contains("docker");
                    foreach (UnicastIPAddressInformation ip in props.UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] a = ip.Address.GetAddressBytes();
                        if (a[0] == 127 || (a[0] == 169 && a[1] == 254)) continue;   // loopback, no DHCP answer
                        int rank = (gateway ? 0 : 2) + (virtualNic ? 1 : 0);
                        ranked.Add(new KeyValuePair<int, string>(rank, ip.Address.ToString()));
                    }
                }
            }
            catch (NetworkInformationException) { }
            return ranked.OrderBy(r => r.Key).Select(r => r.Value).Distinct().ToList();
        }

        /// <summary>
        /// Text of the pairing QR code: npiccard://pair?h=192.168.1.20,10.0.0.5&amp;p=8765&amp;t=CODE&amp;n=PC-NAME.
        /// The phone tries the addresses in order.
        /// </summary>
        public static string PairingUri(IEnumerable<string> hosts, int port, string token)
        {
            return "npiccard://pair?h=" + Uri.EscapeDataString(string.Join(",", hosts)) +
                   "&p=" + port.ToString(CultureInfo.InvariantCulture) +
                   "&t=" + Uri.EscapeDataString(token) +
                   "&n=" + Uri.EscapeDataString(Environment.MachineName);
        }

        // ===================== Connections =====================
        private async Task AcceptLoop(TcpListener listener, CancellationToken cancel)
        {
            while (!cancel.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (cancel.IsCancellationRequested) return; continue; }
                catch (InvalidOperationException) { return; }   // listener stopped

                if (!await _slots.WaitAsync(0).ConfigureAwait(false))
                {
                    client.Close();   // too busy: the phone retries
                    continue;
                }
                _ = Task.Run(async () =>   // one task per connection; the loop goes straight back to accepting
                {
                    try { await Serve(client).ConfigureAwait(false); }
                    catch { /* a broken connection never stops the server */ }
                    finally
                    {
                        client.Close();
                        _slots.Release();
                    }
                });
            }
        }

        private sealed class Request
        {
            public string Method, Path, Remote, Device, Name;   // Device = "Name (address)", Name = phone name or address
            public Dictionary<string, string> Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public byte[] Body = new byte[0];

            public string Q(string key)
            {
                string v;
                return Query.TryGetValue(key, out v) ? v : "";
            }

            public int QInt(string key, int fallback, int min, int max)
            {
                int v;
                if (!int.TryParse(Q(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return fallback;
                return Math.Max(min, Math.Min(max, v));
            }
        }

        private sealed class Response
        {
            public int Status = 200;
            public string ContentType = "application/json; charset=utf-8";
            public byte[] Body = new byte[0];
            public string CacheControl = "no-store";
        }

        private sealed class HttpError : Exception
        {
            public readonly int Status;
            public HttpError(int status, string message) : base(message) { Status = status; }
        }

        private async Task Serve(TcpClient client)
        {
            client.ReceiveTimeout = IoTimeoutMs;
            client.SendTimeout = IoTimeoutMs;
            NetworkStream stream = client.GetStream();
            string remote = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

            Response response;
            Request request = null;
            try
            {
                request = await ReadRequest(stream, remote).ConfigureAwait(false);
                if (request == null) return;   // connection closed before a request
                response = Handle(request);
            }
            catch (HttpError ex)
            {
                response = JsonError(ex.Status, ex.Message);
            }
            catch (IOException) { return; }
            catch (Exception ex)
            {
                Log(request == null ? remote : request.Device, "Error: " + ex.Message.Split('\n')[0], true);
                response = JsonError(500, ex.Message.Split('\n')[0]);
            }
            await WriteResponse(stream, response, request != null && request.Method == "HEAD").ConfigureAwait(false);
        }

        private static async Task<Request> ReadRequest(NetworkStream stream, string remote)
        {
            // ---- headers: read until the blank line ----
            var buffer = new byte[8192];
            var head = new MemoryStream();
            int headerEnd = -1;
            using (var timeout = new CancellationTokenSource(IoTimeoutMs))
            {
                while (headerEnd < 0)
                {
                    int n = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false);
                    if (n == 0) return head.Length == 0 ? null : Fail(400, "Incomplete request.");
                    head.Write(buffer, 0, n);
                    headerEnd = IndexOf(head.GetBuffer(), (int)head.Length, new byte[] { 13, 10, 13, 10 });
                    if (headerEnd < 0 && head.Length > MaxHeaderBytes) Fail(431, "Request headers are too large.");
                }
            }

            byte[] raw = head.ToArray();
            string[] lines = Encoding.ASCII.GetString(raw, 0, headerEnd).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 3) Fail(400, "Bad request line.");

            var req = new Request { Method = first[0].ToUpperInvariant(), Remote = remote };
            string target = first[1];
            int qm = target.IndexOf('?');
            req.Path = Uri.UnescapeDataString(qm < 0 ? target : target.Substring(0, qm)).TrimEnd('/');
            if (req.Path.Length == 0) req.Path = "/";
            if (qm >= 0)
            {
                foreach (string pair in target.Substring(qm + 1).Split('&'))
                {
                    if (pair.Length == 0) continue;
                    int eq = pair.IndexOf('=');
                    string k = Uri.UnescapeDataString((eq < 0 ? pair : pair.Substring(0, eq)).Replace('+', ' '));
                    string v = eq < 0 ? "" : Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
                    req.Query[k] = v;
                }
            }
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) req.Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }

            string device;
            bool named = req.Headers.TryGetValue("X-Device-Name", out device) && device.Length > 0;
            req.Name = named ? Clip(device, 40) : remote;
            req.Device = named ? req.Name + " (" + remote + ")" : remote;

            // ---- body (Content-Length only; the app never sends chunked bodies) ----
            string lengthText;
            if (req.Headers.TryGetValue("Content-Length", out lengthText))
            {
                long length;
                if (!long.TryParse(lengthText, out length) || length < 0) Fail(400, "Bad Content-Length.");
                if (length > MaxBodyBytes) Fail(413, "The photo is larger than 15 MB.");
                var body = new byte[length];
                int have = Math.Min(raw.Length - (headerEnd + 4), (int)length);
                Buffer.BlockCopy(raw, headerEnd + 4, body, 0, have);
                using (var timeout = new CancellationTokenSource(IoTimeoutMs * 3))
                {
                    while (have < length)
                    {
                        int n = await stream.ReadAsync(body, have, (int)length - have, timeout.Token).ConfigureAwait(false);
                        if (n == 0) Fail(400, "The upload was cut off.");
                        have += n;
                    }
                }
                req.Body = body;
            }
            else if (req.Headers.TryGetValue("Transfer-Encoding", out lengthText) &&
                     lengthText.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Some phone HTTP stacks stream uploads in chunks instead of sending a Content-Length.
                var leftover = new MemoryStream();
                leftover.Write(raw, headerEnd + 4, raw.Length - (headerEnd + 4));
                req.Body = await ReadChunked(stream, leftover.ToArray()).ConfigureAwait(false);
            }
            else if (req.Headers.ContainsKey("Transfer-Encoding"))
            {
                Fail(411, "Send the upload with a Content-Length.");
            }
            return req;
        }

        /// <summary>Decodes a "Transfer-Encoding: chunked" body: hex size line, data, CRLF … until a 0-size chunk.</summary>
        private static async Task<byte[]> ReadChunked(NetworkStream stream, byte[] already)
        {
            var input = new MemoryStream();
            input.Write(already, 0, already.Length);
            var body = new MemoryStream();
            int pos = 0;
            var buffer = new byte[16384];
            using (var timeout = new CancellationTokenSource(IoTimeoutMs * 3))
            {
                Func<int, Task> need = async count =>   // makes sure input holds `count` bytes after pos
                {
                    while (input.Length - pos < count)
                    {
                        int n = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false);
                        if (n == 0) Fail(400, "The upload was cut off.");
                        input.Write(buffer, 0, n);
                        if (input.Length > MaxBodyBytes + 64 * 1024) Fail(413, "The photo is larger than 15 MB.");
                    }
                };
                while (true)
                {
                    int lineEnd;
                    while ((lineEnd = IndexOf(input.GetBuffer(), (int)input.Length, new byte[] { 13, 10 }, pos)) < 0)
                        await need((int)(input.Length - pos) + 1).ConfigureAwait(false);
                    string sizeText = Encoding.ASCII.GetString(input.GetBuffer(), pos, lineEnd - pos).Split(';')[0].Trim();
                    int size;
                    if (!int.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out size) || size < 0)
                        Fail(400, "Bad chunked upload.");
                    pos = lineEnd + 2;
                    if (size == 0) break;   // trailers (if any) are ignored: the connection closes after the answer
                    if (body.Length + size > MaxBodyBytes) Fail(413, "The photo is larger than 15 MB.");
                    await need(size + 2).ConfigureAwait(false);
                    body.Write(input.GetBuffer(), pos, size);
                    pos += size + 2;
                }
            }
            return body.ToArray();
        }

        private static async Task WriteResponse(NetworkStream stream, Response r, bool headOnly)
        {
            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(r.Status).Append(' ').Append(Reason(r.Status)).Append("\r\n");
            head.Append("Content-Type: ").Append(r.ContentType).Append("\r\n");
            head.Append("Content-Length: ").Append(r.Body.Length).Append("\r\n");
            head.Append("Cache-Control: ").Append(r.CacheControl).Append("\r\n");
            // Lets the app also run in a browser (Expo web) for testing.
            head.Append("Access-Control-Allow-Origin: *\r\n");
            head.Append("Access-Control-Allow-Headers: Authorization, Content-Type, X-Device-Name\r\n");
            head.Append("Access-Control-Allow-Methods: GET, PUT, POST, OPTIONS\r\n");
            head.Append("Connection: close\r\n\r\n");
            byte[] bytes = Encoding.ASCII.GetBytes(head.ToString());
            await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            if (!headOnly && r.Body.Length > 0) await stream.WriteAsync(r.Body, 0, r.Body.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        // ===================== Routing =====================
        private Response Handle(Request req)
        {
            if (req.Method == "OPTIONS") return new Response { Status = 204 };
            if (req.Path == "/api/ping" || req.Path == "/") return Json(Ping());
            if (!req.Path.StartsWith("/api/")) throw new HttpError(404, "Not found.");

            Authorize(req);

            string[] parts = req.Path.Substring(5).Split('/');   // "/api/students/X/photo" → students, X, photo
            bool get = req.Method == "GET" || req.Method == "HEAD";
            switch (parts[0])
            {
                case "heartbeat":
                    if (get && parts.Length == 1) return Json(new Dictionary<string, object> { { "ok", true } });
                    break;
                case "info":
                    if (get && parts.Length == 1) return Json(Info(req));
                    break;
                case "students":
                    if (parts.Length == 1 && get) return Json(ListStudents(req));
                    if (parts.Length == 2 && get) return Json(Detail(Find(parts[1]), Status()));
                    if (parts.Length == 3 && parts[2] == "photo" && get) return Photo(Find(parts[1]));
                    if (parts.Length == 3 && parts[2] == "photo" && (req.Method == "PUT" || req.Method == "POST"))
                        return Json(SavePhoto(req, Find(parts[1])));
                    if (parts.Length == 3 && parts[2] == "card" && get) return Card(req, Find(parts[1]));
                    if (parts.Length == 3 && parts[2] == "print" && req.Method == "POST") return Json(Print(req, Find(parts[1])));
                    break;
                case "lookup":
                    if (get && parts.Length == 1) return Json(Lookup(req));
                    break;
                case "history":
                    if (get && parts.Length == 1) return Json(History(req));
                    break;
            }
            throw new HttpError(get ? 404 : 405, get ? "Not found." : "Method not allowed.");
        }

        /// <summary>Checks the pairing code in constant time, and locks out an address that keeps guessing.</summary>
        private void Authorize(Request req)
        {
            string sent = req.Q("token");
            string auth;
            if (req.Headers.TryGetValue("Authorization", out auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                sent = auth.Substring(7).Trim();

            lock (_lock)
            {
                BadTokens bad;
                if (_bad.TryGetValue(req.Remote, out bad) && bad.Count >= MaxBadTokens)
                {
                    if (DateTime.UtcNow - bad.Since < LockoutTime)
                        throw new HttpError(429, "Too many wrong pairing codes. Wait a minute, then scan the QR code again.");
                    _bad.Remove(req.Remote);
                }
            }

            if (!SameToken(sent, _token))
            {
                lock (_lock)
                {
                    BadTokens bad;
                    if (!_bad.TryGetValue(req.Remote, out bad)) _bad[req.Remote] = bad = new BadTokens { Since = DateTime.UtcNow };
                    if (bad.Count == 0) bad.Since = DateTime.UtcNow;
                    bad.Count++;
                    if (bad.Count == 1 || bad.Count == MaxBadTokens)
                        Log(req.Device, bad.Count == MaxBadTokens ? "Refused for 1 minute: too many wrong pairing codes"
                                                                  : "Refused: wrong or old pairing code", true);
                }
                throw new HttpError(401, "Wrong pairing code. Scan the QR code on the PC again (Card Studio → Mobile App…).");
            }

            lock (_lock) _bad.Remove(req.Remote);
            if (req.Path == "/api/info") Log(req.Device, "Connected", false);
            Seen(req);
        }

        /// <summary>Notes that a paired phone made a request; announces it when it was not counted as connected.</summary>
        private void Seen(Request req)
        {
            bool isNew;
            lock (_lock)
            {
                SeenPhone phone;
                isNew = !_phones.TryGetValue(req.Device, out phone) || DateTime.UtcNow - phone.At > PhoneTimeout;
                _phones[req.Device] = new SeenPhone { Name = req.Name, At = DateTime.UtcNow };
            }
            if (isNew) RaisePhonesChanged();
        }

        private void ForgetPhones()
        {
            lock (_lock) _phones.Clear();
            RaisePhonesChanged();
        }

        private void RaisePhonesChanged()
        {
            Action handler = PhonesChanged;
            if (handler != null) try { handler(); } catch { }
        }

        private static bool SameToken(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            byte[] x = Encoding.UTF8.GetBytes(a), y = Encoding.UTF8.GetBytes(b);
            int diff = x.Length ^ y.Length;
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++) diff |= x[i] ^ y[i];
            return diff == 0;
        }

        // ===================== Endpoints =====================
        private static Dictionary<string, object> Ping()
        {
            Version v = typeof(MobileLinkServer).Assembly.GetName().Version;
            return new Dictionary<string, object>
            {
                { "app", "Card Studio" },
                { "api", ApiVersion },
                { "version", v.ToString(3) },
                { "computer", Environment.MachineName }
            };
        }

        private Dictionary<string, object> Info(Request req)
        {
            List<Student> students = _host.Students();
            StatusSnapshot st = Status();
            AppSettings settings = _host.Settings();
            Dictionary<string, object> info = Ping();
            info["students"] = students.Count;
            info["withPhoto"] = students.Count(s => st.Photo(s.StudentId) != null);
            info["printed"] = students.Count(s => st.PrintedAt(s.StudentId) != null);
            info["withCard"] = students.Count(s => st.Uid(s.StudentId) != null);
            info["source"] = _host.Source() ?? "";
            info["template"] = settings.CardTemplate.ToString();
            info["accentColor"] = string.IsNullOrEmpty(settings.AccentColor) ? "#6C63F5" : settings.AccentColor;
            info["photoFolderOk"] = !string.IsNullOrEmpty(st.PhotoFolder) && Directory.Exists(st.PhotoFolder);
            info["device"] = req.Device;
            return info;
        }

        private Dictionary<string, object> ListStudents(Request req)
        {
            List<Student> students = _host.Students();
            StatusSnapshot st = Status();
            string q = req.Q("q").Trim();
            string filter = req.Q("filter").Trim().ToLowerInvariant();

            IEnumerable<Student> view = students;
            if (q.Length > 0) view = view.Where(s => Matches(s, q));
            switch (filter)
            {
                case "nophoto": view = view.Where(s => st.Photo(s.StudentId) == null); break;
                case "notprinted": view = view.Where(s => st.PrintedAt(s.StudentId) == null); break;
                case "nocard": view = view.Where(s => st.Uid(s.StudentId) == null); break;
            }
            List<Student> list = view.ToList();
            int offset = req.QInt("offset", 0, 0, int.MaxValue);
            int limit = req.QInt("limit", 50, 1, 500);

            return new Dictionary<string, object>
            {
                { "total", list.Count },
                { "offset", offset },
                { "items", list.Skip(offset).Take(limit).Select(s => Summary(s, st)).ToList() }
            };
        }

        /// <summary>Same matching as the main window search: ID, Khmer name, Latin name, faculty.</summary>
        private static bool Matches(Student s, string q)
        {
            return Has(s.StudentId, q) || Has(s.NameKh, q) || Has(s.NameEn, q) || Has(s.Faculty, q);
        }

        private static bool Has(string value, string q)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Student Find(string id)
        {
            id = (id ?? "").Trim();
            Student s = _host.Students().FirstOrDefault(x => string.Equals((x.StudentId ?? "").Trim(), id, StringComparison.OrdinalIgnoreCase));
            if (s == null) throw new HttpError(404, "Student " + id + " is not in the list loaded in Card Studio.");
            return s;
        }

        private Response Photo(Student s)
        {
            string path = CardRenderer.FindPhoto(Status().PhotoFolder, s.StudentId);
            if (path == null) throw new HttpError(404, "No photo yet.");
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return new Response
            {
                ContentType = ext == ".png" ? "image/png" : ext == ".bmp" ? "image/bmp" : "image/jpeg",
                Body = File.ReadAllBytes(path),
                CacheControl = "private, max-age=3600"   // the URL carries ?v=<file time>, so a new photo has a new URL
            };
        }

        /// <summary>
        /// Saves a photo taken on the phone as {StudentId}.jpg / .png in the photo folder. The photo it replaces (and its
        /// Remove Background original, which belongs to the old photo) is moved to "_replaced\" with the time in its name,
        /// so nothing is lost.
        /// </summary>
        private Dictionary<string, object> SavePhoto(Request req, Student s)
        {
            string folder = _host.Settings().PhotoFolder;
            if (string.IsNullOrEmpty(folder)) throw new HttpError(409, "Card Studio has no photo folder. Choose one with Photo Folder… on the PC.");
            if (!Directory.Exists(folder) && !AppSettings.TryCreate(folder))
                throw new HttpError(409, "The photo folder is missing on the PC: " + folder);
            string id = (s.StudentId ?? "").Trim();
            if (id.Length == 0 || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new HttpError(400, "This StudentId cannot be used as a file name.");

            byte[] body = req.Body;
            string ext = IsJpeg(body) ? ".jpg" : IsPng(body) ? ".png" : null;
            if (ext == null) throw new HttpError(415, "Send the photo as JPEG or PNG.");
            int width, height;
            try
            {
                using (var ms = new MemoryStream(body))
                using (Image img = Image.FromStream(ms, false, true))
                {
                    width = img.Width;
                    height = img.Height;
                }
            }
            catch (ArgumentException) { throw new HttpError(415, "The photo is damaged or not a picture."); }
            if (width < 120 || height < 120) throw new HttpError(422, "The photo is too small (at least 120 × 120 pixels).");

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string replaced = Path.Combine(folder, ReplacedFolderName);
            string original = Path.Combine(folder, "_original");
            int moved = 0;
            foreach (string dir in new[] { folder, original })
            {
                foreach (string e in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
                {
                    string old = Path.Combine(dir, id + e);
                    if (!File.Exists(old)) continue;
                    Directory.CreateDirectory(replaced);
                    string tag = dir == original ? "-original" : "";
                    string target = Path.Combine(replaced, id + "-" + stamp + tag + e);
                    for (int n = 2; File.Exists(target); n++) target = Path.Combine(replaced, id + "-" + stamp + tag + "-" + n + e);
                    File.Move(old, target);
                    moved++;
                }
            }

            string path = Path.Combine(folder, id + ext);
            string temp = path + ".uploading";
            File.WriteAllBytes(temp, body);
            File.Move(temp, path);

            Log(req.Device, "Photo saved for " + id + " " + (s.NameEn ?? "") + (moved > 0 ? " (old photo kept in " + ReplacedFolderName + ")" : ""), false);
            try { _host.PhotoReceived(s, path, req.Device); } catch { /* the photo is saved either way */ }

            return new Dictionary<string, object>
            {
                { "saved", true },
                { "file", Path.GetFileName(path) },
                { "width", width },
                { "height", height },
                { "replaced", moved > 0 },
                { "student", Summary(s, Status()) }
            };
        }

        private Response Card(Request req, Student s)
        {
            bool front = !string.Equals(req.Q("side"), "back", StringComparison.OrdinalIgnoreCase);
            int width = req.QInt("w", 1100, 300, 2000);
            return new Response { ContentType = "image/jpeg", Body = _host.RenderCard(s, front, width) };
        }

        /// <summary>
        /// Print button on the phone: hands the card side to Print Selected on the PC. The answer only says the print was
        /// started; the tray load is confirmed on the PC and the print history shows when the card was really printed.
        /// </summary>
        private Dictionary<string, object> Print(Request req, Student s)
        {
            string sideText = req.Q("side");
            if (sideText.Length > 0 && !sideText.Equals("front", StringComparison.OrdinalIgnoreCase) &&
                !sideText.Equals("back", StringComparison.OrdinalIgnoreCase))
                throw new HttpError(400, "Choose side=front or side=back.");
            bool front = !sideText.Equals("back", StringComparison.OrdinalIgnoreCase);
            string side = front ? "FRONT" : "BACK";

            string problem = _host.RequestPrint(s, front, req.Device);
            if (problem != null)
            {
                Log(req.Device, "Print refused for " + s.StudentId + " (" + side + "): " + problem, true);
                throw new HttpError(409, problem);
            }
            Log(req.Device, "Print requested for " + s.StudentId + " " + (s.NameEn ?? "") + " (" + side + ")", false);
            return new Dictionary<string, object>
            {
                { "started", true },
                { "studentId", s.StudentId },
                { "side", side }
            };
        }

        /// <summary>
        /// The student behind a scanned code. The card QR holds the StudentId inside a URL or text (Card Text… → QR template),
        /// so the code may be the ID itself, a link that contains it, or the student's own QrData.
        /// </summary>
        private Dictionary<string, object> Lookup(Request req)
        {
            string code = req.Q("code").Trim();
            if (code.Length == 0) throw new HttpError(400, "Scan a card or type a StudentId.");
            List<Student> students = _host.Students();
            StatusSnapshot st = Status();

            Student hit = students.FirstOrDefault(s => string.Equals((s.StudentId ?? "").Trim(), code, StringComparison.OrdinalIgnoreCase))
                       ?? students.FirstOrDefault(s => !string.IsNullOrEmpty(s.QrData) && s.QrData.Trim() == code)
                       ?? students.Where(s => !string.IsNullOrWhiteSpace(s.StudentId) && s.StudentId.Trim().Length >= 4 &&
                                              ContainsToken(code, s.StudentId.Trim()))
                                  .OrderByDescending(s => s.StudentId.Trim().Length)
                                  .FirstOrDefault();

            var result = new Dictionary<string, object> { { "code", code }, { "found", hit != null } };
            if (hit != null)
            {
                result["student"] = Detail(hit, st);
                return result;
            }

            // Not in the loaded list (e.g. removed after its RFID card was saved): say what the PC still knows about it.
            PrintRecord printed = SafeHistory().Where(r => !string.IsNullOrEmpty(r.StudentId) && (
                                                         string.Equals(r.StudentId.Trim(), code, StringComparison.OrdinalIgnoreCase) ||
                                                         (r.StudentId.Trim().Length >= 4 && ContainsToken(code, r.StudentId.Trim()))))
                                               .OrderByDescending(r => r.PrintedAt).FirstOrDefault();
            if (printed != null)
            {
                result["history"] = new Dictionary<string, object>
                {
                    { "studentId", printed.StudentId }, { "nameKh", printed.NameKh }, { "nameEn", printed.NameEn },
                    { "faculty", printed.Faculty }, { "printedAt", Iso(printed.PrintedAt) },
                    { "rfidUid", st.Uid(printed.StudentId) }
                };
            }
            return result;
        }

        /// <summary>True when <paramref name="id"/> appears in <paramref name="text"/> not glued to other letters or digits.</summary>
        private static bool ContainsToken(string text, string id)
        {
            for (int at = text.IndexOf(id, StringComparison.OrdinalIgnoreCase); at >= 0;
                 at = text.IndexOf(id, at + 1, StringComparison.OrdinalIgnoreCase))
            {
                bool startOk = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
                int end = at + id.Length;
                bool endOk = end == text.Length || !char.IsLetterOrDigit(text[end]);
                if (startOk && endOk) return true;
            }
            return false;
        }

        private Dictionary<string, object> History(Request req)
        {
            int limit = req.QInt("limit", 50, 1, 500);
            List<PrintRecord> all = SafeHistory();
            return new Dictionary<string, object>
            {
                { "total", all.Count },
                { "printedToday", all.Count(r => r.PrintedAt.Date == DateTime.Today) },
                { "items", all.OrderByDescending(r => r.PrintedAt).Take(limit).Select(r => new Dictionary<string, object>
                    {
                        { "printedAt", Iso(r.PrintedAt) }, { "studentId", r.StudentId }, { "nameKh", r.NameKh },
                        { "nameEn", r.NameEn }, { "faculty", r.Faculty }, { "side", r.Side }, { "printer", r.Printer },
                        { "user", r.User }, { "computer", r.Computer }
                    }).ToList() }
            };
        }

        private static List<PrintRecord> SafeHistory()
        {
            try { return PrintHistory.Load(); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                return new List<PrintRecord>();
            }
        }

        // ===================== Student JSON =====================

        /// <summary>Photo files, print times and RFID numbers, read once per request.</summary>
        private sealed class StatusSnapshot
        {
            public string PhotoFolder;
            public Dictionary<string, DateTime> Printed;
            public Dictionary<string, string> Uids;
            private readonly Dictionary<string, string> _photos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public string Photo(string id)
            {
                id = (id ?? "").Trim();
                string path;
                if (!_photos.TryGetValue(id, out path)) _photos[id] = path = CardRenderer.FindPhoto(PhotoFolder, id);
                return path;
            }

            public DateTime? PrintedAt(string id)
            {
                DateTime t;
                return Printed.TryGetValue((id ?? "").Trim(), out t) ? t : (DateTime?)null;
            }

            public string Uid(string id)
            {
                string uid;
                return Uids.TryGetValue((id ?? "").Trim(), out uid) ? uid : null;
            }
        }

        private StatusSnapshot Status()
        {
            AppSettings settings = _host.Settings();
            var uids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (RfidRecord r in RfidCardWriter.Load(RfidCardWriter.FilePath(settings)))
                    if (!string.IsNullOrWhiteSpace(r.StudentId) && !string.IsNullOrWhiteSpace(r.RfidUid)) uids[r.StudentId.Trim()] = r.RfidUid;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException) { }

            return new StatusSnapshot
            {
                PhotoFolder = settings.PhotoFolder,
                Printed = new Dictionary<string, DateTime>(_host.LastPrinted(), StringComparer.OrdinalIgnoreCase),
                Uids = uids
            };
        }

        private static Dictionary<string, object> Summary(Student s, StatusSnapshot st)
        {
            string photo = st.Photo(s.StudentId);
            return new Dictionary<string, object>
            {
                { "studentId", s.StudentId ?? "" },
                { "nameKh", s.NameKh ?? "" },
                { "nameEn", s.NameEn ?? "" },
                { "gender", s.Gender ?? "" },
                { "faculty", s.Faculty ?? "" },
                { "level", s.Level ?? "" },
                { "generation", s.Generation ?? "" },
                { "hasPhoto", photo != null },
                // Changes whenever the photo file does: the app adds it to the photo URL so its image cache never shows an old photo.
                { "photoVersion", photo == null ? "" : File.GetLastWriteTimeUtc(photo).Ticks.ToString(CultureInfo.InvariantCulture) },
                { "printedAt", Iso(st.PrintedAt(s.StudentId)) },
                { "rfidUid", st.Uid(s.StudentId) }
            };
        }

        private static Dictionary<string, object> Detail(Student s, StatusSnapshot st)
        {
            Dictionary<string, object> d = Summary(s, st);
            d["dateOfBirth"] = s.DateOfBirth ?? "";
            d["dateOfBirthKh"] = SafeKhmerDate(s.DateOfBirth);
            d["validUntil"] = s.ValidUntil ?? "";
            d["qrData"] = s.QrData ?? "";
            d["columns"] = s.Columns.ToDictionary(c => c.Key, c => (object)(c.Value ?? ""));
            return d;
        }

        private static string SafeKhmerDate(string ddMMyyyy)
        {
            try { return KhmerText.Date(ddMMyyyy) ?? ""; }
            catch (Exception) { return ""; }
        }

        private static string Iso(DateTime? t)
        {
            return t.HasValue ? t.Value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) : null;
        }

        // ===================== Helpers =====================
        private Response Json(object value)
        {
            return new Response { Body = Encoding.UTF8.GetBytes(_json.Serialize(value)) };
        }

        private Response JsonError(int status, string message)
        {
            Response r = Json(new Dictionary<string, object> { { "error", message } });
            r.Status = status;
            return r;
        }

        private static Request Fail(int status, string message)
        {
            throw new HttpError(status, message);
        }

        private void Log(string device, string text, bool problem)
        {
            var e = new MobileLinkEvent { At = DateTime.Now, Device = device ?? "", Text = text, Problem = problem };
            lock (_lock)
            {
                // A phone polls /api/info: keep one "Connected" line per device instead of hundreds.
                if (text == "Connected" && _events.Count > 0)
                {
                    MobileLinkEvent last = _events.LastOrDefault(x => x.Device == e.Device);
                    if (last != null && last.Text == "Connected" && (e.At - last.At).TotalMinutes < 10) return;
                }
                _events.Add(e);
                if (_events.Count > MaxEvents) _events.RemoveRange(0, _events.Count - MaxEvents);
            }
            Action<MobileLinkEvent> handler = Activity;
            if (handler != null) try { handler(e); } catch { }
        }

        private static bool IsJpeg(byte[] b) { return b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF; }
        private static bool IsPng(byte[] b) { return b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47; }

        private static string Clip(string s, int max) { return s.Length <= max ? s : s.Substring(0, max); }

        private static int IndexOf(byte[] data, int length, byte[] pattern, int start = 0)
        {
            for (int i = start; i + pattern.Length <= length; i++)
            {
                int j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return i;
            }
            return -1;
        }

        private static string Reason(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 204: return "No Content";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 409: return "Conflict";
                case 411: return "Length Required";
                case 413: return "Payload Too Large";
                case 415: return "Unsupported Media Type";
                case 422: return "Unprocessable Entity";
                case 429: return "Too Many Requests";
                case 431: return "Request Header Fields Too Large";
                case 503: return "Service Unavailable";
                default: return status >= 500 ? "Internal Server Error" : "Error";
            }
        }

        /// <summary>A bitmap → JPEG no wider than <paramref name="maxWidth"/> (the cards are drawn at 600 dpi, ~2000 px).</summary>
        public static byte[] ToJpeg(Bitmap bmp, int maxWidth)
        {
            Bitmap scaled = null;
            try
            {
                Bitmap src = bmp;
                if (bmp.Width > maxWidth)
                {
                    int h = (int)Math.Round(bmp.Height * (double)maxWidth / bmp.Width);
                    scaled = new Bitmap(maxWidth, h, PixelFormat.Format24bppRgb);
                    using (Graphics g = Graphics.FromImage(scaled))
                    {
                        g.Clear(Color.White);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.DrawImage(bmp, 0, 0, maxWidth, h);
                    }
                    src = scaled;
                }
                ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using (var p = new EncoderParameters(1))
                using (var ms = new MemoryStream())
                {
                    p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                    src.Save(ms, jpeg, p);
                    return ms.ToArray();
                }
            }
            finally
            {
                if (scaled != null) scaled.Dispose();
            }
        }
    }
}
