using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "Connect SQL Server" page: server, database, login and the table / query that holds the students.
    ///   Test Connection = open the connection only.   Load Students = run the query and return <see cref="Students"/>.
    /// The connection details are saved in <see cref="AppSettings"/> (the password DPAPI-encrypted, only when ticked).
    /// </summary>
    public class SqlServerForm : ModalForm
    {
        private static readonly Color Surface       = Theme.Surface;
        private static readonly Color TextMain      = Theme.TextMain;
        private static readonly Color TextMuted     = Theme.TextSoft;
        private static readonly Color Success       = Theme.Success;
        private static readonly Color Danger        = Theme.Danger;

        private readonly AppSettings _settings;

        private readonly TextBox _txtServer, _txtDatabase, _txtUser, _txtPassword, _txtQuery;
        private readonly ComboBox _cboAuth;
        private readonly CheckBox _chkRemember, _chkEncrypt, _chkTrust;
        private readonly Label _lblResult;
        private readonly Button _btnTest, _btnLoad, _btnCancel;
        private readonly Panel _form;

        private CancellationTokenSource _cts;
        private bool _closeWhenDone;

        /// <summary>The students read by Load Students (null until then).</summary>
        public List<Student> Students { get; private set; }

        /// <summary>"server / database" for the status bar.</summary>
        public string SourceName { get; private set; }

        public SqlServerForm(AppSettings settings)
        {
            _settings = settings;

            Text = "Connect SQL Server";
            Subtitle = "Load the student list straight from the school's SQL Server database";
            Glyph = Theme.IcoDatabase;
            StartPosition = FormStartPosition.CenterParent;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(680, work.Width), Math.Min(640, work.Height));
            MinimumSize = new Size(Math.Min(560, work.Width), Math.Min(560, work.Height));
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font(CardFonts.UI, 9f);
            BackColor = Surface;
            ForeColor = TextMain;

            // ---- Fields ----
            _txtServer = MakeText(settings.SqlServer, "localhost\\SQLEXPRESS  or  192.168.1.10,1433");
            _txtDatabase = MakeText(settings.SqlDatabase, "Database name, e.g. NPIC");

            _cboAuth = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
            _cboAuth.Items.AddRange(new object[] { "Windows Authentication", "SQL Server Authentication" });
            _cboAuth.SelectedIndex = settings.SqlWindowsAuth ? 0 : 1;
            _cboAuth.SelectedIndexChanged += (s, e) => UpdateAuthFields();

            _txtUser = MakeText(settings.SqlUser, "Login name, e.g. sa");
            string savedPassword = settings.SqlPassword;
            _txtPassword = MakeText(savedPassword, "");
            _txtPassword.UseSystemPasswordChar = true;

            _chkRemember = MakeCheck("Remember password (encrypted for this Windows user)", savedPassword.Length > 0);
            _chkEncrypt = MakeCheck("Encrypt connection", settings.SqlEncrypt);
            _chkTrust = MakeCheck("Trust server certificate", settings.SqlTrustServerCertificate);

            _txtQuery = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true,
                Text = string.IsNullOrWhiteSpace(settings.SqlQuery) ? "dbo.Students" : settings.SqlQuery,
                Font = new Font("Consolas", 9.5f), Margin = new Padding(0, 4, 0, 4)
            };

            var hint = new Label
            {
                AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = TextMuted, Font = new Font(CardFonts.UI, 8.25f),
                Margin = new Padding(0, 2, 0, 0),
                Text = "Type a table name (dbo.Students) or one SELECT query. Columns use the CSV names: StudentId, NameKh, NameEn, " +
                       "Gender, DateOfBirth, Faculty, Level, Generation, ValidUntil, QrData — rename others with AS. " +
                       "The app only reads; it never changes the database."
            };

            _lblResult = new Label { AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(0, 10, 0, 0), ForeColor = TextMuted };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Surface, Padding = new Padding(20, 16, 20, 4), AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddRow(layout, "Server", _txtServer);
            AddRow(layout, "Database", _txtDatabase);
            AddRow(layout, "Authentication", _cboAuth);
            AddRow(layout, "User name", _txtUser);
            AddRow(layout, "Password", _txtPassword);
            AddRow(layout, "", _chkRemember);
            AddRow(layout, "Security", Flow(_chkEncrypt, _chkTrust));
            AddRow(layout, "Table or query", _txtQuery, SizeType.Percent);
            AddRow(layout, "", hint);
            AddRow(layout, "", _lblResult);
            _form = layout;

            // ---- Buttons ----
            var buttons = ModalUi.Footer();
            _btnCancel = MakeButton("Close", false);
            _btnLoad = MakeButton("Load Students", true);
            _btnTest = MakeButton("Test Connection", false);
            buttons.Controls.Add(_btnCancel);
            buttons.Controls.Add(_btnLoad);
            buttons.Controls.Add(_btnTest);

            Controls.Add(layout);
            Controls.Add(buttons);

            AcceptButton = _btnLoad;
            CancelButton = _btnCancel;

            _btnTest.Click += async (s, e) => await RunAsync(false);
            _btnLoad.Click += async (s, e) => await RunAsync(true);
            _btnCancel.Click += (s, e) =>
            {
                if (_cts != null) _cts.Cancel();   // stop the running query, keep the page open
                else DialogResult = DialogResult.Cancel;
            };

            UpdateAuthFields();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            (_txtServer.TextLength == 0 ? _txtServer : _txtDatabase.TextLength == 0 ? _txtDatabase : (Control)_btnLoad).Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_cts != null)
            {
                // Closed (X / Alt+F4) while connecting: cancel first; RunAsync then closes the page.
                e.Cancel = true;
                _closeWhenDone = true;
                _cts.Cancel();
            }
            base.OnFormClosing(e);
        }

        // ===================== Connect / load =====================
        private async Task RunAsync(bool load)
        {
            if (_cts != null) return;

            string connection;
            try
            {
                connection = SqlStudentLoader.BuildConnectionString(_txtServer.Text, _txtDatabase.Text, WindowsAuth,
                    _txtUser.Text, _txtPassword.Text, _chkEncrypt.Checked, _chkTrust.Checked);
                if (load) SqlStudentLoader.ToSql(_txtQuery.Text);   // check the query before connecting
            }
            catch (ArgumentException ex)
            {
                ShowResult(ex.Message, Danger);
                return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true, load ? "Loading students…  (Close stops the query)" : "Connecting…");
            List<Student> loaded = null;
            try
            {
                if (load)
                {
                    loaded = await SqlStudentLoader.LoadAsync(connection, _txtQuery.Text, _cts.Token);
                    ShowResult(string.Format("✔  {0} student(s) read.", loaded.Count), Success);
                }
                else
                {
                    string info = await SqlStudentLoader.TestAsync(connection, _cts.Token);
                    ShowResult("✔  Connected — " + info, Success);
                }
                SaveToSettings();
            }
            catch (OperationCanceledException)
            {
                ShowResult("Cancelled.", TextMuted);
            }
            catch (Exception ex)
            {
                ShowResult(ex.Message, Danger);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false, null);
            }

            if (loaded != null)
            {
                Students = loaded;
                SourceName = _txtServer.Text.Trim() + (_txtDatabase.TextLength > 0 ? " / " + _txtDatabase.Text.Trim() : "");
                DialogResult = DialogResult.OK;
            }
            else if (_closeWhenDone) DialogResult = DialogResult.Cancel;
        }

        private void SaveToSettings()
        {
            _settings.SqlServer = _txtServer.Text.Trim();
            _settings.SqlDatabase = _txtDatabase.Text.Trim();
            _settings.SqlWindowsAuth = WindowsAuth;
            _settings.SqlUser = _txtUser.Text.Trim();
            _settings.SqlPassword = !WindowsAuth && _chkRemember.Checked ? _txtPassword.Text : "";
            _settings.SqlEncrypt = _chkEncrypt.Checked;
            _settings.SqlTrustServerCertificate = _chkTrust.Checked;
            _settings.SqlQuery = _txtQuery.Text.Trim();
        }

        private bool WindowsAuth { get { return _cboAuth.SelectedIndex == 0; } }

        private void UpdateAuthFields()
        {
            _txtUser.Enabled = _txtPassword.Enabled = _chkRemember.Enabled = !WindowsAuth;
        }

        private void SetBusy(bool busy, string message)
        {
            _form.Enabled = _btnTest.Enabled = _btnLoad.Enabled = !busy;
            _btnCancel.Text = busy ? "Stop" : "Close";
            UseWaitCursor = busy;
            if (busy) ShowResult(message, TextMuted);
            else UpdateAuthFields();
        }

        private void ShowResult(string text, Color color)
        {
            _lblResult.Text = text;
            _lblResult.ForeColor = color;
        }

        // ===================== UI helpers =====================
        private static void AddRow(TableLayoutPanel layout, string caption, Control field, SizeType size = SizeType.AutoSize)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(size == SizeType.Percent ? new RowStyle(SizeType.Percent, 100f) : new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = caption, AutoSize = false, Width = 116, Height = 30, ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 4, 0), Anchor = AnchorStyles.Left | AnchorStyles.Top
            }, 0, row);
            layout.Controls.Add(field, 1, row);
            if (size == SizeType.Percent) field.MinimumSize = new Size(0, 90);
        }

        private static TextBox MakeText(string text, string cue)
        {
            var box = new TextBox { Dock = DockStyle.Fill, Text = text ?? "", Margin = new Padding(0, 4, 0, 4) };
            if (cue.Length > 0) box.HandleCreated += (s, e) => SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, cue);
            return box;
        }

        private static CheckBox MakeCheck(string text, bool isChecked)
        {
            return new CheckBox { Text = text, AutoSize = true, Checked = isChecked, Margin = new Padding(0, 6, 18, 4) };
        }

        private static FlowLayoutPanel Flow(params Control[] controls)
        {
            var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            flow.Controls.AddRange(controls);
            return flow;
        }

        private static Button MakeButton(string text, bool primary)
        {
            return ModalUi.Button(text, primary ? PillKind.Primary : PillKind.Secondary);
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }
}
