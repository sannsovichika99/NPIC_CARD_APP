using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using NPIC_CARD_APP.Services;

namespace CardStudio.Admin
{
    /// <summary>
    /// Card Studio Admin: every PC running Card Studio on the network (found by broadcast every 15 s, plus the extra
    /// addresses for other networks), with its version, license, phones and print counts, and commands for the selected PCs.
    /// </summary>
    internal sealed class AdminForm : Form
    {
        // Card Studio's light palette (UiKit.Theme / AccentPalette default violet), as in the Serial Generator.
        internal static readonly Color Accent = Color.FromArgb(108, 99, 245);
        internal static readonly Color AppBack = Color.FromArgb(245, 246, 250);
        internal static readonly Color Surface = Color.White;
        internal static readonly Color Border = Color.FromArgb(228, 229, 238);
        internal static readonly Color TextMain = Color.FromArgb(43, 45, 66);
        internal static readonly Color TextSoft = Color.FromArgb(104, 107, 130);
        internal static readonly Color Success = Color.FromArgb(34, 160, 100);
        internal static readonly Color Warning = Color.FromArgb(196, 110, 0);
        internal static readonly Color Danger = Color.FromArgb(214, 48, 72);
        internal static readonly Color Offline = Color.FromArgb(150, 152, 170);

        internal static readonly Font UiFont = new Font("Segoe UI", 9.75f);
        internal static readonly Font BoldFont = new Font("Segoe UI Semibold", 9.75f);
        internal static readonly Font TitleFont = new Font("Segoe UI Semibold", 14f);
        internal static readonly Font Mono = new Font("Consolas", 10f);

        private const int PollMs = 15000;

        private readonly Inventory _inv = Inventory.Load();
        private readonly AdminClient _client = new AdminClient();
        private AdminKey _key;

        private readonly Label _lblSummary;
        private readonly ToolStripTextBox _search;
        private readonly ListView _list;
        private readonly TextBox _details;
        private readonly ToolStripStatusLabel _lblKey, _lblAction;
        private readonly Timer _poll, _tick, _relist;

        private int _sortColumn;
        private bool _sortDescending;
        private volatile bool _discovering;
        private bool _dirty;

        private enum Col { Status, Computer, Label, Address, Version, License, Phones, Students, Today, Busy, LastSeen }

        public AdminForm()
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            Text = "Card Studio Admin " + v.ToString(3);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            Font = UiFont;
            BackColor = AppBack;
            ForeColor = TextMain;
            StartPosition = FormStartPosition.CenterScreen;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1280, work.Width), Math.Min(760, work.Height));
            MinimumSize = new Size(Math.Min(860, work.Width), Math.Min(480, work.Height));
            KeyPreview = true;

            // ===================== Header =====================
            var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Surface, Padding = new Padding(18, 10, 18, 8) };
            var title = new Label { Text = "Card Studio Admin", Font = TitleFont, AutoSize = true, Location = new Point(16, 8) };
            _lblSummary = new Label { AutoSize = true, ForeColor = TextSoft, Location = new Point(18, 40) };
            header.Controls.Add(title);
            header.Controls.Add(_lblSummary);
            header.Paint += (s, e) => { using (var p = new Pen(Border)) e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1); };

            // ===================== Toolbar =====================
            var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(10, 4, 10, 4), BackColor = Surface, RenderMode = ToolStripRenderMode.System };
            bar.Items.Add(Button("Refresh", "Ask every PC again now (F5)", (s, e) => Discover(true)));
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(Button("Message…", "Show a message on the selected PCs", (s, e) => SendMessage()));
            bar.Items.Add(Button("Check for Updates", "Open Check for Updates on the selected PCs", (s, e) => CheckUpdates()));
            bar.Items.Add(Button("Install Serial…", "Send a new serial number (license) to the selected PC", (s, e) => InstallSerial()));
            bar.Items.Add(Button("Disconnect Phones", "New mobile pairing code: the PCs' phones must scan the QR code again", (s, e) => RevokePhones()));
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(Button("Label…", "Your own name for the selected PCs (room, desk…)", (s, e) => SetLabel()));
            bar.Items.Add(Button("Remove", "Remove the selected PCs from the list (Delete)", (s, e) => RemoveSelected()));
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(Button("Addresses…", "PCs on other networks (behind a router): addresses to ask directly", (s, e) => EditTargets()));
            bar.Items.Add(Button("Export CSV…", "Save the list as a CSV file (Excel)", (s, e) => ExportCsv()));
            var keyMenu = new ToolStripDropDownButton("Key") { ToolTipText = "The administrator's signing key" };
            keyMenu.DropDownItems.Add("Open Key…", null, (s, e) => ChooseKey());
            keyMenu.DropDownItems.Add("Show Public Key…", null, (s, e) => ShowPublicKey(false));
            keyMenu.DropDownItems.Add(new ToolStripSeparator());
            keyMenu.DropDownItems.Add("Create New Key…", null, (s, e) => CreateKey());
            bar.Items.Add(keyMenu);
            _search = new ToolStripTextBox { Alignment = ToolStripItemAlignment.Right, AutoSize = false, Width = 220, ToolTipText = "Search: computer, label, address, user, Machine ID, version (Ctrl+F)" };
            _search.TextChanged += (s, e) => RefreshList();
            bar.Items.Add(_search);
            bar.Items.Add(new ToolStripLabel("Search") { Alignment = ToolStripItemAlignment.Right, ForeColor = TextSoft });

            // ===================== List + details =====================
            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true,
                BorderStyle = BorderStyle.None, BackColor = Surface, Font = UiFont
            };
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_list, true, null);
            AddColumn("Status", 84);
            AddColumn("Computer", 140);
            AddColumn("Label", 110);
            AddColumn("Address", 105);
            AddColumn("Version", 80);
            AddColumn("License", 170);
            AddColumn("Phones", 58, HorizontalAlignment.Right);
            AddColumn("Students", 68, HorizontalAlignment.Right);
            AddColumn("Today", 56, HorizontalAlignment.Right);
            AddColumn("Busy", 150);
            AddColumn("Last seen", 96);
            _list.ColumnClick += (s, e) =>
            {
                if (_sortColumn == e.Column) _sortDescending = !_sortDescending;
                else { _sortColumn = e.Column; _sortDescending = false; }
                RefreshList();
            };
            _list.SelectedIndexChanged += (s, e) => ShowDetails();
            _list.ContextMenuStrip = BuildContextMenu();

            _details = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                BackColor = Surface, Font = Mono, WordWrap = true
            };
            var detailsBox = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Surface };
            detailsBox.Controls.Add(_details);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = Border, SplitterWidth = 1 };
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(detailsBox);

            // ===================== Status bar =====================
            var status = new StatusStrip { BackColor = Surface, SizingGrip = true };
            _lblKey = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleLeft };
            _lblAction = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSoft };
            status.Items.Add(_lblKey);
            status.Items.Add(_lblAction);

            Controls.Add(split);
            Controls.Add(bar);
            Controls.Add(header);
            Controls.Add(status);
            Shown += (s, e) => { split.SplitterDistance = Math.Max(420, split.Width - 340); };

            // ===================== Timers =====================
            _poll = new Timer { Interval = PollMs };
            _poll.Tick += (s, e) => Discover(false);
            _tick = new Timer { Interval = 5000 };   // online → offline, "last seen", saving
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
                case Keys.Control | Keys.F: _search.Focus(); _search.SelectAll(); return true;
                case Keys.Control | Keys.A:
                    if (_list.Focused) { foreach (ListViewItem i in _list.Items) i.Selected = true; return true; }
                    break;
                case Keys.Control | Keys.C:
                    if (_list.Focused) { CopyMachineIds(); return true; }
                    break;
                case Keys.Delete:
                    if (_list.Focused) { RemoveSelected(); return true; }
                    break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ===================== Discovery =====================

        private void Discover(bool now)
        {
            if (_key == null) return;
            if (_discovering) return;
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
            if (now) SetAction("Asked every PC on the network to answer.");
        }

        private void OnStatusReceived(AdminReply reply, IPEndPoint from)   // socket thread
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (_inv.Apply(reply.Status, from) == null) return;
                    _dirty = true;
                    _relist.Stop();
                    _relist.Start();
                }));
            }
            catch (InvalidOperationException) { }   // closing
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
                SetAction("Could not save the list: " + ex.Message);
            }
        }

        // ===================== List =====================

        private void AddColumn(string text, int width, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            _list.Columns.Add(new ColumnHeader { Text = text, Width = width, TextAlign = align });
        }

        private void RefreshList()
        {
            var selected = new HashSet<string>(SelectedDevices().Select(d => d.MachineId), StringComparer.OrdinalIgnoreCase);
            string top = _list.TopItem == null ? null : ((Device)_list.TopItem.Tag).MachineId;
            Version newest = _inv.Devices.Select(d => d.ParsedVersion).DefaultIfEmpty(new Version(0, 0)).Max();

            IEnumerable<Device> view = _inv.Devices.Where(Matches);
            List<Device> sorted = Sort(view).ToList();

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                ListViewItem topItem = null;
                foreach (Device d in sorted)
                {
                    ListViewItem item = MakeItem(d, newest);
                    _list.Items.Add(item);
                    if (selected.Contains(d.MachineId)) item.Selected = true;
                    if (top != null && string.Equals(top, d.MachineId, StringComparison.OrdinalIgnoreCase)) topItem = item;
                }
                if (topItem != null) try { _list.TopItem = topItem; } catch (Exception) { }
            }
            finally
            {
                _list.EndUpdate();
            }
            UpdateSummary();
            ShowDetails();
        }

        private bool Matches(Device d)
        {
            string q = _search == null ? "" : _search.Text.Trim();
            if (q.Length == 0) return true;
            return new[] { d.Computer, d.Label, d.Address, d.User, d.MachineId, d.Version, d.Printer }
                .Any(f => f != null && f.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        private IEnumerable<Device> Sort(IEnumerable<Device> devices)
        {
            Func<Device, IComparable> key;
            switch ((Col)_sortColumn)
            {
                case Col.Computer: key = d => d.Computer ?? ""; break;
                case Col.Label: key = d => d.Label ?? ""; break;
                case Col.Address: key = d => AddressKey(d.Address); break;
                case Col.Version: key = d => d.ParsedVersion; break;
                case Col.License: key = d => d.License == "Activated" ? (d.LicenseDays ?? int.MaxValue) : int.MinValue; break;
                case Col.Phones: key = d => d.Online ? d.Phones.Count : -1; break;
                case Col.Students: key = d => d.Students; break;
                case Col.Today: key = d => d.PrintedToday; break;
                case Col.Busy: key = d => d.Online ? d.Busy ?? "" : "~"; break;
                case Col.LastSeen: key = d => d.LastSeen; break;
                default: key = d => d.Online ? 0 : 1; break;   // online first
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

        private ListViewItem MakeItem(Device d, Version newest)
        {
            bool online = d.Online;
            Color baseColor = online ? TextMain : Offline;
            var item = new ListViewItem(online ? "● Online" : "○ Offline") { Tag = d, UseItemStyleForSubItems = false, ForeColor = online ? Success : Offline };
            Func<string, Color, ListViewItem.ListViewSubItem> add = (text, color) =>
            {
                var sub = new ListViewItem.ListViewSubItem(item, text ?? "") { ForeColor = color };
                item.SubItems.Add(sub);
                return sub;
            };

            add(d.Computer, baseColor).Font = online ? BoldFont : UiFont;
            add(d.Label, baseColor);
            add(d.Address, baseColor);
            bool old = d.ParsedVersion < newest;
            add(d.Version + (old ? "  (old)" : ""), online && old ? Warning : baseColor);
            int level;
            string license = d.LicenseText(out level);
            add(license, level == 2 ? Danger : level == 1 ? Warning : baseColor);
            add(online ? (d.MobileRunning ? d.Phones.Count.ToString() : "off") : "", baseColor);
            add(d.Students > 0 ? d.Students.ToString("N0") : "", baseColor);
            add(d.PrintedToday > 0 ? d.PrintedToday.ToString("N0") : "", baseColor);
            add(online ? d.Busy : "", Warning);
            add(online ? "now" : Ago(d.LastSeen), baseColor);
            return item;
        }

        private void UpdateSummary()
        {
            List<Device> online = _inv.Devices.Where(d => d.Online).ToList();
            int phones = online.Sum(d => d.Phones.Count);
            int today = online.Sum(d => d.PrintedToday);
            int attention = _inv.Devices.Count(d => { int level; d.LicenseText(out level); return level > 0; });
            var sb = new StringBuilder();
            sb.Append(online.Count).Append(" of ").Append(_inv.Devices.Count).Append(_inv.Devices.Count == 1 ? " PC online" : " PCs online");
            sb.Append("  ·  ").Append(phones).Append(phones == 1 ? " phone connected" : " phones connected");
            sb.Append("  ·  ").Append(today.ToString("N0")).Append(" card sides printed today");
            if (attention > 0) sb.Append("  ·  ").Append(attention).Append(attention == 1 ? " license needs attention" : " licenses need attention");
            _lblSummary.Text = sb.ToString();
        }

        private void ShowDetails()
        {
            List<Device> sel = SelectedDevices();
            if (sel.Count == 0)
            {
                _details.Text = _inv.Devices.Count == 0
                    ? "No PC has answered yet." + Environment.NewLine + Environment.NewLine +
                      "• Card Studio must be open on the PCs (version with Card Studio Admin support, installed with its setup so Windows Firewall lets UDP " + AdminProtocol.Port + " in)." + Environment.NewLine +
                      "• PCs behind a router: add their addresses with Addresses…" + Environment.NewLine +
                      "• The key in the status bar must match the one built into Card Studio."
                    : "Select a PC to see its details.";
                return;
            }
            if (sel.Count > 1)
            {
                _details.Text = sel.Count + " PCs selected:" + Environment.NewLine + Environment.NewLine +
                                string.Join(Environment.NewLine, sel.Select(d => (d.Online ? "● " : "○ ") + d.DisplayName));
                return;
            }

            Device x = sel[0];
            int level;
            var lines = new List<KeyValuePair<string, string>>
            {
                Line("Computer", x.DisplayName),
                Line("Status", x.Online ? "Online" + (string.IsNullOrEmpty(x.Busy) ? "" : " · busy: " + x.Busy) : "Offline · last seen " + x.LastSeen.ToString("yyyy-MM-dd HH:mm")),
                Line("Machine ID", x.MachineId),
                Line("User", x.User),
                Line("Address", x.Address + (x.Addresses.Count > 1 ? "  (all: " + string.Join(", ", x.Addresses) + ")" : "")),
                Line("Card Studio", x.Version),
                Line("Windows", x.Os),
                Line("License", x.LicenseText(out level)),
                Line("Mobile App", !x.MobileRunning ? "Off" : "On, port " + x.MobilePort + " · " + (x.Phones.Count == 0 ? "no phone connected" : x.Phones.Count + " connected")),
            };
            foreach (string phone in x.Phones) lines.Add(Line("", "  " + phone));
            lines.Add(Line("Students", x.Students == 0 ? "none loaded" : x.Students.ToString("N0") + (string.IsNullOrEmpty(x.Source) ? "" : " from " + x.Source)));
            lines.Add(Line("Printer", x.Printer));
            lines.Add(Line("Printed", x.PrintedToday.ToString("N0") + " today · " + x.PrintedTotal.ToString("N0") + " in total" +
                                      (string.IsNullOrEmpty(x.LastPrint) ? "" : " · last " + x.LastPrint)));
            lines.Add(Line("Running since", x.StartedAt));
            lines.Add(Line("PC clock", x.PcTime));
            lines.Add(Line("First seen", x.FirstSeen.ToString("yyyy-MM-dd HH:mm")));
            if (!string.IsNullOrEmpty(x.LastResult)) lines.Add(Line("Last command", x.LastResult));

            var sb = new StringBuilder();
            foreach (var l in lines) sb.Append(l.Key.PadRight(15)).Append(l.Value).Append(Environment.NewLine);
            _details.Text = sb.ToString();
        }

        private static KeyValuePair<string, string> Line(string k, string v) { return new KeyValuePair<string, string>(k, v ?? ""); }

        private List<Device> SelectedDevices()
        {
            if (_list == null) return new List<Device>();
            return _list.SelectedItems.Cast<ListViewItem>().Select(i => (Device)i.Tag).ToList();
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Message…", null, (s, e) => SendMessage());
            menu.Items.Add("Check for Updates", null, (s, e) => CheckUpdates());
            menu.Items.Add("Install Serial…", null, (s, e) => InstallSerial());
            menu.Items.Add("Disconnect Phones", null, (s, e) => RevokePhones());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Copy Machine ID", null, (s, e) => CopyMachineIds());
            menu.Items.Add("Label…", null, (s, e) => SetLabel());
            menu.Items.Add("Remove", null, (s, e) => RemoveSelected());
            return menu;
        }

        // ===================== Commands =====================

        /// <summary>The selected PCs that are online, or null after telling the user why there are none.</summary>
        private List<Device> OnlineTargets(bool onlyOne)
        {
            if (_key == null)
            {
                Warn("Open the administrator's key first (Key ▸ Open Key…).");
                return null;
            }
            List<Device> sel = SelectedDevices();
            if (sel.Count == 0) { Warn("Select one or more PCs in the list."); return null; }
            if (onlyOne && sel.Count > 1) { Warn("Select only one PC: a serial number works on one PC only."); return null; }
            List<Device> online = sel.Where(d => d.Online && ParseAddress(d.Address) != null).ToList();
            if (online.Count == 0)
            {
                Warn(sel.Count == 1 ? sel[0].DisplayName + " is offline: Card Studio is not open there, or the PC is off."
                                    : "The selected PCs are offline.");
                return null;
            }
            if (online.Count < sel.Count &&
                MessageBox.Show(this, (sel.Count - online.Count) + " of the selected PCs are offline and will be skipped. Continue?", Text,
                                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return null;
            return online;
        }

        private async Task Run(List<Device> targets, string command, string arg, string what)
        {
            AdminKey key = _key;
            SetAction(what + ": sending to " + targets.Count + (targets.Count == 1 ? " PC…" : " PCs…"));
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
                string outcome = r.Item2 == null
                    ? "No answer (Card Studio closed, PC off, or the firewall blocks UDP " + AdminProtocol.Port + ")."
                    : r.Item2.Message;
                if (r.Item2 != null && r.Item2.Ok) ok++;
                r.Item1.LastResult = DateTime.Now.ToString("HH:mm") + " " + what + ": " + (r.Item2 != null && r.Item2.Ok ? "OK · " : "Failed · ") + outcome;
                lines.Add((r.Item2 != null && r.Item2.Ok ? "✓ " : "✗ ") + r.Item1.DisplayName + ": " + outcome);
            }
            _dirty = true;
            RefreshList();
            SetAction(what + ": " + ok + " of " + results.Length + " done.");
            if (ok < results.Length || results.Length > 1)
                MessageBox.Show(this, string.Join(Environment.NewLine + Environment.NewLine, lines), what,
                                MessageBoxButtons.OK, ok == results.Length ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private async void SendMessage()
        {
            List<Device> targets = OnlineTargets(false);
            if (targets == null) return;
            string text = Prompt.Ask(this, "Message", "Shown on " + Names(targets) + " in a box, and kept under the bell of Card Studio. " +
                                                     "At most " + AdminProtocol.MaxMessageLength + " characters.",
                "", true, t =>
                {
                    if (t.Trim().Length == 0) return "Type the message.";
                    return t.Length > AdminProtocol.MaxMessageLength ? "The message is too long (" + t.Length + " characters)." : null;
                });
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
            string serial = Prompt.Ask(this, "Install Serial",
                "Make the serial for " + d.DisplayName + " with the Serial Generator (Machine ID " + d.MachineId + "), then paste it here.",
                "", false, s => CheckSerial(s, d.MachineId), "Copy Machine ID", () => Clipboard.SetText(d.MachineId));
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
                return "This serial number is not for this PC (Machine ID " + machineId + "). Make one for this Machine ID.";
            if (data.Expires != null && data.Expires.Value.Date < DateTime.Today)
                return "This serial number expired on " + data.Expires.Value.ToString("yyyy-MM-dd") + ".";
            return null;
        }

        private async void RevokePhones()
        {
            List<Device> targets = OnlineTargets(false);
            if (targets == null) return;
            if (MessageBox.Show(this, "Make a new mobile pairing code on " + Names(targets) + "?\n\n" +
                                      "Every phone paired with these PCs is disconnected and must scan the new QR code (Card Studio → Mobile App…). " +
                                      "Use this when a phone was lost or the code was shared by mistake.",
                    "Disconnect Phones", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            await Run(targets, AdminProtocol.RevokePhones, "", "Disconnect Phones");
        }

        // ===================== List management =====================

        private void SetLabel()
        {
            List<Device> sel = SelectedDevices();
            if (sel.Count == 0) { Warn("Select one or more PCs in the list."); return; }
            string label = Prompt.Ask(this, "Label", "Your own name for " + Names(sel) + " (e.g. \"Room 204, front desk\"). Only kept in this list.",
                                      sel.Count == 1 ? sel[0].Label : "", false, t => t.Length > 80 ? "At most 80 characters." : null);
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
            string text = "Remove " + Names(sel) + " from the list?" +
                          (online > 0 ? "\n\nPCs that are online will show up again at the next refresh." : "");
            if (MessageBox.Show(this, text, "Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            foreach (Device d in sel) _inv.Devices.Remove(d);
            _dirty = true;
            RefreshList();
        }

        private void CopyMachineIds()
        {
            List<Device> sel = SelectedDevices();
            if (sel.Count == 0) return;
            Clipboard.SetText(string.Join(Environment.NewLine, sel.Select(d => sel.Count == 1 ? d.MachineId : d.Computer + "\t" + d.MachineId)));
            SetAction(sel.Count == 1 ? "Machine ID of " + sel[0].Computer + " copied." : sel.Count + " Machine IDs copied.");
        }

        private void EditTargets()
        {
            string text = Prompt.Ask(this, "Addresses",
                "PCs on this network are found by broadcast. Add PCs behind a router here, one per line: an address (192.168.2.15), " +
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
                });
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
                var sb = new StringBuilder();
                string[] header =
                {
                    "Computer", "Label", "Online", "Last seen", "Address", "Machine ID", "User", "Version", "Windows", "License",
                    "License expires", "Mobile App", "Phones", "Students", "Source", "Printer", "Printed today", "Printed total", "Last print"
                };
                sb.AppendLine(string.Join(",", header.Select(Csv)));
                foreach (Device d in Sort(_inv.Devices))
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
                    SetAction("Saved " + Path.GetFileName(dlg.FileName) + ".");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Warn("Could not save the file:\n" + ex.Message);
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
                _lblKey.Text = "⚠ No administrator key: Key ▸ Open Key… and choose admin-private.key";
                _lblKey.ForeColor = Danger;
                return;
            }
            AdminKey key;
            try
            {
                key = AdminKey.Load(path);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                Warn(ex.Message);
                return;
            }
            _key = key;   // the previous key may still be signing a discover: left to the garbage collector
            if (remember) AdminKey.RememberedPath = key.Path;
            _lblKey.Text = (key.MatchesApp ? "Key: " : "⚠ Not the key built into Card Studio (the PCs will not answer): ") + key.Path;
            _lblKey.ForeColor = key.MatchesApp ? TextSoft : Danger;
            if (remember) Discover(true);
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
            if (MessageBox.Show(this,
                    "Make a new administrator key?\n\n" +
                    "The PCs only answer the key built into Card Studio. After making a new key you must put its public key in " +
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
                    Warn("Could not make the key:\n" + ex.Message);
                    return;
                }
                UseKey(dlg.FileName, true);
                ShowPublicKey(true);
            }
        }

        private void ShowPublicKey(bool isNew)
        {
            if (_key == null) { Warn("Open the administrator's key first."); return; }
            Prompt.Ask(this, "Public Key",
                (isNew ? "New key made. " : "") + (_key.MatchesApp
                    ? "This key is the one built into Card Studio."
                    : "Put this value in NPIC_CARD_APP\\Services\\AdminProtocol.cs (PublicKey), then rebuild and reinstall Card Studio."),
                _key.PublicKey, false, null, "Copy", () => Clipboard.SetText(_key.PublicKey));
        }

        // ===================== Helpers =====================

        private static ToolStripButton Button(string text, string tip, EventHandler click)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text, Margin = new Padding(2, 1, 2, 1) };
            b.Click += click;
            return b;
        }

        private static IPAddress ParseAddress(string text)
        {
            IPAddress ip;
            return !string.IsNullOrEmpty(text) && IPAddress.TryParse(text, out ip) ? ip : null;
        }

        private static string Names(List<Device> devices)
        {
            if (devices.Count == 1) return devices[0].DisplayName;
            return devices.Count + " PCs";
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

        private void SetAction(string text)
        {
            _lblAction.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
        }

        private void Warn(string text)
        {
            MessageBox.Show(this, text, "Card Studio Admin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
