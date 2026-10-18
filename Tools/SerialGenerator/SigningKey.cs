using System;
using System.IO;
using System.Security.Cryptography;
using NPIC_CARD_APP.Services;

namespace CardStudio.SerialGenerator
{
    /// <summary>
    /// The private key (serial-private.key, made by Installer\serial.ps1 -NewKey) that signs serial numbers.
    /// The file holds a CNG ECCPrivateBlob (ECDSA P-256) in base64.
    /// </summary>
    internal sealed class SigningKey : IDisposable
    {
        public const string FileName = "serial-private.key";

        private readonly CngKey _key;

        /// <summary>Full path of the key file.</summary>
        public string Path { get; private set; }

        /// <summary>
        /// True when this key's public half is the one compiled into Card Studio (<see cref="LicenseService.PublicKey"/>).
        /// False = serials made with it are refused by this build of Card Studio.
        /// </summary>
        public bool MatchesApp { get; private set; }

        private SigningKey(string path, CngKey key)
        {
            Path = path;
            _key = key;
            string publicKey = Convert.ToBase64String(key.Export(CngKeyBlobFormat.EccPublicBlob));
            MatchesApp = publicKey == LicenseService.PublicKey;
        }

        /// <exception cref="InvalidDataException">The file is not a Card Studio private key.</exception>
        /// <exception cref="IOException">The file cannot be read.</exception>
        public static SigningKey Load(string path)
        {
            string text = File.ReadAllText(path).Trim();
            byte[] blob;
            try { blob = Convert.FromBase64String(text); }
            catch (FormatException) { throw new InvalidDataException("This is not a Card Studio private key:\n" + path); }

            CngKey key;
            try { key = CngKey.Import(blob, CngKeyBlobFormat.EccPrivateBlob); }
            catch (CryptographicException) { throw new InvalidDataException("This is not a Card Studio private key:\n" + path); }

            if (key.Algorithm != CngAlgorithm.ECDsaP256)
            {
                key.Dispose();
                throw new InvalidDataException("This key is not an ECDSA P-256 key:\n" + path);
            }
            return new SigningKey(System.IO.Path.GetFullPath(path), key);
        }

        /// <summary>
        /// The serial number for <paramref name="machineId"/> that works up to and including <paramref name="expires"/>
        /// (null = never expires), checked before it is returned.
        /// </summary>
        public string MakeSerial(string machineId, DateTime? expires)
        {
            if (!SerialFormat.IsMachineId(machineId))
                throw new ArgumentException("Not a Machine ID.", "machineId");
            if (expires != null) expires = expires.Value.Date;

            byte[] data = SerialFormat.SignedData(2, machineId, expires);   // throws when the date is out of range
            using (var ecdsa = new ECDsaCng(_key))
            {
                byte[] signature = ecdsa.SignData(data, HashAlgorithmName.SHA256);
                if (!ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256))
                    throw new CryptographicException("The new signature could not be verified.");
                string serial = SerialFormat.Encode(signature, expires);

                // Same check Card Studio runs: decode the text again and verify with the app's own public key.
                SerialData check;
                if (MatchesApp && (!LicenseService.Verify(serial, machineId, out check) || check.Expires != expires))
                    throw new CryptographicException("The serial did not pass Card Studio's check.");
                return serial;
            }
        }

        /// <summary>
        /// Where to look for the key when the generator starts: the file chosen last time, next to the exe, then
        /// Installer\serial-private.key in any folder above the exe (when run from the repo's Tools\SerialGenerator\bin).
        /// </summary>
        public static string FindDefault()
        {
            string remembered = RememberedPath;
            if (!string.IsNullOrEmpty(remembered) && File.Exists(remembered)) return remembered;

            string dir = AppDomain.CurrentDomain.BaseDirectory;
            string beside = System.IO.Path.Combine(dir, FileName);
            if (File.Exists(beside)) return beside;

            for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
            {
                string candidate = System.IO.Path.Combine(d.FullName, "Installer", FileName);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static string SettingsFile
        {
            get
            {
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CardStudio.SerialGenerator", "key-path.txt");
            }
        }

        /// <summary>Key file chosen with Open Key… (remembered between runs; only the path, never the key).</summary>
        public static string RememberedPath
        {
            get
            {
                try { return File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile).Trim() : null; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return null; }
            }
            set
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile));
                    File.WriteAllText(SettingsFile, value ?? "");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
        }

        public void Dispose()
        {
            _key.Dispose();
        }
    }
}
