using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NPIC_CARD_APP.Rendering
{
    /// <summary>Khmer numerals, month names and the fixed phrases used on the card.</summary>
    public static class KhmerText
    {
        private static readonly string[] Months =
        {
            "មករា", "កុម្ភៈ", "មីនា", "មេសា", "ឧសភា", "មិថុនា",
            "កក្កដា", "សីហា", "កញ្ញា", "តុលា", "វិច្ឆិកា", "ធ្នូ"
        };

        private static readonly string[] DateFormats =
        {
            "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "yyyy/MM/dd"
        };

        public static string Digits(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
                sb.Append(c >= '0' && c <= '9' ? (char)('០' + (c - '0')) : c);
            return sb.ToString();
        }

        public static bool TryParseDate(string text, out DateTime date)
        {
            return DateTime.TryParseExact((text ?? "").Trim(), DateFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        /// <summary>24/01/2008 → ២៤ មករា ២០០៨ (other text is returned with Khmer digits).</summary>
        public static string Date(string text)
        {
            DateTime d;
            if (!TryParseDate(text, out d)) return Digits(text);
            return Digits(d.ToString("dd")) + " " + Months[d.Month - 1] + " " + Digits(d.Year.ToString());
        }

        /// <summary>30/12/2027 → ផុតកំណត់ថ្ងៃទី៣០ ខែធ្នូ ឆ្នាំ២០២៧</summary>
        public static string Expiry(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            DateTime d;
            if (!TryParseDate(text, out d)) return "ផុតកំណត់ " + Digits(text);
            return "ផុតកំណត់ថ្ងៃទី" + Digits(d.ToString("dd")) + " ខែ" + Months[d.Month - 1] + " ឆ្នាំ" + Digits(d.Year.ToString());
        }

        public static string Gender(string text)
        {
            string v = (text ?? "").Trim().ToLowerInvariant();
            if (v == "m" || v == "male" || v == "ប្រុស") return "ប្រុស";
            if (v == "f" || v == "female" || v == "ស្រី") return "ស្រី";
            return text ?? "";
        }

        /// <summary>"Gen 14" or "14" → ១៤</summary>
        public static string Generation(string text)
        {
            string digits = new string((text ?? "").Where(char.IsDigit).ToArray());
            return digits.Length > 0 ? Digits(digits) : (text ?? "");
        }
    }
}
