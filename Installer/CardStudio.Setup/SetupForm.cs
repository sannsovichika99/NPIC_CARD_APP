using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CardStudio.Setup
{
    /// <summary>Install wizard: Options → Progress → Finished.</summary>
    internal sealed class SetupForm : Form
    {
        public int ExitCode = ExitCodes.Cancelled;

        private readonly SetupOptions _options;
        private readonly string _existingDir;
        private readonly Panel _optionsPage, _progressPage, _donePage;
        private readonly TextBox _dirBox;
        private readonly CheckBox _desktop, _startMenu, _launch;
        private readonly Label _status, _doneDetail;
        private readonly ProgressBar _bar;
        private readonly Button _primary, _secondary;
        private bool _busy;

        public SetupForm(SetupOptions options)
        {
            _options = options;
            _existingDir = InstallEngine.ExistingInstallDir();
            string existingVersion = InstallEngine.ExistingVersion();

            SuspendLayout();
            SetupUi.InitDialog(this, ProductInfo.Name + " Setup");

            // ---- Options page ----
            _optionsPage = SetupUi.Page();
            SetupUi.Text(_optionsPage, "Install " + ProductInfo.Name + " into this folder:", 24, 20);
            _dirBox = new TextBox
            {
                Location = new Point(24, 44), Width = 400,
                Text = options.InstallDir ?? _existingDir ?? ProductInfo.DefaultInstallDir
            };
            var browse = new Button { Text = "Browse…", Location = new Point(432, 43), Size = new Size(104, 25) };
            browse.Click += Browse;
            _optionsPage.Controls.Add(_dirBox);
            _optionsPage.Controls.Add(browse);

            SetupUi.Text(_optionsPage, "Space required: " + SetupUi.FormatSize(InstallEngine.PayloadSize()), 24, 76, soft: true);

            if (_existingDir != null)
            {
                _dirBox.ReadOnly = true;
                browse.Enabled = false;
                SetupUi.Text(_optionsPage,
                    ProductInfo.Name + " " + (existingVersion ?? "") + " is already installed here. Setup will update it to " +
                    ProductInfo.Version + ". Your settings, print history and card images in Assets are kept.",
                    24, 100, soft: true);
            }

            _desktop = new CheckBox { Text = "Create a desktop shortcut", Location = new Point(24, 156), AutoSize = true, Checked = options.DesktopShortcut };
            _startMenu = new CheckBox { Text = "Create a Start Menu shortcut", Location = new Point(24, 184), AutoSize = true, Checked = options.StartMenuShortcut };
            _optionsPage.Controls.Add(_desktop);
            _optionsPage.Controls.Add(_startMenu);
            SetupUi.Text(_optionsPage, "Requires .NET Framework 4.8.1 (found). Administrator rights are used to install for all users.", 24, 228, soft: true);

            // ---- Progress page ----
            _progressPage = SetupUi.Page();
            SetupUi.Text(_progressPage, "Installing " + ProductInfo.Name + "…", 24, 28);
            _bar = new ProgressBar { Location = new Point(24, 60), Size = new Size(512, 18), Maximum = 100 };
            _status = SetupUi.Text(_progressPage, "", 24, 88, soft: true);
            _progressPage.Controls.Add(_bar);

            // ---- Done page ----
            _donePage = SetupUi.Page();
            var doneTitle = SetupUi.Text(_donePage, ProductInfo.Name + " is installed.", 24, 28);
            doneTitle.Font = new Font("Segoe UI Semibold", 12F);
            _doneDetail = SetupUi.Text(_donePage, "", 24, 64, soft: true);
            _launch = new CheckBox { Text = "Start " + ProductInfo.Name + " now", Location = new Point(24, 150), AutoSize = true, Checked = true };
            _donePage.Controls.Add(_launch);

            // ---- Frame ----
            Panel footer = SetupUi.Footer();
            _primary = SetupUi.FooterButton(footer, _existingDir != null ? "Update" : "Install", 1, true);
            _secondary = SetupUi.FooterButton(footer, "Cancel", 0, false);
            _primary.Click += PrimaryClick;
            _secondary.Click += (s, e) => Close();
            AcceptButton = _primary;

            Controls.Add(_optionsPage);
            Controls.Add(_progressPage);
            Controls.Add(_donePage);
            Controls.Add(footer);
            Controls.Add(SetupUi.Header(ProductInfo.Name + " Setup", "Version " + ProductInfo.Version + "  ·  " + ProductInfo.Publisher));
            ShowPage(_optionsPage);
            ResumeLayout(false);
        }

        private void ShowPage(Panel page)
        {
            foreach (Panel p in new[] { _optionsPage, _progressPage, _donePage }) p.Visible = p == page;
            page.BringToFront();
        }

        private void Browse(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose where to install " + ProductInfo.Name, ShowNewFolderButton = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = dlg.SelectedPath;
                if (!path.TrimEnd('\\').EndsWith(ProductInfo.Name, StringComparison.OrdinalIgnoreCase))
                    path = Path.Combine(path, ProductInfo.Name);
                _dirBox.Text = path;
            }
        }

        private async void PrimaryClick(object sender, EventArgs e)
        {
            if (_donePage.Visible)
            {
                if (_launch.Checked) InstallEngine.LaunchApp(_options.InstallDir);
                Close();
                return;
            }

            if (!ValidateFolder()) return;
            if (!EnsureAppClosed()) return;

            _options.InstallDir = Path.GetFullPath(_dirBox.Text.Trim());
            _options.DesktopShortcut = _desktop.Checked;
            _options.StartMenuShortcut = _startMenu.Checked;

            ShowPage(_progressPage);
            _primary.Enabled = _secondary.Enabled = false;
            _busy = true;
            var progress = new Progress<InstallProgress>(p => { _bar.Value = p.Percent; _status.Text = p.Status; });
            try
            {
                await Task.Run(() => InstallEngine.Install(_options, progress));
                ExitCode = ExitCodes.Success;
                _doneDetail.Text = "Installed in " + _options.InstallDir + "." + Environment.NewLine +
                                   (_desktop.Checked || _startMenu.Checked
                                       ? "Open it from the " + (_desktop.Checked && _startMenu.Checked ? "desktop or Start Menu" : _desktop.Checked ? "desktop" : "Start Menu") + " shortcut."
                                       : "") + Environment.NewLine +
                                   "Remove it any time from Settings > Apps > Installed apps.";
                ShowPage(_donePage);
                _primary.Text = "Finish";
                _primary.Enabled = true;
                _secondary.Visible = false;
            }
            catch (Exception ex)
            {
                ExitCode = ExitCodes.Failed;
                MessageBox.Show(this, "Setup could not finish:" + Environment.NewLine + Environment.NewLine + ex.Message +
                                      Environment.NewLine + Environment.NewLine + "Details: " + Log.FilePath,
                                ProductInfo.Name + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowPage(_optionsPage);
                _primary.Enabled = _secondary.Enabled = true;
            }
            finally
            {
                _busy = false;
            }
        }

        private bool ValidateFolder()
        {
            string dir = _dirBox.Text.Trim();
            bool valid;
            try { valid = dir.Length > 3 && Path.IsPathRooted(dir) && Path.GetFullPath(dir).Length > 3; }
            catch (Exception) { valid = false; }
            if (!valid)
            {
                MessageBox.Show(this, "Choose a full folder path, for example " + ProductInfo.DefaultInstallDir + ".",
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (_existingDir == null && Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
                return MessageBox.Show(this, dir + " already contains files. Install there anyway?", Text,
                                       MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            return true;
        }

        private bool EnsureAppClosed()
        {
            while (InstallEngine.IsAppRunning())
            {
                if (MessageBox.Show(this, ProductInfo.Name + " is running. Close it, then click Retry.", Text,
                                    MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry)
                    return false;
            }
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy) { e.Cancel = true; return; }
            if (e.CloseReason == CloseReason.UserClosing && _optionsPage.Visible &&
                MessageBox.Show(this, "Exit " + ProductInfo.Name + " Setup?", Text,
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
