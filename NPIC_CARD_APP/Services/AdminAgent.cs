using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// What the administrator's commands need from the main window. Every member is called on an agent thread:
    /// the main window marshals to its UI thread itself and never blocks for long.
    /// </summary>
    public interface IAdminAgentHost
    {
        /// <summary>Main-window part of the status: students, printer, phones, print counts, busy… (see <see cref="AdminAgent.Status"/>).</summary>
        void FillStatus(Dictionary<string, object> status);
        /// <summary>Shows <paramref name="text"/> to the user. Returns null when shown, otherwise why not.</summary>
        string ShowAdminMessage(string text);
        /// <summary>Opens Check for Updates (or says in the status bar that the administrator asked for it).</summary>
        string AdminCheckForUpdates();
        /// <summary>The serial number was saved by <see cref="LicenseService.Activate"/>: tell the user.</summary>
        void SerialInstalled();
        /// <summary>Makes a new mobile pairing code. Returns null when done, otherwise why not.</summary>
        string AdminRevokePhones();
    }

    /// <summary>
    /// Answers Card Studio Admin (ADMIN_CARD) on UDP <see cref="AdminProtocol.Port"/> for the whole session: lists this PC
    /// when the administrator looks for PCs, and runs the few commands of <see cref="AdminProtocol"/>. Only commands signed
    /// with the administrator's key are answered; anything else is dropped without a reply, so the port tells nobody else
    /// that Card Studio runs here.
    /// </summary>
    public sealed class AdminAgent : IDisposable
    {
        private const int MaxPacketsPerSecond = 40;   // ECDSA checks are cheap, but a flood must not eat the CPU
        private const int SIO_UDP_CONNRESET = -1744830452;

        private readonly IAdminAgentHost _host;
        private readonly object _lock = new object();
        /// <summary>Nonce → reply sent for it (null while the command runs), kept until the command can no longer be valid.</summary>
        private readonly Dictionary<string, SeenNonce> _nonces = new Dictionary<string, SeenNonce>(StringComparer.Ordinal);
        private readonly DateTime _startedAt = DateTime.Now;
        private UdpClient _udp;
        private long _second;
        private int _packetsThisSecond;

        private sealed class SeenNonce { public DateTime Until; public IPEndPoint From; public byte[] Reply; }

        public AdminAgent(IAdminAgentHost host)
        {
            if (host == null) throw new ArgumentNullException("host");
            _host = host;
        }

        public bool Running { get { return _udp != null; } }

        /// <summary>Why <see cref="Start"/> failed (e.g. another Windows user's Card Studio has the port), else null.</summary>
        public string LastError { get; private set; }

        /// <summary>Starts listening on every network of this PC. Never throws: a problem is kept in <see cref="LastError"/>.</summary>
        public void Start()
        {
            if (_udp != null) return;
            try
            {
                var udp = new UdpClient(AddressFamily.InterNetwork);
                udp.Client.ExclusiveAddressUse = true;
                // Without this, an ICMP "port unreachable" for a reply makes the next receive throw (WSAECONNRESET).
                try { udp.Client.IOControl(SIO_UDP_CONNRESET, new byte[4], null); } catch (SocketException) { }
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, AdminProtocol.Port));
                _udp = udp;
                LastError = null;
                Task.Run(() => ReceiveLoop(udp));
            }
            catch (SocketException ex)
            {
                LastError = ex.Message;
            }
        }

        public void Stop()
        {
            UdpClient udp = _udp;
            _udp = null;
            if (udp != null) udp.Close();
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task ReceiveLoop(UdpClient udp)
        {
            while (_udp == udp)
            {
                UdpReceiveResult packet;
                try { packet = await udp.ReceiveAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (_udp != udp) return; continue; }
                catch (InvalidOperationException) { return; }

                if (!TakeSlot()) continue;
                _ = Task.Run(() =>   // a command may wait for the UI thread: the loop goes straight back to receiving
                {
                    try { Handle(udp, packet.Buffer, packet.RemoteEndPoint); }
                    catch { /* a bad packet never stops the agent */ }
                });
            }
        }

        private bool TakeSlot()
        {
            long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
            lock (_lock)
            {
                if (now != _second) { _second = now; _packetsThisSecond = 0; }
                return ++_packetsThisSecond <= MaxPacketsPerSecond;
            }
        }

        private void Handle(UdpClient udp, byte[] data, IPEndPoint from)
        {
            AdminCommand cmd = AdminCommand.FromPacket(AdminProtocol.Decode(data, data.Length));
            if (cmd == null || !AdminProtocol.Verify(cmd)) return;   // not from the administrator: stay silent

            string me = LicenseService.MachineId;
            bool forMe = string.Equals(cmd.Target, me, StringComparison.OrdinalIgnoreCase);
            if (!forMe && !(cmd.Command == AdminProtocol.Discover && cmd.Target == AdminProtocol.Everyone)) return;

            // ---- once only: a copy (resent because a reply was lost, or the same broadcast on two networks) ----
            DateTime now = DateTime.UtcNow;
            SeenNonce seen;
            lock (_lock)
            {
                foreach (string old in _nonces.Where(n => n.Value.Until < now).Select(n => n.Key).ToList()) _nonces.Remove(old);
                if (_nonces.TryGetValue(cmd.Nonce, out seen))
                {
                    if (seen.Reply != null && seen.From.Equals(from)) Send(udp, seen.Reply, from);
                    return;
                }
                _nonces[cmd.Nonce] = seen = new SeenNonce { Until = now + AdminProtocol.MaxClockSkew + AdminProtocol.MaxClockSkew, From = from };
            }

            AdminReply reply;
            long skew = AdminProtocol.UnixNow() - cmd.Time;
            if (Math.Abs(skew) > AdminProtocol.MaxClockSkew.TotalSeconds)
            {
                reply = new AdminReply
                {
                    Ok = false,
                    ErrorCode = "clock",
                    Message = "The clock of " + Environment.MachineName + " is " + (skew > 0 ? "ahead of" : "behind") +
                              " the administrator's PC by " + FormatSkew(Math.Abs(skew)) + ". Set the date and time on both PCs."
                };
            }
            else
            {
                reply = Run(cmd);
                reply.Status = Status();
            }
            reply.Nonce = cmd.Nonce;

            byte[] bytes = AdminProtocol.Encode(reply.ToPacket());
            lock (_lock) seen.Reply = bytes;
            Send(udp, bytes, from);
        }

        private AdminReply Run(AdminCommand cmd)
        {
            string problem;
            switch (cmd.Command)
            {
                case AdminProtocol.Discover:
                    return new AdminReply { Ok = true };

                case AdminProtocol.Message:
                    string text = (cmd.Arg ?? "").Trim();
                    if (text.Length == 0) return Fail("The message is empty.");
                    problem = Safe(() => _host.ShowAdminMessage(text));
                    return problem == null ? Done("Message shown.") : Fail(problem);

                case AdminProtocol.CheckUpdates:
                    problem = Safe(() => _host.AdminCheckForUpdates());
                    return problem == null ? Done("Check for Updates opened.") : Fail(problem);

                case AdminProtocol.InstallSerial:
                    problem = LicenseService.Activate(cmd.Arg ?? "");
                    if (problem != null) return Fail(problem);
                    Safe(() => { _host.SerialInstalled(); return null; });
                    return Done("Serial number installed. " + LicenseService.Check().Describe());

                case AdminProtocol.RevokePhones:
                    problem = Safe(() => _host.AdminRevokePhones());
                    return problem == null ? Done("New pairing code made: phones must scan the QR code again.") : Fail(problem);
            }
            return Fail("Unknown command.");
        }

        private static string Safe(Func<string> work)
        {
            try { return work(); }
            catch (InvalidOperationException) { return "Card Studio is closing."; }
            catch (Exception ex) { return "Error: " + ex.Message.Split('\n')[0]; }
        }

        private static AdminReply Done(string message) { return new AdminReply { Ok = true, Message = message }; }
        private static AdminReply Fail(string message) { return new AdminReply { Ok = false, Message = message }; }

        private static void Send(UdpClient udp, byte[] bytes, IPEndPoint to)
        {
            try { udp.Send(bytes, bytes.Length, to); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        private static string FormatSkew(long seconds)
        {
            if (seconds < 3600) return (seconds / 60) + " min";
            if (seconds < 172800) return (seconds / 3600) + " h";
            return (seconds / 86400) + " days";
        }

        // ===================== Status =====================

        /// <summary>
        /// Everything the administrator's list shows about this PC. Keep it small (one datagram) and free of secrets:
        /// no pairing code, no serial number, no student data beyond counts.
        /// </summary>
        public Dictionary<string, object> Status()
        {
            Version v = typeof(AdminAgent).Assembly.GetName().Version;
            LicenseStatus license = LicenseService.Check();
            var status = new Dictionary<string, object>
            {
                { "machineId", LicenseService.MachineId },
                { "computer", Environment.MachineName },
                { "user", Environment.UserDomainName + "\\" + Environment.UserName },
                { "version", v.ToString(v.Revision > 0 ? 4 : 3) },
                { "os", OsName() },
                { "addresses", MobileLinkServer.LocalAddresses() },
                { "license", license.State.ToString() },
                { "licenseExpires", license.Expires == null ? "" : license.Expires.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "licenseDays", license.DaysLeft },
                { "startedAt", _startedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
                { "time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) }
            };
            try { _host.FillStatus(status); }
            catch (Exception) { status["busy"] = "Card Studio did not answer."; }
            return status;
        }

        private static string _os;

        /// <summary>"Windows 11 Pro 24H2 (26100)" from the registry (Environment.OSVersion says Windows 8 to unmanifested apps).</summary>
        private static string OsName()
        {
            if (_os != null) return _os;
            string name = "Windows";
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        string product = key.GetValue("ProductName") as string ?? "Windows";
                        string display = key.GetValue("DisplayVersion") as string ?? "";
                        string build = key.GetValue("CurrentBuild") as string ?? "";
                        int b;
                        // Windows 11 still says "Windows 10" in ProductName.
                        if (int.TryParse(build, out b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
                        name = (product + " " + display).Trim() + (build.Length > 0 ? " (" + build + ")" : "");
                    }
                }
            }
            catch (Exception) { }
            return _os = name;
        }
    }
}
