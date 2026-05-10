namespace CourtMetaAPI.Filters;

/// <summary>
/// Marks a controller action as belonging to a parseable endpoint. The string
/// must match an <c>EndpointDefinition.Key</c> in the registry; CI rejects
/// stale keys via <c>EndpointRegistryTests</c>.
///
/// Putting this attribute on an action is the *only* way to opt into the
/// tier-aware response envelope — controllers that don't carry it always
/// behave as free-tier raw passthrough.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ParseableEndpointAttribute : Attribute
{
    public string EndpointKey { get; }

    public ParseableEndpointAttribute(string endpointKey)
    {
        if (string.IsNullOrWhiteSpace(endpointKey))
            throw new ArgumentException("endpointKey is required", nameof(endpointKey));
        EndpointKey = endpointKey;
    }
}
