using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CourtMetaAPI.Services.Mapping;
using Xunit;

namespace CourtMetaAPI.Tests.Mapping;

/// <summary>
/// Parity tests — the C# engine output must match the JS-engine golden output for
/// the same raw fixture. If you add a rule, add a fixture pair.
/// </summary>
public class CnrMappingParityTests
{
    private static readonly string FixturesDir = Path.Combine(
        Path.GetDirectoryName(typeof(CnrMappingParityTests).Assembly.Location)!,
        "Fixtures", "cnr");

    [Fact]
    public void RegularCase_MatchesGolden()
    {
        var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(FixturesDir, "cnr_regular.raw.json")))!;
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(FixturesDir, "cnr_regular.golden.json")))!;

        var config = LoadConfig("cnrMapping.json");
        var engine = new MappingEngine();
        var result = engine.Apply(config, raw);

        AssertJsonEqual(golden, result.Result);
    }

    private static JsonObject LoadConfig(string fileName)
    {
        var asm = typeof(MappingEngine).Assembly;
        var name = $"CourtMetaAPI.Mapping.Configs.{fileName}";
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"Embedded mapping config {name} not found.");
        return (JsonObject)JsonNode.Parse(stream)!;
    }

    private static void AssertJsonEqual(JsonNode? expected, JsonNode? actual)
    {
        // Canonicalize both sides through System.Text.Json so key order
        // differences don't trip the comparison.
        var opts = new JsonSerializerOptions { WriteIndented = false };
        var e = Canonicalize(expected);
        var a = Canonicalize(actual);
        Assert.Equal(e, a);
    }

    private static string Canonicalize(JsonNode? node)
    {
        if (node is null) return "null";
        if (node is JsonObject obj)
        {
            var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in obj) sorted[kv.Key] = Canonicalize(kv.Value);
            return "{" + string.Join(",", sorted.Select(kv => $"\"{kv.Key}\":{kv.Value}")) + "}";
        }
        if (node is JsonArray arr)
        {
            return "[" + string.Join(",", arr.Select(Canonicalize)) + "]";
        }
        return node.ToJsonString();
    }
}
