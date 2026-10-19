using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Full-screen 3D card preview: the front and back are the two faces of one real card (CR80, 0.76 mm thick,
    /// 3.18 mm corners) that can be turned in space. Drawn by a small software ray caster, so it needs no 3D library.
    ///   drag                      turn the card (it keeps spinning a little when released)
    ///   double-click / Space      flip to the other side
    ///   F / B                     face the front / back straight on
    ///   A                         auto spin on / off
    ///   R                         reset the view
    ///   mouse wheel / + −         zoom
    ///   ← → / PageUp PageDown     previous / next student;  Home / End  first / last
    ///   Esc / Shift+F11           close
    /// </summary>
    public class CardPreview3DForm : Form
    {
        private static readonly Color Backdrop = Color.FromArgb(30, 30, 34);
        private static readonly Color Spotlight = Color.FromArgb(58, 58, 68);
        private static readonly Color FooterText = Color.FromArgb(200, 200, 205);
        private const int Edge = 48;
        private const int FooterHeight = 44;
        private const int ToolbarTop = 16;
        private const int ToolbarHeight = 36;
        private const int ToolbarGap = 8;

        private static readonly Color ButtonBack = Color.FromArgb(52, 52, 60);
        private static readonly Color ButtonHover = Color.FromArgb(72, 72, 84);
        private static readonly Color ButtonOn = Theme.ButtonA;
        private static readonly Color ButtonOnHover = Theme.ButtonHover;

        // ---- Card geometry (mm) and camera ----
        private const double ThicknessMm = 0.76;
        private const double CornerMm = 3.18;
        private const double CameraDistance = 2.8;        // × card width: mild, natural perspective
        private const double MaxRenderWidth = 1150;       // card width in rendered pixels; larger screens scale the frame up
        private const double DefaultYaw = -24, DefaultPitch = 12;
        private const double MaxPitch = 80;
        private const double DegreesPerPixel = 0.45;
        private const double SpinSpeed = 40;              // degrees per second
        private const double MinZoom = 0.5, MaxZoom = 2.2;

        // Light from the upper left, in front of the card (world space: x right, y up, z toward the viewer).
        private static readonly double LightX, LightY, LightZ;

        static CardPreview3DForm()
        {
            double lx = -0.35, ly = 0.5, lz = 0.8, len = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            LightX = lx / len; LightY = ly / len; LightZ = lz / len;
        }

        private enum ToolAction { Front, Back, Flip, Spin, Reset, Close }

        private sealed class ToolButton
        {
            public ToolAction Action;
            public string Text;
            public Rectangle Bounds;
        }

        private readonly List<ToolButton> _buttons = new List<ToolButton>();
        private ToolButton _hover;

        private readonly IList<Student> _students;
        private readonly AppSettings _settings;
        private readonly Font _footerFont;
        private readonly Font _buttonFont;
        private int _index;
        private Bitmap _front, _back;            // full-resolution card images
        private Texture _texFront, _texBack;     // scaled copies the ray caster samples
        private string _error;
        private double _cardW = CardRenderer.CardWidthMm, _cardH = CardRenderer.CardHeightMm;
        private double[] _outlineX, _outlineY;   // rounded-rectangle outline (mm), counter-clockwise

        // ---- View state ----
        private double _yaw = DefaultYaw - 200, _pitch = DefaultPitch, _zoom = 1;
        private double _velYaw, _velPitch;       // degrees per second after a drag
        private bool _autoSpin;
        private bool _frontFacing = true;        // which face the last frame showed

        private bool _tweenActive;
        private double _tFromYaw, _tFromPitch, _tToYaw, _tToPitch, _tStart, _tDuration;

        private bool _dragging, _dragMoved;
        private Point _dragLast;
        private double _dragLastTime;

        private readonly Timer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastTick;

        private Bitmap _frame;                   // rendered stage: shadow, card rim and face
        private Bitmap _backdrop;                // cached background with the soft spotlight
        private int[] _buf = new int[0];

        /// <summary>Student shown when the form closed (MainForm selects that row).</summary>
        public int CurrentIndex { get { return _index; } }

        public CardPreview3DForm(IList<Student> students, int startIndex, AppSettings settings)
        {
            if (Branding.Icon != null) Icon = Branding.Icon;
            _students = students ?? new List<Student>();
            _settings = settings;
            _index = _students.Count == 0 ? 0 : Math.Max(0, Math.Min(startIndex, _students.Count - 1));
            _footerFont = new Font(CardFonts.UI, 11f);
            _buttonFont = Theme.UiFont(10f, true);

            AddButton(ToolAction.Front, "Front   F");
            AddButton(ToolAction.Back, "Back   B");
            AddButton(ToolAction.Flip, "Flip   Space");
            AddButton(ToolAction.Spin, "Auto spin   A");
            AddButton(ToolAction.Reset, "Reset   R");
            AddButton(ToolAction.Close, "✕  Close   Esc");

            Text = Lang.T("3D Card Preview");
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Backdrop;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;

            try
            {
                _back = CardRenderer.RenderBack(_settings);   // same for every student
            }
            catch (Exception ex)
            {
                _error = ex.Message;
            }
            RenderFront();
            UpdateGeometry();

            _timer = new Timer { Interval = 15 };
            _timer.Tick += (s, e) => Tick();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Bounds = Owner != null ? Screen.FromControl(Owner).Bounds : Screen.PrimaryScreen.Bounds;

            // Short entrance: the card turns in from the other side and settles at a slight angle.
            double target = _students.Count == 0 ? DefaultYaw + 180 : DefaultYaw;
            _yaw = target - 200;
            StartTween(target, DefaultPitch, 1.1);
            _lastTick = Now;
            _timer.Start();
        }

        // ===================== Data =====================
        private void RenderFront()
        {
            Bitmap old = _front;
            _front = null;
            if (old != null) old.Dispose();
            _texFront = null;

            if (_students.Count == 0)
            {
                _front = Placeholder(Lang.T("No students loaded — back side only"));
                return;
            }
            try
            {
                _front = CardRenderer.RenderFront(_students[_index], _settings);
                _error = null;
            }
            catch (Exception ex)
            {
                _error = ex.Message;
                _front = Placeholder(Lang.T("Select a student"));
            }
        }

        /// <summary>Plain grey face for a card side that cannot be drawn.</summary>
        private static Bitmap Placeholder(string text)
        {
            int w = 1000, h = (int)Math.Round(w * CardRenderer.CardHeightMm / CardRenderer.CardWidthMm);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var font = new Font(CardFonts.UI, 26f))
            {
                g.Clear(Color.FromArgb(226, 226, 232));
                TextRenderer.DrawText(g, text, font, new Rectangle(40, 0, w - 80, h), Color.FromArgb(120, 120, 130),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
            return bmp;
        }

        /// <summary>Card size follows the images: a vertical design gives a standing card.</summary>
        private void UpdateGeometry()
        {
            Bitmap reference = _back ?? _front;
            bool portrait = reference != null && reference.Height > reference.Width;
            _cardW = portrait ? CardRenderer.CardHeightMm : CardRenderer.CardWidthMm;
            _cardH = portrait ? CardRenderer.CardWidthMm : CardRenderer.CardHeightMm;

            const int perCorner = 10;
            double hx = _cardW / 2 - CornerMm, hy = _cardH / 2 - CornerMm;
            double[] cx = { hx, -hx, -hx, hx }, cy = { hy, hy, -hy, -hy };
            var xs = new List<double>();
            var ys = new List<double>();
            for (int c = 0; c < 4; c++)
            {
                for (int k = 0; k <= perCorner; k++)
                {
                    double a = (c * 90 + 90.0 * k / perCorner) * Math.PI / 180;
                    xs.Add(cx[c] + CornerMm * Math.Cos(a));
                    ys.Add(cy[c] + CornerMm * Math.Sin(a));
                }
            }
            _outlineX = xs.ToArray();
            _outlineY = ys.ToArray();
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

        // ===================== Motion =====================
        private double Now { get { return _clock.Elapsed.TotalSeconds; } }

        private void StartTween(double yaw, double pitch, double seconds)
        {
            _tFromYaw = _yaw; _tFromPitch = _pitch;
            _tToYaw = yaw; _tToPitch = pitch;
            _tStart = Now; _tDuration = seconds;
            _tweenActive = true;
            _velYaw = _velPitch = 0;
            Invalidate();
        }

        /// <summary>Turns to the front (yaw ≡ 0°) or back (yaw ≡ 180°) the shortest way, flat to the viewer.</summary>
        private void ShowFace(bool front)
        {
            _autoSpin = false;
            double phase = front ? 0 : 180;
            double target = Math.Round((_yaw - phase) / 360) * 360 + phase;
            StartTween(target, 0, 0.7);
        }

        private void Flip()
        {
            _autoSpin = false;
            StartTween(Math.Round(_yaw / 180) * 180 + 180, 0, 0.75);
        }

        private void ResetView()
        {
            _autoSpin = false;
            _zoom = 1;
            double phase = _students.Count == 0 ? DefaultYaw + 180 : DefaultYaw;
            StartTween(Math.Round((_yaw - phase) / 360) * 360 + phase, DefaultPitch, 0.7);
        }

        private void ToggleSpin()
        {
            _autoSpin = !_autoSpin;
            _tweenActive = false;
            _velYaw = _velPitch = 0;
            Invalidate();
        }

        private void Zoom(double factor)
        {
            double z = Math.Max(MinZoom, Math.Min(MaxZoom, _zoom * factor));
            if (Math.Abs(z - _zoom) < 1e-6) return;
            _zoom = z;
            Invalidate();
        }

        private void Tick()
        {
            double now = Now;
            double dt = Math.Min(0.05, Math.Max(0, now - _lastTick));
            _lastTick = now;
            bool changed = false;

            if (_tweenActive)
            {
                double k = _tDuration <= 0 ? 1 : (now - _tStart) / _tDuration;
                if (k >= 1) { k = 1; _tweenActive = false; }
                double e = k < 0.5 ? 4 * k * k * k : 1 - Math.Pow(-2 * k + 2, 3) / 2;   // ease in-out cubic
                _yaw = _tFromYaw + (_tToYaw - _tFromYaw) * e;
                _pitch = _tFromPitch + (_tToPitch - _tFromPitch) * e;
                changed = true;
            }
            else if (_dragging)
            {
                // the mouse moves the card directly
            }
            else if (_autoSpin)
            {
                _yaw += SpinSpeed * dt;
                changed = true;
            }
            else if (Math.Abs(_velYaw) > 2 || Math.Abs(_velPitch) > 2)
            {
                _yaw += _velYaw * dt;
                _pitch += _velPitch * dt;
                if (Math.Abs(_pitch) > MaxPitch) { _pitch = Math.Sign(_pitch) * MaxPitch; _velPitch = 0; }
                double decay = Math.Exp(-2.6 * dt);
                _velYaw *= decay;
                _velPitch *= decay;
                changed = true;
            }

            if (changed) Invalidate();
        }

        // ===================== Input =====================
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                case Keys.Shift | Keys.F11:
                    Close(); return true;
                case Keys.Space: Flip(); return true;
                case Keys.F: ShowFace(true); return true;
                case Keys.B: ShowFace(false); return true;
                case Keys.A: ToggleSpin(); return true;
                case Keys.R: ResetView(); return true;
                case Keys.Add:
                case Keys.Oemplus:
                    Zoom(1.12); return true;
                case Keys.Subtract:
                case Keys.OemMinus:
                    Zoom(1 / 1.12); return true;
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
            Zoom(Math.Pow(1.12, e.Delta / 120.0));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || HitTest(e.Location) != null) return;
            _dragging = true;
            _dragMoved = false;
            _dragLast = e.Location;
            _dragLastTime = Now;
            _velYaw = _velPitch = 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging && (e.Button & MouseButtons.Left) != 0)
            {
                int dx = e.X - _dragLast.X, dy = e.Y - _dragLast.Y;
                if (dx == 0 && dy == 0) return;
                if (!_dragMoved)
                {
                    _dragMoved = true;
                    _tweenActive = false;
                    _autoSpin = false;
                    Cursor = Cursors.SizeAll;
                }
                double now = Now, dtm = Math.Max(0.004, now - _dragLastTime);
                double dYaw = dx * DegreesPerPixel, dPitch = dy * DegreesPerPixel;
                _yaw += dYaw;
                _pitch = Math.Max(-MaxPitch, Math.Min(MaxPitch, _pitch + dPitch));
                _velYaw = Clamp(0.5 * _velYaw + 0.5 * dYaw / dtm, 900);
                _velPitch = Clamp(0.5 * _velPitch + 0.5 * dPitch / dtm, 900);
                _dragLast = e.Location;
                _dragLastTime = now;
                Invalidate();
                return;
            }

            ToolButton hit = HitTest(e.Location);
            if (hit == _hover) return;
            _hover = hit;
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false;
            if (!_dragMoved || Now - _dragLastTime > 0.08) _velYaw = _velPitch = 0;   // held still before letting go
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && HitTest(e.Location) == null) Flip();
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
                case ToolAction.Front: ShowFace(true); break;
                case ToolAction.Back: ShowFace(false); break;
                case ToolAction.Flip: Flip(); break;
                case ToolAction.Spin: ToggleSpin(); break;
                case ToolAction.Reset: ResetView(); break;
                case ToolAction.Close: Close(); break;
            }
        }

        private static double Clamp(double v, double limit)
        {
            return Math.Max(-limit, Math.Min(limit, v));
        }

        // ===================== Toolbar =====================
        private void AddButton(ToolAction action, string text)
        {
            _buttons.Add(new ToolButton { Action = action, Text = Lang.T(text) });
        }

        /// <summary>View buttons centred at the top of the screen; Close on the right edge.</summary>
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
            if (_backdrop != null) { _backdrop.Dispose(); _backdrop = null; }
            Invalidate();
        }

        private ToolButton HitTest(Point p)
        {
            foreach (ToolButton b in _buttons)
                if (b.Bounds.Contains(p)) return b;
            return null;
        }

        private bool IsOn(ToolButton b)
        {
            switch (b.Action)
            {
                case ToolAction.Front: return _frontFacing;
                case ToolAction.Back: return !_frontFacing;
                case ToolAction.Spin: return _autoSpin;
                default: return false;
            }
        }

        private void DrawToolbar(Graphics g)
        {
            foreach (ToolButton b in _buttons)
            {
                bool on = IsOn(b), hover = b == _hover;
                Color back = on ? (hover ? ButtonOnHover : ButtonOn) : (hover ? ButtonHover : ButtonBack);
                Color text = on ? Color.White : FooterText;

                using (GraphicsPath path = RoundedRect(b.Bounds, ToolbarHeight / 2f))
                using (var brush = new SolidBrush(back))
                    g.FillPath(brush, path);
                TextRenderer.DrawText(g, b.Text, _buttonFont, b.Bounds, text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        // ===================== Drawing =====================
        private Rectangle StageRect()
        {
            int top = ToolbarTop + ToolbarHeight + 8;
            int bottom = ClientSize.Height - Edge / 2 - FooterHeight;
            return new Rectangle(0, top, ClientSize.Width, Math.Max(0, bottom - top));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // everything is painted in OnPaint
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            DrawBackdrop(g);

            Rectangle stage = StageRect();
            if (stage.Width > 40 && stage.Height > 40)
            {
                try
                {
                    double scale = RenderFrame(stage);
                    g.CompositingQuality = CompositingQuality.HighSpeed;
                    if (scale >= 0.999)
                    {
                        g.DrawImageUnscaled(_frame, stage.Location);
                    }
                    else
                    {
                        g.InterpolationMode = InterpolationMode.Bilinear;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(_frame, stage);
                    }
                }
                catch (Exception ex)
                {
                    _error = ex.Message;
                }
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawToolbar(g);
            DrawFooter(g);
        }

        /// <summary>Dark backdrop with a soft spotlight behind the card, cached until the window size changes.</summary>
        private void DrawBackdrop(Graphics g)
        {
            Size size = ClientSize;
            if (size.Width <= 0 || size.Height <= 0) return;
            if (_backdrop == null)
            {
                _backdrop = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                using (Graphics bg = Graphics.FromImage(_backdrop))
                {
                    bg.Clear(Backdrop);
                    Rectangle stage = StageRect();
                    var glow = new RectangleF(stage.X + stage.Width * 0.12f, stage.Y - stage.Height * 0.15f,
                                              stage.Width * 0.76f, stage.Height * 1.2f);
                    if (glow.Width > 1 && glow.Height > 1)
                    {
                        using (var path = new GraphicsPath())
                        {
                            path.AddEllipse(glow);
                            using (var brush = new PathGradientBrush(path))
                            {
                                brush.CenterColor = Spotlight;
                                brush.SurroundColors = new[] { Backdrop };
                                bg.FillPath(brush, path);
                            }
                        }
                    }
                }
            }
            g.DrawImageUnscaled(_backdrop, 0, 0);
        }

        /// <summary>
        /// Renders shadow, rim and the visible face into <see cref="_frame"/>; returns the frame's scale to the stage.
        /// The card lies in its own x/y plane (mm, centred, y up); R = Ry(yaw)·Rx(pitch) turns it into the world,
        /// where the camera sits at (0, 0, D) looking down −z.
        /// </summary>
        private double RenderFrame(Rectangle stage)
        {
            double cw = _cardW, ch = _cardH;
            double fit = Math.Min(stage.Width * 0.62 / cw, stage.Height * 0.64 / ch);
            double screenScale = fit * _zoom;
            double renderScale = Math.Min(1.0, MaxRenderWidth / (cw * screenScale));
            int fw = Math.Max(1, (int)Math.Ceiling(stage.Width * renderScale));
            int fh = Math.Max(1, (int)Math.Ceiling(stage.Height * renderScale));
            if (_frame == null || _frame.Width != fw || _frame.Height != fh)
            {
                if (_frame != null) _frame.Dispose();
                _frame = new Bitmap(fw, fh, PixelFormat.Format32bppPArgb);
            }

            double s = screenScale * renderScale;              // frame pixels per mm at the card's centre
            double camD = CameraDistance * Math.Max(cw, ch);
            double f = s * camD;                               // focal length in frame pixels
            double fcx = fw / 2.0, fcy = fh * 0.46;

            double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
            double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
            double r00 = cy, r01 = sy * sp, r02 = sy * cp;
            double r10 = 0, r11 = cp, r12 = -sp;
            double r20 = -sy, r21 = cy * sp, r22 = cy * cp;

            bool front = r22 > 0;                              // camera on the card's +z side
            _frontFacing = front;
            Texture tex = EnsureTexture(front, cw * s);
            double h = ThicknessMm / 2;

            // ---- projected bounds of the 8 box corners ----
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < 8; i++)
            {
                double x = (i & 1) == 0 ? -cw / 2 : cw / 2, y = (i & 2) == 0 ? -ch / 2 : ch / 2, z = (i & 4) == 0 ? -h : h;
                PointF p = Project(x, y, z, r00, r01, r02, r10, r11, r12, r20, r21, r22, camD, f, fcx, fcy);
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }

            using (Graphics g = Graphics.FromImage(_frame))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawShadow(g, fcx, fcy + ch * s * 0.66, Math.Max(maxX - minX, cw * s * 0.18), ch * s);
                DrawRim(g, h, r00, r01, r02, r10, r11, r12, r20, r21, r22, camD, f, fcx, fcy);
            }

            if (tex == null) return renderScale;

            var box = Rectangle.FromLTRB((int)Math.Floor(minX) - 2, (int)Math.Floor(minY) - 2,
                                         (int)Math.Ceiling(maxX) + 2, (int)Math.Ceiling(maxY) + 2);
            box.Intersect(new Rectangle(0, 0, fw, fh));
            if (box.Width <= 0 || box.Height <= 0) return renderScale;

            // ---- ray cast the visible face ----
            double zf = front ? h : -h;
            // camera in card space: Rᵀ·(0, 0, D)
            double ccx = r20 * camD, ccy = r21 * camD, ccz = r22 * camD;
            // ray direction in card space for screen offset (sx, sy): d = Rᵀ·(sx, −sy, −f) = A·sx + B·sy + C
            double ax = r00, bx = -r10, cxk = -r20 * f;
            double ay = r01, by = -r11, cyk = -r21 * f;
            double az = r02, bz = -r12, czk = -r22 * f;

            // face normal in the world, toward the camera side that is shown
            double sign = front ? 1 : -1;
            double nx = r02 * sign, ny = r12 * sign, nz = r22 * sign;
            double diffuse = 0.80 + 0.22 * Math.Max(0, nx * LightX + ny * LightY + nz * LightZ);

            // edge anti-aliasing: card millimetres covered by one pixel (wider when the card is seen at an angle)
            double fp = 1 / s / Math.Sqrt(Math.Max(Math.Abs(r22), 0.06));
            double halfFp = fp / 2;

            double halfW = cw / 2, halfH = ch / 2;
            double hx = halfW - CornerMm, hy = halfH - CornerMm, rad = CornerMm;
            int tw = tex.Width, th = tex.Height;
            int[] tp = tex.Pixels;
            double f2 = f * f;

            int bw = box.Width, bh = box.Height;
            if (_buf.Length < bw * bh) _buf = new int[bw * bh];
            int[] buf = _buf;

            BitmapData data = _frame.LockBits(box, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < bh; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), buf, y * bw, bw);

                for (int y = 0; y < bh; y++)
                {
                    double syv = box.Y + y + 0.5 - fcy;
                    double rowX = bx * syv + cxk, rowY = by * syv + cyk, rowZ = bz * syv + czk;
                    int row = y * bw;
                    for (int x = 0; x < bw; x++)
                    {
                        double sxv = box.X + x + 0.5 - fcx;
                        double dz = az * sxv + rowZ;
                        if (dz > -1e-9 && dz < 1e-9) continue;
                        double t = (zf - ccz) / dz;
                        if (t <= 0) continue;
                        double px = ccx + t * (ax * sxv + rowX);
                        double py = ccy + t * (ay * sxv + rowY);

                        // signed distance (mm) to the rounded-rectangle edge
                        double qx = (px < 0 ? -px : px) - hx, qy = (py < 0 ? -py : py) - hy;
                        double sd = qx > 0 && qy > 0 ? Math.Sqrt(qx * qx + qy * qy) - rad : Math.Max(qx, qy) - rad;
                        if (sd >= halfFp) continue;
                        double cov = sd <= -halfFp ? 1 : 0.5 - sd / fp;

                        // texture coordinates; the back is seen from behind, so its x runs the other way
                        double u = front ? (px + halfW) / cw : (halfW - px) / cw;
                        double v = (halfH - py) / ch;
                        double fx = u * tw - 0.5, fy = v * th - 0.5;
                        if (fx < 0) fx = 0; else if (fx > tw - 1) fx = tw - 1;
                        if (fy < 0) fy = 0; else if (fy > th - 1) fy = th - 1;
                        int x0 = (int)fx, y0 = (int)fy;
                        int x1 = x0 + 1 < tw ? x0 + 1 : x0, y1 = y0 + 1 < th ? y0 + 1 : y0;
                        int wx = (int)((fx - x0) * 256), wy = (int)((fy - y0) * 256);
                        int c00 = tp[y0 * tw + x0], c10 = tp[y0 * tw + x1], c01 = tp[y1 * tw + x0], c11 = tp[y1 * tw + x1];

                        int ta = Lerp4(c00, c10, c01, c11, wx, wy, 24);
                        double alpha = cov * ta / 255.0;
                        if (alpha <= 0.002) continue;
                        int tr = Lerp4(c00, c10, c01, c11, wx, wy, 16);
                        int tg = Lerp4(c00, c10, c01, c11, wx, wy, 8);
                        int tb = Lerp4(c00, c10, c01, c11, wx, wy, 0);

                        // glossy laminate: Blinn-Phong highlight; view vector = −ray direction
                        double inv = 1 / Math.Sqrt(sxv * sxv + syv * syv + f2);
                        double hvx = LightX - sxv * inv, hvy = LightY + syv * inv, hvz = LightZ + f * inv;
                        double ndh = (nx * hvx + ny * hvy + nz * hvz) / Math.Sqrt(hvx * hvx + hvy * hvy + hvz * hvz);
                        double spec = 0;
                        if (ndh > 0)
                        {
                            double p2 = ndh * ndh, p4 = p2 * p2, p8 = p4 * p4, p16 = p8 * p8, p64 = p16 * p16 * p16 * p16;
                            spec = (0.42 * p64 + 0.06 * p8) * 255;
                        }

                        double rr = tr * diffuse + spec, gg = tg * diffuse + spec, bb = tb * diffuse + spec;
                        if (rr > 255) rr = 255;
                        if (gg > 255) gg = 255;
                        if (bb > 255) bb = 255;

                        int oa = (int)(alpha * 255 + 0.5);
                        int pr = (int)(rr * alpha + 0.5), og = (int)(gg * alpha + 0.5), ob = (int)(bb * alpha + 0.5);
                        int i = row + x;
                        if (oa < 255)
                        {
                            int d = buf[i];
                            double rest = 1 - alpha;
                            oa = Math.Min(255, oa + (int)(((d >> 24) & 255) * rest));
                            pr = Math.Min(255, pr + (int)(((d >> 16) & 255) * rest));
                            og = Math.Min(255, og + (int)(((d >> 8) & 255) * rest));
                            ob = Math.Min(255, ob + (int)((d & 255) * rest));
                        }
                        buf[i] = (oa << 24) | (pr << 16) | (og << 8) | ob;
                    }
                }

                for (int y = 0; y < bh; y++)
                    Marshal.Copy(buf, y * bw, IntPtr.Add(data.Scan0, y * data.Stride), bw);
            }
            finally
            {
                _frame.UnlockBits(data);
            }
            return renderScale;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Lerp4(int c00, int c10, int c01, int c11, int wx, int wy, int shift)
        {
            int top = ((c00 >> shift) & 255) * (256 - wx) + ((c10 >> shift) & 255) * wx;
            int bottom = ((c01 >> shift) & 255) * (256 - wx) + ((c11 >> shift) & 255) * wx;
            return (top * (256 - wy) + bottom * wy) >> 16;
        }

        private static PointF Project(double x, double y, double z,
                                      double r00, double r01, double r02, double r10, double r11, double r12,
                                      double r20, double r21, double r22, double camD, double f, double fcx, double fcy)
        {
            double wx = r00 * x + r01 * y + r02 * z;
            double wy = r10 * x + r11 * y + r12 * z;
            double wz = r20 * x + r21 * y + r22 * z;
            double depth = Math.Max(1e-3, camD - wz);
            return new PointF((float)(fcx + f * wx / depth), (float)(fcy - f * wy / depth));
        }

        /// <summary>Soft contact shadow on an imaginary floor below the card; narrows as the card turns edge-on.</summary>
        private static void DrawShadow(Graphics g, double cx, double cy, double width, double cardHeightPx)
        {
            float w = (float)(width * 0.92), hgt = (float)Math.Max(8, cardHeightPx * 0.13);
            var rect = new RectangleF((float)cx - w / 2, (float)cy - hgt / 2, w, hgt);
            if (rect.Width < 2 || rect.Height < 2) return;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (var brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(120, 0, 0, 0);
                    brush.SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) };
                    g.FillPath(brush, path);
                }
            }
        }

        /// <summary>The card's 0.76 mm edge: one shaded quad per outline segment that faces the camera.</summary>
        private void DrawRim(Graphics g, double h,
                             double r00, double r01, double r02, double r10, double r11, double r12,
                             double r20, double r21, double r22, double camD, double f, double fcx, double fcy)
        {
            int n = _outlineX.Length;
            var quad = new PointF[4];
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                double x0 = _outlineX[i], y0 = _outlineY[i], x1 = _outlineX[j], y1 = _outlineY[j];
                double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                double nlx = dy / len, nly = -dx / len;                       // outward (outline is counter-clockwise)

                double nwx = r00 * nlx + r01 * nly, nwy = r10 * nlx + r11 * nly, nwz = r20 * nlx + r21 * nly;
                double mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
                double mwx = r00 * mx + r01 * my, mwy = r10 * mx + r11 * my, mwz = r20 * mx + r21 * my;
                if (nwx * -mwx + nwy * -mwy + nwz * (camD - mwz) <= 0) continue;   // faces away

                double light = 0.50 + 0.50 * Math.Max(0, nwx * LightX + nwy * LightY + nwz * LightZ);
                int c = (int)Math.Min(255, 236 * light);
                using (var brush = new SolidBrush(Color.FromArgb(255, c, c, Math.Min(255, c + 4))))
                using (var pen = new Pen(brush.Color, 1f))
                {
                    quad[0] = Project(x0, y0, h, r00, r01, r02, r10, r11, r12, r20, r21, r22, camD, f, fcx, fcy);
                    quad[1] = Project(x1, y1, h, r00, r01, r02, r10, r11, r12, r20, r21, r22, camD, f, fcx, fcy);
                    quad[2] = Project(x1, y1, -h, r00, r01, r02, r10, r11, r12, r20, r21, r22, camD, f, fcx, fcy);
                    quad[3] = Project(x0, y0, -h, r00, r01, r02, r10, r11, r12, r20, r21, r22, camD, f, fcx, fcy);
                    g.FillPolygon(brush, quad);
                    g.DrawPolygon(pen, quad);   // closes the hairline seams between neighbouring quads
                }
            }
        }

        /// <summary>Scaled copy of the face image, rebuilt only when the on-screen size changes a lot.</summary>
        private Texture EnsureTexture(bool front, double cardPixels)
        {
            Bitmap source = front ? _front : _back;
            if (source == null) return null;
            int target = (int)Math.Min(source.Width, Math.Max(64, Math.Ceiling(cardPixels * 1.3)));
            Texture tex = front ? _texFront : _texBack;
            if (tex == null || tex.Width < target * 0.8 || tex.Width > target * 1.6)
            {
                tex = new Texture(source, target);
                if (front) _texFront = tex; else _texBack = tex;
            }
            return tex;
        }

        private void DrawFooter(Graphics g)
        {
            string info;
            if (_students.Count == 0)
            {
                info = Lang.T("No students loaded — back side only");
            }
            else
            {
                Student s = _students[_index];
                info = string.Format("{0} / {1}    {2}    {3}", _index + 1, _students.Count, s.StudentId, s.NameKh);
            }
            if (_error != null) info += "    " + Lang.F("Preview error: {0}", _error);

            string side = Lang.T(_frontFacing ? "Front" : "Back");
            string zoom = Math.Abs(_zoom - 1) > 0.01 ? "  ·  " + Math.Round(_zoom * 100) + " %" : "";
            string help = side + zoom +
                          Lang.T("    |    Drag: turn    Wheel: zoom    Space: flip    ← →: student    Esc: close");

            var rect = new Rectangle(Edge, ClientSize.Height - Edge / 2 - FooterHeight, ClientSize.Width - 2 * Edge, FooterHeight);
            TextRenderer.DrawText(g, info, _footerFont, rect, _error != null ? Color.Salmon : Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
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
                _timer.Stop();
                _timer.Dispose();
                if (_front != null) _front.Dispose();
                if (_back != null) _back.Dispose();
                if (_frame != null) _frame.Dispose();
                if (_backdrop != null) _backdrop.Dispose();
                _footerFont.Dispose();
                _buttonFont.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>ARGB pixels of a card face, high-quality scaled to about the size it is drawn at.</summary>
        private sealed class Texture
        {
            public readonly int Width, Height;
            public readonly int[] Pixels;

            public Texture(Bitmap source, int width)
            {
                width = Math.Max(16, Math.Min(width, source.Width));
                int height = Math.Max(1, (int)Math.Round(width * (double)source.Height / source.Width));
                Width = width;
                Height = height;
                Pixels = new int[width * height];

                using (var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(scaled))
                    using (var attributes = new ImageAttributes())
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        attributes.SetWrapMode(WrapMode.TileFlipXY);   // no dark fringe along the edges
                        g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height,
                                    GraphicsUnit.Pixel, attributes);
                    }
                    BitmapData data = scaled.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly,
                                                      PixelFormat.Format32bppArgb);
                    try
                    {
                        for (int y = 0; y < height; y++)
                            Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), Pixels, y * width, width);
                    }
                    finally
                    {
                        scaled.UnlockBits(data);
                    }
                }
            }
        }
    }
}
