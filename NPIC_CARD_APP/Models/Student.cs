using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace NPIC_CARD_APP.Models
{
    public class Student
    {
        // Property = list column (see Services.StudentColumns)
        public string StudentId { get; set; }    // student_id  អត្តលេខ
        public string NameKh { get; set; }       // name_kh     គោត្តនាម-នាម
        public string NameEn { get; set; }       // name_en     អក្សរឡាតាំង
        public string Gender { get; set; }       // gender      M / F / ប្រុស / ស្រី
        public string DateOfBirth { get; set; }  // dob         dd/MM/yyyy → ២៤ មករា ២០០៨
        public string Faculty { get; set; }      // faculty     មហាវិទ្យាល័យ
        public string Level { get; set; }        // degree      កម្រិត
        public string Generation { get; set; }   // gen_no      ជំនាន់ទី
        public string StartDate { get; set; }    // start_date  dd/MM/yyyy: first day the card is valid
        public string ValidUntil { get; set; }   // end_date    dd/MM/yyyy → ផុតកំណត់ថ្ងៃទី...

        private readonly Dictionary<string, string> _columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Every column of the imported list by its header (including ones the app does not know, e.g. Phone, BloodType).
        /// Printed by a Card Designer "Custom field". Hidden from the student grid.
        /// </summary>
        [Browsable(false)]
        public IDictionary<string, string> Columns { get { return _columns; } }

        /// <summary>
        /// Value of a column by header (case-insensitive), or "" when the list has no such column.
        /// A student column may be named the old way too: "StudentId" finds student_id.
        /// </summary>
        public string Column(string header)
        {
            string value;
            if (string.IsNullOrEmpty(header)) return "";
            if (_columns.TryGetValue(header.Trim(), out value)) return value ?? "";
            string column = Services.StudentColumns.Find(header);
            return column != null && _columns.TryGetValue(column, out value) ? value ?? "" : "";
        }
    }
}
