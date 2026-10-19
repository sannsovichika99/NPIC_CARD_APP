using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Printing;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Read-only overview of every setting of the app and of this PC (printer, tray layout, files, web API,
    /// SQL Server, card text, fonts, assets). Rows that need attention are shown in orange.
    /// Double-click a file / folder row to open it in Explorer.   Ctrl+F = search, Esc = close
    /// The API token and the SQL password are never shown, only whether one is saved.
    /// SQL Server… opens the connection page; students loaded there are handed to the main window through
    /// <see cref="SqlStudents"/>.
    /// Export… / Import… save the settings to a file and load them back (backup, or set up another PC);
    /// after an import <see cref="SettingsImported"/> tells the main window to reload its controls.
    /// </summary>
    public class SettingsOverviewForm : ModalForm
    {
        private static readonly Color Accent     = Theme.Accent;
        private static readonly Color AccentSoft = Theme.AccentSoft;
        private static readonly Color Surface    = Theme.Surface;
        private static readonly Color SurfaceAlt = Theme.SurfaceAlt;
        private static readonly Color GridLine   = Theme.GridLine;
        private static readonly Color TextMain   = Theme.TextMain;
        private static readonly Color TextMuted  = Theme.TextSoft;
        private static readonly Color Warning    = Theme.Warning;

        private const string NotSet = "(not set)";

        /// <summary>One line of the overview.</summary>
        private sealed class Entry
        {
            public string Section, Name, Value, Note;
            public string Glyph;      // icon of the section
            public bool Warn;
            public string OpenPath;   // file or folder opened on double-click; null = nothing to open
        }

        private readonly AppSettings _settings;
        private List<Entry> _all;
        private readonly TextBox _txtSearch;
        private readonly SettingsList _list;
        private readonly Panel _nav;
        private readonly Label _lblSummary;
        private readonly ToolTip _tip = new ToolTip();
        private readonly Dictionary<string, NavItem> _navSections = new Dictionary<string, NavItem>();
        private NavItem _navAll, _navAttention;
        private bool _attentionOnly;          // "Needs attention" picked: only the orange rows
        private ContextMenuStrip _rowMenu;

        /// <summary>True after Import… replaced the settings: the main window must reload its controls.</summary>
        public bool SettingsImported { get; private set; }

        /// <summary>Students loaded with SQL Server… (null when none were loaded); the main window shows them.</summary>
        public List<Student> SqlStudents { get; private set; }

        /// <summary>"server / database" the <see cref="SqlStudents"/> came from.</summary>
        public string SqlSource { get; private set; }

        public SettingsOverviewForm(AppSettings settings)
        {
            _settings = settings;
            Text = "All Settings";
            Subtitle = "Every setting of the app and of this PC";
            Glyph = Theme.IcoSettings;
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
            _txtSearch = new TextBox { Width = 260, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 5, 12, 0) };
            _txtSearch.HandleCreated += (s, e) => SetCueBanner(_txtSearch, "Search settings… (Ctrl+F)");
            _txtSearch.TextChanged += (s, e) => ShowEntries();
            _txtSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Down) return;   // ↓ from the search box goes to the first result
                _list.Focus();
                _list.SelectFirst();
                e.Handled = e.SuppressKeyPress = true;
            };

            var toolbar = ModalUi.Toolbar();
            toolbar.Controls.Add(_txtSearch);
            Button btnExport = MakeButton("Export…", (s, e) => ExportSettings());
            Button btnImport = MakeButton("Import…", (s, e) => ImportSettings());
            toolbar.Controls.Add(btnExport);
            toolbar.Controls.Add(btnImport);
            Button btnSql = MakeButton("SQL Server…", (s, e) => ConnectSqlServer());
            toolbar.Controls.Add(btnSql);
            toolbar.Controls.Add(MakeButton("Copy All", (s, e) => CopyAll()));
            toolbar.Controls.Add(MakeButton("Open Settings Folder", (s, e) => OpenInExplorer(AppSettings.FilePath)));

            _tip.SetTip(btnExport, "Save all settings to a file, to back them up or copy them to another PC.\n" +
                                       "The API token and SQL password are not included.");
            _tip.SetTip(btnSql, "Change or test the SQL Server connection, or load students from the database (Ctrl+D in the main window).");
            _tip.SetTip(btnImport, "Replace all settings with the ones in an exported file.\n" +
                                       "The current settings are backed up first.");

            // ---- Section navigator (left) + one card per section (right) ----
            _list = new SettingsList { Dock = DockStyle.Fill };
            _list.OpenRequested += entry => OpenInExplorer(entry.OpenPath);
            _list.MenuRequested += ShowRowMenu;
            _list.TopSectionChanged += (s, e) => HighlightNav();

            _nav = new Panel { Dock = DockStyle.Left, Width = 224, BackColor = Surface, AutoScroll = true, Padding = new Padding(0, 10, 1, 10) };
            _nav.Paint += (s, e) =>
            {
                using (var pen = new Pen(GridLine)) e.Graphics.DrawLine(pen, _nav.Width - 1, 0, _nav.Width - 1, _nav.Height);
            };
            BuildNav();

            _lblSummary = new Label
            {
                Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(10, 6, 10, 4)
            };
            ModalUi.StyleStatus(_lblSummary);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(_list);
            Controls.Add(_nav);
            Controls.Add(toolbar);
            Controls.Add(_lblSummary);

            ShowEntries();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_nav != null) _nav.Visible = ClientSize.Width >= 760;   // narrow window: the cards get the whole width
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_rowMenu != null) _rowMenu.Dispose();
                _tip.Dispose();
            }
            base.Dispose(disposing);
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
        /// <summary>
        /// Left column: All Settings and Needs attention (filters), then one item per section (jumps to its card).
        /// Sections with problems show how many in orange. Rebuilt when the settings change (import, SQL Server…).
        /// </summary>
        private void BuildNav()
        {
            _nav.SuspendLayout();
            foreach (Control c in _nav.Controls.Cast<Control>().ToArray()) c.Dispose();
            _navSections.Clear();

            _navAll = new NavItem("All Settings", Theme.IcoList, (s, e) => ShowSection(null, false));
            _navAttention = new NavItem("Needs attention", Theme.IcoWarning, (s, e) => ShowSection(null, true))
            {
                BadgeColor = Warning
            };
            var divider = new Panel { Height = 17, BackColor = Surface };
            divider.Paint += (s, e) =>
            {
                using (var pen = new Pen(GridLine)) e.Graphics.DrawLine(pen, 18, 8, divider.Width - 18, 8);
            };
            var items = new List<Control> { _navAll, _navAttention, divider };
            foreach (IGrouping<string, Entry> group in _all.GroupBy(x => x.Section))   // keeps the order of the list
            {
                string section = group.Key;
                var item = new NavItem(section, group.First().Glyph, (s, e) => ShowSection(section, false)) { Height = 34 };
                _tip.SetToolTip(item, section);
                _navSections[section] = item;
                items.Add(item);
            }

            // Dock = Top stacks the LAST added control on top, so add bottom-up.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                items[i].Dock = DockStyle.Top;
                _nav.Controls.Add(items[i]);
            }
            _nav.ResumeLayout(true);
        }

        /// <summary>All Settings / Needs attention switch the filter; a section jumps to its card (showing everything again).</summary>
        private void ShowSection(string section, bool attentionOnly)
        {
            if (_attentionOnly != attentionOnly)
            {
                _attentionOnly = attentionOnly;
                ShowEntries();
            }
            if (section == null)
            {
                _list.ScrollToTop();
            }
            else if (!_list.ScrollToSection(section))
            {
                _txtSearch.Clear();   // hidden by the search: show everything, then jump
                _list.ScrollToSection(section);
            }
            HighlightNav();
        }

        /// <summary>Filter item and the section whose card is at the top of the list.</summary>
        private void HighlightNav()
        {
            if (_navAll == null) return;
            _navAll.Active = !_attentionOnly;
            _navAttention.Active = _attentionOnly;
            foreach (KeyValuePair<string, NavItem> pair in _navSections)
                pair.Value.Active = pair.Key == _list.TopSection;
        }

        private void ShowEntries()
        {
            string q = _txtSearch.Text.Trim();
            IEnumerable<Entry> filtered = _all;
            if (_attentionOnly) filtered = filtered.Where(x => x.Warn);
            if (q.Length > 0) filtered = filtered.Where(x => Has(x.Section, q) || Has(x.Name, q) || Has(x.Value, q) || Has(x.Note, q));
            List<Entry> shown = filtered.ToList();

            _list.EmptyText = Lang.T(_attentionOnly && q.Length == 0 ? "Nothing needs attention." : "No setting matches the search.");
            _list.SetEntries(shown);

            int warnings = _all.Count(x => x.Warn);
            _navAll.Badge = _all.Count.ToString();
            _navAttention.Badge = warnings.ToString();
            _navAttention.Visible = warnings > 0 || _attentionOnly;
            foreach (KeyValuePair<string, NavItem> pair in _navSections)
            {
                pair.Value.Visible = shown.Any(x => x.Section == pair.Key);
                int sectionWarnings = _all.Count(x => x.Section == pair.Key && x.Warn);
                pair.Value.Badge = sectionWarnings > 0 ? sectionWarnings.ToString() : null;
                pair.Value.BadgeColor = Warning;
            }
            HighlightNav();

            _lblSummary.Text =
                Lang.F("{0} settings", _all.Count) + (q.Length > 0 ? Lang.F("  ·  {0} shown", shown.Count) : "") +
                (warnings > 0 ? Lang.F("  ·  {0} need attention (orange)", warnings) : Lang.T("  ·  nothing needs attention")) +
                Environment.NewLine +
                Lang.F("Read-only. Change settings in the main window.  Settings file: {0}", AppSettings.FilePath);
            _lblSummary.ForeColor = warnings > 0 ? Warning : TextMuted;
        }

        /// <summary>Settings changed (import, SQL Server…): collect them again and rebuild the navigator and the list.</summary>
        private void Reload()
        {
            _all = Collect(_settings, DeviceDpi);
            BuildNav();
            ShowEntries();
        }

        /// <summary>Right-click / menu key on a row: copy it, or open its file or folder.</summary>
        private void ShowRowMenu(Entry entry, Point at)
        {
            if (_rowMenu != null) _rowMenu.Dispose();
            _rowMenu = ThemedMenu.Create();
            _rowMenu.Items.Add(ThemedMenu.Item(Lang.T("Copy value"), () => CopyText(entry.Value)));
            _rowMenu.Items.Add(ThemedMenu.Item(Lang.T("Copy setting and value"), () => CopyText(entry.Name + ": " + entry.Value)));
            if (entry.OpenPath != null)
            {
                _rowMenu.Items.Add(new ToolStripSeparator());
                _rowMenu.Items.Add(ThemedMenu.Item(Lang.T("Open in Explorer"), () => OpenInExplorer(entry.OpenPath)));
            }
            _rowMenu.Show(_list, at);
        }

        private void CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
            }
            catch (ExternalException ex)
            {
                AppDialog.Show(this, "Could not copy to the clipboard:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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
                AppDialog.Show(this, "Could not copy to the clipboard:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    AppDialog.Show(this, "Could not export the settings:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                AppDialog.Show(this, "Settings exported to:\n" + dlg.FileName +
                    "\n\nThe API token, Google Drive API key and SQL password are not in the file; enter them again with " +
                    "Import API…, Google Drive Photos… and SQL Server… on another PC.",
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
                AppDialog.Show(this, "Could not read the settings file:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (AppDialog.Show(this,
                    "Replace ALL current settings with the ones in this file?\n" + path + "\n\n" +
                    "Printer, tray positions, folders, card text, API address and SQL Server connection will change.\n" +
                    "Your current settings are backed up to:\n" + AppSettings.BackupFilePath + "\n\n" +
                    "Folders and files that do not exist on this PC will be shown in orange.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            try
            {
                if (File.Exists(AppSettings.FilePath)) File.Copy(AppSettings.FilePath, AppSettings.BackupFilePath, true);

                // An exported file has no API token or SQL password: keep the ones saved on this PC.
                string token = _settings.ApiTokenProtected;
                string sqlPassword = _settings.SqlPasswordProtected;
                _settings.CopyFrom(imported);
                if (string.IsNullOrEmpty(_settings.ApiTokenProtected)) _settings.ApiTokenProtected = token;
                if (string.IsNullOrEmpty(_settings.SqlPasswordProtected)) _settings.SqlPasswordProtected = sqlPassword;
                _settings.Save();
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not import the settings:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SettingsImported = true;
            Reload();
            string counts = _lblSummary.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.None)[0];
            _lblSummary.Text = counts + Environment.NewLine + "Imported from " + path +
                "   (previous settings: " + AppSettings.BackupFilePath + ")";
        }

        /// <summary>Opens the SQL Server connection page. Loaded students go to the main window, which this closes for.</summary>
        private void ConnectSqlServer()
        {
            List<Student> loaded = null;
            string source = null;
            using (var form = new SqlServerForm(_settings))
            {
                if (form.ShowDialog(this) == DialogResult.OK) { loaded = form.Students; source = form.SourceName; }
            }

            try
            {
                _settings.Save();   // keep the connection details even when only Test Connection was used
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not save the settings:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (loaded != null)
            {
                SqlStudents = loaded;
                SqlSource = source;
                Close();   // show the loaded students in the main window
                return;
            }

            Reload();
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
                    else AppDialog.Show(this, "Not found:\n" + path, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not open the folder:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ===================== Data =====================
        private static List<Entry> Collect(AppSettings s, int dpi)
        {
            var list = new List<Entry>();
            Action<string, string, string, bool, string, string> add = (section, name, value, warn, note, open) =>
                list.Add(new Entry
                {
                    // In the app's language; values that are data (paths, names) have no table entry and stay as they are.
                    Section = Lang.T(section), Name = Lang.T(name), Value = Lang.T(value), Warn = warn, Note = Lang.T(note), OpenPath = open,
                    Glyph = SectionGlyph(section)
                });

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

            // ---- Appearance ----
            add("Appearance", "Colour theme", s.DarkMode ? "Dark" : "Light", false,
                "Switch with Settings & Help… → Dark Mode / Light Mode in the sidebar (restarts the app). Printed cards are not affected.", null);
            add("Appearance", "Primary colour", AccentPalette.Describe(s.AccentColor), false,
                "Change it with Settings & Help… → Theme Color… in the sidebar (restarts the app). Printed cards are not affected.", null);
            add("Appearance", "Language", Lang.IsKhmerCode(s.UiLanguage) ? "Khmer" : "English", false,
                "Switch with the language menu in the top bar, Settings & Help… → Language or Ctrl+Shift+L (restarts the app). " +
                "Printed cards are not affected.", null);

            // ---- Files & folders ----
            const string files = "Files & folders";
            bool hasPhotos = !string.IsNullOrEmpty(s.PhotoFolder);
            bool photosFound = hasPhotos && Directory.Exists(s.PhotoFolder);
            add(files, "Photo folder", hasPhotos ? s.PhotoFolder : NotSet, !photosFound,
                !hasPhotos ? "Choose it with Photo Folder…" : !photosFound ? "Folder not found." : null,
                photosFound ? s.PhotoFolder : null);
            CardText sigText = s.CardText ?? new CardText();
            bool hasSignature = !string.IsNullOrEmpty(sigText.SignatureFile);
            bool signatureFound = hasSignature && File.Exists(sigText.SignatureFile);
            add(files, "Director signature",
                !hasSignature ? NotSet + " — no signature is printed"
                    : (sigText.SignaturePath == CardText.DefaultSignaturePath ? "Default — " : "") + sigText.SignatureFile +
                      (signatureFound ? "" : " — no signature is printed"),
                hasSignature && !signatureFound,
                (hasSignature && !signatureFound ? "File not found. " : "") + "Change it in Card Text… → Front → Director signature.",
                signatureFound ? sigText.SignatureFile : null);
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

            // ---- Google Sheets ----
            add("Google Sheets", "Sheet link", Or(s.GoogleSheetUrl, NotSet), false,
                "Used by From Google Sheets… (Ctrl+Shift+G). Change it there.", null);

            // ---- Google Drive photos ----
            const string drive = "Google Drive photos";
            add(drive, "Folder link", Or(s.DriveFolderUrl, NotSet), false,
                "Used by Photo Folder… → Google Drive Photos… (Ctrl+Shift+D). Change it there.", null);
            string driveKey;
            bool driveKeyWarn = false;
            if (!string.IsNullOrEmpty(s.DriveApiKey)) driveKey = "Saved (encrypted for this Windows user)";
            else if (!string.IsNullOrEmpty(s.DriveApiKeyProtected)) { driveKey = "Saved by another Windows user or PC — enter it again"; driveKeyWarn = true; }
            else driveKey = NotSet;
            add(drive, "API key", driveKey, driveKeyWarn, "The key itself is never shown.", null);

            // ---- OneDrive photos ----
            const string oneDrive = "OneDrive photos";
            bool oneDriveOnline = OneDriveOnlineSync.IsOnline(s.OneDriveFolder);
            bool oneDriveMissing = !string.IsNullOrEmpty(s.OneDriveFolder) && !oneDriveOnline && !System.IO.Directory.Exists(s.OneDriveFolder);
            add(oneDrive, "Folder", Or(s.OneDriveFolder, NotSet), oneDriveMissing,
                oneDriveMissing ? "Not found on this PC — check that OneDrive is signed in, or choose the folder again in OneDrive Photos…."
                                : "Used by Photo Folder… → OneDrive Photos… (Ctrl+Shift+O). Change it there.", null);
            add(oneDrive, "App ID", Or(s.OneDriveClientId, NotSet), false, "Application (client) ID of the Microsoft app registration.", null);
            string oneDriveAccount;
            bool oneDriveWarn = false;
            if (!string.IsNullOrEmpty(s.OneDriveRefreshToken)) oneDriveAccount = "Signed in" + (string.IsNullOrEmpty(s.OneDriveAccount) ? "" : " as " + s.OneDriveAccount);
            else if (!string.IsNullOrEmpty(s.OneDriveRefreshTokenProtected)) { oneDriveAccount = "Signed in by another Windows user or PC — sign in again"; oneDriveWarn = true; }
            else oneDriveAccount = "Not signed in";
            add(oneDrive, "Account", oneDriveAccount, oneDriveWarn && oneDriveOnline, "Needed for a OneDrive link, not for a synced folder on this PC.", null);

            // ---- SQL Server ----
            const string sql = "SQL Server";
            bool hasServer = !string.IsNullOrEmpty(s.SqlServer);
            add(sql, "Server", Or(s.SqlServer, NotSet), false, "Used by SQL Server… (Ctrl+D). Change it there.", null);
            add(sql, "Database", Or(s.SqlDatabase, hasServer ? "(login's default database)" : NotSet), false, null, null);
            add(sql, "Authentication", s.SqlWindowsAuth ? "Windows Authentication" : "SQL Server Authentication", false,
                s.SqlWindowsAuth ? "Connects as the Windows user running the app (" + Environment.UserDomainName + "\\" + Environment.UserName + ")." : null,
                null);
            if (!s.SqlWindowsAuth)
            {
                bool hasUser = !string.IsNullOrEmpty(s.SqlUser);
                add(sql, "Login", hasUser ? s.SqlUser : NotSet, hasServer && !hasUser,
                    hasServer && !hasUser ? "Enter the login name in SQL Server…" : null, null);
                string password;
                bool passwordWarn = false;
                if (!string.IsNullOrEmpty(s.SqlPassword)) password = "Saved (encrypted for this Windows user)";
                else if (!string.IsNullOrEmpty(s.SqlPasswordProtected)) { password = "Saved by another Windows user or PC — enter it again"; passwordWarn = true; }
                else password = "Not saved — enter it in SQL Server… each time";
                add(sql, "Password", password, passwordWarn, "The password itself is never shown.", null);
            }
            add(sql, "Encrypt connection", YesNo(s.SqlEncrypt), false, null, null);
            add(sql, "Trust server certificate", YesNo(s.SqlTrustServerCertificate), false,
                "Yes = accept the server's self-signed certificate (usual for a school server).", null);
            string query = (s.SqlQuery ?? "").Trim();
            add(sql, "Table or query", query.Length == 0 ? NotSet : query, false,
                "A table name (dbo.Students) or one SELECT query. The app only reads the database.", null);

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
        /// <summary>Icon of a section, from its English name.</summary>
        private static string SectionGlyph(string section)
        {
            if (section.StartsWith("Card text", StringComparison.Ordinal)) return Theme.IcoEdit;
            switch (section)
            {
                case "Printer": return Theme.IcoPrint;
                case "Tray layout": return Theme.IcoTemplate;
                case "Appearance": return Theme.IcoColor;
                case "Files & folders": return Theme.IcoFolder;
                case "Web API": return Theme.IcoGlobe;
                case "Google Sheets": return Theme.IcoDocument;
                case "Google Drive photos":
                case "OneDrive photos": return Theme.IcoCloud;
                case "SQL Server": return Theme.IcoDatabase;
                case "Fonts": return Theme.IcoFont;
                case "System": return Theme.IcoInfo;
                default: return Theme.IcoSettings;
            }
        }

        private static string YesNo(bool value) { return value ? "Yes" : "No"; }

        private static string Or(string value, string fallback) { return string.IsNullOrEmpty(value) ? fallback : value; }

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
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, Lang.T(text));
        }

        // ===================== Card list =====================
        /// <summary>
        /// Owner-drawn settings list: one rounded card per section (icon, title, number of settings, attention badge) with a
        /// name | value row per setting and its note under the value. Rows that need attention are tinted orange; rows
        /// with a file or folder get an open button. Click selects, the button / double-click / Enter opens, right-click
        /// or the menu key asks for the row menu, ↑ ↓ Home End move, Ctrl+C copies "Setting: value".
        /// </summary>
        private sealed class SettingsList : ScrollableControl
        {
            private const int Pad = 16, CardGap = 14, HeadH = 50, RowPadX = 18, RowPadY = 10, MinRowH = 40,
                              OpenSize = 28, Radius = 10, NoteGap = 3;

            private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl |
                                                 TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            private const TextFormatFlags OneLine = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                                                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            // Measuring one line: no ellipsis, or the measured width shrinks to "…".
            private const TextFormatFlags Measure1 = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            private sealed class Card
            {
                public string Section, Glyph;
                public int Warnings;
                public Rectangle Bounds;
                public readonly List<Row> Rows = new List<Row>();
            }

            private sealed class Row
            {
                public Entry Entry;
                public Card Card;
                public Rectangle Bounds, Name, Value, Note, Open;   // content coordinates (not scrolled)
            }

            private readonly Font _titleFont = Theme.UiFont(10.5f, true);
            private readonly Font _nameFont = Theme.UiFont(9f);
            private readonly Font _valueFont = new Font(CardFonts.UI, 9.5f);
            private readonly Font _smallFont = Theme.UiFont(8.25f);
            private readonly ToolTip _tip = new ToolTip();
            private readonly List<Card> _cards = new List<Card>();
            private readonly List<Row> _rows = new List<Row>();
            private Row _hover, _selected;
            private bool _hoverOpen;
            private int _layoutWidth = -1;

            public event Action<Entry> OpenRequested;
            public event Action<Entry, Point> MenuRequested;
            public event EventHandler TopSectionChanged;

            /// <summary>Section whose card is at the top of the visible part of the list.</summary>
            public string TopSection { get; private set; }

            /// <summary>Shown in the middle when there is nothing to list.</summary>
            public string EmptyText { get; set; }

            public SettingsList()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                AutoScroll = true;
                BackColor = Theme.SurfaceAlt;
                TabStop = true;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _titleFont.Dispose();
                    _nameFont.Dispose();
                    _valueFont.Dispose();
                    _smallFont.Dispose();
                    _tip.Dispose();
                }
                base.Dispose(disposing);
            }

            // ---------- Content ----------
            public void SetEntries(List<Entry> entries)
            {
                Entry keep = _selected != null ? _selected.Entry : null;
                _cards.Clear();
                _rows.Clear();
                Card card = null;
                foreach (Entry x in entries)   // entries come grouped by section
                {
                    if (card == null || card.Section != x.Section)
                    {
                        card = new Card { Section = x.Section, Glyph = x.Glyph };
                        _cards.Add(card);
                    }
                    var row = new Row { Entry = x, Card = card };
                    card.Rows.Add(row);
                    _rows.Add(row);
                    if (x.Warn) card.Warnings++;
                }
                _hover = null;
                _selected = _rows.FirstOrDefault(r => r.Entry == keep);
                Relayout();
                AutoScrollPosition = Point.Empty;
                SetTop(_cards.Count > 0 ? _cards[0].Section : null);
                Invalidate();
            }

            public void ScrollToTop()
            {
                SetScrollY(0);
                SetTop(_cards.Count > 0 ? _cards[0].Section : null);
            }

            /// <summary>Brings the section's card to the top. False when the section is not in the list (filtered out).</summary>
            public bool ScrollToSection(string section)
            {
                Card card = _cards.FirstOrDefault(c => c.Section == section);
                if (card == null) return false;
                SetScrollY(card.Bounds.Top - Pad);
                SetTop(section);
                return true;
            }

            public void SelectFirst()
            {
                if (_rows.Count > 0) SelectRow(_rows[0]);
            }

            // ---------- Layout ----------
            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (ClientSize.Width != _layoutWidth) { Relayout(); Invalidate(); }
            }

            private void Relayout()
            {
                int width = ClientSize.Width;
                if (width <= 0) return;
                _layoutWidth = width;

                int cardW = Math.Max(260, width - 2 * Pad);
                int nameW = Math.Max(120, Math.Min(240, cardW * 30 / 100));
                int valueLine = TextRenderer.MeasureText("Ag", _valueFont, Size.Empty, Measure1).Height;
                int nameLine = TextRenderer.MeasureText("Ag", _nameFont, Size.Empty, Measure1).Height;

                int y = Pad;
                foreach (Card card in _cards)
                {
                    int x = Pad, ry = y + HeadH;
                    int nameX = x + RowPadX, valueX = nameX + nameW + 18;
                    int valueW = Math.Max(80, x + cardW - RowPadX - OpenSize - 10 - valueX);
                    foreach (Row row in card.Rows)
                    {
                        Entry e = row.Entry;
                        int nameH = Measure(e.Name, _nameFont, nameW);
                        int valueH = Measure(e.Value, _valueFont, valueW);
                        int noteH = Measure(e.Note, _smallFont, valueW);
                        int contentH = Math.Max(nameH, valueH + (noteH > 0 ? NoteGap + noteH : 0));
                        int h = Math.Max(MinRowH, contentH + 2 * RowPadY);
                        int top = ry + (h - contentH) / 2;

                        row.Bounds = new Rectangle(x, ry, cardW, h);
                        // The name sits level with the first line of the value.
                        row.Name = new Rectangle(nameX, top + Math.Max(0, (valueLine - nameLine) / 2), nameW, nameH);
                        row.Value = new Rectangle(valueX, top, valueW, valueH);
                        row.Note = noteH > 0 ? new Rectangle(valueX, top + valueH + NoteGap, valueW, noteH) : Rectangle.Empty;
                        row.Open = e.OpenPath == null ? Rectangle.Empty
                            : new Rectangle(x + cardW - RowPadX - OpenSize + 6, top + (valueLine - OpenSize) / 2, OpenSize, OpenSize);
                        ry += h;
                    }
                    card.Bounds = new Rectangle(x, y, cardW, ry - y + 2);
                    y = card.Bounds.Bottom + CardGap;
                }
                AutoScrollMinSize = new Size(0, _cards.Count == 0 ? 0 : y - CardGap + Pad);
            }

            private static int Measure(string text, Font font, int width)
            {
                if (string.IsNullOrEmpty(text)) return 0;
                return TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), Wrap).Height + 1;
            }

            // ---------- Painting ----------
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                if (_cards.Count == 0)
                {
                    TextRenderer.DrawText(g, EmptyText ?? "", _nameFont, ClientRectangle, Theme.TextSoft,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                    return;
                }
                Theme.HighQuality(g);
                Point o = AutoScrollPosition;
                foreach (Card card in _cards)
                {
                    Rectangle cb = Shift(card.Bounds, o);
                    if (cb.Bottom < e.ClipRectangle.Top - 12 || cb.Top > e.ClipRectangle.Bottom + 12) continue;
                    PaintCard(g, card, cb, o, e.ClipRectangle);
                }
            }

            private void PaintCard(Graphics g, Card card, Rectangle cb, Point o, Rectangle clip)
            {
                var shape = new RectangleF(cb.X + 0.5f, cb.Y + 0.5f, cb.Width - 1f, cb.Height - 1f);
                Theme.DrawShadow(g, shape, Radius, Theme.Dark ? Color.Black : Theme.Accent, 4, 10, 2f);
                using (GraphicsPath path = Theme.RoundRect(shape, Radius))
                {
                    using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);

                    Region old = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    foreach (Row row in card.Rows)
                    {
                        Rectangle rb = Shift(row.Bounds, o);
                        if (rb.Bottom >= clip.Top && rb.Top <= clip.Bottom) PaintRow(g, row, rb, o);
                    }
                    g.Clip = old;
                    old.Dispose();

                    PaintHeader(g, card, cb);
                    using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
                }
            }

            private void PaintHeader(Graphics g, Card card, Rectangle cb)
            {
                bool warn = card.Warnings > 0;
                var icon = new RectangleF(cb.X + 14, cb.Y + (HeadH - 32) / 2f, 32, 32);
                using (GraphicsPath p = Theme.RoundRect(icon, 9))
                using (var b = new SolidBrush(warn ? Theme.WarningSoft : Theme.AccentSoft))
                    g.FillPath(b, p);
                Theme.DrawIcon(g, card.Glyph, 16f, warn ? Theme.Warning : Theme.Accent, icon);

                int right = cb.Right - 14;
                if (warn)
                {
                    string text = Lang.F("{0} need attention", card.Warnings);
                    int w = TextRenderer.MeasureText(text, _smallFont, Size.Empty, Measure1).Width + 22;
                    var pill = new Rectangle(right - w, cb.Y + (HeadH - 24) / 2, w, 24);
                    using (GraphicsPath p = Theme.RoundRect(pill, 12))
                    using (var b = new SolidBrush(Theme.WarningSoft))
                        g.FillPath(b, p);
                    TextRenderer.DrawText(g, text, _smallFont, pill, Theme.Warning, OneLine | TextFormatFlags.HorizontalCenter);
                    right = pill.Left - 10;
                }

                int titleX = (int)icon.Right + 12;
                string count = Lang.F("{0} settings", card.Rows.Count);
                int countW = TextRenderer.MeasureText(count, _smallFont, Size.Empty, Measure1).Width;
                int titleW = Math.Min(TextRenderer.MeasureText(card.Section, _titleFont, Size.Empty, Measure1).Width,
                                      Math.Max(40, right - titleX - countW - 12));
                TextRenderer.DrawText(g, card.Section, _titleFont, new Rectangle(titleX, cb.Y, titleW, HeadH), Theme.TextMain, OneLine);
                TextRenderer.DrawText(g, count, _smallFont, new Rectangle(titleX + titleW + 10, cb.Y + 1, countW + 2, HeadH),
                                      Theme.TextMuted, OneLine);

                using (var pen = new Pen(Theme.GridLine))
                    g.DrawLine(pen, cb.X, cb.Y + HeadH - 1, cb.Right, cb.Y + HeadH - 1);
            }

            private void PaintRow(Graphics g, Row row, Rectangle rb, Point o)
            {
                Entry e = row.Entry;
                bool selected = row == _selected, hover = row == _hover;

                Color back = selected ? Theme.AccentSoft : e.Warn ? Theme.WarningSoft : hover ? Theme.SurfaceHover : Color.Empty;
                if (!back.IsEmpty)
                    using (var b = new SolidBrush(back)) g.FillRectangle(b, rb);
                if (selected || e.Warn)
                    using (var b = new SolidBrush(selected ? Theme.Accent : Theme.Warning)) g.FillRectangle(b, rb.X, rb.Y, 3, rb.Height);
                if (row != row.Card.Rows[0] && back.IsEmpty)
                    using (var pen = new Pen(Theme.GridLine)) g.DrawLine(pen, rb.X + RowPadX, rb.Y, rb.Right - RowPadX, rb.Y);

                TextRenderer.DrawText(g, e.Name, _nameFont, Shift(row.Name, o), Theme.TextSoft, Wrap);
                TextRenderer.DrawText(g, e.Value, _valueFont, Shift(row.Value, o), e.Warn ? Theme.Warning : Theme.TextMain, Wrap);
                if (!row.Note.IsEmpty)
                    TextRenderer.DrawText(g, e.Note, _smallFont, Shift(row.Note, o), e.Warn ? Theme.Warning : Theme.TextMuted, Wrap);

                if (row.Open.IsEmpty) return;
                Rectangle ob = Shift(row.Open, o);
                bool hot = hover && _hoverOpen;
                if (hot)
                    using (GraphicsPath p = Theme.RoundRect(ob, 8))
                    using (var b = new SolidBrush(selected ? Theme.Surface : Theme.AccentSoft))
                        g.FillPath(b, p);
                Theme.DrawIcon(g, Theme.IcoOpenFolder, 14f,
                    hot ? Theme.Accent : hover || selected ? Theme.TextSoft : Theme.TextMuted, ob);
            }

            private static Rectangle Shift(Rectangle r, Point o)
            {
                r.Offset(o);
                return r;
            }

            // ---------- Mouse ----------
            private Point ToContent(Point client)
            {
                return new Point(client.X - AutoScrollPosition.X, client.Y - AutoScrollPosition.Y);
            }

            private Row HitRow(Point client, out bool onOpen)
            {
                Point p = ToContent(client);
                foreach (Row row in _rows)
                {
                    if (!row.Bounds.Contains(p)) continue;
                    onOpen = !row.Open.IsEmpty && Rectangle.Inflate(row.Open, 4, 4).Contains(p);
                    return row;
                }
                onOpen = false;
                return null;
            }

            private void UpdateHover(Point client)
            {
                bool onOpen = false;
                Row row = ClientRectangle.Contains(client) ? HitRow(client, out onOpen) : null;
                if (row == _hover && onOpen == _hoverOpen) return;
                if (onOpen != _hoverOpen) _tip.SetToolTip(this, onOpen ? Lang.T("Open in Explorer") : null);
                _hover = row;
                _hoverOpen = onOpen;
                Cursor = onOpen ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                UpdateHover(e.Location);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                UpdateHover(new Point(-1, -1));
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Focus();
                bool onOpen;
                Row row = HitRow(e.Location, out onOpen);
                if (row == null) return;
                SelectRow(row);
                if (e.Button == MouseButtons.Right && MenuRequested != null) MenuRequested(row.Entry, e.Location);
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                bool onOpen;
                Row row = HitRow(e.Location, out onOpen);
                if (row != null && onOpen && e.Button == MouseButtons.Left) Open(row);
            }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                bool onOpen;
                Row row = HitRow(e.Location, out onOpen);
                if (row != null && !onOpen && e.Button == MouseButtons.Left) Open(row);   // the button already opened it
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                UserScrolled();
                UpdateHover(e.Location);
            }

            protected override void OnScroll(ScrollEventArgs se)
            {
                base.OnScroll(se);
                Invalidate();
                UserScrolled();
            }

            // ---------- Keyboard ----------
            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            protected override bool IsInputKey(Keys keyData)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                        return true;
                }
                return base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                int i = _selected == null ? -1 : _rows.IndexOf(_selected);
                int page = Math.Max(40, ClientSize.Height - 60);
                switch (e.KeyData)
                {
                    case Keys.Down: SelectIndex(i + 1); break;
                    case Keys.Up: SelectIndex(i < 0 ? 0 : i - 1); break;
                    case Keys.Home: SelectIndex(0); break;
                    case Keys.End: SelectIndex(_rows.Count - 1); break;
                    case Keys.PageDown: SetScrollY(-AutoScrollPosition.Y + page); UserScrolled(); break;
                    case Keys.PageUp: SetScrollY(-AutoScrollPosition.Y - page); UserScrolled(); break;
                    case Keys.Enter: if (_selected != null) Open(_selected); break;
                    case Keys.Control | Keys.C:
                        if (_selected != null) CopyRow(_selected.Entry);
                        break;
                    case Keys.Apps:
                    case Keys.Shift | Keys.F10:
                        if (_selected != null && MenuRequested != null)
                        {
                            Rectangle rb = Shift(_selected.Bounds, AutoScrollPosition);
                            MenuRequested(_selected.Entry, new Point(Shift(_selected.Value, AutoScrollPosition).X, rb.Bottom - 6));
                        }
                        break;
                    default:
                        base.OnKeyDown(e);
                        return;
                }
                e.Handled = e.SuppressKeyPress = true;
            }

            private void SelectIndex(int i)
            {
                if (_rows.Count == 0) return;
                SelectRow(_rows[Math.Max(0, Math.Min(_rows.Count - 1, i))]);
            }

            private void SelectRow(Row row)
            {
                _selected = row;
                EnsureVisible(row);
                Invalidate();
            }

            /// <summary>Scrolls just enough to show the row (and its card's header when it is the first row).</summary>
            private void EnsureVisible(Row row)
            {
                int top = -AutoScrollPosition.Y, view = ClientSize.Height;
                int rowTop = row == row.Card.Rows[0] ? row.Card.Bounds.Top : row.Bounds.Top;
                if (rowTop - 8 < top) SetScrollY(rowTop - 8);
                else if (row.Bounds.Bottom + 8 > top + view) SetScrollY(row.Bounds.Bottom + 8 - view);
                else return;
                UserScrolled();
            }

            private void SetScrollY(int y)
            {
                AutoScrollPosition = new Point(0, Math.Max(0, y));
                Invalidate();
            }

            private void Open(Row row)
            {
                if (row.Entry.OpenPath != null && OpenRequested != null) OpenRequested(row.Entry);
            }

            private void CopyRow(Entry entry)
            {
                try
                {
                    Clipboard.SetText(entry.Name + ": " + entry.Value);
                }
                catch (ExternalException)
                {
                    // clipboard busy (another app holds it): nothing copied
                }
            }

            // ---------- Section at the top (drives the navigator) ----------
            private void UserScrolled()
            {
                if (_cards.Count == 0) { SetTop(null); return; }
                int top = -AutoScrollPosition.Y;
                bool atEnd = top + ClientSize.Height >= AutoScrollMinSize.Height - 2 && top > 0;
                Card card = atEnd ? _cards[_cards.Count - 1]
                                  : _cards.FirstOrDefault(c => c.Bounds.Bottom - 24 > top) ?? _cards[_cards.Count - 1];
                SetTop(card.Section);
            }

            private void SetTop(string section)
            {
                if (section == TopSection) return;
                TopSection = section;
                if (TopSectionChanged != null) TopSectionChanged(this, EventArgs.Empty);
            }
        }
    }
}
