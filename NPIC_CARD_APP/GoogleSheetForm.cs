using System;
using System.Collections.Generic;
using System.Drawing;
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
    /// "Google Sheets" page: everything about the Google Sheet the student list comes from.
    ///   Connect (F5) = download the sheet and preview its rows here.   Import = replace the main list with them.
    /// A saved link is read again as soon as the page opens. The link is kept in <see cref="AppSettings.GoogleSheetUrl"/>.
    /// </summary>
    public class GoogleSheetForm : ModalForm
    {
        private static readonly Color Accent     = Theme.Accent;
        private static readonly Color AccentSoft = Theme.AccentSoft;
        private static readonly Color Surface    = Theme.Surface;
        private static readonly Color SurfaceAlt = Theme.SurfaceAlt;
        private static readonly Color GridLine   = Theme.GridLine;
        private static readonly Color TextMain   = Theme.TextMain;
        private static readonly Color TextMuted  = Theme.TextSoft;
        private static readonly Color Success    = Theme.Success;
        private static readonly Color Warning    = Theme.Warning;
        private static readonly Color Danger     = Theme.Danger;

        /// <summary>Preview columns: property name → header.</summary>
        private static readonly string[,] Columns =
        {
            { "StudentId", "ID" }, { "NameKh", "Name (KH)" }, { "NameEn", "Name (EN)" }, { "Gender", "Sex" },
            { "DateOfBirth", "Date of birth" }, { "Faculty", "Faculty" }, { "Level", "Level" },
            { "Generation", "Gen." }, { "ValidUntil", "Valid until" }
        };

        private const string SetupSteps =
            "Connect a Google Sheet\n\n" +
            "1.  In Google Sheets click Share → General access → “Anyone with the link” (Viewer).\n" +
            "2.  Open the tab with the students and copy the browser's address bar.\n" +
            "3.  Paste it above and click Connect.\n\n" +
            "Row 1 holds the column names: StudentId, NameKh, NameEn, Gender, DateOfBirth, Faculty,\n" +
            "Level, Generation, ValidUntil, QrData — the same as the Excel template. The app only reads the sheet.";

        private readonly AppSettings _settings;
        private readonly TextBox _txtLink;
        private readonly DataGridView _grid;
        private readonly Label _lblEmpty, _lblStatus;
        private readonly Button _btnConnect, _btnOpen, _btnImport, _btnClose;

        private CancellationTokenSource _cts;
        private bool _closeWhenDone;
        private List<Student> _preview;
        private string _previewLink;

        /// <summary>The students to import (set when the page closes with Import).</summary>
        public List<Student> Students { get; private set; }

        public GoogleSheetForm(AppSettings settings)
        {
            _settings = settings;

            Text = "Google Sheets";
            Subtitle = "Read the student list from a shared Google Sheet";
            Glyph = Theme.IcoCloud;
            StartPosition = FormStartPosition.CenterParent;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1040, work.Width), Math.Min(660, work.Height));
            MinimumSize = new Size(Math.Min(720, work.Width), Math.Min(460, work.Height));
            Font = new Font(CardFonts.UI, 9f);
            BackColor = Surface;
            ForeColor = TextMain;
            KeyPreview = true;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // ---- Link bar: [Sheet link ______________________] [Connect] [Open in Browser] ----
            _txtLink = new TextBox { Dock = DockStyle.Fill, Text = settings.GoogleSheetUrl ?? "", Margin = new Padding(0, 9, 10, 0) };
            _txtLink.HandleCreated += (s, e) =>
                SendMessage(_txtLink.Handle, EM_SETCUEBANNER, (IntPtr)1, "Paste the sheet link: https://docs.google.com/spreadsheets/d/…/edit#gid=0");
            _txtLink.TextChanged += (s, e) => UpdateButtons();

            _btnConnect = ModalUi.Button("Connect (F5)", PillKind.Primary, async (s, e) => await ConnectAsync());
            _btnOpen = ModalUi.Button("Open in Browser", PillKind.Secondary, (s, e) => OpenInBrowser());
            _btnConnect.Margin = _btnOpen.Margin = new Padding(0, 4, 6, 0);

            var linkBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 52, ColumnCount = 4, RowCount = 1, BackColor = SurfaceAlt,
                Padding = new Padding(16, 4, 10, 6)
            };
            linkBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            linkBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            linkBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            linkBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            linkBar.Controls.Add(new Label
            {
                Text = "Sheet link", AutoSize = true, ForeColor = TextMuted, Margin = new Padding(0, 12, 10, 0)
            }, 0, 0);
            linkBar.Controls.Add(_txtLink, 1, 0);
            linkBar.Controls.Add(_btnConnect, 2, 0);
            linkBar.Controls.Add(_btnOpen, 3, 0);

            // ---- Preview grid ----
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
                BackgroundColor = Surface, BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = GridLine,
                RowHeadersVisible = false, EnableHeadersVisualStyles = false, AutoGenerateColumns = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _grid.RowTemplate.Height = 30;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font(CardFonts.UI, 9f, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.DefaultCellStyle.BackColor = Surface;
            _grid.DefaultCellStyle.ForeColor = TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            _grid.DefaultCellStyle.SelectionForeColor = Accent;
            _grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.AlternatingRowsDefaultCellStyle.BackColor = SurfaceAlt;
            for (int i = 0; i < Columns.GetLength(0); i++)
            {
                _grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    DataPropertyName = Columns[i, 0], HeaderText = Columns[i, 1], SortMode = DataGridViewColumnSortMode.NotSortable,
                    FillWeight = Columns[i, 0].StartsWith("Name") ? 160 : Columns[i, 0] == "Gender" || Columns[i, 0] == "Generation" ? 50 : 100
                });
            }
            _grid.CellFormatting += FormatMissingId;

            _lblEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted, BackColor = Surface,
                Text = SetupSteps
            };
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(_lblEmpty);
            _grid.Visible = false;   // the setup steps show until a sheet with students is read

            _lblStatus = new Label { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(10, 6, 10, 4) };
            ModalUi.StyleStatus(_lblStatus);

            // ---- Footer (first added = rightmost) ----
            var footer = ModalUi.Footer();
            _btnClose = ModalUi.Button("Close", PillKind.Secondary, (s, e) =>
            {
                if (_cts != null) _cts.Cancel();   // stop the download, keep the page open
                else DialogResult = DialogResult.Cancel;
            });
            _btnImport = ModalUi.Button("Import Students", PillKind.Primary, (s, e) => Import());
            footer.Controls.Add(_btnClose);
            footer.Controls.Add(_btnImport);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(gridHost);
            Controls.Add(linkBar);
            Controls.Add(_lblStatus);
            Controls.Add(footer);
            CancelButton = _btnClose;

            var tip = new ToolTip();
            tip.SetToolTip(_btnConnect, "Download the sheet and preview its students here. Nothing changes until you click Import.");
            tip.SetToolTip(_btnOpen, "Open the sheet in the browser, e.g. to fix its sharing or edit the students");
            tip.SetToolTip(_btnImport, "Replace the student list of the main window with the students shown here");

            ShowStatus("Not connected.", TextMuted);
            UpdateButtons();
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_txtLink.TextLength == 0) { _txtLink.Focus(); return; }
            _btnConnect.Focus();
            await ConnectAsync();   // saved link: show what's in the sheet right away
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5) { BeginConnect(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && _txtLink.Focused) { BeginConnect(); e.Handled = e.SuppressKeyPress = true; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_cts != null)
            {
                // Closed (X / Alt+F4) while downloading: cancel first; ConnectAsync then closes the page.
                e.Cancel = true;
                _closeWhenDone = true;
                _cts.Cancel();
            }
            base.OnFormClosing(e);
        }

        // ===================== Connect / import =====================
        private async void BeginConnect()
        {
            await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            if (_cts != null) return;

            string link = _txtLink.Text.Trim();
            try
            {
                GoogleSheetStudentLoader.ToCsvUri(link);   // check the link before going online
            }
            catch (ArgumentException ex)
            {
                ShowStatus(ex.Message.Replace("\n\n", "  "), Danger);
                _txtLink.Focus();
                return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                List<Student> students = await GoogleSheetStudentLoader.LoadAsync(link, _cts.Token);
                _settings.GoogleSheetUrl = link;
                ShowPreview(students, link);
            }
            catch (OperationCanceledException)
            {
                ShowStatus("Cancelled.", TextMuted);
            }
            catch (Exception ex)
            {
                ShowStatus(ex.Message.Replace("\n\n", "  ").Replace("\n", "  "), Danger);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false);
            }

            if (_closeWhenDone) DialogResult = DialogResult.Cancel;
        }

        private void ShowPreview(List<Student> students, string link)
        {
            _preview = students;
            _previewLink = link;
            _grid.DataSource = students;
            _grid.Visible = students.Count > 0;
            _lblEmpty.Visible = students.Count == 0;
            _lblEmpty.Text = students.Count == 0
                ? "The sheet has a StudentId column but no students yet.\n\nAdd them in Google Sheets, then click Connect again."
                : SetupSteps;

            int missing = 0, duplicates = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Student s in students)
            {
                if (string.IsNullOrEmpty(s.StudentId)) missing++;
                else if (!seen.Add(s.StudentId)) duplicates++;
            }

            var problems = new List<string>();
            if (missing > 0) problems.Add(missing + " without StudentId (red)");
            if (duplicates > 0) problems.Add(duplicates + " duplicate StudentId");
            ShowStatus(string.Format("✔  Connected {0:HH:mm} — {1} student(s){2}",
                    DateTime.Now, students.Count, problems.Count == 0 ? "" : "  ·  " + string.Join("  ·  ", problems)),
                problems.Count == 0 ? Success : Warning);
        }

        private void Import()
        {
            if (_preview == null || _preview.Count == 0) return;
            if (!string.Equals(_previewLink, _txtLink.Text.Trim(), StringComparison.Ordinal) &&
                AppDialog.Show(this, "The link was changed after the preview was read.\n\n" +
                    "Import the students shown (from the previous link)? Choose No, then Connect, to read the new link.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Students = _preview;
            DialogResult = DialogResult.OK;
        }

        /// <summary>Paints the ID cell of a row without StudentId, so it is easy to find and fix in the sheet.</summary>
        private void FormatMissingId(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0 || _preview == null || e.RowIndex >= _preview.Count) return;
            if (!string.IsNullOrEmpty(_preview[e.RowIndex].StudentId)) return;
            e.Value = "(missing)";
            e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Danger;
            e.FormattingApplied = true;
        }

        /// <summary>Opens the pasted link so the sheet and its sharing settings can be checked.</summary>
        private void OpenInBrowser()
        {
            string link = _txtLink.Text.Trim();
            Uri uri;
            if (!Uri.TryCreate(link, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                try { uri = GoogleSheetStudentLoader.ToCsvUri(link); }   // a bare id: open the sheet itself
                catch (ArgumentException ex) { ShowStatus(ex.Message.Replace("\n\n", "  "), Danger); return; }
                uri = new Uri(uri.GetLeftPart(UriPartial.Path).Replace("/export", "/edit"));
            }

            try { System.Diagnostics.Process.Start(uri.AbsoluteUri); }
            catch (Exception ex) { ShowStatus("Could not open the browser: " + ex.Message, Danger); }
        }

        // ===================== UI state =====================
        private void SetBusy(bool busy)
        {
            _txtLink.Enabled = !busy;
            _btnClose.Text = busy ? "Stop" : "Close";
            UseWaitCursor = busy;
            if (busy) ShowStatus("Downloading the sheet…  (Stop cancels)", TextMuted);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool busy = _cts != null, hasLink = _txtLink.TextLength > 0;
            _btnConnect.Enabled = _btnOpen.Enabled = hasLink && !busy;
            _btnImport.Enabled = !busy && _preview != null && _preview.Count > 0;
            _btnImport.Text = _preview != null && _preview.Count > 0
                ? string.Format("Import {0} Student(s)", _preview.Count)
                : "Import Students";
        }

        private void ShowStatus(string text, Color color)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }
}
