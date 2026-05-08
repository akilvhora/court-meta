using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CourtMetaAPI.Services;

/// <summary>
/// Several sections of <c>caseHistoryWebService.php</c>'s response —
/// <c>historyOfCaseHearing</c>, <c>transfer</c>, <c>processes</c> — arrive as
/// HTML <c>&lt;table&gt;</c> strings embedded inside the otherwise-JSON
/// history object. Both <see cref="HistoryParser"/> and the extension's
/// declarative mapper expect arrays, so we expand the HTML in-place into
/// <see cref="JsonArray"/>s of row objects.
///
/// The original HTML is preserved at <c>&lt;key&gt;_html</c> for callers that
/// want to render the upstream markup verbatim. Row keys are chosen so the
/// existing <c>BuildHearing</c>/<c>BuildTransfer</c>/<c>BuildProcess</c> regex
/// matchers in <see cref="HistoryParser"/> pick up the right field, and so the
/// extension's <c>cnrMapping.json</c> <c>regexKey</c> patterns also match
/// without modification.
/// </summary>
public static class HistoryHtmlExpander
{
    private static readonly Regex RowRx =
        new(@"<tr\b[^>]*>(?<body>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex CellRx =
        new(@"<t[hd]\b[^>]*>(?<cell>.*?)</t[hd]>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex TagRx =
        new(@"<[^>]+>", RegexOptions.Singleline);
    private static readonly Regex WhitespaceRx =
        new(@"\s+", RegexOptions.Singleline);

    // onclick=viewBusiness('court_code','dist_code','nextdate1','case_number1',
    //                      'state_code','disposal_flag','businessDate','court_no','cino')
    private static readonly Regex ViewBusinessRx =
        new(@"viewBusiness\s*\(\s*(?<args>[^)]*)\)", RegexOptions.IgnoreCase);
    private static readonly Regex SingleQuotedArgRx =
        new(@"'([^']*)'", RegexOptions.Singleline);

    public static void Expand(JsonNode? history)
    {
        if (history is not JsonObject obj) return;

        ExpandSection(obj, "historyOfCaseHearing", ParseHearings);
        ExpandSection(obj, "transfer", ParseTransfers);
        ExpandSection(obj, "processes", ParseProcesses);
        // For these we don't have hand-validated samples for every state, so
        // we use the generic header-keyed parser. Keys are snake_cased from
        // the <thead> column labels, which is enough for BuildOrder's regexes
        // (order.*date, order_number, judge, …) to pick up the common shapes.
        ExpandSection(obj, "iaFiling", ParseGeneric);
        ExpandSection(obj, "interimOrder", ParseGeneric);
        ExpandSection(obj, "finalOrder", ParseGeneric);
    }

    private static void ExpandSection(JsonObject root, string key, Func<string, JsonArray> parser)
    {
        if (!root.TryGetPropertyValue(key, out var node) || node is null) return;
        if (node is not JsonValue val || !val.TryGetValue<string>(out var html)) return;
        if (string.IsNullOrWhiteSpace(html)) return;

        try
        {
            var rows = parser(html);
            root[$"{key}_html"] = html;
            root[key] = rows;
        }
        catch
        {
            // Leave the original HTML in place on any parse failure — the
            // legacy text path (bundle.HistoryHtml + raw response) still works.
        }
    }

    // Returns the <tbody>-style data rows (cells = stripped text + raw inner HTML).
    private static List<List<(string Text, string Raw)>> ParseRows(string html)
        => ParseTable(html, out _);

    // Same as ParseRows, but also returns the first <th>-only row's stripped
    // text per cell as the header list (empty if the table has no <thead>).
    private static List<List<(string Text, string Raw)>> ParseTable(
        string html, out List<string> headers)
    {
        headers = new List<string>();
        var rows = new List<List<(string, string)>>();
        foreach (Match tr in RowRx.Matches(html))
        {
            var rowHtml = tr.Groups["body"].Value;
            var cells = new List<(string, string)>();
            foreach (Match c in CellRx.Matches(rowHtml))
            {
                var raw = c.Groups["cell"].Value;
                cells.Add((StripTags(raw), raw));
            }
            if (cells.Count == 0) continue;

            var isHeader = rowHtml.Contains("<th", StringComparison.OrdinalIgnoreCase) &&
                           !rowHtml.Contains("<td", StringComparison.OrdinalIgnoreCase);
            if (isHeader)
            {
                if (headers.Count == 0)
                    headers = cells.Select(c => c.Item1).ToList();
                continue;
            }
            rows.Add(cells);
        }
        return rows;
    }

    // Columns: Judge | Business on Date | Hearing Date | Purpose of Hearing
    private static JsonArray ParseHearings(string html)
    {
        var rows = ParseRows(html);
        var arr = new JsonArray();
        foreach (var row in rows)
        {
            string Text(int i) => i < row.Count ? row[i].Text : string.Empty;
            string Raw(int i)  => i < row.Count ? row[i].Raw  : string.Empty;

            var obj = new JsonObject
            {
                ["Judge"]              = Text(0),
                ["Business on Date"]   = Text(1),
                ["Hearing Date"]       = Text(2),
                ["Purpose of Hearing"] = Text(3)
            };

            // Pull viewBusiness(...) onclick args out of the "Business on Date"
            // cell so downstream callers can hit /cnr/business directly without
            // re-deriving routing fields. Field names mirror BuildHearing's
            // FuzzyStr regexes (court.*no, disposal_flag).
            var bizMatch = ViewBusinessRx.Match(Raw(1));
            if (bizMatch.Success)
            {
                var args = SingleQuotedArgRx.Matches(bizMatch.Groups["args"].Value)
                    .Select(m => m.Groups[1].Value).ToList();
                if (args.Count >= 9)
                {
                    obj["court_code"]    = args[0];
                    obj["dist_code"]     = args[1];
                    obj["nextdate1"]     = args[2];   // yyyymmdd of the next listing
                    obj["case_number"]   = args[3];
                    obj["state_code"]    = args[4];
                    obj["disposal_flag"] = args[5];
                    obj["businessDate"]  = args[6];   // dd-mm-yyyy of this hearing
                    obj["court_no"]      = args[7];
                    obj["cino"]          = args[8];
                }
            }

            arr.Add(obj);
        }
        return arr;
    }

    // Columns: Transfer Date | From Court Number and Judge | To Court Number and Judge
    private static JsonArray ParseTransfers(string html)
    {
        var rows = ParseRows(html);
        var arr = new JsonArray();
        foreach (var row in rows)
        {
            string Text(int i) => i < row.Count ? row[i].Text : string.Empty;
            arr.Add(new JsonObject
            {
                ["Transfer Date"]                = Text(0),
                ["From Court Number and Judge"]  = Text(1),
                ["To Court Number and Judge"]    = Text(2)
            });
        }
        return arr;
    }

    // Columns: Process ID | Process Date | Process Title
    private static JsonArray ParseProcesses(string html)
    {
        var rows = ParseRows(html);
        var arr = new JsonArray();
        foreach (var row in rows)
        {
            string Text(int i) => i < row.Count ? row[i].Text : string.Empty;
            arr.Add(new JsonObject
            {
                ["process_id"]   = Text(0),
                ["issue_date"]   = Text(1),  // matches BuildProcess "issue.*date"
                ["process_name"] = Text(2)   // matches BuildProcess "process_name"
            });
        }
        return arr;
    }

    // Header-keyed table parser. Snake-cases the <thead> labels and uses them
    // as JSON keys for each data row. Falls back to col_0, col_1, … when a
    // table has no header row. Used for sections we don't have a hand-tuned
    // shape for (iaFiling, interimOrder, finalOrder).
    private static JsonArray ParseGeneric(string html)
    {
        var rows = ParseTable(html, out var headers);
        var keys = headers.Select(ToSnakeCase).ToList();
        var arr = new JsonArray();
        foreach (var row in rows)
        {
            var obj = new JsonObject();
            for (var i = 0; i < row.Count; i++)
            {
                var key = i < keys.Count && !string.IsNullOrEmpty(keys[i])
                    ? keys[i]
                    : $"col_{i}";
                // If the same key appears twice (rare; happens with malformed
                // duplicate headers), suffix with the column index.
                if (obj.ContainsKey(key)) key = $"{key}_{i}";
                obj[key] = row[i].Text;
            }
            arr.Add(obj);
        }
        return arr;
    }

    private static readonly Regex NonAlnumRx = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    private static string ToSnakeCase(string header)
    {
        if (string.IsNullOrWhiteSpace(header)) return string.Empty;
        var lower = header.Trim().ToLowerInvariant();
        return NonAlnumRx.Replace(lower, "_").Trim('_');
    }

    private static string StripTags(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var noTags = TagRx.Replace(s, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(noTags);
        return WhitespaceRx.Replace(decoded, " ").Trim();
    }
}
