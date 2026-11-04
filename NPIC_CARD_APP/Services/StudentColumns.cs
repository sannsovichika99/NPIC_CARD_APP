using System;
using System.Collections.Generic;
using System.Text;

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// Column names of a student list (row 1 of a CSV / Excel sheet / Google Sheet, API field names, SQL column names).
    /// <para>Header: student_id,name_kh,name_en,gender,dob,faculty,degree,gen_no,start_date,end_date</para>
    /// Older lists that use the previous names (StudentId, NameKh, NameEn, Gender, DateOfBirth, Faculty, Level,
    /// Generation, ValidUntil) are still read: <see cref="Canonical"/> turns them into the new names.
    /// Case, '_', '-' and spaces are ignored when matching, so "Student ID" and "studentId" both mean student_id.
    /// </summary>
    public static class StudentColumns
    {
        public const string StudentId = "student_id";
        public const string NameKh = "name_kh";
        public const string NameEn = "name_en";
        public const string Gender = "gender";
        public const string DateOfBirth = "dob";
        public const string Faculty = "faculty";
        public const string Degree = "degree";
        public const string Generation = "gen_no";
        public const string StartDate = "start_date";
        public const string EndDate = "end_date";

        /// <summary>The columns of a student list, in file order.</summary>
        public static readonly string[] All =
            { StudentId, NameKh, NameEn, Gender, DateOfBirth, Faculty, Degree, Generation, StartDate, EndDate };

        /// <summary>Columns that hold a date (dd/MM/yyyy).</summary>
        public static readonly string[] Dates = { DateOfBirth, StartDate, EndDate };

        /// <summary>Header row of a new, empty student CSV.</summary>
        public static readonly string Header = string.Join(",", All);

        /// <summary>"student_id, name_kh, …, end_date" for help texts.</summary>
        public static readonly string List = string.Join(", ", All);

        /// <summary>Squashed name (see <see cref="Squash"/>) → column name, including the old names.</summary>
        private static readonly Dictionary<string, string> ByKey = BuildMap();

        private static Dictionary<string, string> BuildMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string column in All) map[Squash(column)] = column;

            // Previous column names (lists made before the rename keep working).
            map[Squash("StudentId")] = StudentId;
            map[Squash("NameKh")] = NameKh;
            map[Squash("NameEn")] = NameEn;
            map[Squash("DateOfBirth")] = DateOfBirth;
            map[Squash("Level")] = Degree;
            map[Squash("Generation")] = Generation;
            map[Squash("ValidUntil")] = EndDate;
            return map;
        }

        /// <summary>The student column a header stands for ("StudentId" → "student_id"), or null when it is not one.</summary>
        public static string Find(string header)
        {
            string column;
            return ByKey.TryGetValue(Squash(header), out column) ? column : null;
        }

        /// <summary>The student column a header stands for, or the header itself (trimmed) when it is not one.</summary>
        public static string Canonical(string header)
        {
            return Find(header) ?? (header ?? "").Trim().TrimStart('﻿');
        }

        /// <summary>True when the header is one of the student columns (old or new name).</summary>
        public static bool IsStandard(string header)
        {
            return Find(header) != null;
        }

        /// <summary>Lower-case letters and digits only: "Student_ID" → "studentid".</summary>
        public static string Squash(string key)
        {
            var sb = new StringBuilder(key == null ? 0 : key.Length);
            foreach (char ch in key ?? "")
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }
    }
}
