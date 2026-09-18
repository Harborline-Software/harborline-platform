using System.Text.Json;

namespace Harborline.Contracts.Fields;

/// <summary>Binds a declared kind to its exact admitted revision and executable limits.</summary>
public interface IFieldKindRuntime
{
    /// <summary>Refuses unresolved kinds and invalid parameters before producing a compiled binding.</summary>
    ICompiledFieldKind Bind(FieldKindReference reference, string jsonPointer);
}

/// <summary>A compiled field kind: standard schema and its retained runtime validator.</summary>
public interface ICompiledFieldKind
{
    /// <summary>The exact admitted kind revision supplying the scalar shape and defaults.</summary>
    AdmittedFieldKind Kind { get; }

    /// <summary>The standard JSON Schema projection; consumers must also execute this binding's validator.</summary>
    JsonElement JsonSchema { get; }

    /// <summary>Validates scalar shape and every admitted limit without changing the value.</summary>
    IReadOnlyList<FieldRefusal> Validate(JsonElement value, string jsonPointer);

    /// <summary>Validates original JSON while preserving decimal trailing zeroes for digit checks.</summary>
    IReadOnlyList<FieldRefusal> ValidateJson(string json, string jsonPointer);
}
