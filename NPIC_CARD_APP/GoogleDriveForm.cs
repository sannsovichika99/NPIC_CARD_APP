using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "Google Drive Photos" page: copies student photos from a shared Google Drive folder into the Photo Folder.
    ///   Scan Folder (F5) = list the Drive photos and compare them with the Photo Folder (nothing is written).
    ///   Download = save the new (and, when ticked, changed) photos as {StudentId}.jpg / .png.
    /// The folder link and the API key (DPAPI-encrypted) are kept in <see cref="AppSettings"/>.
    /// </summary>
    public class GoogleDriveForm : ModalForm
    {
        private static readonly Color Accent     = Theme.Accent;
        private static readonly Color AccentSoft = Theme.AccentSoft;
        private static readonly Color Surface    = Theme.Surface;
        private static readonly Color SurfaceAlt = Theme.SurfaceAlt;
        private static readonly Color GridLine   = Theme.GridLine;
        private static readonly Color TextMain   = Theme.TextMain;
        private static readonly Color TextMuted  = Theme.TextSoft;
        private static readonly Color Success    = Theme.Success;
        private static readonly Color Warning    = Theme.Warning;
        private static readonly Color Danger     = Theme.Danger;

        private const string SetupSteps =
            "Connect the Google Drive folder with the student photos\n\n" +
            "1.  In Google Drive right-click the folder → Share → General access → “Anyone with the link” (Viewer) → Copy link.\n" +
            "2.  Paste the link above. Photos in sub-folders (e.g. Google Form uploads) are found too.\n" +
            "3.  Paste a Google API key with the Google Drive API turned on — click “Get a Key…” (free, once per school).\n" +
            "4.  Click Scan Folder, check the list, then Download.\n\n" +
            "Files named NPIC24001.jpg or “NPIC24001 - Sok Dara.jpg” go to student NPIC24001.\n" +
            "Only new photos are downloaded; photos edited with Remove Background are never replaced.";

        private readonly AppSettings _settings;
        private readonly IList<Student> _students;

        private readonly TextBox _txtFolder, _txtKey;
        private readonly CheckBox _chkShowKey, _chkReplace;
        private readonly Label _lblSaveTo, _lblEmpty, _lblStatus;
        private readonly DataGridView _grid;
        private readonly Button _btnScan, _btnOpenDrive, _btnGetKey, _btnChange, _btnOpenLocal, _btnDownload, _btnClose;
        private readonly Panel _top;

        private CancellationTokenSource _cts;
        private bool _closeWhenDone;
        private List<DriveFile> _files;
        private List<PhotoSyncItem> _items;

        /// <summary>Photos written to the Photo Folder while the page was open.</summary>
        public int Downloaded { get; private set; }

        /// <param name="students">The loaded student list, used to match file names to students (may be empty).</param>
        public GoogleDriveForm(AppSettings settings, IList<Student> students)
        {
            _settings = settings;
            _students = students ?? new List<Student>();

            Text = "Google Drive Photos";
            Subtitle = "Download student photos from a shared Google Drive folder";
            Glyph = Theme.IcoSync;
            StartPosition = FormStartPosition.CenterParent;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1040, work.Width), Math.Min(700, work.Height));
            MinimumSize = new Size(Math.Min(760, work.Width), Math.Min(520, work.Height));
            Font = new Font(CardFonts.UI, 9f);
            BackColor = Surface;
            ForeColor = TextMain;
            KeyPreview = true;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // ---- Connection rows ----
            _txtFolder = MakeText(settings.DriveFolderUrl, "Paste the folder link: https://drive.google.com/drive/folders/…");
            _txtKey = MakeText(settings.DriveApiKey, "Google API key, e.g. AIzaSy…");
            _txtKey.UseSystemPasswordChar = true;
            _txtFolder.TextChanged += (s, e) => UpdateButtons();
            _txtKey.TextChanged += (s, e) => UpdateButtons();

            _chkShowKey = new CheckBox { Text = "Show", AutoSize = true, Margin = new Padding(0, 12, 6, 0), BackColor = SurfaceAlt };
            _chkShowKey.CheckedChanged += (s, e) => _txtKey.UseSystemPasswordChar = !_chkShowKey.Checked;

            _btnScan = RowButton("Scan Folder (F5)", PillKind.Primary, async (s, e) => await ScanAsync());
            _btnOpenDrive = RowButton("Open in Browser", PillKind.Secondary, (s, e) => OpenDriveFolder());
            _btnGetKey = RowButton("Get a Key…", PillKind.Secondary, (s, e) => OpenUrl(GoogleDrivePhotoSync.ApiKeyHelpUrl));
            _btnChange = RowButton("Change…", PillKind.Secondary, (s, e) => ChooseSaveFolder());
            _btnOpenLocal = RowButton("Open Folder", PillKind.Secondary, (s, e) => OpenLocalFolder());

            _lblSaveTo = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
                Margin = new Padding(0, 4, 10, 0)
            };

            var rows = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, BackColor = SurfaceAlt, Padding = new Padding(16, 6, 10, 6)
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 3; i++) rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            AddRow(rows, 0, "Folder link", _txtFolder, _btnScan, _btnOpenDrive);
            AddRow(rows, 1, "API key", _txtKey, _chkShowKey, _btnGetKey);
            AddRow(rows, 2, "Save to", _lblSaveTo, _btnChange, _btnOpenLocal);
            _top = new Panel { Dock = DockStyle.Top, Height = 134, BackColor = SurfaceAlt };
            _top.Controls.Add(rows);

            // ---- Photo list ----
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
                BackgroundColor = Surface, BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = GridLine,
                RowHeadersVisible = false, EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Visible = false
            };
            _grid.RowTemplate.Height = 30;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font(CardFonts.UI, 9f, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.DefaultCellStyle.BackColor = Surface;
            _grid.DefaultCellStyle.ForeColor = TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            _grid.DefaultCellStyle.SelectionForeColor = Accent;
            _grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceAlt;
            AddColumn("File on Drive", 190);
            AddColumn("Student", 170);
            AddColumn("Size", 55).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            AddColumn("Modified", 85);
            AddColumn("Result", 150);

            _lblEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted, BackColor = Surface, Text = SetupSteps
            };
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(_lblEmpty);

            _lblStatus = new Label { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(10, 6, 10, 4), UseMnemonic = false };
            ModalUi.StyleStatus(_lblStatus);

            // ---- Footer (first added = rightmost) ----
            var footer = ModalUi.Footer();
            _btnClose = ModalUi.Button("Close", PillKind.Secondary, (s, e) =>
            {
                if (_cts != null) _cts.Cancel();   // stop scanning / downloading, keep the page open
                else DialogResult = DialogResult.Cancel;
            });
            _btnDownload = ModalUi.Button("Download Photos", PillKind.Primary, async (s, e) => await DownloadAsync());
            _chkReplace = new CheckBox
            {
                Text = "Replace photos that changed on Drive", AutoSize = true, Checked = true, BackColor = SurfaceAlt,
                Margin = new Padding(0, 7, 18, 0)
            };
            _chkReplace.CheckedChanged += (s, e) => { RefreshGrid(); UpdateButtons(); };
            footer.Controls.Add(_btnClose);
            footer.Controls.Add(_btnDownload);
            footer.Controls.Add(_chkReplace);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(gridHost);
            Controls.Add(_top);
            Controls.Add(_lblStatus);
            Controls.Add(footer);
            CancelButton = _btnClose;

            var tip = new ToolTip();
            tip.SetToolTip(_btnScan, "List the photos in the Drive folder and compare them with the Photo Folder. Nothing is written yet.");
            tip.SetToolTip(_btnGetKey, "Google Cloud Console → Create credentials → API key, then APIs & Services → Library → " +
                                       "Google Drive API → Enable. The key is saved encrypted for this Windows user.");
            tip.SetToolTip(_btnChange, "Choose the Photo Folder the cards read photos from");
            tip.SetToolTip(_chkReplace, "Ticked: a photo that differs from the one on Drive is replaced by the Drive copy.\n" +
                                        "Photos edited with Remove Background are always kept.");

            ShowSaveTo();
            ShowStatus(_students.Count == 0
                ? "No student list loaded: file names are taken as the StudentId. Import the students first to match names like “NPIC24001 - Sok Dara.jpg”."
                : "Not connected.", _students.Count == 0 ? Warning : TextMuted);
            UpdateButtons();
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_txtFolder.TextLength == 0) { _txtFolder.Focus(); return; }
            if (_txtKey.TextLength == 0) { _txtKey.Focus(); return; }
            _btnScan.Focus();
            await ScanAsync();   // saved folder: show what is new right away
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5) { BeginScan(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && (_txtFolder.Focused || _txtKey.Focused)) { BeginScan(); e.Handled = e.SuppressKeyPress = true; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_cts != null)
            {
                // Closed (X / Alt+F4) while busy: cancel first; the running task then closes the page.
                e.Cancel = true;
                _closeWhenDone = true;
                _cts.Cancel();
            }
            base.OnFormClosing(e);
        }

        // ===================== Scan =====================
        private async void BeginScan()
        {
            await ScanAsync();
        }

        private async Task ScanAsync()
        {
            if (_cts != null) return;

            string link = _txtFolder.Text.Trim(), key = _txtKey.Text.Trim();
            try
            {
                string id, resourceKey;
                GoogleDrivePhotoSync.ParseFolderLink(link, out id, out resourceKey);   // check before going online
                if (key.Length == 0) throw new ArgumentException("Enter a Google API key. Click “Get a Key…” to create one (free).");
            }
            catch (ArgumentException ex)
            {
                ShowStatus(OneLine(ex.Message), Danger);
                (link.Length == 0 || key.Length > 0 ? _txtFolder : _txtKey).Focus();
                return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true, "Reading the Google Drive folder…  (Stop cancels)");
            try
            {
                List<DriveFile> files = await GoogleDrivePhotoSync.ListImagesAsync(link, key, _cts.Token);
                _settings.DriveFolderUrl = link;
                _settings.DriveApiKey = key;
                _files = files;
                await PlanAsync();
            }
            catch (OperationCanceledException)
            {
                ShowStatus("Cancelled.", TextMuted);
            }
            catch (Exception ex)
            {
                ShowStatus(OneLine(ex.Message), Danger);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false, null);
            }

            if (_closeWhenDone) DialogResult = DialogResult.Cancel;
        }

        /// <summary>Compares the listed Drive files with the Photo Folder (local only) and shows the result.</summary>
        private async Task PlanAsync()
        {
            if (_files == null) return;
            List<DriveFile> files = _files;
            string folder = _settings.PhotoFolder ?? "";
            _items = await Task.Run(() => GoogleDrivePhotoSync.Plan(files, folder, _students));
            _items = _items.OrderBy(i => SortOrder(i.Status)).ThenBy(i => i.File.Name, StringComparer.OrdinalIgnoreCase).ToList();
            FillGrid();
            ShowSummary();
        }

        // ===================== Download =====================
        private async Task DownloadAsync()
        {
            if (_cts != null || _items == null) return;

            if (string.IsNullOrEmpty(_settings.PhotoFolder))
            {
                // The list was compared with no folder at all: compare it with the chosen one, then let the user look again.
                if (ChooseSaveFolder(false)) { await PlanAsync(); UpdateButtons(); }
                return;
            }

            List<PhotoSyncItem> todo = _items.Where(i => i.WillDownload(_chkReplace.Checked)).ToList();
            if (todo.Count == 0) return;

            _cts = new CancellationTokenSource();
            SetBusy(true, string.Format("Downloading 0 / {0} photo(s)…  (Stop cancels)", todo.Count));
            var progress = new Progress<int>(done =>
            {
                if (_cts != null) ShowStatus(string.Format("Downloading {0} / {1} photo(s)…  (Stop cancels)", done, todo.Count), TextMuted);
                RefreshGrid();
            });
            bool cancelled = false;
            string error = null;
            try
            {
                await GoogleDrivePhotoSync.DownloadAsync(todo, _settings.PhotoFolder, _txtKey.Text, progress, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                error = OneLine(ex.Message);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false, null);
            }

            int ok = todo.Count(i => i.Status == PhotoSyncStatus.Downloaded);
            int failed = todo.Count(i => i.Status == PhotoSyncStatus.Failed);
            Downloaded += ok;
            RefreshGrid();
            UpdateButtons();
            ShowSaveTo();
            if (error != null) ShowStatus(error, Danger);
            else ShowStatus(string.Format("{0}{1} photo(s) downloaded to {2}{3}",
                    cancelled ? "Stopped — " : "✔  ", ok, _settings.PhotoFolder,
                    failed > 0 ? "  ·  " + failed + " failed (see Result)" : ""),
                failed > 0 || cancelled ? Warning : Success);

            if (_closeWhenDone) DialogResult = DialogResult.Cancel;
        }

        // ===================== List =====================
        private void FillGrid()
        {
            _grid.Rows.Clear();
            foreach (PhotoSyncItem item in _items)
            {
                DriveFile f = item.File;
                int row = _grid.Rows.Add(
                    f.Folder.Length == 0 ? f.Name : f.Folder + " / " + f.Name,
                    item.StudentId == null ? "—" : item.StudentId + (string.IsNullOrEmpty(item.StudentName) ? "" : "  " + item.StudentName),
                    f.Size > 0 ? Math.Max(1, (f.Size + 512) / 1024) + " KB" : "",
                    f.Modified.HasValue ? f.Modified.Value.ToString("yyyy-MM-dd") : "",
                    "");
                _grid.Rows[row].Tag = item;
            }
            RefreshGrid();
            _grid.Visible = _items.Count > 0;
            _lblEmpty.Visible = _items.Count == 0;
            _lblEmpty.Text = _items.Count == 0 ? "The Google Drive folder has no pictures.\n\nCheck the link, or upload the photos to it." : SetupSteps;
        }

        /// <summary>Updates the Result column (statuses change while downloading).</summary>
        private void RefreshGrid()
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var item = row.Tag as PhotoSyncItem;
                if (item == null) continue;
                Color color;
                DataGridViewCell cell = row.Cells[4];
                cell.Value = Describe(item, _chkReplace.Checked, out color);
                cell.Style.ForeColor = cell.Style.SelectionForeColor = color;
                cell.ToolTipText = item.Error ?? "";
            }
        }

        private static string Describe(PhotoSyncItem item, bool replaceChanged, out Color color)
        {
            switch (item.Status)
            {
                case PhotoSyncStatus.New: color = Accent; return "New — will download";
                case PhotoSyncStatus.Changed:
                    color = Warning;
                    return replaceChanged ? "Changed — will replace" : "Changed on Drive — kept";
                case PhotoSyncStatus.UpToDate: color = TextMuted; return "Up to date";
                case PhotoSyncStatus.KeptEdited: color = TextMuted; return "Kept — edited in the app";
                case PhotoSyncStatus.NotMatched: color = Warning; return "No student with this ID";
                case PhotoSyncStatus.Unsupported: color = Danger; return "Not JPG/PNG — skipped";
                case PhotoSyncStatus.Duplicate: color = TextMuted; return "Older copy — newer one used";
                case PhotoSyncStatus.Downloaded: color = Success; return "✔ Downloaded";
                default: color = Danger; return "Failed: " + item.Error;
            }
        }

        private static int SortOrder(PhotoSyncStatus status)
        {
            switch (status)
            {
                case PhotoSyncStatus.New: return 0;
                case PhotoSyncStatus.Changed: return 1;
                case PhotoSyncStatus.NotMatched: return 2;
                case PhotoSyncStatus.Unsupported: return 3;
                case PhotoSyncStatus.Duplicate: return 4;
                case PhotoSyncStatus.KeptEdited: return 5;
                default: return 6;
            }
        }

        private void ShowSummary()
        {
            Func<PhotoSyncStatus, int> count = st => _items.Count(i => i.Status == st);
            var parts = new List<string> { _items.Count + " picture(s) on Drive" };
            Action<int, string> part = (n, text) => { if (n > 0) parts.Add(n + " " + text); };
            part(count(PhotoSyncStatus.New), "new");
            part(count(PhotoSyncStatus.Changed), "changed");
            part(count(PhotoSyncStatus.UpToDate), "up to date");
            part(count(PhotoSyncStatus.KeptEdited), "edited here");
            part(count(PhotoSyncStatus.NotMatched), "not matched");
            part(count(PhotoSyncStatus.Unsupported), "not JPG/PNG");
            part(count(PhotoSyncStatus.Duplicate), "older copies");

            bool problems = count(PhotoSyncStatus.NotMatched) + count(PhotoSyncStatus.Unsupported) > 0;
            ShowStatus(string.Format("Scanned {0:HH:mm} — {1}", DateTime.Now, string.Join("  ·  ", parts)), problems ? Warning : Success);
        }

        // ===================== Folders / browser =====================
        /// <summary>Asks for the Photo Folder; true when one was chosen.</summary>
        /// <param name="replan">Compare the Drive list with the new folder right away.</param>
        private bool ChooseSaveFolder(bool replan = true)
        {
            using (var dlg = new FolderBrowserDialog
            {
                Description = "Photo Folder — the Drive photos are saved here as {StudentId}.jpg / .png",
                SelectedPath = _settings.PhotoFolder ?? ""
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                _settings.PhotoFolder = dlg.SelectedPath;
            }
            ShowSaveTo();
            if (replan && _files != null) BeginPlan();   // statuses depend on what is already in the folder
            return true;
        }

        private async void BeginPlan()
        {
            try { await PlanAsync(); }
            catch (Exception ex) { ShowStatus(OneLine(ex.Message), Danger); }
            UpdateButtons();
        }

        private void ShowSaveTo()
        {
            bool set = !string.IsNullOrEmpty(_settings.PhotoFolder);
            _lblSaveTo.Text = set ? _settings.PhotoFolder : "No Photo Folder yet — click Change… (or Download asks for one)";
            _lblSaveTo.ForeColor = set ? TextMain : Warning;
            _btnOpenLocal.Enabled = set && Directory.Exists(_settings.PhotoFolder);
        }

        private void OpenDriveFolder()
        {
            try { OpenUrl(GoogleDrivePhotoSync.FolderWebUrl(_txtFolder.Text)); }
            catch (ArgumentException ex) { ShowStatus(OneLine(ex.Message), Danger); }
        }

        private void OpenLocalFolder()
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + _settings.PhotoFolder + "\""); }
            catch (Exception ex) { ShowStatus("Could not open the folder: " + ex.Message, Danger); }
        }

        private void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception ex) { ShowStatus("Could not open the browser: " + ex.Message, Danger); }
        }

        // ===================== UI state =====================
        private void SetBusy(bool busy, string message)
        {
            _top.Enabled = _chkReplace.Enabled = !busy;
            _btnClose.Text = busy ? "Stop" : "Close";
            UseWaitCursor = busy;
            if (busy) ShowStatus(message, TextMuted);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool busy = _cts != null;
            _btnScan.Enabled = !busy && _txtFolder.TextLength > 0 && _txtKey.TextLength > 0;
            _btnOpenDrive.Enabled = !busy && _txtFolder.TextLength > 0;
            int todo = _items == null ? 0 : _items.Count(i => i.WillDownload(_chkReplace.Checked));
            _btnDownload.Enabled = !busy && todo > 0;
            _btnDownload.Text = todo > 0 ? string.Format("Download {0} Photo(s)", todo) : "Download Photos";
        }

        private void ShowStatus(string text, Color color)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
        }

        private static string OneLine(string text)
        {
            return (text ?? "").Replace("\r", "").Replace("\n\n", "  ").Replace("\n", "  ");
        }

        // ===================== UI helpers =====================
        private DataGridViewTextBoxColumn AddColumn(string header, float weight)
        {
            var col = new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable };
            _grid.Columns.Add(col);
            return col;
        }

        private static void AddRow(TableLayoutPanel rows, int row, string caption, Control field, Control a, Control b)
        {
            rows.Controls.Add(new Label
            {
                Text = caption, AutoSize = true, ForeColor = TextMuted, Margin = new Padding(0, 12, 6, 0)
            }, 0, row);
            rows.Controls.Add(field, 1, row);
            rows.Controls.Add(a, 2, row);
            rows.Controls.Add(b, 3, row);
        }

        private static Button RowButton(string text, PillKind kind, EventHandler onClick)
        {
            Button b = ModalUi.Button(text, kind, onClick);
            b.Margin = new Padding(0, 4, 6, 0);
            return b;
        }

        private static TextBox MakeText(string text, string cue)
        {
            var box = new TextBox { Dock = DockStyle.Fill, Text = text ?? "", Margin = new Padding(0, 9, 10, 0) };
            box.HandleCreated += (s, e) => SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, cue);
            return box;
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }
}
