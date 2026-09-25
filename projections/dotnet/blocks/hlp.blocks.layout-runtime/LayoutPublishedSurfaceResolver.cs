using System.Text;
using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Blocks.LayoutRuntime;

/// <summary>A published Layout body, the renderer-neutral plan derived from it, and the authority it carries (DES-0052 C1).</summary>
public sealed record LayoutResolvedSurface(string VersionId, LayoutDefinition Definition, LayoutRenderPlan Plan, LayoutSurfaceAuthority Authority);

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

    /// <summary>
    /// Opens one exact published Layout version for <paramref name="reader"/> and derives its plan and authority
    /// (layout-eng-26). A surface the reader may not open is refused before the store is read.
    /// </summary>
    /// <param name="binding">The exact published version.</param>
    /// <param name="reader">The reader's Access, asked for <c>layout:open</c>. Required.</param>
    /// <param name="submitter">The reader's submit answers, from which the returned authority is decided. Required.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async ValueTask<LayoutResolvedSurface> ResolveAsync(
        DefinitionBinding binding,
        ILayoutAccess reader,
        ILayoutSubmitAccess submitter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(submitter);
        if (binding.Key.Kind != DefinitionKind.Layout)
            throw new ArgumentException("A Layout resolver requires a Layout binding.", nameof(binding));
        LayoutAuthorityGate.RequireOpen(binding.Key.DefinitionId, reader);

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

        return new(revision.Document.VersionId, definition, _engine.Flow(definition, _registers.Pages), LayoutAuthorityGate.Authority(definition, submitter));
    }
}
