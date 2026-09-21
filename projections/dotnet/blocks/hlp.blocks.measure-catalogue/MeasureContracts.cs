using System.Text.Json;
using Harborline.Blocks.Aggregates;
using Harborline.Foundation.Authorization;

namespace Harborline.Blocks.MeasureCatalogue;

/// <summary>Stable refusal codes emitted before any row is enumerated.</summary>
public static class MeasureCodes
{
    /// <summary>The reference text is not a legal measure address.</summary>
    public const string ReferenceMalformed = "measure.reference_malformed";
    /// <summary>No entry is registered under the reference.</summary>
    public const string UnknownReference = "measure.unknown_reference";
    /// <summary>A declared parameter was not supplied.</summary>
    public const string ParameterMissing = "measure.parameter_missing";
    /// <summary>A supplied parameter is not declared by the entry.</summary>
    public const string ParameterUnknown = "measure.parameter_unknown";
    /// <summary>A supplied parameter is not in its declared form.</summary>
    public const string ParameterInvalid = "measure.parameter_invalid";
    /// <summary>The supplied filter does not narrow the authored filter.</summary>
    public const string FilterWidened = "measure.filter_widened";
    /// <summary>The entry cannot read the basis the caller supplied.</summary>
    public const string BasisUnsupported = "measure.basis_unsupported";
    /// <summary>Two entries claimed one reference.</summary>
    public const string ReferenceDuplicated = "measure.reference_duplicated";
}

/// <summary>A fail-closed catalogue refusal carrying a stable code and a pointer.</summary>
public sealed class MeasureException(string code, string pointer, string message) : Exception(message)
{
    /// <summary>Stable problem code.</summary>
    public string Code { get; } = code;
    /// <summary>Pointer at the refused element.</summary>
    public string Pointer { get; } = pointer;
}

/// <summary>
/// The one public address for a measure. Nothing in the text says whether the entry behind it is
/// declared or written in code, which is what lets one kind replace the other without breaking a
/// reference (DES-0031 measure-catalogue-ck-3).
/// </summary>
public sealed record MeasureRef
{
    /// <summary>Parses and validates a dotted lowercase address such as <c>finance.trial-balance</c>.</summary>
    /// <param name="value">The address text.</param>
    public MeasureRef(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsWellFormed(value))
        {
            throw new MeasureException(MeasureCodes.ReferenceMalformed, $"/measures/{value}",
                "A measure address is two or more lowercase dot-separated segments.");
        }
        Value = value;
    }

    /// <summary>The address text.</summary>
    public string Value { get; }

    /// <summary>Parses without refusing, for a caller that treats a malformed address as unknown.</summary>
    /// <param name="value">The address text.</param><param name="reference">The parsed address.</param>
    /// <returns><see langword="true"/> when the text is a legal address.</returns>
    public static bool TryParse(string? value, out MeasureRef? reference)
    {
        reference = value is not null && IsWellFormed(value) ? new MeasureRef(value) : null;
        return reference is not null;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    private static bool IsWellFormed(string value)
    {
        var segments = value.Split('.');
        if (segments.Length < 2) return false;
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment[0] is < 'a' or > 'z') return false;
            foreach (var character in segment)
            {
                if (character is (< 'a' or > 'z') and (< '0' or > '9') and not '-') return false;
            }
            if (segment[^1] == '-') return false;
        }
        return true;
    }
}

/// <summary>
/// What a consumer may learn about an entry before binding it. It deliberately carries no entry
/// kind, so a consumer fixture cannot dispatch on one.
/// </summary>
/// <param name="Reference">The entry's address.</param>
/// <param name="ParameterNames">Exactly the parameters the entry requires.</param>
public sealed record MeasureDescriptor(MeasureRef Reference, IReadOnlyList<string> ParameterNames);

/// <summary>One flat caller-supplied row.</summary>
/// <param name="Id">The record identity the authorization filter decides on.</param>
/// <param name="Fields">Typed source values by field.</param>
public sealed record MeasureRow(string Id, IReadOnlyDictionary<string, AggregateValue> Fields);

/// <summary>
/// A caller-held row basis, such as a pinned report snapshot. The catalogue never interprets one;
/// it passes it to the entry, which refuses a basis it cannot read.
/// </summary>
public interface IMeasureBasis
{
    /// <summary>The opaque basis token echoed on the result.</summary>
    string Token { get; }
}

/// <summary>The rows the caller offers for one evaluation. The catalogue fetches none of its own.</summary>
public abstract record MeasureRows;

/// <summary>Rows the caller materialized: a live page, a draft record set, or a replay.</summary>
/// <param name="Rows">The caller's rows, before the authorization filter narrows them.</param>
public sealed record SuppliedRows(IReadOnlyList<MeasureRow> Rows) : MeasureRows;

/// <summary>A caller-held basis the entry reads through, under the catalogue's bound filter.</summary>
/// <param name="Basis">The caller's basis.</param>
public sealed record BasisRows(IMeasureBasis Basis) : MeasureRows;

/// <summary>The caller's row window, applied to the authorization-narrowed set.</summary>
/// <param name="Skip">Rows to skip.</param><param name="Take">Rows to take.</param>
public sealed record MeasurePage(int Skip, int Take);

/// <summary>One evaluation request. The caller supplies the rows and the clock; the catalogue owns neither.</summary>
/// <param name="Tenant">Trusted tenant.</param>
/// <param name="Principal">Trusted principal.</param>
/// <param name="Rows">The caller's rows or basis.</param>
/// <param name="At">The caller's instant: now for a live read, a period boundary for a pinned basis.</param>
/// <param name="Parameters">Exactly the entry's declared parameters.</param>
/// <param name="Narrow">An optional narrowing of the entry's authored filter. It may never widen it.</param>
/// <param name="Page">An optional row window applied after the authorization filter.</param>
public sealed record MeasureRequest(
    string Tenant,
    string Principal,
    MeasureRows Rows,
    DateTimeOffset At,
    IReadOnlyDictionary<string, string>? Parameters = null,
    AggregateFilter? Narrow = null,
    MeasurePage? Page = null);

/// <summary>
/// What the catalogue hands an entry. The rows are already narrowed by the production Access
/// filter; <see cref="Filter"/> is the same bound predicate, for an entry that pulls its own rows
/// from a basis.
/// </summary>
/// <param name="Reference">The resolved address.</param>
/// <param name="Tenant">Trusted tenant.</param>
/// <param name="Principal">Trusted principal.</param>
/// <param name="At">The caller's instant.</param>
/// <param name="Rows">Authorization-narrowed, paged caller rows; empty for a basis binding.</param>
/// <param name="Basis">The caller's basis, or null for supplied rows.</param>
/// <param name="BasisToken">The basis token echoed on the result.</param>
/// <param name="Filter">The bound Access predicate, for basis-pulled rows.</param>
/// <param name="RecordKind">The record kind <paramref name="Filter"/> is bound to.</param>
/// <param name="Parameters">The validated parameters.</param>
/// <param name="Filtering">The authored filter composed with the caller's narrowing.</param>
public sealed record MeasureEvaluation(
    MeasureRef Reference,
    string Tenant,
    string Principal,
    DateTimeOffset At,
    IReadOnlyList<MeasureRow> Rows,
    IMeasureBasis? Basis,
    string BasisToken,
    AccessSetPredicate Filter,
    string RecordKind,
    IReadOnlyDictionary<string, string> Parameters,
    AggregateFilter? Filtering);

/// <summary>
/// One typed result. Cells carry value, null and unavailable as distinct states, so an absent
/// figure is never rendered as a zero, through either kind of entry.
/// </summary>
/// <param name="Reference">The address that produced it.</param>
/// <param name="Basis">The basis token the rows came from.</param>
/// <param name="Groups">Deterministically ordered detail, subtotal and grand-total groups.</param>
public sealed record MeasureResult(MeasureRef Reference, string Basis, IReadOnlyList<AggregateGroup> Groups);

/// <summary>
/// One catalogue entry. A declared entry evaluates an authored definition; a bound entry runs code
/// at bound depth. The catalogue treats them identically and consumers cannot tell them apart.
/// </summary>
public interface IMeasureEntry
{
    /// <summary>The address this entry answers to.</summary>
    MeasureRef Reference { get; }
    /// <summary>The Access operation the filter binds for this entry's rows.</summary>
    string Operation { get; }
    /// <summary>The Access record kind the filter binds for this entry's rows.</summary>
    string RecordKind { get; }
    /// <summary>The entry's own authored filter, which a caller may narrow and never widen.</summary>
    AggregateFilter? AuthoredFilter { get; }
    /// <summary>Exactly the parameters this entry requires.</summary>
    IReadOnlyList<string> ParameterNames { get; }
    /// <summary>Evaluates over the rows and instant the caller supplied.</summary>
    /// <param name="evaluation">The narrowed evaluation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed groups and cells.</returns>
    ValueTask<MeasureResult> EvaluateAsync(MeasureEvaluation evaluation, CancellationToken cancellationToken = default);
}

/// <summary>The one resolution and evaluation path for every measure, of either kind.</summary>
public interface IMeasureCatalogue
{
    /// <summary>Resolves an address without enumerating a row.</summary>
    /// <param name="reference">The address.</param><param name="cancellationToken">Cancellation token.</param>
    /// <returns>The descriptor, or null when nothing is registered.</returns>
    ValueTask<MeasureDescriptor?> ResolveAsync(MeasureRef reference, CancellationToken cancellationToken = default);

    /// <summary>Evaluates one measure over the caller's rows at the caller's instant.</summary>
    /// <param name="reference">The address.</param><param name="request">The binding.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Typed groups and cells.</returns>
    ValueTask<MeasureResult> EvaluateAsync(MeasureRef reference, MeasureRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The narrowing rule from DES-0031 §5: a consumer may narrow a measure's filter within the
/// declared bounds and may never widen it. A supplied filter is admitted only when the authored
/// filter is still one of its conjuncts.
/// </summary>
public static class MeasureNarrowing
{
    private static readonly JsonSerializerOptions Options = AggregateJson.CreateOptions();

    /// <summary>Composes the authored filter with a caller narrowing, refusing a widening.</summary>
    /// <param name="authored">The entry's authored filter.</param>
    /// <param name="narrow">The caller's filter, or null.</param>
    /// <param name="reference">The address, for the refusal pointer.</param>
    /// <returns>The filter to evaluate with.</returns>
    public static AggregateFilter? Compose(AggregateFilter? authored, AggregateFilter? narrow, MeasureRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (narrow is null) return authored;
        if (authored is null) return narrow;
        if (Narrows(authored, narrow)) return narrow;
        throw new MeasureException(MeasureCodes.FilterWidened, $"/measures/{reference.Value}/filter",
            "A consumer may narrow a measure's authored filter and may never widen it.");
    }

    private static bool Narrows(AggregateFilter authored, AggregateFilter candidate)
    {
        if (Same(authored, candidate)) return true;
        return candidate is AggregateAllFilter all && all.Filters.Any(child => Narrows(authored, child));
    }

    // Structural, not reference, equality: a comparison filter carries lists whose record equality
    // is by reference, so two filters spelled the same would otherwise compare unequal.
    private static bool Same(AggregateFilter left, AggregateFilter right) =>
        string.Equals(JsonSerializer.Serialize(left, Options), JsonSerializer.Serialize(right, Options), StringComparison.Ordinal);
}
