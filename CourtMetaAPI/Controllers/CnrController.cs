using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Nodes;
using CourtMetaAPI.Services;

namespace CourtMetaAPI.Controllers;

/// <summary>
/// CNR-driven endpoints (Workflow A):
///   - GET /api/court/cnr             — backwards-compatible wrapper, returns history blob
///   - GET /api/court/cnr/bundle      — structured CaseBundle (parsed sections + raw)
///   - GET /api/court/cnr/business    — View Business for a single hearing (s_show_business.php)
///   - GET /api/court/cnr/writ        — Writ / application detail (s_show_app.php)
///   - GET /api/court/cnr/order-pdf   — Pretrial / final order PDF (binary stream)
/// </summary>
[ApiController]
[Route("api/court/cnr")]
public class CnrController : ControllerBase
{
    private readonly EcourtsClient _ecourts;
    private readonly HistoryParser _parser;

    public CnrController(EcourtsClient ecourts, HistoryParser parser)
    {
        _ecourts = ecourts;
        _parser = parser;
    }

    // ─── /cnr (back-compat) ──────────────────────────────────────────────────
    // GET /api/court/cnr?cino=MHPU010001232022
    [HttpGet]
    public async Task<IActionResult> CnrSearch([FromQuery] string cino, CancellationToken ct)
    {
        if (!ValidateCino(cino, out var error))
            return BadRequest(new { success = false, error });

        var (caseType, history, err) = await ResolveCaseHistory(cino, ct);
        if (err != null) return err;

        return Ok(new
        {
            success = true,
            data = new
            {
                cino,
                type = caseType,
                history
            }
        });
    }

    // ─── /cnr/bundle (structured) ────────────────────────────────────────────
    // GET /api/court/cnr/bundle?cino=...
    [HttpGet("bundle")]
    public async Task<IActionResult> CnrBundle([FromQuery] string cino, CancellationToken ct)
    {
        if (!ValidateCino(cino, out var error))
            return BadRequest(new { success = false, error });

        var (caseType, history, err) = await ResolveCaseHistory(cino, ct);
        if (err != null) return err;

        var bundle = _parser.Parse(cino, caseType ?? "case", history);
        return Ok(new { success = true, data = bundle });
    }

    // ─── /cnr/by-case-no — resolve a cause-list row to a CNR ─────────────────
    // GET /api/court/cnr/by-case-no?state_code=&dist_code=&court_code=&case_no=
    //
    // Cause-list rows arrive without a CNR — only the upstream's internal
    // case_no + establishment court_code (parsed out of the anchor markup).
    // caseHistoryWebService.php accepts that form directly (API_DOCUMENTATION
    // §4.3.2 alt request) and the response carries cino, so we surface that
    // for the UI to feed into /cnr/bundle.
    [HttpGet("by-case-no")]
    public async Task<IActionResult> ByCaseNo(
        [FromQuery] string state_code,
        [FromQuery] string dist_code,
        [FromQuery] string court_code,
        [FromQuery] string case_no,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state_code) ||
            string.IsNullOrWhiteSpace(dist_code) ||
            string.IsNullOrWhiteSpace(court_code) ||
            string.IsNullOrWhiteSpace(case_no))
        {
            return BadRequest(new { success = false, error = "state_code, dist_code, court_code, case_no are required." });
        }

        var (node, error) = await _ecourts.CallAsync("caseHistoryWebService.php", new()
        {
            ["state_code"] = state_code,
            ["dist_code"] = dist_code,
            ["case_no"] = case_no,
            ["court_code"] = court_code,
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        }, ct: ct);

        if (error != null) return StatusCode(502, new { success = false, error });

        var history = node?["history"];
        if (history == null)
            return NotFound(new { success = false, error = "Case history not found for this case_no." });

        var cino = ExtractCino(history);
        if (string.IsNullOrEmpty(cino))
            return Ok(new { success = false, error = "Case history returned without a CNR.", history });

        return Ok(new { success = true, cino, data = _parser.Parse(cino, "case", history) });
    }

    private static string? ExtractCino(JsonNode history)
    {
        // history shape varies — look for cino/cinum on the root, then on the
        // case-info section the parser also targets (caseInfo / case_info).
        string? Try(JsonNode? n) => n?["cino"]?.ToString() ?? n?["cinum"]?.ToString();

        var direct = Try(history);
        if (!string.IsNullOrEmpty(direct)) return direct;

        if (history is JsonObject obj)
        {
            foreach (var key in new[] { "caseInfo", "case_info", "case_details", "caseDetails" })
                if (obj.TryGetPropertyValue(key, out var sub) && Try(sub) is { Length: > 0 } v)
                    return v;
        }
        return null;
    }

    // ─── /cnr/business — drill into a specific hearing ───────────────────────
    // GET /api/court/cnr/business?state_code=&dist_code=&court_code=&case_number=&hearingDate=&disposalFlag=&courtNo=
    [HttpGet("business")]
    public async Task<IActionResult> Business(
        [FromQuery] string state_code,
        [FromQuery] string dist_code,
        [FromQuery] string court_code,
        [FromQuery] string case_number,
        [FromQuery] string hearingDate,
        [FromQuery] string? disposalFlag,
        [FromQuery] string? courtNo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state_code) ||
            string.IsNullOrWhiteSpace(dist_code) ||
            string.IsNullOrWhiteSpace(court_code) ||
            string.IsNullOrWhiteSpace(case_number) ||
            string.IsNullOrWhiteSpace(hearingDate))
        {
            return BadRequest(new { success = false, error = "state_code, dist_code, court_code, case_number, hearingDate are required." });
        }

        var date = NormalizeDate(hearingDate);

        var (node, error) = await _ecourts.CallAsync("s_show_business.php", new()
        {
            ["state_code"] = state_code,
            ["dist_code"] = dist_code,
            ["court_code"] = court_code,
            ["case_number1"] = case_number,
            ["nextdate1"] = date,
            ["businessDate"] = date,
            ["disposal_flag"] = string.IsNullOrWhiteSpace(disposalFlag) ? "P" : disposalFlag!,
            ["court_no"] = string.IsNullOrWhiteSpace(courtNo) ? "1" : courtNo!,
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        }, ct: ct);

        if (error != null) return StatusCode(502, new { success = false, error });

        var view = node?["viewBusiness"];
        return Ok(new { success = true, data = new { viewBusiness = view, raw = node } });
    }

    // ─── /cnr/writ — application / writ detail ───────────────────────────────
    // GET /api/court/cnr/writ?state_code=&dist_code=&court_code=&case_number=&app_cs_no=
    [HttpGet("writ")]
    public async Task<IActionResult> Writ(
        [FromQuery] string state_code,
        [FromQuery] string dist_code,
        [FromQuery] string court_code,
        [FromQuery] string case_number,
        [FromQuery] string app_cs_no,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state_code) ||
            string.IsNullOrWhiteSpace(dist_code) ||
            string.IsNullOrWhiteSpace(court_code) ||
            string.IsNullOrWhiteSpace(case_number) ||
            string.IsNullOrWhiteSpace(app_cs_no))
        {
            return BadRequest(new { success = false, error = "state_code, dist_code, court_code, case_number, app_cs_no are required." });
        }

        var (node, error) = await _ecourts.CallAsync("s_show_app.php", new()
        {
            ["state_code"] = state_code,
            ["dist_code"] = dist_code,
            ["court_code"] = court_code,
            ["case_number1"] = case_number,
            ["app_cs_no"] = app_cs_no,
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        }, ct: ct);

        if (error != null) return StatusCode(502, new { success = false, error });

        var writ = node?["writInfo"];
        return Ok(new { success = true, data = new { writInfo = writ, raw = node } });
    }

    // ─── /cnr/order-pdf — binary stream ──────────────────────────────────────
    // GET /api/court/cnr/order-pdf?state_code=&dist_code=&court_code=&orderYr=&order_id=&crno=
    [HttpGet("order-pdf")]
    public async Task<IActionResult> OrderPdf(
        [FromQuery] string state_code,
        [FromQuery] string dist_code,
        [FromQuery] string court_code,
        [FromQuery] string orderYr,
        [FromQuery] string order_id,
        [FromQuery] string crno,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state_code) ||
            string.IsNullOrWhiteSpace(dist_code) ||
            string.IsNullOrWhiteSpace(court_code) ||
            string.IsNullOrWhiteSpace(orderYr) ||
            string.IsNullOrWhiteSpace(order_id) ||
            string.IsNullOrWhiteSpace(crno))
        {
            return BadRequest(new { success = false, error = "state_code, dist_code, court_code, orderYr, order_id, crno are required." });
        }

        var (bytes, contentType, error) = await _ecourts.CallBinaryAsync("preTrialOrder_pdf.php", new()
        {
            ["state_code"] = state_code,
            ["dist_code"] = dist_code,
            ["court_code"] = court_code,
            ["orderYr"] = orderYr,
            ["order_id"] = order_id,
            ["crno"] = crno
        }, ct);

        if (error != null || bytes == null)
            return StatusCode(502, new { success = false, error = error ?? "Empty PDF response" });

        var ct2 = string.IsNullOrEmpty(contentType) || contentType == "application/octet-stream"
            ? "application/pdf"
            : contentType;

        return File(bytes, ct2, $"order-{order_id}-{orderYr}.pdf");
    }

    // ─── Internals ───────────────────────────────────────────────────────────

    private static bool ValidateCino(string? cino, out string error)
    {
        if (string.IsNullOrWhiteSpace(cino)) { error = "cino is required."; return false; }
        if (cino.Length < 16) { error = "CNR number must be at least 16 characters."; return false; }
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Two-step CNR resolution that mirrors the mobile app:
    ///   1. listOfCasesWebService → check whether this is a regular or filing case
    ///   2. caseHistoryWebService (regular) OR filingCaseHistory (filing)
    /// </summary>
    private async Task<(string? caseType, JsonNode? history, IActionResult? error)> ResolveCaseHistory(
        string cino, CancellationToken ct)
    {
        var (listNode, listError) = await _ecourts.CallAsync("listOfCasesWebService.php", new()
        {
            ["cino"] = cino,
            ["version_number"] = "9.0",
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        }, ct: ct);

        if (listError != null)
            return (null, null, StatusCode(502, new { success = false, error = listError }));

        if (listNode == null)
            return (null, null, NotFound(new { success = false, error = "CNR number not found." }));

        var caseNumber = listNode["case_number"]?.ToString();

        if (string.IsNullOrEmpty(caseNumber))
        {
            // Filing case
            var (filingNode, filingError) = await _ecourts.CallAsync("filingCaseHistory.php", new()
            {
                ["cino"] = cino,
                ["language_flag"] = "english",
                ["bilingual_flag"] = "0"
            }, ct: ct);

            if (filingError != null)
                return (null, null, StatusCode(502, new { success = false, error = filingError }));

            var fhistory = filingNode?["history"];
            if (fhistory == null)
                return (null, null, NotFound(new { success = false, error = "Filing case history not found for this CNR." }));

            return ("filing", fhistory, null);
        }

        // Regular case
        var (historyNode, historyError) = await _ecourts.CallAsync("caseHistoryWebService.php", new()
        {
            ["cinum"] = cino,
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        }, ct: ct);

        if (historyError != null)
            return (null, null, StatusCode(502, new { success = false, error = historyError }));

        var caseHistory = historyNode?["history"];
        if (caseHistory == null)
            return (null, null, NotFound(new { success = false, error = "Case history not found for this CNR." }));

        return ("case", caseHistory, null);
    }

    /// <summary>
    /// eCourts wants dd-MM-yyyy. Accept both that and yyyy-MM-dd from clients.
    /// </summary>
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
