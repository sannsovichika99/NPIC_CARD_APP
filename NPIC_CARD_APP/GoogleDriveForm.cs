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
            "1.  In Google Drive right-click the folder → Share → General access → “Anyone with the link” (Viewer) → Copy link.\n" +
            "2.  Paste the link above. Photos in sub-folders (e.g. Google Form uploads) are found too.\n" +
            "3.  Paste a Google API key with the Google Drive API turned on — click “Get a Key…” (free, once per school).\n" +
            "4.  Click Scan Folder (F5), check the Student Photos tab, then Download.\n\n" +
            "Files named NPIC24001.jpg or “NPIC24001 - Sok Dara.jpg” go to student NPIC24001.\n" +
            "Only new photos are downloaded; photos edited with Remove Background are never replaced.";

        private const int PhotosTab = 0, SettingsTab = 1;

        private readonly AppSettings _settings;
        private readonly IList<Student> _students;

        private readonly PillTabs _tabs;
        private readonly Control _photosPage, _settingsPage;
        private readonly TextBox _txtFolder, _txtKey;
        private readonly CheckBox _chkShowKey, _chkReplace;
        private readonly Label _lblSaveTo, _lblSource, _lblEmpty, _lblStatus;
        private readonly DataGridView _grid;
        private readonly Control _emptyHost;
        private readonly Button _btnScan, _btnOpenDrive, _btnGetKey, _btnChange, _btnOpenLocal, _btnDownload, _btnClose,
                                _btnGoSettings;

        private CancellationTokenSource _cts;
        private bool _closeWhenDone;
        private List<DriveFile> _files;
        private List<PhotoSyncItem> _items;

        // Photo list grouped by Drive folder: one header row per folder, its photos below.
        private const string ChevronDown = "", ChevronRight = "";   // Segoe Fluent Icons / MDL2 Assets
        private const int GroupRowHeight = 40, GroupGap = 6;
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // kept across scans
        private readonly Font _groupFont = new Font(CardFonts.UI, 9f, FontStyle.Bold);

        /// <summary>Tag of a folder header row.</summary>
        private sealed class FolderGroup
        {
            public string Path;                  // "" = the shared folder itself, else "Sub/Sub"
            public List<PhotoSyncItem> Items;
        }

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

            // ---- Tab bar: tabs | where the photos go | Scan (works from both tabs) ----
            _tabs = new PillTabs("Student Photos", "Drive Settings") { Dock = DockStyle.Left, Margin = new Padding(0) };
            _tabs.SelectedIndexChanged += (s, e) => OnTabChanged();
            _btnScan = ModalUi.Button("Scan Folder (F5)", PillKind.Primary, async (s, e) => await ScanAsync());
            _btnScan.Dock = DockStyle.Right;
            _btnScan.MinimumSize = new Size(128, 32);
            _lblSource = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
                Padding = new Padding(16, 0, 12, 0)
            };
            var tabBar = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(16, 12, 16, 12), BackColor = Surface };
            tabBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(GridLine)) e.Graphics.DrawLine(pen, 0, tabBar.Height - 1, tabBar.Width, tabBar.Height - 1);
            };
            tabBar.Controls.Add(_lblSource);   // fill first, edges last
            tabBar.Controls.Add(_btnScan);
            tabBar.Controls.Add(_tabs);

            // ---- Settings tab: connection rows ----
            _txtFolder = MakeText(settings.DriveFolderUrl, "Paste the folder link: https://drive.google.com/drive/folders/…");
            _txtKey = MakeText(settings.DriveApiKey, "Google API key, e.g. AIzaSy…");
            _txtKey.UseSystemPasswordChar = true;
            _txtFolder.TextChanged += (s, e) => { UpdateButtons(); UpdateEmpty(); };
            _txtKey.TextChanged += (s, e) => { UpdateButtons(); UpdateEmpty(); };

            _chkShowKey = new CheckBox { Text = "Show", AutoSize = true, Margin = new Padding(0, 12, 6, 0), BackColor = Surface };
            _chkShowKey.CheckedChanged += (s, e) => _txtKey.UseSystemPasswordChar = !_chkShowKey.Checked;

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
                Dock = DockStyle.Top, Height = 126, ColumnCount = 4, RowCount = 3, BackColor = Surface, Padding = new Padding(0)
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 3; i++) rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            AddRow(rows, 0, "Folder link", _txtFolder, null, _btnOpenDrive);
            rows.SetColumnSpan(_txtFolder, 2);   // the link is long: use the free middle cell too
            AddRow(rows, 1, "API key", _txtKey, _chkShowKey, _btnGetKey);
            AddRow(rows, 2, "Save to", _lblSaveTo, _btnChange, _btnOpenLocal);

            // ---- Settings tab: download option ----
            _chkReplace = new CheckBox
            {
                Text = "Replace photos that changed on Drive", AutoSize = true, Checked = true, BackColor = Surface,
                Margin = new Padding(0, 4, 0, 0)
            };
            _chkReplace.CheckedChanged += (s, e) => { RefreshGrid(); UpdateButtons(); };
            var replaceRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 50, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Surface
            };
            replaceRow.Controls.Add(_chkReplace);
            replaceRow.Controls.Add(new Label
            {
                Text = "Unticked: a photo that differs on Drive keeps the copy in the Photo Folder. " +
                       "Photos edited with Remove Background are always kept.",
                AutoSize = true, ForeColor = TextMuted, Margin = new Padding(18, 2, 0, 0)
            });

            // ---- Settings tab: how-to ----
            var helpBox = new Panel { Dock = DockStyle.Top, BackColor = SurfaceAlt, Padding = new Padding(14, 12, 14, 12) };
            var help = new Label { Dock = DockStyle.Fill, Text = SetupSteps, ForeColor = TextMuted, UseMnemonic = false };
            helpBox.Controls.Add(help);
            helpBox.SizeChanged += (s, e) =>
            {
                // Grow to the wrapped text so nothing is cut off at narrow widths.
                int w = Math.Max(100, helpBox.ClientSize.Width - helpBox.Padding.Horizontal);
                int h = TextRenderer.MeasureText(SetupSteps, help.Font, new Size(w, int.MaxValue), TextFormatFlags.WordBreak).Height;
                int want = h + helpBox.Padding.Vertical + 4;
                if (helpBox.Height != want) helpBox.Height = want;
            };

            var settingsPage = new Panel
            {
                Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(24, 0, 24, 12), AutoScroll = true, Visible = false
            };
            // Top-docked: last added sits highest.
            settingsPage.Controls.Add(helpBox);
            settingsPage.Controls.Add(Section("How to connect"));
            settingsPage.Controls.Add(replaceRow);
            settingsPage.Controls.Add(Section("Download"));
            settingsPage.Controls.Add(rows);
            settingsPage.Controls.Add(Section("Connection"));
            _settingsPage = settingsPage;

            // ---- Photos tab: photo list ----
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
            // Rows alternate within each folder group (set per row in FillGrid), not across the whole list.
            _grid.RowPrePaint += PaintGroupRow;
            _grid.CellMouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && _grid.Rows[e.RowIndex].Tag is FolderGroup) ToggleGroup(e.RowIndex, null);
            };
            _grid.KeyDown += OnGridKeyDown;
            AddColumn("File on Drive", 190);
            AddColumn("Student", 170);
            AddColumn("Size", 55).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            AddColumn("Modified", 85);
            AddColumn("Result", 150);

            // Empty state: icon, message and (when not connected) a shortcut to the settings tab, centred.
            var emptyIcon = new Panel { Size = new Size(56, 56), Anchor = AnchorStyles.None, BackColor = Surface, Margin = new Padding(0, 0, 0, 8) };
            emptyIcon.Paint += (s, e) =>
            {
                Theme.HighQuality(e.Graphics);
                using (var b = new SolidBrush(AccentSoft)) e.Graphics.FillEllipse(b, 0, 0, 55, 55);
                Theme.DrawIcon(e.Graphics, Theme.IcoCloud, 26f, Accent, new RectangleF(0, 0, 56, 56));
            };
            _lblEmpty = new Label
            {
                AutoSize = true, Anchor = AnchorStyles.None, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted,
                BackColor = Surface, UseMnemonic = false, Margin = new Padding(0, 0, 0, 14)
            };
            _btnGoSettings = ModalUi.Button("Open Drive Settings", PillKind.Secondary, (s, e) => ShowTab(SettingsTab));
            _btnGoSettings.Anchor = AnchorStyles.None;
            _btnGoSettings.Margin = new Padding(0);
            var empty = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Surface };
            empty.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            empty.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            empty.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            empty.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            empty.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            empty.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            empty.Controls.Add(emptyIcon, 0, 1);
            empty.Controls.Add(_lblEmpty, 0, 2);
            empty.Controls.Add(_btnGoSettings, 0, 3);
            _emptyHost = empty;

            var photosPage = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
            photosPage.Controls.Add(_grid);
            photosPage.Controls.Add(_emptyHost);
            _photosPage = photosPage;

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
            footer.Controls.Add(_btnClose);
            footer.Controls.Add(_btnDownload);

            // Docking runs in reverse add order: the fill controls first, the edges last.
            Controls.Add(_photosPage);
            Controls.Add(_settingsPage);
            Controls.Add(tabBar);
            Controls.Add(_lblStatus);
            Controls.Add(footer);
            CancelButton = _btnClose;

            var tip = new ToolTip();
            tip.SetToolTip(_tabs, "Ctrl+Tab switches between the photo list and the Drive settings");
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
            UpdateEmpty();
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Not set up yet: start on the settings tab, at the first missing field.
            if (_txtFolder.TextLength == 0) { ShowTab(SettingsTab); _txtFolder.Focus(); return; }
            if (_txtKey.TextLength == 0) { ShowTab(SettingsTab); _txtKey.Focus(); return; }
            _btnScan.Focus();
            await ScanAsync();   // saved folder: show what is new right away
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5) { BeginScan(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && (_txtFolder.Focused || _txtKey.Focused)) { BeginScan(); e.Handled = e.SuppressKeyPress = true; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab))
            {
                ShowTab(_tabs.SelectedIndex == PhotosTab ? SettingsTab : PhotosTab);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ===================== Tabs =====================
        private void ShowTab(int index)
        {
            if (_tabs.SelectedIndex != index) _tabs.SelectedIndex = index;   // raises OnTabChanged
            else OnTabChanged();
        }

        private void OnTabChanged()
        {
            bool photos = _tabs.SelectedIndex == PhotosTab;
            _photosPage.Visible = photos;
            _settingsPage.Visible = !photos;
            if (photos) return;
            if (_txtFolder.TextLength == 0) _txtFolder.Focus();
            else if (_txtKey.TextLength == 0) _txtKey.Focus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _groupFont.Dispose();
            base.Dispose(disposing);
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
                ShowTab(SettingsTab);
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
                ShowTab(PhotosTab);   // connected: show the result
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
        /// <summary>
        /// One header row per Drive folder (the shared folder first, then sub-folders A–Z), its photos below in
        /// status order. Folders the user collapsed stay collapsed after a rescan.
        /// </summary>
        private void FillGrid()
        {
            _grid.Rows.Clear();
            IEnumerable<IGrouping<string, PhotoSyncItem>> groups = _items
                .GroupBy(i => i.File.Folder ?? "", StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key.Length == 0 ? 0 : 1)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, PhotoSyncItem> g in groups)
            {
                var group = new FolderGroup { Path = g.Key, Items = g.ToList() };
                DataGridViewRow header = _grid.Rows[_grid.Rows.Add()];
                header.Tag = group;
                header.Height = GroupRowHeight;
                bool collapsed = _collapsed.Contains(group.Path);

                int n = 0;
                foreach (PhotoSyncItem item in group.Items)
                {
                    DriveFile f = item.File;
                    DataGridViewRow row = _grid.Rows[_grid.Rows.Add(
                        f.Name,
                        item.StudentId == null ? "—" : item.StudentId + (string.IsNullOrEmpty(item.StudentName) ? "" : "  " + item.StudentName),
                        f.Size > 0 ? Math.Max(1, (f.Size + 512) / 1024) + " KB" : "",
                        f.Modified.HasValue ? f.Modified.Value.ToString("yyyy-MM-dd") : "",
                        "")];
                    row.Tag = item;
                    row.DefaultCellStyle.BackColor = n++ % 2 == 1 ? SurfaceAlt : Surface;
                    row.Cells[0].ToolTipText = f.Folder.Length == 0 ? f.Name : f.Folder + "/" + f.Name;
                    row.Visible = !collapsed;
                }
            }
            RefreshGrid();
            UpdateEmpty();
        }

        /// <summary>Collapses or expands the folder whose header is at <paramref name="headerIndex"/> (null = toggle).</summary>
        private void ToggleGroup(int headerIndex, bool? collapse)
        {
            DataGridViewRow header = _grid.Rows[headerIndex];
            var group = header.Tag as FolderGroup;
            if (group == null) return;
            bool hide = collapse ?? !_collapsed.Contains(group.Path);
            if (hide == _collapsed.Contains(group.Path)) return;
            if (hide) _collapsed.Add(group.Path); else _collapsed.Remove(group.Path);

            // Park the current cell on the header first: the grid cannot hide the row it is on.
            _grid.CurrentCell = header.Cells[0];
            for (int i = headerIndex + 1; i < _grid.Rows.Count && !(_grid.Rows[i].Tag is FolderGroup); i++)
                _grid.Rows[i].Visible = !hide;
            _grid.InvalidateRow(headerIndex);
        }

        /// <summary>On a folder header: Enter / Space toggle, Left collapses, Right expands.</summary>
        private void OnGridKeyDown(object sender, KeyEventArgs e)
        {
            DataGridViewRow row = _grid.CurrentRow;
            if (row == null || !(row.Tag is FolderGroup) || e.Modifiers != Keys.None) return;
            bool? collapse;
            switch (e.KeyCode)
            {
                case Keys.Enter:
                case Keys.Space: collapse = null; break;
                case Keys.Left: collapse = true; break;
                case Keys.Right: collapse = false; break;
                default: return;
            }
            ToggleGroup(row.Index, collapse);
            e.Handled = e.SuppressKeyPress = true;
        }

        /// <summary>Folder header: chevron, folder icon, folder path, photo counts, and what Download will do on the right.</summary>
        private void PaintGroupRow(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            var group = _grid.Rows[e.RowIndex].Tag as FolderGroup;
            if (group == null) return;
            e.Handled = true;

            Graphics g = e.Graphics;
            Rectangle r = e.RowBounds;
            int top = e.RowIndex == 0 ? 0 : GroupGap;   // breathing room between groups
            using (var b = new SolidBrush(Surface)) g.FillRectangle(b, r.X, r.Y, r.Width, top);
            Rectangle band = new Rectangle(r.X, r.Y + top, r.Width, r.Height - top);
            bool selected = _grid.Rows[e.RowIndex].Selected;
            using (var b = new SolidBrush(selected ? AccentSoft : Theme.Tile)) g.FillRectangle(b, band);
            using (var pen = new Pen(GridLine)) g.DrawLine(pen, band.Left, band.Bottom - 1, band.Right, band.Bottom - 1);

            bool collapsed = _collapsed.Contains(group.Path);
            Theme.DrawIcon(g, collapsed ? ChevronRight : ChevronDown, 10f, TextMuted, new RectangleF(band.X + 8, band.Y, 20, band.Height));
            Theme.DrawIcon(g, Theme.IcoFolder, 16f, Accent, new RectangleF(band.X + 30, band.Y, 22, band.Height));

            // Counts that describe the folder, then what Download will do with it.
            Func<PhotoSyncStatus, int> count = st => group.Items.Count(i => i.Status == st);
            int toDownload = group.Items.Count(i => i.WillDownload(_chkReplace.Checked));
            int problems = count(PhotoSyncStatus.NotMatched) + count(PhotoSyncStatus.Unsupported) + count(PhotoSyncStatus.Failed);
            var parts = new List<string> { group.Items.Count + (group.Items.Count == 1 ? " photo" : " photos") };
            Action<int, string> part = (n, text) => { if (n > 0) parts.Add(n + " " + text); };
            part(count(PhotoSyncStatus.New), "new");
            part(count(PhotoSyncStatus.Changed), "changed");
            part(count(PhotoSyncStatus.Downloaded), "downloaded");
            part(count(PhotoSyncStatus.NotMatched), "not matched");
            part(count(PhotoSyncStatus.Unsupported), "not JPG/PNG");
            part(count(PhotoSyncStatus.Failed), "failed");

            string state;
            Color stateColor;
            if (toDownload > 0) { state = toDownload + " to download"; stateColor = Accent; }
            else if (problems > 0) { state = problems + " need attention"; stateColor = Warning; }
            else { state = "All up to date"; stateColor = Success; }

            const TextFormatFlags Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                                          TextFormatFlags.EndEllipsis;
            int stateWidth = TextRenderer.MeasureText(g, state, _groupFont, band.Size, Flags).Width;
            var stateRect = new Rectangle(band.Right - stateWidth - 16, band.Y, stateWidth + 4, band.Height);
            TextRenderer.DrawText(g, state, _groupFont, stateRect, stateColor, Flags);

            string name = group.Path.Length == 0 ? "Shared folder" : group.Path.Replace("/", "  ›  ");
            int x = band.X + 56, maxRight = stateRect.Left - 16;
            int nameWidth = Math.Min(TextRenderer.MeasureText(g, name, _groupFont, band.Size, Flags).Width, Math.Max(0, maxRight - x));
            TextRenderer.DrawText(g, name, _groupFont, new Rectangle(x, band.Y, nameWidth, band.Height), TextMain, Flags);
            x += nameWidth + 10;
            if (x < maxRight)
                TextRenderer.DrawText(g, string.Join("  ·  ", parts), _grid.DefaultCellStyle.Font ?? Font,
                    new Rectangle(x, band.Y, maxRight - x, band.Height), TextMuted, Flags);
        }

        /// <summary>Grid when there is a scan result with photos; otherwise the empty state that says what to do next.</summary>
        private void UpdateEmpty()
        {
            bool hasRows = _items != null && _items.Count > 0;
            _grid.Visible = hasRows;
            _emptyHost.Visible = !hasRows;
            if (hasRows) return;

            bool connected = _txtFolder.TextLength > 0 && _txtKey.TextLength > 0;
            if (_items != null)
                _lblEmpty.Text = "The Google Drive folder has no pictures.\nCheck the link, or upload the photos to it.";
            else if (!connected)
                _lblEmpty.Text = "Google Drive is not connected yet.\nAdd the shared folder link and an API key in Drive Settings.";
            else
                _lblEmpty.Text = "Not scanned yet.\nClick Scan Folder (F5) to list the student photos on Drive.";
            _btnGoSettings.Visible = _items != null || !connected;
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
            _grid.Invalidate();   // folder headers recount their photos as statuses change
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
            int folders = _items.Select(i => i.File.Folder ?? "").Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var parts = new List<string> { _items.Count + " picture(s) on Drive" + (folders > 1 ? " in " + folders + " folders" : "") };
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
            _lblSource.Text = set ? "Saving to  " + _settings.PhotoFolder : "No Photo Folder yet — set it in Drive Settings";
            _lblSource.ForeColor = set ? TextMuted : Warning;
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
            _settingsPage.Enabled = !busy;   // tabs stay usable: watch the list while it downloads
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
            if (a != null) rows.Controls.Add(a, 2, row);
            if (b != null) rows.Controls.Add(b, 3, row);
        }

        /// <summary>Small-caps section heading on the settings tab.</summary>
        private static Label Section(string text)
        {
            return new Label
            {
                Text = text.ToUpperInvariant(), Font = Theme.SmallCaps, ForeColor = TextMuted, BackColor = Surface,
                Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(0, 0, 0, 6),
                UseMnemonic = false
            };
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
