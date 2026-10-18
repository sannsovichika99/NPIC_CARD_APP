using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using NPIC_CARD_APP.Services;

namespace CardStudio.SerialGenerator
{
    /// <summary>
    /// Card Studio Serial Generator: paste the Machine ID a PC shows in "Activate Card Studio", click Generate,
    /// send the serial back. Every serial is logged to serials.csv beside the private key.
    /// </summary>
    internal sealed class GeneratorForm : Form
    {
        // Card Studio's light palette (UiKit.Theme / AccentPalette default violet)
        private static readonly Color Accent = Color.FromArgb(108, 99, 245);
        private static readonly Color AccentHover = Color.FromArgb(92, 83, 230);
        private static readonly Color AccentSoft = Color.FromArgb(239, 238, 254);
        private static readonly Color AppBack = Color.FromArgb(245, 246, 250);
        private static readonly Color Surface = Color.White;
        private static readonly Color SurfaceAlt = Color.FromArgb(250, 250, 253);
        private static readonly Color Border = Color.FromArgb(228, 229, 238);
        private static readonly Color TextMain = Color.FromArgb(43, 45, 66);
        private static readonly Color TextSoft = Color.FromArgb(104, 107, 130);
        private static readonly Color Success = Color.FromArgb(34, 160, 100);
        private static readonly Color Warning = Color.FromArgb(196, 110, 0);
        private static readonly Color Danger = Color.FromArgb(214, 48, 72);

        private static readonly Font UiFont = new Font("Segoe UI", 9.75f);
        private static readonly Font BoldFont = new Font("Segoe UI Semibold", 9.75f);
        private static readonly Font TitleFont = new Font("Segoe UI Semibold", 12f);
        private static readonly Font CaptionFont = new Font("Segoe UI", 8.25f, FontStyle.Bold);
        private static readonly Font MonoBig = new Font("Consolas", 13f);
        private static readonly Font Mono = new Font("Consolas", 10.5f);

        /// <summary>A Machine ID inside any pasted text, e.g. "Machine ID: EM3S-QXMF-403A-5466".</summary>
        private static readonly Regex MachineIdInText = new Regex(@"\b[0-9A-Za-z]{4}-[0-9A-Za-z]{4}-[0-9A-Za-z]{4}-[0-9A-Za-z]{4}\b");

        private SigningKey _key;
        private List<SerialRecord> _log = new List<SerialRecord>();
        private SerialRecord _last;   // the serial shown in the result box

        private readonly Label _lblKeyPath, _lblKeyStatus;
        private readonly TextBox _txtMachine, _txtName, _txtSerial, _txtSearch;
        private readonly DateTimePicker _dtpExpires;
        private readonly CheckBox _chkNever;
        private readonly Label _lblExpiresHint;
        private readonly Label _lblMachineHint, _lblResult;
        private readonly Button _btnGenerate, _btnCopySerial, _btnCopyMessage;
        private readonly DataGridView _grid;
        private readonly Label _lblCount;

        public GeneratorForm()
        {
            Text = "Card Studio Serial Generator";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            Font = UiFont;
            BackColor = AppBack;
            ForeColor = TextMain;
            StartPosition = FormStartPosition.CenterScreen;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(900, work.Width), Math.Min(760, work.Height));
            MinimumSize = new Size(Math.Min(700, work.Width), Math.Min(560, work.Height));
            KeyPreview = true;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(root);

            // ===================== Signing key =====================
            var keyCard = Card(3, 2);
            keyCard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            keyCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            keyCard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            keyCard.Controls.Add(new Label { Text = "Signing key", Font = BoldFont, AutoSize = true, Margin = new Padding(0, 4, 14, 0) }, 0, 0);
            _lblKeyPath = new Label { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = TextSoft, Margin = new Padding(0, 4, 8, 0), Height = 22 };
            keyCard.Controls.Add(_lblKeyPath, 1, 0);
            var btnOpenKey = MakeButton("Open Key…", false, (s, e) => ChooseKey());
            keyCard.Controls.Add(btnOpenKey, 2, 0);
            keyCard.SetRowSpan(btnOpenKey, 2);
            _lblKeyStatus = new Label { AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 2, 8, 0) };
            keyCard.Controls.Add(_lblKeyStatus, 1, 1);
            root.Controls.Add(keyCard, 0, 0);

            // ===================== New serial =====================
            var form = Card(3, 8);
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var title = new Label { Text = "New serial number", Font = TitleFont, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
            form.Controls.Add(title, 0, 0);
            form.SetColumnSpan(title, 3);
            var help = new Label
            {
                AutoSize = true, ForeColor = TextSoft, Margin = new Padding(0, 0, 0, 12), MaximumSize = new Size(760, 0),
                Text = "Paste the Machine ID from the PC's \"Activate Card Studio\" window, then click Generate (Enter). " +
                       "The serial is copied to the clipboard and works on that PC only."
            };
            form.Controls.Add(help, 0, 1);
            form.SetColumnSpan(help, 3);

            form.Controls.Add(FieldLabel("Machine ID"), 0, 2);
            _txtMachine = new TextBox { Font = MonoBig, Dock = DockStyle.Fill, CharacterCasing = CharacterCasing.Upper, Margin = new Padding(0, 0, 8, 2) };
            _txtMachine.TextChanged += (s, e) => OnMachineIdChanged();
            _txtMachine.HandleCreated += (s, e) => SetCue(_txtMachine, "XXXX-XXXX-XXXX-XXXX");
            form.Controls.Add(_txtMachine, 1, 2);
            _btnGenerate = MakeButton("Generate", true, (s, e) => Generate());
            _btnGenerate.MinimumSize = new Size(130, 36);
            form.Controls.Add(_btnGenerate, 2, 2);

            _lblMachineHint = new Label { AutoSize = true, ForeColor = TextSoft, Margin = new Padding(0, 0, 0, 10), MaximumSize = new Size(640, 0) };
            form.Controls.Add(_lblMachineHint, 1, 3);
            form.SetColumnSpan(_lblMachineHint, 2);

            form.Controls.Add(FieldLabel("PC / user"), 0, 4);
            _txtName = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 8, 14) };
            _txtName.HandleCreated += (s, e) => SetCue(_txtName, "Optional, for the log: e.g. Office PC 1 – Dara");
            form.Controls.Add(_txtName, 1, 4);

            // Expires  [2027-10-18 ▾] [6 months] [1 year] [2 years]  ☐ Never expires   works up to and including that day
            form.Controls.Add(FieldLabel("Expires"), 0, 5);
            // Fills the cell width (never pushes the Generate column out of the window); the hint wraps to its own line.
            var expiryRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 12)
            };
            _dtpExpires = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 130, Font = Mono,
                MinDate = DateTime.Today, MaxDate = SerialFormat.MaxExpiry, Value = DateTime.Today.AddYears(1),
                Margin = new Padding(0, 2, 10, 0)
            };
            _dtpExpires.ValueChanged += (s, e) => UpdateExpiryHint();
            expiryRow.Controls.Add(_dtpExpires);
            foreach (int months in new[] { 6, 12, 24 })
            {
                int m = months;
                Button quick = MakeButton(m == 6 ? "6 months" : m == 12 ? "1 year" : "2 years", false, (s, e) =>
                {
                    _chkNever.Checked = false;
                    _dtpExpires.Value = DateTime.Today.AddMonths(m);
                });
                quick.MinimumSize = new Size(0, 28);
                quick.Padding = new Padding(2, 0, 2, 0);
                quick.Font = UiFont;
                quick.Margin = new Padding(0, 1, 6, 0);
                quick.TabStop = false;
                expiryRow.Controls.Add(quick);
            }
            _chkNever = new CheckBox { Text = "Never expires", AutoSize = true, Margin = new Padding(8, 6, 12, 0) };
            _chkNever.CheckedChanged += (s, e) => { _dtpExpires.Enabled = !_chkNever.Checked; UpdateExpiryHint(); };
            expiryRow.Controls.Add(_chkNever);
            expiryRow.SetFlowBreak(_chkNever, true);
            _lblExpiresHint = new Label { AutoSize = true, ForeColor = TextSoft, Margin = new Padding(0, 4, 0, 0), MaximumSize = new Size(520, 0) };
            expiryRow.Controls.Add(_lblExpiresHint);
            form.Controls.Add(expiryRow, 1, 5);

            form.Controls.Add(FieldLabel("Serial number"), 0, 6);
            _txtSerial = new TextBox
            {
                Font = Mono, Multiline = true, ReadOnly = true, WordWrap = true, BackColor = SurfaceAlt, Dock = DockStyle.Fill,
                Height = 60, MinimumSize = new Size(0, 60), Margin = new Padding(0, 2, 8, 0), TabStop = false
            };
            form.Controls.Add(_txtSerial, 1, 6);
            var copyButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            _btnCopySerial = MakeButton("Copy Serial", false, (s, e) => CopyText(_last == null ? null : _last.Serial, "Serial copied."));
            _btnCopyMessage = MakeButton("Copy Message", false, (s, e) => CopyText(_last == null ? null : ComposeMessage(_last), "Message copied: paste it in Telegram, email…"));
            _btnCopySerial.Margin = new Padding(0, 0, 0, 6);
            _btnCopyMessage.Margin = new Padding(0);
            _btnCopySerial.MinimumSize = _btnCopyMessage.MinimumSize = new Size(130, 30);
            copyButtons.Controls.Add(_btnCopySerial);
            copyButtons.Controls.Add(_btnCopyMessage);
            form.Controls.Add(copyButtons, 2, 6);

            _lblResult = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 0), MaximumSize = new Size(640, 0) };
            form.Controls.Add(_lblResult, 1, 7);
            form.SetColumnSpan(_lblResult, 2);
            root.Controls.Add(form, 0, 1);

            // ===================== Serials made =====================
            var logCard = Card(4, 2);
            logCard.Dock = DockStyle.Fill;
            logCard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            logCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            logCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
            logCard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            logCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            logCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            logCard.Controls.Add(new Label { Text = "Serials made", Font = TitleFont, AutoSize = true, Margin = new Padding(0, 2, 10, 8) }, 0, 0);
            _lblCount = new Label { AutoSize = true, ForeColor = TextSoft, Margin = new Padding(0, 7, 0, 0) };
            logCard.Controls.Add(_lblCount, 1, 0);
            _txtSearch = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 8, 8) };
            _txtSearch.HandleCreated += (s, e) => SetCue(_txtSearch, "Search Machine ID, name or serial");
            _txtSearch.TextChanged += (s, e) => FillGrid();
            logCard.Controls.Add(_txtSearch, 2, 0);
            logCard.Controls.Add(MakeButton("Open Folder", false, (s, e) => OpenFolder()), 3, 0);

            _grid = MakeGrid();
            logCard.Controls.Add(_grid, 0, 1);
            logCard.SetColumnSpan(_grid, 4);
            root.Controls.Add(logCard, 0, 2);

            // Tab: Machine ID → PC / user → Generate (they are added to the grid in another order)
            _txtMachine.TabIndex = 0;
            _txtName.TabIndex = 1;
            expiryRow.TabIndex = 2;
            _btnGenerate.TabIndex = 3;
            copyButtons.TabIndex = 4;
            UpdateExpiryHint();

            AcceptButton = _btnGenerate;
            LoadKey(SigningKey.FindDefault(), false);
            ShowResult(null, TextSoft);
        }

        // ===================== Key =====================

        private void LoadKey(string path, bool remember)
        {
            if (_key != null) { _key.Dispose(); _key = null; }
            _log = new List<SerialRecord>();

            if (string.IsNullOrEmpty(path))
            {
                _lblKeyPath.Text = "No key file found";
                SetKeyStatus("Click Open Key… and choose serial-private.key (made with Installer\\serial.ps1 -NewKey). " +
                             "Without it no serial can be made.", Danger);
            }
            else
            {
                try
                {
                    _key = SigningKey.Load(path);
                    _lblKeyPath.Text = _key.Path;
                    if (_key.MatchesApp)
                        SetKeyStatus("✓ Matches the public key in Card Studio: its serials are accepted.", Success);
                    else
                        SetKeyStatus("⚠ This key does not match the public key in this Card Studio source. Serials made with it " +
                                     "are refused unless Card Studio was built with this key.", Warning);
                    if (remember) SigningKey.RememberedPath = _key.Path;
                    ReadLog();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
                {
                    _lblKeyPath.Text = path;
                    SetKeyStatus(ex.Message, Danger);
                }
            }
            _btnGenerate.Enabled = _key != null;
            FillGrid();
            OnMachineIdChanged();
        }

        private void SetKeyStatus(string text, Color color)
        {
            _lblKeyStatus.Text = text;
            _lblKeyStatus.ForeColor = color;
        }

        private void ChooseKey()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Open the Card Studio private key",
                Filter = "Card Studio private key (*.key)|*.key|All files (*.*)|*.*",
                FileName = SigningKey.FileName
            })
            {
                string current = _key != null ? _key.Path : SigningKey.FindDefault();
                if (!string.IsNullOrEmpty(current)) dlg.InitialDirectory = Path.GetDirectoryName(current);
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadKey(dlg.FileName, true);
            }
        }

        // ===================== Log =====================

        private string LogPath { get { return _key == null ? null : SerialLog.PathFor(_key.Path); } }

        private void ReadLog()
        {
            try
            {
                _log = SerialLog.Read(LogPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                _log = new List<SerialRecord>();
                ShowResult("The log could not be read: " + ex.Message, Warning);
            }
        }

        private void FillGrid()
        {
            string q = (_txtSearch.Text ?? "").Trim();
            string qId = SerialFormat.Normalize(q);
            IEnumerable<SerialRecord> rows = Enumerable.Reverse(_log);   // newest first
            if (q.Length > 0)
                rows = rows.Where(r =>
                    (r.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (qId.Length > 0 && (SerialFormat.Normalize(r.MachineId).Contains(qId) || SerialFormat.Normalize(r.Serial).Contains(qId))));

            List<SerialRecord> list = rows.ToList();
            _grid.DataSource = list;
            _lblCount.Text = _key == null ? ""
                : q.Length > 0 ? list.Count + " of " + _log.Count
                : _log.Count == 0 ? "none yet" : _log.Count + (_log.Count == 1 ? " serial" : " serials");
        }

        private SerialRecord SelectedRecord
        {
            get { return _grid.CurrentRow == null ? null : _grid.CurrentRow.DataBoundItem as SerialRecord; }
        }

        private void OpenFolder()
        {
            string target = LogPath != null && File.Exists(LogPath) ? LogPath : _key != null ? _key.Path : null;
            if (target == null) { ChooseKey(); return; }
            try { Process.Start("explorer.exe", "/select,\"" + target + "\""); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is IOException) { ShowResult(ex.Message, Danger); }
        }

        // ===================== Generate =====================

        /// <summary>The Machine ID typed or pasted (a Machine ID inside longer pasted text is picked out).</summary>
        private string MachineIdText
        {
            get
            {
                string text = _txtMachine.Text ?? "";
                Match m = MachineIdInText.Match(text);
                return m.Success ? m.Value : text;
            }
        }

        private void OnMachineIdChanged()
        {
            // A whole message was pasted ("Hi, my Machine ID: EM3S-…"): keep only the ID. Re-enters this handler once.
            Match m = MachineIdInText.Match(_txtMachine.Text ?? "");
            if (m.Success && _txtMachine.Text.Trim() != m.Value)
            {
                _txtMachine.Text = m.Value.ToUpperInvariant();
                _txtMachine.SelectionStart = _txtMachine.TextLength;
                return;
            }

            string text = MachineIdText;
            int count = SerialFormat.CharacterCount(text);
            if (count == 0)
            {
                _lblMachineHint.Text = "16 letters and digits, shown in Card Studio's activation window (Copy button there).";
                _lblMachineHint.ForeColor = TextSoft;
                return;
            }
            if (!SerialFormat.IsMachineId(text))
            {
                _lblMachineHint.Text = count == SerialFormat.MachineIdLength
                    ? "Only letters and digits, like EM3S-QXMF-403A-5466."
                    : count + " of 16 letters and digits.";
                _lblMachineHint.ForeColor = count > SerialFormat.MachineIdLength ? Danger : TextSoft;
                return;
            }

            string id = SerialFormat.FormatMachineId(text);
            SerialRecord before = _log.LastOrDefault(r => SerialFormat.Normalize(r.MachineId) == SerialFormat.Normalize(id));
            if (before != null)
            {
                _lblMachineHint.Text = "✓ " + id + "  ·  already has a serial (" + before.Date +
                    (string.IsNullOrEmpty(before.Name) ? "" : ", " + before.Name) +
                    (before.ExpiryDate == null ? ", never expires" : (before.IsExpired ? ", expired " : ", expires ") + before.Expires) +
                    "). A new one also works: once entered, it replaces the old one on that PC.";
                _lblMachineHint.ForeColor = Warning;
                if (_txtName.Text.Length == 0 && !string.IsNullOrEmpty(before.Name)) _txtName.Text = before.Name;
            }
            else
            {
                _lblMachineHint.Text = "✓ " + id;
                _lblMachineHint.ForeColor = Success;
            }
        }

        private void Generate()
        {
            if (_key == null) { ChooseKey(); return; }
            string text = MachineIdText;
            if (!SerialFormat.IsMachineId(text))
            {
                ShowResult("Enter the PC's Machine ID: 16 letters and digits, like EM3S-QXMF-403A-5466.", Danger);
                _txtMachine.Focus();
                return;
            }
            if (!_key.MatchesApp &&
                MessageBox.Show(this, "This key does not match the public key in Card Studio's source, so Card Studio may refuse " +
                    "the serial.\n\nMake the serial anyway?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            DateTime? expires = SelectedExpiry;
            if (expires != null && expires.Value < DateTime.Today)
            {
                ShowResult("The expiry date is in the past: pick a later date or Never expires.", Danger);
                return;
            }

            var record = new SerialRecord
            {
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                MachineId = SerialFormat.FormatMachineId(text),
                Name = _txtName.Text.Trim(),
                Expires = expires == null ? SerialRecord.Never : expires.Value.ToString("yyyy-MM-dd")
            };
            try
            {
                record.Serial = _key.MakeSerial(record.MachineId, expires);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is ArgumentException)
            {
                ShowResult("The serial could not be made: " + ex.Message, Danger);
                return;
            }

            _last = record;
            _txtSerial.Text = record.Serial;
            _btnCopySerial.Enabled = _btnCopyMessage.Enabled = true;

            string logNote;
            try
            {
                SerialLog.Append(LogPath, record);
                _log.Add(record);
                _txtSearch.Text = "";
                FillGrid();
                logNote = "logged to " + SerialLog.FileName;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                logNote = "NOT logged (" + ex.Message + ")";
            }

            bool copied = TrySetClipboard(record.Serial);
            ShowResult("✓ Serial for " + record.MachineId + (record.Name.Length > 0 ? " (" + record.Name + ")" : "") +
                       (expires == null ? ", never expires," : ", expires " + record.Expires + ",") +
                       (copied ? " copied to the clipboard" : "") + "  ·  " + logNote, logNote.StartsWith("NOT") ? Warning : Success);
            OnMachineIdChanged();
        }

        /// <summary>Expiry date chosen for the next serial; null = never expires.</summary>
        private DateTime? SelectedExpiry
        {
            get { return _chkNever.Checked ? (DateTime?)null : _dtpExpires.Value.Date; }
        }

        private void UpdateExpiryHint()
        {
            DateTime? expires = SelectedExpiry;
            if (expires == null)
            {
                _lblExpiresHint.Text = "The serial works for ever on that PC: it cannot be stopped later.";
                return;
            }
            int days = (int)(expires.Value - DateTime.Today).TotalDays;
            _lblExpiresHint.Text = "The serial works up to and including this day (" +
                (days == 0 ? "today only" : days == 1 ? "1 day from today" : days.ToString("N0") + " days from today") +
                "). Card Studio warns " + LicenseService.WarnDays + " days before.";
        }

        private static string ComposeMessage(SerialRecord r)
        {
            string until = r.ExpiryDate == null
                ? "Never expires.\r\nមិនផុតកំណត់។"
                : "Valid until " + r.Expires + " (last day).\r\nមានសុពលភាពរហូតដល់ថ្ងៃ " + r.Expires + "។";
            return "Card Studio serial number" + (string.IsNullOrEmpty(r.Name) ? "" : " – " + r.Name) + "\r\n" +
                   "Machine ID: " + r.MachineId + "\r\n" +
                   until + "\r\n\r\n" +
                   r.Serial + "\r\n\r\n" +
                   "Open Card Studio, paste this serial in \"Serial number\" and click Activate.\r\n" +
                   "បើក Card Studio បិទភ្ជាប់លេខសៀរៀលនេះ ហើយចុច Activate។";
        }

        // ===================== Helpers =====================

        private void ShowResult(string text, Color color)
        {
            _lblResult.Text = text ?? "";
            _lblResult.ForeColor = color;
            if (text == null) _btnCopySerial.Enabled = _btnCopyMessage.Enabled = _last != null;
        }

        private void CopyText(string text, string done)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (TrySetClipboard(text)) ShowResult("✓ " + done, Success);
            else ShowResult("The clipboard is busy: select the text and press Ctrl+C.", Warning);
        }

        private static bool TrySetClipboard(string text)
        {
            try { Clipboard.SetText(text); return true; }
            catch (ExternalException) { return false; }
        }

        private static TableLayoutPanel Card(int columns, int rows)
        {
            var card = new TableLayoutPanel
            {
                ColumnCount = columns, RowCount = rows, Dock = DockStyle.Top, AutoSize = true, BackColor = Surface,
                Padding = new Padding(18, 14, 18, 14), Margin = new Padding(0, 0, 0, 12)
            };
            card.Paint += (s, e) =>
            {
                using (var pen = new Pen(Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };
            return card;
        }

        private static Label FieldLabel(string text)
        {
            return new Label { Text = text.ToUpperInvariant(), Font = CaptionFont, ForeColor = TextSoft, AutoSize = true, Margin = new Padding(0, 9, 8, 0) };
        }

        private static Button MakeButton(string text, bool primary, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = BoldFont,
                Padding = new Padding(10, 2, 10, 2), MinimumSize = new Size(110, 32), Margin = new Padding(0),
                BackColor = primary ? Accent : AccentSoft, ForeColor = primary ? Color.White : Accent, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(226, 224, 253);
            b.EnabledChanged += (s, e) =>
            {
                b.BackColor = !b.Enabled ? Border : primary ? Accent : AccentSoft;
                b.ForeColor = !b.Enabled ? TextSoft : primary ? Color.White : Accent;
            };
            b.Click += onClick;
            return b;
        }

        private DataGridView MakeGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                BackgroundColor = Surface, BorderStyle = BorderStyle.None, RowHeadersVisible = false,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Border,
                EnableHeadersVisualStyles = false, AutoGenerateColumns = false, ColumnHeadersHeight = 30,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Margin = new Padding(0)
            };
            grid.RowTemplate.Height = 28;
            grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextSoft;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.Font = CaptionFont;
            grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = TextMain;
            grid.DefaultCellStyle.ForeColor = TextMain;

            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Date", HeaderText = "DATE", FillWeight = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "MachineId", HeaderText = "MACHINE ID", FillWeight = 110, DefaultCellStyle = { Font = Mono } });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "PC / USER", FillWeight = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ExpiresText", HeaderText = "EXPIRES", FillWeight = 60, Name = "Expires" });
            grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "Expires") return;
                var r = grid.Rows[e.RowIndex].DataBoundItem as SerialRecord;
                if (r != null && r.IsExpired)
                {
                    e.CellStyle.ForeColor = Danger;
                    e.Value = r.Expires + " (expired)";
                }
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Serial", HeaderText = "SERIAL (double-click to copy)", FillWeight = 160, DefaultCellStyle = { Font = Mono, ForeColor = TextSoft } });

            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                SerialRecord r = SelectedRecord;
                if (r != null) CopyText(r.Serial, "Serial for " + r.MachineId + " copied.");
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Copy Serial", null, (s, e) => { var r = SelectedRecord; if (r != null) CopyText(r.Serial, "Serial for " + r.MachineId + " copied."); });
            menu.Items.Add("Copy Message", null, (s, e) => { var r = SelectedRecord; if (r != null) CopyText(ComposeMessage(r), "Message for " + r.MachineId + " copied."); });
            menu.Items.Add("Copy Machine ID", null, (s, e) => { var r = SelectedRecord; if (r != null) CopyText(r.MachineId, "Machine ID copied."); });
            grid.ContextMenuStrip = menu;
            grid.CellMouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
                    grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            };
            return grid;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.O)) { ChooseKey(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { _txtSearch.Focus(); _txtSearch.SelectAll(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _txtMachine.Focus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _key != null) _key.Dispose();
            base.Dispose(disposing);
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void SetCue(TextBox box, string cue)
        {
            SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, cue);
        }
    }
}
