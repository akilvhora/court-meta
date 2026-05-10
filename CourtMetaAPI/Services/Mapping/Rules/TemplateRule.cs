using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CourtMetaAPI.Services.Mapping.Rules;

internal sealed class TemplateRule : IRule
{
    public string Name => "template";
    private static readonly Regex Placeholder = new(@"\$\{(\w+)\}", RegexOptions.Compiled);

    public JsonNode? Evaluate(JsonObject spec, JsonNode? node, MappingContext ctx, Func<JsonObject, JsonNode?, MappingContext, JsonNode?> evaluate)
    {
        var tpl = spec["template"]?.GetValue<string>() ?? "";
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        if (spec["sources"] is JsonObject sources)
        {
            foreach (var kv in sources)
            {
                if (kv.Value is JsonObject so)
                {
                    var v = evaluate(so, node, ctx);
                    resolved[kv.Key] = v is null ? "" : v.ToString();
                }
            }
        }
        return JsonValue.Create(Placeholder.Replace(tpl, m =>
            resolved.TryGetValue(m.Groups[1].Value, out var s) ? s : ""));
    }
}
