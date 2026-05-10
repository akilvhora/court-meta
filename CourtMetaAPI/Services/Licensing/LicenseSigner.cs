using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services.Licensing;

/// <summary>
/// RS256 JWT issuer used by the offline <c>cm-issue</c> tool. Lives in the API
/// project so the issuer, the validator, and the test signer all share one
/// canonical claim layout.
/// </summary>
public static class LicenseSigner
{
    public static string Sign(LicenseRequest req, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (string.IsNullOrEmpty(privateKeyPem))
            throw new ArgumentException("Private key PEM is required", nameof(privateKeyPem));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var header = new JsonObject
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT",
            ["kid"] = req.KeyId
        };
        var payload = new JsonObject
        {
            ["sub"]    = req.Customer,
            ["iat"]    = req.IssuedAt.ToUnixTimeSeconds(),
            ["exp"]    = req.ExpiresAt.ToUnixTimeSeconds(),
            ["scopes"] = new JsonArray(req.Scopes.Select(s => (JsonNode)JsonValue.Create(s)).ToArray())
        };

        var headerSeg  = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSeg = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signing    = Encoding.ASCII.GetBytes($"{headerSeg}.{payloadSeg}");
        var signature  = rsa.SignData(signing, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{headerSeg}.{payloadSeg}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record LicenseRequest(
    string         Customer,
    IReadOnlyList<string> Scopes,
    string         KeyId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);
