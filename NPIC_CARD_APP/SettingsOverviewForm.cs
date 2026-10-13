using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using NPIC_CARD_APP.Printing;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Read-only overview of every setting of the app and of this PC (printer, tray layout, files, web API,
    /// card text, fonts, assets). Rows that need attention are shown in orange.
    /// Double-click a file / folder row to open it in Explorer.   Ctrl+F = search, Esc = close
    /// The API token is never shown, only whether one is saved.
    /// Export… / Import… save the settings to a file and load them back (backup, or set up another PC);
    /// after an import <see cref="SettingsImported"/> tells the main window to reload its controls.
    /// </summary>
    public class SettingsOverviewForm : Form
    {
        private static readonly Color Accent     = Color.FromArgb(46, 49, 146);
        private static readonly Color AccentSoft = Color.FromArgb(232, 233, 248);
        private static readonly Color Surface    = Color.White;
        private static readonly Color SurfaceAlt = Color.FromArgb(249, 250, 252);
        private static readonly Color GridLine   = Color.FromArgb(236, 237, 242);
        private static readonly Color TextMain   = Color.FromArgb(28, 30, 40);
        private static readonly Color TextMuted  = Color.FromArgb(102, 106, 122);
        private static readonly Color Warning    = Color.FromArgb(180, 98, 0);

        private const string NotSet = "(not set)";

        /// <summary>One line of the overview.</summary>
        private sealed class Entry
        {
            public string Section, Name, Value, Note;
            public bool Warn;
            public string OpenPath;   // file or folder opened on double-click; null = nothing to open
        }

        private readonly AppSettings _settings;
        private List<Entry> _all;
        private readonly TextBox _txtSearch;
        private readonly DataGridView _grid;
        private readonly Label _lblSummary, _lblEmpty;

        /// <summary>True after Import… replaced the settings: the main window must reload its controls.</summary>
        public bool SettingsImported { get; private set; }

        public SettingsOverviewForm(AppSettings settings)
        {
            _settings = settings;
            Text = "All Settings";
            StartPosition = FormStartPosition.CenterParent;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(980, work.Width), Math.Min(720, work.Height));
            MinimumSize = new Size(Math.Min(640, work.Width), Math.Min(400, work.Height));
            Font = new Font(CardFonts.UI, 9f);
            BackColor = Surface;
            KeyPreview = true;
            ShowInTaskbar = false;
            MinimizeBox = false;

            _all = Collect(settings, DeviceDpi);

            // ---- Toolbar ----
            _txtSearch = new TextBox { Width = 260, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 9, 12, 3) };
            _txtSearch.HandleCreated += (s, e) => SetCueBanner(_txtSearch, "Search settings… (Ctrl+F)");
            _txtSearch.TextChanged += (s, e) => ShowEntries();

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 2, 8, 0), WrapContents = false, BackColor = SurfaceAlt
            };
            toolbar.Controls.Add(_txtSearch);
            Button btnExport = MakeButton("Export…", (s, e) => ExportSettings());
            Button btnImport = MakeButton("Import…", (s, e) => ImportSettings());
            toolbar.Controls.Add(btnExport);
            toolbar.Controls.Add(btnImport);
            toolbar.Controls.Add(MakeButton("Copy All", (s, e) => CopyAll()));
            toolbar.Controls.Add(MakeButton("Open Settings Folder", (s, e) => OpenInExplorer(AppSettings.FilePath)));
            toolbar.Controls.Add(MakeButton("Close", (s, e) => Close()));

            var tip = new ToolTip();
            tip.SetToolTip(btnExport, "Save all settings to a file, to back them up or copy them to another PC.\n" +
                                      "The API token is not included.");
            tip.SetToolTip(btnImport, "Replace all settings with the ones in an exported file.\n" +
                                      "The current settings are backed up first.");

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
                ColumnHeadersHeight = 32, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
                ShowCellToolTips = true
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
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Section", HeaderText = "Section", Width = 150 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Setting", HeaderText = "Setting", Width = 190 });
            var valueColumn = new DataGridViewTextBoxColumn
            {
                Name = "Value", HeaderText = "Value", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            valueColumn.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.Columns.Add(valueColumn);
            _grid.Columns["Section"].DefaultCellStyle.Font = new Font(CardFonts.UI, 9f, FontStyle.Bold);
            _grid.Columns["Section"].DefaultCellStyle.ForeColor = Accent;
            _grid.Columns["Setting"].DefaultCellStyle.ForeColor = TextMuted;
            foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var entry = _grid.Rows[e.RowIndex].Tag as Entry;
                if (entry != null && entry.OpenPath != null) OpenInExplorer(entry.OpenPath);
            };

            _lblEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted, BackColor = Surface,
                Text = "No setting matches the search.", Visible = false
            };
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(_lblEmpty);
            _lblEmpty.BringToFront();

            _lblSummary = new Label
            {
                Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(10, 4, 10, 4),
                BackColor = Color.FromArgb(240, 240, 240), ForeColor = TextMuted
            };

            Controls.Add(gridHost);
            Controls.Add(toolbar);
            Controls.Add(_lblSummary);

            ShowEntries();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
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
            else if (e.KeyData == (Keys.Control | Keys.F)) { _txtSearch.Focus(); _txtSearch.SelectAll(); e.Handled = true; }
        }

        // ===================== View =====================
        private void ShowEntries()
        {
            string q = _txtSearch.Text.Trim();
            List<Entry> shown = q.Length == 0 ? _all : _all.Where(x => Has(x.Section, q) || Has(x.Name, q) || Has(x.Value, q)).ToList();

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            string lastSection = null;
            foreach (Entry x in shown)
            {
                // Name the section on its first row only, so the list reads as groups.
                int i = _grid.Rows.Add(x.Section == lastSection ? "" : x.Section, x.Name, x.Value);
                lastSection = x.Section;
                DataGridViewRow row = _grid.Rows[i];
                row.Tag = x;

                string tip = x.Note ?? "";
                if (x.OpenPath != null) tip += (tip.Length > 0 ? "\n" : "") + "Double-click to open in Explorer.";
                row.Cells[1].ToolTipText = row.Cells[2].ToolTipText = tip;

                if (x.Warn)
                {
                    row.Cells[2].Style.ForeColor = Warning;
                    row.Cells[2].Style.SelectionForeColor = Warning;
                }
            }
            _grid.ResumeLayout();
            _grid.ClearSelection();

            _lblEmpty.Visible = shown.Count == 0;
            int warnings = _all.Count(x => x.Warn);
            _lblSummary.Text =
                _all.Count + " settings" + (q.Length > 0 ? "  ·  " + shown.Count + " shown" : "") +
                (warnings > 0 ? "  ·  " + warnings + " need attention (orange)" : "  ·  nothing needs attention") +
                Environment.NewLine +
                "Read-only. Change settings in the main window.  Settings file: " + AppSettings.FilePath;
            _lblSummary.ForeColor = warnings > 0 ? Warning : TextMuted;
        }

        private void CopyAll()
        {
            var text = new StringBuilder();
            string lastSection = null;
            foreach (Entry x in _all)
            {
                if (x.Section != lastSection)
                {
                    if (lastSection != null) text.AppendLine();
                    text.AppendLine("[" + x.Section + "]");
                    lastSection = x.Section;
                }
                text.Append(x.Name).Append(": ").Append(x.Value);
                if (x.Warn && !string.IsNullOrEmpty(x.Note)) text.Append("   (!) ").Append(x.Note);
                text.AppendLine();
            }
            try
            {
                Clipboard.SetText(text.ToString());
                _lblSummary.Text = "Copied " + _all.Count + " settings to the clipboard.";
                _lblSummary.ForeColor = TextMuted;
            }
            catch (ExternalException ex)
            {
                MessageBox.Show(this, "Could not copy to the clipboard:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExportSettings()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Export Settings",
                Filter = "Settings file (*.xml)|*.xml",
                FileName = "NPIC_CARD_APP-settings-" + DateTime.Now.ToString("yyyy-MM-dd") + ".xml",
                OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _settings.ExportTo(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not export the settings:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                MessageBox.Show(this, "Settings exported to:\n" + dlg.FileName +
                    "\n\nThe API token is not in the file; enter it again with Import API… on another PC.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ImportSettings()
        {
            string path;
            using (var dlg = new OpenFileDialog
            {
                Title = "Import Settings",
                Filter = "Settings file (*.xml)|*.xml|All files (*.*)|*.*",
                CheckFileExists = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }

            AppSettings imported;
            try
            {
                imported = AppSettings.ReadFile(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not read the settings file:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (MessageBox.Show(this,
                    "Replace ALL current settings with the ones in this file?\n" + path + "\n\n" +
                    "Printer, tray positions, folders, card text and API address will change.\n" +
                    "Your current settings are backed up to:\n" + AppSettings.BackupFilePath + "\n\n" +
                    "Folders and files that do not exist on this PC will be shown in orange.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            try
            {
                if (File.Exists(AppSettings.FilePath)) File.Copy(AppSettings.FilePath, AppSettings.BackupFilePath, true);

                // An exported file has no API token: keep the one saved on this PC.
                string token = _settings.ApiTokenProtected;
                _settings.CopyFrom(imported);
                if (string.IsNullOrEmpty(_settings.ApiTokenProtected)) _settings.ApiTokenProtected = token;
                _settings.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not import the settings:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SettingsImported = true;
            _all = Collect(_settings, DeviceDpi);
            ShowEntries();
            string counts = _lblSummary.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None)[0];
            _lblSummary.Text = counts + Environment.NewLine + "Imported from " + path +
                "   (previous settings: " + AppSettings.BackupFilePath + ")";
        }

        private void OpenInExplorer(string path)
        {
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
                else
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
                    else MessageBox.Show(this, "Not found:\n" + path, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open the folder:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ===================== Data =====================
        private static List<Entry> Collect(AppSettings s, int dpi)
        {
            var list = new List<Entry>();
            Action<string, string, string, bool, string, string> add = (section, name, value, warn, note, open) =>
                list.Add(new Entry { Section = section, Name = name, Value = value, Warn = warn, Note = note, OpenPath = open });

            // ---- Printer ----
            const string printer = "Printer";
            bool hasPrinter = !string.IsNullOrEmpty(s.PrinterName);
            bool installed = hasPrinter && PrinterSettings.InstalledPrinters.Cast<string>()
                .Any(p => string.Equals(p, s.PrinterName, StringComparison.OrdinalIgnoreCase));
            add(printer, "Card printer", hasPrinter ? s.PrinterName : "(none selected)",
                !hasPrinter || !installed,
                !hasPrinter ? "Pick the card printer in the Printer box." :
                !installed ? "This printer is not installed on this PC any more." : null, null);
            add(printer, "Card printer keywords", Or(s.CardPrinterKeywords, NotSet), false,
                "A printer whose name contains one of these words (separated by ;) is listed as a card printer.", null);
            add(printer, "Printer list", s.ShowAllPrinters ? "All installed printers" : "Card printers only", false, null, null);
            add(printer, "Driver settings", DescribeDevMode(s.DevModeBase64), false,
                "Paper size, tray and quality saved from Driver Setup….", null);
            if (hasPrinter && installed)
            {
                string page;
                try { page = new CardPrinter(s).DescribePage(); }
                catch (Exception ex) { page = ex.Message; }
                add(printer, "Page setup", page, false, null, null);
            }

            // ---- Tray layout ----
            const string tray = "Tray layout";
            add(tray, "Slot 1 position", "X " + s.Slot1X.ToString("0.0") + " mm,  Y " + s.Slot1Y.ToString("0.0") + " mm", false, null, null);
            add(tray, "Slot 2 position", "X " + s.Slot2X.ToString("0.0") + " mm,  Y " + s.Slot2Y.ToString("0.0") + " mm", false, null, null);
            add(tray, "Per tray load", DescribeSlotMode(s.SlotMode), false, null, null);
            add(tray, "Card on tray", DescribeOrientation(s.CardOrientation), false, null, null);
            add(tray, "Slot size on page", s.SlotWidthMm.ToString("0.0") + " x " + s.SlotHeightMm.ToString("0.0") + " mm", false, null, null);
            add(tray, "Rotate front 180°", YesNo(s.RotateFront180), false, null, null);
            add(tray, "Rotate back 180°", YesNo(s.RotateBack180), false, null, null);

            // ---- Files & folders ----
            const string files = "Files & folders";
            bool hasPhotos = !string.IsNullOrEmpty(s.PhotoFolder);
            bool photosFound = hasPhotos && Directory.Exists(s.PhotoFolder);
            add(files, "Photo folder", hasPhotos ? s.PhotoFolder : NotSet, !photosFound,
                !hasPhotos ? "Choose it with Photo Folder…" : !photosFound ? "Folder not found." : null,
                photosFound ? s.PhotoFolder : null);
            bool hasSignature = !string.IsNullOrEmpty(s.SignaturePath);
            bool signatureFound = hasSignature && File.Exists(s.SignaturePath);
            add(files, "Director signature", hasSignature ? s.SignaturePath : NotSet + " — no signature is printed",
                hasSignature && !signatureFound, hasSignature && !signatureFound ? "File not found." : null,
                signatureFound ? s.SignaturePath : null);
            bool hasLast = !string.IsNullOrEmpty(s.LastCsvPath);
            add(files, "Last student file", hasLast ? s.LastCsvPath : NotSet,
                hasLast && !File.Exists(s.LastCsvPath), hasLast && !File.Exists(s.LastCsvPath) ? "File not found." : null,
                hasLast ? s.LastCsvPath : null);
            add(files, "Settings file", AppSettings.FilePath, false, null, AppSettings.FilePath);
            add(files, "Print history file", PrintHistory.FilePath, false, null, PrintHistory.FilePath);
            string[] missingAssets = AssetStore.MissingFiles();
            add(files, "Card images (Assets)",
                missingAssets.Length == 0
                    ? "All " + AssetStore.RequiredFiles.Length + " found — " + AssetStore.Folder
                    : "Missing: " + string.Join(", ", missingAssets) + " — " + AssetStore.Folder,
                missingAssets.Length > 0, null, AssetStore.Folder);

            // ---- Web API ----
            const string api = "Web API";
            add(api, "API address", Or(s.ApiUrl, NotSet), false, "Used by Import API…", null);
            string token;
            bool tokenWarn = false;
            if (!string.IsNullOrEmpty(s.ApiToken)) token = "Saved (encrypted for this Windows user)";
            else if (!string.IsNullOrEmpty(s.ApiTokenProtected)) { token = "Saved by another Windows user or PC — enter it again"; tokenWarn = true; }
            else token = NotSet;
            add(api, "API token", token, tokenWarn, "The token itself is never shown.", null);

            // ---- Card text (same names as the Card Text… window) ----
            CardText cardText = s.CardText ?? new CardText();
            foreach (PropertyDescriptor p in TypeDescriptor.GetProperties(cardText))
            {
                if (!p.IsBrowsable) continue;
                string category = p.Category ?? "";
                int dot = category.IndexOf(". ", StringComparison.Ordinal);
                if (dot >= 0) category = category.Substring(dot + 2);   // "1. Front" -> "Front"
                add("Card text — " + category, p.DisplayName, Or(p.GetValue(cardText) as string, NotSet), false,
                    string.IsNullOrEmpty(p.Description) ? null : p.Description, null);
            }

            // ---- Fonts ----
            const string fonts = "Fonts";
            add(fonts, "App font", CardFonts.UI, false, null, null);
            foreach (string family in CardFonts.Required)
            {
                bool ok = CardFonts.IsInstalled(family);
                add(fonts, family, ok ? "Installed" : "Missing — a fallback font is used", !ok,
                    ok ? null : "Install this font for Windows (all users), then restart the app.", null);
            }

            // ---- System ----
            const string system = "System";
            add(system, "App version", Application.ProductVersion, false, null, null);
            add(system, "App folder", AppDomain.CurrentDomain.BaseDirectory, false, null, AppDomain.CurrentDomain.BaseDirectory);
            add(system, "Windows", RuntimeInformation.OSDescription.Trim() +
                (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)"), false, null, null);
            add(system, ".NET", RuntimeInformation.FrameworkDescription.Trim(), false, null, null);
            add(system, "Computer / user", Environment.MachineName + " / " + Environment.UserName, false, null, null);
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            add(system, "Screen", screen.Width + " x " + screen.Height + " px,  scaling " + (dpi * 100 / 96) + " %", false, null, null);

            return list;
        }

        private static string DescribeDevMode(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return "Driver defaults (Driver Setup… not used yet)";
            try { return "Saved from Driver Setup… (" + Convert.FromBase64String(base64).Length + " bytes)"; }
            catch (FormatException) { return "Saved, but unreadable — run Driver Setup… again"; }
        }

        private static string DescribeSlotMode(SlotMode mode)
        {
            switch (mode)
            {
                case SlotMode.One: return "1 — always slot 1 only";
                case SlotMode.Two: return "2 — always slot 1 + slot 2";
                default: return "Auto — 2 cards per load when 2 or more are selected and slot 2 is set up";
            }
        }

        private static string DescribeOrientation(CardOrientation o)
        {
            switch (o)
            {
                case CardOrientation.Horizontal: return "Horizontal";
                case CardOrientation.VerticalLeft: return "Vertical (turn left 90°)";
                default: return "Vertical (turn right 90°)";
            }
        }

        // ===================== Helpers =====================
        private static string YesNo(bool value) { return value ? "Yes" : "No"; }

        private static string Or(string value, string fallback) { return string.IsNullOrEmpty(value) ? fallback : value; }

        private static bool Has(string value, string q)
        {
            return value != null && value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 30, Margin = new Padding(3, 6, 3, 3) };
            b.Click += onClick;
            return b;
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
