using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP.Rendering
{
    // =========================================================================
    // Custom card design (Card Designer…). Saved to %AppData%\NPIC_CARD_APP\custom-card.xml, printed with the
    // "Custom" card template. Positions and sizes are in millimetres on the 84.6 x 54 mm card; elements are drawn
    // in list order (the last one is on top).
    // =========================================================================

    public enum CardFontFamily
    {
        [Description("Khmer OS Siemreap")] KhmerOSSiemreap,
        [Description("Khmer OS Muol")] KhmerOSMuol,
        [Description("Khmer OS Muol Light")] KhmerOSMuolLight,
        [Description("Siemreap")] Siemreap,
        /// <summary>Ornament font: type the characters that give the ornament shape (e.g. "3").</summary>
        [Description("Tacteing (ornament)")] Tacteing
    }

    public enum CardTextAlign { Left, Center, Right }

    /// <summary>Data printed by a <see cref="FieldElement"/>. Student fields print empty on the back (same for every student).</summary>
    public enum CardField
    {
        StudentId, NameKhmer, NameLatin, Gender, DateOfBirth, Faculty, Level, Generation, ExpiryDate,
        IssueLine1, IssueLine2, DirectorTitle, DirectorName, Address, Email, Website,
        /// <summary>Any column of the student list, named in <see cref="FieldElement.Column"/> (e.g. Phone, BloodType).</summary>
        Custom
    }

    /// <summary>How a field's value is printed.</summary>
    public enum CardValueFormat
    {
        /// <summary>As it is in the list / Card Text.</summary>
        AsIs,
        /// <summary>CAPITAL LETTERS.</summary>
        Uppercase,
        /// <summary>Digits as Khmer digits: 2025 → ២០២៥.</summary>
        KhmerDigits,
        /// <summary>A dd/MM/yyyy date in Khmer: 24/01/2008 → ២៤ មករា ២០០៨.</summary>
        KhmerDate
    }

    /// <summary>
    /// Drop-down of the column names in the loaded student list for <see cref="FieldElement.Column"/> (any other name can be typed).
    /// Card Designer fills <see cref="Known"/> when it opens.
    /// </summary>
    public sealed class StudentColumnConverter : StringConverter
    {
        public static readonly List<string> Known = new List<string>();

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return Known.Count > 0; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return false; }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context) { return new StandardValuesCollection(Known.ToArray()); }
    }

    public enum CardImageSource
    {
        NpicLogo, MinistryLogo, Stamp, Signature, BackQrPicture, Facebook, Telegram, YouTube, TikTok,
        PinIcon, MailIcon, GlobeIcon,
        /// <summary>The ornament from Card Text (Tacteing glyphs, or a drawn ornament when the font is missing).</summary>
        Ornament,
        /// <summary>Diagonal "NPIC" pattern of the Classic back, clipped to the box.</summary>
        WatermarkPattern,
        /// <summary>Any PNG/JPG chosen in "File".</summary>
        CustomFile
    }

    public enum CardQrContent
    {
        /// <summary>The student's QR (QR template in Card Text, or the QrData column).</summary>
        StudentQr,
        /// <summary>The back QR from Card Text (generated link with logo, or the fixed picture).</summary>
        BackQr,
        /// <summary>The text typed in "Custom text" (same on every card).</summary>
        CustomText
    }

    public enum CardShapeKind { Rectangle, Ellipse, Line, Triangle, Curve }

    /// <summary>
    /// Where a triangle or curve points. A side = apex / top of the arch on that side.
    /// A corner = right-angled triangle or quarter curve sitting in that corner.
    /// </summary>
    public enum CardShapeDirection { Up, Down, Left, Right, TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>One thing on the card. Concrete types are listed in XmlInclude so the XML file can hold any of them.</summary>
    [XmlInclude(typeof(TextElement)), XmlInclude(typeof(FieldElement)), XmlInclude(typeof(ImageElement)),
     XmlInclude(typeof(PhotoElement)), XmlInclude(typeof(QrElement)), XmlInclude(typeof(ShapeElement))]
    public abstract class CardElement
    {
        protected CardElement()
        {
            Visible = true;
            Name = "";
        }

        [Category("1. Layer"), DisplayName("Name"), Description("Shown in the layer list only; never printed.")]
        public string Name { get; set; }

        [Category("1. Layer"), DisplayName("Visible"), Description("Hidden elements are not printed.")]
        public bool Visible { get; set; }

        [Category("1. Layer"), DisplayName("Locked"), Description("A locked element cannot be moved with the mouse (handy for backgrounds). Select it in the layer list to edit it.")]
        public bool Locked { get; set; }

        /// <summary>
        /// Elements with the same id on one side form a group (Card Designer → Group): a click selects all of them and they move,
        /// line up and copy as one block. Null = not grouped. Only the designer uses it; printing ignores it.
        /// </summary>
        [Browsable(false)]
        public string GroupId { get; set; }

        [Category("2. Position (mm)"), DisplayName("X"), Description("Distance from the left edge of the card, in mm (card is 84.6 mm wide).")]
        public float X { get; set; }

        [Category("2. Position (mm)"), DisplayName("Y"), Description("Distance from the top edge of the card, in mm (card is 54 mm high).")]
        public float Y { get; set; }

        [Category("2. Position (mm)"), DisplayName("Width")]
        public float Width { get; set; }

        [Category("2. Position (mm)"), DisplayName("Height")]
        public float Height { get; set; }

        /// <summary>Kind of element for the layer list ("Text", "Photo"…).</summary>
        [Browsable(false), XmlIgnore]
        public abstract string Kind { get; }

        /// <summary>What the layer list shows: the name, or a description of the content.</summary>
        [Browsable(false), XmlIgnore]
        public string DisplayName { get { return string.IsNullOrWhiteSpace(Name) ? Describe() : Name; } }

        protected abstract string Describe();

        [Browsable(false), XmlIgnore]
        public RectangleF Bounds
        {
            get { return new RectangleF(X, Y, Width, Height); }
            set { X = value.X; Y = value.Y; Width = value.Width; Height = value.Height; }
        }

        public CardElement Clone() { return (CardElement)MemberwiseClone(); }

        // ---- Colour helpers: Color is not XML-serializable, so each colour is saved as #RRGGBB / #AARRGGBB ("" = none) ----
        internal static string ToHex(Color c)
        {
            if (c.IsEmpty || c.A == 0) return "";
            return c.A == 255
                ? string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B)
                : string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B);
        }

        internal static Color FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Color.Empty;
            string h = hex.Trim().TrimStart('#');
            uint v;
            if (!uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return Color.Empty;
            if (h.Length == 6) return Color.FromArgb(255, (int)(v >> 16) & 255, (int)(v >> 8) & 255, (int)v & 255);
            if (h.Length == 8) return Color.FromArgb((int)(v >> 24) & 255, (int)(v >> 16) & 255, (int)(v >> 8) & 255, (int)v & 255);
            return Color.Empty;
        }
    }

    /// <summary>Shared look of <see cref="TextElement"/> and <see cref="FieldElement"/>: one line, shrinks to fit the box.</summary>
    public abstract class TextElementBase : CardElement
    {
        protected TextElementBase()
        {
            Font = CardFontFamily.KhmerOSSiemreap;
            SizePt = 7f;
            Color = CardColors.Navy;
            Align = CardTextAlign.Left;
        }

        [Category("3. Text"), DisplayName("Font"),
         Description("Font of the Khmer letters. Latin letters and digits always use Times New Roman at the same size.")]
        public CardFontFamily Font { get; set; }

        [Category("3. Text"), DisplayName("Size (pt)"), Description("Text size. A line that is wider than the box shrinks (down to 60%) to fit.")]
        public float SizePt { get; set; }

        [Category("3. Text"), DisplayName("Bold Latin"), Description("Times New Roman Bold for the Latin letters (Khmer fonts have no bold).")]
        public bool Bold { get; set; }

        [Category("3. Text"), DisplayName("Colour"), XmlIgnore]
        public Color Color { get; set; }

        [Browsable(false), XmlElement("Color")]
        public string ColorHex { get { return ToHex(Color); } set { Color = FromHex(value); } }

        [Category("3. Text"), DisplayName("Alignment")]
        public CardTextAlign Align { get; set; }
    }

    /// <summary>Fixed text, the same on every card.</summary>
    public sealed class TextElement : TextElementBase
    {
        public TextElement() { Text = "Text"; }

        [Category("3. Text"), DisplayName("Text"), Description("Printed as typed, on one line.")]
        public string Text { get; set; }

        public override string Kind { get { return "Text"; } }
        protected override string Describe() { return string.IsNullOrEmpty(Text) ? "(empty text)" : Text; }
    }

    /// <summary>Student data or Card Text value, optionally with a label in front ("អត្តលេខ: NPIC24001").</summary>
    public sealed class FieldElement : TextElementBase
    {
        public FieldElement()
        {
            Field = CardField.NameKhmer;
            Column = "";
            Label = "";
            LabelColor = CardColors.Navy;
            Format = CardValueFormat.AsIs;
        }

        [Category("3. Text"), DisplayName("Field"),
         Description("Data printed here. Student fields come from the student list; Issue lines, Director, Address, Email and Website from Card Text. " +
                     "Custom = any column of the student list, named in Column. " +
                     "On the back, student fields print empty (the back is the same for every student).")]
        public CardField Field { get; set; }

        [Category("3. Text"), DisplayName("Column"),
         Description("Used when Field is Custom: the column header in your Excel / CSV / Google Sheet / SQL list, e.g. Phone or BloodType " +
                     "(capital letters do not matter). Pick one from the list or type it."),
         TypeConverter(typeof(StudentColumnConverter))]
        public string Column { get; set; }

        [Category("3. Text"), DisplayName("Format"),
         Description("AsIs = as in the list. Uppercase = CAPITALS. KhmerDigits = 2025 → ២០២៥. KhmerDate = 24/01/2008 → ២៤ មករា ២០០៨.")]
        public CardValueFormat Format { get; set; }

        [Category("3. Text"), DisplayName("Space left (mm)"),
         Description("Empty space on the left of the value, in mm. When Label column is more than 0 the space is added after \": \"; " +
                     "otherwise the whole line starts this far from the left edge of the box. 0 = no extra space.")]
        public float SpaceLeft { get; set; }

        [Category("3. Text"), DisplayName("Space right (mm)"),
         Description("Empty space kept on the right of the value, in mm: the value stops this far before the right edge of the box " +
                     "(a longer value shrinks to fit, and right / centre alignment moves in with it). 0 = no extra space.")]
        public float SpaceRight { get; set; }

        [Category("4. Label"), DisplayName("Label"), Description("Optional text in front of the value, e.g. អត្តលេខ or ភេទ. Empty = value only.")]
        public string Label { get; set; }

        [Category("4. Label"), DisplayName("Label column (mm)"),
         Description("0 = label and value on one line (\"Label: value\"). More than 0 = the label gets a column this wide and \": value\" starts after it, so rows line up.")]
        public float LabelWidth { get; set; }

        [Category("4. Label"), DisplayName("Label size (pt)"),
         Description("Size of the label and the colon when Label column is more than 0. 0 = the same size as the value.")]
        public float LabelSizePt { get; set; }

        [Category("4. Label"), DisplayName("Label colour"), XmlIgnore]
        public Color LabelColor { get; set; }

        [Browsable(false), XmlElement("LabelColor")]
        public string LabelColorHex { get { return ToHex(LabelColor); } set { LabelColor = FromHex(value); } }

        public override string Kind { get { return "Field"; } }
        protected override string Describe()
        {
            string what = Field == CardField.Custom ? (string.IsNullOrWhiteSpace(Column) ? "(no column)" : Column.Trim()) : Field.ToString();
            return "{" + what + "}" + (string.IsNullOrEmpty(Label) ? "" : "  " + Label);
        }

        [Browsable(false), XmlIgnore]
        public bool IsStudentField { get { return Field <= CardField.ExpiryDate || Field == CardField.Custom; } }
    }

    public sealed class ImageElement : CardElement
    {
        public ImageElement()
        {
            Source = CardImageSource.NpicLogo;
            FilePath = "";
            Opacity = 100;
            RemoveWhite = true;
        }

        [Category("3. Image"), DisplayName("Image"),
         Description("Built-in picture, or CustomFile to use the picture chosen in File. Signature = the director signature from Card Text.")]
        public CardImageSource Source { get; set; }

        [Category("3. Image"), DisplayName("File"),
         Description("Used when Image is CustomFile. Click … to choose a PNG/JPG; it is copied into the app's settings folder."),
         Editor(typeof(CardImageFileEditor), typeof(UITypeEditor))]
        public string FilePath { get; set; }

        [Category("3. Image"), DisplayName("Opacity (%)"), Description("100 = solid. Use 5–15 for a faded watermark.")]
        public int Opacity { get; set; }

        [Category("3. Image"), DisplayName("Remove white background"), Description("Makes white pixels transparent (logos, stamp, signature).")]
        public bool RemoveWhite { get; set; }

        public override string Kind { get { return "Image"; } }
        protected override string Describe()
        {
            return Source == CardImageSource.CustomFile
                ? (string.IsNullOrEmpty(FilePath) ? "(no file)" : Path.GetFileName(FilePath))
                : Source.ToString();
        }
    }

    /// <summary>The student's photo ({StudentId}.jpg in the photo folder), cropped to fill the box.</summary>
    public sealed class PhotoElement : CardElement
    {
        public PhotoElement()
        {
            FrameColor = CardColors.Navy;
            FrameMm = 0.3f;
        }

        [Category("3. Photo"), DisplayName("Frame colour"), XmlIgnore]
        public Color FrameColor { get; set; }

        [Browsable(false), XmlElement("FrameColor")]
        public string FrameColorHex { get { return ToHex(FrameColor); } set { FrameColor = FromHex(value); } }

        [Category("3. Photo"), DisplayName("Frame (mm)"), Description("Frame thickness around the photo; 0 = no frame.")]
        public float FrameMm { get; set; }

        public override string Kind { get { return "Photo"; } }
        protected override string Describe() { return "Student photo"; }
    }

    public sealed class QrElement : CardElement
    {
        public QrElement()
        {
            Content = CardQrContent.StudentQr;
            CustomText = "";
        }

        [Category("3. QR code"), DisplayName("Content"), Description("StudentQr changes per student. The QR is square: it uses the smaller of Width and Height.")]
        public CardQrContent Content { get; set; }

        [Category("3. QR code"), DisplayName("Custom text"), Description("Used when Content is CustomText. Keep it short; a long text makes a dense code.")]
        public string CustomText { get; set; }

        public override string Kind { get { return "QR"; } }
        protected override string Describe() { return Content == CardQrContent.CustomText ? "QR: " + CustomText : Content.ToString(); }
    }

    public sealed class ShapeElement : CardElement
    {
        public ShapeElement()
        {
            Shape = CardShapeKind.Rectangle;
            FillColor = CardColors.Navy;
            BorderColor = Color.Empty;
        }

        [Category("3. Shape"), DisplayName("Shape"),
         Description("Line: horizontal when wider than high, otherwise vertical. Its thickness is Border (mm). " +
                     "Triangle and Curve fill the box and point toward Direction.")]
        public CardShapeKind Shape { get; set; }

        [Category("3. Shape"), DisplayName("Direction"),
         Description("Triangle and Curve only. Up / Down / Left / Right = the apex or the top of the arch is on that side. " +
                     "A corner (TopLeft…) = a right-angled triangle or a quarter circle sitting in that corner. " +
                     "A curve is drawn with Border colour and Border (mm); give it a Fill colour to fill the area inside the arch.")]
        public CardShapeDirection Direction { get; set; }

        [Category("3. Shape"), DisplayName("Fill colour"), Description("Transparent (Web tab) = no fill."), XmlIgnore]
        public Color FillColor { get; set; }

        [Browsable(false), XmlElement("FillColor")]
        public string FillColorHex { get { return ToHex(FillColor); } set { FillColor = FromHex(value); } }

        [Category("3. Shape"), DisplayName("Border colour"), Description("Transparent (Web tab) = no border."), XmlIgnore]
        public Color BorderColor { get; set; }

        [Browsable(false), XmlElement("BorderColor")]
        public string BorderColorHex { get { return ToHex(BorderColor); } set { BorderColor = FromHex(value); } }

        [Category("3. Shape"), DisplayName("Border (mm)")]
        public float BorderMm { get; set; }

        [Category("3. Shape"), DisplayName("Corner radius (mm)"), Description("Rounded corners of a rectangle.")]
        public float CornerMm { get; set; }

        private float _rotation;

        [Category("3. Shape"), DisplayName("Rotation (°)"),
         Description("Turns the shape clockwise around its centre, 0–359°. On the card, drag the round handle above the selection " +
                     "(Shift = 15° steps), or right-click → Rotate 90°.")]
        public float Rotation
        {
            get { return _rotation; }
            set { _rotation = NormalizeAngle(value); }
        }

        /// <summary>Any angle → 0 ≤ a &lt; 360, rounded to 0.1°.</summary>
        internal static float NormalizeAngle(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return 0f;
            float a = (float)Math.Round(degrees % 360f, 1);
            if (a < 0f) a += 360f;
            return a >= 360f ? 0f : a;
        }

        public override string Kind { get { return "Shape"; } }
        protected override string Describe()
        {
            return Shape == CardShapeKind.Triangle || Shape == CardShapeKind.Curve ? Shape + " " + Direction : Shape.ToString();
        }
    }

    /// <summary>One side of the card.</summary>
    public sealed class CardSideDesign
    {
        public CardSideDesign()
        {
            Elements = new List<CardElement>();
            BackgroundColor = Color.White;
        }

        [Category("Card side"), DisplayName("Background colour"), Description("Colour of the whole side, under every element."), XmlIgnore]
        public Color BackgroundColor { get; set; }

        [Browsable(false), XmlElement("BackgroundColor")]
        public string BackgroundColorHex { get { return CardElement.ToHex(BackgroundColor); } set { BackgroundColor = CardElement.FromHex(value); } }

        [Browsable(false)]
        public List<CardElement> Elements { get; set; }
    }

    [XmlRoot("CustomCard")]
    public sealed class CustomCardDesign
    {
        public const int CurrentVersion = 1;

        public CustomCardDesign()
        {
            Version = CurrentVersion;
            Id = "";
            Name = "Custom card";
            Front = new CardSideDesign();
            Back = new CardSideDesign();
        }

        public int Version { get; set; }

        /// <summary>File name of the design in the CardDesigns folder (without .xml); "" until it is first saved.</summary>
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>Last save (local time); newest designs are listed first.</summary>
        public DateTime Modified { get; set; }

        public CardSideDesign Front { get; set; }
        public CardSideDesign Back { get; set; }
    }

    /// <summary>NPIC colours used by the starter design and as defaults.</summary>
    public static class CardColors
    {
        public static readonly Color Navy = Color.FromArgb(46, 49, 146);
        public static readonly Color Blue = Color.FromArgb(47, 75, 159);
        public static readonly Color Red = Color.FromArgb(172, 30, 37);
        public static readonly Color IdRed = Color.FromArgb(190, 30, 45);
        public static readonly Color BandText = Color.FromArgb(214, 220, 250);
    }

    /// <summary>
    /// Library of custom designs made with Card Designer…: one file per design in %AppData%\NPIC_CARD_APP\CardDesigns\{Id}.xml.
    /// <see cref="ForSettings"/> is the design the Custom template prints (AppSettings.CustomDesignId).
    /// Designs returned by <see cref="All"/> / <see cref="Find"/> are shared: clone before changing one.
    /// </summary>
    public static class CustomCardStore
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(CustomCardDesign));
        private static List<CustomCardDesign> _all;
        private static CustomCardDesign _starter;

        /// <summary>%AppData%\NPIC_CARD_APP\CardDesigns</summary>
        public static string Folder
        {
            get { return Path.Combine(Path.GetDirectoryName(AppSettings.FilePath), "CardDesigns"); }
        }

        /// <summary>Single design file of the first Card Designer version; moved into <see cref="Folder"/> on first use.</summary>
        private static string LegacyFilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(AppSettings.FilePath), "custom-card.xml"); }
        }

        /// <summary>Saved designs, newest first. Unreadable files are skipped.</summary>
        public static IList<CustomCardDesign> All()
        {
            if (_all == null) _all = LoadAll();
            return _all.AsReadOnly();
        }

        /// <summary>The saved design with this id, or null.</summary>
        public static CustomCardDesign Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (CustomCardDesign d in All())
                if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        /// <summary>
        /// Design printed by the Custom template: the chosen one, else the newest saved one, else the starter layout.
        /// Never null.
        /// </summary>
        public static CustomCardDesign ForSettings(AppSettings settings)
        {
            CustomCardDesign d = Find(settings == null ? null : settings.CustomDesignId);
            if (d != null) return d;
            IList<CustomCardDesign> all = All();
            if (all.Count > 0) return all[0];
            return _starter ?? (_starter = CreateStarter());
        }

        /// <summary>A new, unsaved design (starter layout or empty) with a fresh id and a name not used yet.</summary>
        public static CustomCardDesign CreateNew(bool starter)
        {
            CustomCardDesign d = starter ? CreateStarter() : new CustomCardDesign();
            d.Id = NewId();
            d.Name = UniqueName("Card design");
            return d;
        }

        /// <summary>Copy of a design under a new id and name (unsaved).</summary>
        public static CustomCardDesign Duplicate(CustomCardDesign source)
        {
            CustomCardDesign d = Clone(source);
            d.Id = NewId();
            d.Name = UniqueName((source.Name ?? "Card design") + " copy");
            return d;
        }

        /// <summary>"Card design 3": the base name with the first free number (or the base itself when free).</summary>
        public static string UniqueName(string baseName, string exceptId = null)
        {
            baseName = string.IsNullOrWhiteSpace(baseName) ? "Card design" : baseName.Trim();
            Func<string, bool> taken = n =>
            {
                foreach (CustomCardDesign d in All())
                    if (!string.Equals(d.Id, exceptId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(d.Name, n, StringComparison.CurrentCultureIgnoreCase))
                        return true;
                return false;
            };
            if (!taken(baseName) && baseName != "Card design") return baseName;
            for (int i = baseName == "Card design" ? 1 : 2; ; i++)
            {
                string name = baseName + " " + i;
                if (!taken(name)) return name;
            }
        }

        /// <summary>Saves (creates or replaces) the design's file; gives it an id first if it has none.</summary>
        public static void Save(CustomCardDesign design)
        {
            if (string.IsNullOrEmpty(design.Id)) design.Id = NewId();
            design.Modified = DateTime.Now;
            WriteFile(PathOf(design.Id), design);
            CustomCardDesign copy = Clone(design);
            List<CustomCardDesign> all = (List<CustomCardDesign>)GetList();
            all.RemoveAll(d => string.Equals(d.Id, design.Id, StringComparison.OrdinalIgnoreCase));
            all.Insert(0, copy);
        }

        public static void Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            string path = PathOf(id);
            if (File.Exists(path)) File.Delete(path);
            ((List<CustomCardDesign>)GetList()).RemoveAll(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static IList<CustomCardDesign> GetList()
        {
            if (_all == null) _all = LoadAll();
            return _all;
        }

        private static string NewId()
        {
            return "design-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        private static string PathOf(string id)
        {
            // Ids come from NewId(); anything else (an edited file) is reduced to safe file-name characters.
            var safe = new StringBuilder();
            foreach (char c in id) safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return Path.Combine(Folder, safe + ".xml");
        }

        private static List<CustomCardDesign> LoadAll()
        {
            var list = new List<CustomCardDesign>();
            try
            {
                MigrateLegacyFile();
                if (Directory.Exists(Folder))
                {
                    foreach (string file in Directory.GetFiles(Folder, "*.xml"))
                    {
                        try
                        {
                            CustomCardDesign d = ReadFile(file);
                            d.Id = Path.GetFileNameWithoutExtension(file);   // the file name is the id
                            if (string.IsNullOrWhiteSpace(d.Name)) d.Name = d.Id;
                            list.Add(d);
                        }
                        catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
                        {
                            // A damaged or foreign file must not hide the other designs.
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
            list.Sort((a, b) => b.Modified.CompareTo(a.Modified));
            return list;
        }

        /// <summary>custom-card.xml (one design) → CardDesigns\{id}.xml; the old file is kept as custom-card.migrated.xml.</summary>
        private static void MigrateLegacyFile()
        {
            if (!File.Exists(LegacyFilePath)) return;
            try
            {
                CustomCardDesign d = ReadFile(LegacyFilePath);
                d.Id = NewId();
                if (string.IsNullOrWhiteSpace(d.Name) || d.Name == "Custom card") d.Name = "Card design 1";
                d.Modified = File.GetLastWriteTime(LegacyFilePath);
                WriteFile(PathOf(d.Id), d);
                string done = Path.ChangeExtension(LegacyFilePath, ".migrated.xml");
                if (File.Exists(done)) File.Delete(done);
                File.Move(LegacyFilePath, done);
            }
            catch (InvalidDataException)
            {
            }
        }

        public static CustomCardDesign Clone(CustomCardDesign design)
        {
            return Deserialize(Serialize(design));
        }

        public static string Serialize(CustomCardDesign design)
        {
            // The declaration must say utf-8: the text is written to disk as UTF-8 (a StringBuilder writer would say utf-16).
            using (var sw = new Utf8StringWriter())
            {
                using (var w = XmlWriter.Create(sw, new XmlWriterSettings { Indent = true }))
                    Serializer.Serialize(w, design);
                return sw.ToString();
            }
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public Utf8StringWriter() : base(CultureInfo.InvariantCulture) { }
            public override Encoding Encoding { get { return new UTF8Encoding(false); } }
        }

        public static CustomCardDesign Deserialize(string xml)
        {
            using (var r = new StringReader(xml))
                return Normalize((CustomCardDesign)Serializer.Deserialize(r));
        }

        public static void WriteFile(string path, CustomCardDesign design)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, Serialize(design), new UTF8Encoding(false));
        }

        /// <exception cref="InvalidDataException">Not a Card Studio custom card file.</exception>
        public static CustomCardDesign ReadFile(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                    return Normalize((CustomCardDesign)Serializer.Deserialize(fs));
            }
            catch (InvalidOperationException ex)   // XmlSerializer: not XML, not <CustomCard>, or an unknown element type
            {
                throw new InvalidDataException("This is not a Card Studio custom card design:\n" + path, ex);
            }
        }

        private static CustomCardDesign Normalize(CustomCardDesign d)
        {
            if (d.Front == null) d.Front = new CardSideDesign();
            if (d.Back == null) d.Back = new CardSideDesign();
            if (d.Front.Elements == null) d.Front.Elements = new List<CardElement>();
            if (d.Back.Elements == null) d.Back.Elements = new List<CardElement>();
            d.Front.Elements.RemoveAll(e => e == null);
            d.Back.Elements.RemoveAll(e => e == null);
            return d;
        }

        // =====================================================================
        // STARTER DESIGN — a complete card (navy band, fields, photo, QR, director) to start from
        // =====================================================================
        public static CustomCardDesign CreateStarter()
        {
            var d = new CustomCardDesign();
            List<CardElement> f = d.Front.Elements;

            f.Add(Rect("Header band", 0f, 0f, 84.6f, 10.4f, CardColors.Navy, true));
            f.Add(Rect("Header line", 0f, 10.4f, 84.6f, 0.6f, CardColors.Red, true));
            f.Add(new ShapeElement { Name = "Logo disc", Shape = CardShapeKind.Ellipse, FillColor = Color.White, X = 1.8f, Y = 1.1f, Width = 8.2f, Height = 8.2f });
            f.Add(Img("Logo", CardImageSource.NpicLogo, 3.1f, 1.7f, 5.6f, 7.0f));
            f.Add(Txt("Title", "ប័ណ្ណសម្គាល់ខ្លួននិស្សិត", CardFontFamily.KhmerOSMuol, 9f, Color.White, 11.6f, 0.7f, 48f, 5.4f));
            f.Add(Txt("Title (English)", "STUDENT IDENTIFICATION CARD", CardFontFamily.KhmerOSMuol, 5.6f, CardColors.BandText, 11.6f, 5.8f, 48f, 3.4f, bold: true));
            f.Add(new ShapeElement { Name = "Generation pill", FillColor = Color.Empty, BorderColor = Color.White, BorderMm = 0.2f, CornerMm = 2.6f, X = 64.6f, Y = 2.6f, Width = 17.4f, Height = 5.2f });
            f.Add(Fld(null, CardField.Generation, "ជំនាន់ទី ", 0f, 7.2f, Color.White, Color.White, 64.6f, 2.6f, 17.4f, 5.2f, CardTextAlign.Center));
            f.Add(new ImageElement { Name = "Watermark", Source = CardImageSource.NpicLogo, Opacity = 7, X = 46f, Y = 13.5f, Width = 22f, Height = 36f });

            f.Add(new PhotoElement { X = 4.4f, Y = 12.8f, Width = CardRenderer.PhotoWidthMm, Height = CardRenderer.PhotoHeightMm });
            f.Add(Fld("ID", CardField.StudentId, "អត្តលេខ: ", 0f, 7.1f, CardColors.Navy, CardColors.IdRed, 2.0f, 35.4f, 23.1f, 3.4f, CardTextAlign.Center));
            f.Add(Fld(null, CardField.ExpiryDate, "", 0f, 6.4f, CardColors.IdRed, CardColors.IdRed, 2.0f, 38.8f, 23.1f, 3.2f, CardTextAlign.Center));

            string[] labels = { "គោត្តនាម-នាម", "អក្សរឡាតាំង", "ភេទ", "ថ្ងៃខែឆ្នាំកំណើត", "មហាវិទ្យាល័យ", "កម្រិត" };
            CardField[] fields = { CardField.NameKhmer, CardField.NameLatin, CardField.Gender, CardField.DateOfBirth, CardField.Faculty, CardField.Level };
            for (int i = 0; i < labels.Length; i++)
                f.Add(Fld(null, fields[i], labels[i], 13.8f, 6.8f, CardColors.Navy, CardColors.Navy, 26.6f, 12.6f + i * 3.6f, 56.4f, 3.6f, CardTextAlign.Left));

            f.Add(Fld(null, CardField.IssueLine1, "", 0f, 6f, CardColors.Blue, CardColors.Blue, 26.6f, 34.4f, 56.4f, 2.9f, CardTextAlign.Right));
            f.Add(Fld(null, CardField.IssueLine2, "", 0f, 6f, CardColors.Blue, CardColors.Blue, 26.6f, 37.2f, 56.4f, 2.9f, CardTextAlign.Right));
            f.Add(new QrElement { X = 27f, Y = 39.6f, Width = 12.6f, Height = 12.6f });
            AddDirector(f, 58.6f, 40.6f, 0.85f);
            f.Add(Rect("Footer", 0f, 52.6f, 84.6f, 1.4f, CardColors.Navy, true));
            f.Add(Rect("Footer accent", 0f, 52.6f, 22f, 1.4f, CardColors.Red, true));

            List<CardElement> b = d.Back.Elements;
            b.Add(Rect("Header band", 0f, 0f, 84.6f, 13.2f, CardColors.Navy, true));
            b.Add(Rect("Header line", 0f, 13.2f, 84.6f, 0.6f, CardColors.Red, true));
            b.Add(new ShapeElement { Name = "Ministry disc", Shape = CardShapeKind.Ellipse, FillColor = Color.White, X = 2.6f, Y = 1.2f, Width = 10.8f, Height = 10.8f });
            b.Add(Img("Ministry logo", CardImageSource.MinistryLogo, 3.7f, 2.3f, 8.6f, 8.6f));
            b.Add(new ShapeElement { Name = "NPIC disc", Shape = CardShapeKind.Ellipse, FillColor = Color.White, X = 71.2f, Y = 1.2f, Width = 10.8f, Height = 10.8f });
            b.Add(Img("NPIC logo", CardImageSource.NpicLogo, 73.2f, 2.0f, 6.8f, 9.2f));
            b.Add(Txt("Kingdom", "ព្រះរាជាណាចក្រកម្ពុជា", CardFontFamily.KhmerOSMuolLight, 10f, Color.White, 15f, 1.0f, 54.6f, 5.4f, align: CardTextAlign.Center));
            b.Add(Txt("Motto", "ជាតិ សាសនា ព្រះមហាក្សត្រ", CardFontFamily.KhmerOSMuolLight, 9f, CardColors.BandText, 15f, 6.6f, 54.6f, 5.0f, align: CardTextAlign.Center));
            b.Add(Txt("Title", "ប័ណ្ណសម្គាល់ខ្លួននិស្សិត", CardFontFamily.KhmerOSMuol, 15f, CardColors.Red, 8f, 15.6f, 68.6f, 8.2f, align: CardTextAlign.Center));
            b.Add(Txt("Title (English)", "STUDENT IDENTIFICATION CARD", CardFontFamily.KhmerOSMuol, 8.5f, CardColors.Red, 8f, 24.2f, 68.6f, 3.8f, bold: true, align: CardTextAlign.Center));
            b.Add(Txt("Ornament", "3", CardFontFamily.Tacteing, 16f, CardColors.Navy, 33.7f, 27.5f, 17.2f, 5f, align: CardTextAlign.Center));

            b.Add(Img(null, CardImageSource.PinIcon, 5.3f, 35.0f, 2f, 2f));
            b.Add(Fld(null, CardField.Address, "", 0f, 7.2f, CardColors.Navy, CardColors.Navy, 9.4f, 34.0f, 56f, 4.0f, CardTextAlign.Left));
            b.Add(Img(null, CardImageSource.MailIcon, 5.3f, 39.2f, 2f, 2f));
            b.Add(Fld(null, CardField.Email, "", 0f, 7.2f, CardColors.Navy, CardColors.Navy, 9.4f, 38.3f, 56f, 3.8f, CardTextAlign.Left));
            b.Add(Img(null, CardImageSource.GlobeIcon, 5.3f, 43.5f, 2f, 2f));
            b.Add(Fld(null, CardField.Website, "", 0f, 7.2f, CardColors.Navy, CardColors.Navy, 9.4f, 42.6f, 56f, 3.8f, CardTextAlign.Left));

            b.Add(new QrElement { Content = CardQrContent.BackQr, X = 69.3f, Y = 32.0f, Width = 11.3f, Height = 11.3f });
            b.Add(Txt("Scan here", "ស្កេនទីនេះសម្រាប់ព័ត៌មាន", CardFontFamily.Siemreap, 3.6f, CardColors.Red, 66.5f, 43.6f, 16.8f, 2.8f, align: CardTextAlign.Center));
            CardImageSource[] icons = { CardImageSource.Facebook, CardImageSource.Telegram, CardImageSource.YouTube, CardImageSource.TikTok };
            for (int i = 0; i < icons.Length; i++)
                b.Add(Img(null, icons[i], 69.84f + i * 2.73f, 46.7f, 2.5f, 2.5f));
            b.Add(Rect("Footer", 0f, 52.6f, 84.6f, 1.4f, CardColors.Navy, true));
            b.Add(Rect("Footer accent", 62.6f, 52.6f, 22f, 1.4f, CardColors.Red, true));
            return d;
        }

        /// <summary>Director title, stamp, signature and name (same arrangement as the built-in cards).</summary>
        private static void AddDirector(List<CardElement> list, float x, float y, float k)
        {
            list.Add(Fld(null, CardField.DirectorTitle, "", 0f, 6.6f * k, CardColors.Blue, CardColors.Blue, x, y, 24.8f * k, 3.8f * k, CardTextAlign.Center, CardFontFamily.KhmerOSMuol));
            list.Add(Img("Stamp", CardImageSource.Stamp, x + 1.7f * k, y + 0.8f * k, 12.2f * k, 12.2f * k));
            list.Add(Img("Signature", CardImageSource.Signature, x + 6.5f * k, y + 5.6f * k, 13f * k, 5.6f * k));
            list.Add(Fld(null, CardField.DirectorName, "", 0f, 6.2f * k, CardColors.Navy, CardColors.Navy, x + 7.9f * k, y + 10.6f * k, 20f * k, 3.6f * k, CardTextAlign.Center, CardFontFamily.KhmerOSMuol));
        }

        private static ShapeElement Rect(string name, float x, float y, float w, float h, Color fill, bool locked)
        {
            return new ShapeElement { Name = name, FillColor = fill, Locked = locked, X = x, Y = y, Width = w, Height = h };
        }

        private static ImageElement Img(string name, CardImageSource source, float x, float y, float w, float h)
        {
            return new ImageElement { Name = name ?? "", Source = source, X = x, Y = y, Width = w, Height = h };
        }

        private static TextElement Txt(string name, string text, CardFontFamily font, float pt, Color color,
            float x, float y, float w, float h, bool bold = false, CardTextAlign align = CardTextAlign.Left)
        {
            return new TextElement { Name = name, Text = text, Font = font, SizePt = pt, Color = color, Bold = bold, Align = align, X = x, Y = y, Width = w, Height = h };
        }

        private static FieldElement Fld(string name, CardField field, string label, float labelWidth, float pt, Color labelColor, Color color,
            float x, float y, float w, float h, CardTextAlign align, CardFontFamily font = CardFontFamily.KhmerOSSiemreap)
        {
            return new FieldElement
            {
                Name = name ?? "", Field = field, Label = label, LabelWidth = labelWidth, SizePt = pt, LabelColor = labelColor, Color = color,
                Font = font, Align = align, X = x, Y = y, Width = w, Height = h
            };
        }
    }
}
