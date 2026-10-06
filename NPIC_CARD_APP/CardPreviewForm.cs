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
    ///   ← → / PageUp PageDown / mouse wheel   previous / next student
    ///   Home / End                first / last student
    /// </summary>
    public class CardPreviewForm : Form
    {
        private static readonly Color Backdrop = Color.FromArgb(30, 30, 34);
        private static readonly Color FooterText = Color.FromArgb(200, 200, 205);
        private const int Edge = 48;
        private const int Gap = 40;
        private const int FooterHeight = 44;

        private readonly IList<Student> _students;
        private readonly AppSettings _settings;
        private readonly Font _footerFont;
        private int _index;
        private PreviewSide _side;
        private Bitmap _front, _back;
        private string _error;

        /// <summary>Student shown when the form closed (MainForm selects that row).</summary>
        public int CurrentIndex { get { return _index; } }

        public CardPreviewForm(IList<Student> students, int startIndex, AppSettings settings, PreviewSide side)
        {
            _students = students ?? new List<Student>();
            _settings = settings;
            _index = _students.Count == 0 ? 0 : Math.Max(0, Math.Min(startIndex, _students.Count - 1));
            _side = _students.Count == 0 ? PreviewSide.Back : side;
            _footerFont = new Font(CardFonts.UI, 11f);

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
            Close();
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

            var area = new Rectangle(Edge, Edge, ClientSize.Width - 2 * Edge,
                                     ClientSize.Height - 2 * Edge - FooterHeight);

            if (cards.Count > 0 && area.Width > 0 && area.Height > 0)
            {
                foreach (KeyValuePair<Bitmap, RectangleF> item in LayoutCards(cards, area))
                    DrawCard(g, item.Key, item.Value);
            }
            else if (_error != null)
            {
                TextRenderer.DrawText(g, "Preview error: " + _error, _footerFont, area, Color.Salmon,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }

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

        /// <summary>Card with real CR80 rounded corners (3.18 mm) and a soft shadow.</summary>
        private static void DrawCard(Graphics g, Bitmap card, RectangleF rect)
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
            string help = side + "    |    Space: switch side    ← →: student    Esc: close";

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
            }
            base.Dispose(disposing);
        }
    }
}
