using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// Card Studio Admin (ADMIN_CARD) ↔ Card Studio on every PC of the network. Shared by both projects
    /// (ADMIN_CARD links this file), so the two always speak the same version.
    ///
    /// Small JSON datagrams on UDP <see cref="Port"/>:
    ///   admin → PC  {"t":"cmd","v":1,"c":"discover","to":"*","ts":1767000000,"n":"NONCE","a":"","sig":"BASE64"}
    ///               sent to the broadcast address (discover) or to one PC (every other command, "to" = its Machine ID)
    ///   PC → admin  {"t":"reply","v":1,"n":"NONCE","ok":true,"msg":"…","status":{…}}   to the address the command came from
    ///
    /// Every command is signed with the administrator's private key (Installer\admin-private.key, ECDSA P-256) and checked
    /// against <see cref="PublicKey"/> compiled into Card Studio, so nobody else on the network can list the PCs or send them
    /// commands. A command is refused when its time is more than <see cref="MaxClockSkew"/> away from the PC's clock, and a
    /// nonce is only used once (a copy of a command gets the first reply again, it never runs twice).
    /// </summary>
    public static class AdminProtocol
    {
        public const int Port = 8766;
        public const int Version = 1;

        /// <summary>Largest datagram either side accepts (a status reply is about 1–2 KB).</summary>
        public const int MaxPacketBytes = 16 * 1024;
        public const int MaxMessageLength = 1000;

        public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Public half of Installer\admin-private.key (CNG EccPublicBlob, base64). A new key (Key… → Create New Key in
        /// ADMIN_CARD) only works with the PCs after this value is replaced and Card Studio is rebuilt and installed again.
        /// </summary>
        public const string PublicKey = "RUNTMSAAAABVB/rmd60SD8g2kwnOjZjBSx28qLLIQME1zFcePRYYQVqwkQW3IhU29rPdOE+vjCbOmBCNWn3itV4hiVSu84EC";

        /// <summary>"to" of a discover sent to every PC.</summary>
        public const string Everyone = "*";

        // ---- commands ----
        /// <summary>Reply with the status. The only command that may go to <see cref="Everyone"/>.</summary>
        public const string Discover = "discover";
        /// <summary>Show "a" (text) to the user of the PC.</summary>
        public const string Message = "message";
        /// <summary>Open Check for Updates on the PC.</summary>
        public const string CheckUpdates = "update";
        /// <summary>Install the serial number "a" (made for the PC's Machine ID with the Serial Generator).</summary>
        public const string InstallSerial = "serial";
        /// <summary>New mobile pairing code: every paired phone must scan the QR code again.</summary>
        public const string RevokePhones = "revoke";

        public static bool IsKnownCommand(string c)
        {
            return c == Discover || c == Message || c == CheckUpdates || c == InstallSerial || c == RevokePhones;
        }

        /// <summary>The bytes that are signed: every field length-prefixed, so no field can run into the next.</summary>
        public static byte[] SignedData(AdminCommand c)
        {
            var sb = new StringBuilder("CardStudio.Admin");
            foreach (string field in new[]
                     {
                         Version.ToString(CultureInfo.InvariantCulture), c.Command ?? "", c.Target ?? "",
                         c.Time.ToString(CultureInfo.InvariantCulture), c.Nonce ?? "", c.Arg ?? ""
                     })
                sb.Append('|').Append(field.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(field);
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>True when <paramref name="c"/> was signed with the administrator's key.</summary>
        public static bool Verify(AdminCommand c)
        {
            if (c == null || c.Signature == null || c.Signature.Length != 64) return false;
            try
            {
                using (CngKey key = CngKey.Import(Convert.FromBase64String(PublicKey), CngKeyBlobFormat.EccPublicBlob))
                using (var ecdsa = new ECDsaCng(key))
                    return ecdsa.VerifyData(SignedData(c), c.Signature, HashAlgorithmName.SHA256);
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        /// <summary>16 random bytes, base64url (22 characters).</summary>
        public static string NewNonce()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static long UnixNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        // ===================== JSON =====================

        private static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaxPacketBytes * 2 };
        }

        public static byte[] Encode(Dictionary<string, object> packet)
        {
            return Encoding.UTF8.GetBytes(Json().Serialize(packet));
        }

        /// <summary>The JSON object in <paramref name="data"/>, or null when it is not one (or too big).</summary>
        public static Dictionary<string, object> Decode(byte[] data, int length)
        {
            if (data == null || length <= 1 || length > MaxPacketBytes || data[0] != (byte)'{') return null;
            try
            {
                return Json().DeserializeObject(Encoding.UTF8.GetString(data, 0, length)) as Dictionary<string, object>;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return null;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static long? Long(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return null;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch (FormatException) { return null; }
            catch (InvalidCastException) { return null; }
            catch (OverflowException) { return null; }
        }

        public static bool Bool(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is bool && (bool)v;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<string> Strings(Dictionary<string, object> d, string key)
        {
            object v;
            var list = new List<string>();
            if (d == null || !d.TryGetValue(key, out v)) return list;
            var items = v as System.Collections.IEnumerable;
            if (items == null || v is string) return list;
            foreach (object item in items)
                if (item != null) list.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
            return list;
        }

        public static string Clip(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }

    /// <summary>One signed command from the administrator.</summary>
    public sealed class AdminCommand
    {
        public string Command { get; set; }
        /// <summary>Machine ID of the PC, or <see cref="AdminProtocol.Everyone"/> (discover only).</summary>
        public string Target { get; set; }
        /// <summary>Unix time (seconds, UTC) when the administrator sent it.</summary>
        public long Time { get; set; }
        public string Nonce { get; set; }
        /// <summary>Argument: message text, serial number… ("" when the command has none).</summary>
        public string Arg { get; set; }
        public byte[] Signature { get; set; }

        public Dictionary<string, object> ToPacket()
        {
            return new Dictionary<string, object>
            {
                { "t", "cmd" },
                { "v", AdminProtocol.Version },
                { "c", Command },
                { "to", Target },
                { "ts", Time },
                { "n", Nonce },
                { "a", Arg ?? "" },
                { "sig", Signature == null ? "" : Convert.ToBase64String(Signature) }
            };
        }

        /// <summary>The command in <paramref name="p"/>, or null when it is not a well-formed command of this version.</summary>
        public static AdminCommand FromPacket(Dictionary<string, object> p)
        {
            if (AdminProtocol.Str(p, "t") != "cmd" || AdminProtocol.Long(p, "v") != AdminProtocol.Version) return null;
            string command = AdminProtocol.Str(p, "c");
            string target = AdminProtocol.Str(p, "to");
            string nonce = AdminProtocol.Str(p, "n");
            long? time = AdminProtocol.Long(p, "ts");
            string arg = AdminProtocol.Str(p, "a") ?? "";
            if (!AdminProtocol.IsKnownCommand(command) || string.IsNullOrEmpty(target) || target.Length > 64 ||
                string.IsNullOrEmpty(nonce) || nonce.Length < 16 || nonce.Length > 64 || time == null ||
                arg.Length > AdminProtocol.MaxMessageLength)
                return null;
            byte[] sig;
            try { sig = Convert.FromBase64String(AdminProtocol.Str(p, "sig") ?? ""); }
            catch (FormatException) { return null; }
            return new AdminCommand { Command = command, Target = target, Time = time.Value, Nonce = nonce, Arg = arg, Signature = sig };
        }
    }

    /// <summary>What a PC answered.</summary>
    public sealed class AdminReply
    {
        public string Nonce { get; set; }
        public bool Ok { get; set; }
        /// <summary>What happened, or why the command could not run.</summary>
        public string Message { get; set; }
        /// <summary>"clock" when the PC refused the command because its clock (or the admin's) is wrong.</summary>
        public string ErrorCode { get; set; }
        /// <summary>Status of the PC (see AdminAgent.Status); null in a clock error.</summary>
        public Dictionary<string, object> Status { get; set; }

        public Dictionary<string, object> ToPacket()
        {
            var p = new Dictionary<string, object>
            {
                { "t", "reply" },
                { "v", AdminProtocol.Version },
                { "n", Nonce },
                { "ok", Ok },
                { "msg", Message ?? "" }
            };
            if (!string.IsNullOrEmpty(ErrorCode)) p["err"] = ErrorCode;
            if (Status != null) p["status"] = Status;
            return p;
        }

        public static AdminReply FromPacket(Dictionary<string, object> p)
        {
            if (AdminProtocol.Str(p, "t") != "reply" || AdminProtocol.Long(p, "v") != AdminProtocol.Version) return null;
            string nonce = AdminProtocol.Str(p, "n");
            if (string.IsNullOrEmpty(nonce)) return null;
            return new AdminReply
            {
                Nonce = nonce,
                Ok = AdminProtocol.Bool(p, "ok"),
                Message = AdminProtocol.Str(p, "msg") ?? "",
                ErrorCode = AdminProtocol.Str(p, "err"),
                Status = AdminProtocol.Obj(p, "status")
            };
        }
    }
}
