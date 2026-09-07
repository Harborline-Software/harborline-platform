namespace Harborline.Blocks.RelativeChains;

/// <summary>A closed typed expansion failure.</summary>
public enum RelativeChainFailureCode
{
    /// <summary>A predecessor node does not exist.</summary>
    UnknownPredecessor,
    /// <summary>The dependency graph contains a cycle.</summary>
    DependencyCycle,
    /// <summary>A node id is declared more than once.</summary>
    DuplicateNodeId,
    /// <summary>The definition crosses the single-provider/resource fence.</summary>
    UnsupportedResourceCardinality,
    /// <summary>A negative offset is outside r2.</summary>
    UnsupportedNegativeOffset,
    /// <summary>A bound or declaration is outside r2.</summary>
    UnsupportedDefinition,
    /// <summary>A required external anchor snapshot was not supplied.</summary>
    MissingAnchor,
    /// <summary>A supplied external anchor is deleted.</summary>
    DeletedAnchor,
    /// <summary>Checked DateOnly arithmetic overflowed.</summary>
    DateArithmeticOverflow,
    /// <summary>More than one unsuperseded current revision exists.</summary>
    AmbiguousCurrentOccurrence,
    /// <summary>The supplied occurrence/supersession ledger violates invariants.</summary>
    CorruptLedger,
}

/// <summary>A typed failure with deterministic, non-empty explanation refs.</summary>
/// <param name="Code">The closed failure code.</param>
/// <param name="ExplanationRefs">At least one diagnostic ref.</param>
public sealed record RelativeChainFailure(RelativeChainFailureCode Code, IReadOnlyList<string> ExplanationRefs);

/// <summary>The complete immutable ledger snapshot supplied to expansion.</summary>
/// <param name="LedgerVersion">Consumer-owned compare-and-append version.</param>
/// <param name="Occurrences">All historical occurrences.</param>
/// <param name="Supersessions">All historical edges.</param>
public sealed record RelativeChainLedger(long LedgerVersion, IReadOnlyList<RelativeChainOccurrence> Occurrences, IReadOnlyList<OccurrenceSupersession> Supersessions);

/// <summary>All normalized inputs to deterministic expansion.</summary>
/// <param name="Definition">The immutable definition revision.</param>
/// <param name="ChainInstanceId">The instantiated chain id.</param>
/// <param name="SubjectRef">Opaque subject binding.</param>
/// <param name="Anchors">Immutable external anchor snapshots.</param>
/// <param name="PriorLedger">The consistent prior ledger snapshot.</param>
/// <param name="RecordedAt">Injected timestamp for newly emitted edges.</param>
/// <param name="TimezoneId">Injected timezone identity; never resolved from ambient process state.</param>
public sealed record RelativeChainExpansionRequest(RelativeChainDefinition Definition, string ChainInstanceId, string SubjectRef, IReadOnlyList<AnchorSnapshot> Anchors, RelativeChainLedger PriorLedger, DateTimeOffset RecordedAt, string TimezoneId);

/// <summary>The rich deterministic result; failures are never represented as an empty success.</summary>
/// <param name="CurrentOccurrences">Unique unsuperseded current occurrences.</param>
/// <param name="AppendedOccurrences">New immutable occurrence rows.</param>
/// <param name="Supersessions">New immutable supersession rows.</param>
/// <param name="Failures">Typed failures.</param>
/// <param name="ExpectedPriorLedgerVersion">The compare-and-append precondition.</param>
/// <param name="ExplanationRefs">Deterministically ordered operation refs.</param>
public sealed record RelativeChainExpansionResult(IReadOnlyList<RelativeChainOccurrence> CurrentOccurrences, IReadOnlyList<RelativeChainOccurrence> AppendedOccurrences, IReadOnlyList<OccurrenceSupersession> Supersessions, IReadOnlyList<RelativeChainFailure> Failures, long ExpectedPriorLedgerVersion, IReadOnlyList<string> ExplanationRefs);
