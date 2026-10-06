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
    }
}
