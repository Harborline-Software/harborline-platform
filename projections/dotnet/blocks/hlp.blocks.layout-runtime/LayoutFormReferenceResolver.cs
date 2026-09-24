using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Blocks.LayoutRuntime;

/// <summary>
/// DES-0052 layout-ck-37 — resolves a published FormComponent reference to exactly the form
/// version it pins. It never asks for the published head, so a later form publication cannot
/// change what a published surface embeds; an absent or draft pin resolves to nothing.
/// </summary>
public static class LayoutFormReferenceResolver
{
    /// <summary>Resolves one pinned form reference through the shared definition store.</summary>
    /// <param name="store">The tenant's definition store.</param>
    /// <param name="tenant">The tenant that owns the surface.</param>
    /// <param name="reference">The surface's immutable form pin.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The pinned published revision, or <see langword="null"/> when that exact version is not published.</returns>
    public static ValueTask<DefinitionRevision?> ResolveAsync(
        IVersionedDefinitionStore store,
        string tenant,
        LayoutFormReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(reference);

        return store.ResolvePublishedAsync(
            new DefinitionBinding(new DefinitionKey(tenant, DefinitionKind.Forms, reference.FormDefinitionId), reference.FormVersionId),
            cancellationToken);
    }
}
