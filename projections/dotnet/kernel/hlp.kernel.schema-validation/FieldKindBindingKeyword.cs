using System.Text.Json;
using Harborline.Contracts.Fields;
using Json.Schema;

namespace Harborline.Kernel.SchemaValidation;

internal sealed class FieldKindBindingKeyword(IFieldKindRuntime? runtime) : IKeywordHandler
{
    internal const string KeywordName = "x-harborline-field-kind";

    public string Name => KeywordName;

    public object ValidateKeywordValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A field-kind binding must be an object.");

        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in value.EnumerateObject())
        {
            if (member.Name is not ("kind_id" or "version" or "parameters") || !members.Add(member.Name))
                throw new ArgumentException("A field-kind binding contains an unknown or duplicate member.");
        }
        if (members.Count != 3)
            throw new ArgumentException("A field-kind binding requires kind_id, version and parameters.");

        var kindId = ReadIdentity(value, "kind_id");
        var version = ReadIdentity(value, "version");
        var parameterObject = value.GetProperty("parameters");
        if (parameterObject.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Field-kind parameters must be an object of strings.");
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in parameterObject.EnumerateObject())
        {
            if (parameter.Value.ValueKind != JsonValueKind.String
                || !parameters.TryAdd(parameter.Name, parameter.Value.GetString()!))
                throw new ArgumentException("Field-kind parameters must be unique string members.");
        }

        if (runtime is null)
            throw new ArgumentException("An executable field-kind binding requires a field-kind runtime.");
        return runtime.Bind(new(kindId, version, parameters), "/" + KeywordName);
    }

    public KeywordEvaluation Evaluate(KeywordData data, EvaluationContext context)
    {
        var refusals = ((ICompiledFieldKind)data.Value!).Validate(
            context.Instance, context.InstanceLocation.ToString());
        return new KeywordEvaluation
        {
            Keyword = Name,
            IsValid = refusals.Count == 0,
            ContributesToValidation = true,
            // The library carries the complete structured refusal list in its per-evaluation
            // error channel, including failures sharing an instance pointer.
            Error = refusals.Count == 0 ? null : JsonSerializer.Serialize(refusals),
        };
    }

    public void BuildSubschemas(KeywordData data, BuildContext context)
    {
        if (context.LocalSchema.EnumerateObject().Count(member => member.Name == KeywordName) != 1)
            throw new ArgumentException("A schema must not repeat its field-kind binding keyword.");
    }

    private static string ReadIdentity(JsonElement binding, string name)
    {
        var value = binding.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException("Field-kind identities must be nonempty strings.");
        return value.GetString()!;
    }
}
