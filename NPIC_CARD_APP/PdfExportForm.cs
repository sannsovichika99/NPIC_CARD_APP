using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Design Preview (Ctrl+Shift+V): shows the card design laid out on paper — exactly what Export PDF writes — and
    /// exports it as a PDF with no extra software.
    ///   - Students: the current one, the selected ones or every student in the list (a sample student when none is loaded);
    ///   - Layout: "A4 sheet" (front and back side by side at real size, with cut marks) or "Card size" (one card side
    ///     per page, the page the size of the card — for print shops);
    ///   - ◀ ▶, Page Up / Page Down or the mouse wheel turn the pages; Ctrl+S exports.
    /// The screen preview and the PDF are drawn from the same <see cref="SheetPage"/> list.
    /// </summary>
    public class PdfExportForm : ModalForm
    {
        private const float A4W = 210f, A4H = 297f, PageMargin = 12f, HeaderH = 12f, FooterH = 6f;
        private const float CaptionH = 5f, GapX = 8f, GapY = 6f, PairGap = 6f;   // 4 landscape cards per A4
        private const float CaptionPt = 7f, TitlePt = 11f, SubPt = 8f;
        private const float MarkOffset = 0.8f, MarkLength = 2.2f;
        private const int PreviewWidth = 720;   // px kept per card for the screen preview

        private static readonly Color Ink = Color.FromArgb(33, 33, 40);
        private static readonly Color Muted = Color.FromArgb(110, 110, 120);
        private static readonly Color Hairline = Color.FromArgb(190, 190, 198);

        private readonly AppSettings _settings;
        private readonly string _designName;
        private readonly List<Student>[] _choices;
        private readonly bool _sample;

        private readonly PillTabs _studentTabs, _layoutTabs;
        private readonly CheckBox _chkBack, _chkMarks;
        private readonly SheetCanvas _canvas;
        private readonly Label _pageLabel, _status;
        private readonly Button _btnPrev, _btnNext, _btnExport;
        private readonly Label _lblPreparedBy;
        private readonly TextBox _txtPreparedBy;
        private readonly Button _btnPageDesign, _btnDesignMenu;
        private readonly ComboBox _cmbDesign;
        private readonly FlowLayoutPanel _listBar;   // second toolbar row, Student list only
        private ContextMenuStrip _designMenu;
        private bool _fillingDesigns;
        private readonly System.Windows.Forms.Timer _typingTimer;   // rebuilds the list a moment after typing stops

        private SizeF _cardMm;                 // card size on paper, turned like the design (portrait or landscape)
        private Bitmap _backFull, _backSmall;  // the back is the same for every student
        private readonly Dictionary<Student, Bitmap> _frontCache = new Dictionary<Student, Bitmap>();
        private List<SheetPage> _pages = new List<SheetPage>();
        private int _page;

        /// <param name="settings">Card design settings (front and back are rendered from them).</param>
        /// <param name="designName">Name of the design in use, printed in the sheet header.</param>
        /// <param name="current">The previewed student (may be null).</param>
        /// <param name="selected">The students selected to print.</param>
        /// <param name="all">Every student in the list (search filter applied).</param>
        public PdfExportForm(AppSettings settings, string designName, Student current, IList<Student> selected, IList<Student> all)
        {
            _settings = settings;
            _designName = string.IsNullOrWhiteSpace(designName) ? Lang.T("Card design") : designName;

            var cur = current == null ? new List<Student>() : new List<Student> { current };
            var sel = (selected ?? new Student[0]).Where(s => s != null).ToList();
            var every = (all ?? new Student[0]).Where(s => s != null).ToList();
            _sample = cur.Count == 0 && sel.Count == 0 && every.Count == 0;
            _choices = _sample
                ? new[] { new List<Student> { SampleStudent() } }
                : new[] { cur, sel, every };

            Text = "Design Preview";
            Subtitle = _sample
                ? "No students loaded — the design is shown with a sample student"
                : "See the cards on paper at real size, then export them as a PDF";
            Glyph = Theme.IcoDocument;
            Size = new Size(1000, 780);
            MinimumSize = new Size(820, 600);
            KeyPreview = true;

            // ---- Toolbar: students · layout · options ----
            var bar = ModalUi.Toolbar(56);
            bar.Controls.Add(BarLabel("Students"));
            _studentTabs = _sample
                ? new PillTabs("Sample student")
                : new PillTabs("Current", Lang.F("Selected ({0})", sel.Count), Lang.F("All in list ({0})", every.Count));
            _studentTabs.SelectedIndex = _sample ? 0 : sel.Count > 1 ? 1 : cur.Count > 0 ? 0 : sel.Count > 0 ? 1 : 2;
            _studentTabs.SelectedIndexChanged += (s, e) => Rebuild(true);
            bar.Controls.Add(_studentTabs);

            bar.Controls.Add(new Panel { Width = 1, Height = 24, BackColor = Theme.Border, Margin = new Padding(4, 8, 12, 0) });
            bar.Controls.Add(BarLabel("Layout"));
            _layoutTabs = new PillTabs("A4 sheet", "Card size", "Student list");
            _layoutTabs.SelectedIndexChanged += (s, e) =>
            {
                _chkMarks.Enabled = _layoutTabs.SelectedIndex == 0;
                ShowLayoutOptions();
                Rebuild(true);
            };
            bar.Controls.Add(_layoutTabs);

            _chkBack = Check("Back side", true);
            _chkBack.CheckedChanged += (s, e) => Rebuild(false);
            bar.Controls.Add(_chkBack);
            _chkMarks = Check("Cut marks", true);
            _chkMarks.CheckedChanged += (s, e) => Rebuild(false);
            bar.Controls.Add(_chkMarks);

            // ---- Second row, Student list only: Design ▾ · Page Design… · ⋯ · Prepared by ----
            _listBar = ModalUi.Toolbar(50);
            _listBar.Padding = new Padding(16, 7, 16, 0);
            _listBar.Controls.Add(BarLabel("Design"));
            _cmbDesign = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain,
                FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 7, 8, 0), MaxDropDownItems = 16
            };
            _cmbDesign.SelectedIndexChanged += (s, e) => UseDesign();
            _listBar.Controls.Add(_cmbDesign);

            _btnPageDesign = ModalUi.ToolButton("Page Design…", (s, e) => EditDesign(false));
            _btnPageDesign.Margin = new Padding(0, 3, 6, 0);
            var tip = new ToolTip();
            tip.SetToolTip(_btnPageDesign, Lang.T("Paper, margins, fonts and the text above and below the list (Ctrl+D)"));
            _listBar.Controls.Add(_btnPageDesign);
            _btnDesignMenu = ModalUi.ToolButton("⋯", (s, e) => ShowDesignMenu());
            _btnDesignMenu.Margin = new Padding(0, 3, 0, 0);
            _btnDesignMenu.MinimumSize = new Size(36, 32);
            tip.SetToolTip(_btnDesignMenu, Lang.T("New design, delete design"));
            _listBar.Controls.Add(_btnDesignMenu);

            // The name after "រៀបចំដោយលោក" ([name] in the design; remembered)
            _listBar.Controls.Add(new Panel { Width = 1, Height = 24, BackColor = Theme.Border, Margin = new Padding(14, 8, 12, 0) });
            _lblPreparedBy = BarLabel("Prepared by");
            _lblPreparedBy.Margin = new Padding(0, 11, 6, 0);
            _listBar.Controls.Add(_lblPreparedBy);
            _txtPreparedBy = new TextBox
            {
                Width = 200, Text = settings.ListPreparedBy ?? "", BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain, Margin = new Padding(0, 8, 0, 0)
            };
            _typingTimer = new System.Windows.Forms.Timer { Interval = 350 };
            _typingTimer.Tick += (s, e) =>
            {
                _typingTimer.Stop();
                if (ListReport) Rebuild(false);
            };
            _txtPreparedBy.TextChanged += (s, e) =>
            {
                _typingTimer.Stop();
                _typingTimer.Start();
            };
            _listBar.Controls.Add(_txtPreparedBy);
            FillDesigns();
            ShowLayoutOptions();

            // ---- Page preview ----
            _canvas = new SheetCanvas { Dock = DockStyle.Fill };
            _canvas.CardImage = PreviewImage;
            _canvas.MouseWheel += (s, e) => TurnPage(e.Delta < 0 ? 1 : -1);
            _canvas.MouseEnter += (s, e) => _canvas.Focus();

            _status = new Label { Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            ModalUi.StyleStatus(_status);

            // ---- Footer: ◀ page ▶ · Close · Export PDF… ----
            var footer = ModalUi.Footer();
            _btnExport = ModalUi.Button("Export PDF…", PillKind.Primary, (s, e) => ExportPdf());
            Button close = ModalUi.Button("Close", PillKind.Secondary, (s, e) => Close());
            _btnNext = ModalUi.Button("▶", PillKind.Secondary, (s, e) => TurnPage(1));
            _btnPrev = ModalUi.Button("◀", PillKind.Secondary, (s, e) => TurnPage(-1));
            _btnNext.MinimumSize = _btnPrev.MinimumSize = new Size(44, 36);
            _pageLabel = new Label
            {
                AutoSize = true, ForeColor = Theme.TextMain, BackColor = Theme.SurfaceAlt, Margin = new Padding(8, 9, 0, 0)
            };
            footer.Controls.Add(_btnExport);
            footer.Controls.Add(close);
            footer.Controls.Add(new Panel { Width = 18, Height = 1, BackColor = Theme.SurfaceAlt });
            footer.Controls.Add(_btnNext);
            footer.Controls.Add(_pageLabel);
            footer.Controls.Add(_btnPrev);

            Controls.Add(_canvas);
            Controls.Add(_listBar);   // docked under the main toolbar
            Controls.Add(bar);
            Controls.Add(_status);
            Controls.Add(footer);
            CancelButton = close;

            PrepareDesign();
            Rebuild(true);
        }

        // ===================== Layout =====================

        private List<Student> Students
        {
            get { return _choices[Math.Max(0, Math.Min(_choices.Length - 1, _studentTabs.SelectedIndex))]; }
        }

        private bool CardSizePages { get { return _layoutTabs.SelectedIndex == 1; } }

        /// <summary>"Student list": a table report of the students instead of card images.</summary>
        private bool ListReport { get { return _layoutTabs.SelectedIndex == 2; } }

        /// <summary>Card options for the card layouts, the design row for the student list.</summary>
        private void ShowLayoutOptions()
        {
            _chkBack.Visible = _chkMarks.Visible = !ListReport;   // the report has no card images
            _listBar.Visible = ListReport;
        }

        /// <summary>"A4 portrait", "Letter landscape"… of the student list.</summary>
        private string PaperText()
        {
            ListPageDesign d = ListDesigns.Current(_settings);
            return Lang.F(d.Landscape ? "{0} landscape" : "{0} portrait", d.PaperName);
        }

        // ---- Student list designs (like Card Template: built-in designs and My designs) ----

        private sealed class DesignItem
        {
            public readonly ListPageDesign Design;
            public DesignItem(ListPageDesign design) { Design = design; }

            public override string ToString()
            {
                return Design.BuiltIn ? Design.DisplayName + "   · " + Lang.T("built-in") : Design.DisplayName;
            }
        }

        /// <summary>(Re)fills the Design list and selects the design in use.</summary>
        private void FillDesigns()
        {
            _fillingDesigns = true;
            try
            {
                string current = ListDesigns.Current(_settings).Id;
                _cmbDesign.Items.Clear();
                foreach (ListPageDesign d in ListDesigns.All(_settings)) _cmbDesign.Items.Add(new DesignItem(d));
                for (int i = 0; i < _cmbDesign.Items.Count; i++)
                    if (((DesignItem)_cmbDesign.Items[i]).Design.Id == current) _cmbDesign.SelectedIndex = i;
            }
            finally
            {
                _fillingDesigns = false;
            }
        }

        private ListPageDesign SelectedDesign
        {
            get
            {
                var item = _cmbDesign.SelectedItem as DesignItem;
                return item != null ? item.Design : ListDesigns.Current(_settings);
            }
        }

        /// <summary>A design was picked in the list: it becomes the one in use (remembered).</summary>
        private void UseDesign()
        {
            if (_fillingDesigns) return;
            _settings.ListDesignId = SelectedDesign.Id;
            SaveSettings();
            Rebuild(true);
        }

        /// <summary>
        /// Page Design… (Ctrl+D). A saved design is changed in place; a built-in design (or <paramref name="asNew"/>) is
        /// copied first and the copy is added to My designs.
        /// </summary>
        private void EditDesign(bool asNew)
        {
            ListPageDesign source = SelectedDesign;
            bool isNew = asNew || source.BuiltIn;
            ListPageDesign edit = isNew ? ListDesigns.CopyOf(_settings, source) : source.Clone();
            using (var form = new PageDesignForm(_settings, edit, isNew))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                ListDesigns.Save(_settings, form.Result);
            }
            SaveSettings();
            FillDesigns();
            Rebuild(false);
        }

        private void DeleteDesign()
        {
            ListPageDesign d = SelectedDesign;
            if (d.BuiltIn) return;
            if (AppDialog.Show(this, Lang.F("Delete the design \"{0}\"?", d.DisplayName), Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            ListDesigns.Delete(_settings, d.Id);
            SaveSettings();
            FillDesigns();
            Rebuild(true);
        }

        private void ShowDesignMenu()
        {
            ListPageDesign d = SelectedDesign;
            if (_designMenu != null) _designMenu.Dispose();
            _designMenu = ThemedMenu.Create();
            _designMenu.Items.Add(ThemedMenu.Item(d.BuiltIn ? "Copy and edit…\tCtrl+D" : "Edit design…\tCtrl+D", () => EditDesign(false)));
            _designMenu.Items.Add(ThemedMenu.Item("New design from this one…", () => EditDesign(true)));
            _designMenu.Items.Add(new ToolStripSeparator());
            _designMenu.Items.Add(ThemedMenu.Item("Delete design…", DeleteDesign, !d.BuiltIn));
            Lang.Apply(_designMenu);
            _designMenu.Show(_btnDesignMenu, new Point(0, _btnDesignMenu.Height));
        }

        private void SaveSettings()
        {
            try { _settings.Save(); }
            catch (Exception ex)
            {
                AppDialog.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Renders the back once and finds out whether the design is portrait or landscape.</summary>
        private void PrepareDesign()
        {
            _cardMm = new SizeF(CardRenderer.CardWidthMm, CardRenderer.CardHeightMm);
            try
            {
                _backFull = CardRenderer.RenderBack(_settings);
                _backSmall = Shrink(_backFull);
                Student first = _choices.SelectMany(c => c).FirstOrDefault();
                using (Bitmap front = first != null ? CardRenderer.RenderFront(first, _settings) : null)
                {
                    Image probe = (Image)front ?? _backFull;
                    if (probe.Height > probe.Width) _cardMm = new SizeF(_cardMm.Height, _cardMm.Width);
                }
            }
            catch (Exception ex)
            {
                _status.Text = Lang.F("Preview error: {0}", ex.Message);
            }
        }

        private void Rebuild(bool firstPage)
        {
            List<Student> students = Students;
            _pages = ListReport ? ListLayout(students)
                : CardSizePages ? CardSizeLayout(students, _chkBack.Checked)
                : SheetLayout(students, _chkBack.Checked, _chkMarks.Checked);
            if (firstPage || _page >= _pages.Count) _page = 0;
            ShowPage();

            if (students.Count == 0)
            {
                _status.Text = Lang.T("No student in this choice — pick another one above.");
                return;
            }
            int sides = students.Count * (_chkBack.Checked ? 2 : 1);
            string pages = _pages.Count == 1 ? Lang.T("1 page") : Lang.F("{0} pages", _pages.Count);
            string who = students.Count == 1 ? Lang.T("1 student") : Lang.F("{0} students", students.Count);
            if (ListReport)
            {
                _status.Text = who + "  ·  " + pages + "  ·  " + PaperText();
                return;
            }
            _status.Text = who + "  ·  " + Lang.F("{0} card side(s)", sides) + "  ·  " + pages + "  ·  " +
                           Lang.T("Print the PDF at 100% (actual size)");
        }

        private void ShowPage()
        {
            bool any = _pages.Count > 0 && Students.Count > 0;
            _canvas.Page = any ? _pages[_page] : null;
            _canvas.EmptyText = Lang.T("No student in this choice");
            _pageLabel.Text = any ? Lang.F("Page {0} of {1}", _page + 1, _pages.Count) : "";
            _btnPrev.Enabled = any && _page > 0;
            _btnNext.Enabled = any && _page < _pages.Count - 1;
            _btnExport.Enabled = any;
            TrimCache();
        }

        private void TurnPage(int delta)
        {
            int next = Math.Max(0, Math.Min(_pages.Count - 1, _page + delta));
            if (next == _page) return;
            _page = next;
            ShowPage();
        }

        /// <summary>A4 portrait: per student the front (and back) at real size with a caption, as many rows as fit.</summary>
        private List<SheetPage> SheetLayout(List<Student> students, bool back, bool marks)
        {
            float cw = _cardMm.Width, ch = _cardMm.Height;
            float unitW = back ? cw * 2 + PairGap : cw;
            float usableW = A4W - 2 * PageMargin;
            float top = PageMargin + HeaderH, usableH = A4H - top - PageMargin - FooterH;
            int cols = Math.Max(1, (int)((usableW + GapX) / (unitW + GapX)));
            int rows = Math.Max(1, (int)((usableH + GapY) / (CaptionH + ch + GapY)));
            int perPage = cols * rows;
            float gridW = cols * unitW + (cols - 1) * GapX;
            float left = (A4W - gridW) / 2f;

            int total = Math.Max(1, (students.Count + perPage - 1) / perPage);
            string date = DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            string size = string.Format(CultureInfo.InvariantCulture, "{0:0.#} × {1:0.#} mm", cw, ch);
            var pages = new List<SheetPage>();
            for (int p = 0; p < total; p++)
            {
                var page = new SheetPage(A4W, A4H);
                page.Text(Lang.T("Student ID cards") + " — " + _designName, PageMargin, PageMargin - 2f, TitlePt, Ink, true);
                page.Text(Lang.F("{0} student(s)", students.Count) + "  ·  " + Lang.T("card") + " " + size + "  ·  " +
                          Lang.T("print at 100%") + "  ·  " + date, PageMargin, PageMargin + 4.6f, SubPt, Muted, false);
                page.Line(PageMargin, top - 3f, A4W - PageMargin, top - 3f, 0.4f, Hairline);
                page.Text(Lang.F("Page {0} of {1}", p + 1, total), PageMargin, A4H - 9f, SubPt, Muted, false);

                for (int i = 0; i < perPage; i++)
                {
                    int index = p * perPage + i;
                    if (index >= students.Count) break;
                    Student s = students[index];
                    float x = left + (i % cols) * (unitW + GapX);
                    float y = top + (i / cols) * (CaptionH + ch + GapY);

                    float indent = marks ? 1.5f : 0f;   // clear of the cut marks
                    page.Text(Caption(s), x + indent, y, CaptionPt, Ink, false);
                    AddCard(page, new RectangleF(x, y + CaptionH, cw, ch), s, true, marks);
                    if (back)
                    {
                        float bx = x + cw + PairGap;
                        page.Text(Lang.T("Back"), bx + indent, y, CaptionPt, Muted, false);
                        AddCard(page, new RectangleF(bx, y + CaptionH, cw, ch), s, false, marks);
                    }
                }
                pages.Add(page);
            }
            return pages;
        }

        /// <summary>One card side per page; the page is the card.</summary>
        private List<SheetPage> CardSizeLayout(List<Student> students, bool back)
        {
            var pages = new List<SheetPage>();
            foreach (Student s in students)
            {
                for (int side = 0; side < (back ? 2 : 1); side++)
                {
                    var page = new SheetPage(_cardMm.Width, _cardMm.Height);
                    page.Cards.Add(new SheetCard { Box = new RectangleF(0, 0, _cardMm.Width, _cardMm.Height), Student = s, Front = side == 0 });
                    pages.Add(page);
                }
            }
            if (pages.Count == 0) pages.Add(new SheetPage(_cardMm.Width, _cardMm.Height));
            return pages;
        }

        // ---- Student list (report) ----
        // Everything comes from the design in use (ListPageDesign): paper, margins, fonts, the texts above / below the table
        // and the table's columns (ListColumnDesign: field, header, relative width, alignment). Column widths are shared out
        // across the width inside the margins, so a design fits every paper size. Latin and Khmer pieces of a line are set in
        // their own font on one baseline (MixedText), like a Word document. The heading is on page 1 (or on every page), the
        // table header is repeated on every page and the footer ("រៀបចំដោយលោក …") follows the last row.

        private const float PageNoPt = 9f, CellPad = 1.5f, SignBlockW = 80f;
        private const float GridPt = 0.5f;
        private static readonly Color Grid = Color.Black;
        private static readonly Color HeadFill = Color.FromArgb(235, 235, 235);
        private const string KhPage = "ទំព័រ {0}/{1}";

        // Resolved design of the list being laid out (set by ListLayout)
        private MixedText.FontPair _fonts = MixedText.FontPair.Default;
        private float _bodyPt = 10f, _rowH = 7.5f, _headH = 8f;
        private bool _shadeHeader = true;
        private List<ListColumnDesign> _cols = ListFields.DefaultColumns();
        private float[] _colW;

        /// <summary>1 = male, 2 = female, 0 = not given.</summary>
        private static int GenderOf(Student s)
        {
            string g = KhmerText.Gender(s.Gender).Trim();
            if (g == "ប្រុស" || g == "ប") return 1;
            if (g == "ស្រី" || g == "ស") return 2;
            return 0;
        }

        /// <summary>Height of one line at <paramref name="pt"/>: the taller of the two fonts (Khmer OS Siemreap ≈ 1.8 em).</summary>
        private float LineMm(float pt)
        {
            return Math.Max(MixedText.LineEm(_fonts.Khmer), MixedText.LineEm(_fonts.Latin)) * pt * MixedText.MmPerPt;
        }

        /// <summary>Top of a line to its baseline at <paramref name="pt"/>.</summary>
        private float AscentMm(float pt)
        {
            return Math.Max(MixedText.AscentEm(_fonts.Khmer), MixedText.AscentEm(_fonts.Latin)) * pt * MixedText.MmPerPt;
        }

        /// <summary>The report: heading, then the table, then the footer lines.</summary>
        private List<SheetPage> ListLayout(List<Student> students)
        {
            ListPageDesign d = ListDesigns.Current(_settings);
            SizeF paper = d.PageMm;
            float pw = paper.Width, ph = paper.Height;
            float[] mm = d.MarginsMm;   // left, top, right, bottom
            float ml = mm[0], mt = mm[1], mr = mm[2], mb = mm[3];

            _fonts = new MixedText.FontPair(d.LatinFamily, d.ComplexFamily);
            _bodyPt = d.BodyPt;
            _shadeHeader = d.ShadeHeader;
            float headingPt = d.HeadingPt;
            _rowH = LineMm(_bodyPt) * 1.165f;   // 7.5 mm at 10 pt Khmer OS Siemreap
            _headH = LineMm(_bodyPt) * 1.24f;   // 8 mm
            float gap = LineMm(_bodyPt) * 0.6f;  // blank heading line
            float afterHeading = LineMm(_bodyPt) * 0.45f;

            float contentW = Math.Max(40f, pw - ml - mr);
            _cols = d.TableColumns;
            float scale = contentW / _cols.Sum(c => c.Weight);
            _colW = _cols.Select(c => c.Weight * scale).ToArray();
            float left = ml, right = ml + contentW;
            float pageNoH = LineMm(PageNoPt) * 0.8f;
            float bodyBottom = Math.Max(mt + 40f, ph - mb - pageNoH);

            // Texts with the placeholders filled in
            int male = students.Count(s => GenderOf(s) == 1), female = students.Count(s => GenderOf(s) == 2);
            string name = _txtPreparedBy.Text;
            List<string> heading = ListPageDesign.Lines(d.Heading);
            while (heading.Count > 0 && heading[heading.Count - 1].Trim().Length == 0) heading.RemoveAt(heading.Count - 1);
            List<string> footer = ListPageDesign.Lines(d.Footer).Where(l => l.Trim().Length > 0).ToList();

            float headingH = heading.Count == 0 ? 0f : afterHeading;
            foreach (string line in heading)
                headingH += line.Trim().Length == 0 ? gap
                    : LineMm(ListPageDesign.HasPlaceholder(line) ? _bodyPt : headingPt);
            float footerH = footer.Count == 0 ? 0f : LineMm(_bodyPt) * 0.9f + footer.Count * LineMm(_bodyPt);
            Func<int, bool> headed = page => page == 0 || d.HeadingEveryPage;

            // Rows per page; the last row(s) move to a new page when the footer would not fit under them
            var slices = new List<int[]>();   // { first row, row count }
            int next = 0;
            do
            {
                float tableTop = mt + (headed(slices.Count) ? headingH : 0f);
                int capacity = Math.Max(1, (int)((bodyBottom - tableTop - _headH) / _rowH));
                int take = Math.Min(capacity, students.Count - next);
                if (next + take == students.Count && take > 1 &&
                    tableTop + _headH + take * _rowH + footerH > bodyBottom)
                    take--;
                slices.Add(new[] { next, take });
                next += take;
            } while (next < students.Count);

            var pages = new List<SheetPage>();
            for (int p = 0; p < slices.Count; p++)
            {
                var page = new SheetPage(pw, ph);
                float y = mt;
                if (headed(p) && heading.Count > 0)
                {
                    foreach (string line in heading)
                    {
                        if (line.Trim().Length == 0)
                        {
                            y += gap;
                            continue;
                        }
                        bool plain = ListPageDesign.HasPlaceholder(line);
                        string text = ListPageDesign.Fill(line.Trim(), students.Count, male, female, name);
                        y = Centered(page, text, left, contentW, y, plain ? _bodyPt : headingPt, !plain);
                    }
                    y += afterHeading;
                }

                float bottom = Table(page, students, slices[p][0], slices[p][1], left, y);

                if (p == slices.Count - 1 && footer.Count > 0)
                {
                    // Each line centred in a block at the right, like a signature
                    float blockW = Math.Min(SignBlockW, contentW);
                    float fy = bottom + LineMm(_bodyPt) * 0.9f;
                    foreach (string line in footer)
                    {
                        string text = MixedText.Fit(ListPageDesign.Fill(line.Trim(), students.Count, male, female, name),
                            contentW, _bodyPt, false, _fonts);
                        float w = MixedText.Width(text, _bodyPt, false, _fonts);
                        float x = Math.Max(left, Math.Min(right - blockW / 2f - w / 2f, right - w));
                        page.Words(text, x, fy + AscentMm(_bodyPt), _bodyPt, Ink, false, _fonts);
                        fy += LineMm(_bodyPt);
                    }
                }
                if (slices.Count > 1)
                {
                    string number = string.Format(CultureInfo.InvariantCulture, KhPage, p + 1, slices.Count);
                    float w = MixedText.Width(number, PageNoPt, false, _fonts);
                    page.Words(number, left + (contentW - w) / 2f, ph - mb - LineMm(PageNoPt) * 0.25f, PageNoPt, Muted, false, _fonts);
                }
                pages.Add(page);
            }
            return pages;
        }

        /// <summary>One heading line centred in the content width; returns the top of the next line.</summary>
        private float Centered(SheetPage page, string text, float left, float width, float top, float pt, bool bold)
        {
            text = MixedText.Fit(text, width, pt, bold, _fonts);
            float w = MixedText.Width(text, pt, bold, _fonts);
            page.Words(text, left + (width - w) / 2f, top + AscentMm(pt), pt, Ink, bold, _fonts);
            return top + LineMm(pt);
        }

        /// <summary>Header row and <paramref name="count"/> student rows from <paramref name="top"/>; returns the bottom.</summary>
        private float Table(SheetPage page, List<Student> students, int first, int count, float left, float top)
        {
            float tableW = _colW.Sum(), right = left + tableW;
            float bottom = top + _headH + count * _rowH;
            Color? headBack = _shadeHeader ? HeadFill : (Color?)null;

            if (_shadeHeader) page.Fills.Add(new SheetFill { Box = new RectangleF(left, top, tableW, _headH), Color = HeadFill });
            float x = left;
            for (int c = 0; c < _cols.Count; c++)
            {
                Cell(page, (_cols[c].Header ?? "").Trim(), x, _colW[c], top, _headH, ListAlign.Center, true, headBack);
                x += _colW[c];
            }
            ListFields.Field[] fields = _cols.Select(c => ListFields.Find(c.Field)).ToArray();
            for (int r = 0; r < count; r++)
            {
                Student s = students[first + r];
                float y = top + _headH + r * _rowH;
                x = left;
                for (int c = 0; c < _cols.Count; c++)
                {
                    Cell(page, (fields[c].Value(s, first + r) ?? "").Trim(), x, _colW[c], y, _rowH, _cols[c].Align, false, null);
                    x += _colW[c];
                }
            }

            // Grid
            page.Line(left, top, right, top, GridPt, Grid);
            for (int r = 0; r <= count; r++)
            {
                float y = top + _headH + r * _rowH;
                page.Line(left, y, right, y, GridPt, Grid);
            }
            x = left;
            page.Line(x, top, x, bottom, GridPt, Grid);
            foreach (float w in _colW)
            {
                x += w;
                page.Line(x, top, x, bottom, GridPt, Grid);
            }
            return bottom;
        }

        /// <summary>Text in a table cell, shortened with "…" to fit, vertically centred.</summary>
        private void Cell(SheetPage page, string text, float x, float w, float top, float h, ListAlign align, bool bold, Color? back)
        {
            if (string.IsNullOrEmpty(text)) return;
            string fit = MixedText.Fit(text, w - 2 * CellPad, _bodyPt, bold, _fonts);
            float tw = align == ListAlign.Left ? 0f : MixedText.Width(fit, _bodyPt, bold, _fonts);
            float tx = align == ListAlign.Center ? x + (w - tw) / 2f
                : align == ListAlign.Right ? x + w - CellPad - tw
                : x + CellPad;
            float baseline = top + h / 2f + 0.3f * _bodyPt * MixedText.MmPerPt;   // optical middle of mixed text
            page.Words(fit, tx, baseline, _bodyPt, Ink, bold, _fonts).Back = back;
        }

        /// <summary>True when the text has no Khmer (or other non-Latin) characters.</summary>
        internal static bool IsLatin(string s)
        {
            foreach (char c in s ?? "")
                if (c > 0x2FFF || (c >= 0x1780 && c <= 0x19FF)) return false;   // CJK or Khmer
            return true;
        }

        private static void AddCard(SheetPage page, RectangleF box, Student s, bool front, bool marks)
        {
            page.Cards.Add(new SheetCard { Box = box, Student = s, Front = front });
            if (!marks)
            {
                page.Outlines.Add(box);
                return;
            }
            const float w = 0.3f;
            Color c = Color.Black;
            foreach (float x in new[] { box.Left, box.Right })
            {
                float dir = x == box.Left ? -1f : 1f;
                foreach (float y in new[] { box.Top, box.Bottom })
                {
                    float dy = y == box.Top ? -1f : 1f;
                    page.Line(x + dir * MarkOffset, y, x + dir * (MarkOffset + MarkLength), y, w, c);      // horizontal
                    page.Line(x, y + dy * MarkOffset, x, y + dy * (MarkOffset + MarkLength), w, c);      // vertical
                }
            }
        }

        private static string Caption(Student s)
        {
            string name = !string.IsNullOrWhiteSpace(s.NameEn) ? s.NameEn.Trim() : (s.NameKh ?? "").Trim();
            string id = (s.StudentId ?? "").Trim();
            return id.Length == 0 ? name : name.Length == 0 ? id : id + "  ·  " + name;
        }

        // ===================== Card images =====================

        /// <summary>Small copy of a card side for the screen (rendered once per student while it stays near).</summary>
        private Image PreviewImage(SheetCard card)
        {
            if (!card.Front) return _backSmall;
            Bitmap small;
            if (_frontCache.TryGetValue(card.Student, out small)) return small;
            try
            {
                using (Bitmap full = CardRenderer.RenderFront(card.Student, _settings))
                    small = Shrink(full);
            }
            catch (Exception ex)
            {
                _status.Text = Lang.F("Preview error: {0}", ex.Message);
                small = null;
            }
            _frontCache[card.Student] = small;
            return small;
        }

        /// <summary>Drops cached fronts that are not on the page shown.</summary>
        private void TrimCache()
        {
            if (_frontCache.Count <= 32) return;
            var keep = new HashSet<Student>(_canvas.Page == null ? new Student[0] : _canvas.Page.Cards.Select(c => c.Student));
            foreach (Student s in _frontCache.Keys.Where(k => !keep.Contains(k)).ToList())
            {
                if (_frontCache[s] != null) _frontCache[s].Dispose();
                _frontCache.Remove(s);
            }
        }

        private static Bitmap Shrink(Image full)
        {
            if (full == null) return null;
            int w = Math.Min(PreviewWidth, full.Width);
            int h = Math.Max(1, (int)Math.Round(full.Height * (double)w / full.Width));
            var small = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(full, new Rectangle(0, 0, w, h));
            }
            return small;
        }

        // ===================== Export =====================

        private void ExportPdf()
        {
            if (_pages.Count == 0 || Students.Count == 0) return;

            string path;
            using (var dlg = new SaveFileDialog
            {
                Title = Lang.T("Export PDF"),
                Filter = Lang.T("PDF document") + " (*.pdf)|*.pdf",
                FileName = DefaultFileName(),
                DefaultExt = "pdf",
                AddExtension = true,
                OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }

            string temp = path + ".tmp";
            string before = _status.Text;
            Cursor = Cursors.WaitCursor;
            _btnExport.Enabled = false;
            try
            {
                using (var pdf = new PdfWriter(temp, ListReport ? Lang.T("Student list") : Lang.T("Student ID cards") + " — " + _designName))
                {
                    int backId = -1;
                    for (int p = 0; p < _pages.Count; p++)
                    {
                        _status.Text = Lang.F("Exporting page {0} of {1}…", p + 1, _pages.Count);
                        _status.Refresh();

                        SheetPage page = _pages[p];
                        pdf.BeginPage(page.Width, page.Height);
                        foreach (SheetFill f in page.Fills) pdf.FillRectangle(f.Box, f.Color);
                        foreach (SheetCard card in page.Cards)
                        {
                            int id;
                            if (card.Front)
                            {
                                using (Bitmap front = CardRenderer.RenderFront(card.Student, _settings))
                                    id = pdf.AddImage(front);
                            }
                            else
                            {
                                if (backId < 0)
                                {
                                    if (_backFull == null) _backFull = CardRenderer.RenderBack(_settings);
                                    backId = pdf.AddImage(_backFull);
                                }
                                id = backId;
                            }
                            pdf.DrawImage(id, card.Box);
                        }
                        // Text before lines: Khmer text is a picture on a white box, which must not hide the grid
                        foreach (SheetText t in page.Texts)
                        {
                            if (!t.Serif)
                            {
                                pdf.DrawText(t.Text, t.X, t.Y, t.SizePt, t.Color, t.Bold, CardFonts.UI, t.Back);
                                continue;
                            }
                            foreach (MixedText.Piece piece in MixedText.Layout(t.Text, t.SizePt, t.Bold, t.Fonts))
                                pdf.DrawTextOnBaseline(piece.Text, t.X + piece.X, t.Y, t.SizePt, t.Color, t.Bold, !piece.Khmer,
                                    piece.Khmer ? t.Fonts.Khmer : t.Fonts.Latin, t.Back);
                        }
                        foreach (RectangleF box in page.Outlines) pdf.DrawRectangle(box, 0.25f, Hairline);
                        foreach (SheetLine l in page.Lines) pdf.DrawLine(l.X1, l.Y1, l.X2, l.Y2, l.WidthPt, l.Color);
                        pdf.EndPage();
                    }
                    pdf.Finish();
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
                _status.Text = before;
                AppDialog.Show(this, Lang.T("Could not export the PDF:") + "\n\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
                _btnExport.Enabled = true;
            }

            _status.Text = before;
            ExportedPath = path;
            if (AppDialog.Show(this, Lang.T("PDF saved:") + "\n" + path + "\n\n" + Lang.T("Open it now?"), Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { Process.Start(path); }
                catch (Exception ex) { AppDialog.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        /// <summary>Path of the last PDF exported from this window (null = none).</summary>
        public string ExportedPath { get; private set; }

        private string DefaultFileName()
        {
            List<Student> students = Students;
            string who = students.Count == 1 && !_sample ? (students[0].StudentId ?? "").Trim() : students.Count + " students";
            string name = (ListReport ? "NPIC student list" : "NPIC cards - " + _designName) + (who.Length > 0 ? " - " + who : "") + " - " +
                          DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".pdf";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        // ===================== Keys / helpers =====================

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.PageDown: TurnPage(1); return true;
                case Keys.PageUp: TurnPage(-1); return true;
                case Keys.Home: if (_txtPreparedBy.Focused) break; TurnPage(-_page); return true;
                case Keys.End: if (_txtPreparedBy.Focused) break; TurnPage(_pages.Count); return true;
                case Keys.Control | Keys.S: if (_btnExport.Enabled) ExportPdf(); return true;
                case Keys.Control | Keys.D:
                    if (!ListReport) break;
                    EditDesign(false);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static Label BarLabel(string text)
        {
            return new Label
            {
                Text = text, AutoSize = true, ForeColor = Theme.TextMuted, BackColor = Theme.Surface,
                Font = Theme.Small, Margin = new Padding(0, 11, 6, 0)
            };
        }

        private static CheckBox Check(string text, bool on)
        {
            return new CheckBox
            {
                Text = text, AutoSize = true, Checked = on, ForeColor = Theme.TextMain, BackColor = Theme.Surface,
                Margin = new Padding(10, 9, 0, 0)
            };
        }

        private static Student SampleStudent()
        {
            return new Student
            {
                StudentId = "NPIC24001", NameKh = "សុខ សុភា", NameEn = "Sok Sophea", Gender = "F",
                DateOfBirth = "15/03/2005", Faculty = "បច្ចេកវិទ្យាព័ត៌មាន", Level = "បរិញ្ញាបត្របច្ចេកវិទ្យា",
                Generation = "17", ValidUntil = "30/12/2027"
            };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            string name = _txtPreparedBy.Text.Trim();
            if (name != (_settings.ListPreparedBy ?? ""))
            {
                _settings.ListPreparedBy = name;
                try { _settings.Save(); }
                catch (Exception) { }   // only a convenience: the name is typed again next time
            }
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _typingTimer.Dispose();
                if (_designMenu != null) _designMenu.Dispose();
                foreach (Bitmap b in _frontCache.Values) if (b != null) b.Dispose();
                _frontCache.Clear();
                if (_backFull != null) _backFull.Dispose();
                if (_backSmall != null) _backSmall.Dispose();
                _backFull = _backSmall = null;
            }
            base.Dispose(disposing);
        }

        // ===================== Page model =====================

        /// <summary>One page of the preview / PDF, in millimetres from the top-left corner.</summary>
        internal sealed class SheetPage
        {
            public readonly float Width, Height;
            public readonly List<SheetCard> Cards = new List<SheetCard>();
            public readonly List<RectangleF> Outlines = new List<RectangleF>();
            public readonly List<SheetLine> Lines = new List<SheetLine>();
            public readonly List<SheetText> Texts = new List<SheetText>();
            public readonly List<SheetFill> Fills = new List<SheetFill>();

            public SheetPage(float width, float height) { Width = width; Height = height; }

            public SheetText Text(string text, float x, float y, float sizePt, Color color, bool bold)
            {
                var t = new SheetText { Text = text, X = x, Y = y, SizePt = sizePt, Color = color, Bold = bold };
                Texts.Add(t);
                return t;
            }

            /// <summary>
            /// Mixed Khmer / Latin text, each piece in its font of <paramref name="fonts"/> (see <see cref="MixedText"/>),
            /// standing on <paramref name="baseline"/>.
            /// </summary>
            public SheetText Words(string text, float x, float baseline, float sizePt, Color color, bool bold, MixedText.FontPair fonts)
            {
                var t = new SheetText
                {
                    Text = text, X = x, Y = baseline, SizePt = sizePt, Color = color, Bold = bold, Serif = true, Fonts = fonts
                };
                Texts.Add(t);
                return t;
            }

            public void Line(float x1, float y1, float x2, float y2, float widthPt, Color color)
            {
                Lines.Add(new SheetLine { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, WidthPt = widthPt, Color = color });
            }
        }

        internal sealed class SheetCard { public RectangleF Box; public Student Student; public bool Front; }
        internal sealed class SheetFill { public RectangleF Box; public Color Color; }
        internal sealed class SheetLine { public float X1, Y1, X2, Y2, WidthPt; public Color Color; }
        internal sealed class SheetText
        {
            public string Text;
            public float X, Y, SizePt;
            public Color Color;
            public bool Bold;
            public Color? Back;   // colour under the text (null = white paper)
            public bool Serif;    // SheetPage.Words: Latin + Khmer fonts of Fonts, and Y is the BASELINE
            public MixedText.FontPair Fonts;
        }

        /// <summary>Draws a <see cref="SheetPage"/> fitted in the control: grey desk, white page with a soft shadow.</summary>
        internal sealed class SheetCanvas : Control
        {
            private SheetPage _page;

            public Func<SheetCard, Image> CardImage;
            public string EmptyText;

            public SheetCanvas()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                BackColor = Theme.AppBack;
            }

            public SheetPage Page
            {
                get { return _page; }
                set { _page = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                if (_page == null)
                {
                    TextRenderer.DrawText(g, EmptyText ?? "", Theme.Small, ClientRectangle, Theme.TextMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    return;
                }

                const int pad = 24;
                float avW = Math.Max(10, Width - 2 * pad), avH = Math.Max(10, Height - 2 * pad);
                float scale = Math.Min(avW / _page.Width, avH / _page.Height);   // px per mm
                float pw = _page.Width * scale, ph = _page.Height * scale;
                var paper = new RectangleF((Width - pw) / 2f, (Height - ph) / 2f, pw, ph);

                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                    g.FillRectangle(shadow, paper.X + 3, paper.Y + 4, paper.Width, paper.Height);
                g.FillRectangle(Brushes.White, paper);

                Func<float, float> X = mm => paper.X + mm * scale;
                Func<float, float> Y = mm => paper.Y + mm * scale;
                float ptPx = scale * 25.4f / 72f;   // px per point

                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                foreach (SheetFill f in _page.Fills)
                    using (var b = new SolidBrush(f.Color))
                        g.FillRectangle(b, X(f.Box.X), Y(f.Box.Y), f.Box.Width * scale, f.Box.Height * scale);
                foreach (SheetCard c in _page.Cards)
                {
                    var box = new RectangleF(X(c.Box.X), Y(c.Box.Y), c.Box.Width * scale, c.Box.Height * scale);
                    Image img = CardImage != null ? CardImage(c) : null;
                    if (img != null) g.DrawImage(img, box);
                    else using (var b = new SolidBrush(Color.FromArgb(238, 238, 242))) g.FillRectangle(b, box);
                }
                // Same order as the PDF: text, then outlines and lines on top
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                foreach (SheetText t in _page.Texts)
                {
                    float px = t.SizePt * ptPx;
                    if (px < 3f) continue;   // too small to read: skip rather than smear
                    if (t.Serif)
                    {
                        DrawWords(g, t, X, Y, px);
                        continue;
                    }
                    bool latin = IsLatin(t.Text);
                    using (var font = new Font(latin ? "Arial" : CardFonts.UI, px, t.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
                    using (var brush = new SolidBrush(t.Color))
                    {
                        float top = t.Y;
                        if (!latin)
                        {
                            // Same vertical placement as the PDF, which draws such text as a picture
                            float hMm = g.MeasureString(t.Text, font, PointF.Empty, StringFormat.GenericTypographic).Height / scale;
                            top = PdfWriter.TextImageTop(t.Y, t.SizePt, hMm);
                        }
                        g.DrawString(t.Text, font, brush, X(t.X), Y(top), StringFormat.GenericTypographic);
                    }
                }

                foreach (RectangleF o in _page.Outlines)
                    using (var pen = new Pen(Hairline, Math.Max(1f, 0.25f * ptPx)))
                        g.DrawRectangle(pen, X(o.X), Y(o.Y), o.Width * scale, o.Height * scale);
                foreach (SheetLine l in _page.Lines)
                    using (var pen = new Pen(l.Color, Math.Max(1f, l.WidthPt * ptPx)))
                        g.DrawLine(pen, X(l.X1), Y(l.Y1), X(l.X2), Y(l.Y2));
            }

            /// <summary>Mixed text placed piece by piece exactly as in the PDF (each piece on the shared baseline).</summary>
            private static void DrawWords(Graphics g, SheetText t, Func<float, float> X, Func<float, float> Y, float px)
            {
                using (var brush = new SolidBrush(t.Color))
                {
                    foreach (MixedText.Piece piece in MixedText.Layout(t.Text, t.SizePt, t.Bold, t.Fonts))
                    {
                        string family = piece.Khmer ? t.Fonts.Khmer : t.Fonts.Latin;
                        float top = t.Y - MixedText.AscentEm(family) * t.SizePt * MixedText.MmPerPt;
                        using (var font = new Font(family, px, t.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
                            g.DrawString(piece.Text, font, brush, X(t.X + piece.X), Y(top), StringFormat.GenericTypographic);
                    }
                }
            }


            protected override void OnMouseDown(MouseEventArgs e)
            {
                Focus();
                base.OnMouseDown(e);
            }
        }
    }
}
