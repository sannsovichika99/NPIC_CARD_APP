using System;
using System.Security.Cryptography;
using System.Text;

namespace NPIC_CARD_APP.Services
{
    /// <summary>Result of reading a serial number's text (before its signature is checked).</summary>
    internal enum SerialShape
    {
        Ok,
        Empty,
        /// <summary>Not 105 Base32 characters starting with the format version.</summary>
        NotASerial,
        /// <summary>Right length, but the check character does not match: a character was changed.</summary>
        Typo
    }

    /// <summary>
    /// Text format of Card Studio serial numbers and Machine IDs. Compiled into BOTH the app (to check serials) and the
    /// Serial Generator (Tools\SerialGenerator, to make them), so the two can never disagree. Installer\serial.ps1 repeats it.
    ///
    ///   Machine ID = 16 Base32 characters (80 bits), shown as XXXX-XXXX-XXXX-XXXX.
    ///   Serial     = "1" (format version) + ECDSA P-256 signature of <see cref="SignedPrefix"/> + Machine ID (64 bytes =
    ///                103 characters) + 1 check character = 105 characters, shown in groups of 5.
    /// </summary>
    internal static class SerialFormat
    {
        /// <summary>First character of every serial: the format version.</summary>
        public const char FormatVersion = '1';

        /// <summary>Signed text = this prefix + the Machine ID (16 characters, no dashes).</summary>
        public const string SignedPrefix = "CardStudio-Serial-v1|";

        /// <summary>Crockford Base32: no I, L, O or U, so a serial cannot be misread.</summary>
        public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public const int MachineIdLength = 16;
        public const int SignatureBytes = 64;

        /// <summary>1 version + 103 signature + 1 check character.</summary>
        public const int SerialLength = 105;

        /// <summary>Bytes that are signed for <paramref name="machineId"/> (any spelling: dashes, case, spaces).</summary>
        public static byte[] SignedData(string machineId)
        {
            return Encoding.UTF8.GetBytes(SignedPrefix + Normalize(machineId));
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

        /// <summary>Turns a 64-byte signature into the serial the user pastes (groups of 5).</summary>
        public static string Encode(byte[] signature)
        {
            if (signature == null || signature.Length != SignatureBytes)
                throw new ArgumentException("An ECDSA P-256 signature has " + SignatureBytes + " bytes.", "signature");
            string body = FormatVersion + ToBase32(signature);
            return Group(body + CheckChar(body), 5);
        }

        /// <summary>Reads the signature back out of a serial; null unless the result is <see cref="SerialShape.Ok"/>.</summary>
        public static SerialShape Decode(string serial, out byte[] signature)
        {
            signature = null;
            string body = Normalize(serial);
            if (body.Length == 0) return SerialShape.Empty;
            if (body.Length != SerialLength || body[0] != FormatVersion) return SerialShape.NotASerial;
            if (CheckChar(body.Substring(0, SerialLength - 1)) != body[SerialLength - 1]) return SerialShape.Typo;
            signature = FromBase32(body.Substring(1, SerialLength - 2), SignatureBytes);
            return signature == null ? SerialShape.NotASerial : SerialShape.Ok;
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
