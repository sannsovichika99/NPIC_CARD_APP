using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "Check for Updates" page (Ctrl+U, or Settings &amp; Help → Check for Updates). New versions come from the GitHub releases
    /// of Card Studio (<see cref="UpdateService.Source"/>); there is nothing to set up.
    ///   Check Again (F5) = compare the latest release with this version.
    ///   Install Update = download the setup, verify it, start it and close Card Studio; the setup updates the app in place and
    ///   keeps settings, print history and card images.
    ///   Skip This Version = the startup check stays quiet about this version.
    /// The page checks as soon as it opens, unless it was opened by the startup check with the update already found.
    /// </summary>
    public class UpdateForm : ModalForm
    {
        private enum State { Checking, UpToDate, Available, Failed, Downloading }

        /// <summary>Shown under an error (Markdown, written with <see cref="MarkdownWriter"/>).</summary>
        private static readonly string FailureHelp = Lang.T(
            "### What to do\n\n" +
            "Card Studio gets new versions from its GitHub releases: " + UpdateService.ReleasesPage + "\n\n" +
            "- Check that this PC is connected to the internet and that github.com is not blocked, then click **Check Again**.\n" +
            "- Or open that address in a browser, download **CardStudio-Setup.exe** from the latest release and run it.");

        private readonly AppSettings _settings;
        private readonly RichTextBox _notes;
        private readonly Label _lblHeadline, _lblVersions, _lblNotesTitle, _lblStatus;
        private readonly DownloadBar _bar;
        private readonly CheckBox _chkStartup;
        private readonly Button _btnCheck, _btnInstall, _btnSkip, _btnClose;

        private CancellationTokenSource _cts;
        private bool _closeWhenDone;
        private UpdateInfo _found;
        private State _state;

        /// <summary>True when the setup was started: the caller must close Card Studio now so the setup can replace it.</summary>
        public bool SetupStarted { get; private set; }

        /// <param name="found">An update already found by the startup check: shown right away, no new check.</param>
        public UpdateForm(AppSettings settings, UpdateInfo found = null)
        {
            _settings = settings;
            _found = found;

            Text = "Check for Updates";
            Subtitle = "Card Studio " + UpdateService.CurrentVersionText + " is installed  ·  updates from GitHub releases";
            Glyph = Theme.IcoSync;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(760, work.Width), Math.Min(560, work.Height));
            MinimumSize = new Size(Math.Min(600, work.Width), Math.Min(440, work.Height));
            KeyPreview = true;

            // ---- Body: headline, versions, release notes, download bar, startup option ----
            _lblHeadline = new Label
            {
                AutoSize = true, Font = new Font(CardFonts.UI, 14f, FontStyle.Bold), ForeColor = Theme.TextMain,
                Margin = new Padding(0, 0, 0, 4), UseMnemonic = false
            };
            _lblVersions = new Label { AutoSize = true, ForeColor = Theme.TextSoft, Margin = new Padding(0, 0, 0, 14), UseMnemonic = false };
            _lblNotesTitle = new Label
            {
                AutoSize = true, Font = Theme.SmallCaps, ForeColor = Theme.TextSoft, Margin = new Padding(0, 0, 0, 6), Text = "WHAT'S NEW"
            };
            // Release notes, formatted from Markdown (RichEdit 4.1, so Khmer notes are shaped correctly too).
            _notes = new ModernRichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false, WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Vertical, HideSelection = true,
                BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain, TabStop = false
            };
            var notesHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt, Padding = new Padding(14, 12, 6, 10), Margin = new Padding(0, 0, 0, 10) };
            notesHost.Controls.Add(_notes);

            _bar = new DownloadBar { Dock = DockStyle.Fill, Height = 8, Margin = new Padding(0, 0, 0, 10), Visible = false };

            _chkStartup = new CheckBox
            {
                AutoSize = true, Checked = settings.CheckUpdatesOnStartup, Margin = new Padding(0, 2, 0, 0),
                Text = "Check for updates when Card Studio starts (at most once a day)"
            };
            _chkStartup.CheckedChanged += (s, e) => _settings.CheckUpdatesOnStartup = _chkStartup.Checked;

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Theme.Surface, Padding = new Padding(24, 18, 24, 10)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.Controls.Add(_lblHeadline, 0, 0);
            body.Controls.Add(_lblVersions, 0, 1);
            body.Controls.Add(_lblNotesTitle, 0, 2);
            body.Controls.Add(notesHost, 0, 3);
            body.Controls.Add(_bar, 0, 4);
            body.Controls.Add(_chkStartup, 0, 5);

            _lblStatus = new Label
            {
                Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(10, 6, 10, 4), UseMnemonic = false, AutoEllipsis = true
            };
            ModalUi.StyleStatus(_lblStatus);

            // ---- Footer (first added = rightmost) ----
            var footer = ModalUi.Footer();
            _btnClose = ModalUi.Button("Close", PillKind.Secondary, (s, e) =>
            {
                if (_cts != null) _cts.Cancel();   // stop the check / download, keep the page open
                else DialogResult = DialogResult.Cancel;
            });
            _btnInstall = ModalUi.Button("Install Update", PillKind.Primary, async (s, e) => await InstallAsync());
            _btnSkip = ModalUi.Button("Skip This Version", PillKind.Secondary, (s, e) => SkipVersion());
            _btnCheck = ModalUi.Button("Check Again (F5)", PillKind.Secondary, async (s, e) => await CheckAsync());
            footer.Controls.Add(_btnClose);
            footer.Controls.Add(_btnInstall);
            footer.Controls.Add(_btnSkip);
            footer.Controls.Add(_btnCheck);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(body);
            Controls.Add(_lblStatus);
            Controls.Add(footer);
            CancelButton = _btnClose;

            var tip = new ToolTip();
            tip.SetTip(_btnCheck, "Look for a newer release on GitHub");
            tip.SetTip(_btnInstall, "Download the new version, check it, and update Card Studio. Settings, print history and card images are kept.");
            tip.SetTip(_btnSkip, "Don't announce this version again when Card Studio starts. Check Again still shows it.");

            if (_found != null) ShowAvailable(_found);
            else SetState(State.Checking, "Checking for updates…", Theme.TextMain);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ScrollNotesToTop();   // notes written in the constructor (update found at start-up), before the layout settled
            if (_state == State.Available) { _btnInstall.Focus(); return; }
            _btnCheck.Focus();
            await CheckAsync();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5) { BeginCheck(); e.Handled = true; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_cts != null && !SetupStarted)
            {
                // Closed (X / Alt+F4) while checking or downloading: cancel first, then close when it has stopped.
                e.Cancel = true;
                _closeWhenDone = true;
                _cts.Cancel();
            }
            base.OnFormClosing(e);
        }

        // ===================== Check =====================
        private async void BeginCheck()
        {
            await CheckAsync();
        }

        private async Task CheckAsync()
        {
            if (_cts != null) return;

            _cts = new CancellationTokenSource();
            _found = null;
            SetState(State.Checking, "Checking for updates…", Theme.TextMain);
            ShowStatus("Reading the latest release from GitHub…", Theme.TextSoft);
            try
            {
                UpdateInfo info = await UpdateService.CheckAsync(UpdateService.Source, _cts.Token);
                UpdateService.RememberLatest(_settings, info);   // last check time + the version for the main window's title
                if (info.IsNewerThanRunning) ShowAvailable(info);
                else ShowUpToDate(info);
            }
            catch (OperationCanceledException)
            {
                SetState(State.Failed, "Check cancelled", Theme.TextMain);
                ShowFailureNotes("The check was cancelled.");
                ShowStatus("Cancelled.", Theme.TextSoft);
            }
            catch (Exception ex)
            {
                ShowFailure("Could not check for updates", ex);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                UpdateButtons();
            }

            if (_closeWhenDone) DialogResult = DialogResult.Cancel;
        }

        private void ShowUpToDate(UpdateInfo latest)
        {
            SetState(State.UpToDate, "✔  Card Studio is up to date", Theme.Success);
            _lblVersions.Text = Lang.F("Installed {0}   ·   Latest release {1}", UpdateService.CurrentVersionText, latest.VersionText) +
                                (latest.ReleaseDate.HasValue ? "  (" + latest.ReleaseDate.Value.ToString("d MMM yyyy") + ")" : "");
            ShowReleaseNotes(latest);
            ShowStatus(Lang.F("Checked {0:HH:mm} — you have the newest version.", DateTime.Now), Theme.Success);
        }

        private void ShowAvailable(UpdateInfo info)
        {
            _found = info;
            SetState(State.Available, "Card Studio " + info.VersionText + " is available", Theme.Accent);
            _lblVersions.Text = Lang.F("Installed {0}   →   New {1}", UpdateService.CurrentVersionText, info.VersionText) +
                                (info.ReleaseDate.HasValue ? Lang.F("   ·   Released {0}", info.ReleaseDate.Value.ToString("d MMM yyyy")) : "") +
                                (info.Size > 0 ? "   ·   " + Megabytes(info.Size) : "");
            ShowReleaseNotes(info);

            if (info.Sha256 == null)
                ShowStatus("⚠  No SHA-256 checksum is published, so the download cannot be verified.", Theme.Warning);
            else if (string.Equals(_settings.SkippedUpdateVersion, info.VersionText, StringComparison.Ordinal))
                ShowStatus("You chose to skip this version. You can still install it.", Theme.TextSoft);
            else
                ShowStatus("Install Update downloads and verifies it, then runs the setup. Your data is kept.", Theme.TextSoft);
        }

        private void ShowFailure(string headline, Exception ex)
        {
            SetState(State.Failed, headline, Theme.Danger);
            _lblVersions.Text = "Installed version: " + UpdateService.CurrentVersionText;
            ShowFailureNotes(Describe(ex));
            ShowStatus(FirstLine(Describe(ex)), Theme.Danger);
        }

        // ===================== Skip / install =====================

        private void SkipVersion()
        {
            if (_found == null) return;
            _settings.SkippedUpdateVersion = _found.VersionText;
            DialogResult = DialogResult.Cancel;
        }

        private async Task InstallAsync()
        {
            if (_found == null || _cts != null) return;
            UpdateInfo info = _found;

            if (AppDialog.Show(this,
                    "Card Studio will close and update itself to version " + info.VersionText + ".\n\n" +
                    "Windows asks for administrator approval. Your settings, print history, RFID file and card images are kept.\n\n" +
                    "Make sure no cards are printing right now. Continue?",
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            _cts = new CancellationTokenSource();
            SetState(State.Downloading, "Downloading Card Studio " + info.VersionText + "…", Theme.TextMain);
            _bar.Visible = true;
            _bar.Fraction = 0f;
            var progress = new Progress<DownloadProgress>(p =>
            {
                _bar.Fraction = p.Total > 0 ? (float)p.Received / p.Total : -1f;
                ShowStatus(p.Total > 0
                    ? string.Format("Downloading…  {0} of {1}", Megabytes(p.Received), Megabytes(p.Total))
                    : "Downloading…  " + Megabytes(p.Received), Theme.TextSoft);
            });

            string setup = null;
            try
            {
                setup = await UpdateService.DownloadAsync(info, progress, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                ShowAvailable(info);
                ShowStatus("Download cancelled. Nothing was installed.", Theme.TextSoft);
            }
            catch (Exception ex)
            {
                ShowFailure("The update could not be downloaded", ex);
                _found = info;   // Install Update can be tried again
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _bar.Visible = false;
                UpdateButtons();
            }

            if (_closeWhenDone) { DialogResult = DialogResult.Cancel; return; }
            if (setup == null) return;

            ShowStatus("Starting the setup…", Theme.TextSoft);
            bool started;
            try
            {
                started = UpdateService.StartSetup(setup);
            }
            catch (Exception ex)
            {
                ShowFailure("The setup could not be started", ex);
                _found = info;
                UpdateButtons();
                return;
            }

            if (!started)
            {
                ShowAvailable(info);
                ShowStatus("The update needs administrator approval. Click Install Update to try again.", Theme.Warning);
                return;
            }

            SetupStarted = true;
            DialogResult = DialogResult.OK;   // the main window closes now; the setup waits for it, then updates
        }

        // ===================== UI state =====================

        private void SetState(State state, string headline, Color color)
        {
            _state = state;
            _lblHeadline.Text = headline;
            _lblHeadline.ForeColor = color;
            _lblNotesTitle.Text = state == State.Failed ? "DETAILS" : "WHAT'S NEW";
            if (state == State.Checking)
            {
                _lblVersions.Text = "Installed version: " + UpdateService.CurrentVersionText;
                ClearNotes();
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool busy = _cts != null;
            _btnCheck.Enabled = !busy;
            _btnInstall.Visible = _btnSkip.Visible = _found != null;
            _btnInstall.Enabled = _btnSkip.Enabled = !busy && _found != null;
            _btnClose.Text = busy ? "Cancel" : "Close";
        }

        private void ShowStatus(string text, Color color)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
        }

        private static string Describe(Exception ex)
        {
            // HttpRequestException hides the useful part ("remote name could not be resolved") in its inner exceptions.
            string message = ex.Message;
            for (Exception inner = ex.InnerException; inner != null; inner = inner.InnerException)
                if (inner.Message.Length > 0 && !message.Contains(inner.Message))
                    message += "\n" + inner.Message;
            return Lang.T(message);
        }

        private static string FirstLine(string text)
        {
            return (text ?? "").Split('\n')[0].Trim();
        }

        // ===================== Notes box =====================

        /// <summary>The release's description (Markdown written on GitHub), formatted like the user guide.</summary>
        private void ShowReleaseNotes(UpdateInfo info)
        {
            string markdown = (info.Notes ?? "").Trim();
            if (markdown.Length == 0) markdown = "No release notes were published for this version.";
            WriteNotes(markdown, w => MarkdownWriter.Write(markdown, w));
        }

        /// <summary>An error message (plain text, line by line) followed by what to do about it.</summary>
        private void ShowFailureNotes(string message)
        {
            WriteNotes(message, w =>
            {
                foreach (string line in (message ?? "").Replace("\r\n", "\n").Split('\n'))
                    if (line.Trim().Length > 0) w.Para(line.Trim());
                MarkdownWriter.Write(FailureHelp, w);
            });
        }

        private void ClearNotes()
        {
            _notes.Clear();
        }

        private void WriteNotes(string sample, Action<GuideWriter> write)
        {
            bool pause = _notes.IsHandleCreated;
            if (pause) SendMessage(_notes.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);   // one repaint instead of one per line
            try
            {
                _notes.Clear();
                write(new GuideWriter(_notes, DeviceDpi / 96f, MarkdownWriter.HasKhmer(sample)));
                _notes.SelectionStart = 0;
                _notes.SelectionLength = 0;
            }
            finally
            {
                if (pause)
                {
                    SendMessage(_notes.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    _notes.Invalidate();
                }
            }
            ScrollNotesToTop();
        }

        /// <summary>Appending text leaves the box scrolled to the end: show the start of the notes.</summary>
        private void ScrollNotesToTop()
        {
            if (_notes.IsHandleCreated) SendMessage(_notes.Handle, WM_VSCROLL, (IntPtr)SB_TOP, IntPtr.Zero);
        }

        private const int WM_SETREDRAW = 0x000B, WM_VSCROLL = 0x0115, SB_TOP = 6;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private static string Megabytes(long bytes)
        {
            return (bytes / 1048576.0).ToString("0.0") + " MB";
        }

        /// <summary>Thin rounded progress bar in the accent colour. <see cref="Fraction"/> &lt; 0 = size unknown (soft fill).</summary>
        private sealed class DownloadBar : Control
        {
            private float _fraction;

            public DownloadBar()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
                         ControlStyles.UserPaint, true);
            }

            public float Fraction
            {
                get { return _fraction; }
                set { _fraction = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.EffectiveBack(Parent));
                Theme.HighQuality(g);
                var track = new RectangleF(0, 0, Width - 1, Height - 1);
                float radius = track.Height / 2f;
                using (GraphicsPath p = Theme.RoundRect(track, radius))
                using (var b = new SolidBrush(Theme.Tile))
                    g.FillPath(b, p);

                float f = _fraction < 0 ? 1f : Math.Min(1f, _fraction);
                if (f <= 0) return;
                var fill = new RectangleF(0, 0, Math.Max(track.Height, track.Width * f), track.Height);
                using (GraphicsPath p = Theme.RoundRect(fill, radius))
                using (var b = new SolidBrush(_fraction < 0 ? Theme.AccentSoft : Theme.Accent))
                    g.FillPath(b, p);
            }
        }
    }
}
