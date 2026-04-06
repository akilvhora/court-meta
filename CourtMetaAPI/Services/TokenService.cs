using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services;

/// <summary>
/// Manages the eCourts JWT token lifecycle.
///
/// The mobile app flow (mirrored here):
///   1. GET appReleaseWebService.php?params=encryptData({version,uid})  — no auth header
///   2. Decrypt response → extract "token" → cache it
///   3. Every subsequent request:  GET endpoint?params=encryptData(data)
///                                 Authorization: Bearer encryptData(token)
///   4. Every response may contain a rotated token → update cache
///   5. status:'N' + status_code:'401' → refresh token and retry once
/// </summary>
public class TokenService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TokenService> _logger;

    private string _token = string.Empty;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // The mobile app uses device.uuid:packageName; we use machineName:packageName.
    private static readonly string ServerUid =
        $"{Environment.MachineName}:in.gov.ecourts.eCourtsServices";

    public TokenService(IHttpClientFactory httpClientFactory, ILogger<TokenService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync()
    {
        if (!string.IsNullOrEmpty(_token))
            return _token;

        await RefreshAsync();
        return _token;
    }

    public void UpdateFromResponse(JsonNode? responseNode)
    {
        var newToken = responseNode?["token"]?.ToString();
        if (!string.IsNullOrEmpty(newToken))
            _token = newToken;
    }

    public void Invalidate() => _token = string.Empty;

    public async Task RefreshAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(_token)) return;

            _logger.LogInformation("Fetching eCourts JWT token…");

            var client = _httpClientFactory.CreateClient("eCourts");

            // Match the mobile app exactly:
            // GET appReleaseWebService.php?params=<encryptData({version,uid})>
            var payload = new { version = "9.0", uid = ServerUid };
            var encryptedParams = EncryptionHelper.EncryptData(payload);
            var url = $"appReleaseWebService.php?params={Uri.EscapeDataString(encryptedParams)}";

            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            var json = EncryptionHelper.DecryptResponse(body) ?? body;
            _logger.LogDebug("appReleaseWebService decrypted response: {Json}", json);

            var node = JsonNode.Parse(json);
            var token = node?["token"]?.ToString();

            if (!string.IsNullOrEmpty(token))
            {
                _token = token;
                _logger.LogInformation("eCourts JWT token acquired.");
            }
            else
            {
                _logger.LogWarning(
                    "appReleaseWebService.php did not return a token. Decrypted: {Json}", json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch eCourts JWT token.");
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
