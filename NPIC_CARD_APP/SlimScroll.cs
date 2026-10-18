using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Slim overlay scroll bars (Windows 11 style) for every scrolling surface: data grids, property grids, list views,
    /// list boxes, rich text boxes and auto-scroll panels. The native scroll bar keeps doing the scrolling underneath (wheel, keyboard, touchpad, ScrollIntoView);
    /// a <see cref="SlimScrollBar"/> is laid exactly over it, mirrors its range and position, and drives the control
    /// through the control's own API.
    /// <para>Look: a 3 px muted line while you scroll or move over the content, fading out after
    /// <see cref="HideDelayMs"/>; hovering the strip widens it to 8 px with a soft track; the thumb turns
    /// <see cref="Theme.Accent"/> under the pointer and <see cref="Theme.AccentPressed"/> while dragged.</para>
    /// <para>WinForms cannot draw a translucent control over another one, so the bar lives in the strip Windows
    /// reserves for the scroll bar instead of floating over the content. Plain multiline text boxes keep the native bar.</para>
    /// </summary>
    internal static class SlimScroll
    {
        public const int HideDelayMs = 1500;       // idle time before the thumb fades out
        public const int CollapseDelayMs = 450;    // after the pointer leaves the strip, before it narrows again

        private const int WM_MOUSEMOVE = 0x0200, WM_NCMOUSEMOVE = 0x00A0, WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;

        private static readonly List<SlimScrollBar> Bars = new List<SlimScrollBar>();
        private static readonly HashSet<Control> Attached = new HashSet<Control>();
        private static Timer _poll;
        private static Point _lastCursor = new Point(int.MinValue, int.MinValue);

        /// <summary>Gives every scrolling control under <paramref name="root"/> (now and added later) slim scroll bars.</summary>
        public static void Apply(Control root)
        {
            if (root == null) return;
            if (_poll == null)
            {
                // Some scrolling raises no event (ScrollControlIntoView, keyboard in list views): a light poll catches it.
                _poll = new Timer { Interval = 100 };
                _poll.Tick += (s, e) => { foreach (SlimScrollBar bar in Bars.ToArray()) bar.Tick(); };
                _poll.Start();
                Application.AddMessageFilter(new ActivityFilter());
            }
            Walk(root);
        }

        private static void Walk(Control c)
        {
            if (c is SlimScrollBar) return;
            c.ControlAdded -= OnControlAdded;   // -= first: Apply may run again on the same tree
            c.ControlAdded += OnControlAdded;
            c.HandleCreated -= OnHandleCreated;
            c.HandleCreated += OnHandleCreated;
            TryAttach(c);
            if (c is DataGridView || c is ListView || c is PropertyGrid || c is UpDownBase) return;   // their own internals
            var children = new Control[c.Controls.Count];   // snapshot: attaching may add bars to an ancestor
            c.Controls.CopyTo(children, 0);
            foreach (Control child in children) Walk(child);
        }

        private static void OnControlAdded(object sender, ControlEventArgs e) { Walk(e.Control); }

        // AutoScroll is often set after the control joined the tree; by the time its handle exists it is final.
        private static void OnHandleCreated(object sender, EventArgs e) { TryAttach((Control)sender); }

        private static void TryAttach(Control c)
        {
            if (c.IsDisposed || Attached.Contains(c)) return;
            ScrollSource source = CreateSource(c);
            if (source == null) return;

            Attached.Add(c);
            var vertical = new SlimScrollBar(source, true);
            var horizontal = new SlimScrollBar(source, false);
            Bars.Add(vertical);
            Bars.Add(horizontal);
            c.Disposed += (s, e) =>
            {
                Attached.Remove(c);
                Bars.Remove(vertical);
                Bars.Remove(horizontal);
                vertical.Dispose();
                horizontal.Dispose();
            };
            // The bars join the window tree on their first sync, from the message loop: never while a caller is
            // still walking or creating the control collections.
            vertical.RequestSync();
            horizontal.RequestSync();
        }

        private static ScrollSource CreateSource(Control c)
        {
            var grid = c as DataGridView;
            if (grid != null) return new GridSource(grid);
            var props = c as PropertyGrid;
            if (props != null) return PropertyGridSource.Create(props);
            if (c is ListView || c is ListBox || c is RichTextBox) return new WindowSource(c);
            var scrollable = c as ScrollableControl;
            if (scrollable != null && scrollable.AutoScroll && !(c is Form) && !(c.Parent is DataGridView))
                return new WindowSource(c);
            return null;
        }

        private static void OnPointer(bool wheel)
        {
            Point p = Cursor.Position;
            if (!wheel && p == _lastCursor) return;   // WM_MOUSEMOVE also arrives without movement
            _lastCursor = p;
            foreach (SlimScrollBar bar in Bars) bar.OnPointer(p, wheel);
        }

        /// <summary>Pointer movement or wheel anywhere in the app wakes the bars of the control under the pointer.</summary>
        private sealed class ActivityFilter : IMessageFilter
        {
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg == WM_MOUSEMOVE || m.Msg == WM_NCMOUSEMOVE) OnPointer(false);
                else if (m.Msg == WM_MOUSEWHEEL || m.Msg == WM_MOUSEHWHEEL) OnPointer(true);
                return false;
            }
        }
    }

    /// <summary>Scroll range in the native bar's own units: positions run from Min to Max - Page + 1.</summary>
    internal struct ScrollRange : IEquatable<ScrollRange>
    {
        public int Min, Max, Page, Value;

        public int MaxValue { get { return Max - Page + 1; } }
        public bool CanScroll { get { return Page > 0 && MaxValue > Min; } }

        public bool Equals(ScrollRange o) { return Min == o.Min && Max == o.Max && Page == o.Page && Value == o.Value; }
        public override bool Equals(object obj) { return obj is ScrollRange && Equals((ScrollRange)obj); }
        public override int GetHashCode() { return Min ^ (Max << 8) ^ (Page << 16) ^ (Value << 24); }
    }

    /// <summary>Reads and drives one scrolling control. One source serves both of its bars.</summary>
    internal abstract class ScrollSource
    {
        public readonly Control Target;

        /// <summary>Raised when the range, position or placement of the native bars may have changed.</summary>
        public event Action Changed;

        protected ScrollSource(Control target)
        {
            Target = target;
            target.VisibleChanged += Raise;
            target.SizeChanged += Raise;
            target.LocationChanged += Raise;
            target.ParentChanged += Raise;
            target.HandleCreated += Raise;
        }

        protected void Raise(object sender, EventArgs e)
        {
            Action changed = Changed;
            if (changed != null) changed();
        }

        /// <summary>The control the slim bars are children of, or null while there is none.</summary>
        public abstract Control OverlayParent { get; }

        /// <summary>
        /// False when the native bar is not shown. <paramref name="bounds"/> is the strip to cover, in
        /// <see cref="OverlayParent"/> coordinates; <paramref name="lead"/> / <paramref name="trail"/> are pixels at the
        /// start / end of it the track must keep clear of (column headers, the corner box between two bars).
        /// </summary>
        public abstract bool Read(bool vertical, out ScrollRange range, out Rectangle bounds, out int lead, out int trail);

        public abstract void ScrollTo(bool vertical, int value);

        /// <summary>Keeps <paramref name="bar"/> above the native bar it covers.</summary>
        public abstract void KeepAbove(Control bar, bool vertical);

        public virtual Color Back { get { return Target.BackColor; } }

        /// <summary>Colour of the <c>lead</c> band (beside a header row).</summary>
        public virtual Color LeadBack { get { return Back; } }
    }

    /// <summary>
    /// A <see cref="DataGridView"/>: its scroll bars are child controls, so the slim bars become children too, on top.
    /// Rows scroll by whole rows, as with the native bar.
    /// </summary>
    internal sealed class GridSource : ScrollSource
    {
        private readonly DataGridView _grid;

        public GridSource(DataGridView grid) : base(grid)
        {
            _grid = grid;
            grid.Scroll += (s, e) => Raise(s, e);   // raised for user and code scrolling alike
            grid.RowsAdded += (s, e) => Raise(s, e);
            grid.RowsRemoved += (s, e) => Raise(s, e);
            grid.DataBindingComplete += (s, e) => Raise(s, e);
            grid.ColumnWidthChanged += (s, e) => Raise(s, e);
            grid.RowHeightChanged += (s, e) => Raise(s, e);
            grid.ColumnHeadersHeightChanged += Raise;
            foreach (Control c in grid.Controls)
            {
                var bar = c as ScrollBar;
                if (bar == null) continue;
                bar.ValueChanged += Raise;
                bar.VisibleChanged += Raise;
                bar.SizeChanged += Raise;
                bar.LocationChanged += Raise;
            }
        }

        public override Control OverlayParent { get { return _grid; } }

        public override Color Back { get { return _grid.BackgroundColor; } }

        public override Color LeadBack
        {
            get
            {
                Color header = _grid.ColumnHeadersDefaultCellStyle.BackColor;
                return _grid.EnableHeadersVisualStyles || header.IsEmpty ? Back : header;
            }
        }

        private ScrollBar NativeBar(bool vertical)
        {
            foreach (Control c in _grid.Controls)
                if (vertical ? c is VScrollBar : c is HScrollBar) return (ScrollBar)c;
            return null;
        }

        public override bool Read(bool vertical, out ScrollRange range, out Rectangle bounds, out int lead, out int trail)
        {
            range = new ScrollRange();
            bounds = Rectangle.Empty;
            lead = trail = 0;
            ScrollBar bar = NativeBar(vertical);
            if (bar == null || !bar.Visible) return false;

            range = new ScrollRange { Min = bar.Minimum, Max = bar.Maximum, Page = bar.LargeChange, Value = bar.Value };
            bounds = bar.Bounds;
            if (vertical)
            {
                // The native bar runs beside the column headers: keep the track below them.
                if (_grid.ColumnHeadersVisible)
                {
                    int headerBottom = (_grid.BorderStyle == BorderStyle.FixedSingle ? 1 : 0) + _grid.ColumnHeadersHeight;
                    lead = Math.Max(0, Math.Min(bounds.Height, headerBottom - bounds.Top));
                }
                // Cover the corner box between the two bars as well (the grid paints it in a system colour).
                ScrollBar other = NativeBar(false);
                if (other != null && other.Visible && other.Bottom > bounds.Bottom)
                {
                    trail = other.Bottom - bounds.Bottom;
                    bounds.Height += trail;
                }
            }
            return bounds.Width > 0 && bounds.Height > 0;
        }

        public override void ScrollTo(bool vertical, int value)
        {
            try
            {
                if (!vertical)
                {
                    ScrollBar bar = NativeBar(false);
                    int max = bar == null ? 0 : Math.Max(0, bar.Maximum - bar.LargeChange + 1);
                    _grid.HorizontalScrollingOffset = Math.Max(0, Math.Min(max, value));
                    return;
                }

                // Pixel offset -> the scrolling row whose top is nearest to it.
                const DataGridViewElementStates Shown = DataGridViewElementStates.Visible;
                const DataGridViewElementStates Frozen = DataGridViewElementStates.Frozen;
                ScrollBar native = NativeBar(true);
                if (native != null && value >= native.Maximum - native.LargeChange + 1)
                {
                    // End of the track: the grid's own end offset lies a little past Maximum - LargeChange, and asking
                    // for the last row makes it scroll exactly that far (last row fully in view).
                    int last = _grid.Rows.GetLastRow(Shown);
                    if (last >= 0 && _grid.FirstDisplayedScrollingRowIndex != last) _grid.FirstDisplayedScrollingRowIndex = last;
                    return;
                }
                int row = _grid.Rows.GetFirstRow(Shown, Frozen);
                int top = 0;
                while (row >= 0)
                {
                    int height = _grid.Rows.SharedRow(row).Height;
                    if (value < top + height / 2) break;
                    int next = _grid.Rows.GetNextRow(row, Shown, Frozen);
                    if (next < 0) break;
                    top += height;
                    row = next;
                }
                if (row >= 0 && _grid.FirstDisplayedScrollingRowIndex != row) _grid.FirstDisplayedScrollingRowIndex = row;
            }
            catch (InvalidOperationException) { /* grid busy (e.g. committing an edit): skip this step */ }
            catch (ArgumentOutOfRangeException) { /* rows changed under the drag: the next sync catches up */ }
        }

        public override void KeepAbove(Control bar, bool vertical)
        {
            ScrollBar native = NativeBar(vertical);
            if (native == null) return;
            int mine = _grid.Controls.GetChildIndex(bar, false), theirs = _grid.Controls.GetChildIndex(native, false);
            if (mine > theirs && theirs >= 0) _grid.Controls.SetChildIndex(bar, theirs);
        }
    }

    /// <summary>
    /// A <see cref="PropertyGrid"/>: the property list inside it (an internal PropertyGridView) scrolls with a child
    /// <see cref="VScrollBar"/>, so the slim bar becomes a child of that list, on top. The list only reacts to the bar's
    /// own scroll events, so scrolling is driven by line steps sent to the native bar.
    /// </summary>
    internal sealed class PropertyGridSource : ScrollSource
    {
        private const int WM_REFLECT_VSCROLL = 0x2000 + 0x0115, SB_LINEUP = 0, SB_LINEDOWN = 1, SB_ENDSCROLL = 8;

        private readonly PropertyGrid _grid;
        private readonly Control _view;
        private readonly ScrollBar _bar;

        private PropertyGridSource(PropertyGrid grid, Control view, ScrollBar bar) : base(view)
        {
            _grid = grid;
            _view = view;
            _bar = bar;
            bar.ValueChanged += Raise;
            bar.VisibleChanged += Raise;
            bar.SizeChanged += Raise;
            bar.LocationChanged += Raise;
            grid.SelectedObjectsChanged += Raise;
            grid.SelectedGridItemChanged += (s, e) => Raise(s, e);
            grid.PropertyValueChanged += (s, e) => Raise(s, e);
        }

        /// <summary>Null when the grid's internal layout is not the expected one (the native bar stays).</summary>
        public static PropertyGridSource Create(PropertyGrid grid)
        {
            foreach (Control view in grid.Controls)
            {
                if (view.GetType().Name != "PropertyGridView") continue;
                foreach (Control c in view.Controls)
                {
                    var bar = c as VScrollBar;
                    if (bar != null) return new PropertyGridSource(grid, view, bar);
                }
            }
            return null;
        }

        public override Control OverlayParent { get { return _view; } }

        public override Color Back { get { return _grid.ViewBackColor; } }

        public override bool Read(bool vertical, out ScrollRange range, out Rectangle bounds, out int lead, out int trail)
        {
            range = new ScrollRange();
            bounds = Rectangle.Empty;
            lead = trail = 0;
            if (!vertical || !_bar.Visible) return false;   // the property list never scrolls sideways
            range = new ScrollRange { Min = _bar.Minimum, Max = _bar.Maximum, Page = _bar.LargeChange, Value = _bar.Value };
            bounds = _bar.Bounds;
            return bounds.Width > 0 && bounds.Height > 0;
        }

        public override void ScrollTo(bool vertical, int value)
        {
            if (!vertical || !_bar.IsHandleCreated) return;
            bool down = value > _bar.Value;
            IntPtr step = (IntPtr)(down ? SB_LINEDOWN : SB_LINEUP);
            for (int guard = 0; guard < 2000 && (down ? _bar.Value < value : _bar.Value > value); guard++)
            {
                int before = _bar.Value;
                Native.SendMessage(_bar.Handle, WM_REFLECT_VSCROLL, step, IntPtr.Zero);
                if (_bar.Value == before) break;   // refused (an edit that failed to commit) or at the end
            }
            Native.SendMessage(_bar.Handle, WM_REFLECT_VSCROLL, (IntPtr)SB_ENDSCROLL, IntPtr.Zero);
        }

        public override void KeepAbove(Control bar, bool vertical)
        {
            int mine = _view.Controls.GetChildIndex(bar, false), theirs = _view.Controls.GetChildIndex(_bar, false);
            if (mine > theirs && theirs >= 0) _view.Controls.SetChildIndex(bar, theirs);
        }
    }

    /// <summary>
    /// A control with window-style scroll bars in its frame (auto-scroll panels, list views, list boxes). The slim bar is a sibling
    /// laid over that frame strip, placed in the nearest ancestor with free (non-flow, non-table, non-scrolling) layout.
    /// </summary>
    internal sealed class WindowSource : ScrollSource
    {
        private const int SB_HORZ = 0, SB_VERT = 1;
        private const uint SIF_RANGE = 0x1, SIF_PAGE = 0x2, SIF_POS = 0x4, SIF_ALL = SIF_RANGE | SIF_PAGE | SIF_POS;
        private const int OBJID_VSCROLL = unchecked((int)0xFFFFFFFB), OBJID_HSCROLL = unchecked((int)0xFFFFFFFA);
        private const int STATE_SYSTEM_INVISIBLE = 0x8000, STATE_SYSTEM_OFFSCREEN = 0x10000;
        private const int LVM_SCROLL = 0x1014, LVM_GETHEADER = 0x101F;
        private const int WM_HSCROLL = 0x0114, SB_THUMBPOSITION = 4, SB_ENDSCROLL = 8;
        private const int EM_GETSCROLLPOS = 0x0400 + 221, EM_SETSCROLLPOS = 0x0400 + 222;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendPoint(IntPtr hwnd, int msg, IntPtr wParam, ref POINT lParam);

        private static void SendPoint(IntPtr hwnd, int msg, ref POINT point) { SendPoint(hwnd, msg, IntPtr.Zero, ref point); }

        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLINFO
        {
            public uint cbSize, fMask;
            public int nMin, nMax;
            public uint nPage;
            public int nPos, nTrackPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLBARINFO
        {
            public int cbSize;
            public Native.RECT rcScrollBar;
            public int dxyLineButton, xyThumbTop, xyThumbBottom, reserved;
            public int state0, state1, state2, state3, state4, state5;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref SCROLLINFO info);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetScrollBarInfo(IntPtr hwnd, int idObject, ref SCROLLBARINFO info);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        private Control _host, _branch;   // _branch: the child of _host that holds the target

        public WindowSource(Control target) : base(target)
        {
            var scrollable = target as ScrollableControl;
            if (scrollable != null)
            {
                scrollable.Scroll += (s, e) => Raise(s, e);
                scrollable.Layout += (s, e) => Raise(s, e);
            }
            var rich = target as RichTextBox;
            if (rich != null)
            {
                rich.VScroll += Raise;
                rich.HScroll += Raise;
                rich.TextChanged += Raise;   // a new page: new range, back at the top
            }
        }

        public override Control OverlayParent
        {
            get
            {
                if (_host == null || _branch == null || _branch.Parent != _host || !Contains(_branch, Target)) Resolve();
                return _host;
            }
        }

        private static bool Contains(Control ancestor, Control c)
        {
            for (; c != null; c = c.Parent) if (c == ancestor) return true;
            return false;
        }

        private static bool CanHost(Control p)
        {
            if (!(p is Panel || p is Form || p is UserControl || p is GroupBox)) return false;   // others reject or re-lay children
            if (p.LayoutEngine.GetType().Name != "DefaultLayout") return false;                 // flow / table panels
            var scrollable = p as ScrollableControl;
            return scrollable == null || !scrollable.AutoScroll;                                 // would scroll the bar away
        }

        private void Resolve()
        {
            _host = null;
            _branch = Target;
            Control p = Target.Parent;
            while (p != null && !CanHost(p))
            {
                _branch = p;
                p = p.Parent;
            }
            _host = p;
            // Follow the moves of every container between the target and the host.
            for (Control c = Target.Parent; c != null && c != _host; c = c.Parent)
            {
                c.LocationChanged -= Raise;
                c.LocationChanged += Raise;
                c.SizeChanged -= Raise;
                c.SizeChanged += Raise;
            }
        }

        private bool NativeBarRect(bool vertical, out Rectangle screen)
        {
            screen = Rectangle.Empty;
            var info = new SCROLLBARINFO { cbSize = Marshal.SizeOf(typeof(SCROLLBARINFO)) };
            if (!GetScrollBarInfo(Target.Handle, vertical ? OBJID_VSCROLL : OBJID_HSCROLL, ref info)) return false;
            if ((info.state0 & (STATE_SYSTEM_INVISIBLE | STATE_SYSTEM_OFFSCREEN)) != 0) return false;
            Native.RECT r = info.rcScrollBar;
            screen = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return screen.Width > 0 && screen.Height > 0;
        }

        private bool Info(bool vertical, out SCROLLINFO info)
        {
            info = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO)), fMask = SIF_ALL };
            return GetScrollInfo(Target.Handle, vertical ? SB_VERT : SB_HORZ, ref info);
        }

        public override bool Read(bool vertical, out ScrollRange range, out Rectangle bounds, out int lead, out int trail)
        {
            range = new ScrollRange();
            bounds = Rectangle.Empty;
            lead = trail = 0;
            Control host = OverlayParent;
            if (host == null || !host.IsHandleCreated) return false;

            Rectangle screen;
            SCROLLINFO info;
            if (!NativeBarRect(vertical, out screen) || !Info(vertical, out info)) return false;
            range = new ScrollRange { Min = info.nMin, Max = info.nMax, Page = (int)info.nPage, Value = info.nPos };

            if (vertical)
            {
                // Cover the corner box between the two bars as well.
                Rectangle other;
                if (NativeBarRect(false, out other) && other.Bottom > screen.Bottom)
                {
                    trail = other.Bottom - screen.Bottom;
                    screen.Height += trail;
                }
                // Details view: keep the track below the column header.
                var list = Target as ListView;
                if (list != null && list.View == View.Details && list.HeaderStyle != ColumnHeaderStyle.None)
                {
                    IntPtr header = Native.SendMessage(list.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                    Native.RECT hr;
                    if (header != IntPtr.Zero && IsWindowVisible(header) && Native.GetWindowRect(header, out hr))
                        lead = Math.Max(0, Math.Min(screen.Height, hr.Bottom - screen.Top));
                }
            }
            bounds = host.RectangleToClient(screen);
            return true;
        }

        public override void ScrollTo(bool vertical, int value)
        {
            var list = Target as ListView;
            if (list != null)
            {
                SCROLLINFO info;
                if (!Info(vertical, out info)) return;
                int delta = value - info.nPos;
                if (delta == 0) return;
                // LVM_SCROLL takes pixels; details view counts its vertical position in rows.
                if (vertical && list.View == View.Details)
                {
                    int rowHeight = list.Items.Count > 0 ? list.GetItemRect(0).Height : 0;
                    if (rowHeight <= 0) return;
                    delta *= rowHeight;
                }
                Native.SendMessage(list.Handle, LVM_SCROLL, (IntPtr)(vertical ? 0 : delta), (IntPtr)(vertical ? delta : 0));
                return;
            }

            var rich = Target as RichTextBox;
            if (rich != null)
            {
                // Positions are pixels. EM_SETSCROLLPOS takes 32-bit values (WM_VSCROLL thumb positions stop at
                // 65535 px, which a long page passes); move by the bar's delta from where the text is now.
                SCROLLINFO info;
                if (!Info(vertical, out info)) return;
                int delta = value - info.nPos;
                if (delta == 0) return;
                var pos = new POINT();
                SendPoint(rich.Handle, EM_GETSCROLLPOS, ref pos);
                if (vertical) pos.Y += delta; else pos.X += delta;
                SendPoint(rich.Handle, EM_SETSCROLLPOS, ref pos);
                return;
            }

            var box = Target as ListBox;
            if (box != null)
            {
                // Vertical position = index of the top item; horizontal = pixels (or columns in a multi-column box).
                if (vertical) { if (box.TopIndex != value) box.TopIndex = value; }
                else
                {
                    Native.SendMessage(box.Handle, WM_HSCROLL, (IntPtr)(SB_THUMBPOSITION | (value << 16)), IntPtr.Zero);
                    Native.SendMessage(box.Handle, WM_HSCROLL, (IntPtr)SB_ENDSCROLL, IntPtr.Zero);
                }
                return;
            }

            var scrollable = Target as ScrollableControl;
            if (scrollable == null) return;
            Point at = scrollable.AutoScrollPosition;   // read negative, written positive
            scrollable.AutoScrollPosition = vertical ? new Point(-at.X, value) : new Point(value, -at.Y);
        }

        public override void KeepAbove(Control bar, bool vertical)
        {
            if (_host == null || _branch == null || bar.Parent != _host) return;
            int mine = _host.Controls.GetChildIndex(bar, false), theirs = _host.Controls.GetChildIndex(_branch, false);
            if (mine > theirs && theirs >= 0) _host.Controls.SetChildIndex(bar, theirs);
        }
    }

    /// <summary>One slim bar (vertical or horizontal) covering one native scroll bar.</summary>
    internal sealed class SlimScrollBar : Control
    {
        private const int WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;

        // Sizes at 96 dpi
        private const float Thin = 3f, Thick = 8f, EdgeInset = 3f, EndPad = 3f, MinThumb = 24f;

        private readonly ScrollSource _src;
        private readonly bool _vertical;
        private readonly Timer _anim = new Timer { Interval = 15 };
        private readonly Timer _repeat = new Timer();

        private ScrollRange _range;
        private bool _known, _syncPending, _hot, _thumbHot, _dragging;
        private int _lead, _trail, _grab, _pageDir, _lastActivity, _leftAt;
        private float _show, _showTo, _expand, _expandTo;   // 0..1, current and target

        public SlimScrollBar(ScrollSource source, bool vertical)
        {
            _src = source;
            _vertical = vertical;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            TabStop = false;
            Visible = false;
            BackColor = source.Back;
            ForeColor = Theme.TextMuted;
            AccessibleRole = AccessibleRole.ScrollBar;
            _anim.Tick += (s, e) => Animate();
            _repeat.Tick += (s, e) => RepeatPage();
            _src.Changed += RequestSync;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _src.Changed -= RequestSync;
                _anim.Dispose();
                _repeat.Dispose();
            }
            base.Dispose(disposing);
        }

        // ===================== Sync with the native bar =====================

        /// <summary>Coalesced sync after the current message, once the control has finished laying out.</summary>
        public void RequestSync()
        {
            Control target = _src.Target;
            if (_syncPending || IsDisposed || target.IsDisposed || !target.IsHandleCreated) return;
            _syncPending = true;
            target.BeginInvoke((Action)(() => { _syncPending = false; Sync(); }));
        }

        public void Sync()
        {
            Control target = _src.Target;
            if (IsDisposed || target.IsDisposed) return;

            ScrollRange range;
            Rectangle bounds;
            int lead, trail;
            Control parent = target.IsHandleCreated && target.Visible ? _src.OverlayParent : null;
            if (parent == null || !_src.Read(_vertical, out range, out bounds, out lead, out trail))
            {
                if (Visible) Visible = false;
                _known = false;
                return;
            }

            if (Parent != parent) parent.Controls.Add(this);   // still hidden here
            if (Bounds != bounds) Bounds = bounds;
            _src.KeepAbove(this, _vertical);
            Color back = _src.Back;
            if (BackColor != back) BackColor = back;

            bool appeared = !Visible || !_known;
            bool changed = appeared || !range.Equals(_range) || lead != _lead || trail != _trail;
            bool moved = _known && range.Value != _range.Value;
            _range = range;
            _lead = lead;
            _trail = trail;
            _known = true;
            if (!Visible) Visible = true;

            // Flash once when the bar appears (shows there is more to see), and whenever the content scrolls.
            if ((appeared || moved) && _range.CanScroll) Wake();
            if (changed) Invalidate();
        }

        /// <summary>Poll tick: catch silent scrolling, fade out when idle, narrow after the pointer left.</summary>
        public void Tick()
        {
            Sync();
            if (!Visible || _hot || _dragging) return;
            int now = Environment.TickCount;
            if (_showTo > 0f && unchecked(now - _lastActivity) > SlimScroll.HideDelayMs) { _showTo = 0f; StartAnim(); }
            if (_expandTo > 0f && unchecked(now - _leftAt) > SlimScroll.CollapseDelayMs) { _expandTo = 0f; StartAnim(); }
        }

        /// <summary>Pointer moved (or wheel turned) at <paramref name="screen"/>: wake when it is over the target.</summary>
        public void OnPointer(Point screen, bool wheel)
        {
            if (!Visible || !_range.CanScroll) return;
            Control target = _src.Target;
            Native.RECT r;
            if (!target.IsHandleCreated || !Native.GetWindowRect(target.Handle, out r)) return;
            if (screen.X < r.Left || screen.X >= r.Right || screen.Y < r.Top || screen.Y >= r.Bottom) return;
            Wake();
            if (wheel) RequestSync();
        }

        private void Wake()
        {
            _lastActivity = Environment.TickCount;
            if (_showTo < 1f) { _showTo = 1f; StartAnim(); }
        }

        // ===================== Animation =====================

        private void StartAnim()
        {
            if (!_anim.Enabled) _anim.Start();
        }

        private void Animate()
        {
            _show = Approach(_show, _showTo, _showTo > _show ? 0.25f : 0.08f);   // ~60 ms in, ~190 ms fade out
            _expand = Approach(_expand, _expandTo, 0.16f);                      // ~100 ms
            if (_show == _showTo && _expand == _expandTo) _anim.Stop();
            Invalidate();
        }

        private static float Approach(float value, float target, float step)
        {
            return value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);
        }

        private static float Ease(float t) { return 1f - (1f - t) * (1f - t); }

        // ===================== Geometry =====================

        private float Px { get { return DeviceDpi / 96f; } }
        private int Length { get { return _vertical ? Height : Width; } }
        private int Cross { get { return _vertical ? Width : Height; } }
        private int Along(Point p) { return _vertical ? p.Y : p.X; }

        private void Track(out float start, out float length)
        {
            float pad = EndPad * Px;
            start = _lead + pad;
            length = Length - _trail - pad - start;
        }

        private void Thumb(out float start, out float length)
        {
            float trackStart, trackLength;
            Track(out trackStart, out trackLength);
            int span = _range.Max - _range.Min + 1;
            float proportional = span > 0 ? trackLength * _range.Page / span : trackLength;
            length = Math.Max(0f, Math.Min(trackLength, Math.Max(MinThumb * Px, proportional)));
            int maxValue = _range.MaxValue;
            float t = maxValue > _range.Min ? (float)(_range.Value - _range.Min) / (maxValue - _range.Min) : 0f;
            start = trackStart + (trackLength - length) * Math.Max(0f, Math.Min(1f, t));
        }

        private bool OnThumb(int along)
        {
            float start, length;
            Thumb(out start, out length);
            return along >= start && along < start + length;
        }

        // ===================== Pointer =====================

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hot = true;
            Wake();
            if (_range.CanScroll) { _expandTo = 1f; StartAnim(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragging) return;
            _hot = false;
            _leftAt = Environment.TickCount;
            SetThumbHot(false);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) { DragTo(Along(e.Location)); return; }
            if (!_hot) OnMouseEnter(EventArgs.Empty);
            Wake();
            SetThumbHot(_range.CanScroll && OnThumb(Along(e.Location)));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !_range.CanScroll) return;
            Wake();
            _expandTo = 1f;
            StartAnim();
            float start, length;
            Thumb(out start, out length);
            int along = Along(e.Location);
            if (along >= start && along < start + length)
            {
                _dragging = true;
                _grab = (int)(along - start);
                Invalidate();
            }
            else
            {
                // Track click: page toward the pointer, repeating while held (like the native bar).
                _pageDir = along < start ? -1 : 1;
                PageOnce();
                _repeat.Interval = 350;
                _repeat.Start();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            EndPress();
            _hot = ClientRectangle.Contains(e.Location);
            if (!_hot) _leftAt = Environment.TickCount;
            SetThumbHot(_hot && _range.CanScroll && OnThumb(Along(e.Location)));
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) EndPress();   // capture lost (Alt+Tab, popup) mid-drag
        }

        private void EndPress()
        {
            _repeat.Stop();
            if (!_dragging) return;
            _dragging = false;
            _lastActivity = _leftAt = Environment.TickCount;
            Invalidate();
        }

        private void SetThumbHot(bool hot)
        {
            if (_thumbHot == hot) return;
            _thumbHot = hot;
            Invalidate();
        }

        private void DragTo(int along)
        {
            Wake();
            float trackStart, trackLength, thumbStart, thumbLength;
            Track(out trackStart, out trackLength);
            Thumb(out thumbStart, out thumbLength);
            float free = trackLength - thumbLength;
            if (free <= 0f) return;
            float t = Math.Max(0f, Math.Min(1f, (along - _grab - trackStart) / free));
            ScrollTo(_range.Min + (int)Math.Round(t * (_range.MaxValue - _range.Min)));
        }

        private void PageOnce()
        {
            ScrollTo(_range.Value + _pageDir * Math.Max(1, _range.Page));
        }

        private void RepeatPage()
        {
            _repeat.Interval = 60;
            if ((MouseButtons & MouseButtons.Left) == 0) { _repeat.Stop(); return; }
            float start, length;
            Thumb(out start, out length);
            int along = Along(PointToClient(Cursor.Position));
            bool reached = _pageDir < 0 ? along >= start : along < start + length;
            if (reached) { _repeat.Stop(); return; }
            PageOnce();
        }

        private void ScrollTo(int value)
        {
            value = Math.Max(_range.Min, Math.Min(_range.MaxValue, value));
            if (value == _range.Value) return;
            _src.ScrollTo(_vertical, value);
            Sync();   // read back: grids snap to whole rows
        }

        /// <summary>Wheel over the bar scrolls the control underneath.</summary>
        protected override void WndProc(ref Message m)
        {
            if ((m.Msg == WM_MOUSEWHEEL || m.Msg == WM_MOUSEHWHEEL) && _src.Target.IsHandleCreated)
            {
                Native.SendMessage(_src.Target.Handle, m.Msg, m.WParam, m.LParam);
                m.Result = IntPtr.Zero;
                RequestSync();
                return;
            }
            base.WndProc(ref m);
        }

        // ===================== Paint =====================

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = _src.Back;
            using (var brush = new SolidBrush(back)) g.FillRectangle(brush, ClientRectangle);
            if (_lead > 0)
            {
                Color leadBack = _src.LeadBack;
                if (leadBack != back)
                    using (var brush = new SolidBrush(leadBack))
                        g.FillRectangle(brush, _vertical ? new Rectangle(0, 0, Width, _lead) : new Rectangle(0, 0, _lead, Height));
            }
            if (!_range.CanScroll || _show <= 0.01f) return;

            float trackStart, trackLength;
            Track(out trackStart, out trackLength);
            if (trackLength <= 0f) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = Px, expand = Ease(_expand);
            float thin = Thin * s;
            float thick = Math.Max(thin, Math.Min(Thick * s, Cross - 2 * EdgeInset * s));
            float size = thin + (thick - thin) * expand;
            float cross = Cross - Math.Min(EdgeInset * s, Math.Max(0f, (Cross - size) / 2f)) - size;   // hugs the outer edge

            if (expand > 0.01f)
            {
                Color track = Mix(back, Theme.TextMuted, 0.2f);   // adapts to any background, light or dark
                FillBar(g, Mix(back, track, _show * expand), trackStart, trackLength, cross, size);
            }

            float thumbStart, thumbLength;
            Thumb(out thumbStart, out thumbLength);
            Color thumb = _dragging ? Theme.AccentPressed : _thumbHot ? Theme.Accent : Theme.TextMuted;
            FillBar(g, Mix(back, thumb, _show), thumbStart, thumbLength, cross, size);
        }

        private void FillBar(Graphics g, Color color, float along, float length, float cross, float size)
        {
            RectangleF r = _vertical ? new RectangleF(cross, along, size, length) : new RectangleF(along, cross, length, size);
            using (GraphicsPath path = Theme.RoundRect(r, size / 2f))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        private static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }
    }
}
