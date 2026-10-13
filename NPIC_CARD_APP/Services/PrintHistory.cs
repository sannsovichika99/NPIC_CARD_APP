using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NPIC_CARD_APP.Models;

namespace NPIC_CARD_APP.Services
{
    /// <summary>One card side sent to the printer.</summary>
    public class PrintRecord
    {
        public DateTime PrintedAt { get; set; }
        public string StudentId { get; set; }
        public string NameKh { get; set; }
        public string NameEn { get; set; }
        public string Faculty { get; set; }
        public string Level { get; set; }
        public string Generation { get; set; }
        /// <summary>"FRONT" or "BACK".</summary>
        public string Side { get; set; }
        /// <summary>Tray slot the card was in (1 or 2).</summary>
        public int Slot { get; set; }
        public string Printer { get; set; }
        public string Document { get; set; }
        public string User { get; set; }
        public string Computer { get; set; }

        public bool IsFront { get { return string.Equals(Side, "FRONT", StringComparison.OrdinalIgnoreCase); } }
        public bool IsBack { get { return string.Equals(Side, "BACK", StringComparison.OrdinalIgnoreCase); } }

        public static PrintRecord For(Student s, string side, int slot, string printer, string document)
        {
            return new PrintRecord
            {
                PrintedAt = DateTime.Now,
                StudentId = s.StudentId ?? "",
                NameKh = s.NameKh ?? "",
                NameEn = s.NameEn ?? "",
                Faculty = s.Faculty ?? "",
                Level = s.Level ?? "",
                Generation = s.Generation ?? "",
                Side = side,
                Slot = slot,
                Printer = printer ?? "",
                Document = document ?? "",
                User = Environment.UserName,
                Computer = Environment.MachineName
            };
        }
    }

    /// <summary>
    /// Log of every card side sent to the printer, kept in %AppData%\NPIC_CARD_APP\print-history.csv
    /// (UTF-8 with BOM, so Excel shows the Khmer names correctly).
    /// </summary>
    public static class PrintHistory
    {
        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        private static readonly string[] Columns =
        {
            "PrintedAt", "StudentId", "NameKh", "NameEn", "Faculty", "Level", "Generation",
            "Side", "Slot", "Printer", "Document", "User", "Computer"
        };

        private static readonly Encoding Utf8Bom = new UTF8Encoding(true);

        public static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NPIC_CARD_APP");
                return Path.Combine(dir, "print-history.csv");
            }
        }

        /// <summary>Appends records to the log (creates the file with a header the first time).</summary>
        public static void Add(IEnumerable<PrintRecord> records)
        {
            var sb = new StringBuilder();
            foreach (PrintRecord r in records) sb.Append(ToLine(r)).Append("\r\n");
            if (sb.Length == 0) return;

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length == 0)
                sb.Insert(0, HeaderLine() + "\r\n");
            // StreamWriter only writes the BOM when the file is empty.
            using (var writer = new StreamWriter(FilePath, true, Utf8Bom))
                writer.Write(sb.ToString());
        }

        /// <summary>All records, oldest first. Empty when there is no log yet; damaged lines are skipped.</summary>
        public static List<PrintRecord> Load()
        {
            var records = new List<PrintRecord>();
            if (!File.Exists(FilePath)) return records;

            string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);
            Dictionary<string, int> map = null;
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                List<string> cells = CsvStudentLoader.ParseLine(line);
                if (map == null)
                {
                    map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < cells.Count; i++)
                    {
                        string name = cells[i].Trim().TrimStart('﻿');
                        if (name.Length > 0 && !map.ContainsKey(name)) map[name] = i;
                    }
                    if (!map.ContainsKey("PrintedAt") || !map.ContainsKey("StudentId"))
                        throw new InvalidDataException("The print history file has no header row:\n" + FilePath);
                    continue;
                }

                DateTime at;
                if (!DateTime.TryParseExact(Get(cells, map, "PrintedAt"), DateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out at))
                    continue;
                int slot;
                int.TryParse(Get(cells, map, "Slot"), NumberStyles.Integer, CultureInfo.InvariantCulture, out slot);

                records.Add(new PrintRecord
                {
                    PrintedAt = at,
                    StudentId = Get(cells, map, "StudentId"),
                    NameKh = Get(cells, map, "NameKh"),
                    NameEn = Get(cells, map, "NameEn"),
                    Faculty = Get(cells, map, "Faculty"),
                    Level = Get(cells, map, "Level"),
                    Generation = Get(cells, map, "Generation"),
                    Side = Get(cells, map, "Side"),
                    Slot = slot,
                    Printer = Get(cells, map, "Printer"),
                    Document = Get(cells, map, "Document"),
                    User = Get(cells, map, "User"),
                    Computer = Get(cells, map, "Computer")
                });
            }
            return records;
        }

        /// <summary>Rewrites the whole log (used after deleting records).</summary>
        public static void Save(IEnumerable<PrintRecord> records)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var sb = new StringBuilder(HeaderLine()).Append("\r\n");
            foreach (PrintRecord r in records.OrderBy(r => r.PrintedAt))
                sb.Append(ToLine(r)).Append("\r\n");

            // Write a temp file first so a crash half-way never loses the history.
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, sb.ToString(), Utf8Bom);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }

        /// <summary>Writes any table as a UTF-8 CSV that Excel opens directly.</summary>
        public static void WriteCsv(string path, IList<string> header, IEnumerable<IList<string>> rows)
        {
            var sb = new StringBuilder(string.Join(",", header.Select(Escape))).Append("\r\n");
            foreach (IList<string> row in rows)
                sb.Append(string.Join(",", row.Select(Escape))).Append("\r\n");
            File.WriteAllText(path, sb.ToString(), Utf8Bom);
        }

        private static string HeaderLine()
        {
            return string.Join(",", Columns);
        }

        private static string ToLine(PrintRecord r)
        {
            string[] cells =
            {
                r.PrintedAt.ToString(DateFormat, CultureInfo.InvariantCulture),
                r.StudentId, r.NameKh, r.NameEn, r.Faculty, r.Level, r.Generation,
                r.Side, r.Slot.ToString(CultureInfo.InvariantCulture), r.Printer, r.Document, r.User, r.Computer
            };
            return string.Join(",", cells.Select(Escape));
        }

        /// <summary>CSV-quotes a value; line breaks become spaces so one record is always one line.</summary>
        private static string Escape(string value)
        {
            value = (value ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            if (value.IndexOfAny(new[] { ',', '"' }) < 0 && value.Trim() == value) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string Get(List<string> cells, Dictionary<string, int> map, string column)
        {
            int index;
            return map.TryGetValue(column, out index) && index < cells.Count ? (cells[index] ?? "").Trim() : "";
        }
    }
}
