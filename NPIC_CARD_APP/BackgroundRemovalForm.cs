using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Crops a student photo and removes its plain backdrop: a crop frame on the original (locked to the card's
    /// photo shape by default), a live result, a tolerance slider and the colour to put behind the subject. Save writes {StudentId}.png and keeps the untouched photo in an
    /// "_original" sub-folder: editing again always starts from it, and Restore Original puts it back.
    /// </summary>
    public class BackgroundRemovalForm : ModalForm
    {
        private const int PreviewSide = 640;            // live preview works on a downscaled copy (fast)
        private const string BackupFolderName = "_original";

        private static readonly Color IdBlue = Color.FromArgb(67, 142, 219);
        private const float CardShape = CardRenderer.PhotoWidthMm / CardRenderer.PhotoHeightMm;

        private readonly string _photoFolder;
        private readonly string _sourcePath;             // the untouched original (backup when one exists)
        private readonly string _studentId;
        private readonly Bitmap _full;                   // original, full resolution
        private readonly Bitmap _small;                  // downscaled copy for the live preview
        private readonly PhotoCropView _cropView;
        private readonly PictureBox _picAfter;
        private readonly CheckBox _chkCardShape;
        private readonly TrackBar _trkTolerance;
        private readonly Label _lblTolerance;
        private readonly RadioButton _rdoWhite, _rdoBlue, _rdoTransparent, _rdoKeep;
        private readonly Timer _debounce;
        private readonly TextureBrush _checker;
        private readonly ToolTip _tip = new ToolTip();

        /// <summary>Path of the saved photo, or null when nothing was saved.</summary>
        public string SavedPath { get; private set; }

        /// <summary>True when the dialog put the original photo back.</summary>
        public bool Restored { get; private set; }

        public BackgroundRemovalForm(string photoFolder, string studentId)
        {
            _photoFolder = photoFolder;
            _studentId = studentId;
            _sourcePath = FindOriginal(photoFolder, studentId) ?? CardRenderer.FindPhoto(photoFolder, studentId);
            if (_sourcePath == null) throw new FileNotFoundException("No photo found for " + studentId + " in " + photoFolder);
            _full = BackgroundRemover.Load(_sourcePath);
            _small = BackgroundRemover.Downscale(_full, PreviewSide);
            _checker = new TextureBrush(BackgroundRemover.Checkerboard());

            Text = "Edit Photo";
            Subtitle = "Crop and clean the background of student " + studentId;
            Glyph = Theme.IcoPhoto;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(860, 600);
            MinimumSize = new Size(640, 460);
            Font = new Font(CardFonts.UI, 9f);
            BackColor = Theme.Surface;
            ShowInTaskbar = false;
            MinimizeBox = false;
            KeyPreview = true;

            _debounce = new Timer { Interval = 120 };
            _debounce.Tick += (s, e) => { _debounce.Stop(); UpdateResult(); };

            // ---- Crop frame / result ----
            _cropView = new PhotoCropView { Dock = DockStyle.Fill, Margin = new Padding(4), Image = _small };
            _cropView.CropChanged += (s, e) => { _debounce.Stop(); _debounce.Start(); };
            _picAfter = MakePreview();

            var previews = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12, 12, 12, 0) };
            previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            previews.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            previews.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            previews.Controls.Add(Caption("Original — drag to crop"), 0, 0);
            previews.Controls.Add(Caption("Result"), 1, 0);
            previews.Controls.Add(_cropView, 0, 1);
            previews.Controls.Add(_picAfter, 1, 1);

            // ---- Options ----
            _trkTolerance = new TrackBar
            {
                Minimum = 5, Maximum = 160, TickFrequency = 15, SmallChange = 2, LargeChange = 10, Value = 48,
                Width = 260, Margin = new Padding(0, 0, 6, 0)
            };
            _lblTolerance = new Label { AutoSize = true, Margin = new Padding(0, 8, 24, 0), ForeColor = Theme.TextSoft };
            _trkTolerance.ValueChanged += (s, e) => { UpdateToleranceLabel(); _debounce.Stop(); _debounce.Start(); };

            _rdoWhite = MakeChoice("White", true);
            _rdoBlue = MakeChoice("Blue", false);
            _rdoTransparent = MakeChoice("Transparent", false);
            _rdoKeep = MakeChoice("Keep", false);
            _tip.SetTip(_rdoKeep, "Crop only: leave the background as it is");

            _chkCardShape = new CheckBox { Text = "Card photo shape", AutoSize = true, Checked = true, Margin = new Padding(0, 7, 10, 0) };
            _tip.SetTip(_chkCardShape, string.Format("Keep the frame at {0} x {1} mm, the size of the photo box on the card",
                CardRenderer.PhotoWidthMm, CardRenderer.PhotoHeightMm));
            _chkCardShape.CheckedChanged += (s, e) => _cropView.AspectRatio = _chkCardShape.Checked ? CardShape : 0f;
            PillButton btnResetCrop = ModalUi.ToolButton("Reset Crop", null);
            btnResetCrop.MinimumSize = new Size(0, 30);
            btnResetCrop.Margin = new Padding(0, 1, 24, 0);
            btnResetCrop.Click += (s, e) => _cropView.ResetCrop();

            var options = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(12, 10, 12, 0) };
            options.Controls.Add(new Label { Text = "Crop", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            options.Controls.Add(_chkCardShape);
            options.Controls.Add(btnResetCrop);
            options.Controls.Add(new Label { Text = "Tolerance", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
            options.Controls.Add(_trkTolerance);
            options.Controls.Add(_lblTolerance);
            options.Controls.Add(new Label { Text = "New background", AutoSize = true, Margin = new Padding(0, 8, 8, 0) });
            options.Controls.Add(_rdoWhite);
            options.Controls.Add(_rdoBlue);
            options.Controls.Add(_rdoTransparent);
            options.Controls.Add(_rdoKeep);

            var hint = new Label
            {
                Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(14, 4, 14, 0), ForeColor = Theme.TextSoft,
                Text = "Drag inside the frame to move it, drag a corner to resize, drag outside to draw a new one (arrow keys nudge). " +
                       "Raise the tolerance if background is left over; lower it if parts of the face, hair or clothes disappear."
            };

            // ---- Buttons ----
            Button btnSave = ModalUi.Button("Save Photo", PillKind.Primary);
            Button btnCancel = ModalUi.Button("Cancel", PillKind.Secondary);
            btnCancel.DialogResult = DialogResult.Cancel;
            btnSave.Click += (s, e) => Save();
            Button btnRestore = ModalUi.Button("Restore Original", PillKind.Ghost);
            btnRestore.Visible = FindOriginal(photoFolder, studentId) != null;
            btnRestore.Click += (s, e) => Restore();
            var buttons = ModalUi.Footer();
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnSave);
            buttons.Controls.Add(btnRestore);
            btnRestore.Margin = new Padding(0, 0, 24, 0);

            Controls.Add(previews);
            Controls.Add(hint);
            Controls.Add(options);
            Controls.Add(buttons);
            AcceptButton = btnSave;
            CancelButton = btnCancel;

            _cropView.AspectRatio = CardShape;
            _cropView.ResetCrop();
            UpdateToleranceLabel();
            UpdateResult();
        }

        // ---------- UI helpers ----------
        private PictureBox MakePreview()
        {
            return new PictureBox
            {
                Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(4),
                BackColor = Theme.Tile
            };
        }

        private static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font(CardFonts.UI, 9f, FontStyle.Bold), Margin = new Padding(4, 0, 0, 4) };
        }

        private RadioButton MakeChoice(string text, bool isChecked)
        {
            var r = new RadioButton { Text = text, AutoSize = true, Checked = isChecked, Margin = new Padding(0, 7, 10, 0) };
            r.CheckedChanged += (s, e) => { if (r.Checked) UpdateResult(); };
            return r;
        }

        private void UpdateToleranceLabel()
        {
            _lblTolerance.Text = _trkTolerance.Value.ToString();
        }

        private Color FillColor()
        {
            if (_rdoBlue.Checked) return IdBlue;
            if (_rdoTransparent.Checked) return Color.Transparent;
            return Color.White;
        }

        /// <summary>Crops <paramref name="src"/> to the frame (scaled from preview to <paramref name="src"/> pixels), then removes the background.</summary>
        private Bitmap Process(Bitmap src)
        {
            RectangleF c = _cropView.Crop;
            float sx = (float)src.Width / _small.Width, sy = (float)src.Height / _small.Height;
            Rectangle area = Rectangle.Round(new RectangleF(c.X * sx, c.Y * sy, c.Width * sx, c.Height * sy));
            area.Intersect(new Rectangle(0, 0, src.Width, src.Height));
            if (area.Width < 1 || area.Height < 1) area = new Rectangle(0, 0, src.Width, src.Height);

            var cropped = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(cropped))
                g.DrawImage(src, new Rectangle(0, 0, area.Width, area.Height), area, GraphicsUnit.Pixel);

            if (_rdoKeep.Checked) return cropped;
            using (cropped)
                return BackgroundRemover.Remove(cropped, _trkTolerance.Value, FillColor());
        }

        /// <summary>Recomputes the preview on the small copy. Transparent results are shown on a checkerboard.</summary>
        private void UpdateResult()
        {
            Bitmap result;
            Cursor = Cursors.WaitCursor;
            try
            {
                using (Bitmap removed = Process(_small))
                {
                    result = new Bitmap(removed.Width, removed.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(result))
                    {
                        g.FillRectangle(_checker, 0, 0, result.Width, result.Height);
                        g.DrawImage(removed, 0, 0, removed.Width, removed.Height);
                    }
                }
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                AppDialog.Show(this, "Could not process the photo:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Cursor = Cursors.Default;

            Image old = _picAfter.Image;
            _picAfter.Image = result;
            if (old != null) old.Dispose();
        }

        // ---------- Save ----------
        /// <summary>
        /// Backs up the original into "_original\" (first time only, so re-running never loses it) and writes
        /// the cropped {StudentId}.png at full resolution. PNG keeps transparency and is lossless for the card print.
        /// </summary>
        private void Save()
        {
            string backupDir = Path.Combine(_photoFolder, BackupFolderName);
            string target = Path.Combine(_photoFolder, _studentId + ".png");
            string temp = target + ".tmp";

            Cursor = Cursors.WaitCursor;
            try
            {
                // Write to a temp file first, so a failed save never leaves a half-written photo.
                using (Bitmap result = Process(_full))
                    result.Save(temp, ImageFormat.Png);

                if (FindOriginal(_photoFolder, _studentId) == null)
                {
                    Directory.CreateDirectory(backupDir);
                    File.Copy(_sourcePath, Path.Combine(backupDir, Path.GetFileName(_sourcePath)));
                }

                // FindPhoto prefers .jpg/.jpeg over .png, so no other copy of this photo may stay next to the new one.
                DeleteCurrentPhotos(_photoFolder, _studentId);
                File.Move(temp, target);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
                AppDialog.Show(this, "Could not save the photo:\n\n" + ex.Message +
                    "\n\nIf the photo is open in another program, close it and try again.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Cursor = Cursors.Default;

            SavedPath = target;
            DialogResult = DialogResult.OK;
        }

        private void Restore()
        {
            try
            {
                SavedPath = RestoreOriginal(_photoFolder, _studentId);
                Restored = SavedPath != null;
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not restore the original photo:\n\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }

        private static void DeleteCurrentPhotos(string folder, string studentId)
        {
            foreach (string ext in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
            {
                string path = Path.Combine(folder, studentId + ext);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>The backed-up original of a student's photo, or null when there is none.</summary>
        public static string FindOriginal(string photoFolder, string studentId)
        {
            return CardRenderer.FindPhoto(Path.Combine(photoFolder ?? "", BackupFolderName), studentId);
        }

        /// <summary>Puts the backed-up original back in place of the edited photo.</summary>
        public static string RestoreOriginal(string photoFolder, string studentId)
        {
            string original = FindOriginal(photoFolder, studentId);
            if (original == null) return null;

            DeleteCurrentPhotos(photoFolder, studentId);
            string target = Path.Combine(photoFolder, Path.GetFileName(original));
            File.Copy(original, target);
            File.Delete(original);
            return target;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _debounce.Dispose();
                _tip.Dispose();
                if (_picAfter.Image != null) _picAfter.Image.Dispose();
                _checker.Dispose();
                _small.Dispose();
                _full.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
