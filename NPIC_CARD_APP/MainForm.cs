using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Printing;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    public class MainForm : Form
    {
        // ===================== Theme =====================
        // Lavender dashboard palette — tokens live in UiKit.Theme; these aliases keep the dialogs below short.
        private static readonly Color Accent     = Theme.Accent;
        private static readonly Color AccentSoft = Theme.AccentSoft;
        private static readonly Color AppBack    = Theme.AppBack;
        private static readonly Color Surface    = Theme.Surface;
        private static readonly Color SurfaceAlt = Theme.SurfaceAlt;
        private static readonly Color Border     = Theme.Border;
        private static readonly Color GridLine   = Theme.GridLine;
        private static readonly Color TextMain   = Theme.TextMain;
        private static readonly Color TextMuted  = Theme.TextMuted;
        private static readonly Color Warning    = Theme.Warning;
        private static readonly Color Danger     = Theme.Danger;
        private static readonly Color Success    = Theme.Success;

        private static readonly Font UiFont      = new Font(CardFonts.UI, 9f);
        private static readonly Font SemiFont    = new Font(CardFonts.UI, 9f, FontStyle.Bold);
        private static readonly Font CaptionFont = new Font(CardFonts.UI, 8.25f);
        private static readonly Font SetupFont   = new Font(Theme.Family, 9f);

        private const int FieldLabelWidth = 96;

        private static readonly Dictionary<string, string> ColumnTitles = new Dictionary<string, string>
        {
            { "StudentId", "ID" }, { "NameKh", "Name (KH)" }, { "NameEn", "Name (EN)" }, { "Gender", "Sex" },
            { "DateOfBirth", "Date of birth" }, { "Faculty", "Faculty" }, { "Level", "Level" },
            { "Generation", "Gen." }, { "ValidUntil", "Valid until" }, { "QrData", "QR data" }
        };

        // ===================== State =====================
        private readonly AppSettings _settings;
        private List<Student> _students = new List<Student>();
        private List<Student> _view = new List<Student>();   // _students filtered by the search box = what the grid shows

        /// <summary>Students chosen in the Gallery: while set, the list shows only them (null = every student).</summary>
        private HashSet<string> _picked;
        private List<Student> _pickedFrom;   // student list the pick was made in: loading another list ends the pick
        private Control _pickBar;
        private Label _lblPicked;
        private bool _loading;
        private string _source = "";                          // where the list came from: file name, API host or SQL server

        private DataGridView _grid;
        private TextBox _txtSearch;
        private Label _lblCount, _lblEmpty;
        private PreviewTile _picFront, _picBack;
        private PillButton _btnGrid;
        private bool _showGrid;                               // mm grid over the front / back previews (screen only)
        private ComboBox _cboPrinter;
        private CheckBox _chkAllPrinters;
        private NumericUpDown _numS1X, _numS1Y, _numS2X, _numS2Y;
        private SegmentPicker _segSlots, _segOrientation;
        private TrayMap _trayMap;
        private CheckBox _chkRotateBack, _chkRotateFront;
        private Label _lblSlotHint;
        private readonly ToolTip _tip = new ToolTip();
        private PillTabs _sideTabs;
        private Label _lblPage, _lblStatus, _lblStatusDot;
        private StatCard _statStudents, _statSelected, _statPrinted;
        private TableLayoutPanel _dashboard;
        private bool _refreshOnActivate;   // photo folder opened in Explorer: re-read photos when the user comes back

        /// <summary>
        /// A backup was restored: the files on disk are newer than <see cref="_settings"/>, so nothing may save them
        /// again before the restart (closing the window normally saves the settings).
        /// </summary>
        private bool _settingsReplaced;
        private NavItem _navGallery, _navHistory;

        // Sidebar: full (icons + names) or compact (icons only); Ctrl+\ or the button at its bottom switches.
        private const int SidebarFullWidth = 232, SidebarCompactWidth = 68;
        private Panel _sidebar;
        private BrandBlock _brand;
        private NavItem _navToggle;
        private readonly List<Label> _sections = new List<Label>();
        private readonly List<KeyValuePair<NavItem, string>> _navTips = new List<KeyValuePair<NavItem, string>>();
        private bool _sidebarCompact;

        // Bell in the header: warnings, errors and finished jobs of this session (fed by SetStatus).
        private readonly NotificationCenter _notices = new NotificationCenter();
        private NotificationBell _bell;
        private ToolStripDropDown _noticePopup;

        // RFID tab of the Print setup card: save the printed card's chip UID to the RFID CSV file
        private PillTabs _setupTabs;
        private TextBox _txtRfid;
        private Button _btnRfidUpload;
        private Label _lblRfidStudent, _lblRfidPrinted, _lblRfidResult, _lblRfidTarget;
        private bool _rfidBusy;
        private Dictionary<string, DateTime> _lastPrinted =                  // StudentId → last time a side was printed
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True when Print Selected sends the front side.</summary>
        private bool PrintFront { get { return _sideTabs.SelectedIndex == 0; } }

        public MainForm()
        {
            _settings = AppSettings.Load();

            UpdateTitle();
            if (Branding.Icon != null) Icon = Branding.Icon;
            StartPosition = FormStartPosition.CenterScreen;
            // Never open larger than the screen (laptops at 125 % scaling only have ~1536 x 824 to work with).
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1380, work.Width), Math.Min(860, work.Height));
            MinimumSize = new Size(Math.Min(1120, work.Width), Math.Min(700, work.Height));
            Font = UiFont;
            BackColor = AppBack;
            ForeColor = TextMain;

            BuildUi();
            SetSidebarCompact(_settings.SidebarCollapsed, false);
            Lang.Apply(this);           // Khmer only: labels, buttons, columns and status text, also when they change later
            DarkControls.Apply(this);   // dark mode only; before the handles exist, so restyling recreates nothing
            SlimScroll.Apply(this);     // slim scroll bars on every grid, list and scrolling panel
            LoadPrinters();
            ApplySettingsToUi();

            if (File.Exists(_settings.LastCsvPath)) LoadStudents(_settings.LastCsvPath);
            UpdatePageInfo();
            UpdateEmptyState();
            UpdateCount();
            UpdatePrintedStat();

            string[] missing = AssetStore.MissingFiles();
            if (missing.Length > 0)
                SetStatus("Missing in " + AssetStore.Folder + ": " + string.Join(", ", missing));
            UpdatePreview();
            UpdateSideHighlight();
        }

        /// <summary>Short windows (laptops at 125 % scaling) get a lower header and slimmer stat tiles.</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_dashboard == null || WindowState == FormWindowState.Minimized) return;
            bool compact = ClientSize.Height < 760;
            float header = compact ? 58f : 66f, stats = compact ? 100f : 126f;
            if (_dashboard.RowStyles[1].Height != stats)
            {
                _dashboard.RowStyles[0].Height = header;
                _dashboard.RowStyles[1].Height = stats;
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (_refreshOnActivate) UpdatePreview();   // photos may have been added / renamed in Explorer
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CardFonts.WarnIfMissing(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            WarnIfLicenseExpiresSoon();
            CheckForUpdatesQuietly();
            AutoBackupQuietly();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings(false);
            ReplaceImage(_picFront, null);
            ReplaceImage(_picBack, null);
            base.OnFormClosing(e);
        }

        // ===================== UI =====================
        //  ┌──────────┬───────────────────────────────────────────────────────────┐
        //  │ brand    │ Good afternoon, …   bell [EN ▾]  [Front|Back]  Print      │
        //  │ nav      │ [ Students ]   [ Selected ]   [ Printed today ]           │
        //  │  …       │ ┌ Students ─────────────┐  ┌ Card preview ─────────────┐  │
        //  │          │ │ search / grid         │  │ front | back              │  │
        //  │          │ │                       │  ├ Print setup [Printer|Tray]┤  │
        //  │          │ └───────────────────────┘  └───────────────────────────┘  │
        //  │          │ ● status                                                  │
        //  └──────────┴───────────────────────────────────────────────────────────┘
        private void BuildUi()
        {
            SuspendLayout();

            var content = new DecorPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 4) };
            content.Controls.Add(BuildDashboard());

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(content);
            Controls.Add(BuildSidebar());

            ResumeLayout(true);
        }

        /// <summary>
        /// White sidebar: brand pinned at the top, then every command grouped like the sample's navigation (scrolls when
        /// the window is short), and the icons-only / full switch pinned at the bottom.
        /// </summary>
        private Control BuildSidebar()
        {
            var side = new Panel { Dock = DockStyle.Left, Width = SidebarFullWidth, BackColor = Surface, Padding = new Padding(0, 0, 1, 0) };
            side.Paint += (s, e) =>
            {
                using (var pen = new Pen(GridLine)) e.Graphics.DrawLine(pen, side.Width - 1, 0, side.Width - 1, side.Height);
            };
            _sidebar = side;
            var scroll = new Panel { Dock = DockStyle.Fill, BackColor = Surface, AutoScroll = true };

            // App logo embedded in the exe; null (unreadable resource) makes the badge fall back to initials.
            Image logo = Branding.Logo;

            NavItem dashboard = Nav("Dashboard", Theme.IcoHome, () => { _txtSearch.Focus(); _txtSearch.SelectAll(); },
                "Student list, card preview and printing (Ctrl+F searches)");
            dashboard.Active = true;
            _navGallery = Nav("Gallery", Theme.IcoPhoto, ShowGallery,
                "Photos and details of every student; shows missing photos and print status (Ctrl+G)");
            _navHistory = Nav("Print History", Theme.IcoHistory, ShowPrintHistory,
                "Cards already printed: who, which side, when (Ctrl+H)");

            _brand = new BrandBlock("Card Studio", "ID Card Printer", logo);

            var items = new List<Control>
            {
                Spacer(14),
                dashboard,
                Nav("Import Students…", Theme.IcoDownload, ShowImportPage,
                    "Load the student list from Excel, CSV, Google Sheets, the web API or SQL Server, or save an Excel template (Ctrl+L)"),
                Nav("CSV Files…", Theme.IcoDocument, ShowCsvFiles,
                    "Student CSV files in one folder: check, preview, create, rename, delete or load them (Ctrl+Shift+C)"),
                _navGallery,
                _navHistory,
                Nav("RFID Cards", Theme.IcoList, ShowRfidList,
                    "Card numbers saved for printed cards, and loaded students who have no card yet (Ctrl+Shift+R)"),
                Nav("Print Queue", Theme.IcoPrint, ShowPrintQueue, "See or cancel jobs waiting in Windows"),

                Section("CARD"),
                Nav("Photo Folder…", Theme.IcoFolder, ShowPhotoFolderPage,
                    "Choose or open the folder with photos named {StudentId}.jpg / .png, " +
                    "or download photos into it from Google Drive (Ctrl+Shift+D) or OneDrive (Ctrl+Shift+O)"),
                Nav("Remove Background…", Theme.IcoBrush, RemovePhotoBackground,
                    "Crop the selected student's photo and remove the wall / screen behind it (Ctrl+B)"),
                Nav("Card Template…", Theme.IcoTemplate, ChooseCardTemplate,
                    "Choose the card design (Classic, Modern, Minimal, Ribbon or your Custom design) with a live preview of front and back"),
                Nav("Card Designer…", Theme.IcoEdit, ShowCardDesigner,
                    "Design your own card: place text, student fields, photo, QR codes, images and shapes on the front and back"),
                Nav("Card Text…", Theme.IcoFont, EditCardText,
                    "Edit the fixed text printed on the cards and choose the director signature"),

                Section("APP"),
                Nav("Full Screen", Theme.IcoFullScreen, () => ShowFullScreen(PreviewSide.Both), "Full-screen card preview (F11)"),
                Nav("Settings & Help…", Theme.IcoSettings, ShowSettingsHub,
                    Lang.F("{0}, Theme Color, Language (Ctrl+Shift+L), Folders (Ctrl+Shift+F), All Settings (Ctrl+,), " +
                    "Epson Driver Setup (Ctrl+Shift+E), System Info (Ctrl+Shift+I), Check for Updates (Ctrl+U) and Help & About (F1) " +
                    "in one page", Lang.T(Theme.Dark ? "Light Mode" : "Dark Mode"))),

                Spacer(10)
            };

            // Dock = Top stacks the LAST added control on top, so add bottom-up.
            for (int i = items.Count - 1; i >= 0; i--)
            {
                items[i].Dock = DockStyle.Top;
                scroll.Controls.Add(items[i]);
            }

            _navToggle = new NavItem("Collapse sidebar", Theme.IcoPaneClose, (s, e) => ToggleSidebar())
            {
                Dock = DockStyle.Bottom, Height = 44
            };
            var divider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Surface };
            divider.Paint += (s, e) =>
            {
                int inset = _sidebarCompact ? 14 : 20;
                using (var pen = new Pen(GridLine)) e.Graphics.DrawLine(pen, inset, 0, divider.Width - inset, 0);
            };

            // Fill first, edges last: the brand stays pinned at the top and the toggle at the bottom,
            // only the navigation between them scrolls.
            _brand.Dock = DockStyle.Top;
            side.Controls.Add(scroll);
            side.Controls.Add(_brand);
            side.Controls.Add(divider);
            side.Controls.Add(_navToggle);
            return side;
        }

        private void ToggleSidebar()
        {
            SetSidebarCompact(!_sidebarCompact, true);
        }

        /// <summary>
        /// Icons only (compact) or icons with names (full). In compact mode each icon's tooltip starts with its name,
        /// and section titles become thin dividers.
        /// </summary>
        private void SetSidebarCompact(bool compact, bool save)
        {
            _sidebarCompact = compact;
            SuspendLayout();
            _sidebar.SuspendLayout();

            _sidebar.Width = compact ? SidebarCompactWidth : SidebarFullWidth;
            _brand.Compact = compact;
            _tip.SetToolTip(_brand, compact ? Lang.T("Card Studio") + " · " + Lang.T("ID Card Printer") : null);

            foreach (KeyValuePair<NavItem, string> nav in _navTips)
            {
                nav.Key.Compact = compact;
                string tip = Lang.T(nav.Value);
                _tip.SetToolTip(nav.Key, compact ? nav.Key.Text + Environment.NewLine + tip : tip);
            }

            foreach (Label section in _sections)
            {
                section.Text = compact ? "" : (string)section.Tag;   // English: Lang translates it again
                section.Height = compact ? 17 : 32;
            }
            _navToggle.Compact = compact;
            _navToggle.Glyph = compact ? Theme.IcoPaneOpen : Theme.IcoPaneClose;
            _navToggle.Text = compact ? "Expand sidebar" : "Collapse sidebar";
            _tip.SetTip(_navToggle, compact ? "Show icons and names (Ctrl+\\)" : "Show icons only (Ctrl+\\)");

            _sidebar.ResumeLayout(true);
            ResumeLayout(true);
            _sidebar.Invalidate(true);

            if (!save) return;
            _settings.SidebarCollapsed = compact;
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                SetStatus("Could not save settings: " + ex.Message);
            }
        }

        /// <summary>
        /// Header language picker: a 🇬🇧 EN ▾ / 🇰🇭 KH ▾ button that drops down a menu of both languages, the one in
        /// use ticked. Picking the other one asks, saves it and restarts the app (the language of every window).
        /// </summary>
        private Control BuildLanguageSwitch()
        {
            var button = new LanguageButton { Margin = new Padding(2, 6, 0, 0) };
            _tip.SetToolTip(button, "English · ភាសាខ្មែរ  (Ctrl+Shift+L)");

            ContextMenuStrip menu = null;
            Image gbFlag = FlagArt.Create("gb"), khFlag = FlagArt.Create("kh");
            DateTime closedAt = DateTime.MinValue;
            button.Click += (s, e) =>
            {
                // The mouse-down that closed the menu also clicks the button: let that click just close it.
                if ((DateTime.Now - closedAt).TotalMilliseconds < 250) return;

                if (menu != null) menu.Dispose();
                menu = ThemedMenu.Create();
                menu.ShowImageMargin = true;
                menu.Items.Add(ThemedMenu.Header(Lang.T("LANGUAGE")));
                menu.Items.Add(LanguageItem(menu, gbFlag, "English", false));
                menu.Items.Add(LanguageItem(menu, khFlag, "ភាសាខ្មែរ  (Khmer)", true));
                menu.Closed += (s2, e2) =>
                {
                    button.Open = false;
                    if (e2.CloseReason == ToolStripDropDownCloseReason.AppClicked) closedAt = DateTime.Now;
                };
                button.Open = true;
                menu.Show(button, new Point(button.Width, button.Height + 4), ToolStripDropDownDirection.BelowLeft);
                if (button.OpenedWithKeyboard) menu.Items[Lang.Khmer ? 2 : 1].Select();   // start on the language in use
            };
            button.Disposed += (s, e) =>
            {
                if (menu != null) menu.Dispose();
                gbFlag.Dispose();
                khFlag.Dispose();
            };
            return button;
        }

        /// <summary>One language of the header menu: flag, native name, bold with a ✓ when it is the language in use.</summary>
        private ToolStripMenuItem LanguageItem(ContextMenuStrip menu, Image flag, string name, bool khmer)
        {
            bool current = khmer == Lang.Khmer;
            ToolStripMenuItem item = ThemedMenu.Item(name, () => { if (!current) SwitchLanguage(khmer); });
            item.Image = flag;
            item.ImageScaling = ToolStripItemImageScaling.None;
            item.Padding = new Padding(0, 3, 0, 3);
            if (current)
            {
                var bold = new Font(menu.Font, FontStyle.Bold);
                menu.Disposed += (s, e) => bold.Dispose();
                item.Font = bold;
                item.ShortcutKeyDisplayString = "✓";
            }
            return item;
        }

        private NavItem Nav(string text, string glyph, Action action, string tooltip)
        {
            var item = new NavItem(text, glyph, (s, e) => action());
            _tip.SetTip(item, tooltip);
            _navTips.Add(new KeyValuePair<NavItem, string>(item, tooltip));
            return item;
        }

        /// <summary>Small-caps group title; a thin divider while the sidebar shows icons only.</summary>
        private Label Section(string text)
        {
            var label = new Label
            {
                Text = text, Tag = text, Font = Theme.SmallCaps, ForeColor = TextMuted, BackColor = Surface, AutoSize = false,
                Height = 32, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(22, 0, 0, 5)
            };
            label.Paint += (s, e) =>
            {
                if (!_sidebarCompact) return;
                int y = label.Height / 2;
                using (var pen = new Pen(GridLine)) e.Graphics.DrawLine(pen, 14, y, label.Width - 14, y);
            };
            _sections.Add(label);
            return label;
        }

        private static Control Spacer(int height)
        {
            return new Panel { Height = height, BackColor = Surface };
        }

        /// <summary>Content area: greeting row, stat tiles, students + preview/printer cards, status line.</summary>
        private Control BuildDashboard()
        {
            var grid = _dashboard = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent, Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 66f));    // greeting + print actions
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 126f));   // stat tiles
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));    // students | preview + printer
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));    // status line
            grid.Controls.Add(BuildHeader(), 0, 0);
            grid.Controls.Add(BuildStats(), 0, 1);
            grid.Controls.Add(BuildBody(), 0, 2);
            grid.Controls.Add(BuildStatusBar(), 0, 3);
            return grid;
        }

        /// <summary>"Good afternoon, …" on the left; print side pills and the print buttons on the right.</summary>
        private Control BuildHeader()
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent,
                Padding = new Padding(Theme.ShadowPad, 0, Theme.ShadowPad, 0), Margin = new Padding(0)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var titles = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false,
                Anchor = AnchorStyles.Left, BackColor = Color.Transparent, Margin = new Padding(0)
            };
            titles.Controls.Add(new Label
            {
                Text = Greeting(), Font = Theme.Greeting, ForeColor = TextMain, AutoSize = true,
                BackColor = Color.Transparent, Margin = new Padding(0)
            });
            titles.Controls.Add(new Label
            {
                Text = "Pick students, check the card, then print.", Font = Theme.Small,
                ForeColor = TextMuted, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(3, 0, 0, 0)
            });

            _sideTabs = new PillTabs("Front", "Back") { Margin = new Padding(0, 6, 4, 0) };
            _tip.SetTip(_sideTabs, "Side of the card that Print Selected sends to the printer");
            _sideTabs.SelectedIndexChanged += (s, e) => UpdateSideHighlight();

            var actions = new FlowLayoutPanel
            {
                AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, BackColor = Color.Transparent,
                Margin = new Padding(0)
            };

            _bell = new NotificationBell { Margin = new Padding(0, 5, 6, 0) };
            _bell.Click += (s, e) => ShowNotifications();
            _tip.SetTip(_bell, "Notifications: warnings, errors and finished jobs (Ctrl+Shift+N)");
            _notices.Changed += (s, e) =>
            {
                _bell.UnreadCount = _notices.UnreadCount;
                _bell.Alert = _notices.HasUnreadAlert;
            };
            _notices.Added += n => { if (n.Kind >= NoticeKind.Warning) _bell.Ring(); };
            actions.Controls.Add(_bell);
            actions.Controls.Add(BuildLanguageSwitch());
            actions.Controls.Add(new Panel { Width = 1, Height = 26, BackColor = Border, Margin = new Padding(6, 12, 10, 0) });

            actions.Controls.Add(new Label
            {
                Text = "Print side", Font = Theme.Small, ForeColor = TextMuted, AutoSize = true,
                BackColor = Color.Transparent, Margin = new Padding(0, 14, 6, 0)
            });
            actions.Controls.Add(_sideTabs);
            actions.Controls.Add(new Panel { Width = 1, Height = 26, BackColor = Border, Margin = new Padding(8, 9, 6, 0) });
            actions.Controls.Add(MakeButton("Print Selected", PillKind.Primary, (s, e) => PrintSelected(),
                "Print the selected students, tray load by tray load (Ctrl+P)"));

            header.Controls.Add(titles, 0, 0);
            header.Controls.Add(actions, 1, 0);
            return header;
        }

        private static string Greeting()
        {
            int hour = DateTime.Now.Hour;
            string part = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
            string user = Environment.UserName ?? "";
            if (user.Length == 0) return part;
            return part + ", " + char.ToUpperInvariant(user[0]) + user.Substring(1);
        }

        /// <summary>Three gradient tiles: students loaded, selected for printing, printed today.</summary>
        private Control BuildStats()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0)
            };
            for (int i = 0; i < 3; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _statStudents = new StatCard("Total students", Theme.IcoPeople, Theme.VioletA, Theme.VioletB) { Caption = "No list loaded" };
            _statSelected = new StatCard("Selected to print", Theme.IcoMultiSelect, Theme.CoralA, Theme.CoralB) { Caption = "Select rows in the list" };
            _statPrinted = new StatCard("Printed today", Theme.IcoPrint, Theme.PinkA, Theme.PinkB);

            _statStudents.Cursor = Cursors.Hand;
            _statStudents.Click += (s, e) => ShowGallery();
            _tip.SetTip(_statStudents, "Open the student gallery (Ctrl+G)");
            _statPrinted.Cursor = Cursors.Hand;
            _statPrinted.Click += (s, e) => ShowPrintHistory();
            _tip.SetTip(_statPrinted, "Open the print history (Ctrl+H)");

            row.Controls.Add(_statStudents, 0, 0);
            row.Controls.Add(_statSelected, 1, 0);
            row.Controls.Add(_statPrinted, 2, 0);
            return row;
        }

        private Control BuildBody()
        {
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var side = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(0)
            };
            side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            side.RowStyles.Add(new RowStyle(SizeType.Absolute, 262f));
            side.Controls.Add(BuildPreviewCard(), 0, 0);
            side.Controls.Add(BuildSetupCard(), 0, 1);

            body.Controls.Add(BuildStudentPanel(), 0, 0);
            body.Controls.Add(side, 1, 0);
            return body;
        }

        /// <summary>Students card: search field + student list; "View List" in the header opens the student gallery.</summary>
        private Control BuildStudentPanel()
        {
            _lblCount = new Label { AutoSize = true, ForeColor = TextMuted, Font = Theme.Small, BackColor = Surface, Margin = new Padding(0, 13, 2, 0) };

            var search = new SearchField(UiFont) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            _txtSearch = search.Box;
            _txtSearch.HandleCreated += (s, e) => SetCueBanner(_txtSearch, "Search by ID, name or faculty…   (Ctrl+F)");
            _txtSearch.TextChanged += (s, e) => ApplyFilter();
            _txtSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    // First Esc clears the search, the next one shows every student again after a Gallery pick
                    if (_txtSearch.TextLength > 0) _txtSearch.Clear();
                    else ClearGalleryPick();
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Down && _grid.Rows.Count > 0) { _grid.Focus(); e.SuppressKeyPress = true; }
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                BackgroundColor = Surface,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = GridLine,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 36
            };
            _grid.RowTemplate.Height = 36;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Surface;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface;
            _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.Font = Theme.UiFont(8.25f, true);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _grid.DefaultCellStyle.Font = new Font(CardFonts.UI, 9f);
            _grid.DefaultCellStyle.BackColor = Surface;
            _grid.DefaultCellStyle.ForeColor = TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            _grid.DefaultCellStyle.SelectionForeColor = Accent;
            _grid.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            _grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceAlt;
            _grid.DataBindingComplete += (s, e) => ApplyColumnTitles();
            _grid.SelectionChanged += (s, e) => { UpdatePreview(); UpdateSlotHint(); UpdateCount(); UpdateRfidPanel(); };
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ShowFullScreen(PreviewSide.Both); };

            _lblEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted,
                BackColor = Surface, Visible = false
            };

            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(0) };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(_lblEmpty);
            _lblEmpty.BringToFront();

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // Gallery pick bar (hidden = no height)
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            body.Controls.Add(search, 0, 0);
            body.Controls.Add(BuildPickBar(), 0, 1);
            body.Controls.Add(gridHost, 0, 2);

            return MakeCard("Students", body, _lblCount,
                MakeButton("Import…", PillKind.Ghost, (s, e) => ShowImportPage(),
                    "Load the student list from Excel, CSV, the web API or SQL Server (Ctrl+L)"),
                MakeButton("View List", PillKind.Ghost, (s, e) => ShowGallery(),
                    "View every loaded student with photo, details and print status (Ctrl+G)"));
        }

        /// <summary>
        /// Soft accent strip above the student list while it shows only the students chosen in the Gallery:
        /// "Showing 3 students chosen in the Gallery" + Show all.
        /// </summary>
        private Control BuildPickBar()
        {
            var bar = new Panel
            {
                Dock = DockStyle.Fill, Height = 38, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(36, 3, 10, 3),
                BackColor = Surface, Visible = false
            };
            // Label + button sit on the accent fill, inside the rounded edge (the button paints its parent's colour)
            var inner = new Panel { Dock = DockStyle.Fill, BackColor = AccentSoft, Margin = new Padding(0) };
            bar.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                Theme.HighQuality(g);
                var r = new RectangleF(0.5f, 0.5f, bar.Width - 1.5f, bar.Height - 1.5f);
                using (System.Drawing.Drawing2D.GraphicsPath p = Theme.RoundRect(r, 10))
                {
                    using (var b = new SolidBrush(AccentSoft)) g.FillPath(b, p);
                    using (var pen = new Pen(Theme.AccentOutline)) g.DrawPath(pen, p);
                }
                Theme.DrawIcon(g, Theme.IcoPhoto, 15f, Accent, new RectangleF(8, 0, 24, bar.Height));
            };

            _lblPicked = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Accent, Font = Theme.UiFont(9f, true),
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft
            };
            Button showAll = MakeButton("Show all", PillKind.Ghost, (s, e) => ClearGalleryPick(),
                "Show every student in the list again (Esc in the search box)");
            showAll.Dock = DockStyle.Right;
            showAll.Margin = new Padding(0);

            inner.Controls.Add(_lblPicked);   // fill first, docked edge last (docking runs in reverse add order)
            inner.Controls.Add(showAll);
            bar.Controls.Add(inner);
            _pickBar = bar;
            return bar;
        }

        /// <summary>Front and back thumbnails side by side; the side that will print gets a violet outline.</summary>
        private Control BuildPreviewCard()
        {
            _picFront = new PreviewTile { Margin = new Padding(0, 0, 6, 0) };
            _picBack = new PreviewTile { Margin = new Padding(6, 0, 0, 0), EmptyText = "Back side" };
            _picFront.DoubleClick += (s, e) => ShowFullScreen(PreviewSide.Front);
            _picBack.DoubleClick += (s, e) => ShowFullScreen(PreviewSide.Back);
            _picFront.Click += (s, e) => _sideTabs.SelectedIndex = 0;
            _picBack.Click += (s, e) => _sideTabs.SelectedIndex = 1;
            _tip.SetTip(_picFront, "Click to print this side · double-click to enlarge");
            _tip.SetTip(_picBack, "Click to print this side · double-click to enlarge");

            var tiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            tiles.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            tiles.Controls.Add(_picFront, 0, 0);
            tiles.Controls.Add(_picBack, 1, 0);
            tiles.Controls.Add(TileCaption("Front", "ព័ត៌មាននិស្សិត"), 0, 1);
            tiles.Controls.Add(TileCaption("Back", "ព័ត៌មានវិទ្យាស្ថាន"), 1, 1);

            _btnGrid = (PillButton)MakeButton("Grid", PillKind.Ghost, (s, e) => SetPreviewGrid(!_showGrid),
                "Show / hide a millimetre grid on the front and back previews (not printed)");

            return MakeCard("Card preview", tiles, _btnGrid,
                MakeButton("3D View", PillKind.Ghost, (s, e) => Show3DPreview(),
                    "Turn the card in 3D: drag to rotate it, flip between front and back (Shift+F11)"),
                MakeButton("Enlarge", PillKind.Ghost, (s, e) => ShowFullScreen(PreviewSide.Both), "Full-screen card preview (F11)"));
        }

        /// <summary>Turns the mm grid on or off for both preview tiles (and the full-screen preview).</summary>
        private void SetPreviewGrid(bool show)
        {
            _showGrid = show;
            _picFront.ShowGrid = show;
            _picBack.ShowGrid = show;
            _btnGrid.Checked = show;
            _btnGrid.Text = show ? "Grid ✓" : "Grid";
        }

        private static Control TileCaption(string side, string khmer)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(2, 0, 0, 0), Padding = new Padding(0) };
            row.Controls.Add(new Label { Text = side, AutoSize = true, Font = Theme.NavBold, ForeColor = TextMain, Margin = new Padding(0, 6, 4, 0) });
            row.Controls.Add(KhmerCaption(khmer));
            return row;
        }

        /// <summary>Printer and tray settings share one card; pills switch between the two pages.</summary>
        private Control BuildSetupCard()
        {
            Control printerPage = BuildPrinterPage();
            Control trayPage = BuildTrayPage();
            Control rfidPage = BuildRfidPage();
            printerPage.Dock = DockStyle.Fill;
            trayPage.Dock = DockStyle.Fill;
            rfidPage.Dock = DockStyle.Fill;
            trayPage.Visible = false;
            rfidPage.Visible = false;

            var pages = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(0) };
            pages.Controls.Add(printerPage);
            pages.Controls.Add(trayPage);
            pages.Controls.Add(rfidPage);

            var tabs = _setupTabs = new PillTabs("Printer", "Tray", "RFID") { Margin = new Padding(0, 4, 0, 0) };
            tabs.SelectedIndexChanged += (s, e) =>
            {
                printerPage.Visible = tabs.SelectedIndex == 0;
                trayPage.Visible = tabs.SelectedIndex == 1;
                rfidPage.Visible = tabs.SelectedIndex == 2;
                if (rfidPage.Visible) { UpdateRfidPanel(); _txtRfid.Focus(); }
            };
            _tip.SetTip(tabs, "Printer = driver and checks · Tray = slot positions and card orientation · " +
                                  "RFID = save the printed card's chip number to the RFID CSV file");

            return MakeCard("Print setup", pages, tabs);
        }

        private Control BuildPrinterPage()
        {
            _cboPrinter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 8, 4) };
            _cboPrinter.SelectedIndexChanged += (s, e) => OnPrinterChanged();

            _chkAllPrinters = new CheckBox { Text = "Show all", AutoSize = true, ForeColor = TextMuted, Margin = new Padding(0, 6, 0, 0) };
            _tip.SetTip(_chkAllPrinters, "List every installed printer, not only card printers");
            _chkAllPrinters.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                _settings.ShowAllPrinters = _chkAllPrinters.Checked;
                LoadPrinters();
                SaveSettings(false);
                UpdatePageInfo();
            };

            var printerRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            printerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            printerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            printerRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            printerRow.Controls.Add(_cboPrinter, 0, 0);
            printerRow.Controls.Add(_chkAllPrinters, 1, 0);

            _lblPage = new Label
            {
                Dock = DockStyle.Fill, ForeColor = TextMuted, Font = CaptionFont, AutoEllipsis = true,
                Margin = new Padding(1, 4, 0, 4)
            };

            var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            buttons.Controls.Add(FillButton(MakeButton("Driver Setup…", PillKind.Secondary, (s, e) => DriverSetup(), "Epson printers: paper source, paper type and quality (Ctrl+Shift+E)")), 0, 0);
            buttons.Controls.Add(FillButton(MakeButton("Check Printer", PillKind.Secondary, (s, e) => CheckPrinter(), "Show the page setup and any problems found")), 1, 0);
            buttons.Controls.Add(FillButton(MakeButton("Test Print", PillKind.Secondary, (s, e) => TestPrint(), "Print a calibration card to tune slot X / Y")), 0, 1);
            buttons.Controls.Add(FillButton(MakeButton("Print Queue…", PillKind.Secondary, (s, e) => ShowPrintQueue(), "See or cancel jobs waiting in Windows")), 1, 1);

            var body = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Margin = new Padding(0), BackColor = Surface, Font = SetupFont };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
            body.Controls.Add(printerRow, 0, 0);
            body.Controls.Add(_lblPage, 0, 1);
            body.Controls.Add(buttons, 0, 2);
            return body;
        }

        //  Tray page (fits the 174 px card body, no scrolling; the hint + Save strip is docked under the grid):
        //  ┌──────────────────────────────────────────┬───────────┐
        //  │               X (mm)     Y (mm)          │ ┌───────┐ │
        //  │ Slot 1        [  0.0 ]   [  0.0 ]        │ │ 1     │ │  mini map of the page:
        //  │ Slot 2        [  0.0 ]   [ 60.0 ]        │ │   2   │ │  where each slot lands,
        //  │ Per tray load [Auto| 1 | 2 ]             │ │       │ │  live as the values change
        //  │ Card on tray  [ ▭ | ▯ | ▯ ]              │ │       │ │
        //  │ Rotate 180°   ☐ Front  ☐ Back            │ └───────┘ │
        //  ├──────────────────────────────────────────┴───────────┤
        //  │ 5 card(s) → 3 tray load(s), 2 per load        [Save] │
        //  └──────────────────────────────────────────────────────┘
        private Control BuildTrayPage()
        {
            const float rowH = 26f;
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 6, Margin = new Padding(0), Padding = new Padding(0), BackColor = Surface,
                Font = SetupFont   // Latin-only page: Segoe UI keeps the spin boxes compact
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // field names
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // X / Front
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));          // Y / Back
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));     // mini map
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 14f));           // X / Y column heads
            for (int i = 0; i < 5; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, rowH));

            grid.Controls.Add(ColumnHead("X (mm)"), 1, 0);
            grid.Controls.Add(ColumnHead("Y (mm)"), 2, 0);

            _numS1X = TrayNumber(); _numS1Y = TrayNumber();
            _numS2X = TrayNumber(); _numS2Y = TrayNumber();
            foreach (NumericUpDown n in new[] { _numS1X, _numS1Y, _numS2X, _numS2Y })
                n.ValueChanged += (s, e) => UpdateSlotHint();
            _tip.SetTip(_numS1X, "Slot 1 X: distance from the left edge of the page (mm)");
            _tip.SetTip(_numS1Y, "Slot 1 Y: distance from the top edge of the page (mm)");
            _tip.SetTip(_numS2X, "Slot 2 X: distance from the left edge of the page (mm)");
            _tip.SetTip(_numS2Y, "Slot 2 Y: distance from the top edge of the page (mm)");
            grid.Controls.Add(TrayLabel("Slot 1"), 0, 1);
            grid.Controls.Add(_numS1X, 1, 1);
            grid.Controls.Add(_numS1Y, 2, 1);
            grid.Controls.Add(TrayLabel("Slot 2"), 0, 2);
            grid.Controls.Add(_numS2X, 1, 2);
            grid.Controls.Add(_numS2Y, 2, 2);

            // Order must match the SlotMode enum: Auto, One, Two
            _segSlots = new SegmentPicker(new[] { "Auto", "1", "2" }) { Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            _segSlots.SelectedIndexChanged += (s, e) => UpdateSlotHint();
            _tip.SetTip(_segSlots,
                "Auto = 2 cards per tray load when 2 or more students are selected and slot 2 is set up; " +
                "otherwise slot 1 only.\n1 = always slot 1 only.   2 = always slot 1 + slot 2.");
            grid.Controls.Add(TrayLabel("Per tray load"), 0, 3);
            grid.Controls.Add(_segSlots, 1, 3);
            grid.SetColumnSpan(_segSlots, 2);

            // Order must match the CardOrientation enum: Horizontal, VerticalRight, VerticalLeft
            _segOrientation = new SegmentPicker(
                new[] { "Horizontal", "Vertical (turn right 90°)", "Vertical (turn left 90°)" }, 38)
            {
                Anchor = AnchorStyles.Left, Margin = new Padding(0), DrawGlyph = DrawOrientationGlyph
            };
            _segOrientation.SelectedIndexChanged += (s, e) =>
            {
                if (_segOrientation.SelectedIndex < 0) return;
                _settings.CardOrientation = (CardOrientation)_segOrientation.SelectedIndex;
                // Tip = the chosen option's name, then how to choose (both already in the app's language)
                _tip.SetToolTip(_segOrientation, _segOrientation.SelectedText + "\n\n" + Lang.T(
                    "How the card lies in the tray pocket.\n" +
                    "Vertical = the long side of the card runs in the feed direction (54 x 85.6 mm on the page).\n" +
                    "If the card comes out upside down, pick the other Vertical option."));
                UpdateSlotHint();
            };
            grid.Controls.Add(TrayLabel("Card on tray"), 0, 4);
            grid.Controls.Add(_segOrientation, 1, 4);
            grid.SetColumnSpan(_segOrientation, 2);

            _chkRotateFront = new CheckBox { Text = "Front", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(1, 0, 6, 0) };
            _chkRotateFront.CheckedChanged += (s, e) => { _settings.RotateFront180 = _chkRotateFront.Checked; };
            _chkRotateBack = new CheckBox { Text = "Back", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(1, 0, 6, 0) };
            _chkRotateBack.CheckedChanged += (s, e) => { _settings.RotateBack180 = _chkRotateBack.Checked; };
            _tip.SetTip(_chkRotateFront, "Rotate the front side 180° when printing");
            _tip.SetTip(_chkRotateBack, "Rotate the back side 180° when printing");
            grid.Controls.Add(TrayLabel("Rotate 180°"), 0, 5);
            grid.Controls.Add(_chkRotateFront, 1, 5);
            grid.Controls.Add(_chkRotateBack, 2, 5);

            _trayMap = new TrayMap { Dock = DockStyle.Fill, Margin = new Padding(12, 2, 0, 2) };
            _trayMap.SlotClicked += slot =>
            {
                NumericUpDown box = slot == 1 ? _numS1X : _numS2X;
                box.Focus();
                box.Select(0, box.Text.Length);
            };
            _tip.SetTip(_trayMap, "Where slot 1 and slot 2 land on the printer page. Click a slot to edit its X / Y.");
            grid.Controls.Add(_trayMap, 3, 0);
            grid.SetRowSpan(_trayMap, 6);

            _lblSlotHint = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = TextMuted, Font = Theme.Small,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(1, 0, 0, 0)
            };
            Button save = MakeButton("Save", PillKind.Ghost, (s, e) => SaveSettings(true), "Save slot positions and print options");
            save.Anchor = AnchorStyles.Right;
            save.Margin = new Padding(6, 0, 0, 0);
            // Docked under the grid, not a grid row: a row spanning the auto-size columns would widen them and
            // squeeze the map.
            var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 30, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = Surface };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            footer.Controls.Add(_lblSlotHint, 0, 0);
            footer.Controls.Add(save, 1, 0);

            var page = new Panel { Margin = new Padding(0), BackColor = Surface, Font = SetupFont };
            page.Controls.Add(grid);     // fill first, docked edge last (docking runs in reverse add order)
            page.Controls.Add(footer);
            return page;
        }

        private static Label TrayLabel(string text)
        {
            return new Label
            {
                Text = text, AutoSize = true, ForeColor = TextMuted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0)
            };
        }

        /// <summary>Unit heading over a column of spin boxes ("X (mm)"): the same in both languages.</summary>
        private static Label ColumnHead(string text)
        {
            return Lang.Keep(new Label
            {
                Text = text, AutoSize = true, ForeColor = TextMuted, Font = Theme.Small,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = new Padding(1, 0, 0, 0)
            });
        }

        private static NumericUpDown TrayNumber()
        {
            NumericUpDown n = MakeNumber();
            n.Width = 68;
            n.Anchor = AnchorStyles.Left;
            n.Margin = new Padding(0, 0, 8, 0);
            return n;
        }

        /// <summary>
        /// Card-on-tray pictograms: a card with a header stripe along the card's top edge, turned the way the option turns
        /// the printed image (Horizontal = stripe on top; turn right 90° = stripe on the right; turn left 90° = on the left).
        /// </summary>
        private static void DrawOrientationGlyph(Graphics g, int index, RectangleF bounds, Color ink)
        {
            bool vertical = index != 0;
            float w = vertical ? 11f : 17f, h = vertical ? 17f : 11f;
            var card = new RectangleF(bounds.X + (bounds.Width - w) / 2f, bounds.Y + (bounds.Height - h) / 2f, w, h);
            RectangleF stripe =
                index == 1 ? new RectangleF(card.Right - 4f, card.Top, 4f, card.Height) :
                index == 2 ? new RectangleF(card.Left, card.Top, 4f, card.Height) :
                             new RectangleF(card.Left, card.Top, card.Width, 4f);
            using (System.Drawing.Drawing2D.GraphicsPath p = Theme.RoundRect(card, 2f))
            {
                System.Drawing.Drawing2D.GraphicsState st = g.Save();
                g.SetClip(p);
                using (var b = new SolidBrush(ink)) g.FillRectangle(b, stripe);
                g.Restore(st);
                using (var pen = new Pen(ink, 1.3f)) g.DrawPath(pen, p);
            }
        }

        /// <summary>
        /// RFID tab: after a card is printed, tap it on the USB reader (or type its number) and Upload saves
        /// the chip UID for the selected student in the RFID CSV file (<see cref="RfidCardWriter.FilePath"/>).
        /// </summary>
        private Control BuildRfidPage()
        {
            _lblRfidStudent = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, Font = SemiFont, ForeColor = TextMain,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0)
            };
            _lblRfidPrinted = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, Font = CaptionFont, ForeColor = TextMuted,
                TextAlign = ContentAlignment.TopLeft, Margin = new Padding(1, 0, 0, 0)
            };

            _txtRfid = new TextBox
            {
                Dock = DockStyle.Fill, Font = new Font("Consolas", 12f), CharacterCasing = CharacterCasing.Upper,
                MaxLength = 80, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 8, 4)
            };
            _txtRfid.HandleCreated += (s, e) => SetCueBanner(_txtRfid, "Tap the card on the reader…");
            _txtRfid.TextChanged += (s, e) => UpdateRfidButton();
            _txtRfid.KeyDown += (s, e) =>
            {
                // USB RFID readers type the card number followed by Enter.
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UploadRfid(); }
                else if (e.KeyCode == Keys.Escape) { _txtRfid.Clear(); e.SuppressKeyPress = true; }
            };
            _tip.SetTip(_txtRfid, "RFID card number (UID). A USB reader types it here and presses Enter, which uploads it.");

            _btnRfidUpload = MakeButton("Upload", PillKind.Primary, (s, e) => UploadRfid(),
                "Save this RFID card number for the selected student in the RFID CSV file (Enter)");
            _btnRfidUpload.MinimumSize = new Size(110, 36);
            _btnRfidUpload.Margin = new Padding(0, 2, 0, 2);
            _btnRfidUpload.Anchor = AnchorStyles.Right;

            var inputRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            inputRow.Controls.Add(_txtRfid, 0, 0);
            inputRow.Controls.Add(_btnRfidUpload, 1, 0);

            _lblRfidResult = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = TextMuted, Font = Theme.Small,
                TextAlign = ContentAlignment.TopLeft, Margin = new Padding(1, 4, 0, 0)
            };

            _lblRfidTarget = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = TextMuted, Font = CaptionFont,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(1, 0, 0, 0)
            };
            var targetRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            targetRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            targetRow.Controls.Add(_lblRfidTarget, 0, 0);
            targetRow.Controls.Add(MakeButton("List…", PillKind.Ghost, (s, e) => ShowRfidList(),
                "Every saved card number, and loaded students who have no card yet (Ctrl+Shift+R)"), 1, 0);
            targetRow.Controls.Add(MakeButton("Change…", PillKind.Ghost, (s, e) => ChangeRfidFile(),
                "Choose where the RFID CSV file is kept, e.g. a shared network folder used by several PCs"), 2, 0);

            var body = new TableLayoutPanel { ColumnCount = 1, RowCount = 5, Margin = new Padding(0), BackColor = Surface, Font = SetupFont };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));    // selected student
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));    // printed? / saved card
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));    // textbox + Upload
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));    // last result
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));    // RFID file + Open / Change…
            body.Controls.Add(_lblRfidStudent, 0, 0);
            body.Controls.Add(_lblRfidPrinted, 0, 1);
            body.Controls.Add(inputRow, 0, 2);
            body.Controls.Add(_lblRfidResult, 0, 3);
            body.Controls.Add(targetRow, 0, 4);
            return body;
        }

        private Control BuildStatusBar()
        {
            var bar = new Panel
            {
                Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0),
                Padding = new Padding(Theme.ShadowPad + 2, 0, Theme.ShadowPad, 0)
            };
            _lblStatus = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = TextMuted, Text = "Ready.",
                AutoEllipsis = true, BackColor = Color.Transparent, Font = Theme.Small
            };
            _lblStatusDot = new Label
            {
                Dock = DockStyle.Left, Width = 16, Text = "●", ForeColor = Success, TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.Small, BackColor = Color.Transparent
            };
            bar.Controls.Add(_lblStatus);
            bar.Controls.Add(_lblStatusDot);
            return bar;
        }

        // ---------- Building blocks ----------
        /// <summary>Rounded white card with soft shadow, ring-bullet title and optional header extras on the right.</summary>
        private static Control MakeCard(string title, Control body, params Control[] headerExtras)
        {
            var card = new ShadowCard(new Padding(20, 6, 20, 14));
            var head = new CardHeader(title, headerExtras);

            var bodyHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(0, 4, 0, 0) };
            body.Dock = DockStyle.Fill;
            bodyHost.Controls.Add(body);

            card.Controls.Add(bodyHost);
            card.Controls.Add(head);
            return card;
        }

        private Button MakeButton(string text, PillKind kind, EventHandler onClick, string tooltip)
        {
            Button b = MakeButton(text, kind, onClick);
            _tip.SetTip(b, tooltip);
            return b;
        }

        private static Button MakeButton(string text, PillKind kind, EventHandler onClick)
        {
            var b = new PillButton(kind)
            {
                Text = text, AutoSize = true, MinimumSize = new Size(0, 36), Padding = new Padding(12, 0, 12, 0),
                Margin = new Padding(6, 3, 0, 3)
            };
            switch (kind)
            {
                case PillKind.Primary:
                    b.MinimumSize = new Size(136, 38);
                    b.Margin = new Padding(8, 2, 0, 2);
                    break;
                case PillKind.Ghost:
                    b.MinimumSize = new Size(0, 30);
                    b.Padding = new Padding(8, 0, 8, 0);
                    b.Margin = new Padding(2, 4, 0, 2);
                    break;
            }
            if (onClick != null) b.Click += onClick;
            return b;
        }

        private static Button FillButton(Button b)
        {
            b.AutoSize = false;
            b.MinimumSize = Size.Empty;
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(0, 3, 8, 3);
            return b;
        }

        private static Label FieldLabel(string text)
        {
            return new Label
            {
                Text = text, AutoSize = false, Width = FieldLabelWidth, Height = 30, ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 4, 0)
            };
        }

        private static Label Unit(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = TextMuted, Font = CaptionFont, Margin = new Padding(0, 8, 4, 0) };
        }

        private static Label KhmerCaption(string text)
        {
            return new Label
            {
                Text = text, AutoSize = true, ForeColor = TextMuted, Font = new Font(CardFonts.UI, 8.25f),
                Margin = new Padding(0, 6, 8, 0)
            };
        }

        private static NumericUpDown MakeNumber()
        {
            return new NumericUpDown
            {
                DecimalPlaces = 1, Increment = 0.1M, Minimum = -50M, Maximum = 300M, Width = 72,
                TextAlign = HorizontalAlignment.Right, Margin = new Padding(0, 4, 10, 3), BorderStyle = BorderStyle.FixedSingle
            };
        }

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 2) };
            row.Controls.AddRange(controls);
            return row;
        }

        // ---------- Dashboard figures ----------
        /// <summary>Violet outline on the preview tile of the side that will be printed.</summary>
        private void UpdateSideHighlight()
        {
            _picFront.Highlight = PrintFront;
            _picBack.Highlight = !PrintFront;
            UpdateSlotHint();
        }

        /// <summary>"Printed today" tile + Print History badge, read from the print log.</summary>
        private void UpdatePrintedStat()
        {
            try
            {
                List<PrintRecord> all = PrintHistory.Load();
                int today = all.Count(r => r.PrintedAt.Date == DateTime.Today);
                _statPrinted.Value = today.ToString("N0");
                _statPrinted.Caption = all.Count == 0 ? "Nothing printed yet" : all.Count.ToString("N0") + " card sides printed in total";
                _navHistory.Badge = today > 0 ? today.ToString() : null;

                _lastPrinted = all.Where(r => !string.IsNullOrEmpty(r.StudentId))
                    .GroupBy(r => r.StudentId.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Max(r => r.PrintedAt), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                _statPrinted.Value = "–";
                _statPrinted.Caption = "Print history unavailable";
            }
            UpdateRfidPanel();
        }

        // ---------- Search / list helpers ----------
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void SetCueBanner(TextBox box, string text)
        {
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, Lang.T(text));
        }

        /// <summary>Rebinds the grid to the students that match the search box (and the Gallery pick, while there is one).</summary>
        private void ApplyFilter()
        {
            if (_picked != null && !ReferenceEquals(_pickedFrom, _students)) _picked = null;   // another list was loaded
            if (_pickBar != null) _pickBar.Visible = _picked != null;

            string q = (_txtSearch.Text ?? "").Trim();
            IEnumerable<Student> view = _students;
            if (_picked != null) view = view.Where(s => _picked.Contains(s.StudentId ?? ""));
            if (q.Length > 0) view = view.Where(s => Matches(s, q));
            _view = _picked == null && q.Length == 0 ? _students : view.ToList();
            if (_picked != null)
            {
                int chosen = _students.Count(s => _picked.Contains(s.StudentId ?? ""));
                _lblPicked.Text = Lang.F(chosen == 1 ? "Showing {0} student chosen in the Gallery" : "Showing {0} students chosen in the Gallery",
                    chosen);
            }
            _grid.DataSource = null;
            _grid.DataSource = _view;
            UpdateEmptyState();
            UpdateCount();
            UpdatePreview();
            UpdateSlotHint();
        }

        private static bool Matches(Student s, string q)
        {
            return Has(s.StudentId, q) || Has(s.NameKh, q) || Has(s.NameEn, q) || Has(s.Faculty, q);
        }

        private static bool Has(string value, string q)
        {
            return value != null && value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyColumnTitles()
        {
            foreach (DataGridViewColumn col in _grid.Columns)
            {
                string title;
                if (ColumnTitles.TryGetValue(col.DataPropertyName, out title)) col.HeaderText = Lang.T(title);
            }
        }

        private void UpdateEmptyState()
        {
            if (_lblEmpty == null) return;
            _lblEmpty.Text = _students.Count == 0
                ? "No students loaded yet.\n\nClick  Import Students…  in the sidebar (Ctrl+L) to load them from Excel, CSV, API or SQL Server.\n" +
                  "No list yet? Save an Excel template there to fill in.\n\nNew to Card Studio? Press F1 for the user guide."
                : _picked != null && _txtSearch.Text.Trim().Length == 0
                    ? "None of the students chosen in the Gallery are in this list.\n\nClick  Show all  to see every student."
                    : "No students match “" + _txtSearch.Text.Trim() + "”.\n\nPress Esc to clear the search.";
            _lblEmpty.Visible = _view.Count == 0;
        }

        private void UpdateCount()
        {
            if (_lblCount == null) return;
            int selected = _grid.SelectedRows.Count;
            string total = _view.Count == _students.Count
                ? string.Format("{0} students", _students.Count)
                : string.Format("{0} of {1} students", _view.Count, _students.Count);
            _lblCount.Text = selected > 1 ? total + "  ·  " + selected + " selected" : total;

            _statStudents.Value = _students.Count.ToString("N0");
            _statStudents.Caption = _students.Count == 0 ? "No list loaded"
                : _picked != null ? _view.Count.ToString("N0") + " chosen in the Gallery"
                : _view.Count != _students.Count ? _view.Count.ToString("N0") + " match the search"
                : _source.Length > 0 ? "from " + _source : "in the list";
            _statSelected.Value = selected.ToString("N0");
            _navGallery.Badge = _students.Count > 0 ? _students.Count.ToString("N0") : null;
        }

        // ===================== Settings =====================
        /// <summary>Fills the printer list with card printers only (or every printer when "Show all" is ticked).</summary>
        private void LoadPrinters()
        {
            _loading = true;
            _chkAllPrinters.Checked = _settings.ShowAllPrinters;
            _cboPrinter.Items.Clear();
            foreach (string name in PrinterSettings.InstalledPrinters)
            {
                if (_settings.ShowAllPrinters || _settings.IsCardPrinter(name))
                    _cboPrinter.Items.Add(name);
            }

            string target = _settings.PrinterName;
            if (string.IsNullOrEmpty(target) || !_cboPrinter.Items.Contains(target))
            {
                List<string> names = _cboPrinter.Items.Cast<string>().ToList();
                target = names.FirstOrDefault(_settings.IsCardPrinter) ?? names.FirstOrDefault();
            }
            if (target != null) _cboPrinter.SelectedItem = target;
            _cboPrinter.Enabled = _cboPrinter.Items.Count > 0;
            _loading = false;

            if ((_settings.PrinterName ?? "") != (target ?? ""))
            {
                _settings.PrinterName = target ?? "";
                _settings.DevModeBase64 = ""; // driver settings belonged to another printer
            }

            if (target == null)
                SetStatus("No card printer found (" + _settings.CardPrinterKeywords +
                          "). Install the Epson L8050 driver, or tick \"Show all\".");
        }

        private void OnPrinterChanged()
        {
            if (_loading) return;
            string name = _cboPrinter.SelectedItem as string;
            if (name == null || name == _settings.PrinterName) return;

            _settings.PrinterName = name;
            _settings.DevModeBase64 = "";
            SaveSettings(false);
            UpdatePageInfo();
        }

        private void ApplySettingsToUi()
        {
            _loading = true;   // the controls below must not write back into _settings half-way
            _numS1X.Value = Clamp(_numS1X, _settings.Slot1X);
            _numS1Y.Value = Clamp(_numS1Y, _settings.Slot1Y);
            _numS2X.Value = Clamp(_numS2X, _settings.Slot2X);
            _numS2Y.Value = Clamp(_numS2Y, _settings.Slot2Y);
            _segSlots.SelectedIndex = (int)_settings.SlotMode;
            _chkRotateBack.Checked = _settings.RotateBack180;
            _chkRotateFront.Checked = _settings.RotateFront180;
            _segOrientation.SelectedIndex = (int)_settings.CardOrientation;
            _loading = false;
        }

        private static decimal Clamp(NumericUpDown box, float value)
        {
            decimal v = (decimal)value;
            if (v < box.Minimum) return box.Minimum;
            if (v > box.Maximum) return box.Maximum;
            return v;
        }

        private void ReadUiToSettings()
        {
            _settings.Slot1X = (float)_numS1X.Value;
            _settings.Slot1Y = (float)_numS1Y.Value;
            _settings.Slot2X = (float)_numS2X.Value;
            _settings.Slot2Y = (float)_numS2Y.Value;
            if (_segSlots.SelectedIndex >= 0) _settings.SlotMode = (SlotMode)_segSlots.SelectedIndex;
            _settings.RotateBack180 = _chkRotateBack.Checked;
            _settings.RotateFront180 = _chkRotateFront.Checked;
            if (_segOrientation.SelectedIndex >= 0) _settings.CardOrientation = (CardOrientation)_segOrientation.SelectedIndex;
        }

        private void SaveSettings(bool notify)
        {
            if (_settingsReplaced) return;   // a restored backup is on disk: keep it
            ReadUiToSettings();
            try
            {
                _settings.Save();
                if (notify) SetStatus("Settings saved.");
            }
            catch (Exception ex)
            {
                if (notify) ShowError(ex);
            }
        }

        private void UpdatePageInfo()
        {
            if (string.IsNullOrEmpty(_settings.PrinterName))
            {
                _lblPage.Text = "No card printer selected.";
                _trayMap.PageMm = null;
                UpdateSlotHint();
                return;
            }
            var printer = new CardPrinter(_settings);
            try { _lblPage.Text = printer.DescribePage().Replace("  |  ", Environment.NewLine); }
            catch (Exception ex) { _lblPage.Text = ex.Message; }
            _trayMap.PageMm = printer.TryGetPageSizeMm();
            UpdateSlotHint();   // the page size decides whether slot 2 fits
        }

        /// <summary>Shows next to the Slots box how the selected cards will be split into tray loads.</summary>
        private void UpdateSlotHint()
        {
            if (_loading || _lblSlotHint == null || _segSlots.SelectedIndex < 0) return;
            ReadUiToSettings();

            int count = SelectedStudents().Count;
            string reason;
            int perLoad = new CardPrinter(_settings).CardsPerLoad(count, out reason);
            int loads = (count + perLoad - 1) / perLoad;

            string text = count == 0 ? "" : string.Format("{0} card(s) → {1} tray load(s)", count, loads);
            if (_settings.SlotMode == SlotMode.Auto && count >= 2)
                text += perLoad == 2 ? ", 2 per load" : ", slot 1 only — slot 2 not usable";
            _lblSlotHint.Text = text;
            _lblSlotHint.ForeColor = reason != null ? Warning : TextMuted;
            _trayMap.SetSlots(
                new RectangleF(_settings.Slot1X, _settings.Slot1Y, _settings.SlotWidthMm, _settings.SlotHeightMm),
                new RectangleF(_settings.Slot2X, _settings.Slot2Y, _settings.SlotWidthMm, _settings.SlotHeightMm),
                _settings.SlotMode == SlotMode.One ? TraySlotUse.Off : perLoad == 2 ? TraySlotUse.InUse : TraySlotUse.Idle);
            _statSelected.Caption = count == 0
                ? "Select rows in the list"
                : string.Format("{0} tray load(s)  ·  {1} side", loads, PrintFront ? "front" : "back");
            _tip.SetTip(_lblSlotHint, reason ?? "");
        }

        // ===================== Data =====================
        /// <summary>Opens the Import Students page; the source picked there is imported with the usual dialog.</summary>
        private void ShowImportPage()
        {
            ImportSource choice;
            using (var form = new ImportStudentsForm(_students.Count, _source))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                choice = form.Choice;
            }

            switch (choice)
            {
                case ImportSource.Excel: ImportExcel(); break;
                case ImportSource.Csv: ImportCsv(); break;
                case ImportSource.Api: ImportApi(); break;
                case ImportSource.SqlServer: ConnectSqlServer(); break;
                case ImportSource.GoogleSheet: ImportGoogleSheet(); break;
                case ImportSource.ExcelTemplate: SaveExcelTemplate(); break;
            }
        }

        private void ImportCsv()
        {
            using (var dlg = new OpenFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", Title = Lang.T("Import students") })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadStudents(dlg.FileName);
            }
        }

        private void ImportExcel()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "Excel workbook (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Excel 97-2003 (*.xls)|*.xls|All files (*.*)|*.*",
                Title = Lang.T("Import students from Excel")
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadStudents(dlg.FileName);
            }
        }

        /// <summary>Asks for the API address and token, downloads the students and shows them in the list.</summary>
        private void ImportApi()
        {
            List<Student> loaded = null;
            string url = null;

            using (var dlg = new Form
            {
                Text = Lang.T("Import students from API"),
                ClientSize = new Size(560, 300),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                BackColor = Surface,
                Font = UiFont,
                Padding = new Padding(16)
            })
            {
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, BackColor = Surface };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                var txtUrl = new TextBox { Dock = DockStyle.Fill, Text = _settings.ApiUrl ?? "", Margin = new Padding(0, 4, 0, 4) };
                txtUrl.HandleCreated += (s, e) => SetCueBanner(txtUrl, "https://example.edu.kh/api/students");

                string savedToken = _settings.ApiToken;
                var txtToken = new TextBox { Dock = DockStyle.Fill, Text = savedToken, UseSystemPasswordChar = true, Margin = new Padding(0, 4, 0, 4) };
                txtToken.HandleCreated += (s, e) => SetCueBanner(txtToken, "Optional — leave empty if the API is public");

                var chkShow = new CheckBox { Text = "Show token", AutoSize = true, Margin = new Padding(0, 2, 16, 0) };
                chkShow.CheckedChanged += (s, e) => txtToken.UseSystemPasswordChar = !chkShow.Checked;
                var chkRemember = new CheckBox
                {
                    Text = "Remember token on this PC (encrypted for this Windows user)",
                    AutoSize = true, Checked = savedToken.Length > 0, Margin = new Padding(0, 2, 0, 0)
                };
                var options = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
                options.Controls.Add(chkShow);
                options.Controls.Add(chkRemember);

                var hint = new Label
                {
                    AutoSize = true, MaximumSize = new Size(410, 0), ForeColor = TextMuted, Font = CaptionFont,
                    Margin = new Padding(0, 8, 0, 0),
                    Text = "The API must return a JSON list of students (or CSV) using the CSV column names: StudentId, " +
                           "NameKh, NameEn, Gender, DateOfBirth, Faculty, Level, Generation, ValidUntil, QrData.\n" +
                           "The token is sent as “Authorization: Bearer …”. Type “Header-Name: value” to use another header."
                };
                var lblBusy = new Label { AutoSize = true, ForeColor = TextMuted, Margin = new Padding(0, 10, 0, 0) };

                layout.Controls.Add(FieldLabel("API address"), 0, 0);
                layout.Controls.Add(txtUrl, 1, 0);
                layout.Controls.Add(FieldLabel("Access token"), 0, 1);
                layout.Controls.Add(txtToken, 1, 1);
                layout.Controls.Add(options, 1, 2);
                layout.Controls.Add(hint, 1, 3);
                layout.Controls.Add(lblBusy, 1, 4);

                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, BackColor = Surface
                };
                Button btnCancel = MakeButton("Cancel", PillKind.Secondary, null);
                Button btnLoad = MakeButton("Load Students", PillKind.Primary, null);
                buttons.Controls.Add(btnCancel);
                buttons.Controls.Add(btnLoad);

                dlg.Controls.Add(layout);
                dlg.Controls.Add(buttons);
                Lang.Apply(dlg);
                dlg.AcceptButton = btnLoad;
                dlg.CancelButton = btnCancel;

                CancellationTokenSource cts = null;
                bool closeWhenDone = false;
                Action<bool> setBusy = busy =>
                {
                    txtUrl.Enabled = txtToken.Enabled = options.Enabled = btnLoad.Enabled = !busy;
                    dlg.UseWaitCursor = busy;
                    lblBusy.Text = busy ? "Downloading students…  (Cancel stops the download)" : "";
                };

                btnLoad.Click += async (s, e) =>
                {
                    if (cts != null) return;
                    cts = new CancellationTokenSource();
                    setBusy(true);
                    try
                    {
                        loaded = await ApiStudentLoader.LoadAsync(txtUrl.Text, txtToken.Text, cts.Token);
                        url = txtUrl.Text.Trim();
                        _settings.ApiUrl = url;
                        _settings.ApiToken = chkRemember.Checked ? txtToken.Text.Trim() : "";
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancel pressed or dialog closed: nothing to report.
                    }
                    catch (Exception ex)
                    {
                        if (!closeWhenDone) ShowError(ex, dlg);
                    }
                    finally
                    {
                        cts.Dispose();
                        cts = null;
                        setBusy(false);
                    }

                    if (loaded != null) dlg.DialogResult = DialogResult.OK;
                    else if (closeWhenDone) dlg.DialogResult = DialogResult.Cancel;
                    else txtUrl.Focus();
                };

                btnCancel.Click += (s, e) =>
                {
                    if (cts != null) cts.Cancel();               // stop the download, keep the dialog open
                    else dlg.DialogResult = DialogResult.Cancel;
                };

                dlg.FormClosing += (s, e) =>
                {
                    if (cts == null) return;
                    // Closed (X / Alt+F4) while downloading: cancel first; the Load handler then closes the dialog.
                    e.Cancel = true;
                    closeWhenDone = true;
                    cts.Cancel();
                };

                if (dlg.ShowDialog(this) != DialogResult.OK || loaded == null) return;
            }

            Uri uri;
            string source = Uri.TryCreate(url, UriKind.Absolute, out uri) ? uri.Host : url;
            _source = source ?? "";
            _students = loaded;
            ApplyFilter();
            _settings.LastCsvPath = "";   // so the next start doesn't reload an older file over this list
            SaveSettings(false);

            SetStatus(_students.Count == 0
                ? "Missing data: the API returned no students (" + source + ")"
                : string.Format("Loaded {0} student(s) from {1}", _students.Count, source));
        }

        /// <summary>Opens the Google Sheets page; on Import the list is replaced by the sheet's students.</summary>
        private void ImportGoogleSheet()
        {
            List<Student> loaded;
            using (var form = new GoogleSheetForm(_settings))
            {
                loaded = form.ShowDialog(this) == DialogResult.OK ? form.Students : null;
            }
            if (loaded == null)
            {
                SaveSettings(false);   // keep the sheet link even when the page was closed without importing
                return;
            }

            _source = "Google Sheets";
            _students = loaded;
            ApplyFilter();
            _settings.LastCsvPath = "";   // so the next start doesn't reload an older file over this list
            SaveSettings(false);
            SetStatus(_students.Count == 0
                ? "Missing data: the Google Sheet has no students"
                : string.Format("Loaded {0} student(s) from Google Sheets", _students.Count));
        }

        /// <summary>Opens the SQL Server connection page; on Load Students the list is replaced by the database's students.</summary>
        private void ConnectSqlServer()
        {
            List<Student> loaded;
            string source;
            using (var form = new SqlServerForm(_settings))
            {
                bool ok = form.ShowDialog(this) == DialogResult.OK;
                loaded = ok ? form.Students : null;
                source = form.SourceName;
            }
            SaveSettings(false);   // keep the connection details even when only Test Connection was used
            if (loaded != null) ShowSqlStudents(loaded, source);
        }

        /// <summary>Replaces the student list with the students read from SQL Server.</summary>
        private void ShowSqlStudents(List<Student> loaded, string source)
        {
            _source = string.IsNullOrEmpty(source) ? "SQL Server" : source;
            _students = loaded;
            ApplyFilter();
            _settings.LastCsvPath = "";   // so the next start doesn't reload an older file over this list
            SaveSettings(false);
            SetStatus(_students.Count == 0
                ? "Missing data: the SQL query returned no students (" + source + ")"
                : string.Format("Loaded {0} student(s) from SQL Server {1}", _students.Count, source));
        }

        private void SaveExcelTemplate()
        {
            string path;
            using (var dlg = new SaveFileDialog
            {
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                Title = Lang.T("Save Excel template"),
                FileName = "students-template.xlsx",
                OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }

            try
            {
                ExcelTemplateWriter.Write(path);
            }
            catch (IOException ex)
            {
                AppDialog.Show(this, "Could not save the template:\n\n" + ex.Message +
                    "\n\nIf the file is open in Excel, close it and try again.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            catch (Exception ex)
            {
                ShowError(ex);
                return;
            }

            SetStatus("Excel template saved: " + path);
            if (AppDialog.Show(this,
                    "Template saved.\n\nFill in the Students sheet (one student per row), save it, then click " +
                    "Import Students… → From Excel…\n\nOpen it now?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { System.Diagnostics.Process.Start(path); }
                catch (Exception ex) { ShowError(ex); }   // e.g. no program associated with .xlsx
            }
        }

        /// <summary>Loads a student list from .xlsx or .csv (chosen by extension) and remembers it for next start.</summary>
        private void LoadStudents(string path)
        {
            try
            {
                _source = Path.GetFileName(path);
                _students = ExcelStudentLoader.IsExcelFile(path)
                    ? ExcelStudentLoader.Load(path)
                    : CsvStudentLoader.Load(path);
                ApplyFilter();
                _settings.LastCsvPath = path;
                SaveSettings(false);
                SetStatus(string.Format("Loaded {0} student(s) from {1}", _students.Count, Path.GetFileName(path)));
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        /// <summary>Photo Folder page: shows the current folder and lets the user choose another one or open it.</summary>
        private void ShowPhotoFolderPage()
        {
            PhotoFolderAction choice;
            using (var form = new PhotoFolderForm(_settings.PhotoFolder, _students))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                choice = form.Choice;
            }

            switch (choice)
            {
                case PhotoFolderAction.Choose: ChoosePhotoFolder(); break;
                case PhotoFolderAction.Open: OpenPhotoFolder(); break;
                case PhotoFolderAction.GoogleDrive: ShowGoogleDrivePhotos(); break;
                case PhotoFolderAction.OneDrive: ShowOneDrivePhotos(); break;
            }
        }

        private void ChoosePhotoFolder()
        {
            using (var dlg = new FolderBrowserDialog
            {
                Description = Lang.T("Folder with photos named {StudentId}.jpg / .png"),
                SelectedPath = _settings.PhotoFolder
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _settings.PhotoFolder = dlg.SelectedPath;
                SaveSettings(false);
                UpdatePreview();
                SetStatus("Photo folder: " + dlg.SelectedPath);
            }
        }

        /// <summary>Opens the Google Drive Photos page; downloaded photos show on the cards straight away.</summary>
        private void ShowGoogleDrivePhotos()
        {
            ShowPhotoSync(new GoogleDrivePhotoSource(_settings));
        }

        /// <summary>Opens the OneDrive Photos page (a OneDrive folder synced to this PC).</summary>
        private void ShowOneDrivePhotos()
        {
            ShowPhotoSync(new OneDrivePhotoSource(_settings));
        }

        private void ShowPhotoSync(PhotoSource source)
        {
            int downloaded;
            using (var form = new PhotoSyncForm(_settings, _students, source))
            {
                form.ShowDialog(this);
                downloaded = form.Downloaded;
            }
            SaveSettings(false);   // the source's folder / key and a newly chosen photo folder
            if (downloaded == 0) return;
            UpdatePreview();
            SetStatus(string.Format("Downloaded {0} photo(s) from {1} to {2}", downloaded, source.Name, _settings.PhotoFolder));
        }

        /// <summary>
        /// Opens the photo folder in Windows Explorer to add, rename or delete images there.
        /// The selected student's photo is highlighted; with no folder set yet, asks for one first.
        /// </summary>
        private void OpenPhotoFolder()
        {
            if (string.IsNullOrEmpty(_settings.PhotoFolder) || !Directory.Exists(_settings.PhotoFolder))
            {
                if (AppDialog.Show(this, "No photo folder is set yet (or it no longer exists).\n\nChoose one now?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
                ChoosePhotoFolder();
                if (string.IsNullOrEmpty(_settings.PhotoFolder) || !Directory.Exists(_settings.PhotoFolder)) return;
            }

            Student s = CurrentStudent();
            string photo = s == null ? null : CardRenderer.FindPhoto(_settings.PhotoFolder, s.StudentId);
            try
            {
                if (photo != null && File.Exists(photo))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + photo + "\"");
                else
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + _settings.PhotoFolder + "\"");
                _refreshOnActivate = true;
                SetStatus("Opened photo folder: " + _settings.PhotoFolder);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        /// <summary>Opens the background-removal dialog for the current student's photo.</summary>
        private void RemovePhotoBackground()
        {
            Student s = CurrentStudent();
            if (s == null)
            {
                AppDialog.Show(this, "Select a student in the list first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (string.IsNullOrEmpty(_settings.PhotoFolder) || !Directory.Exists(_settings.PhotoFolder))
            {
                AppDialog.Show(this, "Choose the photo folder first (Photo Folder… in the sidebar).", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (CardRenderer.FindPhoto(_settings.PhotoFolder, s.StudentId) == null &&
                BackgroundRemovalForm.FindOriginal(_settings.PhotoFolder, s.StudentId) == null)
            {
                AppDialog.Show(this, "No photo found for " + Describe(s) + ".\n\nExpected " + s.StudentId +
                    ".jpg / .png in:\n" + _settings.PhotoFolder, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            BackgroundRemovalForm dlg;
            Cursor = Cursors.WaitCursor;
            try { dlg = new BackgroundRemovalForm(_settings.PhotoFolder, s.StudentId); }
            catch (Exception ex) { ShowError(ex); return; }
            finally { Cursor = Cursors.Default; }

            using (dlg)
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                UpdatePreview();
                SetStatus(dlg.Restored
                    ? "Original photo restored: " + Path.GetFileName(dlg.SavedPath)
                    : "Background removed: " + Path.GetFileName(dlg.SavedPath) + "  (original kept in _original)");
            }
        }

        /// <summary>Card Template…: picks the card design; preview, full screen and printing use it from now on.</summary>
        private void ChooseCardTemplate()
        {
            CardTemplate chosen;
            string designId;
            bool libraryChanged;
            using (var dlg = new CardTemplateForm(CurrentStudent(), _settings))
            {
                DialogResult result = dlg.ShowDialog(this);
                libraryChanged = dlg.DesignsChanged;
                if (result != DialogResult.OK)   // Cancel / Esc / ✕: the template stays
                {
                    if (libraryChanged) AfterDesignLibraryChanged();
                    if (dlg.OpenDesigner) ShowCardDesigner(dlg.DesignerDesignId, dlg.DesignerCreateNew, true);
                    return;
                }
                chosen = dlg.SelectedTemplate;
                designId = dlg.SelectedDesignId ?? "";
            }

            bool same = chosen == _settings.CardTemplate &&
                        (chosen != CardTemplate.Custom || string.Equals(designId, _settings.CustomDesignId, StringComparison.OrdinalIgnoreCase));
            if (same)
            {
                if (libraryChanged) AfterDesignLibraryChanged();
                return;
            }
            _settings.CardTemplate = chosen;
            if (chosen == CardTemplate.Custom) _settings.CustomDesignId = designId;
            SaveSettings(false);
            UpdatePreview();
            SetStatus("Card template: " + CurrentDesignName() + ". Preview and printing now use this design.");
        }

        /// <summary>
        /// Card Designer…: create or edit custom designs. Save &amp; Use prints with the open design.
        /// fromPicker = opened from Card Template…, which comes back afterwards so the updated list is shown.
        /// </summary>
        private void ShowCardDesigner() { ShowCardDesigner(null, false, false); }

        private void ShowCardDesigner(string designId, bool createNew, bool fromPicker)
        {
            bool changed, use;
            string openId;
            using (var dlg = new CardDesignerForm(CurrentStudent(), _settings, designId, createNew))
            {
                dlg.ShowDialog(this);
                changed = dlg.Changed;
                use = dlg.UseDesign;
                openId = dlg.DesignId;
            }

            if (use)
            {
                _settings.CardTemplate = CardTemplate.Custom;
                _settings.CustomDesignId = openId;
                SaveSettings(false);
                UpdatePreview();
                SetStatus("Card design “" + CurrentDesignName() + "” saved. Preview and printing use it.");
                return;
            }
            if (changed)
            {
                AfterDesignLibraryChanged();
                SetStatus(_settings.CardTemplate == CardTemplate.Custom
                    ? "Card designs saved. Printing uses “" + CurrentDesignName() + "”."
                    : "Card designs saved. Pick one in Card Template… to print with it.");
            }
            if (fromPicker) ChooseCardTemplate();
        }

        /// <summary>After designs were saved or deleted: keep the printed design valid, then redraw the preview.</summary>
        private void AfterDesignLibraryChanged()
        {
            if (_settings.CardTemplate == CardTemplate.Custom && CustomCardStore.Find(_settings.CustomDesignId) == null)
            {
                // The design in use was deleted: print the newest one left, or go back to Classic.
                IList<CustomCardDesign> left = CustomCardStore.All();
                if (left.Count > 0) _settings.CustomDesignId = left[0].Id;
                else
                {
                    _settings.CardTemplate = CardTemplates.Default;
                    _settings.CustomDesignId = "";
                }
                SaveSettings(false);
            }
            UpdatePreview();
        }

        /// <summary>"Modern", or the name of the custom design in use.</summary>
        private string CurrentDesignName()
        {
            return _settings.CardTemplate == CardTemplate.Custom
                ? CustomCardStore.ForSettings(_settings).Name
                : CardTemplates.Find(_settings.CardTemplate).Name;
        }

        private void EditCardText()
        {
            using (var dlg = new CardTextForm(_settings.CardText))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;   // Cancel / Esc / ✕: nothing changes
                _settings.CardText = dlg.Result;
            }
            SaveSettings(false);
            UpdatePreview();
            SetStatus("Card text saved.");
        }

        private Student CurrentStudent()
        {
            return _grid.CurrentRow == null ? null : _grid.CurrentRow.DataBoundItem as Student;
        }

        private List<Student> SelectedStudents()
        {
            return _grid.SelectedRows.Cast<DataGridViewRow>()
                .OrderBy(r => r.Index)
                .Select(r => r.DataBoundItem as Student)
                .Where(s => s != null)
                .ToList();
        }

        // ===================== Preview =====================
        private void UpdatePreview()
        {
            Student s = CurrentStudent();
            try
            {
                ReplaceImage(_picFront, s == null ? null : CardRenderer.RenderFront(s, _settings));
                ReplaceImage(_picBack, CardRenderer.RenderBack(_settings));
            }
            catch (Exception ex)
            {
                SetStatus("Preview error: " + ex.Message);
            }
        }

        private static void ReplaceImage(PreviewTile box, Image image)
        {
            Image old = box.Image;
            box.Image = image;
            if (old != null) old.Dispose();
        }

        private Bitmap RenderSide(Student s, bool front)
        {
            // Turned to match the tray pocket (180° flip and/or 90° for a vertical card)
            return front
                ? CardPrinter.TrayImage(CardRenderer.RenderFront(s, _settings), _settings, _settings.RotateFront180)
                : CardPrinter.TrayImage(CardRenderer.RenderBack(_settings), _settings, _settings.RotateBack180);
        }

        // ===================== Full-screen preview =====================
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F1: ShowHelp(_students.Count == 0 ? HelpForm.Page.QuickStart : HelpForm.Page.Overview); return true;
                case Keys.F11: ShowFullScreen(PreviewSide.Both); return true;
                case Keys.Shift | Keys.F11: Show3DPreview(); return true;
                case Keys.Control | Keys.F: _txtSearch.Focus(); _txtSearch.SelectAll(); return true;
                case Keys.Control | Keys.L: ShowImportPage(); return true;
                case Keys.Control | Keys.O: ImportCsv(); return true;
                case Keys.Control | Keys.E: ImportExcel(); return true;
                case Keys.Control | Keys.I: ImportApi(); return true;
                case Keys.Control | Keys.D: ConnectSqlServer(); return true;
                case Keys.Control | Keys.Shift | Keys.D: ShowGoogleDrivePhotos(); return true;
                case Keys.Control | Keys.Shift | Keys.O: ShowOneDrivePhotos(); return true;
                case Keys.Control | Keys.Shift | Keys.G: ImportGoogleSheet(); return true;
                case Keys.Control | Keys.P: PrintSelected(); return true;
                case Keys.Control | Keys.B: RemovePhotoBackground(); return true;
                case Keys.Control | Keys.H: ShowPrintHistory(); return true;
                case Keys.Control | Keys.G: ShowGallery(); return true;
                case Keys.Control | Keys.Oemcomma: ShowAllSettings(); return true;
                case Keys.Control | Keys.Shift | Keys.F: ShowFolders(); return true;
                case Keys.Control | Keys.Shift | Keys.B: ShowBackup(); return true;
                case Keys.Control | Keys.Shift | Keys.C: ShowCsvFiles(); return true;
                case Keys.Control | Keys.Shift | Keys.E: DriverSetup(); return true;
                case Keys.Control | Keys.Shift | Keys.R: ShowRfidList(); return true;
                case Keys.Control | Keys.Shift | Keys.I: ShowHelp(HelpForm.Page.SystemInfo); return true;
                case Keys.Control | Keys.U: ShowUpdates(); return true;
                case Keys.Control | Keys.Shift | Keys.L: SwitchLanguage(!Lang.Khmer); return true;
                case Keys.Control | Keys.Oem5: ToggleSidebar(); return true;   // Ctrl+\
                case Keys.Control | Keys.Shift | Keys.N: ShowNotifications(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowFullScreen(PreviewSide side)
        {
            int index = _grid.CurrentRow != null ? _grid.CurrentRow.Index : 0;
            using (var form = new CardPreviewForm(_view, index, _settings, side, _showGrid))
            {
                form.ShowDialog(this);
                SetPreviewGrid(form.ShowGrid);   // keep whatever was chosen in full screen
                if (_view.Count > 0 && form.CurrentIndex != index)
                    SelectRow(form.CurrentIndex);
            }
        }

        /// <summary>3D View: the current student's card as a real card that can be turned, flipped and zoomed.</summary>
        private void Show3DPreview()
        {
            int index = _grid.CurrentRow != null ? _grid.CurrentRow.Index : 0;
            using (var form = new CardPreview3DForm(_view, index, _settings))
            {
                form.ShowDialog(this);
                if (_view.Count > 0 && form.CurrentIndex != index)
                    SelectRow(form.CurrentIndex);
            }
        }

        /// <summary>Selects a grid row (this also refreshes the small preview).</summary>
        private void SelectRow(int index)
        {
            if (index < 0 || index >= _grid.Rows.Count) return;
            DataGridViewCell cell = _grid.Rows[index].Cells.Cast<DataGridViewCell>().FirstOrDefault(c => c.Visible);
            if (cell == null) return;
            _grid.ClearSelection();
            _grid.CurrentCell = cell;
            _grid.Rows[index].Selected = true;
        }

        // ===================== Printing =====================
        /// <summary>True when the selected printer is a card printer (or the user confirms another one).</summary>
        private bool EnsureCardPrinter()
        {
            if (string.IsNullOrEmpty(_settings.PrinterName))
            {
                AppDialog.Show(this, "No card printer selected.\n\nInstall the Epson L8050 driver, then choose it in the Printer list.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (_settings.IsCardPrinter(_settings.PrinterName)) return true;

            return AppDialog.Show(this,
                "\"" + _settings.PrinterName + "\" is not a card printer.\n\nPrint to it anyway?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(282, 253);
            this.Name = "MainForm";
            this.ResumeLayout(false);

        }

        /// <summary>Epson Driver Setup page: pick an Epson printer and save its paper source / type / quality.</summary>
        private void DriverSetup()
        {
            ReadUiToSettings();   // the page saves _settings, so it must hold the current tray values
            bool changed;
            using (var form = new EpsonDriverForm(_settings))
            {
                form.ShowDialog(this);
                changed = form.SettingsChanged;
            }
            if (!changed) return;

            LoadPrinters();       // the page may have switched the card printer
            SaveSettings(false);
            UpdatePageInfo();
            SetStatus("Epson driver settings saved for " + _settings.PrinterName + ".");
        }

        /// <summary>Checks tray, driver setup, offline/error state and stuck jobs before sending a job.</summary>
        private bool ConfirmPrinterReady(CardPrinter printer)
        {
            Cursor = Cursors.WaitCursor;
            List<string> problems;
            try { problems = printer.FindProblems(); }
            finally { Cursor = Cursors.Default; }

            if (problems.Count == 0) return true;

            return AppDialog.Show(this,
                "The card may not print:\n\n• " + string.Join("\n\n• ", problems) + "\n\nPrint anyway?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        /// <summary>Toolbar "Check Printer": shows the page setup and every problem found.</summary>
        private void CheckPrinter()
        {
            if (string.IsNullOrEmpty(_settings.PrinterName))
            {
                AppDialog.Show(this, "No card printer selected.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var printer = new CardPrinter(_settings);
            Cursor = Cursors.WaitCursor;
            string page;
            List<string> problems;
            try
            {
                try { page = printer.DescribePage(); }
                catch (Exception ex) { page = ex.Message; }
                problems = printer.FindProblems();
            }
            finally { Cursor = Cursors.Default; }

            string slotReason;
            int perLoad = printer.CardsPerLoad(2, out slotReason);
            string slots = "Slots per tray load: " + _segSlots.SelectedText +
                (_settings.SlotMode == SlotMode.Auto
                    ? " (" + (perLoad == 2 ? "2 cards when 2+ are selected" : "slot 1 only — " + slotReason) + ")"
                    : "");

            string report = "Printer: " + _settings.PrinterName + "\n" + page.Replace("  |  ", "\n") + "\n" + slots + "\n\n" +
                (problems.Count == 0 ? "No problems found." : "Problems:\n• " + string.Join("\n\n• ", problems));
            AppDialog.Show(this, report, "Check Printer", MessageBoxButtons.OK,
                problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void TestPrint()
        {
            if (!EnsureCardPrinter()) return;
            SaveSettings(false);
            var printer = new CardPrinter(_settings);
            if (!ConfirmPrinterReady(printer)) return;

            try
            {
                using (Bitmap c1 = CardPrinter.TrayImage(
                           CardRenderer.RenderCalibration(1, _settings.Slot1X, _settings.Slot1Y), _settings, false))
                using (Bitmap c2 = _settings.UseTwoSlots
                           ? CardPrinter.TrayImage(CardRenderer.RenderCalibration(2, _settings.Slot2X, _settings.Slot2Y), _settings, false)
                           : null)
                {
                    printer.Print(c1, c2, "Card Studio Calibration");
                }

                AppDialog.Show(this,
                    "Calibration job sent. Insert the tray when the printer light turns on.\n\n" +
                    "Red line = card edge, each black line = 1 mm.\n" +
                    "Lines cut off on the RIGHT → decrease X.   On the LEFT → increase X.\n" +
                    "Lines cut off at the BOTTOM → decrease Y.   At the TOP → increase Y.",
                    "Test Print", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void PrintSelected()
        {
            List<Student> list = SelectedStudents();
            if (list.Count == 0)
            {
                AppDialog.Show(this, "Select at least one student in the list.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!EnsureCardPrinter()) return;

            SaveSettings(false);
            var printer = new CardPrinter(_settings);
            if (!ConfirmPrinterReady(printer)) return;

            bool front = PrintFront;
            string side = front ? "FRONT" : "BACK";
            string slotReason;
            int perLoad = printer.CardsPerLoad(list.Count, out slotReason);
            int totalLoads = (list.Count + perLoad - 1) / perLoad;
            string historyError = null;

            for (int i = 0, load = 1; i < list.Count; i += perLoad, load++)
            {
                Student a = list[i];
                Student b = (perLoad == 2 && i + 1 < list.Count) ? list[i + 1] : null;

                string message =
                    (load > 1 ? "Wait until the previous card is finished and the tray is ejected.\n\n" : "") +
                    (load == 1 && slotReason != null ? "Auto slots: printing 1 card per tray load.\n" + slotReason + "\n\n" : "") +
                    "Side: " + side + "\n\n" +
                    "Slot 1: " + Describe(a) + "\n" +
                    (b != null ? "Slot 2: " + Describe(b) + "\n" : "") +
                    "\nClick OK to send the job, then insert the tray when the printer light turns on.";

                DialogResult answer = AppDialog.Show(this, message,
                    string.Format("Tray load {0} of {1}", load, totalLoads),
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (answer != DialogResult.OK) { SetStatus("Printing cancelled."); return; }

                try
                {
                    string document = "Card Studio " + a.StudentId + " " + side;
                    using (Bitmap img1 = RenderSide(a, front))
                    using (Bitmap img2 = b != null ? RenderSide(b, front) : null)
                    {
                        printer.Print(img1, img2, document);
                    }
                    SetStatus(string.Format("Sent tray load {0} of {1} ({2}).", load, totalLoads, side));
                    historyError = RecordPrinted(side, document, a, b) ?? historyError;
                }
                catch (Exception ex)
                {
                    ShowError(ex);
                    return;
                }
            }
            SetStatus(historyError != null
                ? string.Format("Missing history: {0} card(s) printed, but the print history could not be saved ({1}).", list.Count, historyError)
                : string.Format("Done: {0} card(s), {1} side. Tap each card on the RFID reader to save its number.", list.Count, side));
            UpdatePrintedStat();

            // Next step: register the printed card's RFID chip. The selection is kept (the back side may be printed next);
            // the RFID tab saves the card for the current row.
            _setupTabs.SelectedIndex = 2;
            UpdateRfidPanel();
            _txtRfid.Focus();
        }

        /// <summary>
        /// Adds one tray load to the print history. Never throws — a full disk must not stop the cards printing.
        /// Returns the error message, or null when saved.
        /// </summary>
        private string RecordPrinted(string side, string document, Student slot1, Student slot2)
        {
            var records = new List<PrintRecord> { PrintRecord.For(slot1, side, 1, _settings.PrinterName, document) };
            if (slot2 != null) records.Add(PrintRecord.For(slot2, side, 2, _settings.PrinterName, document));
            try
            {
                PrintHistory.Add(records);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>Shows every card printed so far; double-clicking a row selects that student here.</summary>
        private void ShowPrintHistory()
        {
            using (var form = new PrintHistoryForm(SelectStudentById))
            {
                form.ShowDialog(this);
            }
            UpdatePrintedStat();   // records may have been deleted there
        }

        /// <summary>Read-only list of every setting (app + this PC), including tray values not saved yet.</summary>
        /// <summary>
        /// Saves the other colour theme and restarts the app: every window reads the theme colours once at start-up.
        /// </summary>
        private void ToggleDarkMode()
        {
            bool dark = !Theme.Dark;
            string text = (dark ? "Switch to dark mode?" : "Switch to light mode?") +
                "\n\nThe app restarts to apply it. Your settings are saved and a student list opened from a file " +
                "comes back by itself; students imported from the API or SQL Server must be loaded again.\n\n" +
                "Printed cards are not affected.";
            if (AppDialog.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            ReadUiToSettings();
            _settings.DarkMode = dark;
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                _settings.DarkMode = !dark;
                ShowError(ex);
                return;
            }
            Application.Restart();
        }

        /// <summary>
        /// Language menu (top bar), Settings &amp; Help → Language or Ctrl+Shift+L: saves the other language and restarts the app,
        /// like Dark Mode (every window reads the language once at start-up). The user guide follows the new language.
        /// True when the app is restarting.
        /// </summary>
        private bool SwitchLanguage(bool khmer)
        {
            if (khmer == Lang.Khmer) return true;

            // Asked in both languages: the reader may know only the one switched to.
            string text = khmer
                ? "Switch Card Studio to Khmer?\nប្តូរ Card Studio ទៅជាភាសាខ្មែរ?\n\n" +
                  "The app restarts to apply it. Your settings are saved and a student list opened from a file comes back by " +
                  "itself; students imported from the API or SQL Server must be loaded again.\n\nPrinted cards are not affected."
                : "ប្តូរ Card Studio ទៅជាភាសាអង់គ្លេស?\nSwitch Card Studio to English?\n\n" +
                  "កម្មវិធីនឹងបើកឡើងវិញដើម្បីប្រើភាសាថ្មី។ ការកំណត់របស់អ្នកត្រូវបានរក្សាទុក ហើយបញ្ជីនិស្សិតដែលបានបើកពីឯកសារ " +
                  "នឹងត្រឡប់មកវិញដោយខ្លួនឯង។ និស្សិតដែលបាននាំចូលពី API ឬ SQL Server ត្រូវផ្ទុកម្ដងទៀត។\n\n" +
                  "កាតដែលបានបោះពុម្ពមិនមានការប៉ះពាល់ទេ។";
            if (AppDialog.Show(this, text, "Language · ភាសា", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return false;

            ReadUiToSettings();
            string oldLanguage = _settings.UiLanguage;
            bool oldHelp = _settings.HelpKhmer;
            _settings.UiLanguage = khmer ? Lang.KhmerCode : Lang.EnglishCode;
            _settings.HelpKhmer = khmer;   // the user guide opens in the same language
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                _settings.UiLanguage = oldLanguage;
                _settings.HelpKhmer = oldHelp;
                ShowError(ex);
                return false;
            }
            Application.Restart();
            return true;
        }

        /// <summary>
        /// Theme Color…: picks the primary colour, saves it and restarts the app (every window reads the theme colours
        /// once at start-up, like Dark Mode).
        /// </summary>
        private void ChangeThemeColor()
        {
            string chosen;
            using (var form = new ThemeColorForm(_settings.AccentColor))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                chosen = form.SelectedHex;
            }
            if (string.Equals(chosen, AccentPalette.ToHex(Theme.Palette.Base), StringComparison.OrdinalIgnoreCase)) return;

            string old = _settings.AccentColor;
            ReadUiToSettings();
            // The default violet is saved as "not set", so it follows the app's default if that ever changes.
            _settings.AccentColor = string.Equals(chosen, AccentPalette.ToHex(AccentPalette.DefaultBase), StringComparison.OrdinalIgnoreCase)
                ? null : chosen;
            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                _settings.AccentColor = old;
                ShowError(ex);
                return;
            }
            Application.Restart();
        }

        /// <summary>
        /// Settings &amp; Help page: Dark / Light Mode, Theme Color, Folders, All Settings, Epson Driver Setup, System Info,
        /// Updates, License, Help &amp; About.
        /// Runs the command picked there.
        /// </summary>
        private void ShowSettingsHub()
        {
            SettingsHubChoice choice;
            using (var form = new SettingsHubForm(_settings.AccentColor))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                choice = form.Choice;
            }

            switch (choice)
            {
                case SettingsHubChoice.DarkMode: ToggleDarkMode(); break;
                case SettingsHubChoice.Language: SwitchLanguage(!Lang.Khmer); break;
                case SettingsHubChoice.ThemeColor: ChangeThemeColor(); break;
                case SettingsHubChoice.Folders: ShowFolders(); break;
                case SettingsHubChoice.Backup: ShowBackup(); break;
                case SettingsHubChoice.AllSettings: ShowAllSettings(); break;
                case SettingsHubChoice.DriverSetup: DriverSetup(); break;
                case SettingsHubChoice.SystemInfo: ShowHelp(HelpForm.Page.SystemInfo); break;
                case SettingsHubChoice.Updates: ShowUpdates(); break;
                case SettingsHubChoice.HelpAbout: ShowHelp(HelpForm.Page.Overview); break;
                case SettingsHubChoice.License: ShowLicense(); break;
            }
        }

        // ===================== Updates =====================

        /// <summary>Check for Updates page (Ctrl+U). Closes the app when the user starts the update's setup.</summary>
        private void ShowUpdates(UpdateInfo found = null)
        {
            bool setupStarted;
            using (var form = new UpdateForm(_settings, found))
            {
                form.ShowDialog(this);
                setupStarted = form.SetupStarted;
            }
            SaveSettings(false);   // update address, startup option, last check, skipped version
            UpdateTitle();         // a check on the page may have found (or ruled out) a new version

            if (setupStarted) Close();   // the setup waits for this window to close, then updates and can restart the app
        }

        /// <summary>
        /// Window title: product name and running version, plus the newer version when the last check found one
        /// ("Card Studio 1.0.2  —  Update 1.0.3 available (Ctrl+U)"). Shown on every start until that update is installed,
        /// also when it was skipped (skipping only silences the start-up popup).
        /// </summary>
        private void UpdateTitle()
        {
            string title = "Card Studio " + UpdateService.CurrentVersionText;
            string available = _settings.AvailableUpdateVersion;
            if (UpdateService.IsNewerThanRunning(available))
                title += Lang.F("  —  Update {0} available (Ctrl+U)", available.Trim());
            LicenseStatus license = LicenseService.Check();
            if (license.ExpiresSoon)
                title += Lang.F("  —  License expires {0}", Lang.T(license.DaysLeftText));
            Text = title;
        }

        /// <summary>
        /// From <see cref="LicenseService.WarnDays"/> days before the serial's expiry date: say so in the status bar
        /// (the window title says it too, see <see cref="UpdateTitle"/>).
        /// </summary>
        private void WarnIfLicenseExpiresSoon()
        {
            LicenseStatus license = LicenseService.Check();
            if (!license.ExpiresSoon) return;
            SetStatus("License expires " + license.DaysLeftText + " (" + LicenseService.FormatDate(license.Expires.Value) +
                      "): ask your administrator for a new serial, then enter it in Settings & Help → License….");
        }

        /// <summary>License page (Settings &amp; Help → License…): expiry date, and a new serial to renew it.</summary>
        private void ShowLicense()
        {
            if (!ActivationForm.ShowRenew(this)) return;
            UpdateTitle();
            SetStatus("License: " + LicenseService.Check().Describe() + ".");
        }

        /// <summary>
        /// Startup check (at most once a day, when switched on and an update address is known). Silent unless a new version
        /// that was not skipped is found; then the Check for Updates page opens if the main window is free, otherwise only
        /// the status bar says so. Network errors are ignored here: Check for Updates shows them.
        /// </summary>
        private async void CheckForUpdatesQuietly()
        {
            if (!UpdateService.IsStartupCheckDue(_settings)) return;

            UpdateInfo info;
            try
            {
                info = await UpdateService.CheckAsync(UpdateService.Source, CancellationToken.None);
            }
            catch (Exception)
            {
                return;   // offline, share not reachable…: try again at the next start
            }
            if (IsDisposed) return;

            UpdateService.RememberLatest(_settings, info);
            SaveSettings(false);
            UpdateTitle();
            if (!info.IsNewerThanRunning ||
                string.Equals(_settings.SkippedUpdateVersion, info.VersionText, StringComparison.Ordinal))
                return;

            SetStatus("Update available: Card Studio " + info.VersionText + " — press Ctrl+U to install it.");
            if (ActiveForm == this && OwnedForms.Length == 0) ShowUpdates(info);
        }

        private void ShowAllSettings()
        {
            ReadUiToSettings();
            bool imported;
            List<Student> sqlStudents;
            string sqlSource;
            using (var form = new SettingsOverviewForm(_settings))
            {
                form.ShowDialog(this);
                imported = form.SettingsImported;
                sqlStudents = form.SqlStudents;
                sqlSource = form.SqlSource;
            }

            if (imported)
            {
                // Import… replaced _settings: refresh every control, or closing the app would save the old tray values back.
                LoadPrinters();          // falls back to an installed card printer if the imported one is not on this PC
                ApplySettingsToUi();
                SaveSettings(false);
                UpdatePageInfo();
                UpdatePreview();
                UpdateSlotHint();
                SetStatus("Settings imported.");
            }

            if (sqlStudents != null) ShowSqlStudents(sqlStudents, sqlSource);   // loaded with SQL Server… in the overview
            UpdateRfidPanel();   // an imported settings file may use another RFID file
        }

        /// <summary>About &amp; User Guide page: what the system does, how to use it, shortcuts, troubleshooting, system info.</summary>
        private void ShowHelp(HelpForm.Page topic)
        {
            ReadUiToSettings();   // System info shows the current printer and folders
            bool khmer = _settings.HelpKhmer;
            using (var form = new HelpForm(_settings, topic, _students.Count, _source))
            {
                form.ShowDialog(this);
            }
            if (_settings.HelpKhmer != khmer) SaveSettings(false);   // remember the guide's language (English / Khmer)
        }

        /// <summary>Folders page: every folder the app uses. A new Photo Folder chosen there is saved and shown here.</summary>
        private void ShowFolders()
        {
            bool photoFolderChanged;
            using (var form = new FolderManagerForm(_settings))
            {
                form.ShowDialog(this);
                photoFolderChanged = form.PhotoFolderChanged;
            }
            if (!photoFolderChanged) return;
            SaveSettings(false);
            UpdatePreview();
            SetStatus("Photo folder: " + _settings.PhotoFolder);
        }

        /// <summary>
        /// Backup &amp; Restore page (Ctrl+Shift+B). After a restore the app restarts without saving: the restored settings,
        /// history and card files on disk must not be overwritten by the ones this window still holds.
        /// </summary>
        private void ShowBackup()
        {
            ReadUiToSettings();   // a backup saves the settings as they are now, tray values included
            bool restored, changed;
            using (var form = new BackupForm(_settings))
            {
                form.ShowDialog(this);
                restored = form.Restored;
                changed = form.SettingsChanged;
            }
            if (restored)
            {
                _settingsReplaced = true;
                Application.Restart();
                return;
            }
            if (changed) SaveSettings(false);
        }

        /// <summary>
        /// Daily automatic backup at start-up (settings, history, RFID, logos, signatures, designs and CSV files; no photos),
        /// in the background. Quiet when it works; a failure is shown in the status bar and tried again at the next start.
        /// </summary>
        private async void AutoBackupQuietly()
        {
            if (!BackupService.IsAutoBackupDue(_settings)) return;
            SaveSettings(false);   // the backup reads settings.xml from disk
            if (_settingsReplaced) return;
            BackupSources src = BackupSources.From(_settings, false, _settings.BackupIncludeCsv);
            try
            {
                await System.Threading.Tasks.Task.Run(() => BackupService.Create(src, BackupKind.Auto, null, CancellationToken.None));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                if (!IsDisposed) SetStatus("Daily backup failed: " + ex.Message);
                return;
            }
            if (IsDisposed || _settingsReplaced) return;
            _settings.LastAutoBackup = DateTime.Now;
            SaveSettings(false);
        }

        /// <summary>CSV Files page: the student CSVs of one folder. The file picked with Load Students is loaded here.</summary>
        private void ShowCsvFiles()
        {
            string load;
            bool settingsChanged;
            using (var form = new CsvFilesForm(_settings))
            {
                load = form.ShowDialog(this) == DialogResult.OK ? form.FileToLoad : null;
                settingsChanged = form.SettingsChanged;
            }
            if (load != null) LoadStudents(load);           // also saves the settings
            else if (settingsChanged) SaveSettings(false);   // new CSV folder, or the loaded file was renamed
        }

        /// <summary>Photo + details gallery of the loaded students; the student chosen there is selected here.</summary>
        private void ShowGallery()
        {
            Student current = CurrentStudent();
            // The rows selected here open already chosen in the Gallery, so a pick can be adjusted there and sent back
            List<string> selected = SelectedStudents().Select(s => s.StudentId ?? "").ToList();
            using (var form = new StudentGalleryForm(_students, _settings, current == null ? null : current.StudentId, selected))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                ShowGalleryPick(form.SelectedStudentIds, form.SelectedStudentId);
            }
        }

        /// <summary>
        /// The student list shows only the students chosen in the Gallery, all of them selected (ready for Print Selected),
        /// with <paramref name="focusId"/> as the current row (card preview). The search box is cleared so none is hidden.
        /// </summary>
        private void ShowGalleryPick(IEnumerable<string> ids, string focusId)
        {
            var pick = new HashSet<string>(ids ?? new string[0], StringComparer.OrdinalIgnoreCase);
            if (pick.Count == 0) return;
            _picked = pick;
            _pickedFrom = _students;
            if (_txtSearch.TextLength > 0) _txtSearch.Clear();   // rebinds the grid (ApplyFilter)
            else ApplyFilter();

            int focus = Math.Max(0, _view.FindIndex(s => string.Equals(s.StudentId, focusId, StringComparison.OrdinalIgnoreCase)));
            SelectRow(focus);
            if (_grid.Rows.Count > 1) _grid.SelectAll();          // keeps the current row: its card stays in the preview
            if (focus < _grid.Rows.Count) _grid.FirstDisplayedScrollingRowIndex = focus;
            SetStatus(_view.Count == 1
                ? "Showing the student chosen in the Gallery. Click Show all to see every student."
                : string.Format("Showing the {0} students chosen in the Gallery, all selected. Click Show all to see every student.", _view.Count));
        }

        /// <summary>Ends a Gallery pick: every student shows again, and the current student stays selected.</summary>
        private void ClearGalleryPick()
        {
            if (_picked == null) return;
            Student current = CurrentStudent();
            _picked = null;
            ApplyFilter();
            if (current != null) SelectStudentById(current.StudentId);
        }

        /// <summary>Selects the student in the grid (clearing the search or Gallery pick if it hides them). False when not loaded.</summary>
        private bool SelectStudentById(string studentId)
        {
            Func<Student, bool> same = s => string.Equals(s.StudentId, studentId, StringComparison.OrdinalIgnoreCase);
            if (!_students.Any(same)) return false;
            if (!_view.Any(same))
            {
                _picked = null;
                if (_txtSearch.TextLength > 0) _txtSearch.Clear();   // rebinds the grid to every student
                else ApplyFilter();
            }

            int index = _view.FindIndex(s => same(s));
            SelectRow(index);
            if (index >= 0 && index < _grid.Rows.Count) _grid.FirstDisplayedScrollingRowIndex = index;
            return index >= 0;
        }

        /// <summary>Shows the Windows print queue of the selected printer (cancel stuck jobs).</summary>
        private void ShowPrintQueue()
        {
            if (string.IsNullOrEmpty(_settings.PrinterName))
            {
                AppDialog.Show(this, "No card printer selected.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var form = new PrintQueueForm(_settings.PrinterName))
            {
                form.ShowDialog(this);
            }
        }

        private static string Describe(Student s)
        {
            return s.StudentId + " — " + (string.IsNullOrEmpty(s.NameEn) ? s.NameKh : s.NameEn);
        }

        // ===================== RFID upload =====================
        private string _rfidStudentId;   // student the RFID textbox currently belongs to

        /// <summary>Refreshes the RFID tab: selected student, printed or not, and the database it saves to.</summary>
        private void UpdateRfidPanel()
        {
            if (_txtRfid == null || _lblRfidStudent == null) return;

            Student s = CurrentStudent();
            string id = s == null ? null : s.StudentId;
            if (!_rfidBusy && !string.Equals(id, _rfidStudentId, StringComparison.OrdinalIgnoreCase))
            {
                // A number read for the previous student must never be saved for this one.
                _rfidStudentId = id;
                _txtRfid.Clear();
                _lblRfidResult.Text = "";
            }

            if (s == null)
            {
                _lblRfidStudent.Text = "Select the student whose card you printed";
                _lblRfidPrinted.Text = "";
            }
            else
            {
                _lblRfidStudent.Text = Describe(s);
                DateTime printed;
                bool done = _lastPrinted.TryGetValue((s.StudentId ?? "").Trim(), out printed);
                _lblRfidPrinted.Text = done
                    ? "Printed " + printed.ToString("dd/MM/yyyy HH:mm") + "  ·  tap the card on the reader"
                    : "Not printed yet — print the card first";
                _lblRfidPrinted.ForeColor = done ? Success : Warning;
            }

            string path = RfidCardWriter.FilePath(_settings);
            bool isDefault = string.IsNullOrWhiteSpace(_settings.RfidCsvPath);
            _lblRfidTarget.Text = "Saves to " + Path.GetFileName(path) + (isDefault ? "  (app data)" : "  ·  " + Path.GetDirectoryName(path));
            _lblRfidTarget.ForeColor = TextMuted;
            _tip.SetTip(_lblRfidTarget, "RFID CSV file: " + path);

            UpdateRfidButton();
        }

        private void UpdateRfidButton()
        {
            if (_btnRfidUpload == null) return;
            _btnRfidUpload.Enabled = !_rfidBusy && CurrentStudent() != null && _txtRfid.Text.Trim().Length > 0;
        }

        private void SetRfidBusy(bool busy)
        {
            _rfidBusy = busy;
            _txtRfid.ReadOnly = busy;   // a second card tapped during the upload must not be appended
            _btnRfidUpload.Text = busy ? "Saving…" : "Upload";
            UseWaitCursor = busy;
            UpdateRfidButton();
        }

        private void ShowRfidResult(string text, Color tone)
        {
            _lblRfidResult.Text = text;
            _lblRfidResult.ForeColor = tone;
        }

        /// <summary>RFID Cards page: saved card numbers and loaded students without a card; the student chosen there is selected here.</summary>
        private void ShowRfidList()
        {
            using (var form = new RfidListForm(_settings, _students, SelectStudentById))
            {
                form.ShowDialog(this);
            }
            UpdateRfidPanel();
        }

        /// <summary>Chooses where the RFID CSV file is kept. An existing RFID file is used as it is; otherwise the current one is copied there.</summary>
        private void ChangeRfidFile()
        {
            string current = RfidCardWriter.FilePath(_settings);
            string chosen;
            using (var dlg = new SaveFileDialog
            {
                Title = Lang.T("RFID CSV file"),
                Filter = "CSV file (*.csv)|*.csv",
                FileName = Path.GetFileName(current),
                InitialDirectory = Path.GetDirectoryName(current),
                OverwritePrompt = false,   // picking an existing RFID file means "use it", not "replace it"
                AddExtension = true,
                DefaultExt = "csv"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                chosen = dlg.FileName;
            }
            if (string.Equals(Path.GetFullPath(chosen), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase)) return;

            string message;
            try
            {
                if (File.Exists(chosen))
                {
                    int cards = RfidCardWriter.CheckFile(chosen);   // throws when it is not an RFID file
                    message = "Using the existing RFID file with " + cards + " card(s):\n" + chosen;
                }
                else if (File.Exists(current))
                {
                    int cards = RfidCardWriter.CheckFile(current);
                    Directory.CreateDirectory(Path.GetDirectoryName(chosen));
                    File.Copy(current, chosen);
                    message = "The " + cards + " saved card(s) were copied to the new RFID file:\n" + chosen +
                              "\n\nThe old file is kept:\n" + current;
                }
                else
                {
                    message = "New RFID cards will be saved to:\n" + chosen + "\n\nThe file is created with the first card.";
                }
            }
            catch (Exception ex)
            {
                ShowError(ex);
                return;
            }

            // The default location is stored as "" so it follows the Windows user's AppData.
            _settings.RfidCsvPath = string.Equals(Path.GetFullPath(chosen), Path.GetFullPath(RfidCardWriter.DefaultPath),
                StringComparison.OrdinalIgnoreCase) ? "" : chosen;
            SaveSettings(false);
            UpdateRfidPanel();
            SetStatus("RFID file: " + chosen);
            AppDialog.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Saves the RFID number in the textbox for the selected (printed) student to the RFID CSV file.</summary>
        private async void UploadRfid()
        {
            if (_rfidBusy) return;

            Student s = CurrentStudent();
            if (s == null)
            {
                AppDialog.Show(this, "Select the student whose card you printed, then tap the card again.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string typed = _txtRfid.Text.Trim();
            if (typed.Length == 0) { _txtRfid.Focus(); return; }
            string uid = RfidCardWriter.NormalizeUid(typed);
            if (uid == null)
            {
                ShowRfidResult("“" + typed + "” is not an RFID card number.", Danger);
                AppDialog.Show(this, "“" + typed + "” is not an RFID card number.\n\n" +
                    "Tap the card on the reader again, or type its number (letters A–F / digits, at least 4 characters).",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtRfid.Focus();
                _txtRfid.SelectAll();
                return;
            }

            DateTime printedAt;
            bool printed = _lastPrinted.TryGetValue((s.StudentId ?? "").Trim(), out printedAt);
            if (!printed && AppDialog.Show(this,
                    Describe(s) + " has no printed card in the print history.\n\nSave RFID card " + uid + " for this student anyway?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            SetRfidBusy(true);
            try
            {
                DateTime? when = printed ? printedAt : (DateTime?)null;
                RfidSaveResult result = await RfidCardWriter.SaveAsync(_settings, s, uid, when, false, CancellationToken.None);

                if (result.Status == RfidSaveStatus.NeedsReplace)
                {
                    if (AppDialog.Show(this,
                            Describe(s) + " already has RFID card " + result.PreviousUid + ".\n\n" +
                            "Replace it with " + uid + "? (e.g. the card was reprinted)",
                            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        ShowRfidResult("Not saved — kept card " + result.PreviousUid + " for " + s.StudentId + ".", Warning);
                        return;
                    }
                    result = await RfidCardWriter.SaveAsync(_settings, s, uid, when, true, CancellationToken.None);
                }

                if (result.Status == RfidSaveStatus.InUse)
                {
                    ShowRfidResult("Not saved — card " + uid + " belongs to student " + result.OtherStudentId + ".", Danger);
                    SetStatus("Error: RFID card " + uid + " is already saved for student " + result.OtherStudentId + ".");
                    AppDialog.Show(this, "RFID card " + uid + " is already saved for student " + result.OtherStudentId + ".\n\n" +
                        "Nothing was changed. Check you tapped the right card for " + Describe(s) + ".",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _txtRfid.SelectAll();
                    return;
                }

                string what = result.Status == RfidSaveStatus.Updated ? "replaced " + result.PreviousUid + " with"
                            : result.Status == RfidSaveStatus.Unchanged ? "already saved:"
                            : "saved";
                SetStatus("RFID card " + uid + " saved for " + Describe(s) + ".");
                _txtRfid.Clear();

                // The card is done: take the student off the list and go on to the next one.
                bool removed = RemoveFromList(s);
                ShowRfidResult("✓ " + s.StudentId + ": " + what + " " + uid + "  (" + DateTime.Now.ToString("HH:mm:ss") + ")" +
                               (removed ? "  ·  removed from the list" : ""), Success);
            }
            catch (Exception ex)
            {
                ShowRfidResult("Not saved — " + ex.Message.Split('\n')[0], Danger);
                ShowError(ex);
            }
            finally
            {
                SetRfidBusy(false);
                _txtRfid.Focus();
            }
        }

        /// <summary>
        /// Removes a student whose RFID card was saved from the loaded list (not from the Excel / CSV / database it came from)
        /// and selects the student now in that row, so the next card can be tapped straight away. False when not in the list.
        /// </summary>
        private bool RemoveFromList(Student s)
        {
            int at = _students.IndexOf(s);
            if (at < 0) return false;
            int row = _view.IndexOf(s);

            _students.RemoveAt(at);
            ApplyFilter();   // rebinds the grid to the shorter list and updates the counts

            if (_view.Count > 0)
            {
                int next = Math.Max(0, Math.Min(row < 0 ? 0 : row, _view.Count - 1));
                SelectRow(next);
                if (next < _grid.Rows.Count) _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, next - 2);
            }

            // The RFID box now belongs to the newly selected student (the change happened while the upload was busy).
            Student current = CurrentStudent();
            _rfidStudentId = current == null ? null : current.StudentId;
            UpdateRfidPanel();
            return true;
        }

        // ===================== Misc =====================
        private void SetStatus(string text)
        {
            _lblStatus.Text = text;
            string title;
            Action action;
            NoticeKind kind = ClassifyStatus(text, out title, out action);
            Color tone = kind == NoticeKind.Error ? Danger : kind == NoticeKind.Warning ? Warning : Success;
            _lblStatusDot.ForeColor = tone;
            _lblStatus.ForeColor = tone == Success ? TextMuted : tone;

            if (title != null) _notices.Add(kind, Lang.T(title), Lang.T(text), action);
        }

        /// <summary>
        /// Kind of a status message, and — for the ones worth keeping in the notification list (every warning and error,
        /// and finished jobs) — its notification title and what clicking the notification opens. Other messages get no title.
        /// </summary>
        private NoticeKind ClassifyStatus(string text, out string title, out Action action)
        {
            title = null;
            action = null;

            // ---- errors ----
            if (text.StartsWith("Preview error")) { title = "Preview error"; return NoticeKind.Error; }
            if (text.StartsWith("Error")) { title = "Something went wrong"; return NoticeKind.Error; }

            // ---- warnings ----
            if (text.StartsWith("Missing in ")) { title = "Card files missing"; action = ShowFolders; return NoticeKind.Warning; }
            if (text.StartsWith("Missing history")) { title = "Print history not saved"; action = ShowPrintHistory; return NoticeKind.Warning; }
            if (text.StartsWith("Missing data")) { title = "No students found"; action = ShowImportPage; return NoticeKind.Warning; }
            if (text.StartsWith("Missing")) { title = "Something is missing"; return NoticeKind.Warning; }
            if (text.StartsWith("No card printer")) { title = "No card printer found"; action = CheckPrinter; return NoticeKind.Warning; }
            if (text.StartsWith("Auto slots")) { title = "Tray slots"; return NoticeKind.Warning; }
            if (text.StartsWith("Printing cancelled")) { title = "Printing stopped"; return NoticeKind.Warning; }
            if (text.StartsWith("License expires")) { title = "License expiring soon"; action = ShowLicense; return NoticeKind.Warning; }
            if (text.StartsWith("Could not")) { title = "Something went wrong"; return NoticeKind.Warning; }

            // ---- finished jobs and news ----
            if (text.StartsWith("Update available")) { title = "Update available"; action = () => ShowUpdates(); return NoticeKind.Info; }
            if (text.StartsWith("Done: ")) { title = "Printing finished"; action = ShowPrintHistory; return NoticeKind.Success; }
            if (text.StartsWith("Loaded ")) { title = "Students loaded"; action = ShowGallery; return NoticeKind.Success; }
            if (text.StartsWith("Downloaded ")) { title = "Photos downloaded"; action = ShowPhotoFolderPage; return NoticeKind.Success; }
            if (text.StartsWith("RFID card ")) { title = "RFID card saved"; action = ShowRfidList; return NoticeKind.Success; }
            if (text.StartsWith("Settings imported")) { title = "Settings imported"; return NoticeKind.Success; }
            if (text.StartsWith("Card design “")) { title = "Card design saved"; action = ShowCardDesigner; return NoticeKind.Success; }
            return NoticeKind.Success;
        }

        /// <summary>Opens (or closes) the notification list under the bell. Clicking a notification opens its page.</summary>
        private void ShowNotifications()
        {
            if (_noticePopup != null && !_noticePopup.IsDisposed && _noticePopup.Visible)
            {
                _noticePopup.Close();
                return;
            }
            if (NotificationPopup.JustClosed) return;   // this click on the bell is what closed it
            _noticePopup = NotificationPopup.Show(_bell, _notices, notice => notice.Action());
        }

        /// <summary>Shows the whole error chain (Ctrl+C in the message box copies it).</summary>
        private void ShowError(Exception ex, IWin32Window owner = null)
        {
            var text = new System.Text.StringBuilder(ex.Message);
            for (Exception inner = ex.InnerException; inner != null; inner = inner.InnerException)
                text.Append("\n\n→ ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);
            text.Append("\n\n(Press Ctrl+C to copy this message.)");

            SetStatus("Error: " + ex.Message.Split('\n')[0]);
            AppDialog.Show(owner ?? this, text.ToString(), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
