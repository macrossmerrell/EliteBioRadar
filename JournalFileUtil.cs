using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EliteBioRadar
{
    // Elite Dangerous changed its journal filename format partway through 2022:
    //   old:  Journal.170905215203.01.log      (YYMMDDHHMMSS — two-digit year, no separators)
    //   new:  Journal.2022-05-06T154321.01.log (four-digit year, dashes, a literal 'T')
    // Every place in this app that needs files in real chronological order (finding the latest
    // journal, replaying history oldest-to-newest, filtering by date range, ...) used to sort the
    // raw filename TEXT. That happens to still work within either format alone, and even across
    // formats for most years by coincidence — EXCEPT year 2021: the old format's "21..." text-
    // sorts AFTER any new-format "2022-..."+ filename (comparing character by character, '1' >
    // '0' at the second digit), so a real 2021 journal looked NEWER than every 2022+ one to a
    // plain string sort. A player whose journals span 2021 into the new-format era (a real case —
    // Elite's been out since 2014) would have "find the latest journal" silently pick a stale 2021
    // file instead of their actual current one (real report: "current logs not being read").
    public static class JournalFileUtil
    {
        // Captures the timestamp portion between "Journal." and the trailing ".<part>.log".
        private static readonly Regex NameRegex = new(@"^Journal\.(.+)\.\d+\.log$", RegexOptions.Compiled);
        private static readonly Regex OldFormatTimestamp = new(@"^\d{12}$", RegexOptions.Compiled);

        // Real chronological sort key for a journal file path (or bare filename) — parses
        // whichever naming era the file uses. Falls back to the file's own real last-write time
        // if the name can't be parsed at all (e.g. a non-standard name swept up by the same
        // glob), so an unparseable name still sorts sensibly instead of crashing or always
        // landing first/last.
        public static DateTime SortKey(string filePath)
        {
            var name = Path.GetFileName(filePath);
            var m = NameRegex.Match(name);
            if (m.Success)
            {
                var ts = m.Groups[1].Value;
                if (OldFormatTimestamp.IsMatch(ts))
                {
                    // YYMMDDHHMMSS — two-digit year. Elite launched in 2014, so treating YY as
                    // 2000+YY is unambiguous for as long as this app is likely to matter.
                    if (DateTime.TryParseExact(ts, "yyMMddHHmmss", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var oldDt))
                        return oldDt;
                }
                else if (DateTime.TryParseExact(ts, "yyyy-MM-ddTHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var newDt))
                {
                    return newDt;
                }
            }
            try { return File.GetLastWriteTimeUtc(filePath); }
            catch { return DateTime.MinValue; }
        }

        // Oldest-first — what nearly every replay loop actually wants.
        public static IOrderedEnumerable<string> OrderByJournalDate(this IEnumerable<string> files) =>
            files.OrderBy(SortKey);

        // Newest-first — for "find the latest journal(s)" callers.
        public static IOrderedEnumerable<string> OrderByJournalDateDescending(this IEnumerable<string> files) =>
            files.OrderByDescending(SortKey);
    }
}
