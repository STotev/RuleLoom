using System.Text.Json;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Validation;

namespace RuleLoom.Samples.AspNetCore.Rules;

public sealed record ShopRules(IReadOnlyList<RuleDefinition> Valid, IReadOnlyList<RuleWithMistakes> Invalid);

public sealed record RuleWithMistakes(string File, string? Rule, IReadOnlyList<string> Errors);

/// <summary>
/// Reads the shop's rules from <c>Rules/*.json</c> on every call (no caching), so a rule edited during a demo applies to
/// the next event. Each file holds a JSON array of rules; rules that can't be parsed or fail
/// <see cref="RuleValidator"/> are reported and left out, and the rest of the file still loads. Storing rules is the
/// app's choice: a database, per tenant, with a UI in front.
/// </summary>
public sealed class RuleStore(IWebHostEnvironment environment, RuleValidator validator)
{
    public async Task<ShopRules> LoadAsync(CancellationToken cancellationToken)
    {
        var valid = new List<RuleDefinition>();
        var invalid = new List<RuleWithMistakes>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(environment.ContentRootPath, "Rules"), "*.json").Order())
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
                invalid.Add(new RuleWithMistakes(fileName, null, [$"{ex.Path}: {ex.Message}"]));
                continue;
            }

            foreach (var result in results)
            {
                if (!result.IsParsed)
                {
                    invalid.Add(new RuleWithMistakes(fileName, null, [result.Error.Message]));
                    continue;
                }

                var validation = validator.Validate(result.Rule);
                if (validation.IsValid)
                    valid.Add(result.Rule);
                else
                    invalid.Add(new RuleWithMistakes(fileName, result.Rule.Name, validation.Errors.Select(e => $"{e.Path}: {e.Message}").ToList()));
            }
        }

        return new ShopRules(valid, invalid);
    }
}
