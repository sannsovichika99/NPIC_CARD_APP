using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Base of every popup window, so all dialogs share the main window's look:
    ///   - a white header in place of the Windows title bar: violet accent strip, icon badge, title,
    ///     optional subtitle and close / maximize buttons. Drag it to move, double-click to maximize;
    ///   - the normal Windows frame on the other sides, so resizing, snapping, the shadow and the
    ///     Windows 11 rounded corners keep working;
    ///   - a dimmed backdrop over the owner window while the popup is open with ShowDialog.
    /// Derived forms add their controls as usual: docking starts below the header (see DisplayRectangle).
    /// </summary>
    public class ModalForm : Form
    {
        public const int HeaderHeight = 60;
        private const int StripHeight = 3;
        private const int TopGrip = 6;       // band along the top edge that resizes instead of moving
        private const int CaptionSize = 34;

        private readonly CaptionButton _btnClose, _btnMax;
        private string _subtitle, _glyph = Theme.IcoDocument;
        private Color _glyphColor = Theme.Accent, _glyphBack = Theme.AccentSoft;
        private bool _resizable = true, _showClose = true;
        private int _captionLeft;
        private ModalBackdrop _backdrop;

        public ModalForm()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            if (Branding.Icon != null) Icon = Branding.Icon;
            MinimizeBox = false;
            BackColor = Theme.Surface;
            ForeColor = Theme.TextMain;
            Font = new Font(CardFonts.UI, 9f);
            DimOwner = true;

            // Anchored to the right so any layout pass keeps them in the corner; LayoutCaptionButtons sets the exact spot.
            _btnClose = new CaptionButton(CaptionGlyph.Close) { AccessibleName = "Close", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnClose.Click += (s, e) => Close();
            _btnMax = new CaptionButton(CaptionGlyph.Maximize) { AccessibleName = "Maximize", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _btnMax.Click += (s, e) =>
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            Controls.Add(_btnClose);
            Controls.Add(_btnMax);
        }

        // ===================== Public look =====================

        /// <summary>Muted second line under the title. Null = title only.</summary>
        public string Subtitle
        {
            get { return _subtitle; }
            set { _subtitle = value; InvalidateHeader(); }
        }

        /// <summary>Icon of the header badge (a Theme.Ico* glyph).</summary>
        public string Glyph
        {
            get { return _glyph; }
            set { _glyph = value; InvalidateHeader(); }
        }

        /// <summary>Colour of the header icon and of its soft badge.</summary>
        public void SetGlyphColors(Color glyph, Color badge)
        {
            _glyphColor = glyph;
            _glyphBack = badge;
            InvalidateHeader();
        }

        /// <summary>False = fixed size: the frame does not resize and there is no maximize.</summary>
        public bool Resizable
        {
            get { return _resizable; }
            set
            {
                _resizable = value;
                if (!value) MaximizeBox = false;
                PerformLayout();
            }
        }

        /// <summary>Shows the header close button (X). Hide it only when the dialog must be answered.</summary>
        public bool ShowCloseButton
        {
            get { return _showClose; }
            set { _showClose = value; PerformLayout(); }
        }

        /// <summary>Dim the owner window while this popup is shown with ShowDialog (default true).</summary>
        public bool DimOwner { get; set; }

        /// <summary>Content starts under the header: docked and anchored children never cover it.</summary>
        public override Rectangle DisplayRectangle
        {
            get
            {
                Rectangle r = base.DisplayRectangle;
                r.Y += HeaderHeight;
                r.Height = Math.Max(0, r.Height - HeaderHeight);
                return r;
            }
        }

        private Rectangle HeaderBounds { get { return new Rectangle(0, 0, ClientSize.Width, HeaderHeight); } }

        private void InvalidateHeader()
        {
            if (IsHandleCreated) Invalidate(HeaderBounds);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            InvalidateHeader();
        }

        // ===================== Layout / paint =====================

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutCaptionButtons();
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            LayoutCaptionButtons();
        }

        private void LayoutCaptionButtons()
        {
            if (_btnClose == null) return;   // the Form constructor lays out before ours runs

            int y = StripHeight + (HeaderHeight - StripHeight - CaptionSize) / 2;
            int x = ClientSize.Width - 12;

            _btnClose.Visible = _showClose;
            if (_showClose)
            {
                x -= CaptionSize;
                _btnClose.SetBounds(x, y, CaptionSize, CaptionSize);
                x -= 4;
            }

            bool max = MaximizeBox && _resizable;
            _btnMax.Visible = max;
            if (max)
            {
                x -= CaptionSize;
                _btnMax.SetBounds(x, y, CaptionSize, CaptionSize);
                x -= 4;
            }
            _btnMax.Glyph = WindowState == FormWindowState.Maximized ? CaptionGlyph.Restore : CaptionGlyph.Maximize;
            _btnMax.AccessibleName = WindowState == FormWindowState.Maximized ? "Restore" : "Maximize";
            _captionLeft = x;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int w = ClientSize.Width;
            if (w <= 0 || !e.ClipRectangle.IntersectsWith(HeaderBounds)) return;

            Graphics g = e.Graphics;
            Theme.HighQuality(g);

            using (var strip = new LinearGradientBrush(new Rectangle(0, 0, w, StripHeight), Theme.VioletA, Theme.VioletB, 0f))
                g.FillRectangle(strip, 0, 0, w, StripHeight);
            using (var surface = new SolidBrush(Theme.Surface))
                g.FillRectangle(surface, 0, StripHeight, w, HeaderHeight - StripHeight);

            float cy = StripHeight + (HeaderHeight - StripHeight) / 2f;
            float textX = 20;
            if (Theme.IconFamily != null && !string.IsNullOrEmpty(_glyph))
            {
                var badge = new RectangleF(18, cy - 17, 34, 34);
                using (GraphicsPath p = Theme.RoundRect(badge, 10))
                using (var b = new SolidBrush(_glyphBack))
                    g.FillPath(b, p);
                Theme.DrawIcon(g, _glyph, 16f, _glyphColor, badge);
                textX = badge.Right + 12;
            }

            float textW = Math.Max(10, _captionLeft - 8 - textX);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (string.IsNullOrEmpty(_subtitle))
            {
                Theme.DrawText(g, Text, Theme.CardTitle, Theme.TextMain, new RectangleF(textX, StripHeight, textW, HeaderHeight - StripHeight));
            }
            else
            {
                Theme.DrawText(g, Text, Theme.CardTitle, Theme.TextMain, new RectangleF(textX, cy - 21, textW, 22));
                Theme.DrawText(g, _subtitle, Theme.Small, Theme.TextSoft, new RectangleF(textX, cy + 1, textW, 18));
            }

            using (var pen = new Pen(Theme.Border))
                g.DrawLine(pen, 0, HeaderHeight - 1, w, HeaderHeight - 1);
        }

        // ===================== Backdrop =====================

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);   // also centres the popup on its owner
            if (DimOwner && Modal) ShowBackdrop();
        }

        private void ShowBackdrop()
        {
            Form owner = FromHandle(Native.GetWindow(Handle, Native.GW_OWNER)) as Form;
            if (owner == null || !owner.Visible || owner.WindowState == FormWindowState.Minimized) return;
            try
            {
                _backdrop = new ModalBackdrop(owner);
                _backdrop.Show(owner);   // shown before the popup, so the popup stays above it
            }
            catch (Exception)
            {
                CloseBackdrop();         // purely visual: never block the dialog
            }
        }

        private void CloseBackdrop()
        {
            if (_backdrop == null) return;
            ModalBackdrop b = _backdrop;
            _backdrop = null;
            b.Close();
            b.Dispose();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CloseBackdrop();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) CloseBackdrop();
            base.Dispose(disposing);
        }

        // ===================== Window frame =====================

        protected override CreateParams CreateParams
        {
            get
            {
                // Keep the sizing frame (resize, snap, shadow, rounded corners) but drop the Windows title bar.
                CreateParams cp = base.CreateParams;
                cp.Style &= ~Native.WS_CAPTION;
                cp.Style |= Native.WS_THICKFRAME;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetCorners(Handle, true);
            DarkControls.Apply(this);   // before the child handles exist, so restyling recreates nothing
            SlimScroll.Apply(this);
            // Re-run WM_NCCALCSIZE so the top frame becomes client area (the header).
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_FRAMECHANGED | Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER |
                Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }

        /// <summary>The top frame is client area (see WM_NCCALCSIZE), so ClientSize needs only the side and bottom frames.</summary>
        protected override Size SizeFromClientSize(Size clientSize)
        {
            if (IsHandleCreated && WindowState == FormWindowState.Normal)
            {
                // Measure the real frame: AdjustWindowRectEx does not know about the custom WM_NCCALCSIZE.
                Native.RECT wr, cr;
                if (Native.GetWindowRect(Handle, out wr) && Native.GetClientRect(Handle, out cr))
                    return new Size(clientSize.Width + (wr.Right - wr.Left) - cr.Right,
                                    clientSize.Height + (wr.Bottom - wr.Top) - cr.Bottom);
            }
            Size s = base.SizeFromClientSize(clientSize);
            return new Size(s.Width, s.Height - (s.Height - clientSize.Height) / 2);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                var before = (Native.NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(Native.NCCALCSIZE_PARAMS));
                base.WndProc(ref m);
                var after = (Native.NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(Native.NCCALCSIZE_PARAMS));
                int top = before.rgrc0.Top;
                // Maximized windows hang over the screen edge by the frame width: keep the header on screen.
                if (Native.IsZoomed(m.HWnd)) top += before.rgrc0.Bottom - after.rgrc0.Bottom;
                after.rgrc0.Top = top;
                Marshal.StructureToPtr(after, m.LParam, false);
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == Native.WM_NCHITTEST)
            {
                base.WndProc(ref m);
                int hit = m.Result.ToInt32();
                if (hit == Native.HTCLIENT)
                {
                    long lp = m.LParam.ToInt64();
                    Point pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    if (pt.Y < HeaderHeight)
                    {
                        if (_resizable && WindowState == FormWindowState.Normal && pt.Y < TopGrip)
                            hit = pt.X < TopGrip * 3 ? Native.HTTOPLEFT
                                : pt.X >= ClientSize.Width - TopGrip * 3 ? Native.HTTOPRIGHT
                                : Native.HTTOP;
                        else
                            hit = Native.HTCAPTION;
                    }
                }
                else if (!_resizable && hit >= Native.HTLEFT && hit <= Native.HTBOTTOMRIGHT)
                {
                    hit = Native.HTBORDER;
                }
                m.Result = (IntPtr)hit;
                return;
            }

            base.WndProc(ref m);
        }
    }

    /// <summary>Shared building blocks for popup content: footer bar, pill buttons, status bar.</summary>
    internal static class ModalUi
    {
        public const int FooterHeight = 64;

        /// <summary>Rounded pill button sized for popups. Primary = the main action of the popup.</summary>
        public static PillButton Button(string text, PillKind kind, EventHandler onClick = null)
        {
            var b = new PillButton(kind)
            {
                Text = text, AutoSize = true, Padding = new Padding(12, 0, 12, 0), Margin = new Padding(8, 0, 0, 0),
                MinimumSize = new Size(kind == PillKind.Primary ? 120 : 92, 36)
            };
            if (onClick != null) b.Click += onClick;
            return b;
        }

        /// <summary>Smaller secondary pill for toolbars.</summary>
        public static PillButton ToolButton(string text, EventHandler onClick)
        {
            var b = new PillButton(PillKind.Secondary)
            {
                Text = text, AutoSize = true, Padding = new Padding(10, 0, 10, 0), Margin = new Padding(0, 0, 6, 0),
                MinimumSize = new Size(0, 32)
            };
            if (onClick != null) b.Click += onClick;
            return b;
        }

        /// <summary>Bottom button bar: soft background, top divider, buttons right-aligned (first added = rightmost).</summary>
        public static FlowLayoutPanel Footer()
        {
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = FooterHeight, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Padding = new Padding(16, 14, 16, 0), BackColor = Theme.SurfaceAlt
            };
            footer.Paint += PaintTopLine;
            return footer;
        }

        /// <summary>Top toolbar of a popup: white, with a hairline underneath.</summary>
        public static FlowLayoutPanel Toolbar(int height = 52)
        {
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = height, WrapContents = false, Padding = new Padding(16, 10, 16, 0),
                BackColor = Theme.Surface
            };
            bar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.GridLine))
                    e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            };
            return bar;
        }

        /// <summary>Muted status / summary line at the bottom of a popup.</summary>
        public static void StyleStatus(Label label)
        {
            label.BackColor = Theme.SurfaceAlt;
            label.ForeColor = Theme.TextSoft;
            label.Padding = new Padding(16, label.Padding.Top, 16, label.Padding.Bottom);
            label.Paint += PaintTopLine;
        }

        private static void PaintTopLine(object sender, PaintEventArgs e)
        {
            var c = (Control)sender;
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawLine(pen, 0, 0, c.Width, 0);
        }
    }

    /// <summary>
    /// Themed replacement for <see cref="MessageBox.Show(IWin32Window, string)"/> with the same overloads and results:
    /// popup header with a coloured icon, the message, and pill buttons in the usual Windows order.
    /// Enter = default button, Esc = Cancel (or OK when it is the only button), Ctrl+C copies the message.
    /// </summary>
    internal static class AppDialog
    {
        public static DialogResult Show(string text)
        {
            return Show(null, text, null, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(string text, string caption)
        {
            return Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons)
        {
            return Show(null, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(null, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text)
        {
            return Show(owner, text, null, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text, string caption)
        {
            return Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons)
        {
            return Show(owner, text, caption, buttons, MessageBoxIcon.None, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons,
            MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            using (var dlg = new MessageDialog(text, caption, buttons, icon, defaultButton))
            {
                if (owner == null) dlg.StartPosition = FormStartPosition.CenterScreen;
                return owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            }
        }
    }

    /// <summary>The window behind <see cref="AppDialog"/>.</summary>
    internal sealed class MessageDialog : ModalForm
    {
        private const int MaxTextWidth = 440;
        private static readonly Font MessageFont = new Font(CardFonts.UI, 9.75f);

        private readonly string _message;
        private readonly MessageBoxIcon _icon;
        private readonly bool _canCancel;
        private readonly Button _default;
        private readonly Size _clientSize;

        public MessageDialog(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            _message = text ?? "";
            _icon = icon;
            Text = string.IsNullOrEmpty(caption) ? "NPIC Card" : caption;
            Resizable = false;
            KeyPreview = true;
            ApplyIcon(icon);

            // ---- Message ----
            var label = new Label
            {
                AutoSize = true, MaximumSize = new Size(MaxTextWidth, 0), Text = _message, Font = MessageFont,
                ForeColor = Theme.TextMain, BackColor = Theme.Surface, UseMnemonic = false, Margin = new Padding(0)
            };
            Size textSize = label.PreferredSize;
            int maxTextHeight = Math.Max(160, Screen.FromPoint(Cursor.Position).WorkingArea.Height / 2);

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(24, 20, 24, 20) };
            if (textSize.Height <= maxTextHeight)
            {
                label.Location = new Point(24, 20);
                body.Controls.Add(label);
            }
            else
            {
                // Very long text (error details): scrollable, still selectable.
                textSize = new Size(MaxTextWidth + SystemInformation.VerticalScrollBarWidth, maxTextHeight);
                body.Controls.Add(new TextBox
                {
                    Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                    BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.TextMain, Font = MessageFont,
                    Text = _message.Replace("\r\n", "\n").Replace("\n", "\r\n"), TabStop = false
                });
            }

            // ---- Buttons (Windows order, left to right) ----
            DialogResult[] results = ResultsFor(buttons);
            var footer = ModalUi.Footer();
            var made = new Button[results.Length];
            for (int i = 0; i < results.Length; i++)
            {
                made[i] = ModalUi.Button(Caption(results[i]), i == 0 ? PillKind.Primary : PillKind.Secondary);
                made[i].DialogResult = results[i];
            }
            for (int i = results.Length - 1; i >= 0; i--) footer.Controls.Add(made[i]);   // right-to-left flow

            int def = defaultButton == MessageBoxDefaultButton.Button3 ? 2 : defaultButton == MessageBoxDefaultButton.Button2 ? 1 : 0;
            _default = made[Math.Min(def, made.Length - 1)];
            AcceptButton = _default;

            int cancel = Array.IndexOf(results, DialogResult.Cancel);
            if (cancel < 0 && results.Length == 1) cancel = 0;
            _canCancel = cancel >= 0;
            if (_canCancel) CancelButton = made[cancel];
            ShowCloseButton = _canCancel;   // Yes / No questions must be answered, like MessageBox

            Controls.Add(body);
            Controls.Add(footer);

            // ---- Size ----
            int footerWidth = 32;
            foreach (Button b in made) footerWidth += Math.Max(b.MinimumSize.Width, b.PreferredSize.Width) + 8;
            int titleWidth = TextRenderer.MeasureText(Text, Theme.CardTitle).Width + 64 + 60;
            int width = Math.Max(textSize.Width + 48, Math.Max(footerWidth, titleWidth));
            width = Math.Max(400, Math.Min(600, width));
            _clientSize = new Size(width, HeaderHeight + textSize.Height + 40 + ModalUi.FooterHeight);
            ClientSize = _clientSize;
        }

        private void ApplyIcon(MessageBoxIcon icon)
        {
            switch (icon)
            {
                case MessageBoxIcon.Error:
                    Glyph = Theme.IcoError;
                    SetGlyphColors(Theme.Danger, Theme.DangerSoft);
                    break;
                case MessageBoxIcon.Warning:
                    Glyph = Theme.IcoWarning;
                    SetGlyphColors(Theme.Warning, Theme.WarningSoft);
                    break;
                case MessageBoxIcon.Question:
                    Glyph = Theme.IcoQuestion;
                    break;
                default:
                    Glyph = Theme.IcoInfo;
                    break;
            }
        }

        private static DialogResult[] ResultsFor(MessageBoxButtons buttons)
        {
            switch (buttons)
            {
                case MessageBoxButtons.OKCancel: return new[] { DialogResult.OK, DialogResult.Cancel };
                case MessageBoxButtons.AbortRetryIgnore: return new[] { DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore };
                case MessageBoxButtons.YesNoCancel: return new[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel };
                case MessageBoxButtons.YesNo: return new[] { DialogResult.Yes, DialogResult.No };
                case MessageBoxButtons.RetryCancel: return new[] { DialogResult.Retry, DialogResult.Cancel };
                default: return new[] { DialogResult.OK };
            }
        }

        private static string Caption(DialogResult r)
        {
            switch (r)
            {
                case DialogResult.Cancel: return "Cancel";
                case DialogResult.Abort: return "Abort";
                case DialogResult.Retry: return "Retry";
                case DialogResult.Ignore: return "Ignore";
                case DialogResult.Yes: return "Yes";
                case DialogResult.No: return "No";
                default: return "OK";
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            ClientSize = _clientSize;   // again now that the real frame is known; before base.OnLoad centres the window
            base.OnLoad(e);
            ActiveControl = _default;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            switch (_icon)
            {
                case MessageBoxIcon.Error: SystemSounds.Hand.Play(); break;
                case MessageBoxIcon.Warning: SystemSounds.Exclamation.Play(); break;
                case MessageBoxIcon.Question: SystemSounds.Question.Play(); break;
                case MessageBoxIcon.Information: SystemSounds.Asterisk.Play(); break;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Alt+F4 on a Yes / No question: ignored, like MessageBox.
            if (!_canCancel && DialogResult == DialogResult.None && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C))
            {
                try { Clipboard.SetText(Text + Environment.NewLine + Environment.NewLine + _message); }
                catch (ExternalException) { }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal enum CaptionGlyph { Close, Maximize, Restore }

    /// <summary>Header button of a popup (close / maximize / restore), drawn with lines so it needs no icon font.</summary>
    internal sealed class CaptionButton : Control
    {
        private bool _hover, _down;
        private CaptionGlyph _glyph;

        public CaptionButton(CaptionGlyph glyph)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            _glyph = glyph;
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }

        public CaptionGlyph Glyph
        {
            get { return _glyph; }
            set { if (_glyph == value) return; _glyph = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Theme.HighQuality(g);

            bool close = _glyph == CaptionGlyph.Close;
            if (_hover || _down)
            {
                Color back = close
                    ? (_down ? Theme.DangerSoftDown : Theme.DangerSoft)
                    : (_down ? Theme.AccentSoftDown : Theme.AccentSoft);
                using (GraphicsPath p = Theme.RoundRect(new RectangleF(1, 1, Width - 2.5f, Height - 2.5f), 9))
                using (var b = new SolidBrush(back))
                    g.FillPath(b, p);
            }

            Color fore = !_hover ? Theme.TextSoft : close ? Theme.Danger : Theme.Accent;
            float cx = Width / 2f, cy = Height / 2f, h = 5f;
            using (var pen = new Pen(fore, 1.4f))
            {
                switch (_glyph)
                {
                    case CaptionGlyph.Close:
                        g.DrawLine(pen, cx - h, cy - h, cx + h, cy + h);
                        g.DrawLine(pen, cx + h, cy - h, cx - h, cy + h);
                        break;
                    case CaptionGlyph.Maximize:
                        g.DrawRectangle(pen, cx - h, cy - h, 2 * h, 2 * h);
                        break;
                    default:
                        g.DrawRectangle(pen, cx - h, cy - h + 2, 2 * h - 2, 2 * h - 2);
                        g.DrawLines(pen, new[] { new PointF(cx - h + 2, cy - h), new PointF(cx + h, cy - h), new PointF(cx + h, cy + h - 2) });
                        break;
                }
            }
        }
    }

    /// <summary>Translucent veil over the owner window while a popup is open. Never takes focus.</summary>
    internal sealed class ModalBackdrop : Form
    {
        private readonly bool _round;

        public ModalBackdrop(Form owner)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Backdrop;
            Opacity = Theme.BackdropOpacity;
            Bounds = Native.VisibleBounds(owner);
            _round = owner.WindowState == FormWindowState.Normal;
            Text = "";
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetCorners(Handle, _round);   // match the owner's rounded corners on Windows 11
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }
    }

    internal static class Native
    {
        public const int WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
        public const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080;
        public const int WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084, WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;
        public const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                         HTBOTTOMRIGHT = 17, HTBORDER = 18;
        public const uint GW_OWNER = 4;
        public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010,
                          SWP_FRAMECHANGED = 0x0020, SWP_NOOWNERZORDER = 0x0200;

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct NCCALCSIZE_PARAMS
        {
            public RECT rgrc0, rgrc1, rgrc2;
            public IntPtr lppos;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>Dark title bar and frame (Windows 10 1809+ / 11); ignored on older Windows.</summary>
        public static void SetDarkFrame(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref on, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        /// <summary>Visual-style class of a control, e.g. "DarkMode_Explorer" for dark scroll bars; ignored where unknown.</summary>
        public static void SetControlTheme(IntPtr hwnd, string subAppName)
        {
            try { SetWindowTheme(hwnd, subAppName, null); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        /// <summary>Windows 11 corner style; ignored on older Windows.</summary>
        public static void SetCorners(IntPtr hwnd, bool round)
        {
            try
            {
                int pref = round ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        /// <summary>The window as drawn on screen, without the invisible resize borders of Windows 10 / 11.</summary>
        public static Rectangle VisibleBounds(Form form)
        {
            try
            {
                RECT r;
                if (DwmGetWindowAttribute(form.Handle, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) == 0 &&
                    r.Right > r.Left && r.Bottom > r.Top)
                    return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return form.Bounds;
        }
    }
}
