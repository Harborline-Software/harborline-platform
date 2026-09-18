using System.Text.Json;

namespace Harborline.Contracts.Fields;

/// <summary>Binds a declared kind to its exact admitted revision and executable limits.</summary>
public interface IFieldKindRuntime
{
    /// <summary>Refuses unresolved kinds and invalid parameters before producing a compiled binding.</summary>
    ICompiledFieldKind Bind(FieldKindReference reference, string jsonPointer);
}

/// <summary>A compiled field kind: schema with executable binding metadata and its runtime validator.</summary>
public interface ICompiledFieldKind
{
    /// <summary>The exact admitted kind revision supplying the scalar shape and defaults.</summary>
    AdmittedFieldKind Kind { get; }

    /// <summary>
    /// Standard JSON Schema keywords plus x-harborline-field-kind metadata retaining the exact
    /// kind_id, version and parameters. Schema consumers must bind and execute that keyword
    /// through IFieldKindRuntime; standard keywords alone do not enforce every kind limit.
    /// </summary>
    JsonElement JsonSchema { get; }

    /// <summary>Validates scalar shape and every admitted limit without changing the value.</summary>
    IReadOnlyList<FieldRefusal> Validate(JsonElement value, string jsonPointer);

    /// <summary>Validates original JSON while preserving decimal trailing zeroes for digit checks.</summary>
    IReadOnlyList<FieldRefusal> ValidateJson(string json, string jsonPointer);
}
