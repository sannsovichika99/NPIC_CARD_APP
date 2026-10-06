using System;
using System.IO;
using System.Xml.Serialization;

namespace NPIC_CARD_APP.Services
{
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

        // Card position on the page, in millimetres (calibrate with Test Print)
        public float Slot1X { get; set; }
        public float Slot1Y { get; set; }
        public float Slot2X { get; set; }
        public float Slot2Y { get; set; }

        public bool UseTwoSlots { get; set; }
        public bool RotateBack180 { get; set; }

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
            Slot1X = 0f;
            Slot1Y = 0f;
            Slot2X = 0f;
            Slot2Y = 60f;
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

        private static string FilePath
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
            return new AppSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var serializer = new XmlSerializer(typeof(AppSettings));
            using (FileStream fs = File.Create(FilePath))
                serializer.Serialize(fs, this);
        }
    }
}
