using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Shows the real print job: the driver's page size, the cards at their Slot X/Y positions and
    /// red dashed slot frames (preview only). One page = one tray load.
    /// There is no Print button here on purpose: printing goes through 'Print Selected',
    /// which waits for each tray load.
    ///   ← → / PageUp PageDown  previous / next tray load
    ///   F = fit, 1 = 100 %, 2 = 200 %, Esc = close
    /// </summary>
    public class CardPrintPreviewForm : Form
    {
        private readonly PrintPreviewControl _preview;
        private readonly Func<string> _jobInfo;
        private readonly int _pageCount;
        private readonly Label _lblPage;
        private readonly Label _lblInfo;
        private readonly Button _btnPrev, _btnNext;

        public CardPrintPreviewForm(PrintDocument document, int pageCount, string title, Func<string> jobInfo)
        {
            _pageCount = Math.Max(1, pageCount);
            _jobInfo = jobInfo;

            Text = "Print Preview — " + title;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1000, 760);
            MinimumSize = new Size(640, 480);
            Font = new Font("Segoe UI", 9f);
            KeyPreview = true;
            ShowInTaskbar = false;

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 5, 6, 0), WrapContents = false
            };
            _btnPrev = MakeButton("◀ Prev", (s, e) => GoTo(_preview.StartPage - 1));
            _btnNext = MakeButton("Next ▶", (s, e) => GoTo(_preview.StartPage + 1));
            _lblPage = new Label { AutoSize = true, Margin = new Padding(8, 8, 16, 0) };
            toolbar.Controls.Add(_btnPrev);
            toolbar.Controls.Add(_btnNext);
            toolbar.Controls.Add(_lblPage);
            toolbar.Controls.Add(MakeButton("Fit", (s, e) => _preview.AutoZoom = true));
            toolbar.Controls.Add(MakeButton("100%", (s, e) => _preview.Zoom = 1.0));
            toolbar.Controls.Add(MakeButton("200%", (s, e) => _preview.Zoom = 2.0));
            toolbar.Controls.Add(MakeButton("Close", (s, e) => Close()));

            _lblInfo = new Label
            {
                Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8, 4, 8, 4),
                BackColor = Color.FromArgb(240, 240, 240), ForeColor = Color.DimGray,
                Text = "Red dashed frames = slot positions (not printed). Rendering…"
            };

            _preview = new PrintPreviewControl
            {
                Dock = DockStyle.Fill,
                AutoZoom = true,
                UseAntiAlias = true,
                Columns = 1,
                Rows = 1,
                BackColor = Color.FromArgb(120, 120, 125),
                Document = document
            };

            // Show the paper / source Windows really used once the preview has been rendered
            document.EndPrint += (s, e) =>
            {
                if (IsHandleCreated) BeginInvoke((Action)UpdateInfo);
            };

            Controls.Add(_preview);
            Controls.Add(toolbar);
            Controls.Add(_lblInfo);
            UpdatePageLabel();
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 28, Margin = new Padding(3, 0, 3, 0) };
            b.Click += onClick;
            return b;
        }

        private void GoTo(int page)
        {
            page = Math.Max(0, Math.Min(page, _pageCount - 1));
            _preview.StartPage = page;
            UpdatePageLabel();
        }

        private void UpdatePageLabel()
        {
            int page = _preview.StartPage;
            _lblPage.Text = string.Format("Tray load {0} of {1}", page + 1, _pageCount);
            _btnPrev.Enabled = page > 0;
            _btnNext.Enabled = page < _pageCount - 1;
        }

        private void UpdateInfo()
        {
            string info = _jobInfo != null ? _jobInfo() : null;
            _lblInfo.Text = "Red dashed frames = slot positions (not printed).   " +
                            (string.IsNullOrEmpty(info) ? "" : info.Replace("\n", "   |   "));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Close(); return true;
                case Keys.Left:
                case Keys.PageUp: GoTo(_preview.StartPage - 1); return true;
                case Keys.Right:
                case Keys.PageDown: GoTo(_preview.StartPage + 1); return true;
                case Keys.F: _preview.AutoZoom = true; return true;
                case Keys.D1: _preview.Zoom = 1.0; return true;
                case Keys.D2: _preview.Zoom = 2.0; return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
