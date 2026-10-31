using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using NPIC_CARD_APP;
using NPIC_CARD_APP.Services;

namespace CardStudio.Admin
{
    /// <summary>
    /// Card Studio Admin: every PC running Card Studio on the network (found by broadcast every 15 s, plus the extra
    /// addresses for other networks), with its version, license, phones and print counts, and commands for the selected PCs.
    /// Built from Card Studio's own UI kit (sidebar, stat tiles, shadow cards, pill buttons, popups, bell), laid out like
    /// Card Studio's main window.
    /// </summary>
    //  ┌──────────┬────────────────────────────────────────────────────────────────┐
    //  │ brand    │ Good afternoon, …                  bell │ Refresh  Send Message… │
    //  │ PCS      │ [PCs online] [Phones] [Printed today] [Needs attention]        │
    //  │ ACTIONS  │ ┌ Computers ── search ─────────────────┐ ┌ Details ─────────┐   │
    //  │ NETWORK  │ │ grid                                 │ │ facts + actions  │   │
    //  │ APP      │ └──────────────────────────────────────┘ └──────────────────┘   │
    //  │ collapse │ ● status                                           admin key  │
    //  └──────────┴────────────────────────────────────────────────────────────────┘
    internal sealed class AdminForm : Form
    {
        // ---- Theme tokens (Card Studio's palette: light / dark and the primary colour of Card Studio on this PC) ----
        private static readonly Color AppBack = Theme.AppBack;
        private static readonly Color Surface = Theme.Surface;
        private static readonly Color SurfaceAlt = Theme.SurfaceAlt;
        private static readonly Color Border = Theme.Border;
        private static readonly Color GridLine = Theme.GridLine;
        private static readonly Color TextMain = Theme.TextMain;
        private static readonly Color TextSoft = Theme.TextSoft;
        private static readonly Color TextMuted = Theme.TextMuted;
        private static readonly Color Accent = Theme.Accent;
        private static readonly Color AccentSoft = Theme.AccentSoft;
        private static readonly Color Success = Theme.Success;
        private static readonly Color Warning = Theme.Warning;
        private static readonly Color Danger = Theme.Danger;
        private static readonly Color AmberA = Color.FromArgb(255, 164, 64), AmberB = Color.FromArgb(255, 204, 120);

        private static readonly Font UiFont = Theme.UiFont(9.75f);
        private static readonly Font SemiFont = Theme.UiFont(9.75f, true);
        private static readonly Font ChipFont = Theme.UiFont(8.25f, true);
        private static readonly Font NameFont = Theme.UiFont(14f, true);

        // ---- Line icons (same code points in Segoe Fluent Icons and Segoe MDL2 Assets) ----
        private const string IcoPc = "", IcoNetwork = "", IcoMessage = "", IcoRefresh = "",
            IcoUpdate = "", IcoTag = "", IcoExport = "", IcoOnline = "";

        private const int SidebarFullWidth = 232, SidebarCompactWidth = 68, DetailsWidth = 360;
        private const int PollMs = 15000;

        private readonly Inventory _inv = Inventory.Load();
        private readonly AdminClient _client = new AdminClient();
        private readonly AdminSettings _settings = AdminSettings.Current;
        private AdminKey _key;

        private readonly ToolTip _tip = new ToolTip();
        private readonly NotificationCenter _notices = new NotificationCenter();
        private NotificationBell _bell;
        private ToolStripDropDown _noticePopup;

        // Sidebar
        private Panel _sidebar;
        private BrandBlock _brand;
        private NavItem _navToggle, _navAll, _navOnline, _navAttention;
        private readonly List<Label> _sections = new List<Label>();
        private readonly List<KeyValuePair<NavItem, string>> _navTips = new List<KeyValuePair<NavItem, string>>();
        private bool _sidebarCompact;

        // Dashboard
        private StatCard _statOnline, _statPhones, _statPrinted, _statAttention;
        private SearchField _search;
        private DataGridView _grid;
        private Label _lblCount, _lblEmpty;
        private Label _lblStatus, _lblStatusDot, _lblKey;

        // Details card
        private Label _lblName, _lblSub, _lblNoPc, _lblMulti;
        private Panel _factsHost, _detailsBody;
        private FlowLayoutPanel _actions;
        private readonly Dictionary<string, Label> _facts = new Dictionary<string, Label>();

        private readonly Timer _poll, _tick, _relist;

        private enum Filter { All, Online, Attention }
        private Filter _filter = Filter.All;
        private string _sortColumn = "Status";
        private bool _sortDescending;
        private bool _rebuilding;
        private volatile bool _discovering;
        private bool _dirty;

        // What this session has already announced under the bell.
        private readonly HashSet<string> _wasOnline = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _licenseNoticed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _knownAtStart;

        public AdminForm()
        {
            _knownAtStart = new HashSet<string>(_inv.Devices.Select(d => d.MachineId), StringComparer.OrdinalIgnoreCase);

            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            Text = "Card Studio Admin " + v.ToString(3);
            if (Branding.Icon != null) Icon = Branding.Icon;
            StartPosition = FormStartPosition.CenterScreen;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1380, work.Width), Math.Min(860, work.Height));
            MinimumSize = new Size(Math.Min(1100, work.Width), Math.Min(680, work.Height));
            Font = UiFont;
            BackColor = AppBack;
            ForeColor = TextMain;
            KeyPreview = true;

            BuildUi();
            SetSidebarCompact(_settings.SidebarCollapsed, false);
            DarkControls.Apply(this);   // dark mode only; before the handles exist, so restyling recreates nothing
            SlimScroll.Apply(this);     // Card Studio's slim scroll bars on the grid and scrolling panels

            _poll = new Timer { Interval = PollMs };
            _poll.Tick += (s, e) => Discover(false);
            _tick = new Timer { Interval = 5000 };    // online → offline, "last seen", saving
            _tick.Tick += (s, e) => { RefreshList(); SaveIfDirty(); };
            _relist = new Timer { Interval = 300 };   // replies come in bursts: one redraw per burst
            _relist.Tick += (s, e) => { _relist.Stop(); RefreshList(); };

            _client.StatusReceived += OnStatusReceived;

            UseKey(AdminKey.FindDefault(), false);
            RefreshList();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _poll.Start();
            _tick.Start();
            Discover(true);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _poll.Stop();
            _tick.Stop();
            _client.Dispose();
            _dirty = true;
            SaveIfDirty();
            if (_key != null) _key.Dispose();
            base.OnFormClosed(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5: Discover(true); return true;
                case Keys.Control | Keys.F: _search.Box.Focus(); _search.Box.SelectAll(); return true;
                case Keys.Control | Keys.M: SendMessage(); return true;
                case Keys.Control | Keys.U: CheckUpdates(); return true;
                case Keys.Control | Keys.E: ExportCsv(); return true;
                case Keys.Control | Keys.Shift | Keys.N: ShowNotifications(); return true;
                case Keys.Control | Keys.C:
                    if (_grid.Focused) { CopyMachineIds(); return true; }
                    break;
                case Keys.Delete:
                    if (_grid.Focused) { RemoveSelected(); return true; }
                    break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ===================== UI =====================

        private void BuildUi()
        {
            SuspendLayout();
            var content = new DecorPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 4) };
            content.Controls.Add(BuildDashboard());
            Controls.Add(content);       // docking runs in reverse add order: the fill control first
            Controls.Add(BuildSidebar());
            ResumeLayout(true);
        }

        private Control BuildSidebar()
        {
            var side = new Panel { Dock = DockStyle.Left, Width = SidebarFullWidth, BackColor = Surface, Padding = new Padding(0, 0, 1, 0) };
            side.Paint += (s, e) =>
            {
                using (var pen = new Pen(GridLine)) e.Graphics.DrawLine(pen, side.Width - 1, 0, side.Width - 1, side.Height);
            };
            _sidebar = side;
            var scroll = new Panel { Dock = DockStyle.Fill, BackColor = Surface, AutoScroll = true };

            _brand = new BrandBlock("Card Studio Admin", "Network devices", Branding.Logo);

            _navAll = Nav("All PCs", IcoPc, () => SetFilter(Filter.All), "Every PC that ever answered, online or not");
            _navOnline = Nav("Online", IcoOnline, () => SetFilter(Filter.Online), "PCs where Card Studio is open now");
            _navAttention = Nav("Needs Attention", Theme.IcoWarning, () => SetFilter(Filter.Attention),
                "License expiring, expired or not valid, or an older Card Studio version");
            _navAttention.BadgeColor = Warning;
            _navAll.Active = true;

            var items = new List<Control>
            {
                Spacer(14),
                Section("PCS"),
                _navAll,
                _navOnline,
                _navAttention,

                Section("ACTIONS"),
                Nav("Send Message…", IcoMessage, SendMessage, "Show a message on the selected PCs (Ctrl+M)"),
                Nav("Check for Updates", IcoUpdate, CheckUpdates, "Open Check for Updates on the selected PCs (Ctrl+U)"),
                Nav("Install Serial…", Theme.IcoKey, InstallSerial, "Send a new serial number (license) to the selected PC"),
                Nav("Disconnect Phones", Theme.IcoPhone, RevokePhones,
                    "New mobile pairing code on the selected PCs: their phones must scan the QR code again"),

                Section("NETWORK"),
                Nav("Refresh", IcoRefresh, () => Discover(true), "Ask every PC again now (F5)"),
                Nav("Addresses…", IcoNetwork, EditTargets, "PCs on other networks (behind a router): addresses to ask directly"),
                Nav("Export CSV…", IcoExport, ExportCsv, "Save the list as a CSV file for Excel (Ctrl+E)"),
                Nav("Admin Key…", Theme.IcoSettings, ShowKey, "The administrator's signing key: open, create or show the public key"),

                Section("APP"),
                Nav(Theme.Dark ? "Light Mode" : "Dark Mode", Theme.Dark ? Theme.IcoSun : Theme.IcoMoon, ToggleDarkMode,
                    "Switch between light and dark (Card Studio Admin restarts)"),
                Spacer(10)
            };
            for (int i = items.Count - 1; i >= 0; i--)   // Dock = Top stacks the LAST added control on top
            {
                items[i].Dock = DockStyle.Top;
                scroll.Controls.Add(items[i]);
            }

            _navToggle = new NavItem("Collapse sidebar", Theme.IcoPaneClose, (s, e) => SetSidebarCompact(!_sidebarCompact, true))
            {
                Dock = DockStyle.Bottom, Height = 44
            };
            var divider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Surface };
            divider.Paint += (s, e) =>
            {
                int inset = _sidebarCompact ? 14 : 20;
                using (var pen = new Pen(GridLine)) e.Graphics.DrawLine(pen, inset, 0, divider.Width - inset, 0);
            };

            side.Controls.Add(scroll);
            side.Controls.Add(divider);
            side.Controls.Add(_navToggle);
            side.Controls.Add(_brand);
            return side;
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

        /// <summary>Icons only (compact) or icons with names; in compact mode each tooltip starts with the item's name.</summary>
        private void SetSidebarCompact(bool compact, bool save)
        {
            _sidebarCompact = compact;
            SuspendLayout();
            _sidebar.Width = compact ? SidebarCompactWidth : SidebarFullWidth;
            _brand.Compact = compact;
            _tip.SetToolTip(_brand, compact ? "Card Studio Admin · Network devices" : null);
            foreach (KeyValuePair<NavItem, string> nav in _navTips)
            {
                nav.Key.Compact = compact;
                _tip.SetToolTip(nav.Key, compact ? nav.Key.Text + "\n" + nav.Value : nav.Value);
            }
            foreach (Label section in _sections)
            {
                section.Text = compact ? "" : (string)section.Tag;
                section.Height = compact ? 18 : 32;
                section.Invalidate();
            }
            _navToggle.Compact = compact;
            _navToggle.Glyph = compact ? Theme.IcoPaneOpen : Theme.IcoPaneClose;
            _navToggle.Text = compact ? "Expand sidebar" : "Collapse sidebar";
            _tip.SetToolTip(_navToggle, compact ? "Expand sidebar" : null);
            ResumeLayout(true);
            _sidebar.Invalidate(true);
            if (save)
            {
                _settings.SidebarCollapsed = compact;
                _settings.Save();
            }
        }

        /// <summary>Greeting row, stat tiles, computers + details cards, status line (Card Studio's dashboard grid).</summary>
        private Control BuildDashboard()
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.Transparent, Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 66f));    // greeting + main actions
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 126f));   // stat tiles
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));    // computers | details
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));    // status line
            grid.Controls.Add(BuildHeader(), 0, 0);
            grid.Controls.Add(BuildStats(), 0, 1);
            grid.Controls.Add(BuildBody(), 0, 2);
            grid.Controls.Add(BuildStatusBar(), 0, 3);
            return grid;
        }

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
                Text = "Every PC running Card Studio on your network.", Font = Theme.Small,
                ForeColor = TextMuted, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(3, 0, 0, 0)
            });

            var actions = new FlowLayoutPanel
            {
                AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, BackColor = Color.Transparent, Margin = new Padding(0)
            };
            _bell = new NotificationBell { Margin = new Padding(0, 5, 6, 0) };
            _bell.Click += (s, e) => ShowNotifications();
            _tip.SetTip(_bell, "Notifications: PCs found or gone offline, licenses, command results (Ctrl+Shift+N)");
            _notices.Changed += (s, e) =>
            {
                _bell.UnreadCount = _notices.UnreadCount;
                _bell.Alert = _notices.HasUnreadAlert;
            };
            _notices.Added += n => { if (n.Kind >= NoticeKind.Warning) _bell.Ring(); };
            actions.Controls.Add(_bell);
            actions.Controls.Add(new Panel { Width = 1, Height = 26, BackColor = Border, Margin = new Padding(6, 12, 10, 0) });
            actions.Controls.Add(Pill("Refresh", PillKind.Secondary, (s, e) => Discover(true), "Ask every PC again now (F5)"));
            actions.Controls.Add(Pill("Send Message…", PillKind.Primary, (s, e) => SendMessage(), "Show a message on the selected PCs (Ctrl+M)"));

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

        /// <summary>Four gradient tiles: PCs online, phones connected, cards printed today, PCs that need attention.</summary>
        private Control BuildStats()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0)
            };
            for (int i = 0; i < 4; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _statOnline = new StatCard("PCs online", IcoPc, Theme.VioletA, Theme.VioletB) { Caption = "Looking for PCs…" };
            _statPhones = new StatCard("Phones connected", Theme.IcoPhone, Theme.CoralA, Theme.CoralB);
            _statPrinted = new StatCard("Printed today", Theme.IcoPrint, Theme.PinkA, Theme.PinkB) { Caption = "Card sides, all PCs" };
            _statAttention = new StatCard("Needs attention", Theme.IcoWarning, AmberA, AmberB);

            _statOnline.Cursor = _statAttention.Cursor = Cursors.Hand;
            _statOnline.Click += (s, e) => SetFilter(Filter.Online);
            _statAttention.Click += (s, e) => SetFilter(Filter.Attention);
            _tip.SetTip(_statOnline, "Show the PCs that are online");
            _tip.SetTip(_statAttention, "Show the PCs that need attention");

            row.Controls.Add(_statOnline, 0, 0);
            row.Controls.Add(_statPhones, 1, 0);
            row.Controls.Add(_statPrinted, 2, 0);
            row.Controls.Add(_statAttention, 3, 0);
            return row;
        }

        private Control BuildBody()
        {
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DetailsWidth + 2 * Theme.ShadowPad));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            body.Controls.Add(BuildComputersCard(), 0, 0);
            body.Controls.Add(BuildDetailsCard(), 1, 0);
            return body;
        }

        // ===================== Computers card =====================

        private Control BuildComputersCard()
        {
            _lblCount = new Label { AutoSize = true, ForeColor = TextMuted, Font = Theme.Small, BackColor = Surface, Margin = new Padding(0, 13, 2, 0) };

            _search = new SearchField(UiFont) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            _search.Box.TextChanged += (s, e) => RefreshList();
            _search.Box.HandleCreated += (s, e) => Lang.SetCueBanner(_search.Box, "Search computer, label, address, user, Machine ID, version…  (Ctrl+F)");

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
                BackgroundColor = Surface, BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = GridLine,
                RowHeadersVisible = false, EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 34, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, StandardTab = true
            };
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_grid, true, null);
            _grid.RowTemplate.Height = 42;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.Font = Theme.UiFont(9f, true);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 4, 0);
            _grid.DefaultCellStyle.BackColor = Surface;
            _grid.DefaultCellStyle.ForeColor = TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            _grid.DefaultCellStyle.SelectionForeColor = Accent;
            _grid.DefaultCellStyle.Padding = new Padding(6, 0, 4, 0);
            _grid.DefaultCellStyle.Font = UiFont;

            // Students, last seen, user… are in the Details card: the grid keeps what is compared across PCs.
            AddColumn("Status", "Status", 92);
            AddColumn("Computer", "Computer", 170, fill: true);   // name + the admin's label underneath
            AddColumn("Address", "Address", 104);
            AddColumn("Version", "Version", 94);
            AddColumn("License", "License", 114);
            AddColumn("Phones", "Phones", 60, right: true);
            AddColumn("Today", "Today", 56, right: true);

            _grid.CellPainting += PaintStatusCell;
            _grid.CellPainting += PaintComputerCell;
            _grid.SelectionChanged += (s, e) => { if (!_rebuilding) ShowDetails(); };
            _grid.ColumnHeaderMouseClick += (s, e) =>
            {
                string name = _grid.Columns[e.ColumnIndex].Name;
                if (_sortColumn == name) _sortDescending = !_sortDescending;
                else { _sortColumn = name; _sortDescending = false; }
                RefreshList();
            };
            _grid.CellMouseDown += (s, e) =>
            {
                // Right-click on a row that is not selected: work on that row only (as in Explorer).
                if (e.Button != MouseButtons.Right || e.RowIndex < 0 || _grid.Rows[e.RowIndex].Selected) return;
                _grid.ClearSelection();
                _grid.Rows[e.RowIndex].Selected = true;
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[1];
            };
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) SendMessage(); };
            _grid.ContextMenuStrip = BuildContextMenu();

            _lblEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted, BackColor = Surface, Visible = false
            };
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(0) };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(_lblEmpty);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Surface, Margin = new Padding(0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.Controls.Add(_search, 0, 0);
            layout.Controls.Add(gridHost, 0, 1);

            return MakeCard("Computers", layout, _lblCount,
                Pill("Addresses…", PillKind.Ghost, (s, e) => EditTargets(), "PCs on other networks: addresses to ask directly"),
                Pill("Export CSV…", PillKind.Ghost, (s, e) => ExportCsv(), "Save the list as a CSV file for Excel (Ctrl+E)"));
        }

        private void AddColumn(string name, string header, int width, bool right = false, bool fill = false)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.Programmatic, MinimumWidth = 50
            };
            if (fill) { col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; col.MinimumWidth = 120; }
            if (right)
            {
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            _grid.Columns.Add(col);
        }

        /// <summary>Status column: a soft rounded chip with a dot (Online green, Busy orange, Offline grey).</summary>
        private void PaintStatusCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Status") return;
            var d = _grid.Rows[e.RowIndex].Tag as Device;
            if (d == null) return;

            e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
            string text;
            Color tone;
            StatusOf(d, out text, out tone);

            Graphics g = e.Graphics;
            SmoothingMode oldSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Size size = TextRenderer.MeasureText(text, ChipFont);
            var chip = new RectangleF(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - 22) / 2f, size.Width + 22, 22);
            using (GraphicsPath p = Theme.RoundRect(chip, 11))
            using (var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 52 : 30, tone)))
                g.FillPath(b, p);
            using (var dot = new SolidBrush(tone))
                g.FillEllipse(dot, chip.X + 8, chip.Y + 8, 6, 6);
            g.SmoothingMode = oldSmooth;
            TextRenderer.DrawText(g, text, ChipFont, new Rectangle((int)chip.X + 17, (int)chip.Y, size.Width + 4, (int)chip.Height), tone,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            e.Handled = true;
        }

        /// <summary>Computer column: the name (semibold while online) with the admin's label in small muted text underneath.</summary>
        private void PaintComputerCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Computer") return;
            var d = _grid.Rows[e.RowIndex].Tag as Device;
            if (d == null) return;

            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            e.PaintBackground(e.CellBounds, selected);
            Color nameColor = !d.Online ? TextMuted : selected ? Accent : TextMain;
            Rectangle r = e.CellBounds;
            r.X += 8;
            r.Width -= 12;
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                                          TextFormatFlags.SingleLine;
            Font font = d.Online ? SemiFont : UiFont;
            if (string.IsNullOrEmpty(d.Label))
            {
                TextRenderer.DrawText(e.Graphics, d.Computer, font, r, nameColor, flags | TextFormatFlags.VerticalCenter);
            }
            else
            {
                int top = r.Y + (r.Height - 34) / 2;
                TextRenderer.DrawText(e.Graphics, d.Computer, font, new Rectangle(r.X, top, r.Width, 18), nameColor, flags);
                TextRenderer.DrawText(e.Graphics, d.Label, Theme.Small, new Rectangle(r.X, top + 18, r.Width, 16), TextMuted, flags);
            }
            e.Handled = true;
        }

        private static void StatusOf(Device d, out string text, out Color tone)
        {
            if (!d.Online) { text = "Offline"; tone = TextMuted; }
            else if (!string.IsNullOrEmpty(d.Busy)) { text = "Busy"; tone = Warning; }
            else { text = "Online"; tone = Success; }
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false, BackColor = Surface, ForeColor = TextMain, Font = UiFont,
                Renderer = new ToolStripProfessionalRenderer(new MenuColors())
            };
            Action<string, Action> add = (text, action) =>
                menu.Items.Add(new ToolStripMenuItem(text, null, (s, e) => action()) { ForeColor = TextMain, Padding = new Padding(0, 3, 0, 3) });
            add("Send Message…", SendMessage);
            add("Check for Updates", CheckUpdates);
            add("Install Serial…", InstallSerial);
            add("Disconnect Phones", RevokePhones);
            menu.Items.Add(new ToolStripSeparator());
            add("Copy Machine ID", CopyMachineIds);
            add("Label…", SetLabel);
            add("Remove from List", RemoveSelected);
            return menu;
        }

        /// <summary>Card Studio's themed menu colours (CardDesignerForm.MenuColors).</summary>
        private sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Theme.AccentSoft; } }
            public override Color MenuItemSelected { get { return Theme.AccentSoft; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.AccentSoft; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.AccentSoft; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Surface; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        }

        // ===================== Details card =====================

        private static readonly string[] FactNames =
        {
            "Status", "Machine ID", "User", "Address", "Card Studio", "Windows", "License", "Mobile App", "Phones",
            "Students", "Printer", "Printed", "Running since", "PC clock", "First seen", "Last command"
        };

        private Control BuildDetailsCard()
        {
            _lblName = new Label
            {
                Dock = DockStyle.Top, Height = 30, Font = NameFont, ForeColor = TextMain, BackColor = Surface, AutoEllipsis = true,
                UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft
            };
            _lblSub = new Label
            {
                Dock = DockStyle.Top, Height = 22, Font = Theme.Small, ForeColor = TextMuted, BackColor = Surface, AutoEllipsis = true,
                UseMnemonic = false, TextAlign = ContentAlignment.TopLeft
            };

            const int rowH = 24;
            var facts = new TableLayoutPanel
            {
                Dock = DockStyle.Top, ColumnCount = 2, RowCount = FactNames.Length, BackColor = Surface, Margin = new Padding(0),
                Height = FactNames.Length * rowH
            };
            facts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < FactNames.Length; i++)
            {
                facts.RowStyles.Add(new RowStyle(SizeType.Absolute, rowH));
                facts.Controls.Add(new Label
                {
                    Text = FactNames[i], AutoSize = true, ForeColor = TextMuted, Font = Theme.Small, BackColor = Surface,
                    Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0)
                }, 0, i);
                var value = new Label
                {
                    Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false, ForeColor = TextMain, BackColor = Surface,
                    TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 4, 0)
                };
                facts.Controls.Add(value, 1, i);
                _facts[FactNames[i]] = value;
            }
            _facts["Machine ID"].Font = new Font("Consolas", 9.75f);

            _factsHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface, AutoScroll = true, Padding = new Padding(0, 8, 0, 0) };
            _factsHost.Controls.Add(facts);

            _lblMulti = new Label
            {
                Dock = DockStyle.Fill, ForeColor = TextSoft, BackColor = Surface, Visible = false, UseMnemonic = false,
                Padding = new Padding(0, 10, 0, 0)
            };

            _actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                BackColor = Surface, Padding = new Padding(0, 10, 0, 0), Margin = new Padding(0)
            };
            _actions.Paint += (s, e) => { using (var pen = new Pen(GridLine)) e.Graphics.DrawLine(pen, 0, 2, _actions.Width, 2); };
            _actions.Controls.Add(Small("Send Message…", PillKind.Primary, SendMessage));
            _actions.Controls.Add(Small("Check for Updates", PillKind.Secondary, CheckUpdates));
            _actions.Controls.Add(Small("Install Serial…", PillKind.Secondary, InstallSerial));
            _actions.Controls.Add(Small("Disconnect Phones", PillKind.Secondary, RevokePhones));
            _actions.Controls.Add(Small("Label…", PillKind.Ghost, SetLabel));
            _actions.Controls.Add(Small("Copy Machine ID", PillKind.Ghost, CopyMachineIds));
            _actions.Controls.Add(Small("Remove", PillKind.Ghost, RemoveSelected));

            _lblNoPc = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted, BackColor = Surface,
                Padding = new Padding(12, 0, 12, 40)
            };

            _detailsBody = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(0), Visible = false };
            _detailsBody.Controls.Add(_factsHost);   // docking runs in reverse add order: Fill first, then the edges
            _detailsBody.Controls.Add(_lblMulti);
            _detailsBody.Controls.Add(_actions);
            _detailsBody.Controls.Add(_lblSub);
            _detailsBody.Controls.Add(_lblName);

            // Either the details of the selection or the hint, never both.
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(0) };
            host.Controls.Add(_detailsBody);
            host.Controls.Add(_lblNoPc);

            return MakeCard("Details", host);
        }

        private PillButton Small(string text, PillKind kind, Action action)
        {
            var b = new PillButton(kind)
            {
                Text = text, AutoSize = true, Padding = new Padding(10, 0, 10, 0), Margin = new Padding(0, 0, 6, 6),
                MinimumSize = new Size(0, 32)
            };
            b.Click += (s, e) => action();
            return b;
        }

        private void ShowDetails()
        {
            List<Device> sel = SelectedDevices();
            if (sel.Count == 0)
            {
                _lblNoPc.Text = _inv.Devices.Count == 0
                    ? "No PC has answered yet.\n\nCard Studio must be open on the PCs, installed with its setup (Windows Firewall rule for UDP " +
                      AdminProtocol.Port + ").\n\nPCs behind a router: add them with Addresses…"
                    : "Select a PC to see its details and send it commands.";
                _lblNoPc.Visible = true;
                _detailsBody.Visible = false;
                return;
            }
            _lblNoPc.Visible = false;
            _detailsBody.Visible = true;

            if (sel.Count > 1)
            {
                int online = sel.Count(d => d.Online);
                _lblName.Text = sel.Count + " PCs selected";
                _lblSub.Text = online + " online · " + (sel.Count - online) + " offline: commands go to the online ones";
                _lblMulti.Text = string.Join("\n", sel.Take(14).Select(d => (d.Online ? "●  " : "○  ") + d.DisplayName)) +
                                 (sel.Count > 14 ? "\n… and " + (sel.Count - 14) + " more" : "");
                _lblMulti.Visible = true;
                _factsHost.Visible = false;
                return;
            }
            _lblMulti.Visible = false;
            _factsHost.Visible = true;

            Device x = sel[0];
            _lblName.Text = x.Computer;
            _lblSub.Text = string.IsNullOrEmpty(x.Label) ? "No label · Label… to name it" : x.Label;

            string status;
            Color tone;
            StatusOf(x, out status, out tone);
            int level;
            string license = x.LicenseText(out level);
            Version newest = Newest();

            Fact("Status", x.Online ? status + (string.IsNullOrEmpty(x.Busy) ? "" : ": " + x.Busy)
                                    : "Offline · last seen " + x.LastSeen.ToString("yyyy-MM-dd HH:mm"), tone);
            Fact("Machine ID", x.MachineId);
            Fact("User", x.User);
            Fact("Address", x.Address + (x.Addresses.Count > 1 ? "  (" + string.Join(", ", x.Addresses.Where(a => a != x.Address)) + ")" : ""));
            Fact("Card Studio", x.Version + (x.ParsedVersion < newest ? "  · older than " + newest.ToString(3) : ""),
                 x.ParsedVersion < newest ? Warning : TextMain);
            Fact("Windows", x.Os);
            Fact("License", license, level == 2 ? Danger : level == 1 ? Warning : TextMain);
            Fact("Mobile App", x.MobileRunning ? "On, port " + x.MobilePort : "Off");
            Fact("Phones", x.Phones.Count == 0 ? "None connected" : string.Join(", ", x.Phones));
            Fact("Students", x.Students == 0 ? "None loaded" : x.Students.ToString("N0") + (string.IsNullOrEmpty(x.Source) ? "" : " · " + x.Source));
            Fact("Printer", string.IsNullOrEmpty(x.Printer) ? "—" : x.Printer);
            Fact("Printed", x.PrintedToday.ToString("N0") + " today · " + x.PrintedTotal.ToString("N0") + " total" +
                            (string.IsNullOrEmpty(x.LastPrint) ? "" : " · last " + x.LastPrint));
            Fact("Running since", x.StartedAt);
            Fact("PC clock", x.PcTime);
            Fact("First seen", x.FirstSeen == DateTime.MinValue ? "" : x.FirstSeen.ToString("yyyy-MM-dd HH:mm"));
            Fact("Last command", string.IsNullOrEmpty(x.LastResult) ? "—" : x.LastResult,
                 x.LastResult != null && x.LastResult.Contains("Failed") ? Danger : TextMain);
        }

        private void Fact(string name, string value, Color? color = null)
        {
            Label l = _facts[name];
            l.Text = string.IsNullOrEmpty(value) ? "—" : value;
            l.ForeColor = color ?? TextMain;
            _tip.SetToolTip(l, l.Text.Length > 36 ? l.Text : null);
        }

        // ===================== Status line =====================

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
            _lblKey = new Label
            {
                Dock = DockStyle.Right, AutoSize = true, TextAlign = ContentAlignment.MiddleRight, Font = Theme.Small,
                BackColor = Color.Transparent, Cursor = Cursors.Hand, Padding = new Padding(12, 0, 0, 0)
            };
            _lblKey.Click += (s, e) => ShowKey();
            bar.Controls.Add(_lblStatus);
            bar.Controls.Add(_lblStatusDot);
            bar.Controls.Add(_lblKey);
            return bar;
        }

        private void SetStatus(string text, Color? tone = null)
        {
            _lblStatus.Text = DateTime.Now.ToString("HH:mm") + "   " + text;
            Color t = tone ?? Success;
            _lblStatusDot.ForeColor = t;
            _lblStatus.ForeColor = t == Success ? TextMuted : t;
        }

        // ===================== Building blocks =====================

        /// <summary>Card Studio's rounded white card: soft shadow, ring-bullet title, optional header extras on the right.</summary>
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

        private PillButton Pill(string text, PillKind kind, EventHandler onClick, string tooltip)
        {
            var b = new PillButton(kind)
            {
                Text = text, AutoSize = true, Padding = new Padding(12, 0, 12, 0),
                Margin = kind == PillKind.Ghost ? new Padding(2, 4, 0, 0) : new Padding(8, 8, 0, 0),
                MinimumSize = new Size(kind == PillKind.Primary ? 132 : kind == PillKind.Ghost ? 0 : 96, kind == PillKind.Ghost ? 30 : 38)
            };
            b.Click += onClick;
            _tip.SetTip(b, tooltip);
            return b;
        }

        private void ShowNotifications()
        {
            if (_noticePopup != null && !_noticePopup.IsDisposed && _noticePopup.Visible)
            {
                _noticePopup.Close();
                return;
            }
            if (NotificationPopup.JustClosed) return;   // this click on the bell is what closed it
            _noticePopup = NotificationPopup.Show(_bell, _notices, notice => { if (notice.Action != null) notice.Action(); });
        }

        // ===================== Discovery =====================

        private void Discover(bool now)
        {
            if (_key == null || _discovering) return;
            _discovering = true;
            AdminKey key = _key;
            List<string> targets = _inv.Targets.ToList();
            List<IPAddress> known = _inv.Devices.Select(d => ParseAddress(d.Address)).Where(a => a != null).ToList();
            Task.Run(() =>
            {
                try
                {
                    List<IPAddress> extra = Inventory.Expand(targets);   // resolves names: off the UI thread
                    extra.AddRange(known);
                    _client.Discover(key, extra);
                }
                catch (Exception) { /* network gone: the next poll tries again */ }
                finally { _discovering = false; }
            });
            if (now) SetStatus("Asked every PC on the network to answer.");
        }

        private void OnStatusReceived(AdminReply reply, IPEndPoint from)   // socket thread
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    string id = AdminProtocol.Str(reply.Status, "machineId");
                    bool isNew = id != null && _inv.Find(id) == null && !_knownAtStart.Contains(id);
                    Device d = _inv.Apply(reply.Status, from);
                    if (d == null) return;
                    if (isNew)
                        _notices.Add(NoticeKind.Info, "New PC found",
                            d.Computer + " (" + d.Address + ") runs Card Studio " + d.Version + ".", () => SelectDevice(d.MachineId));
                    NoticeLicense(d);
                    _dirty = true;
                    _relist.Stop();
                    _relist.Start();
                }));
            }
            catch (InvalidOperationException) { }   // closing
        }

        /// <summary>One bell notice per PC and session for a license that needs attention.</summary>
        private void NoticeLicense(Device d)
        {
            int level;
            string text = d.LicenseText(out level);
            if (level == 0 || !_licenseNoticed.Add(d.MachineId)) return;
            _notices.Add(level == 2 ? NoticeKind.Error : NoticeKind.Warning, level == 2 ? "License problem" : "License expires soon",
                d.DisplayName + ": " + text + ". Make a serial for Machine ID " + d.MachineId + ", then Install Serial…",
                () => SelectDevice(d.MachineId));
        }

        /// <summary>PCs that went offline since the last check (only those seen online in this session).</summary>
        private void NoticeOffline()
        {
            foreach (Device d in _inv.Devices)
            {
                if (d.Online) { _wasOnline.Add(d.MachineId); continue; }
                if (_wasOnline.Remove(d.MachineId))
                    _notices.Add(NoticeKind.Info, "PC went offline", d.DisplayName + " stopped answering (Card Studio closed or PC off).",
                        () => SelectDevice(d.MachineId));
            }
        }

        private void SaveIfDirty()
        {
            if (!_dirty) return;
            try
            {
                _inv.Save();
                _dirty = false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                SetStatus("Could not save the list: " + ex.Message, Danger);
            }
        }

        // ===================== List =====================

        private void SetFilter(Filter filter)
        {
            _filter = filter;
            _navAll.Active = filter == Filter.All;
            _navOnline.Active = filter == Filter.Online;
            _navAttention.Active = filter == Filter.Attention;
            RefreshList();
        }

        private Version Newest()
        {
            return _inv.Devices.Select(d => d.ParsedVersion).DefaultIfEmpty(new Version(0, 0)).Max();
        }

        private static bool NeedsAttention(Device d, Version newest)
        {
            int level;
            d.LicenseText(out level);
            return level > 0 || d.ParsedVersion < newest;
        }

        private void RefreshList()
        {
            NoticeOffline();
            Version newest = Newest();
            var selected = new HashSet<string>(SelectedDevices().Select(d => d.MachineId), StringComparer.OrdinalIgnoreCase);
            string first = null;
            if (_grid.FirstDisplayedScrollingRowIndex >= 0 && _grid.FirstDisplayedScrollingRowIndex < _grid.Rows.Count)
                first = ((Device)_grid.Rows[_grid.FirstDisplayedScrollingRowIndex].Tag).MachineId;

            IEnumerable<Device> view = _inv.Devices.Where(Matches);
            if (_filter == Filter.Online) view = view.Where(d => d.Online);
            else if (_filter == Filter.Attention) view = view.Where(d => NeedsAttention(d, newest));
            List<Device> rows = Sort(view, newest).ToList();

            _rebuilding = true;
            try
            {
                _grid.Rows.Clear();
                int firstIndex = -1;
                foreach (Device d in rows)
                {
                    int i = _grid.Rows.Add();
                    DataGridViewRow r = _grid.Rows[i];
                    r.Tag = d;
                    FillRow(r, d, newest);
                    if (selected.Contains(d.MachineId)) r.Selected = true;
                    if (first != null && string.Equals(first, d.MachineId, StringComparison.OrdinalIgnoreCase)) firstIndex = i;
                }
                if (firstIndex >= 0) try { _grid.FirstDisplayedScrollingRowIndex = firstIndex; } catch (InvalidOperationException) { }
                if (selected.Count == 0) _grid.ClearSelection();
                foreach (DataGridViewColumn c in _grid.Columns)
                    c.HeaderCell.SortGlyphDirection = c.Name == _sortColumn
                        ? (_sortDescending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
            }
            finally
            {
                _rebuilding = false;
            }

            _lblEmpty.Text = _inv.Devices.Count == 0
                ? (_key == null ? "Open the administrator's key to look for PCs (Admin Key…)." : "Looking for PCs running Card Studio…")
                : "No PC matches" + (_search.Box.Text.Trim().Length > 0 ? " the search." : " this view.");
            _lblEmpty.Visible = rows.Count == 0;
            _lblEmpty.BringToFront();

            UpdateStats(newest);
            ShowDetails();
        }

        private void FillRow(DataGridViewRow r, Device d, Version newest)
        {
            bool online = d.Online;
            Color main = online ? TextMain : TextMuted;
            Action<string, string, Color> set = (col, text, color) =>
            {
                DataGridViewCell cell = r.Cells[col];
                cell.Value = text ?? "";
                cell.Style.ForeColor = color;
                cell.Style.SelectionForeColor = color == TextMain ? Accent : color;
            };
            set("Status", "", main);
            set("Computer", d.Computer + " " + d.Label, main);   // painted by PaintComputerCell; the value is for copy / tooltips
            r.Cells["Computer"].ToolTipText = d.DisplayName + "\nMachine ID " + d.MachineId;
            set("Address", d.Address, main);
            bool old = d.ParsedVersion < newest;
            set("Version", d.Version + (old ? "  old" : ""), old ? Warning : main);
            if (old) r.Cells["Version"].ToolTipText = "Older than " + newest.ToString(3) + ", the newest version in the list";
            int level;
            string license = d.LicenseText(out level);
            set("License", LicenseShort(d), level == 2 ? Danger : level == 1 ? Warning : main);
            r.Cells["License"].ToolTipText = license;
            set("Phones", online ? (d.MobileRunning ? d.Phones.Count.ToString() : "off") : "", main);
            set("Today", d.PrintedToday > 0 ? d.PrintedToday.ToString("N0") : "", main);
            r.Cells["Status"].ToolTipText = !online ? "Last seen " + Ago(d.LastSeen)
                                          : string.IsNullOrEmpty(d.Busy) ? "Card Studio is open" : d.Busy;
        }

        /// <summary>License in a few words for the grid: "12 days left", "Until 2027-10-01", "Expired"… (full text in the tooltip).</summary>
        private static string LicenseShort(Device d)
        {
            switch (d.License)
            {
                case "Activated":
                    if (d.LicenseDays == null) return "Never expires";
                    if (d.LicenseDays.Value <= 0) return "Last day today";
                    return d.LicenseDays.Value <= 30 ? d.LicenseDays.Value + (d.LicenseDays.Value == 1 ? " day left" : " days left")
                                                     : "Until " + d.LicenseExpires;
                case "Expired": return "Expired";
                case "ClockWrong": return "PC date wrong";
                case "Invalid": return "Wrong serial";
                case "Missing": return "Not activated";
                default: return d.License ?? "";
            }
        }

        private void UpdateStats(Version newest)
        {
            List<Device> all = _inv.Devices;
            List<Device> online = all.Where(d => d.Online).ToList();
            int phones = online.Sum(d => d.Phones.Count);
            int today = online.Sum(d => d.PrintedToday);
            int attention = all.Count(d => NeedsAttention(d, newest));

            _statOnline.Value = online.Count + " / " + all.Count;
            _statOnline.Caption = all.Count == 0 ? "Looking for PCs…" : (all.Count - online.Count) + " offline · " +
                                  online.Count(d => !string.IsNullOrEmpty(d.Busy)) + " busy";
            _statPhones.Value = phones.ToString("N0");
            _statPhones.Caption = "On " + Plural(online.Count(d => d.Phones.Count > 0), "PC") +
                                  " · Mobile App on " + online.Count(d => d.MobileRunning);
            _statPrinted.Value = today.ToString("N0");
            _statAttention.Value = attention.ToString("N0");
            _statAttention.Caption = attention == 0 ? "All licenses and versions OK" : "Licenses or old versions";

            _navAll.Badge = all.Count > 0 ? all.Count.ToString() : null;
            _navOnline.Badge = online.Count > 0 ? online.Count.ToString() : null;
            _navAttention.Badge = attention > 0 ? attention.ToString() : null;
            _lblCount.Text = _grid.Rows.Count + (_grid.Rows.Count == 1 ? " PC" : " PCs");
        }

        private bool Matches(Device d)
        {
            string q = _search == null ? "" : _search.Box.Text.Trim();
            if (q.Length == 0) return true;
            return new[] { d.Computer, d.Label, d.Address, d.User, d.MachineId, d.Version, d.Printer }
                .Any(f => f != null && f.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        private IEnumerable<Device> Sort(IEnumerable<Device> devices, Version newest)
        {
            Func<Device, IComparable> key;
            switch (_sortColumn)
            {
                case "Computer": key = d => d.Computer ?? ""; break;
                case "Address": key = d => AddressKey(d.Address); break;
                case "Version": key = d => d.ParsedVersion; break;
                case "License": key = d => d.License == "Activated" ? (d.LicenseDays ?? int.MaxValue) : int.MinValue; break;
                case "Phones": key = d => d.Online ? d.Phones.Count : -1; break;
                case "Today": key = d => d.PrintedToday; break;
                default: key = d => !d.Online ? 2 : string.IsNullOrEmpty(d.Busy) ? 0 : 1; break;   // online, busy, offline
            }
            IOrderedEnumerable<Device> sorted = _sortDescending ? devices.OrderByDescending(key) : devices.OrderBy(key);
            return sorted.ThenBy(d => d.Computer ?? "", StringComparer.CurrentCultureIgnoreCase);
        }

        private static long AddressKey(string address)
        {
            IPAddress ip = ParseAddress(address);
            if (ip == null) return long.MaxValue;
            byte[] b = ip.GetAddressBytes();
            return ((long)b[0] << 24) | ((long)b[1] << 16) | ((long)b[2] << 8) | b[3];
        }

        private List<Device> SelectedDevices()
        {
            if (_grid == null) return new List<Device>();
            return _grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => (Device)r.Tag).ToList();
        }

        /// <summary>Shows <paramref name="machineId"/> selected (from a bell notice), switching to All PCs when it is filtered out.</summary>
        private void SelectDevice(string machineId)
        {
            Func<DataGridViewRow> find = () => _grid.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(r => string.Equals(((Device)r.Tag).MachineId, machineId, StringComparison.OrdinalIgnoreCase));
            DataGridViewRow row = find();
            if (row == null)
            {
                _search.Box.Text = "";
                SetFilter(Filter.All);
                row = find();
            }
            if (row == null) return;
            _grid.ClearSelection();
            row.Selected = true;
            _grid.CurrentCell = row.Cells["Computer"];
            _grid.Focus();
        }

        // ===================== Commands =====================

        /// <summary>The selected PCs that are online, or null after telling the user why there are none.</summary>
        private List<Device> OnlineTargets(bool onlyOne)
        {
            if (_key == null)
            {
                Warn("No administrator key", "Open the administrator's key first (Admin Key…).");
                return null;
            }
            List<Device> sel = SelectedDevices();
            if (sel.Count == 0) { Warn("No PC selected", "Select one or more PCs in the list."); return null; }
            if (onlyOne && sel.Count > 1) { Warn("One PC only", "Select only one PC: a serial number works on one PC only."); return null; }
            List<Device> online = sel.Where(d => d.Online && ParseAddress(d.Address) != null).ToList();
            if (online.Count == 0)
            {
                Warn("PC offline", sel.Count == 1
                    ? sel[0].DisplayName + " is offline: Card Studio is not open there, or the PC is off."
                    : "The selected PCs are offline.");
                return null;
            }
            if (online.Count < sel.Count &&
                AppDialog.Show(this, (sel.Count - online.Count) + " of the selected PCs are offline and will be skipped. Continue?",
                               "Some PCs are offline", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return null;
            return online;
        }

        private async Task Run(List<Device> targets, string command, string arg, string what)
        {
            AdminKey key = _key;
            SetStatus(what + ": sending to " + Names(targets) + "…");
            UseWaitCursor = true;
            Tuple<Device, AdminReply>[] results;
            try
            {
                results = await Task.WhenAll(targets.Select(async d =>
                    Tuple.Create(d, await _client.SendAsync(key, d.MachineId, ParseAddress(d.Address), command, arg))));
            }
            finally
            {
                UseWaitCursor = false;
            }

            var lines = new List<string>();
            int ok = 0;
            foreach (var r in results)
            {
                bool done = r.Item2 != null && r.Item2.Ok;
                string outcome = r.Item2 == null
                    ? "No answer (Card Studio closed, PC off, or the firewall blocks UDP " + AdminProtocol.Port + ")."
                    : r.Item2.Message;
                if (done) ok++;
                r.Item1.LastResult = DateTime.Now.ToString("HH:mm") + " " + what + ": " + (done ? "OK · " : "Failed · ") + outcome;
                lines.Add((done ? "✓  " : "✗  ") + r.Item1.DisplayName + ": " + outcome);
            }
            _dirty = true;
            RefreshList();

            bool allOk = ok == results.Length;
            string summary = what + ": " + ok + " of " + results.Length + (results.Length == 1 ? " PC" : " PCs") + " done.";
            SetStatus(summary, allOk ? Success : Warning);
            _notices.Add(allOk ? NoticeKind.Success : NoticeKind.Warning, what, string.Join("\n", lines));
            if (!allOk || results.Length > 1)
                AppDialog.Show(this, string.Join("\n\n", lines), what, MessageBoxButtons.OK,
                               allOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private async void SendMessage()
        {
            List<Device> targets = OnlineTargets(false);
            if (targets == null) return;
            string text = Prompt.Ask(this, "Send Message", "To " + Names(targets), IcoMessage,
                "Shown on the PC in a box (or under Card Studio's bell while a window is open there). At most " +
                AdminProtocol.MaxMessageLength + " characters; Khmer works.",
                "", true, t =>
                {
                    if (t.Trim().Length == 0) return "Type the message.";
                    return t.Length > AdminProtocol.MaxMessageLength ? "The message is too long (" + t.Length + " characters)." : null;
                }, "Send");
            if (text == null) return;
            await Run(targets, AdminProtocol.Message, text.Trim(), "Message");
        }

        private async void CheckUpdates()
        {
            List<Device> targets = OnlineTargets(false);
            if (targets == null) return;
            await Run(targets, AdminProtocol.CheckUpdates, "", "Check for Updates");
        }

        private async void InstallSerial()
        {
            List<Device> targets = OnlineTargets(true);
            if (targets == null) return;
            Device d = targets[0];
            string serial = Prompt.Ask(this, "Install Serial", d.DisplayName + " · Machine ID " + d.MachineId, Theme.IcoKey,
                "Make the serial for this Machine ID with the Serial Generator, then paste it here. It is checked before it is sent.",
                "", false, s => CheckSerial(s, d.MachineId), "Install",
                "Copy Machine ID", p => { Clipboard.SetText(d.MachineId); p.Note("Machine ID copied: " + d.MachineId); });
            if (serial == null) return;
            await Run(targets, AdminProtocol.InstallSerial, serial.Trim(), "Install Serial");
        }

        private static string CheckSerial(string serial, string machineId)
        {
            SerialData data;
            switch (SerialFormat.Decode(serial, out data))
            {
                case SerialShape.Empty: return "Paste the serial number.";
                case SerialShape.NotASerial: return "This is not a Card Studio serial number. Paste the whole serial.";
                case SerialShape.Typo: return "The serial number has a typo. Paste it again instead of typing it.";
            }
            if (!LicenseService.IsValidFor(serial, machineId))
                return "This serial number is not for this PC. Make one for Machine ID " + machineId + ".";
            if (data.Expires != null && data.Expires.Value.Date < DateTime.Today)
                return "This serial number expired on " + data.Expires.Value.ToString("yyyy-MM-dd") + ".";
            return null;
        }

        private async void RevokePhones()
        {
            List<Device> targets = OnlineTargets(false);
            if (targets == null) return;
            if (AppDialog.Show(this, "Make a new mobile pairing code on " + Names(targets) + "?\n\n" +
                                     "Every phone paired with these PCs is disconnected and must scan the new QR code " +
                                     "(Card Studio → Mobile App…). Use this when a phone was lost or the code was shared by mistake.",
                    "Disconnect Phones", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            await Run(targets, AdminProtocol.RevokePhones, "", "Disconnect Phones");
        }

        // ===================== List management =====================

        private void SetLabel()
        {
            List<Device> sel = SelectedDevices();
            if (sel.Count == 0) { Warn("No PC selected", "Select one or more PCs in the list."); return; }
            string label = Prompt.Ask(this, "Label", Names(sel), IcoTag,
                "Your own name for the PC, e.g. \"Room 204, front desk\". Only kept in this list, never sent to the PC.",
                sel.Count == 1 ? sel[0].Label : "", false, t => t.Length > 80 ? "At most 80 characters." : null, "Save");
            if (label == null) return;
            foreach (Device d in sel) d.Label = label.Trim();
            _dirty = true;
            RefreshList();
        }

        private void RemoveSelected()
        {
            List<Device> sel = SelectedDevices();
            if (sel.Count == 0) return;
            int online = sel.Count(d => d.Online);
            if (AppDialog.Show(this, "Remove " + Names(sel) + " from the list?" +
                                     (online > 0 ? "\n\nPCs that are online show up again at the next refresh." : ""),
                    "Remove from List", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            foreach (Device d in sel) _inv.Devices.Remove(d);
            _dirty = true;
            RefreshList();
        }

        private void CopyMachineIds()
        {
            List<Device> sel = SelectedDevices();
            if (sel.Count == 0) return;
            Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(d => sel.Count == 1 ? d.MachineId : d.Computer + "\t" + d.MachineId)));
            SetStatus(sel.Count == 1 ? "Machine ID of " + sel[0].Computer + " copied." : sel.Count + " Machine IDs copied.");
        }

        private void EditTargets()
        {
            string text = Prompt.Ask(this, "Addresses", "PCs on other networks", IcoNetwork,
                "PCs on this network are found by broadcast. Add PCs behind a router, one per line: an address (192.168.2.15), " +
                "a range (192.168.2.10-60), a network (192.168.2.0/24) or a computer name.",
                string.Join(Environment.NewLine, _inv.Targets), true, t =>
                {
                    foreach (string line in Lines(t))
                    {
                        string error;
                        Inventory.ExpandOne(line, false, out error);
                        if (error != null) return error;
                    }
                    return null;
                }, "Save");
            if (text == null) return;
            _inv.Targets = Lines(text).ToList();
            _dirty = true;
            SaveIfDirty();
            Discover(true);
        }

        private static IEnumerable<string> Lines(string text)
        {
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct();
        }

        private void ExportCsv()
        {
            using (var dlg = new SaveFileDialog { Filter = "CSV file (*.csv)|*.csv", FileName = "card-studio-pcs-" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Version newest = Newest();
                var sb = new StringBuilder();
                string[] header =
                {
                    "Computer", "Label", "Online", "Last seen", "Address", "Machine ID", "User", "Version", "Windows", "License",
                    "License expires", "Mobile App", "Phones", "Students", "Source", "Printer", "Printed today", "Printed total", "Last print"
                };
                sb.AppendLine(string.Join(",", header.Select(Csv)));
                foreach (Device d in Sort(_inv.Devices, newest))
                {
                    int level;
                    sb.AppendLine(string.Join(",", new[]
                    {
                        d.Computer, d.Label, d.Online ? "yes" : "no", d.LastSeen.ToString("yyyy-MM-dd HH:mm"), d.Address, d.MachineId, d.User,
                        d.Version, d.Os, d.LicenseText(out level), d.LicenseExpires, d.MobileRunning ? "on" : "off", string.Join("; ", d.Phones),
                        d.Students.ToString(), d.Source, d.Printer, d.PrintedToday.ToString(), d.PrintedTotal.ToString(), d.LastPrint
                    }.Select(Csv)));
                }
                try
                {
                    File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));   // BOM: Excel reads Khmer names right
                    SetStatus("Saved " + Path.GetFileName(dlg.FileName) + ".");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    AppDialog.Show(this, "Could not save the file:\n" + ex.Message, "Export CSV", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0) value = "'" + value;   // never a formula in Excel
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        // ===================== Key =====================

        private void UseKey(string path, bool remember)
        {
            if (path == null)
            {
                _key = null;
                _lblKey.Text = "⚠  No administrator key";
                _lblKey.ForeColor = Danger;
                _notices.Add(NoticeKind.Error, "No administrator key",
                    "Open admin-private.key (Admin Key…). Without it no PC answers.", ShowKey);
                return;
            }
            AdminKey key;
            try
            {
                key = AdminKey.Load(path);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                AppDialog.Show(this, ex.Message, "Admin Key", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _key = key;   // the previous key may still be signing a discover: left to the garbage collector
            if (remember) AdminKey.RememberedPath = key.Path;
            _lblKey.Text = key.MatchesApp ? "🔑  Admin key: " + Path.GetFileName(key.Path) : "⚠  Key not built into Card Studio";
            _lblKey.ForeColor = key.MatchesApp ? TextMuted : Danger;
            _tip.SetToolTip(_lblKey, key.Path + "\nClick to open, create or show the key");
            if (!key.MatchesApp)
                _notices.Add(NoticeKind.Error, "Wrong administrator key",
                    "This key is not the one built into Card Studio: the PCs will not answer.", ShowKey);
            if (remember) Discover(true);
        }

        /// <summary>The key popup: where it is, whether Card Studio accepts it, its public key; open or create another.</summary>
        private void ShowKey()
        {
            using (var dlg = new ModalForm
            {
                Text = "Admin Key", Glyph = Theme.IcoKey, Resizable = false,
                Subtitle = "Signs every command; the PCs only answer the key built into Card Studio"
            })
            {
                dlg.ClientSize = new Size(600, ModalForm.HeaderHeight + 250 + ModalUi.FooterHeight);
                var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(24, 18, 24, 8), BackColor = Surface };
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                Func<string, Label> name = t => new Label { Text = t, AutoSize = true, ForeColor = TextMuted, Font = Theme.Small, Margin = new Padding(0, 6, 8, 10) };
                Func<string, Color, Label> value = (t, c) => new Label
                {
                    Text = t, AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = c, UseMnemonic = false, Margin = new Padding(0, 5, 0, 10)
                };
                body.Controls.Add(name("Key file"), 0, 0);
                body.Controls.Add(value(_key == null ? "None opened" : _key.Path, _key == null ? Danger : TextMain), 1, 0);
                body.Controls.Add(name("Status"), 0, 1);
                body.Controls.Add(value(_key == null ? "No PC answers without the key."
                                        : _key.MatchesApp ? "✓  Built into Card Studio: the PCs answer this key."
                                        : "✗  Not the key built into Card Studio: the PCs ignore it.",
                                        _key == null || !_key.MatchesApp ? Danger : Success), 1, 1);
                body.Controls.Add(name("Public key"), 0, 2);
                var pub = new TextBox
                {
                    Text = _key == null ? "" : _key.PublicKey, ReadOnly = true, Multiline = true, Height = 52, Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.FixedSingle, BackColor = SurfaceAlt, ForeColor = TextMain, Font = new Font("Consolas", 9f),
                    Margin = new Padding(0, 2, 0, 10)
                };
                body.Controls.Add(pub, 1, 2);
                body.Controls.Add(value("Keep a backup of the key file. Never give it, or this tool, to users.", TextSoft), 1, 3);

                FlowLayoutPanel footer = ModalUi.Footer();
                PillButton close = ModalUi.Button("Close", PillKind.Secondary, (s, e) => dlg.Close());
                footer.Controls.Add(close);
                footer.Controls.Add(ModalUi.Button("Open Key…", PillKind.Primary, (s, e) => { dlg.Close(); ChooseKey(); }));
                footer.Controls.Add(ModalUi.Button("Create New Key…", PillKind.Ghost, (s, e) => { dlg.Close(); CreateKey(); }));
                if (_key != null)
                    footer.Controls.Add(ModalUi.Button("Copy Public Key", PillKind.Ghost, (s, e) => Clipboard.SetText(_key.PublicKey)));
                dlg.Controls.Add(body);
                dlg.Controls.Add(footer);
                dlg.CancelButton = close;
                dlg.ShowDialog(this);
            }
        }

        private void ChooseKey()
        {
            using (var dlg = new OpenFileDialog { Filter = "Admin key (*.key)|*.key|All files (*.*)|*.*", FileName = AdminKey.FileName, Title = "Open the administrator's key" })
            {
                if (_key != null) dlg.InitialDirectory = Path.GetDirectoryName(_key.Path);
                if (dlg.ShowDialog(this) == DialogResult.OK) UseKey(dlg.FileName, true);
            }
        }

        private void CreateKey()
        {
            if (AppDialog.Show(this,
                    "Make a new administrator key?\n\n" +
                    "The PCs only answer the key built into Card Studio. After making a new key, put its public key in " +
                    "NPIC_CARD_APP\\Services\\AdminProtocol.cs (PublicKey), rebuild Card Studio and install it on every PC.\n\n" +
                    "Only do this the first time, or when the old key was lost or leaked. Keep a backup of the new key file.",
                    "Create New Key", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            using (var dlg = new SaveFileDialog { Filter = "Admin key (*.key)|*.key", FileName = AdminKey.FileName, OverwritePrompt = false })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    AdminKey.Create(dlg.FileName).Dispose();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.Cryptography.CryptographicException)
                {
                    AppDialog.Show(this, "Could not make the key:\n" + ex.Message, "Create New Key", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                UseKey(dlg.FileName, true);
                Prompt.Show(this, "Public Key", "New key made", Theme.IcoKey,
                    "Put this value in NPIC_CARD_APP\\Services\\AdminProtocol.cs (PublicKey), then rebuild Card Studio and install it on every PC.",
                    _key.PublicKey, "Copy", p => { Clipboard.SetText(_key.PublicKey); p.Note("Public key copied."); });
            }
        }

        // ===================== App =====================

        private void ToggleDarkMode()
        {
            bool dark = !Theme.Dark;
            if (AppDialog.Show(this, "Card Studio Admin restarts to switch to " + (dark ? "dark" : "light") + " mode.",
                    dark ? "Dark Mode" : "Light Mode", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;
            _settings.DarkMode = dark;
            _settings.Save();
            Application.Restart();
        }

        // ===================== Helpers =====================

        private static IPAddress ParseAddress(string text)
        {
            IPAddress ip;
            return !string.IsNullOrEmpty(text) && IPAddress.TryParse(text, out ip) ? ip : null;
        }

        private static string Plural(int n, string word)
        {
            return n + " " + word + (n == 1 ? "" : "s");
        }

        private static string Names(List<Device> devices)
        {
            return devices.Count == 1 ? devices[0].DisplayName : devices.Count + " PCs";
        }

        private static string Ago(DateTime t)
        {
            if (t == DateTime.MinValue) return "";
            TimeSpan ago = DateTime.Now - t;
            if (ago.TotalMinutes < 1) return "just now";
            if (ago.TotalMinutes < 60) return (int)ago.TotalMinutes + " min ago";
            if (ago.TotalHours < 24) return (int)ago.TotalHours + " h ago";
            return t.ToString("yyyy-MM-dd HH:mm");
        }

        private void Warn(string title, string text)
        {
            AppDialog.Show(this, text, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
