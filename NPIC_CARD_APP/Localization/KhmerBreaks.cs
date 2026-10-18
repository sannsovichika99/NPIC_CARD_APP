using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Line breaks between Khmer words. Khmer is written without spaces between words, and the Windows Forms text drawing
    /// (GDI) only wraps at spaces: a long Khmer phrase that does not fit is cut anywhere, even inside a syllable
    /// ("ឈ្មោ|ះ"). This puts an invisible zero-width space (U+200B), which GDI treats as a place where a line may break, at
    /// every Khmer word boundary. The words come from the Khmer dictionary of ICU, part of Windows 10 (1903) and later
    /// (icu.dll). Without it nothing changes.
    /// Only longer texts get break points (<see cref="MinLength"/>): short labels and column headers never wrap, and stay
    /// plain text for exports and the clipboard.
    /// </summary>
    internal static class KhmerBreaks
    {
        private const char Zwsp = '​';
        private const int MinLength = 24;
        private const int UBRK_WORD = 1;
        private const int UBRK_DONE = -1;

        private static bool _unavailable;
        private static bool? _available;

        /// <summary>
        /// True when Windows can find Khmer word boundaries (ICU). Only then is wrapped Khmer text drawn with GDI+, which
        /// breaks lines at the zero-width spaces; GDI ignores them (see <see cref="Lang.Apply"/>).
        /// </summary>
        public static bool Available
        {
            get
            {
                if (!_available.HasValue)
                {
                    string probe = Break("ការកំណត់ទាំងអស់");   // "settings" + "all": two words
                    _available = !_unavailable && probe.IndexOf(Zwsp) >= 0;
                }
                return _available.Value;
            }
        }
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);

        [DllImport("icu.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr ubrk_open(int type, string locale, [MarshalAs(UnmanagedType.LPWStr)] string text,
            int textLength, ref int status);

        [DllImport("icu.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ubrk_first(IntPtr breakIterator);

        [DllImport("icu.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ubrk_next(IntPtr breakIterator);

        [DllImport("icu.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ubrk_close(IntPtr breakIterator);

        /// <summary><paramref name="text"/> with a zero-width space between Khmer words (same text when it is short or has no Khmer).</summary>
        public static string Insert(string text)
        {
            if (_unavailable || string.IsNullOrEmpty(text) || text.Length < MinLength || !HasKhmer(text)) return text;
            string done;
            lock (Cache)
                if (Cache.TryGetValue(text, out done)) return done;

            done = Break(text);
            lock (Cache)
                if (Cache.Count < 5000) Cache[text] = done;
            return done;
        }

        private static string Break(string text)
        {
            IntPtr it = IntPtr.Zero;
            try
            {
                int status = 0;
                it = ubrk_open(UBRK_WORD, "km", text, text.Length, ref status);
                if (it == IntPtr.Zero || status > 0) return text;   // > 0 = ICU error (negative values are only warnings)

                var sb = new StringBuilder(text.Length + text.Length / 4);
                int start = ubrk_first(it), end;
                while ((end = ubrk_next(it)) != UBRK_DONE)
                {
                    // A break point only between two Khmer letters (not next to a space, digit or Latin word, which GDI
                    // already breaks at) and never twice.
                    if (start > 0 && IsKhmerLetter(text[start - 1]) && IsKhmerLetter(text[start]) && text[start - 1] != Zwsp)
                        sb.Append(Zwsp);
                    sb.Append(text, start, end - start);
                    start = end;
                }
                return sb.ToString();
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException)
            {
                _unavailable = true;   // Windows without ICU (older than 10 version 1903): plain text
                return text;
            }
            finally
            {
                if (it != IntPtr.Zero) ubrk_close(it);
            }
        }

        private static bool HasKhmer(string text)
        {
            foreach (char c in text)
                if (c >= 'ក' && c <= '៿') return true;
            return false;
        }

        private static bool IsKhmerLetter(char c)
        {
            return c >= 'ក' && c <= '៓';   // consonants, vowels and signs (not Khmer punctuation ។ ៖ or digits)
        }
    }
}
