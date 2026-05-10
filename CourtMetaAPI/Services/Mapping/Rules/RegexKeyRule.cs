using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services.Mapping.Rules;

internal sealed class RegexKeyRule : IRule
{
    public string Name => "regexKey";

    public JsonNode? Evaluate(JsonObject spec, JsonNode? node, MappingContext ctx, Func<JsonObject, JsonNode?, MappingContext, JsonNode?> evaluate)
    {
        if (node is not JsonObject obj) return null;
        var pattern = spec["pattern"]?.GetValue<string>();
        if (string.IsNullOrEmpty(pattern)) return null;
        var caseSensitive = spec["caseSensitive"]?.GetValue<bool>() ?? false;

        var found = PathResolver.FindKeyByRegex(obj, pattern, caseSensitive);
        if (found.HasValue) return found.Value.Value?.DeepClone();

        // Positional fallback when no regex match.
        if (spec["fallbackIndex"] is JsonValue fv && fv.TryGetValue<int>(out var idx))
        {
            var keys = obj.Select(p => p.Key).ToList();
            if (idx >= 0 && idx < keys.Count)
                return obj[keys[idx]]?.DeepClone();
        }
        return null;
    }
}
