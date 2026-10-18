using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CardStudio.SerialGenerator
{
    /// <summary>One serial number that was made.</summary>
    internal sealed class SerialRecord
    {
        public const string Never = "never";

        public string Date { get; set; }
        public string MachineId { get; set; }
        public string Name { get; set; }

        /// <summary>Last valid day as yyyy-MM-dd, or "never". Empty in logs from before expiry dates: those serials never expire.</summary>
        public string Expires { get; set; }

        public string Serial { get; set; }

        /// <summary>The expiry date, or null when the serial never expires.</summary>
        public DateTime? ExpiryDate
        {
            get
            {
                DateTime d;
                return DateTime.TryParseExact(Expires ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                    ? d : (DateTime?)null;
            }
        }

        /// <summary>"2027-10-18" or "never" (also for old log lines without the column).</summary>
        public string ExpiresText
        {
            get { return ExpiryDate == null ? Never : Expires; }
        }

        public bool IsExpired
        {
            get { return ExpiryDate != null && DateTime.Today > ExpiryDate.Value; }
        }
    }

    /// <summary>
    /// serials.csv beside the private key: every serial made, by this app or by Installer\serial.ps1 (same columns:
    /// "Date","MachineId","Name","Expires","Serial", UTF-8). Git-ignored, like the key. Logs from before expiry dates
    /// have no Expires column: it is added (empty = never) the next time a serial is logged.
    /// </summary>
    internal static class SerialLog
    {
        public const string FileName = "serials.csv";
        private static readonly string[] Columns = { "Date", "MachineId", "Name", "Expires", "Serial" };

        public static string PathFor(string keyPath)
        {
            return Path.Combine(Path.GetDirectoryName(keyPath), FileName);
        }

        /// <summary>All records, oldest first. A missing file is an empty log.</summary>
        public static List<SerialRecord> Read(string path)
        {
            bool hasExpires;
            return Read(path, out hasExpires);
        }

        private static List<SerialRecord> Read(string path, out bool hasExpires)
        {
            hasExpires = false;
            var records = new List<SerialRecord>();
            if (!File.Exists(path)) return records;

            List<string[]> rows = ParseCsv(File.ReadAllText(path, Encoding.UTF8));
            if (rows.Count == 0) return records;

            string[] header = rows[0];
            int date = Array.IndexOf(header, "Date"), id = Array.IndexOf(header, "MachineId"),
                name = Array.IndexOf(header, "Name"), expires = Array.IndexOf(header, "Expires"), serial = Array.IndexOf(header, "Serial");
            if (id < 0 || serial < 0) throw new InvalidDataException("This is not a serial log (no MachineId / Serial column):\n" + path);
            hasExpires = expires >= 0;

            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i];
                records.Add(new SerialRecord
                {
                    Date = Cell(r, date), MachineId = Cell(r, id), Name = Cell(r, name), Expires = Cell(r, expires),
                    Serial = Cell(r, serial)
                });
            }
            return records;
        }

        public static void Append(string path, SerialRecord record)
        {
            bool exists = File.Exists(path) && new FileInfo(path).Length > 0;
            bool hasExpires = true;
            List<SerialRecord> old = exists ? Read(path, out hasExpires) : null;

            if (exists && !hasExpires)
            {
                // Log from before expiry dates: rewrite it once with the Expires column (old serials never expire).
                var sb = new StringBuilder(Line(Columns));
                foreach (SerialRecord r in old) sb.Append(Line(r));
                sb.Append(Line(record));
                string temp = path + ".tmp";
                File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(true));
                File.Copy(temp, path, true);
                File.Delete(temp);
                return;
            }

            // UTF-8 with BOM when the file is new (as PowerShell's Export-Csv writes it), so Excel shows Khmer names.
            File.AppendAllText(path, (exists ? "" : Line(Columns)) + Line(record), new UTF8Encoding(true));
        }

        private static string Cell(string[] row, int index)
        {
            return index >= 0 && index < row.Length ? row[index] : "";
        }

        private static string Line(SerialRecord r)
        {
            return Line(new[] { r.Date, r.MachineId, r.Name, r.Expires, r.Serial });
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
