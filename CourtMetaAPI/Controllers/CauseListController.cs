using Microsoft.AspNetCore.Mvc;
using CourtMetaAPI.Services;

namespace CourtMetaAPI.Controllers;

/// <summary>
/// Workflow D — cases caused at a specific court on a date.
///   - GET /api/court/cause-list?state_code=&dist_code=&court_code=&court_no=
///                              &date=&flag=civ_t|cri_t&selprevdays=0
///
/// Wraps <c>cases_new.php</c>. The upstream returns an HTML table fragment; we
/// pass that through verbatim plus a parsed row list (with CNRs lifted out)
/// so callers can render either, and click-through to <c>/cnr/bundle</c>.
/// </summary>
[ApiController]
[Route("api/court/cause-list")]
public class CauseListController : ControllerBase
{
    private readonly EcourtsClient _ecourts;
    private readonly CauseListParser _parser;

    public CauseListController(EcourtsClient ecourts, CauseListParser parser)
    {
        _ecourts = ecourts;
        _parser = parser;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string state_code,
        [FromQuery] string dist_code,
        [FromQuery] string court_code,
        [FromQuery] string court_no,
        [FromQuery] string date,
        [FromQuery] string? flag,
        [FromQuery] string? selprevdays,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state_code) ||
            string.IsNullOrWhiteSpace(dist_code) ||
            string.IsNullOrWhiteSpace(court_code) ||
            string.IsNullOrWhiteSpace(court_no) ||
            string.IsNullOrWhiteSpace(date))
        {
            return BadRequest(new
            {
                success = false,
                error = "state_code, dist_code, court_code, court_no, date are required."
            });
        }

        var resolvedFlag = string.IsNullOrWhiteSpace(flag) ? "civ_t" : flag!;
        if (resolvedFlag != "civ_t" && resolvedFlag != "cri_t")
            return BadRequest(new { success = false, error = "flag must be civ_t or cri_t." });

        // /api/court/courts returns each room as "<est_code>^<court_no>" when the
        // complex spans multiple establishments (e.g. NyayMandir Vadodara has
        // njdg_est_code "1,2,17"). cases_new.php wants the *single* owning
        // establishment as court_code and the bare room number as court_no, so
        // split the compound here and override the comma-list court_code.
        var resolvedCourtCode = court_code;
        var resolvedCourtNo = court_no;
        var caretIdx = court_no.IndexOf('^');
        if (caretIdx > 0 && caretIdx < court_no.Length - 1)
        {
            resolvedCourtCode = court_no[..caretIdx];
            resolvedCourtNo = court_no[(caretIdx + 1)..];
        }

        var (node, error) = await _ecourts.CallAsync("cases_new.php", new()
        {
            ["state_code"] = state_code,
            ["dist_code"] = dist_code,
            ["flag"] = resolvedFlag,
            ["selprevdays"] = string.IsNullOrWhiteSpace(selprevdays) ? "0" : selprevdays!,
            ["court_no"] = resolvedCourtNo,
            ["court_code"] = resolvedCourtCode,
            ["causelist_date"] = NormalizeDate(date),
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        }, ct: ct);

        if (error != null) return StatusCode(502, new { success = false, error });

        var html = node?["cases"]?.ToString();
        var parsed = _parser.Parse(html);

        return Ok(new
        {
            success = true,
            count = parsed.Rows.Count(r => !r.IsHeader),
            html = parsed.Html,
            rows = parsed.Rows
        });
    }

    private static string NormalizeDate(string input)
    {
        input = input.Trim();
        if (DateTime.TryParseExact(input, "dd-MM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
            return input;

        if (DateTime.TryParse(input, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
            return dt.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture);

        return input;
    }
}
