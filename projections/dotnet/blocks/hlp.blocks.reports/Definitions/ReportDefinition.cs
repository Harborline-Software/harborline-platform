using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.Reports.Definitions;

/// <summary>Reports' additive pack wire identity; the catalogue namespace is the host's concern.</summary>
public static class ReportPackIdentity
{
    /// <summary>
    /// Content kind 9, <c>ReportDefinition</c>: the api's <c>PackContentKind.ReportDefinition</c>. DES-0020 line 79 says
    /// 8; numbering waits on an owner ruling whose recommendation is that the api values stand.
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

/// <summary>
/// The report definition shape (DES-0020 ruling 5 lets it land first). Layout, parameters and run properties are
/// deliberately absent; they wait on owner rulings.
/// </summary>
/// <param name="Tenant">The owning tenant.</param>
/// <param name="Key">The definition key.</param>
/// <param name="Version">The definition version.</param>
/// <param name="Measures">The catalogue measures the report reads.</param>
/// <param name="Envelope">The portable envelope; required past authoring.</param>
public sealed record ReportDefinition(
    string Tenant,
    string Key,
    string Version,
    IReadOnlyList<ReportMeasureReference> Measures,
    ReportDefinitionEnvelope? Envelope = null);

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
