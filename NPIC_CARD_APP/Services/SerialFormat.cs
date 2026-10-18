using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NPIC_CARD_APP.Services
{
    /// <summary>Result of reading a serial number's text (before its signature is checked).</summary>
    internal enum SerialShape
    {
        Ok,
        Empty,
        /// <summary>Not a version 1 or version 2 serial (wrong length or first character).</summary>
        NotASerial,
        /// <summary>Right length, but the check character does not match: a character was changed.</summary>
        Typo
    }

    /// <summary>What a serial number holds, read from its text (its signature is not checked yet).</summary>
    internal sealed class SerialData
    {
        /// <summary>1 = no expiry date (first format), 2 = with expiry date.</summary>
        public int Version { get; set; }

        /// <summary>Last day the serial works (local date), or null when it never expires.</summary>
        public DateTime? Expires { get; set; }

        public byte[] Signature { get; set; }
    }

    /// <summary>
    /// Text format of Card Studio serial numbers and Machine IDs. Compiled into BOTH the app (to check serials) and the
    /// Serial Generator (Tools\SerialGenerator, to make them), so the two can never disagree. Installer\serial.ps1 repeats it.
    ///
    ///   Machine ID = 16 Base32 characters (80 bits), shown as XXXX-XXXX-XXXX-XXXX.
    ///   Serial v2  = "2" + expiry (3 characters: days after 2020-01-01, 0 = never) + ECDSA P-256 signature of
    ///                "CardStudio-Serial-v2|" + Machine ID + "|" + expiry days (64 bytes = 103 characters) + 1 check character
    ///                = 108 characters, shown in groups of 5. Made by the generator since expiry dates were added.
    ///   Serial v1  = "1" + signature of "CardStudio-Serial-v1|" + Machine ID + 1 check character = 105 characters.
    ///                The first format: still accepted, never expires.
    /// The expiry date is inside the signed text, so it cannot be changed without making the serial invalid.
    /// </summary>
    internal static class SerialFormat
    {
        /// <summary>Crockford Base32: no I, L, O or U, so a serial cannot be misread.</summary>
        public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public const int MachineIdLength = 16;
        public const int SignatureBytes = 64;

        /// <summary>Length of a version 1 serial (no expiry date).</summary>
        public const int LengthV1 = 105;

        /// <summary>Length of a version 2 serial (with expiry date): the one the generator makes.</summary>
        public const int SerialLength = 108;

        /// <summary>Expiry dates are stored as days after this date; 0 = never expires.</summary>
        private static readonly DateTime Epoch = new DateTime(2020, 1, 1);

        /// <summary>3 Base32 characters = 15 bits.</summary>
        private const int MaxExpiryDays = (1 << 15) - 1;

        /// <summary>Latest expiry date a serial can hold (2109).</summary>
        public static DateTime MaxExpiry { get { return Epoch.AddDays(MaxExpiryDays); } }

        /// <summary>Earliest expiry date a serial can hold.</summary>
        public static DateTime MinExpiry { get { return Epoch.AddDays(1); } }

        /// <summary>Bytes that are signed for <paramref name="machineId"/> (any spelling: dashes, case, spaces).</summary>
        public static byte[] SignedData(int version, string machineId, DateTime? expires)
        {
            string text = version == 1
                ? "CardStudio-Serial-v1|" + Normalize(machineId)
                : "CardStudio-Serial-v2|" + Normalize(machineId) + "|" + ExpiryDays(expires).ToString(CultureInfo.InvariantCulture);
            return Encoding.UTF8.GetBytes(text);
        }

        /// <summary>True when <paramref name="text"/> is a Machine ID (16 Base32 characters, dashes / spaces ignored).</summary>
        public static bool IsMachineId(string text)
        {
            string id = Normalize(text);
            return id.Length == MachineIdLength && id.IndexOf('?') < 0;
        }

        /// <summary>Machine ID in its display form XXXX-XXXX-XXXX-XXXX.</summary>
        public static string FormatMachineId(string text)
        {
            return Group(Normalize(text), 4);
        }

        /// <summary>
        /// Turns a signature of <see cref="SignedData"/>(2, id, <paramref name="expires"/>) into the serial the user pastes
        /// (version 2, groups of 5).
        /// </summary>
        public static string Encode(byte[] signature, DateTime? expires)
        {
            if (signature == null || signature.Length != SignatureBytes)
                throw new ArgumentException("An ECDSA P-256 signature has " + SignatureBytes + " bytes.", "signature");
            int days = ExpiryDays(expires);
            string body = "2" + Alphabet[(days >> 10) & 31] + Alphabet[(days >> 5) & 31] + Alphabet[days & 31] + ToBase32(signature);
            return Group(body + CheckChar(body), 5);
        }

        /// <summary>Reads version, expiry date and signature out of a serial; <paramref name="data"/> is null unless Ok.</summary>
        public static SerialShape Decode(string serial, out SerialData data)
        {
            data = null;
            string body = Normalize(serial);
            if (body.Length == 0) return SerialShape.Empty;

            int version = body[0] == '1' && body.Length == LengthV1 ? 1 : body[0] == '2' && body.Length == SerialLength ? 2 : 0;
            if (version == 0) return SerialShape.NotASerial;
            if (CheckChar(body.Substring(0, body.Length - 1)) != body[body.Length - 1]) return SerialShape.Typo;

            int sigStart = version == 1 ? 1 : 4;
            DateTime? expires = null;
            if (version == 2)
            {
                int days = 0;
                for (int i = 1; i < 4; i++)
                {
                    int v = Alphabet.IndexOf(body[i]);
                    if (v < 0) return SerialShape.NotASerial;
                    days = (days << 5) | v;
                }
                expires = days == 0 ? (DateTime?)null : Epoch.AddDays(days);
            }

            byte[] signature = FromBase32(body.Substring(sigStart, body.Length - 1 - sigStart), SignatureBytes);
            if (signature == null) return SerialShape.NotASerial;
            data = new SerialData { Version = version, Expires = expires, Signature = signature };
            return SerialShape.Ok;
        }

        /// <summary>Days after 2020-01-01 of the last valid day; 0 = never expires.</summary>
        private static int ExpiryDays(DateTime? expires)
        {
            if (expires == null) return 0;
            int days = (int)(expires.Value.Date - Epoch).TotalDays;
            if (days < 1 || days > MaxExpiryDays)
                throw new ArgumentOutOfRangeException("expires", "The expiry date must be between " +
                    MinExpiry.ToString("yyyy-MM-dd") + " and " + MaxExpiry.ToString("yyyy-MM-dd") + ".");
            return days;
        }

        /// <summary>Letters and digits typed in a serial / Machine ID, without dashes or spaces.</summary>
        public static int CharacterCount(string text)
        {
            return Normalize(text).Length;
        }

        /// <summary>Upper case, without spaces / dashes / line breaks; O → 0 and I, L → 1 (Crockford).</summary>
        public static string Normalize(string text)
        {
            var sb = new StringBuilder();
            foreach (char ch in text ?? "")
            {
                char c = char.ToUpperInvariant(ch);
                if (c == 'O') c = '0';
                else if (c == 'I' || c == 'L') c = '1';
                if (Alphabet.IndexOf(c) >= 0) sb.Append(c);
                else if (!char.IsWhiteSpace(c) && c != '-') sb.Append('?');   // keeps the length honest; never valid
            }
            return sb.ToString();
        }

        public static string ToBase32(byte[] data)
        {
            var sb = new StringBuilder();
            int buffer = 0, bits = 0;
            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
                buffer &= (1 << bits) - 1;
            }
            if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
            return sb.ToString();
        }

        private static byte[] FromBase32(string text, int byteCount)
        {
            var result = new byte[byteCount];
            int buffer = 0, bits = 0, n = 0;
            foreach (char c in text)
            {
                int v = Alphabet.IndexOf(c);
                if (v < 0) return null;
                buffer = (buffer << 5) | v;
                bits += 5;
                if (bits >= 8)
                {
                    if (n == byteCount) return null;
                    result[n++] = (byte)(buffer >> (bits - 8));
                    bits -= 8;
                }
                buffer &= (1 << bits) - 1;
            }
            return n == byteCount ? result : null;
        }

        private static char CheckChar(string body)
        {
            using (var sha = SHA256.Create())
                return Alphabet[sha.ComputeHash(Encoding.ASCII.GetBytes(body))[0] % 32];
        }

        public static string Group(string text, int size)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i += size)
            {
                if (i > 0) sb.Append('-');
                sb.Append(text, i, Math.Min(size, text.Length - i));
            }
            return sb.ToString();
        }
    }
}
