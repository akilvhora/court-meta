namespace CourtMetaAPI.Services.Licensing;

/// <summary>The decoded license, available on <c>HttpContext.Items["license"]</c>.</summary>
public sealed record LicenseClaims(
    string             Subject,
    DateTimeOffset     IssuedAt,
    DateTimeOffset     ExpiresAt,
    IReadOnlySet<string> Scopes,
    string             KeyId)
{
    /// <summary>True if <paramref name="scope"/> is granted directly or via the verb wildcard <c>parse:*</c>.</summary>
    public bool HasScope(string scope)
    {
        if (Scopes.Contains(scope)) return true;
        // Wildcard is verb-scoped: parse:* covers any parse:<key>
        var colon = scope.IndexOf(':');
        if (colon < 0) return false;
        var verb = scope[..colon];
        return Scopes.Contains(verb + ":*");
    }
}

/// <summary>Per-request license state. <see cref="Status"/> drives /token-status disclosure.</summary>
public sealed record LicenseState(LicenseStatus Status, LicenseClaims? Claims, string? Reason)
{
    public static LicenseState Missing()                   => new(LicenseStatus.Missing,  null,    null);
    public static LicenseState Valid(LicenseClaims claims) => new(LicenseStatus.Valid,    claims,  null);
    public static LicenseState Expired(string reason)      => new(LicenseStatus.Expired,  null,    reason);
    public static LicenseState Invalid(string reason)      => new(LicenseStatus.Invalid,  null,    reason);
}

public enum LicenseStatus
{
    Missing,
    Valid,
    Expired,
    Invalid
}
