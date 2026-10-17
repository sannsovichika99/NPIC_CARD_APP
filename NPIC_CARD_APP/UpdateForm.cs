using System;
using System.Diagnostics;
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
    /// "Check for Updates" page (Ctrl+U, or Settings &amp; Help → Check for Updates):
    ///   Update address = where new versions are published (network folder or https://), saved in <see cref="AppSettings.UpdateUrl"/>.
    ///   Check Now (F5) = compare the published version with this one.
    ///   Install Update = download the setup, verify it, start it and close Card Studio; the setup updates the app in place and
    ///   keeps settings, print history and card images.
    ///   Skip This Version = the startup check stays quiet about this version.
    /// The page checks as soon as it opens, unless it was opened by the startup check with the update already found.
    /// </summary>
    public class UpdateForm : ModalForm
    {
        private enum State { NoSource, Checking, UpToDate, Available, Failed, Downloading }

        private const string SourceHelp =
            "Where are new versions published?\n\n" +
            "•  A network folder, for example  \\\\server\\Software\\CardStudio\n" +
            "•  An https:// address of a web folder\n" +
            "•  A GitHub repository with releases, for example  https://github.com/owner/repo\n\n" +
            "The folder must hold the two files that Installer\\build.ps1 writes to dist\\:\n" +
            "update.json and CardStudio-Setup.exe. Copy both there for every new version.\n\n" +
            "Ask whoever maintains Card Studio for the address, enter it above, then click Check Now.";

        private readonly AppSettings _settings;
        private readonly TextBox _txtSource, _txtNotes;
        private readonly Label _lblHeadline, _lblVersions, _lblNotesTitle, _lblStatus;
        private readonly LinkLabel _lnkPage;
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
            Subtitle = "Card Studio " + UpdateService.CurrentVersionText + " is installed on this PC";
            Glyph = Theme.IcoSync;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(760, work.Width), Math.Min(600, work.Height));
            MinimumSize = new Size(Math.Min(600, work.Width), Math.Min(480, work.Height));
            KeyPreview = true;

            // ---- Address bar: Update address [______________________] [Check Now] ----
            _txtSource = new TextBox { Dock = DockStyle.Fill, Text = settings.UpdateUrl ?? "", Margin = new Padding(0, 9, 10, 0) };
            _txtSource.HandleCreated += (s, e) => SendMessage(_txtSource.Handle, EM_SETCUEBANNER, (IntPtr)1,
                UpdateService.DefaultSource.Length > 0
                    ? "Default: " + UpdateService.DefaultSource
                    : @"\\server\Software\CardStudio   or   https://…");
            _txtSource.TextChanged += (s, e) => UpdateButtons();

            _btnCheck = ModalUi.Button("Check Now (F5)", PillKind.Primary, async (s, e) => await CheckAsync());
            _btnCheck.Margin = new Padding(0, 4, 6, 0);

            var addressBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 52, ColumnCount = 3, RowCount = 1, BackColor = Theme.SurfaceAlt,
                Padding = new Padding(16, 4, 10, 6)
            };
            addressBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            addressBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            addressBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            addressBar.Controls.Add(new Label
            {
                Text = "Update address", AutoSize = true, ForeColor = Theme.TextSoft, Margin = new Padding(0, 12, 10, 0)
            }, 0, 0);
            addressBar.Controls.Add(_txtSource, 1, 0);
            addressBar.Controls.Add(_btnCheck, 2, 0);

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
            _lnkPage = new LinkLabel
            {
                AutoSize = true, Text = "Open release page", Margin = new Padding(12, 0, 0, 6), Visible = false,
                LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent, VisitedLinkColor = Theme.Accent
            };
            _lnkPage.LinkClicked += (s, e) => OpenReleasePage();
            var notesHeader = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Dock = DockStyle.Fill };
            notesHeader.Controls.Add(_lblNotesTitle);
            notesHeader.Controls.Add(_lnkPage);

            _txtNotes = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                BorderStyle = BorderStyle.None, BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain, TabStop = false,
                Margin = new Padding(0, 0, 0, 10)
            };
            var notesHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.SurfaceAlt, Padding = new Padding(12, 10, 6, 10), Margin = new Padding(0, 0, 0, 10) };
            notesHost.Controls.Add(_txtNotes);

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
            body.Controls.Add(notesHeader, 0, 2);
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
            footer.Controls.Add(_btnClose);
            footer.Controls.Add(_btnInstall);
            footer.Controls.Add(_btnSkip);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(body);
            Controls.Add(addressBar);
            Controls.Add(_lblStatus);
            Controls.Add(footer);
            CancelButton = _btnClose;

            var tip = new ToolTip();
            tip.SetToolTip(_btnCheck, "Look for a newer version at the update address");
            tip.SetToolTip(_btnInstall, "Download the new version, check it, and update Card Studio. Settings, print history and card images are kept.");
            tip.SetToolTip(_btnSkip, "Don't announce this version again when Card Studio starts. Check Now still shows it.");

            if (_found != null) ShowAvailable(_found);
            else if (UpdateService.SourceOf(settings).Length == 0) ShowNoSource();
            else SetState(State.Checking, "Checking for updates…", Theme.TextMain);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_state == State.NoSource) { _txtSource.Focus(); return; }
            if (_state == State.Available) { _btnInstall.Focus(); return; }
            _btnCheck.Focus();
            await CheckAsync();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5) { BeginCheck(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && _txtSource.Focused) { BeginCheck(); e.Handled = e.SuppressKeyPress = true; }
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

            string typed = _txtSource.Text.Trim();
            string source = typed.Length > 0 ? typed : UpdateService.DefaultSource;
            if (source.Length == 0) { ShowNoSource(); _txtSource.Focus(); return; }

            _cts = new CancellationTokenSource();
            _found = null;
            SetState(State.Checking, "Checking for updates…", Theme.TextMain);
            ShowStatus("Reading " + source + " …", Theme.TextSoft);
            try
            {
                UpdateInfo info = await UpdateService.CheckAsync(source, _cts.Token);
                _settings.UpdateUrl = typed;   // remembered once it worked ("" = the default source)
                _settings.LastUpdateCheck = DateTime.Now;
                if (info.IsNewerThanRunning) ShowAvailable(info);
                else ShowUpToDate(info);
            }
            catch (OperationCanceledException)
            {
                SetState(State.Failed, "Check cancelled", Theme.TextMain);
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

        private void ShowNoSource()
        {
            SetState(State.NoSource, "Set where updates are published", Theme.TextMain);
            _lblVersions.Text = "Installed version: " + UpdateService.CurrentVersionText;
            _txtNotes.Text = ToWindowsLines(SourceHelp);
            ShowStatus("No update address yet.", Theme.TextSoft);
        }

        private void ShowUpToDate(UpdateInfo latest)
        {
            SetState(State.UpToDate, "✔  Card Studio is up to date", Theme.Success);
            _lblVersions.Text = "Installed " + UpdateService.CurrentVersionText + "   ·   Latest published " + latest.VersionText +
                                (latest.ReleaseDate.HasValue ? "  (" + latest.ReleaseDate.Value.ToString("d MMM yyyy") + ")" : "");
            _txtNotes.Text = ToWindowsLines(latest.Notes.Length > 0 ? latest.Notes : "No release notes were published for this version.");
            _lnkPage.Visible = !string.IsNullOrEmpty(latest.PageUrl);
            ShowStatus(string.Format("Checked {0:HH:mm} — you have the newest version.", DateTime.Now), Theme.Success);
        }

        private void ShowAvailable(UpdateInfo info)
        {
            _found = info;
            SetState(State.Available, "Card Studio " + info.VersionText + " is available", Theme.Accent);
            _lblVersions.Text = "Installed " + UpdateService.CurrentVersionText + "   →   New " + info.VersionText +
                                (info.ReleaseDate.HasValue ? "   ·   Released " + info.ReleaseDate.Value.ToString("d MMM yyyy") : "") +
                                (info.Size > 0 ? "   ·   " + Megabytes(info.Size) : "");
            _txtNotes.Text = ToWindowsLines(info.Notes.Length > 0 ? info.Notes : "No release notes were published for this version.");
            _lnkPage.Visible = !string.IsNullOrEmpty(info.PageUrl);

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
            _txtNotes.Text = ToWindowsLines(Describe(ex) + "\n\n" + SourceHelp);
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

        private void OpenReleasePage()
        {
            string url = _found != null ? _found.PageUrl : null;
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(url); }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not open the browser:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ===================== UI state =====================

        private void SetState(State state, string headline, Color color)
        {
            _state = state;
            _lblHeadline.Text = headline;
            _lblHeadline.ForeColor = color;
            if (state != State.UpToDate && state != State.Available) _lnkPage.Visible = false;
            _lblNotesTitle.Text = state == State.Available || state == State.UpToDate || state == State.Downloading ? "WHAT'S NEW" :
                                  state == State.Failed ? "DETAILS" : "HOW TO SET IT UP";
            if (state == State.Checking)
            {
                _lblVersions.Text = "Installed version: " + UpdateService.CurrentVersionText;
                _txtNotes.Text = "";
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool busy = _cts != null;
            _btnCheck.Enabled = !busy;
            _txtSource.ReadOnly = busy;
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
            return message;
        }

        private static string FirstLine(string text)
        {
            return (text ?? "").Split('\n')[0].Trim();
        }

        private static string ToWindowsLines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        private static string Megabytes(long bytes)
        {
            return (bytes / 1048576.0).ToString("0.0") + " MB";
        }

        // ===================== Win32 =====================
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// <summary>Thin rounded progress bar in the accent colour. <see cref="Fraction"/> &lt; 0 = size unknown (striped).</summary>
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
