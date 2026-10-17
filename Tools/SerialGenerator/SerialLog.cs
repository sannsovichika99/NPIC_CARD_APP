using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CardStudio.SerialGenerator
{
    /// <summary>One serial number that was made.</summary>
    internal sealed class SerialRecord
    {
        public string Date { get; set; }
        public string MachineId { get; set; }
        public string Name { get; set; }
        public string Serial { get; set; }
    }

    /// <summary>
    /// serials.csv beside the private key: every serial made, by this app or by Installer\serial.ps1 (same columns:
    /// "Date","MachineId","Name","Serial", UTF-8). Git-ignored, like the key.
    /// </summary>
    internal static class SerialLog
    {
        public const string FileName = "serials.csv";
        private static readonly string[] Columns = { "Date", "MachineId", "Name", "Serial" };

        public static string PathFor(string keyPath)
        {
            return Path.Combine(Path.GetDirectoryName(keyPath), FileName);
        }

        /// <summary>All records, oldest first. A missing file is an empty log.</summary>
        public static List<SerialRecord> Read(string path)
        {
            var records = new List<SerialRecord>();
            if (!File.Exists(path)) return records;

            List<string[]> rows = ParseCsv(File.ReadAllText(path, Encoding.UTF8));
            if (rows.Count == 0) return records;

            string[] header = rows[0];
            int date = Array.IndexOf(header, "Date"), id = Array.IndexOf(header, "MachineId"),
                name = Array.IndexOf(header, "Name"), serial = Array.IndexOf(header, "Serial");
            if (id < 0 || serial < 0) throw new InvalidDataException("This is not a serial log (no MachineId / Serial column):\n" + path);

            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i];
                records.Add(new SerialRecord
                {
                    Date = Cell(r, date), MachineId = Cell(r, id), Name = Cell(r, name), Serial = Cell(r, serial)
                });
            }
            return records;
        }

        public static void Append(string path, SerialRecord record)
        {
            var sb = new StringBuilder();
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                sb.Append(Line(Columns));
            sb.Append(Line(new[] { record.Date, record.MachineId, record.Name, record.Serial }));
            // UTF-8 with BOM when the file is new (as PowerShell's Export-Csv writes it), so Excel shows Khmer names.
            File.AppendAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Cell(string[] row, int index)
        {
            return index >= 0 && index < row.Length ? row[index] : "";
        }

        private static string Line(string[] values)
        {
            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
                parts[i] = "\"" + (values[i] ?? "").Replace("\"", "\"\"") + "\"";
            return string.Join(",", parts) + "\r\n";
        }

        /// <summary>RFC 4180: quoted fields may hold commas, doubled quotes and line breaks.</summary>
        private static List<string[]> ParseCsv(string text)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false, any = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else field.Append(c);
                    continue;
                }
                switch (c)
                {
                    case '"': quoted = true; any = true; break;
                    case ',': row.Add(field.ToString()); field.Clear(); any = true; break;
                    case '\r': break;
                    case '\n':
                        if (any || field.Length > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
                        row.Clear(); field.Clear(); any = false;
                        break;
                    case '﻿': break;   // BOM
                    default: field.Append(c); any = true; break;
                }
            }
            if (any || field.Length > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
            return rows;
        }
    }
}
