using System.Text.Json.Nodes;
using CourtMetaAPI.Services.Mapping.Rules;
using CourtMetaAPI.Services.Mapping.Transforms;

namespace CourtMetaAPI.Services.Mapping;

/// <summary>
/// Declarative mapping engine — port of extension/mapping/mapper.js.
///
/// Engine is pure / deterministic. Configs are the single source of truth — the
/// same JSON files drive the JS engine in the extension and this C# engine in
/// the API process. Adding a new rule type means adding a class to <see cref="RuleRegistry"/>
/// and a JS module under <c>extension/mapping/rules/</c>; both ship together.
/// </summary>
public sealed class MappingEngine
{
    private const int SupportedMajor = 1;

    private readonly IReadOnlyDictionary<string, IRule> _rules;

    public MappingEngine() : this(RuleRegistry.All) { }

    internal MappingEngine(IReadOnlyDictionary<string, IRule> rules)
    {
        _rules = rules;
    }

    /// <summary>Apply <paramref name="config"/> to <paramref name="input"/>; throws on unsupported config major version.</summary>
    public MappingResult Apply(JsonObject config, JsonNode? input)
    {
        ArgumentNullException.ThrowIfNull(config);

        var version = config["version"]?.GetValue<string>() ?? "1.0.0";
        var major = int.Parse(version.Split('.')[0]);
        if (major != SupportedMajor)
            throw new NotSupportedException($"Unsupported config major version {major} (engine supports {SupportedMajor})");

        var rootPath = config["root"]?.GetValue<string>();
        var root = string.IsNullOrEmpty(rootPath) ? input : PathResolver.GetValueAtPath(input, rootPath);

        var ctx = new MappingContext { Input = input, Root = root, Rules = _rules };
        var result = new JsonObject();

        if (config["fields"] is JsonObject fields)
        {
            foreach (var kv in fields)
            {
                ctx.Path.Clear();
                ctx.Path.Add(kv.Key);
                var spec = kv.Value as JsonObject ?? new JsonObject();
                var value = EvaluateSpec(spec, root, ctx);
                if (kv.Key.Contains('.'))
                    PathResolver.SetValueAtPath(result, kv.Key, value);
                else
                    result[kv.Key] = value;
            }
        }

        return new MappingResult(result, ctx.Diagnostics);
    }

    internal JsonNode? EvaluateSpec(JsonObject spec, JsonNode? node, MappingContext ctx)
    {
        var ruleName = spec["rule"]?.GetValue<string>();
        if (ruleName is null || !ctx.Rules.TryGetValue(ruleName, out var rule))
        {
            ctx.Diagnostics.Missing.Add(new DiagnosticEntry(string.Join('.', ctx.Path), $"unknown rule \"{ruleName}\""));
            return ApplyFallbacks(null, spec, ctx);
        }

        JsonNode? value;
        try
        {
            value = rule.Evaluate(spec, node, ctx, EvaluateSpec);
        }
        catch (Exception ex)
        {
            ctx.Diagnostics.Missing.Add(new DiagnosticEntry(string.Join('.', ctx.Path), ex.Message));
            value = null;
        }
        return ApplyFallbacks(value, spec, ctx);
    }

    private static JsonNode? ApplyFallbacks(JsonNode? value, JsonObject spec, MappingContext ctx)
    {
        if (value is null && spec.ContainsKey("default"))
        {
            ctx.Diagnostics.Fallback.Add(new DiagnosticEntry(string.Join('.', ctx.Path), "default"));
            value = spec["default"]?.DeepClone();
        }
        if (spec["transforms"] is JsonArray steps)
            value = TransformRegistry.Apply(steps, value);
        return value;
    }
}

public sealed record MappingResult(JsonObject Result, Diagnostics Diagnostics);
