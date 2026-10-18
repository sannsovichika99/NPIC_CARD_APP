using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Card Template…: choose the card design used for preview and printing. Every design is drawn live with the selected
    /// student (or a sample student) and the current Card Text, so what is shown is what prints.
    /// Returns the choice in <see cref="SelectedTemplate"/>; the caller saves it.
    /// </summary>
    internal sealed class CardTemplateForm : ModalForm
    {
        private const int BodyPad = 24;
        private const int PreviewHeight = 236;

        private readonly List<TemplateTile> _tiles = new List<TemplateTile>();
        private readonly Dictionary<CardTemplate, Bitmap> _fronts = new Dictionary<CardTemplate, Bitmap>();
        private readonly Dictionary<CardTemplate, Bitmap> _backs = new Dictionary<CardTemplate, Bitmap>();
        private readonly CardPairPreview _preview;
        private readonly Label _hint;
        private readonly CardTemplate _current;
        private readonly Size _clientSize;
        private CardTemplate _selected;

        /// <param name="student">Student drawn on the previews; null = a sample student.</param>
        public CardTemplateForm(Student student, AppSettings settings)
        {
            Text = "Card Template";
            Subtitle = "Design of the printed cards, front and back";
            Glyph = Theme.IcoTemplate;
            Resizable = false;
            KeyPreview = true;

            _current = settings.CardTemplate;
            RenderAll(student ?? SampleStudent(), settings);

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            int width = BodyPad * 2 + CardTemplates.All.Length * TemplateTile.CellWidth;
            int y = 18;

            body.Controls.Add(SectionLabel("TEMPLATE", BodyPad, y));
            y += 24;
            for (int i = 0; i < CardTemplates.All.Length; i++)
            {
                CardTemplateInfo info = CardTemplates.All[i];
                var tile = new TemplateTile(info.Name, _fronts[info.Template], info.Template == _current)
                {
                    Location = new Point(BodyPad + i * TemplateTile.CellWidth, y),
                    TabIndex = i
                };
                CardTemplate template = info.Template;
                tile.Click += (s, e) => Choose(template);
                _tiles.Add(tile);
                body.Controls.Add(tile);
            }
            y += TemplateTile.CellHeight + 14;

            body.Controls.Add(SectionLabel("PREVIEW  ·  FRONT AND BACK", BodyPad, y));
            y += 24;
            _preview = new CardPairPreview { Location = new Point(BodyPad, y), Size = new Size(width - 2 * BodyPad, PreviewHeight) };
            body.Controls.Add(_preview);
            y += PreviewHeight + 10;

            _hint = new Label
            {
                AutoSize = false, Location = new Point(BodyPad, y), Size = new Size(width - 2 * BodyPad, 52),
                Font = Theme.Small, ForeColor = Theme.TextSoft, BackColor = Theme.Surface, UseMnemonic = false
            };
            body.Controls.Add(_hint);
            y += _hint.Height + 8;

            var footer = ModalUi.Footer();
            var apply = ModalUi.Button("Use Template", PillKind.Primary);
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

            Choose(_current);
        }

        public CardTemplate SelectedTemplate { get { return _selected; } }

        /// <summary>Draws front + back of every design once (600 dpi) and keeps screen-size copies.</summary>
        private void RenderAll(Student student, AppSettings settings)
        {
            Cursor old = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                foreach (CardTemplateInfo info in CardTemplates.All)
                {
                    _fronts[info.Template] = Shrink(() => CardRenderer.RenderFront(student, settings, info.Template));
                    _backs[info.Template] = Shrink(() => CardRenderer.RenderBack(settings, info.Template));
                }
            }
            finally
            {
                Cursor.Current = old;
            }
        }

        /// <summary>Screen copy, 2x the largest size it is shown at; null when the design could not be drawn.</summary>
        private static Bitmap Shrink(Func<Bitmap> render)
        {
            Bitmap full;
            try { full = render(); }
            catch (Exception) { return null; }   // e.g. an unreadable photo: show an empty card instead of failing
            using (full)
            {
                const int width = 720;
                int height = (int)Math.Round(width * (double)full.Height / full.Width);
                var small = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(full, 0, 0, width, height);
                }
                return small;
            }
        }

        private static Student SampleStudent()
        {
            return new Student
            {
                StudentId = "NPIC24001", NameKh = "សុខ សុភា", NameEn = "Sok Sophea", Gender = "F",
                DateOfBirth = "15/03/2005", Faculty = "បច្ចេកវិទ្យាព័ត៌មាន", Level = "បរិញ្ញាបត្របច្ចេកវិទ្យា",
                Generation = "17", ValidUntil = "30/12/2027"
            };
        }

        private static Label SectionLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text, Font = Theme.SmallCaps, ForeColor = Theme.TextMuted, BackColor = Theme.Surface,
                AutoSize = true, Location = new Point(x, y)
            };
        }

        private void Choose(CardTemplate template)
        {
            _selected = template;
            for (int i = 0; i < _tiles.Count; i++)
                _tiles[i].Selected = CardTemplates.All[i].Template == template;

            _preview.SetImages(_fronts[template], _backs[template]);
            CardTemplateInfo info = CardTemplates.Find(template);
            _hint.Text = info.Name + (template == _current ? "  (in use)" : "") + "  ·  " + info.Description +
                "\nAll designs print the same student data, Card Text, photo, QR codes, stamp and signature. " +
                "The slot positions from Test Print stay the same.";
        }

        protected override void OnLoad(EventArgs e)
        {
            ClientSize = _clientSize;   // again now that the real frame is known; before base.OnLoad centres the window
            base.OnLoad(e);
            foreach (TemplateTile t in _tiles)
                if (t.Selected) { ActiveControl = t; break; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Left / Right move between the tiles and choose the design, like a radio group.
            int at = _tiles.FindIndex(t => t.Focused);
            if (at >= 0 && (keyData == Keys.Left || keyData == Keys.Right))
            {
                int next = at + (keyData == Keys.Left ? -1 : 1);
                if (next >= 0 && next < _tiles.Count)
                {
                    _tiles[next].Focus();
                    Choose(CardTemplates.All[next].Template);
                }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Bitmap b in _fronts.Values) if (b != null) b.Dispose();
                foreach (Bitmap b in _backs.Values) if (b != null) b.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>Card corner radius (3.18 mm on an 85.6 mm CR80 card) at a given drawn width.</summary>
        internal static float CornerRadius(float cardWidthPx)
        {
            return cardWidthPx * 3.18f / CardRenderer.CardWidthMm;
        }

        /// <summary>Card image with rounded corners and a hairline edge (an empty grey card when image is null).</summary>
        internal static void DrawCard(Graphics g, Bitmap image, RectangleF r)
        {
            using (GraphicsPath path = Theme.RoundRect(r, CornerRadius(r.Width)))
            {
                if (image != null)
                {
                    using (var brush = new TextureBrush(image, WrapMode.Clamp))
                    {
                        brush.TranslateTransform(r.X, r.Y);
                        brush.ScaleTransform(r.Width / image.Width, r.Height / image.Height);
                        g.FillPath(brush, path);
                    }
                }
                else
                {
                    using (var b = new SolidBrush(Theme.Tile)) g.FillPath(b, path);
                }
                using (var pen = new Pen(Color.FromArgb(50, Theme.Dark ? Color.White : Color.Black))) g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>Template choice: card front thumbnail with the name underneath; the selected one gets a ring and a check.</summary>
    internal sealed class TemplateTile : Control
    {
        public const int CellWidth = 184, CellHeight = 150;
        private const int TileWidth = 172, TileHeight = 142;

        private readonly Bitmap _image;
        private readonly bool _inUse;
        private bool _hover, _selected;

        public TemplateTile(string name, Bitmap image, bool inUse)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Text = name; _image = image; _inUse = inUse;
            Size = new Size(TileWidth, TileHeight);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleName = name;
            AccessibleRole = AccessibleRole.RadioButton;
        }

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

            // Outer frame: accent ring when selected, soft border on hover / focus
            var frame = new RectangleF(1f, 1f, Width - 3f, Height - 3f);
            using (GraphicsPath fp = Theme.RoundRect(frame, 12))
            {
                if (_selected)
                {
                    using (var b = new SolidBrush(Theme.AccentSoft)) g.FillPath(b, fp);
                    using (var pen = new Pen(Theme.Accent, 2f)) g.DrawPath(pen, fp);
                }
                else if (_hover || (Focused && ShowFocusCues))
                {
                    using (var pen = new Pen(Theme.Border, 1.5f)) g.DrawPath(pen, fp);
                }
            }

            // Card thumbnail (85.6 : 54)
            float cw = Width - 24f, ch = cw * CardRenderer.CardHeightMm / CardRenderer.CardWidthMm;
            var card = new RectangleF(12f, 12f, cw, ch);
            CardTemplateForm.DrawCard(g, _image, card);

            if (_selected)
            {
                var badge = new RectangleF(card.Right - 22f, card.Y - 6f, 26f, 26f);
                using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, badge);
                using (var pen = new Pen(Theme.Surface, 2f)) g.DrawEllipse(pen, badge);
                Theme.DrawIcon(g, Theme.IcoCheck, 11f, Color.White, badge);
            }

            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            string label = _inUse ? Text + "  ·  in use" : Text;
            Theme.DrawText(g, label, _selected ? Theme.NavBold : Theme.Nav, _selected ? Theme.Accent : Theme.TextMain,
                new RectangleF(4f, card.Bottom + 6f, Width - 8f, Height - card.Bottom - 10f), StringAlignment.Center);
        }
    }

    /// <summary>Front and back of one design side by side, as large as the control allows.</summary>
    internal sealed class CardPairPreview : Control
    {
        private Bitmap _front, _back;

        public CardPairPreview()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
        }

        /// <summary>The images stay owned by the caller.</summary>
        public void SetImages(Bitmap front, Bitmap back)
        {
            _front = front;
            _back = back;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Theme.HighQuality(g);

            var frame = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath outline = Theme.RoundRect(frame, 12))
            {
                using (var b = new SolidBrush(Theme.AppBack)) g.FillPath(b, outline);
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, outline);
            }

            const float pad = 16f, gap = 20f, labelH = 22f;
            float ratio = CardRenderer.CardWidthMm / CardRenderer.CardHeightMm;
            float cw = Math.Min((Width - 2 * pad - gap) / 2f, (Height - 2 * pad - labelH) * ratio);
            float ch = cw / ratio;
            float left = (Width - (2 * cw + gap)) / 2f;
            float top = (Height - (ch + labelH)) / 2f;

            DrawSide(g, _front, "Front", new RectangleF(left, top, cw, ch), labelH);
            DrawSide(g, _back, "Back", new RectangleF(left + cw + gap, top, cw, ch), labelH);
        }

        private static void DrawSide(Graphics g, Bitmap image, string label, RectangleF r, float labelH)
        {
            // Soft drop shadow so the white card stands out from the light background
            var shadow = r;
            shadow.Offset(0f, 2f);
            using (GraphicsPath sp = Theme.RoundRect(shadow, CardTemplateForm.CornerRadius(r.Width)))
            using (var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 70 : 28, Color.Black)))
                g.FillPath(b, sp);
            CardTemplateForm.DrawCard(g, image, r);

            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Theme.DrawText(g, label, Theme.Small, Theme.TextMuted, new RectangleF(r.X, r.Bottom + 2f, r.Width, labelH), StringAlignment.Center);
        }
    }
}
