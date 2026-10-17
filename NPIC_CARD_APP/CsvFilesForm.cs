using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "CSV Files" page: every student CSV in one folder, checked the way Import CSV… reads them (StudentId column,
    /// students, empty / duplicate IDs), with a preview of the selected file.
    ///   Load Students (Enter / double-click) = load the file into the main window
    ///   New… (Ctrl+N)   Add Files… (or drop .csv files on the list)   Edit (Ctrl+E) = open in Excel
    ///   Rename… (F2)    Duplicate    Delete (Del) = to the Recycle Bin    Copy Path (Ctrl+C)    Refresh (F5)
    /// The folder is <see cref="AppSettings.CsvFolder"/>, or the folder of the last student file when none is chosen.
    /// </summary>
    public class CsvFilesForm : ModalForm
    {
        private static readonly Color Accent     = Theme.Accent;
        private static readonly Color AccentSoft = Theme.AccentSoft;
        private static readonly Color Surface    = Theme.Surface;
        private static readonly Color SurfaceAlt = Theme.SurfaceAlt;
        private static readonly Color GridLine   = Theme.GridLine;
        private static readonly Color TextMain   = Theme.TextMain;
        private static readonly Color TextMuted  = Theme.TextSoft;
        private static readonly Color Warning    = Theme.Warning;
        private static readonly Color Danger     = Theme.Danger;
        private static readonly Color Success    = Theme.Success;

        private const int PreviewRows = 200;
        private static readonly Font BoldFont = new Font(CardFonts.UI, 9f, FontStyle.Bold);

        /// <summary>One CSV file of the folder, with what Import CSV… would make of it.</summary>
        private sealed class CsvInfo
        {
            public string Path, Name;
            public long Size;
            public DateTime Modified;
            public string Error;              // could not be read
            public bool HasHeader, HasId;
            public int Students, Columns, MissingIds, DuplicateIds;
            public List<string> MissingColumns = new List<string>();

            public bool Loadable { get { return Error == null && HasId && Students > 0; } }
        }

        private readonly AppSettings _settings;
        private string _folder;
        private List<CsvInfo> _all = new List<CsvInfo>();

        private readonly TextBox _txtSearch;
        private readonly DataGridView _grid, _preview;
        private readonly SplitContainer _split;
        private readonly Label _lblFolder, _lblDetails, _lblEmpty, _lblPreview;
        private readonly Button _btnNew, _btnAdd, _btnEdit, _btnRename, _btnDuplicate, _btnDelete, _btnLoad, _btnExplorer;
        private string _previewPath;
        private bool _refreshOnActivate;   // a file was opened in Excel / Explorer: it may have changed

        /// <summary>The file picked with Load Students (DialogResult = OK), or null.</summary>
        public string FileToLoad { get; private set; }

        /// <summary>True when a setting changed here (CSV folder, or the last student file was renamed); not saved yet.</summary>
        public bool SettingsChanged { get; private set; }

        public CsvFilesForm(AppSettings settings)
        {
            _settings = settings;
            Text = "CSV Files";
            Subtitle = "Student lists in one folder — check, preview, create, rename, delete or load them";
            Glyph = Theme.IcoDocument;
            StartPosition = FormStartPosition.CenterParent;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1080, work.Width), Math.Min(720, work.Height));
            MinimumSize = new Size(Math.Min(880, work.Width), Math.Min(500, work.Height));
            KeyPreview = true;

            // ---- Folder bar ----
            _lblFolder = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false,
                ForeColor = TextMuted, BackColor = Surface
            };
            _btnExplorer = ModalUi.ToolButton("Show in Explorer", (s, e) => ShowInExplorer());
            Button btnFolder = ModalUi.ToolButton("Change Folder…", (s, e) => ChangeFolder());
            _btnExplorer.Anchor = btnFolder.Anchor = AnchorStyles.Right;
            var folderBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 46, ColumnCount = 3, RowCount = 1, BackColor = Surface,
                Padding = new Padding(16, 8, 10, 0)
            };
            folderBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            folderBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            folderBar.Controls.Add(_lblFolder, 0, 0);
            folderBar.Controls.Add(btnFolder, 1, 0);
            folderBar.Controls.Add(_btnExplorer, 2, 0);

            // ---- Toolbar ----
            _txtSearch = new TextBox { Width = 200, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 5, 12, 0) };
            _txtSearch.HandleCreated += (s, e) => SetCueBanner(_txtSearch, "Search files… (Ctrl+F)");
            _txtSearch.TextChanged += (s, e) => ShowFiles(null);

            _btnNew       = ModalUi.ToolButton("New…", (s, e) => NewFile());
            _btnAdd       = ModalUi.ToolButton("Add Files…", (s, e) => AddFiles());
            _btnEdit      = ModalUi.ToolButton("Edit", (s, e) => EditSelected());
            _btnRename    = ModalUi.ToolButton("Rename…", (s, e) => RenameSelected());
            _btnDuplicate = ModalUi.ToolButton("Duplicate", (s, e) => DuplicateSelected());
            _btnDelete    = ModalUi.ToolButton("Delete", (s, e) => DeleteSelected());

            var toolbar = ModalUi.Toolbar();
            toolbar.Controls.Add(_txtSearch);
            toolbar.Controls.Add(_btnNew);
            toolbar.Controls.Add(_btnAdd);
            toolbar.Controls.Add(_btnEdit);
            toolbar.Controls.Add(_btnRename);
            toolbar.Controls.Add(_btnDuplicate);
            toolbar.Controls.Add(_btnDelete);
            toolbar.Controls.Add(ModalUi.ToolButton("Refresh", (s, e) => Reload()));

            var tip = new ToolTip();
            tip.SetToolTip(_btnNew, "Create an empty student CSV with the standard columns (Ctrl+N)");
            tip.SetToolTip(_btnAdd, "Copy CSV files from elsewhere into this folder (you can also drop them on the list)");
            tip.SetToolTip(_btnEdit, "Open the file in Excel, or the program Windows uses for .csv, to edit it (Ctrl+E)");
            tip.SetToolTip(_btnRename, "Rename the selected file (F2)");
            tip.SetToolTip(_btnDuplicate, "Make a copy of the selected file in this folder");
            tip.SetToolTip(_btnDelete, "Move the selected file to the Recycle Bin (Del)");
            tip.SetToolTip(btnFolder, "Choose the folder this page lists");
            tip.SetToolTip(_btnExplorer, "Open the folder in Windows Explorer with the selected file highlighted");

            // ---- File list ----
            _grid = NewGrid();
            _grid.MultiSelect = false;
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "File", HeaderText = "File", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 160
            });
            var students = new DataGridViewTextBoxColumn { Name = "Students", HeaderText = "Students", Width = 90, ValueType = typeof(int) };
            students.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            students.DefaultCellStyle.Format = "N0";
            _grid.Columns.Add(students);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 230 });
            var modified = new DataGridViewTextBoxColumn { Name = "Modified", HeaderText = "Modified", Width = 140, ValueType = typeof(DateTime) };
            modified.DefaultCellStyle.Format = "dd/MM/yyyy HH:mm";
            _grid.Columns.Add(modified);
            var size = new DataGridViewTextBoxColumn { Name = "Size", HeaderText = "Size", Width = 80, ValueType = typeof(long) };
            size.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns.Add(size);
            _grid.Columns["Modified"].DefaultCellStyle.ForeColor = TextMuted;
            _grid.Columns["Size"].DefaultCellStyle.ForeColor = TextMuted;
            foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.Automatic;

            _grid.CellFormatting += FormatFileCell;
            _grid.SelectionChanged += (s, e) => { UpdateButtons(); ShowPreview(); };
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) LoadSelected(); };

            _lblEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted, BackColor = Surface,
                Visible = false, UseMnemonic = false
            };
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(_lblEmpty);
            _lblEmpty.BringToFront();
            foreach (Control c in new Control[] { _grid, _lblEmpty, gridHost })
            {
                c.AllowDrop = true;
                c.DragEnter += OnDragEnter;
                c.DragDrop += OnDragDrop;
            }

            // ---- Preview ----
            _lblPreview = new Label
            {
                Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
                Padding = new Padding(16, 0, 16, 0), BackColor = SurfaceAlt, ForeColor = TextMuted, UseMnemonic = false,
                Font = BoldFont
            };
            _preview = NewGrid();
            _preview.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _preview.RowTemplate.Height = 28;
            _preview.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _preview.MultiSelect = true;   // Ctrl+C copies the selected cells

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, BackColor = Theme.Border,
                FixedPanel = FixedPanel.None
            };
            _split.Panel1.BackColor = Surface;
            _split.Panel2.BackColor = Surface;
            _split.Panel1.Controls.Add(gridHost);
            _split.Panel2.Controls.Add(_preview);
            _split.Panel2.Controls.Add(_lblPreview);

            // ---- Details + footer ----
            _lblDetails = new Label { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(10, 6, 10, 4), UseMnemonic = false };
            ModalUi.StyleStatus(_lblDetails);

            var footer = ModalUi.Footer();
            Button btnClose = ModalUi.Button("Close", PillKind.Secondary, (s, e) => Close());
            _btnLoad = ModalUi.Button("Load Students", PillKind.Primary, (s, e) => LoadSelected());
            footer.Controls.Add(_btnLoad);
            footer.Controls.Add(btnClose);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(_split);
            Controls.Add(toolbar);
            Controls.Add(folderBar);
            Controls.Add(_lblDetails);
            Controls.Add(footer);

            Reload();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try { _split.SplitterDistance = (int)(_split.Height * 0.52); }
            catch (InvalidOperationException) { /* window too small to split: keep the default */ }
            if (_grid.Rows.Count > 0) _grid.Focus();
            else if (_folder == null) ChangeFolder();   // nothing to list yet: ask for the folder straight away
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (!_refreshOnActivate) return;
            _refreshOnActivate = false;
            Reload();   // the file may have been edited in Excel, or files changed in Explorer
        }

        /// <summary>Keys on the file list: handled here because the grid would take them before KeyDown.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_grid.Focused)
            {
                switch (keyData)
                {
                    case Keys.Enter: LoadSelected(); return true;
                    case Keys.F2: RenameSelected(); return true;
                    case Keys.Delete: DeleteSelected(); return true;
                    case Keys.Control | Keys.C: CopySelectedPath(); return true;
                }
            }
            switch (keyData)
            {
                case Keys.Control | Keys.N: NewFile(); return true;
                case Keys.Control | Keys.E: EditSelected(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                // First Esc clears the search, the next one closes the window.
                if (_txtSearch.TextLength > 0) _txtSearch.Clear();
                else Close();
            }
            else if (e.KeyData == (Keys.Control | Keys.F)) { _txtSearch.Focus(); _txtSearch.SelectAll(); }
            else if (e.KeyCode == Keys.F5) Reload();
            else if (e.KeyCode == Keys.Down && _txtSearch.Focused && _grid.Rows.Count > 0) _grid.Focus();
            else return;
            e.Handled = e.SuppressKeyPress = true;
        }

        // ===================== View =====================
        private void Reload(string select = null)
        {
            if (select == null && Selected != null) select = Selected.Path;
            _folder = ResolveFolder(_settings);
            Cursor = Cursors.WaitCursor;
            try { _all = Scan(_folder); }
            finally { Cursor = Cursors.Default; }
            _previewPath = null;   // re-read: the file may have changed
            ShowFiles(select);
        }

        /// <summary>Fills the list with the files matching the search, then selects <paramref name="select"/> (null = keep the selection).</summary>
        private void ShowFiles(string select)
        {
            if (select == null && Selected != null) select = Selected.Path;
            string q = _txtSearch.Text.Trim();
            List<CsvInfo> shown = q.Length == 0 ? _all
                : _all.Where(x => x.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            DataGridViewColumn sortColumn = _grid.SortedColumn ?? _grid.Columns["Modified"];
            ListSortDirection direction = _grid.SortedColumn == null || _grid.SortOrder == SortOrder.Descending
                ? ListSortDirection.Descending : ListSortDirection.Ascending;   // newest first until a header is clicked

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (CsvInfo x in shown)
            {
                bool loaded = IsLoaded(x);
                int i = _grid.Rows.Add(x.Name + (loaded ? "   · loaded" : ""), x.Students, StatusText(x), x.Modified, x.Size);
                DataGridViewRow row = _grid.Rows[i];
                row.Tag = x;

                string tip = x.Path + (loaded ? "\nThe student list in the main window was loaded from this file." : "") +
                    (x.Error != null ? "\n" + x.Error : "");
                foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = tip;

                Color color = StatusColor(x);
                row.Cells["Status"].Style.ForeColor = color;
                row.Cells["Status"].Style.SelectionForeColor = color;
                if (loaded) row.Cells["File"].Style.Font = BoldFont;
            }
            if (_grid.Rows.Count > 1) _grid.Sort(sortColumn, direction);
            _grid.ResumeLayout();

            _lblEmpty.Text = EmptyText(shown.Count, q);
            _lblEmpty.Visible = shown.Count == 0;
            SelectByPath(select);
            UpdateFolderLabel();
            UpdateButtons();
            ShowPreview();
        }

        private string EmptyText(int shown, string q)
        {
            if (_folder == null)
                return "No CSV folder is chosen yet.\nClick Change Folder… and pick the folder with your student CSV files.";
            if (!Directory.Exists(_folder)) return "This folder was not found:\n" + _folder + "\n\nClick Change Folder… to pick it again.";
            if (_all.Count == 0)
                return "There are no CSV files in this folder yet.\n\nClick New… to create one, Add Files… to copy some in, " +
                       "or drop .csv files here.";
            return shown == 0 ? "No file matches \"" + q + "\"." : "";
        }

        private void UpdateFolderLabel()
        {
            if (_folder == null)
            {
                _lblFolder.Text = "Folder: not chosen";
                _lblFolder.ForeColor = Warning;
                return;
            }
            bool exists = Directory.Exists(_folder);
            int ready = _all.Count(x => x.Loadable);
            _lblFolder.Text = "Folder: " + _folder + (exists
                ? "     " + Plural(_all.Count, "CSV file") + (_all.Count > 0 ? ", " + ready + " ready to load" : "")
                : "     (not found)") +
                (string.IsNullOrEmpty(_settings.CsvFolder) ? "     · folder of the last student file" : "");
            _lblFolder.ForeColor = exists ? TextMuted : Warning;
        }

        private CsvInfo Selected
        {
            get { return _grid.SelectedRows.Count == 0 ? null : _grid.SelectedRows[0].Tag as CsvInfo; }
        }

        private void SelectByPath(string path)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var x = row.Tag as CsvInfo;
                if (x != null && (path == null || string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells[0];
                    return;
                }
            }
            if (path != null) SelectByPath(null);   // the file is gone or hidden by the search: select the first one
        }

        private void UpdateButtons()
        {
            CsvInfo x = Selected;
            bool folderOk = _folder != null && Directory.Exists(_folder);
            _btnNew.Enabled = _btnAdd.Enabled = folderOk;
            _btnEdit.Enabled = _btnRename.Enabled = _btnDuplicate.Enabled = _btnDelete.Enabled = x != null;
            _btnLoad.Enabled = x != null && x.Loadable;
            _btnExplorer.Enabled = folderOk;

            if (x == null)
            {
                int problems = _all.Count(f => !f.Loadable);
                _lblDetails.Text = (folderOk ? Plural(_all.Count, "CSV file") : "No folder") +
                    (problems > 0 ? "  ·  " + problems + " cannot be loaded (red)" : "") + Environment.NewLine +
                    "Select a file to preview it. Header row: " + CsvStudentLoader.Header;
                _lblDetails.ForeColor = TextMuted;
                return;
            }

            string line1 = x.Name + "  ·  " + FormatSize(x.Size) + "  ·  modified " + x.Modified.ToString("dd/MM/yyyy HH:mm");
            if (x.HasHeader) line1 += "  ·  " + Plural(x.Columns, "column") + "  ·  " + Plural(x.Students, "student");
            _lblDetails.Text = line1 + Environment.NewLine + Explain(x);
            _lblDetails.ForeColor = x.Error != null || (x.HasHeader && !x.HasId) ? Danger : HasWarnings(x) ? Warning : TextMuted;
        }

        private void FormatFileCell(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var x = _grid.Rows[e.RowIndex].Tag as CsvInfo;
            if (x == null) return;
            string column = _grid.Columns[e.ColumnIndex].Name;
            if (column == "Size") { e.Value = FormatSize(x.Size); e.FormattingApplied = true; }
            else if (column == "Students" && !x.HasHeader) { e.Value = "—"; e.FormattingApplied = true; }
        }

        // ===================== Preview =====================
        private void ShowPreview()
        {
            CsvInfo x = Selected;
            string path = x == null ? null : x.Path;
            if (path == _previewPath) return;
            _previewPath = path;

            _preview.SuspendLayout();
            _preview.Rows.Clear();
            _preview.Columns.Clear();
            try
            {
                if (x == null)
                {
                    _lblPreview.Text = "Preview";
                    return;
                }
                if (x.Error != null) { _lblPreview.Text = "Preview — cannot read " + x.Name + ": " + x.Error; return; }
                if (!x.HasHeader) { _lblPreview.Text = "Preview — " + x.Name + " is empty"; return; }

                List<List<string>> rows;
                try { rows = ReadRows(x.Path); }
                catch (Exception ex) when (IsFileError(ex))
                {
                    _lblPreview.Text = "Preview — cannot read " + x.Name + ": " + ex.Message;
                    return;
                }
                if (rows.Count == 0) { _lblPreview.Text = "Preview — " + x.Name + " is empty"; return; }   // emptied since the scan
                List<string> header = rows[0];
                int idIndex = IndexOf(header, "StudentId");
                int width = Math.Max(header.Count, rows.Take(PreviewRows + 1).Max(r => r.Count));

                var number = new DataGridViewTextBoxColumn { HeaderText = "#", Width = 48, SortMode = DataGridViewColumnSortMode.NotSortable };
                number.DefaultCellStyle.ForeColor = TextMuted;
                number.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                _preview.Columns.Add(number);
                for (int c = 0; c < width; c++)
                {
                    string name = c < header.Count ? Clean(header[c]) : "";
                    var col = new DataGridViewTextBoxColumn
                    {
                        HeaderText = name.Length > 0 ? name : "(no name)", SortMode = DataGridViewColumnSortMode.NotSortable,
                        Width = name == "NameKh" || name == "Faculty" || name == "Level" || name == "QrData" ? 170 : 110
                    };
                    if (c == idIndex) col.HeaderCell.Style.ForeColor = Accent;
                    _preview.Columns.Add(col);
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int shown = Math.Min(PreviewRows, rows.Count - 1);
                for (int r = 1; r <= shown; r++)
                {
                    List<string> cells = rows[r];
                    var values = new object[width + 1];
                    values[0] = r;
                    for (int c = 0; c < cells.Count; c++) values[c + 1] = cells[c];
                    int i = _preview.Rows.Add(values);

                    if (idIndex < 0) continue;
                    string id = idIndex < cells.Count ? cells[idIndex].Trim() : "";
                    if (id.Length == 0 || !seen.Add(id))
                    {
                        DataGridViewCell cell = _preview.Rows[i].Cells[idIndex + 1];
                        cell.Style.BackColor = Theme.WarningSoft;
                        cell.Style.ForeColor = Warning;
                        cell.ToolTipText = id.Length == 0 ? "No StudentId: this card has no photo or ID" : "This StudentId appears more than once";
                    }
                }

                int total = rows.Count - 1;
                _lblPreview.Text = "Preview — " + x.Name + "  ·  " +
                    (total > shown ? "first " + shown + " of " + total.ToString("N0") + " rows" : Plural(total, "row")) +
                    (idIndex < 0 ? "  ·  no StudentId column" : "");
            }
            finally
            {
                _preview.ResumeLayout();
            }
        }

        // ===================== Actions =====================
        private void LoadSelected()
        {
            CsvInfo x = Selected;
            if (x == null) return;
            if (!x.Loadable)
            {
                AppDialog.Show(this, x.Name + " cannot be loaded.\n\n" + Explain(x), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            FileToLoad = x.Path;
            DialogResult = DialogResult.OK;
        }

        private void NewFile()
        {
            if (!FolderReady()) return;
            string path = AskTarget("New CSV File", "An empty student list with the standard columns",
                "students-" + DateTime.Now.ToString("yyyy-MM-dd"), null);
            if (path == null) return;
            try
            {
                // UTF-8 with BOM, so Excel shows Khmer text correctly when it opens the file.
                File.WriteAllText(path, CsvStudentLoader.Header + "\r\n", new UTF8Encoding(true));
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Fail("Could not create the file:\n" + path, ex);
                return;
            }
            Reload(path);
            if (AppDialog.Show(this, Path.GetFileName(path) + " was created.\n\nOpen it now to fill in the students " +
                    "(one per row), then save it as CSV UTF-8?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Launch(path);
        }

        private void AddFiles()
        {
            if (!FolderReady()) return;
            using (var dlg = new OpenFileDialog
            {
                Filter = "CSV UTF-8 (*.csv)|*.csv", Title = "Add CSV files to " + _folder, Multiselect = true
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) AddFiles(dlg.FileNames);
            }
        }

        /// <summary>Copies the files into the folder; a name already there asks: replace, keep both, or skip.</summary>
        private void AddFiles(IEnumerable<string> files)
        {
            if (!FolderReady()) return;
            string last = null;
            foreach (string source in files)
            {
                if (!string.Equals(Path.GetExtension(source), ".csv", StringComparison.OrdinalIgnoreCase)) continue;
                if (SamePath(Path.GetDirectoryName(FullPath(source)), _folder))
                {
                    last = source;   // already in this folder
                    continue;
                }

                string target = Path.Combine(_folder, Path.GetFileName(source));
                if (File.Exists(target))
                {
                    DialogResult answer = AppDialog.Show(this,
                        Path.GetFileName(target) + " already exists in this folder.\n\n" +
                        "Yes = replace it     No = keep both (the new one gets another name)     Cancel = skip it",
                        "Add Files", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (answer == DialogResult.Cancel) continue;
                    if (answer == DialogResult.No) target = UniquePath(_folder, Path.GetFileNameWithoutExtension(source));
                }
                try
                {
                    File.Copy(source, target, true);
                    last = target;
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    Fail("Could not copy " + Path.GetFileName(source) + ".", ex);
                }
            }
            Reload(last);
        }

        private void EditSelected()
        {
            CsvInfo x = Selected;
            if (x != null) Launch(x.Path);
        }

        private void RenameSelected()
        {
            CsvInfo x = Selected;
            if (x == null) return;
            string target = AskTarget("Rename", x.Name, Path.GetFileNameWithoutExtension(x.Name), x.Path);
            if (target == null) return;
            try
            {
                File.Move(x.Path, target);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Fail("Could not rename " + x.Name + ".\nIs it open in Excel? Close it there and try again.", ex);
                return;
            }
            if (IsLoaded(x))
            {
                _settings.LastCsvPath = target;   // keep reloading the same list at the next start
                SettingsChanged = true;
            }
            Reload(target);
        }

        private void DuplicateSelected()
        {
            CsvInfo x = Selected;
            if (x == null) return;
            string target = UniquePath(_folder, Path.GetFileNameWithoutExtension(x.Name) + " - Copy");
            try
            {
                File.Copy(x.Path, target);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Fail("Could not copy " + x.Name + ".", ex);
                return;
            }
            Reload(target);
        }

        private void DeleteSelected()
        {
            CsvInfo x = Selected;
            if (x == null) return;
            string message = "Move " + x.Name + " to the Recycle Bin?" +
                (x.HasHeader ? "\n\nIt holds " + Plural(x.Students, "student") + "." : "") +
                (IsLoaded(x) ? "\n\nThe student list in the main window was loaded from this file. It stays loaded until you import another list." : "");
            if (AppDialog.Show(this, message, "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            int index = _grid.SelectedRows[0].Index;
            try
            {
                FileSystem.DeleteFile(x.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            catch (OperationCanceledException)
            {
                return;   // cancelled in the Windows error dialog
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Fail("Could not delete " + x.Name + ".\nIs it open in Excel? Close it there and try again.", ex);
                return;
            }

            // Select the row that took the deleted one's place.
            string next = null;
            if (_grid.Rows.Count > 1)
            {
                var neighbour = _grid.Rows[index + 1 < _grid.Rows.Count ? index + 1 : index - 1].Tag as CsvInfo;
                if (neighbour != null) next = neighbour.Path;
            }
            Reload(next);
        }

        private void ChangeFolder()
        {
            using (var dlg = new FolderBrowserDialog
            {
                Description = "Folder with the student CSV files",
                SelectedPath = _folder != null && Directory.Exists(_folder) ? _folder : "",
                ShowNewFolderButton = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (string.Equals(dlg.SelectedPath, _settings.CsvFolder, StringComparison.OrdinalIgnoreCase)) return;
                _settings.CsvFolder = dlg.SelectedPath;
                SettingsChanged = true;
            }
            _txtSearch.Clear();
            Reload();
            if (_grid.Rows.Count > 0) _grid.Focus();
        }

        private void ShowInExplorer()
        {
            if (!FolderReady()) return;
            CsvInfo x = Selected;
            try
            {
                Process.Start("explorer.exe", x != null ? "/select,\"" + x.Path + "\"" : "\"" + _folder + "\"");
                _refreshOnActivate = true;
            }
            catch (Exception ex)
            {
                Fail("Could not open the folder.", ex);
            }
        }

        private void CopySelectedPath()
        {
            CsvInfo x = Selected;
            if (x == null) return;
            try
            {
                Clipboard.SetText(x.Path);
                _lblDetails.Text = "Copied to the clipboard:" + Environment.NewLine + x.Path;
                _lblDetails.ForeColor = TextMuted;
            }
            catch (ExternalException ex)
            {
                Fail("Could not copy to the clipboard.", ex);
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            bool ok = files != null && _folder != null && Directory.Exists(_folder) &&
                files.Any(f => string.Equals(Path.GetExtension(f), ".csv", StringComparison.OrdinalIgnoreCase));
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) BeginInvoke((Action)(() => AddFiles(files)));   // let Explorer finish the drop first
        }

        // ---- Action helpers ----
        private bool FolderReady()
        {
            if (_folder != null && Directory.Exists(_folder)) return true;
            AppDialog.Show(this, _folder == null ? "Choose the CSV folder first (Change Folder…)."
                    : "This folder was not found:\n" + _folder + "\n\nChoose it again with Change Folder….",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        /// <summary>Opens the file in its program (Excel for .csv on most PCs) and refreshes when this window is back.</summary>
        private void Launch(string path)
        {
            try
            {
                Process.Start(path);
                _refreshOnActivate = true;
            }
            catch (Exception ex)
            {
                Fail("Could not open " + Path.GetFileName(path) + ".\nIs a program such as Excel installed for .csv files?", ex);
            }
        }

        /// <summary>
        /// Asks for a file name in the folder until it is valid and free. Null = cancelled or unchanged.
        /// <paramref name="current"/> = the file being renamed (a change of upper / lower case only is allowed).
        /// </summary>
        private string AskTarget(string title, string subtitle, string initial, string current)
        {
            string text = initial;
            while (true)
            {
                text = CsvNamePrompt.Ask(this, title, subtitle, text);
                if (text == null) return null;

                string error;
                string name = MakeFileName(text, out error);
                if (error == null)
                {
                    string path = Path.Combine(_folder, name);
                    if (current != null && string.Equals(path, current, StringComparison.Ordinal)) return null;   // unchanged
                    bool sameFile = current != null && string.Equals(path, current, StringComparison.OrdinalIgnoreCase);
                    if (sameFile || !File.Exists(path)) return path;
                    error = "A file named " + name + " already exists in this folder. Choose another name.";
                }
                AppDialog.Show(this, error, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Fail(string message, Exception ex)
        {
            AppDialog.Show(this, message + "\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private bool IsLoaded(CsvInfo x)
        {
            return !string.IsNullOrEmpty(_settings.LastCsvPath) && SamePath(x.Path, _settings.LastCsvPath);
        }

        /// <summary>Same file or folder, whatever the case, short (8.3) names or "..\" in either path.</summary>
        private static bool SamePath(string a, string b)
        {
            return string.Equals(FullPath(a).TrimEnd('\\'), FullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private static string FullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch (Exception ex) when (IsFileError(ex)) { return path ?? ""; }
        }

        // ===================== Data =====================
        /// <summary>The chosen CSV folder, else the folder of the last student file; null when neither is set.</summary>
        private static string ResolveFolder(AppSettings s)
        {
            string folder = (s.CsvFolder ?? "").Trim();
            if (folder.Length == 0 && !string.IsNullOrEmpty(s.LastCsvPath))
            {
                try { folder = Path.GetDirectoryName(s.LastCsvPath) ?? ""; }
                catch (ArgumentException) { folder = ""; }   // invalid characters in an old saved path
            }
            if (folder.Length == 0) return null;
            folder = FullPath(folder).TrimEnd('\\');
            return folder.Length == 2 && folder[1] == ':' ? folder + "\\" : folder;   // "C:" -> "C:\"
        }

        private static List<CsvInfo> Scan(string folder)
        {
            var list = new List<CsvInfo>();
            if (folder == null || !Directory.Exists(folder)) return list;
            FileInfo[] files;
            try { files = new DirectoryInfo(folder).GetFiles("*.csv"); }
            catch (Exception ex) when (IsFileError(ex)) { return list; }

            foreach (FileInfo f in files)
            {
                // "*.csv" also matches "*.csvx" etc. on Windows (8.3 names): keep exact .csv only.
                if (!string.Equals(f.Extension, ".csv", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(Analyze(f));
            }
            return list;
        }

        /// <summary>Reads the file the way <see cref="CsvStudentLoader"/> does and counts what it would load.</summary>
        private static CsvInfo Analyze(FileInfo f)
        {
            var x = new CsvInfo { Path = f.FullName, Name = f.Name, Size = f.Length, Modified = f.LastWriteTime };
            List<List<string>> rows;
            try { rows = ReadRows(f.FullName); }
            catch (Exception ex) when (IsFileError(ex))
            {
                x.Error = ex is IOException && !(ex is FileNotFoundException) ? "In use by another program, or unreadable (" + ex.Message + ")" : ex.Message;
                return x;
            }
            if (rows.Count == 0) return x;

            List<string> header = rows[0];
            x.HasHeader = true;
            x.Columns = header.Count(h => Clean(h).Length > 0);
            int idIndex = IndexOf(header, "StudentId");
            x.HasId = idIndex >= 0;
            foreach (string column in CsvStudentLoader.Header.Split(','))
            {
                if (IndexOf(header, column) >= 0) continue;
                if (column == "Faculty" && IndexOf(header, "Department") >= 0) continue;   // the loader accepts either
                x.MissingColumns.Add(column);
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int r = 1; r < rows.Count; r++)
            {
                x.Students++;
                if (idIndex < 0) continue;
                string id = idIndex < rows[r].Count ? rows[r][idIndex].Trim() : "";
                if (id.Length == 0) x.MissingIds++;
                else if (!ids.Add(id)) x.DuplicateIds++;
            }
            return x;
        }

        /// <summary>
        /// The non-blank rows of the file (row 0 = header), parsed like <see cref="CsvStudentLoader"/>. Opened with shared
        /// access so a file that is open in Excel can still be read.
        /// </summary>
        private static List<List<string>> ReadRows(string path)
        {
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs, Encoding.UTF8, true))
                text = reader.ReadToEnd();

            var rows = new List<List<string>>();
            foreach (string line in text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                List<string> cells = CsvStudentLoader.ParseLine(line);
                if (cells.All(string.IsNullOrWhiteSpace)) continue;
                rows.Add(cells);
            }
            return rows;
        }

        private static string StatusText(CsvInfo x)
        {
            if (x.Error != null) return "✖ Cannot read";
            if (!x.HasHeader) return "Empty file";
            if (!x.HasId) return "✖ No StudentId column";
            if (x.Students == 0) return "No students yet";
            var issues = new List<string>();
            if (x.MissingIds > 0) issues.Add(x.MissingIds + " without ID");
            if (x.DuplicateIds > 0) issues.Add(x.DuplicateIds + " duplicate ID" + (x.DuplicateIds == 1 ? "" : "s"));
            return issues.Count > 0 ? "⚠ " + string.Join(" · ", issues) : "✔ Ready";
        }

        private static Color StatusColor(CsvInfo x)
        {
            if (x.Error != null || (x.HasHeader && !x.HasId)) return Danger;
            if (!x.HasHeader || x.Students == 0) return TextMuted;
            return HasWarnings(x) ? Warning : Success;
        }

        private static bool HasWarnings(CsvInfo x) { return x.MissingIds > 0 || x.DuplicateIds > 0; }

        /// <summary>One line on what Load Students would do with the file, or why it cannot.</summary>
        private static string Explain(CsvInfo x)
        {
            if (x.Error != null) return "Cannot read the file: " + x.Error;
            if (!x.HasHeader) return "The file is empty. Click Edit to add the header row: " + CsvStudentLoader.Header;
            if (!x.HasId) return "The first row has no 'StudentId' column, so the students cannot be loaded. Click Edit to fix the header.";
            if (x.Students == 0) return "Only the header row is there. Click Edit to add students (one per row).";

            var notes = new List<string>();
            if (x.MissingIds > 0) notes.Add(Plural(x.MissingIds, "row") + " without a StudentId (highlighted in the preview)");
            if (x.DuplicateIds > 0) notes.Add(Plural(x.DuplicateIds, "StudentId") + " used more than once");
            if (x.MissingColumns.Count > 0)
                notes.Add("missing columns: " + string.Join(", ", x.MissingColumns.Take(4)) +
                          (x.MissingColumns.Count > 4 ? " and " + (x.MissingColumns.Count - 4) + " more" : ""));
            return notes.Count == 0 ? "Ready to load — press Enter or double-click." : "Can be loaded, but: " + string.Join("; ", notes) + ".";
        }

        // ===================== Helpers =====================
        private static DataGridView NewGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Surface, BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = GridLine,
                RowHeadersVisible = false, EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, ShowCellToolTips = true, StandardTab = true
            };
            grid.RowTemplate.MinimumHeight = 30;
            grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(CardFonts.UI, 9f, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = TextMain;
            grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = Accent;
            grid.DefaultCellStyle.Padding = new Padding(4, 3, 4, 3);
            return grid;
        }

        /// <summary>"name.csv" from what was typed; <paramref name="error"/> says why it cannot be a file name.</summary>
        private static string MakeFileName(string text, out string error)
        {
            error = null;
            string name = (text ?? "").Trim();
            if (name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4).TrimEnd();
            name = name.TrimEnd('.', ' ');
            if (name.Length == 0) error = "Type a name for the file.";
            else if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                error = "A file name cannot contain any of these characters:  \\ / : * ? \" < > |";
            return error == null ? name + ".csv" : null;
        }

        /// <summary>"{name}.csv", or "{name} (2).csv", "{name} (3).csv"… when taken.</summary>
        private static string UniquePath(string folder, string name)
        {
            string path = Path.Combine(folder, name + ".csv");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, name + " (" + n + ").csv");
            return path;
        }

        private static int IndexOf(List<string> header, string column)
        {
            for (int i = 0; i < header.Count; i++)
                if (string.Equals(Clean(header[i]), column, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>Header cell as the loader compares it: trimmed, without a UTF-8 byte-order mark.</summary>
        private static string Clean(string cell) { return (cell ?? "").Trim().TrimStart('\uFEFF'); }

        private static bool IsFileError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException ||
                   ex is ArgumentException || ex is NotSupportedException;
        }

        private static string Plural(int n, string word) { return n.ToString("N0") + " " + word + (n == 1 ? "" : "s"); }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
            return (bytes / (1024.0 * 1024)).ToString("0.#") + " MB";
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void SetCueBanner(TextBox box, string text)
        {
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }
    }

    /// <summary>Small popup asking for a CSV file name (New… / Rename… on the CSV Files page).</summary>
    internal sealed class CsvNamePrompt : ModalForm
    {
        private readonly TextBox _txtName;

        private CsvNamePrompt(string title, string subtitle, string initial)
        {
            Text = title;
            Subtitle = subtitle;
            Glyph = Theme.IcoEdit;
            Resizable = false;
            DimOwner = false;   // the CSV Files page already dims the main window
            Size = new Size(480, 228);

            var label = new Label
            {
                Dock = DockStyle.Top, Height = 24, Text = "File name  (.csv is added)", ForeColor = Theme.TextSoft,
                BackColor = Theme.Surface
            };
            _txtName = new TextBox { Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle, Text = initial ?? "" };
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
            using (var form = new CsvNamePrompt(title, subtitle, initial))
                return form.ShowDialog(owner) == DialogResult.OK ? form._txtName.Text : null;
        }
    }
}
