using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services.Mapping.Rules;

internal sealed class LiteralRule : IRule
{
    public string Name => "literal";

    public JsonNode? Evaluate(JsonObject spec, JsonNode? node, MappingContext ctx, Func<JsonObject, JsonNode?, MappingContext, JsonNode?> evaluate)
    {
        return spec["value"]?.DeepClone();
    }
}
