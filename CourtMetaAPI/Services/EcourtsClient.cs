using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services;

/// <summary>
/// Thin transport over the eCourts mobile API.
///
/// Mirrors the mobile app's <c>callToWebService(url, data, callback)</c>:
///   1. Encrypt the request payload with <see cref="EncryptionHelper.EncryptData"/>.
///   2. Attach <c>Authorization: Bearer encryptData(jwttoken)</c> (omitted only for the auth call).
///   3. Decrypt the response with <see cref="EncryptionHelper.DecryptResponse"/>.
///   4. On status="N" + status_code="401", invalidate the cached token and retry once.
///
/// Returns either a parsed <see cref="JsonNode"/> or a structured error string.
/// All controllers should funnel through this — do not call HttpClient directly.
/// </summary>
public class EcourtsClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenService _tokenService;
    private readonly ILogger<EcourtsClient> _logger;
    private readonly TelemetryService? _telemetry;

    public EcourtsClient(
        IHttpClientFactory httpClientFactory,
        TokenService tokenService,
        ILogger<EcourtsClient> logger,
        TelemetryService? telemetry = null)
    {
        _httpClientFactory = httpClientFactory;
        _tokenService = tokenService;
        _logger = logger;
        _telemetry = telemetry;
    }

    /// <summary>JSON call. Decrypts the response into a JsonNode.</summary>
    public async Task<(JsonNode? node, string? error)> CallAsync(
        string endpoint,
        Dictionary<string, string> fields,
        bool isAuthCall = false,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("eCourts");

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var encryptedParams = EncryptionHelper.EncryptData(fields);
                var url = $"{endpoint}?params={Uri.EscapeDataString(encryptedParams)}";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                if (!isAuthCall)
                {
                    var token = await _tokenService.GetTokenAsync();
                    if (!string.IsNullOrEmpty(token))
                    {
                        var encryptedToken = EncryptionHelper.EncryptData(token);
                        request.Headers.Authorization =
                            new AuthenticationHeaderValue("Bearer", encryptedToken);
                    }
                }

                var response = await client.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                    return (null, $"eCourts API returned HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");

                var json = EncryptionHelper.DecryptResponse(body) ?? body;

                JsonNode? node;
                try { node = JsonNode.Parse(json); }
                catch { return (null, $"Unreadable response: {Truncate(body, 200)}"); }

                _tokenService.UpdateFromResponse(node);

                var status = node?["status"]?.ToString();
                var statusCode = node?["status_code"]?.ToString();

                if (status == "N")
                {
                    if (statusCode == "401" && attempt == 0)
                    {
                        _logger.LogInformation("eCourts {Endpoint} returned 401 — refreshing token and retrying.", endpoint);
                        _telemetry?.TrackTokenRefresh(endpoint);
                        _tokenService.Invalidate();
                        await _tokenService.RefreshAsync();
                        continue;
                    }

                    var msg = node?["Msg"]?.ToString() ?? node?["msg"]?.ToString();
                    return (null, string.IsNullOrEmpty(msg)
                        ? $"eCourts error. Response: {Truncate(json, 300)}"
                        : msg);
                }

                return (node, null);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return (null, "Request to eCourts API timed out.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling eCourts {Endpoint}", endpoint);
                return (null, $"Error calling eCourts API: {ex.Message}");
            }
        }

        return (null, "eCourts API returned 401 even after token refresh.");
    }

    /// <summary>
    /// Binary call — used for endpoints that return a non-JSON, non-encrypted body
    /// (e.g. <c>preTrialOrder_pdf.php</c> which streams a PDF directly).
    ///
    /// The request envelope (encrypted <c>params</c> + Bearer JWT) is built identically;
    /// the response body is returned verbatim as bytes.
    /// </summary>
    public async Task<(byte[]? bytes, string? contentType, string? error)> CallBinaryAsync(
        string endpoint,
        Dictionary<string, string> fields,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("eCourts");

        try
        {
            var encryptedParams = EncryptionHelper.EncryptData(fields);
            var url = $"{endpoint}?params={Uri.EscapeDataString(encryptedParams)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            var token = await _tokenService.GetTokenAsync();
            if (!string.IsNullOrEmpty(token))
            {
                var encryptedToken = EncryptionHelper.EncryptData(token);
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", encryptedToken);
            }

            var response = await client.SendAsync(request, ct);
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            if (!response.IsSuccessStatusCode)
                return (null, null, $"eCourts API returned HTTP {(int)response.StatusCode}");

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return (bytes, contentType, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, null, "Request to eCourts API timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling eCourts binary {Endpoint}", endpoint);
            return (null, null, $"Error calling eCourts API: {ex.Message}");
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : s[..Math.Min(max, s.Length)];
}
