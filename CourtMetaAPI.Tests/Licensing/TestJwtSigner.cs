using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CourtMetaAPI.Tests.Licensing;

internal static class TestJwtSigner
{
    /// <summary>
    /// Sign a JWT with the dev private key shipped at <c>tools/dev-keys/cm-dev-2026.priv.pem</c>.
    /// Mirrors what the Phase 3 issuer tool will do; defined here so the
    /// validator test can run before that tool exists.
    /// </summary>
    public static string Sign(string subject, string[] scopes, string kid, DateTimeOffset issuedAt, DateTimeOffset expiresAt, string privateKeyPath)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(privateKeyPath));

        var header = new JsonObject
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT",
            ["kid"] = kid
        };
        var payload = new JsonObject
        {
            ["sub"]    = subject,
            ["iat"]    = issuedAt.ToUnixTimeSeconds(),
            ["exp"]    = expiresAt.ToUnixTimeSeconds(),
            ["scopes"] = new JsonArray(scopes.Select(s => (JsonNode)JsonValue.Create(s)).ToArray())
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
