using System.Text.Json;

namespace Harborline.Foundation.Assistance;

/// <summary>A bounded sufficient proof of schema inclusion, not a general JSON Schema solver.</summary>
public static class AssistanceArgsSchemaNarrowing
{
    /// <summary>Proves that candidate accepts no more values than baseline; uncertainty refuses.</summary>
    public static bool IsAtLeastAsStrict(JsonElement candidate, JsonElement baseline)
    {
        if (candidate.ValueKind != JsonValueKind.Object || baseline.ValueKind != JsonValueKind.Object) return false;
        if (candidate.GetRawText().Length > 65536 || baseline.GetRawText().Length > 65536) return false;
        var shapeBudget = 4096;
        var hasReferences = false;
        if (!HasUniqueNames(candidate, 0, ref shapeBudget, ref hasReferences) || !HasUniqueNames(baseline, 0, ref shapeBudget, ref hasReferences)) return false;
        // A nested reference can observe a sibling change with negative polarity.
        if (hasReferences) return JsonElement.DeepEquals(candidate, baseline);
        var budget = 4096;
        return Compare(candidate, baseline, 0, ref budget);
    }

    private static bool HasUniqueNames(JsonElement value, int depth, ref int budget, ref bool hasReferences)
    {
        if (depth > 64 || --budget < 0) return false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name is "$ref" or "$dynamicRef") hasReferences = true;
                if (!names.Add(property.Name) || !HasUniqueNames(property.Value, depth + 1, ref budget, ref hasReferences)) return false;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                if (!HasUniqueNames(item, depth + 1, ref budget, ref hasReferences)) return false;
        return true;
    }

    private static bool Compare(JsonElement candidate, JsonElement baseline, int depth, ref int budget)
    {
        if (depth > 32 || --budget < 0) return false;
        if (candidate.ValueKind == JsonValueKind.False || baseline.ValueKind == JsonValueKind.True) return true;
        if (candidate.ValueKind != JsonValueKind.Object || baseline.ValueKind != JsonValueKind.Object) return false;
        // Opaque applicators/references can observe evaluated properties or change scope.
        // Unchanged keyword values alone cannot prove that sibling edits are safe.
        string[] opaque = ["$ref", "$dynamicRef", "$id", "$dynamicAnchor", "unevaluatedProperties", "unevaluatedItems", "allOf", "anyOf", "oneOf", "not", "if", "then", "else", "dependentSchemas"];
        if (opaque.Any(name => candidate.TryGetProperty(name, out _) || baseline.TryGetProperty(name, out _)))
            return JsonElement.DeepEquals(candidate, baseline);
        var current = candidate.EnumerateObject().ToArray();
        var original = baseline.EnumerateObject().ToArray();
        if (current.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != current.Length ||
            original.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != original.Length) return false;
        foreach (var name in current.Select(p => p.Name).Union(original.Select(p => p.Name), StringComparer.Ordinal))
        {
            if (--budget < 0) return false;
            var hasNew = candidate.TryGetProperty(name, out var next);
            var hasOld = baseline.TryGetProperty(name, out var old);
            switch (name)
            {
                case "required":
                    if (!hasNew || !StringSet(next, out var required)) return false;
                    if (hasOld && (!StringSet(old, out var prior) || !prior.IsSubsetOf(required))) return false;
                    break;
                case "enum":
                    if (!hasNew || next.ValueKind != JsonValueKind.Array || next.GetArrayLength() == 0) return false;
                    if (hasOld && (old.ValueKind != JsonValueKind.Array || next.EnumerateArray().Any(value => !old.EnumerateArray().Any(item => JsonElement.DeepEquals(value, item))))) return false;
                    break;
                case "minimum": case "minLength": case "minItems":
                case "maximum": case "maxLength": case "maxItems":
                    if (!hasNew || !TryGetExactDecimal(next, out var number)) return false;
                    if (hasOld)
                    {
                        if (!TryGetExactDecimal(old, out var floor)) return false;
                        if (name.StartsWith("min", StringComparison.Ordinal) ? number < floor : number > floor) return false;
                    }
                    break;
                case "properties":
                    if (!hasNew || next.ValueKind != JsonValueKind.Object || (hasOld && old.ValueKind != JsonValueKind.Object)) return false;
                    if (hasOld)
                        foreach (var property in old.EnumerateObject())
                            if (next.TryGetProperty(property.Name, out var child))
                            {
                                if (!Compare(child, property.Value, depth + 1, ref budget)) return false;
                            }
                            else if (!candidate.TryGetProperty("additionalProperties", out var closed) || closed.ValueKind != JsonValueKind.False || candidate.TryGetProperty("patternProperties", out _)) return false;
                    foreach (var property in next.EnumerateObject())
                    {
                        if (hasOld && old.TryGetProperty(property.Name, out _)) continue;
                        if (baseline.TryGetProperty("additionalProperties", out var extra))
                        {
                            if (extra.ValueKind == JsonValueKind.False) return false;
                            if (extra.ValueKind != JsonValueKind.True && !Compare(property.Value, extra, depth + 1, ref budget)) return false;
                        }
                    }
                    break;
                case "items":
                    if (!hasNew) return false;
                    if (hasOld && !Compare(next, old, depth + 1, ref budget)) return false;
                    break;
                case "additionalProperties":
                    if (!hasNew) return false;
                    if (!hasOld || old.ValueKind == JsonValueKind.True) break;
                    if (next.ValueKind == JsonValueKind.False) break;
                    if (!Compare(next, old, depth + 1, ref budget)) return false;
                    break;
                case "type": case "pattern":
                    if (!hasNew || (hasOld && !JsonElement.DeepEquals(next, old))) return false;
                    break;
                default:
                    // Unsupported keywords (including references and combinators) must be unchanged.
                    if (!hasNew || !hasOld || !JsonElement.DeepEquals(next, old)) return false;
                    break;
            }
        }
        return true;
    }

    private static bool TryGetExactDecimal(JsonElement value, out decimal number)
    {
        number = default;
        // DeepEquals compares the original JSON number without rounding.
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out number) &&
            JsonElement.DeepEquals(value, JsonSerializer.SerializeToElement(number));
    }

    private static bool StringSet(JsonElement value, out HashSet<string> result)
    {
        result = new(StringComparer.Ordinal);
        if (value.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !result.Add(item.GetString()!)) return false;
        }
        return true;
    }
}
