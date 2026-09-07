namespace Harborline.Blocks.RelativeChains;

/// <summary>Identifies the kind of immutable basis selected by a relative-chain node.</summary>
public enum RelativeChainAnchorKind
{
    /// <summary>An external immutable anchor snapshot.</summary>
    External,
    /// <summary>The materialized occurrence of another node in the same definition.</summary>
    Predecessor,
}

/// <summary>Identifies an external anchor or predecessor node.</summary>
/// <param name="Kind">The kind of basis reference.</param>
/// <param name="Reference">The external anchor ref or predecessor node id.</param>
public sealed record RelativeChainAnchor(RelativeChainAnchorKind Kind, string Reference);

/// <summary>An inclusive tolerance around a nominal due date.</summary>
/// <param name="EarlyDays">Whole days before nominal due.</param>
/// <param name="LateDays">Whole days after nominal due.</param>
public sealed record RelativeChainTolerance(int EarlyDays, int LateDays);

/// <summary>One immutable node declaration in a chain definition revision.</summary>
/// <param name="NodeId">Definition-local ordinal routing id.</param>
/// <param name="Anchor">The immutable basis selector.</param>
/// <param name="OffsetDays">Non-negative whole-day offset.</param>
/// <param name="Tolerance">Inclusive early/late tolerance.</param>
/// <param name="ProviderRef">The node provider binding.</param>
/// <param name="ResourceRef">The node resource binding.</param>
public sealed record RelativeChainNode(string NodeId, RelativeChainAnchor Anchor, int OffsetDays, RelativeChainTolerance Tolerance, string ProviderRef, string ResourceRef);

/// <summary>An immutable relative-chain definition revision.</summary>
/// <param name="ChainDefinitionId">Stable definition id.</param>
/// <param name="DefinitionRevision">Positive immutable revision.</param>
/// <param name="ProviderRef">The single provider binding.</param>
/// <param name="ResourceRef">The single resource binding.</param>
/// <param name="Nodes">The node declarations.</param>
public sealed record RelativeChainDefinition(string ChainDefinitionId, int DefinitionRevision, string ProviderRef, string ResourceRef, IReadOnlyList<RelativeChainNode> Nodes);

/// <summary>The state of an immutable external anchor snapshot.</summary>
public enum AnchorSnapshotState
{
    /// <summary>The anchor exists and supplies a date.</summary>
    Present,
    /// <summary>The anchor was explicitly deleted.</summary>
    Deleted,
}

/// <summary>An immutable external anchor revision supplied to expansion.</summary>
/// <param name="AnchorRef">Opaque anchor ref.</param>
/// <param name="AnchorRevision">Positive immutable revision.</param>
/// <param name="AnchorDate">The anchor date when present.</param>
/// <param name="State">Present or deleted.</param>
/// <param name="ExplanationRefs">Non-empty diagnostic references.</param>
public sealed record AnchorSnapshot(string AnchorRef, int AnchorRevision, DateOnly? AnchorDate, AnchorSnapshotState State, IReadOnlyList<string> ExplanationRefs);
