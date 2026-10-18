using System.Security.Cryptography;
using System.Text;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Forms.Engine.Authoring;

namespace Harborline.Blocks.LayoutRuntime;

/// <summary>Read-only full Published-history Layout census for Form publication, not render-time policy.</summary>
public sealed class LayoutFormPublicationImpactSource : IFormPublishedSurfaceSource
{
    private readonly IVersionedDefinitionStore _store;

    /// <summary>Uses the same immutable definition store as exact Layout/Form pin resolution.</summary>
    /// <param name="store">The host's shared definition store.</param>
    public LayoutFormPublicationImpactSource(IVersionedDefinitionStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async ValueTask<FormPublishedSurfaceRead> ReadAsync(FormPublicationBasis basis,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(basis);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var candidate = basis.Candidate;
            var tenant = candidate.Tenant.Value;
            if (string.IsNullOrWhiteSpace(tenant) || candidate.Tenant.IsSystemSentinel
                || string.IsNullOrWhiteSpace(candidate.DefinitionId.Value)
                || string.IsNullOrWhiteSpace(candidate.VersionId)
                || basis.NoPredecessor != (basis.Predecessor is null)) return Unavailable();
            if (basis.Predecessor is { } predecessor)
            {
                if (predecessor.Tenant != candidate.Tenant || predecessor.DefinitionId != candidate.DefinitionId
                    || string.IsNullOrWhiteSpace(predecessor.VersionId)) return Unavailable();
                var binding = new DefinitionBinding(new(tenant, DefinitionKind.Forms, predecessor.DefinitionId.Value),
                    predecessor.VersionId);
                var form = await _store.ResolvePublishedAsync(binding, cancellationToken).ConfigureAwait(false);
                if (form is null || !ValidPublished(form, binding.Key)
                    || form.Document.VersionId != predecessor.VersionId
                    || form.Document.Version != predecessor.Version.ToString()) return Unavailable();
            }

            var keys = await _store.ListKeysAsync(tenant, DefinitionKind.Layout, cancellationToken).ConfigureAwait(false);
            if (keys is null) return Unavailable();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var surfaces = new Dictionary<(string, string), FormPublishedSurface>();
            foreach (var key in keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (key is null || key.Tenant != tenant || key.Kind != DefinitionKind.Layout
                    || string.IsNullOrWhiteSpace(key.DefinitionId) || !seenKeys.Add(key.DefinitionId)) return Unavailable();
                var history = await _store.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false);
                if (history is null || history.Count == 0) return Unavailable();
                var versions = new Dictionary<string, DefinitionRevision>(StringComparer.Ordinal);
                foreach (var revision in history)
                {
                    if (revision is null || revision.Document is null || revision.Document.Key != key
                        || !Enum.IsDefined(revision.Status)) return Unavailable();
                    if (revision.Status == DefinitionStatus.Draft) continue;
                    if (!ValidPublished(revision, key)) return Unavailable();
                    if (versions.TryGetValue(revision.Document.VersionId, out var prior))
                    {
                        if (prior.Document != revision.Document || prior.Digest != revision.Digest) return Unavailable();
                        continue;
                    }
                    versions.Add(revision.Document.VersionId, revision);
                    var layout = LayoutDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(revision.Document.BodyJson));
                    if (layout.Envelope is null || layout.Envelope.Identity != key.DefinitionId
                        || layout.Envelope.Tenant != tenant || layout.Envelope.Version != revision.Document.Version
                        || layout.SchemaVersion != 1 || !Enum.IsDefined(layout.Medium) || !Enum.IsDefined(layout.DefaultIntent)
                        || !Enum.IsDefined(layout.Envelope.CascadeLayer) || layout.Envelope.Requires is null
                        || layout.Blocks is null || layout.PageLayouts is null || layout.PageMasters is null
                        || layout.PageRuns is null || layout.DrillThroughTargets is null) return Unavailable();
                    var matching = new HashSet<string>(StringComparer.Ordinal);
                    var blockIds = new HashSet<string>(StringComparer.Ordinal);
                    var pending = new Stack<LayoutBlock>(layout.Blocks);
                    while (pending.TryPop(out var block))
                    {
                        if (block is null || string.IsNullOrWhiteSpace(block.Id) || !blockIds.Add(block.Id)
                            || string.IsNullOrWhiteSpace(block.Kind) || block.Binding is null
                            || block.Children is null) return Unavailable();
                        if (block.Form is { } pin)
                        {
                            if (string.IsNullOrWhiteSpace(pin.FormDefinitionId)
                                || string.IsNullOrWhiteSpace(pin.FormVersionId)) return Unavailable();
                            if (pin.FormDefinitionId == candidate.DefinitionId.Value)
                            {
                                if (basis.NoPredecessor) return Unavailable();
                                if (pin.FormVersionId == basis.Predecessor!.VersionId) matching.Add(block.Id);
                            }
                        }
                        foreach (var child in block.Children) pending.Push(child);
                    }
                    if (matching.Count != 0)
                        surfaces.Add((key.DefinitionId, revision.Document.VersionId), new(tenant, key.DefinitionId,
                            revision.Document.VersionId, revision.Document.Version, revision.Digest,
                            Array.AsReadOnly(matching.Order(StringComparer.Ordinal).ToArray())));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new FormPublishedSurfaceRead.Complete(tenant, Array.AsReadOnly(surfaces.Values
                .OrderBy(row => row.DefinitionId, StringComparer.Ordinal)
                .ThenBy(row => row.VersionId, StringComparer.Ordinal).ToArray()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Unavailable(); }
    }

    private static FormPublishedSurfaceRead Unavailable() => new FormPublishedSurfaceRead.Unavailable("layout_census_unavailable");

    private static bool ValidPublished(DefinitionRevision revision, DefinitionKey key) =>
        revision.Document is not null && revision.Document.Key == key && revision.Status == DefinitionStatus.Published
        && revision.Revision > 0 && !string.IsNullOrWhiteSpace(revision.Document.VersionId)
        && !string.IsNullOrWhiteSpace(revision.Document.Version) && revision.Document.BodyJson is not null
        && revision.Digest == Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(revision.Document.BodyJson)));
}
