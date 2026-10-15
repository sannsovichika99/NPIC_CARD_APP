using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;

namespace NPIC_CARD_APP
{
    /// <summary>Where the student list is loaded from, as chosen on the <see cref="ImportStudentsForm"/>.</summary>
    public enum ImportSource { None, Api, Excel, Csv, SqlServer, ExcelTemplate }

    /// <summary>
    /// "Import Students" page: every way to load the student list in one place (API, Excel, CSV, SQL Server)
    /// plus the empty Excel template. Picking a source closes the page and sets <see cref="Choice"/>;
    /// the main window then runs that import, so loading itself is unchanged.
    /// </summary>
    public class ImportStudentsForm : ModalForm
    {
        /// <summary>The source picked on the page (None when closed without picking).</summary>
        public ImportSource Choice { get; private set; }

        private readonly ImportOptionCard _first;

        /// <param name="count">Students in the current list.</param>
        /// <param name="source">Where the current list came from (file name, API host, SQL server), or empty.</param>
        public ImportStudentsForm(int count, string source)
        {
            Text = "Import Students";
            Subtitle = "Choose where the student list comes from";
            Glyph = Theme.IcoDownload;
            KeyPreview = true;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(760, work.Width), Math.Min(520, work.Height));
            MinimumSize = new Size(Math.Min(600, work.Width), Math.Min(460, work.Height));

            // ---- Current list ----
            var info = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 44, ForeColor = Theme.TextSoft, BackColor = Theme.Surface,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(22, 8, 22, 0), UseMnemonic = false,
                Text = count == 0
                    ? "No students loaded yet. Pick a source below to begin."
                    : string.Format("Current list: {0} student(s){1}. Importing replaces it.",
                        count, string.IsNullOrEmpty(source) ? "" : " from " + source)
            };

            // ---- Source cards (2 x 2) ----
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Surface,
                Padding = new Padding(16, 4, 16, 12)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            _first = Card(ImportSource.Excel, "From Excel…", Theme.IcoList, "Ctrl+E",
                "Load students from an Excel workbook (.xlsx, .xlsm or .xls). Use the Excel Template to get the right columns.");
            grid.Controls.Add(_first, 0, 0);
            grid.Controls.Add(Card(ImportSource.Csv, "From CSV…", Theme.IcoDocument, "Ctrl+O",
                "Load students from a UTF-8 CSV file with the columns StudentId, NameKh, NameEn, Gender, DateOfBirth…"), 1, 0);
            grid.Controls.Add(Card(ImportSource.Api, "From API…", Theme.IcoGlobe, "Ctrl+I",
                "Download the students from the school's web API (JSON or CSV), with an optional access token."), 0, 1);
            grid.Controls.Add(Card(ImportSource.SqlServer, "SQL Server…", Theme.IcoDatabase, "Ctrl+D",
                "Read the students straight from a SQL Server table or query. The app only reads the database."), 1, 1);

            // ---- Footer: template on the left of Close ----
            var footer = ModalUi.Footer();
            Button btnClose = ModalUi.Button("Close", PillKind.Secondary, (s, e) => DialogResult = DialogResult.Cancel);
            Button btnTemplate = ModalUi.Button("Save Excel Template…", PillKind.Secondary, (s, e) => Pick(ImportSource.ExcelTemplate));
            footer.Controls.Add(btnClose);
            footer.Controls.Add(btnTemplate);
            new ToolTip().SetToolTip(btnTemplate,
                "No list yet? Save an empty Excel file with the right columns, fill it in, then use From Excel…");

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(grid);
            Controls.Add(info);
            Controls.Add(footer);
            CancelButton = btnClose;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _first.Focus();
        }

        /// <summary>The main window's import shortcuts also work here.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.E: Pick(ImportSource.Excel); return true;
                case Keys.Control | Keys.O: Pick(ImportSource.Csv); return true;
                case Keys.Control | Keys.I: Pick(ImportSource.Api); return true;
                case Keys.Control | Keys.D: Pick(ImportSource.SqlServer); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private ImportOptionCard Card(ImportSource source, string title, string glyph, string shortcut, string description)
        {
            var card = new ImportOptionCard(title, glyph, shortcut, description) { Dock = DockStyle.Fill, Margin = new Padding(6) };
            card.Click += (s, e) => Pick(source);
            return card;
        }

        private void Pick(ImportSource source)
        {
            Choice = source;
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>Large clickable tile of the Import Students page: icon badge, title, shortcut and a short description.</summary>
    internal sealed class ImportOptionCard : Control
    {
        private static readonly Font DescriptionFont = new Font(Theme.Family, 9f);
        private const int Radius = 12;

        private readonly string _glyph, _shortcut, _description;
        private bool _hover, _down;

        public ImportOptionCard(string title, string glyph, string shortcut, string description)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Text = title;
            _glyph = glyph;
            _shortcut = shortcut;
            _description = description;
            TabStop = true;
            Cursor = Cursors.Hand;
            BackColor = Theme.Surface;
            MinimumSize = new Size(220, 110);
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = title;
            AccessibleDescription = description;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Enter || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.EffectiveBack(Parent));
            Theme.HighQuality(g);

            bool lit = _hover || Focused;
            var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Theme.RoundRect(card, Radius))
            {
                Color fill = _down ? Theme.AccentSoft : _hover ? Theme.SurfaceHover : Theme.SurfaceAlt;
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                using (var pen = new Pen(Focused ? Theme.Accent : lit ? Theme.AccentOutline : Theme.Border, Focused ? 1.6f : 1f))
                    g.DrawPath(pen, p);
            }

            // Icon badge
            var badge = new RectangleF(18, 18, 40, 40);
            using (GraphicsPath p = Theme.RoundRect(badge, 11))
            using (var b = new SolidBrush(Theme.AccentSoft))
                g.FillPath(b, p);
            Theme.DrawIcon(g, _glyph, 18f, Theme.Accent, badge);

            float textX = badge.Right + 14, right = Width - 16;

            // Shortcut chip (top right)
            float chipW = 0;
            if (!string.IsNullOrEmpty(_shortcut))
            {
                chipW = TextRenderer.MeasureText(g, _shortcut, Theme.Small).Width + 10;
                var chip = new RectangleF(right - chipW, 18, chipW, 20);
                using (GraphicsPath p = Theme.RoundRect(chip, 6))
                using (var pen = new Pen(Theme.Border))
                    g.DrawPath(pen, p);
                Theme.DrawText(g, _shortcut, Theme.Small, Theme.TextMuted, chip, StringAlignment.Center);
            }

            Theme.DrawText(g, Text, Theme.CardTitle, lit ? Theme.Accent : Theme.TextMain,
                new RectangleF(textX, 16, Math.Max(10, right - chipW - 8 - textX), 24));

            var desc = Rectangle.Round(new RectangleF(textX, 44, Math.Max(10, right - textX), Math.Max(10, Height - 44 - 14)));
            TextRenderer.DrawText(g, _description, DescriptionFont, desc, Theme.TextSoft,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
        }
    }
}
