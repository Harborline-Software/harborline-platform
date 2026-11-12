using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Blocks.Scheduling.Definitions;

/// <summary>
/// Authoring-only K9 validation of host-resolved edges (scheduling-auth-28, T-724 ruling 57).
/// The host supplies producer exposures; publication and installation gates belong to T-615.
/// </summary>
public static class SchedulingCrossPackageAuthoring
{
    /// <summary>Checks consumer requirements and producer exposure without storing or publishing anything.</summary>
    public static DefinitionRefusalReport Check(ScheduleDefinition consumer, IReadOnlyList<CrossPackageReference> references,
        IReadOnlyList<PackageExposure> exposures)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        return CrossPackageEdges.Check((consumer.Envelope?.Requires ?? []).Where(requirement => requirement is not null)
                .Select(requirement => requirement.Capability), references, exposures,
            ScheduleDefinitionCodes.ReferenceDependencyUndeclared, ScheduleDefinitionCodes.ReferenceNotExposed,
            ScheduleDefinitionCodes.ReferenceExposureIncompatible);
    }
}
