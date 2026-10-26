using System;
using System.Drawing;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "Print Back" popup: shows the card back (the same for every student) and asks how many copies to print.
    /// No student has to be selected. The main window does the printing, tray load by tray load; this page only picks
    /// <see cref="Copies"/>.
    /// </summary>
    public class PrintBackForm : ModalForm
    {
        public const int MaxCopies = 500;

        private readonly NumericUpDown _copies;
        private readonly Label _loads;
        private readonly PictureBox _preview;
        private readonly int _perLoad;

        /// <summary>Number of card backs to print (1 … <see cref="MaxCopies"/>).</summary>
        public int Copies { get { return (int)_copies.Value; } }

        /// <param name="settings">Card design and tray settings (the back is rendered from them).</param>
        /// <param name="suggested">Copies shown at first, e.g. the number of selected students.</param>
        /// <param name="perLoad">Cards per tray load when printing 2 or more (1 or 2, see CardPrinter.CardsPerLoad).</param>
        public PrintBackForm(AppSettings settings, int suggested, int perLoad)
        {
            _perLoad = Math.Max(1, Math.Min(2, perLoad));

            Text = "Print Back";
            Subtitle = "The back is the same for every student — no student needs to be selected";
            Glyph = Theme.IcoPrint;
            Resizable = false;
            Size = new Size(520, 470);
            KeyPreview = true;

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22, 14, 22, 0), BackColor = Theme.Surface
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 220f));
            for (int i = 0; i < 3; i++) body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ---- Back preview (as designed, not turned for the tray) ----
            _preview = new PictureBox
            {
                Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.SurfaceAlt,
                Margin = new Padding(0, 0, 0, 12)
            };
            try { _preview.Image = CardRenderer.RenderBack(settings); }
            catch (Exception ex)
            {
                // A broken design must not hide the popup: printing will report the same error.
                _preview.Image = null;
                Subtitle = Lang.F("Preview error: {0}", ex.Message);
            }
            body.Controls.Add(_preview, 0, 0);

            // ---- Copies ----
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4), BackColor = Theme.Surface };
            row.Controls.Add(new Label
            {
                Text = "Copies", AutoSize = true, ForeColor = Theme.TextMain, BackColor = Theme.Surface,
                Margin = new Padding(0, 7, 10, 0)
            });
            _copies = new NumericUpDown
            {
                Minimum = 1, Maximum = MaxCopies, Increment = 1, DecimalPlaces = 0, Width = 80,
                TextAlign = HorizontalAlignment.Right, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 10, 3),
                Value = Math.Max(1, Math.Min(MaxCopies, suggested))
            };
            _copies.ValueChanged += (s, e) => UpdateLoads();
            row.Controls.Add(_copies);
            _loads = new Label
            {
                AutoSize = true, ForeColor = Theme.TextSoft, BackColor = Theme.Surface, Margin = new Padding(0, 7, 0, 0)
            };
            row.Controls.Add(_loads);
            body.Controls.Add(row, 0, 1);

            body.Controls.Add(new Label
            {
                Text = "Uses \"Rotate back 180°\" and the slot positions from Print setup. " +
                       "Blank backs are not added to the print history.",
                AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = Theme.TextSoft, BackColor = Theme.Surface,
                Margin = new Padding(0, 6, 0, 0)
            }, 0, 2);

            // ---- Footer ----
            var footer = ModalUi.Footer();
            Button ok = ModalUi.Button("Print", PillKind.Primary, (s, e) => DialogResult = DialogResult.OK);
            Button cancel = ModalUi.Button("Cancel", PillKind.Secondary, (s, e) => DialogResult = DialogResult.Cancel);
            footer.Controls.Add(ok);
            footer.Controls.Add(cancel);

            Controls.Add(body);
            Controls.Add(footer);
            AcceptButton = ok;
            CancelButton = cancel;

            UpdateLoads();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _copies.Focus();
            _copies.Select(0, _copies.Text.Length);
        }

        /// <summary>Tray loads needed for the copies: 1 card per load for a single copy, else <see cref="_perLoad"/>.</summary>
        public static int LoadsFor(int copies, int perLoad)
        {
            int per = copies < 2 ? 1 : Math.Max(1, perLoad);
            return (copies + per - 1) / per;
        }

        private void UpdateLoads()
        {
            int loads = LoadsFor(Copies, _perLoad);
            _loads.Text = loads == 1 ? Lang.T("1 tray load") : Lang.F("{0} tray loads", loads);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _preview != null && _preview.Image != null)
            {
                Image img = _preview.Image;
                _preview.Image = null;
                img.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
