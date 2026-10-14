using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Dashboard look shared by the main window: soft lavender accent, white rounded cards with
    /// a faint coloured shadow, gradient stat tiles and line icons (Segoe Fluent Icons / MDL2 Assets).
    /// </summary>
    internal static class Theme
    {
        // ---- Colour tokens ----
        public static readonly Color Accent        = Color.FromArgb(108, 99, 245);
        public static readonly Color AccentLight   = Color.FromArgb(150, 132, 250);
        public static readonly Color AccentHover   = Color.FromArgb(124, 115, 250);
        public static readonly Color AccentPressed = Color.FromArgb(86, 78, 220);
        public static readonly Color AccentSoft    = Color.FromArgb(239, 238, 254);
        public static readonly Color AppBack       = Color.FromArgb(245, 246, 250);
        public static readonly Color Surface       = Color.White;
        public static readonly Color SurfaceAlt    = Color.FromArgb(250, 250, 253);
        public static readonly Color SurfaceHover  = Color.FromArgb(246, 246, 251);
        public static readonly Color Tile          = Color.FromArgb(244, 244, 250);
        public static readonly Color Border        = Color.FromArgb(228, 229, 238);
        public static readonly Color GridLine      = Color.FromArgb(240, 240, 246);
        public static readonly Color TextMain      = Color.FromArgb(43, 45, 66);
        public static readonly Color TextSoft      = Color.FromArgb(104, 107, 130);
        public static readonly Color TextMuted     = Color.FromArgb(150, 153, 176);
        public static readonly Color Warning       = Color.FromArgb(196, 110, 0);
        public static readonly Color Danger        = Color.FromArgb(214, 48, 72);
        public static readonly Color Success       = Color.FromArgb(34, 160, 100);

        // Gradient pairs of the three stat tiles (violet, coral, pink).
        public static readonly Color VioletA = Color.FromArgb(120, 124, 246), VioletB = Color.FromArgb(184, 156, 250);
        public static readonly Color CoralA  = Color.FromArgb(255, 132, 124), CoralB  = Color.FromArgb(255, 200, 150);
        public static readonly Color PinkA   = Color.FromArgb(255, 108, 164), PinkB   = Color.FromArgb(255, 160, 204);

        public const int ShadowPad = 12;   // space every card reserves around itself for its shadow

        // ---- Fonts (chrome text is Latin: Segoe UI; data text keeps CardFonts.UI for Khmer) ----
        public const string Family = "Segoe UI";
        public static readonly Font Nav      = new Font(Family, 9.75f);
        public static readonly Font NavBold  = new Font(Family, 9.75f, FontStyle.Bold);
        public static readonly Font Small    = new Font(Family, 8.25f);
        public static readonly Font SmallCaps = new Font(Family, 7.5f, FontStyle.Bold);
        public static readonly Font Button   = new Font("Segoe UI Semibold", 9f);
        public static readonly Font CardTitle = new Font("Segoe UI Semibold", 11.25f);
        public static readonly Font Greeting = new Font(Family, 18f);
        public static readonly Font Brand    = new Font("Segoe UI Semibold", 10.5f);

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
            IcoPhoto = "", IcoList = "";

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
            using (var b = new SolidBrush(Theme.Surface))
                g.FillPath(b, p);
        }
    }

    /// <summary>Gradient KPI tile: title, big value, caption and a large line icon (Total Views / Order / Revenue in the sample).</summary>
    internal sealed class StatCard : Control
    {
        private static readonly Font TitleFont = new Font(Theme.Family, 9.75f);
        private static readonly Font ValueFont = new Font("Segoe UI Semibold", 20.25f);
        private static readonly Font CaptionFont = new Font(Theme.Family, 8.25f);

        private string _value = "0", _caption = "";
        public string Title { get; set; }
        public string Glyph { get; set; }
        public Color ColorA { get; set; }
        public Color ColorB { get; set; }

        public string Value { get { return _value; } set { if (_value != value) { _value = value; Invalidate(); } } }
        public string Caption { get { return _caption; } set { if (_caption != value) { _caption = value; Invalidate(); } } }

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

        public bool Active { get { return _active; } set { _active = value; Invalidate(); } }
        public string Badge { get { return _badge; } set { if (_badge != value) { _badge = value; Invalidate(); } } }

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
            Theme.DrawIcon(g, Glyph, 16f, _active ? Theme.Accent : Theme.TextMuted, new RectangleF(18, 0, 30, Height));

            float badgeW = string.IsNullOrEmpty(_badge) ? 0 : g.MeasureString(_badge, Theme.NavBold).Width + 8;
            Theme.DrawText(g, Text, _active ? Theme.NavBold : Font, fore, new RectangleF(54, 0, Width - 54 - badgeW - 14, Height));
            if (badgeW > 0)
                Theme.DrawText(g, _badge, Theme.NavBold, Theme.Accent, new RectangleF(Width - badgeW - 16, 0, badgeW, Height), StringAlignment.Far);
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
            _items = items;
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

    internal enum PillKind { Primary, Secondary, Ghost }

    /// <summary>Rounded flat button: Primary = violet gradient, Secondary = white outlined, Ghost = accent text only.</summary>
    internal sealed class PillButton : Button
    {
        private bool _hover, _down;
        public PillKind Kind { get; private set; }

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
                    Color a = _down ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
                    Color b2 = _down ? Theme.AccentPressed : Theme.AccentLight;
                    using (var b = new LinearGradientBrush(r, a, b2, 0f)) g.FillPath(b, p);
                    text = Color.White;
                }
                else if (Kind == PillKind.Secondary)
                {
                    using (var b = new SolidBrush(_down ? Theme.AccentSoft : _hover ? Theme.SurfaceHover : Theme.Surface)) g.FillPath(b, p);
                    using (var pen = new Pen(_hover ? Color.FromArgb(200, 198, 248) : Theme.Border)) g.DrawPath(pen, p);
                    text = Theme.TextMain;
                }
                else
                {
                    if (_hover || _down)
                        using (var b = new SolidBrush(_down ? Color.FromArgb(226, 224, 252) : Theme.AccentSoft)) g.FillPath(b, p);
                    text = Theme.Accent;
                }
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
        private bool _highlight;
        public string EmptyText { get; set; }

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
            Text = title; _subtitle = subtitle; _logo = logo;
            BackColor = Theme.Surface;
            Dock = DockStyle.Top;
            Height = 96;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Theme.HighQuality(g);

            var badge = new RectangleF(20, 28, 40, 40);
            Theme.DrawShadow(g, badge, 10, Theme.Accent, 6, 14, 3f);
            using (GraphicsPath p = Theme.RoundRect(badge, 10))
            {
                if (_logo != null)
                {
                    using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, p);
                    using (var pen = new Pen(Theme.AccentSoft)) g.DrawPath(pen, p);
                    RectangleF inner = badge;
                    inner.Inflate(-5, -5);
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

            Theme.DrawText(g, Text, Theme.Brand, Theme.TextMain, new RectangleF(70, 28, Width - 76, 22));
            Theme.DrawText(g, _subtitle, Theme.Small, Theme.TextMuted, new RectangleF(70, 49, Width - 76, 18));
            using (var pen = new Pen(Theme.GridLine))
                g.DrawLine(pen, 20, Height - 1, Width - 20, Height - 1);
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
            Text = title;
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
