using Harborline.Blocks.MeasureCatalogue;

namespace Harborline.Blocks.LayoutRuntime;

/// <summary>Stable refusal codes owned by the Layout binding boundary.</summary>
public static class LayoutBindingCodes
{
    /// <summary>The named measure could not be evaluated by the shared catalogue.</summary>
    public const string Unresolvable = "layout.binding.unresolvable";
    /// <summary>The block is outside the measure-observe adapter's bounded contract.</summary>
    public const string Unsupported = "layout.binding.unsupported";
}

/// <summary>One result from resolving a Layout measure binding.</summary>
/// <param name="BlockId">The definition-local block identity.</param>
/// <param name="BindingKind">The authored Layout binding kind.</param>
public abstract record LayoutMeasureBindingResolution(string BlockId, string BindingKind);

/// <summary>A typed catalogue result placed at one Layout block.</summary>
/// <param name="BlockId">The definition-local block identity.</param>
/// <param name="BindingKind">The authored Layout binding kind.</param>
/// <param name="Measure">The unchanged typed result returned by the shared catalogue.</param>
public sealed record LayoutMeasureBindingResult(
    string BlockId,
    string BindingKind,
    MeasureResult Measure) : LayoutMeasureBindingResolution(BlockId, BindingKind);

/// <summary>One stable Layout binding refusal with optional shared-catalogue cause data.</summary>
/// <param name="BlockId">The definition-local block identity.</param>
/// <param name="BindingKind">The authored Layout binding kind.</param>
/// <param name="Pointer">The RFC 6901 pointer to the authored binding.</param>
/// <param name="Code">The Layout boundary refusal code.</param>
/// <param name="CauseCode">The shared measure refusal code, when evaluation reached that boundary.</param>
/// <param name="CausePointer">The shared measure refusal pointer, when evaluation reached that boundary.</param>
public sealed record LayoutBindingRefusal(
    string BlockId,
    string BindingKind,
    string Pointer,
    string Code,
    string? CauseCode = null,
    string? CausePointer = null) : LayoutMeasureBindingResolution(BlockId, BindingKind);
