namespace CourtMetaAPI.Services.Mapping.Rules;

internal static class RuleRegistry
{
    /// <summary>
    /// Registry — add a new rule by adding the class here. Mirrors
    /// extension/mapping/rules/index.js so both engines stay in sync.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IRule> All =
        new IRule[]
        {
            new ExactRule(),
            new RegexKeyRule(),
            new FindArrayRule(),
            new ListRule(),
            new SwitchRule(),
            new LiteralRule(),
            new CombineRule(),
            new CoalesceRule(),
            new TemplateRule()
        }.ToDictionary(r => r.Name, StringComparer.Ordinal);
}
