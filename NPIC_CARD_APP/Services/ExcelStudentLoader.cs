using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using NPIC_CARD_APP.Models;

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// Reads students from an Excel workbook (.xlsx / .xlsm) without needing Excel or any extra library:
    /// an .xlsx file is a zip of XML parts, read here with System.IO.Compression + XmlReader.
    /// Uses the first visible worksheet whose header row has a 'StudentId' column. The columns are the
    /// same as for CSV import. Real Excel date cells in DateOfBirth / ValidUntil become dd/MM/yyyy.
    /// </summary>
    public static class ExcelStudentLoader
    {
        private const string RelNs = "/relationships";
        private static readonly string[] DateColumns = { "DateOfBirth", "ValidUntil" };

        public static bool IsExcelFile(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return ext == ".xlsx" || ext == ".xlsm" || ext == ".xls";
        }

        /// <summary><paramref name="requireStudentId"/> false: any sheet is read (PDF Designer data), every column kept.</summary>
        public static List<Student> Load(string path, bool requireStudentId = true)
        {
            if (string.Equals(Path.GetExtension(path), ".xls", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Old Excel 97-2003 (.xls) files are not supported.\n\n" +
                    "Open the file in Excel, choose File > Save As > \"Excel Workbook (*.xlsx)\", then import the new file.");

            // FileShare.ReadWrite: the workbook can still be imported while it is open in Excel.
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (ZipArchive zip = OpenZip(fs))
            {
                string workbookPath = FindWorkbookPath(zip);
                Dictionary<string, Rel> rels = ReadRels(zip, workbookPath);

                bool date1904;
                List<string> sheetPaths = ReadSheetPaths(zip, workbookPath, rels, out date1904);
                if (sheetPaths.Count == 0)
                    throw new InvalidDataException("The workbook has no worksheets.");

                Rel sharedRel = rels.Values.FirstOrDefault(x => x.Type.EndsWith("/sharedStrings", StringComparison.OrdinalIgnoreCase));
                List<string> shared = sharedRel != null ? ReadSharedStrings(zip, sharedRel.Target) : new List<string>();

                List<List<string>> chosen = null;
                foreach (string sheetPath in sheetPaths)
                {
                    List<List<string>> rows = ReadSheet(zip, sheetPath, shared);
                    if (chosen == null) chosen = rows;               // fallback: first sheet (gives a clear header error)
                    if (HeaderIndex(rows, "StudentId") >= 0) { chosen = rows; break; }
                }

                ConvertDateSerials(chosen, date1904);
                return CsvStudentLoader.FromRows(chosen, "The first row of the Excel sheet", requireStudentId);
            }
        }

        // ===================== Workbook structure =====================
        private sealed class Rel
        {
            public string Type;
            public string Target;   // resolved, zip-relative path
        }

        private static ZipArchive OpenZip(Stream stream)
        {
            try
            {
                return new ZipArchive(stream, ZipArchiveMode.Read);
            }
            catch (InvalidDataException)
            {
                throw new InvalidDataException(
                    "This file is not a valid .xlsx workbook.\n\n" +
                    "If it is password-protected, remove the password in Excel first; if it is an old .xls file, " +
                    "save it as \"Excel Workbook (*.xlsx)\".");
            }
        }

        private static string FindWorkbookPath(ZipArchive zip)
        {
            Rel doc = ReadRels(zip, "").Values
                .FirstOrDefault(x => x.Type.EndsWith("/officeDocument", StringComparison.OrdinalIgnoreCase));
            string path = doc != null ? doc.Target : "xl/workbook.xml";
            if (Entry(zip, path) == null)
                throw new InvalidDataException("This file is not a valid .xlsx workbook (no workbook part found).");
            return path;
        }

        /// <summary>Reads the .rels file that belongs to <paramref name="partPath"/> ("" = the package itself).</summary>
        private static Dictionary<string, Rel> ReadRels(ZipArchive zip, string partPath)
        {
            string dir = DirectoryOf(partPath);
            string relsPath = (dir.Length > 0 ? dir + "/" : "") + "_rels/" + FileNameOf(partPath) + ".rels";
            var result = new Dictionary<string, Rel>(StringComparer.Ordinal);

            ZipArchiveEntry entry = Entry(zip, relsPath);
            if (entry == null) return result;

            using (XmlReader r = OpenXml(entry))
            {
                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element || r.LocalName != "Relationship") continue;
                    string id = r.GetAttribute("Id");
                    string target = r.GetAttribute("Target");
                    if (id == null || target == null) continue;
                    if (string.Equals(r.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)) continue;
                    result[id] = new Rel { Type = r.GetAttribute("Type") ?? "", Target = ResolvePath(dir, target) };
                }
            }
            return result;
        }

        /// <summary>Worksheet parts in workbook tab order (visible sheets first; hidden ones only if nothing else).</summary>
        private static List<string> ReadSheetPaths(ZipArchive zip, string workbookPath, Dictionary<string, Rel> rels, out bool date1904)
        {
            var visible = new List<string>();
            var hidden = new List<string>();
            date1904 = false;

            using (XmlReader r = OpenXml(Entry(zip, workbookPath)))
            {
                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element) continue;
                    if (r.LocalName == "workbookPr")
                    {
                        string d = r.GetAttribute("date1904");
                        date1904 = d == "1" || string.Equals(d, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (r.LocalName == "sheet")
                    {
                        string relId = RelationshipId(r);
                        Rel rel;
                        if (relId == null || !rels.TryGetValue(relId, out rel)) continue;
                        if (!rel.Type.EndsWith("/worksheet", StringComparison.OrdinalIgnoreCase)) continue;   // skip chart sheets
                        string state = r.GetAttribute("state");
                        (string.IsNullOrEmpty(state) || state == "visible" ? visible : hidden).Add(rel.Target);
                    }
                }
            }
            return visible.Count > 0 ? visible : hidden;
        }

        /// <summary>The r:id attribute of a &lt;sheet&gt;, whatever prefix / namespace version the file uses.</summary>
        private static string RelationshipId(XmlReader r)
        {
            string id = null;
            if (r.MoveToFirstAttribute())
            {
                do
                {
                    if (r.LocalName == "id" && r.NamespaceURI.EndsWith(RelNs, StringComparison.OrdinalIgnoreCase))
                    {
                        id = r.Value;
                        break;
                    }
                } while (r.MoveToNextAttribute());
                r.MoveToElement();
            }
            return id;
        }

        // ===================== Cell data =====================
        private static List<string> ReadSharedStrings(ZipArchive zip, string path)
        {
            var list = new List<string>();
            ZipArchiveEntry entry = Entry(zip, path);
            if (entry == null) return list;

            using (XmlReader r = OpenXml(entry))
            {
                while (!r.EOF)
                {
                    if (r.NodeType == XmlNodeType.Element && r.LocalName == "si") { list.Add(ReadRichText(r)); continue; }
                    r.Read();
                }
            }
            return list;
        }

        private static List<List<string>> ReadSheet(ZipArchive zip, string path, List<string> shared)
        {
            var rows = new List<List<string>>();
            ZipArchiveEntry entry = Entry(zip, path);
            if (entry == null) return rows;

            using (XmlReader r = OpenXml(entry))
            {
                List<string> row = null;
                int nextCol = 0;
                while (!r.EOF)
                {
                    if (r.NodeType == XmlNodeType.Element)
                    {
                        if (r.LocalName == "row")
                        {
                            row = new List<string>();
                            rows.Add(row);
                            nextCol = 0;
                        }
                        else if (r.LocalName == "c")
                        {
                            string cellRef = r.GetAttribute("r");
                            string type = r.GetAttribute("t");
                            int col = cellRef != null ? ColumnIndex(cellRef) : nextCol;
                            nextCol = col + 1;
                            string value = ReadCell(r, type, shared);   // moves past </c>
                            if (row != null && col >= 0)
                            {
                                while (row.Count < col) row.Add("");
                                if (row.Count == col) row.Add(value); else row[col] = value;
                            }
                            continue;
                        }
                    }
                    r.Read();
                }
            }
            return rows;
        }

        /// <summary>Reads one &lt;c&gt; element (reader on its start tag) and leaves the reader after it.</summary>
        private static string ReadCell(XmlReader r, string type, List<string> shared)
        {
            if (r.IsEmptyElement) { r.Read(); return ""; }

            string v = null, inline = null;
            int depth = r.Depth;
            r.Read();
            while (!r.EOF && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
            {
                if (r.NodeType == XmlNodeType.Element)
                {
                    if (r.LocalName == "v") { v = r.ReadElementContentAsString(); continue; }
                    if (r.LocalName == "is") { inline = ReadRichText(r); continue; }
                    if (r.LocalName == "f") { r.Skip(); continue; }
                }
                r.Read();
            }
            r.Read();   // past </c>

            switch (type)
            {
                case "s":
                    int index;
                    return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                           && index >= 0 && index < shared.Count ? shared[index] : "";
                case "inlineStr":
                    return inline ?? v ?? "";
                case "str":
                    return v ?? "";
                case "b":
                    return v == "1" ? "TRUE" : v == null ? "" : "FALSE";
                case "e":
                    return "";   // #N/A, #REF! … → treat as empty
                case "d":
                    DateTime iso;
                    return DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out iso)
                        ? iso.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : (v ?? "");
                default:
                    return FormatNumber(v);
            }
        }

        /// <summary>Concatenates the &lt;t&gt; runs of an &lt;si&gt; / &lt;is&gt; element (skipping phonetic hints).</summary>
        private static string ReadRichText(XmlReader r)
        {
            if (r.IsEmptyElement) { r.Read(); return ""; }

            var sb = new StringBuilder();
            int depth = r.Depth;
            r.Read();
            while (!r.EOF && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
            {
                if (r.NodeType == XmlNodeType.Element)
                {
                    if (r.LocalName == "t") { sb.Append(r.ReadElementContentAsString()); continue; }
                    if (r.LocalName == "rPh") { r.Skip(); continue; }
                }
                r.Read();
            }
            r.Read();   // past the end tag
            return sb.ToString();
        }

        /// <summary>"24001" stays "24001" (not "24001.0"); float noise like 14.000000000000002 becomes "14".</summary>
        private static string FormatNumber(string v)
        {
            double d;
            if (string.IsNullOrEmpty(v) || !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return v ?? "";
            double rounded = Math.Round(d);
            if (Math.Abs(d - rounded) < 1e-9 && Math.Abs(rounded) < 1e15)
                return ((long)rounded).ToString(CultureInfo.InvariantCulture);
            return d.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Date cells are stored as day numbers (e.g. 39156 = 15/03/2007); the card expects dd/MM/yyyy.</summary>
        private static void ConvertDateSerials(List<List<string>> rows, bool date1904)
        {
            int headerRow = 0;
            while (headerRow < rows.Count && rows[headerRow].All(string.IsNullOrWhiteSpace)) headerRow++;
            if (headerRow >= rows.Count) return;

            foreach (string column in DateColumns)
            {
                int col = HeaderIndex(rows, column);
                if (col < 0) continue;
                for (int i = headerRow + 1; i < rows.Count; i++)
                {
                    List<string> row = rows[i];
                    double serial;
                    if (col >= row.Count ||
                        !double.TryParse(row[col], NumberStyles.Float, CultureInfo.InvariantCulture, out serial) ||
                        serial < 1 || serial > 2958465)
                        continue;
                    if (date1904) serial += 1462;
                    row[col] = DateTime.FromOADate(serial).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                }
            }
        }

        /// <summary>Column index of <paramref name="name"/> in the first non-blank row, or -1.</summary>
        private static int HeaderIndex(List<List<string>> rows, string name)
        {
            foreach (List<string> row in rows)
            {
                if (row.All(string.IsNullOrWhiteSpace)) continue;
                for (int i = 0; i < row.Count; i++)
                    if (string.Equals((row[i] ?? "").Trim().TrimStart('﻿'), name, StringComparison.OrdinalIgnoreCase))
                        return i;
                return -1;
            }
            return -1;
        }

        // ===================== Helpers =====================
        /// <summary>"B12" → 1 (zero-based column).</summary>
        private static int ColumnIndex(string cellRef)
        {
            int col = 0, letters = 0;
            foreach (char ch in cellRef)
            {
                char c = char.ToUpperInvariant(ch);
                if (c < 'A' || c > 'Z') break;
                col = col * 26 + (c - 'A' + 1);
                letters++;
            }
            return letters == 0 ? -1 : col - 1;
        }

        private static XmlReader OpenXml(ZipArchiveEntry entry)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                CloseInput = true
            };
            return XmlReader.Create(entry.Open(), settings);
        }

        private static ZipArchiveEntry Entry(ZipArchive zip, string path)
        {
            if (path == null) return null;
            path = path.TrimStart('/');
            return zip.GetEntry(path) ??
                   zip.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), path, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolvePath(string baseDir, string target)
        {
            target = target.Replace('\\', '/');
            string combined = target.StartsWith("/") ? target.TrimStart('/') : (baseDir.Length > 0 ? baseDir + "/" + target : target);
            var parts = new List<string>();
            foreach (string part in combined.Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(part);
            }
            return string.Join("/", parts);
        }

        private static string DirectoryOf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? "" : path.Substring(0, slash);
        }

        private static string FileNameOf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }
    }
}
