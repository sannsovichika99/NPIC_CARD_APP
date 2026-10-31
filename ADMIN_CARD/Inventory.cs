using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Xml.Serialization;
using NPIC_CARD_APP.Services;

namespace CardStudio.Admin
{
    /// <summary>One PC running Card Studio, as it last answered. Kept between runs, so PCs that are off stay listed.</summary>
    public sealed class Device
    {
        /// <summary>A PC that answered within this time is online (the list asks every <see cref="AdminForm"/> poll, 15 s).</summary>
        public static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(45);

        public string MachineId { get; set; }
        public string Computer { get; set; }
        /// <summary>The administrator's own name for the PC ("Room 204, front desk").</summary>
        public string Label { get; set; }
        public string User { get; set; }
        public string Version { get; set; }
        public string Os { get; set; }
        /// <summary>Address the last reply came from: commands go there.</summary>
        public string Address { get; set; }
        public List<string> Addresses { get; set; }
        public string License { get; set; }
        public string LicenseExpires { get; set; }
        public int? LicenseDays { get; set; }
        public bool MobileRunning { get; set; }
        public int MobilePort { get; set; }
        public List<string> Phones { get; set; }
        public int Students { get; set; }
        public string Source { get; set; }
        public string Printer { get; set; }
        public int PrintedToday { get; set; }
        public int PrintedTotal { get; set; }
        public string LastPrint { get; set; }
        /// <summary>Why the PC cannot take a command now (a window is open, a card is printing), "" when free.</summary>
        public string Busy { get; set; }
        public string StartedAt { get; set; }
        /// <summary>The PC's own clock at its last answer.</summary>
        public string PcTime { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        /// <summary>Last command sent from here and its result.</summary>
        public string LastResult { get; set; }

        public Device()
        {
            Addresses = new List<string>();
            Phones = new List<string>();
        }

        [XmlIgnore]
        public bool Online { get { return DateTime.Now - LastSeen < OnlineWindow; } }

        [XmlIgnore]
        public string DisplayName { get { return string.IsNullOrEmpty(Label) ? Computer : Computer + " (" + Label + ")"; } }

        [XmlIgnore]
        public Version ParsedVersion
        {
            get
            {
                // Always 4 parts: a PC reports "1.0.1" for 1.0.1.0, and Version says 1.0.1 < 1.0.1.0.
                Version v;
                return System.Version.TryParse(Version ?? "", out v) ? ReleaseNotes.Normalize(v) : new Version(0, 0, 0, 0);
            }
        }

        public void Apply(Dictionary<string, object> s, IPEndPoint from)
        {
            Computer = AdminProtocol.Str(s, "computer") ?? Computer;
            User = AdminProtocol.Str(s, "user") ?? "";
            Version = AdminProtocol.Str(s, "version") ?? "";
            Os = AdminProtocol.Str(s, "os") ?? "";
            Address = from.Address.ToString();
            Addresses = AdminProtocol.Strings(s, "addresses");
            License = AdminProtocol.Str(s, "license") ?? "";
            LicenseExpires = AdminProtocol.Str(s, "licenseExpires") ?? "";
            long? days = AdminProtocol.Long(s, "licenseDays");
            LicenseDays = days == null ? (int?)null : (int)days.Value;
            MobileRunning = AdminProtocol.Bool(s, "mobileRunning");
            MobilePort = (int)(AdminProtocol.Long(s, "mobilePort") ?? 0);
            Phones = AdminProtocol.Strings(s, "phones");
            Students = (int)(AdminProtocol.Long(s, "students") ?? 0);
            Source = AdminProtocol.Str(s, "source") ?? "";
            Printer = AdminProtocol.Str(s, "printer") ?? "";
            PrintedToday = (int)(AdminProtocol.Long(s, "printedToday") ?? 0);
            PrintedTotal = (int)(AdminProtocol.Long(s, "printedTotal") ?? 0);
            LastPrint = AdminProtocol.Str(s, "lastPrint") ?? "";
            Busy = AdminProtocol.Str(s, "busy") ?? "";
            StartedAt = AdminProtocol.Str(s, "startedAt") ?? "";
            PcTime = AdminProtocol.Str(s, "time") ?? "";
            LastSeen = DateTime.Now;
            if (FirstSeen == DateTime.MinValue) FirstSeen = LastSeen;
        }

        /// <summary>"OK · 92 days", "Expires in 12 days", "Expired"… and whether it needs attention (0 = fine, 1 = soon, 2 = now).</summary>
        public string LicenseText(out int level)
        {
            level = 0;
            switch (License)
            {
                case "Activated":
                    if (LicenseDays == null) return "Never expires";
                    if (LicenseDays.Value <= 30) level = 1;
                    if (LicenseDays.Value <= 7) level = 2;
                    return LicenseDays.Value <= 0 ? "Last day today" : "Until " + LicenseExpires + " (" + LicenseDays.Value + " d)";
                case "Expired": level = 2; return "Expired " + LicenseExpires;
                case "ClockWrong": level = 2; return "PC date is wrong";
                case "Invalid": level = 2; return "Serial for another PC";
                case "Missing": level = 2; return "Not activated";
                default: return License ?? "";
            }
        }
    }

    /// <summary>Saved list: every PC ever seen, plus the extra addresses to ask (PCs on other networks).</summary>
    public sealed class Inventory
    {
        public List<Device> Devices { get; set; }

        /// <summary>
        /// Addresses asked directly besides the broadcast, one per line: 192.168.2.15, 192.168.2.10-60, 192.168.2.0/24 or a
        /// computer name. Needed for PCs behind a router (broadcasts stay on their own network).
        /// </summary>
        public List<string> Targets { get; set; }

        public Inventory()
        {
            Devices = new List<Device>();
            Targets = new List<string>();
        }

        public static string DataFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CardStudio.Admin"); }
        }

        public static string FilePath { get { return Path.Combine(DataFolder, "devices.xml"); } }

        public static Inventory Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new Inventory();
                using (var s = File.OpenRead(FilePath))
                {
                    var inv = (Inventory)new XmlSerializer(typeof(Inventory)).Deserialize(s);
                    inv.Devices = inv.Devices.Where(d => !string.IsNullOrEmpty(d.MachineId)).ToList();
                    return inv;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
            {
                return new Inventory();   // unreadable: start a new list (the PCs answer again within seconds)
            }
        }

        /// <exception cref="IOException">The file cannot be written.</exception>
        public void Save()
        {
            Directory.CreateDirectory(DataFolder);
            string temp = FilePath + ".tmp";
            using (var s = File.Create(temp)) new XmlSerializer(typeof(Inventory)).Serialize(s, this);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }

        public Device Find(string machineId)
        {
            return Devices.FirstOrDefault(d => string.Equals(d.MachineId, machineId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Updates (or adds) the PC that sent <paramref name="status"/>.</summary>
        public Device Apply(Dictionary<string, object> status, IPEndPoint from)
        {
            string id = AdminProtocol.Str(status, "machineId");
            if (string.IsNullOrEmpty(id)) return null;
            Device d = Find(id);
            if (d == null)
            {
                d = new Device { MachineId = id };
                Devices.Add(d);
            }
            d.Apply(status, from);
            return d;
        }

        // ===================== Extra addresses =====================

        public const int MaxTargets = 4096;

        /// <summary>The addresses of <see cref="Targets"/> (names resolved: slow, call it off the UI thread).</summary>
        public static List<IPAddress> Expand(IEnumerable<string> targets)
        {
            var list = new List<IPAddress>();
            foreach (string t in targets)
            {
                string error;
                list.AddRange(ExpandOne(t, true, out error));
                if (list.Count >= MaxTargets) break;
            }
            return list.Distinct().Take(MaxTargets).ToList();
        }

        /// <summary>
        /// The addresses of one line, or an empty list with <paramref name="error"/> set. Names are only resolved when
        /// <paramref name="resolveNames"/>; otherwise a name is accepted as it is.
        /// </summary>
        public static List<IPAddress> ExpandOne(string line, bool resolveNames, out string error)
        {
            error = null;
            var list = new List<IPAddress>();
            string t = (line ?? "").Trim();
            if (t.Length == 0) return list;

            IPAddress ip;
            int slash = t.IndexOf('/');
            int dash = t.IndexOf('-');
            if (slash > 0)
            {
                int bits;
                if (!IPAddress.TryParse(t.Substring(0, slash), out ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
                    !int.TryParse(t.Substring(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out bits) || bits < 20 || bits > 32)
                {
                    error = "\"" + t + "\": use a network like 192.168.2.0/24 (from /20 to /32).";
                    return list;
                }
                uint mask = bits == 32 ? uint.MaxValue : ~(uint.MaxValue >> bits);
                uint start = ToUInt(ip) & mask, end = start | ~mask;
                if (bits <= 30) { start++; end--; }   // not the network and broadcast addresses
                for (uint a = start; a <= end && list.Count < MaxTargets; a++) list.Add(FromUInt(a));
                return list;
            }
            if (dash > 0 && IPAddress.TryParse(t.Substring(0, dash), out ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            {
                int last;
                byte[] b = ip.GetAddressBytes();
                if (!int.TryParse(t.Substring(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out last) || last < b[3] || last > 255)
                {
                    error = "\"" + t + "\": use a range like 192.168.2.10-60 (the last number only).";
                    return list;
                }
                for (int x = b[3]; x <= last; x++) list.Add(new IPAddress(new[] { b[0], b[1], b[2], (byte)x }));
                return list;
            }
            if (IPAddress.TryParse(t, out ip))
            {
                if (ip.AddressFamily != AddressFamily.InterNetwork) error = "\"" + t + "\": only IPv4 addresses are supported.";
                else list.Add(ip);
                return list;
            }
            if (t.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_')))
            {
                error = "\"" + t + "\" is not an address, a range or a computer name.";
                return list;
            }
            if (!resolveNames) return list;
            try { list.AddRange(Dns.GetHostAddresses(t).Where(a => a.AddressFamily == AddressFamily.InterNetwork)); }
            catch (SocketException) { }   // name unknown now: maybe later
            catch (ArgumentException) { }
            return list;
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
