using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Drafts;

/// <summary>
/// The fail-closed facade for save-and-resume submission-drafts (ADR 0135 amendment
/// 2026-07-01 — D2). This is the ONE seam that turns "who is calling" into the
/// server-derived <c>(TenantId, PartyId)</c> half of the draft key through one
/// <see cref="IFormsActorScope"/> and combines it with the caller's client-mintable
/// <see cref="DraftCaseId"/>. No method accepts body-supplied actor, party, or tenant values.
/// </summary>
/// <remarks>
/// <b>Fail-closed.</b> Every operation resolves tenant, Party, and actor atomically.
/// An absent or unprovisioned principal blocks before a key is formed.
/// </remarks>
public interface ISubmissionDraftService
{
    /// <summary>
    /// Saves (inserts or replaces) the actor's draft for <paramref name="caseId"/> on
    /// <paramref name="formId"/>. Resolves the tenant + party fail-closed, preserves the
    /// original <c>CreatedAt</c> across re-saves, and returns the resolved key.
    /// </summary>
    Task<SubmissionDraftKey> SaveDraftAsync(
        FormDefinitionId formId,
        DraftCaseId caseId,
        SubmissionDraftProvenance provenance,
        ReadOnlyMemory<byte> body,
        string? subjectId = null,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default);

    /// <summary>
    /// Resumes the actor's draft for <paramref name="caseId"/> only under <paramref name="formId"/>.
    /// Resolves the actor scope fail-closed, then loads by the resolved key. Returns null when there is no
    /// resumable draft (or the subject was crypto-shredded).
    /// </summary>
    Task<SubmissionDraft?> ResumeDraftAsync(FormDefinitionId formId, DraftCaseId caseId, CancellationToken ct = default);

    /// <summary>Abandons the actor's draft only when <paramref name="caseId"/> belongs to <paramref name="formId"/>.</summary>
    Task<bool> AbandonDraftAsync(FormDefinitionId formId, DraftCaseId caseId, CancellationToken ct = default);

    /// <summary>Lists the actor's resumable drafts in the active tenant (the "my in-progress cases" surface), most-recent first.</summary>
    Task<IReadOnlyList<SubmissionDraft>> ListMyDraftsAsync(CancellationToken ct = default);
}
