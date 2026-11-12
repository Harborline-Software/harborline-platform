using Harborline.Blocks.Scheduling.Definitions;

namespace Harborline.Blocks.Scheduling;

/// <summary>One immutable chain definition, carrying the authored shape with exactly one external anchor.</summary>
public sealed record RelativeChainDefinition(string ChainDefinitionId, int DefinitionRevision, string Anchor,
    IReadOnlyList<ScheduleChainNode> Nodes);

/// <summary>The state of the chain's versioned external anchor.</summary>
public enum AnchorSnapshotState
{
    /// <summary>An anchor supplies an instant.</summary>
    Present,
    /// <summary>The anchor was explicitly deleted.</summary>
    Deleted,
}

/// <summary>An immutable anchor snapshot; a deleted anchor explicitly supersedes the whole chain.</summary>
public sealed record AnchorSnapshot(string AnchorRef, int AnchorRevision, DateTimeOffset? AnchorDate,
    AnchorSnapshotState State, IReadOnlyList<string> ExplanationRefs);

/// <summary>Engine validation reuses the same structural checks as authoring.</summary>
public static class RelativeChainValidator
{
    /// <summary>The admitted maximum chain size.</summary>
    public const int MaximumNodeCount = 1024;
    /// <summary>The admitted maximum duration magnitude in days (calendar units retained for generation).</summary>
    public const int MaximumDays = 36500;

    /// <summary>Returns the authored topological order or explicit failures.</summary>
    public static IReadOnlyList<ScheduleChainNode> Validate(RelativeChainDefinition definition, out IReadOnlyList<RelativeChainFailure> failures)
    {
        var found = new List<Harborline.Blocks.BuilderDefinitions.DefinitionRefusal>();
        ScheduleDefinitionAdmission.Chain(new(definition.Anchor, definition.Nodes), found);
        if (definition.DefinitionRevision < 1 || string.IsNullOrWhiteSpace(definition.ChainDefinitionId))
            found.Add(new("scheduling.definition.basis_invalid", "/definition"));
        failures = found.Select(f => new RelativeChainFailure(RelativeChainFailureCode.UnsupportedDefinition, [f.Code, f.Pointer])).ToArray();
        return failures.Count == 0 ? definition.Nodes : [];
    }
}
