using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CourtMetaAPI.Services.Mapping.Transforms;

/// <summary>
/// Pure transforms — port of extension/mapping/transforms.js.
/// Each entry takes (value, optsNode) and returns a new value. Names match the
/// JS engine so cnrMapping.json is consumable by both.
/// </summary>
internal static class TransformRegistry
{
    public static readonly IReadOnlyDictionary<string, Func<JsonNode?, JsonObject?, JsonNode?>> All = new Dictionary<string, Func<JsonNode?, JsonObject?, JsonNode?>>(StringComparer.Ordinal)
    {
        ["trim"]              = (v, _)    => v is JsonValue jv && jv.TryGetValue<string>(out var s) ? JsonValue.Create(s.Trim()) : v,
        ["upper"]             = (v, _)    => v is JsonValue jv && jv.TryGetValue<string>(out var s) ? JsonValue.Create(s.ToUpperInvariant()) : v,
        ["lower"]             = (v, _)    => v is JsonValue jv && jv.TryGetValue<string>(out var s) ? JsonValue.Create(s.ToLowerInvariant()) : v,
        ["nullIfEmpty"]       = (v, _)    => v is JsonValue jv && jv.TryGetValue<string>(out var s) && s.Length == 0 ? null : v,
        ["default"]           = (v, opts) => v is null ? opts?["value"]?.DeepClone() : v,
        ["toDate"]            = (v, _)    => NormalizeDate(v),
        ["parseAdvocateList"] = (v, _)    => ParseAdvocateList(v),
        ["parseActTable"]     = (v, _)    => ParseActTable(v),
        ["parseFirDetails"]   = (v, _)    => ParseFirDetails(v)
    };

    public static JsonNode? Apply(JsonArray? steps, JsonNode? value)
    {
        if (steps is null) return value;
        JsonNode? cur = value;
        foreach (var step in steps)
        {
            string? name;
            JsonObject? opts = null;
            if (step is JsonValue sv && sv.TryGetValue<string>(out var sn))
            {
                name = sn;
            }
            else if (step is JsonObject so)
            {
                name = so["fn"]?.GetValue<string>();
                opts = so;
            }
            else
            {
                continue;
            }
            if (name is null || !All.TryGetValue(name, out var fn)) continue;
            cur = fn(cur, opts);
        }
        return cur;
    }

    private static JsonNode? ParseFirDetails(JsonNode? v)
    {
        if (v is null) return null;
        var s = v.ToString().Trim();
        if (s.Length == 0) return null;
        var parts = s.Split('^').Select(p => p.Trim()).ToArray();
        var obj = new JsonObject
        {
            ["number"]        = parts.Length > 0 && parts[0].Length > 0 ? JsonValue.Create(parts[0]) : null,
            ["policeStation"] = parts.Length > 1 && parts[1].Length > 0 ? JsonValue.Create(parts[1]) : null,
            ["year"]          = parts.Length > 2 && parts[2].Length > 0 ? JsonValue.Create(parts[2]) : null
        };
        return obj;
    }

    private static readonly Regex TdCellRegex = new(@"<td\b[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagRegex    = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WsRegex     = new(@"\s+", RegexOptions.Compiled);

    private static JsonNode? ParseActTable(JsonNode? v)
    {
        var arr = new JsonArray();
        if (v is null) return arr;
        var html = v.ToString();
        var matches = TdCellRegex.Matches(html);
        if (matches.Count == 0) return arr;
        var raw = matches[^1].Groups[1].Value;
        var stripped = TagRegex.Replace(raw, " ")
                               .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
                               .Replace("&amp;",  "&", StringComparison.OrdinalIgnoreCase);
        stripped = WsRegex.Replace(stripped, " ").Trim();
        if (stripped.Length == 0) return arr;
        foreach (var s in stripped.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0))
            arr.Add(s);
        return arr;
    }

    private static readonly Regex BrRegex      = new(@"<\s*br\s*/?\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SplitItem    = new(@"\s*\d+\)\s*", RegexOptions.Compiled);
    private static readonly Regex AdvocateMark = new(@"Advocate\s*-",  RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AdvocatePfx  = new(@"^Advocate\s*-\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static JsonNode? ParseAdvocateList(JsonNode? v)
    {
        var arr = new JsonArray();
        if (v is null) return arr;
        var raw = v.ToString();
        var cleaned = BrRegex.Replace(raw, " ");
        cleaned = TagRegex.Replace(cleaned, " ")
                          .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
                          .Replace("&amp;",  "&", StringComparison.OrdinalIgnoreCase);
        cleaned = WsRegex.Replace(cleaned, " ").Trim();
        if (cleaned.Length == 0) return arr;

        var parts = SplitItem.Split(cleaned).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        foreach (var part in parts)
        {
            var match = AdvocateMark.Match(part);
            string? name, advocateName;
            if (!match.Success)
            {
                name = part.Trim();
                advocateName = null;
            }
            else
            {
                name = part[..match.Index].Trim();
                advocateName = AdvocatePfx.Replace(part[match.Index..], "").Trim();
            }
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(advocateName)) continue;
            arr.Add(new JsonObject
            {
                ["name"]         = string.IsNullOrEmpty(name) ? null : JsonValue.Create(name),
                ["advocateName"] = string.IsNullOrEmpty(advocateName) ? null : JsonValue.Create(advocateName)
            });
        }
        return arr;
    }

    private static readonly Regex IsoRegex = new(@"^(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})$", RegexOptions.Compiled);
    private static readonly Regex DmyRegex = new(@"^(\d{1,2})[-/.](\d{1,2})[-/.](\d{2}|\d{4})$", RegexOptions.Compiled);

    private static JsonNode? NormalizeDate(JsonNode? v)
    {
        if (v is null) return null;
        var s = v.ToString().Trim();
        if (s.Length == 0) return null;
        static string Pad2(string x) => x.Length == 1 ? "0" + x : x;

        var iso = IsoRegex.Match(s);
        if (iso.Success)
        {
            return JsonValue.Create($"{Pad2(iso.Groups[3].Value)}/{Pad2(iso.Groups[2].Value)}/{iso.Groups[1].Value}");
        }
        var m = DmyRegex.Match(s);
        if (!m.Success) return JsonValue.Create(s);
        return JsonValue.Create($"{Pad2(m.Groups[1].Value)}/{Pad2(m.Groups[2].Value)}/{m.Groups[3].Value}");
    }
}
