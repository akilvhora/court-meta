using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using CourtMetaAPI.Services;

namespace CourtMetaAPI.Controllers;

[ApiController]
[Route("api/court")]
public class CourtController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenService _tokenService;

    public CourtController(IHttpClientFactory httpClientFactory, TokenService tokenService)
    {
        _httpClientFactory = httpClientFactory;
        _tokenService = tokenService;
    }

    /// <summary>
    /// Calls an eCourts endpoint exactly as the mobile app does:
    ///   GET /endpoint?params=encryptData(fields)
    ///   Authorization: Bearer encryptData(token)   (omitted for the auth call itself)
    ///
    /// Responses are AES-decrypted before JSON parsing.
    /// status:'N' errors are surfaced; status_code:'401' triggers a token refresh + retry.
    /// </summary>
    private async Task<(JsonNode? node, string? error)> CallECourts(
        string endpoint,
        Dictionary<string, string> fields,
        bool isAuthCall = false)
    {
        var client = _httpClientFactory.CreateClient("eCourts");

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                // Encrypt the request payload exactly as encryptData(data) in the app
                var encryptedParams = EncryptionHelper.EncryptData(fields);
                var url = $"{endpoint}?params={Uri.EscapeDataString(encryptedParams)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                if (!isAuthCall)
                {
                    var token = await _tokenService.GetTokenAsync();
                    if (!string.IsNullOrEmpty(token))
                    {
                        // Mobile app: Authorization: 'Bearer ' + encryptData(jwttoken)
                        var encryptedToken = EncryptionHelper.EncryptData(token);
                        request.Headers.Authorization =
                            new AuthenticationHeaderValue("Bearer", encryptedToken);
                    }
                }

                var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return (null, $"eCourts API returned HTTP {(int)response.StatusCode}: {body}");

                // Every response is AES-encrypted; decrypt before parsing
                var json = EncryptionHelper.DecryptResponse(body) ?? body;

                JsonNode? node;
                try { node = JsonNode.Parse(json); }
                catch { return (null, $"Unreadable response: {body[..Math.Min(200, body.Length)]}"); }

                // Update token cache from every response (server may rotate it)
                _tokenService.UpdateFromResponse(node);

                // Application-level error gate (mirrors callToWebService in main.js)
                var status = node?["status"]?.ToString();
                var statusCode = node?["status_code"]?.ToString();

                if (status == "N")
                {
                    if (statusCode == "401" && attempt == 0)
                    {
                        _tokenService.Invalidate();
                        await _tokenService.RefreshAsync();
                        continue;
                    }

                    // Server uses "Msg" (capital M) — check both casings
                    var msg = node?["Msg"]?.ToString() ?? node?["msg"]?.ToString();
                    return (null, string.IsNullOrEmpty(msg)
                        ? $"eCourts error. Response: {json[..Math.Min(300, json.Length)]}"
                        : msg);
                }

                return (node, null);
            }
            catch (TaskCanceledException)
            {
                return (null, "Request to eCourts API timed out.");
            }
            catch (Exception ex)
            {
                return (null, $"Error calling eCourts API: {ex.Message}");
            }
        }

        return (null, "eCourts API returned 401 even after token refresh.");
    }

    // ─── 0. Token status (debug) ───────────────────────────────────────────────
    // GET /api/court/token-status
    [HttpGet("token-status")]
    public async Task<IActionResult> TokenStatus()
    {
        var token = await _tokenService.GetTokenAsync();
        if (string.IsNullOrEmpty(token))
            return Ok(new { success = false, tokenAcquired = false, message = "No token. Check API logs." });

        // Show just enough of the token to confirm it looks like a JWT (3 segments)
        var preview = token.Length > 40 ? token[..20] + "…" + token[^10..] : token;
        return Ok(new { success = true, tokenAcquired = true, tokenPreview = preview });
    }

    // ─── 1. State Fetch ────────────────────────────────────────────────────────
    // GET /api/court/states
    [HttpGet("states")]
    public async Task<IActionResult> GetStates()
    {
        var timestamp = ((long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds).ToString();

        var (node, error) = await CallECourts("stateWebService.php", new Dictionary<string, string>
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

        return Ok(new { success = true, states = result });
    }

    // ─── 2. District Fetch ─────────────────────────────────────────────────────
    // GET /api/court/districts?state_code=1
    [HttpGet("districts")]
    public async Task<IActionResult> GetDistricts([FromQuery] string state_code)
    {
        if (string.IsNullOrWhiteSpace(state_code))
            return BadRequest(new { success = false, error = "state_code is required." });

        var (node, error) = await CallECourts("districtWebService.php", new Dictionary<string, string>
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

        return Ok(new { success = true, districts = result });
    }

    // ─── 3. Complex Fetch ──────────────────────────────────────────────────────
    // GET /api/court/complexes?state_code=1&dist_code=1
    [HttpGet("complexes")]
    public async Task<IActionResult> GetComplexes(
        [FromQuery] string state_code,
        [FromQuery] string dist_code)
    {
        if (string.IsNullOrWhiteSpace(state_code) || string.IsNullOrWhiteSpace(dist_code))
            return BadRequest(new { success = false, error = "state_code and dist_code are required." });

        var (node, error) = await CallECourts("courtEstWebService.php", new Dictionary<string, string>
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

        return Ok(new { success = true, complexes = result });
    }

    // ─── 4. Court Search ───────────────────────────────────────────────────────
    // GET /api/court/courts?state_code=1&dist_code=1&court_code=101
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

        var (node, error) = await CallECourts("courtNameWebService.php", new Dictionary<string, string>
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
            .ToList();

        // rawCourtNames included temporarily to diagnose display issues
        return Ok(new { success = true, courts, rawCourtNames = courtNamesRaw });
    }

    // ─── 5. CNR Search ─────────────────────────────────────────────────────────
    // GET /api/court/cnr?cino=MHPU010001232022
    [HttpGet("cnr")]
    public async Task<IActionResult> CnrSearch([FromQuery] string cino)
    {
        if (string.IsNullOrWhiteSpace(cino))
            return BadRequest(new { success = false, error = "cino is required." });

        if (cino.Length < 16)
            return BadRequest(new { success = false, error = "CNR number must be at least 16 characters." });

        // Step 1: Check if this CNR belongs to a filing case or regular case
        var (listNode, listError) = await CallECourts("listOfCasesWebService.php", new Dictionary<string, string>
        {
            ["cino"] = cino,
            ["version_number"] = "9.0",
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        });

        if (listError != null)
            return StatusCode(502, new { success = false, error = listError });

        if (listNode == null)
            return NotFound(new { success = false, error = "CNR number not found." });

        var caseNumber = listNode["case_number"]?.ToString();

        // Step 2a: case_number is null → filing case
        if (string.IsNullOrEmpty(caseNumber))
        {
            var (filingNode, filingError) = await CallECourts("filingCaseHistory.php", new Dictionary<string, string>
            {
                ["cino"] = cino,
                ["language_flag"] = "english",
                ["bilingual_flag"] = "0"
            });

            if (filingError != null)
                return StatusCode(502, new { success = false, error = filingError });

            var history = filingNode?["history"];
            if (history == null)
                return NotFound(new { success = false, error = "Filing case history not found for this CNR." });

            return Ok(new
            {
                success = true,
                data = new
                {
                    cino,
                    type = "filing",
                    caseNumber = (string?)null,
                    history
                }
            });
        }

        // Step 2b: regular case → fetch case history
        var (historyNode, historyError) = await CallECourts("caseHistoryWebService.php", new Dictionary<string, string>
        {
            ["cinum"] = cino,
            ["language_flag"] = "english",
            ["bilingual_flag"] = "0"
        });

        if (historyError != null)
            return StatusCode(502, new { success = false, error = historyError });

        var caseHistory = historyNode?["history"];
        if (caseHistory == null)
            return NotFound(new { success = false, error = "Case history not found for this CNR." });

        return Ok(new
        {
            success = true,
            data = new
            {
                cino,
                type = "case",
                caseNumber,
                history = caseHistory
            }
        });
    }
}
