using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "Backup &amp; Restore" page: every backup in %AppData%\NPIC_CARD_APP\Backups (made by hand, by the daily automatic
    /// backup, or just before a restore), newest first.
    ///   Back Up Now (Ctrl+S)   Restore (Enter / double-click)   Restore From File…   Copy To… (e.g. a USB drive)
    ///   Show in Explorer   Delete (Del) = to the Recycle Bin   Refresh (F5)
    /// Options: include photos / CSV files in Back Up Now, daily automatic backup. A finished restore sets
    /// <see cref="Restored"/>: the main window then restarts the app without saving the settings it holds.
    /// See <see cref="BackupService"/> for what a backup contains.
    /// </summary>
    public class BackupForm : ModalForm
    {
        private static readonly Color Accent     = Theme.Accent;
        private static readonly Color AccentSoft = Theme.AccentSoft;
        private static readonly Color Surface    = Theme.Surface;
        private static readonly Color SurfaceAlt = Theme.SurfaceAlt;
        private static readonly Color GridLine   = Theme.GridLine;
        private static readonly Color TextMain   = Theme.TextMain;
        private static readonly Color TextMuted  = Theme.TextSoft;
        private static readonly Color Warning    = Theme.Warning;
        private static readonly Color Success    = Theme.Success;

        private readonly AppSettings _settings;
        private List<BackupFile> _all = new List<BackupFile>();
        private readonly DataGridView _grid;
        private readonly Label _lblStatus, _lblEmpty;
        private readonly CheckBox _chkPhotos, _chkCsv, _chkAuto;
        private readonly Button _btnBackup, _btnRestore, _btnRestoreFile, _btnCopy, _btnExplorer, _btnDelete, _btnRefresh;
        private CancellationTokenSource _cts;
        private bool _refreshOnActivate;

        /// <summary>True when the backup / auto-backup options changed (already in the settings, not saved yet).</summary>
        public bool SettingsChanged { get; private set; }

        /// <summary>True after a restore: the files on disk are new, the settings in memory are old — restart, do not save.</summary>
        public bool Restored { get; private set; }

        public BackupForm(AppSettings settings)
        {
            _settings = settings;
            Text = "Backup & Restore";
            Subtitle = "Save all the app's data to one file and bring it back";
            Glyph = Theme.IcoBackup;
            StartPosition = FormStartPosition.CenterParent;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1000, work.Width), Math.Min(620, work.Height));
            MinimumSize = new Size(Math.Min(720, work.Width), Math.Min(420, work.Height));
            Font = new Font(CardFonts.UI, 9f);
            BackColor = Surface;
            KeyPreview = true;
            ShowInTaskbar = false;
            MinimizeBox = false;

            // ---- Toolbar ----
            _btnBackup      = ModalUi.ToolButton("Back Up Now", (s, e) => BackUpNow());
            _btnRestore     = ModalUi.ToolButton("Restore", (s, e) => RestoreSelected());
            _btnRestoreFile = ModalUi.ToolButton("Restore From File…", (s, e) => RestoreFromFile());
            _btnCopy        = ModalUi.ToolButton("Copy To…", (s, e) => CopySelected());
            _btnExplorer    = ModalUi.ToolButton("Show in Explorer", (s, e) => ShowInExplorer());
            _btnDelete      = ModalUi.ToolButton("Delete", (s, e) => DeleteSelected());
            _btnRefresh     = ModalUi.ToolButton("Refresh", (s, e) => Reload(null));

            var toolbar = ModalUi.Toolbar();
            toolbar.Controls.AddRange(new Control[] { _btnBackup, _btnRestore, _btnRestoreFile, _btnCopy, _btnExplorer, _btnDelete, _btnRefresh });

            var tip = new ToolTip();
            tip.SetTip(_btnBackup, "Save the settings, print history, RFID cards, logos, signatures and card designs to a new backup (Ctrl+S)");
            tip.SetTip(_btnRestore, "Put the files of the selected backup back. The current data is backed up first (Enter)");
            tip.SetTip(_btnRestoreFile, "Restore a backup saved somewhere else, e.g. on a USB drive or another PC");
            tip.SetTip(_btnCopy, "Save a copy of the selected backup somewhere else, e.g. on a USB drive");
            tip.SetTip(_btnExplorer, "Open the backup folder in Windows Explorer with the selected backup highlighted");
            tip.SetTip(_btnDelete, "Move the selected backup to the Recycle Bin (Del)");

            // ---- Options ----
            _chkPhotos = Check("Include student photos", settings.BackupIncludePhotos);
            _chkCsv = Check("Include student CSV files", settings.BackupIncludeCsv);
            _chkAuto = Check(Lang.F("Back up automatically once a day (photos not included, the last {0} are kept)", BackupService.AutoKeep),
                settings.AutoBackup);
            _chkPhotos.CheckedChanged += (s, e) => { _settings.BackupIncludePhotos = _chkPhotos.Checked; SettingsChanged = true; };
            _chkCsv.CheckedChanged += (s, e) => { _settings.BackupIncludeCsv = _chkCsv.Checked; SettingsChanged = true; };
            _chkAuto.CheckedChanged += (s, e) => { _settings.AutoBackup = _chkAuto.Checked; SettingsChanged = true; };
            tip.SetTip(_chkPhotos, "Photos of the Photo Folder and their originals kept by Remove Background…:\n" + Or(settings.PhotoFolder, "(not set)"));
            tip.SetTip(_chkCsv, "CSV files of the CSV folder:\n" + Or(settings.CsvFolder, "(not set)"));

            var options = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 40, WrapContents = false, Padding = new Padding(16, 8, 16, 0), BackColor = Surface
            };
            options.Controls.AddRange(new Control[] { _chkPhotos, _chkCsv, _chkAuto });

            // ---- List ----
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                BackgroundColor = Surface, BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = GridLine,
                RowHeadersVisible = false, EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, ShowCellToolTips = true, StandardTab = true
            };
            _grid.RowTemplate.MinimumHeight = 30;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font(CardFonts.UI, 9f, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.DefaultCellStyle.ForeColor = TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            _grid.DefaultCellStyle.SelectionForeColor = Accent;
            _grid.DefaultCellStyle.Padding = new Padding(4, 3, 4, 3);

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Created", HeaderText = "Created", Width = 150 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", Width = 120 });
            var contents = new DataGridViewTextBoxColumn { Name = "Contents", HeaderText = "Contents", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };
            contents.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.Columns.Add(contents);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Size", HeaderText = "Size", Width = 90 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "From", HeaderText = "From", Width = 150 });
            _grid.Columns["Created"].DefaultCellStyle.Font = new Font(CardFonts.UI, 9f, FontStyle.Bold);
            _grid.Columns["Size"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _grid.Columns["Size"].DefaultCellStyle.ForeColor = TextMuted;
            _grid.Columns["From"].DefaultCellStyle.ForeColor = TextMuted;
            foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

            _grid.SelectionChanged += (s, e) => UpdateButtons();
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) RestoreSelected(); };

            _lblEmpty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TextMuted, BackColor = Surface,
                Text = "No backup yet. Click Back Up Now to make the first one.", Visible = false
            };
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
            gridHost.Controls.Add(_grid);
            gridHost.Controls.Add(_lblEmpty);
            _lblEmpty.BringToFront();

            _lblStatus = new Label { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(10, 6, 10, 4), UseMnemonic = false };
            ModalUi.StyleStatus(_lblStatus);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(gridHost);
            Controls.Add(options);
            Controls.Add(toolbar);
            Controls.Add(_lblStatus);

            Reload(null);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_grid.Rows.Count > 0) _grid.Focus(); else _btnBackup.Focus();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (!_refreshOnActivate || _cts != null) return;
            _refreshOnActivate = false;
            Reload(Selected == null ? null : Selected.Path);   // backups may have been copied in or removed in Explorer
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_cts != null)
            {
                // Closed while working: a backup can stop (its .partial file is removed); a restore must finish.
                e.Cancel = true;
                _cts.Cancel();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_cts == null)
            {
                if (keyData == (Keys.Control | Keys.S)) { BackUpNow(); return true; }
                if (keyData == Keys.F5) { Reload(Selected == null ? null : Selected.Path); return true; }
                if (_grid.Focused && keyData == Keys.Enter) { RestoreSelected(); return true; }
                if (_grid.Focused && keyData == Keys.Delete) { DeleteSelected(); return true; }
            }
            if (keyData == Keys.Escape && _cts == null) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ===================== View =====================
        private void Reload(string select)
        {
            Cursor = Cursors.WaitCursor;
            try { _all = BackupService.List(); }
            finally { Cursor = Cursors.Default; }

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (BackupFile b in _all)
            {
                int i = _grid.Rows.Add(b.Created.ToString("yyyy-MM-dd  HH:mm"), KindText(b.Kind), ContentsText(b),
                    FormatSize(b.Size), b.Info == null ? "" : b.Info.MachineName + " · v" + b.Info.AppVersion);
                DataGridViewRow row = _grid.Rows[i];
                row.Tag = b;
                foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = b.Path;
                if (b.Info == null)
                {
                    row.Cells["Contents"].Style.ForeColor = Warning;
                    row.Cells["Contents"].Style.SelectionForeColor = Warning;
                }
                else if (b.Kind != BackupKind.Manual)
                {
                    row.Cells["Type"].Style.ForeColor = TextMuted;
                }
            }
            _grid.ResumeLayout();
            _lblEmpty.Visible = _all.Count == 0;

            foreach (DataGridViewRow row in _grid.Rows)
            {
                var b = (BackupFile)row.Tag;
                if (select == null || string.Equals(b.Path, select, StringComparison.OrdinalIgnoreCase))
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells[0];
                    break;
                }
            }
            UpdateButtons();
        }

        private BackupFile Selected
        {
            get { return _grid.SelectedRows.Count == 0 ? null : _grid.SelectedRows[0].Tag as BackupFile; }
        }

        private void UpdateButtons()
        {
            bool idle = _cts == null;
            BackupFile x = Selected;
            _btnBackup.Enabled = idle;
            _btnRestore.Enabled = idle && x != null && x.Info != null;
            _btnRestoreFile.Enabled = idle;
            _btnCopy.Enabled = idle && x != null;
            _btnExplorer.Enabled = idle;
            _btnDelete.Enabled = idle && x != null;
            _btnRefresh.Enabled = idle;
            _chkPhotos.Enabled = _chkCsv.Enabled = _chkAuto.Enabled = idle;
            _grid.Enabled = idle;
            if (!idle) return;

            string summary = Lang.F("{0} backups in {1}", _all.Count, BackupService.BackupFolder) +
                (_settings.AutoBackup
                    ? (_settings.LastAutoBackup == DateTime.MinValue ? Lang.T("  ·  daily backup: not run yet")
                       : Lang.F("  ·  last daily backup: {0}", _settings.LastAutoBackup.ToString("yyyy-MM-dd HH:mm")))
                    : Lang.T("  ·  daily backup is off"));
            if (x == null)
            {
                ShowStatus(summary + Environment.NewLine +
                    Lang.T("A backup holds settings, print history, RFID cards, QR logos, signatures and card designs. The licence is not included."),
                    TextMuted);
                return;
            }
            string detail = x.Info == null ? Lang.T("Cannot be restored: ") + x.Error
                : x.Info.Skipped.Count > 0 ? Lang.F("{0} file(s) were in use and are missing from this backup.", x.Info.Skipped.Count)
                : Path.GetFileName(x.Path);
            ShowStatus(detail + Environment.NewLine + summary, x.Info == null || x.Info.Skipped.Count > 0 ? Warning : TextMuted);
        }

        private void ShowStatus(string text, Color color)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
        }

        // ===================== Back up =====================
        private async void BackUpNow()
        {
            if (_cts != null) return;
            if (!SaveCurrentSettings()) return;
            BackupSources src = BackupSources.From(_settings, _chkPhotos.Checked, _chkCsv.Checked);

            string path;
            if (!BeginWork()) return;
            try
            {
                CancellationToken ct = _cts.Token;
                IProgress<BackupProgress> progress = new Progress<BackupProgress>(p =>
                    ShowStatus(Lang.F("Backing up… {0} of {1} files", p.Done, p.Total) + Environment.NewLine +
                               Lang.T("Close the window or press Esc to stop."), Accent));
                path = await Task.Run(() => BackupService.Create(src, BackupKind.Manual, progress, ct));
            }
            catch (OperationCanceledException)
            {
                EndWork();
                Reload(null);
                ShowStatus(Lang.T("Backup stopped. Nothing was saved."), Warning);
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                EndWork();
                AppDialog.Show(this, "Could not make the backup:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Reload(null);
                return;
            }
            EndWork();
            Reload(path);
            BackupFile made = _all.FirstOrDefault(b => string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase));
            ShowStatus(Lang.F("✔  Backup saved: {0}", path) + Environment.NewLine +
                (made != null ? ContentsText(made) + "  ·  " + FormatSize(made.Size) : "") +
                Lang.T("  ·  Use Copy To… to keep a copy on a USB drive or another PC."), Success);
        }

        // ===================== Restore =====================
        private void RestoreSelected()
        {
            BackupFile x = Selected;
            if (x == null || _cts != null) return;
            if (x.Info == null)
            {
                AppDialog.Show(this, "This file cannot be restored:\n" + x.Path + "\n\n" + x.Error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Restore(x);
        }

        private void RestoreFromFile()
        {
            if (_cts != null) return;
            string path;
            using (var dlg = new OpenFileDialog
            {
                Title = Lang.T("Restore From File"),
                Filter = Lang.T("Card Studio backup (*.zip)|*.zip|All files (*.*)|*.*"),
                CheckFileExists = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                path = dlg.FileName;
            }
            BackupFile b = BackupService.Describe(path);
            if (b.Info == null)
            {
                AppDialog.Show(this, "This file cannot be restored:\n" + path + "\n\n" + b.Error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Restore(b);
        }

        private async void Restore(BackupFile b)
        {
            BackupInfo info = b.Info;
            string what = ContentsText(b);
            string from = info.MachineName + " (" + info.UserName + "), Card Studio " + info.AppVersion;
            bool otherPc = !string.Equals(info.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                           !string.Equals(info.UserName, Environment.UserName, StringComparison.OrdinalIgnoreCase);

            string text = Lang.F("Restore the backup of {0}?", info.Created.ToString("yyyy-MM-dd HH:mm")) + "\n" + from + "\n\n" +
                Lang.T("Contents: ") + what + "\n\n" +
                Lang.T("Settings, print history, RFID cards, logos, signatures and card designs are replaced with the ones in the " +
                       "backup. Files added since the backup are kept.") + "\n\n" +
                Lang.T("The current data is backed up first (Before restore), so this can be undone.") + "\n" +
                Lang.T("Card Studio restarts when the restore is done.") +
                (otherPc ? "\n\n" + Lang.T("This backup was made by another PC or Windows user: the API token, SQL password and " +
                                           "Google / OneDrive sign-in must be entered again.") : "");
            if (AppDialog.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            if (!SaveCurrentSettings()) return;
            BackupSources current = BackupSources.From(_settings, false, false);

            RestoreResult result;
            if (!BeginWork()) return;
            try
            {
                CancellationToken ct = _cts.Token;
                IProgress<BackupProgress> progress = new Progress<BackupProgress>(p =>
                    ShowStatus((p.Restoring ? Lang.F("Restoring… {0} of {1} files", p.Done, p.Total)
                                            : Lang.F("Backing up the current data first… {0} of {1} files", p.Done, p.Total)) +
                               Environment.NewLine + Lang.T("Please wait — Card Studio restarts when it is done."), Accent));
                // A restore is not cancelled half-way: only the safety backup before it can stop.
                result = await Task.Run(() => BackupService.Restore(b.Path, current, progress, ct));
            }
            catch (OperationCanceledException)
            {
                EndWork();
                Reload(null);
                ShowStatus(Lang.T("Restore stopped before any file was replaced."), Warning);
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException ||
                                       ex is InvalidDataException)
            {
                EndWork();
                Reload(null);
                AppDialog.Show(this, "Could not restore the backup:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            EndWork();

            Restored = true;
            string done = Lang.F("{0} files restored.", result.Restored) +
                (result.Failed > 0 ? " " + Lang.F("{0} could not be restored.", result.Failed) : "") + "\n\n" +
                Lang.T("The data from before the restore is kept in:") + "\n" + result.SafetyBackup +
                (result.Notes.Count > 0 ? "\n\n" + string.Join("\n", result.Notes.Take(8)) +
                    (result.Notes.Count > 8 ? "\n" + Lang.F("…and {0} more.", result.Notes.Count - 8) : "") : "") +
                "\n\n" + Lang.T("Card Studio restarts now.");
            AppDialog.Show(this, done, Text, MessageBoxButtons.OK, result.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
        }

        // ===================== Files =====================
        private void CopySelected()
        {
            BackupFile x = Selected;
            if (x == null || _cts != null) return;
            using (var dlg = new SaveFileDialog
            {
                Title = Lang.T("Copy Backup To"),
                Filter = Lang.T("Card Studio backup (*.zip)|*.zip"),
                FileName = "CardStudio-" + Path.GetFileName(x.Path),
                OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(x.Path), StringComparison.OrdinalIgnoreCase)) return;
                try
                {
                    Cursor = Cursors.WaitCursor;
                    File.Copy(x.Path, dlg.FileName, true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    AppDialog.Show(this, "Could not copy the backup:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                finally { Cursor = Cursors.Default; }
                ShowStatus(Lang.F("✔  Copied to {0}", dlg.FileName), Success);
            }
        }

        private void ShowInExplorer()
        {
            try
            {
                Directory.CreateDirectory(BackupService.BackupFolder);
                BackupFile x = Selected;
                Process.Start("explorer.exe", x != null && File.Exists(x.Path)
                    ? "/select,\"" + x.Path + "\"" : "\"" + BackupService.BackupFolder + "\"");
                _refreshOnActivate = true;
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not open the folder:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DeleteSelected()
        {
            BackupFile x = Selected;
            if (x == null || _cts != null) return;
            if (AppDialog.Show(this, Lang.F("Move this backup to the Recycle Bin?\n{0}", x.Path) +
                    (x.Kind == BackupKind.BeforeRestore ? "\n\n" + Lang.T("It holds the data from before a restore.") : ""),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            int index = _grid.SelectedRows[0].Index;
            try
            {
                FileSystem.DeleteFile(x.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is OperationCanceledException)
            {
                AppDialog.Show(this, "Could not delete the backup:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string next = null;
            if (_grid.Rows.Count > 1)
            {
                var neighbour = _grid.Rows[index + 1 < _grid.Rows.Count ? index + 1 : index - 1].Tag as BackupFile;
                if (neighbour != null) next = neighbour.Path;
            }
            Reload(next);
        }

        // ===================== Helpers =====================
        /// <summary>A backup reads settings.xml from disk: write the current settings there first.</summary>
        private bool SaveCurrentSettings()
        {
            try
            {
                _settings.Save();
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                AppDialog.Show(this, "Could not save the current settings:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool BeginWork()
        {
            if (BackupService.IsBusy)
            {
                AppDialog.Show(this, "The daily backup is running. Try again in a moment.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            _cts = new CancellationTokenSource();
            UseWaitCursor = true;
            UpdateButtons();
            return true;
        }

        private void EndWork()
        {
            if (_cts != null) _cts.Dispose();
            _cts = null;
            UseWaitCursor = false;
            UpdateButtons();
        }

        private static CheckBox Check(string text, bool isChecked)
        {
            return new CheckBox { Text = text, AutoSize = true, Checked = isChecked, Margin = new Padding(0, 2, 22, 0), BackColor = Surface };
        }

        private static string KindText(BackupKind kind)
        {
            switch (kind)
            {
                case BackupKind.Auto:          return Lang.T("Daily (auto)");
                case BackupKind.BeforeRestore: return Lang.T("Before restore");
                default:                       return Lang.T("Manual");
            }
        }

        /// <summary>"Settings &amp; data (14 files) · 320 photos · 3 CSV files · RFID file"</summary>
        private static string ContentsText(BackupFile b)
        {
            if (b.Info == null) return Lang.T("Not a Card Studio backup, or damaged");
            BackupInfo i = b.Info;
            var parts = new List<string> { Lang.F("Settings & data ({0} files)", i.DataFiles) };
            if (i.PhotoFiles > 0) parts.Add(Lang.F("{0} photos", i.PhotoFiles));
            if (i.CsvFiles > 0) parts.Add(Lang.F("{0} CSV files", i.CsvFiles));
            if (i.HasRfid) parts.Add(Lang.T("RFID file"));
            return string.Join("  ·  ", parts);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("0.#") + " MB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.##") + " GB";
        }

        private static string Or(string value, string fallback) { return string.IsNullOrEmpty(value) ? Lang.T(fallback) : value; }
    }
}
