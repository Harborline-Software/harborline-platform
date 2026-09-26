using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms;

/// <summary>
/// Reports the immutable Published revisions created by <see cref="SubmitGateSeedMigration"/>,
/// and legacy Published forms for which the caller supplied no replacement gate.
/// </summary>
public sealed record SubmitGateSeedMigrationResult(
    IReadOnlyList<FormDefinition> Published,
    IReadOnlyList<FormDefinition> Skipped);

/// <summary>
/// Seeds explicit submit gates onto legacy Published forms without rewriting immutable history.
/// Each replacement gate is supplied by the caller because this package has no generic Forms
/// write capability from which a safe default could be derived.
/// </summary>
public sealed class SubmitGateSeedMigration(IFormDefinitionStore definitions)
{
    private readonly IFormDefinitionStore _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));

    /// <summary>
    /// Registers and publishes a patch-bumped revision for each current Published form without a
    /// submit gate that has a caller-supplied replacement. Forms without a supplied replacement
    /// are returned as skipped. A later invocation observes the gated replacement as current and
    /// therefore does not publish another revision.
    /// </summary>
    public async ValueTask<SubmitGateSeedMigrationResult> ApplyAsync(
        TenantId tenant,
        IReadOnlyDictionary<FormDefinitionId, SubmitGate> replacementGates,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(replacementGates);

        var published = new List<FormDefinition>();
        var skipped = new List<FormDefinition>();
        await foreach (var definition in _definitions.ListCurrentPublishedByTenantAsync(tenant, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (definition.SubmitGate is not null)
            {
                continue;
            }

            if (!replacementGates.TryGetValue(definition.Id, out var replacementGate))
            {
                skipped.Add(definition);
                continue;
            }

            var revision = definition with
            {
                Version = await NextVersionAsync(tenant, definition.Id, definition.Version, ct).ConfigureAwait(false),
                Status = FormDefinitionStatus.Draft,
                SubmitGate = replacementGate,
            };
            published.Add(await _definitions.RegisterAndPublishAsync(revision, ct).ConfigureAwait(false));
        }

        return new SubmitGateSeedMigrationResult(published, skipped);
    }

    private async ValueTask<SemanticVersion> NextVersionAsync(
        TenantId tenant,
        FormDefinitionId id,
        SemanticVersion current,
        CancellationToken ct)
    {
        var highest = current;
        await foreach (var candidate in _definitions.ListByTenantAsync(tenant, ct))
        {
            if (candidate.Id == id && candidate.Version > highest)
            {
                highest = candidate.Version;
            }
        }

        return new SemanticVersion(highest.Major, highest.Minor, checked(highest.Patch + 1));
    }
}
