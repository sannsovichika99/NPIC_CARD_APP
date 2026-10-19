using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Dashboard look shared by the main window: soft lavender accent, white rounded cards with
    /// a faint coloured shadow, gradient stat tiles and line icons (Segoe Fluent Icons / MDL2 Assets).
    /// Light or dark (<see cref="AppSettings.DarkMode"/>) and the primary colour (<see cref="AppSettings.AccentColor"/>)
    /// are read once at start-up: every form copies these tokens into its own static fields, so changing either restarts the app.
    /// </summary>
    internal static class Theme
    {
        /// <summary>True when the dark palette is in use. Must stay the first field: the colours below read it.</summary>
        public static readonly bool Dark = LoadDarkSetting();

        /// <summary>
        /// Accent tokens for the primary colour chosen with Theme Color… (<see cref="AppSettings.AccentColor"/>).
        /// Must stay right after <see cref="Dark"/>: the accent colours below read it.
        /// </summary>
        public static readonly AccentPalette Palette = LoadPalette();

        private static bool LoadDarkSetting()
        {
            try { return AppSettings.Load().DarkMode; }
            catch (Exception) { return false; }
        }

        private static AccentPalette LoadPalette()
        {
            string hex = null;
            try { hex = AppSettings.Load().AccentColor; }
            catch (Exception) { }
            return AccentPalette.FromSetting(hex, Dark);
        }

        private static Color Pick(int lr, int lg, int lb, int dr, int dg, int db)
        {
            return Dark ? Color.FromArgb(dr, dg, db) : Color.FromArgb(lr, lg, lb);
        }

        // ---- Accent tokens: from the chosen primary colour (default violet), see AccentPalette ----
        // In dark mode Accent is lighter so it stays readable as text / icons on dark surfaces (≥ 5.5:1);
        // filled buttons use the Button* tokens below, which keep white text readable in both themes.
        public static readonly Color Accent         = Palette.Accent;
        public static readonly Color AccentLight    = Palette.AccentLight;
        public static readonly Color AccentHover    = Palette.AccentHover;
        public static readonly Color AccentPressed  = Palette.AccentPressed;
        public static readonly Color AccentSoft     = Palette.AccentSoft;
        public static readonly Color AccentSoftDown = Palette.AccentSoftDown;   // pressed soft-accent fill
        public static readonly Color AccentOutline  = Palette.AccentOutline;    // hover outline of outlined pills

        // ---- Colour tokens (light value, dark value) ----
        public static readonly Color AppBack      = Pick(245, 246, 250,  17, 18, 27);
        public static readonly Color Surface       = Pick(255, 255, 255,  28, 29, 42);
        public static readonly Color SurfaceAlt    = Pick(250, 250, 253,  33, 34, 49);
        public static readonly Color SurfaceHover  = Pick(246, 246, 251,  40, 41, 58);
        public static readonly Color Tile          = Pick(244, 244, 250,  37, 38, 54);
        public static readonly Color Border        = Pick(228, 229, 238,  55, 57, 78);
        public static readonly Color GridLine      = Pick(240, 240, 246,  42, 43, 60);
        public static readonly Color TextMain      = Pick(43, 45, 66,     232, 233, 245);
        public static readonly Color TextSoft      = Pick(104, 107, 130,  170, 173, 196);
        public static readonly Color TextMuted     = Pick(150, 153, 176,  128, 131, 158);
        public static readonly Color Warning       = Pick(196, 110, 0,    240, 165, 70);
        public static readonly Color WarningSoft   = Pick(255, 243, 226,  72, 52, 24);
        public static readonly Color Danger        = Pick(214, 48, 72,    255, 112, 128);
        public static readonly Color DangerSoft    = Pick(253, 233, 236,  74, 34, 44);
        public static readonly Color DangerSoftDown = Pick(250, 212, 219, 96, 40, 54);
        public static readonly Color Success       = Pick(34, 160, 100,   72, 199, 132);

        // Filled (primary) buttons and selections with white text: the primary colour in both themes.
        public static readonly Color ButtonA       = Palette.ButtonA;
        public static readonly Color ButtonB       = Palette.ButtonB;
        public static readonly Color ButtonHover   = Palette.ButtonHover;
        public static readonly Color ButtonPressed = Palette.ButtonPressed;

        // App logo colours (Assets\app_logo.png): navy line art and orange accents.
        public static readonly Color BrandBlue   = Color.FromArgb(31, 90, 138);    // #1F5A8A
        public static readonly Color BrandOrange = Color.FromArgb(230, 135, 56);   // #E68738

        /// <summary>Veil drawn over the main window while a popup is open.</summary>
        public static readonly Color Backdrop = Pick(26, 24, 58, 0, 0, 0);
        public static readonly double BackdropOpacity = Dark ? 0.5 : 0.32;

        // Gradient pairs of the three stat tiles (primary colour, coral, pink). The primary pair also colours the
        // popup header strip and the fallback logo badge (names kept from when it was always violet).
        public static readonly Color VioletA = Palette.GradientA, VioletB = Palette.GradientB;
        public static readonly Color CoralA  = Color.FromArgb(255, 132, 124), CoralB  = Color.FromArgb(255, 200, 150);
        public static readonly Color PinkA   = Color.FromArgb(255, 108, 164), PinkB   = Color.FromArgb(255, 160, 204);

        public const int ShadowPad = 12;   // space every card reserves around itself for its shadow

        // ---- Fonts ----
        // English: chrome text is Latin (Segoe UI); data text keeps CardFonts.UI for Khmer.
        // Khmer (Lang.Khmer): the Khmer card font Khmer OS Siemreap (else Khmer UI), which also has Latin letters. Segoe UI has
        // no Khmer letters, so Windows would swap in a wider font that cuts labels off. Khmer OS Siemreap draws larger than
        // Segoe UI at the same size, hence the slightly smaller Khmer sizes (KhmerScale).
        private const float KhmerScale = 0.94f;

        /// <summary>Family of the app's own text: Segoe UI, or the Khmer UI font when the app is in Khmer.</summary>
        public static readonly string Family = Lang.Khmer ? CardFonts.KhmerSiemreap : "Segoe UI";

        /// <summary>
        /// Font for the app's own text in the language in use. <paramref name="semibold"/> = Segoe UI Semibold in English,
        /// bold in Khmer (the Khmer fonts have no semibold).
        /// </summary>
        public static Font UiFont(float size, bool semibold = false, FontStyle style = FontStyle.Regular)
        {
            if (Lang.Khmer)
                return new Font(Family, (float)Math.Round(size * KhmerScale * 4) / 4f, semibold ? style | FontStyle.Bold : style);
            return new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size, style);
        }

        public static readonly Font Nav      = UiFont(9.75f);
        public static readonly Font NavBold  = UiFont(9.75f, false, FontStyle.Bold);
        public static readonly Font Small    = UiFont(8.25f);
        public static readonly Font SmallCaps = UiFont(Lang.Khmer ? 8f : 7.5f, false, FontStyle.Bold);
        public static readonly Font Button   = UiFont(9f, true);
        public static readonly Font CardTitle = UiFont(11.25f, true);
        public static readonly Font Greeting = UiFont(18f);
        public static readonly Font Brand    = UiFont(10.5f, true);

        private static string _iconFamily;
        private static bool _iconResolved;

        /// <summary>Line-icon font of Windows 11 / 10, or null when neither is installed (icons are then skipped).</summary>
        public static string IconFamily
        {
            get
            {
                if (!_iconResolved)
                {
                    _iconResolved = true;
                    _iconFamily = CardFonts.IsInstalled("Segoe Fluent Icons") ? "Segoe Fluent Icons"
                                : CardFonts.IsInstalled("Segoe MDL2 Assets") ? "Segoe MDL2 Assets"
                                : null;
                }
                return _iconFamily;
            }
        }

        // ---- Glyphs (same code points in Fluent Icons and MDL2 Assets) ----
        public const string IcoOpenFolder = "";
        public const string IcoHome = "", IcoPeople = "", IcoHistory = "", IcoPrint = "",
            IcoGlobe = "", IcoDocument = "", IcoOpenFile = "", IcoDatabase = "",
            IcoDownload = "", IcoFolder = "", IcoBrush = "", IcoEdit = "", IcoFont = "",
            IcoFullScreen = "", IcoSettings = "", IcoSearch = "", IcoMultiSelect = "",
            IcoPhoto = "", IcoList = "", IcoCloud = "", IcoSync = "",
            IcoMoon = "", IcoSun = "";   // QuietHours (moon) / Brightness (sun)

        public const string IcoColor = "", IcoCheck = "";   // Color (palette) / CheckMark

        public const string IcoTemplate = "";   // ViewAll (grid of cards): Card Template…
        public const string IcoPaneClose = "", IcoPaneOpen = "";   // ClosePane / OpenPane: sidebar icons only / full

        public const string IcoKey ="";   // Permissions (key): Activate Card Studio

        public const string IcoBackup = "";   // SaveLocal: Backup & Restore…

        // Popup header / message icons
        public const string IcoInfo = "", IcoWarning = "", IcoError = "", IcoQuestion = "",
            IcoLocation = "", IcoMove = "";

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Soft blurred shadow around <paramref name="card"/>: stacked translucent rounded rectangles.</summary>
        public static void DrawShadow(Graphics g, RectangleF card, float radius, Color tint, int spread, int maxAlpha, float offsetY)
        {
            for (int i = spread; i >= 1; i--)
            {
                RectangleF r = card;
                r.Inflate(i, i);
                r.Offset(0, offsetY);
                int alpha = Math.Max(1, maxAlpha * (spread - i + 1) / (spread * spread / 2 + 1));
                using (GraphicsPath p = RoundRect(r, radius + i))
                using (var b = new SolidBrush(Color.FromArgb(Math.Min(255, alpha), tint)))
                    g.FillPath(b, p);
            }
        }

        public static void DrawIcon(Graphics g, string glyph, float size, Color color, RectangleF bounds)
        {
            if (IconFamily == null || string.IsNullOrEmpty(glyph)) return;
            using (var font = new Font(IconFamily, size, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                TextRenderingHint old = g.TextRenderingHint;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph, font, brush, bounds, sf);
                g.TextRenderingHint = old;
            }
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, RectangleF bounds,
            StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Center)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var brush = new SolidBrush(color))
            using (var sf = new StringFormat(StringFormatFlags.NoWrap)
            {
                Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisCharacter
            })
            {
                g.DrawString(text, font, brush, bounds, sf);
            }
        }

        /// <summary>First opaque background colour up the parent chain (for controls that paint their own corners).</summary>
        public static Color EffectiveBack(Control c)
        {
            for (; c != null; c = c.Parent)
                if (c.BackColor.A == 255) return c.BackColor;
            return AppBack;
        }

        public static void HighQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }
    }

    /// <summary>
    /// Dark mode for the standard Windows controls (text boxes, combo boxes, lists, group boxes, scroll bars and the
    /// window frame), which otherwise keep the light system colours. Only colours still at their system default are
    /// replaced, so anything a form already colours from <see cref="Theme"/> is left alone. Controls added later are
    /// styled as they arrive. Does nothing in light mode.
    /// </summary>
    internal static class DarkControls
    {
        private const int LVM_GETHEADER = 0x101F;

        public static void Apply(Control root)
        {
            if (!Theme.Dark || root == null) return;
            Style(root);
        }

        private static void Style(Control c)
        {
            StyleColors(c);
            c.HandleCreated -= OnHandleCreated;   // -= first: Apply may run again on the same tree
            c.HandleCreated += OnHandleCreated;
            if (c.IsHandleCreated) StyleHandle(c);
            if (c is PropertyGrid) return;        // coloured through its own View* / Help* properties
            c.ControlAdded -= OnControlAdded;
            c.ControlAdded += OnControlAdded;
            foreach (Control child in c.Controls) Style(child);
        }

        private static void OnControlAdded(object sender, ControlEventArgs e) { Style(e.Control); }
        private static void OnHandleCreated(object sender, EventArgs e) { StyleHandle((Control)sender); }

        private static void StyleColors(Control c)
        {
            bool input = c is TextBoxBase || c is ListBox || c is ListView || c is ComboBox || c is UpDownBase || c is TreeView;
            if (IsSystem(c.BackColor, KnownColor.Window) || IsSystem(c.BackColor, KnownColor.Control))
                c.BackColor = input ? Theme.SurfaceAlt : Theme.EffectiveBack(c.Parent);
            if (IsSystem(c.ForeColor, KnownColor.WindowText) || IsSystem(c.ForeColor, KnownColor.ControlText))
                c.ForeColor = Theme.TextMain;
            else if (IsSystem(c.ForeColor, KnownColor.GrayText))
                c.ForeColor = Theme.TextMuted;

            // Visual-style rendering ignores BackColor / ForeColor on these; the flat look honours them.
            // (Combo boxes keep their native look and get Windows' dark style in StyleHandle instead.)
            var group = c as GroupBox;
            if (group != null && group.FlatStyle == FlatStyle.Standard) group.FlatStyle = FlatStyle.Flat;
            var button = c as Button;
            if (button != null && !(button is PillButton) && button.FlatStyle == FlatStyle.Standard)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                button.BackColor = Theme.SurfaceAlt;
                button.FlatAppearance.BorderColor = Theme.Border;
                button.FlatAppearance.MouseOverBackColor = Theme.SurfaceHover;
                button.FlatAppearance.MouseDownBackColor = Theme.AccentSoft;
            }

            var grid = c as DataGridView;
            if (grid != null)
            {
                if (IsSystem(grid.BackgroundColor, KnownColor.AppWorkspace)) grid.BackgroundColor = Theme.Surface;
                if (IsSystem(grid.DefaultCellStyle.BackColor, KnownColor.Window)) grid.DefaultCellStyle.BackColor = Theme.Surface;
                if (IsSystem(grid.DefaultCellStyle.ForeColor, KnownColor.ControlText)) grid.DefaultCellStyle.ForeColor = Theme.TextMain;
                if (IsSystem(grid.GridColor, KnownColor.ControlDark)) grid.GridColor = Theme.GridLine;
            }
        }

        /// <summary>Dark title bar for windows; dark combo boxes; dark scroll bars (and list headers) for scrolling controls.</summary>
        private static void StyleHandle(Control c)
        {
            if (c is Form) { Native.SetDarkFrame(c.Handle); return; }

            // Windows' own dark combo box (as in the dark file dialogs): dark field, light text, light chevron.
            // (Date pickers stay light: Windows has no dark style for their text field.)
            if (c is ComboBox) { Native.SetControlTheme(c.Handle, "DarkMode_CFD"); return; }

            var list = c as ListView;
            if (list != null)
            {
                Native.SetControlTheme(list.Handle, "DarkMode_Explorer");
                IntPtr header = Native.SendMessage(list.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (header != IntPtr.Zero) Native.SetControlTheme(header, "DarkMode_ItemsView");
                return;
            }

            var scrollable = c as ScrollableControl;
            if (c is TextBoxBase || c is ListBox || c is TreeView || c is ScrollBar || (scrollable != null && scrollable.AutoScroll))
                Native.SetControlTheme(c.Handle, "DarkMode_Explorer");
        }

        private static bool IsSystem(Color color, KnownColor known)
        {
            // Compare by name, not by value: a control painted Color.White on purpose (photos, paper) is no system colour.
            return color.IsSystemColor && color.ToKnownColor() == known;
        }
    }

    /// <summary>Background of the content area: flat app colour plus two faint decorative shapes (like the sample).</summary>
    internal sealed class DecorPanel : Panel
    {
        public DecorPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.AppBack;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Lavender ring rising from the bottom-right corner
            using (var pen = new Pen(Color.FromArgb(26, Theme.Accent), 38f))
                g.DrawEllipse(pen, Width - 230, Height - 150, 380, 380);
            // Pale coral block peeking in at the bottom-left
            using (var brush = new SolidBrush(Color.FromArgb(22, Theme.CoralA)))
                g.FillRectangle(brush, -30, Height - 120, 150, 150);
        }
    }

    /// <summary>White rounded card with a soft lavender shadow. Put content inside; Padding already clears the shadow.</summary>
    internal class ShadowCard : Panel
    {
        public const int Radius = 14;

        public ShadowCard(Padding inner)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            int s = Theme.ShadowPad;
            Padding = new Padding(s + inner.Left, s + inner.Top, s + inner.Right, s + inner.Bottom);
            Dock = DockStyle.Fill;
            Margin = new Padding(0);
        }

        protected RectangleF CardBounds
        {
            get
            {
                int s = Theme.ShadowPad;
                return new RectangleF(s, s, Width - 2 * s - 1, Height - 2 * s - 1);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width <= 2 * Theme.ShadowPad || Height <= 2 * Theme.ShadowPad) return;
            Graphics g = e.Graphics;
            Theme.HighQuality(g);
            RectangleF card = CardBounds;
            Theme.DrawShadow(g, card, Radius, Theme.Accent, Theme.ShadowPad - 2, 7, 3f);
            using (GraphicsPath p = Theme.RoundRect(card, Radius))
            {
                using (var b = new SolidBrush(Theme.Surface))
                    g.FillPath(b, p);
                if (Theme.Dark)   // a shadow barely shows on a dark background: outline the card instead
                    using (var pen = new Pen(Theme.GridLine))
                        g.DrawPath(pen, p);
            }
        }
    }

    /// <summary>Gradient KPI tile: title, big value, caption and a large line icon (Total Views / Order / Revenue in the sample).</summary>
    internal sealed class StatCard : Control
    {
        private static readonly Font TitleFont = Theme.UiFont(9.75f);
        private static readonly Font ValueFont = new Font("Segoe UI Semibold", 20.25f);   // digits only
        private static readonly Font CaptionFont = Theme.UiFont(8.25f);

        private string _value = "0", _caption = "", _title;
        public string Title { get { return _title; } set { _title = Lang.T(value); Invalidate(); } }
        public string Glyph { get; set; }
        public Color ColorA { get; set; }
        public Color ColorB { get; set; }

        public string Value { get { return _value; } set { if (_value != value) { _value = value; Invalidate(); } } }
        public string Caption
        {
            get { return _caption; }
            set { value = Lang.T(value); if (_caption != value) { _caption = value; Invalidate(); } }
        }

        public StatCard(string title, string glyph, Color a, Color b)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Title = title; Glyph = glyph; ColorA = a; ColorB = b;
            Dock = DockStyle.Fill;
            Margin = new Padding(0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int s = Theme.ShadowPad;
            if (Width <= 2 * s + 20 || Height <= 2 * s + 20) return;
            Graphics g = e.Graphics;
            Theme.HighQuality(g);

            var card = new RectangleF(s, s, Width - 2 * s - 1, Height - 2 * s - 1);
            Theme.DrawShadow(g, card, ShadowCard.Radius, ColorA, s - 1, 16, 5f);

            using (GraphicsPath p = Theme.RoundRect(card, ShadowCard.Radius))
            {
                using (var brush = new LinearGradientBrush(card, ColorA, ColorB, 20f))
                    g.FillPath(brush, p);

                // Big translucent circle on the right, clipped to the tile (sample's light "bubble").
                Region oldClip = g.Clip;
                g.SetClip(p, CombineMode.Intersect);
                using (var bubble = new SolidBrush(Color.FromArgb(34, Color.White)))
                    g.FillEllipse(bubble, card.Right - card.Height * 1.05f, card.Y - card.Height * 0.55f, card.Height * 1.5f, card.Height * 1.5f);
                g.Clip = oldClip;
            }

            float pad = 20;
            float iconSize = Math.Min(44, card.Height * 0.42f);
            Theme.DrawIcon(g, Glyph, iconSize, Color.FromArgb(235, Color.White),
                new RectangleF(card.Right - pad - iconSize - 6, card.Y + 12, iconSize + 8, iconSize + 8));

            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float textW = card.Width - 2 * pad;
            bool showCaption = card.Height >= 90;   // slim tiles (short windows) keep only title + value
            float block = showCaption ? 72 : 54;
            float y = card.Y + Math.Max(6, (card.Height - block) / 2f);
            Theme.DrawText(g, Title, TitleFont, Color.FromArgb(225, Color.White), new RectangleF(card.X + pad, y, textW - iconSize, 20));
            Theme.DrawText(g, Value, ValueFont, Color.White, new RectangleF(card.X + pad - 2, y + 18, textW, 36));
            if (showCaption)
                Theme.DrawText(g, Caption, CaptionFont, Color.FromArgb(215, Color.White), new RectangleF(card.X + pad, y + 54, textW, 18));
        }
    }

    /// <summary>Sidebar entry: line icon, label, optional count badge; the active one gets a lavender band and accent bar.</summary>
    internal sealed class NavItem : Control
    {
        private bool _hover, _active;
        private string _badge;
        public string Glyph { get; set; }

        /// <summary>Font of the label while the item is active. Null = <see cref="Theme.NavBold"/>.</summary>
        public Font ActiveFont { get; set; }

        public bool Active { get { return _active; } set { _active = value; Invalidate(); } }
        public string Badge { get { return _badge; } set { if (_badge != value) { _badge = value; Invalidate(); } } }

        private Color _badgeColor = Color.Empty;
        /// <summary>Colour of the badge text (e.g. <see cref="Theme.Warning"/>); Color.Empty = the accent colour.</summary>
        public Color BadgeColor { get { return _badgeColor; } set { if (_badgeColor != value) { _badgeColor = value; Invalidate(); } } }

        private bool _compact;
        /// <summary>Icon-only sidebar: the icon is centred, the label hidden and the badge becomes a small pill on the icon.</summary>
        public bool Compact { get { return _compact; } set { if (_compact != value) { _compact = value; Invalidate(); } } }

        /// <summary>Label of the item, in the app's language.</summary>
        public override string Text { get { return base.Text; } set { base.Text = Lang.T(value); Invalidate(); } }

        public NavItem(string text, string glyph, EventHandler onClick)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Text = text; Glyph = glyph;
            Height = 36; Dock = DockStyle.Top; Cursor = Cursors.Hand; TabStop = true;
            BackColor = Theme.Surface; Font = Theme.Nav;
            AccessibleRole = AccessibleRole.PushButton;
            if (onClick != null) Click += onClick;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Theme.HighQuality(g);

            if (_active)
            {
                var band = new Rectangle(0, 2, Width, Height - 4);
                using (var brush = new LinearGradientBrush(band, Theme.AccentSoft, Theme.Surface, 0f))
                    g.FillRectangle(brush, band);
                using (var bar = new SolidBrush(Theme.Accent))
                    g.FillRectangle(bar, 0, 2, 3, Height - 4);
            }
            else if (_hover || (Focused && ShowFocusCues))
            {
                using (GraphicsPath p = Theme.RoundRect(new RectangleF(10, 3, Width - 20, Height - 7), 8))
                using (var brush = new SolidBrush(Theme.SurfaceHover))
                    g.FillPath(brush, p);
            }

            Color fore = _active ? Theme.Accent : _hover ? Theme.TextMain : Theme.TextSoft;
            if (_compact)
            {
                Theme.DrawIcon(g, Glyph, 18f, _active ? Theme.Accent : _hover ? Theme.TextMain : Theme.TextMuted,
                               new RectangleF(0, 0, Width, Height));
                if (!string.IsNullOrEmpty(_badge))
                {
                    string text = _badge.Length > 3 ? "99+" : _badge;
                    float w = Math.Max(16f, g.MeasureString(text, Theme.Small).Width + 4f);
                    var pill = new RectangleF(Math.Min(Width / 2f + 2f, Width - w - 4f), 3f, w, 15f);
                    using (GraphicsPath p = Theme.RoundRect(pill, 7.5f))
                    using (var brush = new SolidBrush(Theme.Accent))
                        g.FillPath(brush, p);
                    Theme.DrawText(g, text, Theme.Small, Color.White, pill, StringAlignment.Center);
                }
                return;
            }
            Theme.DrawIcon(g, Glyph, 16f, _active ? Theme.Accent : Theme.TextMuted, new RectangleF(18, 0, 30, Height));

            float badgeW = string.IsNullOrEmpty(_badge) ? 0 : g.MeasureString(_badge, Theme.NavBold).Width + 8;
            Theme.DrawText(g, Text, _active ? (ActiveFont ?? Theme.NavBold) : Font, fore, new RectangleF(54, 0, Width - 54 - badgeW - 14, Height));
            if (badgeW > 0)
                Theme.DrawText(g, _badge, Theme.NavBold, _badgeColor.IsEmpty ? Theme.Accent : _badgeColor,
                               new RectangleF(Width - badgeW - 16, 0, badgeW, Height), StringAlignment.Far);
        }
    }

    /// <summary>Segmented pill switch (Today / Week / Month in the sample). Selected item gets a lavender pill.</summary>
    internal sealed class PillTabs : Control
    {
        private readonly string[] _items;
        private Rectangle[] _rects = new Rectangle[0];
        private int _selected, _hover = -1;
        public event EventHandler SelectedIndexChanged;

        public PillTabs(params string[] items)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.Selectable, true);
            _items = items.Select(Lang.T).ToArray();
            BackColor = Color.Transparent;
            Font = Theme.Nav;
            Cursor = Cursors.Hand;
            TabStop = true;
            Margin = new Padding(0, 4, 8, 4);
            Relayout();
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value < 0 || value >= _items.Length || value == _selected) return;
                _selected = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        private void Relayout()
        {
            _rects = new Rectangle[_items.Length];
            int x = 0, h = 32;
            for (int i = 0; i < _items.Length; i++)
            {
                int w = TextRenderer.MeasureText(_items[i], Theme.NavBold).Width + 30;
                _rects[i] = new Rectangle(x, 0, w, h);
                x += w + 2;
            }
            Size = new Size(Math.Max(1, x - 2), h);
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Relayout(); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hit = Array.FindIndex(_rects, r => r.Contains(e.Location));
            if (hit != _hover) { _hover = hit; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int hit = Array.FindIndex(_rects, r => r.Contains(e.Location));
            if (hit >= 0) SelectedIndex = hit;
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, _selected - 1);
            else if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(_items.Length - 1, _selected + 1);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Theme.HighQuality(g);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            for (int i = 0; i < _items.Length; i++)
            {
                RectangleF r = _rects[i];
                r.Inflate(-0.5f, -0.5f);
                bool sel = i == _selected;
                if (sel || i == _hover)
                {
                    using (GraphicsPath p = Theme.RoundRect(r, r.Height / 2))
                    using (var b = new SolidBrush(sel ? Theme.AccentSoft : Color.FromArgb(120, Theme.AccentSoft)))
                        g.FillPath(b, p);
                    if (sel && Focused && ShowFocusCues)
                        using (GraphicsPath p = Theme.RoundRect(r, r.Height / 2))
                        using (var pen = new Pen(Color.FromArgb(140, Theme.Accent)))
                            g.DrawPath(pen, p);
                }
                Theme.DrawText(g, _items[i], sel ? Theme.NavBold : Font, sel ? Theme.Accent : Theme.TextMuted, r, StringAlignment.Center);
            }
        }
    }

    /// <summary>
    /// Header language picker: the current language's flag and code with a ▾ chevron. Clicking it (or Enter / Space /
    /// ↓ while it has focus) raises <see cref="Control.Click"/>; the owner opens the language menu below it.
    /// </summary>
    internal sealed class LanguageButton : Control
    {
        private const int FlagW = 20, FlagH = 14, PadX = 12, Gap = 7, ChevronW = 8;
        private bool _hover, _open;

        public LanguageButton()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Font = Theme.NavBold;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleName = "Language";
            AccessibleRole = AccessibleRole.ButtonDropDown;
            Text = Lang.Khmer ? "KH" : "EN";
        }

        /// <summary>True while the menu is showing: the button stays highlighted and the chevron points up.</summary>
        public bool Open { get { return _open; } set { if (_open != value) { _open = value; Invalidate(); } } }

        /// <summary>The last click came from Enter / Space / ↓ (the menu then starts with an item selected).</summary>
        public bool OpenedWithKeyboard { get; private set; }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Relayout(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Relayout(); }

        private void Relayout()
        {
            int textW = TextRenderer.MeasureText(Text, Font).Width;
            Size = new Size(PadX + FlagW + Gap + textW + Gap + ChevronW + PadX, 32);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); OpenedWithKeyboard = false; base.OnMouseDown(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                OpenedWithKeyboard = true;
                OnClick(EventArgs.Empty);
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Theme.HighQuality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath p = Theme.RoundRect(r, r.Height / 2))
            {
                Color fill = _open ? Theme.AccentSoft : _hover ? Color.FromArgb(120, Theme.AccentSoft) : Theme.Surface;
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                Color edge = Focused && ShowFocusCues ? Color.FromArgb(160, Theme.Accent) : _open ? Theme.AccentSoft : Theme.Border;
                using (var pen = new Pen(edge)) g.DrawPath(pen, p);
            }

            var flag = new RectangleF(PadX, (Height - FlagH) / 2f, FlagW, FlagH);
            FlagArt.Draw(g, Lang.Khmer ? "kh" : "gb", flag);

            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Color text = _open ? Theme.Accent : Theme.TextMain;
            float textX = flag.Right + Gap;
            float chevronX = Width - PadX - ChevronW;
            Theme.DrawText(g, Text, Font, text, new RectangleF(textX - 2, 0, chevronX - textX + 4, Height));

            float cy = Height / 2f, half = ChevronW / 2f, rise = _open ? -2f : 2f;
            using (var pen = new Pen(_open ? Theme.Accent : Theme.TextMuted, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(pen, new[]
                {
                    new PointF(chevronX, cy - rise), new PointF(chevronX + half, cy + rise), new PointF(chevronX + ChevronW, cy - rise)
                });
        }
    }

    /// <summary>Small vector flags (crisp at any scaling): "gb" Union Jack for English, "kh" Cambodia for Khmer.</summary>
    internal static class FlagArt
    {
        /// <summary>Flag as a bitmap (menu item images), transparent outside its rounded corners.</summary>
        public static Bitmap Create(string code, int width = 20, int height = 14)
        {
            var bmp = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                Draw(g, code, new RectangleF(0.5f, 0.5f, width - 1, height - 1));   // keeps the outline inside
            }
            return bmp;
        }

        public static void Draw(Graphics g, string code, RectangleF r)
        {
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath clip = Theme.RoundRect(r, 2.5f))
            {
                g.SetClip(clip, CombineMode.Intersect);
                if (code == "kh") DrawCambodia(g, r); else DrawUnionJack(g, r);
                g.ResetClip();
                using (var pen = new Pen(Color.FromArgb(Theme.Dark ? 90 : 45, Theme.Dark ? Color.White : Color.Black), 1f))
                    g.DrawPath(pen, clip);
            }
            g.Restore(state);
        }

        private static void DrawUnionJack(Graphics g, RectangleF r)
        {
            Color blue = Color.FromArgb(1, 33, 105), red = Color.FromArgb(200, 16, 46);
            using (var b = new SolidBrush(blue)) g.FillRectangle(b, r);
            float h = r.Height;
            using (var white = new Pen(Color.White, h * 0.22f))
            using (var redPen = new Pen(red, h * 0.09f))
            {
                g.DrawLine(white, r.Left, r.Top, r.Right, r.Bottom);
                g.DrawLine(white, r.Left, r.Bottom, r.Right, r.Top);
                g.DrawLine(redPen, r.Left, r.Top, r.Right, r.Bottom);
                g.DrawLine(redPen, r.Left, r.Bottom, r.Right, r.Top);
            }
            float cx = r.X + r.Width / 2f, cy = r.Y + h / 2f;
            float wc = h * 0.34f, rc = h * 0.2f;
            using (var white = new SolidBrush(Color.White))
            {
                g.FillRectangle(white, cx - wc / 2, r.Top, wc, h);
                g.FillRectangle(white, r.Left, cy - wc / 2, r.Width, wc);
            }
            using (var rb = new SolidBrush(red))
            {
                g.FillRectangle(rb, cx - rc / 2, r.Top, rc, h);
                g.FillRectangle(rb, r.Left, cy - rc / 2, r.Width, rc);
            }
        }

        private static void DrawCambodia(Graphics g, RectangleF r)
        {
            Color blue = Color.FromArgb(3, 46, 161), red = Color.FromArgb(224, 0, 37);
            float w = r.Width, h = r.Height;
            using (var b = new SolidBrush(blue)) g.FillRectangle(b, r);
            using (var b = new SolidBrush(red)) g.FillRectangle(b, r.X, r.Y + h / 4f, w, h / 2f);

            // Angkor Wat, simplified: terrace, gallery and five towers (the middle one tallest).
            float cx = r.X + w / 2f;
            Func<float, float> Y = f => r.Y + h * f;
            using (var white = new SolidBrush(Color.White))
            {
                g.FillRectangle(white, cx - w * 0.30f, Y(0.64f), w * 0.60f, h * 0.07f);   // terrace
                g.FillRectangle(white, cx - w * 0.25f, Y(0.53f), w * 0.50f, h * 0.12f);   // gallery
                Tower(g, white, cx, w * 0.11f, Y(0.28f), Y(0.54f));
                Tower(g, white, cx - w * 0.14f, w * 0.09f, Y(0.36f), Y(0.54f));
                Tower(g, white, cx + w * 0.14f, w * 0.09f, Y(0.36f), Y(0.54f));
                Tower(g, white, cx - w * 0.25f, w * 0.07f, Y(0.44f), Y(0.54f));
                Tower(g, white, cx + w * 0.25f, w * 0.07f, Y(0.44f), Y(0.54f));
            }
        }

        /// <summary>Tower with a pointed top: a body plus a spire about a third of its height.</summary>
        private static void Tower(Graphics g, Brush b, float cx, float width, float top, float bottom)
        {
            float shoulder = top + (bottom - top) * 0.38f, half = width / 2f;
            g.FillPolygon(b, new[]
            {
                new PointF(cx, top), new PointF(cx + half, shoulder), new PointF(cx + half, bottom),
                new PointF(cx - half, bottom), new PointF(cx - half, shoulder)
            });
        }
    }

    internal enum PillKind { Primary, Secondary, Ghost }

    /// <summary>Rounded flat button: Primary = violet gradient, Secondary = white outlined, Ghost = accent text only.</summary>
    internal sealed class PillButton : Button
    {
        private bool _hover, _down, _checked;
        public PillKind Kind { get; private set; }

        /// <summary>Toggle look: a checked Ghost / Secondary pill stays filled with the soft accent and outlined.</summary>
        public bool Checked { get { return _checked; } set { if (_checked == value) return; _checked = value; Invalidate(); } }

        /// <summary>Caption, in the app's language.</summary>
        public override string Text { get { return base.Text; } set { base.Text = Lang.T(value); } }

        public PillButton(PillKind kind)
        {
            Kind = kind;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = Theme.Button;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.EffectiveBack(Parent));
            Theme.HighQuality(g);

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color text;
            using (GraphicsPath p = Theme.RoundRect(r, 9))
            {
                if (!Enabled)
                {
                    using (var b = new SolidBrush(Theme.Tile)) g.FillPath(b, p);
                    text = Theme.TextMuted;
                }
                else if (Kind == PillKind.Primary)
                {
                    Color a = _down ? Theme.ButtonPressed : _hover ? Theme.ButtonHover : Theme.ButtonA;
                    Color b2 = _down ? Theme.ButtonPressed : Theme.ButtonB;
                    using (var b = new LinearGradientBrush(r, a, b2, 0f)) g.FillPath(b, p);
                    text = Color.White;
                }
                else if (Kind == PillKind.Secondary)
                {
                    using (var b = new SolidBrush(_down ? Theme.AccentSoft : _hover ? Theme.SurfaceHover : Theme.Surface)) g.FillPath(b, p);
                    using (var pen = new Pen(_hover ? Theme.AccentOutline : Theme.Border)) g.DrawPath(pen, p);
                    text = Theme.TextMain;
                }
                else
                {
                    if (_hover || _down || _checked)
                        using (var b = new SolidBrush(_down || (_checked && _hover) ? Theme.AccentSoftDown : Theme.AccentSoft)) g.FillPath(b, p);
                    text = Theme.Accent;
                }
                if (_checked && Enabled && Kind != PillKind.Primary)
                    using (var pen = new Pen(Theme.AccentLight)) g.DrawPath(pen, p);
            }

            if (Focused && ShowFocusCues && Enabled)
            {
                var f = new RectangleF(2.5f, 2.5f, Width - 5.5f, Height - 5.5f);
                using (GraphicsPath p = Theme.RoundRect(f, 7))
                using (var pen = new Pen(Kind == PillKind.Primary ? Color.FromArgb(170, Color.White) : Color.FromArgb(150, Theme.Accent)))
                    g.DrawPath(pen, p);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Rounded search field: magnifier glyph + borderless text box; the outline turns violet on focus.</summary>
    internal sealed class SearchField : Panel
    {
        public TextBox Box { get; private set; }

        public SearchField(Font font)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
            Height = 40;
            Box = new TextBox { BorderStyle = BorderStyle.None, Font = font, BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain };
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Controls.Add(Box);
            Cursor = Cursors.IBeam;
            Click += (s, e) => Box.Focus();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Box == null) return;
            int h = Box.PreferredHeight;
            Box.SetBounds(38, Math.Max(2, (Height - h) / 2 + 1), Math.Max(10, Width - 50), h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Theme.HighQuality(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.RoundRect(r, 10))
            {
                using (var b = new SolidBrush(Theme.SurfaceAlt)) g.FillPath(b, p);
                using (var pen = new Pen(Box.Focused ? Theme.AccentLight : Theme.Border)) g.DrawPath(pen, p);
            }
            Theme.DrawIcon(g, Theme.IcoSearch, 14f, Box.Focused ? Theme.Accent : Theme.TextMuted, new RectangleF(8, 0, 26, Height));
        }
    }

    /// <summary>Card thumbnail on a soft rounded tile; draws the image fitted with a small drop shadow.</summary>
    internal sealed class PreviewTile : Control
    {
        private Image _image;
        private bool _highlight, _showGrid;
        private string _emptyText;

        /// <summary>Shown while there is no image, in the app's language.</summary>
        public string EmptyText { get { return _emptyText; } set { _emptyText = Lang.T(value); Invalidate(); } }

        /// <summary>Millimetre layout grid over the card (screen only, never printed).</summary>
        public bool ShowGrid { get { return _showGrid; } set { if (_showGrid == value) return; _showGrid = value; Invalidate(); } }

        public PreviewTile()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            Dock = DockStyle.Fill;
            EmptyText = "Select a student";
        }

        /// <summary>The tile does not own the image: the caller disposes the old one after replacing it.</summary>
        public Image Image { get { return _image; } set { _image = value; Invalidate(); } }

        /// <summary>Violet outline = the side that Print Selected will print.</summary>
        public bool Highlight { get { return _highlight; } set { _highlight = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Theme.HighQuality(g);
            var tile = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.RoundRect(tile, 12))
            {
                using (var b = new SolidBrush(Theme.Tile)) g.FillPath(b, p);
                if (_highlight)
                    using (var pen = new Pen(Theme.AccentLight, 1.5f)) g.DrawPath(pen, p);
            }

            if (_image == null)
            {
                Theme.DrawText(g, EmptyText, Theme.Small, Theme.TextMuted, tile, StringAlignment.Center);
                return;
            }

            float pad = 14;
            float availW = Width - 2 * pad, availH = Height - 2 * pad;
            if (availW < 4 || availH < 4) return;
            float scale = Math.Min(availW / _image.Width, availH / _image.Height);
            float w = _image.Width * scale, h = _image.Height * scale;
            var dest = new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);

            Theme.DrawShadow(g, dest, 6, Color.FromArgb(40, 40, 80), 5, 14, 2f);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (GraphicsPath clip = Theme.RoundRect(dest, Math.Max(2, w * 0.035f)))
            {
                Region old = g.Clip;
                g.SetClip(clip, CombineMode.Intersect);
                g.DrawImage(_image, dest);
                if (_showGrid) NPIC_CARD_APP.Rendering.CardGridOverlay.Draw(g, dest);
                g.Clip = old;
            }
        }
    }

    /// <summary>Sidebar brand block: logo badge, app name and subtitle, with a divider underneath.</summary>
    internal sealed class BrandBlock : Control
    {
        private readonly Image _logo;
        private readonly string _subtitle;

        public BrandBlock(string title, string subtitle, Image logo)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            Text = title; _subtitle = Lang.T(subtitle); _logo = logo;
            BackColor = Theme.Surface;
            Dock = DockStyle.Top;
            Height = 96;
        }

        private bool _compact;
        /// <summary>Icon-only sidebar: just the logo badge, centred.</summary>
        public bool Compact { get { return _compact; } set { if (_compact != value) { _compact = value; Invalidate(); } } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Theme.HighQuality(g);

            var badge = new RectangleF(_compact ? (Width - 44) / 2f : 18, _compact ? 26 : 24, _compact ? 44 : 48, _compact ? 44 : 48);
            Theme.DrawShadow(g, badge, 12, _logo != null ? Theme.BrandBlue : Theme.Accent, 6, 14, 3f);
            using (GraphicsPath p = Theme.RoundRect(badge, 12))
            {
                if (_logo != null)
                {
                    // The logo's navy line art needs a light tile, so the badge stays white in dark mode too.
                    using (var b = new SolidBrush(Color.White)) g.FillPath(b, p);
                    using (var pen = new Pen(Color.FromArgb(Theme.Dark ? 0 : 60, Theme.BrandBlue))) g.DrawPath(pen, p);
                    RectangleF inner = badge;
                    inner.Inflate(-3, -3);
                    float scale = Math.Min(inner.Width / _logo.Width, inner.Height / _logo.Height);
                    float w = _logo.Width * scale, h = _logo.Height * scale;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(_logo, inner.X + (inner.Width - w) / 2, inner.Y + (inner.Height - h) / 2, w, h);
                }
                else
                {
                    using (var b = new LinearGradientBrush(badge, Theme.VioletA, Theme.VioletB, 45f)) g.FillPath(b, p);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    Theme.DrawText(g, "NP", Theme.NavBold, Color.White, badge, StringAlignment.Center);
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                }
            }

            int inset = 20;
            if (!_compact)
            {
                Theme.DrawText(g, Text, Theme.Brand, Theme.TextMain, new RectangleF(76, 28, Width - 82, 22));
                Theme.DrawText(g, _subtitle, Theme.Small, Theme.TextMuted, new RectangleF(76, 49, Width - 82, 18));
            }
            else
            {
                inset = 14;
            }
            using (var pen = new Pen(Theme.GridLine))
                g.DrawLine(pen, inset, Height - 1, Width - inset, Height - 1);
        }
    }

    /// <summary>Card header: violet ring bullet + title on the left, extra controls on the right.</summary>
    internal sealed class CardHeader : Panel
    {
        private readonly FlowLayoutPanel _extras;

        public CardHeader(string title, params Control[] extras)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            Text = Lang.T(title);
            Dock = DockStyle.Top;
            Height = 40;
            BackColor = Theme.Surface;
            _extras = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
                BackColor = Theme.Surface, Padding = new Padding(0, 2, 0, 0), Margin = new Padding(0)
            };
            foreach (Control c in extras) _extras.Controls.Add(c);
            Controls.Add(_extras);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Theme.HighQuality(g);
            float cy = 19;
            using (var soft = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(soft, 0, cy - 9, 18, 18);
            using (var pen = new Pen(Theme.Accent, 2.4f)) g.DrawEllipse(pen, 4.5f, cy - 4.5f, 9, 9);
            Theme.DrawText(g, Text, Theme.CardTitle, Theme.TextMain, new RectangleF(28, 0, Math.Max(10, Width - 28 - _extras.Width), 38));
        }
    }
}
