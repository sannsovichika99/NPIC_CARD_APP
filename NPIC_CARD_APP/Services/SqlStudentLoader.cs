using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NPIC_CARD_APP.Models;

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// Reads the student list from a Microsoft SQL Server database.
    /// <para>The source is either a table / view name (<c>dbo.Students</c>) or a SELECT query. Column names are the
    /// CSV column names; case, '_' and '-' are ignored, so <c>student_id</c> and <c>StudentID</c> both mean StudentId.
    /// Date columns become dd/MM/yyyy. Only SELECT (or WITH … SELECT) is run, so the database is never changed.</para>
    /// </summary>
    public static class SqlStudentLoader
    {
        public const int ConnectTimeoutSeconds = 15;
        public const int QueryTimeoutSeconds = 120;

        private static readonly string[] Columns =
        {
            "StudentId", "NameKh", "NameEn", "Gender", "DateOfBirth", "Faculty", "Department",
            "Level", "Generation", "ValidUntil", "QrData"
        };

        /// <summary>dbo.Students, [dbo].[Students], Students, NPIC.dbo.Students …</summary>
        private static readonly Regex TableName = new Regex(
            @"^\s*(?:(?:\[[^\]]+\]|[\p{L}_][\p{L}\p{N}_@$#]*)\s*\.\s*){0,2}(?:\[[^\]]+\]|[\p{L}_][\p{L}\p{N}_@$#]*)\s*$");

        private static readonly Regex ReadOnlyStart = new Regex(@"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase);

        /// <summary>Statements that change data or the server; refused even inside a SELECT batch.</summary>
        private static readonly Regex Forbidden = new Regex(
            @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|GRANT|REVOKE|DENY|INTO|SHUTDOWN|BACKUP|RESTORE|DBCC)\b",
            RegexOptions.IgnoreCase);

        public static string BuildConnectionString(string server, string database, bool windowsAuth, string user,
            string password, bool encrypt, bool trustServerCertificate)
        {
            server = (server ?? "").Trim();
            if (server.Length == 0) throw new ArgumentException("Enter the SQL Server name, e.g. localhost\\SQLEXPRESS or 192.168.1.10,1433");
            if (!windowsAuth && string.IsNullOrWhiteSpace(user)) throw new ArgumentException("Enter the SQL Server login name.");

            var b = new SqlConnectionStringBuilder
            {
                DataSource = server,
                IntegratedSecurity = windowsAuth,
                Encrypt = encrypt,
                TrustServerCertificate = trustServerCertificate,
                ConnectTimeout = ConnectTimeoutSeconds,
                ApplicationName = "NPIC_CARD_APP"
            };
            if (!string.IsNullOrWhiteSpace(database)) b.InitialCatalog = database.Trim();
            if (!windowsAuth)
            {
                b.UserID = user.Trim();
                b.Password = password ?? "";
            }
            return b.ConnectionString;
        }

        /// <summary>Opens a connection and returns a one-line description, e.g. "SQL Server 16.0.1000 · database NPIC".</summary>
        public static async Task<string> TestAsync(string connectionString, CancellationToken cancel)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                await OpenAsync(connection, cancel).ConfigureAwait(false);
                return string.Format("SQL Server {0}  ·  database {1}", connection.ServerVersion, connection.Database);
            }
        }

        /// <summary>
        /// Runs <paramref name="tableOrQuery"/> and returns the students.
        /// A bare table / view name becomes <c>SELECT * FROM name</c>.
        /// </summary>
        public static async Task<List<Student>> LoadAsync(string connectionString, string tableOrQuery, CancellationToken cancel)
        {
            string sql = ToSql(tableOrQuery);

            using (var connection = new SqlConnection(connectionString))
            {
                await OpenAsync(connection, cancel).ConfigureAwait(false);
                using (var command = new SqlCommand(sql, connection) { CommandTimeout = QueryTimeoutSeconds })
                using (SqlDataReader reader = await ExecuteAsync(command, cancel).ConfigureAwait(false))
                {
                    // Header row: the CSV column a SQL column stands for (student_id → StudentId); others are kept
                    // under their own name and ignored by the CSV loader.
                    var header = new List<string>(reader.FieldCount);
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        string name = Canonical(reader.GetName(i));
                        header.Add(seen.Add(name) ? name : "");   // two columns for StudentId: the first wins
                    }
                    if (!seen.Contains("StudentId"))
                        throw new InvalidDataException("The SQL result has no StudentId column (student_id and StudentID work too).\n\n" +
                            "Columns found: " + string.Join(", ", ColumnNames(reader)) + "\n\n" +
                            "Rename it in the query, e.g.  SELECT code AS StudentId, …");

                    var rows = new List<List<string>> { header };
                    while (await reader.ReadAsync(cancel).ConfigureAwait(false))
                    {
                        var row = new List<string>(reader.FieldCount);
                        for (int i = 0; i < reader.FieldCount; i++) row.Add(CellText(reader.GetValue(i)));
                        rows.Add(row);
                    }
                    return CsvStudentLoader.FromRows(rows, "SQL result");
                }
            }
        }

        /// <summary>Table name → SELECT; a query is checked to be read-only.</summary>
        internal static string ToSql(string tableOrQuery)
        {
            string text = (tableOrQuery ?? "").Trim().TrimEnd(';').Trim();
            if (text.Length == 0) throw new ArgumentException("Enter a table name (e.g. dbo.Students) or a SELECT query.");

            if (TableName.IsMatch(text) && !ReadOnlyStart.IsMatch(text)) return "SELECT * FROM " + QuoteName(text);

            if (!ReadOnlyStart.IsMatch(text) || text.IndexOf(';') >= 0 || Forbidden.IsMatch(StripLiterals(text)))
                throw new ArgumentException("Only one SELECT query is allowed — the app never changes the database.\n\n" +
                    "Example:  SELECT StudentId, NameKh, NameEn, Gender, DateOfBirth, Faculty, Level, Generation, ValidUntil FROM dbo.Students");
            return text;
        }

        // ===================== SQL helpers =====================
        private static async Task OpenAsync(SqlConnection connection, CancellationToken cancel)
        {
            try
            {
                await connection.OpenAsync(cancel).ConfigureAwait(false);
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(Explain(ex), ex);
            }
        }

        private static async Task<SqlDataReader> ExecuteAsync(SqlCommand command, CancellationToken cancel)
        {
            try
            {
                return await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancel).ConfigureAwait(false);
            }
            catch (SqlException ex) when (!cancel.IsCancellationRequested)
            {
                throw new InvalidOperationException("The query failed: " + ex.Message, ex);
            }
        }

        /// <summary>Plain-language hint for the usual connection errors.</summary>
        private static string Explain(SqlException ex)
        {
            switch (ex.Number)
            {
                case 18456:
                    return "Login failed. Check the user name and password, and that the server allows SQL Server authentication.";
                case 4060:
                    return "The database cannot be opened. Check the database name and that this login may use it.";
                case -2:
                    return "The server did not answer within " + ConnectTimeoutSeconds + " seconds.";
                case 2:
                case 53:
                case 26:
                case -1:
                    return "The SQL Server could not be found.\n\nCheck the server name (e.g. PCNAME\\SQLEXPRESS or 192.168.1.10,1433), " +
                           "that SQL Server allows remote connections over TCP/IP, and that the firewall lets port 1433 through.";
                default:
                    if (ex.Message.IndexOf("certificate", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "The server's certificate is not trusted. Tick “Trust server certificate” or turn off encryption.";
                    return "Cannot connect to SQL Server: " + ex.Message;
            }
        }

        /// <summary>True for a plain table / view name: Students, dbo.Students, [NPIC].[dbo].[Students] …</summary>
        internal static bool IsTableName(string text)
        {
            return TableName.IsMatch(text ?? "") && !ReadOnlyStart.IsMatch(text ?? "");
        }

        /// <summary>"dbo.Students" → "[dbo].[Students]" (names already in brackets are kept).</summary>
        internal static string QuoteName(string dotted)
        {
            var parts = new List<string>();
            foreach (Match m in Regex.Matches(dotted, @"\[[^\]]+\]|[^.\s\[\]]+"))
            {
                string part = m.Value;
                parts.Add(part.StartsWith("[") ? part : "[" + part.Replace("]", "]]") + "]");
            }
            return string.Join(".", parts);
        }

        /// <summary>Removes 'text' literals and comments so words inside them are not mistaken for commands.</summary>
        private static string StripLiterals(string sql)
        {
            return Regex.Replace(sql, @"'(?:[^']|'')*'|--[^\r\n]*|/\*.*?\*/|\[[^\]]*\]", " ", RegexOptions.Singleline);
        }

        private static IEnumerable<string> ColumnNames(IDataRecord reader)
        {
            for (int i = 0; i < reader.FieldCount; i++) yield return reader.GetName(i);
        }

        // ===================== Values =====================
        private static string CellText(object value)
        {
            if (value == null || value is DBNull) return "";
            if (value is string) return (string)value;
            if (value is DateTime) return ((DateTime)value).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            if (value is DateTimeOffset) return ((DateTimeOffset)value).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            if (value is bool) return (bool)value ? "true" : "false";
            if (value is byte[]) return "";   // photo / binary columns are not card text
            var formattable = value as IFormattable;   // int, decimal, Guid …
            return formattable != null ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString();
        }

        private static string Canonical(string columnName)
        {
            string key = Squash(columnName);
            foreach (string column in Columns)
                if (Squash(column) == key) return column;
            return columnName ?? "";
        }

        /// <summary>Lower-case letters and digits only: "Student_ID" → "studentid".</summary>
        private static string Squash(string key)
        {
            var sb = new StringBuilder(key == null ? 0 : key.Length);
            foreach (char ch in key ?? "")
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }
    }
}
