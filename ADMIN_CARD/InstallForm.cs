using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NPIC_CARD_APP;

namespace CardStudio.Admin
{
    /// <summary>
    /// Install Card Studio on the chosen Windows PCs (see <see cref="RemoteInstaller"/>): the setup file, an administrator
    /// account, then per PC: access check (Windows, Machine ID), an optional serial number, and the result.
    /// </summary>
    //  ┌ Install Card Studio ─────────────────────────────────────────────────────────────┐
    //  │ Setup file      dist\CardStudio-Setup.exe        Card Studio 1.0.1.1   [Browse…] │
    //  │ Administrator   [NPIC\admin          ]   Password [••••••••]   ☑ Desktop shortcut │
    //  │ PC        Address      Access           Machine ID       Serial      Result        │
    //  │ LAB-PC-08 192.168.1.58 ✓ Windows 11 Pro 7K2Q-M9XD-…      [paste]     ✓ Installed   │
    //  │                        [Copy Machine IDs]  [Check Access]  [Install]  [Close]      │
    //  └────────────────────────────────────────────────────────────────────────────────────┘
    internal sealed class InstallForm : ModalForm
    {
        private const int AtOnce = 4;

        private sealed class Target
        {
            public NetDevice Device;
            public RemoteInfo Info;
            public DataGridViewRow Row;
            public string Host;
            /// <summary>The account for this PC: a bare user name gets the PC's name in front (PC-NAME\user).</summary>
            public RemoteCredentials Cred;
            public RemoteProblem Problem;
        }

        private readonly List<Target> _targets;
        private readonly AdminSettings _settings = AdminSettings.Current;
        private readonly Label _lblSetup, _lblSetupVersion, _lblNote;
        private readonly TextBox _txtUser, _txtPassword;
        private readonly CheckBox _chkDesktop;
        private readonly DataGridView _grid;
        private readonly PillButton _btnInstall, _btnCheck, _btnBrowse;
        private readonly ToolTip _tip = new ToolTip();
        private string _setupPath, _setupVersion;
        private bool _running;
        private CancellationTokenSource _cancel;

        /// <summary>PCs where the setup finished without error in this window.</summary>
        public int InstalledCount { get; private set; }

        public InstallForm(IEnumerable<NetDevice> devices)
        {
            _targets = devices.Select(d => new Target { Device = d }).ToList();
            Text = "Install Card Studio";
            Subtitle = "On " + (_targets.Count == 1 ? _targets[0].Device.DisplayName : _targets.Count + " PCs") +
                       ", over the network, with an administrator account";
            Glyph = Theme.IcoDownload;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1060, work.Width), Math.Min(660, work.Height));
            MinimumSize = new Size(Math.Min(820, work.Width), Math.Min(520, work.Height));

            // ---- Setup file + account ----
            var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, Padding = new Padding(24, 16, 24, 6), BackColor = Theme.Surface };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            form.Controls.Add(FieldName("Setup file"), 0, 0);
            _lblSetup = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Theme.TextMain, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, Margin = new Padding(0, 0, 8, 6) };
            form.Controls.Add(_lblSetup, 1, 0);
            _lblSetupVersion = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.TextSoft, Font = Theme.Small, Margin = new Padding(0, 0, 8, 6) };
            form.Controls.Add(_lblSetupVersion, 2, 0);
            form.SetColumnSpan(_lblSetupVersion, 2);
            _btnBrowse = ModalUi.ToolButton("Browse…", (s, e) => ChooseSetup());
            _btnBrowse.Margin = new Padding(0, 0, 0, 6);
            form.Controls.Add(_btnBrowse, 4, 0);

            form.Controls.Add(FieldName("Administrator"), 0, 1);
            _txtUser = Field(_settings.InstallUser ?? "");
            _txtUser.HandleCreated += (s, e) => Lang.SetCueBanner(_txtUser, @"DOMAIN\admin or PC-NAME\Administrator  (empty = your Windows account)");
            form.Controls.Add(_txtUser, 1, 1);
            form.Controls.Add(new Label { Text = "Password", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.TextMuted, Font = Theme.Small, Margin = new Padding(4, 0, 8, 6) }, 2, 1);
            _txtPassword = Field("");
            _txtPassword.UseSystemPasswordChar = true;
            form.Controls.Add(_txtPassword, 3, 1);
            _chkDesktop = new CheckBox { Text = "Desktop shortcut", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.TextMain, Margin = new Padding(10, 0, 0, 6) };
            form.Controls.Add(_chkDesktop, 4, 1);

            // ---- PCs ----
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false, BackgroundColor = Theme.Surface,
                BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Theme.GridLine,
                RowHeadersVisible = false, EnableHeadersVisualStyles = false, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 34,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            _grid.RowTemplate.Height = 36;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.Font = Theme.UiFont(9f, true);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 4, 0);
            _grid.DefaultCellStyle.BackColor = Theme.Surface;
            _grid.DefaultCellStyle.ForeColor = Theme.TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = Theme.Surface;
            _grid.DefaultCellStyle.SelectionForeColor = Theme.TextMain;
            _grid.DefaultCellStyle.Padding = new Padding(6, 0, 4, 0);
            Column("PC", "PC", 130, true);
            Column("Address", "Address", 108, true);
            Column("Access", "Access", 200, true);
            Column("MachineId", "Machine ID", 160, true);
            Column("Serial", "Serial number (optional)", 190, false);
            Column("Result", "Result", 220, true).AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            DataGridViewColumn serialCol = _grid.Columns["Serial"];
            serialCol.DefaultCellStyle.BackColor = Theme.SurfaceAlt;
            serialCol.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            serialCol.DefaultCellStyle.Font = new Font("Consolas", 9f);
            _grid.EditingControlShowing += (s, e) =>
            {
                var box = e.Control as TextBox;
                if (box == null) return;
                box.BackColor = Theme.SurfaceAlt;
                box.ForeColor = Theme.TextMain;
            };

            foreach (Target t in _targets)
            {
                int i = _grid.Rows.Add(t.Device.DisplayName, t.Device.Address.ToString(), "Not checked yet", "", "", "");
                t.Row = _grid.Rows[i];
                t.Row.Tag = t;
                t.Row.Cells["Access"].Style.ForeColor = Theme.TextMuted;
            }
            var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 6, 24, 0), BackColor = Theme.Surface };
            gridHost.Controls.Add(_grid);

            _lblNote = new Label
            {
                Dock = DockStyle.Bottom, Height = 46, ForeColor = Theme.TextSoft, Font = Theme.Small, Padding = new Padding(24, 6, 24, 0),
                Text = "Card Studio opens only with a serial number made for the PC's Machine ID. Check Access shows the Machine IDs: make the " +
                       "serials with the Serial Generator and paste them in the Serial column, and Install saves them on the PCs. " +
                       "The PCs need File and Printer Sharing and WMI allowed in their firewall."
            };

            // ---- Footer ----
            FlowLayoutPanel footer = ModalUi.Footer();
            PillButton close = ModalUi.Button("Close", PillKind.Secondary, (s, e) => Close());
            footer.Controls.Add(close);
            _btnInstall = ModalUi.Button("Install", PillKind.Primary, async (s, e) => await Run(true));
            footer.Controls.Add(_btnInstall);
            _btnCheck = ModalUi.Button("Check Access", PillKind.Secondary, async (s, e) => await Run(false));
            footer.Controls.Add(_btnCheck);
            footer.Controls.Add(ModalUi.Button("Copy Machine IDs", PillKind.Ghost, (s, e) => CopyMachineIds()));
            footer.Controls.Add(ModalUi.Button("Save Preparation Script…", PillKind.Ghost, (s, e) => SavePreparationScript(this)));
            CancelButton = close;

            Controls.Add(gridHost);
            Controls.Add(_lblNote);
            Controls.Add(form);
            Controls.Add(footer);

            UseSetup(FindSetup());
            Shown += (s, e) => { if (_txtUser.Text.Length > 0) _txtPassword.Focus(); else _txtUser.Focus(); };
        }

        private static Label FieldName(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.TextMuted, Font = Theme.Small, Margin = new Padding(0, 0, 8, 6) };
        }

        private static TextBox Field(string text)
        {
            return new TextBox
            {
                Text = text, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.SurfaceAlt, ForeColor = Theme.TextMain,
                Margin = new Padding(0, 0, 8, 6)
            };
        }

        private DataGridViewColumn Column(string name, string header, int width, bool readOnly)
        {
            var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.NotSortable };
            _grid.Columns.Add(c);
            return c;
        }

        // ===================== Setup file =====================

        /// <summary>The setup chosen last time, one next to ADMIN_CARD.exe, or dist\CardStudio-Setup.exe above it (the repo).</summary>
        private string FindSetup()
        {
            if (!string.IsNullOrEmpty(_settings.SetupPath) && File.Exists(_settings.SetupPath)) return _settings.SetupPath;
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(dir, RemoteInstaller.SetupName))) return Path.Combine(dir, RemoteInstaller.SetupName);
            for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
            {
                string candidate = Path.Combine(d.FullName, "dist", RemoteInstaller.SetupName);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private void UseSetup(string path)
        {
            _setupPath = path;
            _setupVersion = null;
            if (path == null)
            {
                _lblSetup.Text = "No setup chosen: Browse… for CardStudio-Setup.exe";
                _lblSetup.ForeColor = Theme.Danger;
                _lblSetupVersion.Text = "";
                return;
            }
            FileVersionInfo v = FileVersionInfo.GetVersionInfo(path);
            bool cardStudio = (v.FileDescription ?? "").IndexOf("Card Studio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              (v.ProductName ?? "").IndexOf("Card Studio", StringComparison.OrdinalIgnoreCase) >= 0;
            _setupVersion = string.IsNullOrEmpty(v.FileVersion) ? null : v.FileVersion;
            _lblSetup.Text = path;
            _lblSetup.ForeColor = cardStudio ? Theme.TextMain : Theme.Danger;
            _tip.SetToolTip(_lblSetup, path);
            _lblSetupVersion.Text = !cardStudio ? "Not a Card Studio setup"
                : "Card Studio " + (_setupVersion ?? "?") + " · " + (new FileInfo(path).Length / 1024.0 / 1024.0).ToString("0.0") + " MB";
            _lblSetupVersion.ForeColor = cardStudio ? Theme.TextSoft : Theme.Danger;
        }

        private void ChooseSetup()
        {
            using (var dlg = new OpenFileDialog { Filter = "Card Studio setup (CardStudio-Setup.exe)|*.exe", FileName = RemoteInstaller.SetupName, Title = "Choose CardStudio-Setup.exe" })
            {
                if (_setupPath != null) dlg.InitialDirectory = Path.GetDirectoryName(_setupPath);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                UseSetup(dlg.FileName);
                _settings.SetupPath = dlg.FileName;
                _settings.Save();
            }
        }

        // ===================== Check + install =====================

        private async Task Run(bool install)
        {
            if (_running) return;
            _grid.EndEdit();
            if (install && (_setupPath == null || !File.Exists(_setupPath)))
            {
                AppDialog.Show(this, "Choose CardStudio-Setup.exe first (Browse…).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (install && _lblSetupVersion.ForeColor == Theme.Danger &&
                AppDialog.Show(this, "This file does not look like Card Studio's setup:\n" + _setupPath + "\n\nInstall it anyway?", Text,
                               MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            var cred = new RemoteCredentials { User = _txtUser.Text.Trim(), Password = _txtPassword.Text };
            if (!cred.UseMine && cred.Password.Length == 0 &&
                AppDialog.Show(this, "The password is empty. Continue?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _settings.InstallUser = cred.User;
            _settings.Save();

            SetRunning(true);
            _cancel = new CancellationTokenSource();
            try
            {
                foreach (Target t in _targets)
                {
                    t.Host = cred.UseMine && t.Device.Name.Length > 0 ? t.Device.Name : t.Device.Address.ToString();   // Kerberos needs a name
                    t.Cred = new RemoteCredentials
                    {
                        User = cred.UseMine ? "" : RemoteInstaller.QualifyUser(cred.User, t.Device.Name),
                        Password = cred.Password
                    };
                }

                // 1. Access (also before an install, for PCs not checked yet)
                await ForEach(_targets.Where(t => t.Info == null || !install), CheckOne);
                if (IsDisposed) return;
                ShowAccessHelp();
                if (!install) return;

                // 2. Serials must fit their PC before anything is installed
                var problems = new List<string>();
                foreach (Target t in _targets.Where(t => t.Info != null))
                {
                    string serial = Serial(t);
                    if (serial.Length == 0) continue;
                    string why = t.Info.MachineId == null ? "its Machine ID could not be read" : AdminForm.CheckSerial(serial, t.Info.MachineId);
                    if (why == null) continue;
                    Cell(t, "Result", "✗ Serial: " + why, Theme.Danger);
                    problems.Add(t.Device.DisplayName + ": " + why);
                }
                if (problems.Count > 0)
                {
                    AppDialog.Show(this, "Fix these serial numbers (or clear them), then Install again:\n\n" + string.Join("\n", problems), Text,
                                   MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 3. Install where the access check passed
                List<Target> ready = _targets.Where(t => t.Info != null).ToList();
                foreach (Target t in _targets.Where(t => t.Info == null)) Cell(t, "Result", "✗ Not installed: no access", Theme.Danger);
                await ForEach(ready, t => InstallOne(t, _cancel.Token));

                if (IsDisposed) return;
                string summary = "Card Studio was installed on " + InstalledCount + " of " + _targets.Count + (_targets.Count == 1 ? " PC." : " PCs.");
                if (_targets.Any(t => t.Info != null && Serial(t).Length == 0 && Installed(t)))
                    summary += "\n\nPCs installed without a serial ask for one when Card Studio opens: send the Machine ID to the Serial Generator.";
                AppDialog.Show(this, summary, Text, MessageBoxButtons.OK,
                               InstalledCount == _targets.Count ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            finally
            {
                if (!IsDisposed) SetRunning(false);
            }
        }

        private static bool Installed(Target t)
        {
            var v = t.Row.Cells["Result"].Value as string;
            return v != null && v.StartsWith("✓");
        }

        private async Task CheckOne(Target t)
        {
            Cell(t, "Access", "Connecting…", Theme.TextSoft);
            Tuple<RemoteInfo, RemoteException> r = await Remote(() => RemoteInstaller.Check(t.Host, t.Cred));
            t.Info = r.Item1;
            t.Problem = r.Item2 == null ? RemoteProblem.Other : r.Item2.Problem;
            if (r.Item2 != null)
            {
                Cell(t, "Access", "✗ " + r.Item2.Message, Theme.Danger);
                t.Row.Cells["Access"].ToolTipText = r.Item2.Message + "\n\nAccount: " + (t.Cred.UseMine ? "your Windows account" : t.Cred.User) +
                                                    "\nAddress: " + t.Host;
                return;
            }
            Cell(t, "Access", "✓ " + t.Info.Os, Theme.Success);
            Cell(t, "MachineId", t.Info.MachineId ?? "Could not be read", t.Info.MachineId == null ? Theme.Warning : Theme.TextMain);
        }

        /// <summary>
        /// Runs a remote step on a worker thread and returns its result or its <see cref="RemoteException"/>. The exception is
        /// caught inside the worker, so an expected failure (access denied, PC off…) never stops the debugger as "user-unhandled".
        /// </summary>
        private static Task<Tuple<T, RemoteException>> Remote<T>(Func<T> work)
        {
            return Task.Run(() =>
            {
                try { return Tuple.Create(work(), (RemoteException)null); }
                catch (RemoteException ex) { return Tuple.Create(default(T), ex); }
            });
        }

        /// <summary>When PCs refused the account or did not answer: what to do, in the note under the list.</summary>
        private void ShowAccessHelp()
        {
            int denied = _targets.Count(t => t.Info == null && (t.Problem == RemoteProblem.AccessDenied || t.Problem == RemoteProblem.Unreachable));
            int wrong = _targets.Count(t => t.Info == null && t.Problem == RemoteProblem.WrongPassword);
            if (denied == 0 && wrong == 0) return;
            var help = new List<string>();
            if (denied > 0)
                help.Add((denied == 1 ? "1 PC" : denied + " PCs") + " refused remote administration or did not answer. On a network " +
                         "without a domain, Windows blocks it until the PC is prepared: click Save Preparation Script…, run it once " +
                         "on each of those PCs as administrator, then Check Access again.");
            if (wrong > 0)
                help.Add("Wrong user name or password on " + (wrong == 1 ? "1 PC" : wrong + " PCs") + ": type PC-NAME\\user (or a " +
                         "Microsoft account e-mail) and that account's password.");
            _lblNote.Text = string.Join("  ", help);
            _lblNote.ForeColor = Theme.Warning;
        }

        /// <summary>Saves the one-time preparation script (see <see cref="RemoteInstaller.PreparationScript"/>) and says how to use it.</summary>
        public static void SavePreparationScript(IWin32Window owner)
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Save the preparation script", FileName = RemoteInstaller.PreparationScriptName,
                Filter = "Command script (*.cmd)|*.cmd"
            })
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, RemoteInstaller.PreparationScript(), System.Text.Encoding.ASCII);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    AppDialog.Show(owner, "Could not save the script:\n" + ex.Message, "Preparation script", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                AppDialog.Show(owner,
                    "Saved " + Path.GetFileName(dlg.FileName) + ".\n\n" +
                    "On each PC where Card Studio should be installed, once:\n" +
                    "1. Copy the script there (USB stick or a shared folder).\n" +
                    "2. Right-click it → Run as administrator.\n" +
                    "3. Answer Y to set the network to Private, only on a trusted school or office network.\n\n" +
                    "It lets administrator accounts manage the PC over the network and opens file sharing and WMI for private and " +
                    "domain networks only (never public Wi-Fi). Then click Check Access again, with PC-NAME\\user and its password.",
                    "Preparation script", MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Process.Start("explorer.exe", "/select,\"" + dlg.FileName + "\""); } catch (Exception) { }
            }
        }

        private async Task InstallOne(Target t, CancellationToken cancel)
        {
            string serial = Serial(t);
            Action<string> status = text =>
            {
                if (IsDisposed) return;
                try { BeginInvoke((Action)(() => Cell(t, "Result", text, Theme.TextSoft))); } catch (InvalidOperationException) { }
            };
            bool desktop = _chkDesktop.Checked;
            string setup = _setupPath;
            Tuple<string, RemoteException> run = await Remote(() => RemoteInstaller.Install(t.Host, t.Cred, t.Info, setup, desktop, status, cancel));
            if (run.Item2 != null || run.Item1 != null)
            {
                Cell(t, "Result", "✗ " + (run.Item2 != null ? run.Item2.Message : run.Item1), Theme.Danger);
                return;
            }
            if (serial.Length > 0)
            {
                Cell(t, "Result", "Saving the serial…", Theme.TextSoft);
                Tuple<bool, RemoteException> saved = await Remote(() => { RemoteInstaller.WriteSerial(t.Host, t.Cred, t.Info, serial); return true; });
                if (saved.Item2 != null)
                {
                    InstalledCount++;
                    t.Device.InstalledHere = _setupVersion ?? "installed";
                    Cell(t, "Result", "✗ " + saved.Item2.Message, Theme.Warning);
                    return;
                }
            }
            InstalledCount++;
            t.Device.InstalledHere = _setupVersion ?? "installed";
            Cell(t, "Result", "✓ Installed " + (_setupVersion ?? "") + (serial.Length > 0 ? " · serial saved" : " · needs a serial"),
                 serial.Length > 0 ? Theme.Success : Theme.Warning);
        }

        /// <summary>Runs <paramref name="work"/> for every target, <see cref="AtOnce"/> PCs at a time, on the UI thread's context.</summary>
        private static async Task ForEach(IEnumerable<Target> targets, Func<Target, Task> work)
        {
            using (var gate = new SemaphoreSlim(AtOnce))
                await Task.WhenAll(targets.ToList().Select(async t =>
                {
                    await gate.WaitAsync();
                    try { await work(t); }
                    finally { gate.Release(); }
                }));
        }

        private string Serial(Target t)
        {
            return ((t.Row.Cells["Serial"].Value as string) ?? "").Trim();
        }

        private void Cell(Target t, string column, string text, Color color)
        {
            if (IsDisposed) return;
            DataGridViewCell cell = t.Row.Cells[column];
            cell.Value = text;
            cell.Style.ForeColor = color;
            cell.Style.SelectionForeColor = color;
            cell.ToolTipText = text.Length > 30 ? text : null;
        }

        private void SetRunning(bool running)
        {
            _running = running;
            _btnInstall.Enabled = _btnCheck.Enabled = _btnBrowse.Enabled = !running;
            _txtUser.Enabled = _txtPassword.Enabled = _chkDesktop.Enabled = !running;
            _grid.Columns["Serial"].ReadOnly = running;
            UseWaitCursor = running;
        }

        private void CopyMachineIds()
        {
            List<Target> known = _targets.Where(t => t.Info != null && t.Info.MachineId != null).ToList();
            if (known.Count == 0)
            {
                AppDialog.Show(this, "No Machine ID yet: click Check Access first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Clipboard.SetText(string.Join(Environment.NewLine, known.Select(t => t.Info.Computer + "\t" + t.Info.MachineId)));
            _lblNote.Text = known.Count + (known.Count == 1 ? " Machine ID" : " Machine IDs") + " copied (computer, tab, Machine ID): paste them in the Serial Generator.";
            _lblNote.ForeColor = Theme.Success;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_running && e.CloseReason == CloseReason.UserClosing)
            {
                if (AppDialog.Show(this, "Setups are still running on the PCs.\n\nStop waiting and close? The setups finish on the PCs anyway; " +
                                         "rescan the network afterwards to see the result.", Text,
                                   MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                if (_cancel != null) _cancel.Cancel();
            }
            base.OnFormClosing(e);
        }
    }
}
