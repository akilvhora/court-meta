using System.Reflection;
using System.Text.Json.Nodes;

namespace CourtMetaAPI.Services.Mapping;

/// <summary>
/// Loads embedded mapping configs (built from extension/mapping/ at every build)
/// by logical name. Single instance per process; configs are immutable.
/// </summary>
public sealed class MappingConfigStore
{
    private readonly Dictionary<string, JsonObject> _configs;

    public MappingConfigStore()
    {
        _configs = new(StringComparer.Ordinal);
        var asm = typeof(MappingConfigStore).Assembly;
        const string prefix = "CourtMetaAPI.Mapping.Configs.";
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var fileName = name[prefix.Length..];
            // skip schema files; they're not configs the engine consumes.
            if (fileName.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)) continue;

            using var stream = asm.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded mapping config {name} not loadable");
            var node = JsonNode.Parse(stream)
                ?? throw new InvalidOperationException($"Mapping config {fileName} parsed to null");
            if (node is not JsonObject obj)
                throw new InvalidOperationException($"Mapping config {fileName} root is not an object");
            _configs[fileName] = obj;
        }
    }

    /// <summary>Look up a config by file name (e.g. "cnrMapping.json"). Null when absent.</summary>
    public JsonObject? TryGet(string fileName)
        => _configs.TryGetValue(fileName, out var c) ? c : null;

    public IReadOnlyCollection<string> Names => _configs.Keys;
}
