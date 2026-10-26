using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Validation;

namespace RuleLoom.AspNetCore;

/// <summary><c>POST {prefix}/validate</c>: parses and validates one rule or an array of rules, rule by rule.</summary>
internal static class ValidateEndpoint
{
    public static async Task<IResult> HandleAsync(HttpRequest request, RuleValidator validator, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken);

        JsonValueKind kind;
        try
        {
            using var document = JsonDocument.Parse(json);
            kind = document.RootElement.ValueKind;
        }
        catch (JsonException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The body isn't valid JSON.", detail: ex.Message);
        }

        return kind switch
        {
            JsonValueKind.Object => Results.Ok(ValidateOne(json, validator)),
            JsonValueKind.Array => Results.Ok(RuleJsonParser.ParseEach(json)
                .Select(result => result.IsParsed ? Validate(result.Index, result.Rule, validator) : Malformed(result.Index, result.Error))
                .ToList()),
            _ => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Expected a rule (a JSON object) or an array of rules.")
        };
    }

    private static RuleValidationResponse ValidateOne(string json, RuleValidator validator)
    {
        RuleDefinition rule;
        try
        {
            rule = RuleJsonParser.Parse(json);
        }
        catch (RuleParseException ex)
        {
            return Malformed(null, ex);
        }

        return Validate(null, rule, validator);
    }

    private static RuleValidationResponse Validate(int? index, RuleDefinition rule, RuleValidator validator)
    {
        var result = validator.Validate(rule);
        return new RuleValidationResponse(index, rule.Id, true, result.IsValid, result.Errors, result.Warnings);
    }

    private static RuleValidationResponse Malformed(int? index, RuleParseException error)
    {
        var path = error.Path ?? "$";
        var message = error.Message.EndsWith($" (at {path})", StringComparison.Ordinal)
            ? error.Message[..^$" (at {path})".Length]
            : error.Message;

        // The parser locates a rule of an array as "$[i]"; responses are relative to the rule.
        var prefix = $"$[{index}]";
        if (index is not null && path.StartsWith(prefix, StringComparison.Ordinal))
            path = "$" + path[prefix.Length..];

        return new RuleValidationResponse(index, null, false, false, [new RuleValidationError(path, message)], []);
    }
}
