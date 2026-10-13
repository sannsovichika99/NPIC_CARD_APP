using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Shows an image with a crop frame on top. Drag inside the frame to move it, drag a corner to resize it,
    /// drag outside it to draw a new one. The frame can be locked to an aspect ratio (width / height).
    /// <see cref="Crop"/> is in pixels of <see cref="Image"/>.
    /// </summary>
    public class PhotoCropView : Control
    {
        private const int HandleSize = 10;   // corner squares, in screen px
        private const float MinSide = 16f;   // smallest frame, in image px

        private enum Drag { None, Move, Corner, New }

        private Image _image;
        private RectangleF _crop;
        private float _aspect;

        private Drag _drag;
        private PointF _anchor;              // fixed corner while resizing / drawing (image px)
        private PointF _grab;                // mouse position inside the frame when a move started (image px)

        public event EventHandler CropChanged;

        public PhotoCropView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(232, 234, 240);
        }

        /// <summary>The image to crop (not owned: the caller disposes it). Setting it selects the whole image.</summary>
        public Image Image
        {
            get { return _image; }
            set
            {
                _image = value;
                _crop = value == null ? RectangleF.Empty : new RectangleF(0, 0, value.Width, value.Height);
                Invalidate();
            }
        }

        /// <summary>Crop frame in image pixels.</summary>
        public RectangleF Crop
        {
            get { return _crop; }
            set { _crop = Clamp(value); Invalidate(); OnCropChanged(); }
        }

        /// <summary>Width / height the frame is locked to; 0 = free.</summary>
        public float AspectRatio
        {
            get { return _aspect; }
            set
            {
                _aspect = Math.Max(0f, value);
                if (_aspect > 0 && _image != null) Crop = FitAspect(_crop);
            }
        }

        /// <summary>
        /// Largest frame of the locked shape, centred left/right and placed a third of the way down
        /// (the same crop the card uses for photos of another shape, so faces stay in view).
        /// </summary>
        public void ResetCrop()
        {
            if (_image == null) return;
            float w = _image.Width, h = _image.Height;
            if (_aspect <= 0) { Crop = new RectangleF(0, 0, w, h); return; }
            if (w / h > _aspect)
            {
                float cw = h * _aspect;
                Crop = new RectangleF((w - cw) / 2f, 0, cw, h);
            }
            else
            {
                float ch = w / _aspect;
                Crop = new RectangleF(0, (h - ch) / 3f, w, ch);
            }
        }

        // ---------- Geometry ----------
        /// <summary>Where the image is drawn (fit inside the control, centred).</summary>
        private RectangleF ImageRect(out float scale)
        {
            scale = 0;
            if (_image == null) return RectangleF.Empty;
            const float pad = 8f;
            float cw = Math.Max(1, ClientSize.Width - 2 * pad), ch = Math.Max(1, ClientSize.Height - 2 * pad);
            scale = Math.Min(cw / _image.Width, ch / _image.Height);
            float w = _image.Width * scale, h = _image.Height * scale;
            return new RectangleF((ClientSize.Width - w) / 2f, (ClientSize.Height - h) / 2f, w, h);
        }

        private RectangleF ToScreen(RectangleF r)
        {
            float s;
            RectangleF img = ImageRect(out s);
            return new RectangleF(img.X + r.X * s, img.Y + r.Y * s, r.Width * s, r.Height * s);
        }

        private PointF ToImage(Point p)
        {
            float s;
            RectangleF img = ImageRect(out s);
            if (s <= 0) return PointF.Empty;
            float x = (p.X - img.X) / s, y = (p.Y - img.Y) / s;
            return new PointF(Math.Max(0, Math.Min(_image.Width, x)), Math.Max(0, Math.Min(_image.Height, y)));
        }

        private RectangleF Clamp(RectangleF r)
        {
            if (_image == null) return RectangleF.Empty;
            float w = Math.Min(Math.Max(r.Width, 1), _image.Width), h = Math.Min(Math.Max(r.Height, 1), _image.Height);
            float x = Math.Max(0, Math.Min(_image.Width - w, r.X)), y = Math.Max(0, Math.Min(_image.Height - h, r.Y));
            return new RectangleF(x, y, w, h);
        }

        /// <summary>Shrinks a frame around its centre to the locked shape.</summary>
        private RectangleF FitAspect(RectangleF r)
        {
            float w = r.Width, h = r.Height;
            if (w / h > _aspect) w = h * _aspect; else h = w / _aspect;
            return new RectangleF(r.X + (r.Width - w) / 2f, r.Y + (r.Height - h) / 2f, w, h);
        }

        /// <summary>Frame spanned by the fixed corner and the mouse, kept to the locked shape.</summary>
        private RectangleF FromCorners(PointF a, PointF m)
        {
            float w = Math.Abs(m.X - a.X), h = Math.Abs(m.Y - a.Y);
            if (_aspect > 0)
            {
                if (w / Math.Max(h, 0.001f) > _aspect) w = h * _aspect; else h = w / _aspect;
            }
            float x = m.X < a.X ? a.X - w : a.X;
            float y = m.Y < a.Y ? a.Y - h : a.Y;
            return new RectangleF(x, y, w, h);
        }

        private static PointF[] Corners(RectangleF r)
        {
            return new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom) };
        }

        /// <summary>Index of the corner handle under the mouse, or -1.</summary>
        private int HitCorner(Point p)
        {
            PointF[] c = Corners(ToScreen(_crop));
            for (int i = 0; i < 4; i++)
                if (Math.Abs(p.X - c[i].X) <= HandleSize && Math.Abs(p.Y - c[i].Y) <= HandleSize) return i;
            return -1;
        }

        // ---------- Mouse ----------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_image == null || e.Button != MouseButtons.Left) return;
            Focus();

            int corner = HitCorner(e.Location);
            PointF p = ToImage(e.Location);
            if (corner >= 0)
            {
                _drag = Drag.Corner;
                _anchor = Corners(_crop)[(corner + 2) % 4];   // the opposite corner stays put
            }
            else if (ToScreen(_crop).Contains(e.Location))
            {
                _drag = Drag.Move;
                _grab = new PointF(p.X - _crop.X, p.Y - _crop.Y);
            }
            else
            {
                _drag = Drag.New;
                _anchor = p;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_image == null) return;

            if (_drag == Drag.None)
            {
                int corner = HitCorner(e.Location);
                Cursor = corner == 0 || corner == 2 ? Cursors.SizeNWSE
                       : corner == 1 || corner == 3 ? Cursors.SizeNESW
                       : ToScreen(_crop).Contains(e.Location) ? Cursors.SizeAll
                       : Cursors.Cross;
                return;
            }

            PointF p = ToImage(e.Location);
            if (_drag == Drag.Move)
            {
                _crop = Clamp(new RectangleF(p.X - _grab.X, p.Y - _grab.Y, _crop.Width, _crop.Height));
            }
            else
            {
                RectangleF r = FromCorners(_anchor, p);
                if (r.Width >= MinSide && r.Height >= MinSide) _crop = r;
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag == Drag.None) return;
            _drag = Drag.None;
            OnCropChanged();
        }

        /// <summary>Arrow keys nudge the frame by 1 px (Shift = 10 px).</summary>
        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_image == null) return;
            float step = e.Shift ? 10f : 1f;
            float dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0;
            float dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
            if (dx == 0 && dy == 0) return;
            Crop = new RectangleF(_crop.X + dx, _crop.Y + dy, _crop.Width, _crop.Height);
            e.Handled = true;
        }

        protected virtual void OnCropChanged()
        {
            EventHandler h = CropChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        // ---------- Paint ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_image == null) return;

            Graphics g = e.Graphics;
            float s;
            RectangleF img = ImageRect(out s);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(_image, img);

            RectangleF c = ToScreen(_crop);

            // Dim everything outside the frame.
            using (var outside = new Region(img))
            using (var dim = new SolidBrush(Color.FromArgb(140, 20, 22, 30)))
            {
                outside.Exclude(c);
                g.FillRegion(dim, outside);
            }

            // Rule-of-thirds guides help line up the eyes.
            using (var guide = new Pen(Color.FromArgb(110, 255, 255, 255)))
            {
                for (int i = 1; i <= 2; i++)
                {
                    g.DrawLine(guide, c.X + c.Width * i / 3f, c.Y, c.X + c.Width * i / 3f, c.Bottom);
                    g.DrawLine(guide, c.X, c.Y + c.Height * i / 3f, c.Right, c.Y + c.Height * i / 3f);
                }
            }
            using (var frame = new Pen(Color.White, 1.5f))
                g.DrawRectangle(frame, c.X, c.Y, c.Width, c.Height);

            using (var fill = new SolidBrush(Color.White))
            using (var edge = new Pen(Color.FromArgb(46, 49, 146)))
            {
                foreach (PointF p in Corners(c))
                {
                    var h = new RectangleF(p.X - HandleSize / 2f, p.Y - HandleSize / 2f, HandleSize, HandleSize);
                    g.FillRectangle(fill, h);
                    g.DrawRectangle(edge, h.X, h.Y, h.Width, h.Height);
                }
            }
        }
    }
}
