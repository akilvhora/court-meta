using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services.Mapping.Rules;

internal sealed class SwitchRule : IRule
{
    public string Name => "switch";

    public JsonNode? Evaluate(JsonObject spec, JsonNode? node, MappingContext ctx, Func<JsonObject, JsonNode?, MappingContext, JsonNode?> evaluate)
    {
        var path = spec["path"]?.GetValue<string>();
        var v = PathResolver.GetValueAtPath(node, path);
        if (v is null) return null;
        var key = v.ToString();
        if (spec["cases"] is not JsonObject cases) return null;
        if (cases.TryGetPropertyValue(key, out var match)) return match?.DeepClone();
        return null;
    }
}
