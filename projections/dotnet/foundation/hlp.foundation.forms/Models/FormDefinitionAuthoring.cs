using System.Collections.ObjectModel;

namespace Harborline.Foundation.Forms.Models;

/// <summary>
/// Immutable authoring metadata that cannot be reconstructed faithfully from a JSON Schema and
/// runtime overlay alone (for example, radio versus select presentation).
/// </summary>
public sealed record FormDefinitionAuthoring
{
    public FormDefinitionAuthoring(IReadOnlyDictionary<string, FormFieldAuthoringMetadata> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Fields = new ReadOnlyDictionary<string, FormFieldAuthoringMetadata>(
            fields.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Copy(),
                StringComparer.Ordinal));
    }

    /// <summary>Exact per-field authoring snapshots keyed by overlay field name.</summary>
    public IReadOnlyDictionary<string, FormFieldAuthoringMetadata> Fields { get; }
}

/// <summary>Exact field type, validation constraints, and option values authored by Harborline App.</summary>
public sealed record FormFieldAuthoringMetadata
{
    public FormFieldAuthoringMetadata(
        string type,
        bool required,
        IReadOnlyList<FormFieldValidation>? validations = null,
        IReadOnlyList<string>? options = null)
    {
        Type = type;
        Required = required;
        Validations = validations?.Select(item => item with { }).ToArray();
        Options = options?.ToArray();
    }

    public string Type { get; }
    public bool Required { get; }
    public IReadOnlyList<FormFieldValidation>? Validations { get; }
    public IReadOnlyList<string>? Options { get; }

    internal FormFieldAuthoringMetadata Copy() =>
        new(Type, Required, Validations, Options);
}

/// <summary>One stable validation code plus its optional authored parameter.</summary>
public sealed record FormFieldValidation(string Code, string? Param = null);
