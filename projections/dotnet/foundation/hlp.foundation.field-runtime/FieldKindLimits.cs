using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Fields;

namespace Harborline.Foundation.FieldRuntime;

/// <summary>Admitted kind-parameter limits, enforced without rewriting the value.</summary>
public sealed class FieldKindLimits
{
    private static readonly HashSet<string> ParameterNames = new(StringComparer.Ordinal)
    {
        "total_digits", "fraction_digits", "min_length", "max_length", "max_bytes", "minimum", "maximum",
    };
    private readonly FieldNumber? _minimumNumber;
    private readonly FieldNumber? _maximumNumber;

    private FieldKindLimits(int? totalDigits, int? fractionDigits, int? minLength,
        int? maxLength, int? maxBytes, string? minimum, string? maximum)
    {
        TotalDigits = totalDigits;
        FractionDigits = fractionDigits;
        MinLength = minLength;
        MaxLength = maxLength;
        MaxBytes = maxBytes;
        Minimum = minimum;
        Maximum = maximum;
        _minimumNumber = minimum is null ? null : new(minimum);
        _maximumNumber = maximum is null ? null : new(maximum);
    }

    /// <summary>The declared significant-digit cap, or no cap when absent.</summary>
    public int? TotalDigits { get; }

    /// <summary>The declared decimal-place cap, or no cap when absent.</summary>
    public int? FractionDigits { get; }

    /// <summary>The minimum Unicode scalar count of a text value, when declared.</summary>
    public int? MinLength { get; }

    /// <summary>The maximum Unicode scalar count of a text value, when declared.</summary>
    public int? MaxLength { get; }

    /// <summary>The UTF-8 byte cap: decoded content for text, original JSON for other scalar shapes.</summary>
    public int? MaxBytes { get; }

    /// <summary>The admitted inclusive numeric minimum in its original JSON spelling.</summary>
    public string? Minimum { get; }

    /// <summary>The admitted inclusive numeric maximum in its original JSON spelling.</summary>
    public string? Maximum { get; }

    internal bool HasNumericLimits => TotalDigits is not null || FractionDigits is not null
        || Minimum is not null || Maximum is not null;
    internal bool HasTextLimits => MinLength is not null || MaxLength is not null;
    internal bool HasLimits => HasNumericLimits || HasTextLimits || MaxBytes is not null;

    /// <summary>Admits explicit digit facets; malformed or contradictory facets never become defaults.</summary>
    /// <param name="parameters">Parameters from the declared field-kind reference.</param>
    /// <param name="jsonPointer">The authored RFC 6901 location of the parameters object.</param>
    public static FieldKindLimits FromKindParameters(
        IReadOnlyDictionary<string, string> parameters, string jsonPointer)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var refusals = new List<FieldRefusal>();
        foreach (var name in parameters.Keys.Where(name => !ParameterNames.Contains(name)))
        {
            refusals.Add(new("field.kind_parameter_unknown", Child(jsonPointer, name),
                "The kind parameter is not an admitted field limit."));
        }

        var totalDigits = ReadFacet("total_digits", 1);
        var fractionDigits = ReadFacet("fraction_digits", 0);
        var minLength = ReadFacet("min_length", 0);
        var maxLength = ReadFacet("max_length", 0);
        var maxBytes = ReadFacet("max_bytes", 0);
        var minimum = ReadNumber("minimum");
        var maximum = ReadNumber("maximum");
        if (totalDigits is { } total && fractionDigits is { } fraction && fraction > total)
        {
            refusals.Add(new("field.kind_parameter_bounds_conflict", jsonPointer + "/fraction_digits",
                "fraction_digits cannot exceed total_digits."));
        }
        if (minLength is { } lowerLength && maxLength is { } upperLength && lowerLength > upperLength)
            refusals.Add(new("field.kind_parameter_bounds_conflict", jsonPointer + "/max_length",
                "max_length cannot be below min_length."));
        if (minimum is not null && maximum is not null
            && new FieldNumber(minimum).CompareTo(new FieldNumber(maximum)) > 0)
            refusals.Add(new("field.kind_parameter_bounds_conflict", jsonPointer + "/maximum",
                "maximum cannot be below minimum."));
        if (refusals.Count != 0) throw new FieldAdmissionException(refusals);
        return new(totalDigits, fractionDigits, minLength, maxLength, maxBytes, minimum, maximum);

        int? ReadFacet(string name, int minimum)
        {
            if (!parameters.TryGetValue(name, out var text)) return null;
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                && value >= minimum) return value;

            refusals.Add(new("field.kind_parameter_invalid", jsonPointer + "/" + name,
                "The limit must be an integer in its declared range."));
            return null;
        }

        string? ReadNumber(string name)
        {
            if (!parameters.TryGetValue(name, out var text)) return null;
            try
            {
                if (text is not null)
                {
                    using var document = JsonDocument.Parse(text);
                    if (document.RootElement.ValueKind == JsonValueKind.Number
                        && string.Equals(document.RootElement.GetRawText(), text, StringComparison.Ordinal)) return text;
                }
            }
            catch (JsonException) { }
            refusals.Add(new("field.kind_parameter_invalid", jsonPointer + "/" + name,
                "The bound must be a JSON number without surrounding whitespace."));
            return null;
        }
    }

    /// <summary>Validates the original JSON number spelling, including decimal trailing zeroes.</summary>
    public IReadOnlyList<FieldRefusal> Validate(JsonElement value, string jsonPointer)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            return [new("field.value_malformed", jsonPointer, "The field value must be valid JSON.")];
        if (!HasLimits) return [];
        if (HasNumericLimits && value.ValueKind != JsonValueKind.Number)
            return [new("field.value_type_mismatch", jsonPointer, "The field requires a JSON number.")];
        if (HasTextLimits && value.ValueKind != JsonValueKind.String)
            return [new("field.value_type_mismatch", jsonPointer, "The field requires a JSON string.")];

        var refusals = new List<FieldRefusal>();
        if (HasNumericLimits)
        {
            var number = new FieldNumber(value.GetRawText());
            if (TotalDigits is { } maximumTotal && number.TotalDigits > maximumTotal)
                refusals.Add(new("field.total_digits_exceeded", jsonPointer,
                    "The number exceeds the declared total_digits limit."));
            if (FractionDigits is { } maximumFraction && number.FractionDigits > maximumFraction)
                refusals.Add(new("field.fraction_digits_exceeded", jsonPointer,
                    "The number exceeds the declared fraction_digits limit."));
            if (_minimumNumber is not null && number.CompareTo(_minimumNumber) < 0)
                refusals.Add(new("field.below_minimum", jsonPointer, "The number is below the declared minimum."));
            if (_maximumNumber is not null && number.CompareTo(_maximumNumber) > 0)
                refusals.Add(new("field.above_maximum", jsonPointer, "The number exceeds the declared maximum."));
        }
        string? text = null;
        if (value.ValueKind == JsonValueKind.String && (HasTextLimits || MaxBytes is not null))
        {
            try { text = value.GetString(); }
            catch (InvalidOperationException)
            {
                return [new("field.value_malformed", jsonPointer, "The field text must contain valid Unicode.")];
            }
        }
        if (HasTextLimits)
        {
            var length = text!.EnumerateRunes().Count();
            if (MinLength is { } lower && length < lower)
                refusals.Add(new("field.too_short", jsonPointer, "The text is shorter than min_length."));
            if (MaxLength is { } upper && length > upper)
                refusals.Add(new("field.too_long", jsonPointer, "The text is longer than max_length."));
        }
        if (MaxBytes is { } bytes && Encoding.UTF8.GetByteCount(text ?? value.GetRawText()) > bytes)
            refusals.Add(new("field.max_bytes_exceeded", jsonPointer, "The value exceeds max_bytes."));
        return refusals.AsReadOnly();
    }

    /// <summary>Refuses malformed JSON and validates a scalar without rounding or normalizing it.</summary>
    public IReadOnlyList<FieldRefusal> ValidateJson(string json, string jsonPointer)
    {
        if (json is null)
            return [new("field.value_malformed", jsonPointer, "The field value must be valid JSON.")];
        try
        {
            using var document = JsonDocument.Parse(json);
            return Validate(document.RootElement, jsonPointer);
        }
        catch (JsonException)
        {
            return [new("field.value_malformed", jsonPointer, "The field value must be valid JSON.")];
        }
    }

    /// <summary>Produces the schema facet together with the mandatory runtime validator.</summary>
    public CompiledFieldKindLimits Compile() => new(this);

    private static string Child(string pointer, string member)
        => pointer + "/" + member.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}

/// <summary>A schema projection whose runtime-only limits remain executable.</summary>
public sealed class CompiledFieldKindLimits
{
    private readonly FieldKindLimits _limits;

    internal CompiledFieldKindLimits(FieldKindLimits limits)
    {
        _limits = limits;
        var schema = new JsonObject();
        if (limits.FractionDigits is { } fraction)
            schema["multipleOf"] = JsonNode.Parse(fraction == 0 ? "1" : "1e-" + fraction.ToString(CultureInfo.InvariantCulture));
        if (limits.MinLength is { } minimumLength) schema["minLength"] = minimumLength;
        if (limits.MaxLength is { } maximumLength) schema["maxLength"] = maximumLength;
        if (limits.Minimum is { } minimum) schema["minimum"] = JsonNode.Parse(minimum);
        if (limits.Maximum is { } maximum) schema["maximum"] = JsonNode.Parse(maximum);
        using var document = JsonDocument.Parse(schema.ToJsonString());
        JsonSchemaKeywords = document.RootElement.Clone();
    }

    /// <summary>The standard schema projection, not a substitute for runtime validation.</summary>
    public JsonElement JsonSchemaKeywords { get; }

    /// <summary>Whether the compiled kind carries constraints that its consumer must validate.</summary>
    public bool RequiresRuntimeValidation => _limits.HasLimits;

    /// <summary>Enforces both facets, including constraints not expressible by standard schema keywords.</summary>
    public IReadOnlyList<FieldRefusal> Validate(JsonElement value, string jsonPointer)
        => _limits.Validate(value, jsonPointer);

    /// <summary>Validates original JSON without losing authored trailing decimal zeroes.</summary>
    public IReadOnlyList<FieldRefusal> ValidateJson(string json, string jsonPointer)
        => _limits.ValidateJson(json, jsonPointer);
}
