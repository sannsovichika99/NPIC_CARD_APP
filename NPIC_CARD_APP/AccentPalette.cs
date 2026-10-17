using System;
using System.Drawing;
using System.Globalization;

namespace NPIC_CARD_APP
{
    /// <summary>A named primary colour offered by Theme Color….</summary>
    internal sealed class AccentPreset
    {
        public readonly string Name;
        public readonly Color Base;

        public AccentPreset(string name, int r, int g, int b)
        {
            Name = name;
            Base = Color.FromArgb(r, g, b);
        }

        public string Hex { get { return AccentPalette.ToHex(Base); } }
    }

    /// <summary>
    /// Every accent token of <see cref="Theme"/> (accent text, soft fills, outlines, filled buttons, header gradient)
    /// worked out from one primary colour, for the light or the dark theme.
    /// The default violet keeps its hand-tuned values; any other colour is derived and then corrected for contrast:
    ///   - filled buttons keep white text readable (at least 4.5:1, darkening the colour if needed);
    ///   - dark-mode accent text / icons are lightened until they reach 5.5:1 on the dark card surface.
    /// </summary>
    internal sealed class AccentPalette
    {
        /// <summary>The app's original brand violet (#6C63F5), used when no colour is saved.</summary>
        public static readonly Color DefaultBase = Color.FromArgb(108, 99, 245);

        /// <summary>Colours of the Theme Color… picker. All of them carry white text at 4.5:1 or better (Violet: 4.4:1, as before).</summary>
        public static readonly AccentPreset[] Presets =
        {
            new AccentPreset("Violet", 108, 99, 245),    // #6C63F5 (default)
            new AccentPreset("Purple", 147, 51, 234),    // #9333EA
            new AccentPreset("Pink",   219, 39, 119),    // #DB2777
            new AccentPreset("Orange", 194, 65, 12),     // #C2410C
            new AccentPreset("Green",  21, 128, 61),     // #15803D
            new AccentPreset("Teal",   15, 118, 110),    // #0F766E
            new AccentPreset("Cyan",   14, 116, 144),    // #0E7490
            new AccentPreset("Blue",   37, 99, 235),     // #2563EB
            new AccentPreset("Navy",   31, 90, 138),     // #1F5A8A (app logo blue)
            new AccentPreset("Slate",  71, 85, 105),     // #475569
        };

        // Theme.Surface / Theme.AppBack of each theme (kept here so this class does not depend on Theme's field order).
        private static readonly Color LightSurface = Color.FromArgb(255, 255, 255);
        private static readonly Color DarkSurface = Color.FromArgb(28, 29, 42);

        private const double ButtonTextContrast = 4.5;   // white on filled buttons (WCAG AA, normal text)
        private const double DarkTextContrast = 5.5;     // accent text / icons on the dark card surface

        public Color Base { get; private set; }

        // Accent used as text, icons, bars and outlines on the theme's surfaces.
        public Color Accent { get; private set; }
        public Color AccentLight { get; private set; }
        public Color AccentHover { get; private set; }
        public Color AccentPressed { get; private set; }
        public Color AccentSoft { get; private set; }
        public Color AccentSoftDown { get; private set; }
        public Color AccentOutline { get; private set; }

        // Filled (primary) buttons and selections with white text: identical in both themes.
        public Color ButtonA { get; private set; }
        public Color ButtonB { get; private set; }
        public Color ButtonHover { get; private set; }
        public Color ButtonPressed { get; private set; }

        // Gradient of the popup header strip, the first stat tile and the fallback logo badge.
        public Color GradientA { get; private set; }
        public Color GradientB { get; private set; }

        private AccentPalette() { }

        /// <summary>Palette for a saved setting ("#RRGGBB"); empty or unreadable = the default violet.</summary>
        public static AccentPalette FromSetting(string hex, bool dark)
        {
            Color c;
            return Create(TryParseHex(hex, out c) ? c : DefaultBase, dark);
        }

        public static AccentPalette Create(Color baseColor, bool dark)
        {
            baseColor = Color.FromArgb(255, baseColor);
            return baseColor.ToArgb() == DefaultBase.ToArgb() ? Violet(dark) : Derive(baseColor, dark);
        }

        /// <summary>The original hand-tuned violet tokens, unchanged.</summary>
        private static AccentPalette Violet(bool dark)
        {
            Func<int, int, int, int, int, int, Color> pick = (lr, lg, lb, dr, dg, db) =>
                dark ? Color.FromArgb(dr, dg, db) : Color.FromArgb(lr, lg, lb);
            return new AccentPalette
            {
                Base = DefaultBase,
                Accent         = pick(108, 99, 245,   150, 141, 255),
                AccentLight    = pick(150, 132, 250,  176, 164, 255),
                AccentHover    = pick(124, 115, 250,  165, 156, 255),
                AccentPressed  = pick(86, 78, 220,    124, 115, 240),
                AccentSoft     = pick(239, 238, 254,  49, 45, 92),
                AccentSoftDown = pick(226, 224, 252,  62, 57, 118),
                AccentOutline  = pick(200, 198, 248,  98, 90, 180),
                ButtonA       = Color.FromArgb(108, 99, 245),
                ButtonB       = Color.FromArgb(150, 132, 250),
                ButtonHover   = Color.FromArgb(124, 115, 250),
                ButtonPressed = Color.FromArgb(86, 78, 220),
                GradientA = Color.FromArgb(120, 124, 246),
                GradientB = Color.FromArgb(184, 156, 250),
            };
        }

        private static AccentPalette Derive(Color c, bool dark)
        {
            // Filled buttons: darken the colour until white text on it is readable.
            Color button = c;
            for (int i = 0; i < 20 && Contrast(button, Color.White) < ButtonTextContrast; i++)
                button = Mix(button, Color.Black, 0.06);

            var p = new AccentPalette
            {
                Base = c,
                ButtonA = button,
                ButtonB = Mix(button, Color.White, 0.22),
                ButtonHover = Mix(button, Color.White, 0.10),
                ButtonPressed = Mix(button, Color.Black, 0.14),
                GradientA = Mix(button, Color.White, 0.08),
                GradientB = Mix(button, Color.White, 0.42),
            };

            if (!dark)
            {
                p.Accent = button;
                p.AccentLight = Mix(button, Color.White, 0.30);
                p.AccentHover = Mix(button, Color.White, 0.10);
                p.AccentPressed = Mix(button, Color.Black, 0.14);
                p.AccentSoft = Mix(LightSurface, button, 0.09);
                p.AccentSoftDown = Mix(LightSurface, button, 0.16);
                p.AccentOutline = Mix(LightSurface, button, 0.34);
            }
            else
            {
                // Lighter accent so it reads as text on the dark surfaces.
                Color text = Mix(c, Color.White, 0.25);
                for (int i = 0; i < 20 && Contrast(text, DarkSurface) < DarkTextContrast; i++)
                    text = Mix(text, Color.White, 0.08);

                p.Accent = text;
                p.AccentLight = Mix(text, Color.White, 0.22);
                p.AccentHover = Mix(text, Color.White, 0.12);
                p.AccentPressed = Mix(text, Color.Black, 0.14);
                p.AccentSoft = Mix(DarkSurface, c, 0.24);
                p.AccentSoftDown = Mix(DarkSurface, c, 0.38);
                p.AccentOutline = Mix(DarkSurface, text, 0.60);
            }
            return p;
        }

        // ===================== Colour helpers =====================

        /// <summary>Linear blend: t = 0 gives <paramref name="a"/>, t = 1 gives <paramref name="b"/>.</summary>
        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>WCAG 2.1 contrast ratio (1 to 21).</summary>
        public static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static double Luminance(Color c)
        {
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static double Channel(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        public static string ToHex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        public static bool TryParseHex(string text, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim().TrimStart('#');
            int v;
            if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            color = Color.FromArgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
            return true;
        }

        /// <summary>Preset whose colour is <paramref name="c"/>, or null for a custom colour.</summary>
        public static AccentPreset FindPreset(Color c)
        {
            foreach (AccentPreset p in Presets)
                if (p.Base.ToArgb() == Color.FromArgb(255, c).ToArgb()) return p;
            return null;
        }

        /// <summary>Display name for a saved setting: the preset name, or "Custom #RRGGBB".</summary>
        public static string Describe(string hex)
        {
            Color c;
            if (!TryParseHex(hex, out c)) c = DefaultBase;
            AccentPreset preset = FindPreset(c);
            return preset != null ? preset.Name + " (" + preset.Hex + ")" : "Custom (" + ToHex(c) + ")";
        }
    }
}
