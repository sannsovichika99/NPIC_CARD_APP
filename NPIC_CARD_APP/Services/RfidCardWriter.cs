using System;
using System.Data;
using System.Data.SqlClient;
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

    /// <summary>
    /// Saves the RFID chip UID of a printed card to SQL Server, one row per student:
    /// <c>StudentId (key), RfidUid (unique), NameKh, NameEn, PrintedAt, UploadedAt, UploadedBy, Computer</c>.
    /// The table is created the first time if it does not exist. Uses the SQL Server… connection settings.
    /// </summary>
    public static class RfidCardWriter
    {
        public const string DefaultTable = "dbo.StudentRfid";

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

        /// <summary>Connection string from the SQL Server… page settings.</summary>
        public static string ConnectionString(AppSettings settings)
        {
            return SqlStudentLoader.BuildConnectionString(settings.SqlServer, settings.SqlDatabase, settings.SqlWindowsAuth,
                settings.SqlUser, settings.SqlPassword, settings.SqlEncrypt, settings.SqlTrustServerCertificate);
        }

        /// <summary>"[dbo].[StudentRfid]" from the setting; checks it is a plain table name.</summary>
        public static string TableName(AppSettings settings, out int parts)
        {
            string table = string.IsNullOrWhiteSpace(settings.RfidTable) ? DefaultTable : settings.RfidTable.Trim();
            if (!SqlStudentLoader.IsTableName(table))
                throw new ArgumentException("The RFID table name is not valid: " + table + "\n\nUse a name like dbo.StudentRfid.");
            string quoted = SqlStudentLoader.QuoteName(table);
            parts = Regex.Matches(quoted, @"\[(?:[^\]]|\]\])*\]").Count;
            return quoted;
        }

        /// <summary>
        /// Saves <paramref name="uid"/> (already normalized) for the student, in one transaction.
        /// Returns NeedsReplace instead of overwriting another UID unless <paramref name="replace"/> is true.
        /// </summary>
        public static async Task<RfidSaveResult> SaveAsync(AppSettings settings, Student student, string uid,
            DateTime? printedAt, bool replace, CancellationToken cancel)
        {
            if (student == null || string.IsNullOrWhiteSpace(student.StudentId))
                throw new ArgumentException("The student has no StudentId.");
            if (NormalizeUid(uid) != uid) throw new ArgumentException("Not a valid RFID card number: " + uid);

            int parts;
            string table = TableName(settings, out parts);
            if (parts < 3 && string.IsNullOrWhiteSpace(settings.SqlDatabase))
                throw new ArgumentException("No database is set for the RFID table.\n\n" +
                    "Enter the database name on the SQL Server… page (sidebar), then try again.");

            using (var connection = new SqlConnection(ConnectionString(settings)))
            {
                try
                {
                    await connection.OpenAsync(cancel).ConfigureAwait(false);
                    await EnsureTableAsync(connection, table, cancel).ConfigureAwait(false);

                    using (SqlTransaction tx = connection.BeginTransaction(IsolationLevel.Serializable))
                    {
                        RfidSaveResult result = await SaveInTransactionAsync(connection, tx, table, student, uid,
                            printedAt, replace, cancel).ConfigureAwait(false);
                        if (result.Status == RfidSaveStatus.NeedsReplace || result.Status == RfidSaveStatus.InUse) tx.Rollback();
                        else tx.Commit();
                        return result;
                    }
                }
                catch (SqlException ex) when (!cancel.IsCancellationRequested)
                {
                    throw new InvalidOperationException(Explain(ex, table), ex);
                }
            }
        }

        private static async Task<RfidSaveResult> SaveInTransactionAsync(SqlConnection connection, SqlTransaction tx,
            string table, Student student, string uid, DateTime? printedAt, bool replace, CancellationToken cancel)
        {
            string id = student.StudentId.Trim();

            // Another student already has this card?
            object other = await ScalarAsync(connection, tx,
                "SELECT TOP (1) StudentId FROM " + table + " WITH (UPDLOCK, HOLDLOCK) WHERE RfidUid = @uid AND StudentId <> @id",
                cancel, P("@uid", uid, 64), P("@id", id, 50)).ConfigureAwait(false);
            if (other != null && !(other is DBNull))
                return new RfidSaveResult { Status = RfidSaveStatus.InUse, OtherStudentId = Convert.ToString(other) };

            object existing = await ScalarAsync(connection, tx,
                "SELECT RfidUid FROM " + table + " WITH (UPDLOCK, HOLDLOCK) WHERE StudentId = @id",
                cancel, P("@id", id, 50)).ConfigureAwait(false);
            string previous = existing == null || existing is DBNull ? null : Convert.ToString(existing);

            if (previous != null && !string.Equals(previous, uid, StringComparison.OrdinalIgnoreCase) && !replace)
                return new RfidSaveResult { Status = RfidSaveStatus.NeedsReplace, PreviousUid = previous };

            string sql = previous == null
                ? "INSERT INTO " + table + " (StudentId, RfidUid, NameKh, NameEn, PrintedAt, UploadedAt, UploadedBy, Computer) " +
                  "VALUES (@id, @uid, @nameKh, @nameEn, @printedAt, SYSDATETIME(), @user, @computer)"
                : "UPDATE " + table + " SET RfidUid = @uid, NameKh = @nameKh, NameEn = @nameEn, " +
                  "PrintedAt = COALESCE(@printedAt, PrintedAt), UploadedAt = SYSDATETIME(), UploadedBy = @user, Computer = @computer " +
                  "WHERE StudentId = @id";

            using (var command = new SqlCommand(sql, connection, tx) { CommandTimeout = SqlStudentLoader.QueryTimeoutSeconds })
            {
                command.Parameters.Add(P("@id", id, 50));
                command.Parameters.Add(P("@uid", uid, 64));
                command.Parameters.Add(P("@nameKh", student.NameKh ?? "", 200));
                command.Parameters.Add(P("@nameEn", student.NameEn ?? "", 200));
                command.Parameters.Add(new SqlParameter("@printedAt", SqlDbType.DateTime2)
                    { Value = printedAt.HasValue ? (object)printedAt.Value : DBNull.Value });
                command.Parameters.Add(P("@user", Environment.UserName ?? "", 128));
                command.Parameters.Add(P("@computer", Environment.MachineName ?? "", 128));
                await command.ExecuteNonQueryAsync(cancel).ConfigureAwait(false);
            }

            RfidSaveStatus status = previous == null ? RfidSaveStatus.Inserted
                : string.Equals(previous, uid, StringComparison.OrdinalIgnoreCase) ? RfidSaveStatus.Unchanged
                : RfidSaveStatus.Updated;
            return new RfidSaveResult { Status = status, PreviousUid = previous };
        }

        /// <summary>Creates the RFID table when it is missing (needs CREATE TABLE permission the first time only).</summary>
        private static async Task EnsureTableAsync(SqlConnection connection, string table, CancellationToken cancel)
        {
            string sql =
                "IF OBJECT_ID(@table, N'U') IS NULL\n" +
                "CREATE TABLE " + table + " (\n" +
                "    StudentId  NVARCHAR(50)  NOT NULL PRIMARY KEY,\n" +
                "    RfidUid    NVARCHAR(64)  NOT NULL UNIQUE,\n" +
                "    NameKh     NVARCHAR(200) NULL,\n" +
                "    NameEn     NVARCHAR(200) NULL,\n" +
                "    PrintedAt  DATETIME2(0)  NULL,\n" +
                "    UploadedAt DATETIME2(0)  NOT NULL,\n" +
                "    UploadedBy NVARCHAR(128) NULL,\n" +
                "    Computer   NVARCHAR(128) NULL)";
            using (var command = new SqlCommand(sql, connection) { CommandTimeout = SqlStudentLoader.QueryTimeoutSeconds })
            {
                command.Parameters.Add(P("@table", table, 400));
                await command.ExecuteNonQueryAsync(cancel).ConfigureAwait(false);
            }
        }

        private static async Task<object> ScalarAsync(SqlConnection connection, SqlTransaction tx, string sql,
            CancellationToken cancel, params SqlParameter[] parameters)
        {
            using (var command = new SqlCommand(sql, connection, tx) { CommandTimeout = SqlStudentLoader.QueryTimeoutSeconds })
            {
                command.Parameters.AddRange(parameters);
                return await command.ExecuteScalarAsync(cancel).ConfigureAwait(false);
            }
        }

        private static SqlParameter P(string name, string value, int size)
        {
            return new SqlParameter(name, SqlDbType.NVarChar, size) { Value = value ?? "" };
        }

        private static string Explain(SqlException ex, string table)
        {
            switch (ex.Number)
            {
                case 18456:
                    return "Login failed. Check the user name and password on the SQL Server… page.";
                case 4060:
                    return "The database cannot be opened. Check the database name on the SQL Server… page.";
                case 262:
                case 229:
                case 230:
                    return "This SQL login may not write to " + table + ".\n\n" +
                           "Ask the database administrator for INSERT / UPDATE (and CREATE TABLE the first time).\n\n" + ex.Message;
                case 207:
                    return table + " exists but does not have the expected columns " +
                           "(StudentId, RfidUid, NameKh, NameEn, PrintedAt, UploadedAt, UploadedBy, Computer).\n\n" + ex.Message;
                case 2627:
                case 2601:
                    return "This RFID card was just saved for another student on another PC. Try again.";
                case -2:
                    return "SQL Server did not answer in time.";
                case 2:
                case 53:
                case 26:
                case -1:
                    return "The SQL Server could not be found. Check the server name on the SQL Server… page.";
                default:
                    return "Could not save the RFID card: " + ex.Message;
            }
        }
    }
}
