using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services.Mapping.Rules;

/// <summary>Iterates an array source and applies a sub-mapping to each row.</summary>
internal sealed class ListRule : IRule
{
    public string Name => "list";

    public JsonNode? Evaluate(JsonObject spec, JsonNode? node, MappingContext ctx, Func<JsonObject, JsonNode?, MappingContext, JsonNode?> evaluate)
    {
        var arr = spec["source"] is JsonObject src ? evaluate(src, node, ctx) : node;
        if (arr is not JsonArray items) return new JsonArray();

        var item = spec["item"] as JsonObject;
        var result = new JsonArray();
        for (int i = 0; i < items.Count; i++)
        {
            var row = items[i];
            var outObj = new JsonObject();
            if (item is not null)
            {
                foreach (var kv in item)
                {
                    ctx.Path.Add($"[{i}].{kv.Key}");
                    var sub = kv.Value as JsonObject ?? new JsonObject();
                    var value = evaluate(sub, row, ctx);
                    if (kv.Key.Contains('.'))
                        PathResolver.SetValueAtPath(outObj, kv.Key, value);
                    else
                        outObj[kv.Key] = value;
                    ctx.Path.RemoveAt(ctx.Path.Count - 1);
                }
            }
            result.Add(outObj);
        }
        return result;
    }
}
