using System.Text.Json;
using System.Text.RegularExpressions;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Validation;

namespace RuleLoom.Samples.AspNetCore;

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
            IReadOnlyList<RuleParseResult> results;

            try
            {
                results = RuleJsonParser.ParseEach(await File.ReadAllTextAsync(file, cancellationToken));
            }
            catch (JsonException ex)
            {
                // Malformed JSON, or not an array: there are no rules to tell apart.
                invalid.Add(new InvalidRule(fileName, null, [$"{ex.Path}: {ex.Message}"]));
                continue;
            }

            foreach (var result in results)
            {
                if (!result.IsParsed)
                {
                    // A malformed rule is skipped; the rest of the file still loads.
                    invalid.Add(new InvalidRule(fileName, null, [result.Error.Message]));
                    continue;
                }

                var validation = validator.Validate(result.Rule);

                if (validation.IsValid)
                    valid.Add(result.Rule);
                else
                    invalid.Add(new InvalidRule(fileName, result.Rule.Name, validation.Errors.Select(e => $"{e.Path}: {e.Message}").ToList()));
            }
        }

        return new TenantRules(valid, invalid);
    }

    // Tenant IDs become folder names, so only allow plain names (no path traversal).
    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex TenantIdPattern();
}
