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

/// <summary>The attachment kind's admitted field-owned limits; the declared limit is never rewritten.</summary>
public interface ICompiledAttachmentFieldKind : ICompiledFieldKind
{
    /// <summary>The field's authored per-item byte limit, absent when it uses the store's.</summary>
    long? DeclaredLimitBytes { get; }
    /// <summary>The lower of the store's and the field's per-item limits.</summary>
    long EffectiveLimitBytes { get; }
    /// <summary>The admitted media types this field accepts.</summary>
    IReadOnlyList<string> AcceptedMediaTypes { get; }
    /// <summary>Whether installation must warn that the declared field limit exceeds the store's.</summary>
    bool LimitAboveStore { get; }
    /// <summary>
    /// Validates one entry of a write list (ADR-0142 decision 1): a kept content reference, checked as
    /// <see cref="ICompiledFieldKind.Validate"/> checks it, or a staged entry of exactly a GUID <c>stage_id</c> and a
    /// non-blank <c>display_name</c>, whose size and media type the store already enforced at its put. Read grammar and
    /// stored bodies use <see cref="ICompiledFieldKind.Validate"/>, which admits references only.
    /// </summary>
    IReadOnlyList<FieldRefusal> ValidateSubmission(JsonElement value, string jsonPointer);
}
