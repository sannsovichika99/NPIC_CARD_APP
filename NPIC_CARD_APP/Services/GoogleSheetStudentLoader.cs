using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NPIC_CARD_APP.Models;

namespace NPIC_CARD_APP.Services
{
    /// <summary>
    /// Reads students from a Google Sheet through its CSV export — no Google account or API key needed in the app.
    /// <para>The sheet must be shared as "Anyone with the link — Viewer", or published with File &gt; Share &gt;
    /// Publish to web. The first row holds the CSV column names (StudentId, NameKh, NameEn, …).</para>
    /// <para>Accepted links:</para>
    /// <list type="bullet">
    /// <item>The address bar of the open sheet: <c>https://docs.google.com/spreadsheets/d/{id}/edit#gid={tab}</c></item>
    /// <item>A "Publish to web" link: <c>https://docs.google.com/spreadsheets/d/e/{id}/pubhtml?gid={tab}</c></item>
    /// <item>The bare spreadsheet id.</item>
    /// </list>
    /// The tab is taken from <c>gid=</c> in the link; without it the first tab is read.
    /// </summary>
    public static class GoogleSheetStudentLoader
    {
        public const int TimeoutSeconds = 60;

        private static readonly Regex PublishedId = new Regex(@"/spreadsheets/d/e/([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant);
        private static readonly Regex SheetId = new Regex(@"/spreadsheets/d/([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant);
        private static readonly Regex BareId = new Regex(@"^[A-Za-z0-9_-]{25,}$", RegexOptions.CultureInvariant);
        private static readonly Regex Gid = new Regex(@"[#?&]gid=(\d+)", RegexOptions.CultureInvariant);

        // One shared client for the app's lifetime (creating one per request leaks sockets).
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NPIC_CARD_APP/1.0");
            return client;
        }

        /// <summary>Turns a Google Sheets link (or bare id) into its CSV download address.</summary>
        /// <exception cref="ArgumentException">The text is not a Google Sheets link.</exception>
        public static Uri ToCsvUri(string link)
        {
            link = (link ?? "").Trim();
            if (link.Length == 0)
                throw new ArgumentException("Paste the link of the Google Sheet.");

            Match gidMatch = Gid.Match(link);
            string gid = gidMatch.Success ? gidMatch.Groups[1].Value : null;

            Match m = PublishedId.Match(link);
            if (m.Success)
                return new Uri("https://docs.google.com/spreadsheets/d/e/" + m.Groups[1].Value + "/pub?output=csv" +
                               (gid != null ? "&single=true&gid=" + gid : ""));

            m = SheetId.Match(link);
            string id = m.Success ? m.Groups[1].Value : BareId.IsMatch(link) ? link : null;
            if (id == null)
                throw new ArgumentException("This is not a Google Sheets link.\n\n" +
                    "Open the sheet in the browser and copy the address, e.g.\nhttps://docs.google.com/spreadsheets/d/…/edit#gid=0");

            return new Uri("https://docs.google.com/spreadsheets/d/" + id + "/export?format=csv" +
                           (gid != null ? "&gid=" + gid : ""));
        }

        /// <summary>Downloads the sheet tab named by <paramref name="link"/> and reads its students.</summary>
        public static async Task<List<Student>> LoadAsync(string link, CancellationToken cancel)
        {
            Uri uri = ToCsvUri(link);
            string body;

            HttpResponseMessage response;
            try
            {
                response = await Http.GetAsync(uri, cancel).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
            {
                throw new TimeoutException(string.Format("Google Sheets did not answer within {0} seconds.", TimeoutSeconds));
            }

            using (response)
            {
                // Google sends UTF-8; decode the bytes ourselves so the Khmer names never fall back to another charset.
                byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                body = new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');

                Uri final = response.RequestMessage != null ? response.RequestMessage.RequestUri : uri;
                bool signIn = final != null && final.Host.StartsWith("accounts.", StringComparison.OrdinalIgnoreCase);
                bool html = body.TrimStart().StartsWith("<");

                if (signIn || (int)response.StatusCode == 401 || (int)response.StatusCode == 403 || (response.IsSuccessStatusCode && html))
                    throw new InvalidDataException(NotSharedMessage);

                if (!response.IsSuccessStatusCode)
                {
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.NotFound:
                            throw new InvalidDataException("Google Sheets could not find this sheet. Check the link.");
                        case HttpStatusCode.BadRequest:
                            throw new InvalidDataException("Google Sheets could not find this tab.\n\n" +
                                "Open the tab with the students and copy the address again (it ends in #gid=…).");
                        default:
                            throw new HttpRequestException(string.Format("Google Sheets answered {0} {1}. Try again later.",
                                (int)response.StatusCode, response.ReasonPhrase));
                    }
                }
            }

            return CsvStudentLoader.FromRows(ParseCsv(body), "The Google Sheet's first row");
        }

        private const string NotSharedMessage =
            "The Google Sheet is private, so the app cannot read it.\n\n" +
            "In Google Sheets click Share → General access → “Anyone with the link” (Viewer), then try again.\n" +
            "Or use File → Share → Publish to web, and paste that link.";

        /// <summary>
        /// Splits CSV text into rows. Unlike the line-based CSV file import, a quoted cell may contain line breaks —
        /// Google exports a cell typed with Alt+Enter that way.
        /// </summary>
        internal static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;
            text = text ?? "";

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (inQuotes)
                {
                    if (ch != '"') cell.Append(ch);
                    else if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else inQuotes = false;
                }
                else if (ch == '"') inQuotes = true;
                else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else cell.Append(ch);
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }
}
