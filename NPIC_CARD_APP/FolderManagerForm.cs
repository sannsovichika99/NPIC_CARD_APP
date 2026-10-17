using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "Folders" page: every folder the app reads from or writes to (photos, photo backups, OneDrive, app data,
    /// QR logos, card images, program folder, folders of the signature and the last student file), with its status
    /// and what is inside.
    ///   Open (Enter / double-click) = show in Explorer     Change… = choose another Photo Folder
    ///   Create = make a missing folder                     Copy Path (Ctrl+C)     Refresh (F5)
    /// Nothing is ever deleted from here. When the Photo Folder changes, <see cref="PhotoFolderChanged"/> tells the
    /// main window to save the settings and redraw the card.
    /// </summary>
    public class FolderManagerForm : ModalForm
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

        private const string BackupFolderName = "_original";   // same as BackgroundRemovalForm
        private static readonly string[] PhotoExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        private enum FolderState { Ok, Missing, NotSet, NotCreated, Online }

        /// <summary>What Change… does for a row.</summary>
        private enum ChangeKind { None, PhotoFolder }

        /// <summary>One folder of the list.</summary>
        private sealed class Entry
        {
            public string Group, Name, Path, Purpose;
            public FolderState State;
            public string Contents = "";
            public bool Warn;          // shown in orange: the app needs this folder and it is not there
            public bool CanCreate;     // Create makes the folder
            public ChangeKind Change;
            public string ChangeHint;  // where to change it when Change… is not available here
        }

        private readonly AppSettings _settings;
        private List<Entry> _all = new List<Entry>();
        private readonly TextBox _txtSearch;
        private readonly DataGridView _grid;
        private readonly Label _lblDetails, _lblEmpty;
        private readonly Button _btnOpen, _btnChange, _btnCreate, _btnCopy;
        private bool _refreshOnActivate;   // a folder was opened in Explorer: its contents may change

        /// <summary>True when Change… picked another Photo Folder (already stored in the settings, not saved yet).</summary>
        public bool PhotoFolderChanged { get; private set; }

        public FolderManagerForm(AppSettings settings)
        {
            _settings = settings;
            Text = "Folders";
            Subtitle = "Every folder the app uses — open, change or create them";
            Glyph = Theme.IcoFolder;
            StartPosition = FormStartPosition.CenterParent;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1040, work.Width), Math.Min(640, work.Height));
            MinimumSize = new Size(Math.Min(700, work.Width), Math.Min(400, work.Height));
            Font = new Font(CardFonts.UI, 9f);
            BackColor = Surface;
            KeyPreview = true;
            ShowInTaskbar = false;
            MinimizeBox = false;

            // ---- Toolbar ----
            _txtSearch = new TextBox { Width = 220, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 5, 12, 0) };
            _txtSearch.HandleCreated += (s, e) => SetCueBanner(_txtSearch, "Search folders… (Ctrl+F)");
            _txtSearch.TextChanged += (s, e) => ShowEntries();

            _btnOpen   = ModalUi.ToolButton("Open", (s, e) => OpenSelected());
            _btnChange = ModalUi.ToolButton("Change…", (s, e) => ChangeSelected());
            _btnCreate = ModalUi.ToolButton("Create", (s, e) => CreateSelected());
            _btnCopy   = ModalUi.ToolButton("Copy Path", (s, e) => CopySelectedPath());

            var toolbar = ModalUi.Toolbar();
            toolbar.Controls.Add(_txtSearch);
            toolbar.Controls.Add(_btnOpen);
            toolbar.Controls.Add(_btnChange);
            toolbar.Controls.Add(_btnCreate);
            toolbar.Controls.Add(_btnCopy);
            toolbar.Controls.Add(ModalUi.ToolButton("Refresh", (s, e) => Reload()));

            var tip = new ToolTip();
            tip.SetToolTip(_btnOpen, "Open the selected folder in Windows Explorer (Enter or double-click)");
            tip.SetToolTip(_btnChange, "Choose another folder for the selected row");
            tip.SetToolTip(_btnCreate, "Create the selected folder if it does not exist");
            tip.SetToolTip(_btnCopy, "Copy the folder path to the clipboard (Ctrl+C)");

            // ---- List ----
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                BackgroundColor = Surface, BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = GridLine,
                RowHeadersVisible = false, EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, ShowCellToolTips = true,
                StandardTab = true
            };
            _grid.RowTemplate.MinimumHeight = 30;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font(CardFonts.UI, 9f, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.DefaultCellStyle.ForeColor = TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            _grid.DefaultCellStyle.SelectionForeColor = Accent;
            _grid.DefaultCellStyle.Padding = new Padding(4, 3, 4, 3);

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Group", HeaderText = "Group", Width = 120 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Folder", HeaderText = "Folder", Width = 190 });
            var location = new DataGridViewTextBoxColumn
            {
                Name = "Location", HeaderText = "Location", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            location.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.Columns.Add(location);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 120 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Contents", HeaderText = "Contents", Width = 190 });
            _grid.Columns["Group"].DefaultCellStyle.Font = new Font(CardFonts.UI, 9f, FontStyle.Bold);
            _grid.Columns["Group"].DefaultCellStyle.ForeColor = Accent;
            _grid.Columns["Contents"].DefaultCellStyle.ForeColor = TextMuted;
            foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

            _grid.SelectionChanged += (s, e) => UpdateButtons();
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenSelected(); };

            _lblEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted, BackColor = Surface,
                Text = "No folder matches the search.", Visible = false
            };
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(_lblEmpty);
            _lblEmpty.BringToFront();

            _lblDetails = new Label { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(10, 6, 10, 4) };
            ModalUi.StyleStatus(_lblDetails);

            Controls.Add(gridHost);
            Controls.Add(toolbar);
            Controls.Add(_lblDetails);

            Reload();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _grid.Focus();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (!_refreshOnActivate) return;
            _refreshOnActivate = false;
            Reload();   // files may have been added or removed in Explorer
        }

        /// <summary>Enter / Ctrl+C on the list: handled here because the grid would take them before KeyDown.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_grid.Focused)
            {
                if (keyData == Keys.Enter) { OpenSelected(); return true; }
                if (keyData == (Keys.Control | Keys.C)) { CopySelectedPath(); return true; }
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
        private void Reload()
        {
            string selected = Selected == null ? null : Selected.Name;
            Cursor = Cursors.WaitCursor;
            try { _all = Collect(_settings); }
            finally { Cursor = Cursors.Default; }
            ShowEntries();
            SelectByName(selected);
        }

        private void ShowEntries()
        {
            string q = _txtSearch.Text.Trim();
            List<Entry> shown = q.Length == 0 ? _all
                : _all.Where(x => Has(x.Group, q) || Has(x.Name, q) || Has(x.Path, q) || Has(x.Purpose, q)).ToList();

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            string lastGroup = null;
            foreach (Entry x in shown)
            {
                // Name the group on its first row only, so the list reads as groups.
                int i = _grid.Rows.Add(x.Group == lastGroup ? "" : x.Group, x.Name, Or(x.Path, "(not set)"),
                    StateText(x), x.Contents);
                lastGroup = x.Group;
                DataGridViewRow row = _grid.Rows[i];
                row.Tag = x;

                string tip = x.Purpose + (x.ChangeHint != null ? "\n" + x.ChangeHint : "");
                foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = tip;

                Color stateColor = x.Warn ? Warning : x.State == FolderState.Ok || x.State == FolderState.Online ? Success : TextMuted;
                row.Cells["Status"].Style.ForeColor = stateColor;
                row.Cells["Status"].Style.SelectionForeColor = stateColor;
                if (x.Warn)
                {
                    row.Cells["Location"].Style.ForeColor = Warning;
                    row.Cells["Location"].Style.SelectionForeColor = Warning;
                }
            }
            _grid.ResumeLayout();
            _lblEmpty.Visible = shown.Count == 0;
            UpdateButtons();
        }

        private Entry Selected
        {
            get { return _grid.SelectedRows.Count == 0 ? null : _grid.SelectedRows[0].Tag as Entry; }
        }

        private void SelectByName(string name)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var x = row.Tag as Entry;
                if (x != null && (name == null || x.Name == name))
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells[1];
                    return;
                }
            }
        }

        private void UpdateButtons()
        {
            Entry x = Selected;
            _btnOpen.Enabled = x != null && (x.State == FolderState.Ok || x.State == FolderState.Online);
            _btnChange.Enabled = x != null && x.Change != ChangeKind.None;
            _btnCreate.Enabled = x != null && x.CanCreate && (x.State == FolderState.Missing || x.State == FolderState.NotCreated);
            _btnCopy.Enabled = x != null && !string.IsNullOrEmpty(x.Path);

            int warnings = _all.Count(e => e.Warn);
            string summary = _all.Count + " folders" +
                (warnings > 0 ? "  ·  " + warnings + " need attention (orange)" : "  ·  all folders the app needs are there");
            if (x == null)
            {
                _lblDetails.Text = summary + Environment.NewLine + "Select a folder to see what it is used for.";
                _lblDetails.ForeColor = warnings > 0 ? Warning : TextMuted;
                return;
            }
            _lblDetails.Text = x.Name + " — " + x.Purpose +
                (x.ChangeHint != null ? "  " + x.ChangeHint : "") + Environment.NewLine + summary;
            _lblDetails.ForeColor = x.Warn ? Warning : TextMuted;
        }

        // ===================== Actions =====================
        private void OpenSelected()
        {
            Entry x = Selected;
            if (x == null || string.IsNullOrEmpty(x.Path)) return;
            try
            {
                if (x.State == FolderState.Online) Process.Start(x.Path);   // OneDrive share link: open in the browser
                else if (Directory.Exists(x.Path))
                {
                    Process.Start("explorer.exe", "\"" + x.Path + "\"");
                    _refreshOnActivate = true;
                }
                else AppDialog.Show(this, "This folder does not exist:\n" + x.Path +
                        (x.CanCreate ? "\n\nUse Create to make it." : ""), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not open the folder:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ChangeSelected()
        {
            Entry x = Selected;
            if (x == null || x.Change != ChangeKind.PhotoFolder) return;

            using (var dlg = new FolderBrowserDialog
            {
                Description = "Folder with photos named {StudentId}.jpg / .png",
                SelectedPath = Directory.Exists(_settings.PhotoFolder ?? "") ? _settings.PhotoFolder : "",
                ShowNewFolderButton = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (string.Equals(dlg.SelectedPath, _settings.PhotoFolder, StringComparison.OrdinalIgnoreCase)) return;
                _settings.PhotoFolder = dlg.SelectedPath;
                PhotoFolderChanged = true;
            }
            Reload();
        }

        private void CreateSelected()
        {
            Entry x = Selected;
            if (x == null || !x.CanCreate || string.IsNullOrEmpty(x.Path)) return;
            try
            {
                Directory.CreateDirectory(x.Path);
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not create the folder:\n" + x.Path + "\n\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Reload();
        }

        private void CopySelectedPath()
        {
            Entry x = Selected;
            if (x == null || string.IsNullOrEmpty(x.Path)) return;
            try
            {
                Clipboard.SetText(x.Path);
                _lblDetails.Text = "Copied to the clipboard:" + Environment.NewLine + x.Path;
                _lblDetails.ForeColor = TextMuted;
            }
            catch (ExternalException ex)
            {
                AppDialog.Show(this, "Could not copy to the clipboard:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ===================== Data =====================
        private static List<Entry> Collect(AppSettings s)
        {
            var list = new List<Entry>();
            string appData = Path.GetDirectoryName(AppSettings.FilePath);

            // ---- Photos ----
            const string photos = "Photos";
            bool hasPhotos = !string.IsNullOrEmpty(s.PhotoFolder);
            Entry photo = Make(photos, "Photo folder", s.PhotoFolder,
                "Student photos named {StudentId}.jpg / .png — the cards read photos from here.", true, true);
            photo.Change = ChangeKind.PhotoFolder;
            if (photo.State == FolderState.Ok) photo.Contents = CountPhotos(s.PhotoFolder);
            list.Add(photo);

            Entry backup = Make(photos, "Original photos", hasPhotos ? Path.Combine(s.PhotoFolder, BackupFolderName) : null,
                "Untouched photos kept by Remove Background… so Restore can bring them back. Created on first use.", false, false);
            if (backup.State == FolderState.Missing) backup.State = FolderState.NotCreated;
            else if (backup.State == FolderState.Ok) backup.Contents = CountPhotos(backup.Path);
            list.Add(backup);

            string oneDrive = (s.OneDriveFolder ?? "").Trim();
            Entry od;
            if (oneDrive.Length > 0 && OneDriveOnlineSync.IsOnline(oneDrive))
            {
                od = new Entry
                {
                    Group = photos, Name = "OneDrive photos", Path = oneDrive, State = FolderState.Online,
                    Contents = "Online — sign-in needed",
                    Purpose = "OneDrive share link that OneDrive Photos… downloads student photos from."
                };
            }
            else
            {
                od = Make(photos, "OneDrive photos", oneDrive,
                    "OneDrive folder synced to this PC that OneDrive Photos… copies student photos from.", false, false);
                if (od.State == FolderState.Missing) od.Warn = true;   // OneDrive signed out, or the folder moved
                if (od.State == FolderState.Ok) od.Contents = CountPhotos(oneDrive);
            }
            od.ChangeHint = "Change it in OneDrive Photos… (Ctrl+Shift+O).";
            list.Add(od);

            // ---- App data (%AppData%\NPIC_CARD_APP) ----
            const string data = "App data";
            list.Add(Make(data, "Settings & history", appData,
                "settings.xml, its backup and print-history.csv for this Windows user.", false, true));
            Entry logos = Make(data, "QR logos", Path.Combine(appData, "Logos"),
                "Copies of the logos chosen in Card Text… for the QR code, so cards still print if the original moves.", false, true);
            if (logos.State == FolderState.Missing) logos.State = FolderState.NotCreated;
            list.Add(logos);

            // ---- Program ----
            const string program = "Program";
            list.Add(Make(program, "App folder", AppDomain.CurrentDomain.BaseDirectory,
                "Where NPIC_CARD_APP.exe is installed.", true, false));
            Entry assets = Make(program, "Card images (Assets)", AssetStore.Folder,
                "Logo, stamp, QR code and social icons printed on every card.", true, false);
            string[] missingAssets = AssetStore.MissingFiles();
            if (missingAssets.Length > 0)
            {
                assets.Warn = true;
                assets.Contents = "Missing: " + string.Join(", ", missingAssets);
            }
            list.Add(assets);

            // ---- Files in use ----
            const string files = "Files in use";
            list.Add(Make(files, "Signature folder", ParentOf(s.SignaturePath),
                "Folder of the director's signature image (Signature… in the sidebar).", false, false));
            list.Add(Make(files, "Last student file folder", ParentOf(s.LastCsvPath),
                "Folder of the Excel / CSV file the students were last imported from.", false, false));

            return list;
        }

        /// <summary>Row for a folder on this PC; <paramref name="required"/> = orange when it is missing or not set.</summary>
        private static Entry Make(string group, string name, string path, string purpose, bool required, bool canCreate)
        {
            var x = new Entry { Group = group, Name = name, Path = string.IsNullOrEmpty(path) ? null : path.TrimEnd('\\'), Purpose = purpose };
            if (x.Path == null) { x.State = FolderState.NotSet; x.Warn = required; return x; }
            if (x.Path.Length == 2 && x.Path[1] == ':') x.Path += "\\";   // "C:" -> "C:\"

            if (Directory.Exists(x.Path))
            {
                x.State = FolderState.Ok;
                x.Contents = Describe(x.Path);
            }
            else
            {
                x.State = FolderState.Missing;
                x.Warn = required;
            }
            x.CanCreate = canCreate;
            return x;
        }

        private static string StateText(Entry x)
        {
            switch (x.State)
            {
                case FolderState.Ok:         return "✔ OK";
                case FolderState.Missing:    return "Not found";
                case FolderState.NotSet:     return "Not set";
                case FolderState.NotCreated: return "Not created yet";
                default:                     return "Online link";
            }
        }

        /// <summary>"12 files, 3 folders · 4.2 MB" (top level only, so big folders stay quick).</summary>
        private static string Describe(string folder)
        {
            try
            {
                var dir = new DirectoryInfo(folder);
                FileInfo[] fileList = dir.GetFiles();
                int folders = dir.GetDirectories().Length;
                long bytes = fileList.Sum(f => f.Length);
                string text = Plural(fileList.Length, "file");
                if (folders > 0) text += ", " + Plural(folders, "folder");
                return fileList.Length > 0 ? text + "  ·  " + FormatSize(bytes) : text;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is SecurityException)
            {
                return "Cannot read: " + ex.Message;
            }
        }

        private static string CountPhotos(string folder)
        {
            try
            {
                var images = new DirectoryInfo(folder).GetFiles()
                    .Where(f => PhotoExtensions.Contains(f.Extension.ToLowerInvariant())).ToList();
                return Plural(images.Count, "photo") + (images.Count > 0 ? "  ·  " + FormatSize(images.Sum(f => f.Length)) : "");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is SecurityException)
            {
                return "Cannot read: " + ex.Message;
            }
        }

        private static string ParentOf(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            try { return Path.GetDirectoryName(file); }
            catch (ArgumentException) { return null; }   // invalid characters in an old saved path
        }

        // ===================== Helpers =====================
        private static string Plural(int n, string word) { return n + " " + word + (n == 1 ? "" : "s"); }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("0.#") + " MB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.##") + " GB";
        }

        private static string Or(string value, string fallback) { return string.IsNullOrEmpty(value) ? fallback : value; }

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
