using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Services;

namespace NPIC_CARD_APP.Rendering
{
    // =====================================================================
    // PDF Designer: a free page layout (text boxes, tables and pictures placed anywhere) exported as a PDF.
    //   - PdfDesign / PdfDesignPage / PdfItem: the document, saved as XML in %AppData%\NPIC_CARD_APP\PdfDesigns\{Id}.xml;
    //   - PdfDesignRenderer: draws a page on screen and writes the PDF from the same layout code, so both match;
    //   - positions and sizes are millimetres from the top-left corner of the page.
    // =====================================================================

    public enum PdfTextAlign { Left, Center, Right }

    public enum PdfOrientation { Portrait, Landscape }

    /// <summary>A PDF Designer document: paper and pages.</summary>
    [XmlRoot("PdfDesign")]
    public sealed class PdfDesign
    {
        /// <summary>Paper names (portrait sizes in <see cref="PaperSize"/>).</summary>
        public static readonly string[] PaperNames = { "A4", "A5", "Letter", "Legal" };

        public const float DefaultMarginMm = 15f;

        // No default page here: XmlSerializer adds the saved pages to whatever list the constructor makes.
        public PdfDesign()
        {
            Id = "";
            Name = "PDF design";
            Paper = "A4";
            MarginMm = DefaultMarginMm;
            Pages = new List<PdfDesignPage>();
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string Paper { get; set; }
        public bool Landscape { get; set; }
        public DateTime Saved { get; set; }

        /// <summary>
        /// Free space around the page: Align lines objects up with it, and a long data table continues on the next page
        /// at this distance from the top and bottom edges.
        /// </summary>
        public float MarginMm { get; set; }

        /// <summary>The whole design is exported once for each data record (certificates, letters), not once in total.</summary>
        public bool OnePerRecord { get; set; }

        /// <summary><see cref="MarginMm"/> kept between 0 and a third of the shorter page side.</summary>
        [XmlIgnore]
        public float Margin
        {
            get
            {
                SizeF p = PageMm;
                float m = float.IsNaN(MarginMm) ? DefaultMarginMm : MarginMm;
                return Math.Max(0f, Math.Min(Math.Min(p.Width, p.Height) / 3f, m));
            }
        }

        /// <summary>True when some text or table cell has a data field ({NameKh}…) or a table repeats for each record.</summary>
        public bool UsesData()
        {
            foreach (PdfDesignPage p in Pages ?? new List<PdfDesignPage>())
                foreach (PdfItem item in p.Items ?? new List<PdfItem>())
                {
                    var text = item as PdfTextItem;
                    if (text != null && PdfFieldContext.HasFields(text.Text)) return true;
                    var table = item as PdfTableItem;
                    if (table != null && (table.DataRows || table.Rows.Any(r => r.Cells.Any(PdfFieldContext.HasFields)))) return true;
                }
            return false;
        }

        [XmlArray("Pages"), XmlArrayItem("Page")]
        public List<PdfDesignPage> Pages { get; set; }

        /// <summary>Paper name as saved, "A4" when unknown.</summary>
        [XmlIgnore]
        public string PaperName
        {
            get
            {
                string p = PaperNames.FirstOrDefault(n => string.Equals(n, (Paper ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                return p ?? "A4";
            }
        }

        /// <summary>Page size in millimetres, turned for landscape.</summary>
        [XmlIgnore]
        public SizeF PageMm
        {
            get
            {
                SizeF size = PaperSize(PaperName);
                return Landscape ? new SizeF(size.Height, size.Width) : size;
            }
        }

        public static SizeF PaperSize(string paper)
        {
            switch ((paper ?? "").Trim().ToUpperInvariant())
            {
                case "A5": return new SizeF(148f, 210f);
                case "LETTER": return new SizeF(215.9f, 279.4f);
                case "LEGAL": return new SizeF(215.9f, 355.6f);
                default: return new SizeF(210f, 297f);
            }
        }

        /// <summary>At least one page, no null items, tables with complete rows. Call after loading.</summary>
        public void Normalize()
        {
            if (Pages == null) Pages = new List<PdfDesignPage>();
            Pages.RemoveAll(p => p == null);
            if (Pages.Count == 0) Pages.Add(new PdfDesignPage());
            foreach (PdfDesignPage p in Pages)
            {
                if (p.Items == null) p.Items = new List<PdfItem>();
                p.Items.RemoveAll(i => i == null);
                foreach (PdfTableItem t in p.Items.OfType<PdfTableItem>()) t.Normalize();
            }
            if (string.IsNullOrWhiteSpace(Name)) Name = "PDF design";
        }

        /// <summary>Deep copy (pictures share their bytes, which never change).</summary>
        public PdfDesign Clone()
        {
            var copy = (PdfDesign)MemberwiseClone();
            copy.Pages = (Pages ?? new List<PdfDesignPage>()).Select(p => p.Clone()).ToList();
            return copy;
        }
    }

    public sealed class PdfDesignPage
    {
        public PdfDesignPage() { Items = new List<PdfItem>(); }

        /// <summary>Items in drawing order: the last one is on top.</summary>
        [XmlArray("Items"),
         XmlArrayItem("Text", typeof(PdfTextItem)),
         XmlArrayItem("Table", typeof(PdfTableItem)),
         XmlArrayItem("Image", typeof(PdfImageItem))]
        public List<PdfItem> Items { get; set; }

        public PdfDesignPage Clone()
        {
            return new PdfDesignPage { Items = (Items ?? new List<PdfItem>()).Select(i => i.Clone()).ToList() };
        }
    }

    // ===================== Items =====================

    /// <summary>Something placed on a page. Properties with [Category] are shown in the PDF Designer property list.</summary>
    public abstract class PdfItem
    {
        public const float MinSizeMm = 4f;

        [Category("1. Position"), DisplayName("Left (mm)"), Description("Distance from the left edge of the paper.")]
        public float X { get; set; }

        [Category("1. Position"), DisplayName("Top (mm)"), Description("Distance from the top edge of the paper.")]
        public float Y { get; set; }

        [Category("1. Position"), DisplayName("Width (mm)"), Description("Width on the paper. Drag the handles on the page to change it.")]
        public float Width { get; set; }

        /// <summary>Height in mm. Text and tables set it themselves from their content (<see cref="PdfDesignRenderer.Layout"/>).</summary>
        [Browsable(false)]
        public float Height { get; set; }

        [Browsable(false), XmlIgnore]
        public abstract string Kind { get; }

        [Browsable(false), XmlIgnore]
        public RectangleF Bounds { get { return new RectangleF(X, Y, Width, Height); } }

        /// <summary>True when the height can be dragged (pictures); text and tables grow with their content.</summary>
        [Browsable(false), XmlIgnore]
        public virtual bool ResizesHeight { get { return false; } }

        /// <summary>Short description for the status line ("Text “Title”").</summary>
        public abstract string Describe();

        public virtual PdfItem Clone() { return (PdfItem)MemberwiseClone(); }

        // Colours are saved as #RRGGBB ("" = none): Color is not XML-serializable.
        internal static string ToHex(Color c)
        {
            if (c.IsEmpty || c.A == 0) return "";
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
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

        /// <summary>True when a colour is set (not Empty / Transparent).</summary>
        internal static bool Visible(Color c) { return !c.IsEmpty && c.A > 0; }
    }

    /// <summary>A text box: any number of lines, wrapped to its width; its height follows the text.</summary>
    public sealed class PdfTextItem : PdfItem
    {
        public PdfTextItem()
        {
            Text = "Text";
            FontName = CardFonts.KhmerSiemreap;
            SizePt = 12f;
            Color = PdfDesignRenderer.Ink;
            Align = PdfTextAlign.Left;
            Fill = Color.Empty;
            Border = Color.Empty;
            BorderPt = 0.75f;
            Width = 70f;
        }

        /// <summary>The text; lines are separated by "\n". Edited on the page (double-click) or in the Text box above the list.</summary>
        [Browsable(false)]
        public string Text { get; set; }

        [Category("2. Text"), DisplayName("Font"), TypeConverter(typeof(InstalledFontConverter)),
         Description("Any font installed on this PC. Khmer OS fonts also have Latin letters, so one font can show both.")]
        public string FontName { get; set; }

        [Category("2. Text"), DisplayName("Size (pt)"), Description("Text size in points (Word sizes: 12 = normal text).")]
        public float SizePt { get; set; }

        [Category("2. Text"), DisplayName("Bold")]
        public bool Bold { get; set; }

        [Category("2. Text"), DisplayName("Italic")]
        public bool Italic { get; set; }

        [Category("2. Text"), DisplayName("Underline")]
        public bool Underline { get; set; }

        [Category("2. Text"), DisplayName("Colour"), XmlIgnore]
        public Color Color { get; set; }

        [Browsable(false), XmlElement("Color")]
        public string ColorHex { get { return ToHex(Color); } set { Color = FromHex(value); } }

        [Category("2. Text"), DisplayName("Alignment"), Description("Left, centre or right inside the box.")]
        public PdfTextAlign Align { get; set; }

        [Category("3. Box"), DisplayName("Background"), XmlIgnore,
         Description("Colour behind the text. Transparent (on the Web tab) = none.")]
        public Color Fill { get; set; }

        [Browsable(false), XmlElement("Fill")]
        public string FillHex { get { return ToHex(Fill); } set { Fill = FromHex(value); } }

        [Category("3. Box"), DisplayName("Border colour"), XmlIgnore, Description("Line around the box. Transparent = none.")]
        public Color Border { get; set; }

        [Browsable(false), XmlElement("Border")]
        public string BorderHex { get { return ToHex(Border); } set { Border = FromHex(value); } }

        [Category("3. Box"), DisplayName("Border (pt)"), Description("Thickness of the border line.")]
        public float BorderPt { get; set; }

        [Category("3. Box"), DisplayName("Padding (mm)"), Description("Space between the box edge and the text.")]
        public float PaddingMm { get; set; }

        public override string Kind { get { return "Text"; } }

        public override string Describe()
        {
            string first = (Text ?? "").Split('\n')[0].Trim();
            if (first.Length > 30) first = first.Substring(0, 30) + "…";
            return first.Length == 0 ? Lang.T("Text (empty)") : Lang.F("Text “{0}”", first);
        }
    }

    /// <summary>One table row: the text of each cell, left to right.</summary>
    public sealed class PdfTableRow
    {
        public PdfTableRow() { Cells = new List<string>(); }

        [XmlElement("Cell")]
        public List<string> Cells { get; set; }

        public PdfTableRow Clone() { return new PdfTableRow { Cells = new List<string>(Cells ?? new List<string>()) }; }
    }

    /// <summary>A table: rows of cells, relative column widths, optional header row. Row heights follow the text.</summary>
    public sealed class PdfTableItem : PdfItem
    {
        public PdfTableItem()
        {
            Rows = new List<PdfTableRow>();
            ColumnWidths = new List<float>();
            FontName = CardFonts.KhmerSiemreap;
            SizePt = 10f;
            TextColor = PdfDesignRenderer.Ink;
            Align = PdfTextAlign.Left;
            HeaderRow = true;
            HeaderBold = true;
            HeaderAlign = PdfTextAlign.Center;
            HeaderFill = Color.FromArgb(226, 230, 240);
            StripeFill = Color.FromArgb(246, 247, 251);
            BorderColor = Color.FromArgb(70, 72, 84);
            BorderPt = 0.5f;
            PaddingMm = 1.5f;
            MinRowMm = 7f;
            Width = 150f;
        }

        /// <summary>A new table with <paramref name="rows"/> × <paramref name="columns"/> cells and header texts.</summary>
        public static PdfTableItem Create(int rows, int columns)
        {
            var t = new PdfTableItem();
            for (int r = 0; r < rows; r++)
            {
                var row = new PdfTableRow();
                for (int c = 0; c < columns; c++) row.Cells.Add(r == 0 ? Lang.F("Column {0}", c + 1) : "");
                t.Rows.Add(row);
            }
            for (int c = 0; c < columns; c++) t.ColumnWidths.Add(1f);
            return t;
        }

        [Browsable(false), XmlArray("Rows"), XmlArrayItem("Row")]
        public List<PdfTableRow> Rows { get; set; }

        /// <summary>Relative widths: a column with 2 is twice as wide as one with 1. Drag the column lines on the page.</summary>
        [Browsable(false), XmlArray("Widths"), XmlArrayItem("W")]
        public List<float> ColumnWidths { get; set; }

        [Category("2. Text"), DisplayName("Font"), TypeConverter(typeof(InstalledFontConverter)),
         Description("Font of every cell. Khmer OS fonts also have Latin letters.")]
        public string FontName { get; set; }

        [Category("2. Text"), DisplayName("Size (pt)")]
        public float SizePt { get; set; }

        [Category("2. Text"), DisplayName("Colour"), XmlIgnore]
        public Color TextColor { get; set; }

        [Browsable(false), XmlElement("TextColor")]
        public string TextColorHex { get { return ToHex(TextColor); } set { TextColor = FromHex(value); } }

        [Category("2. Text"), DisplayName("Alignment"), Description("Alignment of the cells under the header.")]
        public PdfTextAlign Align { get; set; }

        [Category("3. Header"), DisplayName("Header row"), Description("The first row is the header: shaded, and bold when Bold header is on.")]
        public bool HeaderRow { get; set; }

        [Category("3. Header"), DisplayName("Bold header")]
        public bool HeaderBold { get; set; }

        [Category("3. Header"), DisplayName("Header alignment")]
        public PdfTextAlign HeaderAlign { get; set; }

        [Category("3. Header"), DisplayName("Header shading"), XmlIgnore, Description("Fill of the header row. Transparent = none.")]
        public Color HeaderFill { get; set; }

        [Browsable(false), XmlElement("HeaderFill")]
        public string HeaderFillHex { get { return ToHex(HeaderFill); } set { HeaderFill = FromHex(value); } }

        [Category("4. Table"), DisplayName("Striped rows"), Description("Every second row under the header is shaded.")]
        public bool Stripes { get; set; }

        [Category("4. Table"), DisplayName("Stripe colour"), XmlIgnore]
        public Color StripeFill { get; set; }

        [Browsable(false), XmlElement("StripeFill")]
        public string StripeFillHex { get { return ToHex(StripeFill); } set { StripeFill = FromHex(value); } }

        [Category("4. Table"), DisplayName("Line colour"), XmlIgnore, Description("Colour of the cell borders. Transparent = no lines.")]
        public Color BorderColor { get; set; }

        [Browsable(false), XmlElement("BorderColor")]
        public string BorderColorHex { get { return ToHex(BorderColor); } set { BorderColor = FromHex(value); } }

        [Category("4. Table"), DisplayName("Line (pt)"), Description("Thickness of the cell borders. 0 = no lines.")]
        public float BorderPt { get; set; }

        [Category("4. Table"), DisplayName("Cell padding (mm)"), Description("Space between the cell border and its text.")]
        public float PaddingMm { get; set; }

        [Category("4. Table"), DisplayName("Row height (mm)"), Description("Smallest row height. A row with more text grows.")]
        public float MinRowMm { get; set; }

        [Category("5. Data"), DisplayName("Repeat for each record"),
         Description("The row under the header is printed once for every record of the data (Data ▾), with its fields " +
                     "({NameKh}, {StudentId}, {No}…) filled in. A long list continues on the next page with the header repeated.")]
        public bool DataRows { get; set; }

        /// <summary>The row repeated for each record (the one under the header), or -1 when <see cref="DataRows"/> is off.</summary>
        [Browsable(false), XmlIgnore]
        public int TemplateRow
        {
            get
            {
                if (!DataRows || Rows == null) return -1;
                int r = HeaderRow ? 1 : 0;
                return r < Rows.Count ? r : -1;
            }
        }

        /// <summary>Row heights from the last <see cref="PdfDesignRenderer.Layout"/>, in mm.</summary>
        [Browsable(false), XmlIgnore]
        public float[] RowHeights { get; set; }

        [Browsable(false), XmlIgnore]
        public int ColumnCount { get { return ColumnWidths == null ? 0 : ColumnWidths.Count; } }

        /// <summary>At least one row and column; every row has one cell per column; no null text; positive widths.</summary>
        public void Normalize()
        {
            if (Rows == null) Rows = new List<PdfTableRow>();
            if (ColumnWidths == null) ColumnWidths = new List<float>();
            Rows.RemoveAll(r => r == null);
            int cols = Math.Max(ColumnWidths.Count, Rows.Count == 0 ? 0 : Rows.Max(r => r.Cells == null ? 0 : r.Cells.Count));
            cols = Math.Max(1, cols);
            while (ColumnWidths.Count < cols) ColumnWidths.Add(ColumnWidths.Count == 0 ? 1f : ColumnWidths.Average());
            for (int c = 0; c < ColumnWidths.Count; c++)
                if (!(ColumnWidths[c] > 0f) || float.IsInfinity(ColumnWidths[c])) ColumnWidths[c] = 1f;
            if (Rows.Count == 0) Rows.Add(new PdfTableRow());
            foreach (PdfTableRow r in Rows)
            {
                if (r.Cells == null) r.Cells = new List<string>();
                while (r.Cells.Count < cols) r.Cells.Add("");
                if (r.Cells.Count > cols) r.Cells.RemoveRange(cols, r.Cells.Count - cols);
                for (int c = 0; c < cols; c++) if (r.Cells[c] == null) r.Cells[c] = "";
            }
        }

        public override string Kind { get { return "Table"; } }

        public override string Describe()
        {
            return Lang.F("Table {0} × {1}", Rows == null ? 0 : Rows.Count, ColumnCount);
        }

        public override PdfItem Clone()
        {
            var copy = (PdfTableItem)MemberwiseClone();
            copy.Rows = (Rows ?? new List<PdfTableRow>()).Select(r => r.Clone()).ToList();
            copy.ColumnWidths = new List<float>(ColumnWidths ?? new List<float>());
            copy.RowHeights = RowHeights == null ? null : (float[])RowHeights.Clone();
            return copy;
        }
    }

    /// <summary>A picture (logo, stamp, photo) stretched to its box.</summary>
    public sealed class PdfImageItem : PdfItem
    {
        private Image _picture;

        public PdfImageItem()
        {
            KeepRatio = true;
            Width = 40f;
            Height = 40f;
        }

        private byte[] _data;

        /// <summary>The picture file (PNG or JPEG bytes).</summary>
        [Browsable(false)]
        public byte[] Data
        {
            get { return _data; }
            set { _data = value; _picture = null; }
        }

        [Category("1. Position"), DisplayName("Height (mm)"), XmlIgnore]
        public float HeightMm { get { return Height; } set { Height = value; } }

        [Category("2. Picture"), DisplayName("Keep proportions"), Description("Corner handles keep the picture's width-to-height ratio.")]
        public bool KeepRatio { get; set; }

        [Browsable(false), XmlIgnore]
        public override bool ResizesHeight { get { return true; } }

        /// <summary>The decoded picture (kept; null when the bytes are not a picture).</summary>
        [Browsable(false), XmlIgnore]
        public Image Picture
        {
            get
            {
                if (_picture == null && Data != null && Data.Length > 0)
                {
                    try { _picture = Image.FromStream(new MemoryStream(Data)); }
                    catch (ArgumentException) { }
                }
                return _picture;
            }
        }

        /// <summary>Height ÷ width of the picture (1 when unknown).</summary>
        [Browsable(false), XmlIgnore]
        public float Ratio
        {
            get
            {
                Image p = Picture;
                return p == null || p.Width == 0 ? 1f : p.Height / (float)p.Width;
            }
        }

        /// <summary>A picture from a file or the clipboard: at most 2400 px, PNG when it has transparency, else JPEG.</summary>
        public static PdfImageItem FromImage(Image source, float widthMm)
        {
            const int MaxPx = 2400;
            float scale = Math.Min(1f, MaxPx / (float)Math.Max(source.Width, source.Height));
            int w = Math.Max(1, (int)Math.Round(source.Width * scale)), h = Math.Max(1, (int)Math.Round(source.Height * scale));
            bool alpha = Image.IsAlphaPixelFormat(source.PixelFormat);
            byte[] data;
            using (var bmp = new Bitmap(w, h, alpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(alpha ? Color.Transparent : Color.White);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(source, new Rectangle(0, 0, w, h));
                }
                using (var ms = new MemoryStream())
                {
                    if (alpha) bmp.Save(ms, ImageFormat.Png);
                    else
                    {
                        ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                        using (var args = new EncoderParameters(1))
                        {
                            args.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                            bmp.Save(ms, jpeg, args);
                        }
                    }
                    data = ms.ToArray();
                }
            }
            var item = new PdfImageItem { Data = data, Width = widthMm };
            item.Height = widthMm * item.Ratio;
            return item;
        }

        public override string Kind { get { return "Image"; } }

        public override string Describe() { return Lang.T("Picture"); }
    }

    /// <summary>Drop-down of the fonts installed on this PC (a name can still be typed).</summary>
    public sealed class InstalledFontConverter : StringConverter
    {
        private static StandardValuesCollection _values;

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return false; }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            if (_values == null)
            {
                using (var fonts = new InstalledFontCollection())
                    _values = new StandardValuesCollection(fonts.Families.Select(f => f.Name)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray());
            }
            return _values;
        }
    }

    /// <summary>Drop-down of the paper names.</summary>
    public sealed class PaperNameConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) { return true; }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            return new StandardValuesCollection(PdfDesign.PaperNames);
        }
    }

    // ===================== Data fields =====================

    /// <summary>
    /// Data fields in PDF Designer text and table cells, filled from one record of the imported list:
    ///   {StudentId} {NameKh} {NameEn} {Gender} {DateOfBirth} {Faculty} {Level} {Generation} {ValidUntil}, any column
    ///   header of the list ({Phone}), and {No} (record / row number), {Count} (records), {Date} (today), {Page} {Pages}.
    /// A format after a colon: {DateOfBirth:KhmerDate}, {StudentId:KhmerDigits}, {NameEn:Upper}, {Gender:KhmerGender}.
    /// A field that cannot be filled (no data chosen, a name the list does not have) stays as it was typed, so it shows.
    /// </summary>
    public sealed class PdfFieldContext
    {
        private static readonly Regex Token = new Regex(@"\{([^{}\r\n:]{1,60})(?::([A-Za-z]{1,20}))?\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Fields every student list has (also the ones loaded from the API or SQL Server, which keep no columns).</summary>
        public static readonly string[] StudentFields =
            { "StudentId", "NameKh", "NameEn", "Gender", "DateOfBirth", "Faculty", "Level", "Generation", "ValidUntil" };

        /// <summary>Fields that do not come from the list.</summary>
        public static readonly string[] SpecialFields = { "No", "Count", "Date", "Page", "Pages" };

        public static readonly string[] Formats = { "Upper", "KhmerDigits", "KhmerDate", "KhmerGender" };

        public PdfFieldContext()
        {
            Today = DateTime.Today;
            Count = -1;
        }

        /// <summary>The record the fields are read from (null = none: list fields stay as typed).</summary>
        public Student Record { get; set; }

        /// <summary>{No}: 1 for the first record (0 = unknown, stays as typed).</summary>
        public int Number { get; set; }

        /// <summary>{Count}: number of records (-1 = unknown).</summary>
        public int Count { get; set; }

        /// <summary>{Page} / {Pages} (0 = unknown).</summary>
        public int Page { get; set; }
        public int Pages { get; set; }

        public DateTime Today { get; set; }

        /// <summary>The same context for another record (row of a data table, or the next copy of the design).</summary>
        public PdfFieldContext For(Student record, int number)
        {
            var c = (PdfFieldContext)MemberwiseClone();
            c.Record = record;
            c.Number = number;
            return c;
        }

        /// <summary>True when the text has at least one {field}.</summary>
        public static bool HasFields(string text)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf('{') >= 0 && Token.IsMatch(text);
        }

        /// <summary>The text with every field that can be filled replaced by its value.</summary>
        public string Resolve(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text ?? "";
            return Token.Replace(text, m =>
            {
                string value = Value(m.Groups[1].Value.Trim());
                return value == null ? m.Value : Format(value, m.Groups[2].Value);
            });
        }

        private string Value(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "no": return Number > 0 ? Number.ToString(CultureInfo.InvariantCulture) : null;
                case "count": return Count >= 0 ? Count.ToString(CultureInfo.InvariantCulture) : null;
                case "date": return Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                case "page": return Page > 0 ? Page.ToString(CultureInfo.InvariantCulture) : null;
                case "pages": return Pages > 0 ? Pages.ToString(CultureInfo.InvariantCulture) : null;
            }
            return Record == null ? null : FieldOf(Record, name);
        }

        /// <summary>A field of a record: a student field, else a column of the list; null when it has neither.</summary>
        public static string FieldOf(Student s, string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "studentid": return s.StudentId ?? "";
                case "namekh": return s.NameKh ?? "";
                case "nameen": return s.NameEn ?? "";
                case "gender": return s.Gender ?? "";
                case "dateofbirth": return s.DateOfBirth ?? "";
                case "faculty": return s.Faculty ?? "";
                case "level": return s.Level ?? "";
                case "generation": return s.Generation ?? "";
                case "validuntil": return s.ValidUntil ?? "";
                case "qrdata": return s.QrData ?? "";
            }
            string value;
            return s.Columns.TryGetValue((name ?? "").Trim(), out value) ? value ?? "" : null;
        }

        private static string Format(string value, string format)
        {
            switch ((format ?? "").ToLowerInvariant())
            {
                case "upper": return value.ToUpperInvariant();
                case "lower": return value.ToLowerInvariant();
                case "khmerdigits": return KhmerText.Digits(value);
                case "khmerdate": return KhmerText.Date(value);
                case "khmergender": return KhmerText.Gender(value);
                default: return value;
            }
        }

        /// <summary>
        /// Field names of a list, for the Insert field menu: its column headers in file order, or the student fields
        /// that have values when the list keeps no columns (API, SQL Server).
        /// </summary>
        public static List<string> FieldsOf(IList<Student> records)
        {
            var names = new List<string>();
            if (records == null || records.Count == 0) return names;
            Student withColumns = records.FirstOrDefault(r => r.Columns.Count > 0);
            if (withColumns != null)
                names.AddRange(withColumns.Columns.Keys.Where(k => !string.IsNullOrWhiteSpace(k) && k.IndexOfAny(new[] { '{', '}', ':' }) < 0));
            else
                names.AddRange(StudentFields.Where(f => records.Any(r => !string.IsNullOrEmpty(FieldOf(r, f)))));
            return names;
        }
    }

    // ===================== Library =====================

    /// <summary>Saved PDF designs: one file per design in %AppData%\NPIC_CARD_APP\PdfDesigns\{Id}.xml (part of Backup).</summary>
    public static class PdfDesignStore
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(PdfDesign));

        /// <summary>%AppData%\NPIC_CARD_APP\PdfDesigns</summary>
        public static string Folder
        {
            get { return Path.Combine(Path.GetDirectoryName(AppSettings.FilePath), "PdfDesigns"); }
        }

        /// <summary>Every saved design, newest first. Unreadable files are skipped.</summary>
        public static List<PdfDesign> All()
        {
            var list = new List<PdfDesign>();
            if (!Directory.Exists(Folder)) return list;
            foreach (string file in Directory.GetFiles(Folder, "*.xml"))
            {
                PdfDesign d = Read(file);
                if (d != null) list.Add(d);
            }
            return list.OrderByDescending(d => d.Saved).ToList();
        }

        /// <summary>The saved design with this id, or null.</summary>
        public static PdfDesign Load(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            string file = Path.Combine(Folder, id + ".xml");
            return File.Exists(file) ? Read(file) : null;
        }

        private static PdfDesign Read(string file)
        {
            try
            {
                using (var stream = File.OpenRead(file))
                {
                    var d = (PdfDesign)Serializer.Deserialize(stream);
                    d.Id = Path.GetFileNameWithoutExtension(file);
                    d.Normalize();
                    return d;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Writes the design (a new one gets an id). The old file is replaced only once the new one is complete.</summary>
        public static void Save(PdfDesign design)
        {
            if (string.IsNullOrWhiteSpace(design.Id)) design.Id = NewId();
            design.Saved = DateTime.Now;
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, design.Id + ".xml"), temp = file + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                Serializer.Serialize(writer, design);
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        }

        public static void Delete(string id)
        {
            PdfDesign d = Load(id);
            if (d == null) return;
            File.Delete(Path.Combine(Folder, d.Id + ".xml"));
        }

        public static string NewId()
        {
            return "pdf-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                   Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// <summary>"PDF design 3": the base name with the first free number (or the base itself when free).</summary>
        public static string UniqueName(string baseName, string exceptId)
        {
            baseName = string.IsNullOrWhiteSpace(baseName) ? "PDF design" : baseName.Trim();
            var taken = new HashSet<string>(All().Where(d => d.Id != exceptId).Select(d => (d.Name ?? "").Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(baseName)) return baseName;
            for (int i = 2; ; i++)
            {
                string name = baseName + " " + i;
                if (!taken.Contains(name)) return name;
            }
        }

        /// <summary>A new, unsaved design with one page; <paramref name="starter"/> adds a title, a paragraph and a table.</summary>
        public static PdfDesign CreateNew(bool starter)
        {
            var d = new PdfDesign { Name = UniqueName(Lang.T("PDF design"), null) };
            var page = new PdfDesignPage();
            d.Pages.Add(page);
            if (starter)
            {
                page.Items.Add(new PdfTextItem
                {
                    Text = Lang.T("Title"), SizePt = 20f, Bold = true, Align = PdfTextAlign.Center, X = 20f, Y = 20f, Width = 170f
                });
                page.Items.Add(new PdfTextItem
                {
                    Text = Lang.T("Double-click to write. Drag to move. Alt+drag or Ctrl+D to clone."),
                    SizePt = 11f, X = 20f, Y = 38f, Width = 170f
                });
                PdfTableItem table = PdfTableItem.Create(4, 3);
                table.X = 20f;
                table.Y = 52f;
                table.Width = 170f;
                page.Items.Add(table);
            }
            foreach (PdfItem i in page.Items) PdfDesignRenderer.Layout(i);
            return d;
        }
    }

    // ===================== Drawing and PDF export =====================

    /// <summary>
    /// Layout and drawing of PDF Designer items. The screen and the PDF use the same code: on screen the Graphics is set
    /// to millimetres; for the PDF, fills and lines are written as vector shapes and the text of each item is drawn by
    /// GDI+ into a transparent picture (any font, Khmer included, exactly as on screen).
    /// </summary>
    public static class PdfDesignRenderer
    {
        public const float MmPerPt = 25.4f / 72f;
        public static readonly Color Ink = Color.FromArgb(33, 33, 40);
        private const float MeasurePxPerMm = 20f;   // text is measured at 20 px/mm and scaled (widths are linear without hinting)
        private const float ExportDpi = 400f;
        private const double MaxLayerPixels = 9e6;  // a full-page text layer drops to ~300 dpi to keep memory low

        private static readonly Bitmap Probe = new Bitmap(1, 1);

        // ---------- Fonts ----------

        private static float SafeSize(float pt) { return float.IsNaN(pt) ? 10f : Math.Max(2f, Math.Min(400f, pt)); }

        public static FontStyle Style(bool bold, bool italic, bool underline)
        {
            FontStyle s = FontStyle.Regular;
            if (bold) s |= FontStyle.Bold;
            if (italic) s |= FontStyle.Italic;
            if (underline) s |= FontStyle.Underline;
            return s;
        }

        /// <summary>The font, or the closest one that works (style the family lacks, font not installed).</summary>
        public static Font MakeFont(string family, float size, FontStyle style, GraphicsUnit unit)
        {
            string name = !string.IsNullOrWhiteSpace(family) && CardFonts.IsInstalled(family.Trim()) ? family.Trim() : CardFonts.UI;
            try
            {
                FontStyle use = style;
                using (var fam = new FontFamily(name))
                {
                    if (!fam.IsStyleAvailable(use))
                    {
                        FontStyle underline = style & FontStyle.Underline;
                        FontStyle[] tries = { style & ~FontStyle.Italic, style & ~FontStyle.Bold, underline, FontStyle.Bold | underline };
                        use = tries.FirstOrDefault(fam.IsStyleAvailable);
                        if (!fam.IsStyleAvailable(use)) use = FontStyle.Regular;
                    }
                }
                return new Font(name, size, use, unit);
            }
            catch (ArgumentException)
            {
                return new Font("Segoe UI", size, style, unit);
            }
        }

        private static StringFormat Format(PdfTextAlign align, bool middle)
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.Alignment = align == PdfTextAlign.Center ? StringAlignment.Center
                        : align == PdfTextAlign.Right ? StringAlignment.Far : StringAlignment.Near;
            f.LineAlignment = middle ? StringAlignment.Center : StringAlignment.Near;
            f.Trimming = StringTrimming.None;
            // GenericTypographic has LineLimit, which drops a line that does not fit the box entirely. Line heights
            // differ by a hair between the measuring size and the drawing size (screen zoom, 400 dpi export), so the
            // last line could vanish: draw it even when it pokes out a fraction of a millimetre.
            f.FormatFlags &= ~StringFormatFlags.LineLimit;
            return f;
        }

        /// <summary>Height in mm of <paramref name="text"/> wrapped to <paramref name="widthMm"/> (one line at least).</summary>
        public static float MeasureHeight(string text, string family, float sizePt, FontStyle style, float widthMm)
        {
            float k = MeasurePxPerMm;
            string t = string.IsNullOrEmpty(text) ? " " : text.EndsWith("\n", StringComparison.Ordinal) ? text + " " : text;
            using (Font f = MakeFont(family, SafeSize(sizePt) * MmPerPt * k, style, GraphicsUnit.Pixel))
            using (StringFormat sf = Format(PdfTextAlign.Left, false))
            using (Graphics g = Graphics.FromImage(Probe))
            {
                g.PageUnit = GraphicsUnit.Pixel;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                SizeF s = g.MeasureString(t, f, new SizeF(Math.Max(1f, widthMm * k), 1e6f), sf);
                return Math.Max(s.Height, f.GetHeight(g)) / k;
            }
        }

        /// <summary>Text in a box of a Graphics set to millimetres. The box gets a hair more width than was measured,
        /// so the drawing never breaks a line that the measurement kept.</summary>
        private static void DrawString(Graphics g, string text, string family, float sizePt, FontStyle style, Color color,
            RectangleF box, PdfTextAlign align, bool middle)
        {
            if (string.IsNullOrEmpty(text) || box.Width <= 0f) return;
            float extra = box.Width * 0.004f + 0.05f;
            if (align == PdfTextAlign.Left) box.Width += extra;
            else if (align == PdfTextAlign.Center) { box.X -= extra / 2f; box.Width += extra; }
            else { box.X -= extra; box.Width += extra; }
            using (Font f = MakeFont(family, SafeSize(sizePt) * MmPerPt, style, GraphicsUnit.World))
            using (var brush = new SolidBrush(Opaque(color, Ink)))
            using (StringFormat sf = Format(align, middle))
            {
                TextRenderingHint old = g.TextRenderingHint;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.DrawString(text, f, brush, box, sf);
                g.TextRenderingHint = old;
            }
        }

        private static Color Opaque(Color c, Color fallback)
        {
            return PdfItem.Visible(c) ? Color.FromArgb(255, c) : fallback;
        }

        // ---------- Layout ----------

        /// <summary>The text as it is shown: fields filled from <paramref name="fields"/> (null = as typed).</summary>
        private static string Shown(string text, PdfFieldContext fields)
        {
            return fields == null ? text : fields.Resolve(text);
        }

        /// <summary>
        /// Sets the height of a text box or table from its content (pictures keep theirs). With <paramref name="fields"/>
        /// the height follows the filled-in values, as they are drawn.
        /// </summary>
        public static void Layout(PdfItem item, PdfFieldContext fields = null)
        {
            item.Width = Math.Max(PdfItem.MinSizeMm, float.IsNaN(item.Width) ? 50f : item.Width);
            var text = item as PdfTextItem;
            if (text != null)
            {
                float pad = Math.Max(0f, text.PaddingMm);
                text.Height = MeasureHeight(Shown(text.Text, fields), text.FontName, text.SizePt, TextStyle(text),
                    Math.Max(1f, text.Width - 2f * pad)) + 2f * pad;
                return;
            }
            var table = item as PdfTableItem;
            if (table != null)
            {
                table.Normalize();
                float[] xs = ColumnEdges(table);
                float pad = Math.Max(0f, table.PaddingMm);
                var heights = new float[table.Rows.Count];
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    float h = Math.Max(2f, table.MinRowMm);
                    for (int c = 0; c < table.ColumnCount; c++)
                    {
                        string cell = Shown(table.Rows[r].Cells[c], fields);
                        if (string.IsNullOrEmpty(cell)) continue;
                        float w = Math.Max(1f, xs[c + 1] - xs[c] - 2f * pad);
                        h = Math.Max(h, MeasureHeight(cell, table.FontName, table.SizePt, CellStyle(table, r), w) + 2f * pad);
                    }
                    heights[r] = h;
                }
                table.RowHeights = heights;
                table.Height = heights.Sum();
                return;
            }
            item.Height = Math.Max(PdfItem.MinSizeMm, float.IsNaN(item.Height) ? 40f : item.Height);
        }

        public static void LayoutAll(PdfDesign design, PdfFieldContext fields = null)
        {
            foreach (PdfDesignPage p in design.Pages)
                foreach (PdfItem i in p.Items) Layout(i, fields);
        }

        private static FontStyle TextStyle(PdfTextItem t) { return Style(t.Bold, t.Italic, t.Underline); }

        private static bool IsHeader(PdfTableItem t, int row) { return t.HeaderRow && row == 0; }

        private static FontStyle CellStyle(PdfTableItem t, int row)
        {
            return IsHeader(t, row) && t.HeaderBold ? FontStyle.Bold : FontStyle.Regular;
        }

        /// <summary>X of every column line (left edge … right edge), in mm on the page.</summary>
        public static float[] ColumnEdges(PdfTableItem t)
        {
            int n = Math.Max(1, t.ColumnCount);
            float total = t.ColumnWidths == null ? 0f : t.ColumnWidths.Sum();
            var xs = new float[n + 1];
            xs[0] = t.X;
            for (int c = 0; c < n; c++)
            {
                float share = total > 0f && t.ColumnWidths != null && c < t.ColumnWidths.Count ? t.ColumnWidths[c] / total : 1f / n;
                xs[c + 1] = xs[c] + share * t.Width;
            }
            xs[n] = t.X + t.Width;
            return xs;
        }

        /// <summary>Y of every row line (top … bottom), in mm on the page.</summary>
        public static float[] RowEdges(PdfTableItem t)
        {
            if (t.RowHeights == null || t.RowHeights.Length != t.Rows.Count) Layout(t);
            var ys = new float[t.Rows.Count + 1];
            ys[0] = t.Y;
            for (int r = 0; r < t.Rows.Count; r++) ys[r + 1] = ys[r] + t.RowHeights[r];
            return ys;
        }

        /// <summary>Row and column of the cell under <paramref name="mm"/>, or false when outside the table.</summary>
        public static bool CellAt(PdfTableItem t, PointF mm, out int row, out int column)
        {
            row = column = -1;
            if (!t.Bounds.Contains(mm)) return false;
            float[] xs = ColumnEdges(t), ys = RowEdges(t);
            for (int c = 0; c < xs.Length - 1; c++) if (mm.X < xs[c + 1] || c == xs.Length - 2) { column = c; break; }
            for (int r = 0; r < ys.Length - 1; r++) if (mm.Y < ys[r + 1] || r == ys.Length - 2) { row = r; break; }
            return row >= 0 && column >= 0;
        }

        // ---------- Screen ----------

        /// <summary>
        /// Draws every item of a page on a Graphics set to millimetres (origin = top-left of the paper), with fields
        /// filled from <paramref name="fields"/> (null = as typed).
        /// </summary>
        public static void DrawPage(Graphics g, PdfDesignPage page, PdfItem skip = null, PdfFieldContext fields = null)
        {
            foreach (PdfItem item in page.Items)
                if (item != skip) DrawItem(g, item, fields);
        }

        public static void DrawItem(Graphics g, PdfItem item, PdfFieldContext fields = null)
        {
            var text = item as PdfTextItem;
            if (text != null)
            {
                if (PdfItem.Visible(text.Fill))
                    using (var b = new SolidBrush(Opaque(text.Fill, Color.White))) g.FillRectangle(b, text.Bounds);
                DrawTextLayer(g, text, fields);
                if (PdfItem.Visible(text.Border) && text.BorderPt > 0f)
                    using (var pen = new Pen(Opaque(text.Border, Ink), text.BorderPt * MmPerPt))
                        g.DrawRectangle(pen, text.X, text.Y, text.Width, text.Height);
                return;
            }
            var table = item as PdfTableItem;
            if (table != null)
            {
                foreach (KeyValuePair<RectangleF, Color> fill in TableFills(table))
                    using (var b = new SolidBrush(fill.Value)) g.FillRectangle(b, fill.Key);
                DrawTableText(g, table, fields);
                if (PdfItem.Visible(table.BorderColor) && table.BorderPt > 0f)
                    using (var pen = new Pen(Opaque(table.BorderColor, Ink), table.BorderPt * MmPerPt))
                        foreach (RectangleF line in TableLines(table))
                            g.DrawLine(pen, line.Left, line.Top, line.Right, line.Bottom);
                return;
            }
            var image = item as PdfImageItem;
            if (image != null)
            {
                Image pic = image.Picture;
                if (pic == null) return;
                InterpolationMode old = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(pic, image.Bounds);
                g.InterpolationMode = old;
            }
        }

        private static void DrawTextLayer(Graphics g, PdfTextItem t, PdfFieldContext fields = null)
        {
            float pad = Math.Max(0f, t.PaddingMm);
            var box = new RectangleF(t.X + pad, t.Y + pad, Math.Max(1f, t.Width - 2f * pad), Math.Max(1f, t.Height - 2f * pad));
            DrawString(g, Shown(t.Text, fields), t.FontName, t.SizePt, TextStyle(t), t.Color, box, t.Align, false);
        }

        private static void DrawTableText(Graphics g, PdfTableItem t, PdfFieldContext fields = null)
        {
            float[] xs = ColumnEdges(t), ys = RowEdges(t);
            float pad = Math.Max(0f, t.PaddingMm);
            for (int r = 0; r < t.Rows.Count; r++)
                for (int c = 0; c < t.ColumnCount; c++)
                {
                    string cell = Shown(t.Rows[r].Cells[c], fields);
                    if (string.IsNullOrEmpty(cell)) continue;
                    var box = new RectangleF(xs[c] + pad, ys[r] + pad, Math.Max(1f, xs[c + 1] - xs[c] - 2f * pad),
                        Math.Max(1f, ys[r + 1] - ys[r] - 2f * pad));
                    DrawString(g, cell, t.FontName, t.SizePt, CellStyle(t, r), t.TextColor, box,
                        IsHeader(t, r) ? t.HeaderAlign : t.Align, true);
                }
        }

        /// <summary>Header shading and stripes.</summary>
        private static List<KeyValuePair<RectangleF, Color>> TableFills(PdfTableItem t)
        {
            var fills = new List<KeyValuePair<RectangleF, Color>>();
            float[] ys = RowEdges(t);
            int first = t.HeaderRow ? 1 : 0;
            if (t.HeaderRow && t.Rows.Count > 0 && PdfItem.Visible(t.HeaderFill))
                fills.Add(new KeyValuePair<RectangleF, Color>(new RectangleF(t.X, ys[0], t.Width, ys[1] - ys[0]), Opaque(t.HeaderFill, Color.White)));
            if (t.Stripes && PdfItem.Visible(t.StripeFill))
                for (int r = first + 1; r < t.Rows.Count; r += 2)
                    fills.Add(new KeyValuePair<RectangleF, Color>(new RectangleF(t.X, ys[r], t.Width, ys[r + 1] - ys[r]), Opaque(t.StripeFill, Color.White)));
            return fills;
        }

        /// <summary>Every cell border as a line (a RectangleF from its start to its end point).</summary>
        private static List<RectangleF> TableLines(PdfTableItem t)
        {
            var lines = new List<RectangleF>();
            float[] xs = ColumnEdges(t), ys = RowEdges(t);
            float top = ys[0], bottom = ys[ys.Length - 1];
            foreach (float y in ys) lines.Add(RectangleF.FromLTRB(t.X, y, t.X + t.Width, y));
            foreach (float x in xs) lines.Add(RectangleF.FromLTRB(x, top, x, bottom));
            return lines;
        }

        // ---------- PDF ----------

        /// <summary>
        /// Writes the whole design as a PDF file, with its fields filled from <paramref name="records"/> (see
        /// <see cref="BuildPages"/>). Returns the number of pages written.
        /// </summary>
        public static int Export(PdfDesign design, string path, IList<Student> records = null)
        {
            List<List<PdfItem>> pages = BuildPages(design, records);
            SizeF page = design.PageMm;
            using (var pdf = new PdfWriter(path, design.Name))
            {
                var pictures = new Dictionary<byte[], int>();   // the same picture (clones share the bytes) is stored once
                foreach (List<PdfItem> items in pages)
                {
                    pdf.BeginPage(page.Width, page.Height);
                    foreach (PdfItem item in items) WriteItem(pdf, item, pictures);
                    pdf.EndPage();
                }
            }
            return pages.Count;
        }

        /// <summary>
        /// The pages to print, as copies of the design's items with every field filled in and laid out:
        ///   - normally the design once; its fields come from the first record, {Count} is the number of records;
        ///   - with <see cref="PdfDesign.OnePerRecord"/>, the design once for each record ({No} = 1, 2, 3…);
        ///   - a table with <see cref="PdfTableItem.DataRows"/> gets its template row once per record; when the list is
        ///     longer than the page it continues on new pages (header repeated), and the objects under it move down;
        ///     objects inside the top or bottom margin (page numbers, running titles) stay put and repeat on the new pages;
        ///   - {Page} / {Pages} count the pages of the copy they are on.
        /// </summary>
        public static List<List<PdfItem>> BuildPages(PdfDesign design, IList<Student> records)
        {
            design.Normalize();
            records = records ?? new Student[0];
            var output = new List<List<PdfItem>>();
            if (design.OnePerRecord && records.Count > 0)
            {
                var each = new PdfFieldContext { Count = records.Count };
                for (int i = 0; i < records.Count; i++) AddCopy(output, design, records, each.For(records[i], i + 1));
            }
            else
            {
                var once = new PdfFieldContext { Record = records.Count > 0 ? records[0] : null, Count = records.Count > 0 ? records.Count : -1 };
                AddCopy(output, design, records, once);
            }
            return output;
        }

        private static void AddCopy(List<List<PdfItem>> output, PdfDesign design, IList<Student> records, PdfFieldContext fields)
        {
            var copy = new List<List<PdfItem>>();
            foreach (PdfDesignPage p in design.Pages) copy.AddRange(ExpandPage(p, design.PageMm, design.Margin, records, fields));

            // Page numbers are known only now
            var numbers = new PdfFieldContext { Pages = copy.Count };
            for (int i = 0; i < copy.Count; i++)
            {
                numbers.Page = i + 1;
                foreach (PdfItem item in copy[i])
                    if (FillFields(item, numbers)) Layout(item);
            }
            output.AddRange(copy);
        }

        /// <summary>Puts the values of <paramref name="fields"/> into the item's text. True when something changed.</summary>
        private static bool FillFields(PdfItem item, PdfFieldContext fields)
        {
            bool changed = false;
            var text = item as PdfTextItem;
            if (text != null)
            {
                string v = fields.Resolve(text.Text);
                changed = v != (text.Text ?? "");
                text.Text = v;
            }
            var table = item as PdfTableItem;
            if (table != null)
                foreach (PdfTableRow row in table.Rows)
                    for (int c = 0; c < row.Cells.Count; c++)
                    {
                        string v = fields.Resolve(row.Cells[c]);
                        if (v != (row.Cells[c] ?? "")) changed = true;
                        row.Cells[c] = v;
                    }
            return changed;
        }

        private static PdfTableRow FilledRow(PdfTableRow row, PdfFieldContext fields)
        {
            return new PdfTableRow { Cells = row.Cells.Select(fields.Resolve).ToList() };
        }

        /// <summary>One page of the design with its fields filled in: one page, or more when a data table runs over.</summary>
        private static List<List<PdfItem>> ExpandPage(PdfDesignPage page, SizeF size, float margin, IList<Student> records, PdfFieldContext fields)
        {
            var items = new List<PdfItem>();
            PdfTableItem flow = null;      // the top data table: it continues on new pages
            float flowDesignBottom = 0f;   // its bottom as designed (with one template row)
            foreach (PdfItem source in page.Items)
            {
                PdfItem item = source.Clone();
                var table = item as PdfTableItem;
                int template = table == null ? -1 : table.TemplateRow;
                FillFields(item, fields);
                Layout(item);
                if (template >= 0)
                {
                    float designBottom = table.Y + table.Height;
                    var src = (PdfTableItem)source;
                    var rows = new List<PdfTableRow>();
                    for (int r = 0; r < template; r++) rows.Add(FilledRow(src.Rows[r], fields));
                    if (records.Count == 0) rows.Add(FilledRow(src.Rows[template], fields));
                    for (int i = 0; i < records.Count; i++) rows.Add(FilledRow(src.Rows[template], fields.For(records[i], i + 1)));
                    for (int r = template + 1; r < src.Rows.Count; r++) rows.Add(FilledRow(src.Rows[r], fields));
                    table.Rows = rows;
                    Layout(table);
                    if (flow == null || table.Y < flow.Y)
                    {
                        flow = table;
                        flowDesignBottom = designBottom;
                    }
                }
                items.Add(item);
            }
            if (flow == null) return new List<List<PdfItem>> { items };

            float pageH = size.Height;
            float limit = Math.Min(pageH, Math.Max(pageH - margin, flowDesignBottom));   // the table stops here
            float nextTop = Math.Min(flow.Y, margin);                                        // and goes on from here
            int head = flow.HeaderRow ? 1 : 0;                                               // repeated on every page
            // Objects in the top or bottom margin (page number, running title) stay put and repeat on every new page
            var furniture = margin > 0f
                ? items.Where(i => i != flow && (i.Y >= pageH - margin - 0.01f || i.Y + i.Height <= margin + 0.01f)).ToList()
                : new List<PdfItem>();
            var below = items.Where(i => i != flow && !furniture.Contains(i) && i.Y >= flowDesignBottom - 0.01f).ToList();
            var current = items.Where(i => !below.Contains(i)).ToList();
            int flowIndex = current.IndexOf(flow);
            current.RemoveAt(flowIndex);
            Func<List<PdfItem>> newPage = () => furniture.Select(i => i.Clone()).ToList();

            var pages = new List<List<PdfItem>>();
            float[] heights = flow.RowHeights;
            int next = 0;
            float top = flow.Y, bottom = top;
            bool first = true;
            while (true)
            {
                var rows = new List<PdfTableRow>();
                float y = top;
                if (!first)
                    for (int h = 0; h < head; h++) { rows.Add(flow.Rows[h].Clone()); y += heights[h]; }
                int body = 0;   // rows other than the header: at least one per page, so the list always moves on
                while (next < flow.Rows.Count)
                {
                    if (y + heights[next] > limit + 0.01f && body > 0) break;
                    rows.Add(flow.Rows[next].Clone());
                    y += heights[next];
                    if (next >= head) body++;
                    next++;
                }
                var chunk = (PdfTableItem)flow.Clone();
                chunk.Rows = rows;
                chunk.Y = top;
                Layout(chunk);
                if (first) current.Insert(flowIndex, chunk);
                else current.Add(chunk);
                bottom = chunk.Y + chunk.Height;
                if (next >= flow.Rows.Count) break;
                pages.Add(current);
                current = newPage();
                top = nextTop;
                first = false;
            }

            if (below.Count > 0)
            {
                // What was under the table stays under it; when it no longer fits, it goes to a new page
                float shift = bottom - flowDesignBottom;
                float lowest = below.Max(i => i.Y + i.Height);
                if (shift > 0f && lowest + shift > limit + 0.01f && lowest <= limit + 0.01f)
                {
                    pages.Add(current);
                    current = newPage();
                    shift = nextTop - below.Min(i => i.Y);
                }
                foreach (PdfItem i in below)
                {
                    i.Y += shift;
                    current.Add(i);
                }
            }
            pages.Add(current);
            return pages;
        }

        private static void WriteItem(PdfWriter pdf, PdfItem item, Dictionary<byte[], int> pictures)
        {
            var text = item as PdfTextItem;
            if (text != null)
            {
                if (PdfItem.Visible(text.Fill)) pdf.FillRectangle(text.Bounds, Opaque(text.Fill, Color.White));
                if (!string.IsNullOrWhiteSpace(text.Text)) WriteLayer(pdf, text.Bounds, g => DrawTextLayer(g, text));
                if (PdfItem.Visible(text.Border) && text.BorderPt > 0f) pdf.DrawRectangle(text.Bounds, text.BorderPt, Opaque(text.Border, Ink));
                return;
            }
            var table = item as PdfTableItem;
            if (table != null)
            {
                foreach (KeyValuePair<RectangleF, Color> fill in TableFills(table)) pdf.FillRectangle(fill.Key, fill.Value);
                if (table.Rows.Any(r => r.Cells.Any(c => !string.IsNullOrWhiteSpace(c))))
                    WriteLayer(pdf, table.Bounds, g => DrawTableText(g, table));
                if (PdfItem.Visible(table.BorderColor) && table.BorderPt > 0f)
                    foreach (RectangleF line in TableLines(table))
                        pdf.DrawLine(line.Left, line.Top, line.Right, line.Bottom, table.BorderPt, Opaque(table.BorderColor, Ink));
                return;
            }
            var image = item as PdfImageItem;
            if (image != null && image.Picture != null)
            {
                int id;
                if (!pictures.TryGetValue(image.Data, out id))
                {
                    Image pic = image.Picture;
                    if (Image.IsAlphaPixelFormat(pic.PixelFormat))
                        using (var bmp = new Bitmap(pic)) id = pdf.AddAlphaImage(bmp);
                    else id = pdf.AddImage(pic);
                    pictures[image.Data] = id;
                }
                pdf.DrawImage(id, image.Bounds);
            }
        }

        /// <summary>Draws <paramref name="draw"/> (in page millimetres) into a transparent picture over <paramref name="box"/>.</summary>
        private static void WriteLayer(PdfWriter pdf, RectangleF box, Action<Graphics> draw)
        {
            // Text can reach a little outside its box (italic overhang, tall Khmer marks)
            box.Inflate(1.5f, 1.5f);
            float dpi = ExportDpi;
            double pixels = box.Width * box.Height * Math.Pow(dpi / 25.4, 2);
            if (pixels > MaxLayerPixels) dpi = (float)(dpi * Math.Sqrt(MaxLayerPixels / pixels));
            float ppm = dpi / 25.4f;
            int w = Math.Max(1, (int)Math.Ceiling(box.Width * ppm)), h = Math.Max(1, (int)Math.Ceiling(box.Height * ppm));
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.PageUnit = GraphicsUnit.Pixel;
                    g.ScaleTransform(ppm, ppm);
                    g.TranslateTransform(-box.X, -box.Y);
                    draw(g);
                }
                int id = pdf.AddAlphaImage(bmp);
                pdf.DrawImage(id, new RectangleF(box.X, box.Y, w / ppm, h / ppm));
            }
        }
    }
}
