using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NPIC_CARD_APP
{
    /// <summary>Page picked on the <see cref="SettingsHubForm"/>.</summary>
    public enum SettingsHubChoice { None, DarkMode, ThemeColor, Folders, AllSettings, DriverSetup, SystemInfo, Updates, HelpAbout, License, Language }

    /// <summary>
    /// "Settings &amp; Help" page: Dark Mode, Theme Color…, Language, All Settings…, Folders…, Epson Driver Setup…,
    /// Help &amp; About, System Info, Check for Updates and License… as a grouped list (Appearance · Settings &amp; files ·
    /// Printer · Help &amp; system), each row showing its current state on the right (dark mode switch, colour swatch,
    /// language flag, installed version, licence status). Picking a row closes the page and sets <see cref="Choice"/>; the
    /// main window then runs that command, so each page itself is unchanged. Their keyboard shortcuts keep working here
    /// and in the main window; ↑ ↓ move between the rows.
    /// </summary>
    public class SettingsHubForm : ModalForm
    {
        /// <summary>The page picked (None when closed without picking).</summary>
        public SettingsHubChoice Choice { get; private set; }

        private readonly HubRow _first;
        private readonly Panel _scroll;

        /// <param name="accentHex">Saved primary colour (<see cref="Services.AppSettings.AccentColor"/>), shown on the Theme Color row.</param>
        public SettingsHubForm(string accentHex)
        {
            Text = "Settings & Help";
            Subtitle = "Appearance, language, folders, settings, printer driver, system info, updates and the user guide";
            Glyph = Theme.IcoSettings;
            KeyPreview = true;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(720, work.Width), Math.Min(860, work.Height));
            MinimumSize = new Size(Math.Min(520, work.Width), Math.Min(480, work.Height));

            _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.SurfaceAlt, Padding = new Padding(22, 6, 22, 18) };

            // ---- Appearance ----
            _first = Row(SettingsHubChoice.DarkMode, "Dark Mode", Theme.IcoMoon, "",
                Theme.Dark
                    ? "Switch back to the light colours. The app restarts to apply it; printed cards are not affected."
                    : "Dark colours, easier on the eyes in a dim room. The app restarts to apply it; printed cards are not affected.");
            _first.Toggle = Theme.Dark;

            Color accent;
            if (!AccentPalette.TryParseHex(accentHex, out accent)) accent = Theme.Accent;
            HubRow color = Row(SettingsHubChoice.ThemeColor, "Theme Color…", Theme.IcoColor, "",
                "Primary colour of buttons, tabs and highlights, now " + AccentPalette.Describe(accentHex) +
                ". Pick a preset or any colour, with a live preview.");
            color.Swatch = accent;

            HubRow language = Row(SettingsHubChoice.Language, "Language", Theme.IcoGlobe, "Ctrl+Shift+L",
                Lang.Khmer
                    ? "Show Card Studio in English. The app restarts to apply it; printed cards are not affected."
                    : "Show Card Studio in Khmer · បង្ហាញ Card Studio ជាភាសាខ្មែរ. The app restarts to apply it; " +
                      "printed cards are not affected.");
            language.Flag = Lang.Khmer ? "kh" : "gb";
            language.Status = Lang.Khmer ? "ខ្មែរ" : "English";

            // ---- Settings & files ----
            HubRow all = Row(SettingsHubChoice.AllSettings, "All Settings…", Theme.IcoSettings, "Ctrl+,",
                "Every setting of the app and this PC: printer, tray, files, API, card text, fonts. Export, import or back them up.");
            HubRow folders = Row(SettingsHubChoice.Folders, "Folders…", Theme.IcoFolder, "Ctrl+Shift+F",
                "Every folder the app uses: photos, backups, app data, card images. Open, change or create them.");

            // ---- Printer ----
            HubRow driver = Row(SettingsHubChoice.DriverSetup, "Epson Driver Setup…", Theme.IcoPrint, "Ctrl+Shift+E",
                "Choose the Epson card printer and save its paper source, paper type and quality.");

            // ---- Help & system ----
            HubRow help = Row(SettingsHubChoice.HelpAbout, "Help & About", Theme.IcoQuestion, "F1",
                "What Card Studio does, step-by-step guide, shortcuts and troubleshooting, in English or Khmer · ជាភាសាខ្មែរ.");
            HubRow info = Row(SettingsHubChoice.SystemInfo, "System Info", Theme.IcoInfo, "Ctrl+Shift+I",
                "Version, students loaded, cards printed, RFID cards, data files, printer, fonts and card images of this PC.");
            HubRow updates = Row(SettingsHubChoice.Updates, "Check for Updates", Theme.IcoSync, "Ctrl+U",
                "Version " + Services.UpdateService.CurrentVersionText + " is installed. Look for a newer version and install it; " +
                "settings, print history and card images are kept.");
            updates.Status = "v" + Services.UpdateService.CurrentVersionText;

            Services.LicenseStatus license = Services.LicenseService.Check();
            HubRow licenseRow = Row(SettingsHubChoice.License, "License…", Theme.IcoKey, "",
                Lang.T(license.Describe()) + Lang.T(". Enter a new serial number from your administrator to renew it."));
            licenseRow.Status = !license.IsActivated ? (license.State == Services.LicenseState.Expired ? "Expired" : "Not activated")
                              : license.ExpiresSoon ? "Expires soon" : "Active";
            licenseRow.StatusColor = !license.IsActivated ? Theme.Danger : license.ExpiresSoon ? Theme.Warning : Theme.Success;

            var sections = new List<Control>();
            AddGroup(sections, "APPEARANCE", _first, color, language);
            AddGroup(sections, "SETTINGS & FILES", all, folders);
            AddGroup(sections, "PRINTER", driver);
            AddGroup(sections, "HELP & SYSTEM", help, info, updates, licenseRow);

            // Dock = Top stacks the LAST added control on top, so add bottom-up.
            for (int i = sections.Count - 1; i >= 0; i--)
            {
                sections[i].Dock = DockStyle.Top;
                _scroll.Controls.Add(sections[i]);
            }

            // ---- Footer ----
            var footer = ModalUi.Footer();
            Button btnClose = ModalUi.Button("Close", PillKind.Secondary, (s, e) => DialogResult = DialogResult.Cancel);
            footer.Controls.Add(btnClose);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(_scroll);
            Controls.Add(footer);
            CancelButton = btnClose;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _first.Focus();
        }

        /// <summary>The main window's shortcuts for these pages also work here.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Oemcomma: Pick(SettingsHubChoice.AllSettings); return true;
                case Keys.Control | Keys.Shift | Keys.F: Pick(SettingsHubChoice.Folders); return true;
                case Keys.Control | Keys.Shift | Keys.E: Pick(SettingsHubChoice.DriverSetup); return true;
                case Keys.Control | Keys.Shift | Keys.I: Pick(SettingsHubChoice.SystemInfo); return true;
                case Keys.Control | Keys.U: Pick(SettingsHubChoice.Updates); return true;
                case Keys.F1: Pick(SettingsHubChoice.HelpAbout); return true;
                case Keys.Control | Keys.Shift | Keys.L: Pick(SettingsHubChoice.Language); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private HubRow Row(SettingsHubChoice choice, string title, string glyph, string shortcut, string description)
        {
            var row = new HubRow(title, glyph, shortcut, description);
            row.Click += (s, e) => Pick(choice);
            row.GotFocus += (s, e) =>
            {
                if (row.Heading != null) _scroll.ScrollControlIntoView(row.Heading);   // first row: keep its heading in view
                _scroll.ScrollControlIntoView(row);
            };
            row.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down) return;
                SelectNextControl(row, e.KeyCode == Keys.Down, true, true, false);   // ↑ ↓ walk the rows, no wrap
                e.Handled = true;
            };
            return row;
        }

        /// <summary>Small-caps heading, then the rows joined into one rounded card.</summary>
        private static void AddGroup(List<Control> sections, string heading, params HubRow[] rows)
        {
            var label = new Label
            {
                Text = heading, Font = Theme.SmallCaps, ForeColor = Theme.TextMuted, BackColor = Theme.SurfaceAlt,
                AutoSize = false, Height = 34, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(4, 0, 0, 7),
                UseMnemonic = false
            };
            sections.Add(label);
            rows[0].Heading = label;
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].First = i == 0;
                rows[i].Last = i == rows.Length - 1;
                sections.Add(rows[i]);
            }
        }

        private void Pick(SettingsHubChoice choice)
        {
            Choice = choice;
            DialogResult = DialogResult.OK;
        }

        /// <summary>
        /// One row of a group card: icon tile, title with its shortcut chip, description, and on the right an optional
        /// state (on/off switch, colour swatch, flag, status text) and a chevron. The rows of a group draw one rounded card
        /// together: the first one rounds the top corners, the last one the bottom corners.
        /// </summary>
        private sealed class HubRow : Control
        {
            private const int Radius = 12, PadX = 16, IconSize = 38, MinHeight = 62, PadY = 12, ChevronSpace = 34;

            private readonly string _glyph, _shortcut, _description;
            private readonly Font _titleFont = Theme.UiFont(10f, true);
            private readonly Font _descFont = Theme.UiFont(8.5f);
            private bool _hover, _down;
            private int _measuredWidth = -1;

            public bool First { get; set; }

            /// <summary>Heading of the group (first row only), scrolled into view with it.</summary>
            public Control Heading { get; set; }
            public bool Last { get; set; }

            /// <summary>On/off switch on the right (null = none).</summary>
            public bool? Toggle { get; set; }

            /// <summary>Colour swatch on the right (Color.Empty = none).</summary>
            public Color Swatch { get; set; }

            /// <summary>Flag code for <see cref="FlagArt"/> shown before <see cref="Status"/> (null = none).</summary>
            public string Flag { get; set; }

            /// <summary>Short state on the right ("v1.0.0.7", "Active"); a pill when <see cref="StatusColor"/> is set.</summary>
            public string Status { get; set; }

            public Color StatusColor { get; set; }

            public HubRow(string title, string glyph, string shortcut, string description)
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                         ControlStyles.UserPaint | ControlStyles.Selectable, true);
                Text = Lang.T(title);
                _glyph = glyph;
                _shortcut = shortcut;
                _description = Lang.T(description);
                Swatch = Color.Empty;
                StatusColor = Color.Empty;
                TabStop = true;
                Cursor = Cursors.Hand;
                BackColor = Theme.SurfaceAlt;
                Height = MinHeight;
                AccessibleRole = AccessibleRole.PushButton;
                AccessibleName = Text;
                AccessibleDescription = _description;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { _titleFont.Dispose(); _descFont.Dispose(); }
                base.Dispose(disposing);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override bool IsInputKey(Keys keyData)
            {
                return keyData == Keys.Enter || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
                base.OnKeyDown(e);
            }

            // ---------- Layout: the height follows the wrapped description ----------
            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (Width == _measuredWidth || Width <= 0) return;
                _measuredWidth = Width;
                int h = Math.Max(MinHeight, PadY + TitleHeight() + 3 + DescriptionHeight() + PadY);
                if (h != Height) Height = h;
            }

            private int TextLeft { get { return PadX + IconSize + 14; } }

            private int TextRight { get { return Width - PadX - ChevronSpace - AccessoryWidth() - (AccessoryWidth() > 0 ? 16 : 0); } }

            private int TitleHeight()
            {
                return TextRenderer.MeasureText("Ag", _titleFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
            }

            private int DescriptionHeight()
            {
                return TextRenderer.MeasureText(_description, _descFont, new Size(Math.Max(80, TextRight - TextLeft), int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
            }

            private Size StatusSize()
            {
                if (string.IsNullOrEmpty(Status)) return Size.Empty;
                Size s = TextRenderer.MeasureText(Lang.T(Status), _descFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                return StatusColor.IsEmpty ? s : new Size(s.Width + 32, 24);   // dot + text + padding
            }

            private int AccessoryWidth()
            {
                if (Toggle != null) return 40;
                if (!Swatch.IsEmpty) return 22;
                int w = StatusSize().Width;
                if (Flag != null) w += 20 + (w > 0 ? 8 : 0);
                return w;
            }

            // ---------- Painting ----------
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.SurfaceAlt);
                Theme.HighQuality(g);

                // Part of the group's card: stretch the rounded rectangle past the edges that join another row.
                float top = First ? 0.5f : -Radius * 2, bottom = Last ? Height - 1.5f : Height + Radius * 2;
                var card = new RectangleF(0.5f, top, Width - 1.5f, bottom - top);
                using (GraphicsPath p = Theme.RoundRect(card, Radius))
                {
                    Color fill = _down ? Theme.AccentSoft : _hover ? Theme.SurfaceHover : Theme.Surface;
                    using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                    using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
                }
                if (!First)
                    using (var pen = new Pen(Theme.GridLine)) g.DrawLine(pen, TextLeft, 0, Width - PadX, 0);
                if (Focused && ShowFocusCues)
                {
                    var ring = new RectangleF(3.5f, First ? 3.5f : 2.5f, Width - 7.5f, Height - (First ? 7f : 5.5f) - (Last ? 2f : 0f));
                    using (GraphicsPath p = Theme.RoundRect(ring, Radius - 3))
                    using (var pen = new Pen(Theme.Accent, 1.6f))
                        g.DrawPath(pen, p);
                }

                // Icon tile
                var icon = new RectangleF(PadX, (Height - IconSize) / 2f, IconSize, IconSize);
                using (GraphicsPath p = Theme.RoundRect(icon, 10))
                using (var b = new SolidBrush(Theme.AccentSoft))
                    g.FillPath(b, p);
                Theme.DrawIcon(g, _glyph, 17f, Theme.Accent, icon);

                // Title + shortcut chip, description under it; the block is centred vertically.
                int titleH = TitleHeight(), descH = DescriptionHeight();
                int y = (Height - (titleH + 3 + descH)) / 2;
                int titleW = Math.Min(TextRenderer.MeasureText(Text, _titleFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width,
                                      Math.Max(40, TextRight - TextLeft));
                TextRenderer.DrawText(g, Text, _titleFont, new Rectangle(TextLeft, y, titleW + 2, titleH), Theme.TextMain,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                if (!string.IsNullOrEmpty(_shortcut))
                {
                    Size s = TextRenderer.MeasureText(_shortcut, Theme.Small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    var chip = new Rectangle(TextLeft + titleW + 10, y + (titleH - 18) / 2, s.Width + 12, 18);
                    if (chip.Right <= TextRight)
                    {
                        using (GraphicsPath p = Theme.RoundRect(chip, 5))
                        using (var pen = new Pen(Theme.Border))
                            g.DrawPath(pen, p);
                        TextRenderer.DrawText(g, _shortcut, Theme.Small, chip, Theme.TextMuted,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    }
                }
                TextRenderer.DrawText(g, _description, _descFont,
                    new Rectangle(TextLeft, y + titleH + 3, Math.Max(80, TextRight - TextLeft), descH + 2), Theme.TextSoft,
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

                PaintAccessory(g);
                PaintChevron(g);
            }

            private void PaintAccessory(Graphics g)
            {
                int right = Width - PadX - ChevronSpace + 4, cy = Height / 2;
                if (Toggle != null)
                {
                    var track = new RectangleF(right - 40, cy - 11, 40, 22);
                    using (GraphicsPath p = Theme.RoundRect(track, 11))
                    {
                        using (var b = new SolidBrush(Toggle.Value ? Theme.Accent : Theme.Tile)) g.FillPath(b, p);
                        if (!Toggle.Value) using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
                    }
                    var knob = new RectangleF(Toggle.Value ? track.Right - 19 : track.X + 3, cy - 8, 16, 16);
                    using (var b = new SolidBrush(Toggle.Value ? Color.White : Theme.TextMuted)) g.FillEllipse(b, knob);
                    return;
                }
                if (!Swatch.IsEmpty)
                {
                    var dot = new RectangleF(right - 22, cy - 11, 22, 22);
                    using (var b = new SolidBrush(Swatch)) g.FillEllipse(b, dot);
                    using (var pen = new Pen(Color.FromArgb(Theme.Dark ? 90 : 40, Theme.Dark ? Color.White : Color.Black)))
                        g.DrawEllipse(pen, dot);
                    return;
                }

                Size status = StatusSize();
                int x = right - AccessoryWidth();
                if (Flag != null)
                {
                    FlagArt.Draw(g, Flag, new RectangleF(x, cy - 7, 20, 14));
                    x += 28;
                }
                if (status.IsEmpty) return;
                string text = Lang.T(Status);
                if (StatusColor.IsEmpty)
                {
                    TextRenderer.DrawText(g, text, _descFont, new Rectangle(x, 0, status.Width + 2, Height), Theme.TextSoft,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    return;
                }
                var pill = new Rectangle(x, cy - 12, status.Width, 24);
                using (GraphicsPath p = Theme.RoundRect(pill, 12))
                using (var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 60 : 32, StatusColor)))
                    g.FillPath(b, p);
                using (var dot = new SolidBrush(StatusColor)) g.FillEllipse(dot, pill.X + 10, cy - 3, 6, 6);
                TextRenderer.DrawText(g, text, _descFont, new Rectangle(pill.X + 21, pill.Y, pill.Width - 21, pill.Height), StatusColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }

            private void PaintChevron(Graphics g)
            {
                float x = Width - PadX - 10, cy = Height / 2f;
                Color c = _hover || Focused ? Theme.Accent : Theme.TextMuted;
                using (var pen = new Pen(c, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, new[] { new PointF(x - 3, cy - 5), new PointF(x + 2, cy), new PointF(x - 3, cy + 5) });
            }
        }
    }
}
