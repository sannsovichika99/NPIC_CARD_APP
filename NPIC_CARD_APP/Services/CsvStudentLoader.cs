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
        public static List<Student> Load(string path)
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            var students = new List<Student>();
            if (lines.Length == 0) return students;

            List<string> header = ParseLine(lines[0]);
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++)
                map[header[i].Trim().TrimStart('\uFEFF')] = i;

            if (!map.ContainsKey("StudentId"))
                throw new InvalidDataException("CSV header must contain a 'StudentId' column.");

            for (int r = 1; r < lines.Length; r++)
            {
                if (string.IsNullOrWhiteSpace(lines[r])) continue;
                List<string> cells = ParseLine(lines[r]);
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

        private static string Get(List<string> cells, Dictionary<string, int> map, string column)
        {
            int index;
            if (map.TryGetValue(column, out index) && index < cells.Count)
                return cells[index].Trim();
            return "";
        }

        private static List<string> ParseLine(string line)
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
