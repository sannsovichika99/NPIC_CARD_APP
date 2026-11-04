using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NPIC_CARD_APP.Models;

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// Reads students from a UTF-8 CSV file (Excel: Save As > "CSV UTF-8").
    /// Header: student_id,name_kh,name_en,gender,dob,faculty,degree,gen_no,start_date,end_date
    /// (the old names StudentId, NameKh, … ValidUntil are still accepted, see <see cref="StudentColumns"/>).
    /// </summary>
    public static class CsvStudentLoader
    {
        /// <summary>Header row of a new, empty student CSV (same columns and order as the Excel template).</summary>
        public static readonly string Header = StudentColumns.Header;

        /// <summary><paramref name="requireStudentId"/> false: any table is read (PDF Designer data), every column kept.</summary>
        public static List<Student> Load(string path, bool requireStudentId = true)
        {
            return FromText(File.ReadAllText(path, Encoding.UTF8), "CSV header", requireStudentId);
        }

        /// <summary>Reads students from CSV text (a file's contents, or a CSV download from the API).</summary>
        internal static List<Student> FromText(string text, string headerName, bool requireStudentId = true)
        {
            string[] lines = (text ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var rows = new List<List<string>>(lines.Length);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                rows.Add(ParseLine(line));
            }
            return FromRows(rows, headerName, requireStudentId);
        }

        /// <summary>
        /// Turns a table (first non-blank row = header) into students. Shared by the CSV and Excel loaders
        /// so both accept exactly the same columns.
        /// </summary>
        internal static List<Student> FromRows(IList<List<string>> rows, string headerName, bool requireStudentId = true)
        {
            var students = new List<Student>();
            int headerRow = 0;
            while (headerRow < rows.Count && IsBlank(rows[headerRow])) headerRow++;
            if (headerRow == rows.Count) return students;

            List<string> header = rows[headerRow];
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++)
            {
                // A student column is keyed by its current name (old "StudentId" → "student_id"); others as typed.
                string name = StudentColumns.Canonical(header[i]);
                if (name.Length > 0 && !map.ContainsKey(name)) map[name] = i;
            }

            if (requireStudentId && !map.ContainsKey(StudentColumns.StudentId))
                throw new InvalidDataException(headerName + " must contain a 'student_id' column.");

            for (int r = headerRow + 1; r < rows.Count; r++)
            {
                List<string> cells = rows[r];
                if (IsBlank(cells)) continue;
                string faculty = Get(cells, map, StudentColumns.Faculty);
                var student = new Student
                {
                    StudentId = Get(cells, map, StudentColumns.StudentId),
                    NameKh = Get(cells, map, StudentColumns.NameKh),
                    NameEn = Get(cells, map, StudentColumns.NameEn),
                    Gender = Get(cells, map, StudentColumns.Gender),
                    DateOfBirth = Get(cells, map, StudentColumns.DateOfBirth),
                    Faculty = faculty != "" ? faculty : Get(cells, map, "Department"),
                    Level = Get(cells, map, StudentColumns.Degree),
                    Generation = Get(cells, map, StudentColumns.Generation),
                    StartDate = Get(cells, map, StudentColumns.StartDate),
                    ValidUntil = Get(cells, map, StudentColumns.EndDate)
                };
                // Keep every column, so Card Designer custom fields can print columns the app does not know.
                foreach (KeyValuePair<string, int> col in map)
                    student.Columns[col.Key] = col.Value < cells.Count ? (cells[col.Value] ?? "").Trim() : "";
                students.Add(student);
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
