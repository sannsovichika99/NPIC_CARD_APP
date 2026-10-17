using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CardStudio.Setup
{
    /// <summary>Uninstall dialog: Confirm → Progress → Finished.</summary>
    internal sealed class UninstallForm : Form
    {
        public int ExitCode = ExitCodes.Cancelled;

        private readonly string _dir;
        private readonly Panel _confirmPage, _progressPage, _donePage;
        private readonly CheckBox _purge;
        private readonly ProgressBar _bar;
        private readonly Label _status, _doneDetail;
        private readonly Button _primary, _secondary;
        private bool _busy;

        public UninstallForm(string installDir, bool purge)
        {
            _dir = installDir;

            SuspendLayout();
            SetupUi.InitDialog(this, "Uninstall " + ProductInfo.Name);

            _confirmPage = SetupUi.Page();
            SetupUi.Text(_confirmPage, ProductInfo.Name + " will be removed from this PC:", 24, 20);
            SetupUi.Text(_confirmPage,
                "•  the program in " + installDir + Environment.NewLine +
                "•  the desktop and Start Menu shortcuts" + Environment.NewLine +
                "•  its entry in Settings > Apps", 24, 46, soft: true);
            _purge = new CheckBox
            {
                Text = "Also delete my settings, print history and uploaded images", AutoSize = true,
                Location = new Point(24, 132), Checked = purge
            };
            _confirmPage.Controls.Add(_purge);
            SetupUi.Text(_confirmPage, ProductInfo.UserDataFolder + Environment.NewLine +
                                       "Leave this unchecked if you plan to reinstall Card Studio.", 42, 156, soft: true);

            _progressPage = SetupUi.Page();
            SetupUi.Text(_progressPage, "Removing " + ProductInfo.Name + "…", 24, 28);
            _bar = new ProgressBar { Location = new Point(24, 60), Size = new Size(512, 18), Maximum = 100 };
            _status = SetupUi.Text(_progressPage, "", 24, 88, soft: true);
            _progressPage.Controls.Add(_bar);

            _donePage = SetupUi.Page();
            var title = SetupUi.Text(_donePage, ProductInfo.Name + " was removed.", 24, 28);
            title.Font = new Font("Segoe UI Semibold", 12F);
            _doneDetail = SetupUi.Text(_donePage, "", 24, 64, soft: true);

            Panel footer = SetupUi.Footer();
            _primary = SetupUi.FooterButton(footer, "Uninstall", 1, true);
            _primary.BackColor = SetupUi.Danger;
            _primary.FlatAppearance.BorderColor = SetupUi.Danger;
            _secondary = SetupUi.FooterButton(footer, "Cancel", 0, false);
            _primary.Click += PrimaryClick;
            _secondary.Click += (s, e) => Close();
            CancelButton = _secondary;

            Controls.Add(_confirmPage);
            Controls.Add(_progressPage);
            Controls.Add(_donePage);
            Controls.Add(footer);
            Controls.Add(SetupUi.Header("Uninstall " + ProductInfo.Name, "Version " + (InstallEngine.ExistingVersion() ?? ProductInfo.Version)));
            ShowPage(_confirmPage);
            ResumeLayout(false);
        }

        private void ShowPage(Panel page)
        {
            foreach (Panel p in new[] { _confirmPage, _progressPage, _donePage }) p.Visible = p == page;
            page.BringToFront();
        }

        private async void PrimaryClick(object sender, EventArgs e)
        {
            if (_donePage.Visible) { Close(); return; }

            while (InstallEngine.IsAppRunning())
            {
                if (MessageBox.Show(this, ProductInfo.Name + " is running. Close it, then click Retry.", Text,
                                    MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry)
                    return;
            }

            ShowPage(_progressPage);
            _primary.Enabled = _secondary.Enabled = false;
            _busy = true;
            bool purge = _purge.Checked;
            var progress = new Progress<InstallProgress>(p => { _bar.Value = p.Percent; _status.Text = p.Status; });
            try
            {
                List<string> leftovers = await Task.Run(() => UninstallEngine.Uninstall(_dir, purge, progress));
                ExitCode = ExitCodes.Success;
                _doneDetail.Text = leftovers.Count == 0
                    ? (purge ? "Your settings and print history were deleted too." : "Your settings and print history in " + ProductInfo.UserDataFolder + " were kept.")
                    : "Some items could not be removed (they may be open in another program):" + Environment.NewLine +
                      string.Join(Environment.NewLine, leftovers);
                ShowPage(_donePage);
                _primary.Text = "Close";
                _primary.BackColor = SetupUi.Accent;
                _primary.FlatAppearance.BorderColor = SetupUi.Accent;
                _primary.Enabled = true;
                _secondary.Visible = false;
            }
            catch (Exception ex)
            {
                ExitCode = ExitCodes.Failed;
                MessageBox.Show(this, "Uninstall could not finish:" + Environment.NewLine + Environment.NewLine + ex.Message +
                                      Environment.NewLine + Environment.NewLine + "Details: " + Log.FilePath,
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
            finally
            {
                _busy = false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
