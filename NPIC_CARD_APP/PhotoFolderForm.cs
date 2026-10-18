using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;

namespace NPIC_CARD_APP
{
    /// <summary>What was picked on the <see cref="PhotoFolderForm"/>.</summary>
    public enum PhotoFolderAction { None, Choose, Open, GoogleDrive, OneDrive }

    /// <summary>
    /// "Photo Folder" page: shows the current photo folder (how many photos, how many students have none) and lets the
    /// user choose another folder, open it in Explorer, or download photos into it from Google Drive / OneDrive. Picking closes the page and sets <see cref="Choice"/>;
    /// the main window then runs that action, so choosing / opening itself is unchanged.
    /// </summary>
    public class PhotoFolderForm : ModalForm
    {
        private static readonly string[] PhotoExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        /// <summary>The action picked on the page (None when closed without picking).</summary>
        public PhotoFolderAction Choice { get; private set; }

        private readonly ImportOptionCard _first;

        /// <param name="folder">The current photo folder (may be empty or missing).</param>
        /// <param name="students">The loaded students, to count the ones without a photo.</param>
        public PhotoFolderForm(string folder, IList<Student> students)
        {
            Text = "Photo Folder";
            Subtitle = "Where the cards read student photos from, and how to fill it";
            Glyph = Theme.IcoFolder;
            KeyPreview = true;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(760, work.Width), Math.Min(560, work.Height));
            MinimumSize = new Size(Math.Min(600, work.Width), Math.Min(500, work.Height));

            // ---- Current folder ----
            bool ok;
            string status = Describe(folder, students, out ok);
            var info = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 64, BackColor = Theme.Surface, UseMnemonic = false,
                ForeColor = ok ? Theme.TextSoft : Theme.Warning, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(22, 8, 22, 0), AutoEllipsis = true, Text = status
            };

            // ---- Choice cards ----
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Surface,
                Padding = new Padding(16, 4, 16, 12)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            // Row 1: the folder on this PC.   Row 2: fill it from the cloud.
            ImportOptionCard choose = Card(PhotoFolderAction.Choose, "Choose Photo Folder…", Theme.IcoFolder, null,
                "Pick the folder with photos named {StudentId}.jpg / .png. The cards show the new photos straight away.");
            ImportOptionCard open = Card(PhotoFolderAction.Open, "Open Photo Folder", Theme.IcoOpenFolder, null,
                "Open the folder in Windows Explorer to add, rename or delete photos. The selected student's photo is highlighted.");
            grid.Controls.Add(choose, 0, 0);
            grid.Controls.Add(open, 1, 0);
            grid.Controls.Add(Card(PhotoFolderAction.GoogleDrive, "Google Drive Photos…", Theme.IcoSync, "Ctrl+Shift+D",
                "Download student photos from a shared Google Drive folder into the photo folder."), 0, 1);
            grid.Controls.Add(Card(PhotoFolderAction.OneDrive, "OneDrive Photos…", Theme.IcoCloud, "Ctrl+Shift+O",
                "Download student photos from a OneDrive folder link (Microsoft sign-in) or a synced OneDrive folder."), 1, 1);
            // With a usable folder, opening it is the usual next step; without one, choosing is.
            _first = ok ? open : choose;

            // ---- Footer ----
            var footer = ModalUi.Footer();
            Button btnClose = ModalUi.Button("Close", PillKind.Secondary, (s, e) => DialogResult = DialogResult.Cancel);
            footer.Controls.Add(btnClose);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(grid);
            Controls.Add(info);
            Controls.Add(footer);
            CancelButton = btnClose;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _first.Focus();
        }

        /// <summary>The main window's cloud photo shortcuts also work here.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Shift | Keys.D: Pick(PhotoFolderAction.GoogleDrive); return true;
                case Keys.Control | Keys.Shift | Keys.O: Pick(PhotoFolderAction.OneDrive); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private ImportOptionCard Card(PhotoFolderAction action, string title, string glyph, string shortcut, string description)
        {
            var card = new ImportOptionCard(title, glyph, shortcut, description) { Dock = DockStyle.Fill, Margin = new Padding(6) };
            card.Click += (s, e) => Pick(action);
            return card;
        }

        private void Pick(PhotoFolderAction action)
        {
            Choice = action;
            DialogResult = DialogResult.OK;
        }

        /// <summary>"Current folder: …" plus photo counts; <paramref name="ok"/> = false when no usable folder is set.</summary>
        private static string Describe(string folder, IList<Student> students, out bool ok)
        {
            ok = false;
            if (string.IsNullOrEmpty(folder)) return "No photo folder is set yet. Choose one so the cards can show student photos.";
            if (!Directory.Exists(folder)) return "Current folder not found:\n" + folder + "\nChoose the folder again.";

            int photos;
            try
            {
                photos = Directory.EnumerateFiles(folder)
                    .Count(f => PhotoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                return "Current folder: " + folder + "\nCannot read it: " + ex.Message;
            }

            ok = true;
            string text = Lang.F("Current folder: {0}", folder) + "\n" + Lang.T(photos + " photo" + (photos == 1 ? "" : "s"));
            if (students != null && students.Count > 0)
            {
                int missing = students.Count(s => s != null && CardRenderer.FindPhoto(folder, s.StudentId) == null);
                text += missing == 0
                    ? Lang.T("  ·  every loaded student has a photo")
                    : Lang.F("  ·  {0} of {1} loaded students have no photo", missing, students.Count);
            }
            return text;
        }
    }
}
