using System.Text;
using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Blocks.LayoutRuntime;

/// <summary>A published Layout body and the renderer-neutral plan derived from it.</summary>
public sealed record LayoutResolvedSurface(string VersionId, LayoutDefinition Definition, LayoutRenderPlan Plan);

/// <summary>
/// Adapts Layout's published immutable bodies from the shared definition store. This type owns no
/// history or lifecycle state; all publication and exact version resolution remain in the shared store.
/// </summary>
public sealed class LayoutPublishedSurfaceResolver
{
    private readonly IVersionedDefinitionStore _store;
    private readonly LayoutRuntimeEngine _engine;
    private readonly LayoutHostRegisters _registers;

    /// <summary>Creates a resolver over the shared registry-neutral definition store.</summary>
    /// <param name="store">The shared definition store.</param>
    /// <param name="engine">The flow engine, or a new one.</param>
    /// <param name="registers">The host's bound registers, or the platform grammar alone.</param>
    public LayoutPublishedSurfaceResolver(IVersionedDefinitionStore store, LayoutRuntimeEngine? engine = null, LayoutHostRegisters? registers = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _engine = engine ?? new LayoutRuntimeEngine();
        _registers = registers ?? LayoutHostRegisters.Platform;
    }

    /// <summary>Resolves one exact published Layout version and derives its renderer-neutral plan.</summary>
    public async ValueTask<LayoutResolvedSurface> ResolveAsync(
        DefinitionBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Key.Kind != DefinitionKind.Layout)
            throw new ArgumentException("A Layout resolver requires a Layout binding.", nameof(binding));

        var revision = await _store.ResolvePublishedAsync(binding, cancellationToken);
        if (revision is null)
            throw new InvalidOperationException("layout.published_version_not_found");

        LayoutDefinition definition;
        try
        {
            definition = LayoutDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(revision.Document.BodyJson));
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("layout.persisted_body_invalid", exception);
        }

        try
        {
            LayoutPersistedValueAdmission.ValidateForRuntime(definition, _registers);
        }
        catch (LayoutDefinitionAdmissionException exception)
        {
            throw new InvalidOperationException("layout.persisted_body_invalid", exception);
        }

        return new(revision.Document.VersionId, definition, _engine.Flow(definition, _registers.Pages));
    }
}
