using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Customize the position of tray slot 1 and slot 2 on the page, drawn as the black PVC ID-card tray.
    /// Drag a card, type X/Y in mm, or nudge with the arrow keys (Shift = 1 mm, otherwise 0.1 mm).
    /// OK writes the positions (and slot mode) back to the settings; Cancel leaves them unchanged.
    /// </summary>
    public class SlotLayoutForm : Form
    {
        private const float Snap = 0.1f;

        private readonly AppSettings _settings;
        private readonly Student _sample;
        private readonly SizeF _page;
        private readonly bool _pageKnown;

        private readonly float[] _x = new float[2];
        private readonly float[] _y = new float[2];

        private Bitmap _front, _back;
        private int _selected;          // 0 = slot 1, 1 = slot 2
        private int _hover = -1;
        private int _dragging = -1;
        private PointF _dragOffsetMm;
        private bool _updating;

        private Panel _canvas;
        private NumericUpDown[] _numX = new NumericUpDown[2], _numY = new NumericUpDown[2];
        private RadioButton _rdoTray, _rdoFront, _rdoBack;
        private ComboBox _cboMode;
        private Label _lblStatus;

        public SlotLayoutForm(AppSettings settings, Student sample, SizeF? pageMm)
        {
            _settings = settings;
            _sample = sample;
            _pageKnown = pageMm.HasValue;
            _page = pageMm ?? new SizeF(210f, 297f);   // A4 when the printer page is unknown

            _x[0] = settings.Slot1X; _y[0] = settings.Slot1Y;
            _x[1] = settings.Slot2X; _y[1] = settings.Slot2Y;

            Text = "Slot Position";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1000, 720);
            MinimumSize = new Size(820, 560);
            ShowInTaskbar = false;
            KeyPreview = true;
            Font = new Font("Segoe UI", 9f);

            BuildUi();
            RenderCards();
            SyncControls();
        }

        // ===================== UI =====================
        private void BuildUi()
        {
            var side = new Panel { Dock = DockStyle.Right, Width = 270, Padding = new Padding(10) };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true
            };

            for (int i = 0; i < 2; i++)
            {
                int slot = i;
                var group = new GroupBox { Text = "Slot " + (i + 1), Width = 245, Height = 118 };
                _numX[i] = MakeNumber(); _numY[i] = MakeNumber();
                _numX[i].ValueChanged += (s, e) => OnNumberChanged(slot);
                _numY[i].ValueChanged += (s, e) => OnNumberChanged(slot);
                _numX[i].Enter += (s, e) => Select(slot);
                _numY[i].Enter += (s, e) => Select(slot);

                group.Controls.Add(new Label { Text = "X (mm):", AutoSize = true, Location = new Point(10, 27) });
                _numX[i].Location = new Point(70, 23);
                group.Controls.Add(_numX[i]);
                group.Controls.Add(new Label { Text = "Y (mm):", AutoSize = true, Location = new Point(10, 57) });
                _numY[i].Location = new Point(70, 53);
                group.Controls.Add(_numY[i]);

                var select = new Button { Text = "Select", Location = new Point(160, 22), Width = 70, Height = 26 };
                select.Click += (s, e) => Select(slot);
                group.Controls.Add(select);
                var reset = new Button { Text = "Reset", Location = new Point(160, 52), Width = 70, Height = 26 };
                reset.Click += (s, e) => ResetSlot(slot);
                group.Controls.Add(reset);

                var hint = new Label
                {
                    Text = i == 0 ? "Left slot on the tray" : "Right slot on the tray",
                    AutoSize = true, ForeColor = Color.DimGray, Location = new Point(10, 88)
                };
                group.Controls.Add(hint);
                flow.Controls.Add(group);
            }

            // Arrows move the card the way it looks on the tray view (page X = up, page Y = right).
            var nudge = new GroupBox { Text = "Move selected slot", Width = 245, Height = 128 };
            nudge.Controls.Add(MakeNudge("▲", 90, 22, 1f, 0f));
            nudge.Controls.Add(MakeNudge("◀", 50, 56, 0f, -1f));
            nudge.Controls.Add(MakeNudge("▶", 130, 56, 0f, 1f));
            nudge.Controls.Add(MakeNudge("▼", 90, 90, -1f, 0f));
            nudge.Controls.Add(new Label
            {
                Text = "Step 0.1 mm\n(Shift = 1 mm)", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(170, 30)
            });
            flow.Controls.Add(nudge);

            var options = new GroupBox { Text = "Preview", Width = 245, Height = 118 };
            _rdoTray = new RadioButton { Text = "Tray", AutoSize = true, Checked = true, Location = new Point(12, 24) };
            _rdoFront = new RadioButton { Text = "Front", AutoSize = true, Location = new Point(80, 24) };
            _rdoBack = new RadioButton { Text = "Back", AutoSize = true, Location = new Point(150, 24) };
            EventHandler repaint = (s, e) => _canvas.Invalidate();
            _rdoTray.CheckedChanged += repaint;
            _rdoFront.CheckedChanged += repaint;
            _rdoBack.CheckedChanged += repaint;
            options.Controls.Add(_rdoTray);
            options.Controls.Add(_rdoFront);
            options.Controls.Add(_rdoBack);
            options.Controls.Add(new Label { Text = "Slots per load:", AutoSize = true, Location = new Point(12, 58) });
            _cboMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Location = new Point(110, 54) };
            _cboMode.Items.AddRange(new object[] { "Auto", "1", "2" });   // SlotMode order: Auto, One, Two
            _cboMode.SelectedIndex = (int)_settings.SlotMode;
            _cboMode.SelectedIndexChanged += (s, e) => { UpdateStatus(); _canvas.Invalidate(); };
            options.Controls.Add(_cboMode);
            options.Controls.Add(new Label
            {
                Text = _sample == null ? "Sample card (no student selected)" : "Sample: " + _sample.StudentId,
                AutoSize = true, ForeColor = Color.DimGray, Location = new Point(12, 88)
            });
            flow.Controls.Add(options);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "OK", Width = 80, Height = 30, DialogResult = DialogResult.OK };
            ok.Click += (s, e) => Apply();
            var cancel = new Button { Text = "Cancel", Width = 80, Height = 30, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;

            side.Controls.Add(flow);
            side.Controls.Add(buttons);

            _lblStatus = new Label
            {
                Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(10, 6, 10, 0),
                BackColor = Color.FromArgb(246, 246, 247)
            };

            _canvas = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = CanvasBack };
            _canvas.Paint += CanvasPaint;
            _canvas.MouseDown += CanvasMouseDown;
            _canvas.MouseMove += CanvasMouseMove;
            _canvas.MouseUp += (s, e) => { _dragging = -1; };
            _canvas.MouseLeave += (s, e) => SetHover(-1);
            _canvas.Resize += (s, e) => _canvas.Invalidate();

            Controls.Add(_canvas);
            Controls.Add(_lblStatus);
            Controls.Add(side);
        }

        private Button MakeNudge(string text, int x, int y, float dx, float dy)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(34, 30) };
            b.Click += (s, e) =>
            {
                float step = (ModifierKeys & Keys.Shift) != 0 ? 1f : Snap;
                Nudge(dx * step, dy * step);
            };
            return b;
        }

        private static NumericUpDown MakeNumber()
        {
            return new NumericUpDown
            {
                DecimalPlaces = 1, Increment = 0.1M, Minimum = -50M, Maximum = 300M, Width = 80
            };
        }

        // ===================== Data =====================
        private void RenderCards()
        {
            try
            {
                Student s = _sample ?? new Student
                {
                    StudentId = "000000", NameKh = "គោត្តនាម នាម", NameEn = "SAMPLE NAME", Gender = "M",
                    DateOfBirth = "01/01/2005", Faculty = "Faculty", Level = "Level", Generation = "1", ValidUntil = "01/01/2030"
                };
                _front = CardRenderer.RenderFront(s, _settings);
                _back = CardRenderer.RenderBack(_settings);
                if (_settings.RotateBack180) _back.RotateFlip(RotateFlipType.Rotate180FlipNone);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Preview error: " + ex.Message;
            }
        }

        private void SyncControls()
        {
            _updating = true;
            for (int i = 0; i < 2; i++)
            {
                _numX[i].Value = Clamp(_numX[i], _x[i]);
                _numY[i].Value = Clamp(_numY[i], _y[i]);
            }
            _updating = false;
            UpdateStatus();
            _canvas.Invalidate();
        }

        private static decimal Clamp(NumericUpDown box, float value)
        {
            decimal v = Math.Round((decimal)value, 1);
            return v < box.Minimum ? box.Minimum : (v > box.Maximum ? box.Maximum : v);
        }

        private void OnNumberChanged(int slot)
        {
            if (_updating) return;
            _x[slot] = (float)_numX[slot].Value;
            _y[slot] = (float)_numY[slot].Value;
            _selected = slot;
            UpdateStatus();
            _canvas.Invalidate();
        }

        private void Select(int slot)
        {
            if (_selected == slot) return;
            _selected = slot;
            _canvas.Invalidate();
        }

        private void ResetSlot(int slot)
        {
            _selected = slot;
            _x[slot] = 0f;
            _y[slot] = slot == 0 ? 0f : 60f;
            SyncControls();
        }

        private void Nudge(float dxMm, float dyMm)
        {
            _x[_selected] = Round(_x[_selected] + dxMm);
            _y[_selected] = Round(_y[_selected] + dyMm);
            SyncControls();
        }

        private static float Round(float v) { return (float)Math.Round(v / Snap) * Snap; }

        private void Apply()
        {
            _settings.Slot1X = Round(_x[0]); _settings.Slot1Y = Round(_y[0]);
            _settings.Slot2X = Round(_x[1]); _settings.Slot2Y = Round(_y[1]);
            if (_cboMode.SelectedIndex >= 0) _settings.SlotMode = (SlotMode)_cboMode.SelectedIndex;
        }

        // ===================== Status / validation =====================
        private bool Overlaps()
        {
            return Math.Abs(_x[1] - _x[0]) < CardRenderer.CardWidthMm - 1f &&
                   Math.Abs(_y[1] - _y[0]) < CardRenderer.CardHeightMm - 1f;
        }

        private bool Outside(int slot)
        {
            const float tol = 3f;
            return _x[slot] < -tol || _y[slot] < -tol ||
                   _x[slot] + CardRenderer.CardWidthMm > _page.Width + tol ||
                   _y[slot] + CardRenderer.CardHeightMm > _page.Height + tol;
        }

        private bool SlotUsed(int slot)
        {
            return slot == 0 || _cboMode.SelectedIndex != (int)SlotMode.One;
        }

        private bool SlotBad(int slot)
        {
            return SlotUsed(slot) && (Outside(slot) || (slot == 1 && Overlaps()));
        }

        private void UpdateStatus()
        {
            string text = string.Format(CultureInfo.InvariantCulture, "Page {0:0.0} x {1:0.0} mm{2}.  Card {3} x {4} mm.",
                _page.Width, _page.Height, _pageKnown ? "" : " (printer page unknown, A4 assumed)",
                CardRenderer.CardWidthMm, CardRenderer.CardHeightMm);
            bool warn = false;
            if (Outside(0)) { text += "  Slot 1 is outside the page — it will print blank."; warn = true; }
            if (SlotUsed(1))
            {
                if (Outside(1)) { text += "  Slot 2 is outside the page — it will print blank."; warn = true; }
                if (Overlaps()) { text += "  Slot 2 overlaps slot 1."; warn = true; }
            }
            _lblStatus.Text = text;
            _lblStatus.ForeColor = warn ? Color.DarkRed : Color.DimGray;
        }

        // ===================== Canvas =====================
        // Tray view, drawn like the black PVC ID-card tray: two card recesses with a thumb hole,
        // the CD hub area below them and the white printed markings on the rims.
        // The page is shown rotated 90° counter-clockwise, so page Y runs left→right (slot 1 on the left,
        // slot 2 on the right) and page X runs bottom→top. The top edge (notch + up arrow) feeds into the printer.

        private static readonly Color CanvasBack = Color.FromArgb(239, 239, 241);   // also what shows through cut-outs
        private static readonly Color TrayTop = Color.FromArgb(56, 57, 61);
        private static readonly Color TrayBottom = Color.FromArgb(34, 35, 38);
        private static readonly Color TrayEdge = Color.FromArgb(14, 14, 16);
        private static readonly Color Raised = Color.FromArgb(62, 63, 68);
        private static readonly Color Recess = Color.FromArgb(26, 27, 30);
        private static readonly Color DiscFill = Color.FromArgb(44, 45, 49);
        private static readonly Color Engrave = Color.FromArgb(76, 77, 83);
        private static readonly Color Marking = Color.FromArgb(240, 240, 238);
        private static readonly Color PageLine = Color.FromArgb(100, 101, 108);
        private static readonly Color LabelInk = Color.FromArgb(214, 215, 219);
        private static readonly Color LabelInkLight = Color.FromArgb(150, 151, 157);
        private static readonly Color HoverEdge = Color.FromArgb(178, 179, 185);
        private static readonly Color Accent = Color.FromArgb(64, 160, 255);
        private static readonly Color Danger = Color.FromArgb(255, 96, 96);

        private const float TraySideMm = 16f;     // rim left and right of the page (white markings)
        private const float TrayTopMm = 16f;      // tray above the page (feed notch + arrow)
        private const float TrayBottomMm = 18f;   // tray under the page (pull notch + arrow)
        private const float NotchWMm = 16f, NotchDMm = 7f, StepMm = 3f;
        private const float LipMm = 1.4f;         // raised edge around each card recess
        private const float LabelBandMm = 14f;    // slot number + X/Y under each recess

        private float _scale;     // pixels per mm
        private PointF _origin;   // top-left of the rotated page in canvas pixels

        private float M(float mm) { return mm * _scale; }

        private void LayoutView()
        {
            const float pad = 20f;
            float totalW = _page.Height + 2f * TraySideMm;
            float totalH = TrayTopMm + _page.Width + TrayBottomMm;
            float availW = Math.Max(10f, _canvas.ClientSize.Width - 2f * pad);
            float availH = Math.Max(10f, _canvas.ClientSize.Height - 2f * pad);
            _scale = Math.Min(availW / totalW, availH / totalH);
            float left = (_canvas.ClientSize.Width - totalW * _scale) / 2f;
            float top = (_canvas.ClientSize.Height - totalH * _scale) / 2f;
            _origin = new PointF(left + TraySideMm * _scale, top + TrayTopMm * _scale);
        }

        private PointF ToCanvas(float xMm, float yMm)
        {
            return new PointF(_origin.X + yMm * _scale, _origin.Y + (_page.Width - xMm) * _scale);
        }

        private PointF ToMm(PointF p)
        {
            return new PointF(_page.Width - (p.Y - _origin.Y) / _scale, (p.X - _origin.X) / _scale);
        }

        private RectangleF SlotRectMm(int slot)
        {
            return new RectangleF(_x[slot], _y[slot], CardRenderer.CardWidthMm, CardRenderer.CardHeightMm);
        }

        /// <summary>The slot as it appears on screen (portrait, because the page is rotated).</summary>
        private RectangleF SlotRectCanvas(int slot)
        {
            PointF tl = ToCanvas(_x[slot] + CardRenderer.CardWidthMm, _y[slot]);
            return new RectangleF(tl.X, tl.Y, CardRenderer.CardHeightMm * _scale, CardRenderer.CardWidthMm * _scale);
        }

        private RectangleF PageRect()
        {
            return new RectangleF(_origin.X, _origin.Y, _page.Height * _scale, _page.Width * _scale);
        }

        private RectangleF TrayRect()
        {
            return RectangleF.FromLTRB(_origin.X - M(TraySideMm), _origin.Y - M(TrayTopMm),
                _origin.X + M(_page.Height + TraySideMm), _origin.Y + M(_page.Width + TrayBottomMm));
        }

        /// <summary>Area covered by the slots in use; the rim markings line up with it.</summary>
        private RectangleF SlotBand()
        {
            RectangleF band = SlotRectCanvas(0);
            return SlotUsed(1) ? RectangleF.Union(band, SlotRectCanvas(1)) : band;
        }

        private void CanvasPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            LayoutView();

            RectangleF tray = TrayRect();
            RectangleF band = SlotBand();
            using (GraphicsPath body = TrayPath(tray))
            {
                DrawTray(g, tray, body);

                GraphicsState st = g.Save();
                g.SetClip(body);   // markings follow the slots, but never spill off the tray
                DrawPageArea(g);
                DrawDisc(g, band);
                DrawRimMarkings(g, tray, band);
                DrawFeedArrows(g, tray);
                g.Restore(st);
            }

            Bitmap img = _rdoFront.Checked ? _front : (_rdoBack.Checked ? _back : null);

            // Unselected slot first so the selected one is on top
            DrawSlot(g, 1 - _selected, img);
            DrawSlot(g, _selected, img);
            DrawLabels(g);
        }

        /// <summary>Tray outline: stepped corners and a notch in the middle of the top and bottom edge.</summary>
        private GraphicsPath TrayPath(RectangleF tray)
        {
            float l = tray.Left, r = tray.Right, t = tray.Top, b = tray.Bottom;
            float cx = (l + r) / 2f;
            float step = M(StepMm), sw = tray.Width * 0.16f, nw = M(NotchWMm) / 2f, nd = M(NotchDMm);

            var path = new GraphicsPath();
            path.AddPolygon(new[]
            {
                new PointF(l, t + step), new PointF(l + sw, t + step), new PointF(l + sw, t),
                new PointF(cx - nw, t), new PointF(cx - nw, t + nd), new PointF(cx + nw, t + nd), new PointF(cx + nw, t),
                new PointF(r - sw, t), new PointF(r - sw, t + step), new PointF(r, t + step),
                new PointF(r, b - step), new PointF(r - sw, b - step), new PointF(r - sw, b),
                new PointF(cx + nw, b), new PointF(cx + nw, b - nd), new PointF(cx - nw, b - nd), new PointF(cx - nw, b),
                new PointF(l + sw, b), new PointF(l + sw, b - step), new PointF(l, b - step)
            });
            return path;
        }

        private void DrawTray(Graphics g, RectangleF tray, GraphicsPath body)
        {
            using (var shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
            {
                GraphicsState st = g.Save();
                g.TranslateTransform(3f, 4f);
                g.FillPath(shadow, body);
                g.Restore(st);
            }

            using (var fill = new LinearGradientBrush(RectangleF.Inflate(tray, 0f, 1f), TrayTop, TrayBottom, LinearGradientMode.Vertical))
                g.FillPath(fill, body);
            using (var edge = new Pen(TrayEdge, 1.5f) { LineJoin = LineJoin.Round })
                g.DrawPath(edge, body);

            // moulded pin marks near the corners
            float d = Math.Max(2f, M(1.3f));
            using (var dot = new SolidBrush(TrayEdge))
            {
                foreach (float x in new[] { tray.Left + M(4f), tray.Right - M(4f) })
                    foreach (float y in new[] { tray.Top + M(StepMm + 6f), tray.Bottom - M(StepMm + 6f) })
                        g.FillEllipse(dot, x - d / 2f, y - d / 2f, d, d);
            }
        }

        /// <summary>Faint dashed outline of the printable page inside the tray.</summary>
        private void DrawPageArea(Graphics g)
        {
            RectangleF page = PageRect();
            using (var pen = new Pen(PageLine, 1f) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(pen, page.X, page.Y, page.Width, page.Height);
        }

        /// <summary>The round CD/DVD area under the cards, drawn only when the page has room for it.</summary>
        private void DrawDisc(Graphics g, RectangleF band)
        {
            RectangleF page = PageRect();
            float top = Math.Max(band.Bottom + M(LabelBandMm), page.Top + M(4f));
            float bottom = page.Bottom - M(3f);
            float h = bottom - top, w = page.Width - M(8f);
            if (h < M(40f) || w < M(40f)) return;

            float d = Math.Min(Math.Min(h, w), M(118f));
            float cx = page.Left + page.Width / 2f, cy = top + h / 2f;

            using (var edge = new Pen(TrayEdge, 1.2f))
            using (var hole = new SolidBrush(CanvasBack))
            {
                using (var platform = new SolidBrush(DiscFill))
                    g.FillEllipse(platform, Circle(cx, cy, d));
                using (var hi = new Pen(Color.FromArgb(80, 81, 87), 1f))
                    g.DrawEllipse(hi, Circle(cx, cy, d - 3f));
                g.DrawEllipse(edge, Circle(cx, cy, d));

                // hub with its two clip slots and the centre clip
                using (var hub = new SolidBrush(Color.FromArgb(38, 39, 43)))
                    g.FillEllipse(hub, Circle(cx, cy, d * 0.4f));
                g.DrawEllipse(edge, Circle(cx, cy, d * 0.4f));
                float sw = d * 0.04f, sh = d * 0.24f;
                foreach (float sx in new[] { cx - d * 0.1f, cx + d * 0.1f })
                {
                    var cut = new RectangleF(sx - sw / 2f, cy - sh / 2f, sw, sh);
                    g.FillRectangle(hole, cut);
                    g.DrawRectangle(edge, cut.X, cut.Y, cut.Width, cut.Height);
                }
                g.FillEllipse(hole, Circle(cx, cy, d * 0.08f));
                g.DrawEllipse(edge, Circle(cx, cy, d * 0.08f));
                using (var clip = new SolidBrush(Recess))
                    g.FillEllipse(clip, Circle(cx, cy, d * 0.045f));

                // finger cut-out on the upper-left of the rim, small square hole at the bottom
                float off = d * 0.5f * 0.7071f;
                RectangleF finger = Circle(cx - off, cy - off, d * 0.15f);
                g.FillEllipse(hole, finger);
                g.DrawEllipse(edge, finger);
                float q = d * 0.075f;
                var square = new RectangleF(cx - q / 2f, cy + d * 0.4f - q / 2f, q, q);
                g.FillRectangle(hole, square);
                g.DrawRectangle(edge, square.X, square.Y, square.Width, square.Height);
            }
        }

        /// <summary>White printed markings on the left and right rim (targets, squares, guide lines, arrows).</summary>
        private void DrawRimMarkings(Graphics g, RectangleF tray, RectangleF band)
        {
            RectangleF page = PageRect();
            float lcx = (tray.Left + page.Left) / 2f, rcx = (tray.Right + page.Right) / 2f;
            float h = band.Height;
            float half = M(3.25f);                      // half of a marker square
            float lw = Math.Max(1f, M(0.7f));           // guide line thickness

            using (var white = new SolidBrush(Marking))
            using (var pen = new Pen(Marking, lw))
            {
                // --- beside the cards ---
                float tgtY = band.Top + 0.10f * h;
                float sqY = band.Top + 0.66f * h;

                DrawTarget(g, white, lcx, tgtY);
                float barTop = tgtY + M(3.4f);
                if (sqY - half - barTop > 0f)
                    g.FillRectangle(white, lcx - M(0.65f), barTop, M(1.3f), sqY - half - barTop);
                DrawHoleSquare(g, white, lcx, sqY);
                DrawArrow(g, white, lcx, sqY + half, M(5f), M(4f), true);

                g.DrawRectangle(pen, rcx - M(2f), band.Top + 0.30f * h - M(2.5f), M(4f), M(5f));
                DrawHoleSquare(g, white, rcx, sqY);

                // guide lines level with the bottom of the cards
                g.FillRectangle(white, tray.Left + M(2.5f), band.Bottom - lw / 2f, page.Left + M(6f) - tray.Left - M(2.5f), lw);
                g.FillRectangle(white, page.Right - M(6f), band.Bottom - lw / 2f, tray.Right - M(2.5f) - page.Right + M(6f), lw);

                if (SlotUsed(1))
                {
                    RectangleF a = SlotRectCanvas(0), b = SlotRectCanvas(1);
                    RectangleF left = a.X <= b.X ? a : b, right = a.X <= b.X ? b : a;
                    float gap = right.Left - left.Right - 2f * M(LipMm);
                    if (gap > M(2f))
                    {
                        float len = Math.Min(M(6f), gap - M(1f));
                        float mx = (left.Right + right.Left) / 2f;
                        g.FillRectangle(white, mx - len / 2f, band.Bottom - lw / 2f, len, lw);
                    }
                }

                // --- beside the CD area (only when there is room under the cards) ---
                float lowTop = band.Bottom + M(LabelBandMm), lowBot = page.Bottom;
                float lh = lowBot - lowTop;
                if (lh < M(35f)) return;

                g.FillRectangle(white, tray.Left + M(2.5f), lowTop + 0.04f * lh, page.Left + M(6f) - tray.Left - M(2.5f), lw);
                g.FillRectangle(white, page.Right - M(6f), lowTop, tray.Right - M(2.5f) - page.Right + M(6f), lw);

                DrawHoleSquare(g, white, lcx, lowTop + 0.30f * lh);
                using (GraphicsPath small = RoundedRect(new RectangleF(lcx - M(2f), lowTop + 0.58f * lh - M(2f), M(4f), M(4f)), M(0.5f)))
                    g.FillPath(white, small);

                float rs = lowTop + 0.34f * lh;
                float iconY = lowTop + 0.66f * lh;
                DrawArrow(g, white, rcx, rs - half, M(5f), M(4f), false);
                DrawHoleSquare(g, white, rcx, rs);
                float barTop2 = rs + half, barBot2 = iconY - M(2.5f);
                if (barBot2 > barTop2)
                    g.FillRectangle(white, rcx - M(0.65f), barTop2, M(1.3f), barBot2 - barTop2);
                DrawKeyIcon(g, white, rcx, iconY);
            }
        }

        /// <summary>Feed direction: up arrow under the top notch (into the printer), down arrow above the bottom notch.</summary>
        private void DrawFeedArrows(Graphics g, RectangleF tray)
        {
            float cx = (tray.Left + tray.Right) / 2f;
            using (var white = new SolidBrush(Marking))
            {
                DrawArrow(g, white, cx, tray.Top + M(NotchDMm + 7f), M(5.5f), M(5f), false);
                DrawArrow(g, white, cx, tray.Bottom - M(NotchDMm + 7f), M(5.5f), M(5f), true);
            }
        }

        private void DrawTarget(Graphics g, Brush white, float cx, float cy)
        {
            using (var pen = new Pen(Marking, Math.Max(1f, M(0.8f))))
                g.DrawEllipse(pen, Circle(cx, cy, M(4.6f)));
            g.FillEllipse(white, Circle(cx, cy, M(1.6f)));
        }

        /// <summary>White square with a round hole (the tray's sensor marks).</summary>
        private void DrawHoleSquare(Graphics g, Brush white, float cx, float cy)
        {
            float s = M(6.5f);
            using (GraphicsPath p = RoundedRect(new RectangleF(cx - s / 2f, cy - s / 2f, s, s), M(0.6f)))
                g.FillPath(white, p);
            using (var ring = new SolidBrush(TrayEdge))
                g.FillEllipse(ring, Circle(cx, cy, M(3.4f)));
            using (var hole = new SolidBrush(CanvasBack))
                g.FillEllipse(hole, Circle(cx, cy, M(2.6f)));
        }

        /// <summary>White square with a dark half-disc (the keyed mark at the bottom of the right rim).</summary>
        private void DrawKeyIcon(Graphics g, Brush white, float cx, float cy)
        {
            float s = M(5f);
            using (GraphicsPath p = RoundedRect(new RectangleF(cx - s / 2f, cy - s / 2f, s, s), M(0.5f)))
                g.FillPath(white, p);
            float r = M(1.6f);
            using (var dark = new SolidBrush(TrayBottom))
                g.FillPie(dark, cx - r, cy - M(0.6f) - r, 2f * r, 2f * r, 0f, 180f);
        }

        /// <summary>Solid block arrow starting at <paramref name="yBase"/> and pointing down or up.</summary>
        private static void DrawArrow(Graphics g, Brush brush, float cx, float yBase, float len, float width, bool down)
        {
            float dir = down ? 1f : -1f;
            float stem = width * 0.34f, neck = yBase + dir * len * 0.45f, tip = yBase + dir * len;
            g.FillPolygon(brush, new[]
            {
                new PointF(cx - stem / 2f, yBase), new PointF(cx + stem / 2f, yBase),
                new PointF(cx + stem / 2f, neck), new PointF(cx + width / 2f, neck),
                new PointF(cx, tip),
                new PointF(cx - width / 2f, neck), new PointF(cx - stem / 2f, neck)
            });
        }

        private static RectangleF Circle(float cx, float cy, float d)
        {
            return new RectangleF(cx - d / 2f, cy - d / 2f, d, d);
        }

        private void DrawSlot(Graphics g, int slot, Bitmap img)
        {
            bool used = SlotUsed(slot);
            bool selected = slot == _selected;
            bool hover = slot == _hover && !selected;
            bool bad = SlotBad(slot);
            RectangleF rc = SlotRectCanvas(slot);

            DrawRecess(g, slot, rc);
            if (img != null) DrawCardImage(g, slot, img, used);

            if (!used)
            {
                using (GraphicsPath lip = RoundedRect(RectangleF.Inflate(rc, M(LipMm), M(LipMm)), M(1.4f)))
                using (var veil = new SolidBrush(Color.FromArgb(150, TrayBottom)))
                    g.FillPath(veil, lip);
            }

            Color edge = bad ? Danger : (selected ? Accent : (hover ? HoverEdge : Color.Empty));
            float radius = img != null ? M(3.18f) : M(0.8f);
            using (GraphicsPath path = RoundedRect(rc, radius))
            {
                if (edge.IsEmpty)
                {
                    if (!used)
                        using (var pen = new Pen(PageLine, 1f) { DashStyle = DashStyle.Dash })
                            g.DrawPath(pen, path);
                    return;
                }
                if (selected || bad)
                {
                    using (var glow = new Pen(Color.FromArgb(70, edge), 6f) { LineJoin = LineJoin.Round })
                        g.DrawPath(glow, path);
                }
                using (var pen = new Pen(edge, selected || bad ? 2f : 1.2f) { DashStyle = used ? DashStyle.Solid : DashStyle.Dash })
                    g.DrawPath(pen, path);
            }
        }

        /// <summary>Card pocket: raised lip, dark recess, oval thumb hole and the engraved slot number.</summary>
        private void DrawRecess(Graphics g, int slot, RectangleF rc)
        {
            using (var edge = new Pen(TrayEdge, 1f))
            {
                using (GraphicsPath lip = RoundedRect(RectangleF.Inflate(rc, M(LipMm), M(LipMm)), M(1.4f)))
                using (var fill = new SolidBrush(Raised))
                {
                    g.FillPath(fill, lip);
                    g.DrawPath(edge, lip);
                }
                using (GraphicsPath pocket = RoundedRect(rc, M(0.8f)))
                using (var fill = new SolidBrush(Recess))
                using (var inner = new Pen(TrayEdge, 1.6f))
                {
                    g.FillPath(fill, pocket);
                    g.DrawPath(inner, pocket);
                }

                float cx = rc.X + rc.Width / 2f, cy = rc.Y + rc.Height / 2f;
                var oval = new RectangleF(cx - rc.Width * 0.125f, cy - rc.Height * 0.095f, rc.Width * 0.25f, rc.Height * 0.19f);
                using (var hole = new SolidBrush(CanvasBack))
                    g.FillEllipse(hole, oval);
                g.DrawEllipse(edge, oval);

                float px = M(5f);
                if (px < 8f) return;
                using (var font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var ink = new SolidBrush(Engrave))
                using (var center = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString((slot + 1).ToString(CultureInfo.InvariantCulture), font, ink, cx, rc.Top + M(2.5f), center);
            }
        }

        private void DrawCardImage(Graphics g, int slot, Bitmap img, bool used)
        {
            RectangleF r = SlotRectMm(slot);
            GraphicsState state = g.Save();
            g.TranslateTransform(_origin.X, _origin.Y + _page.Width * _scale);
            g.RotateTransform(-90f);
            g.ScaleTransform(_scale, _scale);

            using (GraphicsPath path = RoundedRect(r, 3.18f))
            {
                g.SetClip(path);
                if (used)
                {
                    g.DrawImage(img, r);
                }
                else
                {
                    using (var attr = new ImageAttributes())
                    {
                        attr.SetColorMatrix(new ColorMatrix { Matrix33 = 0.3f });
                        var dest = new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom) };
                        g.DrawImage(img, dest, new RectangleF(0, 0, img.Width, img.Height), GraphicsUnit.Pixel, attr);
                    }
                }
                g.ResetClip();
            }
            g.Restore(state);
        }

        /// <summary>Slot number and its X/Y under each card recess.</summary>
        private void DrawLabels(Graphics g)
        {
            float numPx = Clamp(5.5f * _scale, 12f, 36f);
            float smallPx = Clamp(2.8f * _scale, 9f, 13f);

            using (var numFont = new Font("Segoe UI", numPx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var smallFont = new Font("Segoe UI", smallPx, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var center = new StringFormat { Alignment = StringAlignment.Center })
            {
                for (int slot = 0; slot < 2; slot++)
                {
                    RectangleF rc = SlotRectCanvas(slot);
                    bool used = SlotUsed(slot);
                    Color c = SlotBad(slot) ? Danger : (slot == _selected ? Accent : LabelInk);
                    if (!used) c = Color.FromArgb(110, c);

                    float cx = rc.X + rc.Width / 2f;
                    float top = rc.Bottom + M(LipMm + 1.2f);

                    using (var brush = new SolidBrush(c))
                        g.DrawString((slot + 1).ToString(CultureInfo.InvariantCulture), numFont, brush, cx, top, center);

                    string coords = string.Format(CultureInfo.InvariantCulture, "X {0:0.0}  ·  Y {1:0.0}{2}",
                        _x[slot], _y[slot], used ? "" : "  (not used)");
                    using (var brush = new SolidBrush(used ? LabelInkLight : Color.FromArgb(110, LabelInkLight)))
                        g.DrawString(coords, smallFont, brush, cx, top + numPx * 1.15f, center);
                }
            }
        }

        // ===================== Mouse / keyboard =====================
        private int SlotAt(PointF mm)
        {
            foreach (int slot in new[] { _selected, 1 - _selected })   // selected slot is drawn on top
                if (SlotRectMm(slot).Contains(mm)) return slot;
            return -1;
        }

        private void SetHover(int slot)
        {
            if (_hover == slot) return;
            _hover = slot;
            _canvas.Invalidate();
        }

        private void CanvasMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            LayoutView();
            PointF mm = ToMm(e.Location);
            int slot = SlotAt(mm);
            if (slot < 0) return;

            _selected = slot;
            _dragging = slot;
            _dragOffsetMm = new PointF(mm.X - _x[slot], mm.Y - _y[slot]);
            _canvas.Invalidate();
        }

        private void CanvasMouseMove(object sender, MouseEventArgs e)
        {
            LayoutView();
            PointF mm = ToMm(e.Location);

            if (_dragging < 0)
            {
                int over = SlotAt(mm);
                _canvas.Cursor = over >= 0 ? Cursors.SizeAll : Cursors.Default;
                SetHover(over);
                return;
            }

            _x[_dragging] = Round(mm.X - _dragOffsetMm.X);
            _y[_dragging] = Round(mm.Y - _dragOffsetMm.Y);
            SyncControls();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Arrow keys move the selected slot on screen, unless a number box has the focus
            if (!(ActiveControl is NumericUpDown) && !(ActiveControl is ComboBox))
            {
                float step = (keyData & Keys.Shift) != 0 ? 1f : Snap;
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Up: Nudge(step, 0f); return true;
                    case Keys.Down: Nudge(-step, 0f); return true;
                    case Keys.Left: Nudge(0f, -step); return true;
                    case Keys.Right: Nudge(0f, step); return true;
                    case Keys.Tab: Select(1 - _selected); return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ===================== Helpers =====================
        private static float Clamp(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = Math.Max(0.1f, Math.Min(radius * 2f, Math.Min(r.Width, r.Height)));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_front != null) _front.Dispose();
                if (_back != null) _back.Dispose();
            }
            base.Dispose(disposing);
        }

        private class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }
    }
}
