namespace CourtMetaAPI.Services.Endpoints;

/// <summary>
/// Single catalogue of public endpoints. Both the licensing layer and the
/// parse filter key off this so a typo in one place can't desync the system.
///
/// Phase 2 ships only <c>cnr</c> as parseable (matches the only mapping config
/// shipped with the extension). Phase 5 fills in the rest.
/// </summary>
public static class EndpointRegistry
{
    public static readonly IReadOnlyList<EndpointDefinition> All = new[]
    {
        // Lookup endpoints — already lightly normalised by the controllers; no
        // declarative mapping config exists yet, so they're not parseable.
        new EndpointDefinition("states",       "/api/court/states",        null,                       null),
        new EndpointDefinition("districts",    "/api/court/districts",     null,                       null),
        new EndpointDefinition("complexes",    "/api/court/complexes",     "complexesMapping.json",    "complexes/1"),
        new EndpointDefinition("courts",       "/api/court/courts",        null,                       null),

        // CNR resolution — bare /cnr is the parseable surface. /cnr/bundle is
        // already a structured endpoint; we don't double-parse it.
        new EndpointDefinition("cnr",          "/api/court/cnr",           "cnrMapping.json",          "cnr/1"),

        // Search & cause-list — Phase 5 adds mapping configs.
        new EndpointDefinition("case-types",   "/api/court/case-types",        null, null),
        new EndpointDefinition("case-search",  "/api/court/search/case-number",   null, null),
        new EndpointDefinition("filing-search","/api/court/search/filing-number", null, null),
        new EndpointDefinition("advocate",     "/api/court/search/advocate",      null, null),
        new EndpointDefinition("cause-list",   "/api/court/cause-list",           null, null)
    };

    private static readonly Dictionary<string, EndpointDefinition> _byKey =
        All.ToDictionary(e => e.Key, StringComparer.OrdinalIgnoreCase);

    public static EndpointDefinition? ByKey(string key)
        => _byKey.TryGetValue(key, out var e) ? e : null;

    public static IEnumerable<string> ParseableScopes()
        => All.Where(e => e.IsParseable).Select(e => e.ScopeName);
}
