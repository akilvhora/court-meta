using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CourtMetaAPI.Services;

namespace CourtMetaAPI.Controllers;

/// <summary>
/// Case-search endpoints (Workflows E + F):
///   - GET /api/court/case-types               — case-type dropdown (caseNumberWebService.php)
///   - GET /api/court/search/case-number       — caseNumberSearch.php
///   - GET /api/court/search/filing-number     — searchByFilingNumberWebService.php
///
/// Both search endpoints return a flat list of result rows. The upstream API
/// is keyed by establishment index ({"0":{caseNos:[…]}, "1":{…}}); we flatten
/// it so callers can render a single table and click through to /cnr/bundle.
/// </summary>
[ApiController]
[Route("api/court")]
public class SearchController : ControllerBase
{
    private readonly EcourtsClient _ecourts;
    private readonly IMemoryCache _cache;
    private readonly AdvocateSearchService _advocateSearch;

    private static readonly TimeSpan CaseTypesTtl = TimeSpan.FromHours(1);

    /// <summary>
    /// Streaming responses use camelCase + ignore-null so the wire output
    /// matches the JSON the rest of the API emits (controllers default to
    /// PascalCase for anonymous types, but the streaming events flow through
    /// our DTOs and would otherwise look inconsistent).
    /// </summary>
    private static readonly JsonSerializerOptions StreamSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SearchController(
        EcourtsClient ecourts,
        IMemoryCache cache,
        AdvocateSearchService advocateSearch)
    {
        _ecourts = ecourts;
        _cache = cache;
        _advocateSearch = advocateSearch;
    }

    // ─── /case-types ─────────────────────────────────────────────────────────
    // GET /api/court/case-types?state_code=&dist_code=&court_code=
    //
    // Upstream returns:  { "case_types": [ { "case_type": "1~Civil#2~Criminal#…" } ] }
    // We split it into [{ id, name }] so the UI doesn't have to.
    [HttpGet("case-types")]
    public async Task<IActionResult> CaseTypes(
        [FromQuery] string state_code,
        [FromQuery] string dist_code,
        [FromQuery] string court_code,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state_code) ||
            string.IsNullOrWhiteSpace(dist_code) ||
            string.IsNullOrWhiteSpace(court_code))
        {
            return BadRequest(new { success = false, error = "state_code, dist_code, court_code are required." });
        }

        var key = $"case-types:{state_code}:{dist_code}:{court_code}";
        if (_cache.TryGetValue(key, out IActionResult? cached) && cached is not null)
            return cached;

        var (node, error) = await _ecourts.CallAsync("caseNumberWebService.php", new()
        {
            ["state_code"] = state_code,
            ["dist_code"] = dist_code,
            ["court_code"] = court_code,
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        }, ct: ct);

        if (error != null) return StatusCode(502, new { success = false, error });

        var caseTypesArr = node?["case_types"]?.AsArray();
        var blob = caseTypesArr?.FirstOrDefault()?["case_type"]?.ToString();

        var caseTypes = ParseTildeHashList(blob);
        var ok = Ok(new { success = true, caseTypes });
        _cache.Set(key, (IActionResult)ok, CaseTypesTtl);
        return ok;
    }

    // ─── /search/case-number ─────────────────────────────────────────────────
    // GET /api/court/search/case-number?state_code=&dist_code=&courts=MHAU01,MHAU02
    //                                  &case_type=1&number=123&year=2020
    //
    // `courts` is a comma-joined list of njdg_est_codes (mapped to court_code_arr).
    [HttpGet("search/case-number")]
    public async Task<IActionResult> SearchByCaseNumber(
        [FromQuery] string state_code,
        [FromQuery] string dist_code,
        [FromQuery] string courts,
        [FromQuery] string case_type,
        [FromQuery] string number,
        [FromQuery] string year,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state_code) ||
            string.IsNullOrWhiteSpace(dist_code) ||
            string.IsNullOrWhiteSpace(courts) ||
            string.IsNullOrWhiteSpace(case_type) ||
            string.IsNullOrWhiteSpace(number) ||
            string.IsNullOrWhiteSpace(year))
        {
            return BadRequest(new
            {
                success = false,
                error = "state_code, dist_code, courts, case_type, number, year are required."
            });
        }

        var (node, error) = await _ecourts.CallAsync("caseNumberSearch.php", new()
        {
            ["state_code"] = state_code,
            ["dist_code"] = dist_code,
            ["court_code_arr"] = courts,
            ["case_number"] = number,
            ["case_type"] = case_type,
            ["year"] = year,
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        }, ct: ct);

        if (error != null) return StatusCode(502, new { success = false, error });

        var rows = FlattenEstablishmentResults(node, "caseNos");
        return Ok(new { success = true, count = rows.Count, results = rows, raw = node });
    }

    // ─── /search/filing-number ───────────────────────────────────────────────
    // GET /api/court/search/filing-number?state_code=&dist_code=&courts=&filingNumber=&year=
    [HttpGet("search/filing-number")]
    public async Task<IActionResult> SearchByFilingNumber(
        [FromQuery] string state_code,
        [FromQuery] string dist_code,
        [FromQuery] string courts,
        [FromQuery] string filingNumber,
        [FromQuery] string year,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state_code) ||
            string.IsNullOrWhiteSpace(dist_code) ||
            string.IsNullOrWhiteSpace(courts) ||
            string.IsNullOrWhiteSpace(filingNumber) ||
            string.IsNullOrWhiteSpace(year))
        {
            return BadRequest(new
            {
                success = false,
                error = "state_code, dist_code, courts, filingNumber, year are required."
            });
        }

        var (node, error) = await _ecourts.CallAsync("searchByFilingNumberWebService.php", new()
        {
            ["state_code"] = state_code,
            ["dist_code"] = dist_code,
            ["court_code_arr"] = courts,
            ["filingNumber"] = filingNumber,
            ["year"] = year,
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        }, ct: ct);

        if (error != null) return StatusCode(502, new { success = false, error });

        var rows = FlattenEstablishmentResults(node, "caseNos");
        return Ok(new { success = true, count = rows.Count, results = rows, raw = node });
    }

    // ─── /search/advocate ────────────────────────────────────────────────────
    // GET /api/court/search/advocate?scope=court|state&state_code=&dist_code=&courts=
    //                              &mode=name|barcode&advocateName=&barstatecode=&barcode=
    //                              &pendingDisposed=&year=&date=
    //
    // scope=court → JSON {success, count, results}
    // scope=state → application/x-ndjson stream, one JSON object per line:
    //   {"type":"start", totalDistricts}
    //   {"type":"progress", districtCode, districtName, completedDistricts, totalDistricts, rows:[…]}
    //   {"type":"complete", totalRows}
    //   {"type":"error", error}                        // fatal at any point
    [HttpGet("search/advocate")]
    public async Task SearchAdvocate(
        [FromQuery] string? scope,
        [FromQuery] string state_code,
        [FromQuery] string? dist_code,
        [FromQuery] string? courts,
        [FromQuery] string? mode,
        [FromQuery] string? advocateName,
        [FromQuery] string? barstatecode,
        [FromQuery] string? barcode,
        [FromQuery] string? pendingDisposed,
        [FromQuery] string? year,
        [FromQuery] string? date,
        CancellationToken ct)
    {
        var resolvedScope = string.IsNullOrWhiteSpace(scope) ? "court" : scope!.ToLowerInvariant();
        var byBarcode = string.Equals(mode, "barcode", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(state_code))
        {
            await WriteJsonAsync(400, new { success = false, error = "state_code is required." });
            return;
        }
        if (byBarcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
            {
                await WriteJsonAsync(400, new { success = false, error = "barcode is required when mode=barcode." });
                return;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(advocateName))
            {
                await WriteJsonAsync(400, new { success = false, error = "advocateName is required when mode=name." });
                return;
            }
        }

        var req = new AdvocateSearchRequest
        {
            StateCode = state_code,
            DistCode = dist_code ?? "",
            CourtCodeArr = courts,
            Mode = byBarcode ? "barcode" : "name",
            AdvocateName = advocateName,
            BarStateCode = barstatecode,
            BarCode = barcode,
            PendingDisposed = pendingDisposed,
            Year = year,
            Date = date
        };

        if (resolvedScope == "court")
        {
            if (string.IsNullOrWhiteSpace(dist_code) || string.IsNullOrWhiteSpace(courts))
            {
                await WriteJsonAsync(400, new
                {
                    success = false,
                    error = "scope=court requires dist_code and courts."
                });
                return;
            }

            var (rows, raw, error) = await _advocateSearch.SearchSingleAsync(req, courts!, ct);
            if (error != null)
            {
                await WriteJsonAsync(502, new { success = false, error });
                return;
            }
            await WriteJsonAsync(200, new { success = true, count = rows.Count, results = rows, raw });
            return;
        }

        if (resolvedScope == "state")
        {
            Response.StatusCode = 200;
            Response.ContentType = "application/x-ndjson";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            await foreach (var evt in _advocateSearch.SearchStateAsync(req, ct))
            {
                var line = JsonSerializer.Serialize(evt, StreamSerializerOptions);
                await Response.WriteAsync(line + "\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            return;
        }

        await WriteJsonAsync(400, new { success = false, error = "scope must be 'court' or 'state'." });
    }

    private async Task WriteJsonAsync(int statusCode, object body)
    {
        Response.StatusCode = statusCode;
        Response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(body, StreamSerializerOptions);
        await Response.WriteAsync(json);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits the eCourts <c>"id~name#id~name#…"</c> dropdown blobs into pairs.
    /// </summary>
    private static List<object> ParseTildeHashList(string? blob)
    {
        var list = new List<object>();
        if (string.IsNullOrWhiteSpace(blob)) return list;

        foreach (var entry in blob.Split('#', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('~');
            if (parts.Length < 2) continue;
            list.Add(new { id = parts[0].Trim(), name = parts[1].Trim() });
        }
        return list;
    }

    /// <summary>
    /// The DC search endpoints return:
    ///   { "0": { court_code, establishment_name, caseNos: [...] }, "1": {...} }
    /// We flatten that into a single array, copying the establishment context
    /// onto each row so a UI can render one table and link to /cnr/bundle.
    ///
    /// Tolerant of variants where <c>caseNos</c> is itself an object keyed by index
    /// rather than a JSON array (the upstream is inconsistent across builds).
    /// </summary>
    private static List<JsonObject> FlattenEstablishmentResults(JsonNode? root, string innerKey)
    {
        var rows = new List<JsonObject>();
        if (root is not JsonObject obj) return rows;

        foreach (var (estKey, estNode) in obj)
        {
            if (estNode is not JsonObject est) continue;

            // Skip envelope keys that aren't establishment indices.
            if (!int.TryParse(estKey, out _)) continue;

            var courtCode = est["court_code"]?.ToString();
            var establishment = est["establishment_name"]?.ToString();
            var advocate = est["advocateName"]?.ToString();   // present on advocate searches
            var inner = est[innerKey];

            foreach (var raw in EnumerateRows(inner))
            {
                if (raw is not JsonObject row) continue;

                var flat = (JsonObject)row.DeepClone();
                if (!string.IsNullOrEmpty(courtCode))      flat["court_code"]         ??= courtCode;
                if (!string.IsNullOrEmpty(establishment))  flat["establishment_name"] ??= establishment;
                if (!string.IsNullOrEmpty(advocate))       flat["advocateName"]       ??= advocate;
                rows.Add(flat);
            }
        }

        return rows;
    }

    private static IEnumerable<JsonNode?> EnumerateRows(JsonNode? inner)
    {
        if (inner is JsonArray arr) return arr;
        if (inner is JsonObject map) return map.Select(kv => kv.Value);
        return Array.Empty<JsonNode?>();
    }
}
