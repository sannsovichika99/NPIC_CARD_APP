using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Photos from a Google Drive folder shared as "Anyone with the link", read with a Google API key.
    /// The folder link and the API key (DPAPI-encrypted) are kept in <see cref="AppSettings"/>.
    /// </summary>
    public sealed class GoogleDrivePhotoSource : PhotoSource
    {
        private readonly TextBox _txtFolder, _txtKey;
        private readonly CheckBox _chkShowKey;
        private readonly Button _btnOpenDrive, _btnGetKey;

        public GoogleDrivePhotoSource(AppSettings settings)
        {
            _txtFolder = PhotoSyncUi.MakeText(settings.DriveFolderUrl, "Paste the folder link: https://drive.google.com/drive/folders/…");
            _txtKey = PhotoSyncUi.MakeText(settings.DriveApiKey, "Google API key, e.g. AIzaSy…");
            _txtKey.UseSystemPasswordChar = true;
            _txtFolder.TextChanged += (s, e) => FieldsChanged();
            _txtKey.TextChanged += (s, e) => FieldsChanged();

            _chkShowKey = new CheckBox { Text = "Show", AutoSize = true, Margin = new Padding(0, 12, 6, 0), BackColor = Theme.Surface };
            _chkShowKey.CheckedChanged += (s, e) => _txtKey.UseSystemPasswordChar = !_chkShowKey.Checked;

            _btnOpenDrive = PhotoSyncUi.RowButton("Open in Browser", PillKind.Secondary, (s, e) => OpenDriveFolder());
            _btnGetKey = PhotoSyncUi.RowButton("Get a Key…", PillKind.Secondary,
                (s, e) => PhotoSyncUi.OpenUrl(GoogleDrivePhotoSync.ApiKeyHelpUrl, ShowError));
            _btnOpenDrive.Enabled = _txtFolder.TextLength > 0;
        }

        public override string Name { get { return "Google Drive"; } }
        public override string ShortName { get { return "Drive"; } }
        public override string Subtitle { get { return "Download student photos from a shared Google Drive folder"; } }
        public override string Glyph { get { return Theme.IcoSync; } }
        public override string RootFolderName { get { return "Shared folder"; } }

        public override string SetupSteps
        {
            get
            {
                return
                    "1.  In Google Drive right-click the folder → Share → General access → “Anyone with the link” (Viewer) → Copy link.\n" +
                    "2.  Paste the link above. Photos in sub-folders (e.g. Google Form uploads) are found too.\n" +
                    "3.  Paste a Google API key with the Google Drive API turned on — click “Get a Key…” (free, once per school).\n" +
                    "4.  Click Scan Folder (F5), check the Student Photos tab, then Download.\n\n" +
                    "Files named NPIC24001.jpg or “NPIC24001 - Sok Dara.jpg” go to student NPIC24001.\n" +
                    "Only new photos are downloaded; photos edited with Remove Background are never replaced.";
            }
        }

        public override string NotConnectedText
        {
            get { return "Google Drive is not connected yet.\nAdd the shared folder link and an API key in Drive Settings."; }
        }

        public override string NoPicturesText
        {
            get { return "The Google Drive folder has no pictures.\nCheck the link, or upload the photos to it."; }
        }

        public override IList<SourceRow> CreateRows()
        {
            return new[]
            {
                new SourceRow { Caption = "Folder link", Field = _txtFolder, B = _btnOpenDrive, SpanField = true },
                new SourceRow { Caption = "API key", Field = _txtKey, A = _chkShowKey, B = _btnGetKey }
            };
        }

        public override void AddTips(ToolTip tip)
        {
            tip.SetToolTip(_btnGetKey, "Google Cloud Console → Create credentials → API key, then APIs & Services → Library → " +
                                       "Google Drive API → Enable. The key is saved encrypted for this Windows user.");
        }

        public override Control FirstMissingField()
        {
            if (_txtFolder.TextLength == 0) return _txtFolder;
            if (_txtKey.TextLength == 0) return _txtKey;
            return null;
        }

        public override string Validate(string photoFolder, out Control field)
        {
            string link = _txtFolder.Text.Trim(), key = _txtKey.Text.Trim();
            field = null;
            try
            {
                string id, resourceKey;
                GoogleDrivePhotoSync.ParseFolderLink(link, out id, out resourceKey);
                if (key.Length == 0) throw new ArgumentException("Enter a Google API key. Click “Get a Key…” to create one (free).");
                return null;
            }
            catch (ArgumentException ex)
            {
                field = link.Length == 0 || key.Length > 0 ? _txtFolder : _txtKey;
                return ex.Message;
            }
        }

        public override Task<List<DriveFile>> ListAsync(string photoFolder, CancellationToken cancel)
        {
            return GoogleDrivePhotoSync.ListImagesAsync(_txtFolder.Text.Trim(), _txtKey.Text.Trim(), cancel);
        }

        public override void Save(AppSettings settings)
        {
            settings.DriveFolderUrl = _txtFolder.Text.Trim();
            settings.DriveApiKey = _txtKey.Text.Trim();
        }

        public override Task FetchAsync(IList<PhotoSyncItem> items, string photoFolder, IProgress<int> progress, CancellationToken cancel)
        {
            return GoogleDrivePhotoSync.DownloadAsync(items, photoFolder, _txtKey.Text, progress, cancel);
        }

        private void FieldsChanged()
        {
            _btnOpenDrive.Enabled = _txtFolder.TextLength > 0;
            OnChanged();
        }

        private void OpenDriveFolder()
        {
            try { PhotoSyncUi.OpenUrl(GoogleDrivePhotoSync.FolderWebUrl(_txtFolder.Text), ShowError); }
            catch (ArgumentException ex) { ShowError(ex.Message); }
        }
    }
}
