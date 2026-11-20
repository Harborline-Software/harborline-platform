using Harborline.Contracts.Authorization;

namespace Harborline.Blocks.Scheduling;

/// <summary>Scheduling's capabilities declared to Access (ADR-0181).</summary>
public static class SchedulingPermissions
{
    /// <summary>Author a schedule, protocol or constraint.</summary>
    public const string Author = "scheduling:author";
    /// <summary>Run a plan.</summary>
    public const string Plan = "scheduling:plan";
    /// <summary>Commit a Proposal, layered on Records write.</summary>
    public const string Commit = "scheduling:commit";
    /// <summary>The schedule record addressed by the gate.</summary>
    public const string ScheduleRecordKind = "schedule";
    /// <summary>Version-one capability declarations; grants remain the host's.</summary>
    public static IReadOnlyList<AuthorizationCapabilityDefinition> Declarations { get; } =
        [new(new(Author), 1), new(new(Plan), 1), new(new(Commit), 1)];
}
