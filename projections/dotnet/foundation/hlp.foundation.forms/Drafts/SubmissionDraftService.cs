using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Drafts;

/// <summary>
/// Reference <see cref="ISubmissionDraftService"/> (ADR 0135 amendment 2026-07-01 — D2).
/// Composes one fail-closed <see cref="IFormsActorScope"/>, an
/// <see cref="ISubmissionDraftStore"/>, and a clock to key every draft by
/// <c>(TenantId, case/subject id, PartyId)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fail-closed keying is the whole point.</b> <see cref="ResolveScopeAsync"/> obtains
/// tenant, Party, and actor from one atomic actor scope. A missing or unprovisioned actor
/// blocks before a key is formed, and values from different principals cannot be combined.
/// </para>
/// </remarks>
public sealed class SubmissionDraftService : ISubmissionDraftService
{
    private readonly IFormsActorScope _actorScope;
    private readonly ISubmissionDraftStore _store;
    private readonly TimeProvider _clock;

    /// <summary>Constructs the service over one atomic actor scope, the draft store, and a clock.</summary>
    public SubmissionDraftService(
        IFormsActorScope actorScope,
        ISubmissionDraftStore store,
        TimeProvider? clock = null)
    {
        _actorScope = actorScope ?? throw new ArgumentNullException(nameof(actorScope));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<SubmissionDraftKey> SaveDraftAsync(
        FormDefinitionId formId,
        DraftCaseId caseId,
        SubmissionDraftProvenance provenance,
        ReadOnlyMemory<byte> body,
        string? subjectId = null,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        var (tenant, partyId) = await ResolveScopeAsync(ct).ConfigureAwait(false);
        var key = new SubmissionDraftKey(tenant, caseId, partyId);
        var now = _clock.GetUtcNow();

        // Preserve the original CreatedAt across re-saves (same key = the same draft).
        var existing = await _store.GetAsync(key, ct).ConfigureAwait(false);
        var createdAt = existing?.CreatedAt ?? now;

        var draft = new SubmissionDraft(
            Key: key,
            FormId: formId,
            Provenance: provenance,
            Body: body,
            SubjectId: subjectId,
            CreatedAt: createdAt,
            UpdatedAt: now,
            ExpiresAt: expiresAt);

        await _store.UpsertAsync(draft, ct).ConfigureAwait(false);
        return key;
    }

    /// <inheritdoc />
    public async Task<SubmissionDraft?> ResumeDraftAsync(FormDefinitionId formId, DraftCaseId caseId, CancellationToken ct = default)
    {
        var (tenant, partyId) = await ResolveScopeAsync(ct).ConfigureAwait(false);
        var key = new SubmissionDraftKey(tenant, caseId, partyId);
        var draft = await _store.GetAsync(key, ct).ConfigureAwait(false);
        return draft?.FormId == formId ? draft : null;
    }

    /// <inheritdoc />
    public async Task<bool> AbandonDraftAsync(FormDefinitionId formId, DraftCaseId caseId, CancellationToken ct = default)
    {
        var (tenant, partyId) = await ResolveScopeAsync(ct).ConfigureAwait(false);
        var key = new SubmissionDraftKey(tenant, caseId, partyId);
        var draft = await _store.GetAsync(key, ct).ConfigureAwait(false);
        if (draft is null || draft.FormId != formId)
        {
            return false;
        }
        return await _store.DeleteAsync(key, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubmissionDraft>> ListMyDraftsAsync(CancellationToken ct = default)
    {
        var (tenant, partyId) = await ResolveScopeAsync(ct).ConfigureAwait(false);
        return await _store.ListByPartyAsync(tenant, partyId, ct).ConfigureAwait(false);
    }

    /// <summary>Resolves one fail-closed actor scope and returns its tenant/Party draft key.</summary>
    private async Task<(TenantId Tenant, Guid PartyId)> ResolveScopeAsync(CancellationToken ct)
    {
        var scope = await _actorScope.GetRequiredAsync(ct).ConfigureAwait(false);
        return (scope.Tenant, scope.PartyId);
    }

}
