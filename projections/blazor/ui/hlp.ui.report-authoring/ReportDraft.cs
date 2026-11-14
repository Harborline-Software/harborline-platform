namespace Harborline.UIAdapters.Blazor.Components.ReportAuthoring;

/// <summary>A named catalogue measure.</summary>
/// <param name="Measure">The catalogue path.</param>
public sealed record ReportMeasure(string Measure);
/// <summary>A typed default in the Reports wire grammar.</summary>
/// <param name="Kind">The discriminator.</param>
/// <param name="Value">The typed value.</param>
/// <param name="Type">The declared type.</param>
/// <param name="IsValid">Whether the value is valid.</param>
public sealed record ReportParameterDefault(string Kind, object Value, string Type, bool IsValid);
/// <summary>A report parameter.</summary>
/// <param name="Name">The parameter name.</param>
/// <param name="Type">The declared type.</param>
/// <param name="Default">The typed default.</param>
/// <param name="EffectiveDatedType">The effective-dated type for as-at.</param>
public sealed record ReportParameter(string Name, string Type, ReportParameterDefault Default, string? EffectiveDatedType = null);
/// <summary>A host-owned report draft.</summary>
/// <param name="Tenant">The tenant.</param>
/// <param name="Key">The definition key.</param>
/// <param name="Version">The definition version.</param>
/// <param name="Measures">The named measures.</param>
/// <param name="Parameters">The typed parameters.</param>
public sealed record ReportDraft(string Tenant, string Key, string Version, IReadOnlyList<ReportMeasure> Measures, IReadOnlyList<ReportParameter> Parameters)
{
    /// <summary>A new empty draft.</summary>
    public static ReportDraft Empty => new("", "", "", [], []);
}
/// <summary>A host-supplied measure path and label.</summary>
/// <param name="Id">The stable path.</param>
/// <param name="Label">The display label.</param>
public sealed record MeasureOption(string Id, string Label)
{
    /// <summary>Uses an unlabelled path as its own display label.</summary>
    public static implicit operator MeasureOption(string value) => new(value, value);
}
/// <summary>An admission refusal located in a report draft.</summary>
/// <param name="Code">The stable refusal code.</param>
/// <param name="Pointer">The field pointer.</param>
/// <param name="Measure">The refused measure, when present.</param>
/// <param name="Catalogue">The catalogue name, when present.</param>
public sealed record ReportRefusal(string Code, string Pointer, string? Measure = null, string? Catalogue = null);
