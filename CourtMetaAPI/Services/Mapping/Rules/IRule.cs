using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services.Mapping.Rules;

internal interface IRule
{
    string Name { get; }
    JsonNode? Evaluate(JsonObject spec, JsonNode? node, MappingContext ctx, Func<JsonObject, JsonNode?, MappingContext, JsonNode?> evaluate);
}
