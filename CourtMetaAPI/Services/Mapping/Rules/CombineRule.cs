using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services.Mapping.Rules;

internal sealed class CombineRule : IRule
{
    public string Name => "combine";

    public JsonNode? Evaluate(JsonObject spec, JsonNode? node, MappingContext ctx, Func<JsonObject, JsonNode?, MappingContext, JsonNode?> evaluate)
    {
        if (spec["sources"] is not JsonArray sources) return null;
        var sep = spec["separator"]?.GetValue<string>() ?? " ";
        var skipEmpty = spec["skipEmpty"]?.GetValue<bool>() ?? true;

        var values = new List<string>();
        foreach (var s in sources)
        {
            if (s is not JsonObject so) continue;
            var v = evaluate(so, node, ctx);
            if (skipEmpty)
            {
                if (v is null) continue;
                var text = v.ToString();
                if (text.Length == 0) continue;
                values.Add(text);
            }
            else
            {
                values.Add(v?.ToString() ?? "");
            }
        }
        if (values.Count == 0) return null;
        return JsonValue.Create(string.Join(sep, values));
    }
}
