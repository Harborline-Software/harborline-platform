using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms;

/// <summary>
/// Reports the immutable Published revisions created by <see cref="SubmitGateSeedMigration"/>,
/// legacy Published forms for which the caller supplied no replacement gate, forms deferred
/// because a pending Draft outranks the Published revision being migrated, and System-owned
/// Withdrawn revisions with no gate that an operator can repair and restore.
/// </summary>
public sealed record SubmitGateSeedMigrationResult(
    IReadOnlyList<FormDefinition> Published,
    IReadOnlyList<FormDefinition> Skipped,
    IReadOnlyList<FormDefinition> DeferredPendingDraft,
    IReadOnlyList<FormDefinition> WithdrawnPendingGate);

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
        var deferredPendingDraft = new List<FormDefinition>();
        await foreach (var definition in _definitions.ListCurrentPublishedByTenantAsync(tenant, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (definition.SubmitGate is not null)
            {
                continue;
            }

            // A Draft above the Published version being migrated is a pending author edit. Bumping
            // past it (the old NextVersionAsync picked the highest version across EVERY status,
            // Draft included, and republished the LEGACY content one patch above it) would let the
            // migrated legacy content outrank and permanently shadow that Draft once published,
            // since GetCurrentPublishedAsync/ListCurrentPublishedByTenantAsync select by version, not
            // by recency. Defer instead: the operator resolves the Draft (gates and publishes it, or
            // discards it) before this form is safe to migrate.
            if (await HasDraftAboveAsync(tenant, definition.Id, definition.Version, ct).ConfigureAwait(false))
            {
                deferredPendingDraft.Add(definition);
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

        // Legacy System-owned Withdrawn revisions with no gate cannot be restored to Published
        // (RestorePackProjectionAsync now requires one, fail-closed) and this migration never
        // republishes a Withdrawn revision on the operator's behalf. Surface them so the operator
        // can map a gate and repair each one directly.
        var withdrawnPendingGate = new List<FormDefinition>();
        await foreach (var candidate in _definitions.ListByTenantAsync(tenant, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (candidate.Status == FormDefinitionStatus.Withdrawn
                && candidate.Owner == IdentityRef.System
                && candidate.SubmitGate is null)
            {
                withdrawnPendingGate.Add(candidate);
            }
        }

        return new SubmitGateSeedMigrationResult(published, skipped, deferredPendingDraft, withdrawnPendingGate);
    }

    private async ValueTask<bool> HasDraftAboveAsync(
        TenantId tenant,
        FormDefinitionId id,
        SemanticVersion current,
        CancellationToken ct)
    {
        await foreach (var candidate in _definitions.ListByTenantAsync(tenant, ct))
        {
            if (candidate.Id == id && candidate.Status == FormDefinitionStatus.Draft && candidate.Version > current)
            {
                return true;
            }
        }

        return false;
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
