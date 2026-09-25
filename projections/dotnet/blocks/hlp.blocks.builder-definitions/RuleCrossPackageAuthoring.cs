using Harborline.Foundation.RuleAuthoring;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>One side of a cross-package edge, as <c>records-ck-41</c> pins it: package, definition, exact version and digest.</summary>
public sealed record CrossPackageEndpoint(string PackageId, string DefinitionId, string Version, string Digest);

/// <summary>A producer package's declared exposure: the exact definitions other packages may reference.</summary>
public sealed record PackageExposure(string PackageId, IReadOnlyList<CrossPackageEndpoint> Exposed);

/// <summary>One reference in the consumer definition, at its RFC 6901 pointer, from source to target.</summary>
public sealed record CrossPackageReference(string Pointer, CrossPackageEndpoint Source, CrossPackageEndpoint Target);

/// <summary>
/// The AUTHORING-ONLY cross-package reference check (DES-0018 <c>rules-auth-30</c>, DES-0014 K9). Each reference is
/// tested against the consumer envelope's <c>requires</c> clause (ADR 0006) and the producer's declared exposure; an
/// absent or incompatible declaration is a named refusal. It has no publication or installation authority: T-615
/// owns those two gates (ADR-0103 decision 5). A refusal names its target only when the producer exposes it.
/// </summary>
public static class RuleCrossPackageAuthoring
{
    /// <summary>The consumer envelope does not require the target's package.</summary>
    public const string DependencyUndeclared = "rules.reference.dependency_undeclared";

    /// <summary>The producer does not expose the target definition.</summary>
    public const string NotExposed = "rules.reference.not_exposed";

    /// <summary>The producer exposes the definition at another version or digest.</summary>
    public const string ExposureIncompatible = "rules.reference.exposure_incompatible";

    /// <summary>Checks every reference; refusals come back in reference order, all at the author stage.</summary>
    public static DefinitionRefusalReport Check(RuleDefinitionEnvelope consumer, IReadOnlyList<CrossPackageReference> references,
        IReadOnlyList<PackageExposure> exposures)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(exposures);
        var refusals = new List<DefinitionRefusal>();
        foreach (var reference in references)
        {
            var target = reference.Target;
            if (string.Equals(reference.Source.PackageId, target.PackageId, StringComparison.Ordinal)) continue;
            var exposed = exposures.Where(exposure => string.Equals(exposure.PackageId, target.PackageId, StringComparison.Ordinal))
                .SelectMany(exposure => exposure.Exposed)
                .FirstOrDefault(item => string.Equals(item.DefinitionId, target.DefinitionId, StringComparison.Ordinal));
            string? visible = exposed is null ? null : $"{exposed.PackageId}/{exposed.DefinitionId}@{exposed.Version}";

            if (!consumer.Requires.Contains(target.PackageId, StringComparer.Ordinal))
                refusals.Add(new(DependencyUndeclared, reference.Pointer, visible));
            if (exposed is null)
                refusals.Add(new(NotExposed, reference.Pointer));
            else if (exposed.Version != target.Version || exposed.Digest != target.Digest)
                refusals.Add(new(ExposureIncompatible, reference.Pointer, visible));
        }
        return new(DefinitionAdmissionPhase.Author, refusals.AsReadOnly());
    }
}
