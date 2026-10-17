using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// Writes an empty student list workbook (.xlsx) that <see cref="ExcelStudentLoader"/> can import:
    /// sheet "Students" = header row to fill in, sheet "Instructions" = what goes in each column.
    /// Every Students cell is formatted as Text, so Excel keeps "15/03/2007" and "0012" exactly as typed
    /// (no date / number conversion that depends on the PC's regional settings).
    /// </summary>
    public static class ExcelTemplateWriter
    {
        // Column, what to enter, example. Order = column order of the Students sheet.
        private static readonly string[][] Columns =
        {
            new[] { "StudentId",   "Student ID. Photos must be named {StudentId}.jpg or .png", "NPIC24001" },
            new[] { "NameKh",      "Full name in Khmer",                                     "សុខ ចាន់ធីតា" },
            new[] { "NameEn",      "Full name in Latin letters",                             "Sok Chanthida" },
            new[] { "Gender",      "M / F / ប្រុស / ស្រី (pick from the list)",                "F" },
            new[] { "DateOfBirth", "Date of birth as dd/MM/yyyy",                            "15/03/2007" },
            new[] { "Faculty",     "Faculty / department",                                   "បច្ចេកវិទ្យាព័ត៌មាន" },
            new[] { "Level",       "Study level",                                            "ជាន់ខ្ពស់បច្ចេកទេស" },
            new[] { "Generation",  "Generation number",                                      "14" },
            new[] { "ValidUntil",  "Card expiry date as dd/MM/yyyy",                         "30/12/2027" },
            new[] { "QrData",      "Optional: text or link for this student's QR code",      "https://www.npic.edu.kh" }
        };

        private static readonly double[] ColumnWidths = { 14, 26, 24, 10, 14, 30, 26, 12, 14, 30 };
        private const int ValidationRows = 5000;

        // Cell style indexes in styles.xml (cellXfs)
        private const int StyleHeader = 1, StyleText = 2, StyleTitle = 3, StyleBold = 4, StyleWrap = 5;

        private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static void Write(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml",
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    "</Types>");

                Add(zip, "_rels/.rels",
                    "<Relationships xmlns=\"" + PkgRelNs + "\">" +
                    "<Relationship Id=\"rId1\" Type=\"" + RelNs + "/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");

                Add(zip, "xl/workbook.xml",
                    "<workbook xmlns=\"" + MainNs + "\" xmlns:r=\"" + RelNs + "\">" +
                    "<bookViews><workbookView activeTab=\"0\"/></bookViews>" +
                    "<sheets>" +
                    "<sheet name=\"Students\" sheetId=\"1\" r:id=\"rId1\"/>" +
                    "<sheet name=\"Instructions\" sheetId=\"2\" r:id=\"rId2\"/>" +
                    "</sheets></workbook>");

                Add(zip, "xl/_rels/workbook.xml.rels",
                    "<Relationships xmlns=\"" + PkgRelNs + "\">" +
                    "<Relationship Id=\"rId1\" Type=\"" + RelNs + "/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"" + RelNs + "/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
                    "<Relationship Id=\"rId3\" Type=\"" + RelNs + "/styles\" Target=\"styles.xml\"/>" +
                    "</Relationships>");

                Add(zip, "xl/styles.xml", StylesXml());
                Add(zip, "xl/worksheets/sheet1.xml", StudentsSheetXml());
                Add(zip, "xl/worksheets/sheet2.xml", InstructionsSheetXml());
            }
        }

        private static string StylesXml()
        {
            // Navy header = the accent colour used on the card and in the app (46, 49, 146).
            return "<styleSheet xmlns=\"" + MainNs + "\">" +
                   "<fonts count=\"4\">" +
                   "<font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
                   "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
                   "<font><b/><sz val=\"14\"/><color rgb=\"FF2E3192\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
                   "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
                   "</fonts>" +
                   "<fills count=\"3\">" +
                   "<fill><patternFill patternType=\"none\"/></fill>" +
                   "<fill><patternFill patternType=\"gray125\"/></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF2E3192\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "</fills>" +
                   "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                   "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                   "<cellXfs count=\"6\">" +
                   "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                   "<xf numFmtId=\"49\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" +
                   "<xf numFmtId=\"49\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                   "</cellXfs>" +
                   "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                   "</styleSheet>";
        }

        private static string StudentsSheetXml()
        {
            var sb = new StringBuilder();
            sb.Append("<worksheet xmlns=\"").Append(MainNs).Append("\" xmlns:r=\"").Append(RelNs).Append("\">");
            sb.Append("<sheetViews><sheetView tabSelected=\"1\" workbookViewId=\"0\">")
              .Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>")
              .Append("<selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/>")
              .Append("</sheetView></sheetViews>");
            sb.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");

            // Whole columns formatted as Text, so new rows typed in later are Text too.
            sb.Append("<cols>");
            for (int i = 0; i < Columns.Length; i++)
                sb.AppendFormat("<col min=\"{0}\" max=\"{0}\" width=\"{1}\" style=\"{2}\" customWidth=\"1\"/>",
                    i + 1, ColumnWidths[i].ToString(System.Globalization.CultureInfo.InvariantCulture), StyleText);
            sb.Append("</cols>");

            sb.Append("<sheetData><row r=\"1\" ht=\"22\" customHeight=\"1\">");
            for (int i = 0; i < Columns.Length; i++)
                AppendCell(sb, ColumnLetter(i) + "1", Columns[i][0], StyleHeader);
            sb.Append("</row></sheetData>");

            // Gender drop-down (typing something else is still allowed: no error message).
            int genderCol = IndexOf("Gender");
            string range = ColumnLetter(genderCol) + "2:" + ColumnLetter(genderCol) + ValidationRows;
            sb.Append("<dataValidations count=\"1\">")
              .Append("<dataValidation type=\"list\" allowBlank=\"1\" showInputMessage=\"1\" sqref=\"").Append(range).Append("\">")
              .Append("<formula1>").Append(Escape("\"M,F,ប្រុស,ស្រី\"")).Append("</formula1>")
              .Append("</dataValidation></dataValidations>");

            sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static string InstructionsSheetXml()
        {
            var sb = new StringBuilder();
            sb.Append("<worksheet xmlns=\"").Append(MainNs).Append("\">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>");
            sb.Append("<cols>")
              .Append("<col min=\"1\" max=\"1\" width=\"16\" customWidth=\"1\"/>")
              .Append("<col min=\"2\" max=\"2\" width=\"58\" customWidth=\"1\"/>")
              .Append("<col min=\"3\" max=\"3\" width=\"28\" customWidth=\"1\"/>")
              .Append("</cols>");
            sb.Append("<sheetData>");

            int r = 1;
            Row(sb, r++, StyleTitle, "Card Studio — student list template");
            Row(sb, r++, 0, "1. Fill in the Students sheet: one student per row. Keep the header row (row 1) unchanged.");
            Row(sb, r++, 0, "2. Type dates as dd/MM/yyyy, e.g. 15/03/2007. The columns are formatted as Text so Excel keeps them exactly as typed.");
            Row(sb, r++, 0, "3. Save the file, then in Card Studio click  Import Excel…  and choose it.");
            r++;

            sb.AppendFormat("<row r=\"{0}\">", r);
            AppendCell(sb, "A" + r, "Column", StyleHeader);
            AppendCell(sb, "B" + r, "What to enter", StyleHeader);
            AppendCell(sb, "C" + r, "Example", StyleHeader);
            sb.Append("</row>");
            r++;

            foreach (string[] col in Columns)
            {
                sb.AppendFormat("<row r=\"{0}\">", r);
                AppendCell(sb, "A" + r, col[0], StyleBold);
                AppendCell(sb, "B" + r, col[1], StyleWrap);
                AppendCell(sb, "C" + r, col[2], StyleText);
                sb.Append("</row>");
                r++;
            }

            sb.Append("</sheetData>");
            sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static void Row(StringBuilder sb, int r, int style, string text)
        {
            sb.AppendFormat("<row r=\"{0}\">", r);
            AppendCell(sb, "A" + r, text, style);
            sb.Append("</row>");
        }

        private static void AppendCell(StringBuilder sb, string cellRef, string text, int style)
        {
            sb.Append("<c r=\"").Append(cellRef).Append("\" s=\"").Append(style).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
              .Append(Escape(text)).Append("</t></is></c>");
        }

        private static void Add(ZipArchive zip, string name, string xml)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name);
            using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
                w.Write(xml);
            }
        }

        private static int IndexOf(string column)
        {
            for (int i = 0; i < Columns.Length; i++)
                if (Columns[i][0] == column) return i;
            return -1;
        }

        private static string ColumnLetter(int index)   // 0 → A, 25 → Z, 26 → AA
        {
            string s = "";
            for (int n = index + 1; n > 0; n = (n - 1) / 26)
                s = (char)('A' + (n - 1) % 26) + s;
            return s;
        }

        private static string Escape(string text)
        {
            return SecurityElement.Escape(text);
        }
    }
}
