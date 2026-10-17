using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NPIC_CARD_APP.Models;

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// Reads students from a UTF-8 CSV file (Excel: Save As > "CSV UTF-8").
    /// Header: StudentId,NameKh,NameEn,Gender,DateOfBirth,Faculty,Level,Generation,ValidUntil,QrData
    /// </summary>
    public static class CsvStudentLoader
    {
        /// <summary>Header row of a new, empty student CSV (same columns and order as the Excel template).</summary>
        public const string Header = "StudentId,NameKh,NameEn,Gender,DateOfBirth,Faculty,Level,Generation,ValidUntil,QrData";

        public static List<Student> Load(string path)
        {
            return FromText(File.ReadAllText(path, Encoding.UTF8), "CSV header");
        }

        /// <summary>Reads students from CSV text (a file's contents, or a CSV download from the API).</summary>
        internal static List<Student> FromText(string text, string headerName)
        {
            string[] lines = (text ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var rows = new List<List<string>>(lines.Length);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                rows.Add(ParseLine(line));
            }
            return FromRows(rows, headerName);
        }

        /// <summary>
        /// Turns a table (first non-blank row = header) into students. Shared by the CSV and Excel loaders
        /// so both accept exactly the same columns.
        /// </summary>
        internal static List<Student> FromRows(IList<List<string>> rows, string headerName)
        {
            var students = new List<Student>();
            int headerRow = 0;
            while (headerRow < rows.Count && IsBlank(rows[headerRow])) headerRow++;
            if (headerRow == rows.Count) return students;

            List<string> header = rows[headerRow];
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++)
            {
                string name = (header[i] ?? "").Trim().TrimStart('﻿');
                if (name.Length > 0 && !map.ContainsKey(name)) map[name] = i;
            }

            if (!map.ContainsKey("StudentId"))
                throw new InvalidDataException(headerName + " must contain a 'StudentId' column.");

            for (int r = headerRow + 1; r < rows.Count; r++)
            {
                List<string> cells = rows[r];
                if (IsBlank(cells)) continue;
                string faculty = Get(cells, map, "Faculty");
                students.Add(new Student
                {
                    StudentId = Get(cells, map, "StudentId"),
                    NameKh = Get(cells, map, "NameKh"),
                    NameEn = Get(cells, map, "NameEn"),
                    Gender = Get(cells, map, "Gender"),
                    DateOfBirth = Get(cells, map, "DateOfBirth"),
                    Faculty = faculty != "" ? faculty : Get(cells, map, "Department"),
                    Level = Get(cells, map, "Level"),
                    Generation = Get(cells, map, "Generation"),
                    ValidUntil = Get(cells, map, "ValidUntil"),
                    QrData = Get(cells, map, "QrData")
                });
            }
            return students;
        }

        private static bool IsBlank(List<string> cells)
        {
            foreach (string c in cells)
                if (!string.IsNullOrWhiteSpace(c)) return false;
            return true;
        }

        private static string Get(List<string> cells, Dictionary<string, int> map, string column)
        {
            int index;
            if (map.TryGetValue(column, out index) && index < cells.Count)
                return (cells[index] ?? "").Trim();
            return "";
        }

        internal static List<string> ParseLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(ch);
                }
                else if (ch == '"') inQuotes = true;
                else if (ch == ',') { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            result.Add(sb.ToString());
            return result;
        }
    }
}
