using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace NPIC_CARD_APP.Services
{
    /// <summary>What <see cref="LicenseService.Check"/> found.</summary>
    public enum LicenseState
    {
        /// <summary>A serial number valid for this PC is saved and has not expired.</summary>
        Activated,
        /// <summary>No serial number has been entered on this PC.</summary>
        Missing,
        /// <summary>A serial number is saved but it belongs to another PC (or the PC's Windows was reinstalled).</summary>
        Invalid,
        /// <summary>The saved serial number is valid for this PC but its expiry date has passed.</summary>
        Expired,
        /// <summary>
        /// The PC's date is earlier than the last day Card Studio was used (clock turned back to stretch an expiry date,
        /// or a wrong date / dead clock battery). Only for serials with an expiry date.
        /// </summary>
        ClockWrong
    }

    /// <summary>The licence of this PC, as found by <see cref="LicenseService.Check"/>.</summary>
    public sealed class LicenseStatus
    {
        public LicenseState State { get; internal set; }

        /// <summary>Last day the saved serial works, or null when it never expires (or there is no valid serial).</summary>
        public DateTime? Expires { get; internal set; }

        /// <summary>Latest date Card Studio was used on this PC (for <see cref="LicenseState.ClockWrong"/>).</summary>
        public DateTime? LastSeen { get; internal set; }

        public bool IsActivated { get { return State == LicenseState.Activated; } }

        /// <summary>Days the serial still works after today (0 = today is the last day); null = never expires.</summary>
        public int? DaysLeft
        {
            get { return Expires == null ? (int?)null : (int)(Expires.Value.Date - DateTime.Today).TotalDays; }
        }

        /// <summary>Activated, but expires within <see cref="LicenseService.WarnDays"/> days: time to ask for a new serial.</summary>
        public bool ExpiresSoon
        {
            get { return IsActivated && DaysLeft != null && DaysLeft.Value <= LicenseService.WarnDays; }
        }

        /// <summary>"in 5 days", "tomorrow", "today (last day)", "never".</summary>
        public string DaysLeftText
        {
            get
            {
                int? d = DaysLeft;
                if (d == null) return "never";
                if (d.Value <= 0) return "today (last day)";
                return d.Value == 1 ? "tomorrow" : "in " + d.Value + " days";
            }
        }

        /// <summary>One line for System Info and Settings: "Activated until 2027-10-18 (expires in 365 days)", "Expired on …".</summary>
        public string Describe()
        {
            switch (State)
            {
                case LicenseState.Activated:
                    return Expires == null
                        ? "Activated · never expires"
                        : "Activated until " + LicenseService.FormatDate(Expires.Value) + " (expires " + DaysLeftText + ")";
                case LicenseState.Expired:
                    return "Expired on " + LicenseService.FormatDate(Expires.Value);
                case LicenseState.ClockWrong:
                    return "The PC's date is wrong (it is before " + LicenseService.FormatDate(LastSeen.Value) + ")";
                case LicenseState.Invalid:
                    return "The saved serial number is for another PC";
                default:
                    return "Not activated";
            }
        }
    }

    /// <summary>
    /// Serial number required to open Card Studio. A serial is made for ONE PC:
    ///   1. the activation window shows this PC's Machine ID (e.g. 7K2Q-M9XD-4TRB-0HCA);
    ///   2. the administrator turns it into a serial number with the Serial Generator (Tools\SerialGenerator)
    ///      or  Installer\serial.ps1 -MachineId 7K2Q-M9XD-4TRB-0HCA;
    ///   3. the user pastes the serial; it is saved and checked again on every start.
    ///
    /// The serial is an ECDSA P-256 signature of the Machine ID and the expiry date (see <see cref="SerialFormat"/>), so
    /// neither can be changed. Serials made before expiry dates existed (version 1) never expire.
    /// The app holds only the PUBLIC key below, so serials
    /// cannot be made from the source code or the exe; the private key stays with the administrator
    /// (Installer\serial-private.key, never committed). Everything is offline: no internet needed.
    ///
    /// Saved to %ProgramData%\NPIC_CARD_APP\license.txt (all Windows users of the PC), or
    /// %AppData%\NPIC_CARD_APP\license.txt when ProgramData cannot be written. Kept out of settings.xml so
    /// Export / Import Settings never move it.
    ///
    /// Expiry: a serial works up to and including its expiry date. So the date cannot be stretched by turning the PC's
    /// clock back, the latest date the app ran is kept in license-seen.txt beside license.txt; a date more than a day
    /// before it gives <see cref="LicenseState.ClockWrong"/>. Entering a NEW serial from the administrator resets it.
    /// </summary>
    public static class LicenseService
    {
        /// <summary>
        /// Public key (CNG ECCPublicBlob, base64) matching Installer\serial-private.key.
        /// Replaced by  Installer\serial.ps1 -NewKey ; serials made with an older key stop working when it changes.
        /// </summary>
        internal const string PublicKey = "RUNTMSAAAAARYKmajFgKYSshVgo0BQ3vsXZX8OV4AuUEdlfzWHKJWfKzS/py0ExZEE/XDUMnSPq5mBQ4q72+Lc3wgDIEFuyR";

        private static string _machineId;

        // ===================== Machine ID =====================

        /// <summary>This PC's ID as shown to the user: 16 characters in groups of 4 (XXXX-XXXX-XXXX-XXXX).</summary>
        public static string MachineId
        {
            get
            {
                if (_machineId == null) _machineId = MachineIdFrom(MachineSource());
                return _machineId;
            }
        }

        /// <summary>
        /// The Machine ID of a PC whose <see cref="MachineSource"/> is <paramref name="source"/> (its MachineGuid in lower case).
        /// Card Studio Admin uses it to show the ID of a PC it installs Card Studio on, read over the network.
        /// </summary>
        internal static string MachineIdFrom(string source)
        {
            byte[] hash;
            using (var sha = SHA256.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes("CardStudio.MachineId|" + source));
            var raw = new byte[10];   // 80 bits = 16 Base32 characters
            Array.Copy(hash, raw, raw.Length);
            return SerialFormat.Group(SerialFormat.ToBase32(raw), 4);
        }

        /// <summary>
        /// Windows' own per-installation ID (HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid). Stable across restarts,
        /// updates and user accounts; new after a Windows reinstall (the PC then needs a new serial).
        /// </summary>
        private static string MachineSource()
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    {
                        string guid = key == null ? null : key.GetValue("MachineGuid") as string;
                        if (!string.IsNullOrWhiteSpace(guid)) return guid.Trim().ToLowerInvariant();
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
                {
                }
            }
            return "name:" + Environment.MachineName.ToUpperInvariant();   // no MachineGuid (very unusual)
        }

        // ===================== Check / activate =====================

        /// <summary>From this many days before the expiry date the app warns (window title, status bar).</summary>
        public const int WarnDays = 30;

        /// <summary>Dates as the app shows them: 2027-10-18.</summary>
        public static string FormatDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>The saved serial in use ("" = none): the best of the license files (see <see cref="Check"/>).</summary>
        public static string SavedSerial
        {
            get
            {
                string serial, path;
                SerialData data;
                BestSaved(out serial, out path, out data);
                return serial ?? "";
            }
        }

        /// <summary>
        /// Is Card Studio activated on this PC? Reads the license files, checks signature, expiry date and the PC's clock,
        /// and remembers today as the latest date the app ran.
        /// </summary>
        public static LicenseStatus Check()
        {
            string serial, path;
            SerialData data;
            BestSaved(out serial, out path, out data);

            var status = new LicenseStatus { LastSeen = ReadLastSeen() };
            if (serial == null) status.State = LicenseState.Missing;
            else if (data == null) status.State = LicenseState.Invalid;
            else
            {
                status.Expires = data.Expires;
                status.State = IsExpired(data.Expires) ? LicenseState.Expired : LicenseState.Activated;
            }

            if (status.State == LicenseState.Activated && status.Expires != null && ClockTurnedBack(status.LastSeen))
                status.State = LicenseState.ClockWrong;
            else if (status.State == LicenseState.Activated)
                RecordLastSeen(status.LastSeen);
            return status;
        }

        /// <summary>
        /// Checks <paramref name="serial"/> for this PC and saves it when it is valid and not expired.
        /// Returns null on success, otherwise the reason to show the user.
        /// </summary>
        public static string Activate(string serial)
        {
            SerialData data;
            switch (SerialFormat.Decode(serial, out data))
            {
                case SerialShape.Empty:
                    return "Enter the serial number.";
                case SerialShape.NotASerial:
                    return "This is not a Card Studio serial number: it should have " + SerialFormat.SerialLength + " letters and digits " +
                           "(now " + SerialFormat.CharacterCount(serial) + "). Paste the whole serial, including the last group.";
                case SerialShape.Typo:
                    return "The serial number has a typo. Paste it again instead of typing it.";
            }
            if (!Verify(serial, MachineId, out data))
                return "This serial number is for another PC. Send this PC's Machine ID to your administrator to get its own serial.";
            if (IsExpired(data.Expires))
                return "This serial number expired on " + FormatDate(data.Expires.Value) + ". Ask your administrator for a new one.";

            string formatted = SerialFormat.Group(SerialFormat.Normalize(serial), 5);
            bool sameAsSaved = SerialFormat.Normalize(SavedSerial) == SerialFormat.Normalize(serial);
            DateTime? lastSeen = ReadLastSeen();
            bool resetClock = false;
            if (data.Expires != null && ClockTurnedBack(lastSeen))
            {
                if (sameAsSaved)
                    return "This PC's date (" + FormatDate(DateTime.Today) + ") is earlier than the last time Card Studio was used (" +
                           FormatDate(lastSeen.Value) + "). Set the correct date and time in Windows, then try again.";
                resetClock = true;   // a new serial from the administrator: count again from today
            }

            Exception last = null;
            foreach (string path in FilePaths)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, formatted + Environment.NewLine);
                    if (resetClock) WriteLastSeen(DateTime.Today, true);
                    else RecordLastSeen(lastSeen);
                    return null;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    last = ex;   // e.g. ProgramData file made by another Windows user: try AppData
                }
            }
            return "The serial number is valid but could not be saved:\n" + (last == null ? "" : last.Message);
        }

        /// <summary>Where the serial is saved: for all users first, then for this Windows user.</summary>
        public static string[] FilePaths
        {
            get
            {
                return new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NPIC_CARD_APP", "license.txt"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NPIC_CARD_APP", "license.txt")
                };
            }
        }

        /// <summary>The license file in use, or the one a new serial is saved to.</summary>
        public static string FilePath
        {
            get
            {
                string serial, path;
                SerialData data;
                BestSaved(out serial, out path, out data);
                return path ?? FilePaths[0];
            }
        }

        /// <summary>True when <paramref name="serial"/> is correctly signed for <paramref name="machineId"/> (expired or not).</summary>
        internal static bool IsValidFor(string serial, string machineId)
        {
            SerialData data;
            return Verify(serial, machineId, out data);
        }

        /// <summary>Signature check; <paramref name="data"/> (version, expiry date) is set when it passes, else null.</summary>
        internal static bool Verify(string serial, string machineId, out SerialData data)
        {
            if (SerialFormat.Decode(serial, out data) != SerialShape.Ok) { data = null; return false; }
            try
            {
                using (CngKey key = CngKey.Import(Convert.FromBase64String(PublicKey), CngKeyBlobFormat.EccPublicBlob))
                using (var ecdsa = new ECDsaCng(key))
                {
                    if (ecdsa.VerifyData(SerialFormat.SignedData(data.Version, machineId, data.Expires), data.Signature,
                            HashAlgorithmName.SHA256))
                        return true;
                }
            }
            catch (CryptographicException)
            {
            }
            data = null;
            return false;
        }

        /// <summary>True after the last day of <paramref name="expires"/> (never for null = no expiry).</summary>
        internal static bool IsExpired(DateTime? expires)
        {
            return expires != null && DateTime.Today > expires.Value.Date;
        }

        /// <summary>
        /// The best serial of the license files: valid and not expired (the one that lasts longest), else valid but expired
        /// (the latest), else any. Both files can hold one: a renewal goes to AppData when another Windows user owns the
        /// ProgramData file. <paramref name="data"/> is null unless the serial is valid for this PC.
        /// </summary>
        private static void BestSaved(out string serial, out string path, out SerialData data)
        {
            serial = path = null;
            data = null;
            int bestRank = 0;
            foreach (string p in FilePaths)
            {
                string text;
                try
                {
                    if (!File.Exists(p)) continue;
                    text = File.ReadAllText(p).Trim();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    continue;
                }
                if (text.Length == 0) continue;

                SerialData d;
                bool valid = Verify(text, MachineId, out d);
                int rank = !valid ? 1 : IsExpired(d.Expires) ? 2 : 3;
                bool lastsLonger = rank >= 2 && rank == bestRank && data != null && data.Expires != null &&
                                   (d.Expires == null || d.Expires.Value > data.Expires.Value);
                if (rank > bestRank || lastsLonger)
                {
                    bestRank = rank;
                    serial = text;
                    path = p;
                    data = valid ? d : null;
                }
            }
        }

        // ===================== Clock check =====================

        private static string[] LastSeenPaths
        {
            get
            {
                string[] files = FilePaths;
                var paths = new string[files.Length];
                for (int i = 0; i < files.Length; i++)
                    paths[i] = Path.Combine(Path.GetDirectoryName(files[i]), "license-seen.txt");
                return paths;
            }
        }

        /// <summary>Today is more than a day before the latest date the app ran (one day of slack for time zones).</summary>
        private static bool ClockTurnedBack(DateTime? lastSeen)
        {
            return lastSeen != null && DateTime.Today < lastSeen.Value.AddDays(-1);
        }

        /// <summary>The latest date in the license-seen files (ProgramData and AppData), or null.</summary>
        private static DateTime? ReadLastSeen()
        {
            DateTime? latest = null;
            foreach (string path in LastSeenPaths)
            {
                DateTime d;
                if (TryReadDate(path, out d) && (latest == null || d > latest.Value)) latest = d;
            }
            return latest;
        }

        private static void RecordLastSeen(DateTime? lastSeen)
        {
            if (lastSeen == null || DateTime.Today > lastSeen.Value) WriteLastSeen(DateTime.Today, false);
        }

        /// <param name="replace">True = also write an EARLIER date (a new serial was entered); false = only move forward.</param>
        private static void WriteLastSeen(DateTime date, bool replace)
        {
            foreach (string path in LastSeenPaths)
            {
                try
                {
                    DateTime old;
                    if (!replace && TryReadDate(path, out old) && old >= date) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, FormatDate(date));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // best effort: the other file may still be writable
                }
            }
        }

        private static bool TryReadDate(string path, out DateTime date)
        {
            date = default(DateTime);
            try
            {
                return File.Exists(path) && DateTime.TryParseExact(File.ReadAllText(path).Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
