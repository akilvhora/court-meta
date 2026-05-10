using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CourtMetaAPI.Services.Mapping.Rules;

/// <summary>
/// Walks the input tree and returns the first array of object rows whose container
/// key matches <c>keyPattern</c>. If no keyed match, returns the first array whose
/// rows contain at least one key matching <c>rowKeyPattern</c>.
/// </summary>
internal sealed class FindArrayRule : IRule
{
    public string Name => "findArray";

    public JsonNode? Evaluate(JsonObject spec, JsonNode? node, MappingContext ctx, Func<JsonObject, JsonNode?, MappingContext, JsonNode?> evaluate)
    {
        var keyRe = spec["keyPattern"] is JsonValue kv && kv.TryGetValue<string>(out var kp)
            ? new Regex(kp, RegexOptions.IgnoreCase)
            : null;
        var rowRe = spec["rowKeyPattern"] is JsonValue rv && rv.TryGetValue<string>(out var rp)
            ? new Regex(rp, RegexOptions.IgnoreCase)
            : null;
        var maxDepth = spec["maxDepth"] is JsonValue dv && dv.TryGetValue<int>(out var dd) ? dd : 4;

        return Walk(node, 0, maxDepth, keyRe, rowRe) ?? new JsonArray();
    }

    private static JsonNode? Walk(JsonNode? obj, int depth, int maxDepth, Regex? keyRe, Regex? rowRe)
    {
        if (obj is null || depth > maxDepth) return null;

        if (obj is JsonObject jo)
        {
            // (1) keyed match: child key matches keyPattern AND its value is array of objects
            if (keyRe is not null)
            {
                foreach (var kv in jo)
                {
                    if (keyRe.IsMatch(kv.Key) && kv.Value is JsonArray arr && arr.Count > 0 && arr[0] is JsonObject)
                        return arr.DeepClone();
                }
            }
            foreach (var kv in jo)
            {
                var found = Walk(kv.Value, depth + 1, maxDepth, keyRe, rowRe);
                if (found is not null) return found;
            }
        }
        else if (obj is JsonArray ja && ja.Count > 0 && ja[0] is JsonObject first)
        {
            // (2) row-pattern match
            if (rowRe is null) return obj.DeepClone();
            foreach (var k in first.Select(p => p.Key))
                if (rowRe.IsMatch(k)) return obj.DeepClone();

            foreach (var item in ja)
            {
                var found = Walk(item, depth + 1, maxDepth, keyRe, rowRe);
                if (found is not null) return found;
            }
        }
        return null;
    }
}
