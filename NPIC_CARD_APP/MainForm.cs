using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Printing;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    public class MainForm : Form
    {
        // ===================== Theme =====================
        // Accent = the navy printed on the card itself, so the app and the card read as one brand.
        private static readonly Color Accent        = Color.FromArgb(46, 49, 146);
        private static readonly Color AccentHover   = Color.FromArgb(64, 68, 176);
        private static readonly Color AccentPressed = Color.FromArgb(34, 36, 112);
        private static readonly Color AccentSoft    = Color.FromArgb(232, 233, 248);
        private static readonly Color AccentOnDark  = Color.FromArgb(70, 74, 168);
        private static readonly Color AccentEdge    = Color.FromArgb(98, 102, 186);
        private static readonly Color AppBack       = Color.FromArgb(243, 244, 247);
        private static readonly Color Surface       = Color.White;
        private static readonly Color SurfaceAlt    = Color.FromArgb(249, 250, 252);
        private static readonly Color SurfaceHover  = Color.FromArgb(242, 243, 248);
        private static readonly Color Border        = Color.FromArgb(222, 224, 231);
        private static readonly Color GridLine      = Color.FromArgb(236, 237, 242);
        private static readonly Color TextMain      = Color.FromArgb(28, 30, 40);
        private static readonly Color TextMuted     = Color.FromArgb(102, 106, 122);
        private static readonly Color PreviewBack   = Color.FromArgb(232, 234, 240);
        private static readonly Color Warning       = Color.FromArgb(180, 98, 0);
        private static readonly Color Danger        = Color.FromArgb(185, 28, 28);
        private static readonly Color Success       = Color.FromArgb(21, 128, 61);

        private static readonly Font UiFont      = new Font(CardFonts.UI, 9f);
        private static readonly Font SemiFont    = new Font(CardFonts.UI, 9f, FontStyle.Bold);
        private static readonly Font SectionFont = new Font(CardFonts.UI, 10f, FontStyle.Bold);
        private static readonly Font TitleFont   = new Font(CardFonts.UI, 14f, FontStyle.Bold);
        private static readonly Font CaptionFont = new Font(CardFonts.UI, 8.25f);

        private const int FieldLabelWidth = 96;

        private enum ButtonKind { Primary, Secondary, Ghost, OnAccent }

        private static readonly Dictionary<string, string> ColumnTitles = new Dictionary<string, string>
        {
            { "StudentId", "ID" }, { "NameKh", "Name (KH)" }, { "NameEn", "Name (EN)" }, { "Gender", "Sex" },
            { "DateOfBirth", "Date of birth" }, { "Faculty", "Faculty" }, { "Level", "Level" },
            { "Generation", "Gen." }, { "ValidUntil", "Valid until" }, { "QrData", "QR data" }
        };

        // ===================== State =====================
        private readonly AppSettings _settings;
        private List<Student> _students = new List<Student>();
        private List<Student> _view = new List<Student>();   // _students filtered by the search box = what the grid shows
        private bool _loading;

        private SplitContainer _split;
        private DataGridView _grid;
        private TextBox _txtSearch;
        private Label _lblCount, _lblEmpty;
        private PictureBox _picFront, _picBack;
        private ComboBox _cboPrinter;
        private CheckBox _chkAllPrinters;
        private NumericUpDown _numS1X, _numS1Y, _numS2X, _numS2Y;
        private ComboBox _cboSlots, _cboOrientation;
        private CheckBox _chkRotateBack, _chkRotateFront;
        private Label _lblSlotHint;
        private readonly ToolTip _tip = new ToolTip();
        private RadioButton _rdoFront, _rdoBack;
        private Label _lblPage, _lblStatus, _lblStatusDot;

        public MainForm()
        {
            _settings = AppSettings.Load();

            Text = "NPIC Card App — Epson L8050";
            StartPosition = FormStartPosition.CenterScreen;
            // Never open larger than the screen (laptops at 125 % scaling only have ~1536 x 824 to work with).
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1320, work.Width), Math.Min(840, work.Height));
            MinimumSize = new Size(Math.Min(1100, work.Width), Math.Min(700, work.Height));
            Font = UiFont;
            BackColor = AppBack;
            ForeColor = TextMain;

            BuildUi();
            LoadPrinters();
            ApplySettingsToUi();

            if (File.Exists(_settings.LastCsvPath)) LoadStudents(_settings.LastCsvPath);
            UpdatePageInfo();
            UpdateEmptyState();
            UpdateCount();

            string[] missing = AssetStore.MissingFiles();
            if (missing.Length > 0)
                SetStatus("Missing in " + AssetStore.Folder + ": " + string.Join(", ", missing));
            UpdatePreview();
            UpdateSlotHint();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                _split.SplitterDistance = (int)(ClientSize.Width * 0.40);
                _split.Panel1MinSize = 320;
                _split.Panel2MinSize = 660;
            }
            catch (InvalidOperationException) { /* window too small for the min sizes — keep the defaults */ }
            CardFonts.WarnIfMissing(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings(false);
            ReplaceImage(_picFront, null);
            ReplaceImage(_picBack, null);
            base.OnFormClosing(e);
        }

        // ===================== UI =====================
        private void BuildUi()
        {
            SuspendLayout();

            _split = new SplitContainer { Dock = DockStyle.Fill, BackColor = AppBack, SplitterWidth = 10 };
            _split.Panel1.Padding = new Padding(14, 14, 0, 14);
            _split.Panel2.Padding = new Padding(0, 14, 14, 14);
            _split.Panel1.Controls.Add(BuildStudentPanel());
            _split.Panel2.Controls.Add(BuildRightPanel());

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(_split);
            Controls.Add(BuildStatusBar());
            Controls.Add(BuildHeader());

            ResumeLayout(true);
        }

        /// <summary>Navy app bar: title on the left, data commands on the right.</summary>
        private Control BuildHeader()
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 64, BackColor = Accent, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(20, 0, 14, 0), Margin = new Padding(0)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var brand = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false,
                Anchor = AnchorStyles.Left, Margin = new Padding(0), BackColor = Accent
            };
            brand.Controls.Add(new Label
            {
                Text = "NPIC Card Studio", Font = TitleFont, ForeColor = Color.White, AutoSize = true, Margin = new Padding(0)
            });
            brand.Controls.Add(new Label
            {
                Text = "Student ID cards  ·  Epson L8050 card tray", Font = CaptionFont,
                ForeColor = Color.FromArgb(196, 199, 238), AutoSize = true, Margin = new Padding(2, 0, 0, 0)
            });

            var actions = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false,
                Anchor = AnchorStyles.Right, Margin = new Padding(0), BackColor = Accent
            };
            actions.Controls.Add(MakeButton("Import API…", ButtonKind.OnAccent, (s, e) => ImportApi(), "Download students from the school's web API (Ctrl+I)"));
            actions.Controls.Add(MakeButton("Import Excel…", ButtonKind.OnAccent, (s, e) => ImportExcel(), "Load students from an Excel workbook (.xlsx) (Ctrl+E)"));
            actions.Controls.Add(MakeButton("Import CSV…", ButtonKind.OnAccent, (s, e) => ImportCsv(), "Load students from a UTF-8 CSV file (Ctrl+O)"));
            actions.Controls.Add(MakeButton("Photo Folder…", ButtonKind.OnAccent, (s, e) => ChoosePhotoFolder(), "Folder with photos named {StudentId}.jpg / .png"));
            actions.Controls.Add(MakeToolStrip(MakeToolButton("Remove Background…", (s, e) => RemovePhotoBackground(),
                "Crop the selected student's photo and remove the wall / screen behind it (Ctrl+B)")));
            actions.Controls.Add(MakeButton("Signature…", ButtonKind.OnAccent, (s, e) => ChooseSignature(), "Director signature image (white background is removed)"));
            actions.Controls.Add(MakeButton("Card Text…", ButtonKind.OnAccent, (s, e) => EditCardText(), "Edit the fixed text printed on the cards"));
            actions.Controls.Add(new Panel { Width = 1, Height = 24, BackColor = AccentEdge, Margin = new Padding(10, 6, 4, 0) });
            actions.Controls.Add(MakeButton("Full Screen", ButtonKind.OnAccent, (s, e) => ShowFullScreen(PreviewSide.Both), "Full-screen card preview (F11)"));

            header.Controls.Add(brand, 0, 0);
            header.Controls.Add(actions, 1, 0);
            return header;
        }

        /// <summary>Left side: search box + student list.</summary>
        private Control BuildStudentPanel()
        {
            _lblCount = new Label { AutoSize = true, ForeColor = TextMuted, Font = CaptionFont, Margin = new Padding(0, 10, 4, 0) };

            _txtSearch = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 10) };
            _txtSearch.HandleCreated += (s, e) => SetCueBanner(_txtSearch, "Search by ID, name or faculty…   (Ctrl+F)");
            _txtSearch.TextChanged += (s, e) => ApplyFilter();
            _txtSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { _txtSearch.Clear(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down && _grid.Rows.Count > 0) { _grid.Focus(); e.SuppressKeyPress = true; }
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                BackgroundColor = Surface,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = GridLine,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 34
            };
            _grid.RowTemplate.Height = 32;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.Font = SemiFont;
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _grid.DefaultCellStyle.Font = new Font(CardFonts.UI, 9f);
            _grid.DefaultCellStyle.BackColor = Surface;
            _grid.DefaultCellStyle.ForeColor = TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            _grid.DefaultCellStyle.SelectionForeColor = Accent;
            _grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            _grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceAlt;
            _grid.DataBindingComplete += (s, e) => ApplyColumnTitles();
            _grid.SelectionChanged += (s, e) => { UpdatePreview(); UpdateSlotHint(); UpdateCount(); };
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ShowFullScreen(PreviewSide.Both); };

            _lblEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted,
                BackColor = Surface, Visible = false
            };

            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(0) };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(_lblEmpty);
            _lblEmpty.BringToFront();

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            body.Controls.Add(_txtSearch, 0, 0);
            body.Controls.Add(gridHost, 0, 1);

            return MakeCard("Students", body, _lblCount,
                MakeButton("Excel Template", ButtonKind.Ghost, (s, e) => SaveExcelTemplate(),
                    "Save an empty Excel file with the right columns to fill in, then use Import Excel…"));
        }

        /// <summary>Right side: card previews, printer + tray settings, print actions.</summary>
        private Control BuildRightPanel()
        {
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 268f));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));

            // ---- Previews ----
            _picFront = MakePreviewBox();
            _picBack = MakePreviewBox();
            _picFront.DoubleClick += (s, e) => ShowFullScreen(PreviewSide.Front);
            _picBack.DoubleClick += (s, e) => ShowFullScreen(PreviewSide.Back);
            _tip.SetToolTip(_picFront, "Double-click to enlarge");
            _tip.SetToolTip(_picBack, "Double-click to enlarge");

            var previews = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 12) };
            previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            previews.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Control front = MakeCard("Front", _picFront,
                KhmerCaption("ព័ត៌មាននិស្សិត"),
                MakeButton("Enlarge", ButtonKind.Ghost, (s, e) => ShowFullScreen(PreviewSide.Front)));
            Control back = MakeCard("Back", _picBack,
                KhmerCaption("ព័ត៌មានវិទ្យាស្ថាន"),
                MakeButton("Enlarge", ButtonKind.Ghost, (s, e) => ShowFullScreen(PreviewSide.Back)));
            front.Margin = new Padding(0, 0, 6, 0);
            back.Margin = new Padding(6, 0, 0, 0);
            previews.Controls.Add(front, 0, 0);
            previews.Controls.Add(back, 1, 0);

            // ---- Settings ----
            var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 12) };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Control printer = BuildPrinterCard();
            Control tray = BuildTrayCard();
            printer.Margin = new Padding(0, 0, 6, 0);
            tray.Margin = new Padding(6, 0, 0, 0);
            settings.Controls.Add(printer, 0, 0);
            settings.Controls.Add(tray, 1, 0);

            right.Controls.Add(previews, 0, 0);
            right.Controls.Add(settings, 0, 1);
            right.Controls.Add(BuildActionBar(), 0, 2);
            return right;
        }

        private Control BuildPrinterCard()
        {
            _cboPrinter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
            _cboPrinter.SelectedIndexChanged += (s, e) => OnPrinterChanged();

            _chkAllPrinters = new CheckBox { Text = "Show all", AutoSize = true, ForeColor = TextMuted, Margin = new Padding(0, 9, 4, 0) };
            _tip.SetToolTip(_chkAllPrinters, "List every installed printer, not only card printers");
            _chkAllPrinters.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                _settings.ShowAllPrinters = _chkAllPrinters.Checked;
                LoadPrinters();
                SaveSettings(false);
                UpdatePageInfo();
            };

            _lblPage = new Label
            {
                Dock = DockStyle.Fill, ForeColor = TextMuted, Font = CaptionFont, AutoEllipsis = true,
                Margin = new Padding(1, 2, 0, 4)
            };

            var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            buttons.Controls.Add(FillButton(MakeButton("Driver Setup…", ButtonKind.Secondary, (s, e) => DriverSetup(), "Paper size, tray and quality in the Epson driver")), 0, 0);
            buttons.Controls.Add(FillButton(MakeButton("Check Printer", ButtonKind.Secondary, (s, e) => CheckPrinter(), "Show the page setup and any problems found")), 1, 0);
            buttons.Controls.Add(FillButton(MakeButton("Test Print", ButtonKind.Secondary, (s, e) => TestPrint(), "Print a calibration card to tune slot X / Y")), 0, 1);
            buttons.Controls.Add(FillButton(MakeButton("Print Queue…", ButtonKind.Secondary, (s, e) => ShowPrintQueue(), "See or cancel jobs waiting in Windows")), 1, 1);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 84f));
            body.Controls.Add(_cboPrinter, 0, 0);
            body.Controls.Add(_lblPage, 0, 1);
            body.Controls.Add(buttons, 0, 2);

            return MakeCard("Printer", body, _chkAllPrinters);
        }

        private Control BuildTrayCard()
        {
            var rows = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, Margin = new Padding(0)
            };

            _numS1X = MakeNumber(); _numS1Y = MakeNumber();
            _numS2X = MakeNumber(); _numS2Y = MakeNumber();
            foreach (NumericUpDown n in new[] { _numS1X, _numS1Y, _numS2X, _numS2Y })
                n.ValueChanged += (s, e) => UpdateSlotHint();
            rows.Controls.Add(Row(FieldLabel("Slot 1"), Unit("X"), _numS1X, Unit("Y"), _numS1Y, Unit("mm")));
            rows.Controls.Add(Row(FieldLabel("Slot 2"), Unit("X"), _numS2X, Unit("Y"), _numS2Y, Unit("mm")));

            // Order must match the SlotMode enum: Auto, One, Two
            _cboSlots = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 76, Margin = new Padding(0, 3, 10, 3) };
            _cboSlots.Items.AddRange(new object[] { "Auto", "1", "2" });
            _cboSlots.SelectedIndexChanged += (s, e) => UpdateSlotHint();
            _tip.SetToolTip(_cboSlots,
                "Auto = 2 cards per tray load when 2 or more students are selected and slot 2 is set up; " +
                "otherwise slot 1 only.\n1 = always slot 1 only.   2 = always slot 1 + slot 2.");
            _lblSlotHint = new Label { AutoSize = true, ForeColor = TextMuted, Font = CaptionFont, Margin = new Padding(0, 7, 0, 0) };
            rows.Controls.Add(Row(FieldLabel("Per tray load"), _cboSlots, _lblSlotHint));

            // Order must match the CardOrientation enum: Horizontal, VerticalRight, VerticalLeft
            _cboOrientation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Margin = new Padding(0, 3, 6, 3) };
            _cboOrientation.Items.AddRange(new object[] { "Horizontal", "Vertical (turn right 90°)", "Vertical (turn left 90°)" });
            _cboOrientation.SelectedIndexChanged += (s, e) =>
            {
                if (_cboOrientation.SelectedIndex >= 0) _settings.CardOrientation = (CardOrientation)_cboOrientation.SelectedIndex;
                UpdateSlotHint();
            };
            _tip.SetToolTip(_cboOrientation,
                "How the card lies in the tray pocket.\n" +
                "Vertical = the long side of the card runs in the feed direction (54 x 85.6 mm on the page).\n" +
                "If the card comes out upside down, pick the other Vertical option.");
            rows.Controls.Add(Row(FieldLabel("Card on tray"), _cboOrientation));

            _chkRotateFront = new CheckBox { Text = "Front", AutoSize = true, Margin = new Padding(0, 6, 14, 3) };
            _chkRotateFront.CheckedChanged += (s, e) => { _settings.RotateFront180 = _chkRotateFront.Checked; };
            _chkRotateBack = new CheckBox { Text = "Back", AutoSize = true, Margin = new Padding(0, 6, 3, 3) };
            _chkRotateBack.CheckedChanged += (s, e) => { _settings.RotateBack180 = _chkRotateBack.Checked; };
            _tip.SetToolTip(_chkRotateFront, "Rotate the front side 180° when printing");
            _tip.SetToolTip(_chkRotateBack, "Rotate the back side 180° when printing");
            rows.Controls.Add(Row(FieldLabel("Rotate 180°"), _chkRotateFront, _chkRotateBack));

            return MakeCard("Tray layout", rows,
                MakeButton("Save", ButtonKind.Ghost, (s, e) => SaveSettings(true), "Save slot positions and print options"));
        }

        /// <summary>Bottom bar: choose the side, then preview or print.</summary>
        private Control BuildActionBar()
        {
            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Surface,
                Padding = new Padding(14, 0, 12, 0), Margin = new Padding(0)
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            AttachBorder(bar);

            _rdoFront = MakeSegment("Front", true);
            _rdoBack = MakeSegment("Back", false);
            var side = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0), BackColor = Surface };
            side.Controls.Add(new Label { Text = "Print side", AutoSize = true, Font = SemiFont, Margin = new Padding(0, 10, 10, 0) });
            side.Controls.Add(_rdoFront);
            side.Controls.Add(_rdoBack);

            // RightToLeft flow: the first control added ends up right-most.
            var actions = new FlowLayoutPanel
            {
                AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft,
                Anchor = AnchorStyles.Right, Margin = new Padding(0), BackColor = Surface
            };
            actions.Controls.Add(MakeButton("Print Selected", ButtonKind.Primary, (s, e) => PrintSelected(), "Print the selected students, tray load by tray load (Ctrl+P)"));
            actions.Controls.Add(MakeButton("Print Preview", ButtonKind.Secondary, (s, e) => ShowPrintPreview(), "Show exactly what will be sent to the printer"));

            bar.Controls.Add(side, 0, 0);
            bar.Controls.Add(actions, 1, 0);
            return bar;
        }

        private Control BuildStatusBar()
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Surface, Padding = new Padding(14, 0, 14, 0) };
            bar.Paint += (s, e) =>
            {
                using (var pen = new Pen(Border)) e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0);
            };
            _lblStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = TextMuted, Text = "Ready.", AutoEllipsis = true };
            _lblStatusDot = new Label { Dock = DockStyle.Left, Width = 16, Text = "●", ForeColor = Success, TextAlign = ContentAlignment.MiddleLeft, Font = CaptionFont };
            bar.Controls.Add(_lblStatus);
            bar.Controls.Add(_lblStatusDot);
            return bar;
        }

        // ---------- Building blocks ----------
        /// <summary>White surface with a 1 px border and a header row (title left, extras right).</summary>
        private static Control MakeCard(string title, Control body, params Control[] headerExtras)
        {
            var card = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(1), Margin = new Padding(0) };
            AttachBorder(card);

            var head = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Surface, Padding = new Padding(14, 0, 10, 0) };
            var titleLabel = new Label
            {
                Text = title, Font = SectionFont, ForeColor = TextMain, Dock = DockStyle.Left, AutoSize = true,
                Padding = new Padding(0, 12, 0, 0)
            };
            var extras = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
                BackColor = Surface, Padding = new Padding(0, 5, 0, 0)
            };
            foreach (Control c in headerExtras) extras.Controls.Add(c);
            head.Controls.Add(extras);
            head.Controls.Add(titleLabel);

            var bodyHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(14, 0, 14, 12) };
            body.Dock = DockStyle.Fill;
            bodyHost.Controls.Add(body);

            card.Controls.Add(bodyHost);
            card.Controls.Add(head);
            return card;
        }

        private static void AttachBorder(Control c)
        {
            c.Paint += (s, e) =>
            {
                using (var pen = new Pen(Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
            };
            c.Resize += (s, e) => c.Invalidate();
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            return MakeButton(text, ButtonKind.Secondary, onClick);
        }

        private Button MakeButton(string text, ButtonKind kind, EventHandler onClick, string tooltip)
        {
            Button b = MakeButton(text, kind, onClick);
            _tip.SetToolTip(b, tooltip);
            return b;
        }

        private static Button MakeButton(string text, ButtonKind kind, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, MinimumSize = new Size(0, 34), Padding = new Padding(10, 0, 10, 0),
                Margin = new Padding(6, 3, 0, 3), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand, Font = SemiFont
            };
            switch (kind)
            {
                case ButtonKind.Primary:
                    b.BackColor = Accent; b.ForeColor = Color.White;
                    b.FlatAppearance.BorderSize = 0;
                    b.FlatAppearance.MouseOverBackColor = AccentHover;
                    b.FlatAppearance.MouseDownBackColor = AccentPressed;
                    b.MinimumSize = new Size(132, 36);
                    break;
                case ButtonKind.Secondary:
                    b.BackColor = Surface; b.ForeColor = TextMain;
                    b.FlatAppearance.BorderColor = Border;
                    b.FlatAppearance.MouseOverBackColor = SurfaceHover;
                    b.FlatAppearance.MouseDownBackColor = AccentSoft;
                    b.MinimumSize = new Size(0, 36);
                    break;
                case ButtonKind.Ghost:
                    b.BackColor = Surface; b.ForeColor = Accent;
                    b.FlatAppearance.BorderSize = 0;
                    b.FlatAppearance.MouseOverBackColor = AccentSoft;
                    b.FlatAppearance.MouseDownBackColor = AccentSoft;
                    b.MinimumSize = new Size(0, 28);
                    b.Padding = new Padding(6, 0, 6, 0);
                    b.Margin = new Padding(2, 2, 0, 2);
                    break;
                case ButtonKind.OnAccent:
                    b.BackColor = Accent; b.ForeColor = Color.White;
                    b.FlatAppearance.BorderColor = AccentEdge;
                    b.FlatAppearance.MouseOverBackColor = AccentOnDark;
                    b.FlatAppearance.MouseDownBackColor = AccentPressed;
                    b.Padding = new Padding(7, 0, 7, 0);   // the header holds many buttons: keep them compact
                    break;
            }
            b.Click += onClick;
            return b;
        }

        /// <summary>Tool strip that sits in the navy header and looks like the OnAccent buttons next to it.</summary>
        private static ToolStrip MakeToolStrip(params ToolStripItem[] items)
        {
            var strip = new ToolStrip
            {
                Dock = DockStyle.None, AutoSize = true, GripStyle = ToolStripGripStyle.Hidden,
                BackColor = Accent, ForeColor = Color.White, Font = SemiFont, ShowItemToolTips = true,
                Padding = new Padding(0), Margin = new Padding(6, 3, 0, 3),
                Renderer = new HeaderStripRenderer(), CanOverflow = false
            };
            strip.Items.AddRange(items);
            return strip;
        }

        private static ToolStripButton MakeToolButton(string text, EventHandler onClick, string tooltip)
        {
            var b = new ToolStripButton(text)
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text, ForeColor = Color.White, Font = SemiFont,
                AutoSize = true, Padding = new Padding(7, 5, 7, 5), Margin = new Padding(0), ToolTipText = tooltip
            };
            b.Click += onClick;
            return b;
        }

        /// <summary>Flat navy look for header tool strips: same fill, hover, pressed and edge colours as OnAccent buttons.</summary>
        private sealed class HeaderStripRenderer : ToolStripProfessionalRenderer
        {
            public HeaderStripRenderer() { RoundedEdges = false; }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var brush = new SolidBrush(Accent)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { /* no border line */ }

            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            {
                var bounds = new Rectangle(Point.Empty, e.Item.Size);
                Color fill = e.Item.Pressed ? AccentPressed : e.Item.Selected ? AccentOnDark : Accent;
                using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, bounds);
                using (var pen = new Pen(AccentEdge)) e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = Color.White;
                base.OnRenderItemText(e);
            }
        }

        private static Button FillButton(Button b)
        {
            b.AutoSize = false;
            b.MinimumSize = Size.Empty;
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(0, 3, 6, 3);
            return b;
        }

        /// <summary>Radio button drawn as one half of a segmented toggle.</summary>
        private static RadioButton MakeSegment(string text, bool isChecked)
        {
            var r = new RadioButton
            {
                Text = text, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, Checked = isChecked,
                Size = new Size(80, 34), TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 3, 0, 3),
                Cursor = Cursors.Hand, Font = SemiFont, UseVisualStyleBackColor = false
            };
            r.FlatAppearance.BorderColor = Border;
            r.FlatAppearance.CheckedBackColor = Accent;
            EventHandler restyle = (s, e) =>
            {
                r.ForeColor = r.Checked ? Color.White : TextMain;
                r.BackColor = r.Checked ? Accent : Surface;
                r.FlatAppearance.MouseOverBackColor = r.Checked ? AccentHover : SurfaceHover;
            };
            r.CheckedChanged += restyle;
            restyle(r, EventArgs.Empty);
            return r;
        }

        private static Label FieldLabel(string text)
        {
            return new Label
            {
                Text = text, AutoSize = false, Width = FieldLabelWidth, Height = 30, ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 4, 0)
            };
        }

        private static Label Unit(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = TextMuted, Font = CaptionFont, Margin = new Padding(0, 8, 4, 0) };
        }

        private static Label KhmerCaption(string text)
        {
            return new Label
            {
                Text = text, AutoSize = true, ForeColor = TextMuted, Font = new Font(CardFonts.UI, 8.25f),
                Margin = new Padding(0, 8, 8, 0)
            };
        }

        private static NumericUpDown MakeNumber()
        {
            return new NumericUpDown
            {
                DecimalPlaces = 1, Increment = 0.1M, Minimum = -50M, Maximum = 300M, Width = 72,
                TextAlign = HorizontalAlignment.Right, Margin = new Padding(0, 4, 10, 3)
            };
        }

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 2) };
            row.Controls.AddRange(controls);
            return row;
        }

        private static PictureBox MakePreviewBox()
        {
            return new PictureBox
            {
                Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = PreviewBack,
                Padding = new Padding(14), Cursor = Cursors.Hand   // double-click → full screen
            };
        }

        // ---------- Search / list helpers ----------
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void SetCueBanner(TextBox box, string text)
        {
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }

        /// <summary>Rebinds the grid to the students that match the search box.</summary>
        private void ApplyFilter()
        {
            string q = (_txtSearch.Text ?? "").Trim();
            _view = q.Length == 0 ? _students : _students.Where(s => Matches(s, q)).ToList();
            _grid.DataSource = null;
            _grid.DataSource = _view;
            UpdateEmptyState();
            UpdateCount();
            UpdatePreview();
            UpdateSlotHint();
        }

        private static bool Matches(Student s, string q)
        {
            return Has(s.StudentId, q) || Has(s.NameKh, q) || Has(s.NameEn, q) || Has(s.Faculty, q);
        }

        private static bool Has(string value, string q)
        {
            return value != null && value.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyColumnTitles()
        {
            foreach (DataGridViewColumn col in _grid.Columns)
            {
                string title;
                if (ColumnTitles.TryGetValue(col.DataPropertyName, out title)) col.HeaderText = title;
            }
        }

        private void UpdateEmptyState()
        {
            if (_lblEmpty == null) return;
            _lblEmpty.Text = _students.Count == 0
                ? "No students loaded yet.\n\nClick  Import API…,  Import Excel…  or  Import CSV…  in the top bar to begin.\n" +
                  "No list yet? Click  Excel Template  above for a file to fill in."
                : "No students match “" + _txtSearch.Text.Trim() + "”.\n\nPress Esc to clear the search.";
            _lblEmpty.Visible = _view.Count == 0;
        }

        private void UpdateCount()
        {
            if (_lblCount == null) return;
            int selected = _grid.SelectedRows.Count;
            string total = _view.Count == _students.Count
                ? string.Format("{0} students", _students.Count)
                : string.Format("{0} of {1} students", _view.Count, _students.Count);
            _lblCount.Text = selected > 1 ? total + "  ·  " + selected + " selected" : total;
        }

        // ===================== Settings =====================
        /// <summary>Fills the printer list with card printers only (or every printer when "Show all" is ticked).</summary>
        private void LoadPrinters()
        {
            _loading = true;
            _chkAllPrinters.Checked = _settings.ShowAllPrinters;
            _cboPrinter.Items.Clear();
            foreach (string name in PrinterSettings.InstalledPrinters)
            {
                if (_settings.ShowAllPrinters || _settings.IsCardPrinter(name))
                    _cboPrinter.Items.Add(name);
            }

            string target = _settings.PrinterName;
            if (string.IsNullOrEmpty(target) || !_cboPrinter.Items.Contains(target))
            {
                List<string> names = _cboPrinter.Items.Cast<string>().ToList();
                target = names.FirstOrDefault(_settings.IsCardPrinter) ?? names.FirstOrDefault();
            }
            if (target != null) _cboPrinter.SelectedItem = target;
            _cboPrinter.Enabled = _cboPrinter.Items.Count > 0;
            _loading = false;

            if ((_settings.PrinterName ?? "") != (target ?? ""))
            {
                _settings.PrinterName = target ?? "";
                _settings.DevModeBase64 = ""; // driver settings belonged to another printer
            }

            if (target == null)
                SetStatus("No card printer found (" + _settings.CardPrinterKeywords +
                          "). Install the Epson L8050 driver, or tick \"Show all\".");
        }

        private void OnPrinterChanged()
        {
            if (_loading) return;
            string name = _cboPrinter.SelectedItem as string;
            if (name == null || name == _settings.PrinterName) return;

            _settings.PrinterName = name;
            _settings.DevModeBase64 = "";
            SaveSettings(false);
            UpdatePageInfo();
        }

        private void ApplySettingsToUi()
        {
            _loading = true;   // the controls below must not write back into _settings half-way
            _numS1X.Value = Clamp(_numS1X, _settings.Slot1X);
            _numS1Y.Value = Clamp(_numS1Y, _settings.Slot1Y);
            _numS2X.Value = Clamp(_numS2X, _settings.Slot2X);
            _numS2Y.Value = Clamp(_numS2Y, _settings.Slot2Y);
            _cboSlots.SelectedIndex = (int)_settings.SlotMode;
            _chkRotateBack.Checked = _settings.RotateBack180;
            _chkRotateFront.Checked = _settings.RotateFront180;
            _cboOrientation.SelectedIndex = (int)_settings.CardOrientation;
            _loading = false;
        }

        private static decimal Clamp(NumericUpDown box, float value)
        {
            decimal v = (decimal)value;
            if (v < box.Minimum) return box.Minimum;
            if (v > box.Maximum) return box.Maximum;
            return v;
        }

        private void ReadUiToSettings()
        {
            _settings.Slot1X = (float)_numS1X.Value;
            _settings.Slot1Y = (float)_numS1Y.Value;
            _settings.Slot2X = (float)_numS2X.Value;
            _settings.Slot2Y = (float)_numS2Y.Value;
            if (_cboSlots.SelectedIndex >= 0) _settings.SlotMode = (SlotMode)_cboSlots.SelectedIndex;
            _settings.RotateBack180 = _chkRotateBack.Checked;
            _settings.RotateFront180 = _chkRotateFront.Checked;
            if (_cboOrientation.SelectedIndex >= 0) _settings.CardOrientation = (CardOrientation)_cboOrientation.SelectedIndex;
        }

        private void SaveSettings(bool notify)
        {
            ReadUiToSettings();
            try
            {
                _settings.Save();
                if (notify) SetStatus("Settings saved.");
            }
            catch (Exception ex)
            {
                if (notify) ShowError(ex);
            }
        }

        private void UpdatePageInfo()
        {
            if (string.IsNullOrEmpty(_settings.PrinterName)) { _lblPage.Text = "No card printer selected."; return; }
            try { _lblPage.Text = new CardPrinter(_settings).DescribePage().Replace("  |  ", Environment.NewLine); }
            catch (Exception ex) { _lblPage.Text = ex.Message; }
            UpdateSlotHint();   // the page size decides whether slot 2 fits
        }

        /// <summary>Shows next to the Slots box how the selected cards will be split into tray loads.</summary>
        private void UpdateSlotHint()
        {
            if (_loading || _lblSlotHint == null || _cboSlots.SelectedIndex < 0) return;
            ReadUiToSettings();

            int count = SelectedStudents().Count;
            string reason;
            int perLoad = new CardPrinter(_settings).CardsPerLoad(count, out reason);
            int loads = (count + perLoad - 1) / perLoad;

            string text = count == 0 ? "" : string.Format("{0} card(s) → {1} tray load(s)", count, loads);
            if (_settings.SlotMode == SlotMode.Auto && count >= 2)
                text += perLoad == 2 ? ", 2 per load" : ", slot 1 only — slot 2 not usable";
            _lblSlotHint.Text = text;
            _lblSlotHint.ForeColor = reason != null ? Color.DarkOrange : Color.DimGray;
            _tip.SetToolTip(_lblSlotHint, reason ?? "");
        }

        // ===================== Data =====================
        private void ImportCsv()
        {
            using (var dlg = new OpenFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", Title = "Import students" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadStudents(dlg.FileName);
            }
        }

        private void ImportExcel()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "Excel workbook (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Excel 97-2003 (*.xls)|*.xls|All files (*.*)|*.*",
                Title = "Import students from Excel"
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadStudents(dlg.FileName);
            }
        }

        /// <summary>Asks for the API address and token, downloads the students and shows them in the list.</summary>
        private void ImportApi()
        {
            List<Student> loaded = null;
            string url = null;

            using (var dlg = new Form
            {
                Text = "Import students from API",
                ClientSize = new Size(560, 300),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                BackColor = Surface,
                Font = UiFont,
                Padding = new Padding(16)
            })
            {
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, BackColor = Surface };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                var txtUrl = new TextBox { Dock = DockStyle.Fill, Text = _settings.ApiUrl ?? "", Margin = new Padding(0, 4, 0, 4) };
                txtUrl.HandleCreated += (s, e) => SetCueBanner(txtUrl, "https://example.edu.kh/api/students");

                string savedToken = _settings.ApiToken;
                var txtToken = new TextBox { Dock = DockStyle.Fill, Text = savedToken, UseSystemPasswordChar = true, Margin = new Padding(0, 4, 0, 4) };
                txtToken.HandleCreated += (s, e) => SetCueBanner(txtToken, "Optional — leave empty if the API is public");

                var chkShow = new CheckBox { Text = "Show token", AutoSize = true, Margin = new Padding(0, 2, 16, 0) };
                chkShow.CheckedChanged += (s, e) => txtToken.UseSystemPasswordChar = !chkShow.Checked;
                var chkRemember = new CheckBox
                {
                    Text = "Remember token on this PC (encrypted for this Windows user)",
                    AutoSize = true, Checked = savedToken.Length > 0, Margin = new Padding(0, 2, 0, 0)
                };
                var options = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
                options.Controls.Add(chkShow);
                options.Controls.Add(chkRemember);

                var hint = new Label
                {
                    AutoSize = true, MaximumSize = new Size(410, 0), ForeColor = TextMuted, Font = CaptionFont,
                    Margin = new Padding(0, 8, 0, 0),
                    Text = "The API must return a JSON list of students (or CSV) using the CSV column names: StudentId, " +
                           "NameKh, NameEn, Gender, DateOfBirth, Faculty, Level, Generation, ValidUntil, QrData.\n" +
                           "The token is sent as “Authorization: Bearer …”. Type “Header-Name: value” to use another header."
                };
                var lblBusy = new Label { AutoSize = true, ForeColor = TextMuted, Margin = new Padding(0, 10, 0, 0) };

                layout.Controls.Add(FieldLabel("API address"), 0, 0);
                layout.Controls.Add(txtUrl, 1, 0);
                layout.Controls.Add(FieldLabel("Access token"), 0, 1);
                layout.Controls.Add(txtToken, 1, 1);
                layout.Controls.Add(options, 1, 2);
                layout.Controls.Add(hint, 1, 3);
                layout.Controls.Add(lblBusy, 1, 4);

                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, BackColor = Surface
                };
                Button btnCancel = MakeButton("Cancel", ButtonKind.Secondary, null);
                Button btnLoad = MakeButton("Load Students", ButtonKind.Primary, null);
                buttons.Controls.Add(btnCancel);
                buttons.Controls.Add(btnLoad);

                dlg.Controls.Add(layout);
                dlg.Controls.Add(buttons);
                dlg.AcceptButton = btnLoad;
                dlg.CancelButton = btnCancel;

                CancellationTokenSource cts = null;
                bool closeWhenDone = false;
                Action<bool> setBusy = busy =>
                {
                    txtUrl.Enabled = txtToken.Enabled = options.Enabled = btnLoad.Enabled = !busy;
                    dlg.UseWaitCursor = busy;
                    lblBusy.Text = busy ? "Downloading students…  (Cancel stops the download)" : "";
                };

                btnLoad.Click += async (s, e) =>
                {
                    if (cts != null) return;
                    cts = new CancellationTokenSource();
                    setBusy(true);
                    try
                    {
                        loaded = await ApiStudentLoader.LoadAsync(txtUrl.Text, txtToken.Text, cts.Token);
                        url = txtUrl.Text.Trim();
                        _settings.ApiUrl = url;
                        _settings.ApiToken = chkRemember.Checked ? txtToken.Text.Trim() : "";
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancel pressed or dialog closed: nothing to report.
                    }
                    catch (Exception ex)
                    {
                        if (!closeWhenDone) ShowError(ex, dlg);
                    }
                    finally
                    {
                        cts.Dispose();
                        cts = null;
                        setBusy(false);
                    }

                    if (loaded != null) dlg.DialogResult = DialogResult.OK;
                    else if (closeWhenDone) dlg.DialogResult = DialogResult.Cancel;
                    else txtUrl.Focus();
                };

                btnCancel.Click += (s, e) =>
                {
                    if (cts != null) cts.Cancel();               // stop the download, keep the dialog open
                    else dlg.DialogResult = DialogResult.Cancel;
                };

                dlg.FormClosing += (s, e) =>
                {
                    if (cts == null) return;
                    // Closed (X / Alt+F4) while downloading: cancel first; the Load handler then closes the dialog.
                    e.Cancel = true;
                    closeWhenDone = true;
                    cts.Cancel();
                };

                if (dlg.ShowDialog(this) != DialogResult.OK || loaded == null) return;
            }

            _students = loaded;
            ApplyFilter();
            _settings.LastCsvPath = "";   // so the next start doesn't reload an older file over this list
            SaveSettings(false);

            Uri uri;
            string source = Uri.TryCreate(url, UriKind.Absolute, out uri) ? uri.Host : url;
            SetStatus(_students.Count == 0
                ? "Missing data: the API returned no students (" + source + ")"
                : string.Format("Loaded {0} student(s) from {1}", _students.Count, source));
        }

        private void SaveExcelTemplate()
        {
            string path;
            using (var dlg = new SaveFileDialog
            {
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                Title = "Save Excel template",
                FileName = "students-template.xlsx",
                OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }

            try
            {
                ExcelTemplateWriter.Write(path);
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "Could not save the template:\n\n" + ex.Message +
                    "\n\nIf the file is open in Excel, close it and try again.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            catch (Exception ex)
            {
                ShowError(ex);
                return;
            }

            SetStatus("Excel template saved: " + path);
            if (MessageBox.Show(this,
                    "Template saved.\n\nFill in the Students sheet (one student per row), save it, then click Import Excel…\n\nOpen it now?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { System.Diagnostics.Process.Start(path); }
                catch (Exception ex) { ShowError(ex); }   // e.g. no program associated with .xlsx
            }
        }

        /// <summary>Loads a student list from .xlsx or .csv (chosen by extension) and remembers it for next start.</summary>
        private void LoadStudents(string path)
        {
            try
            {
                _students = ExcelStudentLoader.IsExcelFile(path)
                    ? ExcelStudentLoader.Load(path)
                    : CsvStudentLoader.Load(path);
                ApplyFilter();
                _settings.LastCsvPath = path;
                SaveSettings(false);
                SetStatus(string.Format("Loaded {0} student(s) from {1}", _students.Count, Path.GetFileName(path)));
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void ChoosePhotoFolder()
        {
            using (var dlg = new FolderBrowserDialog
            {
                Description = "Folder with photos named {StudentId}.jpg / .png",
                SelectedPath = _settings.PhotoFolder
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _settings.PhotoFolder = dlg.SelectedPath;
                SaveSettings(false);
                UpdatePreview();
                SetStatus("Photo folder: " + dlg.SelectedPath);
            }
        }

        private void ChooseSignature()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg)|*.png;*.jpg;*.jpeg",
                Title = "Director signature (white background is removed automatically)"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _settings.SignaturePath = dlg.FileName;
                SaveSettings(false);
                UpdatePreview();
            }
        }

        /// <summary>Opens the background-removal dialog for the current student's photo.</summary>
        private void RemovePhotoBackground()
        {
            Student s = CurrentStudent();
            if (s == null)
            {
                MessageBox.Show(this, "Select a student in the list first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (string.IsNullOrEmpty(_settings.PhotoFolder) || !Directory.Exists(_settings.PhotoFolder))
            {
                MessageBox.Show(this, "Choose the photo folder first (Photo Folder… in the top bar).", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (CardRenderer.FindPhoto(_settings.PhotoFolder, s.StudentId) == null &&
                BackgroundRemovalForm.FindOriginal(_settings.PhotoFolder, s.StudentId) == null)
            {
                MessageBox.Show(this, "No photo found for " + Describe(s) + ".\n\nExpected " + s.StudentId +
                    ".jpg / .png in:\n" + _settings.PhotoFolder, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            BackgroundRemovalForm dlg;
            Cursor = Cursors.WaitCursor;
            try { dlg = new BackgroundRemovalForm(_settings.PhotoFolder, s.StudentId); }
            catch (Exception ex) { ShowError(ex); return; }
            finally { Cursor = Cursors.Default; }

            using (dlg)
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                UpdatePreview();
                SetStatus(dlg.Restored
                    ? "Original photo restored: " + Path.GetFileName(dlg.SavedPath)
                    : "Background removed: " + Path.GetFileName(dlg.SavedPath) + "  (original kept in _original)");
            }
        }

        private void EditCardText()
        {
            using (var dlg = new Form
            {
                Text = "Card Text",
                Size = new Size(720, 480),
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false
            })
            {
                var grid = new PropertyGrid
                {
                    Dock = DockStyle.Fill,
                    SelectedObject = _settings.CardText,
                    PropertySort = PropertySort.Categorized,
                    ToolbarVisible = false,
                    Font = new Font(CardFonts.UI, 9f)
                };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 34 };
                dlg.Controls.Add(grid);
                dlg.Controls.Add(ok);
                dlg.AcceptButton = ok;
                dlg.ShowDialog(this);
            }
            SaveSettings(false);
            UpdatePreview();
        }

        private Student CurrentStudent()
        {
            return _grid.CurrentRow == null ? null : _grid.CurrentRow.DataBoundItem as Student;
        }

        private List<Student> SelectedStudents()
        {
            return _grid.SelectedRows.Cast<DataGridViewRow>()
                .OrderBy(r => r.Index)
                .Select(r => r.DataBoundItem as Student)
                .Where(s => s != null)
                .ToList();
        }

        // ===================== Preview =====================
        private void UpdatePreview()
        {
            Student s = CurrentStudent();
            try
            {
                ReplaceImage(_picFront, s == null ? null : CardRenderer.RenderFront(s, _settings));
                ReplaceImage(_picBack, CardRenderer.RenderBack(_settings));
            }
            catch (Exception ex)
            {
                SetStatus("Preview error: " + ex.Message);
            }
        }

        private static void ReplaceImage(PictureBox box, Image image)
        {
            Image old = box.Image;
            box.Image = image;
            if (old != null) old.Dispose();
        }

        private Bitmap RenderSide(Student s, bool front)
        {
            // Turned to match the tray pocket (180° flip and/or 90° for a vertical card)
            return front
                ? CardPrinter.TrayImage(CardRenderer.RenderFront(s, _settings), _settings, _settings.RotateFront180)
                : CardPrinter.TrayImage(CardRenderer.RenderBack(_settings), _settings, _settings.RotateBack180);
        }

        // ===================== Full-screen preview =====================
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F11: ShowFullScreen(PreviewSide.Both); return true;
                case Keys.Control | Keys.F: _txtSearch.Focus(); _txtSearch.SelectAll(); return true;
                case Keys.Control | Keys.O: ImportCsv(); return true;
                case Keys.Control | Keys.E: ImportExcel(); return true;
                case Keys.Control | Keys.I: ImportApi(); return true;
                case Keys.Control | Keys.P: PrintSelected(); return true;
                case Keys.Control | Keys.B: RemovePhotoBackground(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowFullScreen(PreviewSide side)
        {
            int index = _grid.CurrentRow != null ? _grid.CurrentRow.Index : 0;
            using (var form = new CardPreviewForm(_view, index, _settings, side))
            {
                form.ShowDialog(this);
                if (_view.Count > 0 && form.CurrentIndex != index)
                    SelectRow(form.CurrentIndex);
            }
        }

        /// <summary>Selects a grid row (this also refreshes the small preview).</summary>
        private void SelectRow(int index)
        {
            if (index < 0 || index >= _grid.Rows.Count) return;
            DataGridViewCell cell = _grid.Rows[index].Cells.Cast<DataGridViewCell>().FirstOrDefault(c => c.Visible);
            if (cell == null) return;
            _grid.ClearSelection();
            _grid.CurrentCell = cell;
            _grid.Rows[index].Selected = true;
        }

        // ===================== Printing =====================
        /// <summary>True when the selected printer is a card printer (or the user confirms another one).</summary>
        private bool EnsureCardPrinter()
        {
            if (string.IsNullOrEmpty(_settings.PrinterName))
            {
                MessageBox.Show(this, "No card printer selected.\n\nInstall the Epson L8050 driver, then choose it in the Printer list.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (_settings.IsCardPrinter(_settings.PrinterName)) return true;

            return MessageBox.Show(this,
                "\"" + _settings.PrinterName + "\" is not a card printer.\n\nPrint to it anyway?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(282, 253);
            this.Name = "MainForm";
            this.ResumeLayout(false);

        }

        private void DriverSetup()
        {
            if (!EnsureCardPrinter()) return;
            try
            {
                PrinterSettings ps = new CardPrinter(_settings).CreatePrinterSettings();
                byte[] devMode = PrinterDevMode.ShowDriverDialog(Handle, ps);
                if (devMode == null) return; // cancelled

                _settings.DevModeBase64 = Convert.ToBase64String(devMode);
                SaveSettings(false);
                UpdatePageInfo();
                SetStatus("Driver settings saved.");
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        /// <summary>Checks tray, driver setup, offline/error state and stuck jobs before sending a job.</summary>
        private bool ConfirmPrinterReady(CardPrinter printer)
        {
            Cursor = Cursors.WaitCursor;
            List<string> problems;
            try { problems = printer.FindProblems(); }
            finally { Cursor = Cursors.Default; }

            if (problems.Count == 0) return true;

            return MessageBox.Show(this,
                "The card may not print:\n\n• " + string.Join("\n\n• ", problems) + "\n\nPrint anyway?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        /// <summary>Toolbar "Check Printer": shows the page setup and every problem found.</summary>
        private void CheckPrinter()
        {
            if (string.IsNullOrEmpty(_settings.PrinterName))
            {
                MessageBox.Show(this, "No card printer selected.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var printer = new CardPrinter(_settings);
            Cursor = Cursors.WaitCursor;
            string page;
            List<string> problems;
            try
            {
                try { page = printer.DescribePage(); }
                catch (Exception ex) { page = ex.Message; }
                problems = printer.FindProblems();
            }
            finally { Cursor = Cursors.Default; }

            string slotReason;
            int perLoad = printer.CardsPerLoad(2, out slotReason);
            string slots = "Slots per tray load: " + _cboSlots.Text +
                (_settings.SlotMode == SlotMode.Auto
                    ? " (" + (perLoad == 2 ? "2 cards when 2+ are selected" : "slot 1 only — " + slotReason) + ")"
                    : "");

            string report = "Printer: " + _settings.PrinterName + "\n" + page.Replace("  |  ", "\n") + "\n" + slots + "\n\n" +
                (problems.Count == 0 ? "No problems found." : "Problems:\n• " + string.Join("\n\n• ", problems));
            MessageBox.Show(this, report, "Check Printer", MessageBoxButtons.OK,
                problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void TestPrint()
        {
            if (!EnsureCardPrinter()) return;
            SaveSettings(false);
            var printer = new CardPrinter(_settings);
            if (!ConfirmPrinterReady(printer)) return;

            try
            {
                using (Bitmap c1 = CardPrinter.TrayImage(
                           CardRenderer.RenderCalibration(1, _settings.Slot1X, _settings.Slot1Y), _settings, false))
                using (Bitmap c2 = _settings.UseTwoSlots
                           ? CardPrinter.TrayImage(CardRenderer.RenderCalibration(2, _settings.Slot2X, _settings.Slot2Y), _settings, false)
                           : null)
                {
                    printer.Print(c1, c2, "NPIC Card Calibration");
                }

                MessageBox.Show(this,
                    "Calibration job sent. Insert the tray when the printer light turns on.\n\n" +
                    "Red line = card edge, each black line = 1 mm.\n" +
                    "Lines cut off on the RIGHT → decrease X.   On the LEFT → increase X.\n" +
                    "Lines cut off at the BOTTOM → decrease Y.   At the TOP → increase Y.",
                    "Test Print", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        /// <summary>Shows exactly what 'Print Selected' will send: one page per tray load, same side and slots.</summary>
        private void ShowPrintPreview()
        {
            List<Student> list = SelectedStudents();
            if (list.Count == 0)
            {
                MessageBox.Show(this, "Select at least one student in the list.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!EnsureCardPrinter()) return;
            SaveSettings(false);

            bool front = _rdoFront.Checked;
            var printer = new CardPrinter(_settings);
            string slotReason;
            int perLoad = printer.CardsPerLoad(list.Count, out slotReason);
            int loads = (list.Count + perLoad - 1) / perLoad;
            if (slotReason != null) SetStatus("Auto slots: slot 1 only. " + slotReason);

            try
            {
                using (PrintDocument doc = printer.CreateDocument("NPIC Card Preview", loads, (g, page, index) =>
                {
                    Student a = list[index * perLoad];
                    Student b = (perLoad == 2 && index * perLoad + 1 < list.Count) ? list[index * perLoad + 1] : null;
                    using (Bitmap img1 = RenderSide(a, front))
                    using (Bitmap img2 = b != null ? RenderSide(b, front) : null)
                    {
                        printer.DrawLoad(g, img1, img2);
                    }
                }))
                using (var form = new CardPrintPreviewForm(doc, loads,
                           string.Format("{0} card(s), {1}", list.Count, front ? "FRONT" : "BACK"),
                           () => printer.LastJobInfo))
                {
                    form.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void PrintSelected()
        {
            List<Student> list = SelectedStudents();
            if (list.Count == 0)
            {
                MessageBox.Show(this, "Select at least one student in the list.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!EnsureCardPrinter()) return;

            SaveSettings(false);
            var printer = new CardPrinter(_settings);
            if (!ConfirmPrinterReady(printer)) return;

            bool front = _rdoFront.Checked;
            string side = front ? "FRONT" : "BACK";
            string slotReason;
            int perLoad = printer.CardsPerLoad(list.Count, out slotReason);
            int totalLoads = (list.Count + perLoad - 1) / perLoad;

            for (int i = 0, load = 1; i < list.Count; i += perLoad, load++)
            {
                Student a = list[i];
                Student b = (perLoad == 2 && i + 1 < list.Count) ? list[i + 1] : null;

                string message =
                    (load > 1 ? "Wait until the previous card is finished and the tray is ejected.\n\n" : "") +
                    (load == 1 && slotReason != null ? "Auto slots: printing 1 card per tray load.\n" + slotReason + "\n\n" : "") +
                    "Side: " + side + "\n\n" +
                    "Slot 1: " + Describe(a) + "\n" +
                    (b != null ? "Slot 2: " + Describe(b) + "\n" : "") +
                    "\nClick OK to send the job, then insert the tray when the printer light turns on.";

                DialogResult answer = MessageBox.Show(this, message,
                    string.Format("Tray load {0} of {1}", load, totalLoads),
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (answer != DialogResult.OK) { SetStatus("Printing cancelled."); return; }

                try
                {
                    using (Bitmap img1 = RenderSide(a, front))
                    using (Bitmap img2 = b != null ? RenderSide(b, front) : null)
                    {
                        printer.Print(img1, img2, "NPIC Card " + a.StudentId + " " + side);
                    }
                    SetStatus(string.Format("Sent tray load {0} of {1} ({2}).", load, totalLoads, side));
                }
                catch (Exception ex)
                {
                    ShowError(ex);
                    return;
                }
            }
            SetStatus(string.Format("Done: {0} card(s), {1} side.", list.Count, side));
        }

        /// <summary>Shows the Windows print queue of the selected printer (cancel stuck jobs).</summary>
        private void ShowPrintQueue()
        {
            if (string.IsNullOrEmpty(_settings.PrinterName))
            {
                MessageBox.Show(this, "No card printer selected.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var form = new PrintQueueForm(_settings.PrinterName))
            {
                form.ShowDialog(this);
            }
        }

        private static string Describe(Student s)
        {
            return s.StudentId + " — " + (string.IsNullOrEmpty(s.NameEn) ? s.NameKh : s.NameEn);
        }

        // ===================== Misc =====================
        private void SetStatus(string text)
        {
            _lblStatus.Text = text;
            Color tone =
                text.StartsWith("Error") || text.StartsWith("Preview error") ? Danger :
                text.StartsWith("Missing") || text.StartsWith("No card printer") || text.StartsWith("Auto slots") ||
                text.StartsWith("Printing cancelled") ? Warning :
                Success;
            _lblStatusDot.ForeColor = tone;
            _lblStatus.ForeColor = tone == Success ? TextMuted : tone;
        }

        /// <summary>Shows the whole error chain (Ctrl+C in the message box copies it).</summary>
        private void ShowError(Exception ex, IWin32Window owner = null)
        {
            var text = new System.Text.StringBuilder(ex.Message);
            for (Exception inner = ex.InnerException; inner != null; inner = inner.InnerException)
                text.Append("\n\n→ ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);
            text.Append("\n\n(Press Ctrl+C to copy this message.)");

            SetStatus("Error: " + ex.Message.Split('\n')[0]);
            MessageBox.Show(owner ?? this, text.ToString(), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
