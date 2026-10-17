using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    public enum PreviewSide { Both, Front, Back }

    /// <summary>
    /// Full-screen card preview.
    ///   Esc / F11 / double-click  close
    ///   Space / Tab               Both → Front → Back
    ///   F / B / A                 Front / Back / All (both)
    ///   G                         mm grid lines on / off (screen only, never printed)
    ///   ← → / PageUp PageDown / mouse wheel   previous / next student
    ///   Home / End                first / last student
    /// The same side / grid / close actions are on the toolbar buttons at the top of the screen.
    /// </summary>
    public class CardPreviewForm : Form
    {
        private static readonly Color Backdrop = Color.FromArgb(30, 30, 34);
        private static readonly Color FooterText = Color.FromArgb(200, 200, 205);
        private const int Edge = 48;
        private const int Gap = 40;
        private const int FooterHeight = 44;
        private const int ToolbarTop = 16;
        private const int ToolbarHeight = 36;
        private const int ToolbarGap = 8;

        private static readonly Color ButtonBack = Color.FromArgb(52, 52, 60);
        private static readonly Color ButtonHover = Color.FromArgb(72, 72, 84);
        private static readonly Color ButtonOn = Color.FromArgb(108, 99, 245);       // Theme.Accent
        private static readonly Color ButtonOnHover = Color.FromArgb(124, 115, 250);
        private static readonly Color ButtonDisabledText = Color.FromArgb(110, 110, 118);

        private enum ToolAction { Front, Back, Both, Grid, Close }

        private sealed class ToolButton
        {
            public ToolAction Action;
            public string Text;
            public Rectangle Bounds;
        }

        private readonly List<ToolButton> _buttons = new List<ToolButton>();
        private ToolButton _hover;
        private bool _showGrid;

        private readonly IList<Student> _students;
        private readonly AppSettings _settings;
        private readonly Font _footerFont;
        private readonly Font _buttonFont;
        private int _index;
        private PreviewSide _side;
        private Bitmap _front, _back;
        private string _error;

        /// <summary>Student shown when the form closed (MainForm selects that row).</summary>
        public int CurrentIndex { get { return _index; } }

        /// <summary>Whether the mm grid was on when the form closed (MainForm keeps the choice).</summary>
        public bool ShowGrid { get { return _showGrid; } }

        public CardPreviewForm(IList<Student> students, int startIndex, AppSettings settings, PreviewSide side,
                               bool showGrid = false)
        {
            if (Branding.Icon != null) Icon = Branding.Icon;
            _showGrid = showGrid;
            _students = students ?? new List<Student>();
            _settings = settings;
            _index = _students.Count == 0 ? 0 : Math.Max(0, Math.Min(startIndex, _students.Count - 1));
            _side = _students.Count == 0 ? PreviewSide.Back : side;
            _footerFont = new Font(CardFonts.UI, 11f);
            _buttonFont = new Font("Segoe UI Semibold", 10f);

            AddButton(ToolAction.Front, "Front   F");
            AddButton(ToolAction.Back, "Back   B");
            AddButton(ToolAction.Both, "Front + Back   A");
            AddButton(ToolAction.Grid, "Grid lines   G");
            AddButton(ToolAction.Close, "✕  Close   Esc");

            Text = "Card Preview";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Backdrop;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            ResizeRedraw = true;

            try
            {
                _back = CardRenderer.RenderBack(_settings);   // same for every student
            }
            catch (Exception ex)
            {
                _error = ex.Message;
            }
            RenderFront();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Cover the whole monitor the main window is on (including the taskbar)
            Bounds = Owner != null ? Screen.FromControl(Owner).Bounds : Screen.PrimaryScreen.Bounds;
        }

        // ===================== Data =====================
        private void RenderFront()
        {
            Bitmap old = _front;
            _front = null;
            if (old != null) old.Dispose();

            if (_students.Count == 0) return;
            try
            {
                _front = CardRenderer.RenderFront(_students[_index], _settings);
                _error = null;
            }
            catch (Exception ex)
            {
                _error = ex.Message;
            }
        }

        private void MoveTo(int index)
        {
            if (_students.Count == 0) return;
            index = Math.Max(0, Math.Min(index, _students.Count - 1));
            if (index == _index) return;

            _index = index;
            Cursor = Cursors.WaitCursor;
            RenderFront();
            Cursor = Cursors.Default;
            Invalidate();
        }

        private void SetSide(PreviewSide side)
        {
            if (_students.Count == 0) side = PreviewSide.Back;   // no student → only the back exists
            _side = side;
            Invalidate();
        }

        private void SetGrid(bool show)
        {
            _showGrid = show;
            Invalidate();
        }

        private void CycleSide()
        {
            SetSide(_side == PreviewSide.Both ? PreviewSide.Front
                  : _side == PreviewSide.Front ? PreviewSide.Back
                  : PreviewSide.Both);
        }

        // ===================== Input =====================
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                case Keys.F11:
                    Close(); return true;
                case Keys.Space:
                case Keys.Tab:
                    CycleSide(); return true;
                case Keys.F: SetSide(PreviewSide.Front); return true;
                case Keys.B: SetSide(PreviewSide.Back); return true;
                case Keys.A: SetSide(PreviewSide.Both); return true;
                case Keys.G: SetGrid(!_showGrid); return true;
                case Keys.Right:
                case Keys.Down:
                case Keys.PageDown:
                    MoveTo(_index + 1); return true;
                case Keys.Left:
                case Keys.Up:
                case Keys.PageUp:
                    MoveTo(_index - 1); return true;
                case Keys.Home: MoveTo(0); return true;
                case Keys.End: MoveTo(_students.Count - 1); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            MoveTo(_index - Math.Sign(e.Delta));
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            if (HitTest(PointToClient(Cursor.Position)) == null) Close();   // fast clicks on a button are not a "close"
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ToolButton hit = HitTest(e.Location);
            if (hit == _hover) return;
            _hover = hit;
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
            Invalidate(new Rectangle(0, 0, ClientSize.Width, ToolbarTop + ToolbarHeight + 4));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover == null) return;
            _hover = null;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            ToolButton hit = HitTest(e.Location);
            if (hit == null) return;
            switch (hit.Action)
            {
                case ToolAction.Front: SetSide(PreviewSide.Front); break;
                case ToolAction.Back: SetSide(PreviewSide.Back); break;
                case ToolAction.Both: SetSide(PreviewSide.Both); break;
                case ToolAction.Grid: SetGrid(!_showGrid); break;
                case ToolAction.Close: Close(); break;
            }
        }

        // ===================== Toolbar =====================
        private void AddButton(ToolAction action, string text)
        {
            _buttons.Add(new ToolButton { Action = action, Text = text });
        }

        /// <summary>Side and grid buttons centred at the top of the screen; Close on the right edge.</summary>
        private void LayoutButtons()
        {
            int total = 0, count = 0;
            foreach (ToolButton b in _buttons)
            {
                int w = TextRenderer.MeasureText(b.Text, _buttonFont).Width + 32;
                b.Bounds = new Rectangle(0, ToolbarTop, w, ToolbarHeight);
                if (b.Action != ToolAction.Close) { total += w; count++; }
            }
            total += ToolbarGap * Math.Max(0, count - 1);

            int x = (ClientSize.Width - total) / 2;
            foreach (ToolButton b in _buttons)
            {
                int w = b.Bounds.Width;
                if (b.Action == ToolAction.Close)
                {
                    b.Bounds = new Rectangle(ClientSize.Width - Edge / 2 - w, ToolbarTop, w, ToolbarHeight);
                    continue;
                }
                b.Bounds = new Rectangle(x, ToolbarTop, w, ToolbarHeight);
                x += w + ToolbarGap;
            }
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            LayoutButtons();
        }

        private ToolButton HitTest(Point p)
        {
            foreach (ToolButton b in _buttons)
                if (b.Bounds.Contains(p) && IsAvailable(b)) return b;
            return null;
        }

        /// <summary>Front and Front + Back need a student; the back exists without one.</summary>
        private bool IsAvailable(ToolButton b)
        {
            return _students.Count > 0 || (b.Action != ToolAction.Front && b.Action != ToolAction.Both);
        }

        private bool IsOn(ToolButton b)
        {
            switch (b.Action)
            {
                case ToolAction.Front: return _side == PreviewSide.Front;
                case ToolAction.Back: return _side == PreviewSide.Back;
                case ToolAction.Both: return _side == PreviewSide.Both;
                case ToolAction.Grid: return _showGrid;
                default: return false;
            }
        }

        private void DrawToolbar(Graphics g)
        {
            foreach (ToolButton b in _buttons)
            {
                bool available = IsAvailable(b), on = IsOn(b), hover = b == _hover;
                Color back = on ? (hover ? ButtonOnHover : ButtonOn) : (hover ? ButtonHover : ButtonBack);
                Color text = !available ? ButtonDisabledText : on ? Color.White : FooterText;

                using (GraphicsPath path = RoundedRect(b.Bounds, ToolbarHeight / 2f))
                using (var brush = new SolidBrush(back))
                    g.FillPath(brush, path);
                TextRenderer.DrawText(g, b.Text, _buttonFont, b.Bounds, text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        // ===================== Drawing =====================
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Backdrop);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var cards = new List<Bitmap>();
            if (_side != PreviewSide.Back && _front != null) cards.Add(_front);
            if (_side != PreviewSide.Front && _back != null) cards.Add(_back);

            int top = ToolbarTop + ToolbarHeight + Edge / 2;   // below the toolbar buttons
            var area = new Rectangle(Edge, top, ClientSize.Width - 2 * Edge,
                                     ClientSize.Height - top - Edge - FooterHeight);

            if (cards.Count > 0 && area.Width > 0 && area.Height > 0)
            {
                foreach (KeyValuePair<Bitmap, RectangleF> item in LayoutCards(cards, area))
                    DrawCard(g, item.Key, item.Value, _showGrid);
            }
            else if (_error != null)
            {
                TextRenderer.DrawText(g, "Preview error: " + _error, _footerFont, area, Color.Salmon,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }

            DrawToolbar(g);
            DrawFooter(g);
        }

        /// <summary>Side by side or stacked — whichever makes the cards bigger.</summary>
        private static List<KeyValuePair<Bitmap, RectangleF>> LayoutCards(List<Bitmap> cards, Rectangle area)
        {
            int n = cards.Count;
            float ratio = CardRenderer.CardWidthMm / CardRenderer.CardHeightMm;

            float wSideBySide = Math.Min((area.Width - Gap * (n - 1)) / (float)n, area.Height * ratio);
            float wStacked = Math.Min(area.Width, (area.Height - Gap * (n - 1)) / (float)n * ratio);
            bool sideBySide = wSideBySide >= wStacked;

            float w = Math.Max(1f, sideBySide ? wSideBySide : wStacked);
            float h = w / ratio;
            float totalW = sideBySide ? n * w + (n - 1) * Gap : w;
            float totalH = sideBySide ? h : n * h + (n - 1) * Gap;
            float x = area.X + (area.Width - totalW) / 2f;
            float y = area.Y + (area.Height - totalH) / 2f;

            var result = new List<KeyValuePair<Bitmap, RectangleF>>();
            for (int i = 0; i < n; i++)
            {
                var rect = sideBySide
                    ? new RectangleF(x + i * (w + Gap), y, w, h)
                    : new RectangleF(x, y + i * (h + Gap), w, h);
                result.Add(new KeyValuePair<Bitmap, RectangleF>(cards[i], rect));
            }
            return result;
        }

        /// <summary>Card with real CR80 rounded corners (3.18 mm), a soft shadow and optionally the mm grid.</summary>
        private static void DrawCard(Graphics g, Bitmap card, RectangleF rect, bool showGrid)
        {
            float radius = rect.Width * 3.18f / CardRenderer.CardWidthMm;

            for (int i = 3; i >= 1; i--)
            {
                RectangleF shadow = rect;
                shadow.Offset(0, i * 3f);
                shadow.Inflate(i * 2f, i * 2f);
                using (GraphicsPath path = RoundedRect(shadow, radius + i * 2f))
                using (var brush = new SolidBrush(Color.FromArgb(28, 0, 0, 0)))
                    g.FillPath(brush, path);
            }

            using (GraphicsPath path = RoundedRect(rect, radius))
            {
                g.SetClip(path);
                g.DrawImage(card, rect);
                if (showGrid) CardGridOverlay.Draw(g, rect);
                g.ResetClip();
                using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
                    g.DrawPath(pen, path);
            }
        }

        private void DrawFooter(Graphics g)
        {
            string info;
            if (_students.Count == 0)
            {
                info = "No students loaded — back side only";
            }
            else
            {
                Student s = _students[_index];
                info = string.Format("{0} / {1}    {2}    {3}", _index + 1, _students.Count, s.StudentId, s.NameKh);
            }

            string side = _side == PreviewSide.Both ? "Front + Back" : _side == PreviewSide.Front ? "Front" : "Back";
            string help = side + (_showGrid ? " · grid" : "") + "    |    Space: switch side    G: grid    ← →: student    Esc: close";

            var rect = new Rectangle(Edge, ClientSize.Height - Edge / 2 - FooterHeight, ClientSize.Width - 2 * Edge, FooterHeight);
            TextRenderer.DrawText(g, info, _footerFont, rect, Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, help, _footerFont, rect, FooterText,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_front != null) _front.Dispose();
                if (_back != null) _back.Dispose();
                _footerFont.Dispose();
                _buttonFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
