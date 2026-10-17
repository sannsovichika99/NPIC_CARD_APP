using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "RFID Cards" page: every card number saved in the RFID CSV file (see <see cref="RfidCardWriter"/>).
    ///   "Saved cards" view: one row per student with the card UID, when it was printed and saved, by whom.
    ///   "Loaded students without a card" view: students of the current list that still need their card tapped.
    /// Double-click a row to select that student in the main list.
    ///   F5 = reload, Ctrl+F = search, Ctrl+C = copy card number(s), Del = delete, Esc = clear search / close
    /// </summary>
    public class RfidListForm : ModalForm
    {
        private const string DateTimeFormat = "yyyy-MM-dd HH:mm";
        private static readonly Font UidFont = new Font("Consolas", 9.75f);

        private readonly string _path;
        private readonly IList<Student> _students;
        private readonly Func<string, bool> _showStudent;
        private List<RfidRecord> _all = new List<RfidRecord>();
        private bool _busy;

        private readonly TextBox _txtSearch;
        private readonly ComboBox _cboView;
        private readonly DataGridView _grid;
        private readonly Label _lblSummary, _lblEmpty;
        private readonly Button _btnCopy, _btnDelete, _btnExport;

        private bool MissingView { get { return _cboView.SelectedIndex == 1; } }

        /// <param name="students">The loaded student list (may be empty), to find who has no card yet.</param>
        /// <param name="showStudent">Selects a student (by ID) in the main list; returns false when not loaded. May be null.</param>
        public RfidListForm(AppSettings settings, IList<Student> students, Func<string, bool> showStudent)
        {
            _path = RfidCardWriter.FilePath(settings);
            _students = students ?? new List<Student>();
            _showStudent = showStudent;

            Text = "RFID Cards";
            Subtitle = "Card numbers saved for printed cards  ·  " + _path;
            Glyph = Theme.IcoList;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1100, work.Width), Math.Min(640, work.Height));
            MinimumSize = new Size(Math.Min(760, work.Width), Math.Min(400, work.Height));
            Font = new Font(CardFonts.UI, 9f);
            KeyPreview = true;

            // ---- Filters ----
            _txtSearch = new TextBox { Width = 260, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 7, 12, 3) };
            _txtSearch.HandleCreated += (s, e) => SetCueBanner(_txtSearch, "Search ID, name, card number… (Ctrl+F)");
            _txtSearch.TextChanged += (s, e) => ShowRecords();

            _cboView = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, Margin = new Padding(3, 6, 3, 3) };
            _cboView.Items.AddRange(new object[] { "Saved cards", "Loaded students without a card" });
            _cboView.SelectedIndex = 0;
            _cboView.SelectedIndexChanged += (s, e) => ShowRecords();

            var filters = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 44, Padding = new Padding(13, 2, 13, 0), WrapContents = false,
                BackColor = Theme.SurfaceAlt
            };
            filters.Controls.Add(_txtSearch);
            filters.Controls.Add(Caption("View"));
            filters.Controls.Add(_cboView);

            // ---- Commands ----
            var toolbar = ModalUi.Toolbar();
            toolbar.Controls.Add(ModalUi.ToolButton("Reload (F5)", (s, e) => LoadRecords()));
            _btnCopy = ModalUi.ToolButton("Copy Card No.", (s, e) => CopyUids());
            _btnDelete = ModalUi.ToolButton("Delete Selected", (s, e) => DeleteSelected());
            _btnExport = ModalUi.ToolButton("Export CSV…", (s, e) => ExportCsv());
            toolbar.Controls.Add(_btnCopy);
            toolbar.Controls.Add(_btnDelete);
            toolbar.Controls.Add(_btnExport);
            toolbar.Controls.Add(ModalUi.ToolButton("Open in Excel", (s, e) => OpenFile()));
            toolbar.Controls.Add(ModalUi.ToolButton("Open Folder", (s, e) => OpenFolder()));

            // ---- List ----
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
                BackgroundColor = Theme.Surface, BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Theme.GridLine,
                RowHeadersVisible = false, EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable   // Ctrl+C copies the card numbers (CopyUids)
            };
            _grid.RowTemplate.Height = 30;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextSoft;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font(CardFonts.UI, 9f, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.DefaultCellStyle.BackColor = Theme.Surface;
            _grid.DefaultCellStyle.ForeColor = Theme.TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            _grid.DefaultCellStyle.SelectionForeColor = Theme.Accent;
            _grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            _grid.SelectionChanged += (s, e) => UpdateButtons();
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ShowStudent(e.RowIndex); };

            _lblEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextSoft,
                BackColor = Theme.Surface, Visible = false, UseMnemonic = false
            };
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(_lblEmpty);
            _lblEmpty.BringToFront();

            _lblSummary = new Label { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(10, 6, 10, 4), UseMnemonic = false };
            ModalUi.StyleStatus(_lblSummary);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(gridHost);
            Controls.Add(toolbar);
            Controls.Add(filters);
            Controls.Add(_lblSummary);

            var tip = new ToolTip();
            tip.SetToolTip(_cboView, "Saved cards = every card number in the RFID file.\n" +
                                     "Loaded students without a card = students in the main list whose card has not been tapped yet.");
            tip.SetToolTip(_btnCopy, "Copy the card numbers of the selected rows (Ctrl+C)");
            tip.SetToolTip(_btnDelete, "Remove the selected cards from the RFID file (Del) — e.g. a lost card. Tap a new card on the RFID tab.");
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
            else if (e.KeyData == (Keys.Control | Keys.C) && _grid.Focused && _btnCopy.Enabled) { CopyUids(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete && _grid.Focused && _btnDelete.Enabled) { DeleteSelected(); e.Handled = true; }
        }

        // ===================== Data =====================

        private void LoadRecords()
        {
            try
            {
                _all = RfidCardWriter.Load(_path);
            }
            catch (Exception ex)
            {
                _all = new List<RfidRecord>();
                AppDialog.Show(this, "Could not read the RFID file:\n\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            ShowRecords();
        }

        private void ShowRecords()
        {
            string q = _txtSearch.Text.Trim();
            var saved = new HashSet<string>(_all.Select(r => r.StudentId), StringComparer.OrdinalIgnoreCase);
            var loaded = new HashSet<string>(_students.Where(s => s != null).Select(s => (s.StudentId ?? "").Trim()),
                StringComparer.OrdinalIgnoreCase);

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            _grid.Columns.Clear();
            int shown = MissingView ? FillMissing(q, saved) : FillSaved(q, loaded);
            _grid.ResumeLayout();

            // ---- Summary ----
            int loadedWithCard = loaded.Count(saved.Contains);
            string coverage = loaded.Count == 0 ? "No student list loaded"
                : string.Format("{0} of {1} loaded students have a card", loadedWithCard, loaded.Count);
            int duplicates = _all.GroupBy(r => r.RfidUid, StringComparer.OrdinalIgnoreCase).Count(g => g.Key.Length > 0 && g.Count() > 1);
            _lblSummary.ForeColor = duplicates > 0 ? Theme.Danger : Theme.TextSoft;
            _lblSummary.Text = string.Format("{0} card(s) saved  ·  {1}{2}{3}\nDouble-click a row to select the student in the main list.",
                _all.Count, coverage,
                shown == (MissingView ? loaded.Count - loadedWithCard : _all.Count) ? "" : "  ·  " + shown + " match the search",
                duplicates > 0 ? "  ·  " + duplicates + " card number(s) saved for more than one student (red) — fix them in Excel" : "");

            if (MissingView)
                _lblEmpty.Text = loaded.Count == 0
                    ? "No student list is loaded.\n\nImport the students first to see who still needs a card."
                    : q.Length > 0 ? "No students without a card match the search."
                    : "Every loaded student has a card saved. ✓";
            else
                _lblEmpty.Text = _all.Count == 0
                    ? "No RFID cards saved yet.\n\nAfter printing, tap each card on the reader on the RFID tab of the main window."
                    : "No cards match the search.";
            _lblEmpty.Visible = shown == 0;
            UpdateButtons();
        }

        private int FillSaved(string q, HashSet<string> loaded)
        {
            AddColumn("Student ID", 100);
            AddColumn("Name (KH)", 170);
            AddColumn("Name (EN)", 170);
            AddColumn("Card number", 150);
            AddColumn("Printed", 125, typeof(DateTime));
            AddColumn("Saved", 125, typeof(DateTime));
            AddColumn("By", 100);
            AddColumn("Computer", 110);
            _grid.Columns[3].DefaultCellStyle.Font = UidFont;

            var duplicateUids = new HashSet<string>(
                _all.GroupBy(r => r.RfidUid, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Key.Length > 0 && g.Count() > 1).Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);

            int count = 0;
            foreach (RfidRecord r in _all
                         .Where(r => q.Length == 0 || Has(r.StudentId, q) || Has(r.NameKh, q) || Has(r.NameEn, q) ||
                                     Has(r.RfidUid, q) || Has(r.UploadedBy, q) || Has(r.Computer, q))
                         .OrderByDescending(r => r.UploadedAt ?? DateTime.MinValue))
            {
                // Boxed empty DateTime? = null, which the grid shows blank and sorts last.
                int i = _grid.Rows.Add(r.StudentId, r.NameKh, r.NameEn, r.RfidUid,
                    r.PrintedAt, r.UploadedAt, r.UploadedBy, r.Computer);
                DataGridViewRow row = _grid.Rows[i];
                row.Tag = r;
                if (duplicateUids.Contains(r.RfidUid)) Tone(row.Cells[3], Theme.Danger);
                if (loaded.Count > 0 && !loaded.Contains(r.StudentId)) Tone(row.Cells[0], Theme.TextSoft);   // not in the list loaded now
                count++;
            }
            return count;
        }

        private int FillMissing(string q, HashSet<string> saved)
        {
            AddColumn("Student ID", 100);
            AddColumn("Name (KH)", 190);
            AddColumn("Name (EN)", 190);
            AddColumn("Faculty", 180);
            AddColumn("Level", 100);
            AddColumn("Gen.", 60);

            int count = 0;
            foreach (Student s in _students.Where(s => s != null && !string.IsNullOrWhiteSpace(s.StudentId) &&
                                                        !saved.Contains(s.StudentId.Trim()) &&
                                                        (q.Length == 0 || Has(s.StudentId, q) || Has(s.NameKh, q) ||
                                                         Has(s.NameEn, q) || Has(s.Faculty, q))))
            {
                int i = _grid.Rows.Add(s.StudentId, s.NameKh, s.NameEn, s.Faculty, s.Level, s.Generation);
                _grid.Rows[i].Tag = s;
                count++;
            }
            return count;
        }

        private void AddColumn(string title, int width, Type type = null)
        {
            var col = new DataGridViewTextBoxColumn
            {
                HeaderText = title, Width = width, SortMode = DataGridViewColumnSortMode.Automatic,
                ValueType = type ?? typeof(string)
            };
            if (type == typeof(DateTime)) col.DefaultCellStyle.Format = DateTimeFormat;
            _grid.Columns.Add(col);
        }

        private static void Tone(DataGridViewCell cell, Color color)
        {
            cell.Style.ForeColor = color;
            cell.Style.SelectionForeColor = color;
        }

        // ===================== Commands =====================

        private void ShowStudent(int rowIndex)
        {
            object tag = _grid.Rows[rowIndex].Tag;
            string id = tag is RfidRecord ? ((RfidRecord)tag).StudentId : tag is Student ? ((Student)tag).StudentId : null;
            if (id == null || _showStudent == null) return;
            if (_showStudent(id))
            {
                Close();
                return;
            }
            AppDialog.Show(this,
                id + " is not in the student list that is loaded now.\n\nImport the list that contains this student, then try again.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private List<RfidRecord> SelectedRecords()
        {
            return _grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index)
                .Select(r => r.Tag as RfidRecord).Where(r => r != null).ToList();
        }

        private void CopyUids()
        {
            List<RfidRecord> selected = SelectedRecords();
            if (selected.Count == 0) return;
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(r => r.RfidUid)));
            }
            catch (ExternalException ex)
            {
                AppDialog.Show(this, "Could not copy to the clipboard:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void DeleteSelected()
        {
            if (_busy) return;
            List<RfidRecord> selected = SelectedRecords();
            if (selected.Count == 0) return;

            string question = selected.Count == 1
                ? "Delete the RFID card of " + selected[0].StudentId + " (" + selected[0].RfidUid + ")?"
                : "Delete the RFID cards of " + selected.Count + " students?";
            if (AppDialog.Show(this, question + "\n\nThe card number(s) are removed from the RFID file. " +
                    "A backup of the file is kept as " + Path.GetFileName(_path) + ".bak.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            _busy = true;
            UseWaitCursor = true;
            UpdateButtons();
            try
            {
                await RfidCardWriter.DeleteAsync(_path, selected.Select(r => r.StudentId).ToList(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    AppDialog.Show(this, "Could not delete the cards:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _busy = false;
                if (!IsDisposed) UseWaitCursor = false;
            }
            if (!IsDisposed) LoadRecords();   // shows what is really in the file now (another PC may have saved meanwhile)
        }

        /// <summary>Saves exactly the rows and columns shown now (view and search applied).</summary>
        private void ExportCsv()
        {
            if (_grid.Rows.Count == 0) return;
            string path;
            using (var dlg = new SaveFileDialog
            {
                Filter = "CSV UTF-8 (*.csv)|*.csv",
                Title = "Export RFID cards",
                FileName = (MissingView ? "students-without-rfid-" : "rfid-cards-") + DateTime.Today.ToString("yyyy-MM-dd") + ".csv",
                OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_path), StringComparison.OrdinalIgnoreCase))
            {
                AppDialog.Show(this, "That is the RFID file itself. Choose another file name for the export.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<DataGridViewColumn> columns = _grid.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex).ToList();
            var header = columns.Select(c => c.HeaderText).ToList();
            var rows = _grid.Rows.Cast<DataGridViewRow>()
                .Select(row => (IList<string>)columns.Select(c => Convert.ToString(row.Cells[c.Index].FormattedValue)).ToList());
            try
            {
                PrintHistory.WriteCsv(path, header, rows);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                AppDialog.Show(this, "Could not save the file:\n\n" + ex.Message +
                    "\n\nIf the file is open in Excel, close it and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AppDialog.Show(this, "Exported " + _grid.Rows.Count + " row(s) to:\n" + path, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenFile()
        {
            if (!File.Exists(_path))
            {
                AppDialog.Show(this, "No RFID card has been saved yet, so the file does not exist:\n" + _path,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                Process.Start(_path);
                AppDialog.Show(this, "Close the file in Excel before tapping the next card — Excel locks it, " +
                    "so card numbers cannot be saved while it is open.\n\nClick Reload (F5) here after editing it.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not open the file:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenFolder()
        {
            try
            {
                if (File.Exists(_path)) Process.Start("explorer.exe", "/select,\"" + _path + "\"");
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(_path) + "\"");
                }
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not open the folder:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateButtons()
        {
            bool cards = !MissingView && _grid.SelectedRows.Count > 0;
            _btnCopy.Enabled = cards;
            _btnDelete.Enabled = cards && !_busy;
            _btnExport.Enabled = _grid.Rows.Count > 0;
        }

        // ===================== Helpers =====================

        private static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Theme.TextSoft, Margin = new Padding(0, 10, 0, 0) };
        }

        private static bool Has(string value, string q)
        {
            return value != null && value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
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
