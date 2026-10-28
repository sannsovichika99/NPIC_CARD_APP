using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "Page Design…" of a Student list design (Preview &amp; PDF → Student list, Ctrl+D), in two tabs:
    ///   - Page &amp; text: name, paper size and orientation, margins in inches, the Latin and complex-script (Khmer) fonts with
    ///     their sizes, the text above and below the table, heading on every page;
    ///   - Table: the columns left to right (what each shows, header text, relative width, alignment), header shading.
    ///     Widths are relative, so the same design fits every paper size; "≈ mm" shows the result for the chosen paper.
    /// Works on a copy: Save returns it in <see cref="Result"/>, Cancel / Esc leave the design untouched.
    /// </summary>
    public class PageDesignForm : ModalForm
    {
        private readonly ComboBox _paper, _orientation, _latin, _complex;
        private readonly NumericUpDown _top, _left, _right, _bottom, _bodySize, _headingSize;
        private readonly TextBox _name, _heading, _footer;
        private readonly AppSettings _settings;
        private readonly string _id;
        private readonly TableLayoutPanel _grid;
        private int _row;
        private readonly CheckBox _chkEveryPage, _chkShade;
        private readonly PillTabs _tabs;
        private Panel _tablePage;
        private DataGridView _cols;
        private const int ColField = 0, ColHeader = 1, ColWidth = 2, ColAlign = 3, ColMm = 4;

        /// <summary>Items of the "Shows" drop-down (public properties for data binding).</summary>
        public sealed class FieldChoice
        {
            public string Id { get; set; }
            public string Label { get; set; }
        }

        /// <summary>Items of the "Align" drop-down.</summary>
        public sealed class AlignChoice
        {
            public ListAlign Value { get; set; }
            public string Label { get; set; }
        }

        /// <summary>The edited design after Save (DialogResult.OK).</summary>
        public ListPageDesign Result { get; private set; }

        /// <param name="settings">For the names of the other designs (a name must be unique).</param>
        /// <param name="design">The saved design to change, or a new copy (<paramref name="isNew"/>).</param>
        /// <param name="isNew">True for a copy that is not saved yet (from a built-in design or "New design").</param>
        public PageDesignForm(AppSettings settings, ListPageDesign design, bool isNew)
        {
            _settings = settings;
            ListPageDesign current = design ?? new ListPageDesign();
            _id = current.Id;

            Text = "Page Design";
            Subtitle = "Student list: paper, margins, fonts and the text above and below the table";
            Glyph = Theme.IcoDocument;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(780, work.Width), Math.Min(740, work.Height));
            MinimumSize = new Size(Math.Min(600, work.Width), Math.Min(520, work.Height));

            _grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, BackColor = Theme.Surface,
                Padding = new Padding(22, 10, 22, 10)
            };
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // ---- Design name ----
            Section("Design");
            _name = new TextBox
            {
                Width = 300, Text = current.DisplayName, BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain,
                BorderStyle = BorderStyle.FixedSingle, MaxLength = 80
            };
            Row("Name", _name);
            if (isNew)
            {
                var note = new Label
                {
                    Text = Lang.T("A new design: Save adds it to My designs. The built-in designs stay as they are."),
                    AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = Theme.TextMuted, BackColor = Theme.Surface,
                    Font = Theme.Small, Margin = new Padding(0, 0, 0, 4)
                };
                _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _grid.Controls.Add(note, 1, _row++);
            }

            // ---- Paper ----
            Section("Paper");
            _paper = Combo(ListPageDesign.PaperNames, false);
            _orientation = Combo(new[] { Lang.T("Portrait"), Lang.T("Landscape") }, false);
            Row("Paper size", _paper);
            Row("Orientation", _orientation);

            // ---- Margins ----
            Section("Margins (inches)");
            _top = Number((decimal)ListPageDesign.MinMarginIn, (decimal)ListPageDesign.MaxMarginIn, 0.05m, 2);
            _left = Number((decimal)ListPageDesign.MinMarginIn, (decimal)ListPageDesign.MaxMarginIn, 0.05m, 2);
            _right = Number((decimal)ListPageDesign.MinMarginIn, (decimal)ListPageDesign.MaxMarginIn, 0.05m, 2);
            _bottom = Number((decimal)ListPageDesign.MinMarginIn, (decimal)ListPageDesign.MaxMarginIn, 0.05m, 2);
            Row("Top", Pair(_top, "Bottom", _bottom));
            Row("Left", Pair(_left, "Right", _right));

            // ---- Fonts ----
            Section("Font style");
            string[] families;
            using (var installed = new InstalledFontCollection())
                families = installed.Families.Select(f => f.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            _latin = Combo(families, true);
            // Khmer fonts first in the complex-script list
            _complex = Combo(families.Where(IsKhmerFont).Concat(families.Where(f => !IsKhmerFont(f))).ToArray(), true);
            Row("Latin", _latin);
            Row("Complex (Khmer)", _complex);
            _bodySize = Number((decimal)ListPageDesign.MinSizePt, (decimal)ListPageDesign.MaxSizePt, 0.5m, 1);
            _headingSize = Number((decimal)ListPageDesign.MinSizePt, (decimal)ListPageDesign.MaxSizePt, 0.5m, 1);
            Row("Size (pt)", Pair(_bodySize, "Heading", _headingSize));

            // ---- Text ----
            Section("Text");
            _heading = TextArea(7);
            _footer = TextArea(3);
            Row("Above the table", _heading);
            Row("Below the table", _footer);
            var hint = new Label
            {
                Text = Lang.T("Placeholders: [total]  [total male]  [total female]  [name]  [date]") + "\n" +
                       Lang.T("Heading lines are bold; a line with a placeholder is printed at the table size. A blank line adds space. [name] is the Prepared by name."),
                AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = Theme.TextMuted, BackColor = Theme.Surface,
                Font = Theme.Small, Margin = new Padding(0, 2, 0, 8)
            };
            _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _grid.Controls.Add(hint, 1, _row++);
            _chkEveryPage = new CheckBox
            {
                Text = Lang.T("Print the text above the table on every page"), AutoSize = true, ForeColor = Theme.TextMain,
                BackColor = Theme.Surface
            };
            Row("", _chkEveryPage);
            _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // filler so the last row is not stretched

            // ---- Table tab ----
            _chkShade = new CheckBox
            {
                Text = Lang.T("Grey header row"), AutoSize = true, ForeColor = Theme.TextMain, BackColor = Theme.Surface,
                Margin = new Padding(0, 6, 0, 0)
            };
            BuildTablePage();

            // ---- Tabs ----
            var tabsBar = ModalUi.Toolbar(52);
            _tabs = new PillTabs("Page & text", "Table");
            _tabs.SelectedIndexChanged += (s, e) =>
            {
                _grid.Visible = _tabs.SelectedIndex == 0;
                _tablePage.Visible = _tabs.SelectedIndex == 1;
                if (_tablePage.Visible) UpdateMm();
            };
            tabsBar.Controls.Add(_tabs);

            // ---- Footer: Reset on the left, Cancel / Save on the right ----
            var footer = ModalUi.Footer();
            Button save = ModalUi.Button("Save", PillKind.Primary, (s, e) => SaveAndClose());
            Button cancel = ModalUi.Button("Cancel", PillKind.Secondary);
            cancel.DialogResult = DialogResult.Cancel;
            Button reset = ModalUi.Button("Reset to Standard", PillKind.Ghost, (s, e) => ShowDesign(new ListPageDesign()));
            reset.Margin = new Padding(0, 0, 24, 0);
            new ToolTip().SetToolTip(reset, Lang.T("A4 portrait, 0.5\" margins, Times New Roman + Khmer OS Siemreap, the standard text"));
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            footer.Controls.Add(reset);

            Controls.Add(_grid);
            Controls.Add(_tablePage);
            Controls.Add(tabsBar);
            Controls.Add(footer);
            CancelButton = cancel;   // no AcceptButton: Enter makes a new line in the text boxes

            ShowDesign(current);
        }

        /// <summary>Shows a design in the controls.</summary>
        private void ShowDesign(ListPageDesign d)
        {
            _paper.SelectedItem = d.PaperName;
            _orientation.SelectedIndex = d.Landscape ? 1 : 0;
            float[] mm = d.MarginsMm;   // left, top, right, bottom (out-of-range values already put back to 0.5")
            _left.Value = Inches(mm[0]);
            _top.Value = Inches(mm[1]);
            _right.Value = Inches(mm[2]);
            _bottom.Value = Inches(mm[3]);
            _latin.Text = d.LatinFont ?? "";
            _complex.Text = d.ComplexFont ?? "";
            _bodySize.Value = (decimal)d.BodyPt;
            _headingSize.Value = (decimal)d.HeadingPt;
            _heading.Text = string.Join("\r\n", ListPageDesign.Lines(d.Heading));
            _footer.Text = string.Join("\r\n", ListPageDesign.Lines(d.Footer));
            _chkEveryPage.Checked = d.HeadingEveryPage;
            _chkShade.Checked = d.ShadeHeader;
            FillColumns(d.TableColumns);
        }

        private void SaveAndClose()
        {
            string name = _name.Text.Trim();
            if (name.Length == 0 || ListDesigns.UniqueName(_settings, name, _id) != name)
            {
                AppDialog.Show(this, Lang.T(name.Length == 0 ? "Give the design a name." : "Another design already has this name."),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _name.Focus();
                _name.SelectAll();
                return;
            }

            if (!_cols.EndEdit()) return;   // a width that is not a number stays in edit with its message
            List<ListColumnDesign> columns = ReadColumns();
            if (columns.Count == 0)
            {
                AppDialog.Show(this, Lang.T("The table needs at least one column."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tabs.SelectedIndex = 1;
                return;
            }

            var d = new ListPageDesign
            {
                Id = _id,
                Columns = columns,
                HeadingEveryPage = _chkEveryPage.Checked,
                ShadeHeader = _chkShade.Checked,
                Name = name,
                Paper = (string)_paper.SelectedItem ?? "A4",
                Landscape = _orientation.SelectedIndex == 1,
                MarginTop = (float)_top.Value,
                MarginLeft = (float)_left.Value,
                MarginRight = (float)_right.Value,
                MarginBottom = (float)_bottom.Value,
                LatinFont = _latin.Text.Trim(),
                ComplexFont = _complex.Text.Trim(),
                BodySize = (float)_bodySize.Value,
                HeadingSize = (float)_headingSize.Value,
                Heading = _heading.Text.Replace("\r\n", "\n"),
                Footer = _footer.Text.Replace("\r\n", "\n")
            };

            // The table needs room: at least 100 mm wide and 80 mm high inside the margins
            SizeF page = d.PageMm;
            float[] mm = d.MarginsMm;
            if (page.Width - mm[0] - mm[2] < 100f || page.Height - mm[1] - mm[3] < 80f)
            {
                AppDialog.Show(this, Lang.T("The margins leave too little room for the table. Make them smaller."), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string missing = string.Join(", ", new[] { d.LatinFont, d.ComplexFont }
                .Where(f => f.Length > 0 && !CardFonts.IsInstalled(f)).Distinct());
            if (missing.Length > 0 &&
                AppDialog.Show(this, Lang.T("This font is not installed on this PC, so a default font is used instead:") + "\n\n" +
                                     missing + "\n\n" + Lang.T("Save anyway?"), Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            Result = d;
            DialogResult = DialogResult.OK;
            Close();
        }

        // ===================== Table tab =====================

        private void BuildTablePage()
        {
            _tablePage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(22, 10, 22, 8), Visible = false };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, BackColor = Theme.Surface, WrapContents = false };
            buttons.Controls.Add(ModalUi.ToolButton("Add column", (s, e) => AddColumn()));
            buttons.Controls.Add(ModalUi.ToolButton("Remove", (s, e) => RemoveColumn()));
            buttons.Controls.Add(ModalUi.ToolButton("Move up", (s, e) => MoveColumn(-1)));
            buttons.Controls.Add(ModalUi.ToolButton("Move down", (s, e) => MoveColumn(1)));
            buttons.Controls.Add(ModalUi.ToolButton("Standard columns", (s, e) => FillColumns(ListFields.DefaultColumns())));

            _cols = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                AllowUserToOrderColumns = false, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
                EditMode = DataGridViewEditMode.EditOnEnter, BackgroundColor = Theme.Surface, BorderStyle = BorderStyle.FixedSingle,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Theme.GridLine,
                RowHeadersVisible = false, EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 32,
                Font = new Font(CardFonts.UI, 9.5f)
            };
            _cols.RowTemplate.Height = 32;
            _cols.ColumnHeadersDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            _cols.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextSoft;
            _cols.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.SurfaceAlt;
            _cols.ColumnHeadersDefaultCellStyle.Font = new Font(CardFonts.UI, 9f, FontStyle.Bold);
            _cols.DefaultCellStyle.BackColor = Theme.Surface;
            _cols.DefaultCellStyle.ForeColor = Theme.TextMain;
            _cols.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            _cols.DefaultCellStyle.SelectionForeColor = Theme.TextMain;
            _cols.DefaultCellStyle.Padding = new Padding(2, 0, 2, 0);

            _cols.Columns.Add(new DataGridViewComboBoxColumn
            {
                HeaderText = Lang.T("Shows"), Width = 230, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                DataSource = ListFields.All.Select(f => new FieldChoice { Id = f.Id, Label = Lang.T(f.Label) }).ToList(),
                DisplayMember = "Label", ValueMember = "Id", ValueType = typeof(string), MaxDropDownItems = 16
            });
            _cols.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = Lang.T("Header text"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 120,
                ValueType = typeof(string)
            });
            var width = new DataGridViewTextBoxColumn { HeaderText = Lang.T("Width"), Width = 64, ValueType = typeof(float) };
            width.DefaultCellStyle.Format = "0.##";
            width.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _cols.Columns.Add(width);
            _cols.Columns.Add(new DataGridViewComboBoxColumn
            {
                HeaderText = Lang.T("Align"), Width = 96, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                DataSource = new List<AlignChoice>
                {
                    new AlignChoice { Value = ListAlign.Left, Label = Lang.T("Left") },
                    new AlignChoice { Value = ListAlign.Center, Label = Lang.T("Centre") },
                    new AlignChoice { Value = ListAlign.Right, Label = Lang.T("Right") }
                },
                DisplayMember = "Label", ValueMember = "Value", ValueType = typeof(ListAlign)
            });
            var mm = new DataGridViewTextBoxColumn { HeaderText = "≈ mm", Width = 64, ReadOnly = true };
            mm.DefaultCellStyle.ForeColor = Theme.TextMuted;
            mm.DefaultCellStyle.SelectionForeColor = Theme.TextMuted;
            mm.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _cols.Columns.Add(mm);

            // Drop-downs take effect at once; a new field brings its usual header when the header was not typed by hand
            _cols.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_cols.IsCurrentCellDirty && _cols.CurrentCell is DataGridViewComboBoxCell)
                    _cols.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _cols.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (e.ColumnIndex == ColField) FieldChanged(_cols.Rows[e.RowIndex]);
                if (e.ColumnIndex == ColWidth || e.ColumnIndex == ColField) UpdateMm();
            };
            _cols.CellValidating += (s, e) =>
            {
                if (e.ColumnIndex != ColWidth) return;
                float w;
                if (float.TryParse(Convert.ToString(e.FormattedValue), NumberStyles.Float, CultureInfo.CurrentCulture, out w) &&
                    w >= 1f && w <= 500f)
                    return;
                e.Cancel = true;
                AppDialog.Show(this, Lang.T("Width must be a number from 1 to 500."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            _cols.DataError += (s, e) => e.ThrowException = false;
            _cols.RowsRemoved += (s, e) => UpdateMm();
            _cols.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && !_cols.IsCurrentCellInEditMode) { RemoveColumn(); e.Handled = true; }
            };

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                BackColor = Theme.Surface, Padding = new Padding(0, 6, 0, 0)
            };
            bottom.Controls.Add(new Label
            {
                Text = Lang.T("Widths are shared out across the page inside the margins: a column with 20 is twice as wide as one with 10, on any paper size. ≈ mm is the width on the paper chosen in Page & text."),
                AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = Theme.TextMuted, BackColor = Theme.Surface, Font = Theme.Small, UseMnemonic = false
            });
            bottom.Controls.Add(_chkShade);

            _tablePage.Controls.Add(_cols);
            _tablePage.Controls.Add(buttons);
            _tablePage.Controls.Add(bottom);

            // ≈ mm follows the paper and margins
            _paper.SelectedIndexChanged += (s, e) => UpdateMm();
            _orientation.SelectedIndexChanged += (s, e) => UpdateMm();
            _left.ValueChanged += (s, e) => UpdateMm();
            _right.ValueChanged += (s, e) => UpdateMm();
        }

        private void FillColumns(List<ListColumnDesign> columns)
        {
            _cols.Rows.Clear();
            foreach (ListColumnDesign c in columns)
                _cols.Rows.Add(ListFields.Find(c.Field).Id, c.Header ?? "", c.Weight, c.Align, "");
            UpdateMm();
        }

        private List<ListColumnDesign> ReadColumns()
        {
            var list = new List<ListColumnDesign>();
            foreach (DataGridViewRow row in _cols.Rows)
            {
                string field = row.Cells[ColField].Value as string ?? ListFields.Empty;
                float width = row.Cells[ColWidth].Value is float ? (float)row.Cells[ColWidth].Value : 20f;
                ListAlign align = row.Cells[ColAlign].Value is ListAlign ? (ListAlign)row.Cells[ColAlign].Value : ListAlign.Left;
                list.Add(new ListColumnDesign(field, Convert.ToString(row.Cells[ColHeader].Value) ?? "", width, align));
            }
            return list;
        }

        /// <summary>Another field was picked: its usual header (unless one was typed) and alignment.</summary>
        private void FieldChanged(DataGridViewRow row)
        {
            ListFields.Field f = ListFields.Find(row.Cells[ColField].Value as string);
            string header = (Convert.ToString(row.Cells[ColHeader].Value) ?? "").Trim();
            if (header.Length == 0 || ListFields.All.Any(x => x.DefaultHeader == header)) row.Cells[ColHeader].Value = f.DefaultHeader;
            row.Cells[ColAlign].Value = f.DefaultAlign;
        }

        private void AddColumn()
        {
            int at = _cols.CurrentCell != null ? _cols.CurrentCell.RowIndex + 1 : _cols.Rows.Count;
            ListColumnDesign c = ListFields.NewColumn(ListFields.Empty);
            _cols.Rows.Insert(at, c.Field, c.Header, c.Weight, c.Align, "");
            _cols.CurrentCell = _cols.Rows[at].Cells[ColField];
            UpdateMm();
        }

        private void RemoveColumn()
        {
            if (_cols.CurrentCell == null) return;
            int at = _cols.CurrentCell.RowIndex;
            _cols.EndEdit();
            _cols.Rows.RemoveAt(at);
            if (_cols.Rows.Count > 0) _cols.CurrentCell = _cols.Rows[Math.Min(at, _cols.Rows.Count - 1)].Cells[ColField];
        }

        private void MoveColumn(int delta)
        {
            if (_cols.CurrentCell == null || !_cols.EndEdit()) return;
            int from = _cols.CurrentCell.RowIndex, to = from + delta, col = _cols.CurrentCell.ColumnIndex;
            if (to < 0 || to >= _cols.Rows.Count) return;
            for (int c = 0; c < _cols.Columns.Count; c++)
            {
                object v = _cols.Rows[from].Cells[c].Value;
                _cols.Rows[from].Cells[c].Value = _cols.Rows[to].Cells[c].Value;
                _cols.Rows[to].Cells[c].Value = v;
            }
            _cols.CurrentCell = _cols.Rows[to].Cells[col];
        }

        /// <summary>"≈ mm": each column's width on the paper and margins chosen in Page &amp; text.</summary>
        private void UpdateMm()
        {
            if (_cols == null || _cols.Rows.Count == 0) return;
            var probe = new ListPageDesign
            {
                Paper = (string)_paper.SelectedItem ?? "A4", Landscape = _orientation.SelectedIndex == 1,
                MarginLeft = (float)_left.Value, MarginRight = (float)_right.Value
            };
            float[] margins = probe.MarginsMm;
            float content = probe.PageMm.Width - margins[0] - margins[2];
            List<ListColumnDesign> cols = ReadColumns();
            float total = cols.Sum(c => c.Weight);
            for (int i = 0; i < cols.Count; i++)
                _cols.Rows[i].Cells[ColMm].Value = total > 0 ? (cols[i].Weight / total * content).ToString("0.0", CultureInfo.CurrentCulture) : "";
        }

        // ===================== Building the grid =====================

        private void Section(string title)
        {
            var label = new Label
            {
                Text = title, AutoSize = true, ForeColor = Theme.Accent, BackColor = Theme.Surface,
                Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, _row == 0 ? 6 : 16, 0, 4)
            };
            _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _grid.Controls.Add(label, 0, _row);
            _grid.SetColumnSpan(label, 2);
            _row++;
        }

        private void Row(string caption, Control control)
        {
            var label = new Label
            {
                Text = caption, AutoSize = true, ForeColor = Theme.TextMain, BackColor = Theme.Surface,
                Margin = new Padding(0, 8, 8, 0)
            };
            control.Margin = new Padding(0, 4, 0, 4);
            _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _grid.Controls.Add(label, 0, _row);
            _grid.Controls.Add(control, 1, _row);
            _row++;
        }

        /// <summary>"[first]   Second caption [second]" on one line.</summary>
        private Control Pair(Control first, string secondCaption, Control second)
        {
            var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface };
            first.Margin = new Padding(0, 0, 18, 0);
            flow.Controls.Add(first);
            flow.Controls.Add(new Label
            {
                Text = secondCaption, AutoSize = true, ForeColor = Theme.TextMain, BackColor = Theme.Surface,
                Margin = new Padding(0, 4, 8, 0)
            });
            second.Margin = new Padding(0);
            flow.Controls.Add(second);
            return flow;
        }

        private static ComboBox Combo(string[] items, bool editable)
        {
            var box = new ComboBox
            {
                DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
                Width = editable ? 300 : 160, BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain,
                FlatStyle = FlatStyle.Flat, MaxDropDownItems = 16
            };
            box.Items.AddRange(items.Cast<object>().ToArray());
            if (editable)
            {
                box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                box.AutoCompleteSource = AutoCompleteSource.ListItems;
            }
            return box;
        }

        private static NumericUpDown Number(decimal min, decimal max, decimal step, int decimals)
        {
            return new NumericUpDown
            {
                Minimum = min, Maximum = max, Increment = step, DecimalPlaces = decimals, Width = 80,
                BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain, TextAlign = HorizontalAlignment.Right
            };
        }

        private static TextBox TextArea(int lines)
        {
            var box = new TextBox
            {
                Multiline = true, AcceptsReturn = true, WordWrap = false, ScrollBars = ScrollBars.Both,
                Width = 470, BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font(CardFonts.KhmerSiemreap, 10f)
            };
            box.Height = box.Font.Height * lines + 8;
            return box;
        }

        private static decimal Inches(float mm)
        {
            return Math.Round((decimal)(mm / 25.4f), 2);
        }

        private static bool IsKhmerFont(string family)
        {
            string f = family.ToLowerInvariant();
            return f.Contains("khmer") || f.Contains("siemreap") || f.Contains("battambang") || f.Contains("moul") ||
                   f.Contains("hanuman") || f.Contains("kantumruy") || f.Contains("koulen") || f.Contains("bokor") ||
                   f.Contains("dangrek") || f.Contains("suwannaphum") || f.Contains("nokora");
        }
    }
}
