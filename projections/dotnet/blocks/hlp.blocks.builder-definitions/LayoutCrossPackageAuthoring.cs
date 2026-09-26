namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The AUTHORING-ONLY cross-package edge check for a Layout surface (DES-0052 <c>layout-auth-30</c>, DES-0014 K9), on
/// T-724 ruling 57's pattern. Each edge the host resolves from the surface is tested against the surface envelope's
/// <c>requires</c> (a requirement naming the producer's package declares the dependency, ADR 0006) and the producer's
/// declared exposure; an absent or incompatible declaration on either side is a named refusal at the edge's pointer.
/// Publication and installation gates are T-615's (ADR-0103 decision 5). A refusal names its target only when the
/// producer exposes it.
/// </summary>
public static class LayoutCrossPackageAuthoring
{
    /// <summary>Checks every edge; refusals come back in edge order, all at the author stage.</summary>
    /// <param name="consumer">The surface being authored.</param>
    /// <param name="references">The surface's edges, each at its RFC 6901 pointer.</param>
    /// <param name="exposures">The producer packages' declared exposures.</param>
    public static DefinitionRefusalReport Check(LayoutDefinition consumer, IReadOnlyList<CrossPackageReference> references,
        IReadOnlyList<PackageExposure> exposures)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        return CrossPackageEdges.Check((consumer.Envelope?.Requires ?? []).Where(requirement => requirement is not null).Select(requirement => requirement.Capability),
            references, exposures,
            LayoutDefinitionCodes.ReferenceDependencyUndeclared, LayoutDefinitionCodes.ReferenceNotExposed, LayoutDefinitionCodes.ReferenceExposureIncompatible);
    }
}
