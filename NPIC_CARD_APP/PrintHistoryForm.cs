using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// History of every card side sent to the printer (see <see cref="PrintHistory"/>).
    ///   "Every print" view: one row per card side printed — delete wrong entries here.
    ///   "Per student" view: one row per student with front / back counts — shows who is finished, who still
    ///   needs the back, and who was printed more than once.
    /// Double-click a row to select that student in the main list.   F5 = reload, Ctrl+F = search, Del = delete, Esc = close
    /// </summary>
    public class PrintHistoryForm : ModalForm
    {
        private static readonly Color Accent     = Theme.Accent;
        private static readonly Color AccentSoft = Theme.AccentSoft;
        private static readonly Color Surface    = Theme.Surface;
        private static readonly Color SurfaceAlt = Theme.SurfaceAlt;
        private static readonly Color GridLine   = Theme.GridLine;
        private static readonly Color TextMain   = Theme.TextMain;
        private static readonly Color TextMuted  = Theme.TextSoft;
        private static readonly Color Warning    = Theme.Warning;
        private static readonly Color Success    = Theme.Success;
        private static readonly Color Danger     = Theme.Danger;

        private const string DateTimeFormat = "yyyy-MM-dd HH:mm";

        private readonly Func<string, bool> _showStudent;
        private List<PrintRecord> _all = new List<PrintRecord>();

        private readonly TextBox _txtSearch;
        private readonly DateTimePicker _dtFrom, _dtTo;
        private readonly ComboBox _cboSide, _cboView;
        private readonly DataGridView _grid;
        private readonly Label _lblSummary, _lblEmpty;
        private readonly Button _btnDelete, _btnClear, _btnExport;

        private bool PerStudent { get { return _cboView.SelectedIndex == 1; } }

        /// <param name="showStudent">Selects a student (by ID) in the main list; returns false when not loaded. May be null.</param>
        public PrintHistoryForm(Func<string, bool> showStudent)
        {
            _showStudent = showStudent;

            Text = "Print History";
            Subtitle = "Every card side sent to the printer";
            Glyph = Theme.IcoHistory;
            StartPosition = FormStartPosition.CenterParent;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1100, work.Width), Math.Min(640, work.Height));
            MinimumSize = new Size(Math.Min(760, work.Width), Math.Min(400, work.Height));
            Font = new Font(CardFonts.UI, 9f);
            BackColor = Surface;
            KeyPreview = true;
            ShowInTaskbar = false;

            // ---- Filters ----
            _txtSearch = new TextBox { Width = 230, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 7, 12, 3) };
            _txtSearch.HandleCreated += (s, e) => SetCueBanner(_txtSearch, "Search ID, name, faculty… (Ctrl+F)");
            _txtSearch.TextChanged += (s, e) => ShowRecords();

            _dtFrom = MakeDatePicker(DateTime.Today.AddDays(-30));
            _dtTo = MakeDatePicker(DateTime.Today);

            _cboSide = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80, Margin = new Padding(3, 6, 12, 3) };
            _cboSide.Items.AddRange(new object[] { "All", "Front", "Back" });
            _cboSide.SelectedIndex = 0;
            _cboSide.SelectedIndexChanged += (s, e) => ShowRecords();

            _cboView = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(3, 6, 3, 3) };
            _cboView.Items.AddRange(new object[] { "Every print", "Per student" });
            _cboView.SelectedIndex = 0;
            _cboView.SelectedIndexChanged += (s, e) => ShowRecords();

            var filters = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 44, Padding = new Padding(13, 2, 13, 0), WrapContents = false, BackColor = SurfaceAlt
            };
            filters.Controls.Add(_txtSearch);
            filters.Controls.Add(Caption("From"));
            filters.Controls.Add(_dtFrom);
            filters.Controls.Add(Caption("To"));
            filters.Controls.Add(_dtTo);
            filters.Controls.Add(Caption("Side"));
            filters.Controls.Add(_cboSide);
            filters.Controls.Add(Caption("View"));
            filters.Controls.Add(_cboView);

            // ---- Commands ----
            var toolbar = ModalUi.Toolbar();
            toolbar.Controls.Add(MakeButton("Reload (F5)", (s, e) => LoadRecords()));
            _btnExport = MakeButton("Export CSV…", (s, e) => ExportCsv());
            _btnDelete = MakeButton("Delete Selected", (s, e) => DeleteSelected());
            _btnClear = MakeButton("Clear History…", (s, e) => ClearAll());
            toolbar.Controls.Add(_btnExport);
            toolbar.Controls.Add(_btnDelete);
            toolbar.Controls.Add(_btnClear);
            toolbar.Controls.Add(MakeButton("Open Folder", (s, e) => OpenFolder()));

            // ---- List ----
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
                BackgroundColor = Surface, BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = GridLine,
                RowHeadersVisible = false, EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
            };
            _grid.RowTemplate.Height = 30;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font(CardFonts.UI, 9f, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.DefaultCellStyle.ForeColor = TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            _grid.DefaultCellStyle.SelectionForeColor = Accent;
            _grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceAlt;
            _grid.SelectionChanged += (s, e) => UpdateButtons();
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ShowStudent(e.RowIndex); };

            _lblEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted, BackColor = Surface, Visible = false
            };
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(_lblEmpty);
            _lblEmpty.BringToFront();

            _lblSummary = new Label
            {
                Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(10, 6, 10, 4)
            };
            ModalUi.StyleStatus(_lblSummary);

            Controls.Add(gridHost);
            Controls.Add(toolbar);
            Controls.Add(filters);
            Controls.Add(_lblSummary);

            var tip = new ToolTip();
            tip.SetToolTip(_dtFrom, "Untick to show prints from any date");
            tip.SetToolTip(_dtTo, "Untick to show prints up to today");
            tip.SetToolTip(_cboView, "Every print = one row per card side sent to the printer.\n" +
                                     "Per student = one row per student with front / back counts.");
            tip.SetToolTip(_btnDelete, "Remove the selected entries from the history (Del). The cards are not affected.");
            tip.SetToolTip(_btnExport, "Save the rows shown now as a CSV file for Excel");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LoadRecords();
            _txtSearch.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                // First Esc clears the search, the next one closes the window.
                if (_txtSearch.TextLength > 0) _txtSearch.Clear();
                else Close();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.F5) { LoadRecords(); e.Handled = true; }
            else if (e.KeyData == (Keys.Control | Keys.F)) { _txtSearch.Focus(); _txtSearch.SelectAll(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete && _grid.Focused && _btnDelete.Enabled) { DeleteSelected(); e.Handled = true; }
        }

        // ===================== Data =====================
        private void LoadRecords()
        {
            try
            {
                _all = PrintHistory.Load();
            }
            catch (Exception ex)
            {
                _all = new List<PrintRecord>();
                AppDialog.Show(this, "Could not read the print history:\n\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            ShowRecords();
        }

        /// <summary>Records that pass the search / date / side filters, newest first.</summary>
        private List<PrintRecord> Filtered()
        {
            string q = _txtSearch.Text.Trim();
            DateTime? from = _dtFrom.Checked ? _dtFrom.Value.Date : (DateTime?)null;
            DateTime? toExclusive = _dtTo.Checked ? _dtTo.Value.Date.AddDays(1) : (DateTime?)null;
            int side = _cboSide.SelectedIndex;   // 0 all, 1 front, 2 back

            return _all.Where(r =>
                    (from == null || r.PrintedAt >= from.Value) &&
                    (toExclusive == null || r.PrintedAt < toExclusive.Value) &&
                    (side == 0 || (side == 1 ? r.IsFront : r.IsBack)) &&
                    (q.Length == 0 || Has(r.StudentId, q) || Has(r.NameKh, q) || Has(r.NameEn, q) ||
                                      Has(r.Faculty, q) || Has(r.Generation, q) || Has(r.User, q)))
                .OrderByDescending(r => r.PrintedAt)
                .ToList();
        }

        private void ShowRecords()
        {
            List<PrintRecord> shown = Filtered();

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            _grid.Columns.Clear();
            if (PerStudent) FillPerStudent(shown);
            else FillEveryPrint(shown);
            _grid.ResumeLayout();

            int students = shown.Select(r => r.StudentId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int fronts = shown.Count(r => r.IsFront), backs = shown.Count(r => r.IsBack);
            string range = shown.Count == 0 ? "" : string.Format("   ·   {0:yyyy-MM-dd} → {1:yyyy-MM-dd}",
                shown[shown.Count - 1].PrintedAt, shown[0].PrintedAt);
            _lblSummary.Text = string.Format(
                "{0} card side(s) printed for {1} student(s)  ·  {2} front, {3} back{4}{5}\n{6}",
                shown.Count, students, fronts, backs, range,
                shown.Count == _all.Count ? "" : string.Format("   ·   filtered from {0} total", _all.Count),
                "Double-click a row to select the student in the main list.   Saved in " + PrintHistory.FilePath);

            _lblEmpty.Text = _all.Count == 0
                ? "No cards printed yet.\n\nEvery card side sent with  Print Selected  is recorded here."
                : "No prints match the filters.\n\nClear the search or untick the From / To dates.";
            _lblEmpty.Visible = shown.Count == 0;
            UpdateButtons();
        }

        private void FillEveryPrint(List<PrintRecord> shown)
        {
            AddColumn("Printed", 130, typeof(DateTime));
            AddColumn("Student ID", 100);
            AddColumn("Name (KH)", 170);
            AddColumn("Name (EN)", 170);
            AddColumn("Faculty", 150);
            AddColumn("Gen.", 50);
            AddColumn("Side", 60);
            AddColumn("Slot", 45, typeof(int));
            AddColumn("Printer", 150);
            AddColumn("User", 90);

            foreach (PrintRecord r in shown)
            {
                int i = _grid.Rows.Add(r.PrintedAt, r.StudentId, r.NameKh, r.NameEn, r.Faculty, r.Generation,
                    r.IsFront ? "Front" : r.IsBack ? "Back" : r.Side, r.Slot, r.Printer, r.User);
                _grid.Rows[i].Tag = r;
                _grid.Rows[i].Cells[6].Style.ForeColor = r.IsFront ? Accent : Success;
            }
        }

        private void FillPerStudent(List<PrintRecord> shown)
        {
            AddColumn("Student ID", 100);
            AddColumn("Name (KH)", 170);
            AddColumn("Name (EN)", 170);
            AddColumn("Faculty", 150);
            AddColumn("Front", 55, typeof(int));
            AddColumn("Back", 55, typeof(int));
            AddColumn("Status", 120);
            AddColumn("First printed", 130, typeof(DateTime));
            AddColumn("Last printed", 130, typeof(DateTime));

            var groups = shown
                .GroupBy(r => r.StudentId ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Latest = g.OrderByDescending(r => r.PrintedAt).First(),   // newest name / faculty spelling
                    Front = g.Count(r => r.IsFront),
                    Back = g.Count(r => r.IsBack),
                    First = g.Min(r => r.PrintedAt),
                    Last = g.Max(r => r.PrintedAt)
                })
                .OrderByDescending(g => g.Last);

            foreach (var g in groups)
            {
                string status;
                Color tone;
                if (g.Front > 0 && g.Back > 0)
                {
                    bool reprint = g.Front > 1 || g.Back > 1;
                    status = reprint ? "Done (reprinted)" : "Done";
                    tone = reprint ? Warning : Success;
                }
                else if (g.Front > 0) { status = "Back missing"; tone = Danger; }
                else { status = "Front missing"; tone = Danger; }

                int i = _grid.Rows.Add(g.Latest.StudentId, g.Latest.NameKh, g.Latest.NameEn, g.Latest.Faculty,
                    g.Front, g.Back, status, g.First, g.Last);
                _grid.Rows[i].Tag = g.Latest;
                _grid.Rows[i].Cells[6].Style.ForeColor = tone;
                _grid.Rows[i].Cells[6].Style.SelectionForeColor = tone;
            }
        }

        private void AddColumn(string title, int width, Type type = null)
        {
            var col = new DataGridViewTextBoxColumn
            {
                HeaderText = title, Width = width, SortMode = DataGridViewColumnSortMode.Automatic,
                ValueType = type ?? typeof(string)
            };
            if (type == typeof(DateTime)) col.DefaultCellStyle.Format = DateTimeFormat;
            if (type == typeof(int)) col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns.Add(col);
        }

        // ===================== Commands =====================
        private void ShowStudent(int rowIndex)
        {
            var record = _grid.Rows[rowIndex].Tag as PrintRecord;
            if (record == null || _showStudent == null) return;
            if (_showStudent(record.StudentId))
            {
                Close();
                return;
            }
            AppDialog.Show(this,
                record.StudentId + " is not in the student list that is loaded now.\n\n" +
                "Import the list that contains this student, then try again.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void DeleteSelected()
        {
            if (PerStudent || _grid.SelectedRows.Count == 0) return;
            List<PrintRecord> selected = _grid.SelectedRows.Cast<DataGridViewRow>()
                .Select(r => r.Tag as PrintRecord).Where(r => r != null).ToList();

            string question = selected.Count == 1
                ? "Delete this entry from the print history?\n\n" + selected[0].StudentId + "  " +
                  selected[0].Side + "  " + selected[0].PrintedAt.ToString(DateTimeFormat)
                : "Delete " + selected.Count + " entries from the print history?";
            if (AppDialog.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            var remove = new HashSet<PrintRecord>(selected);
            SaveAndShow(_all.Where(r => !remove.Contains(r)).ToList());
        }

        private void ClearAll()
        {
            if (_all.Count == 0) return;
            if (AppDialog.Show(this,
                    "Delete ALL " + _all.Count + " entries from the print history?\n\n" +
                    "Use Export CSV… first if you want to keep a copy. This cannot be undone.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            SaveAndShow(new List<PrintRecord>());
        }

        private void SaveAndShow(List<PrintRecord> records)
        {
            try
            {
                PrintHistory.Save(records);
                _all = records;
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not save the print history:\n\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            ShowRecords();
        }

        /// <summary>Saves exactly the rows and columns shown now (filters and view applied).</summary>
        private void ExportCsv()
        {
            if (_grid.Rows.Count == 0) return;
            string path;
            using (var dlg = new SaveFileDialog
            {
                Filter = "CSV UTF-8 (*.csv)|*.csv",
                Title = "Export print history",
                FileName = (PerStudent ? "print-history-per-student-" : "print-history-") + DateTime.Today.ToString("yyyy-MM-dd") + ".csv",
                OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }

            List<DataGridViewColumn> columns = _grid.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex).ToList();
            var header = columns.Select(c => c.HeaderText).ToList();
            var rows = _grid.Rows.Cast<DataGridViewRow>()
                .Select(row => (IList<string>)columns.Select(c => Convert.ToString(row.Cells[c.Index].FormattedValue)).ToList());
            try
            {
                PrintHistory.WriteCsv(path, header, rows);
            }
            catch (IOException ex)
            {
                AppDialog.Show(this, "Could not save the file:\n\n" + ex.Message +
                    "\n\nIf the file is open in Excel, close it and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AppDialog.Show(this, "Exported " + _grid.Rows.Count + " row(s) to:\n" + path, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenFolder()
        {
            try
            {
                string file = PrintHistory.FilePath;
                if (File.Exists(file)) Process.Start("explorer.exe", "/select,\"" + file + "\"");
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(file) + "\"");
                }
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not open the folder:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateButtons()
        {
            _btnDelete.Enabled = !PerStudent && _grid.SelectedRows.Count > 0;
            _btnClear.Enabled = _all.Count > 0;
            _btnExport.Enabled = _grid.Rows.Count > 0;
        }

        // ===================== Helpers =====================
        private DateTimePicker MakeDatePicker(DateTime value)
        {
            var dt = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 120, ShowCheckBox = true,
                Value = value, Checked = false, Margin = new Padding(3, 6, 12, 3)
            };
            dt.ValueChanged += (s, e) => ShowRecords();   // also fires when the checkbox is ticked / unticked
            return dt;
        }

        private static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = TextMuted, Margin = new Padding(0, 10, 0, 0) };
        }

        private static bool Has(string value, string q)
        {
            return value != null && value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            return ModalUi.ToolButton(text, onClick);
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void SetCueBanner(TextBox box, string text)
        {
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }
    }
}
