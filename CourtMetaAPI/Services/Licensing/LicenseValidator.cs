using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services.Licensing;

/// <summary>
/// Stateless RS256 JWT verifier — kept dependency-free on purpose so the
/// single-file API exe doesn't pull in Microsoft.IdentityModel.* (which would
/// add ~3 MB and a chain of versioned deps).
///
/// Spec compliance is intentionally narrow: only RS256, only the claims this
/// service mints (sub/iat/exp/scopes/kid).
/// </summary>
public sealed class LicenseValidator
{
    private readonly LicensePublicKeys _keys;

    public LicenseValidator(LicensePublicKeys keys)
    {
        _keys = keys;
    }

    public LicenseState Validate(string? authorizationHeader, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)) return LicenseState.Missing();

        const string bearer = "Bearer ";
        if (!authorizationHeader.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
            return LicenseState.Invalid("Authorization scheme must be Bearer.");

        var token = authorizationHeader[bearer.Length..].Trim();
        var parts = token.Split('.');
        if (parts.Length != 3) return LicenseState.Invalid("Malformed JWT (expected 3 segments).");

        JsonObject header, payload;
        byte[] signature;
        try
        {
            header    = (JsonObject)JsonNode.Parse(Base64UrlDecode(parts[0]))!;
            payload   = (JsonObject)JsonNode.Parse(Base64UrlDecode(parts[1]))!;
            signature = Base64UrlDecodeBytes(parts[2]);
        }
        catch (Exception ex)
        {
            return LicenseState.Invalid($"JWT decode failed: {ex.Message}");
        }

        if (!"RS256".Equals(header["alg"]?.GetValue<string>(), StringComparison.Ordinal))
            return LicenseState.Invalid("Only RS256 is accepted.");

        var kid = header["kid"]?.GetValue<string>();
        if (string.IsNullOrEmpty(kid))
            return LicenseState.Invalid("Missing kid header claim.");
        var rsa = _keys.TryGet(kid);
        if (rsa is null)
            return LicenseState.Invalid($"Unknown signing key: {kid}");

        var signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        if (!rsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            return LicenseState.Invalid("Signature failed verification.");

        var sub = payload["sub"]?.GetValue<string>() ?? "";
        var iat = payload["iat"]?.GetValue<long>() ?? 0;
        var exp = payload["exp"]?.GetValue<long>() ?? 0;
        if (exp <= 0) return LicenseState.Invalid("Missing exp claim.");

        var expAt = DateTimeOffset.FromUnixTimeSeconds(exp);
        if (expAt < now)
            return LicenseState.Expired($"License expired on {expAt:O}.");

        var scopes = new HashSet<string>(StringComparer.Ordinal);
        if (payload["scopes"] is JsonArray scopesArr)
        {
            foreach (var s in scopesArr)
            {
                if (s is JsonValue sv && sv.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text))
                    scopes.Add(text);
            }
        }

        var claims = new LicenseClaims(
            Subject:    sub,
            IssuedAt:   DateTimeOffset.FromUnixTimeSeconds(iat),
            ExpiresAt:  expAt,
            Scopes:     scopes,
            KeyId:      kid);
        return LicenseState.Valid(claims);
    }

    private static string Base64UrlDecode(string input)
        => Encoding.UTF8.GetString(Base64UrlDecodeBytes(input));

    private static byte[] Base64UrlDecodeBytes(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "=";  break;
        }
        return Convert.FromBase64String(s);
    }
}
