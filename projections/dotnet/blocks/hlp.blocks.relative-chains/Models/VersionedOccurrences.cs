namespace Harborline.Blocks.RelativeChains;

/// <summary>The immutable identity of one materialized occurrence revision.</summary>
/// <param name="ChainInstanceId">The instantiated chain.</param>
/// <param name="NodeId">The definition-local node.</param>
/// <param name="DueDate">The nominal derived date.</param>
/// <param name="OccurrenceRevision">The positive node-ledger revision.</param>
public sealed record RelativeChainOccurrenceId(string ChainInstanceId, string NodeId, DateOnly DueDate, int OccurrenceRevision)
{
    /// <summary>Returns the injective r2 opaque reference encoding.</summary>
    public override string ToString() => RelativeChainReferenceCodec.Encode(this);
}

/// <summary>An immutable materialized relative-chain occurrence.</summary>
/// <param name="OccurrenceId">The full versioned identity.</param>
/// <param name="DefinitionRevision">The definition revision that first materialized it.</param>
/// <param name="BasisRef">Immediate external or predecessor basis identity.</param>
/// <param name="BasisRevision">Immediate basis revision.</param>
/// <param name="DueDate">Nominal due date.</param>
/// <param name="EarliestDate">Inclusive earliest date.</param>
/// <param name="LatestDate">Inclusive latest date.</param>
/// <param name="SubjectRef">Opaque subject binding.</param>
/// <param name="ProviderRef">Opaque provider binding.</param>
/// <param name="ResourceRef">Opaque resource binding.</param>
/// <param name="DerivationFingerprint">Canonical effective-semantics fingerprint.</param>
/// <param name="ExplanationRefs">Deterministically ordered explanation refs.</param>
public sealed record RelativeChainOccurrence(RelativeChainOccurrenceId OccurrenceId, int DefinitionRevision, string BasisRef, int BasisRevision, DateOnly DueDate, DateOnly EarliestDate, DateOnly LatestDate, string SubjectRef, string ProviderRef, string ResourceRef, string DerivationFingerprint, IReadOnlyList<string> ExplanationRefs);

/// <summary>A closed reason for superseding an immutable occurrence.</summary>
public enum SupersessionReasonCode
{
    /// <summary>An external anchor revision changed without moving its date.</summary>
    AnchorRevised,
    /// <summary>An external anchor date moved.</summary>
    AnchorMoved,
    /// <summary>An external anchor was deleted.</summary>
    AnchorDeleted,
    /// <summary>The exact predecessor occurrence basis changed.</summary>
    PredecessorRevised,
    /// <summary>Effective definition semantics changed.</summary>
    DefinitionRevised,
    /// <summary>Subject, provider, or resource binding changed.</summary>
    BindingRevised,
}

/// <summary>An immutable old-to-new or terminal supersession edge.</summary>
/// <param name="SupersessionId">Deterministic retry-safe id.</param>
/// <param name="ChainInstanceId">Owning instantiated chain.</param>
/// <param name="NodeId">Owning node.</param>
/// <param name="SupersededOccurrenceId">The immutable old revision.</param>
/// <param name="SuccessorOccurrenceId">The greater successor revision, or null for a tombstone.</param>
/// <param name="ReasonCode">Closed transition reason.</param>
/// <param name="TriggerRef">Opaque immutable trigger ref.</param>
/// <param name="RecordedAt">Injected recording instant.</param>
/// <param name="ExplanationRefs">Non-empty explanation refs.</param>
public sealed record OccurrenceSupersession(string SupersessionId, string ChainInstanceId, string NodeId, RelativeChainOccurrenceId SupersededOccurrenceId, RelativeChainOccurrenceId? SuccessorOccurrenceId, SupersessionReasonCode ReasonCode, string TriggerRef, DateTimeOffset RecordedAt, IReadOnlyList<string> ExplanationRefs);
