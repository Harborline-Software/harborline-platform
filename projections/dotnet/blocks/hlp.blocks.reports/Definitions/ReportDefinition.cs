using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.Reports.Definitions;

/// <summary>Reports' additive pack wire identity; the catalogue namespace is the host's concern.</summary>
public static class ReportPackIdentity
{
    /// <summary>
    /// Content kind 9, <c>ReportDefinition</c>: the api's <c>PackContentKind.ReportDefinition</c>, as
    /// <see cref="PackContentKindRegistry"/> records it (T-738). DES-0020 line 79's 8 is superseded.
    /// </summary>
    public const int ContentKind = 9;
}

/// <summary>Stable refusal codes for report definition admission.</summary>
public static class ReportDefinitionCodes
{
    /// <summary>A measure reference is not a legal catalogue path.</summary>
    public const string MeasureMalformed = "report.measure_malformed";
    /// <summary>A measure reference names nothing the catalogue carries.</summary>
    public const string MeasureUnknown = "report.measure_unknown";
    /// <summary>A definition past authoring carries no envelope.</summary>
    public const string EnvelopeRequired = "definition.envelope_required";
    /// <summary>The envelope's coordinates disagree with the body's.</summary>
    public const string EnvelopeMismatch = "definition.envelope_mismatch";
    /// <summary>A parameter declares a type outside Reports' closed type list.</summary>
    public const string ParameterTypeUnsupported = "report.parameter_type_unsupported";
    /// <summary>A parameter's default is invalid for its declared type.</summary>
    public const string ParameterDefaultInvalid = "report.parameter_default_invalid";
    /// <summary>A parameter's declared type disagrees with its typed default.</summary>
    public const string ParameterDefaultTypeMismatch = "report.parameter_default_type_mismatch";
    /// <summary>The <c>as_at</c> parameter is not declared as DateTime.</summary>
    public const string AsAtMustBeDateTime = "report.as_at_must_be_datetime";
    /// <summary>The <c>as_at</c> parameter names no effective-dated type.</summary>
    public const string AsAtEffectiveTypeRequired = "report.as_at_effective_type_required";
    /// <summary>An effective-dated type was attached to a parameter other than <c>as_at</c>.</summary>
    public const string AsAtNameRequired = "report.as_at_name_required";
}

/// <summary>The portable envelope. <see cref="Contract"/> is required with no default (T-572).</summary>
/// <param name="Identity">The definition key.</param>
/// <param name="Version">The definition version.</param>
/// <param name="Tenant">The owning tenant.</param>
/// <param name="Contract">The declared application-contract version.</param>
public sealed record ReportDefinitionEnvelope(string Identity, string Version, string Tenant, DefinitionContractVersion? Contract);

/// <summary>
/// One measure the report reads, named by its stable catalogue path such as <c>finance.trial-balance</c>. The path
/// says nothing about whether the entry behind it is declared or written in code (DES-0031 measure-catalogue-ck-3).
/// </summary>
/// <param name="Measure">The catalogue path text, kept verbatim so a malformed path is refused by name.</param>
public sealed record ReportMeasureReference(string Measure);

/// <summary>The five parameter types the Reports grammar permits.</summary>
public enum ReportParameterType
{
    /// <summary>A true-or-false value.</summary>
    Boolean,
    /// <summary>An instant with an offset.</summary>
    DateTime,
    /// <summary>A signed whole number.</summary>
    Integer,
    /// <summary>A finite floating-point number.</summary>
    Float,
    /// <summary>A text value.</summary>
    String,
}

/// <summary>
/// A typed parameter default. Its internal constructor closes the grammar: callers can use only the five public
/// concrete forms below, never an arbitrary object payload.
/// </summary>
public abstract record ReportParameterDefault
{
    internal ReportParameterDefault()
    {
    }

    /// <summary>Gets the Reports parameter type that owns this value.</summary>
    public abstract ReportParameterType Type { get; }

    /// <summary>Gets whether this value is representable by its declared Reports type.</summary>
    public virtual bool IsValid => true;
}

/// <summary>A Boolean parameter default.</summary>
/// <param name="Value">The default true-or-false value.</param>
public sealed record BooleanReportParameterDefault(bool Value) : ReportParameterDefault
{
    /// <inheritdoc />
    public override ReportParameterType Type => ReportParameterType.Boolean;
}

/// <summary>A DateTime parameter default.</summary>
/// <param name="Value">The default instant.</param>
public sealed record DateTimeReportParameterDefault(DateTimeOffset Value) : ReportParameterDefault
{
    /// <inheritdoc />
    public override ReportParameterType Type => ReportParameterType.DateTime;
}

/// <summary>An Integer parameter default.</summary>
/// <param name="Value">The default signed whole number.</param>
public sealed record IntegerReportParameterDefault(long Value) : ReportParameterDefault
{
    /// <inheritdoc />
    public override ReportParameterType Type => ReportParameterType.Integer;
}

/// <summary>A Float parameter default.</summary>
/// <param name="Value">The default finite floating-point number.</param>
public sealed record FloatReportParameterDefault(double Value) : ReportParameterDefault
{
    /// <inheritdoc />
    public override ReportParameterType Type => ReportParameterType.Float;

    /// <inheritdoc />
    public override bool IsValid => !double.IsNaN(Value) && !double.IsInfinity(Value);
}

/// <summary>A String parameter default.</summary>
/// <param name="Value">The default text.</param>
public sealed record StringReportParameterDefault(string Value) : ReportParameterDefault
{
    /// <inheritdoc />
    public override ReportParameterType Type => ReportParameterType.String;
}

/// <summary>
/// A report parameter declaration. Only <c>as_at</c> may name an effective-dated type, and it must use DateTime;
/// other parameter names have no ambient type target.
/// </summary>
/// <param name="Name">The parameter name exposed to the report author and reader.</param>
/// <param name="Type">One of the five closed Reports parameter types.</param>
/// <param name="Default">The default value in that same declared type.</param>
/// <param name="EffectiveDatedType">The named effective-dated type for <c>as_at</c>, when applicable.</param>
public sealed record ReportParameterDefinition(
    string Name,
    ReportParameterType Type,
    ReportParameterDefault Default,
    string? EffectiveDatedType = null);

/// <summary>
/// The report definition shape. Its parameters use the ratified DES-0020 closed list; layout and run properties
/// remain outside this definition slice.
/// </summary>
/// <param name="Tenant">The owning tenant.</param>
/// <param name="Key">The definition key.</param>
/// <param name="Version">The definition version.</param>
/// <param name="Measures">The catalogue measures the report reads.</param>
/// <param name="Envelope">The portable envelope; required past authoring.</param>
/// <param name="Parameters">The report's closed typed parameter declarations.</param>
public sealed record ReportDefinition(
    string Tenant,
    string Key,
    string Version,
    IReadOnlyList<ReportMeasureReference> Measures,
    ReportDefinitionEnvelope? Envelope = null,
    IReadOnlyList<ReportParameterDefinition>? Parameters = null);

/// <summary>A located refusal. <see cref="Measure"/> and <see cref="Catalogue"/> name what was refused and by whom.</summary>
/// <param name="Code">Stable problem code.</param>
/// <param name="Pointer">RFC 6901 pointer into the definition.</param>
/// <param name="Measure">The refused measure path, when the refusal concerns one.</param>
/// <param name="Catalogue">The catalogue that was asked, when the refusal concerns a measure.</param>
public sealed record ReportRefusal(string Code, string Pointer, string? Measure = null, string? Catalogue = null);

/// <summary>A pure admission verdict: the stage it ran at and every refusal, empty when admitted.</summary>
/// <param name="Stage">The admission stage.</param>
/// <param name="Refusals">Every refusal, in definition order.</param>
public sealed record ReportAdmissionReport(DefinitionAdmissionPhase Stage, IReadOnlyList<ReportRefusal> Refusals);

/// <summary>A refused report definition.</summary>
/// <param name="report">The refusing verdict.</param>
public sealed class ReportAdmissionException(ReportAdmissionReport report) : Exception("The report definition was refused.")
{
    /// <summary>The refusing verdict.</summary>
    public ReportAdmissionReport Report { get; } = report;
}
