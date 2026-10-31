using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using NPIC_CARD_APP;

namespace CardStudio.Admin
{
    /// <summary>
    /// Versions page: every Card Studio release (from Document.md, embedded at build time) plus any other version a PC runs,
    /// how many PCs run each one, and the release notes of the selected version. Older PCs can be sent Check for Updates.
    /// The page only chooses: the main window runs the commands after it closes (<see cref="PcsToUpdate"/>, <see cref="PcToShow"/>).
    /// </summary>
    //  ┌ Versions ──────────────────────────────────────────────────────────────────────┐
    //  │ Latest release 1.0.1.1 · 3 of 5 PCs up to date        [Update Older PCs] GitHub │
    //  │ ┌ v1.0.1.1  Latest   3 PCs ┐  Card Studio 1.0.1.1                              │
    //  │ │ v1.0.1.0           1 PC  │  Released 2026-10-29 · …                           │
    //  │ │ v1.0.0.9                 │  INSTALLED ON  [PC-01] [PC-02]  [Update These PCs] │
    //  │ │ …                        │  RELEASE NOTES  Feature / Change / Known issue …   │
    //  └──────────────────────────────────────────────────────────────────────────────────┘
    internal sealed class VersionsForm : ModalForm
    {
        /// <summary>Card Studio's releases page (NPIC_CARD_APP\Services\UpdateService.cs: Source).</summary>
        public const string ReleasesPage = "https://github.com/sannsovichika99/NPIC_CARD_APP/releases";

        private static readonly Font VersionFont = Theme.UiFont(10.5f, true);
        private static readonly Font ChipFont = Theme.UiFont(7.5f, true);
        private static readonly Font TitleFont = Theme.UiFont(15f, true);
        private static readonly Font NotesFont = Theme.UiFont(9.75f);
        private static readonly Font NotesHeadFont = Theme.UiFont(10.5f, true);

        private sealed class Row
        {
            public Version Version;
            public string Text;
            public Release Release;          // null = a version some PC runs that is not in the notes
            public List<Device> Pcs;
            public bool Latest;
        }

        private readonly List<Device> _devices;
        private readonly Version _latest;
        private readonly List<Row> _rows;
        private readonly ListBox _list;
        private readonly Label _title, _sub, _noPcs, _updateNote;
        private readonly FlowLayoutPanel _pcs, _updateBar;
        private readonly PillButton _btnUpdateThese;
        private readonly RichTextBox _notes;
        private readonly ToolTip _tip = new ToolTip();

        /// <summary>PCs the administrator chose to update (Check for Updates), or null.</summary>
        public List<Device> PcsToUpdate { get; private set; }

        /// <summary>Machine ID of a PC clicked to show it in the main list, or null.</summary>
        public string PcToShow { get; private set; }

        public VersionsForm(IEnumerable<Device> devices)
        {
            _devices = devices.ToList();
            Text = "Versions";
            Subtitle = "Every Card Studio release, and the PCs that run it";
            Glyph = Theme.IcoHistory;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1120, work.Width), Math.Min(760, work.Height));
            MinimumSize = new Size(Math.Min(860, work.Width), Math.Min(520, work.Height));

            // ---- Versions: every release + every version seen on a PC ----
            Release latestRelease = ReleaseNotes.Latest;
            Version newestSeen = _devices.Select(d => d.ParsedVersion).DefaultIfEmpty(new Version(0, 0, 0, 0)).Max();
            _latest = latestRelease == null ? newestSeen : ReleaseNotes.Normalize(latestRelease.Version);
            _rows = ReleaseNotes.All.Select(r => new Row { Version = ReleaseNotes.Normalize(r.Version), Text = r.VersionText, Release = r }).ToList();
            foreach (var group in _devices.Where(d => !string.IsNullOrEmpty(d.Version)).GroupBy(d => d.ParsedVersion))
            {
                Row row = _rows.FirstOrDefault(r => r.Version == group.Key);
                if (row == null)
                {
                    row = new Row { Version = group.Key, Text = group.First().Version };
                    _rows.Add(row);
                }
                row.Pcs = group.OrderByDescending(d => d.Online).ThenBy(d => d.Computer, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            foreach (Row r in _rows)
            {
                if (r.Pcs == null) r.Pcs = new List<Device>();
                r.Latest = r.Release != null && r.Release == latestRelease;
            }
            _rows = _rows.OrderByDescending(r => r.Version).ToList();

            // ---- Summary strip ----
            var strip = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 64, ColumnCount = 2, BackColor = Theme.SurfaceAlt, Padding = new Padding(24, 12, 20, 10)
            };
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            strip.Paint += (s, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, strip.Height - 1, strip.Width, strip.Height - 1); };
            var summary = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0), BackColor = Theme.SurfaceAlt };
            summary.Controls.Add(new Label
            {
                Text = latestRelease == null ? "No release notes in this build" : "Latest release: Card Studio " + latestRelease.VersionText +
                       (latestRelease.Date.Length > 0 ? "  ·  " + latestRelease.Date : ""),
                Font = Theme.UiFont(10.5f, true), ForeColor = Theme.TextMain, AutoSize = true, Margin = new Padding(0), BackColor = Theme.SurfaceAlt
            });
            int known = _devices.Count(d => !string.IsNullOrEmpty(d.Version));
            int upToDate = _devices.Count(d => !string.IsNullOrEmpty(d.Version) && d.ParsedVersion >= _latest);
            List<Device> olderOnline = _devices.Where(d => !string.IsNullOrEmpty(d.Version) && d.ParsedVersion < _latest && d.Online).ToList();
            int older = known - upToDate;
            summary.Controls.Add(new Label
            {
                Text = known == 0 ? "No PC in the list yet."
                     : upToDate + " of " + known + (known == 1 ? " PC" : " PCs") + " up to date  ·  " + older + " on an older version" +
                       (older > 0 ? " (" + olderOnline.Count + " online)" : "") + "  ·  " + ReleaseNotes.All.Count + " releases",
                Font = Theme.Small, ForeColor = older > 0 ? Theme.Warning : Theme.TextSoft, AutoSize = true, Margin = new Padding(1, 2, 0, 0),
                BackColor = Theme.SurfaceAlt
            });
            var stripActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0), BackColor = Theme.SurfaceAlt };
            stripActions.Controls.Add(ModalUi.Button("Releases on GitHub", PillKind.Ghost, (s, e) => OpenReleasesPage()));
            PillButton updateAll = ModalUi.Button("Update All Older PCs", PillKind.Primary, (s, e) => Choose(olderOnline));
            updateAll.Enabled = olderOnline.Count > 0;
            _tip.SetToolTip(updateAll, olderOnline.Count > 0
                ? "Open Check for Updates on the " + olderOnline.Count + " online PCs that run an older version"
                : "No online PC runs an older version");
            stripActions.Controls.Add(updateAll);
            strip.Controls.Add(summary, 0, 0);
            strip.Controls.Add(stripActions, 1, 0);

            // ---- Version list (left) ----
            _list = new ListBox
            {
                Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 62, BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface, ForeColor = Theme.TextMain, IntegralHeight = false
            };
            foreach (Row r in _rows) _list.Items.Add(r);
            _list.DrawItem += DrawRow;
            _list.SelectedIndexChanged += (s, e) => ShowRow(_list.SelectedItem as Row);
            _list.MouseMove += (s, e) =>
            {
                int i = _list.IndexFromPoint(e.Location);
                Row r = i >= 0 && i < _list.Items.Count ? (Row)_list.Items[i] : null;
                string tip = r == null ? null : r.Pcs.Count == 0 ? "No PC in the list runs this version"
                           : string.Join("\n", r.Pcs.Take(20).Select(d => (d.Online ? "● " : "○ ") + d.DisplayName));
                if (_tip.GetToolTip(_list) != (tip ?? "")) _tip.SetToolTip(_list, tip);
            };
            var left = new Panel { Dock = DockStyle.Left, Width = 340, BackColor = Theme.Surface, Padding = new Padding(12, 12, 12, 12) };
            left.Paint += (s, e) => { using (var pen = new Pen(Theme.GridLine)) e.Graphics.DrawLine(pen, left.Width - 1, 0, left.Width - 1, left.Height); };
            left.Controls.Add(_list);

            // ---- Details (right) ----
            _title = new Label { Dock = DockStyle.Top, Height = 36, Font = TitleFont, ForeColor = Theme.TextMain, AutoEllipsis = true, UseMnemonic = false };
            _sub = new Label { Dock = DockStyle.Top, Height = 24, Font = Theme.Small, ForeColor = Theme.TextSoft, AutoEllipsis = true, UseMnemonic = false };
            _pcs = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                Margin = new Padding(0), Padding = new Padding(0, 0, 0, 4)
            };
            _noPcs = new Label { AutoSize = true, ForeColor = Theme.TextMuted, Margin = new Padding(0, 4, 0, 8), Text = "No PC in the list runs this version." };
            _updateBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 2, 0, 6) };
            _btnUpdateThese = ModalUi.Button("Update These PCs", PillKind.Primary, (s, e) => UpdateThese());
            _btnUpdateThese.Margin = new Padding(0, 0, 10, 0);
            _updateNote = new Label { AutoSize = true, ForeColor = Theme.Warning, Font = Theme.Small, Margin = new Padding(0, 10, 0, 0) };
            _updateBar.Controls.Add(_btnUpdateThese);
            _updateBar.Controls.Add(_updateNote);
            _notes = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false, BackColor = Theme.Surface,
                ForeColor = Theme.TextMain, Font = NotesFont, ScrollBars = RichTextBoxScrollBars.Vertical, TabStop = false
            };

            var details = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(24, 14, 12, 8) };
            details.Controls.Add(_notes);   // docking runs in reverse add order: Fill first, then the edges bottom-up
            details.Controls.Add(SectionLabel("RELEASE NOTES"));
            details.Controls.Add(_updateBar);
            details.Controls.Add(_pcs);
            details.Controls.Add(SectionLabel("INSTALLED ON"));
            details.Controls.Add(_sub);
            details.Controls.Add(_title);

            // ---- Footer ----
            FlowLayoutPanel footer = ModalUi.Footer();
            PillButton close = ModalUi.Button("Close", PillKind.Secondary, (s, e) => Close());
            close.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(close);
            CancelButton = close;

            Controls.Add(details);
            Controls.Add(left);
            Controls.Add(strip);
            Controls.Add(footer);

            Shown += (s, e) =>
            {
                // Start on the newest version some PC runs that is older than the latest (what needs work), else the latest.
                Row start = _rows.FirstOrDefault(r => r.Pcs.Count > 0 && r.Version < _latest) ?? _rows.FirstOrDefault(r => r.Latest) ?? _rows.FirstOrDefault();
                if (start != null) _list.SelectedItem = start;
                else ShowRow(null);
                _list.Focus();
            };
        }

        private static Label SectionLabel(string text)
        {
            return new Label
            {
                Text = text, Dock = DockStyle.Top, Height = 30, Font = Theme.SmallCaps, ForeColor = Theme.TextMuted,
                TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(0, 0, 0, 6)
            };
        }

        // ===================== Version list =====================

        private void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _list.Items.Count) return;
            var row = (Row)_list.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Graphics g = e.Graphics;
            Rectangle b = e.Bounds;
            using (var back = new SolidBrush(Theme.Surface)) g.FillRectangle(back, b);
            Theme.HighQuality(g);

            var card = new RectangleF(b.X + 1, b.Y + 3, b.Width - 3, b.Height - 6);
            if (selected)
                using (GraphicsPath p = Theme.RoundRect(card, 10))
                {
                    using (var fill = new SolidBrush(Theme.AccentSoft)) g.FillPath(fill, p);
                    using (var pen = new Pen(Theme.AccentLight)) g.DrawPath(pen, p);
                }
            else if (e.Index < _list.Items.Count - 1)
                using (var pen = new Pen(Theme.GridLine)) g.DrawLine(pen, b.X + 12, b.Bottom - 1, b.Right - 12, b.Bottom - 1);

            float x = card.X + 14, y = card.Y + 8;
            string version = "v" + row.Text;
            Color main = selected ? Theme.Accent : row.Release == null ? Theme.Warning : Theme.TextMain;
            Size vs = TextRenderer.MeasureText(g, version, VersionFont, Size.Empty, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, version, VersionFont, new Point((int)x, (int)y), main, TextFormatFlags.NoPadding);
            float chipX = x + vs.Width + 8;
            if (row.Latest) chipX += Chip(g, "LATEST", Theme.Success, chipX, y + 1) + 6;
            if (row.Release == null) chipX += Chip(g, row.Version > _latest ? "NEWER THAN NOTES" : "NOT IN NOTES", Theme.Warning, chipX, y + 1) + 6;
            else if (row.Pcs.Count > 0 && row.Version < _latest) Chip(g, "UPDATE", Theme.Warning, chipX, y + 1);

            // PCs count badge on the right
            int online = row.Pcs.Count(d => d.Online);
            string count = row.Pcs.Count == 0 ? "—" : row.Pcs.Count + (row.Pcs.Count == 1 ? " PC" : " PCs");
            Size cs = TextRenderer.MeasureText(g, count, ChipFont);
            var badge = new RectangleF(card.Right - cs.Width - 22, y, cs.Width + 12, 20);
            if (row.Pcs.Count > 0)
            {
                using (GraphicsPath p = Theme.RoundRect(badge, 10))
                using (var fill = new SolidBrush(selected ? Theme.Surface : Theme.Tile)) g.FillPath(fill, p);
                if (online > 0)
                    using (var dot = new SolidBrush(Theme.Success)) g.FillEllipse(dot, badge.X - 9, badge.Y + 7, 6, 6);
            }
            TextRenderer.DrawText(g, count, ChipFont, Rectangle.Round(badge), row.Pcs.Count > 0 ? Theme.TextMain : Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            string line2 = row.Release == null
                ? "Seen on PCs, no release notes in this build"
                : (row.Release.Date.Length > 0 ? row.Release.Date + "  ·  " : "") + row.Release.Headline;
            TextRenderer.DrawText(g, line2, Theme.Small, new Rectangle((int)x, (int)y + 26, (int)(card.Right - x - 12), 18), Theme.TextMuted,
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        /// <summary>Small uppercase chip; returns its width.</summary>
        private static float Chip(Graphics g, string text, Color tone, float x, float y)
        {
            Size s = TextRenderer.MeasureText(g, text, ChipFont, Size.Empty, TextFormatFlags.NoPadding);
            var r = new RectangleF(x, y, s.Width + 14, 18);
            using (GraphicsPath p = Theme.RoundRect(r, 9))
            using (var fill = new SolidBrush(Color.FromArgb(Theme.Dark ? 56 : 32, tone))) g.FillPath(fill, p);
            TextRenderer.DrawText(g, text, ChipFont, Rectangle.Round(r), tone,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return r.Width;
        }

        // ===================== Details =====================

        private void ShowRow(Row row)
        {
            SuspendLayout();
            _pcs.SuspendLayout();
            foreach (Control c in _pcs.Controls.Cast<Control>().ToList()) { _pcs.Controls.Remove(c); if (c != _noPcs) c.Dispose(); }
            _notes.Clear();

            if (row == null)
            {
                _title.Text = "No versions";
                _sub.Text = "The release notes could not be read and no PC has answered yet.";
                _pcs.Controls.Add(_noPcs);
                _updateBar.Visible = false;
                _pcs.ResumeLayout();
                ResumeLayout(true);
                return;
            }

            _title.Text = "Card Studio " + row.Text;
            var sub = new List<string>();
            if (row.Release != null && row.Release.Date.Length > 0)
            {
                DateTime? date = ReleaseNotes.ParseDate(row.Release.Date);
                sub.Add("Released " + row.Release.Date + (date == null ? "" : " (" + DaysAgo(date.Value) + ")"));
            }
            if (row.Latest) sub.Add("Latest release");
            else if (row.Version < _latest)
            {
                int behind = _rows.Count(r => r.Release != null && r.Version > row.Version && r.Version <= _latest);
                sub.Add(behind + (behind == 1 ? " release" : " releases") + " behind the latest");
            }
            else if (row.Release == null) sub.Add("Newer than the release notes in this build of Card Studio Admin");
            if (row.Release != null) sub.Add(row.Release.ItemCount + " changes listed");
            _sub.Text = string.Join("  ·  ", sub);

            // ---- PCs on this version ----
            if (row.Pcs.Count == 0) _pcs.Controls.Add(_noPcs);
            foreach (Device d in row.Pcs)
            {
                Device pc = d;
                PillButton b = ModalUi.ToolButton((pc.Online ? "● " : "○ ") + pc.Computer, (s, e) => { PcToShow = pc.MachineId; DialogResult = DialogResult.OK; });
                b.Margin = new Padding(0, 0, 6, 6);
                _tip.SetToolTip(b, pc.DisplayName + "\n" + (pc.Online ? "Online" : "Offline, last seen " + pc.LastSeen.ToString("yyyy-MM-dd HH:mm")) +
                                   " · " + pc.Address + "\nClick to show it in the list");
                _pcs.Controls.Add(b);
            }
            List<Device> online = row.Pcs.Where(d => d.Online).ToList();
            bool older = row.Pcs.Count > 0 && row.Version < _latest;
            _updateBar.Visible = older;
            if (older)
            {
                _btnUpdateThese.Enabled = online.Count > 0;
                _btnUpdateThese.Tag = online;
                _updateNote.Text = online.Count == 0
                    ? "These PCs are offline: update them when Card Studio is open there."
                    : "Opens Check for Updates on " + online.Count + (online.Count == 1 ? " online PC" : " online PCs") +
                      (online.Count < row.Pcs.Count ? " (" + (row.Pcs.Count - online.Count) + " offline)" : "") + ".";
            }

            // ---- Release notes ----
            if (row.Release == null)
            {
                Append("No release notes for this version in this build of Card Studio Admin.", NotesFont, Theme.TextSoft, false);
                Append("It is newer than the notes (rebuild Card Studio Admin after updating Document.md) or a test build.", NotesFont, Theme.TextMuted, false);
            }
            else
            {
                if (row.Release.Summary.Length > 0) Append(row.Release.Summary + "\n", NotesFont, Theme.TextMain, false);
                foreach (ReleaseSection section in row.Release.Sections)
                {
                    Append(section.Title + "  (" + section.Items.Count + ")", NotesHeadFont, SectionColor(section.Title), false);
                    foreach (string item in section.Items) Append(item, NotesFont, Theme.TextMain, true);
                    Append("", NotesFont, Theme.TextMain, false);
                }
            }
            _notes.Select(0, 0);
            _notes.ScrollToCaret();

            _pcs.ResumeLayout(true);
            ResumeLayout(true);
        }

        private void Append(string text, Font font, Color color, bool bullet)
        {
            _notes.Select(_notes.TextLength, 0);
            _notes.SelectionFont = font;
            _notes.SelectionColor = color;
            _notes.SelectionBullet = bullet;
            _notes.SelectionIndent = bullet ? 6 : 0;
            _notes.BulletIndent = 14;
            _notes.SelectedText = text + "\n";
        }

        private static Color SectionColor(string title)
        {
            string t = title.ToLowerInvariant();
            if (t.StartsWith("feature")) return Theme.Success;
            if (t.StartsWith("known")) return Theme.Warning;
            if (t.StartsWith("fix")) return Theme.Danger;
            return Theme.Accent;
        }

        private static string DaysAgo(DateTime date)
        {
            int days = (int)(DateTime.Today - date.Date).TotalDays;
            if (days <= 0) return "today";
            if (days == 1) return "yesterday";
            return days + " days ago";
        }

        // ===================== Actions =====================

        private void UpdateThese()
        {
            var online = _btnUpdateThese.Tag as List<Device>;
            if (online != null && online.Count > 0) Choose(online);
        }

        private void Choose(List<Device> pcs)
        {
            PcsToUpdate = pcs;
            DialogResult = DialogResult.OK;
        }

        private void OpenReleasesPage()
        {
            try { Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not open the browser:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
