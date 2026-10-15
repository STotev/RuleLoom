using System.Text.Json;
using System.Text.RegularExpressions;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Validation;

namespace RuleLoom.Samples.RulesService;

public sealed record TenantRules(IReadOnlyList<RuleDefinition> Valid, IReadOnlyList<InvalidRule> Invalid);

/// <summary>
/// Reads a tenant's rules from <c>Rules/{tenantId}/*.json</c> on every call (no caching), so edits apply to the next
/// event. Each file holds a JSON array of rules. Rules that fail parsing or <see cref="RuleValidator"/> are reported
/// and skipped.
/// </summary>
public sealed partial class TenantRuleStore(IWebHostEnvironment environment, RuleValidator validator)
{
    /// <returns>The tenant's rules, or <c>null</c> if the tenant is unknown.</returns>
    public async Task<TenantRules?> LoadAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (!TenantIdPattern().IsMatch(tenantId))
            return null;

        var folder = Path.Combine(environment.ContentRootPath, "Rules", tenantId);
        if (!Directory.Exists(folder))
            return null;

        var valid = new List<RuleDefinition>();
        var invalid = new List<InvalidRule>();

        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order())
        {
            var fileName = Path.GetFileName(file);
            List<RuleDefinition> rules;

            try
            {
                rules = RuleJsonParser.ParseMany(await File.ReadAllTextAsync(file, cancellationToken));
            }
            catch (JsonException ex)
            {
                // A shape error anywhere in the file rejects the whole file.
                invalid.Add(new InvalidRule(fileName, null, [$"{ex.Path}: {ex.Message}"]));
                continue;
            }

            foreach (var rule in rules)
            {
                var validation = validator.Validate(rule);

                if (validation.IsValid)
                    valid.Add(rule);
                else
                    invalid.Add(new InvalidRule(fileName, rule.Name, validation.Errors.Select(e => $"{e.Path}: {e.Message}").ToList()));
            }
        }

        return new TenantRules(valid, invalid);
    }

    // Tenant IDs become folder names, so only allow plain names (no path traversal).
    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex TenantIdPattern();
}
