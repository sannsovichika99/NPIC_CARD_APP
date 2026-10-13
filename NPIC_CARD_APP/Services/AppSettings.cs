using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using NPIC_CARD_APP.Rendering;

namespace NPIC_CARD_APP.Services
{
    /// <summary>How many tray slots one print job (tray load) uses.</summary>
    public enum SlotMode
    {
        /// <summary>2 cards per load when 2+ cards are printed and slot 2 is usable, otherwise slot 1 only.</summary>
        Auto,
        One,
        Two
    }

    /// <summary>How the card lies in the tray pocket, seen on the (portrait) page.</summary>
    public enum CardOrientation
    {
        /// <summary>Long side across the page: slot is 85.6 mm wide x 54 mm high.</summary>
        Horizontal,
        /// <summary>Long side down the page, card turned 90° clockwise (card top on the page's right).</summary>
        VerticalRight,
        /// <summary>Long side down the page, card turned 90° counter-clockwise (card top on the page's left).</summary>
        VerticalLeft
    }

    /// <summary>Saved to %AppData%\NPIC_CARD_APP\settings.xml</summary>
    public class AppSettings
    {
        public string PrinterName { get; set; }

        /// <summary>A printer whose name contains one of these words (separated by ;) is a card printer.</summary>
        public string CardPrinterKeywords { get; set; }
        public bool ShowAllPrinters { get; set; }    // false = list card printers only
        public string DevModeBase64 { get; set; }   // Epson driver settings (Paper Source / Paper Type)
        public string PhotoFolder { get; set; }
        public string SignaturePath { get; set; }   // optional PNG/JPG of the director's signature
        public string LastCsvPath { get; set; }

        /// <summary>Web API address for Import API… (remembered between runs).</summary>
        public string ApiUrl { get; set; }

        /// <summary>
        /// API access token, encrypted with Windows DPAPI so only this Windows user on this PC can read it.
        /// Use <see cref="ApiToken"/> to read or change it.
        /// </summary>
        public string ApiTokenProtected { get; set; }

        [XmlIgnore]
        public string ApiToken
        {
            get { return Unprotect(ApiTokenProtected, TokenEntropy); }
            set { ApiTokenProtected = Protect(value, TokenEntropy); }
        }

        private static readonly byte[] TokenEntropy = Encoding.UTF8.GetBytes("NPIC_CARD_APP.ApiToken");

        // ---- SQL Server connection (Connect SQL Server… page) ----
        /// <summary>Server name, e.g. "localhost\SQLEXPRESS" or "192.168.1.10,1433".</summary>
        public string SqlServer { get; set; }
        public string SqlDatabase { get; set; }
        /// <summary>True = Windows Authentication, false = SQL Server login (<see cref="SqlUser"/> + password).</summary>
        public bool SqlWindowsAuth { get; set; }
        public string SqlUser { get; set; }
        public bool SqlEncrypt { get; set; }
        public bool SqlTrustServerCertificate { get; set; }
        /// <summary>Table name (e.g. dbo.Students) or a SELECT query that returns the students.</summary>
        public string SqlQuery { get; set; }

        /// <summary>SQL login password, encrypted with Windows DPAPI like the API token. Use <see cref="SqlPassword"/>.</summary>
        public string SqlPasswordProtected { get; set; }

        [XmlIgnore]
        public string SqlPassword
        {
            get { return Unprotect(SqlPasswordProtected, SqlPasswordEntropy); }
            set { SqlPasswordProtected = Protect(value, SqlPasswordEntropy); }
        }

        private static readonly byte[] SqlPasswordEntropy = Encoding.UTF8.GetBytes("NPIC_CARD_APP.SqlPassword");

        private static string Protect(string value, byte[] entropy)
        {
            return string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(value), entropy, DataProtectionScope.CurrentUser));
        }

        private static string Unprotect(string stored, byte[] entropy)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(stored), entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                return "";   // saved by another Windows user / PC: ask again
            }
        }

        // Card position on the page, in millimetres (calibrate with Test Print)
        public float Slot1X { get; set; }
        public float Slot1Y { get; set; }
        public float Slot2X { get; set; }
        public float Slot2Y { get; set; }

        /// <summary>
        /// True when slot 2 is in use (Slot mode Auto or 2). Still saved so older versions of the app
        /// read the setting; the app itself uses <see cref="SlotMode"/>.
        /// </summary>
        public bool UseTwoSlots { get; set; }

        /// <summary>Saved form of <see cref="SlotMode"/>; empty in settings files from older versions.</summary>
        [XmlElement("SlotMode")]
        public string SlotModeText { get; set; }

        [XmlIgnore]
        public SlotMode SlotMode
        {
            get
            {
                SlotMode mode;
                if (!string.IsNullOrEmpty(SlotModeText) && Enum.TryParse(SlotModeText, out mode)) return mode;
                return UseTwoSlots ? SlotMode.Auto : SlotMode.One;   // settings from before Slot mode existed
            }
            set
            {
                SlotModeText = value.ToString();
                UseTwoSlots = value != SlotMode.One;
            }
        }
        public bool RotateBack180 { get; set; }
        public bool RotateFront180 { get; set; }

        /// <summary>Card direction in the tray pocket. The PVC tray holds the card upright (vertical).</summary>
        public CardOrientation CardOrientation { get; set; }

        [XmlIgnore]
        public bool CardVertical { get { return CardOrientation != CardOrientation.Horizontal; } }

        /// <summary>Size of one slot on the page in mm (card size, swapped when the card lies vertical).</summary>
        [XmlIgnore]
        public float SlotWidthMm { get { return CardVertical ? CardRenderer.CardHeightMm : CardRenderer.CardWidthMm; } }

        [XmlIgnore]
        public float SlotHeightMm { get { return CardVertical ? CardRenderer.CardWidthMm : CardRenderer.CardHeightMm; } }

        public CardText CardText { get; set; }

        public AppSettings()
        {
            PrinterName = "";
            CardPrinterKeywords = "L8050;L8058;L18050;Card;PVC";
            ShowAllPrinters = false;
            DevModeBase64 = "";
            PhotoFolder = "";
            SignaturePath = "";
            LastCsvPath = "";
            ApiUrl = "";
            ApiTokenProtected = "";
            SqlServer = "";
            SqlDatabase = "";
            SqlWindowsAuth = true;
            SqlUser = "";
            SqlPasswordProtected = "";
            SqlEncrypt = false;
            SqlTrustServerCertificate = true;
            SqlQuery = "";
            Slot1X = 0f;
            Slot1Y = 0f;
            Slot2X = 0f;
            Slot2Y = 60f;
            SlotModeText = "";
            CardOrientation = CardOrientation.VerticalRight;
            CardText = new CardText();
        }

        public bool IsCardPrinter(string printerName)
        {
            if (string.IsNullOrEmpty(printerName)) return false;
            foreach (string word in (CardPrinterKeywords ?? "").Split(';'))
            {
                string w = word.Trim();
                if (w.Length > 0 && printerName.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>%AppData%\NPIC_CARD_APP\settings.xml</summary>
        public static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NPIC_CARD_APP");
                return Path.Combine(dir, "settings.xml");
            }
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var serializer = new XmlSerializer(typeof(AppSettings));
                    using (FileStream fs = File.OpenRead(FilePath))
                    {
                        var settings = (AppSettings)serializer.Deserialize(fs);
                        if (settings.CardText == null) settings.CardText = new CardText();
                        return settings;
                    }
                }
            }
            catch
            {
                // Corrupt settings file: fall back to defaults.
            }
            return new AppSettings { SlotMode = SlotMode.Auto };   // first run
        }

        public void Save()
        {
            WriteTo(FilePath, this);
        }

        /// <summary>Copy of settings.xml made just before Import Settings… replaces it.</summary>
        public static string BackupFilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(FilePath), "settings.backup.xml"); }
        }

        /// <summary>
        /// Writes the settings to any file (Export Settings…). The API token and SQL password are left out: they are
        /// encrypted for this Windows user only, so they would be useless on another PC and should not travel with the file.
        /// </summary>
        public void ExportTo(string path)
        {
            var copy = (AppSettings)MemberwiseClone();
            copy.ApiTokenProtected = "";
            copy.SqlPasswordProtected = "";
            WriteTo(path, copy);
        }

        /// <summary>Reads a settings file for Import Settings…; unlike <see cref="Load"/> it fails loudly.</summary>
        /// <exception cref="InvalidDataException">The file is not an NPIC Card App settings file.</exception>
        public static AppSettings ReadFile(string path)
        {
            try
            {
                var serializer = new XmlSerializer(typeof(AppSettings));
                using (FileStream fs = File.OpenRead(path))
                {
                    var settings = (AppSettings)serializer.Deserialize(fs);
                    if (settings.CardText == null) settings.CardText = new CardText();
                    return settings;
                }
            }
            catch (InvalidOperationException ex)   // XmlSerializer: not XML, or not <AppSettings>
            {
                throw new InvalidDataException("This is not an NPIC Card App settings file:\n" + path, ex);
            }
        }

        /// <summary>
        /// Takes over every saved value of <paramref name="other"/>, keeping this object (the main window holds on to it).
        /// Copies the properties the XML file stores, so settings added later are copied too.
        /// </summary>
        public void CopyFrom(AppSettings other)
        {
            foreach (PropertyInfo p in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite || p.IsDefined(typeof(XmlIgnoreAttribute), false)) continue;
                p.SetValue(this, p.GetValue(other));
            }
            if (CardText == null) CardText = new CardText();
        }

        private static void WriteTo(string path, AppSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var serializer = new XmlSerializer(typeof(AppSettings));
            using (FileStream fs = File.Create(path))
                serializer.Serialize(fs, settings);
        }
    }
}
