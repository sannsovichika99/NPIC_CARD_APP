using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Forms;
using NPIC_CARD_APP.Printing;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "Epson Driver Setup" page. Lists only the Epson printers installed on this PC and shows, for the selected one,
    /// the driver settings Card Studio will print with (paper source, size, quality, colour, copies) and whether they
    /// suit PVC ID cards. "Open Epson Settings…" opens the real Epson Printing Preferences and saves the full DEVMODE
    /// (including Paper Type = PVC ID Card), making that printer the card printer.
    ///   F5 = refresh, Enter / double-click = open Epson settings, Esc = close
    /// </summary>
    public class EpsonDriverForm : ModalForm
    {
        private const float HundredthInchToMm = 0.254f;

        private readonly AppSettings _settings;
        private readonly ListView _printers, _details;
        private readonly Label _lblInfo;
        private readonly Button _btnOpen, _btnReset, _btnProperties;
        private List<EpsonPrinter> _found = new List<EpsonPrinter>();
        private bool _refreshing;

        /// <summary>True when the printer or its saved driver settings were changed (already saved to disk).</summary>
        public bool SettingsChanged { get; private set; }

        public EpsonDriverForm(AppSettings settings)
        {
            _settings = settings;

            Text = "Epson Driver Setup";
            Subtitle = "Epson printers only — paper source, paper type and quality used for the cards";
            Glyph = Theme.IcoPrint;
            KeyPreview = true;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(860, work.Width), Math.Min(640, work.Height));
            MinimumSize = new Size(Math.Min(620, work.Width), Math.Min(480, work.Height));

            // ---- Toolbar ----
            var toolbar = ModalUi.Toolbar();
            toolbar.Controls.Add(ModalUi.ToolButton("Refresh (F5)", (s, e) => RefreshPrinters()));
            _btnProperties = ModalUi.ToolButton("Windows Printer Properties", (s, e) => OpenWindowsProperties());
            _btnReset = ModalUi.ToolButton("Reset to Driver Defaults", (s, e) => ResetToDefaults());
            toolbar.Controls.Add(_btnProperties);
            toolbar.Controls.Add(_btnReset);

            // ---- Epson printers ----
            _printers = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                HideSelection = false, GridLines = true, BorderStyle = BorderStyle.None
            };
            _printers.Columns.Add("Printer", 230);
            _printers.Columns.Add("Driver", 210);
            _printers.Columns.Add("Port", 110);
            _printers.Columns.Add("Status", 120);
            _printers.Columns.Add("Card Studio", 100);
            _printers.SelectedIndexChanged += (s, e) => ShowDetails();
            _printers.ItemActivate += (s, e) => OpenEpsonSettings();   // double-click / Enter

            // ---- Settings of the selected printer ----
            _details = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                HideSelection = true, BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _details.Columns.Add("", 34, HorizontalAlignment.Center);
            _details.Columns.Add("Setting", 150);
            _details.Columns.Add("Value", 560);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Surface,
                Padding = new Padding(16, 0, 16, 8)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));
            body.Controls.Add(SectionLabel("EPSON PRINTERS ON THIS PC"), 0, 0);
            body.Controls.Add(_printers, 0, 1);
            body.Controls.Add(SectionLabel("DRIVER SETTINGS CARD STUDIO WILL PRINT WITH"), 0, 2);
            body.Controls.Add(_details, 0, 3);

            _lblInfo = new Label
            {
                Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(8, 8, 8, 4), UseMnemonic = false,
                Text = "For PVC cards on the L8050, choose in the Epson dialog: Paper Source = ID Card Tray · " +
                       "Paper Type = PVC ID Card · Quality = High · Borderless off · Copies = 1. Click OK to save."
            };
            ModalUi.StyleStatus(_lblInfo);

            // ---- Footer ----
            var footer = ModalUi.Footer();
            Button btnClose = ModalUi.Button("Close", PillKind.Secondary, (s, e) => Close());
            _btnOpen = ModalUi.Button("Open Epson Settings…", PillKind.Primary, (s, e) => OpenEpsonSettings());
            footer.Controls.Add(_btnOpen);    // first added = rightmost
            footer.Controls.Add(btnClose);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(body);
            Controls.Add(toolbar);
            Controls.Add(_lblInfo);
            Controls.Add(footer);
            CancelButton = btnClose;

            UpdateButtons();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshPrinters();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5) { RefreshPrinters(); e.Handled = true; }
        }

        // ===================== Printer list =====================

        /// <summary>Reads the Epson printers on a worker thread (WMI can be slow), keeping the selection.</summary>
        private async void RefreshPrinters()
        {
            if (_refreshing) return;
            _refreshing = true;
            UseWaitCursor = true;
            try
            {
                string keep = SelectedPrinter() != null ? SelectedPrinter().Name : _settings.PrinterName;
                List<EpsonPrinter> found = await Task.Run(() => FindEpsonPrinters());
                if (IsDisposed) return;

                _found = found;
                FillPrinterList(keep);
            }
            finally
            {
                _refreshing = false;
                if (!IsDisposed) UseWaitCursor = false;
            }
        }

        private void FillPrinterList(string select)
        {
            _printers.BeginUpdate();
            _printers.Items.Clear();
            foreach (EpsonPrinter p in _found)
            {
                var item = new ListViewItem(p.Name) { Tag = p };
                item.SubItems.Add(p.Driver);
                item.SubItems.Add(p.Port);
                item.SubItems.Add(p.StatusText);
                item.SubItems.Add(IsInUse(p) ? "In use" : _settings.IsCardPrinter(p.Name) ? "Card printer" : "");
                if (p.Problems.Count > 0) item.ForeColor = Theme.Danger;
                else if (IsInUse(p)) item.ForeColor = Theme.Success;
                _printers.Items.Add(item);
            }

            // Keep the selection; otherwise prefer the printer in use, then the first card printer.
            ListViewItem target =
                _printers.Items.Cast<ListViewItem>().FirstOrDefault(i => Same(((EpsonPrinter)i.Tag).Name, select)) ??
                _printers.Items.Cast<ListViewItem>().FirstOrDefault(i => _settings.IsCardPrinter(((EpsonPrinter)i.Tag).Name)) ??
                _printers.Items.Cast<ListViewItem>().FirstOrDefault();
            if (target != null) { target.Selected = true; target.Focused = true; target.EnsureVisible(); }
            _printers.EndUpdate();

            if (target == null) ShowDetails();   // no SelectedIndexChanged without items
        }

        /// <summary>Every installed printer whose name or driver says Epson, with its current Windows status.</summary>
        private static List<EpsonPrinter> FindEpsonPrinters()
        {
            var list = new List<EpsonPrinter>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, DriverName, PortName FROM Win32_Printer"))
                using (ManagementObjectCollection printers = searcher.Get())
                {
                    foreach (ManagementObject p in printers)
                    {
                        using (p)
                        {
                            string name = p["Name"] as string ?? "";
                            string driver = p["DriverName"] as string ?? "";
                            if (!IsEpson(name) && !IsEpson(driver)) continue;
                            list.Add(new EpsonPrinter { Name = name, Driver = driver, Port = p["PortName"] as string ?? "" });
                        }
                    }
                }
            }
            catch (Exception)
            {
                // WMI unavailable: fall back to the printer names (driver / port unknown).
                list.Clear();
                foreach (string name in PrinterSettings.InstalledPrinters)
                    if (IsEpson(name)) list.Add(new EpsonPrinter { Name = name, Driver = "", Port = "" });
            }

            foreach (EpsonPrinter p in list) p.Problems = PrinterStatus.CheckPrinterOnly(p.Name);
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        private static bool IsEpson(string text)
        {
            return (text ?? "").IndexOf("EPSON", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ===================== Details =====================

        /// <summary>Fills the lower list with the driver settings of the selected printer and how well they suit cards.</summary>
        private void ShowDetails()
        {
            _details.BeginUpdate();
            _details.Items.Clear();
            try
            {
                EpsonPrinter p = SelectedPrinter();
                if (p == null)
                {
                    if (_found.Count == 0)
                    {
                        AddRow(Check.Bad, "Epson printer", "None installed. Install the Epson L8050 driver from epson.com, then click Refresh.");
                        AddRow(Check.Info, "Other printers", "Not listed here — this page is for Epson printers only.");
                    }
                    return;
                }
                FillDetails(p);
            }
            catch (Exception ex)
            {
                AddRow(Check.Bad, "Driver", "Could not read the driver settings: " + ex.Message);
            }
            finally
            {
                _details.EndUpdate();
                UpdateButtons();
            }
        }

        private void FillDetails(EpsonPrinter p)
        {
            bool inUse = IsInUse(p);
            bool saved = inUse && !string.IsNullOrEmpty(_settings.DevModeBase64);

            // In use: the settings Card Studio really prints with (saved DEVMODE applied). Otherwise: the driver defaults.
            string loadError = null;
            PrinterSettings ps;
            try { ps = inUse ? new CardPrinter(_settings).CreatePrinterSettings() : new PrinterSettings { PrinterName = p.Name }; }
            catch (InvalidOperationException ex)
            {
                loadError = ex.Message;
                saved = false;
                ps = new PrinterSettings { PrinterName = p.Name };
            }

            AddRow(inUse ? Check.Ok : Check.Info, "Card printer",
                inUse ? "Yes — Card Studio prints the cards on this printer"
                      : "No — saving its Epson settings makes it the card printer");

            if (!ps.IsValid)
            {
                AddRow(Check.Bad, "Printer", "Windows cannot open this printer. Reinstall the Epson driver, then click Refresh.");
                return;
            }

            if (loadError != null)
                AddRow(Check.Bad, "Saved settings", loadError.Replace("\n", " ").Replace("'Driver Setup...'", "'Open Epson Settings…'"));
            else if (saved)
                AddRow(Check.Ok, "Saved settings", "Saved (" + Convert.FromBase64String(_settings.DevModeBase64).Length + " bytes of Epson driver data)");
            else
                AddRow(inUse ? Check.Warn : Check.Info, "Saved settings",
                    "None — the Epson driver defaults below are used. Click 'Open Epson Settings…' to set them.");

            PageSettings page = ps.DefaultPageSettings;

            string source = page.PaperSource.SourceName ?? "";
            AddRow(CardPrinter.IsCardSource(source) ? Check.Ok : Check.Warn, "Paper source",
                CardPrinter.IsCardSource(source) ? source : source + "  —  should be the ID card tray");

            PaperSize size = page.PaperSize;
            AddRow(Check.Info, "Paper size", string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.0} x {2:0.0} mm)",
                size.PaperName, size.Width * HundredthInchToMm, size.Height * HundredthInchToMm));

            AddRow(Check.Info, "Paper type", "Choose 'PVC ID Card' in the Epson dialog (Windows cannot read this value back)");

            AddRow(page.Landscape ? Check.Info : Check.Ok, "Orientation",
                page.Landscape ? "Landscape  —  Card Studio always prints portrait, so this is ignored" : "Portrait");

            AddRow(Check.Info, "Print quality", DescribeQuality(page.PrinterResolution));

            AddRow(page.Color ? Check.Ok : Check.Warn, "Colour",
                page.Color ? "Colour" : "Greyscale  —  the cards will print without colour");

            AddRow(ps.Copies > 1 ? Check.Warn : Check.Ok, "Copies",
                ps.Copies > 1 ? ps.Copies + "  —  every tray load will print " + ps.Copies + " times" : ps.Copies.ToString());

            if (p.Problems.Count == 0) AddRow(Check.Ok, "Printer status", "Ready");
            else foreach (string problem in p.Problems) AddRow(Check.Bad, "Printer status", problem);
        }

        private static string DescribeQuality(PrinterResolution res)
        {
            if (res == null) return "Driver default";
            if (res.Kind == PrinterResolutionKind.Custom)
                return res.X == res.Y || res.Y <= 0 ? res.X + " dpi" : res.X + " x " + res.Y + " dpi";
            return res.Kind.ToString();   // High / Medium / Low / Draft
        }

        private enum Check { Ok, Warn, Bad, Info }

        private void AddRow(Check check, string setting, string value)
        {
            string mark;
            Color color;
            switch (check)
            {
                case Check.Ok: mark = "✓"; color = Theme.Success; break;
                case Check.Warn: mark = "!"; color = Theme.Warning; break;
                case Check.Bad: mark = "✕"; color = Theme.Danger; break;
                default: mark = "·"; color = Theme.TextSoft; break;
            }
            var item = new ListViewItem(mark) { UseItemStyleForSubItems = false, ForeColor = color };
            item.SubItems.Add(setting, Theme.TextSoft, _details.BackColor, _details.Font);
            item.SubItems.Add(value, check == Check.Info || check == Check.Ok ? _details.ForeColor : color,
                _details.BackColor, _details.Font);
            _details.Items.Add(item);
        }

        // ===================== Actions =====================

        /// <summary>Opens the Epson Printing Preferences; OK saves them and makes this printer the card printer.</summary>
        private void OpenEpsonSettings()
        {
            EpsonPrinter p = SelectedPrinter();
            if (p == null) return;
            bool inUse = IsInUse(p);

            // Start from the saved settings when they belong to this printer, else from the driver defaults.
            PrinterSettings ps;
            try { ps = inUse ? new CardPrinter(_settings).CreatePrinterSettings() : new PrinterSettings { PrinterName = p.Name }; }
            catch (InvalidOperationException) { ps = new PrinterSettings { PrinterName = p.Name }; }   // damaged / other printer
            if (!ps.IsValid)
            {
                AppDialog.Show(this, "Windows cannot open \"" + p.Name + "\".\n\nReinstall the Epson driver, then click Refresh.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            byte[] devMode;
            try { devMode = PrinterDevMode.ShowDriverDialog(Handle, ps); }
            catch (Exception ex)
            {
                AppDialog.Show(this, "The Epson driver dialog could not be opened:\n\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (devMode == null) return;   // cancelled

            bool switched = !inUse;
            _settings.PrinterName = p.Name;
            _settings.DevModeBase64 = Convert.ToBase64String(devMode);
            // The main window lists card printers only; make sure this one stays selectable there.
            if (!_settings.IsCardPrinter(p.Name)) _settings.ShowAllPrinters = true;

            if (SaveSettings())
                SetInfo((switched ? "\"" + p.Name + "\" is now the card printer. " : "") +
                        "Epson driver settings saved — check the list above for anything marked ! or ✕.", false);
            FillPrinterList(p.Name);   // "In use" column + details
        }

        /// <summary>Forgets the saved Epson settings of the card printer, so the driver defaults are used again.</summary>
        private void ResetToDefaults()
        {
            EpsonPrinter p = SelectedPrinter();
            if (p == null || !IsInUse(p) || string.IsNullOrEmpty(_settings.DevModeBase64)) return;

            if (AppDialog.Show(this,
                    "Forget the saved Epson settings for \"" + p.Name + "\"?\n\n" +
                    "Paper source and paper type go back to the driver defaults, which usually print on A4 paper " +
                    "instead of the ID card tray.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            _settings.DevModeBase64 = "";
            if (SaveSettings()) SetInfo("Saved settings removed — the Epson driver defaults are used.", false);
            ShowDetails();
        }

        private void OpenWindowsProperties()
        {
            EpsonPrinter p = SelectedPrinter();
            if (p == null) return;
            try
            {
                Process.Start("rundll32.exe", "printui.dll,PrintUIEntry /p /n \"" + p.Name + "\"");
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, "Could not open the Windows printer properties:\n\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool SaveSettings()
        {
            SettingsChanged = true;   // _settings changed in memory either way; the main window saves again on close
            try
            {
                _settings.Save();
                return true;
            }
            catch (Exception ex)
            {
                SetInfo("Could not save the settings: " + ex.Message, true);
                return false;
            }
        }

        // ===================== Helpers =====================

        private EpsonPrinter SelectedPrinter()
        {
            return _printers.SelectedItems.Count > 0 ? _printers.SelectedItems[0].Tag as EpsonPrinter : null;
        }

        private bool IsInUse(EpsonPrinter p)
        {
            return p != null && Same(p.Name, _settings.PrinterName);
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private void SetInfo(string text, bool error)
        {
            _lblInfo.ForeColor = error ? Theme.Danger : Theme.TextSoft;
            _lblInfo.Text = text;
        }

        private void UpdateButtons()
        {
            EpsonPrinter p = SelectedPrinter();
            _btnOpen.Enabled = p != null;
            _btnProperties.Enabled = p != null;
            _btnReset.Enabled = p != null && IsInUse(p) && !string.IsNullOrEmpty(_settings.DevModeBase64);
        }

        private static Label SectionLabel(string text)
        {
            return new Label
            {
                Text = text, Dock = DockStyle.Fill, Font = Theme.SmallCaps, ForeColor = Theme.TextMuted,
                BackColor = Theme.Surface, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(0, 0, 0, 5),
                Margin = new Padding(0)
            };
        }

        /// <summary>One installed Epson printer.</summary>
        private sealed class EpsonPrinter
        {
            public string Name;
            public string Driver;
            public string Port;
            public List<string> Problems = new List<string>();

            public string StatusText
            {
                get
                {
                    if (Problems.Count == 0) return "Ready";
                    return Problems.Any(x => x.IndexOf("OFFLINE", StringComparison.OrdinalIgnoreCase) >= 0)
                        ? "Offline" : "Needs attention";
                }
            }
        }
    }
}
