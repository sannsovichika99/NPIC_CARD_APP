using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NPIC_CARD_APP.Models;

namespace NPIC_CARD_APP.Services
{
    /// <summary>What happened when an RFID UID was saved for a student.</summary>
    public enum RfidSaveStatus
    {
        /// <summary>First card for this student.</summary>
        Inserted,
        /// <summary>The student's previous card UID was replaced.</summary>
        Updated,
        /// <summary>The same UID was already saved for this student (only the upload time changed).</summary>
        Unchanged,
        /// <summary>The student already has another UID; nothing saved. Call again with replace = true.</summary>
        NeedsReplace,
        /// <summary>The UID belongs to another student; nothing saved.</summary>
        InUse
    }

    public class RfidSaveResult
    {
        public RfidSaveStatus Status { get; set; }
        /// <summary>The student's UID before this save (NeedsReplace / Updated).</summary>
        public string PreviousUid { get; set; }
        /// <summary>The student who already owns the UID (InUse).</summary>
        public string OtherStudentId { get; set; }
    }

    /// <summary>One saved card: a row of the RFID CSV file.</summary>
    public class RfidRecord
    {
        public string StudentId { get; set; }
        public string RfidUid { get; set; }
        public string NameKh { get; set; }
        public string NameEn { get; set; }
        public DateTime? PrintedAt { get; set; }
        /// <summary>When the card number was saved (tapped on the reader).</summary>
        public DateTime? UploadedAt { get; set; }
        public string UploadedBy { get; set; }
        public string Computer { get; set; }
    }

    /// <summary>
    /// Saves the RFID chip UID of a printed card to a CSV file, one row per student:
    /// <c>StudentId, RfidUid, NameKh, NameEn, PrintedAt, UploadedAt, UploadedBy, Computer</c>.
    /// The file is <see cref="AppSettings.RfidCsvPath"/>, or %AppData%\NPIC_CARD_APP\rfid-cards.csv when that is empty.
    /// It is UTF-8 with BOM (Excel shows the Khmer names) and is locked while a card is saved, so several PCs can share
    /// one file in a network folder. Columns added by hand in Excel are kept.
    /// </summary>
    public static class RfidCardWriter
    {
        public const string DefaultFileName = "rfid-cards.csv";
        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";
        private const int LockRetries = 15;          // × 200 ms: wait up to 3 s for another PC to finish its save
        private const int SharingViolation = 32, LockViolation = 33;

        private static readonly string[] Columns =
            { "StudentId", "RfidUid", "NameKh", "NameEn", "PrintedAt", "UploadedAt", "UploadedBy", "Computer" };

        private static readonly Encoding Utf8Bom = new UTF8Encoding(true);
        private static readonly Regex UidPattern = new Regex(@"^[0-9A-Z:\-]{4,64}$");

        /// <summary>
        /// Text from the reader / textbox → stored form: spaces removed, upper case.
        /// Returns null when it does not look like a card UID (hex or decimal digits, ':' or '-', 4–64 characters).
        /// </summary>
        public static string NormalizeUid(string text)
        {
            string uid = Regex.Replace(text ?? "", @"\s+", "").ToUpperInvariant();
            return UidPattern.IsMatch(uid) ? uid : null;
        }

        /// <summary>%AppData%\NPIC_CARD_APP\rfid-cards.csv</summary>
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NPIC_CARD_APP", DefaultFileName);
            }
        }

        /// <summary>The RFID CSV file in use: the chosen one, or <see cref="DefaultPath"/>.</summary>
        public static string FilePath(AppSettings settings)
        {
            return string.IsNullOrWhiteSpace(settings.RfidCsvPath) ? DefaultPath : settings.RfidCsvPath.Trim();
        }

        /// <summary>
        /// Saves <paramref name="uid"/> (already normalized) for the student in the RFID CSV file.
        /// Returns NeedsReplace instead of overwriting another UID unless <paramref name="replace"/> is true.
        /// </summary>
        public static Task<RfidSaveResult> SaveAsync(AppSettings settings, Student student, string uid,
            DateTime? printedAt, bool replace, CancellationToken cancel)
        {
            if (student == null || string.IsNullOrWhiteSpace(student.StudentId))
                throw new ArgumentException("The student has no StudentId.");
            if (NormalizeUid(uid) != uid) throw new ArgumentException("Not a valid RFID card number: " + uid);

            string path = FilePath(settings);
            return Task.Run(() => Save(path, student, uid, printedAt, replace, cancel), cancel);
        }

        /// <summary>
        /// Checks that <paramref name="path"/> can be used as the RFID file: missing, empty, or a CSV with
        /// StudentId and RfidUid columns. Returns the number of saved cards (0 for a new file).
        /// </summary>
        public static int CheckFile(string path)
        {
            if (!File.Exists(path)) return 0;
            List<string> header;
            return Parse(File.ReadAllText(path, Encoding.UTF8), path, out header).Count;
        }

        /// <summary>
        /// Every saved card, in file order. Empty when the file does not exist yet.
        /// Reads while Excel has the file open. Throws <see cref="InvalidDataException"/> for a file that is not an RFID list.
        /// </summary>
        public static List<RfidRecord> Load(string path)
        {
            var list = new List<RfidRecord>();
            if (!File.Exists(path)) return list;

            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                text = reader.ReadToEnd();

            List<string> header;
            foreach (Dictionary<string, string> row in Parse(text, path, out header))
            {
                list.Add(new RfidRecord
                {
                    StudentId = Get(row, "StudentId"),
                    RfidUid = Get(row, "RfidUid"),
                    NameKh = Get(row, "NameKh"),
                    NameEn = Get(row, "NameEn"),
                    PrintedAt = ParseDate(Get(row, "PrintedAt")),
                    UploadedAt = ParseDate(Get(row, "UploadedAt")),
                    UploadedBy = Get(row, "UploadedBy"),
                    Computer = Get(row, "Computer")
                });
            }
            return list;
        }

        /// <summary>Removes the cards of these students from the file. Returns how many rows were removed.</summary>
        public static Task<int> DeleteAsync(string path, ICollection<string> studentIds, CancellationToken cancel)
        {
            var ids = new HashSet<string>(studentIds.Select(i => (i ?? "").Trim()), StringComparer.OrdinalIgnoreCase);
            return Task.Run(() =>
            {
                if (!File.Exists(path)) return 0;
                return Update(path, cancel, (List<string> header, List<Dictionary<string, string>> rows, out bool write) =>
                {
                    int removed = rows.RemoveAll(r => ids.Contains(Get(r, "StudentId")));
                    write = removed > 0;
                    return removed;
                });
            }, cancel);
        }

        // ===================== Save =====================

        private static RfidSaveResult Save(string path, Student student, string uid, DateTime? printedAt, bool replace,
            CancellationToken cancel)
        {
            string id = student.StudentId.Trim();
            return Update(path, cancel, (List<string> header, List<Dictionary<string, string>> rows, out bool write) =>
            {
                write = false;

                // ---- Check: one row per student + unique card number ----
                Dictionary<string, string> other = rows.FirstOrDefault(r =>
                    Same(Get(r, "RfidUid"), uid) && !Same(Get(r, "StudentId"), id));
                if (other != null)
                    return new RfidSaveResult { Status = RfidSaveStatus.InUse, OtherStudentId = Get(other, "StudentId") };

                Dictionary<string, string> row = rows.FirstOrDefault(r => Same(Get(r, "StudentId"), id));
                string previous = row == null ? null : Get(row, "RfidUid");
                if (previous == "") previous = null;

                if (previous != null && !Same(previous, uid) && !replace)
                    return new RfidSaveResult { Status = RfidSaveStatus.NeedsReplace, PreviousUid = previous };

                // ---- Change ----
                if (row == null)
                {
                    row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    rows.Add(row);
                }
                row["StudentId"] = id;
                row["RfidUid"] = uid;
                row["NameKh"] = student.NameKh ?? "";
                row["NameEn"] = student.NameEn ?? "";
                if (printedAt.HasValue) row["PrintedAt"] = printedAt.Value.ToString(DateFormat, CultureInfo.InvariantCulture);
                else if (!row.ContainsKey("PrintedAt")) row["PrintedAt"] = "";
                row["UploadedAt"] = DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture);
                row["UploadedBy"] = Environment.UserName ?? "";
                row["Computer"] = Environment.MachineName ?? "";
                write = true;

                RfidSaveStatus status = previous == null ? RfidSaveStatus.Inserted
                    : Same(previous, uid) ? RfidSaveStatus.Unchanged
                    : RfidSaveStatus.Updated;
                return new RfidSaveResult { Status = status, PreviousUid = previous };
            });
        }

        private delegate T RowsChange<T>(List<string> header, List<Dictionary<string, string>> rows, out bool write);

        /// <summary>
        /// Locked read → change → write of the RFID file. <paramref name="change"/> edits the rows and sets write = true
        /// when the file must be rewritten. A backup (.bak) of the old content is made before writing.
        /// </summary>
        private static T Update<T>(string path, CancellationToken cancel, RowsChange<T> change)
        {
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new InvalidOperationException("The folder of the RFID file cannot be created:\n" + path + "\n\n" + ex.Message, ex);
            }

            using (FileStream file = OpenLocked(path, cancel))
            {
                // ---- Read ----
                var original = new MemoryStream();
                file.CopyTo(original);
                byte[] originalBytes = original.ToArray();
                string text;
                using (var reader = new StreamReader(new MemoryStream(originalBytes), Encoding.UTF8, true))
                    text = reader.ReadToEnd();

                List<string> header;
                List<Dictionary<string, string>> rows = Parse(text, path, out header);

                bool write;
                T result = change(header, rows, out write);
                if (!write) return result;

                // ---- Write: backup first, then rewrite the locked file in place ----
                cancel.ThrowIfCancellationRequested();
                try
                {
                    if (originalBytes.Length > 0) File.WriteAllBytes(path + ".bak", originalBytes);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // A missing backup must not stop the save.
                }

                byte[] data = Utf8Bom.GetBytes(Serialize(header, rows));
                byte[] bom = Utf8Bom.GetPreamble();
                file.SetLength(0);
                file.Write(bom, 0, bom.Length);
                file.Write(data, 0, data.Length);
                file.Flush(true);
                return result;
            }
        }

        private static DateTime? ParseDate(string text)
        {
            DateTime value;
            if (DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)) return value;
            // Excel re-saves dates in the PC's own format.
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out value)) return value;
            return null;
        }

        /// <summary>Opens the file for exclusive read/write, waiting briefly when another PC is saving to it.</summary>
        private static FileStream OpenLocked(string path, CancellationToken cancel)
        {
            for (int attempt = 0; ; attempt++)
            {
                cancel.ThrowIfCancellationRequested();
                try
                {
                    return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException ex) when (IsLocked(ex))
                {
                    if (attempt >= LockRetries)
                        throw new InvalidOperationException(
                            "The RFID file is open in another program (Excel?), so it cannot be changed.\n\n" +
                            "Close the file, then try again.\n\n" + path, ex);
                    Thread.Sleep(200);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new InvalidOperationException(
                        "Windows does not allow this app to write the RFID file:\n" + path + "\n\n" +
                        "Choose another location with 'Change…' on the RFID tab, or check that the file is not read-only.", ex);
                }
                catch (DirectoryNotFoundException ex)
                {
                    throw new InvalidOperationException("The folder of the RFID file was not found:\n" + path +
                        "\n\nIf it is on a network drive, check that it is connected.", ex);
                }
            }
        }

        private static bool IsLocked(IOException ex)
        {
            int code = Marshal.GetHRForException(ex) & 0xFFFF;
            return code == SharingViolation || code == LockViolation;
        }

        // ===================== CSV =====================

        /// <summary>Rows as column → value. The header keeps any extra columns; the standard ones are added if missing.</summary>
        private static List<Dictionary<string, string>> Parse(string text, string path, out List<string> header)
        {
            var rows = new List<Dictionary<string, string>>();
            header = null;
            foreach (string line in (text ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                List<string> cells = CsvStudentLoader.ParseLine(line);
                if (header == null)
                {
                    header = cells.Select(c => c.Trim().TrimStart('\uFEFF')).ToList();
                    if (!header.Contains("StudentId", StringComparer.OrdinalIgnoreCase) ||
                        !header.Contains("RfidUid", StringComparer.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            "This file is not an RFID card list (it needs the columns StudentId and RfidUid):\n" + path);
                    continue;
                }

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < header.Count && i < cells.Count; i++)
                    if (header[i].Length > 0 && !row.ContainsKey(header[i])) row[header[i]] = cells[i].Trim();
                if (Get(row, "StudentId").Length > 0) rows.Add(row);
            }

            if (header == null) header = new List<string>();
            foreach (string column in Columns)
                if (!header.Contains(column, StringComparer.OrdinalIgnoreCase)) header.Add(column);
            return rows;
        }

        private static string Serialize(List<string> header, List<Dictionary<string, string>> rows)
        {
            var sb = new StringBuilder(string.Join(",", header.Select(Escape))).Append("\r\n");
            foreach (Dictionary<string, string> row in rows)
                sb.Append(string.Join(",", header.Select(h => Escape(Get(row, h))))).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>CSV-quotes a value; line breaks become spaces so one record is always one line.</summary>
        private static string Escape(string value)
        {
            value = (value ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            if (value.IndexOfAny(new[] { ',', '"' }) < 0 && value.Trim() == value) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string Get(Dictionary<string, string> row, string column)
        {
            string value;
            return column.Length > 0 && row.TryGetValue(column, out value) ? value ?? "" : "";
        }

        private static bool Same(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
