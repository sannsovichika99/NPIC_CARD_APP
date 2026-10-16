using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Photos from OneDrive. Online: a OneDrive / SharePoint folder link (or a "/path" in the signed-in account's OneDrive),
    /// read with Microsoft Graph after a one-time Microsoft sign-in. Offline alternative: a folder the OneDrive app syncs to this PC.
    /// The folder, App ID, account name and the encrypted sign-in are kept in <see cref="AppSettings"/>.
    /// </summary>
    public sealed class OneDrivePhotoSource : PhotoSource
    {
        private readonly AppSettings _settings;
        private readonly List<OneDriveRoot> _roots;
        private readonly OneDriveSession _session;
        private readonly TextBox _txtFolder, _txtClientId;
        private readonly Label _lblAccount;
        private readonly Button _btnBrowse, _btnOpen, _btnRegister, _btnSignIn;

        private CancellationTokenSource _signInCts;
        private string _signInMessage;
        private string _scannedName;

        public OneDrivePhotoSource(AppSettings settings)
        {
            _settings = settings;
            _roots = OneDrivePhotoSync.FindRoots();
            _session = new OneDriveSession(settings.OneDriveClientId, settings.OneDriveRefreshToken);
            _session.TokensChanged += OnTokensChanged;

            _txtFolder = PhotoSyncUi.MakeText(settings.OneDriveFolder, "Paste the OneDrive folder link: https://1drv.ms/f/…  (or Browse… for a synced folder)");
            _txtClientId = PhotoSyncUi.MakeText(settings.OneDriveClientId, "Application (client) ID, e.g. 1a2b3c4d-…  — click Register App…");
            _txtFolder.TextChanged += (s, e) => FieldsChanged();
            _txtClientId.TextChanged += (s, e) => FieldsChanged();

            _btnBrowse = PhotoSyncUi.RowButton("Browse…", PillKind.Secondary, (s, e) => Browse());
            _btnOpen = PhotoSyncUi.RowButton("Open", PillKind.Secondary, (s, e) => OpenFolder());
            _btnRegister = PhotoSyncUi.RowButton("Register App…", PillKind.Secondary,
                (s, e) => PhotoSyncUi.OpenUrl(OneDriveOnlineSync.RegisterAppUrl, ShowError));
            _btnSignIn = PhotoSyncUi.RowButton("Sign In…", PillKind.Secondary, async (s, e) => await SignInOrOutAsync());
            _btnSignIn.MinimumSize = new Size(100, 0);

            _lblAccount = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
                Margin = new Padding(0, 4, 10, 0)
            };
            ShowAccount();
        }

        public override string Name { get { return "OneDrive"; } }
        public override string ShortName { get { return "OneDrive"; } }
        public override string Subtitle { get { return "Download student photos from a OneDrive folder"; } }
        public override string Glyph { get { return Theme.IcoCloud; } }

        public override string RootFolderName
        {
            get { return string.IsNullOrEmpty(_scannedName) ? "Shared folder" : _scannedName; }
        }

        public override string SetupSteps
        {
            get
            {
                return
                    "1.  Once per school: click “Register App…” → New registration → any name (e.g. NPIC Card App) → Supported account types: " +
                    "“Accounts in any organizational directory and personal Microsoft accounts” → Register.\n" +
                    "     Copy the “Application (client) ID” into App ID, then open Authentication → “Allow public client flows” → Yes → Save.\n" +
                    "2.  In OneDrive right-click the photo folder → Share → Copy link, and paste it above " +
                    "(or type a path in your own OneDrive, e.g. /Photos/2025).\n" +
                    "3.  Click Sign In…, enter the code shown in the browser that opens, and sign in with your Microsoft account.\n" +
                    "4.  Click Scan Folder (F5), check the Student Photos tab, then Download.\n\n" +
                    "Files named NPIC24001.jpg or “NPIC24001 - Sok Dara.jpg” go to student NPIC24001. Sub-folders are searched too.\n" +
                    "Only new photos are downloaded; photos edited with Remove Background are never replaced.\n" +
                    "No internet app registration? Browse… to a OneDrive folder synced to this PC instead — no sign-in needed.";
            }
        }

        public override string NotConnectedText
        {
            get { return "OneDrive is not connected yet.\nAdd the folder link, the App ID and sign in, in OneDrive Settings."; }
        }

        public override string NoPicturesText
        {
            get { return "The OneDrive folder has no pictures.\nCheck the folder, or upload the photos to it."; }
        }

        public override IList<SourceRow> CreateRows()
        {
            return new[]
            {
                new SourceRow { Caption = "Folder", Field = _txtFolder, A = _btnBrowse, B = _btnOpen },
                new SourceRow { Caption = "App ID", Field = _txtClientId, B = _btnRegister, SpanField = true },
                new SourceRow { Caption = "Account", Field = _lblAccount, B = _btnSignIn, SpanField = true }
            };
        }

        public override void AddTips(ToolTip tip)
        {
            tip.SetToolTip(_btnBrowse, "Choose a OneDrive folder synced to this PC instead of a link (no sign-in needed)");
            tip.SetToolTip(_btnOpen, "Open the folder in the browser (link) or in Windows Explorer (synced folder)");
            tip.SetToolTip(_btnRegister, "Microsoft Entra → App registrations → New registration. Free; done once per school.");
            tip.SetToolTip(_btnSignIn, "Sign in with the Microsoft account that can open the folder. The app never sees the password; " +
                                       "the sign-in is saved encrypted for this Windows user.");
        }

        private bool Online { get { return OneDriveOnlineSync.IsOnline(_txtFolder.Text); } }

        /// <summary>Signed in with the App ID now in the box.</summary>
        private bool SignedIn
        {
            get
            {
                return _session.SignedIn &&
                       string.Equals(_session.ClientId, _txtClientId.Text.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public override Control FirstMissingField()
        {
            if (_txtFolder.Text.Trim().Length == 0) return _txtFolder;
            if (!Online) return null;
            if (_txtClientId.Text.Trim().Length == 0) return _txtClientId;
            if (!SignedIn) return _btnSignIn;
            return null;
        }

        public override string Validate(string photoFolder, out Control field)
        {
            field = _txtFolder;
            try
            {
                if (!Online)
                {
                    OneDrivePhotoSync.CheckFolder(_txtFolder.Text, photoFolder);
                    return null;
                }
                OneDriveOnlineSync.CheckLink(_txtFolder.Text);
                field = _txtClientId;
                OneDriveOnlineSync.CheckClientId(_txtClientId.Text);
                field = _btnSignIn;
                if (_signInCts != null) return "Finish signing in first: enter the code in the browser.";
                if (!SignedIn) return "Sign in to OneDrive: click Sign In… and enter the code in the browser.";
                return null;
            }
            catch (ArgumentException ex)
            {
                return ex.Message;
            }
        }

        public override Task<List<DriveFile>> ListAsync(string photoFolder, CancellationToken cancel)
        {
            return Online
                ? OneDriveOnlineSync.ListImagesAsync(_session, _txtFolder.Text.Trim(), cancel)
                : OneDrivePhotoSync.ListImagesAsync(_txtFolder.Text, photoFolder, cancel);
        }

        public override void Save(AppSettings settings)
        {
            if (Online)
            {
                settings.OneDriveFolder = _txtFolder.Text.Trim();
                _scannedName = _txtFolder.Text.Trim().StartsWith("/") ? _txtFolder.Text.Trim() : null;
                return;
            }
            string folder = OneDrivePhotoSync.CheckFolder(_txtFolder.Text, settings.PhotoFolder);
            settings.OneDriveFolder = folder;
            _scannedName = Path.GetFileName(folder.TrimEnd('\\'));
            if (string.IsNullOrEmpty(_scannedName)) _scannedName = folder;   // a whole drive
        }

        public override Task FetchAsync(IList<PhotoSyncItem> items, string photoFolder, IProgress<int> progress, CancellationToken cancel)
        {
            // The listed files decide: a scan of a link has "driveId/itemId" ids, a local scan has Windows file paths.
            bool online = items.Count > 0 && items[0].File.Id.IndexOf('\\') < 0;
            return online
                ? OneDriveOnlineSync.DownloadAsync(_session, items, photoFolder, progress, cancel)
                : OneDrivePhotoSync.CopyAsync(items, photoFolder, progress, cancel);
        }

        public override void Dispose()
        {
            if (_signInCts != null) _signInCts.Cancel();   // stop waiting for the code
            _session.TokensChanged -= OnTokensChanged;
        }

        // ===================== Sign in =====================
        /// <summary>Sign In… (device code: the user enters a code at microsoft.com/devicelogin), Cancel while waiting, or Sign Out.</summary>
        private async Task SignInOrOutAsync()
        {
            if (_signInCts != null) { _signInCts.Cancel(); return; }
            if (SignedIn)
            {
                _session.SignOut();
                _settings.OneDriveAccount = "";
                FieldsChanged();
                return;
            }

            string clientId = _txtClientId.Text.Trim();
            try { OneDriveOnlineSync.CheckClientId(clientId); }
            catch (ArgumentException ex)
            {
                ShowError(ex.Message);
                _txtClientId.Focus();
                return;
            }

            _signInCts = new CancellationTokenSource();
            _signInMessage = "Contacting Microsoft…";
            FieldsChanged();
            try
            {
                DeviceCodeInfo code = await _session.StartSignInAsync(clientId, _signInCts.Token);
                bool copied = false;
                try { Clipboard.SetText(code.UserCode); copied = true; }
                catch (Exception) { /* clipboard busy: the code is on screen anyway */ }
                _signInMessage = "Enter the code  " + code.UserCode + "  at " + code.VerificationUri +
                                 (copied ? "  (copied — paste it in the browser)" : "") + ".  Waiting…";
                ShowAccount();
                PhotoSyncUi.OpenUrl(code.VerificationUri, ShowError);

                await _session.CompleteSignInAsync(clientId, code, _signInCts.Token);
                _settings.OneDriveClientId = clientId;
                _settings.OneDriveRefreshToken = _session.RefreshToken;
                try { _settings.OneDriveAccount = await OneDriveOnlineSync.GetAccountNameAsync(_session, _signInCts.Token); }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { _settings.OneDriveAccount = "Microsoft account"; }
            }
            catch (OperationCanceledException) { /* Cancel, or the page closed */ }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
            finally
            {
                _signInCts.Dispose();
                _signInCts = null;
                _signInMessage = null;
            }
            if (!_lblAccount.IsDisposed) FieldsChanged();
        }

        /// <summary>Saves a renewed or cleared sign-in (may run on a download thread).</summary>
        private void OnTokensChanged(OneDriveSession session)
        {
            if (!string.Equals(session.ClientId, _settings.OneDriveClientId, StringComparison.OrdinalIgnoreCase) &&
                _settings.OneDriveClientId.Length > 0 && session.SignedIn) return;   // a new sign-in is saved when it completes
            _settings.OneDriveRefreshToken = session.RefreshToken;
            if (!session.SignedIn)
            {
                _settings.OneDriveAccount = "";
                if (_lblAccount.IsHandleCreated && !_lblAccount.IsDisposed) _lblAccount.BeginInvoke((Action)FieldsChanged);
            }
        }

        // ===================== Folder =====================
        private void Browse()
        {
            string current = _txtFolder.Text.Trim();
            using (var dlg = new FolderBrowserDialog
            {
                Description = "Choose a OneDrive folder synced to this PC that holds the student photos",
                SelectedPath = !Online && Directory.Exists(current) ? current : (_roots.Count > 0 ? _roots[0].Path : ""),
                ShowNewFolderButton = false
            })
            {
                if (dlg.ShowDialog(_txtFolder.FindForm()) != DialogResult.OK) return;
                _txtFolder.Text = dlg.SelectedPath;
            }
        }

        private void OpenFolder()
        {
            string folder = _txtFolder.Text.Trim();
            if (!Online) PhotoSyncUi.OpenInExplorer(folder, ShowError);
            else if (folder.StartsWith("/")) PhotoSyncUi.OpenUrl("https://onedrive.live.com/", ShowError);
            else PhotoSyncUi.OpenUrl(folder, ShowError);
        }

        private void FieldsChanged()
        {
            ShowAccount();
            OnChanged();
        }

        /// <summary>Account row: the sign-in state for a link, or whether a local folder is inside OneDrive.</summary>
        private void ShowAccount()
        {
            string folder = _txtFolder.Text.Trim();
            bool online = Online;
            _btnOpen.Enabled = folder.Length > 0 && (online || Directory.Exists(folder));

            bool waiting = _signInCts != null;
            _btnSignIn.Text = waiting ? "Cancel" : SignedIn ? "Sign Out" : "Sign In…";
            _btnSignIn.Enabled = waiting || SignedIn || _txtClientId.Text.Trim().Length > 0;
            _txtClientId.Enabled = !waiting;

            if (waiting)
            {
                Set(_signInMessage ?? "Contacting Microsoft…", Theme.Accent);
            }
            else if (folder.Length > 0 && !online)
            {
                // A folder on this PC: no sign-in needed.
                OneDriveRoot root = OneDrivePhotoSync.RootOf(folder, _roots);
                if (root != null) Set("Folder on this PC — no sign-in needed.  ✔ Synced by OneDrive — " + root.Name, Theme.Success);
                else Set("Folder on this PC — no sign-in needed. It is not inside OneDrive, so it is read as a normal folder.", Theme.Warning);
            }
            else if (SignedIn)
            {
                Set("✔  Signed in as " + (string.IsNullOrEmpty(_settings.OneDriveAccount) ? "a Microsoft account" : _settings.OneDriveAccount),
                    Theme.Success);
            }
            else if (_session.SignedIn)
            {
                Set("The App ID changed — click Sign In… again.", Theme.Warning);
            }
            else
            {
                Set(_txtClientId.Text.Trim().Length == 0
                        ? "Not signed in. Enter the App ID first, then click Sign In…."
                        : "Not signed in. Click Sign In… and enter the code in the browser.",
                    Theme.TextSoft);
            }
        }

        private void Set(string text, Color color)
        {
            _lblAccount.Text = text;
            _lblAccount.ForeColor = color;
        }
    }
}
