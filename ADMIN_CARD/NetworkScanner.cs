using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CardStudio.Admin
{
    /// <summary>One device that answered the scan.</summary>
    internal sealed class NetDevice
    {
        public IPAddress Address;
        /// <summary>DNS or NetBIOS name, "" when it has none.</summary>
        public string Name = "";
        /// <summary>"AA-BB-CC-DD-EE-FF" (same network only), "" when unknown.</summary>
        public string Mac = "";
        /// <summary>Windows file sharing (TCP 445) answers: a Windows PC (or a NAS) Card Studio can be installed on.</summary>
        public bool FileSharing;
        /// <summary>Ping time in ms, -1 = no ping answer (Windows blocks ping by default).</summary>
        public int PingMs = -1;
        /// <summary>This PC (ADMIN_CARD runs here).</summary>
        public bool Self;
        /// <summary>Set by the install dialog after Card Studio was installed from this page ("1.0.1.1").</summary>
        public string InstalledHere;

        public string DisplayName { get { return Name.Length > 0 ? Name : Address.ToString(); } }

        public long SortKey
        {
            get
            {
                byte[] b = Address.GetAddressBytes();
                return ((long)b[0] << 24) | ((long)b[1] << 16) | ((long)b[2] << 8) | b[3];
            }
        }
    }

    /// <summary>
    /// Finds the devices on the networks of this PC: ARP (answers even when the device's firewall blocks ping), ping, and the
    /// Windows file-sharing port, plus the name from DNS or NetBIOS. Large networks are limited to the /24 around this PC;
    /// the Addresses… list of the main window adds other networks.
    /// </summary>
    internal static class NetworkScanner
    {
        private const int Parallel = 64;
        private const int PingMs = 700, PortMs = 700, NameMs = 1200;
        private const int MaxHostsPerNetwork = 1022;   // up to a /22; larger networks: only this PC's /24

        public sealed class Plan
        {
            public List<IPAddress> Targets = new List<IPAddress>();
            /// <summary>Addresses on a network of this PC (ARP works there).</summary>
            public HashSet<IPAddress> Local = new HashSet<IPAddress>();
            public HashSet<IPAddress> SelfAddresses = new HashSet<IPAddress>();
            public Dictionary<IPAddress, string> SelfMacs = new Dictionary<IPAddress, string>();
            /// <summary>"192.168.1.0/24, 10.0.5.0/24 + 12 addresses".</summary>
            public string Description = "";
        }

        /// <summary>What to scan: every network of this PC (physical adapters that are up) and <paramref name="extra"/>.</summary>
        public static Plan MakePlan(IEnumerable<string> extra)
        {
            var plan = new Plan();
            var parts = new List<string>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    string desc = (nic.Description + " " + nic.Name).ToLowerInvariant();
                    if (new[] { "virtual", "vmware", "hyper-v", "vethernet", "wsl", "docker", "virtualbox", "loopback" }.Any(desc.Contains)) continue;
                    string mac = FormatMac(nic.GetPhysicalAddress().GetAddressBytes());

                    foreach (UnicastIPAddressInformation ip in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily != AddressFamily.InterNetwork || ip.IPv4Mask == null) continue;
                        byte[] a = ip.Address.GetAddressBytes();
                        if (a[0] == 127 || (a[0] == 169 && a[1] == 254)) continue;
                        plan.SelfAddresses.Add(ip.Address);
                        plan.SelfMacs[ip.Address] = mac;

                        uint addr = ToUInt(ip.Address), mask = ToUInt(ip.IPv4Mask);
                        int bits = 32 - CountZeroBits(mask);
                        if (bits >= 31) continue;
                        if ((~mask) - 1 > MaxHostsPerNetwork) { bits = 24; mask = 0xFFFFFF00; }
                        uint network = addr & mask, last = network | ~mask;
                        for (uint h = network + 1; h < last; h++)
                        {
                            IPAddress t = FromUInt(h);
                            if (plan.Local.Add(t)) plan.Targets.Add(t);
                        }
                        parts.Add(FromUInt(network) + "/" + bits);
                    }
                }
            }
            catch (NetworkInformationException) { }

            int added = 0;
            foreach (IPAddress t in Inventory.Expand(extra))   // resolves names: call off the UI thread
                if (!plan.Local.Contains(t) && !plan.Targets.Contains(t)) { plan.Targets.Add(t); added++; }
            plan.Description = (parts.Count == 0 ? "no network" : string.Join(", ", parts.Distinct())) +
                               (added > 0 ? " + " + added + (added == 1 ? " address" : " addresses") : "");
            return plan;
        }

        /// <summary>
        /// Probes every target, <see cref="Parallel"/> at a time. <paramref name="found"/> and <paramref name="progress"/>
        /// (done, total) are called on worker threads.
        /// </summary>
        public static async Task ScanAsync(Plan plan, Action<NetDevice> found, Action<int, int> progress, CancellationToken cancel)
        {
            int done = 0, total = plan.Targets.Count;
            // ARP blocks a thread for up to ~3 s per silent address: let the pool start enough threads at once.
            int workers, io;
            ThreadPool.GetMinThreads(out workers, out io);
            if (workers < Parallel * 2) ThreadPool.SetMinThreads(Parallel * 2, Math.Max(io, Parallel));
            using (var gate = new SemaphoreSlim(Parallel))
            {
                var tasks = plan.Targets.Select(async ip =>
                {
                    await gate.WaitAsync(cancel).ConfigureAwait(false);
                    try
                    {
                        NetDevice d = await Probe(ip, plan, cancel).ConfigureAwait(false);
                        if (d != null) found(d);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { /* one odd host never stops the scan */ }
                    finally
                    {
                        gate.Release();
                        progress(Interlocked.Increment(ref done), total);
                    }
                }).ToList();
                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }

        private static async Task<NetDevice> Probe(IPAddress ip, Plan plan, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if (plan.SelfAddresses.Contains(ip))
            {
                string selfMac;
                plan.SelfMacs.TryGetValue(ip, out selfMac);
                return new NetDevice { Address = ip, Name = Environment.MachineName, Mac = selfMac ?? "", Self = true, FileSharing = true, PingMs = 0 };
            }

            bool local = plan.Local.Contains(ip);
            Task<string> arp = local ? Task.Run(() => Arp(ip, plan)) : Task.FromResult((string)null);
            Task<int> ping = Ping(ip);
            await Task.WhenAll(arp, ping).ConfigureAwait(false);
            string mac = arp.Result;
            int ms = ping.Result;
            // Only probe the port of hosts that answered (same network), but always for other networks (no ARP there).
            bool smb = (mac != null || ms >= 0 || !local) && await PortOpen(ip, 445).ConfigureAwait(false);
            if (mac == null && ms < 0 && !smb) return null;

            var d = new NetDevice { Address = ip, Mac = mac ?? "", PingMs = ms, FileSharing = smb };
            d.Name = await NameOf(ip, d).ConfigureAwait(false);
            return d;
        }

        // ===================== Probes =====================

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref int physicalAddrLen);

        /// <summary>MAC address by ARP (same network only), or null when nothing answered.</summary>
        private static string Arp(IPAddress ip, Plan plan)
        {
            var mac = new byte[6];
            int len = mac.Length;
            uint dest = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
            if (SendARP(dest, 0, mac, ref len) != 0 || len < 6) return null;
            if (mac.All(b => b == 0)) return null;
            return FormatMac(mac);
        }

        private static async Task<int> Ping(IPAddress ip)
        {
            try
            {
                using (var p = new Ping())
                {
                    PingReply r = await p.SendPingAsync(ip, PingMs).ConfigureAwait(false);
                    return r.Status == IPStatus.Success ? (int)r.RoundtripTime : -1;
                }
            }
            catch (PingException) { return -1; }
            catch (InvalidOperationException) { return -1; }
        }

        public static async Task<bool> PortOpen(IPAddress ip, int port)
        {
            using (var client = new TcpClient(AddressFamily.InterNetwork))
            {
                Task connect = client.ConnectAsync(ip, port);
                Task first = await Task.WhenAny(connect, Task.Delay(PortMs)).ConfigureAwait(false);
                _ = connect.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                return first == connect && connect.Status == TaskStatus.RanToCompletion && client.Connected;
            }
        }

        /// <summary>Reverse DNS, else the NetBIOS name (Windows PCs answer it on a local network). Fills a missing MAC from NetBIOS.</summary>
        private static async Task<string> NameOf(IPAddress ip, NetDevice d)
        {
            try
            {
                Task<IPHostEntry> dns = Dns.GetHostEntryAsync(ip);
                if (await Task.WhenAny(dns, Task.Delay(NameMs)).ConfigureAwait(false) == dns && dns.Status == TaskStatus.RanToCompletion)
                {
                    string host = dns.Result.HostName ?? "";
                    if (host.Length > 0 && host != ip.ToString()) return host.Split('.')[0].ToUpperInvariant();
                }
                else
                {
                    _ = dns.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
            catch (SocketException) { }
            catch (ArgumentException) { }

            Tuple<string, string> nb = await NetBios(ip).ConfigureAwait(false);
            if (nb == null) return "";
            if (d.Mac.Length == 0 && nb.Item2 != null) d.Mac = nb.Item2;
            return nb.Item1 ?? "";
        }

        /// <summary>NetBIOS node status (NBSTAT) on UDP 137: (computer name, MAC), or null when the host does not answer.</summary>
        private static async Task<Tuple<string, string>> NetBios(IPAddress ip)
        {
            using (var udp = new UdpClient(AddressFamily.InterNetwork))
            {
                try
                {
                    byte[] query = NbStatQuery();
                    await udp.SendAsync(query, query.Length, new IPEndPoint(ip, 137)).ConfigureAwait(false);
                    Task<UdpReceiveResult> receive = udp.ReceiveAsync();
                    _ = receive.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    if (await Task.WhenAny(receive, Task.Delay(NameMs)).ConfigureAwait(false) != receive ||
                        receive.Status != TaskStatus.RanToCompletion)
                        return null;
                    string name, mac;
                    ParseNbStat(receive.Result.Buffer, out name, out mac);
                    return Tuple.Create(name, mac);
                }
                catch (SocketException) { return null; }
                catch (ObjectDisposedException) { return null; }
            }
        }

        private static byte[] NbStatQuery()
        {
            var q = new List<byte> { 0x13, 0x37, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20 };
            var name = new byte[16];
            name[0] = (byte)'*';
            foreach (byte b in name) { q.Add((byte)('A' + (b >> 4))); q.Add((byte)('A' + (b & 0x0F))); }
            q.AddRange(new byte[] { 0x00, 0x00, 0x21, 0x00, 0x01 });   // end of name, type NBSTAT, class IN
            return q.ToArray();
        }

        private static void ParseNbStat(byte[] r, out string name, out string mac)
        {
            name = null;
            mac = null;
            const int countAt = 56;   // header 12 + name 34 + type 2 + class 2 + TTL 4 + length 2
            if (r == null || r.Length <= countAt) return;
            int count = r[countAt];
            int at = countAt + 1;
            for (int i = 0; i < count && at + 18 <= r.Length; i++, at += 18)
            {
                string n = Encoding.ASCII.GetString(r, at, 15).TrimEnd(' ', '\0');
                byte suffix = r[at + 15];
                bool group = (r[at + 16] & 0x80) != 0;
                if (name == null && suffix == 0x00 && !group && n.Length > 0) name = n.ToUpperInvariant();
            }
            if (at + 6 <= r.Length)
            {
                var m = new byte[6];
                Array.Copy(r, at, m, 0, 6);
                if (!m.All(b => b == 0)) mac = FormatMac(m);
            }
        }

        // ===================== Helpers =====================

        public static string FormatMac(byte[] b)
        {
            return b == null || b.Length < 6 ? "" : string.Join("-", b.Take(6).Select(x => x.ToString("X2")));
        }

        private static int CountZeroBits(uint mask)
        {
            int n = 0;
            for (int i = 0; i < 32 && (mask & (1u << i)) == 0; i++) n++;
            return n;
        }

        private static uint ToUInt(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        private static IPAddress FromUInt(uint a)
        {
            return new IPAddress(new[] { (byte)(a >> 24), (byte)(a >> 16), (byte)(a >> 8), (byte)a });
        }
    }
}
