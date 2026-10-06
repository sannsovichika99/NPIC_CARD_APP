using System;
using System.IO;
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
            return new AppSettings { SlotMode = SlotMode.Auto };   // first run
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
