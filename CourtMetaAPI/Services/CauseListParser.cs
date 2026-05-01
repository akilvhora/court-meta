using System.Text.RegularExpressions;

namespace CourtMetaAPI.Services;

/// <summary>
/// Parses the HTML fragment returned by <c>cases_new.php</c> (Workflow D)
/// into structured rows.
///
/// The upstream payload looks like
/// <code>{ "cases": "&lt;table&gt;…&lt;tr&gt;&lt;td&gt;1&lt;/td&gt;&lt;td&gt;…&lt;/td&gt;…&lt;/tr&gt;…" }</code>.
/// We don't take a dependency on HtmlAgilityPack — the markup is narrow and
/// regex extraction is good enough. The parser also pulls out anything that
/// looks like a CNR (16 alphanumeric chars) so a UI can wire each row to
/// <c>/cnr/bundle?cino=…</c>.
///
/// Always returns <see cref="CauseListResult.Html"/> verbatim so a caller can
/// render the fragment if it prefers.
/// </summary>
public class CauseListParser
{
    private static readonly Regex RowRx =
        new(@"<tr\b[^>]*>(?<body>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex CellRx =
        new(@"<t[hd]\b[^>]*>(?<cell>.*?)</t[hd]>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex TagRx =
        new(@"<[^>]+>", RegexOptions.Singleline);

    private static readonly Regex CinoRx =
        new(@"\b[A-Z]{2}[A-Z0-9]{4}\d{6}\d{4}\b", RegexOptions.IgnoreCase);

    private static readonly Regex WhitespaceRx =
        new(@"\s+", RegexOptions.Singleline);

    public CauseListResult Parse(string? html)
    {
        var result = new CauseListResult { Html = html ?? string.Empty };
        if (string.IsNullOrWhiteSpace(html)) return result;

        var trMatches = RowRx.Matches(html);
        for (var i = 0; i < trMatches.Count; i++)
        {
            var rowHtml = trMatches[i].Groups["body"].Value;
            var cells = new List<string>();
            foreach (Match m in CellRx.Matches(rowHtml))
            {
                var raw = m.Groups["cell"].Value;
                cells.Add(StripTags(raw));
            }

            // Skip empty rows or pure header rows (no data).
            if (cells.Count == 0 || cells.All(string.IsNullOrWhiteSpace)) continue;

            // Header rows are commonly all-<th>; we detect by looking at the original markup.
            var isHeader = rowHtml.Contains("<th", StringComparison.OrdinalIgnoreCase) &&
                           !rowHtml.Contains("<td", StringComparison.OrdinalIgnoreCase);

            // Try to pull a CNR out of the row body or any onclick attribute on the row.
            var cinoMatch = CinoRx.Match(rowHtml);
            var cino = cinoMatch.Success ? cinoMatch.Value.ToUpperInvariant() : null;

            result.Rows.Add(new CauseListRow
            {
                Index = result.Rows.Count + 1,
                IsHeader = isHeader,
                Cino = cino,
                Cells = cells
            });
        }

        return result;
    }

    private static string StripTags(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var noTags = TagRx.Replace(s, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(noTags);
        return WhitespaceRx.Replace(decoded, " ").Trim();
    }
}

public class CauseListResult
{
    public string Html { get; set; } = string.Empty;
    public List<CauseListRow> Rows { get; set; } = new();
}

public class CauseListRow
{
    public int Index { get; set; }
    public bool IsHeader { get; set; }
    public string? Cino { get; set; }
    public List<string> Cells { get; set; } = new();
}
