using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using NPIC_CARD_APP.Rendering;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// "About &amp; User Guide" page (F1), in English or Khmer: what Card Studio is, a first-time checklist, how to use
    /// every part of the app, keyboard shortcuts, troubleshooting, and a live System info topic (version, data files,
    /// printer, fonts, card images) that can be copied for support.
    /// Topics are listed on the left; the search box looks through every topic (Enter / F3 = next match).
    /// The English | ខ្មែរ switch changes the whole page and is remembered in <see cref="AppSettings.HelpKhmer"/>.
    /// Ctrl+F = search, Esc = clear the search, then close.
    /// UI names (buttons, menu items) stay in English in both languages, because the app itself is in English.
    /// </summary>
    public class HelpForm : ModalForm
    {
        /// <summary>Topics of the guide, in the order they are listed.</summary>
        public enum Page { Overview, QuickStart, Import, Photos, CardText, PrinterSetup, Printing, Rfid, History, Settings, Shortcuts, Troubleshooting, SystemInfo }

        private sealed class Topic
        {
            public string TitleEn, TitleKh, Glyph;
            public Action<GuideWriter> Write;
            public string Plain;   // the topic as plain text in the current language, for searching
            public NavItem Nav;
        }

        private readonly AppSettings _settings;
        private readonly List<Topic> _topics = new List<Topic>();
        private readonly RichTextBox _text;
        private readonly TextBox _txtSearch;
        private readonly Label _lblFind;
        private readonly PillTabs _lang;
        private readonly Button _btnClose, _btnCopy, _btnData;
        private readonly ToolTip _tip = new ToolTip();
        private readonly int _startTopic;
        private int _current = -1;
        private bool _kh;

        // System info facts, read once when the page opens (files may be on a slow network share).
        private readonly int _studentCount;
        private readonly string _studentSource;
        private string _installedVersion, _installedDir;
        private int _historyTotal, _historyToday, _rfidCount;
        private string _historyError, _rfidError;

        /// <param name="studentCount">Students loaded in the main window (-1 = unknown: the Activity row is left out).</param>
        /// <param name="studentSource">Where they came from (file name, API host, SQL server), or empty.</param>
        public HelpForm(AppSettings settings, Page start = Page.Overview, int studentCount = -1, string studentSource = null)
        {
            _settings = settings;
            _kh = settings.HelpKhmer;
            _studentCount = studentCount;
            _studentSource = studentSource ?? "";
            ReadFacts();   // before ApplyLanguage, which writes System info for the search index
            _startTopic = (int)start;
            Glyph = Theme.IcoQuestion;
            KeyPreview = true;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1040, work.Width), Math.Min(740, work.Height));
            MinimumSize = new Size(Math.Min(720, work.Width), Math.Min(480, work.Height));

            AddTopic("Overview", "ទិដ្ឋភាពទូទៅ", Theme.IcoHome, WriteOverview);
            AddTopic("Quick start", "ចាប់ផ្តើមរហ័ស", Theme.IcoInfo, WriteQuickStart);
            AddTopic("Import students", "នាំចូលនិស្សិត", Theme.IcoDownload, WriteImport);
            AddTopic("Photos", "រូបថត", Theme.IcoPhoto, WritePhotos);
            // The two Khmer names below are short forms of the page titles: the full ones do not fit the topic list.
            AddTopic("Card text & preview", "អត្ថបទ និងការមើលកាត", Theme.IcoFont, WriteCardText);
            AddTopic("Printer & tray setup", "ម៉ាស៊ីនបោះពុម្ព និងថាស", Theme.IcoSettings, WritePrinterSetup);
            AddTopic("Printing cards", "ការបោះពុម្ពកាត", Theme.IcoPrint, WritePrinting);
            AddTopic("RFID cards", "កាត RFID", Theme.IcoList, WriteRfid);
            AddTopic("History & gallery", "ប្រវត្តិ និងវិចិត្រសាល", Theme.IcoHistory, WriteHistory);
            AddTopic("Settings & folders", "ការកំណត់ និងថតឯកសារ", Theme.IcoFolder, WriteSettings);
            AddTopic("Keyboard shortcuts", "គ្រាប់ចុចកាត់", Theme.IcoMultiSelect, WriteShortcuts);
            AddTopic("Troubleshooting", "ដោះស្រាយបញ្ហា", Theme.IcoWarning, WriteTroubleshooting);
            AddTopic("System info", "ព័ត៌មានប្រព័ន្ធ", Theme.IcoDatabase, WriteSystemInfo);

            // ---- Toolbar: language, search ----
            _lang = new PillTabs("English", "ខ្មែរ") { Margin = new Padding(0, 0, 14, 0) };
            _lang.SelectedIndex = _kh ? 1 : 0;   // before the handler: the texts are applied once below
            _lang.SelectedIndexChanged += (s, e) => SetKhmer(_lang.SelectedIndex == 1);
            _tip.SetToolTip(_lang, "Language of the guide  ·  ភាសានៃការណែនាំ");

            _txtSearch = new TextBox { Width = 260, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 5, 12, 0) };
            _txtSearch.HandleCreated += (s, e) => UpdateCueBanner();
            _txtSearch.TextChanged += (s, e) => FindNext(true);
            _txtSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { FindNext(false); e.Handled = e.SuppressKeyPress = true; }
            };
            _lblFind = new Label
            {
                AutoSize = true, ForeColor = Theme.TextSoft, Font = Theme.Small, Margin = new Padding(0, 9, 0, 0),
                UseMnemonic = false   // topic names contain "&"
            };

            var toolbar = ModalUi.Toolbar();
            toolbar.Controls.Add(_lang);
            toolbar.Controls.Add(_txtSearch);
            toolbar.Controls.Add(_lblFind);

            // ---- Topic list (left) ----
            // 260 px: the item draws its text from x = 54 with a 14 px right margin, leaving 192 px — room for the
            // longest title on screen (≈ 175 px, "ការកំណត់ និងថតឯកសារ" in bold) even when a short window adds a scroll bar.
            var nav = new Panel { Dock = DockStyle.Left, Width = Scale(260), BackColor = Theme.Surface, AutoScroll = true, Padding = new Padding(0, 8, 0, 8) };
            nav.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.GridLine)) e.Graphics.DrawLine(pen, nav.Width - 1, 0, nav.Width - 1, nav.Height);
            };
            for (int i = _topics.Count - 1; i >= 0; i--)   // Dock = Top stacks the last added on top: add bottom-up
            {
                int index = i;
                Topic t = _topics[i];
                t.Nav = new NavItem(t.TitleEn, t.Glyph, (s, e) => ShowTopic(index)) { Dock = DockStyle.Top };
                nav.Controls.Add(t.Nav);
            }

            // ---- Guide text (right) ----
            _text = new ModernRichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false,
                HideSelection = false, ScrollBars = RichTextBoxScrollBars.Vertical, WordWrap = true,
                BackColor = Theme.Surface, ForeColor = Theme.TextMain, TabStop = true
            };
            var textHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(Scale(30), Scale(16), Scale(8), Scale(4)) };
            textHost.Controls.Add(_text);

            // ---- Footer ----
            var footer = ModalUi.Footer();
            _btnClose = ModalUi.Button("Close", PillKind.Primary, (s, e) => Close());
            _btnCopy = ModalUi.Button("Copy System Info", PillKind.Secondary, (s, e) => CopySystemInfo());
            _btnData = ModalUi.Button("Open Data Folder", PillKind.Secondary, (s, e) => OpenDataFolder());
            footer.Controls.Add(_btnClose);   // right-to-left: first added = rightmost
            footer.Controls.Add(_btnCopy);
            footer.Controls.Add(_btnData);

            // Docking runs in reverse add order: the fill control first, the edges last.
            Controls.Add(textHost);
            Controls.Add(nav);
            Controls.Add(toolbar);
            Controls.Add(footer);
            CancelButton = _btnClose;

            ApplyLanguage();   // the first topic is written in OnLoad
        }

        private void AddTopic(string titleEn, string titleKh, string glyph, Action<GuideWriter> write)
        {
            _topics.Add(new Topic { TitleEn = titleEn, TitleKh = titleKh, Glyph = glyph, Write = write });
        }

        private int Scale(int px) { return (int)Math.Round(px * DeviceDpi / 96f); }

        /// <summary>The English or the Khmer text, for the language shown.</summary>
        private string T(string en, string kh) { return _kh ? kh : en; }

        private string TitleOf(Topic t) { return _kh ? t.TitleKh : t.TitleEn; }

        // Topic list in Khmer: the card font (Khmer OS Siemreap, else Khmer UI), 9.5 pt. Keep every Khmer topic name
        // under ~175 px on screen (measured on the window, which draws Khmer wider than an offscreen bitmap).
        private static readonly Font KhmerNavFont = new Font(CardFonts.KhmerSiemreap, 9.5f);
        private static readonly Font KhmerNavBoldFont = new Font(CardFonts.KhmerSiemreap, 9.5f, FontStyle.Bold);

        /// <summary>The first topic is written here, once every handle exists (the RichTextBox needs its handle).</summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ShowTopic(_startTopic);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_current >= 0) _topics[_current].Nav.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                // First Esc clears the search, the next one closes the window.
                if (_txtSearch.TextLength > 0) _txtSearch.Clear();
                else Close();
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.F: _txtSearch.Focus(); _txtSearch.SelectAll(); return true;
                case Keys.F3: FindNext(false); return true;
                case Keys.Control | Keys.PageDown: ShowTopic((_current + 1) % _topics.Count); return true;
                case Keys.Control | Keys.PageUp: ShowTopic((_current + _topics.Count - 1) % _topics.Count); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ===================== Language =====================
        private void SetKhmer(bool khmer)
        {
            if (khmer == _kh) return;
            _kh = khmer;
            _settings.HelpKhmer = khmer;   // the main window saves the settings when this page closes
            ApplyLanguage();
            if (_current >= 0) ShowTopic(_current);
            if (_txtSearch.TextLength > 0) FindNext(true);
        }

        /// <summary>Window texts, topic names, buttons and the search index in the current language.</summary>
        private void ApplyLanguage()
        {
            Text = T("About & User Guide", "អំពី និងការណែនាំប្រើប្រាស់");
            Subtitle = T("What Card Studio does and how to use it", "Card Studio ធ្វើអ្វីខ្លះ និងរបៀបប្រើប្រាស់");

            foreach (Topic t in _topics)
            {
                t.Nav.Text = TitleOf(t);
                // Khmer titles in the Khmer card font: Segoe UI has no Khmer letters, so Windows would swap in
                // a wider font that cuts the long titles off.
                t.Nav.Font = _kh ? KhmerNavFont : Theme.Nav;
                t.Nav.ActiveFont = _kh ? KhmerNavBoldFont : null;
                t.Nav.Invalidate();
                var plain = new GuideWriter(null, 1f, _kh);
                t.Write(plain);
                t.Plain = plain.PlainText;
            }

            _btnClose.Text = T("Close", "បិទ");
            _btnCopy.Text = T("Copy System Info", "ចម្លងព័ត៌មានប្រព័ន្ធ");
            _btnData.Text = T("Open Data Folder", "បើកថតទិន្នន័យ");
            _tip.SetToolTip(_btnCopy, T(
                "Copy the version, file locations, printer, fonts and card images of this PC — paste it in a message to support.",
                "ចម្លងកំណែ ទីតាំងឯកសារ ម៉ាស៊ីនបោះពុម្ព ពុម្ពអក្សរ និងរូបភាពកាតនៃកុំព្យូទ័រនេះ — បិទភ្ជាប់វាក្នុងសារទៅអ្នកជំនួយបច្ចេកទេស។"));
            _tip.SetToolTip(_btnData, T("Open ", "បើក ") + DataFolder +
                T(" (settings, print history, RFID file).", " (ការកំណត់ ប្រវត្តិបោះពុម្ព ឯកសារ RFID)។"));

            _lblFind.Text = "";
            UpdateCueBanner();
        }

        private void UpdateCueBanner()
        {
            if (_txtSearch.IsHandleCreated)
                SetCueBanner(_txtSearch, T("Search the guide…  (Ctrl+F)", "ស្វែងរកក្នុងការណែនាំ…  (Ctrl+F)"));
        }

        // ===================== View =====================
        private void ShowTopic(int index)
        {
            if (index < 0 || index >= _topics.Count) return;
            if (_current >= 0) _topics[_current].Nav.Active = false;
            _current = index;
            _topics[index].Nav.Active = true;

            // Redraw once at the end: the text is built piece by piece.
            bool live = _text.IsHandleCreated;
            if (live) Native.SendMessage(_text.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                _text.ReadOnly = false;
                _text.Clear();
                _topics[index].Write(new GuideWriter(_text, DeviceDpi / 96f, _kh));
                _text.ReadOnly = true;
                _text.Select(0, 0);
                _text.ScrollToCaret();
            }
            finally
            {
                if (live)
                {
                    Native.SendMessage(_text.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                    _text.Invalidate();
                }
            }
        }

        /// <summary>
        /// Finds the search text: from the top of the current topic (<paramref name="fromStart"/>) or after the current
        /// match, then in the following topics, wrapping round to the current one.
        /// </summary>
        private void FindNext(bool fromStart)
        {
            string q = _txtSearch.Text.Trim();
            if (q.Length == 0)
            {
                _lblFind.Text = "";
                _text.Select(0, 0);
                return;
            }

            int from = fromStart ? 0 : _text.SelectionStart + _text.SelectionLength;
            if (FindIn(q, from)) return;

            for (int k = 1; k <= _topics.Count; k++)
            {
                int i = (_current + k) % _topics.Count;
                if (_topics[i].Plain.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (i != _current) ShowTopic(i);
                if (FindIn(q, 0)) return;
            }

            _lblFind.Text = T("No match for “", "រកមិនឃើញ “") + q + "”";
            _lblFind.ForeColor = Theme.Warning;
        }

        private bool FindIn(string q, int from)
        {
            if (from >= _text.TextLength) return false;
            int at = _text.Find(q, from, RichTextBoxFinds.None);   // None = not case-sensitive
            if (at < 0) return false;
            _text.Select(at, q.Length);
            _text.ScrollToCaret();
            _lblFind.Text = T("Found in ", "រកឃើញក្នុង ") + TitleOf(_topics[_current]) +
                            T("   ·   Enter / F3 = next", "   ·   Enter / F3 = បន្ទាប់");
            _lblFind.ForeColor = Theme.TextSoft;
            return true;
        }

        // ===================== System facts =====================
        /// <summary>Uninstall entry written by the Card Studio installer (CardStudio.Setup, ProductInfo.UninstallKey).</summary>
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CardStudio";

        /// <summary>Installed version, print history and RFID counts. Never throws: a problem becomes a warning row.</summary>
        private void ReadFacts()
        {
            try
            {
                // The installer writes to the 64-bit registry on 64-bit Windows; this AnyCPU app may run as 32-bit.
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                           Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
                using (RegistryKey key = hklm.OpenSubKey(UninstallKey))
                {
                    if (key != null)
                    {
                        _installedVersion = key.GetValue("DisplayVersion") as string;
                        _installedDir = key.GetValue("InstallLocation") as string;
                    }
                }
            }
            catch (Exception) { _installedVersion = _installedDir = null; }   // no access: show the exe's own version

            try
            {
                List<PrintRecord> history = PrintHistory.Load();
                _historyTotal = history.Count;
                _historyToday = history.Count(r => r.PrintedAt.Date == DateTime.Today);
            }
            catch (Exception ex) { _historyError = FirstLine(ex.Message); }

            try { _rfidCount = RfidCardWriter.CheckFile(RfidCardWriter.FilePath(_settings)); }
            catch (Exception ex) { _rfidError = FirstLine(ex.Message); }
        }

        /// <summary>
        /// The version to show: the installed version when this exe is the installed copy; otherwise the exe's own
        /// version, or "development build" when it was not stamped (0.0.0.0, e.g. run from Visual Studio).
        /// </summary>
        private string VersionText(out bool warn)
        {
            warn = false;
            string own = Application.ProductVersion;
            bool stamped = !string.IsNullOrEmpty(own) && own != "0.0.0.0";
            bool runningInstalled = !string.IsNullOrEmpty(_installedDir) && SamePath(_installedDir, AppDomain.CurrentDomain.BaseDirectory);

            if (!string.IsNullOrEmpty(_installedVersion) && runningInstalled) return _installedVersion;

            string dev = stamped ? own : T("Development build", "កំណែអភិវឌ្ឍន៍");
            if (!string.IsNullOrEmpty(_installedVersion))
            {
                warn = true;   // the installed copy is somewhere else: the user may be running the wrong one
                return dev + T("  ·  version " + _installedVersion + " is installed in " + _installedDir,
                               "  ·  កំណែ " + _installedVersion + " បានដំឡើងនៅ " + _installedDir);
            }
            return dev + T("  ·  not installed with the Card Studio installer", "  ·  មិនបានដំឡើងដោយកម្មវិធីដំឡើង Card Studio");
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static string FirstLine(string text)
        {
            return (text ?? "").Split('\n')[0].Trim();
        }

        // ===================== Actions =====================
        private static string DataFolder { get { return Path.GetDirectoryName(AppSettings.FilePath); } }

        private void CopySystemInfo()
        {
            var plain = new GuideWriter(null, 1f, _kh);
            WriteSystemInfo(plain);
            try
            {
                Clipboard.SetText(plain.PlainText);
                _lblFind.Text = T("System info copied to the clipboard.", "បានចម្លងព័ត៌មានប្រព័ន្ធទៅ clipboard។");
                _lblFind.ForeColor = Theme.Success;
            }
            catch (ExternalException ex)
            {
                AppDialog.Show(this, T("Could not copy to the clipboard:", "មិនអាចចម្លងទៅ clipboard បានទេ៖") + "\n\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenDataFolder()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                Process.Start("explorer.exe", "\"" + DataFolder + "\"");
            }
            catch (Exception ex)
            {
                AppDialog.Show(this, T("Could not open the folder:", "មិនអាចបើកថតបានទេ៖") + "\n\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ===================== Topics =====================
        private void WriteOverview(GuideWriter w)
        {
            w.Title(T("Card Studio — ID Card Printer", "Card Studio — កម្មវិធីបោះពុម្ពកាតសម្គាល់ខ្លួន"));
            w.Lead(T("Card Studio prints student ID cards for NPIC (National Polytechnic Institute of Cambodia) on an Epson " +
                     "card printer, and records the RFID chip number of every card it prints.",
                     "Card Studio បោះពុម្ពកាតសម្គាល់ខ្លួននិស្សិតរបស់ វិទ្យាស្ថានជាតិពហុបច្ចេកទេសកម្ពុជា (NPIC) ដោយប្រើម៉ាស៊ីនបោះពុម្ពកាត Epson " +
                     "ហើយកត់ត្រាលេខបន្ទះឈីប RFID នៃកាតនីមួយៗដែលបានបោះពុម្ព។"));

            w.Heading(T("What the system does", "ប្រព័ន្ធនេះធ្វើអ្វីខ្លះ"));
            w.Bullet(T("**Loads the student list** from Excel, CSV, Google Sheets, the school's web API or SQL Server.",
                       "**នាំចូលបញ្ជីនិស្សិត** ពី Excel, CSV, Google Sheets, Web API របស់សាលា ឬ SQL Server។"));
            w.Bullet(T("**Finds each student's photo** in a photo folder by student ID, and can download photos from Google Drive or OneDrive.",
                       "**ស្វែងរករូបថតនិស្សិតម្នាក់ៗ** ក្នុងថតរូបថត តាមអត្តលេខ ហើយអាចទាញយករូបថតពី Google Drive ឬ OneDrive។"));
            w.Bullet(T("**Draws both sides of the card** at real size (85.6 × 54 mm): the front with the student's details, photo, QR code " +
                       "and expiry date, and the back with the institute's details.",
                       "**គូរកាតទាំងពីរផ្នែក** តាមទំហំពិត (85.6 × 54 mm)៖ ផ្នែកខាងមុខមានព័ត៌មាននិស្សិត រូបថត QR code និងថ្ងៃផុតកំណត់ " +
                       "ហើយផ្នែកខាងក្រោយមានព័ត៌មានវិទ្យាស្ថាន។"));
            w.Bullet(T("**Prints one or two cards per tray load**, front and back separately, and checks the printer before every job.",
                       "**បោះពុម្ពកាតមួយ ឬពីរក្នុងមួយថាស** ផ្នែកខាងមុខ និងខាងក្រោយដាច់ដោយឡែកពីគ្នា ហើយពិនិត្យម៉ាស៊ីនបោះពុម្ពមុនពេលបោះពុម្ពរាល់ដង។"));
            w.Bullet(T("**Keeps a print history** of every card side printed: who, which side, when, on which printer.",
                       "**រក្សាទុកប្រវត្តិបោះពុម្ព** នៃផ្នែកកាតនីមួយៗដែលបានបោះពុម្ព៖ អ្នកណា ផ្នែកណា ពេលណា និងលើម៉ាស៊ីនណា។"));
            w.Bullet(T("**Saves the RFID card number** (UID) of each printed card to a CSV file, which can be shared by several PCs.",
                       "**រក្សាទុកលេខកាត RFID** (UID) នៃកាតនីមួយៗដែលបានបោះពុម្ព ទៅក្នុងឯកសារ CSV ដែលកុំព្យូទ័រច្រើនគ្រឿងអាចប្រើរួមគ្នាបាន។"));

            w.Heading(T("The main window", "ផ្ទាំងមេ"));
            w.Bullet(T("**Sidebar (left)** — every command: importing, CSV files, gallery, print history, RFID cards, print queue, " +
                       "driver setup, photo folder, background removal, card text, folders, settings and this guide.",
                       "**របារចំហៀង (ខាងឆ្វេង)** — ពាក្យបញ្ជាទាំងអស់៖ នាំចូល ឯកសារ CSV វិចិត្រសាល ប្រវត្តិបោះពុម្ព កាត RFID ជួរបោះពុម្ព " +
                       "ការរៀបចំ driver ថតរូបថត ការលុបផ្ទៃខាងក្រោយ អត្ថបទកាត ថតឯកសារ ការកំណត់ និងការណែនាំនេះ។"));
            w.Bullet(T("**Header** — **Print side** (Front / Back) and **Print Selected**.",
                       "**ផ្នែកខាងលើ** — **Print side** (Front / Back) និង **Print Selected**។"));
            w.Bullet(T("**Tiles** — Total students (click: gallery), Selected to print (with the number of tray loads), " +
                       "Printed today (click: print history).",
                       "**ប្រអប់ស្ថិតិ** — Total students (ចុច៖ វិចិត្រសាល), Selected to print (ព្រមទាំងចំនួនថាស), " +
                       "Printed today (ចុច៖ ប្រវត្តិបោះពុម្ព)។"));
            w.Bullet(T("**Students** — the search box and the list. Select one or more rows to preview and print them.",
                       "**Students** — ប្រអប់ស្វែងរក និងបញ្ជីនិស្សិត។ ជ្រើសរើសជួរមួយ ឬច្រើន ដើម្បីមើលជាមុន និងបោះពុម្ព។"));
            w.Bullet(T("**Card preview** — front and back of the selected student. The side that will print has a violet outline.",
                       "**Card preview** — ផ្នែកខាងមុខ និងខាងក្រោយនៃនិស្សិតដែលបានជ្រើស។ ផ្នែកដែលនឹងត្រូវបោះពុម្ពមានស៊ុមពណ៌ស្វាយ។"));
            w.Bullet(T("**Print setup** — three tabs: **Printer** (driver and checks), **Tray** (slot positions and card orientation) " +
                       "and **RFID** (save the printed card's chip number).",
                       "**Print setup** — មានបីផ្ទាំង៖ **Printer** (driver និងការពិនិត្យ), **Tray** (ទីតាំងរន្ធ និងទិសដៅកាត) " +
                       "និង **RFID** (រក្សាទុកលេខឈីបនៃកាតដែលបានបោះពុម្ព)។"));
            w.Bullet(T("**Status line (bottom)** — the result of the last action. Green = done, orange = needs attention, red = error.",
                       "**បន្ទាត់ស្ថានភាព (ខាងក្រោម)** — លទ្ធផលនៃសកម្មភាពចុងក្រោយ។ បៃតង = រួចរាល់, ទឹកក្រូច = ត្រូវពិនិត្យ, ក្រហម = កំហុស។"));

            w.Heading(T("The usual workflow", "លំដាប់ការងារធម្មតា"));
            w.Step(1, T("Import the students.", "នាំចូលបញ្ជីនិស្សិត។"));
            w.Step(2, T("Check that every student has a photo.", "ពិនិត្យថានិស្សិតគ្រប់រូបមានរូបថត។"));
            w.Step(3, T("Select the students and print the **front** side.", "ជ្រើសរើសនិស្សិត ហើយបោះពុម្ពផ្នែក **ខាងមុខ**។"));
            w.Step(4, T("Put the cards back in the tray and print the **back** side.", "ដាក់កាតចូលថាសវិញ ហើយបោះពុម្ពផ្នែក **ខាងក្រោយ**។"));
            w.Step(5, T("Tap each card on the RFID reader to save its number.", "ប៉ះកាតនីមួយៗលើឧបករណ៍អាន RFID ដើម្បីរក្សាទុកលេខកាត។"));
            w.Note(T("New to the app? Open **Quick start** in the list on the left.",
                     "ទើបប្រើលើកដំបូង? សូមបើក **ចាប់ផ្តើមរហ័ស** នៅក្នុងបញ្ជីខាងឆ្វេង។"));
        }

        private void WriteQuickStart(GuideWriter w)
        {
            string fonts = string.Join(", ", CardFonts.Required);
            w.Title(T("Quick start", "ចាប់ផ្តើមរហ័ស"));
            w.Lead(T("Do steps 1–3 once on each PC. After that, every print run is steps 4–8.",
                     "ធ្វើជំហានទី 1–3 តែម្តងគត់នៅលើកុំព្យូទ័រនីមួយៗ។ បន្ទាប់មក ការបោះពុម្ពរាល់លើកគឺជំហានទី 4–8។"));

            w.Heading(T("Set up the PC (once)", "រៀបចំកុំព្យូទ័រ (តែម្តង)"));
            w.Step(1, T("**Install the card fonts** for all users: " + fonts + ". The app warns at start-up when one is missing. " +
                        "See **System info** for what is installed.",
                        "**ដំឡើងពុម្ពអក្សរកាត** សម្រាប់អ្នកប្រើទាំងអស់៖ " + fonts + "។ កម្មវិធីនឹងព្រមាននៅពេលបើក ប្រសិនបើខ្វះពុម្ពអក្សរណាមួយ។ " +
                        "មើល **ព័ត៌មានប្រព័ន្ធ** ដើម្បីដឹងថាពុម្ពអក្សរណាបានដំឡើងហើយ។"));
            w.Step(2, T("**Install the Epson printer driver** (Epson L8050 with the card tray). In **Print setup → Printer**, pick the " +
                        "printer, then click **Driver Setup…** to save its paper source, paper type and quality.",
                        "**ដំឡើង driver ម៉ាស៊ីនបោះពុម្ព Epson** (Epson L8050 ជាមួយថាសកាត)។ នៅ **Print setup → Printer** ជ្រើសរើសម៉ាស៊ីនបោះពុម្ព " +
                        "រួចចុច **Driver Setup…** ដើម្បីរក្សាទុកប្រភពក្រដាស ប្រភេទក្រដាស និងគុណភាព។"));
            w.Step(3, T("**Calibrate the tray**: click **Test Print**, look at the calibration card, adjust **Slot X / Y** in the " +
                        "**Tray** tab and click **Save**. Repeat until the lines are centred (see **Printer & tray setup**).",
                        "**តម្រឹមថាស**៖ ចុច **Test Print** មើលកាតតម្រឹម កែ **Slot X / Y** នៅផ្ទាំង **Tray** ហើយចុច **Save**។ " +
                        "ធ្វើម្តងទៀតរហូតដល់បន្ទាត់នៅចំកណ្តាល (មើល **ម៉ាស៊ីនបោះពុម្ព និងថាស**)។"));

            w.Heading(T("Print a batch of cards", "បោះពុម្ពកាតមួយបាច់"));
            w.Step(4, T("**Import the students** — sidebar **Import Students…** (Ctrl+L).",
                        "**នាំចូលនិស្សិត** — របារចំហៀង **Import Students…** (Ctrl+L)។"));
            w.Step(5, T("**Choose the photo folder** — sidebar **Photo Folder…**. Photos are named after the student ID, " +
                        "for example NPIC24001.jpg.",
                        "**ជ្រើសរើសថតរូបថត** — របារចំហៀង **Photo Folder…**។ ឈ្មោះរូបថតគឺជាអត្តលេខនិស្សិត ឧទាហរណ៍ NPIC24001.jpg។"));
            w.Step(6, T("**Check the card text** — sidebar **Card Text…** (director signature, dates and fixed text).",
                        "**ពិនិត្យអត្ថបទកាត** — របារចំហៀង **Card Text…** (ហត្ថលេខានាយក កាលបរិច្ឆេទ និងអត្ថបទថេរ)។"));
            w.Step(7, T("**Print** — select the students, choose **Front**, click **Print Selected** (Ctrl+P) and follow the " +
                        "tray-load messages. Then choose **Back** and print again with the same selection.",
                        "**បោះពុម្ព** — ជ្រើសរើសនិស្សិត ជ្រើស **Front** ចុច **Print Selected** (Ctrl+P) ហើយធ្វើតាមសារសម្រាប់ថាសនីមួយៗ។ " +
                        "បន្ទាប់មកជ្រើស **Back** ហើយបោះពុម្ពម្តងទៀតដោយប្រើការជ្រើសរើសដដែល។"));
            w.Step(8, T("**Save the RFID numbers** — the **RFID** tab opens after printing. Select each student, tap their " +
                        "card on the reader; it is saved automatically.",
                        "**រក្សាទុកលេខ RFID** — ផ្ទាំង **RFID** បើកដោយស្វ័យប្រវត្តិក្រោយការបោះពុម្ព។ ជ្រើសរើសនិស្សិតម្នាក់ៗ " +
                        "ហើយប៉ះកាតរបស់គេលើឧបករណ៍អាន។ លេខកាតត្រូវបានរក្សាទុកដោយស្វ័យប្រវត្តិ។"));
            w.Note(T("Not sure everything is ready? Sidebar **Settings & Help…** → **All Settings…** (Ctrl+,) shows every setting, with problems in orange.",
                     "មិនច្បាស់ថាអ្វីៗរួចរាល់ឬនៅ? របារចំហៀង **Settings & Help…** → **All Settings…** (Ctrl+,) បង្ហាញការកំណត់ទាំងអស់ ហើយបញ្ហាត្រូវបានបង្ហាញជាពណ៌ទឹកក្រូច។"));
        }

        private void WriteImport(GuideWriter w)
        {
            w.Title(T("Import students", "នាំចូលនិស្សិត"));
            w.Lead(T("Sidebar **Import Students…** (Ctrl+L) shows every source. Importing replaces the current list.",
                     "របារចំហៀង **Import Students…** (Ctrl+L) បង្ហាញប្រភពទាំងអស់។ ការនាំចូលនឹងជំនួសបញ្ជីបច្ចុប្បន្ន។"));

            w.Heading(T("Sources", "ប្រភព"));
            w.Key("From Excel…", T("An .xlsx, .xlsm or .xls workbook. (Ctrl+E)", "ឯកសារ Excel ប្រភេទ .xlsx, .xlsm ឬ .xls។ (Ctrl+E)"));
            w.Key("From CSV…", T("A UTF-8 CSV file. (Ctrl+O)", "ឯកសារ CSV ប្រភេទ UTF-8។ (Ctrl+O)"));
            w.Key("From API…", T("The school's web API, returning JSON or CSV, with an optional access token. (Ctrl+I)",
                                 "Web API របស់សាលា ដែលផ្តល់ JSON ឬ CSV ព្រមទាំង access token (ស្រេចចិត្ត)។ (Ctrl+I)"));
            w.Key("SQL Server…", T("A table or a SELECT query. The app only reads the database. (Ctrl+D)",
                                   "តារាង ឬ SELECT query។ កម្មវិធីគ្រាន់តែអានទិន្នន័យប៉ុណ្ណោះ។ (Ctrl+D)"));
            w.Key("Google Sheets", T("A sheet shared as “Anyone with the link”. (Ctrl+Shift+G)",
                                     "Google Sheet ដែលចែករំលែកជា “Anyone with the link”។ (Ctrl+Shift+G)"));
            w.Key("Excel template", T("No list yet? **Save Excel Template…** gives an empty workbook with the right columns.",
                                      "មិនទាន់មានបញ្ជី? **Save Excel Template…** ផ្តល់ឯកសារ Excel ទទេដែលមានជួរឈរត្រឹមត្រូវ។"));

            w.Heading(T("Columns", "ជួរឈរ"));
            w.Para(T("Every source uses the same column names (row 1 of a sheet or CSV):",
                     "ប្រភពទាំងអស់ប្រើឈ្មោះជួរឈរដូចគ្នា (ជួរទី 1 នៃ sheet ឬ CSV)៖"));
            w.Key("StudentId", T("Required. Printed on the card and used as the photo file name.",
                                 "ចាំបាច់។ បោះពុម្ពលើកាត ហើយប្រើជាឈ្មោះឯកសាររូបថត។"));
            w.Key("NameKh", T("Name in Khmer.", "ឈ្មោះជាអក្សរខ្មែរ។"));
            w.Key("NameEn", T("Name in Latin letters (printed in capitals).", "ឈ្មោះជាអក្សរឡាតាំង (បោះពុម្ពជាអក្សរធំ)។"));
            w.Key("Gender", T("M / F (or the Khmer words).", "M / F (ឬ ប្រុស / ស្រី)។"));
            w.Key("DateOfBirth", T("dd/MM/yyyy, e.g. 17/11/2002.", "dd/MM/yyyy ឧ. 17/11/2002។"));
            w.Key("Faculty", T("Faculty / department.", "មហាវិទ្យាល័យ / ដេប៉ាតឺម៉ង់។"));
            w.Key("Level", T("Study level.", "កម្រិតសិក្សា។"));
            w.Key("Generation", T("Generation number.", "ជំនាន់ទី។"));
            w.Key("ValidUntil", T("Expiry date, dd/MM/yyyy.", "ថ្ងៃផុតកំណត់ dd/MM/yyyy។"));
            w.Key("QrData", T("Optional text or link for the student's QR code.", "អត្ថបទ ឬតំណភ្ជាប់សម្រាប់ QR code របស់និស្សិត (ស្រេចចិត្ត)។"));

            w.Heading(T("Good to know", "គួរដឹង"));
            w.Bullet(T("A list opened **from a file** comes back by itself the next time the app starts. Lists from the API, " +
                       "SQL Server or Google Sheets must be imported again.",
                       "បញ្ជីដែលបើក **ពីឯកសារ** នឹងត្រលប់មកវិញដោយស្វ័យប្រវត្តិ នៅពេលបើកកម្មវិធីលើកក្រោយ។ " +
                       "បញ្ជីពី API, SQL Server ឬ Google Sheets ត្រូវនាំចូលម្តងទៀត។"));
            w.Bullet(T("**CSV Files…** (Ctrl+Shift+C) manages the student CSV files of one folder: check, preview, create, " +
                       "rename, delete or load them.",
                       "**CSV Files…** (Ctrl+Shift+C) គ្រប់គ្រងឯកសារ CSV និស្សិតក្នុងថតមួយ៖ ពិនិត្យ មើលជាមុន បង្កើត ប្តូរឈ្មោះ លុប ឬនាំចូល។"));
            w.Bullet(T("**Search** (Ctrl+F) by ID, name or faculty. Esc clears the search; the Down arrow moves into the list.",
                       "**ស្វែងរក** (Ctrl+F) តាមអត្តលេខ ឈ្មោះ ឬមហាវិទ្យាល័យ។ Esc សម្អាតការស្វែងរក ហើយគ្រាប់ចុចព្រួញចុះ ផ្លាស់ទីទៅក្នុងបញ្ជី។"));
            w.Bullet(T("API tokens and SQL passwords are saved encrypted for the current Windows user only.",
                       "API token និងពាក្យសម្ងាត់ SQL ត្រូវបានរក្សាទុកដោយអ៊ិនគ្រីប សម្រាប់តែអ្នកប្រើ Windows បច្ចុប្បន្នប៉ុណ្ណោះ។"));
        }

        private void WritePhotos(GuideWriter w)
        {
            w.Title(T("Photos", "រូបថត"));
            w.Lead(T("Each card shows the photo whose file name is the student's ID.",
                     "កាតនីមួយៗបង្ហាញរូបថតដែលមានឈ្មោះឯកសារដូចអត្តលេខនិស្សិត។"));

            w.Heading(T("Photo folder", "ថតរូបថត"));
            w.Bullet(T("Sidebar **Photo Folder…** lets you choose the folder, or open it in Explorer to add or rename photos.",
                       "របារចំហៀង **Photo Folder…** អនុញ្ញាតឱ្យជ្រើសរើសថត ឬបើកវាក្នុង Explorer ដើម្បីបន្ថែម ឬប្តូរឈ្មោះរូបថត។"));
            w.Bullet(T("Name each photo **{StudentId}.jpg** (or .jpeg, .png, .bmp) — for example **NPIC24001.jpg**. " +
                       "The name must match the ID in the list exactly.",
                       "ដាក់ឈ្មោះរូបថតនីមួយៗជា **{StudentId}.jpg** (ឬ .jpeg, .png, .bmp) — ឧទាហរណ៍ **NPIC24001.jpg**។ " +
                       "ឈ្មោះត្រូវតែដូចអត្តលេខក្នុងបញ្ជីទាំងស្រុង។"));
            w.Bullet(T("After you change photos in Explorer, the preview refreshes when you come back to the app.",
                       "ក្រោយពេលកែរូបថតក្នុង Explorer ការមើលជាមុននឹងធ្វើបច្ចុប្បន្នភាព នៅពេលអ្នកត្រលប់មកកម្មវិធីវិញ។"));

            w.Heading(T("Download photos", "ទាញយករូបថត"));
            w.Bullet(T("**Google Drive Photos…** (Ctrl+Shift+D) — copies photos from a shared Drive folder into the photo folder.",
                       "**Google Drive Photos…** (Ctrl+Shift+D) — ចម្លងរូបថតពីថត Google Drive ដែលបានចែករំលែក មកក្នុងថតរូបថត។"));
            w.Bullet(T("**OneDrive Photos…** (Ctrl+Shift+O) — copies photos from a OneDrive folder (synced on this PC, or by link after signing in).",
                       "**OneDrive Photos…** (Ctrl+Shift+O) — ចម្លងរូបថតពីថត OneDrive (ដែលធ្វើសមកាលកម្មលើកុំព្យូទ័រនេះ ឬតាមតំណភ្ជាប់ក្រោយពេលចូលគណនី)។"));

            w.Heading(T("Remove the background", "លុបផ្ទៃខាងក្រោយ"));
            w.Para(T("Select a student, then sidebar **Remove Background…** (Ctrl+B). Crop the photo and remove the wall or " +
                     "screen behind the student. The original is kept in the **_original** sub-folder, so you can redo it or " +
                     "**Restore Original** at any time.",
                     "ជ្រើសរើសនិស្សិត រួចចុចរបារចំហៀង **Remove Background…** (Ctrl+B)។ កាត់រូបថត ហើយលុបជញ្ជាំង ឬផ្ទាំងខាងក្រោយនិស្សិតចេញ។ " +
                     "រូបដើមត្រូវបានរក្សាទុកក្នុងថតរង **_original** ដូច្នេះអ្នកអាចធ្វើម្តងទៀត ឬចុច **Restore Original** នៅពេលណាក៏បាន។"));

            w.Note(T("**Gallery** (Ctrl+G) shows every student with their photo — the quickest way to spot missing photos.",
                     "**Gallery** (Ctrl+G) បង្ហាញនិស្សិតទាំងអស់ជាមួយរូបថត — ជាវិធីលឿនបំផុតដើម្បីរកមើលរូបថតដែលខ្វះ។"));
        }

        private void WriteCardText(GuideWriter w)
        {
            w.Title(T("Card text & preview", "អត្ថបទកាត និងការមើលជាមុន"));

            w.Heading(T("Card text", "អត្ថបទកាត"));
            w.Para(T("Sidebar **Card Text…** edits the fixed text printed on every card (institute details, issue date, " +
                     "contact lines and so on) and chooses the **director signature** image. **Save** applies it to the preview " +
                     "straight away; **Cancel** or Esc changes nothing.",
                     "របារចំហៀង **Card Text…** កែអត្ថបទថេរដែលបោះពុម្ពលើកាតគ្រប់សន្លឹក (ព័ត៌មានវិទ្យាស្ថាន ថ្ងៃចេញកាត ព័ត៌មានទំនាក់ទំនង ។ល។) " +
                     "និងជ្រើសរើសរូប **ហត្ថលេខានាយក**។ **Save** អនុវត្តលើការមើលជាមុនភ្លាមៗ ចំណែក **Cancel** ឬ Esc មិនផ្លាស់ប្តូរអ្វីទេ។"));

            w.Heading(T("Preview", "ការមើលជាមុន"));
            w.Bullet(T("The preview shows the **current row** of the list: front on the left, back on the right.",
                       "ការមើលជាមុនបង្ហាញ **ជួរបច្ចុប្បន្ន** នៃបញ្ជី៖ ផ្នែកខាងមុខនៅខាងឆ្វេង ផ្នែកខាងក្រោយនៅខាងស្តាំ។"));
            w.Bullet(T("**Click** a preview to make that side the print side (violet outline). **Double-click** it to enlarge.",
                       "**ចុច** លើរូបមើលជាមុន ដើម្បីកំណត់ផ្នែកនោះជាផ្នែកត្រូវបោះពុម្ព (ស៊ុមពណ៌ស្វាយ)។ **ចុចពីរដង** ដើម្បីពង្រីក។"));
            w.Bullet(T("**Enlarge** or F11 opens the full-screen preview; double-clicking a row in the list does the same.",
                       "**Enlarge** ឬ F11 បើកការមើលជាមុនពេញអេក្រង់។ ការចុចពីរដងលើជួរក្នុងបញ្ជីក៏ធ្វើដូចគ្នាដែរ។"));
            w.Bullet(T("**Grid** puts a millimetre grid over the preview to check positions. The grid is never printed.",
                       "**Grid** បង្ហាញក្រឡាចត្រង្គមីលីម៉ែត្រលើការមើលជាមុន ដើម្បីពិនិត្យទីតាំង។ ក្រឡាចត្រង្គនេះមិនត្រូវបានបោះពុម្ពទេ។"));
        }

        private void WritePrinterSetup(GuideWriter w)
        {
            w.Title(T("Printer & tray setup", "រៀបចំម៉ាស៊ីនបោះពុម្ព និងថាស"));
            w.Lead(T("Everything is in the **Print setup** card at the bottom right of the main window.",
                     "អ្វីៗទាំងអស់នៅក្នុងប្រអប់ **Print setup** នៅជ្រុងខាងក្រោមខាងស្តាំនៃផ្ទាំងមេ។"));

            w.Heading(T("Printer tab", "ផ្ទាំង Printer"));
            w.Key("Printer list", T("Shows card printers only. Tick **Show all** to list every installed printer.",
                                    "បង្ហាញតែម៉ាស៊ីនបោះពុម្ពកាត។ ធីក **Show all** ដើម្បីបង្ហាញម៉ាស៊ីនបោះពុម្ពទាំងអស់ដែលបានដំឡើង។"));
            w.Key("Driver Setup…", T("Epson printers: save the paper source, paper type and quality. (Ctrl+Shift+E)",
                                     "ម៉ាស៊ីន Epson៖ រក្សាទុកប្រភពក្រដាស ប្រភេទក្រដាស និងគុណភាព។ (Ctrl+Shift+E)"));
            w.Key("Check Printer", T("Shows the page setup and any problem found (offline, error, stuck jobs, driver not set up).",
                                     "បង្ហាញការកំណត់ទំព័រ និងបញ្ហាដែលរកឃើញ (offline, កំហុស, ការងារជាប់គាំង, driver មិនទាន់រៀបចំ)។"));
            w.Key("Test Print", T("Prints a calibration card on each slot in use.", "បោះពុម្ពកាតតម្រឹមលើរន្ធនីមួយៗដែលកំពុងប្រើ។"));
            w.Key("Print Queue…", T("Jobs waiting in Windows — cancel a stuck job here.",
                                    "ការងារដែលកំពុងរង់ចាំក្នុង Windows — បោះបង់ការងារដែលជាប់គាំងនៅទីនេះ។"));

            w.Heading(T("Tray tab", "ផ្ទាំង Tray"));
            w.Key("Slot 1 / Slot 2", T("Position of each card pocket on the tray, X and Y in millimetres.",
                                       "ទីតាំងរន្ធដាក់កាតនីមួយៗលើថាស គិតជាមីលីម៉ែត្រ (X និង Y)។"));
            w.Key("Per tray load", T("**Auto** = 2 cards per load when 2 or more are selected and slot 2 is set up. **1** = slot 1 only. **2** = always both slots.",
                                     "**Auto** = កាត 2 ក្នុងមួយថាស នៅពេលជ្រើសរើស 2 ឬច្រើនជាងនេះ ហើយរន្ធទី 2 បានរៀបចំរួច។ **1** = តែរន្ធទី 1។ **2** = ប្រើរន្ធទាំងពីរជានិច្ច។"));
            w.Key("Card on tray", T("Horizontal, or Vertical (turn right / left 90°). If cards come out upside down, pick the other Vertical option.",
                                    "ផ្តេក ឬបញ្ឈរ (បង្វិលស្តាំ / ឆ្វេង 90°)។ ប្រសិនបើកាតចេញមកបញ្ច្រាស សូមជ្រើសជម្រើសបញ្ឈរមួយទៀត។"));
            w.Key("Rotate 180°", T("Turn the front and/or back side round when printing.",
                                   "បង្វិលផ្នែកខាងមុខ និង/ឬខាងក្រោយ 180° ពេលបោះពុម្ព។"));
            w.Key("Save", T("Saves the tray values. They are also saved when you print and when you close the app.",
                            "រក្សាទុកតម្លៃថាស។ វាក៏ត្រូវបានរក្សាទុកពេលបោះពុម្ព និងពេលបិទកម្មវិធីផងដែរ។"));

            w.Heading(T("Calibrating with Test Print", "តម្រឹមដោយប្រើ Test Print"));
            w.Para(T("The red line is the card edge, and each black line is 1 mm.",
                     "បន្ទាត់ក្រហមគឺគែមកាត ហើយបន្ទាត់ខ្មៅនីមួយៗ = 1 mm។"));
            w.Bullet(T("Lines cut off on the **right** → decrease X.   On the **left** → increase X.",
                       "បន្ទាត់ត្រូវកាត់បាត់នៅខាង **ស្តាំ** → បន្ថយ X។   នៅខាង **ឆ្វេង** → បង្កើន X។"));
            w.Bullet(T("Lines cut off at the **bottom** → decrease Y.   At the **top** → increase Y.",
                       "បន្ទាត់ត្រូវកាត់បាត់នៅខាង **ក្រោម** → បន្ថយ Y។   នៅខាង **លើ** → បង្កើន Y។"));
            w.Para(T("Change the value by the number of millimetres that are cut off, click **Save**, and test again.",
                     "ប្តូរតម្លៃតាមចំនួនមីលីម៉ែត្រដែលត្រូវកាត់បាត់ ចុច **Save** ហើយសាកល្បងម្តងទៀត។"));
        }

        private void WritePrinting(GuideWriter w)
        {
            w.Title(T("Printing cards", "ការបោះពុម្ពកាត"));

            w.Heading(T("Step by step", "មួយជំហានម្តងៗ"));
            w.Step(1, T("**Select the students** in the list. Use Ctrl+click or Shift+click to select several. " +
                        "The **Selected to print** tile shows how many tray loads it will take.",
                        "**ជ្រើសរើសនិស្សិត** ក្នុងបញ្ជី។ ប្រើ Ctrl+ចុច ឬ Shift+ចុច ដើម្បីជ្រើសរើសច្រើននាក់។ " +
                        "ប្រអប់ **Selected to print** បង្ហាញថាត្រូវប្រើប៉ុន្មានថាស។"));
            w.Step(2, T("**Choose the side**: **Front** or **Back** in the header, or click the preview of that side.",
                        "**ជ្រើសរើសផ្នែក**៖ **Front** ឬ **Back** នៅផ្នែកខាងលើ ឬចុចលើរូបមើលជាមុននៃផ្នែកនោះ។"));
            w.Step(3, T("Click **Print Selected** (Ctrl+P). The app checks the printer first and warns you about any problem.",
                        "ចុច **Print Selected** (Ctrl+P)។ កម្មវិធីពិនិត្យម៉ាស៊ីនបោះពុម្ពជាមុន ហើយព្រមានអ្នកប្រសិនបើមានបញ្ហា។"));
            w.Step(4, T("For each tray load a message lists the students in **Slot 1** and **Slot 2**. Click **OK** to send the job, " +
                        "then insert the tray when the printer light turns on.",
                        "សម្រាប់ថាសនីមួយៗ សារមួយបង្ហាញឈ្មោះនិស្សិតនៅ **Slot 1** និង **Slot 2**។ ចុច **OK** ដើម្បីផ្ញើការងារ " +
                        "រួចបញ្ចូលថាស នៅពេលភ្លើងម៉ាស៊ីនបោះពុម្ពភ្លឺ។"));
            w.Step(5, T("Wait until the card is finished and the tray is ejected before you click **OK** for the next load. " +
                        "**Cancel** stops the remaining loads.",
                        "រង់ចាំរហូតដល់កាតបោះពុម្ពរួច ហើយថាសត្រូវបានបញ្ចេញ មុននឹងចុច **OK** សម្រាប់ថាសបន្ទាប់។ " +
                        "**Cancel** បញ្ឈប់ថាសដែលនៅសល់។"));
            w.Step(6, T("Put the printed cards back in the tray turned over, choose the other side, and print again. " +
                        "The selection is kept, so you don't need to select the students again.",
                        "ដាក់កាតដែលបានបោះពុម្ពចូលថាសវិញដោយបង្វិលផ្ទុយ ជ្រើសរើសផ្នែកម្ខាងទៀត ហើយបោះពុម្ពម្តងទៀត។ " +
                        "ការជ្រើសរើសនៅដដែល ដូច្នេះមិនចាំបាច់ជ្រើសរើសនិស្សិតម្តងទៀតទេ។"));

            w.Heading(T("After printing", "ក្រោយពេលបោះពុម្ព"));
            w.Bullet(T("Every side printed is added to the **Print History** and the **Printed today** tile.",
                       "ផ្នែកនីមួយៗដែលបានបោះពុម្ពត្រូវបានបន្ថែមទៅក្នុង **Print History** និងប្រអប់ **Printed today**។"));
            w.Bullet(T("The **RFID** tab opens so you can save the card numbers (see **RFID cards**).",
                       "ផ្ទាំង **RFID** បើកឡើង ដើម្បីឱ្យអ្នករក្សាទុកលេខកាត (មើល **កាត RFID**)។"));
            w.Note(T("If the selected printer is not a card printer, the app asks before printing to it.",
                     "ប្រសិនបើម៉ាស៊ីនបោះពុម្ពដែលបានជ្រើសមិនមែនជាម៉ាស៊ីនបោះពុម្ពកាត កម្មវិធីនឹងសួរមុនពេលបោះពុម្ព។"));
        }

        private void WriteRfid(GuideWriter w)
        {
            w.Title(T("RFID cards", "កាត RFID"));
            w.Lead(T("Each printed card has an RFID chip. Card Studio saves the chip number (UID) next to the student's ID.",
                     "កាតនីមួយៗដែលបានបោះពុម្ពមានបន្ទះឈីប RFID។ Card Studio រក្សាទុកលេខឈីប (UID) ជាមួយអត្តលេខនិស្សិត។"));

            w.Heading(T("Save a card number", "រក្សាទុកលេខកាត"));
            w.Step(1, T("Open **Print setup → RFID**. It opens by itself after printing.",
                        "បើក **Print setup → RFID**។ វាបើកដោយស្វ័យប្រវត្តិក្រោយការបោះពុម្ព។"));
            w.Step(2, T("Select the student whose card you are holding. The tab shows whether their card was printed.",
                        "ជ្រើសរើសនិស្សិតដែលជាម្ចាស់កាតក្នុងដៃអ្នក។ ផ្ទាំងនេះបង្ហាញថាកាតរបស់គេបានបោះពុម្ពហើយឬនៅ។"));
            w.Step(3, T("Tap the card on the USB reader. The reader types the number and presses Enter, which saves it. " +
                        "You can also type the number and click **Upload**.",
                        "ប៉ះកាតលើឧបករណ៍អាន USB។ ឧបករណ៍អានវាយលេខកាត ហើយចុច Enter ដែលរក្សាទុកវាភ្លាម។ " +
                        "អ្នកក៏អាចវាយលេខដោយដៃ ហើយចុច **Upload** ផងដែរ។"));

            w.Heading(T("Safety checks", "ការការពារកំហុស"));
            w.Bullet(T("A card already saved for **another student** is refused, and nothing changes.",
                       "កាតដែលបានរក្សាទុកសម្រាប់ **និស្សិតផ្សេង** រួចហើយ នឹងត្រូវបដិសេធ ហើយគ្មានអ្វីផ្លាស់ប្តូរទេ។"));
            w.Bullet(T("If the student **already has a card**, the app asks before replacing it (for example after a reprint).",
                       "ប្រសិនបើនិស្សិត **មានកាតរួចហើយ** កម្មវិធីនឹងសួរមុនពេលជំនួស (ឧទាហរណ៍ ក្រោយពេលបោះពុម្ពឡើងវិញ)។"));
            w.Bullet(T("If the student has **no printed card** in the history, the app asks you to confirm.",
                       "ប្រសិនបើនិស្សិត **មិនមានកាតដែលបានបោះពុម្ព** ក្នុងប្រវត្តិ កម្មវិធីនឹងសួរឱ្យអ្នកបញ្ជាក់។"));
            w.Bullet(T("Changing the selected student clears the box, so a number is never saved for the wrong student.",
                       "ការប្តូរនិស្សិតដែលបានជ្រើស នឹងសម្អាតប្រអប់លេខ ដូច្នេះលេខកាតមិនដែលត្រូវរក្សាទុកសម្រាប់និស្សិតខុសឡើយ។"));

            w.Heading(T("The RFID file", "ឯកសារ RFID"));
            w.Bullet(T("By default the numbers are saved to **rfid-cards.csv** in the app data folder.",
                       "តាមលំនាំដើម លេខកាតត្រូវបានរក្សាទុកក្នុង **rfid-cards.csv** នៅក្នុងថតទិន្នន័យកម្មវិធី។"));
            w.Bullet(T("**Change…** moves it, for example to a shared network folder used by several PCs. An existing RFID file is " +
                       "used as it is; otherwise the current one is copied to the new place.",
                       "**Change…** ផ្លាស់ទីឯកសារនេះ ឧទាហរណ៍ទៅថតចែករំលែកលើបណ្តាញ ដែលកុំព្យូទ័រច្រើនគ្រឿងប្រើរួមគ្នា។ " +
                       "ប្រសិនបើមានឯកសារ RFID នៅទីនោះរួចហើយ វានឹងត្រូវប្រើដដែល បើមិនដូច្នោះទេ ឯកសារបច្ចុប្បន្ននឹងត្រូវចម្លងទៅទីតាំងថ្មី។"));
            w.Bullet(T("Sidebar **RFID Cards** (Ctrl+Shift+R) lists every saved number and the loaded students who have no card yet.",
                       "របារចំហៀង **RFID Cards** (Ctrl+Shift+R) បង្ហាញលេខកាតទាំងអស់ដែលបានរក្សាទុក និងនិស្សិតដែលមិនទាន់មានកាត។"));
        }

        private void WriteHistory(GuideWriter w)
        {
            w.Title(T("History & gallery", "ប្រវត្តិ និងវិចិត្រសាល"));

            w.Heading("Print History (Ctrl+H)");
            w.Para(T("Every card side printed: student, side, slot, printer and time. Double-click a row to select that student " +
                     "in the main list. You can delete wrong records here.",
                     "ផ្នែកកាតនីមួយៗដែលបានបោះពុម្ព៖ និស្សិត ផ្នែក រន្ធ ម៉ាស៊ីនបោះពុម្ព និងពេលវេលា។ " +
                     "ចុចពីរដងលើជួរ ដើម្បីជ្រើសរើសនិស្សិតនោះក្នុងបញ្ជីមេ។ អ្នកអាចលុបកំណត់ត្រាដែលខុសនៅទីនេះ។"));

            w.Heading("Gallery (Ctrl+G)");
            w.Para(T("Every loaded student with their photo, details and print status. Missing photos stand out. Choose a student " +
                     "to select them in the main list.",
                     "និស្សិតទាំងអស់ដែលបាននាំចូល ជាមួយរូបថត ព័ត៌មាន និងស្ថានភាពបោះពុម្ព។ រូបថតដែលខ្វះត្រូវបានបង្ហាញឱ្យឃើញច្បាស់។ " +
                     "ជ្រើសរើសនិស្សិត ដើម្បីជ្រើសគេក្នុងបញ្ជីមេ។"));

            w.Heading("Print Queue");
            w.Para(T("The Windows queue of the card printer. Cancel a job that is stuck before printing again.",
                     "ជួររង់ចាំបោះពុម្ពរបស់ Windows សម្រាប់ម៉ាស៊ីនបោះពុម្ពកាត។ បោះបង់ការងារដែលជាប់គាំង មុននឹងបោះពុម្ពម្តងទៀត។"));
        }

        private void WriteSettings(GuideWriter w)
        {
            w.Title(T("Settings & folders", "ការកំណត់ និងថតឯកសារ"));

            w.Heading(T("Where things are saved", "កន្លែងរក្សាទុកទិន្នន័យ"));
            w.Para(T("All settings are saved automatically for the current Windows user in:",
                     "ការកំណត់ទាំងអស់ត្រូវបានរក្សាទុកដោយស្វ័យប្រវត្តិ សម្រាប់អ្នកប្រើ Windows បច្ចុប្បន្ន នៅក្នុង៖"));
            w.Mono(DataFolder);
            w.Key("settings.xml", T("Printer, tray, folders, card text, API / SQL / Google / OneDrive connections.",
                                    "ម៉ាស៊ីនបោះពុម្ព ថាស ថតឯកសារ អត្ថបទកាត ការភ្ជាប់ API / SQL / Google / OneDrive។"));
            w.Key("print-history.csv", T("Every card side printed.", "ផ្នែកកាតនីមួយៗដែលបានបោះពុម្ព។"));
            w.Key("rfid-cards.csv", T("Saved RFID numbers (unless you moved the file with **Change…**).",
                                      "លេខកាត RFID ដែលបានរក្សាទុក (លើកលែងតែអ្នកបានផ្លាស់ទីវាដោយ **Change…**)។"));

            w.Heading("All Settings… (Ctrl+,)");
            w.Bullet(T("A read-only list of every setting of the app and this PC. Problems are shown in **orange**.",
                       "បញ្ជីការកំណត់ទាំងអស់របស់កម្មវិធី និងកុំព្យូទ័រនេះ (មើលបានតែប៉ុណ្ណោះ)។ បញ្ហាត្រូវបានបង្ហាញជា **ពណ៌ទឹកក្រូច**។"));
            w.Bullet(T("Double-click a file or folder row to open it in Explorer.",
                       "ចុចពីរដងលើជួរឯកសារ ឬថត ដើម្បីបើកវាក្នុង Explorer។"));
            w.Bullet(T("**Export…** saves the settings to a file, to back them up or set up another PC. Tokens, keys and " +
                       "passwords are not exported.",
                       "**Export…** រក្សាទុកការកំណត់ទៅក្នុងឯកសារ ដើម្បីបម្រុងទុក ឬរៀបចំកុំព្យូទ័រផ្សេងទៀត។ " +
                       "Token, key និងពាក្យសម្ងាត់ មិនត្រូវបាន export ទេ។"));
            w.Bullet(T("**Import…** replaces all settings with an exported file. The current settings are backed up first " +
                       "(settings.backup.xml).",
                       "**Import…** ជំនួសការកំណត់ទាំងអស់ដោយឯកសារដែលបាន export។ ការកំណត់បច្ចុប្បន្នត្រូវបានបម្រុងទុកជាមុន (settings.backup.xml)។"));

            w.Heading(T("Other", "ផ្សេងៗ"));
            w.Bullet(T("**Settings & Help…** — one page with **Dark Mode / Light Mode**, **Theme Color…**, **Folders…**, **All Settings…**, " +
                       "**System Info**, **Check for Updates** and **Help & About**. " +
                       "Their shortcuts (Ctrl+Shift+F, Ctrl+,, Ctrl+Shift+I, Ctrl+U, F1) still open each one directly.",
                       "**Settings & Help…** — ទំព័រតែមួយដែលមាន **Dark Mode / Light Mode** **Theme Color…** **Folders…** **All Settings…** " +
                       "**System Info** **Check for Updates** និង **Help & About**។ " +
                       "ផ្លូវកាត់ (Ctrl+Shift+F, Ctrl+,, Ctrl+Shift+I, Ctrl+U, F1) នៅតែបើកនីមួយៗដោយផ្ទាល់។"));
            w.Bullet(T("**Check for Updates** (Ctrl+U) — looks for a newer version in Card Studio's GitHub releases (needs the " +
                       "internet) and installs it. Card Studio closes, Windows asks for " +
                       "administrator approval, and your settings, print history, RFID file and card images are kept. " +
                       "It also checks by itself once a day at start-up; untick the option on that page to stop this.",
                       "**Check for Updates** (Ctrl+U) — ស្វែងរកកំណែថ្មីនៅក្នុង GitHub releases របស់ Card Studio (ត្រូវការអ៊ីនធឺណិត) " +
                       "ហើយដំឡើងវា។ Card Studio នឹងបិទ Windows សុំការអនុញ្ញាតពីអ្នកគ្រប់គ្រង " +
                       "(administrator) ហើយការកំណត់ ប្រវត្តិបោះពុម្ព ឯកសារ RFID និងរូបភាពកាតរបស់អ្នកត្រូវបានរក្សាទុក។ " +
                       "កម្មវិធីក៏ពិនិត្យដោយខ្លួនឯងម្តងក្នុងមួយថ្ងៃពេលបើកដែរ — ដោះធីកជម្រើសនៅលើទំព័រនោះ ដើម្បីបញ្ឈប់។"));
            w.Bullet(T("**Folders…** (Ctrl+Shift+F) — every folder the app uses: open, change or create them.",
                       "**Folders…** (Ctrl+Shift+F) — ថតទាំងអស់ដែលកម្មវិធីប្រើ៖ បើក ប្តូរ ឬបង្កើតថត។"));
            w.Bullet(T("**Dark Mode / Light Mode** — changes the app's colours and restarts it. Printed cards are not affected.",
                       "**Dark Mode / Light Mode** — ប្តូរពណ៌កម្មវិធី ហើយចាប់ផ្តើមកម្មវិធីឡើងវិញ។ កាតដែលបោះពុម្ពមិនរងផលប៉ះពាល់ទេ។"));
            w.Bullet(T("**Theme Color…** — choose the main colour of buttons, tabs and highlights (10 colours or any custom colour), " +
                       "with a live preview. The app restarts to apply it. Printed cards are not affected.",
                       "**Theme Color…** — ជ្រើសរើសពណ៌ចម្បងរបស់ប៊ូតុង ផ្ទាំង និងការរំលេច (ពណ៌ ១០ ឬពណ៌ផ្ទាល់ខ្លួន) " +
                       "ដោយមានការមើលជាមុនភ្លាមៗ។ កម្មវិធីនឹងចាប់ផ្តើមឡើងវិញដើម្បីអនុវត្ត។ កាតដែលបោះពុម្ពមិនរងផលប៉ះពាល់ទេ។"));
            w.Bullet(T("**Card images** (logos, stamp, QR and social icons) live in the **Assets** folder next to the app.",
                       "**រូបភាពកាត** (ឡូហ្គោ ត្រា QR និងរូបតំណាងបណ្តាញសង្គម) ស្ថិតនៅក្នុងថត **Assets** ជាប់នឹងកម្មវិធី។"));
            w.Bullet(T("**This guide** is available in English and Khmer — use the switch at the top. The choice is remembered.",
                       "**ការណែនាំនេះ** មានជាភាសាអង់គ្លេស និងភាសាខ្មែរ — ប្រើប៊ូតុងប្តូរភាសានៅខាងលើ។ ជម្រើសរបស់អ្នកត្រូវបានចងចាំ។"));
        }

        private void WriteShortcuts(GuideWriter w)
        {
            w.Title(T("Keyboard shortcuts", "គ្រាប់ចុចកាត់"));

            w.Heading(T("Main window", "ផ្ទាំងមេ"));
            w.Key("F1", T("This guide", "ការណែនាំនេះ"));
            w.Key("Ctrl+F", T("Search the student list", "ស្វែងរកក្នុងបញ្ជីនិស្សិត"));
            w.Key("Ctrl+P", T("Print the selected students", "បោះពុម្ពនិស្សិតដែលបានជ្រើស"));
            w.Key("F11", T("Full-screen card preview", "មើលកាតជាមុនពេញអេក្រង់"));
            w.Key("Ctrl+L", T("Import Students page", "ទំព័រ Import Students"));
            w.Key("Ctrl+E / Ctrl+O", T("Import from Excel / CSV", "នាំចូលពី Excel / CSV"));
            w.Key("Ctrl+I / Ctrl+D", T("Import from the web API / SQL Server", "នាំចូលពី Web API / SQL Server"));
            w.Key("Ctrl+Shift+G", T("Import from Google Sheets", "នាំចូលពី Google Sheets"));
            w.Key("Ctrl+Shift+C", T("CSV Files", "ឯកសារ CSV"));
            w.Key("Ctrl+Shift+D", T("Download photos from Google Drive", "ទាញយករូបថតពី Google Drive"));
            w.Key("Ctrl+Shift+O", T("Download photos from OneDrive", "ទាញយករូបថតពី OneDrive"));
            w.Key("Ctrl+B", T("Remove the background of the selected photo", "លុបផ្ទៃខាងក្រោយនៃរូបថតដែលបានជ្រើស"));
            w.Key("Ctrl+G", T("Gallery", "វិចិត្រសាល (Gallery)"));
            w.Key("Ctrl+H", T("Print History", "ប្រវត្តិបោះពុម្ព"));
            w.Key("Ctrl+Shift+R", T("RFID Cards", "កាត RFID"));
            w.Key("Ctrl+Shift+E", "Epson Driver Setup");
            w.Key("Ctrl+Shift+F", T("Folders", "ថតឯកសារ (Folders)"));
            w.Key("Ctrl+,", T("All Settings", "ការកំណត់ទាំងអស់"));
            w.Key("Ctrl+Shift+I", T("System info", "ព័ត៌មានប្រព័ន្ធ"));
            w.Key("Ctrl+U", T("Check for updates", "ពិនិត្យរកបច្ចុប្បន្នភាព"));

            w.Heading(T("Lists and boxes", "បញ្ជី និងប្រអប់"));
            w.Key("Esc", T("In the search box: clear the search", "ក្នុងប្រអប់ស្វែងរក៖ សម្អាតការស្វែងរក"));
            w.Key(T("Down arrow", "ព្រួញចុះ"), T("From the search box into the list", "ពីប្រអប់ស្វែងរក ទៅក្នុងបញ្ជី"));
            w.Key(T("Double-click a row", "ចុចពីរដងលើជួរ"), T("Full-screen preview of that student", "មើលនិស្សិតនោះជាមុនពេញអេក្រង់"));
            w.Key("Enter", T("In the RFID box: save the card number", "ក្នុងប្រអប់ RFID៖ រក្សាទុកលេខកាត"));

            w.Heading(T("Messages and this guide", "សារ និងការណែនាំនេះ"));
            w.Key("Ctrl+C", T("In any message: copy its text (useful for error messages)",
                              "ក្នុងសារណាមួយ៖ ចម្លងអត្ថបទ (មានប្រយោជន៍សម្រាប់សារកំហុស)"));
            w.Key("Enter / F3", T("In this guide: next search match", "ក្នុងការណែនាំនេះ៖ លទ្ធផលស្វែងរកបន្ទាប់"));
            w.Key("Ctrl+PgUp / PgDn", T("In this guide: previous / next topic", "ក្នុងការណែនាំនេះ៖ ប្រធានបទមុន / បន្ទាប់"));
        }

        private void WriteTroubleshooting(GuideWriter w)
        {
            w.Title(T("Troubleshooting", "ដោះស្រាយបញ្ហា"));

            Problem(w, T("“No card printer found”", "“No card printer found” (រកមិនឃើញម៉ាស៊ីនបោះពុម្ពកាត)"),
                T("Install the Epson L8050 driver, or tick **Show all** in **Print setup → Printer** and choose the printer.",
                  "ដំឡើង driver Epson L8050 ឬធីក **Show all** នៅ **Print setup → Printer** ហើយជ្រើសរើសម៉ាស៊ីនបោះពុម្ព។"));
            Problem(w, T("The card prints in the wrong place", "កាតបោះពុម្ពខុសទីតាំង"),
                T("Run **Test Print** and adjust Slot X / Y in the **Tray** tab (see **Printer & tray setup**).",
                  "ចុច **Test Print** ហើយកែ Slot X / Y នៅផ្ទាំង **Tray** (មើល **ម៉ាស៊ីនបោះពុម្ព និងថាស**)។"));
            Problem(w, T("The card is upside down or sideways", "កាតចេញមកបញ្ច្រាស ឬទិសខុស"),
                T("Change **Card on tray** (try the other Vertical option) or tick **Rotate 180°** for that side.",
                  "ប្តូរ **Card on tray** (សាកជម្រើសបញ្ឈរមួយទៀត) ឬធីក **Rotate 180°** សម្រាប់ផ្នែកនោះ។"));
            Problem(w, T("Nothing comes out / the job is stuck", "គ្មានអ្វីចេញមក / ការងារជាប់គាំង"),
                T("Click **Check Printer**, then **Print Queue…** and cancel the stuck job. Check the printer is on and has no error light.",
                  "ចុច **Check Printer** បន្ទាប់មក **Print Queue…** ហើយបោះបង់ការងារដែលជាប់គាំង។ ពិនិត្យថាម៉ាស៊ីនបោះពុម្ពបើក ហើយគ្មានភ្លើងកំហុស។"));
            Problem(w, T("A photo is missing on the card", "រូបថតមិនបង្ហាញលើកាត"),
                T("The file name must be exactly the student ID (e.g. NPIC24001.jpg) and be in the photo folder. " +
                  "**Gallery** shows who has no photo.",
                  "ឈ្មោះឯកសារត្រូវតែដូចអត្តលេខនិស្សិតទាំងស្រុង (ឧ. NPIC24001.jpg) ហើយស្ថិតក្នុងថតរូបថត។ " +
                  "**Gallery** បង្ហាញថាអ្នកណាគ្មានរូបថត។"));
            Problem(w, T("Khmer text looks wrong or uses another font", "អក្សរខ្មែរមើលទៅខុស ឬប្រើពុម្ពអក្សរផ្សេង"),
                T("A card font is missing. Check **System info**, install the font for all users, then restart the app.",
                  "ខ្វះពុម្ពអក្សរកាត។ ពិនិត្យ **ព័ត៌មានប្រព័ន្ធ** ដំឡើងពុម្ពអក្សរសម្រាប់អ្នកប្រើទាំងអស់ រួចបើកកម្មវិធីឡើងវិញ។"));
            Problem(w, T("“Missing in …\\Assets: …” in the status line", "“Missing in …\\Assets: …” នៅបន្ទាត់ស្ថានភាព"),
                T("Card images are missing next to the app. Reinstall Card Studio or copy the files back into the Assets folder.",
                  "រូបភាពកាតបាត់ពីថតកម្មវិធី។ ដំឡើង Card Studio ឡើងវិញ ឬចម្លងឯកសារត្រលប់ទៅថត Assets វិញ។"));
            Problem(w, "“RFID card … is already saved for student …”",
                T("That card belongs to another student. Check that you tapped the right card for the selected student.",
                  "កាតនោះជារបស់និស្សិតផ្សេង។ ពិនិត្យថាអ្នកបានប៉ះកាតត្រឹមត្រូវសម្រាប់និស្សិតដែលបានជ្រើស។"));
            Problem(w, "“The print history could not be saved”",
                T("The cards were printed, but the history file could not be written (disk full or no permission). " +
                  "Check the app data folder.",
                  "កាតបានបោះពុម្ពរួចហើយ ប៉ុន្តែមិនអាចសរសេរឯកសារប្រវត្តិបានទេ (ទំហំផ្ទុកពេញ ឬគ្មានសិទ្ធិ)។ ពិនិត្យថតទិន្នន័យកម្មវិធី។"));
            Problem(w, T("The students are gone after a restart", "និស្សិតបាត់ក្រោយពេលបើកកម្មវិធីឡើងវិញ"),
                T("Only lists opened from a file come back by themselves. Import from the API, SQL Server or Google Sheets again.",
                  "មានតែបញ្ជីដែលបើកពីឯកសារប៉ុណ្ណោះ ដែលត្រលប់មកវិញដោយស្វ័យប្រវត្តិ។ សូមនាំចូលពី API, SQL Server ឬ Google Sheets ម្តងទៀត។"));

            w.Note(T("Asking for help? Press Ctrl+C on the error message to copy it, and click **Copy System Info** below.",
                     "ត្រូវការជំនួយ? ចុច Ctrl+C លើសារកំហុស ដើម្បីចម្លងវា ហើយចុចប៊ូតុង **ចម្លងព័ត៌មានប្រព័ន្ធ** ខាងក្រោម។"));
        }

        private static void Problem(GuideWriter w, string problem, string fix)
        {
            w.Subheading(problem);
            w.Para(fix);
        }

        private void WriteSystemInfo(GuideWriter w)
        {
            w.Title(T("System info", "ព័ត៌មានប្រព័ន្ធ"));
            w.Lead(T("Live details of this installation. Open it any time with **Settings & Help…** → **System Info** in the sidebar (Ctrl+Shift+I). " +
                     "**Copy System Info** puts all of it on the clipboard.",
                     "ព័ត៌មានផ្ទាល់នៃការដំឡើងនេះ។ បើកវាបានគ្រប់ពេលតាម **Settings & Help…** → **System Info** នៅរបារចំហៀង (Ctrl+Shift+I)។ " +
                     "ប៊ូតុង **ចម្លងព័ត៌មានប្រព័ន្ធ** ចម្លងទាំងអស់ទៅ clipboard។"));

            w.Heading(T("Application", "កម្មវិធី"));
            w.Row(T("Product", "ផលិតផល"), T("Card Studio — ID Card Printer", "Card Studio — កម្មវិធីបោះពុម្ពកាតសម្គាល់ខ្លួន"));
            bool versionWarn;
            string version = VersionText(out versionWarn);
            w.Row(T("Version", "កំណែ"), version, versionWarn);
            w.Row(T("App folder", "ថតកម្មវិធី"), AppDomain.CurrentDomain.BaseDirectory);
            w.Row("Windows", RuntimeInformation.OSDescription.Trim() + (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)"));
            w.Row(".NET", RuntimeInformation.FrameworkDescription.Trim());
            w.Row(T("Computer / user", "កុំព្យូទ័រ / អ្នកប្រើ"), Environment.MachineName + " / " + Environment.UserName);

            w.Heading(T("Updates", "បច្ចុប្បន្នភាព"));
            w.Row(T("Updates from", "បច្ចុប្បន្នភាពពី"), UpdateService.ReleasesPage);
            string available = _settings.AvailableUpdateVersion;
            if (UpdateService.IsNewerThanRunning(available))
                w.Row(T("New version", "កំណែថ្មី"), T(available + " is available — Check for Updates (Ctrl+U)",
                                                       "កំណែ " + available + " អាចដំឡើងបាន — Check for Updates (Ctrl+U)"), true);
            w.Row(T("Check at start-up", "ពិនិត្យពេលបើកកម្មវិធី"), _settings.CheckUpdatesOnStartup
                ? T("On (at most once a day)", "បើក (យ៉ាងច្រើនម្តងក្នុងមួយថ្ងៃ)")
                : T("Off", "បិទ"));
            w.Row(T("Last checked", "ពិនិត្យចុងក្រោយ"), _settings.LastUpdateCheck == DateTime.MinValue
                ? T("Never", "មិនដែល")
                : _settings.LastUpdateCheck.ToString("yyyy-MM-dd HH:mm"));
            if (!string.IsNullOrEmpty(_settings.SkippedUpdateVersion))
                w.Row(T("Skipped version", "កំណែដែលបានរំលង"), _settings.SkippedUpdateVersion);

            w.Heading(T("Activity", "សកម្មភាព"));
            if (_studentCount >= 0)
                w.Row(T("Students loaded", "និស្សិតបាននាំចូល"), _studentCount == 0
                    ? T("None — Import Students… (Ctrl+L)", "គ្មាន — Import Students… (Ctrl+L)")
                    : _studentCount.ToString("N0") + (_studentSource.Length == 0 ? "" : T("  ·  from ", "  ·  ពី ") + _studentSource));
            w.Row(T("Cards printed", "កាតបានបោះពុម្ព"), _historyError != null
                ? T("Print history unreadable: ", "មិនអាចអានប្រវត្តិបោះពុម្ព៖ ") + _historyError
                : T(_historyTotal.ToString("N0") + " card sides in total  ·  " + _historyToday.ToString("N0") + " today",
                    "សរុប " + _historyTotal.ToString("N0") + " ផ្នែកកាត  ·  ថ្ងៃនេះ " + _historyToday.ToString("N0")),
                _historyError != null);
            w.Row(T("RFID cards saved", "កាត RFID បានរក្សាទុក"), _rfidError != null
                ? T("RFID file unreadable: ", "មិនអាចអានឯកសារ RFID៖ ") + _rfidError
                : _rfidCount.ToString("N0"),
                _rfidError != null);

            w.Heading(T("Data files", "ឯកសារទិន្នន័យ"));
            w.Row(T("Settings", "ការកំណត់"), AppSettings.FilePath, !File.Exists(AppSettings.FilePath),
                T("not saved yet", "មិនទាន់រក្សាទុក"));
            w.Row(T("Print history", "ប្រវត្តិបោះពុម្ព"), PrintHistory.FilePath, !File.Exists(PrintHistory.FilePath),
                T("nothing printed yet", "មិនទាន់បោះពុម្ពអ្វីទេ"));
            string rfid = RfidCardWriter.FilePath(_settings);
            w.Row(T("RFID file", "ឯកសារ RFID"), rfid, !File.Exists(rfid), T("no card saved yet", "មិនទាន់មានកាតរក្សាទុក"));

            w.Heading(T("Printing", "ការបោះពុម្ព"));
            string printer = _settings.PrinterName;
            bool hasPrinter = !string.IsNullOrEmpty(printer);
            bool installed = hasPrinter && PrinterSettings.InstalledPrinters.Cast<string>()
                .Any(p => string.Equals(p, printer, StringComparison.OrdinalIgnoreCase));
            w.Row(T("Card printer", "ម៉ាស៊ីនបោះពុម្ពកាត"), hasPrinter ? printer : T("(none selected)", "(មិនទាន់ជ្រើសរើស)"),
                !hasPrinter || !installed,
                hasPrinter && !installed ? T("not installed on this PC", "មិនបានដំឡើងលើកុំព្យូទ័រនេះ") : null);
            w.Row(T("Driver settings", "ការកំណត់ driver"), string.IsNullOrEmpty(_settings.DevModeBase64)
                ? T("Driver defaults (Driver Setup… not used yet)", "លំនាំដើមរបស់ driver (មិនទាន់ប្រើ Driver Setup…)")
                : T("Saved from Driver Setup…", "បានរក្សាទុកពី Driver Setup…"));
            bool hasPhotos = !string.IsNullOrEmpty(_settings.PhotoFolder);
            bool photosFound = hasPhotos && Directory.Exists(_settings.PhotoFolder);
            w.Row(T("Photo folder", "ថតរូបថត"), hasPhotos ? _settings.PhotoFolder : T("(not set)", "(មិនទាន់កំណត់)"), !photosFound,
                hasPhotos && !photosFound ? T("not found", "រកមិនឃើញ") : null);

            w.Heading(T("Card images", "រូបភាពកាត"));
            string[] missing = AssetStore.MissingFiles();
            w.Row(T("Assets folder", "ថត Assets"), AssetStore.Folder);
            w.Row(T("Status", "ស្ថានភាព"), missing.Length == 0
                ? T("All " + AssetStore.RequiredFiles.Length + " images found", "រកឃើញរូបភាពទាំង " + AssetStore.RequiredFiles.Length)
                : T("Missing: ", "ខ្វះ៖ ") + string.Join(", ", missing), missing.Length > 0);

            w.Heading(T("Card fonts", "ពុម្ពអក្សរកាត"));
            foreach (string family in CardFonts.Required)
            {
                bool ok = CardFonts.IsInstalled(family);
                w.Row(family, ok ? T("Installed", "បានដំឡើង") : T("Missing — a fallback font is used", "ខ្វះ — ប្រើពុម្ពអក្សរជំនួស"), !ok);
            }
        }

        // ===================== Win32 =====================
        private const int WM_SETREDRAW = 0x000B;
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessage")]
        private static extern IntPtr SendMessageText(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void SetCueBanner(TextBox box, string text)
        {
            SendMessageText(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }
    }

    /// <summary>
    /// <see cref="RichTextBox"/> on the RichEdit 4.1 control (msftedit.dll, part of Windows since XP SP1) instead of the
    /// old RichEdit 2.0 that WinForms uses: RichEdit 2.0 cannot shape Khmer (split words, misplaced subscript consonants).
    /// Falls back to the normal control if msftedit.dll cannot be loaded.
    /// </summary>
    internal sealed class ModernRichTextBox : RichTextBox
    {
        private static bool _tried, _available;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (!_tried)
                {
                    _tried = true;
                    try { _available = LoadLibrary("msftedit.dll") != IntPtr.Zero; }
                    catch (Exception) { _available = false; }
                }
                if (_available) cp.ClassName = "RICHEDIT50W";
                return cp;
            }
        }
    }

    /// <summary>
    /// Writes one guide topic, either formatted into a <see cref="RichTextBox"/> or, when the box is null, as plain text
    /// (for searching and copying). Text between ** and ** is bold. Khmer pages use the Khmer card fonts
    /// (Khmer OS Siemreap for text, Khmer OS Muol Light for titles), with the usual fallbacks when they are missing.
    /// </summary>
    internal sealed class GuideWriter
    {
        /// <summary>The fonts of one language.</summary>
        private sealed class FontSet
        {
            public Font Title, Lead, Heading, Sub, Body, Bold, Key, Mono, Gap, TinyGap;
        }

        private static FontSet _english, _khmer;

        private static FontSet English
        {
            get
            {
                return _english ?? (_english = new FontSet
                {
                    Title = new Font("Segoe UI Semibold", 17f),
                    Lead = new Font(Theme.Family, 10.5f),
                    Heading = new Font("Segoe UI Semibold", 12.5f),
                    Sub = new Font("Segoe UI Semibold", 10.5f),
                    Body = new Font(Theme.Family, 10f),
                    Bold = new Font("Segoe UI Semibold", 10f),
                    Key = new Font("Segoe UI Semibold", 9.75f),
                    Mono = new Font("Consolas", 9.75f),
                    Gap = new Font(Theme.Family, 5f),
                    TinyGap = new Font(Theme.Family, 2f)
                });
            }
        }

        private static FontSet Khmer
        {
            get
            {
                if (_khmer != null) return _khmer;
                string text = CardFonts.KhmerSiemreap, title = CardFonts.MuolLight;
                return _khmer = new FontSet
                {
                    Title = new Font(title, 14f),
                    Lead = new Font(text, 10.5f),
                    Heading = new Font(title, 11f),
                    Sub = new Font(text, 10.5f, FontStyle.Bold),
                    Body = new Font(text, 10f),
                    Bold = new Font(text, 10f, FontStyle.Bold),
                    Key = new Font(text, 9.75f, FontStyle.Bold),
                    Mono = new Font("Consolas", 9.75f),
                    Gap = new Font(Theme.Family, 4f),
                    TinyGap = new Font(Theme.Family, 1.5f)
                };
            }
        }

        private readonly RichTextBox _box;
        private readonly float _scale;
        private readonly FontSet _f;
        private readonly StringBuilder _plain = new StringBuilder();
        private bool _started;

        public GuideWriter(RichTextBox box, float scale, bool khmer)
        {
            _box = box;
            _scale = scale;
            _f = khmer ? Khmer : English;
        }

        public string PlainText { get { return _plain.ToString(); } }

        private int Px(int value) { return (int)Math.Round(value * _scale); }

        // ---------- Blocks ----------
        public void Title(string text)
        {
            Line(text, _f.Title, Theme.TextMain, 0, 0);
            Gap();
        }

        public void Lead(string text)
        {
            Rich(text, _f.Lead, _f.Lead, Theme.TextSoft, 0, 0, null);
            Gap();
        }

        public void Heading(string text)
        {
            if (_started) Gap();
            Line(text, _f.Heading, Theme.Accent, 0, 0);
            Gap();
        }

        public void Subheading(string text)
        {
            Gap();
            Line(text, _f.Sub, Theme.TextMain, 0, 0);
        }

        public void Para(string text)
        {
            Rich(text, _f.Body, _f.Bold, Theme.TextMain, 0, 0, null);
            Gap();
        }

        public void Bullet(string text)
        {
            Rich(text, _f.Body, _f.Bold, Theme.TextMain, Px(6), Px(18), "•\t");
            Gap();
        }

        public void Step(int number, string text)
        {
            Rich(text, _f.Body, _f.Bold, Theme.TextMain, Px(6), Px(26), number + ".\t");
            Gap();
        }

        /// <summary>Two columns: a key / name in bold on the left, its description on the right.</summary>
        public void Key(string key, string description)
        {
            Columns(key, _f.Key, Theme.Accent, description, Theme.TextMain, Px(160));
        }

        /// <summary>Two columns: setting name (muted) and value; a warning value is orange with an optional note.</summary>
        public void Row(string name, string value, bool warn = false, string note = null)
        {
            string shown = string.IsNullOrEmpty(value) ? "–" : value;
            if (warn && !string.IsNullOrEmpty(note)) shown += "   (" + note + ")";
            Columns(name, _f.Body, Theme.TextSoft, shown, warn ? Theme.Warning : Theme.TextMain, Px(170));
        }

        /// <summary>A file path or other literal text, in a monospaced font.</summary>
        public void Mono(string path)
        {
            Line(path, _f.Mono, Theme.TextMain, Px(6), 0);
            Gap();
        }

        public void Note(string text)
        {
            Gap();
            Rich(text, _f.Body, _f.Bold, Theme.Warning, Px(6), Px(22), "!\t");
        }

        /// <summary>A block of monospaced lines (code, command lines) kept together, with one gap after the block.</summary>
        public void Code(IEnumerable<string> lines)
        {
            foreach (string line in lines) Line(line.Length == 0 ? " " : line, _f.Mono, Theme.TextSoft, Px(12), 0);
            Gap();
        }

        /// <summary>Normal space between blocks, e.g. after a run of <see cref="Key"/> rows or a <see cref="Note"/>.</summary>
        public void Space()
        {
            Gap();
        }

        /// <summary>A horizontal divider line (Markdown "---").</summary>
        public void Rule()
        {
            Gap();
            Line(new string('─', 40), _f.Body, Theme.Border, 0, 0);
            Gap();
        }

        // ---------- Plumbing ----------
        private void Columns(string left, Font leftFont, Color leftColor, string right, Color rightColor, int column)
        {
            _started = true;
            _plain.Append(left).Append(": ").AppendLine(StripBold(right));
            if (_box == null) return;

            int indent = Px(6);
            Paragraph(indent, column - indent, new[] { column });
            Append(left + "\t", leftFont, leftColor);
            AppendRich(right, _f.Body, _f.Bold, rightColor);
            Append("\n", _f.Body, rightColor);
            Gap(true);
        }

        private void Line(string text, Font font, Color color, int indent, int hanging)
        {
            _started = true;
            _plain.AppendLine(text);
            if (_box == null) return;
            Paragraph(indent, hanging, null);
            Append(text + "\n", font, color);
        }

        /// <summary>A paragraph with **bold** runs; <paramref name="marker"/> (bullet / number + tab) hangs in the margin.</summary>
        private void Rich(string text, Font body, Font bold, Color color, int indent, int hanging, string marker)
        {
            _started = true;
            _plain.Append(marker == null ? "" : marker.Replace("\t", " ")).AppendLine(StripBold(text));
            if (_box == null) return;

            Paragraph(indent, hanging, hanging > 0 ? new[] { indent + hanging } : null);
            if (marker != null) Append(marker, bold, marker == "•\t" ? Theme.Accent : color);
            AppendRich(text, body, bold, color);
            Append("\n", body, color);
        }

        private void AppendRich(string text, Font body, Font bold, Color color)
        {
            string[] parts = text.Split(new[] { "**" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0) Append(parts[i], i % 2 == 1 ? bold : body, color);
        }

        /// <summary>Small vertical space between blocks.</summary>
        private void Gap(bool tiny = false)
        {
            if (_box == null) return;
            Paragraph(0, 0, null);
            Append("\n", tiny ? _f.TinyGap : _f.Gap, Theme.TextMain);
        }

        private void Paragraph(int indent, int hanging, int[] tabs)
        {
            _box.SelectionStart = _box.TextLength;
            _box.SelectionLength = 0;
            _box.SelectionIndent = indent;
            _box.SelectionHangingIndent = hanging;
            _box.SelectionTabs = tabs ?? new int[0];
        }

        private void Append(string text, Font font, Color color)
        {
            _box.SelectionStart = _box.TextLength;
            _box.SelectionLength = 0;
            _box.SelectionFont = font;
            _box.SelectionColor = color;
            _box.SelectedText = text;
        }

        private static string StripBold(string text)
        {
            return (text ?? "").Replace("**", "");
        }
    }
}
