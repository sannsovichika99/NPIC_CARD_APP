using System;
using System.IO;
using System.Security.Cryptography;
using NPIC_CARD_APP.Services;

namespace CardStudio.Admin
{
    /// <summary>
    /// The administrator's private key (admin-private.key: a CNG ECCPrivateBlob, ECDSA P-256, in base64) that signs every
    /// command. Card Studio only answers commands signed with the key whose public half is <see cref="AdminProtocol.PublicKey"/>.
    /// </summary>
    internal sealed class AdminKey : IDisposable
    {
        public const string FileName = "admin-private.key";

        private readonly CngKey _key;

        public string Path { get; private set; }

        /// <summary>False = this key's public half is not the one compiled into Card Studio: the PCs ignore its commands.</summary>
        public bool MatchesApp { get; private set; }

        /// <summary>Public half (EccPublicBlob, base64): the value of AdminProtocol.PublicKey for this key.</summary>
        public string PublicKey { get; private set; }

        private AdminKey(string path, CngKey key)
        {
            Path = path;
            _key = key;
            PublicKey = Convert.ToBase64String(key.Export(CngKeyBlobFormat.EccPublicBlob));
            MatchesApp = PublicKey == AdminProtocol.PublicKey;
        }

        /// <exception cref="InvalidDataException">The file is not an admin private key.</exception>
        /// <exception cref="IOException">The file cannot be read.</exception>
        public static AdminKey Load(string path)
        {
            string text = File.ReadAllText(path).Trim();
            byte[] blob;
            try { blob = Convert.FromBase64String(text); }
            catch (FormatException) { throw new InvalidDataException("This is not a Card Studio Admin private key:\n" + path); }

            CngKey key;
            try { key = CngKey.Import(blob, CngKeyBlobFormat.EccPrivateBlob); }
            catch (CryptographicException) { throw new InvalidDataException("This is not a Card Studio Admin private key:\n" + path); }

            if (key.Algorithm != CngAlgorithm.ECDsaP256)
            {
                key.Dispose();
                throw new InvalidDataException("This key is not an ECDSA P-256 key:\n" + path);
            }
            return new AdminKey(System.IO.Path.GetFullPath(path), key);
        }

        /// <summary>
        /// Makes a new key pair in <paramref name="path"/> (never over an existing file) and returns it. Its
        /// <see cref="PublicKey"/> must then be put in AdminProtocol.PublicKey and Card Studio installed again on every PC.
        /// </summary>
        public static AdminKey Create(string path)
        {
            if (File.Exists(path)) throw new IOException("A file with this name already exists:\n" + path);
            var p = new CngKeyCreationParameters { ExportPolicy = CngExportPolicies.AllowPlaintextExport };
            using (CngKey key = CngKey.Create(CngAlgorithm.ECDsaP256, null, p))
                File.WriteAllText(path, Convert.ToBase64String(key.Export(CngKeyBlobFormat.EccPrivateBlob)));
            return Load(path);
        }

        /// <summary>A new signed command (fresh time and nonce).</summary>
        public AdminCommand Sign(string command, string target, string arg)
        {
            var c = new AdminCommand
            {
                Command = command,
                Target = target,
                Time = AdminProtocol.UnixNow(),
                Nonce = AdminProtocol.NewNonce(),
                Arg = arg ?? ""
            };
            using (var ecdsa = new ECDsaCng(_key))
                c.Signature = ecdsa.SignData(AdminProtocol.SignedData(c), HashAlgorithmName.SHA256);
            return c;
        }

        /// <summary>
        /// Where to look for the key at start: the file chosen last time, next to the exe, then Installer\admin-private.key
        /// in any folder above the exe (when run from the repo's ADMIN_CARD\bin).
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
            get { return System.IO.Path.Combine(Inventory.DataFolder, "key-path.txt"); }
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
                    Directory.CreateDirectory(Inventory.DataFolder);
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
