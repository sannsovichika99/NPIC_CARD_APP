using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Card Template…: choose the card design used for preview and printing — a built-in template or one of the designs made
    /// with Card Designer ("My designs"). Every design is drawn live with the selected student (or a sample student) and the
    /// current Card Text, so what is shown is what prints. My designs can be created, edited, renamed, duplicated and deleted here.
    /// Returns the choice in <see cref="SelectedTemplate"/> / <see cref="SelectedDesignId"/>; the caller saves it.
    /// </summary>
    internal sealed class CardTemplateForm : ModalForm
    {
        private const int BodyPad = 24;
        private const int Columns = 5;
        private const int PreviewHeight = 236;
        private const int MaxDesignRows = 2;

        /// <summary>One selectable design: a built-in template, or a saved custom design (Template = Custom, DesignId set).</summary>
        private sealed class Choice
        {
            public CardTemplate Template;
            public string DesignId;      // null for built-in templates
            public string Name;
            public string Description;
            public Bitmap Front, Back;
            public TemplateTile Tile;
        }

        private readonly Student _student;
        private readonly AppSettings _settings;
        private readonly List<Choice> _builtIn = new List<Choice>();
        private readonly List<Choice> _mine = new List<Choice>();
        private readonly FlowLayoutPanel _minePanel;
        private readonly Label _mineTitle, _mineEmpty;
        private readonly CardPairPreview _preview;
        private readonly Label _hint;
        private readonly ToolTip _tip = new ToolTip();
        private readonly CardTemplate _currentTemplate;
        private readonly string _currentDesignId;
        private readonly Size _clientSize;
        private readonly PillButton _btnCopy;
        private Choice _selected;
        private ContextMenuStrip _menu;

        /// <param name="student">Student drawn on the previews; null = a sample student.</param>
        public CardTemplateForm(Student student, AppSettings settings)
        {
            Text = "Card Template";
            Subtitle = "Design of the printed cards, front and back — a built-in template or one of your own designs";
            Glyph = Theme.IcoTemplate;
            Resizable = false;
            KeyPreview = true;

            _student = student ?? SampleStudent();
            _settings = settings;
            _currentTemplate = settings.CardTemplate;
            _currentDesignId = _currentTemplate == CardTemplate.Custom ? CustomCardStore.ForSettings(settings).Id : null;

            Cursor oldCursor = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                foreach (CardTemplateInfo info in CardTemplates.All)
                {
                    if (info.Template == CardTemplate.Custom) continue;   // listed one by one under My designs
                    CardTemplate t = info.Template;
                    _builtIn.Add(new Choice
                    {
                        Template = t, Name = Lang.T(info.Name), Description = Lang.T(info.Description),
                        Front = Shrink(() => CardRenderer.RenderFront(_student, settings, t)),
                        Back = Shrink(() => CardRenderer.RenderBack(settings, t))
                    });
                }
                foreach (CustomCardDesign d in CustomCardStore.All()) _mine.Add(MakeChoice(d));
            }
            finally
            {
                Cursor.Current = oldCursor;
            }

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            int width = BodyPad * 2 + Columns * TemplateTile.CellWidth;
            int y = 16;

            // ---- Built-in templates ----
            body.Controls.Add(SectionLabel("BUILT-IN TEMPLATES", BodyPad, y));
            body.Controls.Add(new Label
            {
                Text = "Right-click a template to copy it into My designs and change it  ·  double-click to use it",
                Font = Theme.Small, ForeColor = Theme.TextMuted, BackColor = Theme.Surface, AutoSize = false,
                TextAlign = ContentAlignment.TopRight, Location = new Point(BodyPad + 200, y - 1), Size = new Size(width - 2 * BodyPad - 200, 18)
            });
            y += 24;
            for (int i = 0; i < _builtIn.Count; i++)
            {
                Choice c = _builtIn[i];
                c.Tile = NewTile(c);
                c.Tile.Location = new Point(BodyPad + i * TemplateTile.CellWidth, y);
                c.Tile.DoubleClick += (s, e) => { Choose(c); DialogResult = DialogResult.OK; };
                c.Tile.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowBuiltInMenu(c, e.Location); };
                body.Controls.Add(c.Tile);
            }
            y += TemplateTile.CellHeight + 10;

            // ---- My designs (Card Designer) ----
            _mineTitle = SectionLabel("", BodyPad, y);
            body.Controls.Add(_mineTitle);
            var mineHint = new Label
            {
                Text = "Right-click a design to edit, rename, duplicate or delete it  ·  double-click to edit",
                Font = Theme.Small, ForeColor = Theme.TextMuted, BackColor = Theme.Surface, AutoSize = false,
                TextAlign = ContentAlignment.TopRight, Location = new Point(BodyPad + 200, y - 1), Size = new Size(width - 2 * BodyPad - 200, 18)
            };
            body.Controls.Add(mineHint);
            y += 24;
            int rows = Math.Max(1, Math.Min(MaxDesignRows, (_mine.Count + 1 + Columns - 1) / Columns));
            _minePanel = new FlowLayoutPanel
            {
                Location = new Point(BodyPad, y), Size = new Size(width - 2 * BodyPad + 14, rows * TemplateTile.CellHeight),
                AutoScroll = true, WrapContents = true, BackColor = Theme.Surface, Margin = new Padding(0), Padding = new Padding(0)
            };
            body.Controls.Add(_minePanel);
            _mineEmpty = new Label
            {
                Text = "No designs yet.\nClick “New design” to make your own card with Card Designer.",
                Font = Theme.Small, ForeColor = Theme.TextSoft, BackColor = Theme.Surface, AutoSize = false,
                Size = new Size(width - 2 * BodyPad - TemplateTile.CellWidth - 20, 60), TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(12, 40, 0, 0)
            };
            y += _minePanel.Height + 12;

            // ---- Preview ----
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

            // ---- Footer ----
            var footer = ModalUi.Footer();
            var apply = ModalUi.Button("Use Template", PillKind.Primary);
            apply.DialogResult = DialogResult.OK;
            var cancel = ModalUi.Button("Cancel", PillKind.Secondary);
            cancel.DialogResult = DialogResult.Cancel;
            var designer = ModalUi.Button("Card Designer…", PillKind.Ghost, (s, e) =>
                OpenInDesigner(_selected != null ? _selected.DesignId : null, false));
            designer.Margin = new Padding(0, 0, 24, 0);
            _tip.SetTip(designer, "Open Card Designer: edit the selected design of yours, or create a new one");
            _btnCopy = ModalUi.Button("Copy as new design", PillKind.Ghost, (s, e) => CopyBuiltIn(_selected, true));
            _tip.SetTip(_btnCopy, "Copy the selected built-in template into My designs and open it in Card Designer to change it");
            footer.Controls.Add(apply);    // right-to-left flow: first added = rightmost
            footer.Controls.Add(cancel);
            footer.Controls.Add(designer);
            footer.Controls.Add(_btnCopy);
            AcceptButton = apply;
            CancelButton = cancel;

            Controls.Add(body);
            Controls.Add(footer);

            _clientSize = new Size(width, HeaderHeight + y + ModalUi.FooterHeight);
            ClientSize = _clientSize;

            FillMyDesigns();
            Choose(FindCurrent() ?? _builtIn[0]);
        }

        // =====================================================================
        // RESULTS
        // =====================================================================
        public CardTemplate SelectedTemplate { get { return _selected.Template; } }

        /// <summary>Id of the chosen design when <see cref="SelectedTemplate"/> is Custom, otherwise null.</summary>
        public string SelectedDesignId { get { return _selected.DesignId; } }

        /// <summary>True when the user asked for Card Designer (the form closes with Cancel; the caller opens the designer).</summary>
        public bool OpenDesigner { get; private set; }

        /// <summary>Design to open in Card Designer (null = the one in use / newest).</summary>
        public string DesignerDesignId { get; private set; }

        /// <summary>Card Designer should start a new design.</summary>
        public bool DesignerCreateNew { get; private set; }

        /// <summary>A design was renamed, duplicated or deleted here.</summary>
        public bool DesignsChanged { get; private set; }

        // =====================================================================
        // MY DESIGNS
        // =====================================================================
        private Choice MakeChoice(CustomCardDesign d)
        {
            return new Choice
            {
                Template = CardTemplate.Custom, DesignId = d.Id, Name = string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name,
                Description = d.Modified > DateTime.MinValue
                    ? Lang.F("Your design, made with Card Designer · last saved {0}.", d.Modified.ToString("d MMM yyyy, HH:mm"))
                    : Lang.T("Your design, made with Card Designer."),
                Front = Shrink(() => CardRenderer.RenderCustomSide(d.Front, _student, _settings, 300f)),
                Back = Shrink(() => CardRenderer.RenderCustomSide(d.Back, null, _settings, 300f))
            };
        }

        /// <summary>(Re)builds the My designs row: one tile per saved design, then the New design tile.</summary>
        private void FillMyDesigns()
        {
            _minePanel.SuspendLayout();
            var old = new Control[_minePanel.Controls.Count];
            _minePanel.Controls.CopyTo(old, 0);   // disposing removes a control from the collection
            _minePanel.Controls.Clear();
            foreach (Control c in old) if (c != _mineEmpty) c.Dispose();

            foreach (Choice c in _mine)
            {
                Choice choice = c;
                c.Tile = NewTile(c);
                c.Tile.Margin = new Padding(0, 0, TemplateTile.CellWidth - c.Tile.Width, TemplateTile.CellHeight - c.Tile.Height);
                c.Tile.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowDesignMenu(choice, e.Location); };
                c.Tile.DoubleClick += (s, e) => OpenInDesigner(choice.DesignId, false);
                _minePanel.Controls.Add(c.Tile);
            }

            var add = new NewDesignTile
            {
                Margin = new Padding(0, 0, TemplateTile.CellWidth - NewDesignTile.TileWidth, TemplateTile.CellHeight - NewDesignTile.TileHeight)
            };
            add.Click += (s, e) => OpenInDesigner(null, true);
            _tip.SetTip(add, "Create a new blank card design with Card Designer");
            _minePanel.Controls.Add(add);
            if (_mine.Count == 0) _minePanel.Controls.Add(_mineEmpty);
            _minePanel.ResumeLayout();

            _mineTitle.Text = "MY DESIGNS  ·  " + _mine.Count;
            if (_selected != null && _selected.DesignId != null && _selected.Tile != null) _selected.Tile.Selected = true;
        }

        private TemplateTile NewTile(Choice c)
        {
            bool inUse = c.DesignId == null
                ? c.Template == _currentTemplate
                : _currentTemplate == CardTemplate.Custom && string.Equals(c.DesignId, _currentDesignId, StringComparison.OrdinalIgnoreCase);
            var tile = new TemplateTile(c.Name, c.Front, inUse);
            tile.Click += (s, e) => Choose(c);
            _tip.SetTip(tile, c.Name);
            return tile;
        }

        private void ShowDesignMenu(Choice c, Point at)
        {
            Choose(c);
            if (_menu != null) _menu.Dispose();
            _menu = ThemedMenu.Create();
            _menu.Items.Add(ThemedMenu.Item("Edit in Card Designer…", () => OpenInDesigner(c.DesignId, false)));
            _menu.Items.Add(ThemedMenu.Item("Rename…", () => RenameDesign(c)));
            _menu.Items.Add(ThemedMenu.Item("Duplicate", () => DuplicateDesign(c)));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(ThemedMenu.Item("Delete…", () => DeleteDesign(c)));
            _menu.Show(c.Tile, at);
        }

        private void ShowBuiltInMenu(Choice c, Point at)
        {
            Choose(c);
            if (_menu != null) _menu.Dispose();
            _menu = ThemedMenu.Create();
            _menu.Items.Add(ThemedMenu.Item("Use this template", () => { Choose(c); DialogResult = DialogResult.OK; }));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(ThemedMenu.Item("Copy and edit in Card Designer…", () => CopyBuiltIn(c, true)));
            _menu.Items.Add(ThemedMenu.Item("Copy to My designs", () => CopyBuiltIn(c, false)));
            _menu.Show(c.Tile, at);
        }

        /// <summary>
        /// Rebuilds a built-in template as an editable design (same look, live data), saves it under My designs and selects it.
        /// <paramref name="edit"/> = open it in Card Designer right away.
        /// </summary>
        private void CopyBuiltIn(Choice c, bool edit)
        {
            if (c == null || c.DesignId != null || !BuiltInDesigns.CanCopy(c.Template)) return;
            CustomCardDesign d = BuiltInDesigns.Create(c.Template);
            d.Name = CustomCardStore.UniqueName(Lang.F("{0} (copy)", c.Name));
            if (!TrySave(d)) return;
            Choice added = MakeChoice(d);
            _mine.Insert(0, added);
            AfterLibraryChange(added);
            if (edit) OpenInDesigner(d.Id, false);
        }

        private void RenameDesign(Choice c)
        {
            CustomCardDesign d = CustomCardStore.Find(c.DesignId);
            if (d == null) return;
            string name = DesignNamePrompt.Ask(this, "Rename design", "Name shown in this list and in Card Designer", d.Name);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == d.Name) return;
            CustomCardDesign copy = CustomCardStore.Clone(d);
            copy.Name = CustomCardStore.UniqueName(name.Trim(), d.Id);
            if (!TrySave(copy)) return;
            c.Name = copy.Name;
            AfterLibraryChange(c);
        }

        private void DuplicateDesign(Choice c)
        {
            CustomCardDesign d = CustomCardStore.Find(c.DesignId);
            if (d == null) return;
            CustomCardDesign copy = CustomCardStore.Duplicate(d);
            if (!TrySave(copy)) return;
            Choice added = MakeChoice(copy);
            _mine.Insert(0, added);
            AfterLibraryChange(added);
        }

        private void DeleteDesign(Choice c)
        {
            bool inUse = _currentTemplate == CardTemplate.Custom && string.Equals(c.DesignId, _currentDesignId, StringComparison.OrdinalIgnoreCase);
            if (AppDialog.Show(this, "Delete the design “" + c.Name + "”?" +
                    (inUse ? "\n\nIt is the design printed now; printing goes back to another design." : "") + "\n\nThis cannot be undone.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            try
            {
                CustomCardStore.Delete(c.DesignId);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                AppDialog.Show(this, "Could not delete the design:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _mine.Remove(c);
            if (c.Front != null) c.Front.Dispose();
            if (c.Back != null) c.Back.Dispose();
            c.Front = c.Back = null;
            AfterLibraryChange(_selected == c ? (FindCurrent() ?? _builtIn[0]) : _selected);
        }

        private bool TrySave(CustomCardDesign d)
        {
            try
            {
                CustomCardStore.Save(d);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                AppDialog.Show(this, "Could not save the design:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private void AfterLibraryChange(Choice select)
        {
            DesignsChanged = true;
            _selected = null;
            FillMyDesigns();
            Choose(select);
        }

        private void OpenInDesigner(string designId, bool createNew)
        {
            OpenDesigner = true;
            DesignerDesignId = designId;
            DesignerCreateNew = createNew;
            DialogResult = DialogResult.Cancel;
        }

        // =====================================================================
        // SELECTION
        // =====================================================================
        private IEnumerable<Choice> AllChoices()
        {
            foreach (Choice c in _builtIn) yield return c;
            foreach (Choice c in _mine) yield return c;
        }

        private Choice FindCurrent()
        {
            foreach (Choice c in AllChoices())
            {
                if (c.DesignId == null ? c.Template == _currentTemplate
                    : _currentTemplate == CardTemplate.Custom && string.Equals(c.DesignId, _currentDesignId, StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            return null;
        }

        private void Choose(Choice choice)
        {
            if (choice == null) return;
            _selected = choice;
            foreach (Choice c in AllChoices())
                if (c.Tile != null) c.Tile.Selected = c == choice;
            if (choice.Tile != null && choice.DesignId != null) _minePanel.ScrollControlIntoView(choice.Tile);

            _preview.SetImages(choice.Front, choice.Back);
            _btnCopy.Enabled = choice.DesignId == null && BuiltInDesigns.CanCopy(choice.Template);
            bool inUse = choice == FindCurrent();
            _hint.Text = choice.Name + (inUse ? Lang.T("  (in use)") : "") + "  ·  " + choice.Description +
                "\nAll designs print the same student data, Card Text, photo and QR codes. " +
                "The slot positions from Test Print stay the same.";
        }

        protected override void OnLoad(EventArgs e)
        {
            ClientSize = _clientSize;   // again now that the real frame is known; before base.OnLoad centres the window
            base.OnLoad(e);
            if (_selected != null && _selected.Tile != null) ActiveControl = _selected.Tile;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Left / Right move between the tiles (built-in, then my designs) and choose the design, like a radio group.
            var list = new List<Choice>(AllChoices());
            int at = list.FindIndex(c => c.Tile != null && c.Tile.Focused);
            if (at >= 0 && (keyData == Keys.Left || keyData == Keys.Right))
            {
                int next = at + (keyData == Keys.Left ? -1 : 1);
                if (next >= 0 && next < list.Count)
                {
                    list[next].Tile.Focus();
                    Choose(list[next]);
                }
                return true;
            }
            if (at >= 0 && keyData == Keys.Delete && list[at].DesignId != null)
            {
                DeleteDesign(list[at]);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Choice c in AllChoices())
                {
                    if (c.Front != null) c.Front.Dispose();
                    if (c.Back != null) c.Back.Dispose();
                }
                _tip.Dispose();
                if (_menu != null) _menu.Dispose();
                if (_mineEmpty.Parent == null) _mineEmpty.Dispose();
            }
            base.Dispose(disposing);
        }

        // =====================================================================
        // HELPERS
        // =====================================================================
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
                     ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.StandardDoubleClick, true);
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
            string label = _inUse ? Text + Lang.T("  ·  in use") : Text;
            Theme.DrawText(g, label, _selected ? Theme.NavBold : Theme.Nav, _selected ? Theme.Accent : Theme.TextMain,
                new RectangleF(4f, card.Bottom + 6f, Width - 8f, Height - card.Bottom - 10f), StringAlignment.Center);
        }
    }

    /// <summary>"+ New design" tile at the end of My designs: dashed card outline with a plus sign.</summary>
    internal sealed class NewDesignTile : Control
    {
        public const int TileWidth = 172, TileHeight = 142;
        private bool _hover;

        public NewDesignTile()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Text = "New design";
            Size = new Size(TileWidth, TileHeight);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleName = "New design";
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Theme.HighQuality(g);

            float cw = Width - 24f, ch = cw * CardRenderer.CardHeightMm / CardRenderer.CardWidthMm;
            var card = new RectangleF(12f, 12f, cw, ch);
            bool hot = _hover || (Focused && ShowFocusCues);
            using (GraphicsPath p = Theme.RoundRect(card, CardTemplateForm.CornerRadius(card.Width)))
            {
                using (var b = new SolidBrush(hot ? Theme.AccentSoft : Theme.Tile)) g.FillPath(b, p);
                using (var pen = new Pen(hot ? Theme.Accent : Theme.Border, 1.5f) { DashStyle = DashStyle.Dash }) g.DrawPath(pen, p);
            }
            var circle = new RectangleF(card.X + card.Width / 2f - 17f, card.Y + card.Height / 2f - 17f, 34f, 34f);
            using (var b = new SolidBrush(hot ? Theme.Accent : Theme.Surface)) g.FillEllipse(b, circle);
            using (var pen = new Pen(hot ? Theme.Accent : Theme.Border, 1.5f)) g.DrawEllipse(pen, circle);
            using (var pen = new Pen(hot ? Color.White : Theme.Accent, 2.4f))
            {
                float cx = circle.X + circle.Width / 2f, cy = circle.Y + circle.Height / 2f;
                g.DrawLine(pen, cx - 7f, cy, cx + 7f, cy);
                g.DrawLine(pen, cx, cy - 7f, cx, cy + 7f);
            }

            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Theme.DrawText(g, Lang.T("New design"), Theme.NavBold, Theme.Accent,
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
            Theme.DrawText(g, Lang.T(label), Theme.Small, Theme.TextMuted, new RectangleF(r.X, r.Bottom + 2f, r.Width, labelH), StringAlignment.Center);
        }
    }
}
