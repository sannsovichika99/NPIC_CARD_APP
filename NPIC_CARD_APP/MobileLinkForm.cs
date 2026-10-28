using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "Mobile App" page: connects the Card Studio phone app (folder "mobile") to this PC over the local Wi-Fi.
    /// Shows the pairing QR code, the PC's addresses, Start / Stop, a new pairing code, Allow in Firewall,
    /// and what the phones did (connected, photo saved, wrong code…).
    /// </summary>
    public class MobileLinkForm : ModalForm
    {
        private static readonly Font MonoFont = new Font("Consolas", 10.5f);
        private const string FirewallRuleName = "Card Studio Mobile App";

        private readonly MobileLinkServer _server;
        private readonly AppSettings _settings;
        private readonly Action _saveSettings;

        private readonly PictureBox _qr;
        private readonly Label _lblQrHint, _lblState, _lblAddresses, _lblCode;
        private readonly NumericUpDown _numPort;
        private readonly CheckBox _chkAutoStart;
        private readonly ListView _log;
        private readonly PillButton _btnStart;

        /// <param name="saveSettings">Saves <paramref name="settings"/> to disk (the main window's settings object).</param>
        public MobileLinkForm(MobileLinkServer server, AppSettings settings, Action saveSettings)
        {
            _server = server;
            _settings = settings;
            _saveSettings = saveSettings;

            Text = "Mobile App";
            Subtitle = "Connect the Card Studio phone app over Wi-Fi: browse students, take photos, check cards";
            Glyph = Theme.IcoPhone;
            KeyPreview = true;
            Resizable = true;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(900, work.Width), Math.Min(640, work.Height));
            MinimumSize = new Size(Math.Min(760, work.Width), Math.Min(560, work.Height));

            // ---- Left: pairing QR ----
            var left = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = Theme.Surface, Padding = new Padding(22, 18, 10, 12) };
            _qr = new PictureBox
            {
                Dock = DockStyle.Top, Height = 260, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Surface
            };
            _lblQrHint = new Label
            {
                Dock = DockStyle.Fill, ForeColor = Theme.TextSoft, Padding = new Padding(0, 10, 0, 0), UseMnemonic = false,
                Text = "Open the Card Studio app on the phone and tap Scan PC QR code. The phone must be on the same Wi-Fi as this PC."
            };
            left.Controls.Add(_lblQrHint);
            left.Controls.Add(_qr);

            // ---- Right: state, settings, activity ----
            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Surface, Padding = new Padding(10, 18, 22, 12)
            };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _lblState = Value("", true);
            _lblAddresses = Value("", false);
            _lblCode = Value("", false);
            _lblCode.Font = MonoFont;
            _numPort = new NumericUpDown
            {
                Minimum = 1024, Maximum = 65535, Width = 90, Margin = new Padding(0, 4, 0, 4),
                Value = Clamp(settings.MobileLinkPort <= 0 ? MobileLinkServer.DefaultPort : settings.MobileLinkPort)
            };
            _numPort.ValueChanged += (s, e) =>
            {
                _settings.MobileLinkPort = (int)_numPort.Value;
                _saveSettings();
                if (_server.Running) StartServer();   // move to the new port straight away
                else RefreshState();
            };
            _chkAutoStart = new CheckBox
            {
                Text = "Start when Card Studio starts", AutoSize = true, Checked = settings.MobileLinkEnabled,
                Margin = new Padding(0, 6, 0, 6), ForeColor = Theme.TextMain
            };
            _chkAutoStart.CheckedChanged += (s, e) => { _settings.MobileLinkEnabled = _chkAutoStart.Checked; _saveSettings(); };

            _log = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain,
                Margin = new Padding(0, 4, 0, 0)
            };
            _log.Columns.Add("Time", 70);
            _log.Columns.Add("Phone", 170);
            _log.Columns.Add("What happened", 300);
            _log.Resize += (s, e) => _log.Columns[2].Width = Math.Max(120, _log.ClientSize.Width - 240 - 4);

            AddRow(right, "Status", _lblState);
            AddRow(right, "PC address", _lblAddresses);
            AddRow(right, "Port", _numPort);
            AddRow(right, "Pairing code", _lblCode);
            AddRow(right, "", _chkAutoStart);
            var logTitle = new Label
            {
                Text = "Activity", AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Theme.TextMain,
                Margin = new Padding(0, 10, 0, 0)
            };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.Controls.Add(logTitle, 0, right.RowCount);
            right.SetColumnSpan(logTitle, 2);
            right.RowCount++;
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            right.Controls.Add(_log, 0, right.RowCount);
            right.SetColumnSpan(_log, 2);
            right.RowCount++;

            // ---- Footer ----
            var footer = ModalUi.Footer();
            Button btnClose = ModalUi.Button("Close", PillKind.Secondary, (s, e) => DialogResult = DialogResult.Cancel);
            _btnStart = ModalUi.Button("Start", PillKind.Primary, (s, e) => { if (_server.Running) StopServer(); else StartServer(); });
            footer.Controls.Add(btnClose);
            footer.Controls.Add(_btnStart);
            footer.Controls.Add(ModalUi.Button("New Pairing Code", PillKind.Secondary, (s, e) => NewCode()));
            footer.Controls.Add(ModalUi.Button("Allow in Firewall", PillKind.Secondary, (s, e) => AllowInFirewall()));
            footer.Controls.Add(ModalUi.Button("Copy Link", PillKind.Ghost, (s, e) => CopyLink()));

            Controls.Add(right);
            Controls.Add(left);
            Controls.Add(footer);
            CancelButton = btnClose;

            foreach (MobileLinkEvent ev in _server.RecentEvents()) AddEvent(ev);
            _server.Activity += OnActivity;
            RefreshState();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _server.Activity -= OnActivity;
            ReplaceQr(null);
            base.OnFormClosed(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C) && !(ActiveControl is NumericUpDown)) { CopyLink(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ===================== Actions =====================

        /// <summary>The pairing code, made the first time it is needed (or when the saved one belongs to another Windows user).</summary>
        private string Token()
        {
            string token = _settings.MobileLinkToken;
            if (string.IsNullOrEmpty(token))
            {
                token = MobileLinkServer.NewToken();
                _settings.MobileLinkToken = token;
                _saveSettings();
            }
            return token;
        }

        private void StartServer()
        {
            int port = (int)_numPort.Value;
            try
            {
                _server.Start(port, Token());
                if (!_settings.MobileLinkEnabled) _chkAutoStart.Checked = true;   // started once = wanted every time
            }
            catch (SocketException ex)
            {
                AppDialog.Show(this, ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                        ? Lang.F("Port {0} is already used by another program. Choose another port, e.g. {1}.", port, port + 1)
                        : Lang.F("The mobile app link could not start on port {0}:", port) + "\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshState();
        }

        private void StopServer()
        {
            _server.Stop();
            _chkAutoStart.Checked = false;
            RefreshState();
        }

        private void NewCode()
        {
            if (AppDialog.Show(this, "Make a new pairing code?\n\nPhones that are connected now must scan the new QR code again. " +
                                     "Use this when a phone was lost, or the code was shared by mistake.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            string token = MobileLinkServer.NewToken();
            _settings.MobileLinkToken = token;
            _saveSettings();
            if (_server.Running) _server.ChangeToken(token);
            RefreshState();
        }

        /// <summary>
        /// Adds an inbound rule for this exe on the chosen port (private and domain networks only, never public Wi-Fi).
        /// Needs administrator rights, so Windows asks first.
        /// </summary>
        private void AllowInFirewall()
        {
            int port = (int)_numPort.Value;
            string exe = Application.ExecutablePath;
            string args = string.Format(
                "/c netsh advfirewall firewall delete rule name=\"{0}\" >nul & " +
                "netsh advfirewall firewall add rule name=\"{0}\" dir=in action=allow protocol=TCP localport={1} " +
                "profile=private,domain program=\"{2}\"", FirewallRuleName, port, exe);
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo("cmd.exe", args)
                       { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }))
                {
                    p.WaitForExit(15000);
                    bool ok = p.HasExited && p.ExitCode == 0;
                    AppDialog.Show(this, ok
                            ? Lang.F("Windows Firewall now lets phones on private networks reach Card Studio on port {0}.", port)
                            : "Windows Firewall did not accept the rule. Ask your IT administrator to allow Card Studio on this port.",
                        Text, MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { /* the user said No to the admin prompt */ }
            catch (Win32Exception ex)
            {
                AppDialog.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CopyLink()
        {
            try
            {
                Clipboard.SetText(MobileLinkServer.PairingUri(MobileLinkServer.LocalAddresses(), (int)_numPort.Value, Token()));
                AppDialog.Show(this, "The pairing link is copied. Paste it in the phone app (Connect → Paste link) if the camera cannot scan the QR code.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (System.Runtime.InteropServices.ExternalException) { }
        }

        // ===================== State =====================
        private void RefreshState()
        {
            bool running = _server.Running;
            List<string> hosts = MobileLinkServer.LocalAddresses();
            int port = (int)_numPort.Value;

            _lblState.Text = running ? Lang.F("● Running — phones can connect on port {0}", _server.Port) : Lang.T("● Stopped — press Start");
            _lblState.ForeColor = running ? Theme.Success : Theme.TextMuted;
            _lblAddresses.Text = hosts.Count == 0
                ? Lang.T("No network found. Connect this PC to Wi-Fi or a network cable.")
                : string.Join("   ", hosts.ConvertAll(h => h + ":" + port));
            _lblAddresses.ForeColor = hosts.Count == 0 ? Theme.Warning : Theme.TextMain;
            string token = Token();
            _lblCode.Text = token.Substring(0, 4) + " " + token.Substring(4, 4) + " ••••••••••••••••••";
            _btnStart.Text = Lang.T(running ? "Stop" : "Start");

            if (running && hosts.Count > 0)
            {
                ReplaceQr(DrawQr(MobileLinkServer.PairingUri(hosts, _server.Port, token)));
                _lblQrHint.Text = Lang.T("Open the Card Studio app on the phone and tap Scan PC QR code. The phone must be on the same Wi-Fi as this PC.") +
                                  "\n\n" + Lang.T("Anyone who scans this code can see the student list and change photos. Show it only to staff.");
                _lblQrHint.ForeColor = Theme.TextSoft;
            }
            else
            {
                ReplaceQr(null);
                _lblQrHint.Text = Lang.T(running ? "No network found. Connect this PC to Wi-Fi or a network cable."
                                                 : "Press Start to show the QR code for the phone.");
                _lblQrHint.ForeColor = running ? Theme.Warning : Theme.TextSoft;
            }
        }

        private void OnActivity(MobileLinkEvent e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)(() => { if (!IsDisposed) AddEvent(e); })); }
            catch (InvalidOperationException) { }
        }

        private void AddEvent(MobileLinkEvent e)
        {
            var item = new ListViewItem(new[] { e.At.ToString("HH:mm:ss"), e.Device, Lang.T(e.Text) })
            {
                ForeColor = e.Problem ? Theme.Danger : Theme.TextMain
            };
            _log.Items.Insert(0, item);
            while (_log.Items.Count > 200) _log.Items.RemoveAt(_log.Items.Count - 1);
        }

        // ===================== Helpers =====================

        /// <summary>The QR code as black squares on white, with the 4-module quiet zone phone cameras need.</summary>
        private static Bitmap DrawQr(string text)
        {
            QrEncoder qr = QrEncoder.Encode(text, QrEcc.Medium);
            const int quiet = 4, scale = 8;
            int size = (qr.Size + quiet * 2) * scale;
            var bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                for (int y = 0; y < qr.Size; y++)
                    for (int x = 0; x < qr.Size; x++)
                        if (qr.IsDark(x, y)) g.FillRectangle(Brushes.Black, (x + quiet) * scale, (y + quiet) * scale, scale, scale);
            }
            return bmp;
        }

        private void ReplaceQr(Image image)
        {
            Image old = _qr.Image;
            _qr.Image = image;
            if (old != null) old.Dispose();
        }

        private static decimal Clamp(int port)
        {
            return Math.Max(1024, Math.Min(65535, port));
        }

        private static Label Value(string text, bool bold)
        {
            var l = new Label
            {
                Text = text, AutoSize = true, ForeColor = Theme.TextMain, UseMnemonic = false,
                Margin = new Padding(0, 6, 0, 6), MaximumSize = new Size(520, 0)
            };
            if (bold) l.Font = new Font(Control.DefaultFont, FontStyle.Bold);
            return l;
        }

        private static void AddRow(TableLayoutPanel table, string caption, Control value)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label
            {
                Text = caption, AutoSize = true, ForeColor = Theme.TextSoft, Margin = new Padding(0, 6, 8, 6)
            }, 0, table.RowCount);
            table.Controls.Add(value, 1, table.RowCount);
            table.RowCount++;
        }
    }
}
