namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The named artefacts that compose an authored Layout surface.</summary>
public enum LayoutCompositionKind
{
    /// <summary>A form retains capture and submission semantics.</summary>
    Form,
    /// <summary>A template retains document issuance semantics.</summary>
    Template,
    /// <summary>A report retains report semantics.</summary>
    Report,
}

/// <summary>Pins a composition to an immutable surface without absorbing its own artefact.</summary>
/// <param name="Kind">The composing artefact family.</param>
/// <param name="DefinitionId">The composition's stable identity.</param>
/// <param name="Version">The composition's immutable version.</param>
/// <param name="SurfaceDefinitionId">The authored surface's identity.</param>
/// <param name="SurfaceVersion">The authored surface's immutable version.</param>
public sealed record LayoutCompositionReference(
    LayoutCompositionKind Kind,
    string DefinitionId,
    string Version,
    string SurfaceDefinitionId,
    string SurfaceVersion);

/// <summary>Produces an independent draft candidate from a composition's pinned surface.</summary>
public static class LayoutComposition
{
    /// <summary>Copies the pinned surface without publishing or retaining a synchronisation link.</summary>
    /// <param name="reference">The source composition and surface pin.</param>
    /// <param name="source">The surface resolved by the shared catalogue using the immutable pin.</param>
    /// <param name="draftEnvelope">The new draft's independent envelope.</param>
    /// <param name="kinds">The host kind register, or the platform grammar.</param>
    /// <returns>A detached candidate, not a stored or published version. The composition is unchanged.</returns>
    public static LayoutDefinition Detach(
        LayoutCompositionReference reference,
        LayoutDefinition source,
        LayoutDefinitionEnvelope draftEnvelope,
        LayoutBlockKindRegistry? kinds = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(draftEnvelope);
        if (!Enum.IsDefined(reference.Kind) || string.IsNullOrWhiteSpace(reference.DefinitionId)
            || !LayoutVersionSyntax.IsValid(reference.Version)
            || reference.SurfaceDefinitionId != source.Envelope.Identity
            || reference.SurfaceVersion != source.Envelope.Version
            || draftEnvelope.Identity == source.Envelope.Identity
            || draftEnvelope.Tenant != source.Envelope.Tenant)
            throw new ArgumentException("layout.composition.detach_invalid", nameof(reference));
        var candidate = source with { Envelope = draftEnvelope };
        LayoutDefinitionAdmission.ValidateForAuthoring(candidate, kinds ?? LayoutBlockKindRegistry.Platform);
        return LayoutDefinitionJson.Deserialize(LayoutDefinitionJson.SerializeCanonical(candidate));
    }
}
