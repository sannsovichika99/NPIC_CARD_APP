using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CardStudio.Admin
{
    /// <summary>One Card Studio release, as written in the repo's Document.md.</summary>
    internal sealed class Release
    {
        public Version Version { get; set; }
        /// <summary>"1.0.1.1", as the PCs report it (see AdminAgent: 3 parts when the 4th is 0).</summary>
        public string VersionText { get; set; }
        /// <summary>"2026-10-29", or "" when the entry has no release date.</summary>
        public string Date { get; set; }
        public string Summary { get; set; }
        public List<ReleaseSection> Sections { get; set; }

        public Release()
        {
            Summary = "";
            Date = "";
            Sections = new List<ReleaseSection>();
        }

        /// <summary>First sentence of the summary, for the version list.</summary>
        public string Headline
        {
            get
            {
                string s = Summary.Replace("\n", " ").Trim();
                int dot = s.IndexOf(". ", StringComparison.Ordinal);
                return dot > 0 ? s.Substring(0, dot + 1) : s;
            }
        }

        public int ItemCount { get { return Sections.Sum(x => x.Items.Count); } }
    }

    /// <summary>"Feature", "Change", "Fix", "Known issue"… with its bullet points.</summary>
    internal sealed class ReleaseSection
    {
        public string Title { get; set; }
        public List<string> Items { get; set; }
        public ReleaseSection() { Items = new List<string>(); }
    }

    /// <summary>
    /// Card Studio's release history: <c>Document.md</c> at the root of the repo, embedded in ADMIN_CARD.exe when it is built
    /// (see ADMIN_CARD.csproj), so the Versions page always matches the changelog of the build. Format of each entry:
    /// <code>
    /// ### v1.0.1.1
    /// Release date: 2026-10-29
    /// Summary paragraph…
    /// ## Feature
    /// * item
    /// ---
    /// </code>
    /// </summary>
    internal static class ReleaseNotes
    {
        public const string ResourceName = "CardStudio.Admin.ReleaseNotes.md";

        private static List<Release> _all;

        /// <summary>Every release, newest first (empty when the notes could not be read).</summary>
        public static List<Release> All
        {
            get
            {
                if (_all != null) return _all;
                try
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                    using (var r = new StreamReader(s))
                        _all = Parse(r.ReadToEnd());
                }
                catch (Exception)
                {
                    _all = new List<Release>();
                }
                return _all;
            }
        }

        /// <summary>The newest release in the notes, or null.</summary>
        public static Release Latest { get { return All.FirstOrDefault(); } }

        public static Release Find(Version v)
        {
            return All.FirstOrDefault(r => SameVersion(r.Version, v));
        }

        /// <summary>1.0.1 and 1.0.1.0 are the same version (the PCs drop a 4th part that is 0).</summary>
        public static bool SameVersion(Version a, Version b)
        {
            return a != null && b != null && Normalize(a) == Normalize(b);
        }

        public static Version Normalize(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        private static readonly Regex Heading = new Regex(@"^###\s+v?(\d+(?:\.\d+){1,3})\s*$");
        private static readonly Regex DateLine = new Regex(@"^Release date:?\s*(\S.*)$", RegexOptions.IgnoreCase);

        public static List<Release> Parse(string markdown)
        {
            var list = new List<Release>();
            Release current = null;
            ReleaseSection section = null;
            var summary = new List<string>();

            Action finish = () =>
            {
                if (current == null) return;
                current.Summary = string.Join(" ", summary).Trim();
                list.Add(current);
            };

            foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                Match m = Heading.Match(line);
                if (m.Success)
                {
                    finish();
                    Version v = Version.Parse(m.Groups[1].Value);
                    current = new Release { Version = v, VersionText = v.ToString(v.Revision > 0 ? 4 : 3) };
                    section = null;
                    summary.Clear();
                    continue;
                }
                if (current == null || line.Length == 0 || line == "---") continue;

                Match d = DateLine.Match(line);
                if (d.Success && current.Date.Length == 0 && section == null)
                {
                    current.Date = d.Groups[1].Value.Trim();
                    continue;
                }
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    section = new ReleaseSection { Title = line.Substring(3).Trim() };
                    current.Sections.Add(section);
                    continue;
                }
                if (line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("- ", StringComparison.Ordinal))
                {
                    if (section == null)
                    {
                        section = new ReleaseSection { Title = "Notes" };
                        current.Sections.Add(section);
                    }
                    section.Items.Add(line.Substring(2).Trim());
                    continue;
                }
                if (section == null) summary.Add(line);
                else if (section.Items.Count > 0) section.Items[section.Items.Count - 1] += " " + line;   // wrapped bullet
            }
            finish();
            return list.OrderByDescending(r => Normalize(r.Version)).ToList();
        }

        /// <summary>"2026-10-29" → date, for "x days ago"; null when it is not a date.</summary>
        public static DateTime? ParseDate(string text)
        {
            DateTime d;
            return DateTime.TryParseExact(text ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                ? d : (DateTime?)null;
        }
    }
}
