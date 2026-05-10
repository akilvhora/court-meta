namespace CourtMetaAPI.Services.Endpoints;

/// <summary>
/// One catalog row per public endpoint. Drives:
/// — the parse-on-tier action filter
/// — the published list of scopes the issuer tool may grant
/// — observability (endpoint key in request logs)
/// — the doc-generator (forthcoming).
///
/// Adding an endpoint = add a row + (if parseable) a *Mapping.json file +
/// parity-test fixture. Removing one = a breaking change for any JWT that
/// names it as a scope; bump <see cref="SchemaVersion"/> instead.
/// </summary>
public sealed record EndpointDefinition(
    string  Key,
    string  Path,
    string? MappingConfig,
    string? SchemaVersion)
{
    public bool IsParseable => MappingConfig is not null;
    public string ScopeName => $"parse:{Key}";
}
