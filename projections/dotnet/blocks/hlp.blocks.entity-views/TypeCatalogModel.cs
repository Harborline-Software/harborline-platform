namespace Harborline.Blocks.EntityViews;

/// <summary>An effective entity-type catalog row, mirroring the pinned TypeWire.</summary>
public sealed record TypeWire(
    string Id, string DisplayName, IReadOnlyList<string> Traits, string? ParentType,
    IReadOnlyList<string> Disciplines, string Provenance, bool OverridesSeed,
    bool HasPropertyForm, int InspectionFormCount, int? ConditionScaleMax,
    int? ExpectedUsefulLifeYears, MoneyWire? ReplacementCost);

/// <summary>The editable effective entity type, mirroring the pinned TypeDetailWire.</summary>
public sealed record TypeDetailWire(
    string Id, string DisplayName, IReadOnlyList<string> Traits, string? ParentType,
    IReadOnlyList<string> Disciplines, string Provenance, bool OverridesSeed,
    bool SeedExists, bool HasTenantRow, PropertyFormWire? PropertyForm,
    IReadOnlyList<InspectionBindingWire> InspectionForms, int? ConditionScaleMax,
    int? ExpectedUsefulLifeYears, MoneyWire? TypicalReplacementCost);

/// <summary>A pinned property-form binding.</summary>
public sealed record PropertyFormWire(string Definition, string Version);

/// <summary>A discipline-to-inspection-form binding.</summary>
public sealed record InspectionBindingWire(string Discipline, string Definition, string Version);

/// <summary>A currency-bound capital-planning amount.</summary>
public sealed record MoneyWire(decimal Amount, string Currency);

/// <summary>The wire-faithful authoring body for a type.</summary>
public sealed record TypeUpsertBody(
    string? Id, string? DisplayName, IReadOnlyList<string>? Traits, string? ParentType,
    IReadOnlyList<string>? Disciplines, TypeFormRefBody? PropertyForm,
    IReadOnlyList<InspectionBindingBody>? InspectionForms, int? ConditionScaleMax,
    int? ExpectedUsefulLifeYears, MoneyBody? TypicalReplacementCost);

/// <summary>A nullable pinned-form request body, field-for-field with the host request.</summary>
public sealed record TypeFormRefBody(string? Definition, string? Version);

/// <summary>A nullable inspection-binding request body, field-for-field with the host request.</summary>
public sealed record InspectionBindingBody(string? Discipline, string? Definition, string? Version);

/// <summary>A replacement-cost request body; absent currency defaults to USD.</summary>
public sealed record MoneyBody(decimal Amount, string? Currency);

/// <summary>A surfaced condition-projection skip. Submitted values are deliberately absent.</summary>
public sealed record ConditionCaptureSkip(string Reason, string Field);

/// <summary>The condition capture result. Successful captures leave both skip fields null.</summary>
public sealed record ConditionCaptureResult(
    ConditionAssessment? Assessment, string? Projection, IReadOnlyList<ConditionCaptureSkip>? Skips);
