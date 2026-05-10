using System.Text.Json.Nodes;
using CourtMetaAPI.Services.Mapping.Rules;

namespace CourtMetaAPI.Services.Mapping;

internal sealed class MappingContext
{
    public required JsonNode? Input { get; init; }
    public required JsonNode? Root { get; init; }
    public required IReadOnlyDictionary<string, IRule> Rules { get; init; }
    public List<string> Path { get; } = new();
    public Diagnostics Diagnostics { get; } = new();
}

public sealed class Diagnostics
{
    public List<DiagnosticEntry> Missing { get; } = new();
    public List<DiagnosticEntry> Fallback { get; } = new();
}

public sealed record DiagnosticEntry(string Path, string Reason);
