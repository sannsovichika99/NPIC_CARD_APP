using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Guided "Find Position": print the position card, read one square on each printed card,
    /// and the slot X / Y are worked out (square − distance from the card edge) and applied.
    /// OK writes the measured slots into the settings; Cancel leaves them unchanged.
    /// </summary>
    public class FindPositionForm : ModalForm
    {
        // On-screen example: a card at X 23, Y 36 → square X 30 / Y 40, 7 mm from the left edge, 4 mm from the top.
        private const float ExampleX = 23f, ExampleY = 36f;
        private const int ExampleSquareX = 30, ExampleSquareY = 40;
        private static readonly Color Accent = Color.FromArgb(0, 140, 60);       // the square to read
        private static readonly Color Measure = Color.FromArgb(230, 105, 0);     // distances

        private readonly AppSettings _settings;
        private readonly Func<IWin32Window, string> _printCard;
        private readonly SlotReading[] _slots = new SlotReading[2];

        private PictureBox _picExample;
        private Label _lblPrinted;
        private CheckBox _chkSlot2;

        /// <param name="printCard">Prints the position card; returns what Windows used, or null when nothing was sent.</param>
        public FindPositionForm(AppSettings settings, Func<IWin32Window, string> printCard)
        {
            _settings = settings;
            _printCard = printCard;

            Text = "Find Position";
            Subtitle = "Print a position card, read it, and the app works out the slot";
            Glyph = Theme.IcoLocation;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1060, 700);
            MinimumSize = new Size(900, 640);
            ShowInTaskbar = false;
            MinimizeBox = false;
            Font = new Font(CardFonts.UI, 9f);

            BuildUi();
            _slots[0].Load(settings.Slot1X, settings.Slot1Y);
            _slots[1].Load(settings.Slot2X, settings.Slot2Y);
            _chkSlot2.Checked = settings.SlotMode != SlotMode.One;
            UpdateResults();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_picExample.Image != null) _picExample.Image.Dispose();
            base.OnFormClosed(e);
        }

        // ===================== UI =====================
        private void BuildUi()
        {
            // ---- Right: steps ----
            var side = new Panel { Dock = DockStyle.Right, Width = 440, Padding = new Padding(8, 8, 10, 8) };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true
            };

            var print = new GroupBox { Text = "Step 1 — Print the position card", Width = 410, Height = 168 };
            var printText = new Label
            {
                Text = "Put a BLANK card in slot 1 and, if you use it, slot 2. Click Print, then insert the " +
                       "tray when the printer light turns on. Every card comes out covered in labelled squares.",
                Location = new Point(10, 22), Size = new Size(390, 50)
            };
            var btnPrint = new PillButton(PillKind.Secondary) { Text = "Print Position Card", Location = new Point(10, 74), Size = new Size(170, 32) };
            btnPrint.Click += (s, e) => PrintCard();
            _lblPrinted = new Label { Location = new Point(10, 110), Size = new Size(390, 52), ForeColor = Theme.TextSoft };
            print.Controls.AddRange(new Control[] { printText, btnPrint, _lblPrinted });
            flow.Controls.Add(print);

            _slots[0] = new SlotReading(1, UpdateResults);
            _slots[1] = new SlotReading(2, UpdateResults);
            flow.Controls.Add(_slots[0].Group);

            _chkSlot2 = new CheckBox { Text = "I also printed a card in slot 2", AutoSize = true, Margin = new Padding(6, 6, 3, 0) };
            _chkSlot2.CheckedChanged += (s, e) => UpdateResults();
            flow.Controls.Add(_chkSlot2);
            flow.Controls.Add(_slots[1].Group);
            side.Controls.Add(flow);

            var buttons = ModalUi.Footer();
            Button cancel = ModalUi.Button("Cancel", PillKind.Secondary);
            cancel.DialogResult = DialogResult.Cancel;
            Button apply = ModalUi.Button("Apply to Slots", PillKind.Primary);
            apply.Click += (s, e) => ApplyAndClose();
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(apply);
            CancelButton = cancel;

            // ---- Left: how to read the card ----
            var help = new GroupBox { Text = "How to read the printed card", Dock = DockStyle.Fill, Padding = new Padding(8) };
            _picExample = new PictureBox
            {
                Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, Image = RenderExample(_settings.SlotWidthMm, _settings.SlotHeightMm)
            };
            var steps = new Label
            {
                Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(4, 8, 4, 0),
                Text =
                    "1.  Turn the card so the labels read upright (red X on top, blue Y below).\n" +
                    "2.  Choose a square that is FULLY on the card, near its top-left corner (green), and note its " +
                    "X and Y labels.\n" +
                    "3.  Along the line above the square, count the 1 mm ticks from the card's LEFT edge to the square " +
                    "(the long tick = 5 mm). Along the line left of the square, count from the TOP edge.\n" +
                    "4.  Type the labels and the two distances on the right — the app works out the slot:\n" +
                    "      Slot X = 30 − 7 = 23 mm      Slot Y = 40 − 4 = 36 mm"
            };
            help.Controls.Add(_picExample);
            help.Controls.Add(steps);

            Controls.Add(help);
            Controls.Add(side);
            Controls.Add(buttons);
        }

        private void PrintCard()
        {
            string info = _printCard(this);
            if (info == null) return;
            _lblPrinted.ForeColor = Theme.Success;
            _lblPrinted.Text = "Sent — " + info.Replace("\n", "  |  ") + "\n" +
                               "Card blank? 'Driver Setup...' → Source = Disc/ID Card Tray, Paper Type = PVC ID Card.";
        }

        private void UpdateResults()
        {
            if (_slots[1] == null || _chkSlot2 == null) return;   // still building the UI
            _slots[1].Group.Enabled = _chkSlot2.Checked;
            _slots[0].ShowResult();
            _slots[1].ShowResult();
        }

        private void ApplyAndClose()
        {
            _settings.Slot1X = _slots[0].SlotX;
            _settings.Slot1Y = _slots[0].SlotY;
            if (_chkSlot2.Checked)
            {
                _settings.Slot2X = _slots[1].SlotX;
                _settings.Slot2Y = _slots[1].SlotY;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        // ===================== One slot's reading =====================
        private class SlotReading
        {
            private static readonly Font RegularFont = new Font(CardFonts.UI, 9f, FontStyle.Regular);
            private static readonly Font BoldFont = new Font(CardFonts.UI, 9f, FontStyle.Bold);

            private readonly NumericUpDown _squareX, _squareY, _left, _top;
            private readonly Label _result;
            private float _currentX, _currentY;

            public readonly GroupBox Group;

            public SlotReading(int slot, Action changed)
            {
                Group = new GroupBox { Text = "Step 2 — Card in slot " + slot, Width = 410, Height = 176 };

                _squareX = MakeNumber(-50, 330, 10, 0);
                _squareY = MakeNumber(-50, 330, 10, 0);
                _left = MakeNumber(0, 85, 0.5M, 1);
                _top = MakeNumber(0, 54, 0.5M, 1);
                foreach (NumericUpDown n in new[] { _squareX, _squareY, _left, _top })
                    n.ValueChanged += (s, e) => changed();

                var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4, Padding = new Padding(4, 2, 4, 0) };
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

                grid.Controls.Add(MakeLabel("Square label   X", PositionGrid.XColor), 0, 0);
                grid.Controls.Add(_squareX, 1, 0);
                grid.Controls.Add(MakeLabel("Y", PositionGrid.YColor), 2, 0);
                grid.Controls.Add(_squareY, 3, 0);

                grid.Controls.Add(MakeLabel("mm from card's LEFT edge", Measure), 0, 1);
                grid.Controls.Add(_left, 1, 1);
                grid.Controls.Add(MakeLabel("mm from card's TOP edge", Measure), 0, 2);
                grid.Controls.Add(_top, 1, 2);

                _result = new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 0) };
                grid.Controls.Add(_result, 0, 3);
                grid.SetColumnSpan(_result, 4);

                Group.Controls.Add(grid);
            }

            public float SlotX { get { return (float)(_squareX.Value - _left.Value); } }
            public float SlotY { get { return (float)(_squareY.Value - _top.Value); } }

            /// <summary>Pre-fills the reading the card should show if the saved position is already right.</summary>
            public void Load(float x, float y)
            {
                _currentX = x;
                _currentY = y;
                int sx = PositionGrid.Ceil10(x), sy = PositionGrid.Ceil10(y);
                _squareX.Value = Clamp(_squareX, sx);
                _squareY.Value = Clamp(_squareY, sy);
                _left.Value = Clamp(_left, (decimal)Math.Round(sx - x, 1));
                _top.Value = Clamp(_top, (decimal)Math.Round(sy - y, 1));
            }

            public void ShowResult()
            {
                bool changed = Math.Abs(SlotX - _currentX) >= 0.05f || Math.Abs(SlotY - _currentY) >= 0.05f;
                _result.Text = string.Format(CultureInfo.InvariantCulture,
                    "→  X = {0:0} − {1:0.0} = {2:0.0} mm      Y = {3:0} − {4:0.0} = {5:0.0} mm\n" +
                    "     {6}",
                    _squareX.Value, _left.Value, SlotX, _squareY.Value, _top.Value, SlotY,
                    changed
                        ? string.Format(CultureInfo.InvariantCulture, "now X {0:0.0}, Y {1:0.0} mm — will change", _currentX, _currentY)
                        : "same as the saved position");
                _result.ForeColor = changed ? Accent : Theme.TextSoft;
                _result.Font = changed ? BoldFont : RegularFont;
            }

            private static NumericUpDown MakeNumber(decimal min, decimal max, decimal step, int decimals)
            {
                return new NumericUpDown
                {
                    Minimum = min, Maximum = max, Increment = step, DecimalPlaces = decimals,
                    Width = 70, Margin = new Padding(3, 4, 10, 3), TextAlign = HorizontalAlignment.Right
                };
            }

            private static Label MakeLabel(string text, Color color)
            {
                return new Label { Text = text, AutoSize = true, ForeColor = color, Margin = new Padding(3, 8, 3, 0) };
            }

            private static decimal Clamp(NumericUpDown box, decimal v)
            {
                return Math.Max(box.Minimum, Math.Min(box.Maximum, v));
            }
        }

        // ===================== Example picture =====================
        /// <summary>A printed card at X 23 / Y 36 on the grid, with the square to read and the two distances.</summary>
        private static Bitmap RenderExample(float cardW, float cardH)
        {
            const float scale = 7f;   // px per mm
            var card = new RectangleF(ExampleX, ExampleY, cardW, cardH);
            var view = new RectangleF(ExampleX - 13f, ExampleY - 11f, cardW + 21f, cardH + 17f);

            var bmp = new Bitmap((int)(view.Width * scale), (int)(view.Height * scale));
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(scale, scale);
                g.TranslateTransform(-view.X, -view.Y);

                PositionGrid.Draw(g, view);

                // The tray only prints on the card: fade everything else
                using (var outside = new Region(view))
                using (var fade = new SolidBrush(Color.FromArgb(215, 250, 250, 250)))
                {
                    outside.Exclude(card);
                    g.FillRegion(fade, outside);
                }
                using (GraphicsPath edge = RoundedRect(card, 3.2f))
                using (var pen = new Pen(Color.Black, 0.5f))
                    g.DrawPath(pen, edge);

                // The square to read
                using (var pen = new Pen(Accent, 0.7f))
                    g.DrawRectangle(pen, ExampleSquareX, ExampleSquareY, PositionGrid.Cell, PositionGrid.Cell);

                // Distances from the card edges to the square
                DrawDimension(g, new PointF(ExampleX, ExampleY - 5f), new PointF(ExampleSquareX, ExampleY - 5f),
                    ExampleY, (ExampleSquareX - ExampleX) + " mm", true);
                DrawDimension(g, new PointF(ExampleX - 5f, ExampleY), new PointF(ExampleX - 5f, ExampleSquareY),
                    ExampleX, (ExampleSquareY - ExampleY) + " mm", false);

                // Card corner = what we are looking for
                using (var brush = new SolidBrush(Measure))
                using (var pen = new Pen(Measure, 0.2f))
                using (var font = new Font(CardFonts.UI, 2.4f, FontStyle.Bold, GraphicsUnit.World))
                {
                    g.FillEllipse(brush, ExampleX - 0.9f, ExampleY - 0.9f, 1.8f, 1.8f);
                    g.DrawString("Slot X, Y", font, brush, ExampleX - 12.5f, ExampleY - 10.5f);
                    g.DrawLine(pen, ExampleX - 3.5f, ExampleY - 7f, ExampleX - 0.7f, ExampleY - 0.7f);
                }
            }
            return bmp;
        }

        /// <summary>Dimension arrow from a to b, with extension lines back to the card edge.</summary>
        private static void DrawDimension(Graphics g, PointF a, PointF b, float edge, string text, bool horizontal)
        {
            using (var pen = new Pen(Measure, 0.35f))
            using (var ext = new Pen(Measure, 0.2f) { DashStyle = DashStyle.Dash })
            using (var cap = new AdjustableArrowCap(4f, 4f))
            using (var brush = new SolidBrush(Measure))
            using (var font = new Font(CardFonts.UI, 2.6f, FontStyle.Bold, GraphicsUnit.World))
            {
                pen.CustomStartCap = cap;
                pen.CustomEndCap = cap;
                g.DrawLine(pen, a, b);

                SizeF size = g.MeasureString(text, font);
                if (horizontal)
                {
                    g.DrawLine(ext, a.X, a.Y - 1.5f, a.X, edge);
                    g.DrawLine(ext, b.X, b.Y - 1.5f, b.X, edge + 4f);
                    g.DrawString(text, font, brush, (a.X + b.X - size.Width) / 2f, a.Y - size.Height - 0.3f);
                }
                else
                {
                    g.DrawLine(ext, a.X - 1.5f, a.Y, edge, a.Y);
                    g.DrawLine(ext, b.X - 1.5f, b.Y, edge + 7f, b.Y);
                    g.DrawString(text, font, brush, a.X - size.Width - 0.6f, (a.Y + b.Y - size.Height) / 2f);
                }
            }
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
