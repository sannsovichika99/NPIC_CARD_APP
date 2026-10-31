using System;
using System.Drawing;
using System.Windows.Forms;

namespace CardStudio.Admin
{
    /// <summary>Small "type a text" window: a message, a label, a serial number, the list of addresses.</summary>
    internal sealed class Prompt : Form
    {
        private readonly TextBox _text;
        private readonly Func<string, string> _validate;

        private Prompt(string title, string help, string value, bool multiline, Func<string, string> validate, string extraButton,
                       Action extraAction)
        {
            _validate = validate;
            Text = title;
            Font = AdminForm.UiFont;
            BackColor = AdminForm.AppBack;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, multiline ? 360 : 170);
            Padding = new Padding(16);

            var lbl = new Label { Text = help, Dock = DockStyle.Top, AutoSize = false, Height = multiline ? 64 : 48, ForeColor = AdminForm.TextSoft };
            _text = new TextBox
            {
                Dock = DockStyle.Top,
                Text = value ?? "",
                Multiline = multiline,
                AcceptsReturn = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                Height = multiline ? 210 : 26,
                Font = multiline ? AdminForm.UiFont : AdminForm.Mono
            };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(0, 8, 0, 0) };
            var ok = new Button { Text = "OK", Width = 96, Height = 30 };
            var cancel = new Button { Text = "Cancel", Width = 96, Height = 30, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => Accept();
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            if (extraButton != null)
            {
                var extra = new Button { Text = extraButton, AutoSize = true, Height = 30 };
                extra.Click += (s, e) => extraAction();
                buttons.Controls.Add(extra);
            }

            Controls.Add(_text);
            Controls.Add(lbl);
            Controls.Add(buttons);
            if (!multiline) AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Accept()
        {
            string problem = _validate == null ? null : _validate(_text.Text);
            if (problem != null)
            {
                MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }

        /// <summary>The text typed, or null when cancelled. <paramref name="validate"/> returns why a text is refused, or null.</summary>
        public static string Ask(IWin32Window owner, string title, string help, string value = "", bool multiline = false,
                                 Func<string, string> validate = null, string extraButton = null, Action extraAction = null)
        {
            using (var p = new Prompt(title, help, value, multiline, validate, extraButton, extraAction))
                return p.ShowDialog(owner) == DialogResult.OK ? p._text.Text : null;
        }
    }
}
