using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Gallery of the loaded students: photo thumbnail + main details per tile, and a details panel with the
    /// large photo and every field of the selected student. Shows at a glance whose photo is missing and
    /// whose card is already printed (from <see cref="PrintHistory"/>).
    /// Thumbnails load in the background, so long lists open immediately.
    /// Several students can be chosen (Ctrl / Shift + click, Ctrl+A = every tile shown); "Select in List" hands them all to
    /// the main window, whose student list then shows just those students, selected and ready to print.
    ///   Enter / double-click = select in the main list, F5 = reload photos, Ctrl+F = search,
    ///   Ctrl+A = choose every tile shown, Ctrl+B = remove background, Esc = close
    /// </summary>
    public class StudentGalleryForm : ModalForm
    {
        private static readonly Color Accent     = Theme.Accent;
        private static readonly Color AccentSoft = Theme.AccentSoft;
        private static readonly Color AppBack    = Theme.AppBack;
        private static readonly Color Surface    = Theme.Surface;
        private static readonly Color Border     = Theme.Border;
        private static readonly Color TextMain   = Theme.TextMain;
        private static readonly Color TextMuted  = Theme.TextSoft;
        private static readonly Color Placeholder = Theme.Tile;
        private static readonly Color Warning    = Theme.Warning;
        private static readonly Color Success    = Theme.Success;
        private static readonly Color Danger     = Theme.Danger;

        // Thumbnail in the card's photo ratio (18.7 x 22 mm).
        private const int ThumbW = 84, ThumbH = 99;
        private static readonly Size TileSize = new Size(310, 132);

        private static readonly Font IdFont    = new Font("Segoe UI", 10f, FontStyle.Bold);
        private static readonly Font LatinFont = new Font("Segoe UI", 9f);
        private static readonly Font SmallFont = Theme.UiFont(8f);
        private static readonly Font BadgeFont = Theme.UiFont(7.5f, false, FontStyle.Bold);
        private static readonly Font KhmerFont = new Font(CardFonts.KhmerSiemreap, 9f);
        private static readonly Font KhmerSmallFont = new Font(CardFonts.KhmerSiemreap, 7.5f);   // faculty / level are Khmer

        private enum Filter { All, MissingPhoto, NotPrinted, PartlyPrinted, Printed }

        private sealed class PrintCount
        {
            public int Front, Back;
            public DateTime Last;
        }

        private readonly List<Student> _students;
        private readonly string _photoFolder;
        private Dictionary<string, PrintCount> _printed = new Dictionary<string, PrintCount>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _photoPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Thumbnails by StudentId. Written by the loader thread, drawn and disposed on the UI thread; guarded by _thumbLock.
        private readonly Dictionary<string, Bitmap> _thumbs = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly object _thumbLock = new object();
        private bool _closed;
        private Student _detailStudent;   // student the details panel shows now
        private CancellationTokenSource _loaderCts;
        private volatile bool _thumbsDirty;
        private readonly System.Windows.Forms.Timer _repaintTimer;

        private readonly TextBox _txtSearch;
        private readonly ComboBox _cboFilter;
        private readonly GalleryList _list;
        private readonly Label _lblEmpty, _lblSummary;
        private readonly PictureBox _picPhoto;
        private readonly TableLayoutPanel _fields;
        private readonly Label _lblDetailTitle;
        private readonly Button _btnSelect, _btnOpenPhoto, _btnShowFile, _btnRemoveBg;

        /// <summary>StudentId the user chose with Enter / double-click / "Select in List" (DialogResult = OK).</summary>
        public string SelectedStudentId { get; private set; }

        /// <summary>
        /// Every StudentId chosen when the gallery closed with OK, in tile order (the one the details panel showed is
        /// <see cref="SelectedStudentId"/>). One entry when a single student was chosen.
        /// </summary>
        public IReadOnlyList<string> SelectedStudentIds { get; private set; } = new string[0];

        /// <param name="startStudentId">Student shown in the details panel when the gallery opens.</param>
        /// <param name="startSelectedIds">Students chosen when it opens (e.g. the rows selected in the main list); may be null.</param>
        public StudentGalleryForm(List<Student> students, AppSettings settings, string startStudentId,
            IEnumerable<string> startSelectedIds = null)
        {
            _students = students ?? new List<Student>();
            _photoFolder = settings.PhotoFolder ?? "";

            Text = "Student Gallery";
            Subtitle = "Photos and print status of every student";
            Glyph = Theme.IcoPeople;
            StartPosition = FormStartPosition.CenterParent;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1180, work.Width), Math.Min(760, work.Height));
            MinimumSize = new Size(Math.Min(820, work.Width), Math.Min(480, work.Height));
            Font = new Font(CardFonts.UI, 9f);
            BackColor = AppBack;
            KeyPreview = true;
            ShowInTaskbar = false;

            // ---- Top bar: search + filter ----
            _txtSearch = new TextBox { Width = 260, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 5, 14, 0) };
            _txtSearch.HandleCreated += (s, e) => SetCueBanner(_txtSearch, "Search ID, name or faculty…  (Ctrl+F)");
            _txtSearch.TextChanged += (s, e) => ShowStudents(CurrentId(), ChosenIds());
            _txtSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && _list.Items.Count > 0) { _list.Focus(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { ChooseCurrent(); e.SuppressKeyPress = true; }
            };

            _cboFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(3, 4, 14, 0) };
            _cboFilter.Items.AddRange(new object[] { "All students", "Missing photo", "Not printed yet", "Partly printed", "Fully printed" });
            _cboFilter.SelectedIndex = 0;
            _cboFilter.SelectedIndexChanged += (s, e) => ShowStudents(CurrentId(), ChosenIds());

            var top = ModalUi.Toolbar();
            top.Controls.Add(_txtSearch);
            top.Controls.Add(new Label { Text = "Show", AutoSize = true, ForeColor = TextMuted, Margin = new Padding(0, 8, 0, 0) });
            top.Controls.Add(_cboFilter);
            top.Controls.Add(MakeButton("Reload Photos (F5)", (s, e) => Reload()));

            // ---- Tiles ----
            _list = new GalleryList
            {
                Dock = DockStyle.Fill, View = View.Tile, OwnerDraw = true, TileSize = TileSize, MultiSelect = true,
                HideSelection = false, BorderStyle = BorderStyle.None, BackColor = AppBack
            };
            _list.DrawItem += DrawTile;
            _list.SelectedIndexChanged += (s, e) => { ShowDetails(); UpdateChoice(); };
            _list.ItemActivate += (s, e) => ChooseCurrent();   // double-click or Enter

            _lblEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted, BackColor = AppBack, Visible = false
            };
            var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 4, 8), BackColor = AppBack };
            listHost.Controls.Add(_list);
            listHost.Controls.Add(_lblEmpty);
            _lblEmpty.BringToFront();

            // ---- Details panel ----
            _lblDetailTitle = new Label
            {
                Dock = DockStyle.Top, Height = 36, Font = new Font(CardFonts.UI, 10f, FontStyle.Bold), ForeColor = TextMain,
                Padding = new Padding(0, 8, 0, 0), AutoEllipsis = true
            };
            _picPhoto = new PictureBox
            {
                Dock = DockStyle.Top, Height = 230, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Placeholder,
                Cursor = Cursors.Hand
            };
            _picPhoto.DoubleClick += (s, e) => OpenPhoto();
            _picPhoto.Paint += (s, e) =>
            {
                if (_picPhoto.Image != null) return;
                TextRenderer.DrawText(e.Graphics, _list.SelectedItems.Count == 0 ? "" : Lang.T("No photo"), Lang.Khmer ? KhmerFont : LatinFont,
                    _picPhoto.ClientRectangle, TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            _fields = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 10, 0, 0), BackColor = Surface
            };
            _fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _btnSelect = ModalUi.Button("Select in List", PillKind.Primary, (s, e) => ChooseCurrent());
            _btnSelect.MinimumSize = new Size(0, 32);
            _btnSelect.Margin = new Padding(0, 0, 6, 6);
            _btnOpenPhoto = MakeButton("Open Photo", (s, e) => OpenPhoto());
            _btnShowFile = MakeButton("Show in Folder", (s, e) => ShowPhotoInFolder());
            _btnRemoveBg = MakeButton("Remove Background… (Ctrl+B)", (s, e) => RemoveBackground());
            var detailButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, BackColor = Surface, Padding = new Padding(0, 6, 0, 0)
            };
            detailButtons.Controls.Add(_btnSelect);
            detailButtons.Controls.Add(_btnOpenPhoto);
            detailButtons.Controls.Add(_btnShowFile);
            detailButtons.Controls.Add(_btnRemoveBg);

            var fieldsScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Surface };
            fieldsScroll.Controls.Add(_fields);

            var details = new Panel { Dock = DockStyle.Right, Width = 330, BackColor = Surface, Padding = new Padding(16, 4, 16, 12) };
            details.Paint += (s, e) => { using (var pen = new Pen(Border)) e.Graphics.DrawLine(pen, 0, 0, 0, details.Height); };
            details.Controls.Add(fieldsScroll);
            details.Controls.Add(detailButtons);
            details.Controls.Add(_picPhoto);
            details.Controls.Add(_lblDetailTitle);

            _lblSummary = new Label
            {
                Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(12, 0, 12, 0), TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            ModalUi.StyleStatus(_lblSummary);

            Controls.Add(listHost);
            Controls.Add(details);
            Controls.Add(top);
            Controls.Add(_lblSummary);

            _repaintTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _repaintTimer.Tick += (s, e) =>
            {
                if (!_thumbsDirty) return;
                _thumbsDirty = false;
                _list.Invalidate();
            };

            ReadPhotosAndHistory();
            ShowStudents(startStudentId, startSelectedIds == null ? null : new HashSet<string>(startSelectedIds, StringComparer.OrdinalIgnoreCase));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _repaintTimer.Start();
            StartThumbnailLoader();
            if (_list.SelectedItems.Count > 0)
            {
                _list.SelectedItems[0].EnsureVisible();
                _list.Focus();
            }
            else _txtSearch.Focus();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _repaintTimer.Stop();
            _repaintTimer.Dispose();
            if (_loaderCts != null) _loaderCts.Cancel();
            lock (_thumbLock)
            {
                _closed = true;
                foreach (Bitmap b in _thumbs.Values) b.Dispose();
                _thumbs.Clear();
            }
            ReplacePhoto(null);
            base.OnFormClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                // First Esc clears the search, the next one closes the window.
                if (_txtSearch.TextLength > 0) _txtSearch.Clear();
                else Close();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.F5) { Reload(); e.Handled = true; }
            else if (e.KeyData == (Keys.Control | Keys.F)) { _txtSearch.Focus(); _txtSearch.SelectAll(); e.Handled = true; }
            else if (e.KeyData == (Keys.Control | Keys.B)) { RemoveBackground(); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyData == (Keys.Control | Keys.A) && !_txtSearch.Focused)   // in the search box Ctrl+A selects its text
            {
                ChooseAllShown();
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        // ===================== Data =====================
        /// <summary>Finds every student's photo file and counts their printed card sides.</summary>
        private void ReadPhotosAndHistory()
        {
            _photoPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Student s in _students)
            {
                string id = s.StudentId ?? "";
                if (!_photoPaths.ContainsKey(id)) _photoPaths[id] = CardRenderer.FindPhoto(_photoFolder, id);
            }

            _printed = new Dictionary<string, PrintCount>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (PrintRecord r in PrintHistory.Load())
                {
                    PrintCount c;
                    if (!_printed.TryGetValue(r.StudentId ?? "", out c)) _printed[r.StudentId ?? ""] = c = new PrintCount();
                    if (r.IsFront) c.Front++;
                    if (r.IsBack) c.Back++;
                    if (r.PrintedAt > c.Last) c.Last = r.PrintedAt;
                }
            }
            catch
            {
                // Unreadable history: the gallery still works, it just shows nothing as printed.
            }
        }

        private void Reload()
        {
            string current = CurrentId();
            if (_loaderCts != null) _loaderCts.Cancel();
            lock (_thumbLock)
            {
                foreach (Bitmap b in _thumbs.Values) b.Dispose();
                _thumbs.Clear();
            }
            HashSet<string> chosen = ChosenIds();
            ReadPhotosAndHistory();
            _detailStudent = null;   // the photo file may have changed: load it again
            ShowStudents(current, chosen);
            StartThumbnailLoader();
        }

        private string PhotoPath(Student s)
        {
            string path;
            return _photoPaths.TryGetValue(s.StudentId ?? "", out path) ? path : null;
        }

        private PrintCount Printed(Student s)
        {
            PrintCount c;
            return _printed.TryGetValue(s.StudentId ?? "", out c) ? c : null;
        }

        private bool PassesFilter(Student s)
        {
            PrintCount c = Printed(s);
            switch ((Filter)Math.Max(0, _cboFilter.SelectedIndex))
            {
                case Filter.MissingPhoto: return PhotoPath(s) == null;
                case Filter.NotPrinted: return c == null || (c.Front == 0 && c.Back == 0);
                case Filter.PartlyPrinted: return c != null && (c.Front > 0) != (c.Back > 0);
                case Filter.Printed: return c != null && c.Front > 0 && c.Back > 0;
                default: return true;
            }
        }

        private static bool Matches(Student s, string q)
        {
            return q.Length == 0 || Has(s.StudentId, q) || Has(s.NameKh, q) || Has(s.NameEn, q) ||
                   Has(s.Faculty, q) || Has(s.Generation, q);
        }

        /// <summary>
        /// Rebuilds the tiles from the search + filter. Students in <paramref name="chosenIds"/> that are still shown stay
        /// chosen; <paramref name="focusId"/> gets the focus (and the details panel). With nothing chosen, the focused tile
        /// (or the first one) is chosen.
        /// </summary>
        private void ShowStudents(string focusId, ICollection<string> chosenIds)
        {
            string q = _txtSearch.Text.Trim();
            List<Student> shown = _students.Where(s => Matches(s, q) && PassesFilter(s)).ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            ListViewItem focus = null, firstChosen = null;
            bool anyChosen = false;
            foreach (Student s in shown)
            {
                var item = new ListViewItem(s.StudentId ?? "") { Tag = s };
                _list.Items.Add(item);
                if (focus == null && focusId != null && string.Equals(s.StudentId, focusId, StringComparison.OrdinalIgnoreCase))
                    focus = item;
                if (chosenIds != null && chosenIds.Contains(s.StudentId ?? ""))
                {
                    item.Selected = true;
                    anyChosen = true;
                    if (firstChosen == null) firstChosen = item;
                }
            }
            if (focus != null && anyChosen && !focus.Selected) focus = firstChosen;   // the details panel shows a chosen student
            if (focus == null) focus = firstChosen ?? (_list.Items.Count > 0 ? _list.Items[0] : null);
            if (focus != null)
            {
                if (!anyChosen) focus.Selected = true;
                focus.Focused = true;
            }
            _list.EndUpdate();
            if (focus != null && _list.IsHandleCreated) focus.EnsureVisible();

            _lblEmpty.Text = _students.Count == 0
                ? "No students loaded.\n\nImport a student list in the main window first."
                : "No students match the search or the “Show” filter.";
            _lblEmpty.Visible = shown.Count == 0;

            int withPhoto = _students.Count(s => PhotoPath(s) != null);
            int done = _students.Count(s => { PrintCount c = Printed(s); return c != null && c.Front > 0 && c.Back > 0; });
            _lblSummary.Text = string.Format(
                "{0} of {1} students shown   ·   {2} with photo, {3} missing   ·   {4} fully printed   ·   Photo folder: {5}",
                shown.Count, _students.Count, withPhoto, _students.Count - withPhoto, done,
                _photoFolder.Length == 0 ? "(not chosen)" : _photoFolder);

            ShowDetails();
            UpdateChoice();
            if (_loaderCts != null) StartThumbnailLoader();   // load the newly shown tiles first
        }

        /// <summary>The student the details panel shows: the focused tile when it is chosen, else the first chosen one.</summary>
        private Student CurrentStudent()
        {
            ListViewItem focused = _list.FocusedItem;
            if (focused != null && focused.Selected) return focused.Tag as Student;
            return _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as Student;
        }

        /// <summary>StudentIds of the chosen tiles, in tile order.</summary>
        private List<string> ChosenIdList()
        {
            return _list.SelectedItems.Cast<ListViewItem>()
                .OrderBy(i => i.Index)
                .Select(i => ((Student)i.Tag).StudentId ?? "")
                .ToList();
        }

        private HashSet<string> ChosenIds()
        {
            return new HashSet<string>(ChosenIdList(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Ctrl+A: chooses every tile the search + filter show.</summary>
        private void ChooseAllShown()
        {
            if (_list.Items.Count == 0) return;
            _list.BeginUpdate();
            foreach (ListViewItem item in _list.Items) item.Selected = true;
            _list.EndUpdate();
            _list.Focus();
        }

        /// <summary>"Select in List" names how many students it hands over.</summary>
        private void UpdateChoice()
        {
            int n = _list.SelectedItems.Count;
            _btnSelect.Text = n > 1 ? Lang.F("Select {0} in List", n) : Lang.T("Select in List");
        }

        private string CurrentId()
        {
            Student s = CurrentStudent();
            return s == null ? null : s.StudentId;
        }

        // ===================== Thumbnails =====================
        /// <summary>Loads missing thumbnails on a worker thread, tiles in the order they are shown first.</summary>
        private void StartThumbnailLoader()
        {
            if (_loaderCts != null) _loaderCts.Cancel();
            var cts = new CancellationTokenSource();
            _loaderCts = cts;

            var shownFirst = _list.Items.Cast<ListViewItem>().Select(i => (Student)i.Tag).ToList();
            var jobs = shownFirst.Concat(_students.Except(shownFirst))
                .Select(s => new KeyValuePair<string, string>(s.StudentId ?? "", PhotoPath(s)))
                .Where(j => j.Value != null)
                .ToList();
            CancellationToken token = cts.Token;

            Task.Run(() =>
            {
                foreach (KeyValuePair<string, string> job in jobs)
                {
                    if (token.IsCancellationRequested) return;
                    lock (_thumbLock)
                    {
                        if (_closed) return;
                        if (_thumbs.ContainsKey(job.Key)) continue;
                    }

                    Bitmap thumb;
                    try { thumb = MakeThumb(job.Value, ThumbW * 2, ThumbH * 2); }   // 2x: sharp on high-DPI screens
                    catch { continue; }   // unreadable image: tile keeps the placeholder

                    lock (_thumbLock)
                    {
                        if (_closed || token.IsCancellationRequested || _thumbs.ContainsKey(job.Key)) thumb.Dispose();
                        else _thumbs[job.Key] = thumb;
                    }
                    _thumbsDirty = true;
                }
            }, token);
        }

        /// <summary>Photo cropped to the card photo ratio (keeps the top third for faces), EXIF rotation applied.</summary>
        private static Bitmap MakeThumb(string path, int w, int h)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image img = Image.FromStream(fs))
            {
                CardRenderer.ApplyExifOrientation(img);
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                using (var attr = new ImageAttributes())
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.White);   // transparent PNGs (background removed) show on white, like on the card
                    attr.SetWrapMode(WrapMode.TileFlipXY);
                    Rectangle src = CoverCrop(img.Size, new Size(w, h));
                    g.DrawImage(img, new Rectangle(0, 0, w, h), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attr);
                }
                return bmp;
            }
        }

        private static Rectangle CoverCrop(Size src, Size dst)
        {
            float srcRatio = (float)src.Width / src.Height;
            float dstRatio = (float)dst.Width / dst.Height;
            if (srcRatio > dstRatio)
            {
                int w = (int)(src.Height * dstRatio);
                return new Rectangle((src.Width - w) / 2, 0, w, src.Height);
            }
            int h = (int)(src.Width / dstRatio);
            return new Rectangle(0, (src.Height - h) / 3, src.Width, h);
        }

        // ===================== Drawing =====================
        private void DrawTile(object sender, DrawListViewItemEventArgs e)
        {
            var s = e.Item.Tag as Student;
            if (s == null) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle tile = Rectangle.Inflate(e.Bounds, -4, -4);
            bool selected = e.Item.Selected;
            using (var back = new SolidBrush(selected ? AccentSoft : Surface)) g.FillRectangle(back, tile);
            using (var pen = new Pen(selected ? Accent : Border, selected ? 2f : 1f))
                g.DrawRectangle(pen, tile.X, tile.Y, tile.Width - 1, tile.Height - 1);

            // Photo
            var photo = new Rectangle(tile.X + 8, tile.Y + (tile.Height - ThumbH) / 2, ThumbW, ThumbH);
            Bitmap thumb;
            lock (_thumbLock) _thumbs.TryGetValue(s.StudentId ?? "", out thumb);
            string photoPath = PhotoPath(s);
            if (thumb != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(thumb, photo);
            }
            else
            {
                using (var ph = new SolidBrush(Placeholder)) g.FillRectangle(ph, photo);
                TextRenderer.DrawText(g, Lang.T(photoPath == null ? "No photo" : "Loading…"), SmallFont, photo,
                    photoPath == null ? Danger : TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            using (var pen = new Pen(Border)) g.DrawRectangle(pen, photo.X, photo.Y, photo.Width - 1, photo.Height - 1);

            // Text
            int x = photo.Right + 10, w = tile.Right - x - 8, y = tile.Y + 6;
            Color main = selected ? Accent : TextMain;
            y = Line(g, s.StudentId, IdFont, main, x, y, w);
            y = Line(g, s.NameKh, KhmerFont, main, x, y - 2, w) - 2;
            y = Line(g, s.NameEn, LatinFont, main, x, y, w);
            string meta = string.Join("  ·  ", new[] { s.Faculty, s.Level, string.IsNullOrEmpty(s.Generation) ? null : Lang.T("Gen " + s.Generation) }
                .Where(t => !string.IsNullOrEmpty(t)));
            y = Line(g, meta, KhmerSmallFont, TextMuted, x, y - 2, w);

            // Badges under the text (never past the tile edge)
            int bx = x, by = Math.Min(y + 2, tile.Bottom - 22);
            PrintCount c = Printed(s);
            if (photoPath == null) bx = Badge(g, Lang.T("NO PHOTO"), Danger, bx, by);
            if (c == null || (c.Front == 0 && c.Back == 0)) Badge(g, Lang.T("NOT PRINTED"), TextMuted, bx, by);
            else
            {
                bx = Badge(g, Lang.T(c.Front > 0 ? "FRONT ✓" : "FRONT –"), c.Front > 0 ? Success : Danger, bx, by);
                bx = Badge(g, Lang.T(c.Back > 0 ? "BACK ✓" : "BACK –"), c.Back > 0 ? Success : Danger, bx, by);
                if (c.Front > 1 || c.Back > 1) Badge(g, Lang.T("REPRINTED"), Warning, bx, by);
            }

            // Chosen tiles get a check in the corner, so several chosen students stand out at a glance
            if (selected)
            {
                var dot = new Rectangle(tile.Right - 26, tile.Y + 6, 18, 18);
                using (var b = new SolidBrush(Accent)) g.FillEllipse(b, dot);
                using (var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[]
                    {
                        new Point(dot.X + 5, dot.Y + 9), new Point(dot.X + 8, dot.Y + 12), new Point(dot.X + 13, dot.Y + 6)
                    });
            }
        }

        /// <summary>Draws one line of text (ellipsis when too long) and returns the next line's y.</summary>
        private static int Line(Graphics g, string text, Font font, Color color, int x, int y, int width)
        {
            if (string.IsNullOrEmpty(text)) return y;
            const TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            Size size = TextRenderer.MeasureText(g, text, font, new Size(width, int.MaxValue), flags);
            TextRenderer.DrawText(g, text, font, new Rectangle(x, y, width, size.Height), color, flags);
            return y + size.Height;
        }

        /// <summary>Small outlined pill; returns the x for the next badge.</summary>
        private static int Badge(Graphics g, string text, Color color, int x, int y)
        {
            Size size = TextRenderer.MeasureText(g, text, BadgeFont, Size.Empty, TextFormatFlags.NoPadding);
            var rect = new Rectangle(x, y, size.Width + 12, 17);
            using (GraphicsPath path = RoundRect(rect, 8))
            using (var fill = new SolidBrush(Color.FromArgb(24, color)))
            using (var pen = new Pen(Color.FromArgb(120, color)))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, text, BadgeFont, rect, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return rect.Right + 5;
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ===================== Details panel =====================
        private void ShowDetails()
        {
            Student s = CurrentStudent();
            if (s != null && s == _detailStudent) return;   // selection events repeat while the tiles rebuild
            _detailStudent = s;
            _fields.SuspendLayout();
            _fields.Controls.Clear();
            _fields.RowStyles.Clear();
            _fields.RowCount = 0;

            if (s == null)
            {
                _lblDetailTitle.Text = "";
                ReplacePhoto(null);
                _btnSelect.Enabled = _btnOpenPhoto.Enabled = _btnShowFile.Enabled = _btnRemoveBg.Enabled = false;
                _fields.ResumeLayout();
                return;
            }

            _lblDetailTitle.Text = string.IsNullOrEmpty(s.NameEn) ? s.StudentId : s.StudentId + "  —  " + s.NameEn;
            string photo = PhotoPath(s);
            ReplacePhoto(photo == null ? null : LoadPhotoCopy(photo));

            AddField("ID", s.StudentId);
            AddField("Name (KH)", s.NameKh);
            AddField("Name (EN)", s.NameEn);
            AddField("Sex", s.Gender);
            AddField("Date of birth", s.DateOfBirth);
            AddField("Faculty", s.Faculty);
            AddField("Level", s.Level);
            AddField("Generation", s.Generation);
            AddField("Valid until", s.ValidUntil);
            if (!string.IsNullOrEmpty(s.QrData)) AddField("QR data", s.QrData);
            AddField("Photo", photo == null ? Lang.F("Missing — expected {0}.jpg / .png", s.StudentId) : Path.GetFileName(photo),
                photo == null ? Danger : TextMain);

            PrintCount c = Printed(s);
            if (c == null || (c.Front == 0 && c.Back == 0)) AddField("Printed", Lang.T("Not printed yet"), TextMuted);
            else
                AddField("Printed",
                    Lang.F("Front {0}×, Back {1}×\nLast {2:yyyy-MM-dd HH:mm}", c.Front, c.Back, c.Last),
                    c.Front > 0 && c.Back > 0 ? Success : Warning);

            _fields.ResumeLayout(true);
            _btnSelect.Enabled = true;
            _btnOpenPhoto.Enabled = _btnShowFile.Enabled = photo != null;
            _btnRemoveBg.Enabled = CanRemoveBackground(s);
        }

        /// <summary>True when there is a photo (or a kept original to restore) in an existing photo folder.</summary>
        private bool CanRemoveBackground(Student s)
        {
            if (s == null || _photoFolder.Length == 0 || !Directory.Exists(_photoFolder)) return false;
            return PhotoPath(s) != null || BackgroundRemovalForm.FindOriginal(_photoFolder, s.StudentId) != null;
        }

        private void AddField(string label, string value, Color? color = null)
        {
            int row = _fields.RowCount++;
            _fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _fields.Controls.Add(new Label
            {
                Text = label, AutoSize = true, ForeColor = TextMuted, Margin = new Padding(0, 3, 6, 3)
            }, 0, row);
            _fields.Controls.Add(Lang.Keep(new Label   // student data: shown as it is
            {
                Text = string.IsNullOrEmpty(value) ? "—" : value, AutoSize = true, MaximumSize = new Size(190, 0),
                ForeColor = color ?? TextMain, UseMnemonic = false, Margin = new Padding(0, 3, 0, 3)
            }), 1, row);
        }

        /// <summary>Copy of the photo that does not keep the file locked (so Remove Background can still save it).</summary>
        private static Bitmap LoadPhotoCopy(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (Image img = Image.FromStream(fs))
                {
                    CardRenderer.ApplyExifOrientation(img);
                    return new Bitmap(img);
                }
            }
            catch
            {
                return null;   // damaged file: show the "No photo" placeholder
            }
        }

        private void ReplacePhoto(Image image)
        {
            Image old = _picPhoto.Image;
            _picPhoto.Image = image;
            if (old != null) old.Dispose();
            _picPhoto.Invalidate();
        }

        // ===================== Commands =====================
        private void ChooseCurrent()
        {
            Student s = CurrentStudent();
            if (s == null) return;
            SelectedStudentId = s.StudentId;
            SelectedStudentIds = ChosenIdList();
            DialogResult = DialogResult.OK;
        }

        private void OpenPhoto()
        {
            Student s = CurrentStudent();
            string path = s == null ? null : PhotoPath(s);
            if (path == null) return;
            try { Process.Start(path); }
            catch (Exception ex) { ShowError("Could not open the photo", ex); }
        }

        private void ShowPhotoInFolder()
        {
            Student s = CurrentStudent();
            string path = s == null ? null : PhotoPath(s);
            if (path == null) return;
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); }
            catch (Exception ex) { ShowError("Could not open the folder", ex); }
        }

        /// <summary>Opens the background-removal dialog for the selected student, then refreshes their photo and tile.</summary>
        private void RemoveBackground()
        {
            Student s = CurrentStudent();
            if (!CanRemoveBackground(s)) return;
            string id = s.StudentId ?? "";

            // The loader may be reading this photo right now; stop it so the dialog can replace the file.
            if (_loaderCts != null) _loaderCts.Cancel();
            ReplacePhoto(null);

            BackgroundRemovalForm dlg = null;
            Cursor = Cursors.WaitCursor;
            try { dlg = new BackgroundRemovalForm(_photoFolder, id); }
            catch (Exception ex) { ShowError("Could not open the photo", ex); }
            finally { Cursor = Cursors.Default; }

            bool changed = false;
            if (dlg != null)
                using (dlg) changed = dlg.ShowDialog(this) == DialogResult.OK;

            if (changed)
            {
                // The saved photo may have a new extension (.jpg → .png), so look it up again.
                _photoPaths[id] = CardRenderer.FindPhoto(_photoFolder, id);
                lock (_thumbLock)
                {
                    Bitmap old;
                    if (_thumbs.TryGetValue(id, out old)) { _thumbs.Remove(id); old.Dispose(); }
                }
            }
            _detailStudent = null;   // reload the large photo
            ShowStudents(id, ChosenIds());   // also restarts the thumbnail loader
        }

        private void ShowError(string what, Exception ex)
        {
            AppDialog.Show(this, what + ":\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ===================== Helpers =====================
        private static bool Has(string value, string q)
        {
            return value != null && value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            PillButton b = ModalUi.ToolButton(text, onClick);
            b.Margin = new Padding(0, 0, 6, 6);
            return b;
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void SetCueBanner(TextBox box, string text)
        {
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, Lang.T(text));
        }

        /// <summary>Double-buffered list so tiles don't flicker while thumbnails arrive.</summary>
        private sealed class GalleryList : ListView
        {
            public GalleryList()
            {
                DoubleBuffered = true;
            }
        }
    }
}
