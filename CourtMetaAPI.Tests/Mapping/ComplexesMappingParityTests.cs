using System.Text.Json.Nodes;
using CourtMetaAPI.Services.Mapping;
using Xunit;

namespace CourtMetaAPI.Tests.Mapping;

public class ComplexesMappingParityTests
{
    private static readonly string FixturesDir = Path.Combine(
        Path.GetDirectoryName(typeof(ComplexesMappingParityTests).Assembly.Location)!,
        "Fixtures", "complexes");

    [Fact]
    public void Pune_MatchesGolden()
    {
        var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(FixturesDir, "complexes_pune.raw.json")))!;
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(FixturesDir, "complexes_pune.golden.json")))!;

        var asm = typeof(MappingEngine).Assembly;
        using var stream = asm.GetManifestResourceStream("CourtMetaAPI.Mapping.Configs.complexesMapping.json")!;
        var config = (JsonObject)JsonNode.Parse(stream)!;

        var result = new MappingEngine().Apply(config, raw);

        var expected = Canonicalize(golden);
        var actual   = Canonicalize(result.Result);
        Assert.Equal(expected, actual);
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
            return "[" + string.Join(",", arr.Select(Canonicalize)) + "]";
        return node.ToJsonString();
    }
}
