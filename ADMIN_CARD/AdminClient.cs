using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using NPIC_CARD_APP.Services;

namespace CardStudio.Admin
{
    /// <summary>
    /// The admin side of <see cref="AdminProtocol"/>: one UDP socket that broadcasts discovers, sends commands to one PC
    /// (resent until it answers, the PC runs each command once) and receives every reply.
    /// </summary>
    internal sealed class AdminClient : IDisposable
    {
        private const int SIO_UDP_CONNRESET = -1744830452;
        private const int ResendMs = 1500;
        private const int Attempts = 5;

        private readonly UdpClient _udp;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<AdminReply>> _waiting =
            new ConcurrentDictionary<string, TaskCompletionSource<AdminReply>>(StringComparer.Ordinal);
        /// <summary>Nonces of our recent discovers: a reply to anything else is ignored.</summary>
        private readonly ConcurrentDictionary<string, DateTime> _discovers = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private bool _closed;

        /// <summary>Raised on a socket thread for every reply that carries a PC's status (discover or command).</summary>
        public event Action<AdminReply, IPEndPoint> StatusReceived;

        public AdminClient()
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
            try { _udp.Client.IOControl(SIO_UDP_CONNRESET, new byte[4], null); } catch (SocketException) { }
            Task.Run(ReceiveLoop);
        }

        /// <summary>Asks every PC to answer: broadcast on each network of this PC, plus one copy to each of <paramref name="unicast"/>.</summary>
        public void Discover(AdminKey key, IEnumerable<IPAddress> unicast)
        {
            AdminCommand c = key.Sign(AdminProtocol.Discover, AdminProtocol.Everyone, "");
            DateTime now = DateTime.UtcNow;
            foreach (var old in _discovers.Where(d => now - d.Value > TimeSpan.FromMinutes(2)).ToList())
            {
                DateTime ignored;
                _discovers.TryRemove(old.Key, out ignored);
            }
            _discovers[c.Nonce] = now;

            byte[] bytes = AdminProtocol.Encode(c.ToPacket());
            // The same nonce everywhere: a PC reached twice (broadcast and its known address) runs it once.
            var targets = new List<IPAddress> { IPAddress.Broadcast };
            targets.AddRange(BroadcastAddresses());
            targets.AddRange(unicast);
            foreach (IPAddress ip in targets.Distinct()) Send(bytes, ip);
        }

        /// <summary>
        /// Sends one command to the PC <paramref name="machineId"/> at <paramref name="address"/> and waits for its reply.
        /// Null = no answer (PC off, Card Studio closed, firewall), although the command may still have run.
        /// </summary>
        public async Task<AdminReply> SendAsync(AdminKey key, string machineId, IPAddress address, string command, string arg)
        {
            AdminCommand c = key.Sign(command, machineId, arg);
            byte[] bytes = AdminProtocol.Encode(c.ToPacket());
            var tcs = new TaskCompletionSource<AdminReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiting[c.Nonce] = tcs;
            try
            {
                for (int attempt = 0; attempt < Attempts; attempt++)
                {
                    Send(bytes, address);
                    if (await Task.WhenAny(tcs.Task, Task.Delay(ResendMs)).ConfigureAwait(false) == tcs.Task) return tcs.Task.Result;
                }
                return null;
            }
            finally
            {
                TaskCompletionSource<AdminReply> ignored;
                _waiting.TryRemove(c.Nonce, out ignored);
            }
        }

        private void Send(byte[] bytes, IPAddress ip)
        {
            try { _udp.Send(bytes, bytes.Length, new IPEndPoint(ip, AdminProtocol.Port)); }
            catch (SocketException) { }       // no route to that network
            catch (ObjectDisposedException) { }
        }

        private async Task ReceiveLoop()
        {
            while (!_closed)
            {
                UdpReceiveResult packet;
                try { packet = await _udp.ReceiveAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (_closed) return; continue; }
                catch (InvalidOperationException) { return; }

                AdminReply reply = AdminReply.FromPacket(AdminProtocol.Decode(packet.Buffer, packet.Buffer.Length));
                if (reply == null) continue;

                TaskCompletionSource<AdminReply> tcs;
                bool ours = _waiting.TryGetValue(reply.Nonce, out tcs) | _discovers.ContainsKey(reply.Nonce);
                if (!ours) continue;

                if (reply.Status != null)
                {
                    Action<AdminReply, IPEndPoint> handler = StatusReceived;
                    if (handler != null) try { handler(reply, packet.RemoteEndPoint); } catch { }
                }
                if (tcs != null) tcs.TrySetResult(reply);
            }
        }

        /// <summary>Directed broadcast address of every IPv4 network that is up (192.168.1.255 for 192.168.1.20/24).</summary>
        public static List<IPAddress> BroadcastAddresses()
        {
            var list = new List<IPAddress>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation ip in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily != AddressFamily.InterNetwork || ip.IPv4Mask == null) continue;
                        byte[] a = ip.Address.GetAddressBytes(), m = ip.IPv4Mask.GetAddressBytes();
                        if (a[0] == 127 || (a[0] == 169 && a[1] == 254)) continue;
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                        list.Add(new IPAddress(b));
                    }
                }
            }
            catch (NetworkInformationException) { }
            return list;
        }

        public void Dispose()
        {
            _closed = true;
            _udp.Close();
        }
    }
}
