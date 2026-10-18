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
    /// "Google Drive Photos" / "OneDrive Photos" page: copies student photos from a cloud folder into the Photo Folder.
    ///   Scan Folder (F5) = list the photos and compare them with the Photo Folder (nothing is written).
    ///   Download = save the new (and, when ticked, changed) photos as {StudentId}.jpg / .png.
    /// Everything that depends on where the photos come from is in the <see cref="PhotoSource"/>.
    /// </summary>
    public class PhotoSyncForm : ModalForm
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

        private const int PhotosTab = 0, SettingsTab = 1;

        private readonly AppSettings _settings;
        private readonly IList<Student> _students;
        private readonly PhotoSource _source;

        private readonly PillTabs _tabs;
        private readonly Control _photosPage, _settingsPage;
        private readonly CheckBox _chkReplace;
        private readonly Label _lblSaveTo, _lblSource, _lblEmpty, _lblStatus;
        private readonly DataGridView _grid;
        private readonly Control _emptyHost;
        private readonly Button _btnScan, _btnChange, _btnOpenLocal, _btnDownload, _btnClose, _btnGoSettings;

        private CancellationTokenSource _cts;
        private bool _closeWhenDone;
        private List<DriveFile> _files;
        private List<PhotoSyncItem> _items;

        // Photo list grouped by folder: one header row per folder, its photos below.
        private const string ChevronDown = "", ChevronRight = "";   // Segoe Fluent Icons / MDL2 Assets
        private const int GroupRowHeight = 40, GroupGap = 6;
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // kept across scans
        private readonly Font _groupFont = new Font(CardFonts.UI, 9f, FontStyle.Bold);

        /// <summary>Tag of a folder header row.</summary>
        private sealed class FolderGroup
        {
            public string Path;                  // "" = the chosen folder itself, else "Sub/Sub"
            public List<PhotoSyncItem> Items;
        }

        /// <summary>Photos written to the Photo Folder while the page was open.</summary>
        public int Downloaded { get; private set; }

        /// <summary>Where the photos come from.</summary>
        public PhotoSource Source { get { return _source; } }

        /// <param name="students">The loaded student list, used to match file names to students (may be empty).</param>
        public PhotoSyncForm(AppSettings settings, IList<Student> students, PhotoSource source)
        {
            _settings = settings;
            _students = students ?? new List<Student>();
            _source = source;
            _source.ErrorSink = message => ShowStatus(OneLine(message), Danger);

            Text = source.Name + " Photos";
            Subtitle = source.Subtitle;
            Glyph = source.Glyph;
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
            _tabs = new PillTabs("Student Photos", SettingsTabName) { Dock = DockStyle.Left, Margin = new Padding(0) };
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

            // ---- Settings tab: connection rows from the source, then where the photos are saved ----
            _source.Changed += (s, e) => { UpdateButtons(); UpdateEmpty(); };
            _btnChange = PhotoSyncUi.RowButton("Change…", PillKind.Secondary, (s, e) => ChooseSaveFolder());
            _btnOpenLocal = PhotoSyncUi.RowButton("Open Folder", PillKind.Secondary, (s, e) => OpenLocalFolder());

            _lblSaveTo = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
                Margin = new Padding(0, 4, 10, 0)
            };

            var sourceRows = new List<SourceRow>(_source.CreateRows());
            sourceRows.Add(new SourceRow { Caption = "Save to", Field = _lblSaveTo, A = _btnChange, B = _btnOpenLocal });
            var rows = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 42 * sourceRows.Count, ColumnCount = 4, RowCount = sourceRows.Count,
                BackColor = Surface, Padding = new Padding(0)
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < sourceRows.Count; i++)
            {
                rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                AddRow(rows, i, sourceRows[i]);
            }

            // ---- Settings tab: download option ----
            _chkReplace = new CheckBox
            {
                Text = "Replace photos that changed on " + source.ShortName, AutoSize = true, Checked = true, BackColor = Surface,
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
                Text = "Unticked: a photo that differs on " + source.ShortName + " keeps the copy in the Photo Folder. " +
                       "Photos edited with Remove Background are always kept.",
                AutoSize = true, ForeColor = TextMuted, Margin = new Padding(18, 2, 0, 0)
            });

            // ---- Settings tab: how-to ----
            string setupSteps = Lang.T(source.SetupSteps);   // translated before its height is measured
            var helpBox = new Panel { Dock = DockStyle.Top, BackColor = SurfaceAlt, Padding = new Padding(14, 12, 14, 12) };
            var help = new Label { Dock = DockStyle.Fill, Text = setupSteps, ForeColor = TextMuted, UseMnemonic = false };
            helpBox.Controls.Add(help);
            helpBox.SizeChanged += (s, e) =>
            {
                // Grow to the wrapped text so nothing is cut off at narrow widths.
                int w = Math.Max(100, helpBox.ClientSize.Width - helpBox.Padding.Horizontal);
                int h = Lang.WrappedHeight(help, setupSteps, help.Font, w);
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
            AddColumn("File on " + source.ShortName, 190);
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
            _btnGoSettings = ModalUi.Button("Open " + SettingsTabName, PillKind.Secondary, (s, e) => ShowTab(SettingsTab));
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
            tip.SetTip(_tabs, "Ctrl+Tab switches between the photo list and the " + source.ShortName + " settings");
            tip.SetTip(_btnScan, "List the photos in the " + source.Name + " folder and compare them with the Photo Folder. " +
                                     "Nothing is written yet.");
            tip.SetTip(_btnChange, "Choose the Photo Folder the cards read photos from");
            tip.SetTip(_chkReplace, "Ticked: a photo that differs from the one on " + source.ShortName + " is replaced by the " +
                                        source.ShortName + " copy.\nPhotos edited with Remove Background are always kept.");
            _source.AddTips(tip);

            ShowSaveTo();
            ShowStatus(_students.Count == 0
                ? "No student list loaded: file names are taken as the StudentId. Import the students first to match names like “NPIC24001 - Sok Dara.jpg”."
                : "Not connected.", _students.Count == 0 ? Warning : TextMuted);
            UpdateButtons();
            UpdateEmpty();
        }

        private string SettingsTabName { get { return _source.ShortName + " Settings"; } }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Not set up yet: start on the settings tab, at the first missing field.
            Control missing = _source.FirstMissingField();
            if (missing != null) { ShowTab(SettingsTab); missing.Focus(); return; }
            _btnScan.Focus();
            await ScanAsync();   // saved folder: show what is new right away
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5) { BeginScan(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && _settingsPage.Visible && ActiveControl is TextBox)
            {
                BeginScan();
                e.Handled = e.SuppressKeyPress = true;
            }
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
            Control missing = _source.FirstMissingField();
            if (missing != null) missing.Focus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _groupFont.Dispose(); _source.Dispose(); }
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

            Control field;
            string problem = _source.Validate(_settings.PhotoFolder, out field);   // check before going online
            if (problem != null)
            {
                ShowStatus(OneLine(problem), Danger);
                ShowTab(SettingsTab);
                if (field != null) field.Focus();
                return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true, "Reading the " + _source.Name + " folder…  (Stop cancels)");
            try
            {
                List<DriveFile> files = await _source.ListAsync(_settings.PhotoFolder, _cts.Token);
                _source.Save(_settings);
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

        /// <summary>Compares the listed files with the Photo Folder (local only) and shows the result.</summary>
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
                await _source.FetchAsync(todo, _settings.PhotoFolder, progress, _cts.Token);
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
            else ShowStatus(Lang.F("{0}{1} photo(s) downloaded to {2}{3}",
                    cancelled ? Lang.T("Stopped — ") : "✔  ", ok, _settings.PhotoFolder,
                    failed > 0 ? Lang.F("  ·  {0} failed (see Result)", failed) : ""),
                failed > 0 || cancelled ? Warning : Success);

            if (_closeWhenDone) DialogResult = DialogResult.Cancel;
        }

        // ===================== List =====================
        /// <summary>
        /// One header row per folder (the chosen folder first, then sub-folders A–Z), its photos below in
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
            var parts = new List<string> { Lang.T(group.Items.Count + (group.Items.Count == 1 ? " photo" : " photos")) };
            Action<int, string> part = (n, text) => { if (n > 0) parts.Add(Lang.T(n + " " + text)); };
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
            state = Lang.T(state);

            const TextFormatFlags Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                                          TextFormatFlags.EndEllipsis;
            int stateWidth = TextRenderer.MeasureText(g, state, _groupFont, band.Size, Flags).Width;
            var stateRect = new Rectangle(band.Right - stateWidth - 16, band.Y, stateWidth + 4, band.Height);
            TextRenderer.DrawText(g, state, _groupFont, stateRect, stateColor, Flags);

            string name = group.Path.Length == 0 ? _source.RootFolderName : group.Path.Replace("/", "  ›  ");
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

            bool connected = _source.FirstMissingField() == null;
            if (_items != null)
                _lblEmpty.Text = _source.NoPicturesText;
            else if (!connected)
                _lblEmpty.Text = _source.NotConnectedText;
            else
                _lblEmpty.Text = "Not scanned yet.\nClick Scan Folder (F5) to list the student photos on " + _source.ShortName + ".";
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
                cell.Value = Lang.T(Describe(item, _chkReplace.Checked, out color));
                cell.Style.ForeColor = cell.Style.SelectionForeColor = color;
                cell.ToolTipText = item.Error ?? "";
            }
            _grid.Invalidate();   // folder headers recount their photos as statuses change
        }

        private string Describe(PhotoSyncItem item, bool replaceChanged, out Color color)
        {
            switch (item.Status)
            {
                case PhotoSyncStatus.New: color = Accent; return "New — will download";
                case PhotoSyncStatus.Changed:
                    color = Warning;
                    return replaceChanged ? "Changed — will replace" : "Changed on " + _source.ShortName + " — kept";
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
            var parts = new List<string>
            {
                Lang.F("{0} picture(s) on {1}", _items.Count, _source.ShortName) + (folders > 1 ? Lang.F(" in {0} folders", folders) : "")
            };
            Action<int, string> part = (n, text) => { if (n > 0) parts.Add(Lang.T(n + " " + text)); };
            part(count(PhotoSyncStatus.New), "new");
            part(count(PhotoSyncStatus.Changed), "changed");
            part(count(PhotoSyncStatus.UpToDate), "up to date");
            part(count(PhotoSyncStatus.KeptEdited), "edited here");
            part(count(PhotoSyncStatus.NotMatched), "not matched");
            part(count(PhotoSyncStatus.Unsupported), "not JPG/PNG");
            part(count(PhotoSyncStatus.Duplicate), "older copies");

            bool problems = count(PhotoSyncStatus.NotMatched) + count(PhotoSyncStatus.Unsupported) > 0;
            ShowStatus(Lang.F("Scanned {0:HH:mm} — {1}", DateTime.Now, string.Join("  ·  ", parts)), problems ? Warning : Success);
        }

        // ===================== Folders =====================
        /// <summary>Asks for the Photo Folder; true when one was chosen.</summary>
        /// <param name="replan">Compare the listed photos with the new folder right away.</param>
        private bool ChooseSaveFolder(bool replan = true)
        {
            using (var dlg = new FolderBrowserDialog
            {
                Description = Lang.F("Photo Folder — the {0} photos are saved here as {{StudentId}}.jpg / .png", _source.ShortName),
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
            _lblSource.Text = set ? "Saving to  " + _settings.PhotoFolder : "No Photo Folder yet — set it in " + SettingsTabName;
            _lblSource.ForeColor = set ? TextMuted : Warning;
            _btnOpenLocal.Enabled = set && Directory.Exists(_settings.PhotoFolder);
        }

        private void OpenLocalFolder()
        {
            PhotoSyncUi.OpenInExplorer(_settings.PhotoFolder, message => ShowStatus(message, Danger));
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
            _btnScan.Enabled = !busy && _source.FirstMissingField() == null;
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

        private static void AddRow(TableLayoutPanel rows, int row, SourceRow r)
        {
            rows.Controls.Add(new Label
            {
                Text = r.Caption, AutoSize = true, ForeColor = TextMuted, Margin = new Padding(0, 12, 6, 0)
            }, 0, row);
            rows.Controls.Add(r.Field, 1, row);
            if (r.A != null) rows.Controls.Add(r.A, 2, row);
            else if (r.SpanField) rows.SetColumnSpan(r.Field, 2);   // e.g. a long link: use the free middle cell too
            if (r.B != null) rows.Controls.Add(r.B, 3, row);
        }

        /// <summary>Small-caps section heading on the settings tab.</summary>
        private static Label Section(string text)
        {
            return new Label
            {
                Text = Lang.T(text).ToUpperInvariant(), Font = Theme.SmallCaps, ForeColor = TextMuted, BackColor = Surface,
                Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(0, 0, 0, 6),
                UseMnemonic = false
            };
        }
    }

    /// <summary>One connection row on the settings tab: caption | field | button | button.</summary>
    public sealed class SourceRow
    {
        public string Caption;
        public Control Field;
        /// <summary>Optional controls right of the field.</summary>
        public Control A, B;
        /// <summary>With no <see cref="A"/>: the field also takes A's column (for long links).</summary>
        public bool SpanField;
    }

    /// <summary>
    /// Where a <see cref="PhotoSyncForm"/> gets its photos: the connection fields on the settings tab, the wording,
    /// and listing / fetching the files. The form does the rest (matching, the photo list, saving, progress).
    /// </summary>
    public abstract class PhotoSource
    {
        /// <summary>"Google Drive" — in sentences such as "Reading the Google Drive folder…".</summary>
        public abstract string Name { get; }
        /// <summary>"Drive" — in short labels such as "File on Drive" and "Drive Settings".</summary>
        public abstract string ShortName { get; }
        public abstract string Subtitle { get; }
        public abstract string Glyph { get; }
        public abstract string SetupSteps { get; }
        /// <summary>Folder header for the photos directly in the chosen folder.</summary>
        public abstract string RootFolderName { get; }
        /// <summary>Empty-state text while the connection fields are not filled in.</summary>
        public abstract string NotConnectedText { get; }
        /// <summary>Empty-state text when the scanned folder has no pictures.</summary>
        public abstract string NoPicturesText { get; }

        /// <summary>The connection rows, top to bottom; the form adds "Save to" below them. Called once.</summary>
        public abstract IList<SourceRow> CreateRows();
        /// <summary>The first connection field that is still empty, or null when the source is ready to scan.</summary>
        public abstract Control FirstMissingField();
        /// <summary>Checks the fields before scanning: null when fine, else the problem and the field to fix.</summary>
        public abstract string Validate(string photoFolder, out Control field);
        /// <summary>Lists every picture in the folder and its sub-folders.</summary>
        public abstract Task<List<DriveFile>> ListAsync(string photoFolder, CancellationToken cancel);
        /// <summary>Remembers the fields in the settings (after a successful scan).</summary>
        public abstract void Save(AppSettings settings);
        /// <summary>Saves the planned photos in the Photo Folder, reporting the number finished.</summary>
        public abstract Task FetchAsync(IList<PhotoSyncItem> items, string photoFolder, IProgress<int> progress, CancellationToken cancel);

        public virtual void AddTips(ToolTip tip) { }

        /// <summary>Called when the page closes: stop anything still running (e.g. waiting for a sign-in).</summary>
        public virtual void Dispose() { }

        /// <summary>Raised when a connection field changes.</summary>
        public event EventHandler Changed;

        protected void OnChanged()
        {
            EventHandler handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>Set by the form: shows a message in its status line.</summary>
        internal Action<string> ErrorSink;

        protected void ShowError(string message)
        {
            if (ErrorSink != null) ErrorSink(message);
        }
    }

    /// <summary>Controls and shell helpers shared by the photo sources.</summary>
    internal static class PhotoSyncUi
    {
        public static Button RowButton(string text, PillKind kind, EventHandler onClick)
        {
            Button b = ModalUi.Button(text, kind, onClick);
            b.Margin = new Padding(0, 4, 6, 0);
            return b;
        }

        /// <summary>Single-line text box with a grey hint shown while it is empty.</summary>
        public static TextBox MakeText(string text, string cue)
        {
            var box = new TextBox { Dock = DockStyle.Fill, Text = text ?? "", Margin = new Padding(0, 9, 10, 0) };
            box.HandleCreated += (s, e) => SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, Lang.T(cue));
            return box;
        }

        public static void OpenUrl(string url, Action<string> onError)
        {
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception ex) { onError("Could not open the browser: " + ex.Message); }
        }

        public static void OpenInExplorer(string folder, Action<string> onError)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\""); }
            catch (Exception ex) { onError("Could not open the folder: " + ex.Message); }
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }
}
