using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Harborline.Contracts.Fields;

namespace Harborline.Kernel.SchemaValidation;

internal static class RecordsDefinitionShape
{
    internal static void Validate(
        JsonElement value,
        List<RecordsRefusal> refusals)
        => Visit(value, typeof(RecordTypeDefinition), "", false, null, refusals);

    internal static void Validate(
        JsonElement value,
        IFieldDomainRuntime fieldDomains,
        List<RecordsRefusal> refusals)
        => Visit(value, typeof(RecordTypeDefinition), "", false, fieldDomains, refusals);

    private static void Visit(
        JsonElement value,
        Type type,
        string pointer,
        bool nullable,
        IFieldDomainRuntime? fieldDomains,
        List<RecordsRefusal> refusals)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!nullable) Refuse("null_forbidden", pointer, refusals);
            return;
        }
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(ValueDomainDefinition) && fieldDomains is not null)
        {
            refusals.AddRange(fieldDomains.ValidateValueDomainJson(value.GetRawText(), pointer)
                .Select(refusal => new RecordsRefusal(refusal.Code, refusal.JsonPointer, refusal.Message)));
            return;
        }
        if (type == typeof(string))
        {
            if (value.ValueKind != JsonValueKind.String) Refuse("string_required", pointer, refusals);
            return;
        }
        if (type == typeof(bool))
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Refuse("boolean_required", pointer, refusals);
            return;
        }
        if (type == typeof(int))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out _)) Refuse("integer_required", pointer, refusals);
            return;
        }
        if (type.IsEnum)
        {
            if (value.ValueKind != JsonValueKind.String
                || !Enum.GetNames(type).Any(name => JsonNamingPolicy.SnakeCaseLower.ConvertName(name) == value.GetString()))
                Refuse("enum_invalid", pointer, refusals);
            return;
        }
        if (type.IsGenericType)
        {
            var generic = type.GetGenericTypeDefinition();
            if (generic == typeof(IReadOnlyDictionary<,>))
            {
                if (value.ValueKind != JsonValueKind.Object) { Refuse("object_required", pointer, refusals); return; }
                foreach (var property in value.EnumerateObject())
                    Visit(property.Value, type.GenericTypeArguments[1], Child(pointer, property.Name), false,
                        fieldDomains, refusals);
                return;
            }
            if (generic == typeof(IReadOnlyList<>))
            {
                if (value.ValueKind != JsonValueKind.Array) { Refuse("array_required", pointer, refusals); return; }
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    Visit(item, type.GenericTypeArguments[0], $"{pointer}/{index++}", false,
                        fieldDomains, refusals);
                return;
            }
        }
        if (value.ValueKind != JsonValueKind.Object) { Refuse("object_required", pointer, refusals); return; }
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var nullability = new NullabilityInfoContext();
        foreach (var property in value.EnumerateObject())
        {
            var child = Child(pointer, property.Name);
            if (!seen.Add(property.Name)) Refuse("member_duplicate", child, refusals);
            if (!properties.TryGetValue(property.Name, out var declared))
            {
                Refuse("member_unknown", child, refusals);
                continue;
            }
            Visit(property.Value, declared.PropertyType, child,
                nullability.Create(declared).ReadState == NullabilityState.Nullable, fieldDomains, refusals);
        }
        foreach (var (name, property) in properties)
        {
            if (!seen.Contains(name) && property.IsDefined(typeof(RequiredMemberAttribute)))
                Refuse("member_required", Child(pointer, name), refusals);
        }
    }

    private static string Child(string pointer, string member)
        => pointer + "/" + member.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static void Refuse(string suffix, string pointer, List<RecordsRefusal> refusals)
        => refusals.Add(new("records.definition." + suffix, pointer, "The member does not match the Records grammar."));
}
