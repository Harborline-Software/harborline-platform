using System.Text.Json;
using Harborline.Contracts.Fields;
using Json.Schema;

namespace Harborline.Kernel.SchemaValidation;

internal sealed class LiteralDomainKeyword(IFieldDomainRuntime? runtime) : IKeywordHandler
{
    internal const string KeywordName = "x-harborline-literal-domain";

    public string Name => KeywordName;

    public object ValidateKeywordValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A literal-domain binding must be an object.");

        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in value.EnumerateObject())
        {
            if (member.Name != "values" || !members.Add(member.Name))
                throw new ArgumentException("A literal-domain binding contains an unknown or duplicate member.");
        }
        if (members.Count != 1)
            throw new ArgumentException("A literal-domain binding requires values.");

        var values = value.GetProperty("values");
        if (values.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Literal-domain values must be an array of strings.");
        var literals = new List<string>();
        foreach (var literal in values.EnumerateArray())
        {
            if (literal.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Literal-domain values must be an array of strings.");
            literals.Add(literal.GetString()!);
        }

        if (runtime is null)
            throw new ArgumentException("An executable literal-domain binding requires a field-domain runtime.");
        return literals.AsReadOnly();
    }

    public KeywordEvaluation Evaluate(KeywordData data, EvaluationContext context)
    {
        if (context.Instance.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return KeywordEvaluation.Ignore;
        var refusals = runtime!.ValidateLiteralMembership(
            (IReadOnlyList<string>)data.Value!,
            context.Instance,
            context.InstanceLocation.ToString());
        return new KeywordEvaluation
        {
            Keyword = Name,
            IsValid = refusals.Count == 0,
            ContributesToValidation = true,
            Error = refusals.Count == 0 ? null : JsonSerializer.Serialize(refusals),
        };
    }

    public void BuildSubschemas(KeywordData data, BuildContext context)
    {
        if (context.LocalSchema.EnumerateObject().Count(member => member.Name == KeywordName) != 1)
            throw new ArgumentException("A schema must not repeat its literal-domain binding keyword.");
    }
}
