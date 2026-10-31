using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NPIC_CARD_APP;

namespace CardStudio.Admin
{
    /// <summary>
    /// Network Devices page: every device that answers on the networks of this PC (see <see cref="NetworkScanner"/>), which of
    /// them are Windows PCs and which run Card Studio, and Install Card Studio… for the selected Windows PCs (<see cref="InstallForm"/>).
    /// </summary>
    //  ┌ Network Devices ─────────────────────────────────────────────────────────────────────┐
    //  │ 23 devices · 9 Windows PCs · 5 with Card Studio   [All|Windows PCs|Without Card Studio]│
    //  │ Scanned 192.168.1.0/24 at 20:31                         [Scan Again] [Install Card Studio…]│
    //  │ ▬▬▬▬▬▬▬▬▬▬▬▬ progress                                                                 │
    //  │ Type          Name         Address        MAC                Card Studio     Ping       │
    //  │ (Card Studio) REG-OFFICE-1 192.168.1.21   3C-52-82-…         1.0.1.1          2 ms      │
    //  │ (Windows PC)  LAB-PC-08    192.168.1.58   …                  Not found                  │
    //  └──────────────────────────────────────────────────────────────────────────────────────────┘
    internal sealed class NetworkForm : ModalForm
    {
        private static readonly Font ChipFont = Theme.UiFont(8.25f, true);
        private static readonly Font SemiFont = Theme.UiFont(9.75f, true);

        private readonly Inventory _inv;
        private readonly Dictionary<string, NetDevice> _found = new Dictionary<string, NetDevice>();
        private readonly Label _lblSummary, _lblScope;
        private readonly Panel _progress;
        private readonly PillTabs _filter;
        private readonly PillButton _btnScan, _btnInstall;
        private readonly DataGridView _grid;
        private readonly Label _lblEmpty;
        private readonly System.Windows.Forms.Timer _relist;
        private readonly ToolTip _tip = new ToolTip();
        private CancellationTokenSource _scan;
        private string _scope = "";
        private int _done, _total;
        private string _sortColumn = "Address";
        private bool _sortDescending;
        private bool _rebuilding;   // rows are being added: their Tag is not set yet

        /// <summary>Machine ID of a Card Studio PC double-clicked to show it in the main list, or null.</summary>
        public string PcToShow { get; private set; }

        /// <summary>True when Card Studio was installed on at least one PC from this page.</summary>
        public bool InstalledAny { get; private set; }

        public NetworkForm(Inventory inventory)
        {
            _inv = inventory;
            Text = "Network Devices";
            Subtitle = "Every device that answers on your network, and where Card Studio can be installed";
            Glyph = Theme.IcoGlobe;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1120, work.Width), Math.Min(760, work.Height));
            MinimumSize = new Size(Math.Min(860, work.Width), Math.Min(520, work.Height));

            // ---- Strip: summary, filter, actions ----
            var strip = new TableLayoutPanel { Dock = DockStyle.Top, Height = 72, ColumnCount = 2, RowCount = 1, BackColor = Theme.SurfaceAlt, Padding = new Padding(24, 12, 20, 8) };
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0), BackColor = Theme.SurfaceAlt };
            _lblSummary = new Label { AutoSize = true, Font = Theme.UiFont(10.5f, true), ForeColor = Theme.TextMain, Margin = new Padding(0), BackColor = Theme.SurfaceAlt };
            _lblScope = new Label { AutoSize = true, Font = Theme.Small, ForeColor = Theme.TextSoft, Margin = new Padding(1, 3, 0, 0), BackColor = Theme.SurfaceAlt, UseMnemonic = false };
            titles.Controls.Add(_lblSummary);
            titles.Controls.Add(_lblScope);
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0), BackColor = Theme.SurfaceAlt };
            _filter = new PillTabs("All", "Windows PCs", "Without Card Studio") { Margin = new Padding(0, 4, 12, 0) };
            _filter.SelectedIndexChanged += (s, e) => Relist();
            _btnScan = ModalUi.Button("Scan Again", PillKind.Secondary, (s, e) => ToggleScan());
            _btnInstall = ModalUi.Button("Install Card Studio…", PillKind.Primary, (s, e) => Install());
            _tip.SetToolTip(_btnInstall, "Install Card Studio on the selected Windows PCs, over the network, with an administrator account");
            actions.Controls.Add(_filter);
            actions.Controls.Add(_btnScan);
            actions.Controls.Add(_btnInstall);
            strip.Controls.Add(titles, 0, 0);
            strip.Controls.Add(actions, 1, 0);

            _progress = new Panel { Dock = DockStyle.Top, Height = 3, BackColor = Theme.SurfaceAlt };
            _progress.Paint += (s, e) =>
            {
                if (_total <= 0 || _scan == null) return;
                int w = (int)(_progress.Width * Math.Min(1.0, _done / (double)_total));
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, _progress.Width), 3), Theme.VioletA, Theme.VioletB, 0f))
                    e.Graphics.FillRectangle(b, 0, 0, w, 3);
            };

            // ---- Grid ----
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, BackgroundColor = Theme.Surface, BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Theme.GridLine, RowHeadersVisible = false,
                EnableHeadersVisualStyles = false, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 34, StandardTab = true
            };
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_grid, true, null);
            _grid.RowTemplate.Height = 38;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.Font = Theme.UiFont(9f, true);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 4, 0);
            _grid.DefaultCellStyle.BackColor = Theme.Surface;
            _grid.DefaultCellStyle.ForeColor = Theme.TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            _grid.DefaultCellStyle.SelectionForeColor = Theme.Accent;
            _grid.DefaultCellStyle.Padding = new Padding(6, 0, 4, 0);
            Column("Type", "Type", 124);
            Column("Name", "Name", 170).AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            Column("Address", "Address", 120);
            Column("Mac", "MAC address", 150);
            Column("CardStudio", "Card Studio", 170);
            Column("Ping", "Ping", 70).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns["Mac"].DefaultCellStyle.Font = new Font("Consolas", 9f);
            _grid.CellPainting += PaintTypeCell;
            _grid.SelectionChanged += (s, e) => { if (!_rebuilding) UpdateButtons(); };
            _grid.ColumnHeaderMouseClick += (s, e) =>
            {
                string name = _grid.Columns[e.ColumnIndex].Name;
                if (_sortColumn == name) _sortDescending = !_sortDescending;
                else { _sortColumn = name; _sortDescending = false; }
                Relist();
            };
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                Device pc = CardStudioOn((NetDevice)_grid.Rows[e.RowIndex].Tag);
                if (pc != null) { PcToShow = pc.MachineId; DialogResult = DialogResult.OK; }
                else Install();
            };

            _lblEmpty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextMuted, BackColor = Theme.Surface, Visible = false };
            var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 10, 24, 6), BackColor = Theme.Surface };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(_lblEmpty);

            // ---- Footer ----
            FlowLayoutPanel footer = ModalUi.Footer();
            PillButton close = ModalUi.Button("Close", PillKind.Secondary, (s, e) => Close());
            footer.Controls.Add(close);
            footer.Controls.Add(ModalUi.Button("Copy List", PillKind.Ghost, (s, e) => CopyList()));
            PillButton prepare = ModalUi.Button("Save Preparation Script…", PillKind.Ghost, (s, e) => InstallForm.SavePreparationScript(this));
            _tip.SetToolTip(prepare, "A script to run once on each PC (as administrator) so Card Studio can be installed on it from here");
            footer.Controls.Add(prepare);
            CancelButton = close;

            Controls.Add(gridHost);
            Controls.Add(_progress);
            Controls.Add(strip);
            Controls.Add(footer);

            _relist = new System.Windows.Forms.Timer { Interval = 400 };
            _relist.Tick += (s, e) => { _relist.Stop(); Relist(); };
            UpdateSummary();
            UpdateButtons();
            Shown += (s, e) => StartScan();
        }

        private DataGridViewColumn Column(string name, string header, int width)
        {
            var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.Programmatic, MinimumWidth = 60 };
            _grid.Columns.Add(c);
            return c;
        }

        // ===================== Scan =====================

        private void ToggleScan()
        {
            if (_scan != null) _scan.Cancel();
            else StartScan();
        }

        private async void StartScan()
        {
            if (_scan != null) return;
            var cts = _scan = new CancellationTokenSource();
            _found.Clear();
            _done = _total = 0;
            _btnScan.Text = "Stop";
            Relist();
            _lblScope.Text = "Looking at the networks of this PC…";
            _lblScope.ForeColor = Theme.TextSoft;

            List<string> targets = _inv.Targets.ToList();
            try
            {
                NetworkScanner.Plan plan = await Task.Run(() => NetworkScanner.MakePlan(targets));
                if (IsDisposed) return;
                _scope = plan.Description;
                _total = plan.Targets.Count;
                if (_total == 0)
                {
                    _lblScope.Text = "No network to scan: this PC is not connected.";
                    return;
                }
                await NetworkScanner.ScanAsync(plan,
                    d => Ui(() => { _found[d.Address.ToString()] = d; _relist.Stop(); _relist.Start(); }),
                    (done, total) => { Interlocked.Exchange(ref _done, done); if (done % 8 == 0 || done == total) Ui(UpdateProgress); },
                    cts.Token);
                if (IsDisposed) return;
                _lblScope.Text = (cts.IsCancellationRequested ? "Stopped scanning " : "Scanned ") + _scope + " at " + DateTime.Now.ToString("HH:mm") +
                                 "  ·  ARP, ping, Windows file sharing (TCP 445), DNS and NetBIOS names";
                if (!_found.Values.Any(n => n.FileSharing && !n.Self) && _found.Count > 1)
                {
                    // Windows laptops on a "Public" network, or with sharing off, look like any other device.
                    _lblScope.Text = "No PC shares files here, so none can be installed from this page yet: run the preparation script " +
                                     "once on each PC (Save Preparation Script…, below), then Scan Again.";
                    _lblScope.ForeColor = Theme.Warning;
                }
                else _lblScope.ForeColor = Theme.TextSoft;
            }
            finally
            {
                if (!IsDisposed)
                {
                    _scan = null;
                    _btnScan.Text = "Scan Again";
                    _progress.Invalidate();
                    Relist();
                }
                cts.Dispose();
            }
        }

        private void UpdateProgress()
        {
            if (_scan == null) return;
            _lblScope.Text = "Scanning " + _scope + "  ·  " + _done + " of " + _total + " addresses";
            _progress.Invalidate();
        }

        private void Ui(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_scan != null) _scan.Cancel();
            _relist.Dispose();
            base.OnFormClosed(e);
        }

        // ===================== List =====================

        /// <summary>The Card Studio PC of the main list at this address, or null.</summary>
        private Device CardStudioOn(NetDevice n)
        {
            string ip = n.Address.ToString();
            return _inv.Devices.Where(d => d.Address == ip || d.Addresses.Contains(ip))
                       .OrderByDescending(d => d.LastSeen).FirstOrDefault();
        }

        private enum Kind { Self, CardStudio, Windows, Other }

        private Kind KindOf(NetDevice n)
        {
            if (n.Self) return Kind.Self;
            if (CardStudioOn(n) != null || n.InstalledHere != null) return Kind.CardStudio;
            return n.FileSharing ? Kind.Windows : Kind.Other;
        }

        private void Relist()
        {
            var selected = new HashSet<string>(Selected().Select(n => n.Address.ToString()));
            IEnumerable<NetDevice> view = _found.Values;
            if (_filter.SelectedIndex == 1) view = view.Where(n => n.FileSharing);
            else if (_filter.SelectedIndex == 2) view = view.Where(n => n.FileSharing && !n.Self && KindOf(n) != Kind.CardStudio);

            Func<NetDevice, IComparable> key;
            switch (_sortColumn)
            {
                case "Type": key = n => (int)KindOf(n); break;
                case "Name": key = n => n.Name.Length == 0 ? "~" : n.Name; break;
                case "Mac": key = n => n.Mac; break;
                case "CardStudio": key = n => CardStudioText(n); break;
                case "Ping": key = n => n.PingMs < 0 ? int.MaxValue : n.PingMs; break;
                default: key = n => n.SortKey; break;
            }
            List<NetDevice> rows = (_sortDescending ? view.OrderByDescending(key) : view.OrderBy(key)).ThenBy(n => n.SortKey).ToList();

            _rebuilding = true;
            _grid.SuspendLayout();
            try
            {
                _grid.Rows.Clear();
                foreach (NetDevice n in rows)
                {
                    Color cs;
                    string csText = CardStudioText(n, out cs);
                    int i = _grid.Rows.Add("", n.Name.Length > 0 ? n.Name : "—", n.Address.ToString(), n.Mac.Length > 0 ? n.Mac : "—", csText,
                                           n.PingMs >= 0 ? n.PingMs + " ms" : "—");
                    DataGridViewRow r = _grid.Rows[i];
                    r.Tag = n;
                    if (n.Name.Length == 0) Style(r.Cells["Name"], Theme.TextMuted);
                    else r.Cells["Name"].Style.Font = SemiFont;
                    if (n.Mac.Length == 0) Style(r.Cells["Mac"], Theme.TextMuted);
                    Style(r.Cells["CardStudio"], cs);
                    Style(r.Cells["Ping"], Theme.TextSoft);
                    r.Cells["Type"].ToolTipText = TypeTip(n);
                    if (selected.Contains(n.Address.ToString())) r.Selected = true;
                }
                if (selected.Count == 0) _grid.ClearSelection();
                foreach (DataGridViewColumn c in _grid.Columns)
                    c.HeaderCell.SortGlyphDirection = c.Name == _sortColumn ? (_sortDescending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
            }
            finally
            {
                _grid.ResumeLayout();
                _rebuilding = false;
            }

            _lblEmpty.Text = _scan != null ? "Looking for devices…"
                           : _found.Count == 0 ? "No device answered. Check the network connection of this PC, then Scan Again."
                           : "No device in this view.";
            _lblEmpty.Visible = rows.Count == 0;
            _lblEmpty.BringToFront();
            UpdateSummary();
            UpdateButtons();
        }

        private static void Style(DataGridViewCell cell, Color color)
        {
            cell.Style.ForeColor = color;
            cell.Style.SelectionForeColor = color;
        }

        private string CardStudioText(NetDevice n)
        {
            Color ignored;
            return CardStudioText(n, out ignored);
        }

        private string CardStudioText(NetDevice n, out Color color)
        {
            Device pc = CardStudioOn(n);
            if (n.InstalledHere != null && (pc == null || !pc.Online))
            {
                color = Theme.Success;
                return "Installed " + n.InstalledHere + " just now";
            }
            if (pc != null)
            {
                color = pc.Online ? Theme.Success : Theme.TextMain;
                return pc.Version + (pc.Online ? "  · open" : "  · closed now");
            }
            if (n.Self)
            {
                color = Theme.TextSoft;
                return "Card Studio Admin";
            }
            color = Theme.TextMuted;
            return n.FileSharing ? "Not found" : "—";
        }

        private string TypeTip(NetDevice n)
        {
            switch (KindOf(n))
            {
                case Kind.Self: return "This PC (Card Studio Admin runs here)";
                case Kind.CardStudio: return "A PC in the main list: Card Studio answered from this address";
                case Kind.Windows: return "Windows file sharing answers (TCP 445): a Windows PC where Card Studio can be installed. " +
                                          "Card Studio may also be installed but closed.";
                default: return "Answers on the network, but no Windows file sharing: a phone, printer, router or a PC that blocks it";
            }
        }

        /// <summary>Type column: a soft chip (Card Studio green, Windows PC accent, other grey).</summary>
        private void PaintTypeCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Columns[e.ColumnIndex].Name != "Type") return;
            var n = _grid.Rows[e.RowIndex].Tag as NetDevice;
            if (n == null) return;
            e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);

            string text;
            Color tone;
            switch (KindOf(n))
            {
                case Kind.Self: text = "This PC"; tone = Theme.TextSoft; break;
                case Kind.CardStudio: text = "Card Studio"; tone = Theme.Success; break;
                case Kind.Windows: text = "Windows PC"; tone = Theme.Accent; break;
                default: text = "Device"; tone = Theme.TextMuted; break;
            }
            Graphics g = e.Graphics;
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Size size = TextRenderer.MeasureText(text, ChipFont);
            var chip = new RectangleF(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - 22) / 2f, size.Width + 22, 22);
            using (GraphicsPath p = Theme.RoundRect(chip, 11))
            using (var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 52 : 30, tone)))
                g.FillPath(b, p);
            using (var dot = new SolidBrush(tone))
                g.FillEllipse(dot, chip.X + 8, chip.Y + 8, 6, 6);
            g.SmoothingMode = old;
            TextRenderer.DrawText(g, text, ChipFont, new Rectangle((int)chip.X + 17, (int)chip.Y, size.Width + 4, (int)chip.Height), tone,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            e.Handled = true;
        }

        private void UpdateSummary()
        {
            List<NetDevice> all = _found.Values.ToList();
            int windows = all.Count(n => n.FileSharing && !n.Self);
            int cardStudio = all.Count(n => KindOf(n) == Kind.CardStudio);
            int without = all.Count(n => n.FileSharing && !n.Self && KindOf(n) != Kind.CardStudio);
            _lblSummary.Text = all.Count + (all.Count == 1 ? " device" : " devices") + "  ·  " + windows + " Windows PCs  ·  " +
                               cardStudio + " with Card Studio  ·  " + without + " without";
        }

        private List<NetDevice> Selected()
        {
            return _grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => r.Tag).OfType<NetDevice>().ToList();
        }

        private void UpdateButtons()
        {
            List<NetDevice> sel = Selected();
            int ready = sel.Count(n => n.FileSharing && !n.Self);
            _btnInstall.Enabled = ready > 0;
            _btnInstall.Text = ready > 1 ? "Install Card Studio on " + ready + " PCs…" : "Install Card Studio…";
        }

        // ===================== Install =====================

        private void Install()
        {
            List<NetDevice> sel = Selected();
            List<NetDevice> ready = sel.Where(n => n.FileSharing && !n.Self).ToList();
            if (ready.Count == 0)
            {
                AppDialog.Show(this, sel.Any(n => n.Self)
                        ? "This is the PC Card Studio Admin runs on: run CardStudio-Setup.exe here instead."
                        : "Select one or more Windows PCs (Type \"Windows PC\"). Devices without Windows file sharing cannot be installed from here.",
                    "Install Card Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int running = ready.Count(n => { Device pc = CardStudioOn(n); return pc != null && pc.Online; });
            if (running > 0 &&
                AppDialog.Show(this, running + (running == 1 ? " of these PCs has" : " of these PCs have") + " Card Studio open now. " +
                                     "The setup cannot update it while it is open: close it on those PCs first, or use Check for Updates.\n\nContinue?",
                               "Install Card Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            using (var dlg = new InstallForm(ready))
            {
                dlg.ShowDialog(this);
                if (dlg.InstalledCount > 0) InstalledAny = true;
            }
            Relist();
        }

        private void CopyList()
        {
            var sb = new StringBuilder("Type\tName\tAddress\tMAC\tCard Studio\tPing\r\n");
            foreach (DataGridViewRow r in _grid.Rows)
            {
                var n = (NetDevice)r.Tag;
                sb.Append(KindOf(n)).Append('\t').Append(n.Name).Append('\t').Append(n.Address).Append('\t').Append(n.Mac).Append('\t')
                  .Append(CardStudioText(n)).Append('\t').Append(n.PingMs >= 0 ? n.PingMs + " ms" : "").Append("\r\n");
            }
            Clipboard.SetText(sb.ToString());
            _lblScope.Text = _grid.Rows.Count + " rows copied: paste them in Excel.";
        }
    }
}
