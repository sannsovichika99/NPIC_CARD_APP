using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NPIC_CARD_APP.Models;

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// Downloads the student list from a web API with an HTTP GET.
    /// <para>Accepted responses:</para>
    /// <list type="bullet">
    /// <item>A JSON array of students: <c>[ { "StudentId": "NPIC24001", "NameKh": "…", … }, … ]</c></item>
    /// <item>A JSON object holding that array in <c>data</c>, <c>students</c>, <c>items</c>, <c>results</c> or
    /// <c>records</c> (Laravel / Django REST style). Further pages are followed through <c>next</c>,
    /// <c>next_page_url</c> or <c>links.next</c>.</item>
    /// <item>CSV text with the same header as the CSV import.</item>
    /// </list>
    /// Field names are the CSV column names. Case, '_' and '-' are ignored, so <c>student_id</c> and
    /// <c>studentId</c> both mean StudentId. ISO dates (2007-03-15 or 2007-03-15T00:00:00Z) become dd/MM/yyyy.
    /// </summary>
    public static class ApiStudentLoader
    {
        public const int TimeoutSeconds = 60;
        private const int MaxPages = 500;

        private static readonly string[] Columns =
        {
            "StudentId", "NameKh", "NameEn", "Gender", "DateOfBirth", "Faculty", "Department",
            "Level", "Generation", "ValidUntil", "QrData"
        };
        private static readonly string[] DateColumns = { "DateOfBirth", "ValidUntil" };
        private static readonly string[] ListKeys = { "data", "students", "items", "results", "records" };
        private static readonly string[] NextKeys = { "next", "next_page_url", "nextPageUrl" };

        /// <summary>"studentid" → "StudentId", "nameen" → "NameEn", …</summary>
        private static readonly Dictionary<string, string> CanonicalByKey = BuildCanonicalMap();

        // One shared client for the app's lifetime (creating one per request leaks sockets).
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NPIC_CARD_APP/1.0");
            return client;
        }

        /// <summary>
        /// Downloads every page of students from <paramref name="url"/>.
        /// <paramref name="token"/> is sent as "Authorization: Bearer token"; written as "Header-Name: value"
        /// (e.g. "X-API-Key: abc123") it is sent as that header instead. Empty = no authentication.
        /// </summary>
        public static async Task<List<Student>> LoadAsync(string url, string token, CancellationToken cancel)
        {
            Uri first;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out first) ||
                (first.Scheme != Uri.UriSchemeHttp && first.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Enter the full API address, starting with http:// or https://");

            var records = new List<Dictionary<string, object>>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            Uri page = first;

            while (page != null && visited.Add(page.AbsoluteUri))   // the Add() check stops a page that links to itself
            {
                if (visited.Count > MaxPages)
                    throw new InvalidDataException("The API returned more than " + MaxPages + " pages. Ask for bigger pages (e.g. ?per_page=500).");

                string body = (await GetAsync(page, token, cancel).ConfigureAwait(false)).TrimStart('﻿', ' ', '\t', '\r', '\n');

                if (body.StartsWith("<"))
                    throw new InvalidDataException("The server returned a web page instead of student data.\n\n" +
                        "Check the address (it must be the API endpoint, not a website page) and the access token.");

                if (!body.StartsWith("[") && !body.StartsWith("{"))
                {
                    if (page != first) throw new InvalidDataException("Page " + page + " is not JSON.");
                    return CsvStudentLoader.FromText(body, "API CSV header");
                }

                object root = ParseJson(body);
                records.AddRange(ExtractRecords(root));
                page = NextPage(root, page, first);
            }

            return ToStudents(records);
        }

        // ===================== HTTP =====================
        private static async Task<string> GetAsync(Uri uri, string token, CancellationToken cancel)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Accept.ParseAdd("text/csv; q=0.9");
                request.Headers.Accept.ParseAdd("*/*; q=0.1");
                AddAuth(request, token);

                HttpResponseMessage response;
                try
                {
                    response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
                {
                    throw new TimeoutException(string.Format(
                        "{0} did not answer within {1} seconds.", uri.Host, TimeoutSeconds));
                }

                using (response)
                {
                    // Decode the bytes ourselves: without a charset, ReadAsStringAsync may fall back to
                    // ISO-8859-1 and garble the Khmer names. JSON is UTF-8 unless the server says otherwise.
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    string body = Decode(bytes, response.Content.Headers.ContentType);

                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException(string.Format("The server answered {0} {1}.\n\n{2}{3}{4}",
                            (int)response.StatusCode, response.ReasonPhrase, uri, Hint(response.StatusCode), Excerpt(body)));
                    return body;
                }
            }
        }

        private static void AddAuth(HttpRequestMessage request, string token)
        {
            token = (token ?? "").Trim();
            if (token.Length == 0) return;

            // "X-API-Key: abc123" → custom header (header names never contain spaces)
            int colon = token.IndexOf(':');
            if (colon > 0 && token.IndexOf(' ', 0, colon) < 0)
            {
                string name = token.Substring(0, colon).Trim();
                string value = token.Substring(colon + 1).Trim();
                if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    request.Headers.TryAddWithoutValidation("Authorization", value);
                else if (!request.Headers.TryAddWithoutValidation(name, value))
                    throw new ArgumentException("'" + name + "' cannot be used as a request header.");
                return;
            }

            // A pasted "Bearer abc123" works too.
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token.Substring(7).Trim();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        private static string Decode(byte[] bytes, MediaTypeHeaderValue contentType)
        {
            Encoding encoding = new UTF8Encoding(false);
            string charset = contentType == null ? null : contentType.CharSet;
            if (!string.IsNullOrWhiteSpace(charset))
            {
                try { encoding = Encoding.GetEncoding(charset.Trim('"', ' ')); }
                catch (ArgumentException) { /* unknown charset: keep UTF-8 */ }
            }
            return encoding.GetString(bytes);
        }

        private static string Hint(HttpStatusCode status)
        {
            switch ((int)status)
            {
                case 401:
                case 403: return "\n\nCheck the access token.";
                case 404: return "\n\nCheck the API address.";
                case 429: return "\n\nToo many requests — wait a minute and try again.";
                default: return (int)status >= 500 ? "\n\nThe problem is on the server; try again later or contact its administrator." : "";
            }
        }

        private static string Excerpt(string body)
        {
            body = (body ?? "").Trim();
            if (body.Length == 0 || body.StartsWith("<")) return "";   // skip HTML error pages
            return "\n\nServer message:\n" + (body.Length > 300 ? body.Substring(0, 300) + "…" : body);
        }

        // ===================== JSON =====================
        private static object ParseJson(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            try
            {
                return serializer.DeserializeObject(json);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                throw new InvalidDataException("The API response is not valid JSON: " + ex.Message, ex);
            }
        }

        private static List<Dictionary<string, object>> ExtractRecords(object root)
        {
            IList list = AsList(root) ?? FindList(root as Dictionary<string, object>, 0);
            if (list == null)
                throw new InvalidDataException("The API response contains no student list.\n\n" +
                    "Expected a JSON array, or an object with a \"data\", \"students\", \"items\", \"results\" or \"records\" array.");

            var records = new List<Dictionary<string, object>>(list.Count);
            foreach (object item in list)
            {
                var record = item as Dictionary<string, object>;
                if (record == null)
                    throw new InvalidDataException("Every entry in the student list must be a JSON object { … }.");
                records.Add(record);
            }
            return records;
        }

        /// <summary>Finds the student array inside a wrapper object such as { "data": [ … ], "meta": { … } }.</summary>
        private static IList FindList(Dictionary<string, object> obj, int depth)
        {
            if (obj == null || depth > 2) return null;

            foreach (string key in ListKeys)
            {
                object value = GetIgnoreCase(obj, key);
                IList list = AsList(value) ?? FindList(value as Dictionary<string, object>, depth + 1);
                if (list != null) return list;
            }

            // Otherwise use the object's only array, e.g. { "count": 3, "rows": [ … ] }
            IList only = null;
            foreach (object value in obj.Values)
            {
                IList list = AsList(value);
                if (list == null) continue;
                if (only != null) return null;   // two arrays: ambiguous
                only = list;
            }
            return only;
        }

        private static Uri NextPage(object root, Uri current, Uri first)
        {
            var obj = root as Dictionary<string, object>;
            if (obj == null) return null;

            string link = null;
            foreach (string key in NextKeys)
            {
                link = GetIgnoreCase(obj, key) as string;
                if (!string.IsNullOrWhiteSpace(link)) break;
            }
            if (string.IsNullOrWhiteSpace(link))
                link = GetIgnoreCase(GetIgnoreCase(obj, "links") as Dictionary<string, object>, "next") as string;
            if (string.IsNullOrWhiteSpace(link)) return null;

            Uri next;
            if (!Uri.TryCreate(current, link.Trim(), out next))
                throw new InvalidDataException("The API's next-page link is not a valid address: " + link);

            // The token is sent with every page, so only follow links to the same server.
            if (Uri.Compare(next, first, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
                throw new InvalidDataException("The API's next-page link points to another server (" + next.Host + ").\n\n" +
                    "It was not followed, so the access token is not sent there.");
            return next;
        }

        private static IList AsList(object value)
        {
            return value is string ? null : value as IList;   // DeserializeObject gives object[] for arrays
        }

        private static object GetIgnoreCase(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return null;
            object value;
            if (obj.TryGetValue(key, out value)) return value;
            foreach (KeyValuePair<string, object> pair in obj)
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            return null;
        }

        // ===================== Records → students =====================
        /// <summary>
        /// Turns the JSON objects into a table and hands it to the CSV loader, so the API accepts exactly the
        /// same columns as the CSV and Excel imports.
        /// </summary>
        private static List<Student> ToStudents(List<Dictionary<string, object>> records)
        {
            if (records.Count == 0) return new List<Student>();

            var header = new List<string>();
            var columnIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var cellsByRecord = new List<Dictionary<string, string>>(records.Count);

            foreach (Dictionary<string, object> record in records)
            {
                var cells = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> field in record)
                {
                    string column;
                    if (!CanonicalByKey.TryGetValue(Squash(field.Key), out column)) continue;   // unknown field: ignore

                    string text = CellText(field.Value);
                    if (Array.IndexOf(DateColumns, column) >= 0) text = NormalizeDate(text);

                    string existing;
                    if (cells.TryGetValue(column, out existing) && existing.Length > 0) continue;   // student_id and studentId both present: first wins
                    cells[column] = text;

                    if (!columnIndex.ContainsKey(column))
                    {
                        columnIndex[column] = header.Count;
                        header.Add(column);
                    }
                }
                cellsByRecord.Add(cells);
            }

            if (!columnIndex.ContainsKey("StudentId"))
            {
                var found = new List<string>(records[0].Keys);
                throw new InvalidDataException("The API's students have no StudentId field (student_id and studentId work too).\n\n" +
                    "Fields found: " + (found.Count == 0 ? "(none)" : string.Join(", ", found)));
            }

            var rows = new List<List<string>>(records.Count + 1) { header };
            foreach (Dictionary<string, string> cells in cellsByRecord)
            {
                var row = new List<string>(header.Count);
                foreach (string column in header)
                {
                    string text;
                    row.Add(cells.TryGetValue(column, out text) ? text : "");
                }
                rows.Add(row);
            }
            return CsvStudentLoader.FromRows(rows, "API response");
        }

        private static string CellText(object value)
        {
            if (value == null) return "";
            if (value is string) return (string)value;
            if (value is bool) return (bool)value ? "true" : "false";
            if (value is DateTime) return ((DateTime)value).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            // { "faculty": { "id": 3, "name": "…" } } → the name
            var obj = value as Dictionary<string, object>;
            if (obj != null) return CellText(GetIgnoreCase(obj, "name"));
            if (value is IList) return "";

            var formattable = value as IFormattable;   // int, long, decimal, double
            return formattable != null ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString();
        }

        /// <summary>"2007-03-15", "2007-03-15T00:00:00Z" or "2007-03-15 00:00:00" → "15/03/2007"; anything else unchanged.</summary>
        private static string NormalizeDate(string text)
        {
            text = (text ?? "").Trim();
            DateTime date;
            if (text.Length >= 10 && (text.Length == 10 || text[10] == 'T' || text[10] == ' ') &&
                DateTime.TryParseExact(text.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            return text;
        }

        private static Dictionary<string, string> BuildCanonicalMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string column in Columns) map[Squash(column)] = column;
            return map;
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
