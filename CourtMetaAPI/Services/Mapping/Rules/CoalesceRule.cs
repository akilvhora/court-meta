using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services.Mapping.Rules;

internal sealed class CoalesceRule : IRule
{
    public string Name => "coalesce";

    public JsonNode? Evaluate(JsonObject spec, JsonNode? node, MappingContext ctx, Func<JsonObject, JsonNode?, MappingContext, JsonNode?> evaluate)
    {
        if (spec["sources"] is not JsonArray sources) return null;
        foreach (var s in sources)
        {
            if (s is not JsonObject so) continue;
            var v = evaluate(so, node, ctx);
            if (v is null) continue;
            if (v is JsonValue jv && jv.TryGetValue<string>(out var text) && text.Length == 0) continue;
            return v;
        }
        return null;
    }
}
