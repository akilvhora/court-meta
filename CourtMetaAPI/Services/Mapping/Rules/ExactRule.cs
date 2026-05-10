using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services.Mapping.Rules;

internal sealed class ExactRule : IRule
{
    public string Name => "exact";

    public JsonNode? Evaluate(JsonObject spec, JsonNode? node, MappingContext ctx, Func<JsonObject, JsonNode?, MappingContext, JsonNode?> evaluate)
    {
        var path = spec["path"]?.GetValue<string>();
        var v = PathResolver.GetValueAtPath(node, path);
        return v?.DeepClone();
    }
}
