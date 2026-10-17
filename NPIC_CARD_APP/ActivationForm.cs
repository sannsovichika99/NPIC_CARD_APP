using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "Activate Card Studio": shown before the main window until a serial number valid for this PC is entered
    /// (see <see cref="LicenseService"/>). Shows the Machine ID to send to the administrator, takes the serial they send back.
    /// Exit (or the X) closes the app.
    /// </summary>
    public sealed class ActivationForm : ModalForm
    {
        private const int BodyWidth = 520;
        private static readonly Font MonoFont = new Font("Consolas", 11f);
        private static readonly Font SerialFont = new Font("Consolas", 10f);
        private static readonly Font CaptionFont = new Font(CardFonts.UI, 8.25f, FontStyle.Bold);

        private readonly TextBox _txtSerial;
        private readonly Label _lblError;
        private readonly Button _btnCopy;
        private readonly Timer _copiedTimer = new Timer { Interval = 1500 };
        private Size _clientSize;

        /// <summary>
        /// True when Card Studio may open: already activated, or the user has just entered a valid serial.
        /// False = the user chose Exit.
        /// </summary>
        public static bool EnsureActivated()
        {
            LicenseState state = LicenseService.Check();
            if (state == LicenseState.Activated) return true;
            using (var form = new ActivationForm(state))
                return form.ShowDialog() == DialogResult.OK;
        }

        private ActivationForm(LicenseState state)
        {
            Text = "Activate Card Studio";
            Subtitle = "A serial number is required to open the app";
            Glyph = Theme.IcoKey;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;   // the first window of the app: it must be findable on the taskbar
            DimOwner = false;
            Resizable = false;
            KeyPreview = true;

            // ---- Body ----
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Surface, Padding = new Padding(24, 18, 24, 12)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var intro = new Label
            {
                AutoSize = true, MaximumSize = new Size(BodyWidth, 0), ForeColor = Theme.TextMain, Margin = new Padding(0, 0, 0, 4),
                Text = "Each PC needs its own serial number. Send the Machine ID below to your administrator, " +
                       "then paste the serial number you receive and click Activate."
            };
            var introKh = new Label
            {
                AutoSize = true, MaximumSize = new Size(BodyWidth, 0), ForeColor = Theme.TextSoft, Margin = new Padding(0, 0, 0, 14),
                Text = "កុំព្យូទ័រនីមួយៗត្រូវការលេខសៀរៀលផ្ទាល់ខ្លួន។ សូមផ្ញើ Machine ID ខាងក្រោមទៅអ្នកគ្រប់គ្រង " +
                       "បន្ទាប់មកបិទភ្ជាប់លេខសៀរៀលដែលទទួលបាន ហើយចុច Activate។"
            };
            body.Controls.Add(intro, 0, 0);
            body.SetColumnSpan(intro, 2);
            body.Controls.Add(introKh, 0, 1);
            body.SetColumnSpan(introKh, 2);

            // Machine ID  [7K2Q-M9XD-4TRB-0HCA        ] [Copy]
            body.Controls.Add(Caption("MACHINE ID"), 0, 2);
            var txtMachine = new TextBox
            {
                Text = LicenseService.MachineId, ReadOnly = true, Font = MonoFont, Anchor = AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain, Margin = new Padding(0, 0, 8, 14), TabStop = false
            };
            _btnCopy = ModalUi.Button("Copy", PillKind.Secondary, (s, e) => CopyMachineId());
            _btnCopy.Margin = new Padding(0, 0, 0, 14);
            body.Controls.Add(txtMachine, 0, 3);
            body.Controls.Add(_btnCopy, 1, 3);

            // Serial number (multi-line: 105 characters wrap)
            body.Controls.Add(Caption("SERIAL NUMBER"), 0, 4);
            _txtSerial = new TextBox
            {
                Multiline = true, AcceptsReturn = false, WordWrap = true, Font = SerialFont, AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Width = BodyWidth, Height = 72, MinimumSize = new Size(0, 72),
                Margin = new Padding(0, 4, 0, 6), CharacterCasing = CharacterCasing.Upper,
                MaxLength = 400
            };
            _txtSerial.TextChanged += (s, e) => ShowError(null);
            body.Controls.Add(_txtSerial, 0, 5);
            body.SetColumnSpan(_txtSerial, 2);

            _lblError = new Label
            {
                AutoSize = true, MaximumSize = new Size(BodyWidth, 0), ForeColor = Theme.Danger, Margin = new Padding(0, 2, 0, 0),
                Visible = false
            };
            body.Controls.Add(_lblError, 0, 6);
            body.SetColumnSpan(_lblError, 2);

            // ---- Footer: [Exit] [Activate] ----
            var footer = ModalUi.Footer();
            var btnActivate = ModalUi.Button("Activate", PillKind.Primary, (s, e) => TryActivate());
            var btnExit = ModalUi.Button("Exit", PillKind.Secondary);
            btnExit.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(btnActivate);   // right-to-left flow: first = rightmost
            footer.Controls.Add(btnExit);
            AcceptButton = btnActivate;
            CancelButton = btnExit;

            Controls.Add(body);
            Controls.Add(footer);

            _copiedTimer.Tick += (s, e) => { _copiedTimer.Stop(); _btnCopy.Text = "Copy"; };

            if (state == LicenseState.Invalid)
                ShowError("The saved serial number is not valid for this PC (it was made for another PC, or Windows was " +
                          "reinstalled). Ask your administrator for a new serial for this Machine ID.");

            Size pref = body.GetPreferredSize(new Size(BodyWidth + 48, 0));
            _clientSize = new Size(BodyWidth + 48, HeaderHeight + pref.Height + 8 + ModalUi.FooterHeight);
            ClientSize = _clientSize;
        }

        private static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = CaptionFont, ForeColor = Theme.TextSoft, Margin = new Padding(0) };
        }

        private void TryActivate()
        {
            string error = LicenseService.Activate(_txtSerial.Text);
            if (error == null)
            {
                DialogResult = DialogResult.OK;
                return;
            }
            ShowError(error);
            _txtSerial.Focus();
            _txtSerial.SelectAll();
        }

        private void ShowError(string message)
        {
            bool show = !string.IsNullOrEmpty(message);
            if (!show && !_lblError.Visible) return;
            _lblError.Text = message ?? "";
            _lblError.Visible = show;
            if (IsHandleCreated) FitHeight();
        }

        /// <summary>Grows / shrinks the window so the error line always fits.</summary>
        private void FitHeight()
        {
            Control body = _txtSerial.Parent;
            Size pref = body.GetPreferredSize(new Size(ClientSize.Width, 0));
            ClientSize = new Size(ClientSize.Width, HeaderHeight + pref.Height + 8 + ModalUi.FooterHeight);
        }

        private void CopyMachineId()
        {
            try
            {
                Clipboard.SetText(LicenseService.MachineId);
                _btnCopy.Text = "Copied";
                _copiedTimer.Stop();
                _copiedTimer.Start();
            }
            catch (ExternalException)
            {
                // Clipboard busy (another app has it open): the ID can still be selected and copied by hand.
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            ClientSize = _clientSize;   // again now that the real frame is known; before base.OnLoad centres the window
            base.OnLoad(e);
            if (_lblError.Visible) FitHeight();
            ActiveControl = _txtSerial;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _copiedTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
