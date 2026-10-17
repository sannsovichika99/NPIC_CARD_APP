using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace NPIC_CARD_APP.Services
{
    /// <summary>What <see cref="LicenseService.Check"/> found.</summary>
    public enum LicenseState
    {
        /// <summary>A serial number valid for this PC is saved.</summary>
        Activated,
        /// <summary>No serial number has been entered on this PC.</summary>
        Missing,
        /// <summary>A serial number is saved but it belongs to another PC (or the PC's Windows was reinstalled).</summary>
        Invalid
    }

    /// <summary>
    /// Serial number required to open Card Studio. A serial is made for ONE PC:
    ///   1. the activation window shows this PC's Machine ID (e.g. 7K2Q-M9XD-4TRB-0HCA);
    ///   2. the administrator turns it into a serial number with the Serial Generator (Tools\SerialGenerator)
    ///      or  Installer\serial.ps1 -MachineId 7K2Q-M9XD-4TRB-0HCA;
    ///   3. the user pastes the serial; it is saved and checked again on every start.
    ///
    /// The serial is an ECDSA P-256 signature of the Machine ID. The app holds only the PUBLIC key below, so serials
    /// cannot be made from the source code or the exe; the private key stays with the administrator
    /// (Installer\serial-private.key, never committed). Everything is offline: no internet needed.
    ///
    /// Saved to %ProgramData%\NPIC_CARD_APP\license.txt (all Windows users of the PC), or
    /// %AppData%\NPIC_CARD_APP\license.txt when ProgramData cannot be written. Kept out of settings.xml so
    /// Export / Import Settings never move it.
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
                if (_machineId == null)
                {
                    byte[] hash;
                    using (var sha = SHA256.Create())
                        hash = sha.ComputeHash(Encoding.UTF8.GetBytes("CardStudio.MachineId|" + MachineSource()));
                    var raw = new byte[10];   // 80 bits = 16 Base32 characters
                    Array.Copy(hash, raw, raw.Length);
                    _machineId = SerialFormat.Group(SerialFormat.ToBase32(raw), 4);
                }
                return _machineId;
            }
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

        /// <summary>The saved serial, if any ("" = none).</summary>
        public static string SavedSerial
        {
            get
            {
                foreach (string path in FilePaths)
                {
                    try
                    {
                        if (!File.Exists(path)) continue;
                        string text = File.ReadAllText(path).Trim();
                        if (text.Length > 0 && IsValidFor(text, MachineId)) return text;   // first VALID one wins
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                    }
                }
                foreach (string path in FilePaths)
                {
                    try { if (File.Exists(path)) return File.ReadAllText(path).Trim(); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                }
                return "";
            }
        }

        /// <summary>Is Card Studio activated on this PC?</summary>
        public static LicenseState Check()
        {
            string serial = SavedSerial;
            if (serial.Length == 0) return LicenseState.Missing;
            return IsValidFor(serial, MachineId) ? LicenseState.Activated : LicenseState.Invalid;
        }

        /// <summary>
        /// Checks <paramref name="serial"/> for this PC and saves it when it is valid.
        /// Returns null on success, otherwise the reason to show the user.
        /// </summary>
        public static string Activate(string serial)
        {
            byte[] signature;
            switch (SerialFormat.Decode(serial, out signature))
            {
                case SerialShape.Empty:
                    return "Enter the serial number.";
                case SerialShape.NotASerial:
                    return "This is not a Card Studio serial number: it should have " + SerialFormat.SerialLength + " letters and digits " +
                           "(now " + SerialFormat.CharacterCount(serial) + "). Paste the whole serial, including the last group.";
                case SerialShape.Typo:
                    return "The serial number has a typo. Paste it again instead of typing it.";
            }
            if (!IsValidFor(serial, MachineId))
                return "This serial number is for another PC. Send this PC's Machine ID to your administrator to get its own serial.";

            string formatted = SerialFormat.Group(SerialFormat.Normalize(serial), 5);
            Exception last = null;
            foreach (string path in FilePaths)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, formatted + Environment.NewLine);
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
                foreach (string path in FilePaths)
                    if (File.Exists(path)) return path;
                return FilePaths[0];
            }
        }

        /// <summary>True when <paramref name="serial"/> is a correctly signed serial for <paramref name="machineId"/>.</summary>
        internal static bool IsValidFor(string serial, string machineId)
        {
            byte[] signature;
            if (SerialFormat.Decode(serial, out signature) != SerialShape.Ok) return false;
            try
            {
                using (CngKey key = CngKey.Import(Convert.FromBase64String(PublicKey), CngKeyBlobFormat.EccPublicBlob))
                using (var ecdsa = new ECDsaCng(key))
                    return ecdsa.VerifyData(SerialFormat.SignedData(machineId), signature, HashAlgorithmName.SHA256);
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }
}
