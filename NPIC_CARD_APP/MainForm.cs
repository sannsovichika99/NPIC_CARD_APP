using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Printing;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    public class MainForm : Form
    {
        private readonly AppSettings _settings;
        private List<Student> _students = new List<Student>();
        private bool _loading;

        private SplitContainer _split;
        private DataGridView _grid;
        private PictureBox _picFront, _picBack;
        private ComboBox _cboPrinter;
        private CheckBox _chkAllPrinters;
        private NumericUpDown _numS1X, _numS1Y, _numS2X, _numS2Y;
        private ComboBox _cboSlots, _cboOrientation;
        private CheckBox _chkRotateBack, _chkRotateFront;
        private Label _lblSlotHint;
        private readonly ToolTip _tip = new ToolTip();
        private RadioButton _rdoFront, _rdoBack;
        private Label _lblPage, _lblStatus;

        public MainForm()
        {
            _settings = AppSettings.Load();

            Text = "NPIC Card App — Epson L8050";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1280, 800);
            MinimumSize = new Size(1000, 650);
            Font = new Font("Segoe UI", 9f);

            BuildUi();
            LoadPrinters();
            ApplySettingsToUi();

            if (File.Exists(_settings.LastCsvPath)) LoadCsv(_settings.LastCsvPath);
            UpdatePageInfo();

            string[] missing = AssetStore.MissingFiles();
            if (missing.Length > 0)
                SetStatus("Missing in " + AssetStore.Folder + ": " + string.Join(", ", missing));
            UpdatePreview();
            UpdateSlotHint();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _split.SplitterDistance = (int)(ClientSize.Width * 0.5);
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
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 44, Padding = new Padding(6, 6, 6, 0), WrapContents = false
            };
            toolbar.Controls.Add(MakeButton("Import CSV...", (s, e) => ImportCsv()));
            toolbar.Controls.Add(MakeButton("Photo Folder...", (s, e) => ChoosePhotoFolder()));
            toolbar.Controls.Add(MakeButton("Signature...", (s, e) => ChooseSignature()));
            toolbar.Controls.Add(MakeButton("Card Text...", (s, e) => EditCardText()));
            toolbar.Controls.Add(MakeButton("Full Screen (F11)", (s, e) => ShowFullScreen(PreviewSide.Both)));
            toolbar.Controls.Add(new Label { Text = "Printer:", AutoSize = true, Margin = new Padding(16, 9, 4, 0) });
            _cboPrinter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Margin = new Padding(0, 5, 4, 0) };
            _cboPrinter.SelectedIndexChanged += (s, e) => OnPrinterChanged();
            toolbar.Controls.Add(_cboPrinter);
            _chkAllPrinters = new CheckBox { Text = "Show all", AutoSize = true, Margin = new Padding(0, 9, 8, 0) };
            _chkAllPrinters.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                _settings.ShowAllPrinters = _chkAllPrinters.Checked;
                LoadPrinters();
                SaveSettings(false);
                UpdatePageInfo();
            };
            toolbar.Controls.Add(_chkAllPrinters);
            toolbar.Controls.Add(MakeButton("Driver Setup...", (s, e) => DriverSetup()));
            toolbar.Controls.Add(MakeButton("Check Printer", (s, e) => CheckPrinter()));
            toolbar.Controls.Add(MakeButton("Test Print", (s, e) => TestPrint()));

            _lblStatus = new Label
            {
                Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0), BackColor = Color.FromArgb(240, 240, 240), Text = "Ready."
            };

            _split = new SplitContainer { Dock = DockStyle.Fill };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                BackgroundColor = SystemColors.Window,
                RowHeadersWidth = 28
            };
            _grid.DefaultCellStyle.Font = new Font(CardRenderer.KhmerFont, 9f);
            _grid.SelectionChanged += (s, e) => { UpdatePreview(); UpdateSlotHint(); };
            _split.Panel1.Controls.Add(_grid);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(6) };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _picFront = MakePreviewBox();
            _picBack = MakePreviewBox();
            _picFront.DoubleClick += (s, e) => ShowFullScreen(PreviewSide.Front);
            _picBack.DoubleClick += (s, e) => ShowFullScreen(PreviewSide.Back);
            right.Controls.Add(WrapInGroup("Front — ព័ត៌មាននិស្សិត", _picFront), 0, 0);
            right.Controls.Add(WrapInGroup("Back — ព័ត៌មានវិទ្យាស្ថាន", _picBack), 0, 1);
            right.Controls.Add(BuildPrintPanel(), 0, 2);

            _lblPage = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 3) };
            right.Controls.Add(_lblPage, 0, 3);
            _split.Panel2.Controls.Add(right);

            Controls.Add(_split);
            Controls.Add(toolbar);
            Controls.Add(_lblStatus);
        }

        private Control BuildPrintPanel()
        {
            var group = new GroupBox
            {
                Text = "Print", Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8)
            };
            var rows = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false
            };

            _numS1X = MakeNumber(); _numS1Y = MakeNumber();
            _numS2X = MakeNumber(); _numS2Y = MakeNumber();
            foreach (NumericUpDown n in new[] { _numS1X, _numS1Y, _numS2X, _numS2Y })
                n.ValueChanged += (s, e) => UpdateSlotHint();
            rows.Controls.Add(Row(MakeLabel("Slot 1   X (mm):"), _numS1X, MakeLabel("Y (mm):"), _numS1Y));
            rows.Controls.Add(Row(MakeLabel("Slot 2   X (mm):"), _numS2X, MakeLabel("Y (mm):"), _numS2Y));

            // Order must match the SlotMode enum: Auto, One, Two
            _cboSlots = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Margin = new Padding(3, 4, 6, 3) };
            _cboSlots.Items.AddRange(new object[] { "Auto", "1", "2" });
            _cboSlots.SelectedIndexChanged += (s, e) => UpdateSlotHint();
            _tip.SetToolTip(_cboSlots,
                "Auto = 2 cards per tray load when 2 or more students are selected and slot 2 is set up; " +
                "otherwise slot 1 only.\n1 = always slot 1 only.   2 = always slot 1 + slot 2.");
            _lblSlotHint = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 8, 12, 0) };
            _chkRotateBack = new CheckBox { Text = "Rotate back side 180°", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            _chkRotateBack.CheckedChanged += (s, e) => { _settings.RotateBack180 = _chkRotateBack.Checked; };
            rows.Controls.Add(Row(MakeLabel("Slots per tray load:"), _cboSlots, _lblSlotHint));
            _chkRotateFront = new CheckBox { Text = "Rotate front side 180°", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            _chkRotateFront.CheckedChanged += (s, e) => { _settings.RotateFront180 = _chkRotateFront.Checked; };
            rows.Controls.Add(Row(_chkRotateFront, _chkRotateBack));

            // Order must match the CardOrientation enum: Horizontal, VerticalRight, VerticalLeft
            _cboOrientation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(3, 4, 6, 3) };
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
            rows.Controls.Add(Row(MakeLabel("Card on tray:"), _cboOrientation));

            _rdoFront = new RadioButton { Text = "Front", AutoSize = true, Checked = true, Margin = new Padding(3, 7, 3, 3) };
            _rdoBack = new RadioButton { Text = "Back", AutoSize = true, Margin = new Padding(3, 7, 16, 3) };
            rows.Controls.Add(Row(MakeLabel("Side:"), _rdoFront, _rdoBack,
                MakeButton("Print Preview", (s, e) => ShowPrintPreview()),
                MakeButton("Print Selected", (s, e) => PrintSelected()),
                MakeButton("Print Queue...", (s, e) => ShowPrintQueue()),
                MakeButton("Save Settings", (s, e) => SaveSettings(true))));

            group.Controls.Add(rows);
            return group;
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 28, Margin = new Padding(3) };
            b.Click += onClick;
            return b;
        }

        private static Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 8, 3, 0) };
        }

        private static NumericUpDown MakeNumber()
        {
            return new NumericUpDown
            {
                DecimalPlaces = 1, Increment = 0.1M, Minimum = -50M, Maximum = 300M, Width = 70, Margin = new Padding(3, 4, 12, 3)
            };
        }

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            row.Controls.AddRange(controls);
            return row;
        }

        private static PictureBox MakePreviewBox()
        {
            return new PictureBox
            {
                Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(225, 225, 225),
                Cursor = Cursors.Hand   // double-click → full screen
            };
        }

        private static GroupBox WrapInGroup(string title, Control inner)
        {
            var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6) };
            group.Controls.Add(inner);
            return group;
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
            try { _lblPage.Text = new CardPrinter(_settings).DescribePage(); }
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
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadCsv(dlg.FileName);
            }
        }

        private void LoadCsv(string path)
        {
            try
            {
                _students = CsvStudentLoader.Load(path);
                _grid.DataSource = null;
                _grid.DataSource = _students;
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
                    Font = new Font(CardRenderer.KhmerFont, 9f)
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
            if (keyData == Keys.F11)
            {
                ShowFullScreen(PreviewSide.Both);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowFullScreen(PreviewSide side)
        {
            int index = _grid.CurrentRow != null ? _grid.CurrentRow.Index : 0;
            using (var form = new CardPreviewForm(_students, index, _settings, side))
            {
                form.ShowDialog(this);
                if (_students.Count > 0 && form.CurrentIndex != index)
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
        private void SetStatus(string text) { _lblStatus.Text = text; }

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
