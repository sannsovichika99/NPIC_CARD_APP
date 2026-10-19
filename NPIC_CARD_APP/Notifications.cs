using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace NPIC_CARD_APP
{
    internal enum NoticeKind { Info, Success, Warning, Error }

    /// <summary>One entry of the notification list (newest first).</summary>
    internal sealed class Notice
    {
        public NoticeKind Kind;
        public string Title;
        public string Message;
        public DateTime Time;
        public bool Read;
        public int Count = 1;      // the same notice repeated is counted instead of listed again
        public Action Action;      // what clicking the notice opens; null = nothing
    }

    /// <summary>Notifications of this session: warnings, errors and finished jobs. Keeps the newest <see cref="Capacity"/>.</summary>
    internal sealed class NotificationCenter
    {
        public const int Capacity = 50;
        private readonly List<Notice> _items = new List<Notice>();

        /// <summary>Raised after every change (added, read, removed, cleared).</summary>
        public event EventHandler Changed;

        /// <summary>Raised for a new (or repeated) notice, before <see cref="Changed"/>.</summary>
        public event Action<Notice> Added;

        public IList<Notice> Items { get { return _items.AsReadOnly(); } }
        public int UnreadCount { get { return _items.Count(n => !n.Read); } }

        /// <summary>True when an unread notice is a warning or an error (the bell badge turns red).</summary>
        public bool HasUnreadAlert { get { return _items.Any(n => !n.Read && n.Kind >= NoticeKind.Warning); } }

        public Notice Add(NoticeKind kind, string title, string message, Action action = null)
        {
            Notice top = _items.Count > 0 ? _items[0] : null;
            if (top != null && top.Kind == kind && top.Title == title && top.Message == message)
            {
                top.Count++;
                top.Time = DateTime.Now;
                top.Read = false;
                if (action != null) top.Action = action;
            }
            else
            {
                top = new Notice { Kind = kind, Title = title, Message = message, Time = DateTime.Now, Action = action };
                _items.Insert(0, top);
                if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
            }
            if (Added != null) Added(top);
            RaiseChanged();
            return top;
        }

        public void MarkAllRead()
        {
            bool any = false;
            foreach (Notice n in _items)
                if (!n.Read) { n.Read = true; any = true; }
            if (any) RaiseChanged();
        }

        public void Remove(Notice notice)
        {
            if (_items.Remove(notice)) RaiseChanged();
        }

        public void Clear()
        {
            if (_items.Count == 0) return;
            _items.Clear();
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        /// <summary>"Just now", "5 min ago", "2 h ago", then the clock time (and the date from yesterday on).</summary>
        public static string Ago(DateTime time)
        {
            TimeSpan age = DateTime.Now - time;
            if (age.TotalMinutes < 1) return Lang.T("Just now");
            if (age.TotalHours < 1) return Lang.F("{0} min ago", (int)age.TotalMinutes);
            if (age.TotalHours < 6) return Lang.F("{0} h ago", (int)age.TotalHours);
            return time.Date == DateTime.Today ? time.ToString("HH:mm") : time.ToString("dd-MM HH:mm");
        }

        // ---- Look of each kind: icon, colour, soft background ----
        public static string Glyph(NoticeKind kind)
        {
            switch (kind)
            {
                case NoticeKind.Error: return Theme.IcoError;
                case NoticeKind.Warning: return Theme.IcoWarning;
                case NoticeKind.Success: return Theme.IcoCheck;
                default: return Theme.IcoInfo;
            }
        }

        public static Color Tone(NoticeKind kind)
        {
            switch (kind)
            {
                case NoticeKind.Error: return Theme.Danger;
                case NoticeKind.Warning: return Theme.Warning;
                case NoticeKind.Success: return Theme.Success;
                default: return Theme.Accent;
            }
        }

        public static Color SoftTone(NoticeKind kind)
        {
            switch (kind)
            {
                case NoticeKind.Error: return Theme.DangerSoft;
                case NoticeKind.Warning: return Theme.WarningSoft;
                case NoticeKind.Success: return Color.FromArgb(Theme.Dark ? 60 : 34, Theme.Success);
                default: return Theme.AccentSoft;
            }
        }
    }

    /// <summary>
    /// Round bell button with an unread badge (red while a warning or error is unread). <see cref="Ring"/> swings the
    /// bell for a moment when something new needs attention.
    /// </summary>
    internal sealed class NotificationBell : Control
    {
        public const string BellGlyph = "";   // Ringer (E7ED is the crossed-out RingerSilent)

        private bool _hover, _open, _alert;
        private int _unread;
        private readonly Timer _ringTimer = new Timer { Interval = 16 };
        private DateTime _ringStart;
        private const double RingSeconds = 0.9;

        public NotificationBell()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Size = new Size(40, 40);
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = Lang.T("Notifications");
            _ringTimer.Tick += (s, e) =>
            {
                if ((DateTime.Now - _ringStart).TotalSeconds >= RingSeconds) _ringTimer.Stop();
                Invalidate();
            };
        }

        public int UnreadCount
        {
            get { return _unread; }
            set { if (_unread != value) { _unread = value; Invalidate(); } }
        }

        /// <summary>Badge in the danger colour (an unread warning or error) instead of the accent colour.</summary>
        public bool Alert
        {
            get { return _alert; }
            set { if (_alert != value) { _alert = value; Invalidate(); } }
        }

        /// <summary>Pressed look while the notification panel is open.</summary>
        public bool Open
        {
            get { return _open; }
            set { if (_open != value) { _open = value; Invalidate(); } }
        }

        public void Ring()
        {
            _ringStart = DateTime.Now;
            _ringTimer.Start();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.HighQuality(g);

            // Circle button: white with a hairline, soft accent while hovered or open.
            var circle = new RectangleF(2, 2, Width - 5, Height - 5);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(circle);
                Color fill = _open ? Theme.AccentSoftDown : _hover || (Focused && ShowFocusCues) ? Theme.AccentSoft : Theme.Surface;
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                using (var pen = new Pen(_hover || _open ? Theme.AccentOutline : Theme.Border)) g.DrawPath(pen, path);
            }

            // Bell, swinging from its top while ringing (a damped wobble).
            Color bellColor = _open || _hover ? Theme.Accent : Theme.TextSoft;
            double t = (DateTime.Now - _ringStart).TotalSeconds;
            GraphicsState state = g.Save();
            if (_ringTimer.Enabled && t < RingSeconds)
            {
                float angle = (float)(18 * Math.Sin(t * 26) * (1 - t / RingSeconds));
                g.TranslateTransform(circle.X + circle.Width / 2, circle.Y + 9);
                g.RotateTransform(angle);
                g.TranslateTransform(-(circle.X + circle.Width / 2), -(circle.Y + 9));
            }
            Theme.DrawIcon(g, BellGlyph, 17f, bellColor, circle);
            g.Restore(state);

            if (_unread <= 0) return;
            string text = _unread > 9 ? "9+" : _unread.ToString();
            float w = Math.Max(17f, g.MeasureString(text, Theme.Small).Width + 3f);
            var badge = new RectangleF(Width - w - 1, 0, w, 17f);
            Color ring = Theme.EffectiveBack(Parent);
            using (GraphicsPath p = Theme.RoundRect(RectangleF.Inflate(badge, 2, 2), 10.5f))
            using (var b = new SolidBrush(ring)) g.FillPath(b, p);
            using (GraphicsPath p = Theme.RoundRect(badge, 8.5f))
            using (var b = new SolidBrush(_alert ? Theme.Danger : Theme.ButtonA)) g.FillPath(b, p);
            Theme.DrawText(g, text, Theme.Small, Color.White, badge, StringAlignment.Center);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _ringTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// The list that drops down from the bell: header with Clear all, then one row per notice (icon, title, two lines of
    /// message, time, unread dot). Clicking a row raises <see cref="Activated"/>; the × on a hovered row removes it.
    /// </summary>
    internal sealed class NotificationPanel : Control
    {
        private const int PanelWidth = 384, HeaderHeight = 54, RowHeight = 78, MaxRows = 5, EmptyHeight = 168;
        private const string CloseGlyph = "";   // Cancel

        private readonly NotificationCenter _center;
        private readonly HashSet<Notice> _unread;      // unread when the panel opened (their dots stay until it closes)
        private readonly ToolTip _tip = new ToolTip { InitialDelay = 600 };
        private int _scroll;
        private int _hoverRow = -1;
        private bool _hoverClear, _hoverClose;
        private string _tipText;

        public event Action<Notice> Activated;

        public NotificationPanel(NotificationCenter center)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            _center = center;
            _unread = new HashSet<Notice>(center.Items.Where(n => !n.Read));
            BackColor = Theme.Surface;
            Font = Theme.Nav;
            _center.Changed += OnCenterChanged;
            UpdateHeight();
        }

        private void OnCenterChanged(object sender, EventArgs e)
        {
            foreach (Notice n in _center.Items)
                if (!n.Read) _unread.Add(n);
            UpdateHeight();
            Invalidate();
        }

        private int ListHeight { get { return Height - HeaderHeight; } }
        private int ContentHeight { get { return _center.Items.Count * RowHeight; } }

        private void UpdateHeight()
        {
            int count = _center.Items.Count;
            int body = count == 0 ? EmptyHeight : Math.Min(count, MaxRows) * RowHeight + 6;
            Size = new Size(PanelWidth, HeaderHeight + body);
            _scroll = Math.Max(0, Math.Min(_scroll, ContentHeight - ListHeight));
        }

        private Rectangle ClearBounds
        {
            get
            {
                Size s = TextRenderer.MeasureText(Lang.T("Clear all"), Theme.Nav);
                return new Rectangle(Width - s.Width - 22, 14, s.Width + 8, 26);
            }
        }

        private Rectangle RowBounds(int index)
        {
            return new Rectangle(0, HeaderHeight + index * RowHeight - _scroll, Width, RowHeight);
        }

        private Rectangle CloseBounds(Rectangle row)
        {
            return new Rectangle(row.Right - 34, row.Y + 38, 24, 24);
        }

        private int RowAt(Point p)
        {
            if (p.Y < HeaderHeight) return -1;
            int i = (p.Y - HeaderHeight + _scroll) / RowHeight;
            return i >= 0 && i < _center.Items.Count ? i : -1;
        }

        // ===================== Input =====================
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = RowAt(e.Location);
            bool clear = _center.Items.Count > 0 && ClearBounds.Contains(e.Location);
            bool close = row >= 0 && CloseBounds(RowBounds(row)).Contains(e.Location);
            if (row != _hoverRow || clear != _hoverClear || close != _hoverClose)
            {
                _hoverRow = row; _hoverClear = clear; _hoverClose = close;
                Cursor = row >= 0 || clear ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            // Full message as a tooltip: rows only show two lines of it.
            string tip = row >= 0 && !close ? _center.Items[row].Message : close ? Lang.T("Remove") : null;
            if (tip != _tipText)
            {
                _tipText = tip;
                _tip.SetToolTip(this, tip);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverRow = -1; _hoverClear = _hoverClose = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int max = Math.Max(0, ContentHeight - ListHeight);
            int next = Math.Max(0, Math.Min(max, _scroll - Math.Sign(e.Delta) * RowHeight / 2));
            if (next == _scroll) return;
            _scroll = next;
            OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, PointToClient(Cursor.Position).X, PointToClient(Cursor.Position).Y, 0));
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (_center.Items.Count > 0 && ClearBounds.Contains(e.Location))
            {
                _center.Clear();
                return;
            }
            int row = RowAt(e.Location);
            if (row < 0) return;
            Notice notice = _center.Items[row];
            if (CloseBounds(RowBounds(row)).Contains(e.Location))
            {
                _center.Remove(notice);
                return;
            }
            if (Activated != null) Activated(notice);
        }

        // ===================== Drawing =====================
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Theme.HighQuality(g);

            DrawHeader(g);

            IList<Notice> items = _center.Items;
            if (items.Count == 0)
            {
                DrawEmpty(g);
            }
            else
            {
                g.SetClip(new Rectangle(0, HeaderHeight, Width, Height - HeaderHeight));
                int first = Math.Max(0, _scroll / RowHeight);
                for (int i = first; i < items.Count; i++)
                {
                    Rectangle row = RowBounds(i);
                    if (row.Y > Height) break;
                    DrawRow(g, items[i], row, i == _hoverRow, i < items.Count - 1);
                }
                DrawScrollBar(g);
                g.ResetClip();
            }

            using (var pen = new Pen(Theme.Border))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        private void DrawHeader(Graphics g)
        {
            string title = Lang.T("Notifications");
            Size size = TextRenderer.MeasureText(title, Theme.CardTitle);
            Theme.DrawText(g, title, Theme.CardTitle, Theme.TextMain, new RectangleF(18, 0, size.Width + 4, HeaderHeight));

            int unread = _unread.Count(n => _center.Items.Contains(n));
            if (unread > 0)
            {
                string text = Lang.F("{0} new", unread);
                float w = g.MeasureString(text, Theme.Small).Width + 12;
                var chip = new RectangleF(18 + size.Width + 6, (HeaderHeight - 20) / 2f, w, 20);
                using (GraphicsPath p = Theme.RoundRect(chip, 10))
                using (var b = new SolidBrush(Theme.AccentSoft)) g.FillPath(b, p);
                Theme.DrawText(g, text, Theme.Small, Theme.Accent, chip, StringAlignment.Center);
            }

            if (_center.Items.Count > 0)
            {
                Rectangle clear = ClearBounds;
                if (_hoverClear)
                    using (GraphicsPath p = Theme.RoundRect(clear, 8))
                    using (var b = new SolidBrush(Theme.SurfaceHover)) g.FillPath(b, p);
                Theme.DrawText(g, Lang.T("Clear all"), Theme.Nav, _hoverClear ? Theme.Accent : Theme.TextSoft, clear,
                               StringAlignment.Center);
            }

            using (var pen = new Pen(Theme.GridLine))
                g.DrawLine(pen, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
        }

        private void DrawEmpty(Graphics g)
        {
            var circle = new RectangleF((Width - 56) / 2f, HeaderHeight + 26, 56, 56);
            using (var b = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(b, circle);
            Theme.DrawIcon(g, NotificationBell.BellGlyph, 24f, Theme.Accent, circle);
            Theme.DrawText(g, Lang.T("You're all caught up"), Theme.NavBold, Theme.TextMain,
                           new RectangleF(16, circle.Bottom + 12, Width - 32, 22), StringAlignment.Center);
            Theme.DrawText(g, Lang.T("Warnings, errors and finished jobs appear here."), Theme.Small, Theme.TextMuted,
                           new RectangleF(16, circle.Bottom + 34, Width - 32, 20), StringAlignment.Center);
        }

        private void DrawRow(Graphics g, Notice n, Rectangle row, bool hover, bool separator)
        {
            if (hover)
                using (GraphicsPath p = Theme.RoundRect(new RectangleF(6, row.Y + 3, row.Width - 12, row.Height - 6), 10))
                using (var b = new SolidBrush(Theme.SurfaceHover)) g.FillPath(b, p);

            // kind icon in a soft circle
            var icon = new RectangleF(18, row.Y + 14, 36, 36);
            using (var b = new SolidBrush(NotificationCenter.SoftTone(n.Kind))) g.FillEllipse(b, icon);
            Theme.DrawIcon(g, NotificationCenter.Glyph(n.Kind), 16f, NotificationCenter.Tone(n.Kind), icon);

            bool unread = _unread.Contains(n);
            string time = NotificationCenter.Ago(n.Time);
            float timeW = g.MeasureString(time, Theme.Small).Width + 2;
            int right = row.Right - 20;

            string title = n.Count > 1 ? n.Title + "  ×" + n.Count : n.Title;
            Theme.DrawText(g, title, unread ? Theme.NavBold : Theme.Nav, Theme.TextMain,
                           new RectangleF(66, row.Y + 11, right - 66 - timeW - 8, 22));
            Theme.DrawText(g, time, Theme.Small, Theme.TextMuted, new RectangleF(right - timeW, row.Y + 11, timeW, 22),
                           StringAlignment.Far);

            // two lines of the message, cut with … at a word
            var message = new RectangleF(66, row.Y + 33, right - 66 - 22, 36);
            using (var brush = new SolidBrush(Theme.TextSoft))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit })
                g.DrawString(n.Message ?? "", Theme.Small, brush, message, sf);

            Rectangle close = CloseBounds(row);
            if (hover)
            {
                if (_hoverClose)
                    using (var b = new SolidBrush(Theme.Dark ? Theme.Tile : Theme.Border)) g.FillEllipse(b, close);
                Theme.DrawIcon(g, CloseGlyph, 10f, _hoverClose ? Theme.TextMain : Theme.TextMuted, close);
            }
            else if (unread)
            {
                using (var b = new SolidBrush(Theme.Accent))
                    g.FillEllipse(b, close.X + close.Width / 2f - 4, close.Y + close.Height / 2f - 4, 8, 8);
            }

            if (separator && !hover)
                using (var pen = new Pen(Theme.GridLine))
                    g.DrawLine(pen, 66, row.Bottom - 1, row.Right - 16, row.Bottom - 1);
        }

        private void DrawScrollBar(Graphics g)
        {
            int content = ContentHeight, view = ListHeight;
            if (content <= view) return;
            float h = Math.Max(24f, view * (float)view / content);
            float y = HeaderHeight + (view - h) * _scroll / (float)(content - view);
            using (GraphicsPath p = Theme.RoundRect(new RectangleF(Width - 7, y + 2, 4, h - 4), 2))
            using (var b = new SolidBrush(Theme.Border)) g.FillPath(b, p);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _center.Changed -= OnCenterChanged;
                _tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Opens <see cref="NotificationPanel"/> under the bell as a light-dismiss drop-down.</summary>
    internal static class NotificationPopup
    {
        private static DateTime _closedAt;

        /// <summary>
        /// True just after the drop-down closed because the bell itself was clicked: that click must not open it again.
        /// </summary>
        public static bool JustClosed { get { return (DateTime.Now - _closedAt).TotalMilliseconds < 250; } }

        public static ToolStripDropDown Show(NotificationBell bell, NotificationCenter center, Action<Notice> activate)
        {
            var panel = new NotificationPanel(center);
            var host = new ToolStripControlHost(panel)
            {
                AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty, Size = panel.Size
            };
            var drop = new ToolStripDropDown { Padding = Padding.Empty, AutoClose = true, DropShadowEnabled = true };
            drop.Items.Add(host);
            panel.SizeChanged += (s, e) => host.Size = panel.Size;

            panel.Activated += notice =>
            {
                drop.Close(ToolStripDropDownCloseReason.ItemClicked);
                if (notice.Action != null && activate != null)
                    bell.BeginInvoke(new Action(() => activate(notice)));   // after the drop-down is gone
            };
            drop.Closed += (s, e) =>
            {
                _closedAt = DateTime.Now;
                bell.Open = false;
                center.MarkAllRead();
                drop.BeginInvoke(new Action(drop.Dispose));
            };

            bell.Open = true;
            drop.Show(bell, new Point(bell.Width - panel.Width + 4, bell.Height + 6));
            panel.Focus();
            return drop;
        }
    }
}
