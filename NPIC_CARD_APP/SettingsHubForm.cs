using System;
using System.Drawing;
using System.Windows.Forms;

namespace NPIC_CARD_APP
{
    /// <summary>Page picked on the <see cref="SettingsHubForm"/>.</summary>
    public enum SettingsHubChoice { None, DarkMode, ThemeColor, Folders, AllSettings, DriverSetup, SystemInfo, Updates, HelpAbout, License, Language }

    /// <summary>
    /// "Settings &amp; Help" page: Dark / Light Mode, Theme Color…, Folders…, All Settings…, Epson Driver Setup…, System Info,
    /// Check for Updates, License…, Language (English / Khmer) and Help &amp; About
    /// grouped in one popup, so the sidebar stays short. Picking a tile closes the page and sets <see cref="Choice"/>; the main
    /// window then runs that command, so each page itself is unchanged. Their keyboard shortcuts keep working here and in the
    /// main window.
    /// </summary>
    public class SettingsHubForm : ModalForm
    {
        /// <summary>The page picked (None when closed without picking).</summary>
        public SettingsHubChoice Choice { get; private set; }

        private readonly ImportOptionCard _first;

        /// <param name="accentHex">Saved primary colour (<see cref="Services.AppSettings.AccentColor"/>), shown on the Theme Color tile.</param>
        public SettingsHubForm(string accentHex)
        {
            Text = "Settings & Help";
            Subtitle = "Appearance, language, folders, settings, printer driver, system info, updates and the user guide";
            Glyph = Theme.IcoSettings;
            KeyPreview = true;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(720, work.Width), Math.Min(880, work.Height));
            MinimumSize = new Size(Math.Min(560, work.Width), Math.Min(760, work.Height));

            // ---- Tiles (2 x 5): appearance, settings, printer driver, help, updates and license ----
            const int rows = 5;
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = rows, BackColor = Theme.Surface,
                Padding = new Padding(16, 14, 16, 12)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            for (int i = 0; i < rows; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

            _first = Theme.Dark
                ? Card(SettingsHubChoice.DarkMode, "Light Mode", Theme.IcoSun, "",
                    "Switch back to the light colours. The app restarts to apply it; printed cards are not affected.")
                : Card(SettingsHubChoice.DarkMode, "Dark Mode", Theme.IcoMoon, "",
                    "Dark colours, easier on the eyes in a dim room. The app restarts to apply it; printed cards are not affected.");
            grid.Controls.Add(_first, 0, 0);
            grid.Controls.Add(Card(SettingsHubChoice.ThemeColor, "Theme Color…", Theme.IcoColor, "",
                "Primary colour of buttons, tabs and highlights, now " + AccentPalette.Describe(accentHex) +
                ". Pick a preset or any colour, with a live preview."), 1, 0);
            grid.Controls.Add(Card(SettingsHubChoice.AllSettings, "All Settings…", Theme.IcoSettings, "Ctrl+,",
                "Every setting of the app and this PC: printer, tray, files, API, card text, fonts. Export, import or back them up."), 0, 1);
            grid.Controls.Add(Card(SettingsHubChoice.Folders, "Folders…", Theme.IcoFolder, "Ctrl+Shift+F",
                "Every folder the app uses: photos, backups, app data, card images. Open, change or create them."), 1, 1);
            grid.Controls.Add(Card(SettingsHubChoice.DriverSetup, "Epson Driver Setup…", Theme.IcoPrint, "Ctrl+Shift+E",
                "Choose the Epson card printer and save its paper source, paper type and quality."), 0, 2);
            grid.Controls.Add(Card(SettingsHubChoice.SystemInfo, "System Info", Theme.IcoInfo, "Ctrl+Shift+I",
                "Version, students loaded, cards printed, RFID cards, data files, printer, fonts and card images of this PC."), 1, 2);
            grid.Controls.Add(Card(SettingsHubChoice.HelpAbout, "Help & About", Theme.IcoQuestion, "F1",
                "What Card Studio does, step-by-step guide, shortcuts and troubleshooting, in English or Khmer · ជាភាសាខ្មែរ."), 0, 3);
            ImportOptionCard updates = Card(SettingsHubChoice.Updates, "Check for Updates", Theme.IcoSync, "Ctrl+U",
                "Version " + Services.UpdateService.CurrentVersionText + " is installed. Look for a newer version and install it; " +
                "settings, print history and card images are kept.");
            grid.Controls.Add(updates, 1, 3);
            Services.LicenseStatus license = Services.LicenseService.Check();
            grid.Controls.Add(Card(SettingsHubChoice.License, "License…", Theme.IcoKey, "",
                Lang.T(license.Describe()) + Lang.T(". Enter a new serial number from your administrator to renew it.")), 0, 4);
            // Named in the language it switches to, so it can be found without reading the other one.
            grid.Controls.Add(Lang.Khmer
                ? Card(SettingsHubChoice.Language, "English · អង់គ្លេស", Theme.IcoGlobe, "Ctrl+Shift+L",
                    "Show Card Studio in English. The app restarts to apply it; printed cards are not affected.")
                : Card(SettingsHubChoice.Language, "ភាសាខ្មែរ · Khmer", Theme.IcoGlobe, "Ctrl+Shift+L",
                    "Show Card Studio in Khmer · បង្ហាញ Card Studio ជាភាសាខ្មែរ. The app restarts to apply it; " +
                    "printed cards are not affected."), 1, 4);

            // ---- Footer ----
            var footer = ModalUi.Footer();
            Button btnClose = ModalUi.Button("Close", PillKind.Secondary, (s, e) => DialogResult = DialogResult.Cancel);
            footer.Controls.Add(btnClose);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(grid);
            Controls.Add(footer);
            CancelButton = btnClose;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _first.Focus();
        }

        /// <summary>The main window's shortcuts for these pages also work here.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Oemcomma: Pick(SettingsHubChoice.AllSettings); return true;
                case Keys.Control | Keys.Shift | Keys.F: Pick(SettingsHubChoice.Folders); return true;
                case Keys.Control | Keys.Shift | Keys.E: Pick(SettingsHubChoice.DriverSetup); return true;
                case Keys.Control | Keys.Shift | Keys.I: Pick(SettingsHubChoice.SystemInfo); return true;
                case Keys.Control | Keys.U: Pick(SettingsHubChoice.Updates); return true;
                case Keys.F1: Pick(SettingsHubChoice.HelpAbout); return true;
                case Keys.Control | Keys.Shift | Keys.L: Pick(SettingsHubChoice.Language); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private ImportOptionCard Card(SettingsHubChoice choice, string title, string glyph, string shortcut, string description)
        {
            var card = new ImportOptionCard(title, glyph, shortcut, description) { Dock = DockStyle.Fill, Margin = new Padding(6) };
            card.Click += (s, e) => Pick(choice);
            return card;
        }

        private void Pick(SettingsHubChoice choice)
        {
            Choice = choice;
            DialogResult = DialogResult.OK;
        }
    }
}
