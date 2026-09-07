using System.Text.Json;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Harborline.Kernel.SchemaValidation;

internal static class TimedSchemaDialect
{
    private static readonly Uri Draft202012Id = new("https://json-schema.org/draft/2020-12/schema");

    internal static BuildOptions Build(TimeSpan timeout)
    {
        var standardKeywords = new[]
            {
                Vocabulary.Draft202012_Core,
                Vocabulary.Draft202012_Applicator,
                Vocabulary.Draft202012_Validation,
                Vocabulary.Draft202012_MetaData,
                Vocabulary.Draft202012_FormatAnnotation,
                Vocabulary.Draft202012_Content,
                Vocabulary.Draft202012_Unevaluated,
            }
            .SelectMany(vocabulary => vocabulary.Keywords)
            .GroupBy(keyword => keyword.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .Where(handler => handler.Name != "pattern")
            .Append((IKeywordHandler)new TimedPatternKeyword(timeout))
            .ToList();

        var dialect = new Dialect(standardKeywords) { Id = Draft202012Id };
        var registry = new DialectRegistry();
        registry.Register(dialect);
        return new BuildOptions { Dialect = dialect, DialectRegistry = registry };
    }
}

internal sealed class TimedPatternKeyword(TimeSpan timeout) : IKeywordHandler
{
    public string Name => "pattern";

    public object? ValidateKeywordValue(JsonElement value)
        => value.ValueKind == JsonValueKind.String
            ? new Regex(value.GetString()!, RegexOptions.ECMAScript, timeout)
            : throw new ArgumentException("The `pattern` keyword value must be a string.");

    public KeywordEvaluation Evaluate(KeywordData data, EvaluationContext context)
    {
        if (context.Instance.ValueKind != JsonValueKind.String) return KeywordEvaluation.Ignore;
        var isMatch = ((Regex)data.Value!).IsMatch(context.Instance.GetString()!);
        return new KeywordEvaluation
        {
            Keyword = Name,
            IsValid = isMatch,
            ContributesToValidation = true,
            Error = isMatch ? null : "The string value did not match the required pattern.",
        };
    }

    public void BuildSubschemas(KeywordData data, BuildContext context) { }
}
