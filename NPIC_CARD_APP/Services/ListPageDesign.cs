using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Xml.Serialization;
using NPIC_CARD_APP.Models;
using NPIC_CARD_APP.Rendering;

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// Page Design of the Student list report (Preview &amp; PDF → Student list → Page Design…): paper, margins, fonts and
    /// the text above and below the table. Saved in settings.xml; the defaults give the standard NPIC list:
    /// A4 portrait, 0.5" margins, Times New Roman + Khmer OS Siemreap, the national motto and the title.
    /// Placeholders in the texts: [total], [total male], [total female], [name], [date].
    /// </summary>
    public class ListPageDesign
    {
        public const string DefaultHeading =
            "ព្រះរាជាណាចក្រកម្ពុជា\n" +
            "ជាតិ សាសនា ព្រះមហាក្សត្រ\n" +
            "\n" +
            "បញ្ជីឈ្មោះនិស្សិតធ្វើកាតសម្គាល់ខ្លួន\n" +
            "និស្សិតសរុប [total] ប្រុស [total male] ស្រី [total female]";

        public const string DefaultFooter = "រៀបចំដោយលោក [name]";

        /// <summary>Paper names and sizes in millimetres (portrait).</summary>
        public static readonly string[] PaperNames = { "A4", "A5", "Letter", "Legal" };

        public const float MinMarginIn = 0f, MaxMarginIn = 2f, MinSizePt = 6f, MaxSizePt = 28f;

        /// <summary>"builtin:…" for the designs that come with the app, "my:…" for saved ones (see <see cref="ListDesigns"/>).</summary>
        [XmlAttribute]
        public string Id { get; set; }

        /// <summary>Name shown in the Design list (built-in names are translated when shown).</summary>
        public string Name { get; set; }

        /// <summary>Comes with the app: cannot be changed or deleted, only copied.</summary>
        [XmlIgnore]
        public bool BuiltIn { get { return (Id ?? "").StartsWith(ListDesigns.BuiltInPrefix, StringComparison.Ordinal); } }

        /// <summary>Name in the app's language.</summary>
        [XmlIgnore]
        public string DisplayName { get { return BuiltIn ? Lang.T(Name) : (Name ?? ""); } }

        public string Paper { get; set; }
        public bool Landscape { get; set; }

        /// <summary>Margins in inches.</summary>
        public float MarginTop { get; set; }
        public float MarginLeft { get; set; }
        public float MarginRight { get; set; }
        public float MarginBottom { get; set; }

        /// <summary>Font for Latin letters and digits.</summary>
        public string LatinFont { get; set; }

        /// <summary>Font for complex script (Khmer).</summary>
        public string ComplexFont { get; set; }

        /// <summary>Table, totals line and footer (pt).</summary>
        public float BodySize { get; set; }

        /// <summary>Heading lines without a placeholder, printed bold (pt).</summary>
        public float HeadingSize { get; set; }

        /// <summary>Lines above the table, centred on page 1. A blank line adds a little space.</summary>
        public string Heading { get; set; }

        /// <summary>Lines under the last row, on the right.</summary>
        public string Footer { get; set; }

        /// <summary>Print the heading at the top of every page, not only page 1.</summary>
        public bool HeadingEveryPage { get; set; }

        /// <summary>Grey background behind the table's header row.</summary>
        public bool ShadeHeader { get; set; }

        /// <summary>
        /// The table's columns, left to right. Null or empty = the standard columns (<see cref="ListFields.DefaultColumns"/>).
        /// Not filled in by the constructor: XmlSerializer adds to an existing list instead of replacing it.
        /// </summary>
        [XmlArrayItem("Column")]
        public List<ListColumnDesign> Columns { get; set; }

        /// <summary>The columns to print: <see cref="Columns"/>, or the standard ones (new copies).</summary>
        [XmlIgnore]
        public List<ListColumnDesign> TableColumns
        {
            get
            {
                List<ListColumnDesign> cols = (Columns ?? new List<ListColumnDesign>()).Where(c => c != null).ToList();
                return cols.Count > 0 ? cols.Select(c => c.Clone()).ToList() : ListFields.DefaultColumns();
            }
        }

        public ListPageDesign()
        {
            ShadeHeader = true;
            Id = ListDesigns.DefaultId;
            Name = "NPIC standard (A4)";
            Paper = "A4";
            Landscape = false;
            MarginTop = MarginLeft = MarginRight = MarginBottom = 0.5f;
            LatinFont = CardFonts.TimesNewRoman;
            ComplexFont = CardFonts.KhmerOSSiemreap;
            BodySize = 10f;
            HeadingSize = 12f;
            Heading = DefaultHeading;
            Footer = DefaultFooter;
        }

        /// <summary>Copy that shares nothing with this design (the column list is copied too).</summary>
        public ListPageDesign Clone()
        {
            var copy = (ListPageDesign)MemberwiseClone();
            copy.Columns = Columns == null ? null : Columns.Where(c => c != null).Select(c => c.Clone()).ToList();
            return copy;
        }

        // ===================== Resolved values (out-of-range or missing values fall back to the defaults) =====================

        /// <summary>Page size in millimetres, turned for landscape.</summary>
        [XmlIgnore]
        public SizeF PageMm
        {
            get
            {
                SizeF size;
                switch ((Paper ?? "").Trim().ToUpperInvariant())
                {
                    case "A5": size = new SizeF(148f, 210f); break;
                    case "LETTER": size = new SizeF(215.9f, 279.4f); break;
                    case "LEGAL": size = new SizeF(215.9f, 355.6f); break;
                    default: size = new SizeF(210f, 297f); break;
                }
                return Landscape ? new SizeF(size.Height, size.Width) : size;
            }
        }

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

        /// <summary>Margins in millimetres: left, top, right, bottom.</summary>
        [XmlIgnore]
        public float[] MarginsMm
        {
            get
            {
                return new[] { MarginLeft, MarginTop, MarginRight, MarginBottom }
                    .Select(v => Clamp(v, MinMarginIn, MaxMarginIn, 0.5f) * 25.4f).ToArray();
            }
        }

        [XmlIgnore]
        public string LatinFamily { get { return Installed(LatinFont, CardFonts.Serif); } }

        [XmlIgnore]
        public string ComplexFamily { get { return Installed(ComplexFont, CardFonts.KhmerSiemreap); } }

        [XmlIgnore]
        public float BodyPt { get { return Clamp(BodySize, MinSizePt, MaxSizePt, 10f); } }

        [XmlIgnore]
        public float HeadingPt { get { return Clamp(HeadingSize, MinSizePt, MaxSizePt, 12f); } }

        /// <summary>The heading split into lines (saved XML may hold "\r\n" or "\n").</summary>
        public static List<string> Lines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToList();
        }

        /// <summary>True when the line has a placeholder (such lines are printed at the body size, not bold).</summary>
        public static bool HasPlaceholder(string line)
        {
            return line.IndexOf("[total", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("[name]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("[date]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Puts the numbers, the preparer's name and today's date in place of the placeholders.</summary>
        public static string Fill(string line, int total, int male, int female, string name)
        {
            string n = string.IsNullOrWhiteSpace(name) ? "........................................" : name.Trim();
            return Replace(Replace(Replace(Replace(Replace(line,
                "[total male]", male.ToString(CultureInfo.InvariantCulture)),
                "[total female]", female.ToString(CultureInfo.InvariantCulture)),
                "[total]", total.ToString(CultureInfo.InvariantCulture)),
                "[name]", n),
                "[date]", DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        }

        private static string Replace(string text, string key, string value)
        {
            int i, from = 0;   // scans forward, so a value that contains the key is not replaced again
            while ((i = text.IndexOf(key, from, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                text = text.Substring(0, i) + value + text.Substring(i + key.Length);
                from = i + value.Length;
            }
            return text;
        }

        private static float Clamp(float v, float min, float max, float fallback)
        {
            if (float.IsNaN(v) || v < min || v > max) return fallback;
            return v;
        }

        private static string Installed(string family, string fallback)
        {
            return !string.IsNullOrWhiteSpace(family) && CardFonts.IsInstalled(family.Trim()) ? family.Trim() : fallback;
        }
    }

    /// <summary>
    /// The Student list designs, like Card Template for the cards: built-in designs that come with the app (copied to change
    /// them) and "My designs" saved in settings.xml (<see cref="AppSettings.ListDesigns"/>). The one in use is
    /// <see cref="AppSettings.ListDesignId"/>.
    /// </summary>
    public static class ListDesigns
    {
        public const string BuiltInPrefix = "builtin:", MyPrefix = "my:";
        public const string DefaultId = BuiltInPrefix + "standard";

        /// <summary>The designs that come with the app (new copies each call).</summary>
        public static List<ListPageDesign> BuiltIns()
        {
            var standard = new ListPageDesign();

            ListPageDesign landscape = standard.Clone();
            landscape.Id = BuiltInPrefix + "landscape";
            landscape.Name = "A4 landscape";
            landscape.Landscape = true;

            ListPageDesign compact = standard.Clone();
            compact.Id = BuiltInPrefix + "compact";
            compact.Name = "Compact (A4, more rows)";
            compact.MarginTop = compact.MarginLeft = compact.MarginRight = compact.MarginBottom = 0.4f;
            compact.BodySize = 9f;
            compact.HeadingSize = 11f;

            ListPageDesign table = standard.Clone();
            table.Id = BuiltInPrefix + "table";
            table.Name = "Table only (A4)";
            table.Heading = "";
            table.Footer = "";

            ListPageDesign signature = standard.Clone();
            signature.Id = BuiltInPrefix + "signature";
            signature.Name = "Signature sheet (A4)";
            signature.Columns = new List<ListColumnDesign>
            {
                new ListColumnDesign(ListFields.No, "ល.រ", 9f, ListAlign.Center),
                new ListColumnDesign(ListFields.StudentId, "អត្តលេខ", 24f, ListAlign.Center),
                new ListColumnDesign(ListFields.NameKh, "គោត្តនាម-នាម", 40f, ListAlign.Left),
                new ListColumnDesign(ListFields.NameEnUpper, "អក្សរឡាតាំង", 42f, ListAlign.Left),
                new ListColumnDesign(ListFields.Gender, "ភេទ", 12f, ListAlign.Center),
                new ListColumnDesign(ListFields.Empty, "ហត្ថលេខា", 45f, ListAlign.Center),
            };

            return new List<ListPageDesign> { standard, landscape, compact, table, signature };
        }

        /// <summary>Built-in designs first, then My designs.</summary>
        public static List<ListPageDesign> All(AppSettings settings)
        {
            return BuiltIns().Concat(Mine(settings)).ToList();
        }

        /// <summary>My designs (never null; designs without an id get one).</summary>
        public static List<ListPageDesign> Mine(AppSettings settings)
        {
            if (settings.ListDesigns == null) settings.ListDesigns = new List<ListPageDesign>();
            settings.ListDesigns.RemoveAll(d => d == null);
            foreach (ListPageDesign d in settings.ListDesigns)
                if (string.IsNullOrEmpty(d.Id) || d.BuiltIn) d.Id = NewId();
            return settings.ListDesigns;
        }

        /// <summary>The design in use: <see cref="AppSettings.ListDesignId"/>, or the standard design when it is gone.</summary>
        public static ListPageDesign Current(AppSettings settings)
        {
            return Find(settings, settings.ListDesignId) ?? BuiltIns()[0];
        }

        public static ListPageDesign Find(AppSettings settings, string id)
        {
            return All(settings).FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));
        }

        /// <summary>A copy of <paramref name="source"/> ready to save as a new design ("… (copy)", new id).</summary>
        public static ListPageDesign CopyOf(AppSettings settings, ListPageDesign source)
        {
            ListPageDesign copy = source.Clone();
            copy.Id = NewId();
            copy.Name = UniqueName(settings, source.DisplayName + " " + Lang.T("(copy)"), null);
            return copy;
        }

        /// <summary>Adds a new design or replaces the saved one with the same id, and makes it the design in use.</summary>
        public static void Save(AppSettings settings, ListPageDesign design)
        {
            if (design.BuiltIn) throw new InvalidOperationException("Built-in designs cannot be changed.");
            List<ListPageDesign> mine = Mine(settings);
            int at = mine.FindIndex(d => d.Id == design.Id);
            if (at >= 0) mine[at] = design;
            else mine.Add(design);
            settings.ListDesignId = design.Id;
        }

        /// <summary>Deletes a saved design; the standard design is used when it was the one in use.</summary>
        public static void Delete(AppSettings settings, string id)
        {
            Mine(settings).RemoveAll(d => d.Id == id);
            if (settings.ListDesignId == id) settings.ListDesignId = DefaultId;
        }

        /// <summary><paramref name="wanted"/>, or "wanted 2", "wanted 3"… when another design (not <paramref name="exceptId"/>) has it.</summary>
        public static string UniqueName(AppSettings settings, string wanted, string exceptId)
        {
            wanted = string.IsNullOrWhiteSpace(wanted) ? Lang.T("My design") : wanted.Trim();
            var taken = new HashSet<string>(All(settings).Where(d => d.Id != exceptId).Select(d => d.DisplayName),
                StringComparer.CurrentCultureIgnoreCase);
            string name = wanted;
            for (int i = 2; taken.Contains(name); i++) name = wanted + " " + i.ToString(CultureInfo.InvariantCulture);
            return name;
        }

        private static string NewId()
        {
            return MyPrefix + Guid.NewGuid().ToString("N").Substring(0, 12);
        }
    }

    public enum ListAlign
    {
        Left,
        Center,
        Right
    }

    /// <summary>One column of the Student list table: what it shows, its header, relative width and alignment.</summary>
    public class ListColumnDesign
    {
        /// <summary>A <see cref="ListFields"/> id ("NameKh", "Empty"…).</summary>
        [XmlAttribute]
        public string Field { get; set; }

        /// <summary>
        /// Relative width: the widths of all columns share out the width inside the margins
        /// (a column with 20 is twice as wide as one with 10), so a design fits every paper size.
        /// </summary>
        [XmlAttribute]
        public float Width { get; set; }

        [XmlAttribute]
        public ListAlign Align { get; set; }

        /// <summary>Text of the header row (Khmer, Latin or both).</summary>
        public string Header { get; set; }

        public ListColumnDesign()
        {
            Field = ListFields.Empty;
            Width = 20f;
            Align = ListAlign.Left;
            Header = "";
        }

        public ListColumnDesign(string field, string header, float width, ListAlign align)
        {
            Field = field;
            Header = header;
            Width = width;
            Align = align;
        }

        public ListColumnDesign Clone()
        {
            return (ListColumnDesign)MemberwiseClone();
        }

        /// <summary>Width used for printing (1 … 500; anything else counts as 20).</summary>
        [XmlIgnore]
        public float Weight { get { return float.IsNaN(Width) || Width < 1f || Width > 500f ? 20f : Width; } }
    }

    /// <summary>What a Student list column can show: student fields in a few formats, the row number, or nothing.</summary>
    public static class ListFields
    {
        public const string No = "No", NoKh = "NoKh", StudentId = "StudentId", NameKh = "NameKh", NameEn = "NameEn",
            NameEnUpper = "NameEnUpper", Gender = "Gender", GenderAsTyped = "GenderAsTyped", DateOfBirth = "DateOfBirth",
            DateOfBirthKh = "DateOfBirthKh", Faculty = "Faculty", Level = "Level", Generation = "Generation",
            ValidUntil = "ValidUntil", Empty = "Empty";

        public sealed class Field
        {
            public string Id { get; private set; }

            /// <summary>Name in the Page Design column list (English; translated when shown).</summary>
            public string Label { get; private set; }

            public string DefaultHeader { get; private set; }
            public float DefaultWidth { get; private set; }
            public ListAlign DefaultAlign { get; private set; }

            /// <summary>Cell text for a student and its 0-based row number in the list.</summary>
            public Func<Student, int, string> Value { get; private set; }

            public Field(string id, string label, string header, float width, ListAlign align, Func<Student, int, string> value)
            {
                Id = id; Label = label; DefaultHeader = header; DefaultWidth = width; DefaultAlign = align; Value = value;
            }
        }

        public static readonly Field[] All =
        {
            new Field(No, "No. (1, 2, 3)", "ល.រ", 9f, ListAlign.Center, (s, i) => (i + 1).ToString(CultureInfo.InvariantCulture)),
            new Field(NoKh, "No. in Khmer digits (១, ២, ៣)", "ល.រ", 9f, ListAlign.Center,
                (s, i) => KhmerText.Digits((i + 1).ToString(CultureInfo.InvariantCulture))),
            new Field(StudentId, "Student ID", "អត្តលេខ", 23f, ListAlign.Center, (s, i) => s.StudentId),
            new Field(NameKh, "Name (Khmer)", "គោត្តនាម-នាម", 36f, ListAlign.Left, (s, i) => s.NameKh),
            new Field(NameEnUpper, "Name (Latin, CAPITALS)", "អក្សរឡាតាំង", 38f, ListAlign.Left,
                (s, i) => (s.NameEn ?? "").ToUpperInvariant()),
            new Field(NameEn, "Name (Latin, as typed)", "អក្សរឡាតាំង", 38f, ListAlign.Left, (s, i) => s.NameEn),
            new Field(Gender, "Gender (ប្រុស / ស្រី)", "ភេទ", 11f, ListAlign.Center, (s, i) => KhmerText.Gender(s.Gender)),
            new Field(GenderAsTyped, "Gender (as typed)", "ភេទ", 11f, ListAlign.Center, (s, i) => s.Gender),
            new Field(DateOfBirth, "Date of birth (01/03/2005)", "ថ្ងៃខែឆ្នាំកំណើត", 25f, ListAlign.Center, (s, i) => s.DateOfBirth),
            new Field(DateOfBirthKh, "Date of birth (០១ មីនា ២០០៥)", "ថ្ងៃខែឆ្នាំកំណើត", 30f, ListAlign.Center,
                (s, i) => KhmerText.Date(s.DateOfBirth)),
            new Field(Faculty, "Faculty", "មហាវិទ្យាល័យ", 29.6f, ListAlign.Left, (s, i) => s.Faculty),
            new Field(Level, "Level", "កម្រិត", 30f, ListAlign.Left, (s, i) => s.Level),
            new Field(Generation, "Generation", "ជំនាន់", 13f, ListAlign.Center, (s, i) => s.Generation),
            new Field(ValidUntil, "Valid until", "ផុតកំណត់", 24f, ListAlign.Center, (s, i) => s.ValidUntil),
            new Field(Empty, "Empty (signature, remarks…)", "ហត្ថលេខា", 30f, ListAlign.Left, (s, i) => ""),
        };

        /// <summary>The field with this id; unknown ids show an empty column.</summary>
        public static Field Find(string id)
        {
            return All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal)) ?? All.Last();
        }

        /// <summary>A new column for a field, with its usual header, width and alignment.</summary>
        public static ListColumnDesign NewColumn(string id)
        {
            Field f = Find(id);
            return new ListColumnDesign(f.Id, f.DefaultHeader, f.DefaultWidth, f.DefaultAlign);
        }

        /// <summary>The standard NPIC columns (184.6 mm in all on A4 portrait with 0.5" margins).</summary>
        public static List<ListColumnDesign> DefaultColumns()
        {
            return new[] { No, StudentId, NameKh, NameEnUpper, Gender, DateOfBirth, Faculty, Generation }
                .Select(NewColumn).ToList();
        }
    }
}
