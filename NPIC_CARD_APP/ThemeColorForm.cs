using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Theme Color…: pick the primary (accent) colour of the app from preset swatches or any custom colour,
    /// with a live preview of buttons, tabs, navigation and tiles in the current light / dark theme.
    /// Returns the colour in <see cref="SelectedHex"/>; the caller saves it and restarts the app.
    /// </summary>
    internal sealed class ThemeColorForm : ModalForm
    {
        private const int Columns = 6;
        private const int BodyPad = 24;

        private readonly List<ColorSwatch> _swatches = new List<ColorSwatch>();
        private readonly ColorSwatch _custom;
        private readonly ThemePreview _preview;
        private readonly Label _hint;
        private readonly ToolTip _tip = new ToolTip();
        private readonly Size _clientSize;
        private Color _selected;

        public ThemeColorForm(string currentHex)
        {
            Text = "Theme Color";
            Subtitle = "Primary colour of buttons, tabs, selections and highlights";
            Glyph = Theme.IcoColor;
            Resizable = false;
            KeyPreview = true;

            Color current;
            if (!AccentPalette.TryParseHex(currentHex, out current)) current = AccentPalette.DefaultBase;

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            int width = BodyPad * 2 + Columns * ColorSwatch.CellWidth;
            int y = 18;

            body.Controls.Add(SectionLabel("COLOUR", BodyPad, y));
            y += 24;

            // ---- Swatches: presets, then Custom… ----
            int index = 0;
            foreach (AccentPreset preset in AccentPalette.Presets)
            {
                var sw = new ColorSwatch(preset.Name, preset.Base, false);
                AddSwatch(body, sw, index++, y);
                bool isDefault = preset.Base.ToArgb() == AccentPalette.DefaultBase.ToArgb();
                _tip.SetToolTip(sw, preset.Name + "  " + preset.Hex + (isDefault ? "  (default)" : ""));
                Color c = preset.Base;
                sw.Click += (s, e) => SelectColor(c);
            }
            _custom = new ColorSwatch("Custom…", Color.Empty, true);
            AddSwatch(body, _custom, index++, y);
            _tip.SetToolTip(_custom, "Choose any colour");
            _custom.Click += (s, e) => PickCustom();
            int rows = (index + Columns - 1) / Columns;
            y += rows * ColorSwatch.CellHeight + 12;

            // ---- Preview ----
            body.Controls.Add(SectionLabel("PREVIEW", BodyPad, y));
            y += 24;
            _preview = new ThemePreview { Location = new Point(BodyPad, y), Size = new Size(width - 2 * BodyPad, 176) };
            body.Controls.Add(_preview);
            y += _preview.Height + 10;

            _hint = new Label
            {
                AutoSize = false, Location = new Point(BodyPad, y), Size = new Size(width - 2 * BodyPad, 52),
                Font = Theme.Small, ForeColor = Theme.TextSoft, BackColor = Theme.Surface, UseMnemonic = false
            };
            body.Controls.Add(_hint);
            y += _hint.Height + 8;

            // ---- Footer ----
            var footer = ModalUi.Footer();
            var apply = ModalUi.Button("Apply & Restart", PillKind.Primary);
            apply.DialogResult = DialogResult.OK;
            var cancel = ModalUi.Button("Cancel", PillKind.Secondary);
            cancel.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(apply);    // right-to-left flow: first added = rightmost
            footer.Controls.Add(cancel);
            AcceptButton = apply;
            CancelButton = cancel;

            Controls.Add(body);
            Controls.Add(footer);

            _clientSize = new Size(width, HeaderHeight + y + ModalUi.FooterHeight);
            ClientSize = _clientSize;

            SelectColor(current);
        }

        /// <summary>Chosen colour as "#RRGGBB".</summary>
        public string SelectedHex { get { return AccentPalette.ToHex(_selected); } }

        private void AddSwatch(Control parent, ColorSwatch sw, int index, int top)
        {
            sw.Location = new Point(BodyPad + (index % Columns) * ColorSwatch.CellWidth, top + (index / Columns) * ColorSwatch.CellHeight);
            sw.TabIndex = index;
            _swatches.Add(sw);
            parent.Controls.Add(sw);
        }

        private static Label SectionLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text, Font = Theme.SmallCaps, ForeColor = Theme.TextMuted, BackColor = Theme.Surface,
                AutoSize = true, Location = new Point(x, y)
            };
        }

        private void SelectColor(Color c)
        {
            _selected = Color.FromArgb(255, c);
            bool isPreset = AccentPalette.FindPreset(_selected) != null;
            foreach (ColorSwatch sw in _swatches)
                sw.Selected = sw.IsCustom ? !isPreset : sw.Color.ToArgb() == _selected.ToArgb();
            if (!isPreset) _custom.Color = _selected;

            AccentPalette palette = AccentPalette.Create(_selected, Theme.Dark);
            _preview.Palette = palette;

            AccentPreset preset = AccentPalette.FindPreset(_selected);
            string name = preset != null ? preset.Name : "Custom colour";
            string text = name + "  " + AccentPalette.ToHex(_selected) +
                "  ·  white text on buttons " + AccentPalette.Contrast(palette.ButtonA, Color.White).ToString("0.0") + ":1";
            if (palette.ButtonA.ToArgb() != _selected.ToArgb())
                text += "  ·  darkened to " + AccentPalette.ToHex(palette.ButtonA) + " so button text stays readable";
            text += "\nThe app restarts to apply it. A student list opened from a file comes back by itself; " +
                    "students imported from the API or SQL Server must be loaded again. Printed cards are not affected.";
            _hint.Text = text;
        }

        private void PickCustom()
        {
            using (var dlg = new ColorDialog { FullOpen = true, AnyColor = true, Color = _selected })
            {
                // Offer the presets in the dialog's custom-colour boxes (BGR order).
                var custom = new int[16];
                for (int i = 0; i < custom.Length; i++)
                {
                    Color c = i < AccentPalette.Presets.Length ? AccentPalette.Presets[i].Base : Color.White;
                    custom[i] = c.R | (c.G << 8) | (c.B << 16);
                }
                dlg.CustomColors = custom;
                if (dlg.ShowDialog(this) == DialogResult.OK) SelectColor(dlg.Color);
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            ClientSize = _clientSize;   // again now that the real frame is known; before base.OnLoad centres the window
            base.OnLoad(e);
            foreach (ColorSwatch sw in _swatches)
                if (sw.Selected) { ActiveControl = sw; break; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Arrow keys move between swatches and choose the colour, like a radio group
            // (landing on Custom… only focuses it: Space opens the colour dialog).
            int at = _swatches.FindIndex(s => s.Focused);
            if (at >= 0)
            {
                int next = -1;
                switch (keyData)
                {
                    case Keys.Left: next = at - 1; break;
                    case Keys.Right: next = at + 1; break;
                    case Keys.Up: next = at - Columns; break;
                    case Keys.Down: next = at + Columns; break;
                }
                if (next >= 0 && next < _swatches.Count)
                {
                    ColorSwatch target = _swatches[next];
                    target.Focus();
                    if (!target.IsCustom) SelectColor(target.Color);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tip.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Round colour swatch with its name underneath; the selected one gets a ring and a check mark.</summary>
    internal sealed class ColorSwatch : Control
    {
        public const int CellWidth = 84, CellHeight = 76;
        private const int Dot = 40;

        private bool _hover, _selected;
        private Color _color;
        public bool IsCustom { get; private set; }

        public ColorSwatch(string name, Color color, bool isCustom)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Text = name; _color = color; IsCustom = isCustom;
            Size = new Size(CellWidth, CellHeight);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleName = name;
            AccessibleRole = AccessibleRole.RadioButton;
        }

        public Color Color { get { return _color; } set { _color = value; Invalidate(); } }

        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                AccessibleDescription = value ? "Selected" : null;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Theme.HighQuality(g);

            var dot = new RectangleF((Width - Dot) / 2f, 8, Dot, Dot);
            RectangleF ring = dot;
            ring.Inflate(4.5f, 4.5f);

            if (_selected)
                using (var pen = new Pen(Theme.TextMain, 2f)) g.DrawEllipse(pen, ring);
            else if (_hover || (Focused && ShowFocusCues))
                using (var pen = new Pen(Theme.Border, 2f)) g.DrawEllipse(pen, ring);
            if (Focused && ShowFocusCues && _selected)
            {
                RectangleF f = ring;
                f.Inflate(3, 3);
                using (var pen = new Pen(Theme.TextMuted) { DashStyle = DashStyle.Dot }) g.DrawEllipse(pen, f);
            }

            bool empty = IsCustom && (_color.IsEmpty || !_selected);
            if (empty)
            {
                // Custom… without a custom colour: rainbow ring + plus sign.
                using (var b = new SolidBrush(Theme.Tile)) g.FillEllipse(b, dot);
                Color[] hues = { Color.FromArgb(239, 68, 68), Color.FromArgb(245, 158, 11), Color.FromArgb(34, 197, 94),
                                 Color.FromArgb(6, 182, 212), Color.FromArgb(59, 130, 246), Color.FromArgb(168, 85, 247) };
                for (int i = 0; i < hues.Length; i++)
                    using (var pen = new Pen(hues[i], 5f)) g.DrawArc(pen, dot.X + 2.5f, dot.Y + 2.5f, Dot - 5, Dot - 5, i * 60 - 90, 61);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Theme.DrawText(g, "+", Theme.CardTitle, Theme.TextMain, dot, StringAlignment.Center);
            }
            else
            {
                using (var b = new SolidBrush(_color)) g.FillEllipse(b, dot);
                // Hairline so very light custom colours still show their edge.
                using (var pen = new Pen(Color.FromArgb(40, Theme.Dark ? Color.White : Color.Black))) g.DrawEllipse(pen, dot);
                if (_selected)
                {
                    Color check = AccentPalette.Contrast(_color, Color.White) >= 2.5 ? Color.White : Color.FromArgb(30, 30, 40);
                    Theme.DrawIcon(g, Theme.IcoCheck, 16f, check, dot);
                }
            }

            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Theme.DrawText(g, Text, _selected ? Theme.NavBold : Theme.Small, _selected ? Theme.TextMain : Theme.TextSoft,
                new RectangleF(0, dot.Bottom + 6, Width, 20), StringAlignment.Center);
        }
    }

    /// <summary>Miniature of the main window drawn with a candidate palette: sidebar, stat tile, tabs, buttons.</summary>
    internal sealed class ThemePreview : Control
    {
        private static readonly Font ValueFont = new Font("Segoe UI Semibold", 15f);
        private AccentPalette _palette = Theme.Palette;

        public ThemePreview()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
        }

        public AccentPalette Palette { get { return _palette; } set { _palette = value ?? Theme.Palette; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Theme.HighQuality(g);
            AccentPalette p = _palette;

            var frame = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath outline = Theme.RoundRect(frame, 12))
            {
                using (var b = new SolidBrush(Theme.AppBack)) g.FillPath(b, outline);
                Region oldClip = g.Clip;
                g.SetClip(outline, CombineMode.Intersect);

                // Header strip
                using (var strip = new LinearGradientBrush(new RectangleF(frame.X, frame.Y, frame.Width, 4), p.GradientA, p.GradientB, 0f))
                    g.FillRectangle(strip, frame.X, frame.Y, frame.Width, 4);

                // Sidebar with an active and two idle items
                var side = new RectangleF(frame.X, frame.Y + 4, 150, frame.Height - 4);
                using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, side);
                using (var pen = new Pen(Theme.GridLine)) g.DrawLine(pen, side.Right, side.Y, side.Right, side.Bottom);
                float ny = side.Y + 14;
                NavRow(g, p, side, ny, Theme.IcoHome, "Dashboard", true);
                NavRow(g, p, side, ny + 34, Theme.IcoPhoto, "Gallery", false);
                NavRow(g, p, side, ny + 68, Theme.IcoHistory, "Print History", false);
                NavRow(g, p, side, ny + 102, Theme.IcoPrint, "Print Queue", false);

                // Content: stat tile on the left, card with tabs and buttons on the right
                float x0 = side.Right + 14, top = frame.Y + 18;
                var tile = new RectangleF(x0, top, 128, 68);
                using (GraphicsPath tp = Theme.RoundRect(tile, 10))
                using (var b = new LinearGradientBrush(tile, p.GradientA, p.GradientB, 20f))
                    g.FillPath(b, tp);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Theme.DrawText(g, "Total students", Theme.Small, Color.FromArgb(225, Color.White), new RectangleF(tile.X + 12, tile.Y + 8, tile.Width - 16, 18));
                Theme.DrawText(g, "248", ValueFont, Color.White, new RectangleF(tile.X + 10, tile.Y + 26, tile.Width - 16, 34));

                var card = new RectangleF(tile.Right + 12, top, frame.Right - tile.Right - 26, frame.Bottom - top - 14);
                using (GraphicsPath cp = Theme.RoundRect(card, 10))
                {
                    using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, cp);
                    using (var pen = new Pen(Theme.GridLine)) g.DrawPath(pen, cp);
                }

                // Card header bullet + title
                float cy = card.Y + 20;
                using (var soft = new SolidBrush(p.AccentSoft)) g.FillEllipse(soft, card.X + 12, cy - 8, 16, 16);
                using (var pen = new Pen(p.Accent, 2.2f)) g.DrawEllipse(pen, card.X + 16, cy - 4, 8, 8);
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                Theme.DrawText(g, "Card preview", Theme.NavBold, Theme.TextMain, new RectangleF(card.X + 34, cy - 10, 120, 20));

                // Pill tabs
                var front = new RectangleF(card.X + 12, card.Y + 40, 64, 26);
                using (GraphicsPath fp = Theme.RoundRect(front, 13))
                using (var b = new SolidBrush(p.AccentSoft))
                    g.FillPath(b, fp);
                Theme.DrawText(g, "Front", Theme.NavBold, p.Accent, front, StringAlignment.Center);
                Theme.DrawText(g, "Back", Theme.Nav, Theme.TextMuted, new RectangleF(front.Right + 4, front.Y, 56, front.Height), StringAlignment.Center);

                // Buttons: primary, secondary, ghost
                float by = card.Bottom - 46;
                var primary = new RectangleF(card.X + 12, by, 104, 32);
                using (GraphicsPath bp = Theme.RoundRect(primary, 9))
                using (var b = new LinearGradientBrush(primary, p.ButtonA, p.ButtonB, 0f))
                    g.FillPath(b, bp);
                TextRenderer.DrawText(g, "Print Selected", Theme.Button, Rectangle.Round(primary), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                var secondary = new RectangleF(primary.Right + 8, by, 72, 32);
                using (GraphicsPath sp = Theme.RoundRect(secondary, 9))
                {
                    using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, sp);
                    using (var pen = new Pen(p.AccentOutline)) g.DrawPath(pen, sp);
                }
                TextRenderer.DrawText(g, "Preview", Theme.Button, Rectangle.Round(secondary), Theme.TextMain,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                var ghost = new RectangleF(secondary.Right + 8, by, 56, 32);
                if (ghost.Right <= card.Right - 8)
                {
                    using (GraphicsPath gp = Theme.RoundRect(ghost, 9))
                    using (var b = new SolidBrush(p.AccentSoft))
                        g.FillPath(b, gp);
                    TextRenderer.DrawText(g, "Grid", Theme.Button, Rectangle.Round(ghost), p.Accent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }

                g.Clip = oldClip;
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, outline);
            }
        }

        private static void NavRow(Graphics g, AccentPalette p, RectangleF side, float y, string glyph, string text, bool active)
        {
            if (active)
            {
                var band = new RectangleF(side.X, y, side.Width, 30);
                using (var brush = new LinearGradientBrush(band, p.AccentSoft, Theme.Surface, 0f))
                    g.FillRectangle(brush, band);
                using (var bar = new SolidBrush(p.Accent))
                    g.FillRectangle(bar, side.X, y, 3, 30);
            }
            Theme.DrawIcon(g, glyph, 14f, active ? p.Accent : Theme.TextMuted, new RectangleF(side.X + 12, y, 26, 30));
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Theme.DrawText(g, text, active ? Theme.NavBold : Theme.Nav, active ? p.Accent : Theme.TextSoft,
                new RectangleF(side.X + 42, y, side.Width - 46, 30));
        }
    }
}
