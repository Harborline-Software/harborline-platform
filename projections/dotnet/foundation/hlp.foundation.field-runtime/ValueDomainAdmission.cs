using System.Globalization;
using System.Text.Json;
using Harborline.Contracts.Fields;

namespace Harborline.Foundation.FieldRuntime;

/// <summary>Admits the shared three-source permitted-value declaration.</summary>
public static class ValueDomainAdmission
{
    private static readonly JsonSerializerOptions DeclarationJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Refuses declarations that do not name exactly one permitted-value source.</summary>
    public static IReadOnlyList<FieldRefusal> Validate(ValueDomainDefinition domain, string jsonPointer)
    {
        ArgumentNullException.ThrowIfNull(domain);

        return ValidateJson(JsonSerializer.Serialize(domain, DeclarationJson), jsonPointer);
    }

    /// <summary>Admits the domain object without accepting inline enums or patterns as membership.</summary>
    public static IReadOnlyList<FieldRefusal> ValidateJson(string json, string jsonPointer)
    {
        if (json is null) return [ShapeRefusal(jsonPointer)];
        try
        {
            using var document = JsonDocument.Parse(json);
            return ValidateObject(document.RootElement, jsonPointer);
        }
        catch (JsonException)
        {
            return [ShapeRefusal(jsonPointer)];
        }
    }

    private static IReadOnlyList<FieldRefusal> ValidateObject(JsonElement domain, string pointer)
    {
        if (domain.ValueKind != JsonValueKind.Object) return [ShapeRefusal(pointer)];
        var refusals = new List<FieldRefusal>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sources = 0;
        foreach (var property in domain.EnumerateObject())
        {
            var child = Child(pointer, property.Name);
            if (!seen.Add(property.Name))
            {
                refusals.Add(new("field.value_domain_member_duplicate", child,
                    "A value-domain member may only be declared once."));
                continue;
            }
            switch (property.Name)
            {
                case "enum":
                case "regex":
                    refusals.Add(new("field.inline_membership_forbidden", child,
                        "Permitted values must use one declared value-domain source."));
                    break;
                case "pattern":
                    refusals.Add(new("field.pattern_membership_forbidden", child,
                        "Pattern constrains shape and cannot prove value-domain membership."));
                    break;
                case "literal_values":
                    if (property.Value.ValueKind == JsonValueKind.Null) break;
                    sources++;
                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        refusals.Add(ShapeRefusal(child));
                        break;
                    }
                    var index = 0;
                    foreach (var value in property.Value.EnumerateArray())
                    {
                        if (value.ValueKind != JsonValueKind.String)
                            refusals.Add(ShapeRefusal(Child(child, index.ToString(CultureInfo.InvariantCulture))));
                        index++;
                    }
                    break;
                case "taxonomy_scheme":
                    if (property.Value.ValueKind == JsonValueKind.Null) break;
                    sources++;
                    ValidateReference(property.Value, child, ["scheme_id", "version"], refusals);
                    break;
                case "record_query":
                    if (property.Value.ValueKind == JsonValueKind.Null) break;
                    sources++;
                    ValidateReference(property.Value, child, ["record_type_id", "predicate"], refusals);
                    break;
                default:
                    refusals.Add(new("field.value_domain_member_unknown", child,
                        "The member is not part of a value-domain declaration."));
                    break;
            }
        }

        // The promoted Records source-count rule: optional null sources do not
        // count, while an empty declaration is not the absence of a declaration.
        if (sources != 1)
            refusals.Add(new("field.value_domain_source_count", pointer,
                "A value domain must name exactly one permitted-value source."));
        return refusals.AsReadOnly();
    }

    private static void ValidateReference(JsonElement reference, string pointer,
        IReadOnlyList<string> members, List<FieldRefusal> refusals)
    {
        if (reference.ValueKind != JsonValueKind.Object)
        {
            refusals.Add(ShapeRefusal(pointer));
            return;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in reference.EnumerateObject())
        {
            var child = Child(pointer, property.Name);
            if (!seen.Add(property.Name))
            {
                refusals.Add(new("field.value_domain_member_duplicate", child,
                    "A value-domain member may only be declared once."));
                continue;
            }
            if (!members.Contains(property.Name, StringComparer.Ordinal))
                refusals.Add(new("field.value_domain_member_unknown", child,
                    "The member is not part of the declared value source."));
            else if (property.Value.ValueKind != JsonValueKind.String)
                refusals.Add(ShapeRefusal(child));
            else if (string.IsNullOrWhiteSpace(property.Value.GetString()))
                refusals.Add(new("field.value_domain_reference_required", child,
                    "The value source requires a non-empty reference or predicate."));
        }
        foreach (var member in members)
            if (!seen.Contains(member))
                refusals.Add(new("field.value_domain_reference_required", Child(pointer, member),
                    "The value source requires this reference or predicate."));
    }

    private static string Child(string pointer, string member)
        => pointer + "/" + member.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    private static FieldRefusal ShapeRefusal(string pointer)
        => new("field.value_domain_shape_invalid", pointer, "The value domain does not match its declared shape.");
}
