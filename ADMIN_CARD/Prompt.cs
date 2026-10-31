using System;
using System.Drawing;
using System.Windows.Forms;
using NPIC_CARD_APP;
using NPIC_CARD_APP.Rendering;

namespace CardStudio.Admin
{
    /// <summary>
    /// "Type a text" popup in Card Studio's style (ModalForm header, help text, field, pill buttons in the footer):
    /// a message for the PCs, a label, a serial number, the list of addresses, the public key.
    /// </summary>
    internal sealed class Prompt : ModalForm
    {
        private readonly TextBox _text;
        private readonly Label _error;
        private readonly Func<string, string> _validate;

        private Prompt(string title, string subtitle, string glyph, string help, string value, bool multiline, bool readOnly,
                       Func<string, string> validate, string okText, string extraButton, Action<Prompt> extraAction)
        {
            _validate = validate;
            Text = title;
            Subtitle = subtitle;
            Glyph = glyph;
            Resizable = multiline;
            MinimumSize = new Size(520, multiline ? 420 : 300);
            ClientSize = new Size(multiline ? 620 : 560, ModalForm.HeaderHeight + (multiline ? 330 : 180) + ModalUi.FooterHeight);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(24, 18, 24, 8), BackColor = Theme.Surface
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(multiline ? SizeType.Percent : SizeType.AutoSize, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            body.Controls.Add(new Label
            {
                Text = help, AutoSize = true, MaximumSize = new Size(ClientSize.Width - 48, 0), ForeColor = Theme.TextSoft,
                BackColor = Theme.Surface, Margin = new Padding(0, 0, 0, 12)
            }, 0, 0);

            _text = new TextBox
            {
                Dock = DockStyle.Fill, Text = value ?? "", Multiline = multiline, AcceptsReturn = multiline, ReadOnly = readOnly,
                ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain, Margin = new Padding(0),
                Font = readOnly || !multiline ? new Font("Consolas", 11f) : new Font(CardFonts.UI, 10.5f)
            };
            if (!multiline) _text.Height = 32;
            _text.TextChanged += (s, e) => { _error.Visible = false; UpdateScrollBar(); };
            _text.Resize += (s, e) => UpdateScrollBar();
            body.Controls.Add(_text, 0, 1);

            _error = new Label
            {
                AutoSize = true, MaximumSize = new Size(ClientSize.Width - 48, 0), ForeColor = Theme.Danger, BackColor = Theme.Surface,
                Margin = new Padding(0, 8, 0, 0), Visible = false
            };
            body.Controls.Add(_error, 0, 2);

            // Footer: first added = rightmost, so Cancel then OK gives the usual [OK] [Cancel] order.
            FlowLayoutPanel footer = ModalUi.Footer();
            PillButton cancel = ModalUi.Button(readOnly ? "Close" : "Cancel", PillKind.Secondary, (s, e) => Close());
            cancel.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(cancel);
            if (!readOnly)
            {
                PillButton ok = ModalUi.Button(okText ?? "OK", PillKind.Primary, (s, e) => Accept());
                footer.Controls.Add(ok);
                if (!multiline) AcceptButton = ok;
            }
            if (extraButton != null)
                footer.Controls.Add(ModalUi.Button(extraButton, PillKind.Ghost, (s, e) => extraAction(this)));

            Controls.Add(body);
            Controls.Add(footer);
            CancelButton = cancel;
            Shown += (s, e) => { UpdateScrollBar(); _text.Focus(); if (!multiline) _text.SelectAll(); };
        }

        public string Value { get { return _text.Text; } }

        /// <summary>A multi-line field shows its scroll bar only while the text is longer than the field.</summary>
        private void UpdateScrollBar()
        {
            if (!_text.Multiline || !_text.IsHandleCreated) return;
            int lines = _text.GetLineFromCharIndex(_text.TextLength) + 1;
            bool overflow = lines * _text.Font.Height > _text.ClientSize.Height - 4;
            ScrollBars wanted = overflow ? ScrollBars.Vertical : ScrollBars.None;
            if (_text.ScrollBars != wanted) _text.ScrollBars = wanted;
        }

        /// <summary>Shows a short note under the field (e.g. "Copied").</summary>
        public void Note(string text, bool problem = false)
        {
            _error.ForeColor = problem ? Theme.Danger : Theme.Success;
            _error.Text = text;
            _error.Visible = true;
        }

        private void Accept()
        {
            string problem = _validate == null ? null : _validate(_text.Text);
            if (problem != null)
            {
                Note(problem, true);
                _text.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        }

        /// <summary>
        /// The text typed, or null when cancelled. <paramref name="validate"/> returns why a text is refused (shown in red under
        /// the field), or null. <paramref name="extraButton"/> adds a Ghost pill on the left of the footer.
        /// </summary>
        public static string Ask(IWin32Window owner, string title, string subtitle, string glyph, string help, string value = "",
                                 bool multiline = false, Func<string, string> validate = null, string okText = null,
                                 string extraButton = null, Action<Prompt> extraAction = null)
        {
            using (var p = new Prompt(title, subtitle, glyph, help, value, multiline, false, validate, okText, extraButton, extraAction))
                return p.ShowDialog(owner) == DialogResult.OK ? p.Value : null;
        }

        /// <summary>Shows a read-only value (e.g. the public key) with an optional extra button such as Copy.</summary>
        public static void Show(IWin32Window owner, string title, string subtitle, string glyph, string help, string value,
                                string extraButton = null, Action<Prompt> extraAction = null)
        {
            using (var p = new Prompt(title, subtitle, glyph, help, value, false, true, null, null, extraButton, extraAction))
                p.ShowDialog(owner);
        }
    }
}
