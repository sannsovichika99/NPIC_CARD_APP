using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Card Designer…: build your own cards. Add text, student fields, the photo, QR codes, images and shapes to the front and the
    /// back, move and resize them on a live preview (drawn by the same renderer that prints), and edit every detail on the right.
    /// Keeps a library of designs (+ New Design, the design menu to switch, duplicate, rename or delete). Works on a copy of one
    /// design: Save writes it to %AppData%\NPIC_CARD_APP\CardDesigns; Save &amp; Use also prints with it (<see cref="UseDesign"/>,
    /// <see cref="DesignId"/>). Close / Esc / ✕ ask before dropping unsaved changes.
    /// </summary>
    internal sealed class CardDesignerForm : ModalForm
    {
        private const int MaxUndo = 100;

        private readonly AppSettings _settings;
        private readonly Student _student;
        private CustomCardDesign _design;
        private bool _isNew;   // not saved yet (new, duplicated or imported)
        private bool _back;
        private CardElement _selected;
        private CardElement _clipboard;

        private readonly DesignCanvas _canvas;
        private readonly ListBox _layers;
        private readonly PropertyGrid _grid;
        private readonly Label _status;
        private readonly Label _propsTitle;
        private readonly PillButton _tabFront, _tabBack, _btnUndo, _btnRedo, _btnSnap, _btnOutlines, _btnDesign;
        private readonly Timer _renderTimer = new Timer { Interval = 15 };
        private readonly ToolTip _tip = new ToolTip();
        private bool _syncingLayers;

        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();
        private string _committed;   // design as of the last finished change
        private string _saved;       // design as last saved (or as opened / created)

        /// <summary>True after Save &amp; Use: the caller prints with <see cref="DesignId"/> (Custom template).</summary>
        public bool UseDesign { get; private set; }

        /// <summary>True when any design was saved or deleted (previews and the template list must be refreshed).</summary>
        public bool Changed { get; private set; }

        /// <summary>Id of the design open when the page closed ("" if it was never saved).</summary>
        public string DesignId { get { return _design.Id ?? ""; } }

        /// <param name="student">Student drawn on the front; null = a sample student.</param>
        /// <param name="designId">Saved design to open; null = the one in use (or the newest).</param>
        /// <param name="createNew">Start a new, blank design instead.</param>
        public CardDesignerForm(Student student, AppSettings settings, string designId = null, bool createNew = false)
        {
            // Khmer only: property names, help and choices of the elements in Khmer (saved designs stay unchanged).
            LocalizedProperties.Register(typeof(TextElement), typeof(FieldElement), typeof(ImageElement), typeof(PhotoElement),
                typeof(QrElement), typeof(ShapeElement), typeof(CardSideDesign));
            _settings = settings;
            _student = student ?? SampleStudent();

            Text = "Card Designer";
            Glyph = Theme.IcoEdit;
            KeyPreview = true;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1380, work.Width), Math.Min(900, work.Height));
            MinimumSize = new Size(Math.Min(1080, work.Width), Math.Min(700, work.Height));
            Font = new Font(CardFonts.UI, 9f);

            // ---- Toolbar: design library, side tabs, undo / redo, snapping, outlines ----
            FlowLayoutPanel toolbar = ModalUi.Toolbar(56);
            _btnDesign = Tool(toolbar, "Design ▾", null,
                "Switch to another saved design, duplicate, rename or delete this one, start over, import or export");
            _btnDesign.Click += (s, e) => ShowMenu(_btnDesign, DesignMenu());
            PillButton newDesign = ModalUi.ToolButton("+ New Design", null);
            newDesign.Click += (s, e) => ShowMenu(newDesign, NewDesignMenu());
            toolbar.Controls.Add(newDesign);
            _tip.SetTip(newDesign, "Create another card design (your saved designs stay as they are)");
            newDesign.Margin = new Padding(0, 0, 22, 0);
            _tabFront = Tool(toolbar, "Front", () => ShowSide(false), "Edit the front (student side)");
            _tabBack = Tool(toolbar, "Back", () => ShowSide(true), "Edit the back (same for every student)");
            _tabBack.Margin = new Padding(0, 0, 22, 0);
            _btnUndo = Tool(toolbar, "Undo", Undo, "Undo (Ctrl+Z)");
            _btnRedo = Tool(toolbar, "Redo", Redo, "Redo (Ctrl+Y)");
            _btnRedo.Margin = new Padding(0, 0, 22, 0);
            _btnSnap = Tool(toolbar, "Snap", ToggleSnap, "Snap to a 0.5 mm grid and to the card's edges and centre lines");
            _btnSnap.Checked = true;
            _btnOutlines = Tool(toolbar, "Outlines", ToggleOutlines, "Show the outline of every element and the 1.5 mm safe margin (never printed)");
            _btnOutlines.Checked = true;

            // ---- Left: add tools + layer list ----
            var left = new TableLayoutPanel
            {
                Dock = DockStyle.Left, Width = 248, ColumnCount = 1, RowCount = 5, BackColor = Theme.Surface,
                Padding = new Padding(14, 12, 10, 12)
            };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Paint += (s, e) => { using (var p = new Pen(Theme.GridLine)) e.Graphics.DrawLine(p, left.Width - 1, 0, left.Width - 1, left.Height); };

            left.Controls.Add(SectionLabel("ADD"), 0, 0);
            var adds = new TableLayoutPanel { ColumnCount = 2, RowCount = 3, AutoSize = true, Dock = DockStyle.Top, BackColor = Theme.Surface, Margin = new Padding(0, 4, 0, 10) };
            adds.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            adds.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            adds.Controls.Add(AddButton("Text", "Fixed text, the same on every card", (b) => AddText()), 0, 0);
            adds.Controls.Add(AddButton("Field ▾", "Student data (name, ID, date of birth…), a custom field from any column of your list, " +
                "or Card Text (director, address…)", (b) => ShowMenu(b, FieldMenu())), 1, 0);
            adds.Controls.Add(AddButton("Image ▾", "Upload your own picture, paste one (Ctrl+V) or use a logo, stamp, signature or icon. " +
                "You can also drag picture files onto the card.", (b) => ShowMenu(b, ImageMenu())), 0, 1);
            adds.Controls.Add(AddButton("Photo", "The student's photo from the photo folder", (b) => AddPhoto()), 1, 1);
            adds.Controls.Add(AddButton("QR ▾", "Student QR, back QR (Card Text) or a QR with your own text", (b) => ShowMenu(b, QrMenu())), 0, 2);
            adds.Controls.Add(AddButton("Shape ▾", "Rectangle, circle / ellipse, triangle, curve or line", (b) => ShowMenu(b, ShapeMenu())), 1, 2);
            left.Controls.Add(adds, 0, 1);

            left.Controls.Add(SectionLabel("LAYERS  ·  TOP FIRST"), 0, 2);
            _layers = new ListBox
            {
                Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 34, IntegralHeight = false,
                BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.TextMain, Margin = new Padding(0, 4, 0, 8)
            };
            _layers.DrawItem += DrawLayerItem;
            _layers.SelectedIndexChanged += (s, e) => { if (!_syncingLayers) SelectElement(_layers.SelectedItem as CardElement); };
            _layers.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                int i = _layers.IndexFromPoint(e.Location);
                if (i >= 0) { _layers.SelectedIndex = i; ShowElementMenu(_layers, e.Location); }
            };
            left.Controls.Add(_layers, 0, 3);

            var layerButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, BackColor = Theme.Surface, Margin = new Padding(0) };
            SmallTool(layerButtons, "▲", BringForward, "Bring forward (Ctrl+])");
            SmallTool(layerButtons, "▼", SendBackward, "Send backward (Ctrl+[)");
            SmallTool(layerButtons, "Copy", DuplicateSelected, "Duplicate (Ctrl+D)");
            SmallTool(layerButtons, "Delete", DeleteSelected, "Delete (Del)");
            left.Controls.Add(layerButtons, 0, 4);

            // ---- Right: properties ----
            var right = new Panel { Dock = DockStyle.Right, Width = 340, BackColor = Theme.Surface, Padding = new Padding(12, 12, 14, 12) };
            right.Paint += (s, e) => { using (var p = new Pen(Theme.GridLine)) e.Graphics.DrawLine(p, 0, 0, 0, right.Height); };
            _grid = new PropertyGrid
            {
                Dock = DockStyle.Fill, PropertySort = PropertySort.Categorized, ToolbarVisible = false, HelpVisible = true,
                Font = new Font(CardFonts.UI, 9.25f), BackColor = Theme.Surface, ViewBackColor = Theme.Surface,
                ViewForeColor = Theme.TextMain, ViewBorderColor = Theme.Border, LineColor = Theme.AppBack,
                CategoryForeColor = Theme.Accent, CategorySplitterColor = Theme.Border, HelpBackColor = Theme.SurfaceAlt,
                HelpForeColor = Theme.TextSoft, HelpBorderColor = Theme.Border, SelectedItemWithFocusBackColor = Theme.ButtonA,
                SelectedItemWithFocusForeColor = Color.White, DisabledItemForeColor = Theme.TextMuted
            };
            _grid.PropertyValueChanged += (s, e) => OnPropertyChanged();
            _propsTitle = new Label
            {
                Dock = DockStyle.Top, Height = 26, Font = Theme.SmallCaps, ForeColor = Theme.TextMuted, BackColor = Theme.Surface,
                TextAlign = ContentAlignment.TopLeft, UseMnemonic = false
            };
            right.Controls.Add(_grid);
            right.Controls.Add(_propsTitle);

            // ---- Centre: canvas + status line ----
            var centre = new Panel { Dock = DockStyle.Fill, BackColor = Theme.AppBack };
            _canvas = new DesignCanvas { Dock = DockStyle.Fill };
            _canvas.SelectionChanged += (s, e) => SelectElement(_canvas.Selected);
            _canvas.ElementMoving += (s, e) => { RequestRender(); UpdateStatus(); };
            _canvas.ElementMoved += (s, e) => { _grid.Refresh(); Commit(); };
            _canvas.ContextMenuRequested += (s, e) => ShowElementMenu(_canvas, _canvas.PointToClient(Cursor.Position));
            _canvas.Resize += (s, e) => RequestRender();
            _canvas.AllowDrop = true;   // drop picture files on the card
            _canvas.DragEnter += (s, e) => e.Effect = DroppedImages(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
            _canvas.DragDrop += (s, e) =>
                AddPictureFiles(DroppedImages(e.Data), _canvas.ClientToMm(_canvas.PointToClient(new Point(e.X, e.Y))));
            _status = new Label
            {
                Dock = DockStyle.Bottom, Height = 30, Font = Theme.Small, ForeColor = Theme.TextSoft, BackColor = Theme.AppBack,
                TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false, AutoEllipsis = true
            };
            centre.Controls.Add(_canvas);
            centre.Controls.Add(_status);

            // ---- Footer ----
            var footer = ModalUi.Footer();
            var saveUse = ModalUi.Button("Save & Use", PillKind.Primary, (s, e) => SaveDesign(true));
            var save = ModalUi.Button("Save", PillKind.Secondary, (s, e) => SaveDesign(false));
            var cancel = ModalUi.Button("Close", PillKind.Secondary, (s, e) => Close());
            footer.Controls.Add(saveUse);   // right-to-left flow: first added = rightmost
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            _tip.SetTip(saveUse, "Save the design and print with it from now on (Card Template → Custom)");
            _tip.SetTip(save, "Save the design (Ctrl+S); printing keeps the current template");
            CancelButton = cancel;

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(centre);
            Controls.Add(right);
            Controls.Add(left);
            Controls.Add(toolbar);
            Controls.Add(footer);

            _renderTimer.Tick += (s, e) => { _renderTimer.Stop(); RenderNow(); };

            // Column names offered for custom fields: the preview student's columns, extra (unknown) ones first.
            StudentColumnConverter.Known.Clear();
            var standard = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "StudentId", "NameKh", "NameEn", "Gender", "DateOfBirth", "Faculty", "Department", "Level", "Generation", "ValidUntil", "QrData" };
            foreach (string col in _student.Columns.Keys) if (!standard.Contains(col)) StudentColumnConverter.Known.Add(col);
            foreach (string col in _student.Columns.Keys) if (standard.Contains(col)) StudentColumnConverter.Known.Add(col);

            CustomCardDesign open = createNew ? null
                : CustomCardStore.Find(designId) ?? CustomCardStore.Find(settings.CustomDesignId) ??
                  (CustomCardStore.All().Count > 0 ? CustomCardStore.All()[0] : null);
            if (open != null) LoadDesign(CustomCardStore.Clone(open), false);
            else LoadDesign(CustomCardStore.CreateNew(false), true);
        }

        // =====================================================================
        // DESIGN LIBRARY — open, new, duplicate, rename, delete
        // =====================================================================
        /// <summary>Shows a design in the editor; its undo history starts fresh.</summary>
        private void LoadDesign(CustomCardDesign design, bool isNew)
        {
            _design = design;
            _isNew = isNew;
            _undo.Clear();
            _redo.Clear();
            _committed = _saved = CustomCardStore.Serialize(_design);
            ShowSide(false);
            UpdateTitle();
        }

        private bool IsDirty { get { return CustomCardStore.Serialize(_design) != _saved; } }

        private void UpdateTitle()
        {
            string name = string.IsNullOrWhiteSpace(_design.Name) ? Lang.T("Untitled design") : _design.Name.Trim();
            _btnDesign.Text = (name.Length > 26 ? name.Substring(0, 25) + "…" : name) + "  ▾";
            Subtitle = "“" + name + "”" + (_isNew ? "  ·  new, not saved yet" : "") +
                       "  —  move, resize and edit anything on the front and back";
        }

        /// <summary>Before leaving the open design: save, discard or stay. False = stay.</summary>
        private bool ConfirmLeave()
        {
            if (!IsDirty) return true;
            DialogResult r = AppDialog.Show(this, "Save the changes to “" + _design.Name + "” first?", Text,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
            if (r == DialogResult.Cancel) return false;
            return r == DialogResult.No || SaveDesign(false);
        }

        private void OpenDesign(string id)
        {
            if (string.Equals(id, _design.Id, StringComparison.OrdinalIgnoreCase) || !ConfirmLeave()) return;
            CustomCardDesign d = CustomCardStore.Find(id);
            if (d != null) LoadDesign(CustomCardStore.Clone(d), false);
        }

        private void NewDesign(bool starter)
        {
            if (!ConfirmLeave()) return;
            LoadDesign(CustomCardStore.CreateNew(starter), true);
            _status.Text = starter
                ? "New design “" + _design.Name + "” from the starter layout. Change anything, then click Save."
                : "New blank design “" + _design.Name + "”. Add text, fields, photo, QR, images and shapes from the left, then click Save.";
        }

        private void DuplicateDesign()
        {
            if (!ConfirmLeave()) return;
            LoadDesign(CustomCardStore.Duplicate(_design), true);
            _status.Text = "Copy created as “" + _design.Name + "”. Click Save to keep it.";
        }

        private void RenameDesign()
        {
            string name = DesignNamePrompt.Ask(this, "Rename design", "Name shown in Card Template and in this list", _design.Name);
            if (name == null || string.IsNullOrWhiteSpace(name) || name.Trim() == _design.Name) return;
            _design.Name = CustomCardStore.UniqueName(name.Trim(), _design.Id);
            UpdateTitle();
            Commit();
        }

        private void DeleteDesign()
        {
            bool inUse = !_isNew && _settings.CardTemplate == CardTemplate.Custom &&
                         string.Equals(CustomCardStore.ForSettings(_settings).Id, _design.Id, StringComparison.OrdinalIgnoreCase);
            if (!Confirm("Delete the design “" + _design.Name + "”?" +
                         (inUse ? "\n\nIt is the design printed now; printing goes back to another design." : "") +
                         "\n\nThis cannot be undone."))
                return;
            if (!_isNew)
            {
                try
                {
                    CustomCardStore.Delete(_design.Id);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    AppDialog.Show(this, "Could not delete the design:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Changed = true;
            }
            string deleted = _design.Name;
            IList<CustomCardDesign> left = CustomCardStore.All();
            if (left.Count > 0) LoadDesign(CustomCardStore.Clone(left[0]), false);
            else LoadDesign(CustomCardStore.CreateNew(false), true);
            _status.Text = "Deleted “" + deleted + "”.";
        }

        private ContextMenuStrip DesignMenu()
        {
            ContextMenuStrip m = NewMenu();
            IList<CustomCardDesign> all = CustomCardStore.All();
            m.Items.Add(Header("MY DESIGNS"));
            if (_isNew)
            {
                var current = Item("✓  " + _design.Name + "  (not saved)", () => { });
                current.Font = new Font(current.Font, FontStyle.Bold);
                m.Items.Add(current);
            }
            foreach (CustomCardDesign d in all)
            {
                string id = d.Id;
                bool open = !_isNew && string.Equals(id, _design.Id, StringComparison.OrdinalIgnoreCase);
                ToolStripMenuItem item = Item((open ? "✓  " : "     ") + d.Name, () => OpenDesign(id));
                if (open) item.Font = new Font(item.Font, FontStyle.Bold);
                m.Items.Add(item);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("Rename…", RenameDesign));
            m.Items.Add(Item("Duplicate this design", DuplicateDesign));
            m.Items.Add(Item("Delete this design…", DeleteDesign));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Header("START OVER (undo brings it back)"));
            foreach (ToolStripItem i in StartOverItems()) m.Items.Add(i);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("Import design…", ImportDesign));
            m.Items.Add(Item("Export this design…", ExportDesign));
            return m;
        }

        private ContextMenuStrip NewDesignMenu()
        {
            ContextMenuStrip m = NewMenu();
            m.Items.Add(Item("Blank card\tCtrl+N", () => NewDesign(false)));
            m.Items.Add(Item("From the starter layout", () => NewDesign(true)));
            m.Items.Add(Item("Copy of “" + _design.Name + "”", DuplicateDesign));
            return m;
        }

        // =====================================================================
        // SIDE, SELECTION, RENDERING
        // =====================================================================
        private CardSideDesign Side { get { return _back ? _design.Back : _design.Front; } }

        private void ShowSide(bool back)
        {
            _back = back;
            _tabFront.Checked = !back;
            _tabBack.Checked = back;
            _canvas.Side = Side;
            SelectElement(null);
            RefreshLayers();
            RenderNow();
        }

        private void SelectElement(CardElement element)
        {
            if (element != null && !Side.Elements.Contains(element)) element = null;
            _selected = element;
            _canvas.Selected = element;
            _grid.SelectedObject = element ?? (object)Side;
            _propsTitle.Text = element == null
                ? Lang.T(_back ? "BACK OF THE CARD" : "FRONT OF THE CARD") + Lang.T("  ·  NOTHING SELECTED")
                : Lang.F("{0} PROPERTIES", Lang.T(element.Kind).ToUpperInvariant());

            _syncingLayers = true;
            _layers.SelectedItem = element;
            if (element == null) _layers.ClearSelected();
            _syncingLayers = false;
            UpdateStatus();
        }

        private void RefreshLayers()
        {
            _syncingLayers = true;
            _layers.BeginUpdate();
            _layers.Items.Clear();
            for (int i = Side.Elements.Count - 1; i >= 0; i--) _layers.Items.Add(Side.Elements[i]);
            _layers.SelectedItem = _selected;
            _layers.EndUpdate();
            _syncingLayers = false;
        }

        /// <summary>Asks for a redraw; mouse moves are combined so the canvas never falls behind.</summary>
        private void RequestRender()
        {
            if (!_renderTimer.Enabled) _renderTimer.Start();
        }

        private void RenderNow()
        {
            RectangleF card = _canvas.CardRect;
            if (card.Width < 10f) return;
            float dpi = Math.Max(100f, Math.Min(400f, card.Width / (CardRenderer.CardWidthMm / 25.4f)));
            Bitmap bmp;
            try
            {
                bmp = CardRenderer.RenderCustomSide(Side, _back ? null : _student, _settings, dpi, true);
            }
            catch (Exception ex)
            {
                _status.Text = "Could not draw the card: " + ex.Message;
                return;
            }
            _canvas.SetImage(bmp);
        }

        private void UpdateStatus()
        {
            CardElement e = _selected;
            _btnUndo.Enabled = _undo.Count > 0;
            _btnRedo.Enabled = _redo.Count > 0;
            if (e == null)
            {
                _status.Text = "Click an element to select it  ·  drag to move, handles to resize (Shift keeps the shape)  ·  " +
                               "arrows nudge 0.1 mm (Shift 1 mm)  ·  right-click for more";
                return;
            }
            float angle = e is ShapeElement ? ((ShapeElement)e).Rotation : 0f;
            _status.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}  ·  {1}      X {2:0.0}   Y {3:0.0}   W {4:0.0}   H {5:0.0} mm{6}{7}",
                Lang.T(e.Kind), Lang.T(e.DisplayName), e.X, e.Y, e.Width, e.Height,
                angle != 0f ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "   ↻ {0:0.#}°", angle) : "",
                e.Locked ? Lang.T("   ·  locked") : "") + (_back && e is FieldElement && ((FieldElement)e).IsStudentField
                    ? Lang.T("   ·  student fields print empty on the back") : "");
        }

        // =====================================================================
        // UNDO / REDO — whole-design snapshots, taken after each finished change
        // =====================================================================
        private void Commit()
        {
            string now = CustomCardStore.Serialize(_design);
            if (now == _committed) { UpdateStatus(); return; }
            _undo.Add(_committed);
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            _redo.Clear();
            _committed = now;
            _layers.Invalidate();
            UpdateStatus();
        }

        private void Undo() { StepHistory(_undo, _redo); }
        private void Redo() { StepHistory(_redo, _undo); }

        private void StepHistory(List<string> from, List<string> to)
        {
            if (from.Count == 0) return;
            int index = _selected == null ? -1 : Side.Elements.IndexOf(_selected);
            to.Add(_committed);
            _committed = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Restore(_committed, index);
        }

        private void Restore(string xml, int selectIndex)
        {
            _design = CustomCardStore.Deserialize(xml);
            _canvas.Side = Side;
            RefreshLayers();
            SelectElement(selectIndex >= 0 && selectIndex < Side.Elements.Count ? Side.Elements[selectIndex] : null);
            RefreshLayers();
            RenderNow();
            UpdateTitle();   // undo may bring back an old name
        }

        /// <summary>Replaces the whole design (Start Over, Import) as one undoable step.</summary>
        private void ReplaceDesign(CustomCardDesign design)
        {
            _design = design;
            _canvas.Side = Side;
            SelectElement(null);
            RefreshLayers();
            RenderNow();
            Commit();
        }

        private void OnPropertyChanged()
        {
            if (_selected != null)
            {
                // Keep sizes usable whatever was typed
                _selected.Width = Math.Max(0.3f, _selected.Width);
                _selected.Height = Math.Max(0.3f, _selected.Height);
            }
            _layers.Invalidate();
            RenderNow();
            Commit();
        }

        // =====================================================================
        // ADD ELEMENTS
        // =====================================================================
        private void Add(CardElement e)
        {
            // New elements appear in the middle of the card (a little lower each time, so they do not hide each other)
            int sameSpot = Side.Elements.FindAll(x => Math.Abs(x.X + x.Width / 2f - CardRenderer.CardWidthMm / 2f) < 0.01f).Count;
            e.X = (CardRenderer.CardWidthMm - e.Width) / 2f;
            e.Y = Math.Min(CardRenderer.CardHeightMm - e.Height, (CardRenderer.CardHeightMm - e.Height) / 2f + (sameSpot % 6) * 2f);
            Side.Elements.Add(e);
            RefreshLayers();
            SelectElement(e);
            RenderNow();
            Commit();
            _canvas.Focus();
        }

        private void AddText()
        {
            Add(new TextElement { Text = "អត្ថបទថ្មី New text", Width = 40f, Height = 5f, SizePt = 8f });
        }

        private void AddField(CardField field, string label, float labelWidth, Color color, float width)
        {
            Add(new FieldElement
            {
                Field = field, Label = label, LabelWidth = labelWidth, LabelColor = CardColors.Navy, Color = color,
                SizePt = 7f, Width = width, Height = 4f
            });
        }

        private void AddImage(CardImageSource source)
        {
            var e = new ImageElement { Source = source, Width = 12f, Height = 12f };
            switch (source)
            {
                case CardImageSource.Signature: e.Width = 13f; e.Height = 5.6f; break;
                case CardImageSource.Facebook: case CardImageSource.Telegram: case CardImageSource.YouTube: case CardImageSource.TikTok:
                case CardImageSource.PinIcon: case CardImageSource.MailIcon: case CardImageSource.GlobeIcon:
                    e.Width = e.Height = 2.5f; break;
                case CardImageSource.BackQrPicture: e.RemoveWhite = false; break;
                case CardImageSource.Ornament: e.Width = 17.2f; e.Height = 5f; break;
                case CardImageSource.WatermarkPattern:
                    e.Width = CardRenderer.CardWidthMm; e.Height = CardRenderer.CardHeightMm; e.Locked = true; break;
                case CardImageSource.CustomFile:
                    UploadImages();
                    return;
            }
            Add(e);
        }

        // =====================================================================
        // YOUR OWN PICTURES — upload, drag & drop, paste
        // =====================================================================
        private const string CardImagesFolder = "CardImages";

        /// <summary>Upload image…: pick one or more pictures; each becomes an image element.</summary>
        private void UploadImages()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Upload image to the card", Multiselect = true,
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                AddPictureFiles(new List<string>(dlg.FileNames), null);
            }
        }

        /// <summary>Copies the pictures into the app's settings folder and adds them, centred on atMm (null = card centre).</summary>
        private void AddPictureFiles(List<string> files, PointF? atMm)
        {
            var failed = new List<string>();
            float offset = 0f;
            foreach (string file in files)
            {
                string stored;
                try
                {
                    stored = ImageFileEditor.Import(file, CardImagesFolder, "image_");
                }
                catch (Exception ex)
                {
                    failed.Add(Path.GetFileName(file) + ": " + ex.Message);
                    continue;
                }
                AddPicture(stored, Path.GetFileNameWithoutExtension(file), atMm, offset);
                offset += 2f;
            }
            if (failed.Count > 0)
                AppDialog.Show(this, "These files are not pictures the card can use:\n\n" + string.Join("\n", failed),
                    "Add image", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>Ctrl+V with a picture or picture files on the clipboard (e.g. a screenshot or a copied logo).</summary>
        private bool PastePictureFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsData(ElementClipboardFormat)) return false;   // an element copied here: paste that
                if (Clipboard.ContainsImage())
                {
                    using (Image img = Clipboard.GetImage())
                    {
                        if (img == null) return false;
                        AddPicture(ImageFileEditor.SavePng(img, CardImagesFolder, "pasted_"), "Pasted picture", null, 0f);
                    }
                    return true;
                }
                List<string> files = DroppedImages(Clipboard.GetDataObject());
                if (files.Count > 0) { AddPictureFiles(files, null); return true; }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException || ex is IOException ||
                                       ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                _status.Text = "Could not paste the picture: " + ex.Message;
                return true;
            }
            return false;
        }

        /// <summary>Picture files in a drag &amp; drop or clipboard data object.</summary>
        private static List<string> DroppedImages(IDataObject data)
        {
            var list = new List<string>();
            var files = data == null ? null : data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return list;
            foreach (string f in files)
                if (Array.IndexOf(ImageFileEditor.ImageExtensions, Path.GetExtension(f).ToLowerInvariant()) >= 0) list.Add(f);
            return list;
        }

        /// <summary>Adds a picture element that keeps the picture's proportions (about 20 mm on its long side).</summary>
        private void AddPicture(string storedPath, string name, PointF? atMm, float offset)
        {
            SizeF size = PictureSize(CardText.ResolveAppPath(storedPath), 20f);
            var e = new ImageElement
            {
                Source = CardImageSource.CustomFile, FilePath = storedPath, Name = name, RemoveWhite = false,
                Width = size.Width, Height = size.Height
            };
            if (!atMm.HasValue) { Add(e); return; }

            e.X = Math.Max(0f, Math.Min(CardRenderer.CardWidthMm - e.Width, atMm.Value.X - e.Width / 2f + offset));
            e.Y = Math.Max(0f, Math.Min(CardRenderer.CardHeightMm - e.Height, atMm.Value.Y - e.Height / 2f + offset));
            Side.Elements.Add(e);
            RefreshLayers();
            SelectElement(e);
            RenderNow();
            Commit();
            _canvas.Focus();
        }

        /// <summary>Element size in mm: longSideMm on the long side, the picture's own proportions.</summary>
        private static SizeF PictureSize(string path, float longSideMm)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (Image img = Image.FromStream(fs, false, false))
                {
                    if (img.Width <= 0 || img.Height <= 0) return new SizeF(longSideMm, longSideMm);
                    return img.Width >= img.Height
                        ? new SizeF(longSideMm, (float)Math.Round(longSideMm * img.Height / img.Width, 2))
                        : new SizeF((float)Math.Round(longSideMm * img.Width / img.Height, 2), longSideMm);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                return new SizeF(longSideMm, longSideMm);
            }
        }

        /// <summary>Change picture…: another file for the selected image element (its box stays; the picture fits inside).</summary>
        private void ChangePicture()
        {
            var img = _selected as ImageElement;
            if (img == null) return;
            using (var dlg = new OpenFileDialog { Title = "Change picture", Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    img.FilePath = ImageFileEditor.Import(dlg.FileName, CardImagesFolder, "image_");
                }
                catch (Exception ex)
                {
                    AppDialog.Show(this, "Could not use this file:\n\n" + ex.Message, "Change picture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                img.Source = CardImageSource.CustomFile;
                if (string.IsNullOrWhiteSpace(img.Name) || img.Name == "Pasted picture") img.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
                _grid.Refresh();
                _layers.Invalidate();
                RenderNow();
                Commit();
            }
        }

        // =====================================================================
        // CUSTOM FIELD — any column of the student list
        // =====================================================================
        private void AddCustomField()
        {
            string column, label;
            if (!CustomFieldPrompt.Ask(this, _student, out column, out label)) return;
            Add(new FieldElement
            {
                Field = CardField.Custom, Column = column, Label = label, LabelWidth = string.IsNullOrEmpty(label) ? 0f : 13.8f,
                LabelColor = CardColors.Navy, Color = CardColors.Navy, SizePt = 7f, Width = 56f, Height = 4f, Name = column
            });
        }

        private void AddPhoto()
        {
            Add(new PhotoElement { Width = CardRenderer.PhotoWidthMm, Height = CardRenderer.PhotoHeightMm });
        }

        private void AddQr(CardQrContent content)
        {
            var e = new QrElement { Content = content, Width = 12f, Height = 12f };
            if (content == CardQrContent.CustomText) e.CustomText = "https://www.npic.edu.kh";
            Add(e);
        }

        private void AddShape(CardShapeKind shape)
        {
            var e = new ShapeElement { Shape = shape, Width = 30f, Height = 10f };
            if (shape == CardShapeKind.Ellipse) { e.Width = e.Height = 10f; }
            if (shape == CardShapeKind.Triangle) { e.Width = 10f; e.Height = 9f; }
            if (shape == CardShapeKind.Curve)
            {
                // A curved line by default; set a Fill colour to make it a filled arch / corner.
                e.Height = 8f;
                e.FillColor = Color.Empty;
                e.BorderColor = CardColors.Navy;
                e.BorderMm = 0.5f;
            }
            if (shape == CardShapeKind.Line)
            {
                e.Height = 1f;
                e.FillColor = Color.Empty;
                e.BorderColor = CardColors.Navy;
                e.BorderMm = 0.3f;
            }
            Add(e);
        }

        private ContextMenuStrip FieldMenu()
        {
            ContextMenuStrip m = NewMenu();
            m.Items.Add(Header("STUDENT"));
            m.Items.Add(Item("Student ID  ·  អត្តលេខ", () => AddField(CardField.StudentId, "អត្តលេខ: ", 0f, CardColors.IdRed, 24f)));
            m.Items.Add(Item("Name (Khmer)  ·  គោត្តនាម-នាម", () => AddField(CardField.NameKhmer, "គោត្តនាម-នាម", 13.8f, CardColors.Navy, 56f)));
            m.Items.Add(Item("Name (Latin)  ·  អក្សរឡាតាំង", () => AddField(CardField.NameLatin, "អក្សរឡាតាំង", 13.8f, CardColors.Navy, 56f)));
            m.Items.Add(Item("Gender  ·  ភេទ", () => AddField(CardField.Gender, "ភេទ", 13.8f, CardColors.Navy, 40f)));
            m.Items.Add(Item("Date of birth  ·  ថ្ងៃខែឆ្នាំកំណើត", () => AddField(CardField.DateOfBirth, "ថ្ងៃខែឆ្នាំកំណើត", 13.8f, CardColors.Navy, 50f)));
            m.Items.Add(Item("Faculty  ·  មហាវិទ្យាល័យ", () => AddField(CardField.Faculty, "មហាវិទ្យាល័យ", 13.8f, CardColors.Navy, 56f)));
            m.Items.Add(Item("Level  ·  កម្រិត", () => AddField(CardField.Level, "កម្រិត", 13.8f, CardColors.Navy, 56f)));
            m.Items.Add(Item("Generation  ·  ជំនាន់ទី", () => AddField(CardField.Generation, "ជំនាន់ទី: ", 0f, CardColors.Navy, 18f)));
            m.Items.Add(Item("Expiry date  ·  ផុតកំណត់", () => AddField(CardField.ExpiryDate, "", 0f, CardColors.IdRed, 26f)));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Header("ANY COLUMN OF YOUR LIST"));
            ToolStripMenuItem custom = Item("Custom field (any column)…", AddCustomField);
            custom.Font = new Font(custom.Font, FontStyle.Bold);
            custom.ToolTipText = "Print a column the app does not know, e.g. Phone, BloodType, Department";
            m.Items.Add(custom);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Header("CARD TEXT (same on every card)"));
            m.Items.Add(Item("Issue line 1", () => AddField(CardField.IssueLine1, "", 0f, CardColors.Blue, 48f)));
            m.Items.Add(Item("Issue line 2", () => AddField(CardField.IssueLine2, "", 0f, CardColors.Blue, 44f)));
            m.Items.Add(Item("Director title", () => AddField(CardField.DirectorTitle, "", 0f, CardColors.Blue, 25f)));
            m.Items.Add(Item("Director name", () => AddField(CardField.DirectorName, "", 0f, CardColors.Navy, 20f)));
            m.Items.Add(Item("Address", () => AddField(CardField.Address, "", 0f, CardColors.Navy, 56f)));
            m.Items.Add(Item("Email", () => AddField(CardField.Email, "", 0f, CardColors.Navy, 40f)));
            m.Items.Add(Item("Website", () => AddField(CardField.Website, "", 0f, CardColors.Navy, 40f)));
            return m;
        }

        private ContextMenuStrip ImageMenu()
        {
            ContextMenuStrip m = NewMenu();
            m.Items.Add(Header("YOUR OWN PICTURE"));
            ToolStripMenuItem upload = Item("Upload image…", UploadImages);
            upload.Font = new Font(upload.Font, FontStyle.Bold);
            m.Items.Add(upload);
            bool canPaste;
            try { canPaste = Clipboard.ContainsImage() || DroppedImages(Clipboard.GetDataObject()).Count > 0; }
            catch (System.Runtime.InteropServices.ExternalException) { canPaste = false; }
            m.Items.Add(Item("Paste copied picture\tCtrl+V", () => PastePictureFromClipboard(), canPaste));
            m.Items.Add(Header("Tip: you can also drag picture files onto the card"));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Header("BUILT-IN"));
            m.Items.Add(Item("NPIC logo", () => AddImage(CardImageSource.NpicLogo)));
            m.Items.Add(Item("Ministry logo", () => AddImage(CardImageSource.MinistryLogo)));
            m.Items.Add(Item("Stamp", () => AddImage(CardImageSource.Stamp)));
            m.Items.Add(Item("Director signature (Card Text)", () => AddImage(CardImageSource.Signature)));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("Facebook", () => AddImage(CardImageSource.Facebook)));
            m.Items.Add(Item("Telegram", () => AddImage(CardImageSource.Telegram)));
            m.Items.Add(Item("YouTube", () => AddImage(CardImageSource.YouTube)));
            m.Items.Add(Item("TikTok", () => AddImage(CardImageSource.TikTok)));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("Location icon", () => AddImage(CardImageSource.PinIcon)));
            m.Items.Add(Item("Email icon", () => AddImage(CardImageSource.MailIcon)));
            m.Items.Add(Item("Website icon", () => AddImage(CardImageSource.GlobeIcon)));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("Ornament (Card Text)", () => AddImage(CardImageSource.Ornament)));
            m.Items.Add(Item("NPIC watermark pattern", () => AddImage(CardImageSource.WatermarkPattern)));
            return m;
        }

        private ContextMenuStrip QrMenu()
        {
            ContextMenuStrip m = NewMenu();
            m.Items.Add(Item("Student QR (changes per student)", () => AddQr(CardQrContent.StudentQr)));
            m.Items.Add(Item("Back QR (from Card Text)", () => AddQr(CardQrContent.BackQr)));
            m.Items.Add(Item("QR with my own text…", () => AddQr(CardQrContent.CustomText)));
            return m;
        }

        private ContextMenuStrip ShapeMenu()
        {
            ContextMenuStrip m = NewMenu();
            m.Items.Add(Item("Rectangle / band", () => AddShape(CardShapeKind.Rectangle)));
            m.Items.Add(Item("Circle / ellipse", () => AddShape(CardShapeKind.Ellipse)));
            m.Items.Add(Item("Triangle", () => AddShape(CardShapeKind.Triangle)));
            m.Items.Add(Item("Curve", () => AddShape(CardShapeKind.Curve)));
            m.Items.Add(Item("Line", () => AddShape(CardShapeKind.Line)));
            return m;
        }

        private IEnumerable<ToolStripItem> StartOverItems()
        {
            yield return Item("Replace both sides with the starter layout", () =>
            {
                if (!Confirm("Replace both sides of “" + _design.Name + "” with the starter layout?\n\nUndo (Ctrl+Z) brings your design back.")) return;
                CustomCardDesign starter = CustomCardStore.CreateStarter();
                starter.Id = _design.Id;
                starter.Name = _design.Name;
                starter.Modified = _design.Modified;
                ReplaceDesign(starter);
            });
            yield return Item("Empty the " + (_back ? "back" : "front"), () =>
            {
                if (!Confirm("Remove everything from the " + (_back ? "back" : "front") + "?\n\nUndo (Ctrl+Z) brings it back.")) return;
                Side.Elements.Clear();
                Side.BackgroundColor = Color.White;
                SelectElement(null);
                RefreshLayers();
                RenderNow();
                Commit();
            });
        }

        // =====================================================================
        // EDIT COMMANDS
        // =====================================================================
        private void DeleteSelected()
        {
            if (_selected == null) return;
            int i = Side.Elements.IndexOf(_selected);
            Side.Elements.Remove(_selected);
            RefreshLayers();
            SelectElement(Side.Elements.Count == 0 ? null : Side.Elements[Math.Min(i, Side.Elements.Count - 1)]);
            RenderNow();
            Commit();
        }

        private void DuplicateSelected()
        {
            if (_selected == null) return;
            PasteElement(_selected.Clone(), 1.5f);
        }

        /// <summary>Marks the Windows clipboard as "an element copied in Card Designer", so Ctrl+V pastes the element, not a picture.</summary>
        private const string ElementClipboardFormat = "NPIC_CARD_APP.CardElement";

        private void CopySelected()
        {
            if (_selected == null) return;
            _clipboard = _selected.Clone();
            try { Clipboard.SetData(ElementClipboardFormat, "1"); }
            catch (System.Runtime.InteropServices.ExternalException) { }   // clipboard busy: Ctrl+V still pastes the element
        }

        private void Paste()
        {
            if (_clipboard != null) PasteElement(_clipboard.Clone(), Side.Elements.Exists(e => e.Bounds == _clipboard.Bounds) ? 1.5f : 0f);
        }

        private void PasteElement(CardElement copy, float offset)
        {
            copy.X = Math.Min(copy.X + offset, CardRenderer.CardWidthMm - 1f);
            copy.Y = Math.Min(copy.Y + offset, CardRenderer.CardHeightMm - 1f);
            copy.Locked = false;
            int at = _selected == null ? Side.Elements.Count : Side.Elements.IndexOf(_selected) + 1;
            Side.Elements.Insert(at, copy);
            RefreshLayers();
            SelectElement(copy);
            RenderNow();
            Commit();
        }

        private void BringForward() { MoveLayer(1); }
        private void SendBackward() { MoveLayer(-1); }

        /// <summary>+1 / -1 = one step up / down; int.MaxValue / int.MinValue = to the top / bottom.</summary>
        private void MoveLayer(int step)
        {
            if (_selected == null) return;
            List<CardElement> list = Side.Elements;
            int from = list.IndexOf(_selected);
            int to = step == int.MaxValue ? list.Count - 1 : step == int.MinValue ? 0 : from + step;
            to = Math.Max(0, Math.Min(list.Count - 1, to));
            if (to == from) return;
            list.RemoveAt(from);
            list.Insert(to, _selected);
            RefreshLayers();
            RenderNow();
            Commit();
        }

        private void Nudge(float dx, float dy)
        {
            if (_selected == null) return;
            _selected.X = (float)Math.Round(_selected.X + dx, 2);
            _selected.Y = (float)Math.Round(_selected.Y + dy, 2);
            _grid.Refresh();
            RenderNow();
            Commit();
        }

        private void ToggleLock()
        {
            if (_selected == null) return;
            _selected.Locked = !_selected.Locked;
            _grid.Refresh();
            _canvas.Invalidate();
            Commit();
        }

        private void ShowElementMenu(Control owner, Point at)
        {
            ContextMenuStrip m = NewMenu();
            bool has = _selected != null;
            m.Items.Add(Item("Bring to front", () => MoveLayer(int.MaxValue), has));
            m.Items.Add(Item("Bring forward\tCtrl+]", BringForward, has));
            m.Items.Add(Item("Send backward\tCtrl+[", SendBackward, has));
            m.Items.Add(Item("Send to back", () => MoveLayer(int.MinValue), has));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("Duplicate\tCtrl+D", DuplicateSelected, has));
            m.Items.Add(Item("Copy\tCtrl+C", CopySelected, has));
            m.Items.Add(Item("Paste\tCtrl+V", Paste, _clipboard != null));
            if (_selected is ImageElement) m.Items.Add(Item("Change picture…", ChangePicture));
            m.Items.Add(Item(has && _selected.Locked ? "Unlock" : "Lock", ToggleLock, has));
            m.Items.Add(Item("Centre horizontally", () => Centre(true, false), has));
            m.Items.Add(Item("Centre vertically", () => Centre(false, true), has));
            var shape = _selected as ShapeElement;
            if (shape != null)
            {
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(Item("Rotate 90° right", () => RotateSelected(90f)));
                m.Items.Add(Item("Rotate 90° left", () => RotateSelected(-90f)));
                m.Items.Add(Item("Reset rotation", () => RotateSelected(0f), shape.Rotation != 0f));
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item("Delete\tDel", DeleteSelected, has));
            m.Show(owner, at);
        }

        /// <summary>Turns the selected shape by <paramref name="delta"/> degrees clockwise; 0 = back to upright.</summary>
        private void RotateSelected(float delta)
        {
            var shape = _selected as ShapeElement;
            if (shape == null) return;
            shape.Rotation = delta == 0f ? 0f : shape.Rotation + delta;
            _grid.Refresh();
            _canvas.Invalidate();
            RenderNow();
            Commit();
        }

        private void Centre(bool horizontal, bool vertical)
        {
            if (_selected == null) return;
            if (horizontal) _selected.X = (float)Math.Round((CardRenderer.CardWidthMm - _selected.Width) / 2f, 2);
            if (vertical) _selected.Y = (float)Math.Round((CardRenderer.CardHeightMm - _selected.Height) / 2f, 2);
            _grid.Refresh();
            RenderNow();
            Commit();
        }

        private void ToggleSnap()
        {
            _btnSnap.Checked = !_btnSnap.Checked;
            _canvas.Snap = _btnSnap.Checked;
        }

        private void ToggleOutlines()
        {
            _btnOutlines.Checked = !_btnOutlines.Checked;
            _canvas.ShowOutlines = _btnOutlines.Checked;
        }

        // =====================================================================
        // SAVE / IMPORT / EXPORT / CLOSE
        // =====================================================================
        /// <summary>Saves the open design; false when it could not be written.</summary>
        private bool SaveDesign(bool use)
        {
            try
            {
                CustomCardStore.Save(_design);   // gives a new design its id
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                AppDialog.Show(this, "Could not save the design:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            _saved = _committed = CustomCardStore.Serialize(_design);
            _isNew = false;
            Changed = true;
            UpdateTitle();
            if (use)
            {
                UseDesign = true;
                DialogResult = DialogResult.OK;   // closes
                return true;
            }
            _status.Text = "“" + _design.Name + "” saved.  Pick it in Card Template… (or click Save & Use) to print with it.";
            return true;
        }

        /// <summary>Opens a design file as a new design (saved designs are not touched until Save).</summary>
        private void ImportDesign()
        {
            using (var dlg = new OpenFileDialog { Title = "Import card design", Filter = "Card design (*.xml)|*.xml|All files (*.*)|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || !ConfirmLeave()) return;
                try
                {
                    CustomCardDesign imported = CustomCardStore.ReadFile(dlg.FileName);
                    imported.Id = "";
                    imported.Name = CustomCardStore.UniqueName(string.IsNullOrWhiteSpace(imported.Name) || imported.Name == "Custom card"
                        ? Path.GetFileNameWithoutExtension(dlg.FileName) : imported.Name);
                    LoadDesign(imported, true);
                    _status.Text = "Imported " + Path.GetFileName(dlg.FileName) + " as “" + _design.Name + "”. Click Save to keep it.";
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    AppDialog.Show(this, ex.Message, "Import card design", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void ExportDesign()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Export card design", Filter = "Card design (*.xml)|*.xml", FileName = SafeFileName(_design.Name) + ".xml", OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    CustomCardStore.WriteFile(dlg.FileName, _design);
                    _status.Text = "Exported to " + dlg.FileName +
                                   ". Pictures chosen with From file… stay on this PC: copy them along if you move the design.";
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    AppDialog.Show(this, "Could not export the design:\n\n" + ex.Message, "Export card design", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!UseDesign && e.CloseReason == CloseReason.UserClosing && IsDirty &&
                !Confirm("Close without saving?\n\nYour changes to “" + _design.Name + "” will be lost."))
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // While a value is being typed in the property grid, its text box gets the keys (Ctrl+Z, Del, arrows…).
            bool typing = _grid.ContainsFocus;
            switch (keyData)
            {
                case Keys.Control | Keys.S: SaveDesign(false); return true;
                case Keys.Control | Keys.N: NewDesign(false); return true;
            }
            if (!typing)
            {
                switch (keyData)
                {
                    case Keys.Control | Keys.Z: Undo(); return true;
                    case Keys.Control | Keys.Y:
                    case Keys.Control | Keys.Shift | Keys.Z: Redo(); return true;
                    case Keys.Control | Keys.D: DuplicateSelected(); return true;
                    case Keys.Control | Keys.C: CopySelected(); return true;
                    case Keys.Control | Keys.V: if (!PastePictureFromClipboard()) Paste(); return true;
                    case Keys.Control | Keys.OemCloseBrackets: BringForward(); return true;
                    case Keys.Control | Keys.OemOpenBrackets: SendBackward(); return true;
                    case Keys.Delete: DeleteSelected(); return true;
                }
                if (_canvas.Focused && _selected != null)
                {
                    float step = (keyData & Keys.Shift) != 0 ? 1f : 0.1f;
                    switch (keyData & ~Keys.Shift)
                    {
                        case Keys.Left: Nudge(-step, 0f); return true;
                        case Keys.Right: Nudge(step, 0f); return true;
                        case Keys.Up: Nudge(0f, -step); return true;
                        case Keys.Down: Nudge(0f, step); return true;
                    }
                }
                if (keyData == Keys.Escape && _selected != null && (_canvas.Focused || _layers.Focused))
                {
                    SelectElement(null);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            NarrowNameColumn(130);
            RenderNow();
            _canvas.Focus();
        }

        /// <summary>The grid splits 50/50 by default; values need the room. No public API, so via reflection (cosmetic only).</summary>
        private void NarrowNameColumn(int width)
        {
            try
            {
                foreach (Control c in _grid.Controls)
                {
                    if (c.GetType().Name != "PropertyGridView") continue;
                    MethodInfo move = c.GetType().GetMethod("MoveSplitterTo", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (move != null) move.Invoke(c, new object[] { width });
                }
            }
            catch (Exception)
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderTimer.Dispose();
                _tip.Dispose();
                if (_lastMenu != null) _lastMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        // =====================================================================
        // SMALL UI HELPERS
        // =====================================================================
        private static string SafeFileName(string name)
        {
            string s = string.IsNullOrWhiteSpace(name) ? "card-design" : name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private bool Confirm(string text)
        {
            return AppDialog.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private PillButton Tool(Control parent, string text, Action action, string tip)
        {
            PillButton b = ModalUi.ToolButton(text, action == null ? null : (EventHandler)((s, e) => action()));
            parent.Controls.Add(b);
            _tip.SetTip(b, tip);
            return b;
        }

        private void SmallTool(Control parent, string text, Action action, string tip)
        {
            PillButton b = Tool(parent, text, action, tip);
            b.MinimumSize = new Size(0, 30);
            b.Padding = new Padding(6, 0, 6, 0);
            b.Margin = new Padding(0, 0, 4, 4);
        }

        private PillButton AddButton(string text, string tip, Action<PillButton> action)
        {
            var b = new PillButton(PillKind.Secondary)
            {
                Text = text, Dock = DockStyle.Fill, Height = 34, Margin = new Padding(0, 0, 6, 6), MinimumSize = new Size(0, 34)
            };
            b.Click += (s, e) => action(b);
            _tip.SetTip(b, tip);
            return b;
        }

        private static Label SectionLabel(string text)
        {
            return new Label
            {
                Text = text, Font = Theme.SmallCaps, ForeColor = Theme.TextMuted, BackColor = Theme.Surface, AutoSize = true,
                Margin = new Padding(0, 4, 0, 2)
            };
        }

        private ContextMenuStrip _lastMenu;

        /// <summary>
        /// New one-shot menu. The previous one is disposed only now: a menu command may still be running (e.g. a file dialog)
        /// after its menu has closed.
        /// </summary>
        private ContextMenuStrip NewMenu()
        {
            if (_lastMenu != null) _lastMenu.Dispose();
            _lastMenu = ThemedMenu.Create();
            Lang.Hook(_lastMenu);   // Khmer only: items are translated when the menu opens
            return _lastMenu;
        }

        private static ToolStripMenuItem Item(string text, Action action, bool enabled = true)
        {
            return ThemedMenu.Item(text, action, enabled);
        }

        private static ToolStripLabel Header(string text)
        {
            return ThemedMenu.Header(text);
        }

        private static void ShowMenu(Control under, ContextMenuStrip menu)
        {
            menu.Show(under, new Point(0, under.Height + 2));
        }

        private void DrawLayerItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var el = (CardElement)_layers.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Graphics g = e.Graphics;
            using (var back = new SolidBrush(Theme.Surface)) g.FillRectangle(back, e.Bounds);
            Theme.HighQuality(g);
            var row = new RectangleF(e.Bounds.X + 1, e.Bounds.Y + 2, e.Bounds.Width - 3, e.Bounds.Height - 4);
            if (selected)
                using (GraphicsPath p = Theme.RoundRect(row, 8))
                using (var b = new SolidBrush(Theme.AccentSoft))
                    g.FillPath(b, p);

            // Kind badge
            var badge = new RectangleF(row.X + 6, row.Y + (row.Height - 18) / 2f, 44, 18);
            using (GraphicsPath p = Theme.RoundRect(badge, 9))
            using (var b = new SolidBrush(selected ? Theme.Surface : Theme.Tile))
                g.FillPath(b, p);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Theme.DrawText(g, Lang.T(el.Kind), Theme.SmallCaps, selected ? Theme.Accent : Theme.TextSoft, badge, StringAlignment.Center);

            string flags = (el.Locked ? Lang.T("  · locked") : "") + (el.Visible ? "" : Lang.T("  hidden"));
            Color fore = !el.Visible ? Theme.TextMuted : selected ? Theme.Accent : Theme.TextMain;
            TextRenderer.DrawText(g, Lang.T(el.DisplayName) + flags, _layers.Font,
                Rectangle.Round(new RectangleF(badge.Right + 8, row.Y, row.Right - badge.Right - 12, row.Height)), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
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

    }

    /// <summary>Pop-up menus in the app's light / dark colours (Card Designer, Card Template).</summary>
    internal static class ThemedMenu
    {
        public static ContextMenuStrip Create()
        {
            return new ContextMenuStrip
            {
                Font = new Font(CardFonts.UI, 9.25f), ShowImageMargin = false, BackColor = Theme.Surface, ForeColor = Theme.TextMain,
                Renderer = new ToolStripProfessionalRenderer(new MenuColors())
            };
        }

        /// <summary>Menu command; "Text\tCtrl+D" shows the part after the tab as its shortcut.</summary>
        public static ToolStripMenuItem Item(string text, Action action, bool enabled = true)
        {
            string shortcut = null;
            int tab = text.IndexOf('\t');
            if (tab >= 0) { shortcut = text.Substring(tab + 1); text = text.Substring(0, tab); }
            var item = new ToolStripMenuItem(text) { Enabled = enabled, ForeColor = Theme.TextMain, ShortcutKeyDisplayString = shortcut };
            item.Click += (s, e) => action();
            return item;
        }

        public static ToolStripLabel Header(string text)
        {
            return new ToolStripLabel(text) { Font = Theme.SmallCaps, ForeColor = Theme.TextMuted, Margin = new Padding(6, 6, 0, 2) };
        }

        private sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Theme.AccentSoft; } }
            public override Color MenuItemSelected { get { return Theme.AccentSoft; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.AccentSoft; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.AccentSoft; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Surface; } }
        }
    }

    /// <summary>
    /// Custom field…: choose the column of the student list to print (any header, also ones the app does not know) and an
    /// optional label. Shows the value of the preview student, so the right column is easy to spot.
    /// </summary>
    internal sealed class CustomFieldPrompt : ModalForm
    {
        private readonly ComboBox _column;
        private readonly TextBox _label;
        private readonly Label _sample;
        private readonly Student _student;
        private bool _labelEdited;

        private CustomFieldPrompt(Student student)
        {
            _student = student;
            Text = "Custom field";
            Subtitle = "Print any column of your student list (Excel, CSV, Google Sheet, API or SQL)";
            Glyph = Theme.IcoEdit;
            Resizable = false;
            DimOwner = false;
            Size = new Size(540, 360);
            Font = new Font(CardFonts.UI, 9.75f);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(22, 14, 22, 0), BackColor = Theme.Surface
            };
            for (int i = 0; i < 6; i++) body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            body.Controls.Add(Caption("Column  (header of the column in your list — pick one or type it)"), 0, 0);
            _column = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDown, Margin = new Padding(0, 2, 0, 4) };
            foreach (string c in StudentColumnConverter.Known) _column.Items.Add(c);
            body.Controls.Add(_column, 0, 1);

            _sample = new Label
            {
                Dock = DockStyle.Top, Height = 22, ForeColor = Theme.TextSoft, BackColor = Theme.Surface, UseMnemonic = false,
                Margin = new Padding(0, 0, 0, 10), AutoEllipsis = true
            };
            body.Controls.Add(_sample, 0, 2);

            body.Controls.Add(Caption("Label printed in front  (optional, e.g. លេខទូរស័ព្ទ)"), 0, 3);
            _label = new TextBox { Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 2, 0, 4) };
            _label.TextChanged += (s, e) => { if (_label.Focused) _labelEdited = true; };
            body.Controls.Add(_label, 0, 4);

            var footer = ModalUi.Footer();
            Button ok = ModalUi.Button("Add Field", PillKind.Primary, (s, e) =>
            {
                if (_column.Text.Trim().Length == 0)
                {
                    AppDialog.Show(this, "Type or pick the column to print.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _column.Focus();
                    return;
                }
                DialogResult = DialogResult.OK;
            });
            Button cancel = ModalUi.Button("Cancel", PillKind.Secondary, (s, e) => DialogResult = DialogResult.Cancel);
            footer.Controls.Add(ok);
            footer.Controls.Add(cancel);

            Controls.Add(body);
            Controls.Add(footer);
            AcceptButton = ok;
            CancelButton = cancel;

            _column.TextChanged += (s, e) => ColumnChanged();
            if (_column.Items.Count > 0) _column.SelectedIndex = 0;
            ColumnChanged();
        }

        private Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Theme.TextMain, BackColor = Theme.Surface, Margin = new Padding(0, 4, 0, 0) };
        }

        private void ColumnChanged()
        {
            string col = _column.Text.Trim();
            if (col.Length == 0)
                _sample.Text = StudentColumnConverter.Known.Count == 0
                    ? "No student list loaded: type the column header exactly as it is in your list."
                    : "";
            else if (_student.Columns.ContainsKey(col))
            {
                string v = _student.Column(col);
                _sample.Text = "Example (" + (_student.StudentId ?? "") + "):  " + (v.Length == 0 ? "(empty)" : v);
            }
            else
                _sample.Text = StudentColumnConverter.Known.Count == 0
                    ? "Shown as [" + col + "] in the designer until a list with this column is loaded."
                    : "The loaded list has no column “" + col + "”: it prints empty until a list with it is loaded.";
            if (!_labelEdited) _label.Text = col;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _column.Focus();
            _column.SelectAll();
        }

        /// <summary>False when cancelled.</summary>
        public static bool Ask(IWin32Window owner, Student student, out string column, out string label)
        {
            using (var form = new CustomFieldPrompt(student))
            {
                bool ok = form.ShowDialog(owner) == DialogResult.OK;
                column = form._column.Text.Trim();
                label = form._label.Text.Trim();
                return ok;
            }
        }
    }

    /// <summary>Small popup asking for a design name (Rename… in Card Designer and Card Template).</summary>
    internal sealed class DesignNamePrompt : ModalForm
    {
        private readonly TextBox _txtName;

        private DesignNamePrompt(string title, string subtitle, string initial)
        {
            Text = title;
            Subtitle = subtitle;
            Glyph = Theme.IcoEdit;
            Resizable = false;
            DimOwner = false;   // the page behind already dims the main window
            Size = new Size(480, 228);
            Font = new Font(CardFonts.UI, 9.75f);

            var label = new Label { Dock = DockStyle.Top, Height = 24, Text = "Design name", ForeColor = Theme.TextSoft, BackColor = Theme.Surface };
            _txtName = new TextBox { Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle, Text = initial ?? "", MaxLength = 80 };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 0), BackColor = Theme.Surface };
            body.Controls.Add(_txtName);
            body.Controls.Add(label);   // added last = docked first = on top

            var footer = ModalUi.Footer();
            Button ok = ModalUi.Button("OK", PillKind.Primary, (s, e) => DialogResult = DialogResult.OK);
            Button cancel = ModalUi.Button("Cancel", PillKind.Secondary, (s, e) => DialogResult = DialogResult.Cancel);
            footer.Controls.Add(ok);
            footer.Controls.Add(cancel);

            Controls.Add(body);
            Controls.Add(footer);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _txtName.Focus();
            _txtName.SelectAll();
        }

        /// <summary>The typed name, or null when cancelled.</summary>
        public static string Ask(IWin32Window owner, string title, string subtitle, string initial)
        {
            using (var form = new DesignNamePrompt(title, subtitle, initial))
                return form.ShowDialog(owner) == DialogResult.OK ? form._txtName.Text : null;
        }
    }

    /// <summary>
    /// Live card canvas of Card Designer: shows the rendered side, outlines, the selection with 8 resize handles and snap guides.
    /// Mouse: click selects, drag moves, handles resize (Shift keeps the shape). Works in mm; the form owns the design.
    /// </summary>
    internal sealed class DesignCanvas : Control
    {
        private const int Pad = 28;
        private const float HandlePx = 8f;
        private const float GridMm = 0.5f;
        private const float SafeMm = 1.5f;
        private const float RotateGapPx = 26f;   // distance of the rotate knob above the top edge
        private const int RotateHandle = 8;      // index of the rotate knob in HandlePoints

        private Bitmap _image;
        private CardElement _selected, _hover;
        private bool _snap = true, _outlines = true;

        // Drag state
        private enum Drag { None, Move, Resize, Rotate }
        private Drag _drag;
        private int _handle;
        private PointF _startMm;
        private RectangleF _startBounds;
        private bool _moved;
        private float? _guideX, _guideY;

        public event EventHandler SelectionChanged, ElementMoving, ElementMoved, ContextMenuRequested;

        public DesignCanvas()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Theme.AppBack;
            TabStop = true;
        }

        public CardSideDesign Side { get; set; }

        public CardElement Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public bool Snap { get { return _snap; } set { _snap = value; } }
        public bool ShowOutlines { get { return _outlines; } set { _outlines = value; Invalidate(); } }

        /// <summary>Takes ownership of the bitmap.</summary>
        public void SetImage(Bitmap image)
        {
            Bitmap old = _image;
            _image = image;
            if (old != null) old.Dispose();
            Invalidate();
        }

        /// <summary>Where the card is drawn, in pixels (as large as fits).</summary>
        public RectangleF CardRect
        {
            get
            {
                float ratio = CardRenderer.CardWidthMm / CardRenderer.CardHeightMm;
                float w = Math.Max(1f, Math.Min(Width - 2f * Pad, (Height - 2f * Pad) * ratio));
                float h = w / ratio;
                return new RectangleF((float)Math.Round((Width - w) / 2f), (float)Math.Round((Height - h) / 2f), (float)Math.Round(w), (float)Math.Round(h));
            }
        }

        private float PxPerMm { get { return CardRect.Width / CardRenderer.CardWidthMm; } }

        private RectangleF ToPx(RectangleF mm)
        {
            RectangleF c = CardRect;
            float s = PxPerMm;
            return new RectangleF(c.X + mm.X * s, c.Y + mm.Y * s, mm.Width * s, mm.Height * s);
        }

        /// <summary>A point on the canvas (client pixels) in card millimetres.</summary>
        public PointF ClientToMm(Point px) { return ToMm(px); }

        private PointF ToMm(Point px)
        {
            RectangleF c = CardRect;
            float s = PxPerMm;
            return new PointF((px.X - c.X) / s, (px.Y - c.Y) / s);
        }

        // ---- Rotation helpers (only shapes rotate) ----

        private static float AngleOf(CardElement e)
        {
            var shape = e as ShapeElement;
            return shape == null ? 0f : shape.Rotation;
        }

        private static PointF Centre(RectangleF r) { return new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f); }

        /// <summary>Turns p clockwise around c, the same way the renderer turns the shape.</summary>
        private static PointF Turn(PointF p, PointF c, float degrees)
        {
            if (degrees == 0f) return p;
            double a = degrees * Math.PI / 180.0, cos = Math.Cos(a), sin = Math.Sin(a);
            float dx = p.X - c.X, dy = p.Y - c.Y;
            return new PointF(c.X + (float)(dx * cos - dy * sin), c.Y + (float)(dx * sin + dy * cos));
        }

        /// <summary>Outline of the element's box on the canvas, turned with the shape.</summary>
        private void DrawBox(Graphics g, Pen pen, CardElement e)
        {
            RectangleF r = ToPx(e.Bounds);
            float a = AngleOf(e);
            if (a == 0f) { g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height); return; }
            PointF c = Centre(r);
            g.DrawPolygon(pen, new[]
            {
                Turn(new PointF(r.Left, r.Top), c, a), Turn(new PointF(r.Right, r.Top), c, a),
                Turn(new PointF(r.Right, r.Bottom), c, a), Turn(new PointF(r.Left, r.Bottom), c, a)
            });
        }

        /// <summary>Is p inside the element's (turned) box, grown by the given pixels on each side?</summary>
        private bool HitBox(CardElement e, PointF p, float growX, float growY)
        {
            RectangleF r = ToPx(e.Bounds);
            PointF local = Turn(p, Centre(r), -AngleOf(e));
            r.Inflate(growX, growY);
            return r.Contains(local);
        }

        // ---- Painting ----
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.AppBack);
            Theme.HighQuality(g);
            RectangleF card = CardRect;
            float radius = card.Width * 3.18f / CardRenderer.CardWidthMm;

            var shadow = card;
            shadow.Offset(0f, 3f);
            using (GraphicsPath sp = Theme.RoundRect(shadow, radius))
            using (var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 80 : 30, Color.Black)))
                g.FillPath(b, sp);

            using (GraphicsPath cp = Theme.RoundRect(card, radius))
            {
                Region oldClip = g.Clip;
                g.SetClip(cp, CombineMode.Intersect);
                g.FillRectangle(Brushes.White, card);
                if (_image != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(_image, card);
                }
                g.Clip = oldClip;
                using (var pen = new Pen(Color.FromArgb(60, Theme.Dark ? Color.White : Color.Black))) g.DrawPath(pen, cp);
            }

            if (Side != null && _outlines)
            {
                // Safe margin: keep text inside it, the printer may shift the card a little
                RectangleF safe = ToPx(new RectangleF(SafeMm, SafeMm, CardRenderer.CardWidthMm - 2 * SafeMm, CardRenderer.CardHeightMm - 2 * SafeMm));
                using (var pen = new Pen(Color.FromArgb(70, 0, 150, 200)) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, safe.X, safe.Y, safe.Width, safe.Height);
                using (var pen = new Pen(Color.FromArgb(90, 120, 120, 140)) { DashStyle = DashStyle.Dot })
                    foreach (CardElement el in Side.Elements)
                        if (el != _selected && el.Visible)
                            DrawBox(g, pen, el);
            }

            if (Side != null && Side.Elements.Count == 0)
            {
                // Blank side: say how to start (never printed — the canvas only)
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                var hint = new RectangleF(card.X + 20, card.Y + card.Height / 2f - 30, card.Width - 40, 60);
                Theme.DrawText(g, Lang.T("Blank card"), Theme.CardTitle, Color.FromArgb(150, 150, 165),
                    new RectangleF(hint.X, hint.Y, hint.Width, 28), StringAlignment.Center);
                Theme.DrawText(g, Lang.T("Add Text, Field, Image, Photo, QR or Shape from the panel on the left"), Theme.Small,
                    Color.FromArgb(150, 150, 165), new RectangleF(hint.X, hint.Y + 30, hint.Width, 22), StringAlignment.Center);
            }

            if (_hover != null && _hover != _selected && Side != null && Side.Elements.Contains(_hover))
            {
                using (var pen = new Pen(Color.FromArgb(150, Theme.Accent), 1.2f)) DrawBox(g, pen, _hover);
            }

            // Snap guides while dragging
            using (var guide = new Pen(Color.FromArgb(230, 40, 160), 1f) { DashStyle = DashStyle.Dash })
            {
                if (_guideX.HasValue) { float x = card.X + _guideX.Value * PxPerMm; g.DrawLine(guide, x, card.Y - 10, x, card.Bottom + 10); }
                if (_guideY.HasValue) { float y = card.Y + _guideY.Value * PxPerMm; g.DrawLine(guide, card.X - 10, y, card.Right + 10, y); }
            }

            if (_selected != null)
            {
                using (var pen = new Pen(Theme.Accent, 1.6f) { DashStyle = _selected.Locked ? DashStyle.Dash : DashStyle.Solid })
                    DrawBox(g, pen, _selected);
                if (!_selected.Locked)
                {
                    PointF[] hs = HandlePoints(_selected);
                    using (var fill = new SolidBrush(Color.White))
                    using (var pen = new Pen(Theme.Accent, 1.4f))
                    {
                        if (hs.Length > RotateHandle)
                        {
                            // Rotate knob: a round handle on a stem above the top edge
                            PointF knob = hs[RotateHandle];
                            using (var stem = new Pen(Theme.Accent, 1.2f)) g.DrawLine(stem, hs[1], knob);
                            RectangleF k = HandleRect(knob);
                            k.Inflate(1.5f, 1.5f);
                            using (var knobFill = new SolidBrush(_drag == Drag.Rotate ? Theme.Accent : Color.White))
                                g.FillEllipse(knobFill, k);
                            g.DrawEllipse(pen, k);
                        }
                        for (int i = 0; i < Math.Min(8, hs.Length); i++)
                        {
                            RectangleF h = HandleRect(hs[i]);
                            g.FillRectangle(fill, h);
                            g.DrawRectangle(pen, h.X, h.Y, h.Width, h.Height);
                        }
                    }
                    if (_drag == Drag.Rotate && hs.Length > RotateHandle) DrawAngleBadge(g, hs[RotateHandle], AngleOf(_selected));
                }
            }

            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Theme.Border) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, 2, 2, Width - 5, Height - 5);
        }

        /// <summary>Handle centres, turned with the shape: TL, T, TR, R, BR, B, BL, L, then the rotate knob (shapes only).</summary>
        private PointF[] HandlePoints(CardElement e)
        {
            RectangleF r = ToPx(e.Bounds);
            PointF c = Centre(r);
            var pts = new List<PointF>
            {
                new PointF(r.Left, r.Top), new PointF(c.X, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, c.Y),
                new PointF(r.Right, r.Bottom), new PointF(c.X, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, c.Y)
            };
            if (e is ShapeElement) pts.Add(new PointF(c.X, r.Top - RotateGapPx));
            float a = AngleOf(e);
            for (int i = 0; i < pts.Count; i++) pts[i] = Turn(pts[i], c, a);
            return pts.ToArray();
        }

        private static RectangleF HandleRect(PointF centre)
        {
            return new RectangleF(centre.X - HandlePx / 2f, centre.Y - HandlePx / 2f, HandlePx, HandlePx);
        }

        private int HandleAt(Point p)
        {
            if (_selected == null || _selected.Locked) return -1;
            PointF[] hs = HandlePoints(_selected);
            for (int i = hs.Length - 1; i >= 0; i--)   // the rotate knob first
            {
                RectangleF h = HandleRect(hs[i]);
                h.Inflate(3f, 3f);
                if (h.Contains(p)) return i;
            }
            return -1;
        }

        /// <summary>"45°" pill next to the rotate knob while rotating.</summary>
        private void DrawAngleBadge(Graphics g, PointF knob, float degrees)
        {
            string text = degrees.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "°";
            SizeF size = g.MeasureString(text, Theme.Small);
            var box = new RectangleF(knob.X + 12f, knob.Y - size.Height / 2f - 3f, size.Width + 10f, size.Height + 6f);
            if (box.Right > Width - 4) box.X = knob.X - 12f - box.Width;
            using (GraphicsPath p = Theme.RoundRect(box, box.Height / 2f))
            using (var b = new SolidBrush(Theme.Accent))
                g.FillPath(b, p);
            using (var b = new SolidBrush(Color.White))
                g.DrawString(text, Theme.Small, b, box.X + 5f, box.Y + 3f);
        }

        /// <summary>Points the shape's top at the mouse. Shift = 15° steps; with Snap on, 0 / 90 / 180 / 270° pull within 3°.</summary>
        private void RotateTowards(Point p, bool steps)
        {
            var shape = _selected as ShapeElement;
            if (shape == null) return;
            PointF c = Centre(ToPx(shape.Bounds));
            float vx = p.X - c.X, vy = p.Y - c.Y;
            if (vx * vx + vy * vy < 9f) return;   // too close to the centre to tell a direction
            double deg = Math.Atan2(vx, -vy) * 180.0 / Math.PI;
            if (steps) deg = Math.Round(deg / 15.0) * 15.0;
            else
            {
                deg = Math.Round(deg);
                double right = Math.Round(deg / 90.0) * 90.0;
                if (_snap && Math.Abs(deg - right) <= 3.0) deg = right;
            }
            shape.Rotation = (float)deg;
        }

        /// <summary>Top-most visible, unlocked element under the point (a tiny element gets a few extra pixels).</summary>
        private CardElement ElementAt(Point p)
        {
            if (Side == null) return null;
            for (int i = Side.Elements.Count - 1; i >= 0; i--)
            {
                CardElement el = Side.Elements[i];
                if (!el.Visible || el.Locked) continue;
                RectangleF r = ToPx(el.Bounds);
                if (HitBox(el, p, Math.Max(0f, 6f - r.Width) / 2f + 1f, Math.Max(0f, 6f - r.Height) / 2f + 1f)) return el;
            }
            return null;
        }

        // ---- Mouse ----
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
            int handle = e.Button == MouseButtons.Left ? HandleAt(e.Location) : -1;
            if (handle < 0)
            {
                CardElement hit = ElementAt(e.Location);
                // Clicking inside the selected (maybe locked) element keeps it selected
                if (hit == null && _selected != null && HitBox(_selected, e.Location, 0f, 0f)) hit = _selected;
                if (hit != _selected)
                {
                    _selected = hit;
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                    Invalidate();
                }
            }

            if (e.Button == MouseButtons.Right)
            {
                ContextMenuRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (e.Button != MouseButtons.Left || _selected == null || _selected.Locked) return;

            _drag = handle == RotateHandle ? Drag.Rotate : handle >= 0 ? Drag.Resize : Drag.Move;
            _handle = handle;
            _startMm = ToMm(e.Location);
            _startBounds = _selected.Bounds;
            _moved = false;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag == Drag.None)
            {
                int h = HandleAt(e.Location);
                Cursor = h == RotateHandle ? Cursors.Hand
                    : h >= 0 ? HandleCursor(h, AngleOf(_selected))
                    : ElementAt(e.Location) != null ? Cursors.SizeAll : Cursors.Default;
                CardElement hover = ElementAt(e.Location);
                if (hover != _hover) { _hover = hover; Invalidate(); }
                return;
            }

            PointF now = ToMm(e.Location);
            float dx = now.X - _startMm.X, dy = now.Y - _startMm.Y;
            if (!_moved && Math.Abs(dx) * PxPerMm < 2f && Math.Abs(dy) * PxPerMm < 2f) return;   // ignore click jitter
            _moved = true;
            bool shift = (ModifierKeys & Keys.Shift) != 0;

            if (_drag == Drag.Rotate)
            {
                RotateTowards(e.Location, shift);
                Invalidate();
                ElementMoving?.Invoke(this, EventArgs.Empty);
                return;
            }

            RectangleF r = _drag == Drag.Move ? MoveRect(dx, dy) : ResizeTurned(dx, dy, shift);
            _selected.Bounds = new RectangleF((float)Math.Round(r.X, 2), (float)Math.Round(r.Y, 2), (float)Math.Round(r.Width, 2), (float)Math.Round(r.Height, 2));
            Invalidate();
            ElementMoving?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag == Drag.None) return;
            _drag = Drag.None;
            Capture = false;
            _guideX = _guideY = null;
            Invalidate();
            if (_moved) ElementMoved?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != null) { _hover = null; Invalidate(); }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & ~Keys.Shift)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        /// <summary>Resize cursor of a handle, following the shape's rotation in 45° steps.</summary>
        private static Cursor HandleCursor(int h, float degrees)
        {
            switch (((h + (int)Math.Round(degrees / 45f)) % 8 + 8) % 8)
            {
                case 0: case 4: return Cursors.SizeNWSE;
                case 2: case 6: return Cursors.SizeNESW;
                case 1: case 5: return Cursors.SizeNS;
                default: return Cursors.SizeWE;
            }
        }

        // ---- Snapping ----
        private RectangleF MoveRect(float dx, float dy)
        {
            var r = new RectangleF(_startBounds.X + dx, _startBounds.Y + dy, _startBounds.Width, _startBounds.Height);
            _guideX = _guideY = null;
            if (!_snap) return r;

            float tol = 6f / PxPerMm;   // 6 px
            float W = CardRenderer.CardWidthMm, H = CardRenderer.CardHeightMm;
            float snapped;
            if (SnapEdges(r.X, r.Width, new[] { 0f, SafeMm, W / 2f, W - SafeMm, W }, tol, out snapped, out _guideX)) r.X = snapped;
            else r.X = Round(r.X);
            if (SnapEdges(r.Y, r.Height, new[] { 0f, SafeMm, H / 2f, H - SafeMm, H }, tol, out snapped, out _guideY)) r.Y = snapped;
            else r.Y = Round(r.Y);
            return r;
        }

        /// <summary>Snaps the start, centre or end of a span to the nearest target line within tol.</summary>
        private static bool SnapEdges(float start, float size, float[] targets, float tol, out float newStart, out float? guide)
        {
            newStart = start;
            guide = null;
            float best = tol;
            float[] offsets = { 0f, size / 2f, size };
            foreach (float t in targets)
                foreach (float o in offsets)
                {
                    float d = Math.Abs(start + o - t);
                    if (d < best) { best = d; newStart = t - o; guide = t; }
                }
            return guide.HasValue;
        }

        private float Round(float mm) { return _snap ? (float)Math.Round(mm / GridMm) * GridMm : mm; }

        /// <summary>
        /// Resize along the shape's own (turned) sides: the mouse move is turned back into the shape's frame,
        /// and the box is shifted so the opposite handle stays where it is on the card.
        /// </summary>
        private RectangleF ResizeTurned(float dx, float dy, bool keepRatio)
        {
            float a = AngleOf(_selected);
            if (a == 0f) return ResizeRect(dx, dy, keepRatio, true);
            PointF local = Turn(new PointF(dx, dy), PointF.Empty, -a);
            RectangleF n = ResizeRect(local.X, local.Y, keepRatio, false);   // the mm grid does not apply to turned sides
            PointF centre = Turn(Centre(n), Centre(_startBounds), a);
            return new RectangleF(centre.X - n.Width / 2f, centre.Y - n.Height / 2f, n.Width, n.Height);
        }

        private RectangleF ResizeRect(float dx, float dy, bool keepRatio, bool grid)
        {
            float l = _startBounds.Left, t = _startBounds.Top, r = _startBounds.Right, b = _startBounds.Bottom;
            bool left = _handle == 0 || _handle == 6 || _handle == 7;
            bool right = _handle == 2 || _handle == 3 || _handle == 4;
            bool top = _handle == 0 || _handle == 1 || _handle == 2;
            bool bottom = _handle == 4 || _handle == 5 || _handle == 6;
            if (left) l = grid ? Round(l + dx) : l + dx;
            if (right) r = grid ? Round(r + dx) : r + dx;
            if (top) t = grid ? Round(t + dy) : t + dy;
            if (bottom) b = grid ? Round(b + dy) : b + dy;

            const float min = 0.5f;
            if (r - l < min) { if (left) l = r - min; else r = l + min; }
            if (b - t < min) { if (top) t = b - min; else b = t + min; }

            bool corner = (left || right) && (top || bottom);
            if (keepRatio && corner && _startBounds.Height > 0f)
            {
                float ratio = _startBounds.Width / _startBounds.Height;
                float w = r - l, h = b - t;
                if (w / ratio > h) h = w / ratio; else w = h * ratio;
                if (left) l = r - w; else r = l + w;
                if (top) t = b - h; else b = t + h;
            }
            _guideX = _guideY = null;
            return RectangleF.FromLTRB(l, t, r, b);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null) { _image.Dispose(); _image = null; }
            base.Dispose(disposing);
        }
    }
}
