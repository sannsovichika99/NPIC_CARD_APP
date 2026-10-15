using NPIC_CARD_APP.Rendering;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NPIC_CARD_APP.Printing;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Shows the jobs waiting in the Windows print queue of the selected card printer and lets the user
    /// cancel stuck ones. Refreshes itself every 2 seconds.
    ///   F5 = refresh, Del = cancel selected job(s), Esc = close
    /// </summary>
    public class PrintQueueForm : ModalForm
    {
        private readonly string _printerName;
        private readonly ListView _list;
        private readonly Label _lblInfo;
        private readonly Button _btnCancel, _btnCancelAll;
        private readonly Timer _timer;
        private bool _refreshing;

        public PrintQueueForm(string printerName)
        {
            _printerName = printerName;

            Text = "Print Queue";
            Subtitle = printerName;
            Glyph = Theme.IcoPrint;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(820, 420);
            MinimumSize = new Size(560, 300);
            Font = new Font(CardFonts.UI, 9f);
            KeyPreview = true;
            ShowInTaskbar = false;

            var toolbar = ModalUi.Toolbar();
            toolbar.Controls.Add(MakeButton("Refresh (F5)", (s, e) => RefreshJobs()));
            _btnCancel = MakeButton("Cancel Selected", (s, e) => CancelJobs(SelectedJobIds()));
            _btnCancelAll = MakeButton("Cancel All", (s, e) => CancelJobs(AllJobIds()));
            toolbar.Controls.Add(_btnCancel);
            toolbar.Controls.Add(_btnCancelAll);
            toolbar.Controls.Add(MakeButton("Open Windows Queue", (s, e) => OpenWindowsQueue()));

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true,
                HideSelection = false, GridLines = true, BorderStyle = BorderStyle.None
            };
            _list.Columns.Add("Job", 50);
            _list.Columns.Add("Document", 240);
            _list.Columns.Add("Status", 130);
            _list.Columns.Add("Pages", 60, HorizontalAlignment.Right);
            _list.Columns.Add("Size", 70, HorizontalAlignment.Right);
            _list.Columns.Add("Submitted", 120);
            _list.Columns.Add("Owner", 100);
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();

            _lblInfo = new Label
            {
                Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(8, 6, 8, 4), Text = "Reading queue…"
            };
            ModalUi.StyleStatus(_lblInfo);

            Controls.Add(_list);
            Controls.Add(toolbar);
            Controls.Add(_lblInfo);

            _timer = new Timer { Interval = 2000 };
            _timer.Tick += (s, e) => RefreshJobs();
            UpdateButtons();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshJobs();
            _timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.F5) { RefreshJobs(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete && _list.SelectedItems.Count > 0) { CancelJobs(SelectedJobIds()); e.Handled = true; }
        }

        /// <summary>Reads the queue on a worker thread (WMI can be slow) and updates the list in place.</summary>
        private async void RefreshJobs()
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                List<PrintJobInfo> jobs = null;
                List<string> problems = null;
                string error = null;
                await Task.Run(() =>
                {
                    try
                    {
                        jobs = PrinterStatus.GetJobs(_printerName);
                        problems = PrinterStatus.CheckPrinterOnly(_printerName);
                    }
                    catch (Exception ex) { error = ex.Message; }
                });
                if (IsDisposed) return;

                if (error != null)
                {
                    _lblInfo.ForeColor = Theme.Danger;
                    _lblInfo.Text = "Could not read the print queue from Windows: " + error;
                    return;
                }
                ShowJobs(jobs, problems);
            }
            finally { _refreshing = false; }
        }

        private void ShowJobs(List<PrintJobInfo> jobs, List<string> problems)
        {
            HashSet<int> selected = new HashSet<int>(SelectedJobIds());

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (PrintJobInfo job in jobs)
            {
                var item = new ListViewItem(job.JobId.ToString()) { Tag = job.JobId };
                item.SubItems.Add(job.Document);
                item.SubItems.Add(job.StatusText);
                item.SubItems.Add(job.TotalPages > 0 ? job.PagesPrinted + " / " + job.TotalPages : "");
                item.SubItems.Add(FormatSize(job.SizeBytes));
                item.SubItems.Add(job.Submitted.HasValue ? job.Submitted.Value.ToString("g") : "");
                item.SubItems.Add(job.Owner);
                if (job.RawStatus.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    job.RawStatus.IndexOf("Offline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    job.RawStatus.IndexOf("Blocked", StringComparison.OrdinalIgnoreCase) >= 0)
                    item.ForeColor = Theme.Danger;
                else if (job.RawStatus.IndexOf("Printing", StringComparison.OrdinalIgnoreCase) >= 0)
                    item.ForeColor = Theme.Success;
                item.Selected = selected.Contains(job.JobId);
                _list.Items.Add(item);
            }
            _list.EndUpdate();

            string summary = jobs.Count == 0
                ? "Queue is empty — the next card job will print immediately."
                : jobs.Count + " job(s) in the queue. The printer prints them in order; cancel old or stuck jobs before printing new cards.";
            if (problems.Count > 0)
            {
                _lblInfo.ForeColor = Theme.Danger;
                _lblInfo.Text = string.Join("  ", problems) + "\n" + summary;
            }
            else
            {
                _lblInfo.ForeColor = Theme.TextSoft;
                _lblInfo.Text = "Printer ready.  " + summary + "\nAuto-refresh every 2 s.  Updated " + DateTime.Now.ToString("T") + ".";
            }
            UpdateButtons();
        }

        private void CancelJobs(List<int> jobIds)
        {
            if (jobIds.Count == 0) return;
            string question = jobIds.Count == 1
                ? "Cancel job " + jobIds[0] + "?"
                : "Cancel " + jobIds.Count + " jobs?";
            if (AppDialog.Show(this, question + "\n\nIf a card is printing right now, it will stop part-way.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            var failed = new List<string>();
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (int id in jobIds)
                {
                    try { PrinterStatus.CancelJob(_printerName, id); }
                    catch (Exception ex) { failed.Add("Job " + id + ": " + ex.Message); }
                }
            }
            finally { Cursor = Cursors.Default; }

            if (failed.Count > 0)
            {
                AppDialog.Show(this,
                    "Some jobs could not be cancelled:\n\n" + string.Join("\n", failed) +
                    "\n\nJobs from another user need administrator rights — use 'Open Windows Queue' instead.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshJobs();
        }

        private void OpenWindowsQueue()
        {
            try
            {
                Process.Start("rundll32.exe", "printui.dll,PrintUIEntry /o /n \"" + _printerName + "\"");
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not open the Windows print queue:\n\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private List<int> SelectedJobIds()
        {
            return _list.SelectedItems.Cast<ListViewItem>().Select(i => (int)i.Tag).ToList();
        }

        private List<int> AllJobIds()
        {
            return _list.Items.Cast<ListViewItem>().Select(i => (int)i.Tag).ToList();
        }

        private void UpdateButtons()
        {
            _btnCancel.Enabled = _list.SelectedItems.Count > 0;
            _btnCancelAll.Enabled = _list.Items.Count > 0;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "";
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0") + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            return ModalUi.ToolButton(text, onClick);
        }
    }
}
