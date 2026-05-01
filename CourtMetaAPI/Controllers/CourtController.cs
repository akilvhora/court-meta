using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json.Nodes;
using CourtMetaAPI.Services;

namespace CourtMetaAPI.Controllers;

/// <summary>
/// Lookup endpoints — geographic / court hierarchy.
///
/// All responses are cached in <see cref="IMemoryCache"/> with conservative TTLs
/// because the underlying data is quasi-static and the upstream API is rate-sensitive.
/// </summary>
[ApiController]
[Route("api/court")]
public class CourtController : ControllerBase
{
    private readonly EcourtsClient _ecourts;
    private readonly TokenService _tokenService;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan StatesTtl     = TimeSpan.FromHours(24);
    private static readonly TimeSpan DistrictsTtl  = TimeSpan.FromHours(12);
    private static readonly TimeSpan ComplexesTtl  = TimeSpan.FromHours(6);
    private static readonly TimeSpan CourtsTtl     = TimeSpan.FromHours(1);

    public CourtController(EcourtsClient ecourts, TokenService tokenService, IMemoryCache cache)
    {
        _ecourts = ecourts;
        _tokenService = tokenService;
        _cache = cache;
    }

    // ─── Token status (debug) ─────────────────────────────────────────────────
    [HttpGet("token-status")]
    public async Task<IActionResult> TokenStatus()
    {
        var token = await _tokenService.GetTokenAsync();
        if (string.IsNullOrEmpty(token))
            return Ok(new { success = false, tokenAcquired = false, message = "No token. Check API logs." });

        var preview = token.Length > 40 ? token[..20] + "…" + token[^10..] : token;
        return Ok(new { success = true, tokenAcquired = true, tokenPreview = preview });
    }

    // ─── State Fetch ──────────────────────────────────────────────────────────
    [HttpGet("states")]
    public async Task<IActionResult> GetStates()
    {
        if (_cache.TryGetValue("states", out IActionResult? cached) && cached is not null)
            return cached;

        var timestamp = ((long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds).ToString();

        var (node, error) = await _ecourts.CallAsync("stateWebService.php", new()
        {
            ["action_code"] = "fillState",
            ["time"] = timestamp
        });

        if (error != null)
            return StatusCode(502, new { success = false, error });

        var states = node?["states"]?.AsArray();
        if (states == null)
            return StatusCode(502, new { success = false, error = "Unexpected response format from eCourts API." });

        var result = states.Select(s => new
        {
            state_code = s?["state_code"]?.ToString(),
            state_name = s?["state_name"]?.ToString(),
            state_lang = s?["state_lang"]?.ToString()
        }).ToList();

        var ok = Ok(new { success = true, states = result });
        _cache.Set("states", (IActionResult)ok, StatesTtl);
        return ok;
    }

    // ─── District Fetch ──────────────────────────────────────────────────────
    [HttpGet("districts")]
    public async Task<IActionResult> GetDistricts([FromQuery] string state_code)
    {
        if (string.IsNullOrWhiteSpace(state_code))
            return BadRequest(new { success = false, error = "state_code is required." });

        var key = $"districts:{state_code}";
        if (_cache.TryGetValue(key, out IActionResult? cached) && cached is not null)
            return cached;

        var (node, error) = await _ecourts.CallAsync("districtWebService.php", new()
        {
            ["state_code"] = state_code,
            ["test_param"] = "pending"
        });

        if (error != null)
            return StatusCode(502, new { success = false, error });

        var districts = node?["districts"]?.AsArray();
        if (districts == null)
            return StatusCode(502, new { success = false, error = "Unexpected response format from eCourts API." });

        var result = districts.Select(d => new
        {
            dist_code = d?["dist_code"]?.ToString(),
            dist_name = d?["dist_name"]?.ToString(),
            mardist_name = d?["mardist_name"]?.ToString()
        }).ToList();

        var ok = Ok(new { success = true, districts = result });
        _cache.Set(key, (IActionResult)ok, DistrictsTtl);
        return ok;
    }

    // ─── Complex Fetch ───────────────────────────────────────────────────────
    [HttpGet("complexes")]
    public async Task<IActionResult> GetComplexes(
        [FromQuery] string state_code,
        [FromQuery] string dist_code)
    {
        if (string.IsNullOrWhiteSpace(state_code) || string.IsNullOrWhiteSpace(dist_code))
            return BadRequest(new { success = false, error = "state_code and dist_code are required." });

        var key = $"complexes:{state_code}:{dist_code}";
        if (_cache.TryGetValue(key, out IActionResult? cached) && cached is not null)
            return cached;

        var (node, error) = await _ecourts.CallAsync("courtEstWebService.php", new()
        {
            ["action_code"] = "fillCourtComplex",
            ["state_code"] = state_code,
            ["dist_code"] = dist_code
        });

        if (error != null)
            return StatusCode(502, new { success = false, error });

        var complexes = node?["courtComplex"]?.AsArray();
        if (complexes == null)
            return StatusCode(502, new { success = false, error = "Unexpected response format from eCourts API." });

        var result = complexes.Select(c => new
        {
            njdg_est_code = c?["njdg_est_code"]?.ToString(),
            complex_code = c?["complex_code"]?.ToString(),
            court_complex_name = c?["court_complex_name"]?.ToString(),
            lcourt_complex_name = c?["lcourt_complex_name"]?.ToString()
        }).ToList();

        var ok = Ok(new { success = true, complexes = result });
        _cache.Set(key, (IActionResult)ok, ComplexesTtl);
        return ok;
    }

    // ─── Court Search ────────────────────────────────────────────────────────
    [HttpGet("courts")]
    public async Task<IActionResult> GetCourts(
        [FromQuery] string state_code,
        [FromQuery] string dist_code,
        [FromQuery] string court_code)
    {
        if (string.IsNullOrWhiteSpace(state_code) ||
            string.IsNullOrWhiteSpace(dist_code) ||
            string.IsNullOrWhiteSpace(court_code))
        {
            return BadRequest(new { success = false, error = "state_code, dist_code, and court_code are required." });
        }

        var key = $"courts:{state_code}:{dist_code}:{court_code}";
        if (_cache.TryGetValue(key, out IActionResult? cached) && cached is not null)
            return cached;

        var (node, error) = await _ecourts.CallAsync("courtNameWebService.php", new()
        {
            ["state_code"] = state_code,
            ["dist_code"] = dist_code,
            ["court_code"] = court_code,
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        });

        if (error != null)
            return StatusCode(502, new { success = false, error });

        var courtNamesRaw = node?["courtNames"]?.ToString();
        if (courtNamesRaw == null)
            return StatusCode(502, new
            {
                success = false,
                error = "courtNames field missing from eCourts response.",
                rawResponse = node?.ToJsonString()
            });

        var courts = courtNamesRaw
            .Split('#', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry =>
            {
                var parts = entry.Split('~');
                return new
                {
                    code = parts.Length > 0 ? parts[0] : "",
                    name = parts.Length > 1 ? parts[1] : "",
                    isHeader = parts.Length > 0 && parts[0] == "D"
                };
            })
            .Where(c => c.code != "0")
            .ToList();

        var ok = Ok(new { success = true, courts });
        _cache.Set(key, (IActionResult)ok, CourtsTtl);
        return ok;
    }

    // ─── Latlong (court complex location) ────────────────────────────────────
    // GET /api/court/courts/latlong?state_code=&dist_code=&court_code=&complex_code=
    //
    // Optional Phase 6 enrichment — useful for premise-on-map UIs. Cached
    // alongside the regular court list since the underlying data is static.
    [HttpGet("courts/latlong")]
    public async Task<IActionResult> GetLatLong(
        [FromQuery] string state_code,
        [FromQuery] string dist_code,
        [FromQuery] string court_code,
        [FromQuery] string complex_code,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state_code) ||
            string.IsNullOrWhiteSpace(dist_code) ||
            string.IsNullOrWhiteSpace(court_code) ||
            string.IsNullOrWhiteSpace(complex_code))
        {
            return BadRequest(new
            {
                success = false,
                error = "state_code, dist_code, court_code, complex_code are required."
            });
        }

        var key = $"latlong:{state_code}:{dist_code}:{court_code}:{complex_code}";
        if (_cache.TryGetValue(key, out IActionResult? cached) && cached is not null)
            return cached;

        var (node, error) = await _ecourts.CallAsync("latlong.php", new()
        {
            ["state_code"] = state_code,
            ["dist_code"] = dist_code,
            ["court_code"] = court_code,
            ["complex_code"] = complex_code
        }, ct: ct);

        if (error != null) return StatusCode(502, new { success = false, error });

        var ok = Ok(new
        {
            success = true,
            latitude = node?["latitude"]?.ToString(),
            longitude = node?["longitude"]?.ToString(),
            map_url = node?["map_url"]?.ToString(),
            court_complex = node?["court_complex"]?.ToString()
        });
        _cache.Set(key, (IActionResult)ok, CourtsTtl);
        return ok;
    }
}
