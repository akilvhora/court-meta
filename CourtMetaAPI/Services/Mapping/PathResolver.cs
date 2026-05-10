using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CourtMetaAPI.Services.Mapping;

/// <summary>
/// Path / key resolution helpers — direct port of extension/mapping/resolvers.js.
/// </summary>
internal static class PathResolver
{
    /// <summary>Walk a dotted path like "data.cino" or "items.0.name". Returns null if any segment is missing.</summary>
    public static JsonNode? GetValueAtPath(JsonNode? obj, string? path)
    {
        if (obj is null) return null;
        if (string.IsNullOrEmpty(path)) return obj;
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? cur = obj;
        foreach (var p in parts)
        {
            if (cur is null) return null;
            cur = cur switch
            {
                JsonObject o when o.TryGetPropertyValue(p, out var v) => v,
                JsonArray a when int.TryParse(p, out var idx) && idx >= 0 && idx < a.Count => a[idx],
                _ => null
            };
        }
        return cur;
    }

    /// <summary>Assign a value at a dotted path, creating intermediate objects as needed.</summary>
    public static void SetValueAtPath(JsonObject obj, string path, JsonNode? value)
    {
        if (string.IsNullOrEmpty(path)) return;
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        JsonObject cur = obj;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var p = parts[i];
            if (!cur.TryGetPropertyValue(p, out var next) || next is not JsonObject nextObj)
            {
                nextObj = new JsonObject();
                cur[p] = nextObj;
            }
            cur = nextObj;
        }
        cur[parts[^1]] = value;
    }

    /// <summary>First own-key of <paramref name="node"/> matching <paramref name="pattern"/>; null if no match.</summary>
    public static (string Key, JsonNode? Value)? FindKeyByRegex(JsonNode? node, string pattern, bool caseSensitive = false)
    {
        if (node is not JsonObject obj) return null;
        var re = new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        foreach (var kv in obj)
        {
            if (re.IsMatch(kv.Key)) return (kv.Key, kv.Value);
        }
        return null;
    }
}
