using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// PDF Designer… (Ctrl+Shift+T): design a free page and export it as a PDF.
    ///   - Write text anywhere: double-click the page (or + Text) and type; the box grows with the text;
    ///   - move anything by dragging it (it snaps to the page centre and to the edges of the other objects); the side
    ///     handles change the width, the corners of a picture its size; arrow keys nudge (Shift = 5 mm);
    ///   - tables: rows and columns are edited in the cell grid on the right (paste from Excel works there), the column
    ///     lines are dragged on the page; header row, stripes, lines and padding in the property list;
    ///   - clone: Ctrl+D or Clone puts a copy next to the object; Alt+drag pulls a copy off and leaves the original;
    ///   - several pages, A4 / A5 / Letter / Legal in portrait or landscape, undo / redo, zoom (Ctrl+wheel).
    /// Designs are saved in %AppData%\NPIC_CARD_APP\PdfDesigns (<see cref="PdfDesignStore"/>) and exported by
    /// <see cref="PdfDesignRenderer.Export"/>, which draws from the same layout as the screen.
    /// </summary>
    internal sealed class PdfDesignerForm : ModalForm
    {
        private const int MaxUndo = 80;
        private const float NudgeMm = 0.5f, BigNudgeMm = 5f, CloneOffsetMm = 5f;
        private static readonly float PxPerMm100 = 96f / 25.4f;   // zoom 100% = real size on a 96-dpi screen
        private static string _lastExportFolder;

        private sealed class Snapshot
        {
            public PdfDesign Doc;
            public int Id, Page;
            public int[] Selected;
        }

        private enum AlignTo { Left, Centre, Right, Top, Middle, Bottom }

        /// <summary>Where the data fields get their values.</summary>
        private enum DataChoice { None, List, Picked, File }

        private readonly AppSettings _settings;
        private PdfDesign _doc;
        private int _page;
        private bool _isNew;   // never saved

        private readonly List<Snapshot> _undo = new List<Snapshot>(), _redo = new List<Snapshot>();
        private Snapshot _committed;       // the design as of the last finished change
        private int _savedState = -1, _stateCounter;
        private bool _pending;             // typing / nudging not committed yet (see _commitTimer)
        private PdfTextItem _newText;      // added for typing; dropped again when it is left empty

        private List<PdfItem> _clipboard;
        private uint _clipboardSeq;        // Windows clipboard sequence number right after our Copy
        private int _pasteCount;
        private bool _alignToPage;         // several objects selected: align with the page margins instead of each other

        // Data for the fields ({NameKh}…): the main window's list, its selected students, or a file imported here
        private readonly List<Student> _listRecords, _pickedRecords;
        private List<Student> _fileRecords = new List<Student>();
        private string _fileName;
        private DataChoice _data;
        private int _record;               // the record shown on the page
        private bool _showFieldNames;      // show {NameKh} instead of the record's values
        private PdfFieldContext _fields;   // what the page fills the fields with (null = field names)

        // Preview (F5): the pages as Export writes them. A data table is filled with every record and, when the list is
        // longer than the page, continues on the next pages (header repeated). Read only: F5 / Esc goes back to editing.
        private bool _preview;
        private List<List<PdfItem>> _previewPages;
        private int _previewIndex;
        private readonly List<Control> _editTools = new List<Control>();   // disabled while previewing

        private readonly PdfPageCanvas _canvas;
        private readonly PdfScrollHost _scroller;
        private readonly PropertyGrid _props;
        private readonly Label _propsTitle, _status, _pageLabel, _zoomLabel, _recordLabel;
        private readonly Panel _textPanel, _tablePanel, _side;
        private readonly TextBox _txtText;
        private readonly DataGridView _cells;
        private readonly PillButton _btnDoc, _btnPage, _btnUndo, _btnRedo, _btnPrev, _btnNext, _btnClone, _btnDelete,
                                    _btnAlign, _btnData, _btnPreview, _btnRecordPrev, _btnRecordNext, _btnTextField, _btnCellField;
        private readonly ContextMenuStrip _editorMenu;
        private readonly ToolTip _tip = new ToolTip();
        private readonly System.Windows.Forms.Timer _commitTimer = new System.Windows.Forms.Timer { Interval = 700 };
        private ContextMenuStrip _menu;
        private bool _syncing;             // controls are being filled from the design: ignore their change events
        private float _zoom = 3f;          // pixels per millimetre
        private bool _fitZoom = true;

        /// <summary>The last PDF exported (null when none).</summary>
        public string ExportedPath { get; private set; }

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        /// <param name="list">Students of the main window's list: the default data for the fields.</param>
        /// <param name="picked">The students selected in the main window.</param>
        public PdfDesignerForm(AppSettings settings, IList<Student> list = null, IList<Student> picked = null)
        {
            // Khmer only: property names and descriptions in Khmer (saved files keep the English names)
            LocalizedProperties.Register(typeof(PdfTextItem), typeof(PdfTableItem), typeof(PdfImageItem), typeof(PdfPageSetup));
            _settings = settings;
            _listRecords = list == null ? new List<Student>() : list.Where(s => s != null).ToList();
            _pickedRecords = picked == null ? new List<Student>() : picked.Where(s => s != null).ToList();
            _data = _listRecords.Count > 0 ? DataChoice.List : DataChoice.None;

            Text = "PDF Designer";
            Subtitle = "Write text, add tables and pictures, move them anywhere on the page, then export a PDF";
            Glyph = Theme.IcoDocument;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1380, work.Width), Math.Min(920, work.Height));
            MinimumSize = new Size(Math.Min(980, work.Width), Math.Min(640, work.Height));
            Font = new Font(CardFonts.UI, 9f);

            // ---- Toolbar ----
            var bar = ModalUi.Toolbar(56);
            _btnDoc = Tool(bar, "Design ▾", ShowDocumentMenu, "Your PDF designs: open another one, new design, save a copy, delete", false);
            _btnDoc.MaximumSize = new Size(260, 32);
            _btnDoc.AutoEllipsis = true;
            _editTools.Add(_btnDoc);
            Divider(bar);
            _editTools.Add(Tool(bar, "+ Text", () => AddText(null), "Add a text box and type (or double-click the page where the text should go)"));
            _editTools.Add(Tool(bar, "+ Table", () => AddTable(null), "Add a table: edit its cells on the right, drag its column lines on the page"));
            _editTools.Add(Tool(bar, "+ Picture", () => AddPicture(null), "Add a logo, stamp or photo from a file (Ctrl+V pastes a copied picture)"));
            Divider(bar);
            _btnClone = Tool(bar, "Clone", CloneSelected, "Put a copy next to the selected object (Ctrl+D). Alt+drag also pulls off a copy");
            _btnDelete = Tool(bar, "Delete", DeleteSelected, "Delete the selected object (Del)");
            Divider(bar);
            _btnUndo = Tool(bar, "Undo", Undo, "Undo (Ctrl+Z)");
            _btnRedo = Tool(bar, "Redo", Redo, "Redo (Ctrl+Y)");
            Divider(bar);
            _btnPage = Tool(bar, "Page ▾", ShowPageMenu, "Add, duplicate or delete pages; paper size and orientation", false);
            _btnAlign = Tool(bar, "Align ▾", () => ShowMenuUnder(AlignMenu(), _btnAlign),
                "Line objects up with the page margins, or with each other when several are selected (Shift+click or drag a box)", false);
            _btnData = Tool(bar, "Data ▾", ShowDataMenu,
                "Fill text and tables from the student list or an Excel / CSV file: type {NameKh}, {StudentId}… or use Insert field", false);
            _btnData.MaximumSize = new Size(200, 32);
            _btnData.AutoEllipsis = true;
            _btnPreview = Tool(bar, "Preview", TogglePreview,
                "See every page as it is exported: data tables filled with all the records, a long list continuing on the next pages (F5)");
            _editTools.Add(_btnPage);
            _editTools.Add(_btnAlign);
            Divider(bar);
            PillButton zoomOut = Tool(bar, "−", () => ZoomBy(1f / 1.2f, null), "Zoom out (Ctrl+−, or Ctrl+mouse wheel)");
            zoomOut.MinimumSize = new Size(34, 32);
            _zoomLabel = new Label
            {
                AutoSize = false, Size = new Size(52, 32), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextSoft,
                BackColor = Theme.Surface, Margin = new Padding(0, 0, 6, 0), Cursor = Cursors.Hand
            };
            _zoomLabel.Click += (s, e) => FitZoom();
            _tip.SetTip(_zoomLabel, "Click to fit the page width (Ctrl+0)");
            bar.Controls.Add(_zoomLabel);
            PillButton zoomIn = Tool(bar, "+", () => ZoomBy(1.2f, null), "Zoom in (Ctrl++, or Ctrl+mouse wheel)");
            zoomIn.MinimumSize = new Size(34, 32);

            // ---- Page ----
            _scroller = new PdfScrollHost { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.AppBack };
            _canvas = new PdfPageCanvas { Location = Point.Empty };
            _scroller.Controls.Add(_canvas);
            _scroller.Resize += (s, e) => { if (_fitZoom) FitZoom(); else SizeCanvas(); };
            _canvas.SelectionChanged += (s, e) => OnSelectionChanged();
            _canvas.ItemMoving += (s, e) => UpdateStatus();
            _canvas.ItemChanged += (s, e) => { RefreshProperties(); Commit(); };
            _canvas.TextEditing += (s, e) => SyncSideText();
            _canvas.TextEditEnded += OnTextEditEnded;
            _canvas.EmptyDoubleClicked += mm => AddText(mm);
            _canvas.CellClicked += (t, r, c) => ShowCell(r, c, false);
            _canvas.CellDoubleClicked += (t, r, c) => ShowCell(r, c, true);
            _canvas.PictureDoubleClicked += ReplacePicture;
            _canvas.MenuRequested += ShowCanvasMenu;
            _canvas.ZoomRequested += (f, p) => ZoomBy(f, p);
            _canvas.PreviewDoubleClicked += (s, e) => EndPreview();

            // Right-click while typing on the page: cut / copy / paste and the data fields
            _editorMenu = ThemedMenu.Create();
            _editorMenu.Opening += (s, e) => FillEditorMenu();
            _canvas.EditorMenu = _editorMenu;

            // ---- Right: text or cells of the selection, then its properties ----
            var right = _side = new Panel { Dock = DockStyle.Right, Width = 360, BackColor = Theme.Surface, Padding = new Padding(14, 10, 12, 8) };
            right.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, 0, 0, right.Height);
            };
            _props = new PropertyGrid
            {
                Dock = DockStyle.Fill, PropertySort = PropertySort.Categorized, ToolbarVisible = false, HelpVisible = true,
                Font = new Font(CardFonts.UI, 9.25f), BackColor = Theme.Surface, ViewBackColor = Theme.Surface,
                ViewForeColor = Theme.TextMain, ViewBorderColor = Theme.Border, LineColor = Theme.AppBack,
                CategoryForeColor = Theme.Accent, CategorySplitterColor = Theme.Border, HelpBackColor = Theme.SurfaceAlt,
                HelpForeColor = Theme.TextSoft, HelpBorderColor = Theme.Border, SelectedItemWithFocusBackColor = Theme.ButtonA,
                SelectedItemWithFocusForeColor = Color.White, DisabledItemForeColor = Theme.TextMuted
            };
            _props.PropertyValueChanged += (s, e) => OnPropertyChanged();

            _textPanel = new Panel { Dock = DockStyle.Top, Height = 210, BackColor = Theme.Surface, Padding = new Padding(0, 0, 0, 12), Visible = false };
            _txtText = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain,
                Font = new Font(CardFonts.KhmerSiemreap, 11f)
            };
            _txtText.TextChanged += (s, e) => SideTextChanged();
            _txtText.Leave += (s, e) => FlushPending();
            var textButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.Surface, WrapContents = true };
            _btnTextField = SmallTool(textButtons, "Insert field ▾", () => ShowFieldMenu(_btnTextField),
                "Put a data field ({NameKh}, {StudentId}…) where the cursor is: it is filled from the data (Data ▾)", true);
            _textPanel.Controls.Add(_txtText);
            _textPanel.Controls.Add(textButtons);
            _textPanel.Controls.Add(SmallNote("Type here or double-click the text on the page. Enter = new line."));

            _tablePanel = new Panel { Dock = DockStyle.Top, Height = 320, BackColor = Theme.Surface, Padding = new Padding(0, 0, 0, 12), Visible = false };
            _cells = BuildCellGrid();
            var tableButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.Surface, WrapContents = true };
            SmallTool(tableButtons, "+ Row", TableAddRow, "Add a row under the current cell");
            SmallTool(tableButtons, "+ Column", TableAddColumn, "Add a column right of the current cell");
            SmallTool(tableButtons, "− Row", TableDeleteRow, "Delete the row of the current cell");
            SmallTool(tableButtons, "− Column", TableDeleteColumn, "Delete the column of the current cell");
            SmallTool(tableButtons, "Equal widths", TableEqualWidths, "Make every column the same width");
            _btnCellField = SmallTool(tableButtons, "Insert field ▾", () => ShowFieldMenu(_btnCellField),
                "Put a data field in the current cell. Turn on “Repeat for each record” below to list every record", true);
            _tablePanel.Controls.Add(_cells);
            _tablePanel.Controls.Add(SmallNote("Ctrl+V pastes cells copied from Excel or Word. Drag the column lines on the page."));
            _tablePanel.Controls.Add(tableButtons);

            _propsTitle = new Label
            {
                Dock = DockStyle.Top, Height = 26, Font = Theme.SmallCaps, ForeColor = Theme.TextMuted, BackColor = Theme.Surface,
                TextAlign = ContentAlignment.TopLeft, UseMnemonic = false
            };
            right.Controls.Add(_props);
            right.Controls.Add(_tablePanel);
            right.Controls.Add(_textPanel);
            right.Controls.Add(_propsTitle);

            // ---- Status and footer ----
            _status = new Label { Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false };
            ModalUi.StyleStatus(_status);

            var footer = ModalUi.Footer();
            Button export = ModalUi.Button("Export PDF…", PillKind.Primary, (s, e) => ExportPdf());
            _tip.SetTip(export, "Write every page to a PDF file (Ctrl+E)");
            Button save = ModalUi.Button("Save", PillKind.Secondary, (s, e) => SaveDesign());
            _tip.SetTip(save, "Save this design (Ctrl+S)");
            Button close = ModalUi.Button("Close", PillKind.Secondary, (s, e) => Close());
            _btnNext = ModalUi.Button("▶", PillKind.Secondary, (s, e) => TurnPage(1));
            _btnPrev = ModalUi.Button("◀", PillKind.Secondary, (s, e) => TurnPage(-1));
            _btnNext.MinimumSize = _btnPrev.MinimumSize = new Size(44, 36);
            _pageLabel = new Label { AutoSize = true, ForeColor = Theme.TextMain, BackColor = Theme.SurfaceAlt, Margin = new Padding(8, 9, 0, 0) };
            footer.Controls.Add(export);
            footer.Controls.Add(save);
            footer.Controls.Add(close);
            footer.Controls.Add(new Panel { Width = 18, Height = 1, BackColor = Theme.SurfaceAlt });
            footer.Controls.Add(_btnNext);
            footer.Controls.Add(_pageLabel);
            footer.Controls.Add(_btnPrev);

            // Record shown in the fields (only when there is data)
            _btnRecordNext = ModalUi.Button("▶", PillKind.Secondary, (s, e) => TurnRecord(1));
            _btnRecordPrev = ModalUi.Button("◀", PillKind.Secondary, (s, e) => TurnRecord(-1));
            _btnRecordNext.MinimumSize = _btnRecordPrev.MinimumSize = new Size(44, 36);
            _tip.SetTip(_btnRecordNext, "Next record in the fields (Ctrl+Page Down)");
            _tip.SetTip(_btnRecordPrev, "Previous record in the fields (Ctrl+Page Up)");
            _recordLabel = new Label
            {
                AutoSize = true, ForeColor = Theme.TextMain, BackColor = Theme.SurfaceAlt,
                Margin = new Padding(8, 9, 0, 0), UseMnemonic = false
            };
            footer.Controls.Add(new Panel { Width = 18, Height = 1, BackColor = Theme.SurfaceAlt });
            footer.Controls.Add(_btnRecordNext);
            footer.Controls.Add(_recordLabel);
            footer.Controls.Add(_btnRecordPrev);

            Controls.Add(_scroller);
            Controls.Add(right);
            Controls.Add(bar);
            Controls.Add(_status);
            Controls.Add(footer);

            _commitTimer.Tick += (s, e) => FlushPending();

            // The design used last, else the newest, else a new one with a small example
            PdfDesign start = PdfDesignStore.Load(settings.PdfDesignId) ?? PdfDesignStore.All().FirstOrDefault();
            if (start == null) OpenDesign(PdfDesignStore.CreateNew(true), true);
            else OpenDesign(start, false);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FitZoom();
            _canvas.Focus();
        }

        // ===================== Design, page, selection =====================

        private PdfDesignPage Page { get { return _doc.Pages[_page]; } }

        /// <summary>The selected object (the one clicked last when several are selected).</summary>
        private PdfItem Selected { get { return _canvas.Selected; } }

        /// <summary>Every selected object, in page order (bottom first).</summary>
        private List<PdfItem> SelectedItems
        {
            get
            {
                var set = new HashSet<PdfItem>(_canvas.SelectedItems);
                return Page.Items.Where(set.Contains).ToList();
            }
        }

        /// <summary>The selected object when exactly one is selected, else null.</summary>
        private PdfItem Single { get { return _canvas.SelectedItems.Count == 1 ? Selected : null; } }

        private bool Dirty { get { return _pending || _committed == null || _committed.Id != _savedState; } }

        private void OpenDesign(PdfDesign design, bool isNew)
        {
            _canvas.EndTextEdit();
            ResetPreview();
            _doc = design;
            _doc.Normalize();
            _isNew = isNew;
            _page = 0;
            _pending = false;
            _newText = null;
            _undo.Clear();
            _redo.Clear();
            ShowPage();
            _committed = Snap();
            _savedState = isNew ? -1 : _committed.Id;
            UpdateCommands();
            if (_fitZoom && IsHandleCreated) FitZoom();
        }

        /// <summary>Shows page <see cref="_page"/> with the items at the indexes <paramref name="select"/> selected.</summary>
        private void ShowPage(params int[] select)
        {
            _page = Math.Max(0, Math.Min(_doc.Pages.Count - 1, _page));
            _canvas.Design = _doc;
            _canvas.Page = Page;
            RefreshFields();
            _canvas.SelectItems((select ?? new int[0]).Where(i => i >= 0 && i < Page.Items.Count).Select(i => Page.Items[i]), false);
            SizeCanvas();
            OnSelectionChanged();
        }

        private void Select(PdfItem item)
        {
            _canvas.SelectItem(item, false);
            OnSelectionChanged();
        }

        private void SelectMany(IEnumerable<PdfItem> items)
        {
            _canvas.SelectItems(items, false);
            OnSelectionChanged();
        }

        private void SelectAll()
        {
            _canvas.EndTextEdit();
            SelectMany(Page.Items);
            if (Page.Items.Count > 1) _status.Text = Lang.F("{0} objects selected. Align ▾ lines them up; drag one to move them all.", Page.Items.Count);
        }

        private void OnSelectionChanged()
        {
            FlushPending();
            ShowProperties();
        }

        private void ShowProperties()
        {
            _syncing = true;
            try
            {
                List<PdfItem> all = SelectedItems;
                PdfItem s = Single;
                var text = s as PdfTextItem;
                var table = s as PdfTableItem;
                _textPanel.Visible = text != null;
                _tablePanel.Visible = table != null;
                if (text != null)
                {
                    _txtText.Text = (text.Text ?? "").Replace("\n", "\r\n");
                    _txtText.Select(_txtText.TextLength, 0);   // Insert field adds at the end unless the cursor is moved
                }
                if (table != null) FillCells(table, 0, 0);
                if (all.Count > 1)
                {
                    // The list edits what they have in common (position, width; font and size when they are all text…)
                    _props.SelectedObjects = all.Cast<object>().ToArray();
                    _propsTitle.Text = Lang.F("{0} OBJECTS", all.Count);
                }
                else
                {
                    _props.SelectedObject = s ?? (object)new PdfPageSetup(_doc, _page);
                    _propsTitle.Text = Lang.T(s == null ? "PAGE & DESIGN" : text != null ? "TEXT" : table != null ? "TABLE" : "PICTURE");
                }
            }
            finally { _syncing = false; }
            UpdateCommands();
            UpdateStatus();
        }

        /// <summary>The property list shows the new position / size after a drag (without rebuilding the panels).</summary>
        private void RefreshProperties()
        {
            if (_props.SelectedObject != null) _props.Refresh();
            UpdateStatus();
        }

        private void UpdateCommands()
        {
            bool edit = !_preview;
            foreach (Control c in _editTools) c.Enabled = edit;
            bool has = _canvas.SelectedItems.Count > 0;
            _btnClone.Enabled = _btnDelete.Enabled = has && edit;
            _btnUndo.Enabled = edit && (_undo.Count > 0 || _pending);
            _btnRedo.Enabled = edit && _redo.Count > 0;
            _btnPreview.Checked = _preview;
            if (_preview)
            {
                int count = _previewPages == null ? 1 : _previewPages.Count;
                _btnPrev.Enabled = _previewIndex > 0;
                _btnNext.Enabled = _previewIndex < count - 1;
                _pageLabel.Text = Lang.F("Preview: page {0} of {1}", _previewIndex + 1, count);
            }
            else
            {
                _btnPrev.Enabled = _page > 0;
                _btnNext.Enabled = _page < _doc.Pages.Count - 1;
                _pageLabel.Text = Lang.F("Page {0} of {1}", _page + 1, _doc.Pages.Count);
            }
            string name = _doc.Name ?? "";
            if (name.Length > 28) name = name.Substring(0, 27) + "…";
            _btnDoc.Text = name + (Dirty ? " •" : "") + "  ▾";
            _zoomLabel.Text = Math.Round(_zoom / PxPerMm100 * 100f).ToString(CultureInfo.CurrentCulture) + "%";
        }

        private void UpdateStatus()
        {
            List<PdfItem> all = SelectedItems;
            if (all.Count == 0)
            {
                _status.Text = Lang.T("Double-click the page to write  ·  drag to move  ·  Alt+drag or Ctrl+D to clone  ·  right-click for more");
                return;
            }
            if (all.Count > 1)
            {
                RectangleF u = Union(all);
                _status.Text = Lang.F("{0} objects  ·  left {1} mm, top {2} mm  ·  {3} × {4} mm", all.Count, Mm(u.X), Mm(u.Y), Mm(u.Width), Mm(u.Height)) +
                               "  ·  " + Lang.T("Align ▾ lines them up; drag one to move them all");
                return;
            }
            PdfItem s = all[0];
            string where = Lang.F("{0}  ·  left {1} mm, top {2} mm  ·  {3} × {4} mm", s.Describe(), Mm(s.X), Mm(s.Y), Mm(s.Width), Mm(s.Height));
            string hint = s is PdfTextItem ? Lang.T("double-click to edit the text")
                        : s is PdfTableItem ? Lang.T("drag a column line to change its width, double-click a cell to edit it")
                        : Lang.T("drag a corner to resize");
            _status.Text = where + "  ·  " + hint;
        }

        private static string Mm(float v) { return v.ToString("0.0", CultureInfo.CurrentCulture); }

        private static RectangleF Union(IEnumerable<PdfItem> items)
        {
            RectangleF u = RectangleF.Empty;
            bool first = true;
            foreach (PdfItem i in items)
            {
                u = first ? i.Bounds : RectangleF.Union(u, i.Bounds);
                first = false;
            }
            return u;
        }

        /// <summary>Height of a text box or table from its content, as the page shows it (fields filled in).</summary>
        private void Lay(PdfItem item)
        {
            PdfDesignRenderer.Layout(item, _fields);
        }

        // ===================== Undo / redo =====================

        private Snapshot Snap()
        {
            return new Snapshot
            {
                Doc = _doc.Clone(), Id = ++_stateCounter, Page = _page,
                Selected = SelectedItems.Select(i => Page.Items.IndexOf(i)).Where(i => i >= 0).ToArray()
            };
        }

        /// <summary>Records a finished change (one undo step).</summary>
        private void Commit()
        {
            _commitTimer.Stop();
            _pending = false;
            if (_committed != null)
            {
                _undo.Add(_committed);
                if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            }
            _redo.Clear();
            _committed = Snap();
            UpdateCommands();
        }

        /// <summary>Typing and nudging are recorded as one step once they pause.</summary>
        private void MarkPending()
        {
            _pending = true;
            _commitTimer.Stop();
            _commitTimer.Start();
            UpdateCommands();
        }

        private void FlushPending()
        {
            if (_pending) Commit();
        }

        private void Undo()
        {
            _canvas.EndTextEdit();
            FlushPending();
            if (_undo.Count == 0) return;
            _redo.Add(_committed);
            _committed = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Restore(_committed);
        }

        private void Redo()
        {
            _canvas.EndTextEdit();
            FlushPending();
            if (_redo.Count == 0) return;
            _undo.Add(_committed);
            _committed = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            Restore(_committed);
        }

        private void Restore(Snapshot s)
        {
            string id = _doc.Id;   // a Save after the snapshot gave the design its file: keep it
            DateTime saved = _doc.Saved;
            _doc = s.Doc.Clone();
            _doc.Id = id;
            _doc.Saved = saved;
            _doc.Normalize();
            _page = s.Page;
            ShowPage(s.Selected);
            UpdateCommands();
            if (_fitZoom) FitZoom();
        }

        // ===================== Adding, cloning, deleting =====================

        /// <summary>Middle of the part of the page that is on screen, in mm.</summary>
        private PointF VisibleCentreMm()
        {
            Size view = _scroller.ClientSize;
            return _canvas.ToMm(new Point(view.Width / 2 - _canvas.Left, view.Height / 2 - _canvas.Top));
        }

        /// <summary>Puts <paramref name="item"/> on the page centred on <paramref name="centre"/> (kept on the paper).</summary>
        private void Place(PdfItem item, PointF centre)
        {
            SizeF page = _doc.PageMm;
            item.X = Snap05(Math.Max(0f, Math.Min(page.Width - item.Width, centre.X - item.Width / 2f)));
            item.Y = Snap05(Math.Max(0f, Math.Min(page.Height - item.Height, centre.Y - item.Height / 2f)));
        }

        private static float Snap05(float v) { return (float)Math.Round(v * 2f) / 2f; }

        private void Insert(PdfItem item)
        {
            InsertAll(new List<PdfItem> { item });
        }

        /// <summary>Puts new items above the selection (on top of the page when nothing is selected) and selects them.</summary>
        private void InsertAll(List<PdfItem> items)
        {
            _canvas.EndTextEdit();
            int at = SelectedItems.Select(i => Page.Items.IndexOf(i)).DefaultIfEmpty(-1).Max();
            if (at >= 0) Page.Items.InsertRange(at + 1, items);
            else Page.Items.AddRange(items);
            SelectMany(items);
        }

        /// <summary>A new text box, typed into at once. Clicked away while still empty, it disappears again.</summary>
        private void AddText(PointF? at)
        {
            var t = new PdfTextItem { Text = "" };
            SizeF page = _doc.PageMm;
            t.Width = Math.Min(80f, page.Width - 20f);
            Lay(t);
            if (at.HasValue)
            {
                // Double-click: the text starts where the mouse is
                t.X = Snap05(Math.Max(0f, Math.Min(page.Width - t.Width, at.Value.X)));
                t.Y = Snap05(Math.Max(0f, Math.Min(page.Height - t.Height, at.Value.Y - t.Height / 2f)));
            }
            else Place(t, VisibleCentreMm());
            Insert(t);
            _newText = t;
            _canvas.BeginTextEdit(t);
        }

        private void AddTable(PointF? at)
        {
            PdfTableItem t = PdfTableItem.Create(4, 3);
            t.Width = Math.Min(160f, _doc.PageMm.Width - 30f);
            Lay(t);
            Place(t, at ?? VisibleCentreMm());
            Insert(t);
            Commit();
        }

        private void AddPicture(PointF? at)
        {
            PdfImageItem item = PickPicture(Math.Min(60f, _doc.PageMm.Width / 3f));
            if (item == null) return;
            Place(item, at ?? VisibleCentreMm());
            Insert(item);
            Commit();
        }

        private PdfImageItem PickPicture(float widthMm)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = Lang.T("Choose a picture"),
                Filter = Lang.T("Pictures") + " (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                try
                {
                    using (Image img = Image.FromFile(dlg.FileName)) return PdfImageItem.FromImage(img, widthMm);
                }
                catch (Exception ex) when (ex is OutOfMemoryException || ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
                {
                    AppDialog.Show(this, Lang.T("This file could not be opened as a picture.") + "\n\n" + dlg.FileName, Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
            }
        }

        private void ReplacePicture(PdfImageItem item)
        {
            PdfImageItem fresh = PickPicture(item.Width);
            if (fresh == null) return;
            item.Data = fresh.Data;
            item.Height = item.Width * item.Ratio;
            _canvas.Invalidate();
            RefreshProperties();
            Commit();
        }

        /// <summary>Ctrl+D: copies a little lower and to the right, selected so they can be dragged into place.</summary>
        private void CloneSelected()
        {
            _canvas.EndTextEdit();
            List<PdfItem> all = SelectedItems;
            if (all.Count == 0) return;
            var copies = all.Select(i =>
            {
                PdfItem copy = i.Clone();
                copy.X += CloneOffsetMm;
                copy.Y += CloneOffsetMm;
                return copy;
            }).ToList();
            InsertAll(copies);
            Commit();
            _status.Text = Lang.T("Cloned. Drag the copy into place (Alt+drag clones while dragging).");
        }

        private void DeleteSelected()
        {
            _canvas.EndTextEdit();
            List<PdfItem> all = SelectedItems;
            if (all.Count == 0) return;
            foreach (PdfItem i in all) Page.Items.Remove(i);
            Select(null);
            Commit();
            _status.Text = all.Count == 1
                ? Lang.F("Deleted: {0}. Undo (Ctrl+Z) brings it back.", all[0].Describe())
                : Lang.F("Deleted {0} objects. Undo (Ctrl+Z) brings them back.", all.Count);
        }

        private void BringSelectedToFront()
        {
            List<PdfItem> all = SelectedItems;
            if (all.Count == 0 || Page.Items.Skip(Page.Items.Count - all.Count).SequenceEqual(all)) return;
            foreach (PdfItem i in all) Page.Items.Remove(i);
            Page.Items.AddRange(all);
            _canvas.Invalidate();
            Commit();
        }

        private void SendSelectedToBack()
        {
            List<PdfItem> all = SelectedItems;
            if (all.Count == 0 || Page.Items.Take(all.Count).SequenceEqual(all)) return;
            foreach (PdfItem i in all) Page.Items.Remove(i);
            Page.Items.InsertRange(0, all);
            _canvas.Invalidate();
            Commit();
        }

        private void Nudge(float dx, float dy)
        {
            List<PdfItem> all = SelectedItems;
            if (all.Count == 0) return;
            foreach (PdfItem i in all)
            {
                i.X += dx;
                i.Y += dy;
            }
            _canvas.Invalidate();
            RefreshProperties();
            MarkPending();
        }

        private void EditSelected()
        {
            var text = Single as PdfTextItem;
            if (text != null) { _canvas.BeginTextEdit(text); return; }
            var table = Single as PdfTableItem;
            if (table != null) { ShowCell(Math.Max(0, _canvas.CellRow), Math.Max(0, _canvas.CellColumn), true); return; }
            var image = Single as PdfImageItem;
            if (image != null) ReplacePicture(image);
        }

        // ===================== Align =====================

        /// <summary>The page inside its margins: what one object (or several, with "Relative to the page") is aligned with.</summary>
        private RectangleF MarginBox
        {
            get
            {
                SizeF p = _doc.PageMm;
                float m = _doc.Margin;
                return new RectangleF(m, m, p.Width - 2f * m, p.Height - 2f * m);
            }
        }

        private bool AlignsToPage { get { return _canvas.SelectedItems.Count < 2 || _alignToPage; } }

        /// <summary>
        /// Lines the selected objects up: one object with the page margins (centre / middle = the middle of the page);
        /// several with each other (the box around them), or with the margins when "Relative to the page" is on.
        /// </summary>
        private void AlignSelected(AlignTo how)
        {
            _canvas.EndTextEdit();
            List<PdfItem> all = SelectedItems;
            if (all.Count == 0)
            {
                _status.Text = Lang.T("Select what to align first: click an object, Shift+click more, or drag a box around them.");
                return;
            }
            bool toPage = AlignsToPage;
            RectangleF area = toPage ? MarginBox : Union(all);
            foreach (PdfItem i in all)
                switch (how)
                {
                    case AlignTo.Left: i.X = area.Left; break;
                    case AlignTo.Centre: i.X = area.Left + (area.Width - i.Width) / 2f; break;
                    case AlignTo.Right: i.X = area.Right - i.Width; break;
                    case AlignTo.Top: i.Y = area.Top; break;
                    case AlignTo.Middle: i.Y = area.Top + (area.Height - i.Height) / 2f; break;
                    case AlignTo.Bottom: i.Y = area.Bottom - i.Height; break;
                }
            AfterArrange();
            _status.Text = toPage
                ? Lang.F("Aligned with the page margins ({0} mm). The margin is set in the page properties (click the empty page).", Mm(_doc.Margin))
                : Lang.T("Aligned with each other.");
        }

        /// <summary>Equal space between three or more objects; the outer two stay where they are.</summary>
        private void Distribute(bool horizontal)
        {
            _canvas.EndTextEdit();
            List<PdfItem> all = SelectedItems;
            if (all.Count < 3)
            {
                _status.Text = Lang.T("Select three or more objects to space them evenly.");
                return;
            }
            List<PdfItem> order = horizontal ? all.OrderBy(i => i.X).ToList() : all.OrderBy(i => i.Y).ToList();
            float start = horizontal ? order[0].X : order[0].Y;
            float end = order.Max(i => horizontal ? i.X + i.Width : i.Y + i.Height);
            float total = order.Sum(i => horizontal ? i.Width : i.Height);
            float gap = (end - start - total) / (order.Count - 1);
            float at = start;
            foreach (PdfItem i in order)
            {
                if (horizontal) { i.X = at; at += i.Width + gap; }
                else { i.Y = at; at += i.Height + gap; }
            }
            AfterArrange();
            _status.Text = Lang.F("Spaced evenly: {0} mm between the objects.", Mm(gap));
        }

        /// <summary>The selected objects get the width of the one clicked last (pictures keep their proportions).</summary>
        private void SameWidth()
        {
            _canvas.EndTextEdit();
            List<PdfItem> all = SelectedItems;
            PdfItem model = Selected;
            if (all.Count < 2 || model == null)
            {
                _status.Text = Lang.T("Select two or more objects: they get the width of the one you clicked last.");
                return;
            }
            foreach (PdfItem i in all)
            {
                if (i == model) continue;
                i.Width = model.Width;
                var image = i as PdfImageItem;
                if (image != null && image.KeepRatio) image.Height = image.Width * image.Ratio;
                Lay(i);
            }
            AfterArrange();
            _status.Text = Lang.F("Every selected object is now {0} mm wide.", Mm(model.Width));
        }

        private void AfterArrange()
        {
            foreach (PdfItem i in SelectedItems)
            {
                i.X = (float)Math.Round(i.X, 2);
                i.Y = (float)Math.Round(i.Y, 2);
            }
            _canvas.Invalidate();
            RefreshProperties();
            Commit();
        }

        /// <summary>The Align menu (toolbar and right-click).</summary>
        private ContextMenuStrip AlignMenu()
        {
            ContextMenuStrip m = NewMenu();
            AddAlignItems(m.Items);
            return m;
        }

        private void AddAlignItems(ToolStripItemCollection items)
        {
            int n = _canvas.SelectedItems.Count;
            items.Add(ThemedMenu.Header(Lang.T(n == 0 ? "ALIGN" : AlignsToPage ? "ALIGN WITH THE PAGE MARGINS" : "ALIGN WITH EACH OTHER")));
            items.Add(ThemedMenu.Item("Align left", () => AlignSelected(AlignTo.Left), n > 0));
            items.Add(ThemedMenu.Item("Align centre", () => AlignSelected(AlignTo.Centre), n > 0));
            items.Add(ThemedMenu.Item("Align right", () => AlignSelected(AlignTo.Right), n > 0));
            items.Add(new ToolStripSeparator());
            items.Add(ThemedMenu.Item("Align top", () => AlignSelected(AlignTo.Top), n > 0));
            items.Add(ThemedMenu.Item("Align middle", () => AlignSelected(AlignTo.Middle), n > 0));
            items.Add(ThemedMenu.Item("Align bottom", () => AlignSelected(AlignTo.Bottom), n > 0));
            items.Add(new ToolStripSeparator());
            items.Add(ThemedMenu.Item("Space evenly across", () => Distribute(true), n >= 3));
            items.Add(ThemedMenu.Item("Space evenly down", () => Distribute(false), n >= 3));
            items.Add(ThemedMenu.Item("Same width", SameWidth, n >= 2));
            items.Add(new ToolStripSeparator());
            items.Add(ThemedMenu.Item((_alignToPage || n < 2 ? "✓  " : "     ") + Lang.T("Relative to the page"),
                () => { _alignToPage = !_alignToPage; }, n >= 2));
            if (n < 2)
                items.Add(ThemedMenu.Item(Lang.T("Shift+click or drag a box to select several"), () => { }, false));
        }

        // ===================== Copy / paste =====================

        private void CopySelected()
        {
            List<PdfItem> all = SelectedItems;
            if (all.Count == 0) return;
            _clipboard = all.Select(i => i.Clone()).ToList();
            _pasteCount = 0;
            try
            {
                // Other programs get the text (or picture) too
                var parts = new List<string>();
                foreach (PdfItem s in all)
                {
                    var text = s as PdfTextItem;
                    var table = s as PdfTableItem;
                    if (text != null && !string.IsNullOrEmpty(text.Text)) parts.Add(text.Text.Replace("\n", "\r\n"));
                    else if (table != null) parts.Add(string.Join("\r\n", table.Rows.Select(r => string.Join("\t", r.Cells))));
                }
                var image = all.Count == 1 ? all[0] as PdfImageItem : null;
                if (image != null && image.Picture != null) Clipboard.SetImage(image.Picture);
                else if (parts.Count > 0) Clipboard.SetText(string.Join("\r\n\r\n", parts));
                else Clipboard.Clear();
            }
            catch (ExternalException) { }
            _clipboardSeq = GetClipboardSequenceNumber();
            _status.Text = Lang.T("Copied. Ctrl+V pastes it on any page.");
        }

        private void CutSelected()
        {
            CopySelected();
            DeleteSelected();
        }

        /// <summary>
        /// Ctrl+V: our own copied objects while the clipboard still holds them; otherwise a copied picture, or copied text
        /// (text with tabs, such as Excel cells, becomes a table).
        /// </summary>
        private void Paste()
        {
            _canvas.EndTextEdit();
            bool ours = _clipboard != null && GetClipboardSequenceNumber() == _clipboardSeq;
            try
            {
                if (!ours && Clipboard.ContainsImage())
                {
                    using (Image img = Clipboard.GetImage())
                    {
                        if (img == null) return;
                        PdfImageItem item = PdfImageItem.FromImage(img, Math.Min(60f, _doc.PageMm.Width / 3f));
                        Place(item, VisibleCentreMm());
                        Insert(item);
                        Commit();
                    }
                    return;
                }
                if (!ours && Clipboard.ContainsText())
                {
                    PasteText(Clipboard.GetText());
                    return;
                }
            }
            catch (ExternalException) { return; }

            if (_clipboard == null || _clipboard.Count == 0) return;
            _pasteCount++;
            var copies = _clipboard.Select(i =>
            {
                PdfItem copy = i.Clone();
                copy.X += CloneOffsetMm * _pasteCount;
                copy.Y += CloneOffsetMm * _pasteCount;
                Lay(copy);
                return copy;
            }).ToList();
            InsertAll(copies);
            Commit();
        }

        private void PasteText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            List<string[]> rows = SplitCells(text);
            if (rows.Any(r => r.Length > 1))
            {
                int cols = rows.Max(r => r.Length);
                PdfTableItem t = PdfTableItem.Create(rows.Count, cols);
                for (int r = 0; r < rows.Count; r++)
                    for (int c = 0; c < cols; c++) t.Rows[r].Cells[c] = c < rows[r].Length ? rows[r][c] : "";
                t.Width = Math.Min(170f, _doc.PageMm.Width - 30f);
                Lay(t);
                Place(t, VisibleCentreMm());
                Insert(t);
            }
            else
            {
                var t = new PdfTextItem { Text = text.Replace("\r\n", "\n").TrimEnd('\n'), Width = Math.Min(120f, _doc.PageMm.Width - 30f) };
                Lay(t);
                Place(t, VisibleCentreMm());
                Insert(t);
            }
            Commit();
        }

        /// <summary>Rows (lines) of tab-separated cells, as Excel and Word put them on the clipboard.</summary>
        private static List<string[]> SplitCells(string text)
        {
            List<string> lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines.Select(l => l.Split('\t')).ToList();
        }

        // ===================== Text =====================

        /// <summary>Typing in the Text box on the right changes the page at once (one undo step per pause).</summary>
        private void SideTextChanged()
        {
            if (_syncing) return;
            var t = Single as PdfTextItem;
            if (t == null) return;
            t.Text = _txtText.Text.Replace("\r\n", "\n");
            Lay(t);
            _canvas.Invalidate();
            UpdateStatus();
            MarkPending();
        }

        /// <summary>Typing on the page shows in the Text box on the right.</summary>
        private void SyncSideText()
        {
            var t = Single as PdfTextItem;
            if (t == null) return;
            _syncing = true;
            try { _txtText.Text = (t.Text ?? "").Replace("\n", "\r\n"); }
            finally { _syncing = false; }
            UpdateStatus();
        }

        private void OnTextEditEnded(PdfTextItem t, bool changed)
        {
            bool isNew = t == _newText;
            _newText = null;
            if (string.IsNullOrWhiteSpace(t.Text))
            {
                // An empty text box is not kept (a new one that was never typed into leaves no undo step)
                Page.Items.Remove(t);
                if (Selected == t) Select(null);
                _canvas.Invalidate();
                if (!isNew) Commit();
                return;
            }
            Lay(t);   // typing measured the text as typed; the page shows the fields filled in
            _canvas.Invalidate();
            if (changed || isNew) Commit();
            if (Single == t)
            {
                SyncSideText();
                _txtText.Select(Math.Min(_canvas.EditorCaret, _txtText.TextLength), 0);   // Insert field goes where typing stopped
            }
            RefreshProperties();
        }

        // ===================== Table cells =====================

        private DataGridView BuildCellGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                AllowUserToOrderColumns = false, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = true,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2, BackgroundColor = Theme.Surface, BorderStyle = BorderStyle.FixedSingle,
                GridColor = Theme.Border, EnableHeadersVisualStyles = false, RowHeadersWidth = 42,
                RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single, RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 28,
                Font = new Font(CardFonts.UI, 9.5f)
            };
            grid.RowTemplate.Height = 28;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextSoft;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.RowHeadersDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            grid.RowHeadersDefaultCellStyle.ForeColor = Theme.TextSoft;
            grid.RowHeadersDefaultCellStyle.SelectionBackColor = Theme.SurfaceAlt;
            grid.DefaultCellStyle.BackColor = Theme.Surface;
            grid.DefaultCellStyle.ForeColor = Theme.TextMain;
            grid.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = Theme.TextMain;

            grid.CellValueChanged += (s, e) =>
            {
                if (_syncing || e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var t = Single as PdfTableItem;
                if (t == null || e.RowIndex >= t.Rows.Count || e.ColumnIndex >= t.ColumnCount) return;
                t.Rows[e.RowIndex].Cells[e.ColumnIndex] = Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value) ?? "";
                Lay(t);
                _canvas.Invalidate();
                RefreshProperties();
                Commit();
            };
            grid.CurrentCellChanged += (s, e) =>
            {
                if (_syncing || grid.CurrentCell == null) return;
                _canvas.SetCell(grid.CurrentCell.RowIndex, grid.CurrentCell.ColumnIndex);
            };
            grid.KeyDown += (s, e) =>
            {
                if (grid.IsCurrentCellInEditMode) return;
                if (e.KeyData == (Keys.Control | Keys.V)) { PasteCells(); e.Handled = true; }
                else if (e.KeyCode == Keys.Delete) { ClearCells(); e.Handled = true; }
            };
            grid.DataError += (s, e) => e.ThrowException = false;
            return grid;
        }

        /// <summary>Fills the cell grid from <paramref name="t"/> and puts the current cell at (row, column).</summary>
        private void FillCells(PdfTableItem t, int row, int column)
        {
            bool was = _syncing;
            _syncing = true;
            try
            {
                _cells.Rows.Clear();
                _cells.Columns.Clear();
                for (int c = 0; c < t.ColumnCount; c++)
                    _cells.Columns.Add(new DataGridViewTextBoxColumn
                    {
                        HeaderText = ColumnName(c), Width = 96, SortMode = DataGridViewColumnSortMode.NotSortable, ValueType = typeof(string)
                    });
                for (int r = 0; r < t.Rows.Count; r++)
                {
                    int i = _cells.Rows.Add(t.Rows[r].Cells.Cast<object>().ToArray());
                    // ↻ = the row repeated for each record
                    _cells.Rows[i].HeaderCell.Value = (r + 1).ToString(CultureInfo.CurrentCulture) + (r == t.TemplateRow ? " ↻" : "");
                    if (r == t.TemplateRow) _cells.Rows[i].HeaderCell.ToolTipText = Lang.T("Repeated for each record of the data");
                }
                row = Math.Max(0, Math.Min(t.Rows.Count - 1, row));
                column = Math.Max(0, Math.Min(t.ColumnCount - 1, column));
                if (_cells.Rows.Count > 0) _cells.CurrentCell = _cells.Rows[row].Cells[column];
                _canvas.SetCell(row, column);
            }
            finally { _syncing = was; }
        }

        /// <summary>A, B, … Z, AA, AB … like Excel.</summary>
        private static string ColumnName(int index)
        {
            string name = "";
            for (int n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
            return name;
        }

        /// <summary>Selects a cell of the selected table in the grid (and starts typing in it when <paramref name="edit"/>).</summary>
        private void ShowCell(int row, int column, bool edit)
        {
            var t = Single as PdfTableItem;
            if (t == null || row < 0 || column < 0 || row >= _cells.Rows.Count || column >= _cells.Columns.Count) return;
            _syncing = true;
            try { _cells.CurrentCell = _cells.Rows[row].Cells[column]; }
            finally { _syncing = false; }
            _canvas.SetCell(row, column);
            if (!edit) return;
            _cells.Focus();
            _cells.BeginEdit(true);
        }

        private int CurrentRow { get { return _cells.CurrentCell == null ? -1 : _cells.CurrentCell.RowIndex; } }
        private int CurrentColumn { get { return _cells.CurrentCell == null ? -1 : _cells.CurrentCell.ColumnIndex; } }

        private void TableChanged(PdfTableItem t, int row, int column)
        {
            t.Normalize();
            Lay(t);
            FillCells(t, row, column);
            _canvas.Invalidate();
            RefreshProperties();
            Commit();
        }

        private void TableAddRow()
        {
            var t = Single as PdfTableItem;
            if (t == null) return;
            _cells.EndEdit();
            int at = CurrentRow < 0 ? t.Rows.Count : CurrentRow + 1;
            var row = new PdfTableRow();
            for (int c = 0; c < t.ColumnCount; c++) row.Cells.Add("");
            t.Rows.Insert(at, row);
            TableChanged(t, at, Math.Max(0, CurrentColumn));
        }

        private void TableAddColumn()
        {
            var t = Single as PdfTableItem;
            if (t == null) return;
            _cells.EndEdit();
            int at = CurrentColumn < 0 ? t.ColumnCount : CurrentColumn + 1;
            float width = t.ColumnWidths.Average();
            t.ColumnWidths.Insert(at, width);
            foreach (PdfTableRow r in t.Rows) r.Cells.Insert(at, "");
            TableChanged(t, Math.Max(0, CurrentRow), at);
        }

        private void TableDeleteRow()
        {
            var t = Single as PdfTableItem;
            if (t == null || CurrentRow < 0) return;
            if (t.Rows.Count <= 1) { _status.Text = Lang.T("A table keeps at least one row. Delete the table instead (Del on the page)."); return; }
            _cells.EndEdit();
            int at = CurrentRow;
            t.Rows.RemoveAt(at);
            TableChanged(t, Math.Min(at, t.Rows.Count - 1), Math.Max(0, CurrentColumn));
        }

        private void TableDeleteColumn()
        {
            var t = Single as PdfTableItem;
            if (t == null || CurrentColumn < 0) return;
            if (t.ColumnCount <= 1) { _status.Text = Lang.T("A table keeps at least one column. Delete the table instead (Del on the page)."); return; }
            _cells.EndEdit();
            int at = CurrentColumn;
            t.ColumnWidths.RemoveAt(at);
            foreach (PdfTableRow r in t.Rows) r.Cells.RemoveAt(at);
            TableChanged(t, Math.Max(0, CurrentRow), Math.Min(at, t.ColumnCount - 1));
        }

        private void TableEqualWidths()
        {
            var t = Single as PdfTableItem;
            if (t == null) return;
            for (int c = 0; c < t.ColumnWidths.Count; c++) t.ColumnWidths[c] = 1f;
            TableChanged(t, Math.Max(0, CurrentRow), Math.Max(0, CurrentColumn));
        }

        /// <summary>Ctrl+V in the cell grid: cells copied from Excel / Word fill the table from the current cell, growing it as needed.</summary>
        private void PasteCells()
        {
            var t = Single as PdfTableItem;
            if (t == null) return;
            string text;
            try { text = Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch (ExternalException) { return; }
            if (string.IsNullOrEmpty(text)) return;
            List<string[]> rows = SplitCells(text);
            if (rows.Count == 0) return;
            int r0 = Math.Max(0, CurrentRow), c0 = Math.Max(0, CurrentColumn);
            int cols = rows.Max(r => r.Length);
            while (t.ColumnCount < c0 + cols)
            {
                t.ColumnWidths.Add(t.ColumnWidths.Average());
                foreach (PdfTableRow row in t.Rows) row.Cells.Add("");
            }
            while (t.Rows.Count < r0 + rows.Count)
            {
                var row = new PdfTableRow();
                for (int c = 0; c < t.ColumnCount; c++) row.Cells.Add("");
                t.Rows.Add(row);
            }
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < rows[r].Length; c++) t.Rows[r0 + r].Cells[c0 + c] = rows[r][c];
            TableChanged(t, r0, c0);
        }

        private void ClearCells()
        {
            var t = Single as PdfTableItem;
            if (t == null || _cells.SelectedCells.Count == 0) return;
            foreach (DataGridViewCell cell in _cells.SelectedCells)
                if (cell.RowIndex < t.Rows.Count && cell.ColumnIndex < t.ColumnCount) t.Rows[cell.RowIndex].Cells[cell.ColumnIndex] = "";
            TableChanged(t, Math.Max(0, CurrentRow), Math.Max(0, CurrentColumn));
        }

        // ===================== Properties =====================

        private void OnPropertyChanged()
        {
            List<PdfItem> all = SelectedItems;
            foreach (PdfItem s in all)
            {
                // Values that cannot work are put back in range
                s.Width = Math.Max(PdfItem.MinSizeMm, s.Width);
                if (s.ResizesHeight) s.Height = Math.Max(PdfItem.MinSizeMm, s.Height);
                var text = s as PdfTextItem;
                if (text != null)
                {
                    text.SizePt = Clamp(text.SizePt, 2f, 400f);
                    text.PaddingMm = Clamp(text.PaddingMm, 0f, 50f);
                    text.BorderPt = Clamp(text.BorderPt, 0f, 20f);
                }
                var table = s as PdfTableItem;
                if (table != null)
                {
                    table.SizePt = Clamp(table.SizePt, 2f, 200f);
                    table.PaddingMm = Clamp(table.PaddingMm, 0f, 20f);
                    table.BorderPt = Clamp(table.BorderPt, 0f, 10f);
                    table.MinRowMm = Clamp(table.MinRowMm, 2f, 100f);
                    if (table.DataRows && table.TemplateRow < 0)
                    {
                        // "Repeat for each record" on a table that is only a header: give it the row to repeat
                        var row = new PdfTableRow();
                        for (int c = 0; c < table.ColumnCount; c++) row.Cells.Add("");
                        table.Rows.Add(row);
                    }
                }
                Lay(s);
            }
            if (all.Count == 0)
            {
                // Paper, orientation or margin: the page changes; the data mode changes what the fields show
                _doc.MarginMm = Clamp(_doc.MarginMm, 0f, 100f);
                RefreshFields();
                if (_fitZoom) FitZoom();
                else SizeCanvas();
            }
            var single = Single as PdfTableItem;
            if (single != null) FillCells(single, Math.Max(0, CurrentRow), Math.Max(0, CurrentColumn));   // ↻ follows Header row / Repeat
            _canvas.Invalidate();
            RefreshProperties();
            Commit();
        }

        private static float Clamp(float v, float min, float max)
        {
            return float.IsNaN(v) ? min : Math.Max(min, Math.Min(max, v));
        }

        // ===================== Pages =====================

        private void TurnPage(int delta)
        {
            _canvas.EndTextEdit();
            FlushPending();
            if (_preview)
            {
                int count = _previewPages == null ? 0 : _previewPages.Count;
                int at = (int)Math.Max(0L, Math.Min(count - 1L, (long)_previewIndex + delta));
                if (count == 0 || at == _previewIndex) return;
                _previewIndex = at;
                ShowPreviewPage();
                _scroller.AutoScrollPosition = Point.Empty;
                return;
            }
            int to = Math.Max(0, Math.Min(_doc.Pages.Count - 1, _page + delta));
            if (to == _page) return;
            _page = to;
            ShowPage(-1);
            _scroller.AutoScrollPosition = Point.Empty;
        }

        private void AddPage(bool duplicate)
        {
            _canvas.EndTextEdit();
            FlushPending();
            PdfDesignPage page = duplicate ? Page.Clone() : new PdfDesignPage();
            _doc.Pages.Insert(_page + 1, page);
            _page++;
            ShowPage(-1);
            Commit();
            _status.Text = Lang.F(duplicate ? "Page {0} is a copy of the page before it." : "Page {0} added.", _page + 1);
        }

        private void DeletePage()
        {
            _canvas.EndTextEdit();
            FlushPending();
            if (_doc.Pages.Count <= 1)
            {
                if (Page.Items.Count == 0 ||
                    !Confirm(Lang.T("Remove everything from this page?") + "\n\n" + Lang.T("Undo (Ctrl+Z) brings it back."))) return;
                Page.Items.Clear();
            }
            else
            {
                if (Page.Items.Count > 0 &&
                    !Confirm(Lang.F("Delete page {0} and everything on it?", _page + 1) + "\n\n" + Lang.T("Undo (Ctrl+Z) brings it back."))) return;
                _doc.Pages.RemoveAt(_page);
            }
            ShowPage(-1);
            Commit();
        }

        private void SetPaper(string paper, bool? landscape)
        {
            _canvas.EndTextEdit();
            if (paper != null) _doc.Paper = paper;
            if (landscape.HasValue) _doc.Landscape = landscape.Value;
            if (_fitZoom) FitZoom();
            else SizeCanvas();
            ShowProperties();
            Commit();
        }

        private void ShowPageMenu()
        {
            ContextMenuStrip m = NewMenu();
            m.Items.Add(ThemedMenu.Item("Add page", () => AddPage(false)));
            m.Items.Add(ThemedMenu.Item("Duplicate this page", () => AddPage(true)));
            m.Items.Add(ThemedMenu.Item(_doc.Pages.Count > 1 ? "Delete this page" : "Clear this page", DeletePage));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(ThemedMenu.Header(Lang.T("PAPER")));
            foreach (string p in PdfDesign.PaperNames)
            {
                string paper = p;
                m.Items.Add(ThemedMenu.Item((_doc.PaperName == paper ? "✓  " : "     ") + paper, () => SetPaper(paper, null)));
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(ThemedMenu.Item((_doc.Landscape ? "     " : "✓  ") + Lang.T("Portrait"), () => SetPaper(null, false)));
            m.Items.Add(ThemedMenu.Item((_doc.Landscape ? "✓  " : "     ") + Lang.T("Landscape"), () => SetPaper(null, true)));
            ShowMenuUnder(m, _btnPage);
        }

        // ===================== Zoom =====================

        private void FitZoom()
        {
            if (_doc == null) return;
            _fitZoom = true;
            float avail = _scroller.ClientSize.Width - 2 * PdfPageCanvas.Gutter - SystemInformation.VerticalScrollBarWidth;
            SetZoom(avail / _doc.PageMm.Width);
        }

        /// <summary>Zooms by <paramref name="factor"/>, keeping the point under the mouse (or the middle of the view) in place.</summary>
        private void ZoomBy(float factor, Point? at)
        {
            Point view = at.HasValue
                ? new Point(at.Value.X + _canvas.Left, at.Value.Y + _canvas.Top)
                : new Point(_scroller.ClientSize.Width / 2, _scroller.ClientSize.Height / 2);
            PointF mm = _canvas.ToMm(new Point(view.X - _canvas.Left, view.Y - _canvas.Top));
            _fitZoom = false;
            SetZoom(_zoom * factor);
            PointF px = _canvas.ToPx(mm);
            _scroller.AutoScrollPosition = new Point(Math.Max(0, (int)px.X - view.X), Math.Max(0, (int)px.Y - view.Y));
        }

        private void SetZoom(float zoom)
        {
            _canvas.EndTextEdit();
            _zoom = Clamp(zoom, 0.6f, 16f);
            SizeCanvas();
            UpdateCommands();
        }

        private void SizeCanvas()
        {
            if (_doc == null) return;
            SizeF page = _doc.PageMm;
            int w = (int)Math.Ceiling(page.Width * _zoom) + 2 * PdfPageCanvas.Gutter;
            int h = (int)Math.Ceiling(page.Height * _zoom) + 2 * PdfPageCanvas.Gutter;
            Size client = _scroller.ClientSize;
            bool vScroll = h > client.Height;
            int minW = client.Width - (vScroll && !_scroller.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
            _canvas.Zoom = _zoom;
            _canvas.Size = new Size(Math.Max(w, minW), Math.Max(h, client.Height));
            _canvas.Invalidate();
        }

        // ===================== Design library =====================

        private void ShowDocumentMenu()
        {
            ContextMenuStrip m = NewMenu();
            m.Items.Add(ThemedMenu.Header(Lang.T("MY PDF DESIGNS")));
            List<PdfDesign> all = PdfDesignStore.All();
            if (all.Count == 0) m.Items.Add(ThemedMenu.Item(Lang.T("(none saved yet)"), () => { }, false));
            foreach (PdfDesign d in all.Take(25))
            {
                string id = d.Id;
                string label = (d.Id == _doc.Id ? "✓  " : "     ") + d.Name;
                m.Items.Add(ThemedMenu.Item(label, () => OpenSaved(id), d.Id != _doc.Id));
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(ThemedMenu.Item("New design\tCtrl+N", NewDesign));
            m.Items.Add(ThemedMenu.Item("Save\tCtrl+S", () => SaveDesign()));
            m.Items.Add(ThemedMenu.Item("Save a copy", SaveCopy));
            m.Items.Add(ThemedMenu.Item("Rename…", Rename));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(ThemedMenu.Item("Delete this design…", DeleteDesign, !_isNew));
            ShowMenuUnder(m, _btnDoc);
        }

        /// <summary>Asks to save unsaved changes. False = the user cancelled.</summary>
        private bool ConfirmDiscard()
        {
            _canvas.EndTextEdit();
            FlushPending();
            if (!Dirty) return true;
            DialogResult r = AppDialog.Show(this, Lang.F("Save the changes to “{0}”?", _doc.Name), Text,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            return r == DialogResult.No || SaveDesign();
        }

        private void OpenSaved(string id)
        {
            if (!ConfirmDiscard()) return;
            PdfDesign d = PdfDesignStore.Load(id);
            if (d == null)
            {
                AppDialog.Show(this, Lang.T("This design could not be opened."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            OpenDesign(d, false);
            Remember();
        }

        private void NewDesign()
        {
            if (!ConfirmDiscard()) return;
            OpenDesign(PdfDesignStore.CreateNew(false), true);
            _status.Text = Lang.T("New design. Double-click the page to start writing.");
        }

        private bool SaveDesign()
        {
            _canvas.EndTextEdit();
            FlushPending();
            try
            {
                _doc.Name = PdfDesignStore.UniqueName(_doc.Name, _doc.Id);
                PdfDesignStore.Save(_doc);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                AppDialog.Show(this, Lang.T("The design could not be saved.") + "\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            _isNew = false;
            _savedState = _committed.Id;
            Remember();
            UpdateCommands();
            if (_props.SelectedObject is PdfPageSetup) _props.Refresh();
            _status.Text = Lang.F("Saved “{0}”.", _doc.Name);
            return true;
        }

        private void SaveCopy()
        {
            _canvas.EndTextEdit();
            FlushPending();
            PdfDesign copy = _doc.Clone();
            copy.Id = "";
            copy.Name = PdfDesignStore.UniqueName(_doc.Name + " " + Lang.T("copy"), null);
            try { PdfDesignStore.Save(copy); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                AppDialog.Show(this, Lang.T("The design could not be saved.") + "\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            OpenDesign(copy, false);
            Remember();
            _status.Text = Lang.F("Saved a copy: “{0}”. You are now editing the copy.", copy.Name);
        }

        /// <summary>Shows the Name line of the design in the property list, ready to type.</summary>
        private void Rename()
        {
            _canvas.EndTextEdit();
            Select(null);
            GridItem root = _props.SelectedGridItem;
            while (root != null && root.Parent != null) root = root.Parent;
            GridItem name = root == null ? null : FindGridItem(root, "Name");
            if (name == null) return;
            _props.Focus();
            _props.SelectedGridItem = name;
            SendKeys.Send("{TAB}");
        }

        private static GridItem FindGridItem(GridItem item, string property)
        {
            if (item.PropertyDescriptor != null && item.PropertyDescriptor.Name == property) return item;
            foreach (GridItem child in item.GridItems)
            {
                GridItem found = FindGridItem(child, property);
                if (found != null) return found;
            }
            return null;
        }

        private void DeleteDesign()
        {
            if (_isNew) return;
            if (!Confirm(Lang.F("Delete the design “{0}”?", _doc.Name) + "\n\n" + Lang.T("It cannot be brought back."))) return;
            try { PdfDesignStore.Delete(_doc.Id); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                AppDialog.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            PdfDesign next = PdfDesignStore.All().FirstOrDefault();
            if (next != null) OpenDesign(next, false);
            else OpenDesign(PdfDesignStore.CreateNew(false), true);
            Remember();
        }

        /// <summary>The design opens again next time.</summary>
        private void Remember()
        {
            string id = _isNew ? "" : _doc.Id;
            if (_settings.PdfDesignId == id) return;
            _settings.PdfDesignId = id;
            try { _settings.Save(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        // ===================== Export =====================

        private void ExportPdf()
        {
            _canvas.EndTextEdit();
            FlushPending();
            List<Student> records = Records;
            if (records.Count == 0 && _doc.UsesData() &&
                !Confirm(Lang.T("This design has data fields, but no data is chosen: they will print as they are typed ({NameKh}…).") +
                         "\n\n" + Lang.T("Choose the data with Data ▾. Export anyway?"))) return;
            using (var dlg = new SaveFileDialog
            {
                Title = Lang.T("Export PDF"), Filter = "PDF (*.pdf)|*.pdf", DefaultExt = "pdf", OverwritePrompt = true,
                FileName = SafeFileName(_doc.Name) + ".pdf",
                InitialDirectory = _lastExportFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = dlg.FileName;
                Cursor = Cursors.WaitCursor;
                int pages;
                try
                {
                    if (records.Count > 0 && _doc.UsesData())
                    {
                        _status.Text = Lang.F("Writing the PDF for {0} records…", records.Count);
                        _status.Update();
                    }
                    pages = PdfDesignRenderer.Export(_doc.Clone(), path, _doc.UsesData() ? records : null);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ExternalException ||
                                           ex is OutOfMemoryException || ex is ArgumentException)
                {
                    Cursor = Cursors.Default;
                    AppDialog.Show(this, Lang.T("The PDF could not be written.") + "\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                finally { Cursor = Cursors.Default; }

                ExportedPath = path;
                _lastExportFolder = Path.GetDirectoryName(path);
                _status.Text = Lang.F("PDF exported: {0}", path) + "  ·  " + Lang.F("{0} pages", pages);
                if (AppDialog.Show(this, Lang.T("The PDF was exported. Open it now?") + "\n\n" + path, Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    try { Process.Start(path); }
                    catch (Exception ex) when (ex is Win32Exception || ex is FileNotFoundException) { }
                }
            }
        }

        private static string SafeFileName(string name)
        {
            string s = string.IsNullOrWhiteSpace(name) ? "design" : name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        // ===================== Preview =====================

        /// <summary>F5 / Preview: shows the pages as Export writes them, or goes back to editing.</summary>
        private void TogglePreview()
        {
            if (_preview)
            {
                EndPreview();
                return;
            }
            _canvas.EndTextEdit();
            FlushPending();
            Select(null);
            _preview = true;
            _previewIndex = 0;
            BuildPreview();
        }

        /// <summary>
        /// Makes the exported pages again (from <see cref="PdfDesignRenderer.BuildPages"/>, the same code as Export) and shows
        /// one of them: every data table gets one row per record and continues on new pages when the list is long.
        /// </summary>
        private void BuildPreview()
        {
            List<Student> records = Records;
            Cursor = Cursors.WaitCursor;
            try
            {
                _previewPages = PdfDesignRenderer.BuildPages(_doc.Clone(), _doc.UsesData() ? records : null);
            }
            catch (Exception ex) when (ex is OutOfMemoryException || ex is ArgumentException || ex is ExternalException)
            {
                Cursor = Cursors.Default;
                EndPreview();
                AppDialog.Show(this, Lang.T("The preview could not be made.") + "\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally { Cursor = Cursors.Default; }
            if (_previewPages.Count == 0) _previewPages.Add(new List<PdfItem>());
            ShowPreviewPage();
        }

        private void ShowPreviewPage()
        {
            _previewIndex = Math.Max(0, Math.Min(_previewPages.Count - 1, _previewIndex));
            _canvas.ReadOnly = true;
            _canvas.Fields = null;   // the preview pages have their fields filled in already
            _canvas.Page = new PdfDesignPage { Items = _previewPages[_previewIndex] };
            _canvas.Caption = Lang.F("Preview: page {0} of {1}", _previewIndex + 1, _previewPages.Count);
            _canvas.Invalidate();
            _side.Enabled = false;
            _btnRecordPrev.Visible = _btnRecordNext.Visible = _recordLabel.Visible = false;
            UpdateCommands();

            int records = _doc.UsesData() ? Records.Count : 0;
            int rows = _previewPages[_previewIndex].OfType<PdfTableItem>().Where(t => t.DataRows)
                .Sum(t => Math.Max(0, t.Rows.Count - (t.HeaderRow ? 1 : 0)));
            _status.Text = (records > 0 ? Lang.F("Preview with {0} records", records) : Lang.T("Preview without data")) +
                           (rows > 0 ? "  ·  " + Lang.F("{0} rows on this page", rows) : "") +
                           "  ·  " + Lang.T("PgUp / PgDn turn the pages; F5, Esc or a double-click goes back to editing");
        }

        /// <summary>Leaves the preview without showing the design again (a design is being opened).</summary>
        private void ResetPreview()
        {
            _preview = false;
            _previewPages = null;
            _previewIndex = 0;
            _canvas.ReadOnly = false;
            _canvas.Caption = null;
            _side.Enabled = true;
        }

        /// <summary>Back to editing, on the design page that was shown before the preview.</summary>
        private void EndPreview()
        {
            if (!_preview) return;
            ResetPreview();
            ShowPage();
            _canvas.Focus();
        }

        // ===================== Data =====================

        /// <summary>The records the fields are filled from (empty = no data).</summary>
        private List<Student> Records
        {
            get
            {
                switch (_data)
                {
                    case DataChoice.List: return _listRecords;
                    case DataChoice.Picked: return _pickedRecords;
                    case DataChoice.File: return _fileRecords;
                    default: return new List<Student>();
                }
            }
        }

        /// <summary>
        /// The page shows the fields of the current record (or the field names), and the heights follow; the record
        /// buttons and the Data button show where the data comes from.
        /// </summary>
        private void RefreshFields()
        {
            if (_doc == null) return;
            List<Student> records = Records;
            _record = records.Count == 0 ? 0 : Math.Max(0, Math.Min(records.Count - 1, _record));
            if (_showFieldNames && records.Count > 0) _fields = null;
            else
            {
                // No data: {Page} and {Date} still show; list fields stay as typed
                var f = new PdfFieldContext { Page = _page + 1, Pages = _doc.Pages.Count };
                if (records.Count > 0)
                {
                    f = f.For(records[_record], _record + 1);
                    f.Count = records.Count;
                }
                _fields = f;
            }
            _canvas.Fields = _fields;
            _canvas.RecordCount = records.Count;
            PdfDesignRenderer.LayoutAll(_doc, _fields);
            _canvas.Invalidate();

            bool show = records.Count > 0;
            _btnRecordPrev.Visible = _btnRecordNext.Visible = _recordLabel.Visible = show;
            if (show)
            {
                string who = RecordName(records[_record]);
                _recordLabel.Text = Lang.F("Record {0} of {1}", _record + 1, records.Count) + (who.Length > 0 ? "  ·  " + who : "") +
                                    (_showFieldNames ? "  ·  " + Lang.T("field names shown") : "");
                _btnRecordPrev.Enabled = _record > 0;
                _btnRecordNext.Enabled = _record < records.Count - 1;
            }
            _btnData.Text = show ? Lang.F("Data: {0} ▾", records.Count) : Lang.T("Data ▾");
            if (_preview) BuildPreview();   // other data: the table gets the new records, and the pages they need
        }

        /// <summary>A short name for a record: Khmer name, Latin name, ID, else its first value.</summary>
        private static string RecordName(Student s)
        {
            string name = new[] { s.NameKh, s.NameEn, s.StudentId }.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                       ?? s.Columns.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
            name = name.Trim();
            return name.Length > 28 ? name.Substring(0, 27) + "…" : name;
        }

        private void TurnRecord(int delta)
        {
            List<Student> records = Records;
            int to = Math.Max(0, Math.Min(records.Count - 1, _record + delta));
            if (records.Count == 0 || to == _record) return;
            _canvas.EndTextEdit();
            _record = to;
            RefreshFields();
            UpdateStatus();
        }

        private void SetData(DataChoice choice)
        {
            _canvas.EndTextEdit();
            _data = choice;
            _record = 0;
            RefreshFields();
            List<Student> records = Records;
            List<string> fields = PdfFieldContext.FieldsOf(records);
            _status.Text = records.Count == 0
                ? Lang.T("No data: the fields show their names.")
                : Lang.F("{0} records. Fields: {1}", records.Count, string.Join(", ", fields.Take(12).Select(f => "{" + f + "}")) +
                         (fields.Count > 12 ? " …" : ""));
        }

        private void ShowDataMenu()
        {
            ContextMenuStrip m = NewMenu();
            m.Items.Add(ThemedMenu.Header(Lang.T("DATA FOR THE FIELDS")));
            m.Items.Add(ThemedMenu.Item(Check(_data == DataChoice.List) + Lang.F("Students in the main list ({0})", _listRecords.Count),
                () => SetData(DataChoice.List), _listRecords.Count > 0));
            m.Items.Add(ThemedMenu.Item(Check(_data == DataChoice.Picked) + Lang.F("Selected students ({0})", _pickedRecords.Count),
                () => SetData(DataChoice.Picked), _pickedRecords.Count > 0));
            if (_fileName != null)
                m.Items.Add(ThemedMenu.Item(Check(_data == DataChoice.File) + Lang.F("{0} ({1} rows)", _fileName, _fileRecords.Count),
                    () => SetData(DataChoice.File), _fileRecords.Count > 0));
            m.Items.Add(ThemedMenu.Item(Check(_data == DataChoice.None) + Lang.T("No data"), () => SetData(DataChoice.None)));
            m.Items.Add(ThemedMenu.Item("Import Excel or CSV file…", ImportDataFile));
            m.Items.Add(ThemedMenu.Item("Paste rows copied from Excel", PasteData));
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem insert = ThemedMenu.SubMenu(Lang.T("Insert field"));
            AddFieldItems(insert.DropDownItems);
            insert.Enabled = !_preview;
            m.Items.Add(insert);
            m.Items.Add(ThemedMenu.Item("Add a list table of the data", AddDataTable, !_preview && PdfFieldContext.FieldsOf(Records).Count > 0));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(ThemedMenu.Item(Check(_preview) + Lang.T("Preview the pages with the data") + "\tF5", TogglePreview));
            m.Items.Add(ThemedMenu.Item(Check(_doc.OnePerRecord) + Lang.T("One copy of the design for each record"), ToggleOnePerRecord));
            m.Items.Add(ThemedMenu.Item(Check(_showFieldNames) + Lang.T("Show field names instead of values"), () =>
            {
                _showFieldNames = !_showFieldNames;
                RefreshFields();
            }, !_preview && Records.Count > 0));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(ThemedMenu.Item("How data fields work…", ShowDataHelp));
            ShowMenuUnder(m, _btnData);
        }

        private static string Check(bool on) { return on ? "✓  " : "     "; }

        /// <summary>One item per field (with the current record's value at the right), then {No}, {Count}, {Date}, {Page}, {Pages}.</summary>
        private void AddFieldItems(ToolStripItemCollection items)
        {
            List<Student> records = Records;
            Student current = records.Count > 0 ? records[_record] : null;
            List<string> fields = PdfFieldContext.FieldsOf(records);
            if (fields.Count == 0) fields.AddRange(PdfFieldContext.StudentFields);   // no data yet: the usual student fields
            if (records.Count == 0)
                items.Add(ThemedMenu.Item(Lang.T("No data chosen: these fields fill in once it is"), () => { }, false));
            foreach (string f in fields)
            {
                string token = "{" + f + "}";
                string sample = current == null ? "" : PdfFieldContext.FieldOf(current, f) ?? "";
                if (sample.Length > 26) sample = sample.Substring(0, 25) + "…";
                items.Add(ThemedMenu.Item(token + (sample.Length > 0 ? "\t" + sample.Replace("\t", " ") : ""), () => InsertField(token)));
            }
            items.Add(new ToolStripSeparator());
            items.Add(ThemedMenu.Item("{No}\t" + Lang.T("row / record number"), () => InsertField("{No}")));
            items.Add(ThemedMenu.Item("{Count}\t" + Lang.T("number of records"), () => InsertField("{Count}")));
            items.Add(ThemedMenu.Item("{Date}\t" + Lang.T("today"), () => InsertField("{Date}")));
            items.Add(ThemedMenu.Item("{Page} / {Pages}\t" + Lang.T("page numbers"), () => InsertField(Lang.F("Page {0} of {1}", "{Page}", "{Pages}"))));
        }

        /// <summary>Insert field ▾ of the Text / Table panel.</summary>
        private void ShowFieldMenu(Control button)
        {
            ContextMenuStrip m = NewMenu();
            AddFieldItems(m.Items);
            ShowMenuUnder(m, button);
        }

        /// <summary>Right-click while typing on the page.</summary>
        private void FillEditorMenu()
        {
            ToolStripItemCollection items = _editorMenu.Items;
            foreach (ToolStripItem old in items.Cast<ToolStripItem>().ToList()) old.Dispose();
            items.Clear();
            items.Add(ThemedMenu.Item(Lang.T("Cut") + "\tCtrl+X", () => _canvas.EditorCommand(0)));
            items.Add(ThemedMenu.Item(Lang.T("Copy") + "\tCtrl+C", () => _canvas.EditorCommand(1)));
            items.Add(ThemedMenu.Item(Lang.T("Paste") + "\tCtrl+V", () => _canvas.EditorCommand(2)));
            items.Add(ThemedMenu.Item(Lang.T("Select all") + "\tCtrl+A", () => _canvas.EditorCommand(3)));
            items.Add(new ToolStripSeparator());
            ToolStripMenuItem insert = ThemedMenu.SubMenu(Lang.T("Insert field"));
            AddFieldItems(insert.DropDownItems);
            items.Add(insert);
        }

        /// <summary>
        /// Puts a field where it is wanted: at the cursor while typing on the page or in the Text box, in the current cell
        /// of a table, else in a new text box.
        /// </summary>
        private void InsertField(string token)
        {
            if (_canvas.InsertInEditor(token)) return;
            var text = Single as PdfTextItem;
            if (text != null)
            {
                int at = Math.Min(_txtText.SelectionStart, _txtText.TextLength);
                _txtText.Select(at, Math.Min(_txtText.SelectionLength, _txtText.TextLength - at));
                _txtText.SelectedText = token;   // TextChanged puts it on the page
                _txtText.Focus();
                FlushPending();
                return;
            }
            var table = Single as PdfTableItem;
            if (table != null)
            {
                _cells.EndEdit();
                int r = Math.Max(0, CurrentRow), c = Math.Max(0, CurrentColumn);
                string cell = table.Rows[r].Cells[c] ?? "";
                table.Rows[r].Cells[c] = cell.Length == 0 || cell.EndsWith(" ", StringComparison.Ordinal) ? cell + token : cell + " " + token;
                TableChanged(table, r, c);
                _status.Text = r == table.TemplateRow ? Lang.T("Field added to the row that repeats for each record.")
                             : table.DataRows ? Lang.T("Field added.")
                             : Lang.T("Field added. Turn on “Repeat for each record” (5. Data) to list every record in this table.");
                return;
            }
            var t = new PdfTextItem { Text = token, Width = Math.Min(70f, _doc.PageMm.Width - 30f) };
            Lay(t);
            Place(t, VisibleCentreMm());
            Insert(t);
            Commit();
        }

        private void ImportDataFile()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = Lang.T("Import data"),
                Filter = Lang.T("Excel or CSV") + " (*.xlsx;*.xlsm;*.csv)|*.xlsx;*.xlsm;*.csv|" + Lang.T("All files") + " (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                List<Student> rows;
                Cursor = Cursors.WaitCursor;
                try
                {
                    // Any table: the first row is the header, every column becomes a field
                    rows = ExcelStudentLoader.IsExcelFile(dlg.FileName)
                        ? ExcelStudentLoader.Load(dlg.FileName, false)
                        : CsvStudentLoader.Load(dlg.FileName, false);
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException ||
                                           ex is System.Xml.XmlException || ex is ArgumentException)
                {
                    Cursor = Cursors.Default;
                    AppDialog.Show(this, Lang.T("The file could not be read.") + "\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                finally { Cursor = Cursors.Default; }
                UseData(rows, Path.GetFileName(dlg.FileName));
            }
        }

        /// <summary>A header row and the rows under it, copied in Excel (or Word, Google Sheets).</summary>
        private void PasteData()
        {
            string text;
            try { text = Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch (ExternalException) { text = null; }
            List<string[]> rows = string.IsNullOrEmpty(text) ? new List<string[]>() : SplitCells(text);
            if (rows.Count < 2 || rows[0].Length < 1)
            {
                AppDialog.Show(this, Lang.T("Copy the header row and the rows under it in Excel first, then choose Paste rows again."), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            UseData(CsvStudentLoader.FromRows(rows.Select(r => r.ToList()).ToList(), "", false), Lang.T("Pasted data"));
        }

        private void UseData(List<Student> rows, string name)
        {
            if (rows.Count == 0)
            {
                AppDialog.Show(this, Lang.T("There are no rows under the header row."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _fileRecords = rows;
            _fileName = name;
            SetData(DataChoice.File);
        }

        /// <summary>
        /// A table listing the data: a header with the column names and one row of fields that repeats for each record
        /// ({No} first; at most 8 columns, more can be added with Insert field).
        /// </summary>
        private void AddDataTable()
        {
            List<string> fields = PdfFieldContext.FieldsOf(Records)
                .Where(f => !f.Equals("No", StringComparison.OrdinalIgnoreCase) && !f.Equals("QrData", StringComparison.OrdinalIgnoreCase)).ToList();
            if (fields.Count == 0) return;
            const int MaxColumns = 8;
            List<string> columns = fields.Take(MaxColumns - 1).ToList();
            PdfTableItem t = PdfTableItem.Create(2, columns.Count + 1);
            t.Rows[0].Cells[0] = Lang.T("No.");
            t.Rows[1].Cells[0] = "{No}";
            for (int c = 0; c < columns.Count; c++)
            {
                t.Rows[0].Cells[c + 1] = columns[c];
                t.Rows[1].Cells[c + 1] = "{" + columns[c] + "}";
            }
            t.ColumnWidths[0] = 0.45f;
            t.DataRows = true;
            t.Stripes = true;
            RectangleF box = MarginBox;
            t.Width = box.Width;
            Lay(t);
            t.X = box.X;
            // Under what is already on the page when there is room, else in the middle of the view
            float under = Page.Items.Count == 0 ? box.Y : Snap05(Page.Items.Max(i => i.Y + i.Height) + 6f);
            t.Y = under + t.Height <= box.Bottom ? Math.Max(box.Y, under)
                : Snap05(Math.Max(box.Y, Math.Min(box.Bottom - t.Height, VisibleCentreMm().Y - t.Height / 2f)));
            Insert(t);
            Commit();
            _status.Text = Lang.F("List table added: its second row repeats for each of the {0} records when you export.", Records.Count) +
                           (fields.Count > columns.Count ? "  " + Lang.F("{0} more columns can be added with Insert field.", fields.Count - columns.Count) : "");
        }

        private void ToggleOnePerRecord()
        {
            _canvas.EndTextEdit();
            _doc.OnePerRecord = !_doc.OnePerRecord;
            if (_props.SelectedObject is PdfPageSetup) _props.Refresh();
            Commit();
            if (_preview) BuildPreview();
            _status.Text = _doc.OnePerRecord
                ? Lang.F("Export writes the design once for each record: {0} copies.", Math.Max(1, Records.Count))
                : Lang.T("Export writes the design once; data tables list every record.");
        }

        private void ShowDataHelp()
        {
            string text = Lang.T("Type a field in curly brackets in any text or table cell, for example {NameKh} or {StudentId}. " +
                                 "Every column header of your list is a field, so {Phone} works when the list has a Phone column.") +
                          "\n\n" + Lang.T("Formats: {DateOfBirth:KhmerDate}  {StudentId:KhmerDigits}  {NameEn:Upper}  {Gender:KhmerGender}") +
                          "\n\n" + Lang.T("Also: {No} = row or record number, {Count} = number of records, {Date} = today, {Page} and {Pages}.") +
                          "\n\n" + Lang.T("A list: in a table, turn on “Repeat for each record” (5. Data). The row under the header is printed once " +
                                          "for every record; a long list continues on the next page with the header repeated.") +
                          "\n\n" + Lang.T("Objects inside the top or bottom margin (dotted line), such as “Page {Page} of {Pages}”, repeat on every page of a long list.") +
                          "\n\n" + Lang.T("Preview (F5) shows the pages as they are exported: the table with every record, and the next pages a long list needs.") +
                          "\n\n" + Lang.T("Certificates or letters: Data ▾ › One copy of the design for each record.") +
                          "\n\n" + Lang.T("The data is the student list of the main window, its selected students, or an Excel / CSV file " +
                                          "imported here (any columns; the first row is the header). ◀ ▶ at the bottom show another record.");
            AppDialog.Show(this, text, Lang.T("Data fields"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ===================== Context menu =====================

        private void ShowCanvasMenu(Point at, PointF mm)
        {
            ContextMenuStrip m = NewMenu();
            PdfItem s = Single;
            if (_canvas.SelectedItems.Count > 0)
            {
                if (s is PdfTextItem)
                {
                    m.Items.Add(ThemedMenu.Item("Edit text\tF2", EditSelected));
                    ToolStripMenuItem fields = ThemedMenu.SubMenu(Lang.T("Insert field"));
                    AddFieldItems(fields.DropDownItems);
                    m.Items.Add(fields);
                }
                if (s is PdfTableItem)
                {
                    m.Items.Add(ThemedMenu.Item("Edit cell\tF2", EditSelected));
                    m.Items.Add(ThemedMenu.Item("Add row", TableAddRow));
                    m.Items.Add(ThemedMenu.Item("Add column", TableAddColumn));
                    m.Items.Add(ThemedMenu.Item("Equal column widths", TableEqualWidths));
                }
                if (s is PdfImageItem) m.Items.Add(ThemedMenu.Item("Replace picture…", EditSelected));
                if (s != null) m.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem align = ThemedMenu.SubMenu(Lang.T("Align"));
                AddAlignItems(align.DropDownItems);
                m.Items.Add(align);
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(ThemedMenu.Item("Clone\tCtrl+D", CloneSelected));
                m.Items.Add(ThemedMenu.Item("Copy\tCtrl+C", CopySelected));
                m.Items.Add(ThemedMenu.Item("Cut\tCtrl+X", CutSelected));
                m.Items.Add(ThemedMenu.Item("Paste\tCtrl+V", Paste));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(ThemedMenu.Item("Bring to front\tCtrl+]", BringSelectedToFront));
                m.Items.Add(ThemedMenu.Item("Send to back\tCtrl+[", SendSelectedToBack));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(ThemedMenu.Item("Delete\tDel", DeleteSelected));
            }
            else
            {
                m.Items.Add(ThemedMenu.Item("Write text here", () => AddText(mm)));
                m.Items.Add(ThemedMenu.Item("Add table here", () => AddTable(mm)));
                m.Items.Add(ThemedMenu.Item("Add picture here…", () => AddPicture(mm)));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(ThemedMenu.Item("Paste\tCtrl+V", Paste));
                m.Items.Add(ThemedMenu.Item("Select all\tCtrl+A", SelectAll, Page.Items.Count > 0));
            }
            m.Show(_canvas, at);
        }

        private ContextMenuStrip NewMenu()
        {
            if (_menu != null) _menu.Dispose();
            _menu = ThemedMenu.Create();
            Lang.Hook(_menu);   // Khmer only: items are translated when the menu opens
            return _menu;
        }

        private static void ShowMenuUnder(ContextMenuStrip m, Control button)
        {
            m.Show(button, new Point(0, button.Height + 2));
        }

        // ===================== Keys and closing =====================

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_canvas.Editing)
            {
                // Typing on the page: Esc or Ctrl+Enter finishes, everything else goes to the text
                if (keyData == Keys.Escape || keyData == (Keys.Control | Keys.Enter)) { _canvas.EndTextEdit(); _canvas.Focus(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            switch (keyData)
            {
                case Keys.Control | Keys.S: SaveDesign(); return true;
                case Keys.Control | Keys.E: ExportPdf(); return true;
                case Keys.Control | Keys.N: NewDesign(); return true;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Add: ZoomBy(1.2f, null); return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract: ZoomBy(1f / 1.2f, null); return true;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0: FitZoom(); return true;
                case Keys.F5: TogglePreview(); return true;
            }

            if (_preview)
            {
                // Preview: turn the pages; the editing keys do nothing until F5 / Esc goes back to the design
                switch (keyData)
                {
                    case Keys.PageUp:
                    case Keys.Left: TurnPage(-1); return true;
                    case Keys.PageDown:
                    case Keys.Right: TurnPage(1); return true;
                    case Keys.Home: TurnPage(-_previewIndex); return true;
                    case Keys.End: TurnPage(_previewPages == null ? 0 : _previewPages.Count); return true;
                    case Keys.Escape: EndPreview(); return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            bool inText = _txtText.Focused || _props.ContainsFocus || (_cells.ContainsFocus && _cells.IsCurrentCellInEditMode);
            if (!inText)
            {
                switch (keyData)
                {
                    case Keys.Control | Keys.Z: Undo(); return true;
                    case Keys.Control | Keys.Y:
                    case Keys.Control | Keys.Shift | Keys.Z: Redo(); return true;
                    case Keys.PageUp: TurnPage(-1); return true;
                    case Keys.PageDown: TurnPage(1); return true;
                    case Keys.Control | Keys.PageUp: TurnRecord(-1); return true;
                    case Keys.Control | Keys.PageDown: TurnRecord(1); return true;
                }
            }

            if (_canvas.Focused)
            {
                switch (keyData)
                {
                    case Keys.Control | Keys.D: CloneSelected(); return true;
                    case Keys.Control | Keys.A: SelectAll(); return true;
                    case Keys.Control | Keys.C: CopySelected(); return true;
                    case Keys.Control | Keys.X: CutSelected(); return true;
                    case Keys.Control | Keys.V: Paste(); return true;
                    case Keys.Control | Keys.OemCloseBrackets: BringSelectedToFront(); return true;
                    case Keys.Control | Keys.OemOpenBrackets: SendSelectedToBack(); return true;
                    case Keys.Delete:
                    case Keys.Back: DeleteSelected(); return true;
                    case Keys.F2:
                    case Keys.Enter: EditSelected(); return true;
                    case Keys.Escape:
                        if (_canvas.SelectedItems.Count > 0) { Select(null); return true; }
                        Close();
                        return true;
                }
                if (_canvas.SelectedItems.Count > 0)
                {
                    float step = (keyData & Keys.Shift) != 0 ? BigNudgeMm : NudgeMm;
                    switch (keyData & ~Keys.Shift)
                    {
                        case Keys.Left: Nudge(-step, 0f); return true;
                        case Keys.Right: Nudge(step, 0f); return true;
                        case Keys.Up: Nudge(0f, -step); return true;
                        case Keys.Down: Nudge(0f, step); return true;
                    }
                }
            }
            else if (keyData == Keys.Escape && !inText)
            {
                if (_txtText.Focused || _cells.ContainsFocus) { _canvas.Focus(); return true; }
                Close();
                return true;
            }
            else if (keyData == Keys.Escape && _txtText.Focused)
            {
                _canvas.Focus();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Alt+drag clones: releasing Alt afterwards must not put the window into menu mode.</summary>
        protected override void WndProc(ref Message m)
        {
            const int WM_SYSCOMMAND = 0x0112, SC_KEYMENU = 0xF100;
            if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_KEYMENU && m.LParam == IntPtr.Zero) return;
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!e.Cancel && !ConfirmDiscard()) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _commitTimer.Dispose();
                _tip.Dispose();
                _editorMenu.Dispose();
                if (_menu != null) _menu.Dispose();
            }
            base.Dispose(disposing);
        }

        // ===================== Small UI helpers =====================

        private bool Confirm(string text)
        {
            return AppDialog.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        /// <summary>
        /// Toolbar button. Afterwards the keyboard goes back to the page (or to the cell grid for the table buttons), so
        /// Del, Ctrl+D and the arrow keys keep working; menu buttons (<paramref name="refocus"/> false) leave the focus alone.
        /// </summary>
        private PillButton Tool(Control parent, string text, Action action, string tip, bool refocus = true, Control focusAfter = null)
        {
            PillButton b = ModalUi.ToolButton(text, (s, e) =>
            {
                action();
                if (!refocus || _canvas.Editing || !ContainsFocus || !(ActiveControl is Button)) return;
                Control target = focusAfter != null && focusAfter.Visible ? focusAfter : _canvas;
                if (target.CanFocus) target.Focus();
            });
            parent.Controls.Add(b);
            _tip.SetTip(b, tip);
            return b;
        }

        /// <summary>Small button of the Text / Table panel; <paramref name="menu"/> = it opens a menu (the focus stays).</summary>
        private PillButton SmallTool(Control parent, string text, Action action, string tip, bool menu = false)
        {
            PillButton b = Tool(parent, text, action, tip, !menu, _cells);
            b.MinimumSize = new Size(0, 30);
            b.Padding = new Padding(6, 0, 6, 0);
            b.Margin = new Padding(0, 0, 4, 4);
            return b;
        }

        private static void Divider(Control bar)
        {
            bar.Controls.Add(new Panel { Width = 1, Height = 24, BackColor = Theme.Border, Margin = new Padding(6, 4, 12, 0) });
        }

        private static Label SmallNote(string text)
        {
            return new Label
            {
                Text = text, Dock = DockStyle.Bottom, AutoSize = false, Height = 36, ForeColor = Theme.TextMuted, BackColor = Theme.Surface,
                Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false
            };
        }
    }

    /// <summary>What the property list shows when nothing is selected: the design's name, paper and pages.</summary>
    internal sealed class PdfPageSetup
    {
        private readonly PdfDesign _d;
        private readonly int _page;

        public PdfPageSetup(PdfDesign design, int page)
        {
            _d = design;
            _page = page;
        }

        [Category("1. Design"), DisplayName("Name"), Description("Name of the design in the Design menu; also the title of the PDF.")]
        public string Name
        {
            get { return _d.Name; }
            set { if (!string.IsNullOrWhiteSpace(value)) _d.Name = value.Trim(); }
        }

        [Category("2. Paper"), DisplayName("Paper size"), TypeConverter(typeof(PaperNameConverter)),
         Description("Every page of the design has this size. Objects keep their place in mm.")]
        public string Paper
        {
            get { return _d.PaperName; }
            set { _d.Paper = value; }
        }

        [Category("2. Paper"), DisplayName("Orientation")]
        public PdfOrientation Orientation
        {
            get { return _d.Landscape ? PdfOrientation.Landscape : PdfOrientation.Portrait; }
            set { _d.Landscape = value == PdfOrientation.Landscape; }
        }

        [Category("2. Paper"), DisplayName("Size (mm)"), ReadOnly(true)]
        public string SizeMm
        {
            get
            {
                SizeF s = _d.PageMm;
                return s.Width.ToString("0.#", CultureInfo.CurrentCulture) + " × " + s.Height.ToString("0.#", CultureInfo.CurrentCulture);
            }
        }

        [Category("2. Paper"), DisplayName("Margin (mm)"),
         Description("Space kept free around the page: Align ▾ lines objects up with it (dotted line on the page), " +
                     "and a long data table continues on the next page at this distance from the edges.")]
        public float Margin
        {
            get { return _d.MarginMm; }
            set { _d.MarginMm = value; }
        }

        [Category("4. Data"), DisplayName("One copy per record"),
         Description("Export writes the whole design once for each record of the data (certificates, letters), " +
                     "with its fields filled in. Off: the design once; data tables list every record.")]
        public bool OnePerRecord
        {
            get { return _d.OnePerRecord; }
            set { _d.OnePerRecord = value; }
        }

        [Category("3. Pages"), DisplayName("Pages"), ReadOnly(true), Description("Page ▾ adds, duplicates or deletes pages.")]
        public int Pages { get { return _d.Pages.Count; } }

        [Category("3. Pages"), DisplayName("Objects on this page"), ReadOnly(true)]
        public int Objects { get { return _page < _d.Pages.Count ? _d.Pages[_page].Items.Count : 0; } }
    }

    /// <summary>Scrolling panel that does not jump to the page canvas when it gets the focus.</summary>
    internal sealed class PdfScrollHost : Panel
    {
        protected override Point ScrollToControl(Control activeControl)
        {
            return DisplayRectangle.Location;
        }
    }

    /// <summary>
    /// The page of PDF Designer: draws it (through <see cref="PdfDesignRenderer"/>) and lets the user select, move, resize
    /// and clone objects, drag table column lines and type text in place. Shift / Ctrl+click adds objects to the
    /// selection, dragging on the empty page draws a box that selects what it touches; a drag moves every selected
    /// object. Changes are made to the design directly; the form records them for undo when <see cref="ItemChanged"/> /
    /// <see cref="TextEditEnded"/> fire.
    /// </summary>
    internal sealed class PdfPageCanvas : Control
    {
        public const int Gutter = 28;
        private const float HandlePx = 8f;
        private const float SnapPx = 6f;
        private const float MinColumnMm = 3f;

        private enum Drag { None, Move, Resize, Column, Box }

        private readonly List<PdfItem> _selection = new List<PdfItem>();
        private PdfItem _selected, _hover;   // _selected: the one clicked last
        private Drag _drag;
        private int _handle, _column;
        private PointF _startMm;
        private RectangleF _startBounds;
        private Dictionary<PdfItem, PointF> _startPositions;
        private PdfItem _collapseTo;         // click on one of several selected: it alone stays selected unless dragged
        private List<PdfItem> _boxBase;      // selection before the box (Shift+drag adds to it)
        private RectangleF _boxMm;
        private float[] _startWidths;
        private bool _moved, _cloneOnDrag, _cloned;
        private float? _guideX, _guideY;
        private int _hoverColumn = -1;
        private TextBox _editor;
        private PdfTextItem _editing;
        private string _editStart;

        public PdfDesign Design { get; set; }
        public PdfDesignPage Page { get; set; }

        /// <summary>Pixels per millimetre.</summary>
        public float Zoom { get; set; }

        /// <summary>What the data fields show (null = their names).</summary>
        public PdfFieldContext Fields { get; set; }

        /// <summary>Records in the data: shown on the badge of tables that repeat for each record.</summary>
        public int RecordCount { get; set; }

        /// <summary>
        /// Preview: the page is an exported page (fields filled, data tables with every record). Nothing can be selected
        /// or moved; margins, badges and the outlines of empty text boxes are not drawn; a double-click asks to leave.
        /// </summary>
        public bool ReadOnly { get; set; }

        /// <summary>Shown above the paper (preview page number); null = nothing.</summary>
        public string Caption { get; set; }

        public event EventHandler PreviewDoubleClicked;

        /// <summary>Right-click menu of the text box for typing on the page.</summary>
        public ContextMenuStrip EditorMenu { get; set; }

        /// <summary>Where the cursor was when typing on the page ended (Insert field continues there).</summary>
        public int EditorCaret { get; private set; }

        /// <summary>Row and column of the table cell that is current in the cell grid (-1 = none).</summary>
        public int CellRow { get; private set; }
        public int CellColumn { get; private set; }

        public event EventHandler SelectionChanged, ItemMoving, ItemChanged, TextEditing;
        public event Action<PdfTextItem, bool> TextEditEnded;           // item, text changed
        public event Action<PointF> EmptyDoubleClicked;                  // mm on the page
        public event Action<PdfTableItem, int, int> CellClicked, CellDoubleClicked;
        public event Action<PdfImageItem> PictureDoubleClicked;
        public event Action<Point, PointF> MenuRequested;                // client point, mm
        public event Action<float, Point> ZoomRequested;                 // factor, client point

        public PdfPageCanvas()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Theme.AppBack;
            Zoom = 3f;
            CellRow = CellColumn = -1;
        }

        /// <summary>The selected object (the one clicked last when several are selected), or null.</summary>
        public PdfItem Selected { get { return _selected; } }

        public IList<PdfItem> SelectedItems { get { return _selection.AsReadOnly(); } }

        public bool Editing { get { return _editor != null; } }

        /// <summary>Selects an item (null = none); <paramref name="raise"/> fires SelectionChanged.</summary>
        public void SelectItem(PdfItem item, bool raise)
        {
            if (item == _selected && _selection.Count == (item == null ? 0 : 1)) return;
            SetSelection(item == null ? new PdfItem[0] : new[] { item }, item, raise);
        }

        /// <summary>Selects these items (the last one is the one "clicked last").</summary>
        public void SelectItems(IEnumerable<PdfItem> items, bool raise)
        {
            SetSelection(items, null, raise);
        }

        private void SetSelection(IEnumerable<PdfItem> items, PdfItem primary, bool raise)
        {
            List<PdfItem> list = items.Where(i => i != null).Distinct().ToList();
            if (_editing != null && !(list.Count == 1 && list[0] == _editing)) EndTextEdit();
            PdfItem was = _selected;
            _selection.Clear();
            _selection.AddRange(list);
            _selected = primary != null && list.Contains(primary) ? primary : list.LastOrDefault();
            if (_selected != was || list.Count != 1) CellRow = CellColumn = -1;
            Invalidate();
            if (raise && SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        private bool IsSelected(PdfItem item) { return item != null && _selection.Contains(item); }

        /// <summary>The one selected object when exactly one is selected (handles and column lines are shown for it).</summary>
        private PdfItem Single { get { return _selection.Count == 1 ? _selected : null; } }

        public void SetCell(int row, int column)
        {
            CellRow = row;
            CellColumn = column;
            Invalidate();
        }

        // ---------- Coordinates ----------

        /// <summary>The paper on screen, in client pixels.</summary>
        public RectangleF PageRect
        {
            get
            {
                SizeF p = Design == null ? new SizeF(210f, 297f) : Design.PageMm;
                float w = p.Width * Zoom, h = p.Height * Zoom;
                float x = (float)Math.Round(Math.Max(Gutter, (Width - w) / 2f));
                return new RectangleF(x, Gutter, w, h);
            }
        }

        public PointF ToMm(Point p)
        {
            RectangleF r = PageRect;
            return new PointF((p.X - r.X) / Zoom, (p.Y - r.Y) / Zoom);
        }

        public PointF ToPx(PointF mm)
        {
            RectangleF r = PageRect;
            return new PointF(r.X + mm.X * Zoom, r.Y + mm.Y * Zoom);
        }

        public RectangleF ToPx(RectangleF mm)
        {
            PointF a = ToPx(mm.Location);
            return new RectangleF(a.X, a.Y, mm.Width * Zoom, mm.Height * Zoom);
        }

        // ---------- Painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.AppBack);
            if (Design == null || Page == null) return;
            RectangleF pr = PageRect;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.DrawShadow(g, pr, 2f, Color.Black, 8, Theme.Dark ? 60 : 28, 2f);
            g.FillRectangle(Brushes.White, pr);

            // Margins (Align ▾ lines up with them; not printed)
            float m = Design.Margin;
            if (m > 0f && !ReadOnly)
                using (var pen = new Pen(Color.FromArgb(70, 110, 140, 210)) { DashStyle = DashStyle.Dot })
                {
                    RectangleF box = ToPx(new RectangleF(m, m, Design.PageMm.Width - 2f * m, Design.PageMm.Height - 2f * m));
                    g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                }

            GraphicsState state = g.Save();
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SetClip(pr);
            g.TranslateTransform(pr.X, pr.Y);
            g.ScaleTransform(Zoom, Zoom);
            PdfDesignRenderer.DrawPage(g, Page, _editing, Fields);   // the text being typed is in the text box on top
            g.Restore(state);

            g.SmoothingMode = SmoothingMode.None;

            if (!string.IsNullOrEmpty(Caption))
                using (var font = new Font(CardFonts.UI, 9f, FontStyle.Bold))
                    TextRenderer.DrawText(g, Caption, font, new Rectangle((int)pr.X, 0, (int)pr.Width, Gutter), Theme.Accent,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (ReadOnly) return;   // preview: the page as it prints

            // Empty text boxes would be invisible: show where they are
            using (var faint = new Pen(Color.FromArgb(150, 160, 165, 185)) { DashStyle = DashStyle.Dot })
                foreach (PdfTextItem t in Page.Items.OfType<PdfTextItem>())
                    if (string.IsNullOrWhiteSpace(t.Text) && !IsSelected(t)) g.DrawRectangle(faint, Rectangle.Round(ToPx(t.Bounds)));

            foreach (PdfTableItem t in Page.Items.OfType<PdfTableItem>()) DrawRepeatBadge(g, t);

            if (_hover != null && !IsSelected(_hover) && Page.Items.Contains(_hover))
                using (var pen = new Pen(Color.FromArgb(160, Theme.Accent)) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, Rectangle.Round(ToPx(_hover.Bounds)));

            if (Single != null && Page.Items.Contains(Single)) DrawSelection(g);
            else if (_selection.Count > 1)
            {
                // Several: each one outlined, the one clicked last a little stronger
                foreach (PdfItem item in _selection)
                {
                    if (!Page.Items.Contains(item)) continue;
                    RectangleF r = ToPx(item.Bounds);
                    using (var pen = new Pen(Theme.Accent, item == _selected ? 2.5f : 1.5f)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                }
            }

            // Selection box
            if (_drag == Drag.Box && !_boxMm.IsEmpty)
            {
                RectangleF r = ToPx(_boxMm);
                using (var fill = new SolidBrush(Color.FromArgb(40, Theme.Accent))) g.FillRectangle(fill, r);
                using (var pen = new Pen(Theme.Accent) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }

            // Snap guides
            using (var guide = new Pen(Color.FromArgb(230, 64, 120)) { DashStyle = DashStyle.Dash })
            {
                if (_guideX.HasValue) { float x = ToPx(new PointF(_guideX.Value, 0)).X; g.DrawLine(guide, x, pr.Top, x, pr.Bottom); }
                if (_guideY.HasValue) { float y = ToPx(new PointF(0, _guideY.Value)).Y; g.DrawLine(guide, pr.Left, y, pr.Right, y); }
            }
        }

        /// <summary>"↻ 120" beside the row of a table that repeats for each record (on screen only).</summary>
        private void DrawRepeatBadge(Graphics g, PdfTableItem t)
        {
            int row = t.TemplateRow;
            if (row < 0) return;
            float[] ys = PdfDesignRenderer.RowEdges(t);
            if (row + 1 >= ys.Length) return;
            RectangleF r = ToPx(RectangleF.FromLTRB(t.X, ys[row], t.X + t.Width, ys[row + 1]));
            string label = "↻ " + (RecordCount > 0 ? RecordCount.ToString("N0", CultureInfo.CurrentCulture) : "");
            using (var font = new Font(CardFonts.UI, 8f, FontStyle.Bold))
            {
                Size size = TextRenderer.MeasureText(label.Trim(), font);
                var badge = new Rectangle(0, (int)(r.Top + (r.Height - size.Height - 2) / 2f), size.Width + 8, size.Height + 2);
                badge.X = r.Left - badge.Width - 4 >= 2 ? (int)r.Left - badge.Width - 4 : (int)r.Right + 4;
                using (var fill = new SolidBrush(Theme.Accent)) g.FillRectangle(fill, badge);
                TextRenderer.DrawText(g, label.Trim(), font, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using (var pen = new Pen(Color.FromArgb(150, Theme.Accent), 2f))
                    g.DrawLine(pen, r.Left - 1, r.Top + 1, r.Left - 1, r.Bottom - 1);
            }
        }

        private void DrawSelection(Graphics g)
        {
            PdfItem item = Single;
            RectangleF r = ToPx(item.Bounds);
            var table = item as PdfTableItem;
            if (table != null)
            {
                float[] xs = PdfDesignRenderer.ColumnEdges(table), ys = PdfDesignRenderer.RowEdges(table);
                // Current cell of the cell grid
                if (CellRow >= 0 && CellColumn >= 0 && CellRow < ys.Length - 1 && CellColumn < xs.Length - 1)
                {
                    RectangleF cell = ToPx(RectangleF.FromLTRB(xs[CellColumn], ys[CellRow], xs[CellColumn + 1], ys[CellRow + 1]));
                    using (var fill = new SolidBrush(Color.FromArgb(50, Theme.Accent))) g.FillRectangle(fill, cell);
                    using (var pen = new Pen(Theme.Accent, 2f)) g.DrawRectangle(pen, Rectangle.Round(cell));
                }
                // Column lines that can be dragged
                int hot = _drag == Drag.Column ? _column : _hoverColumn;
                for (int c = 1; c < xs.Length - 1; c++)
                {
                    float x = ToPx(new PointF(xs[c], 0)).X;
                    using (var pen = new Pen(c == hot ? Theme.Accent : Color.FromArgb(120, Theme.Accent), c == hot ? 3f : 1f))
                        g.DrawLine(pen, x, r.Top - 6, x, r.Bottom + 6);
                    using (var knob = new SolidBrush(Theme.Accent))
                        g.FillRectangle(knob, x - 3, r.Top - 9, 6, 6);
                }
            }

            using (var pen = new Pen(Theme.Accent, 1.5f)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            if (_editing == item) return;
            PointF[] hs = HandlePoints(item);
            using (var fill = new SolidBrush(Color.White))
            using (var pen = new Pen(Theme.Accent, 1.5f))
                for (int i = 0; i < hs.Length; i++)
                {
                    if (!HandleActive(item, i)) continue;
                    RectangleF h = HandleRect(hs[i]);
                    g.FillRectangle(fill, h);
                    g.DrawRectangle(pen, h.X, h.Y, h.Width, h.Height);
                }
        }

        /// <summary>Handle centres: TL, T, TR, R, BR, B, BL, L.</summary>
        private PointF[] HandlePoints(PdfItem item)
        {
            RectangleF r = ToPx(item.Bounds);
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            return new[]
            {
                new PointF(r.Left, r.Top), new PointF(cx, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, cy),
                new PointF(r.Right, r.Bottom), new PointF(cx, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, cy)
            };
        }

        /// <summary>Text and tables only change width (their height follows the content); pictures use all eight handles.</summary>
        private static bool HandleActive(PdfItem item, int handle)
        {
            return item.ResizesHeight || handle == 3 || handle == 7;
        }

        private static RectangleF HandleRect(PointF centre)
        {
            return new RectangleF(centre.X - HandlePx / 2f, centre.Y - HandlePx / 2f, HandlePx, HandlePx);
        }

        // ---------- Hit testing ----------

        private int HandleAt(Point p)
        {
            PdfItem item = Single;
            if (item == null || _editing == item) return -1;
            PointF[] hs = HandlePoints(item);
            for (int i = 0; i < hs.Length; i++)
            {
                if (!HandleActive(item, i)) continue;
                RectangleF h = HandleRect(hs[i]);
                h.Inflate(3f, 3f);
                if (h.Contains(p)) return i;
            }
            return -1;
        }

        private int ColumnLineAt(Point p)
        {
            var t = Single as PdfTableItem;
            if (t == null) return -1;
            RectangleF r = ToPx(t.Bounds);
            if (p.Y < r.Top - 10 || p.Y > r.Bottom + 4) return -1;
            float[] xs = PdfDesignRenderer.ColumnEdges(t);
            for (int c = 1; c < xs.Length - 1; c++)
                if (Math.Abs(ToPx(new PointF(xs[c], 0)).X - p.X) <= 4f) return c;
            return -1;
        }

        /// <summary>Topmost item under the point (a few pixels of slack around thin items).</summary>
        private PdfItem ItemAt(Point p)
        {
            if (Page == null) return null;
            PointF mm = ToMm(p);
            float slack = 3f / Zoom;
            for (int i = Page.Items.Count - 1; i >= 0; i--)
            {
                RectangleF b = Page.Items[i].Bounds;
                b.Inflate(slack, slack);
                if (b.Contains(mm)) return Page.Items[i];
            }
            return null;
        }

        private static Cursor HandleCursor(int h)
        {
            switch (h)
            {
                case 0: case 4: return Cursors.SizeNWSE;
                case 2: case 6: return Cursors.SizeNESW;
                case 1: case 5: return Cursors.SizeNS;
                default: return Cursors.SizeWE;
            }
        }

        private static RectangleF Union(IEnumerable<PdfItem> items)
        {
            RectangleF u = RectangleF.Empty;
            bool first = true;
            foreach (PdfItem i in items)
            {
                u = first ? i.Bounds : RectangleF.Union(u, i.Bounds);
                first = false;
            }
            return u;
        }

        // ---------- Mouse ----------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();   // also ends typing in place (the text box loses the focus)
            base.OnMouseDown(e);
            if (Page == null || ReadOnly) return;

            if (e.Button == MouseButtons.Right)
            {
                PdfItem hit = ItemAt(e.Location);
                if (!IsSelected(hit)) SelectItem(hit, true);   // right-click on one of several keeps them all
                if (MenuRequested != null) MenuRequested(e.Location, ToMm(e.Location));
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            _startMm = ToMm(e.Location);
            _moved = _cloned = false;
            _collapseTo = null;
            int handle = HandleAt(e.Location);
            if (handle >= 0)
            {
                _drag = Drag.Resize;
                _handle = handle;
                _startBounds = Single.Bounds;
                return;
            }
            int column = ColumnLineAt(e.Location);
            if (column >= 0)
            {
                var t = (PdfTableItem)Single;
                float[] xs = PdfDesignRenderer.ColumnEdges(t);
                _startWidths = new float[xs.Length - 1];
                for (int c = 0; c < _startWidths.Length; c++) _startWidths[c] = xs[c + 1] - xs[c];
                _drag = Drag.Column;
                _column = column;
                return;
            }

            PdfItem item = ItemAt(e.Location);
            bool add = (ModifierKeys & (Keys.Shift | Keys.Control)) != 0;
            if (item == null)
            {
                // Empty page: drag a box to select what it touches (Shift / Ctrl: add to the selection)
                _drag = Drag.Box;
                _boxMm = RectangleF.Empty;
                _boxBase = add ? _selection.ToList() : new List<PdfItem>();
                if (!add && _selection.Count > 0) SetSelection(new PdfItem[0], null, true);
                return;
            }
            if (add)
            {
                if (IsSelected(item))
                {
                    // Shift+click on a selected object takes it out of the selection
                    SetSelection(_selection.Where(i => i != item).ToList(), _selected == item ? null : _selected, true);
                    _drag = Drag.None;
                    return;
                }
                SetSelection(_selection.Concat(new[] { item }).ToList(), item, true);
            }
            else if (!IsSelected(item)) SetSelection(new[] { item }, item, true);
            else if (_selection.Count > 1)
            {
                _collapseTo = item;   // a click (no drag) on one of several selects it alone
                _selected = item;
                Invalidate();
            }

            _drag = Drag.Move;
            _startBounds = Union(_selection);
            _startPositions = _selection.ToDictionary(i => i, i => new PointF(i.X, i.Y));
            _cloneOnDrag = (ModifierKeys & Keys.Alt) != 0;

            var table = Single as PdfTableItem;
            int row, col;
            if (table != null && PdfDesignRenderer.CellAt(table, _startMm, out row, out col))
            {
                SetCell(row, col);
                if (CellClicked != null) CellClicked(table, row, col);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Page == null) return;
            if (ReadOnly)
            {
                Cursor = Cursors.Default;
                return;
            }
            if (_drag == Drag.None)
            {
                int h = HandleAt(e.Location);
                int column = h < 0 ? ColumnLineAt(e.Location) : -1;
                PdfItem over = h < 0 && column < 0 ? ItemAt(e.Location) : _selected;
                Cursor = h >= 0 ? HandleCursor(h)
                       : column >= 0 ? Cursors.VSplit
                       : over != null ? ((ModifierKeys & Keys.Alt) != 0 ? Cursors.Cross : Cursors.SizeAll)
                       : Cursors.Default;
                if (over != _hover || column != _hoverColumn)
                {
                    _hover = over;
                    _hoverColumn = column;
                    Invalidate();
                }
                return;
            }

            PointF mm = ToMm(e.Location);
            float dx = mm.X - _startMm.X, dy = mm.Y - _startMm.Y;
            if (!_moved && Math.Abs(dx * Zoom) < 3f && Math.Abs(dy * Zoom) < 3f) return;   // a click, not a drag yet
            _moved = true;

            switch (_drag)
            {
                case Drag.Move:
                    if (_cloneOnDrag && !_cloned) CloneForDrag();
                    float x = _startBounds.X + dx, y = _startBounds.Y + dy;
                    SnapMove(ref x, ref y, _startBounds.Size);
                    float ox = x - _startBounds.X, oy = y - _startBounds.Y;
                    foreach (PdfItem item in _selection)
                    {
                        PointF start = _startPositions[item];
                        item.X = start.X + ox;
                        item.Y = start.Y + oy;
                    }
                    break;
                case Drag.Resize:
                    ResizeTo(mm);
                    break;
                case Drag.Column:
                    DragColumn(mm.X);
                    break;
                case Drag.Box:
                    _boxMm = RectangleF.FromLTRB(Math.Min(_startMm.X, mm.X), Math.Min(_startMm.Y, mm.Y), Math.Max(_startMm.X, mm.X), Math.Max(_startMm.Y, mm.Y));
                    SetSelection(_boxBase.Concat(Page.Items.Where(i => i.Bounds.IntersectsWith(_boxMm))).ToList(), null, false);
                    break;
            }
            Invalidate();
            if (_drag != Drag.Box && ItemMoving != null) ItemMoving(this, EventArgs.Empty);
        }

        /// <summary>Alt+drag: copies of the selected objects move, the originals stay where they were.</summary>
        private void CloneForDrag()
        {
            var copies = new List<PdfItem>();
            var positions = new Dictionary<PdfItem, PointF>();
            PdfItem primary = null;
            foreach (PdfItem source in _selection.OrderBy(i => Page.Items.IndexOf(i)).ToList())
            {
                PdfItem copy = source.Clone();
                Page.Items.Insert(Page.Items.IndexOf(source) + 1, copy);
                copies.Add(copy);
                positions[copy] = _startPositions[source];
                if (source == _selected) primary = copy;
            }
            _startPositions = positions;
            _collapseTo = null;
            _cloned = true;
            SetSelection(copies, primary, true);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Drag was = _drag;
            _drag = Drag.None;
            _guideX = _guideY = null;
            Invalidate();
            if (was == Drag.Box)
            {
                _boxMm = RectangleF.Empty;
                if (_moved && SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                return;
            }
            if (was == Drag.Move && !_moved && _collapseTo != null) SetSelection(new[] { _collapseTo }, _collapseTo, true);
            _collapseTo = null;
            if (was != Drag.None && (_moved || _cloned) && ItemChanged != null) ItemChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover == null && _hoverColumn < 0) return;
            _hover = null;
            _hoverColumn = -1;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (ReadOnly)
            {
                if (e.Button == MouseButtons.Left && PreviewDoubleClicked != null) PreviewDoubleClicked(this, EventArgs.Empty);
                return;
            }
            if (e.Button != MouseButtons.Left || Page == null || HandleAt(e.Location) >= 0 || ColumnLineAt(e.Location) >= 0) return;
            if ((ModifierKeys & (Keys.Shift | Keys.Control)) != 0) return;   // Shift+clicking fast is not a double-click
            PdfItem item = ItemAt(e.Location);
            PointF mm = ToMm(e.Location);
            var text = item as PdfTextItem;
            var table = item as PdfTableItem;
            var image = item as PdfImageItem;
            int row, col;
            if (text != null) BeginTextEdit(text);
            else if (table != null && PdfDesignRenderer.CellAt(table, mm, out row, out col))
            {
                if (CellDoubleClicked != null) CellDoubleClicked(table, row, col);
            }
            else if (image != null)
            {
                if (PictureDoubleClicked != null) PictureDoubleClicked(image);
            }
            else if (item == null && Design != null && new RectangleF(PointF.Empty, Design.PageMm).Contains(mm))
            {
                if (EmptyDoubleClicked != null) EmptyDoubleClicked(mm);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0)
            {
                if (ZoomRequested != null) ZoomRequested(e.Delta > 0 ? 1.15f : 1f / 1.15f, e.Location);
                var handled = e as HandledMouseEventArgs;
                if (handled != null) handled.Handled = true;
                return;
            }
            base.OnMouseWheel(e);   // not handled: Windows passes it on and the page scrolls
        }

        // ---------- Dragging ----------

        /// <summary>
        /// Lines the moved object(s) up with the page centre, edges and margins and with the other objects (left /
        /// centre / right, top / middle / bottom) when within a few pixels; otherwise keeps them on a 0.5 mm grid.
        /// </summary>
        private void SnapMove(ref float x, ref float y, SizeF size)
        {
            float tol = SnapPx / Zoom;
            SizeF page = Design.PageMm;
            float m = Design.Margin;
            var xs = new List<float> { 0f, page.Width / 2f, page.Width };
            var ys = new List<float> { 0f, page.Height / 2f, page.Height };
            if (m > 0f)
            {
                xs.Add(m); xs.Add(page.Width - m);
                ys.Add(m); ys.Add(page.Height - m);
            }
            foreach (PdfItem other in Page.Items)
            {
                if (IsSelected(other)) continue;
                xs.Add(other.X); xs.Add(other.X + other.Width / 2f); xs.Add(other.X + other.Width);
                ys.Add(other.Y); ys.Add(other.Y + other.Height / 2f); ys.Add(other.Y + other.Height);
            }
            _guideX = SnapAxis(ref x, size.Width, xs, tol);
            _guideY = SnapAxis(ref y, size.Height, ys, tol);
        }

        private static float? SnapAxis(ref float start, float size, List<float> lines, float tol)
        {
            float best = tol, shift = 0f;
            float? guide = null;
            float[] own = { 0f, size / 2f, size };
            foreach (float line in lines)
                foreach (float o in own)
                {
                    float d = line - (start + o);
                    if (Math.Abs(d) < best) { best = Math.Abs(d); shift = d; guide = line; }
                }
            if (guide.HasValue) start += shift;
            else start = (float)Math.Round(start * 2f) / 2f;
            return guide;
        }

        private void ResizeTo(PointF mm)
        {
            PdfItem item = Single;
            if (item == null) return;
            float x = (float)Math.Round(mm.X * 2f) / 2f, y = (float)Math.Round(mm.Y * 2f) / 2f;
            RectangleF b = _startBounds;
            float l = b.Left, t = b.Top, r = b.Right, bo = b.Bottom, min = PdfItem.MinSizeMm;
            switch (_handle)
            {
                case 0: l = Math.Min(x, r - min); t = Math.Min(y, bo - min); break;
                case 1: t = Math.Min(y, bo - min); break;
                case 2: r = Math.Max(x, l + min); t = Math.Min(y, bo - min); break;
                case 3: r = Math.Max(x, l + min); break;
                case 4: r = Math.Max(x, l + min); bo = Math.Max(y, t + min); break;
                case 5: bo = Math.Max(y, t + min); break;
                case 6: l = Math.Min(x, r - min); bo = Math.Max(y, t + min); break;
                case 7: l = Math.Min(x, r - min); break;
            }

            var image = item as PdfImageItem;
            bool corner = _handle == 0 || _handle == 2 || _handle == 4 || _handle == 6;
            if (image != null && image.KeepRatio && corner)
            {
                // The width leads; the height follows the picture's proportions
                float h = (r - l) * image.Ratio;
                if (_handle == 0 || _handle == 2) t = bo - h;
                else bo = t + h;
            }

            item.X = l;
            item.Width = r - l;
            if (item.ResizesHeight)
            {
                item.Y = t;
                item.Height = bo - t;
            }
            PdfDesignRenderer.Layout(item, item == _editing ? null : Fields);
            if (_editing == item) PlaceEditor();
        }

        /// <summary>Moves the line between column <see cref="_column"/> − 1 and <see cref="_column"/>; the others keep their width.</summary>
        private void DragColumn(float xMm)
        {
            var t = Single as PdfTableItem;
            if (t == null || _startWidths == null || _column < 1 || _column >= _startWidths.Length) return;
            float left = t.X;
            for (int c = 0; c < _column - 1; c++) left += _startWidths[c];
            float pair = _startWidths[_column - 1] + _startWidths[_column];
            float at = Math.Max(left + MinColumnMm, Math.Min(left + pair - MinColumnMm, (float)Math.Round(xMm * 2f) / 2f));
            var widths = (float[])_startWidths.Clone();
            widths[_column - 1] = at - left;
            widths[_column] = pair - widths[_column - 1];
            t.ColumnWidths = widths.ToList();
            PdfDesignRenderer.Layout(t, Fields);
        }

        // ---------- Typing in place ----------

        /// <summary>Opens a text box over <paramref name="t"/> to type in. Esc, Ctrl+Enter or a click elsewhere finishes.</summary>
        public void BeginTextEdit(PdfTextItem t)
        {
            if (_editing == t) return;
            EndTextEdit();
            if (Single != t) SetSelection(new[] { t }, t, true);
            _editing = t;
            _editStart = t.Text ?? "";
            PdfDesignRenderer.Layout(t);   // while typing, the box fits the text as typed ({NameKh}, not the value)

            var box = new TextBox
            {
                Multiline = true, AcceptsReturn = true, AcceptsTab = false, WordWrap = true, BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.None,
                Font = PdfDesignRenderer.MakeFont(t.FontName, Math.Max(2f, t.SizePt) * PdfDesignRenderer.MmPerPt * Zoom,
                    PdfDesignRenderer.Style(t.Bold, t.Italic, t.Underline), GraphicsUnit.Pixel),
                BackColor = PdfItem.Visible(t.Fill) ? Color.FromArgb(255, t.Fill) : Color.White,
                ForeColor = PdfItem.Visible(t.Color) ? Color.FromArgb(255, t.Color) : PdfDesignRenderer.Ink,
                TextAlign = t.Align == PdfTextAlign.Center ? HorizontalAlignment.Center
                          : t.Align == PdfTextAlign.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Text = _editStart.Replace("\n", "\r\n"),
                ContextMenuStrip = EditorMenu
            };
            _editor = box;
            PlaceEditor();
            Controls.Add(box);
            box.TextChanged += (s, e) =>
            {
                if (_editing == null || _editor != box) return;
                _editing.Text = box.Text.Replace("\r\n", "\n");
                PdfDesignRenderer.Layout(_editing);
                PlaceEditor();
                Invalidate();
                if (TextEditing != null) TextEditing(this, EventArgs.Empty);
            };
            box.Leave += (s, e) =>
            {
                // The right-click menu does not end typing: its commands come back to the box
                if (_editor == box && (EditorMenu == null || !EditorMenu.Visible)) EndTextEdit();
            };
            box.Focus();
            box.Select(box.TextLength, 0);
            Invalidate();
        }

        private void PlaceEditor()
        {
            if (_editor == null || _editing == null) return;
            RectangleF r = ToPx(_editing.Bounds);
            float pad = Math.Max(0f, _editing.PaddingMm) * Zoom;
            int lines = Math.Max(1, _editor.GetLineFromCharIndex(_editor.TextLength) + 1);
            float h = Math.Max(r.Height - 2f * pad, lines * _editor.Font.Height + 4);
            _editor.Bounds = Rectangle.Round(new RectangleF(r.X + pad, r.Y + pad, Math.Max(24f, r.Width - 2f * pad), h));
        }

        /// <summary>Puts <paramref name="text"/> at the cursor of the text being typed on the page. False when nothing is being typed.</summary>
        public bool InsertInEditor(string text)
        {
            if (_editor == null) return false;
            _editor.SelectedText = text;
            _editor.Focus();
            return true;
        }

        /// <summary>Cut (0), copy (1), paste (2) or select all (3) in the text being typed on the page.</summary>
        public void EditorCommand(int command)
        {
            if (_editor == null) return;
            switch (command)
            {
                case 0: _editor.Cut(); break;
                case 1: _editor.Copy(); break;
                case 2: _editor.Paste(); break;
                default: _editor.SelectAll(); break;
            }
            _editor.Focus();
        }

        /// <summary>Closes the text box opened by <see cref="BeginTextEdit"/> (nothing happens when none is open).</summary>
        public void EndTextEdit()
        {
            if (_editor == null) return;
            TextBox box = _editor;
            PdfTextItem t = _editing;
            EditorCaret = box.SelectionStart;
            _editor = null;
            _editing = null;
            if (box.Focused) Focus();
            box.Visible = false;
            if (IsHandleCreated)
                BeginInvoke((Action)(() => { Controls.Remove(box); box.Font.Dispose(); box.Dispose(); }));
            Invalidate();
            if (TextEditEnded != null) TextEditEnded(t, (t.Text ?? "") != _editStart);
        }
    }
}
