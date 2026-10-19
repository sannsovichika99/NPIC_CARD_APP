using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
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
    ///   I                         export a picture of the card as it is shown (.png, transparent or dark background / .jpg)
    ///   Shift+I                   the same picture for every student in the list, one file each, into one folder
    ///   V                         export a video of the card turning (.mp4, see Mp4VideoWriter)
    ///   Esc / Shift+F11           close (cancels a running video export)
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

        // ---- Video export: one seamless loop of a full turn, Full HD H.264 ----
        private const int VideoWidth = 1920, VideoHeight = 1080, VideoFps = 30;
        private const double VideoSeconds = 8;
        private const int VideoBitrate = 12000000;
        private const double VideoYaw = -16, VideoPitch = 9;
        private const int ExportBudgetMs = 45;            // frames rendered per timer tick, so the window stays responsive

        // ---- Image export: the view on screen, re-rendered so the card is drawn at its own (600 dpi) resolution ----
        private const int ImageMaxSide = 8000;            // pixels; keeps a zoomed-in export within memory
        private const int ImageMargin = 48;               // pixels left around the card + shadow in a transparent PNG
        private static int _imageFilter = 1;              // last file type picked, as an ImageKind

        /// <summary>Image file types, numbered as the entries of the save dialog's type list.</summary>
        private enum ImageKind { TransparentPng = 1, DarkPng = 2, Jpeg = 3 }

        // Light from the upper left, in front of the card (world space: x right, y up, z toward the viewer).
        private static readonly double LightX, LightY, LightZ;

        static CardPreview3DForm()
        {
            double lx = -0.35, ly = 0.5, lz = 0.8, len = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            LightX = lx / len; LightY = ly / len; LightZ = lz / len;
        }

        private enum ToolAction { Front, Back, Flip, Spin, Reset, Image, AllImages, Video, Close, Cancel }

        private sealed class ToolButton
        {
            public ToolAction Action;
            public string Text;
            public bool Compact;               // narrow screen: the label without its shortcut
            public Rectangle Bounds;

            /// <summary>"Reset   R" → "Reset" when the toolbar has to be compact.</summary>
            public string Shown
            {
                get
                {
                    int cut = Compact ? Text.LastIndexOf("   ", StringComparison.Ordinal) : -1;
                    return cut > 0 ? Text.Substring(0, cut) : Text;
                }
            }
        }

        private readonly List<ToolButton> _buttons = new List<ToolButton>();
        private readonly ToolButton _cancelButton;   // the only button while a video is exported
        private ToolButton _hover;

        /// <summary>A running video export: frames are rendered and encoded a few per timer tick.</summary>
        private sealed class VideoExport
        {
            public string FilePath;
            public Mp4VideoWriter Writer;
            public Bitmap Frame;               // the frame being written; also the live preview on screen
            public Bitmap Backdrop;            // video-sized background with the spotlight
            public int Done, Total;
            public double StartYaw;
            public double Yaw, Pitch, Zoom;    // the view to return to afterwards
            public bool Spin;
        }

        private VideoExport _export;

        /// <summary>A running "all students" image export: one student per timer tick.</summary>
        private sealed class ImageBatch
        {
            public string Folder;
            public string[] Paths;             // one file per student, same order as the list
            public ImageKind Kind;
            public double Yaw, Pitch, Zoom;    // the view every image is rendered from
            public int Done, Saved;
            public readonly List<string> Failed = new List<string>();
            public int StartIndex;             // the student and view to return to afterwards
            public double ViewYaw, ViewPitch;
            public bool Spin;
        }

        private ImageBatch _batch;

        /// <summary>A video or image export is running: the view and the toolbar wait for it.</summary>
        private bool Busy { get { return _export != null || _batch != null; } }

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
            AddButton(ToolAction.Image, "Export image   I");
            AddButton(ToolAction.AllImages, "Export all images   Shift+I");
            AddButton(ToolAction.Video, "Export video   V");
            AddButton(ToolAction.Close, "✕  Close   Esc");
            _cancelButton = new ToolButton { Action = ToolAction.Cancel, Text = Lang.T("✕  Cancel   Esc") };

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
            if (_export != null)
            {
                ExportFrames();
                return;
            }
            if (_batch != null)
            {
                ExportNextImage();
                return;
            }
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
            if (Busy)
            {
                // the view is not touched while a video or images are exported; Esc cancels it
                if (keyData == Keys.Escape) { CancelExport(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
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
                case Keys.I: ExportImage(); return true;
                case Keys.Shift | Keys.I: StartImageBatch(); return true;
                case Keys.V: StartVideoExport(); return true;
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
            if (!Busy) Zoom(Math.Pow(1.12, e.Delta / 120.0));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Busy || e.Button != MouseButtons.Left || HitTest(e.Location) != null) return;
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
            if (!Busy && e.Button == MouseButtons.Left && HitTest(e.Location) == null) Flip();
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
                case ToolAction.Image: ExportImage(); break;
                case ToolAction.AllImages: StartImageBatch(); break;
                case ToolAction.Video: StartVideoExport(); break;
                case ToolAction.Close: Close(); break;
                case ToolAction.Cancel: CancelExport(); break;
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

        private static bool OnRight(ToolAction action)
        {
            return action == ToolAction.Image || action == ToolAction.AllImages || action == ToolAction.Video ||
                   action == ToolAction.Close;
        }

        private int ButtonWidth(ToolButton b)
        {
            return TextRenderer.MeasureText(b.Shown, _buttonFont).Width + 32;
        }

        /// <summary>
        /// View buttons centred at the top of the screen; the export buttons and Close on the right edge.
        /// When they do not all fit, the labels drop their shortcut keys.
        /// </summary>
        private void LayoutButtons()
        {
            int total = 0, count = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int all = 0;
                total = 0;
                count = 0;
                foreach (ToolButton b in _buttons)
                {
                    b.Compact = pass == 1;
                    b.Bounds = new Rectangle(0, ToolbarTop, ButtonWidth(b), ToolbarHeight);
                    all += b.Bounds.Width + ToolbarGap;
                    if (!OnRight(b.Action)) { total += b.Bounds.Width; count++; }
                }
                if (all + Edge + 2 * ToolbarGap <= ClientSize.Width) break;
            }
            total += ToolbarGap * Math.Max(0, count - 1);

            int right = ClientSize.Width - Edge / 2;
            for (int i = _buttons.Count - 1; i >= 0; i--)
            {
                ToolButton b = _buttons[i];
                if (!OnRight(b.Action)) continue;
                right -= b.Bounds.Width;
                b.Bounds = new Rectangle(right, ToolbarTop, b.Bounds.Width, ToolbarHeight);
                right -= ToolbarGap;
            }

            // centred, but moved left when a narrow screen would make it run into the right-hand buttons
            int x = Math.Max(Edge / 2, Math.Min((ClientSize.Width - total) / 2, right - total));
            foreach (ToolButton b in _buttons)
            {
                if (OnRight(b.Action)) continue;
                b.Bounds = new Rectangle(x, ToolbarTop, b.Bounds.Width, ToolbarHeight);
                x += b.Bounds.Width + ToolbarGap;
            }

            int cw = ButtonWidth(_cancelButton);
            _cancelButton.Bounds = new Rectangle(ClientSize.Width - Edge / 2 - cw, ToolbarTop, cw, ToolbarHeight);
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
            if (Busy) return _cancelButton.Bounds.Contains(p) ? _cancelButton : null;
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
            if (_export != null)
            {
                double k = _export.Total <= 0 ? 1 : (double)_export.Done / _export.Total;
                DrawProgress(g, k, Lang.F("Exporting video…  {0} %", (int)(Math.Min(1, k) * 100)));
                DrawButton(g, _cancelButton, false);
                return;
            }
            if (_batch != null)
            {
                int total = _batch.Paths.Length;
                DrawProgress(g, total == 0 ? 1 : (double)_batch.Done / total,
                    Lang.F("Exporting images…  {0} / {1}", _batch.Done, total));
                DrawButton(g, _cancelButton, false);
                return;
            }
            foreach (ToolButton b in _buttons) DrawButton(g, b, IsOn(b));
        }

        private void DrawButton(Graphics g, ToolButton b, bool on)
        {
            bool hover = b == _hover;
            Color back = on ? (hover ? ButtonOnHover : ButtonOn) : (hover ? ButtonHover : ButtonBack);
            Color text = on ? Color.White : FooterText;

            using (GraphicsPath path = RoundedRect(b.Bounds, ToolbarHeight / 2f))
            using (var brush = new SolidBrush(back))
                g.FillPath(brush, path);
            TextRenderer.DrawText(g, b.Shown, _buttonFont, b.Bounds, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        /// <summary>Progress pill in the middle of the toolbar while a video or images are exported.</summary>
        private void DrawProgress(Graphics g, double k, string label)
        {
            k = Math.Max(0, Math.Min(1, k));
            int w = Math.Max(120, Math.Min(460, _cancelButton.Bounds.Left - Edge - ToolbarGap));
            var bar = new Rectangle((ClientSize.Width - w) / 2, ToolbarTop, w, ToolbarHeight);
            if (bar.Right > _cancelButton.Bounds.Left - ToolbarGap) bar.X = Math.Max(Edge / 2, _cancelButton.Bounds.Left - ToolbarGap - w);

            using (GraphicsPath path = RoundedRect(bar, ToolbarHeight / 2f))
            {
                using (var brush = new SolidBrush(ButtonBack)) g.FillPath(brush, path);
                GraphicsState state = g.Save();
                g.SetClip(new Rectangle(bar.X, bar.Y, (int)Math.Round(bar.Width * k), bar.Height));
                using (var brush = new SolidBrush(ButtonOn)) g.FillPath(brush, path);
                g.Restore(state);
            }
            TextRenderer.DrawText(g, label, _buttonFont, bar, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        // ===================== Video export =====================
        /// <summary>Asks for a file name, then records <see cref="VideoSeconds"/> of the card turning into an .mp4.</summary>
        private void StartVideoExport()
        {
            if (Busy) return;

            string path;
            using (var dlg = new SaveFileDialog
            {
                Title = Lang.T("Export 3D card video"),
                Filter = Lang.T("MP4 video") + " (*.mp4)|*.mp4",
                FileName = ExportFileName(".mp4"),
                DefaultExt = "mp4",
                AddExtension = true,
                OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }

            Mp4VideoWriter writer;
            try
            {
                writer = new Mp4VideoWriter(path, VideoWidth, VideoHeight, VideoFps, VideoBitrate);
            }
            catch (Exception ex)
            {
                ShowExportError(ex);
                return;
            }

            var size = new Size(VideoWidth, VideoHeight);
            _export = new VideoExport
            {
                FilePath = path,
                Writer = writer,
                Frame = new Bitmap(VideoWidth, VideoHeight, PixelFormat.Format32bppRgb),
                Backdrop = CreateBackdrop(size, new Rectangle(Point.Empty, size)),
                Total = (int)Math.Round(VideoSeconds * VideoFps),
                StartYaw = _students.Count == 0 ? VideoYaw + 180 : VideoYaw,   // no student: start on the back
                Yaw = _tweenActive ? _tToYaw : _yaw,
                Pitch = _tweenActive ? _tToPitch : _pitch,
                Zoom = _zoom,
                Spin = _autoSpin
            };
            _autoSpin = false;
            _tweenActive = false;
            _dragging = false;
            _velYaw = _velPitch = 0;
            _hover = null;
            Cursor = Cursors.Default;
            Invalidate();
        }

        /// <summary>"&lt;student id&gt;-3D&lt;extension&gt;", the suggested name for an exported image or video.</summary>
        private string ExportFileName(string extension)
        {
            return CardFileName(_students.Count == 0 ? null : _students[_index], extension);
        }

        /// <summary>"&lt;student id&gt;-3D&lt;extension&gt;"; "card-back-3D…" without a student.</summary>
        private static string CardFileName(Student student, string extension)
        {
            string name = student == null ? "card-back" : (student.StudentId ?? "").Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return (name.Length == 0 ? "card" : name) + "-3D" + extension;
        }

        /// <summary>Esc / Cancel while exporting: stops the video or the image batch.</summary>
        private void CancelExport()
        {
            if (_export != null) EndExport(false);
            else if (_batch != null) EndImageBatch(false, true);
        }

        /// <summary>Renders and encodes frames for about <see cref="ExportBudgetMs"/>, then lets the window repaint.</summary>
        private void ExportFrames()
        {
            VideoExport ex = _export;
            var budget = Stopwatch.StartNew();
            try
            {
                do
                {
                    RenderVideoFrame(ex);
                    ex.Writer.WriteFrame(ex.Frame);
                    ex.Done++;
                }
                while (ex.Done < ex.Total && budget.ElapsedMilliseconds < ExportBudgetMs);

                if (ex.Done < ex.Total)
                {
                    Invalidate();
                    return;
                }
                ex.Writer.Finish();
            }
            catch (Exception error)
            {
                EndExport(false);
                ShowExportError(error);
                return;
            }

            EndExport(true);
            if (AppDialog.Show(this, "Video saved:\n" + ex.FilePath + "\n\nOpen it now?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { Process.Start(ex.FilePath); }
                catch (Exception error) { AppDialog.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        /// <summary>
        /// One full turn that slows down while each face is toward the viewer and loops seamlessly:
        /// yaw speed ∝ 1 − 0.8·cos 4πu, so it is slowest at the front (u = 0, 1) and the back (u = ½).
        /// </summary>
        private void RenderVideoFrame(VideoExport ex)
        {
            double u = (double)ex.Done / ex.Total;   // the last frame stops one step short of the first
            _yaw = ex.StartYaw + 360 * (u - 0.8 * Math.Sin(4 * Math.PI * u) / (4 * Math.PI));
            _pitch = VideoPitch + 4 * Math.Sin(2 * Math.PI * u);
            _zoom = 1;

            var stage = new Rectangle(0, 0, ex.Frame.Width, ex.Frame.Height);
            double scale = RenderFrame(stage);
            using (Graphics g = Graphics.FromImage(ex.Frame))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(ex.Backdrop, 0, 0);
                g.CompositingMode = CompositingMode.SourceOver;
                DrawStageFrame(g, stage, scale);
            }
        }

        /// <summary>The frame last written, fitted into the stage, as a live preview of the recording.</summary>
        private void DrawExportPreview(Graphics g, Rectangle stage)
        {
            Bitmap frame = _export.Frame;
            if (_export.Done == 0) return;
            double k = Math.Min((stage.Width - 2.0 * Edge) / frame.Width, (double)stage.Height / frame.Height);
            if (k <= 0) return;
            int w = (int)(frame.Width * k), h = (int)(frame.Height * k);
            var r = new Rectangle(stage.X + (stage.Width - w) / 2, stage.Y + (stage.Height - h) / 2, w, h);
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(frame, r);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            using (var pen = new Pen(ButtonHover))
                g.DrawRectangle(pen, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
        }

        /// <summary>Stops the export and returns to the view from before it; an unfinished file is deleted.</summary>
        private void EndExport(bool completed)
        {
            VideoExport ex = _export;
            if (ex == null) return;
            _export = null;

            ex.Writer.Dispose();
            ex.Frame.Dispose();
            ex.Backdrop.Dispose();
            if (!completed)
            {
                try { File.Delete(ex.FilePath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            _yaw = ex.Yaw;
            _pitch = ex.Pitch;
            _zoom = ex.Zoom;
            _autoSpin = ex.Spin;
            _lastTick = Now;
            _hover = null;
            Invalidate();
        }

        private void ShowExportError(Exception error)
        {
            string text = error is DllNotFoundException || error is EntryPointNotFoundException
                ? "Video export needs Windows Media Foundation, which is missing on this computer.\n\n" +
                  "On Windows N or KN editions, install the Media Feature Pack (Settings → Apps → Optional features), then try again."
                : "Could not export the video:\n\n" + error.Message;
            AppDialog.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ===================== Image export =====================
        /// <summary>
        /// Saves the card as it is shown: same angle, zoom and framing as the screen, re-rendered larger so the card
        /// face keeps the detail of the card images. A transparent PNG is cropped to the card and its shadow.
        /// </summary>
        private void ExportImage()
        {
            if (Busy) return;

            // The view at the moment of the click: a tween or auto spin keeps running behind the file dialog.
            double yaw = _tweenActive ? _tToYaw : _yaw, pitch = _tweenActive ? _tToPitch : _pitch, zoom = _zoom;

            string path;
            ImageKind kind;
            using (SaveFileDialog dlg = ImageDialog(Lang.T("Export 3D card image")))
            {
                dlg.OverwritePrompt = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
                _imageFilter = dlg.FilterIndex;
                kind = KindFor(path, dlg.FilterIndex);
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                RenderAndSave(path, kind, yaw, pitch, zoom);
            }
            catch (Exception error)
            {
                AppDialog.Show(this, "Could not export the image:\n\n" + error.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally
            {
                _lastTick = Now;
                Cursor = Cursors.Default;
                Invalidate();
            }

            if (AppDialog.Show(this, "Image saved:\n" + path + "\n\nOpen it now?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { Process.Start(path); }
                catch (Exception error) { AppDialog.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        /// <summary>Save dialog for one image (or, in a batch, the folder and type), opened on the type used last.</summary>
        private SaveFileDialog ImageDialog(string title)
        {
            if (_imageFilter < 1 || _imageFilter > 3) _imageFilter = 1;
            string ext = Extension((ImageKind)_imageFilter);
            return new SaveFileDialog
            {
                Title = title,
                Filter = Lang.T("PNG image, transparent background") + " (*.png)|*.png|" +
                         Lang.T("PNG image, dark background") + " (*.png)|*.png|" +
                         Lang.T("JPEG image, dark background") + " (*.jpg)|*.jpg;*.jpeg",
                FilterIndex = _imageFilter,
                FileName = ExportFileName(ext),
                DefaultExt = ext.Substring(1),
                AddExtension = true
            };
        }

        private static string Extension(ImageKind kind)
        {
            return kind == ImageKind.Jpeg ? ".jpg" : ".png";
        }

        /// <summary>The file name's extension decides JPEG or PNG; the type picked decides the PNG background.</summary>
        private static ImageKind KindFor(string path, int filterIndex)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg") return ImageKind.Jpeg;
            return filterIndex == (int)ImageKind.TransparentPng ? ImageKind.TransparentPng : ImageKind.DarkPng;
        }

        /// <summary>Renders the view (<paramref name="yaw"/>, <paramref name="pitch"/>, <paramref name="zoom"/>) at export size and writes it.</summary>
        private void RenderAndSave(string path, ImageKind kind, double yaw, double pitch, double zoom)
        {
            double nowYaw = _yaw, nowPitch = _pitch, nowZoom = _zoom;
            try
            {
                _yaw = yaw; _pitch = pitch; _zoom = zoom;
                using (Bitmap image = RenderImage(kind == ImageKind.TransparentPng))
                    SaveImage(image, path, kind == ImageKind.Jpeg);
            }
            finally
            {
                _yaw = nowYaw; _pitch = nowPitch; _zoom = nowZoom;
                // drop the export-sized frame (the next paint makes a screen-sized one); a batch keeps the pixel buffer
                if (_frame != null) { _frame.Dispose(); _frame = null; }
                if (_batch == null) _buf = new int[0];
            }
        }

        // ===================== Image export: all students =====================
        /// <summary>
        /// Asks for a folder and file type (through a save dialog), then saves the current view of every student's card,
        /// named by student id. Runs one student per timer tick, so the window shows each card and Cancel works.
        /// </summary>
        private void StartImageBatch()
        {
            if (Busy) return;
            if (_students.Count == 0)
            {
                AppDialog.Show(this, "Load a student list first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            double viewYaw = _tweenActive ? _tToYaw : _yaw, viewPitch = _tweenActive ? _tToPitch : _pitch;
            double yaw = viewYaw, pitch = viewPitch;
            // The back is the same on every card: when it faces the viewer, turn the card round so each image shows its student.
            if (Math.Cos(yaw * Math.PI / 180) * Math.Cos(pitch * Math.PI / 180) <= 0) yaw += 180;

            string folder;
            ImageKind kind;
            using (SaveFileDialog dlg = ImageDialog(Lang.F("Export images of all {0} students: choose the folder and file type",
                                                           _students.Count)))
            {
                dlg.OverwritePrompt = false;   // asked below, once for all files
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                folder = Path.GetDirectoryName(dlg.FileName);
                _imageFilter = dlg.FilterIndex;
                kind = KindFor(dlg.FileName, dlg.FilterIndex);
            }

            // one file per student, named by id; repeated or empty ids get -2, -3 …
            string ext = Extension(kind);
            var paths = new string[_students.Count];
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int existing = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                string file = CardFileName(_students[i], ext);
                string stem = Path.GetFileNameWithoutExtension(file);
                for (int n = 2; !used.Add(file); n++) file = stem + "-" + n + ext;
                paths[i] = Path.Combine(folder, file);
                if (File.Exists(paths[i])) existing++;
            }
            if (existing > 0 &&
                AppDialog.Show(this, string.Format("{0} of the {1} image files are already in this folder. Replace them?",
                                                   existing, paths.Length),
                               Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _batch = new ImageBatch
            {
                Folder = folder,
                Paths = paths,
                Kind = kind,
                Yaw = yaw,
                Pitch = pitch,
                Zoom = _zoom,
                StartIndex = _index,
                ViewYaw = viewYaw,
                ViewPitch = viewPitch,
                Spin = _autoSpin
            };
            // the screen shows each card from the angle it is saved at
            _yaw = yaw;
            _pitch = pitch;
            _autoSpin = false;
            _tweenActive = false;
            _dragging = false;
            _velYaw = _velPitch = 0;
            _hover = null;
            Cursor = Cursors.Default;
            Invalidate();
        }

        /// <summary>Renders and saves the next student's image; ends the batch after the last one.</summary>
        private void ExportNextImage()
        {
            ImageBatch b = _batch;
            int i = b.Done;
            if (i < b.Paths.Length)
            {
                Student s = _students[i];
                string who = string.IsNullOrWhiteSpace(s.StudentId) ? "#" + (i + 1) : s.StudentId.Trim();
                _index = i;
                RenderFront();   // also what the screen shows next
                if (_error != null)
                {
                    b.Failed.Add(who + ": " + _error);
                }
                else
                {
                    try
                    {
                        RenderAndSave(b.Paths[i], b.Kind, b.Yaw, b.Pitch, b.Zoom);
                        b.Saved++;
                    }
                    catch (Exception error)
                    {
                        b.Failed.Add(who + ": " + error.Message);
                    }
                }
                b.Done++;
                if (b.Done < b.Paths.Length)
                {
                    Invalidate();
                    return;
                }
            }
            EndImageBatch(true, true);
        }

        /// <summary>
        /// Stops the batch (images already saved are kept), returns to the student and view from before it and,
        /// when <paramref name="report"/>, tells how many images were saved and offers to open the folder.
        /// </summary>
        private void EndImageBatch(bool completed, bool report)
        {
            ImageBatch b = _batch;
            if (b == null) return;
            _batch = null;
            _buf = new int[0];

            _index = b.StartIndex;
            _yaw = b.ViewYaw;
            _pitch = b.ViewPitch;
            _autoSpin = b.Spin;
            _lastTick = Now;
            _hover = null;
            if (!report) return;   // the window is closing

            RenderFront();
            Invalidate();

            var text = new System.Text.StringBuilder();
            if (!completed) text.Append("Export cancelled.\n\n");
            text.Append(string.Format("Saved {0} of {1} images in:", b.Saved, b.Paths.Length)).Append('\n').Append(b.Folder);
            if (b.Failed.Count > 0)
            {
                text.Append("\n\n").Append("Not exported:");
                const int shown = 8;
                for (int i = 0; i < b.Failed.Count && i < shown; i++) text.Append('\n').Append(b.Failed[i]);
                if (b.Failed.Count > shown) text.Append('\n').Append(string.Format("… and {0} more", b.Failed.Count - shown));
            }

            if (b.Saved == 0)
            {
                AppDialog.Show(this, text.ToString(), Text, MessageBoxButtons.OK,
                    completed ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                return;
            }
            text.Append("\n\n").Append("Open the folder now?");
            if (AppDialog.Show(this, text.ToString(), Text, MessageBoxButtons.YesNo,
                    b.Failed.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { Process.Start(b.Folder); }
                catch (Exception error) { AppDialog.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        /// <summary>The current view at export size, on the dark backdrop or (cropped) on a transparent background.</summary>
        private Bitmap RenderImage(bool transparent)
        {
            Rectangle screen = StageRect();
            if (screen.Width < 40 || screen.Height < 40) screen = new Rectangle(0, 0, 1600, 900);

            // scale the screen stage so that at 100 % zoom the card is as many pixels wide as its own image
            int cardPx = Math.Max(_front != null ? _front.Width : 0, _back != null ? _back.Width : 0);
            if (cardPx <= 0) cardPx = 2000;
            double fit = Math.Min(screen.Width * 0.62 / _cardW, screen.Height * 0.64 / _cardH);   // as in RenderFrame
            double k = Math.Max(1, cardPx / (_cardW * fit));
            k = Math.Min(k, (double)ImageMaxSide / Math.Max(screen.Width, screen.Height));

            var stage = new Rectangle(0, 0, (int)Math.Round(screen.Width * k), (int)Math.Round(screen.Height * k));
            RenderFrame(stage, double.MaxValue);

            Bitmap image = new Bitmap(stage.Width, stage.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(image))
                {
                    if (transparent)
                    {
                        g.Clear(Color.Transparent);
                    }
                    else
                    {
                        using (Bitmap backdrop = CreateBackdrop(stage.Size, stage))
                            g.DrawImageUnscaled(backdrop, 0, 0);
                    }
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.DrawImageUnscaled(_frame, 0, 0);
                }
                if (transparent) image = CropToContent(image, ImageMargin);

                // pixels per inch of the card at 100 % zoom, so the picture is about card-sized when placed in a document
                float dpi = (float)(fit * k * 25.4);
                image.SetResolution(dpi, dpi);
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        /// <summary>Cuts a transparent image down to its visible pixels plus <paramref name="margin"/>; disposes the original when it does.</summary>
        private static Bitmap CropToContent(Bitmap image, int margin)
        {
            int w = image.Width, h = image.Height;
            int minX = w, minY = h, maxX = -1, maxY = -1;
            var all = new Rectangle(0, 0, w, h);
            var row = new int[w];
            BitmapData data = image.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, w);
                    for (int x = 0; x < w; x++)
                    {
                        if (((row[x] >> 24) & 255) <= 2) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        maxY = y;
                    }
                }
            }
            finally
            {
                image.UnlockBits(data);
            }
            if (maxX < 0) return image;   // nothing drawn

            Rectangle crop = Rectangle.FromLTRB(minX - margin, minY - margin, maxX + 1 + margin, maxY + 1 + margin);
            crop.Intersect(all);
            if (crop == all) return image;
            Bitmap cropped = image.Clone(crop, PixelFormat.Format32bppArgb);
            image.Dispose();
            return cropped;
        }

        private static void SaveImage(Bitmap image, string path, bool jpeg)
        {
            if (!jpeg)
            {
                image.Save(path, ImageFormat.Png);
                return;
            }
            ImageCodecInfo codec = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.FormatID == ImageFormat.Jpeg.Guid);
            if (codec == null)
            {
                image.Save(path, ImageFormat.Jpeg);
                return;
            }
            using (var parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                image.Save(path, codec, parameters);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            EndExport(false);   // closing in the middle of a recording drops the unfinished file
            EndImageBatch(false, false);   // images already saved stay
            base.OnFormClosing(e);
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
            if (_export != null)
            {
                DrawExportPreview(g, stage);
            }
            else if (stage.Width > 40 && stage.Height > 40)
            {
                try
                {
                    double scale = RenderFrame(stage);
                    DrawStageFrame(g, stage, scale);
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

        /// <summary>The rendered stage (<see cref="RenderFrame"/>) drawn into <paramref name="stage"/>.</summary>
        private void DrawStageFrame(Graphics g, Rectangle stage, double scale)
        {
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

        /// <summary>Dark backdrop with a soft spotlight behind the card, cached until the window size changes.</summary>
        private void DrawBackdrop(Graphics g)
        {
            Size size = ClientSize;
            if (size.Width <= 0 || size.Height <= 0) return;
            if (_backdrop == null) _backdrop = CreateBackdrop(size, StageRect());
            g.DrawImageUnscaled(_backdrop, 0, 0);
        }

        private static Bitmap CreateBackdrop(Size size, Rectangle stage)
        {
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics bg = Graphics.FromImage(bmp))
            {
                bg.Clear(Backdrop);
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
            return bmp;
        }

        /// <summary>
        /// Renders shadow, rim and the visible face into <see cref="_frame"/>; returns the frame's scale to the stage.
        /// The card lies in its own x/y plane (mm, centred, y up); R = Ry(yaw)·Rx(pitch) turns it into the world,
        /// where the camera sits at (0, 0, D) looking down −z.
        /// <paramref name="maxCardPixels"/> caps the card's width in the frame (the frame is then scaled up to the stage).
        /// </summary>
        private double RenderFrame(Rectangle stage, double maxCardPixels = MaxRenderWidth)
        {
            double cw = _cardW, ch = _cardH;
            double fit = Math.Min(stage.Width * 0.62 / cw, stage.Height * 0.64 / ch);
            double screenScale = fit * _zoom;
            double renderScale = Math.Min(1.0, maxCardPixels / (cw * screenScale));
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

            string help;
            if (_export != null)
            {
                help = Lang.F("Recording {0} × {1}, {2} s    |    Esc: cancel", VideoWidth, VideoHeight, VideoSeconds);
            }
            else if (_batch != null)
            {
                string folder = Path.GetFileName(_batch.Folder.TrimEnd(Path.DirectorySeparatorChar));
                help = Lang.F("Saving one image per student to {0}    |    Esc: cancel",
                              string.IsNullOrEmpty(folder) ? _batch.Folder : folder);
            }
            else
            {
                string side = Lang.T(_frontFacing ? "Front" : "Back");
                string zoom = Math.Abs(_zoom - 1) > 0.01 ? "  ·  " + Math.Round(_zoom * 100) + " %" : "";
                help = side + zoom +
                       Lang.T("    |    Drag: turn    Wheel: zoom    Space: flip    ← →: student    Esc: close");
            }

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
                EndExport(false);
                _batch = null;
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
