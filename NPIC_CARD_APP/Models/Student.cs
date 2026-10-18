using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace NPIC_CARD_APP.Models
{
    public class Student
    {
        public string StudentId { get; set; }    // អត្តលេខ
        public string NameKh { get; set; }       // គោត្តនាម-នាម
        public string NameEn { get; set; }       // អក្សរឡាតាំង
        public string Gender { get; set; }       // M / F / ប្រុស / ស្រី
        public string DateOfBirth { get; set; }  // dd/MM/yyyy → ២៤ មករា ២០០៨
        public string Faculty { get; set; }      // មហាវិទ្យាល័យ
        public string Level { get; set; }        // កម្រិត
        public string Generation { get; set; }   // ជំនាន់ទី
        public string ValidUntil { get; set; }   // dd/MM/yyyy → ផុតកំណត់ថ្ងៃទី...
        public string QrData { get; set; }       // optional: text/URL for this student's QR

        private readonly Dictionary<string, string> _columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Every column of the imported list by its header (including ones the app does not know, e.g. Phone, BloodType).
        /// Printed by a Card Designer "Custom field". Hidden from the student grid.
        /// </summary>
        [Browsable(false)]
        public IDictionary<string, string> Columns { get { return _columns; } }

        /// <summary>Value of a column by header (case-insensitive), or "" when the list has no such column.</summary>
        public string Column(string header)
        {
            string value;
            return !string.IsNullOrEmpty(header) && _columns.TryGetValue(header.Trim(), out value) ? value ?? "" : "";
        }
    }
}
