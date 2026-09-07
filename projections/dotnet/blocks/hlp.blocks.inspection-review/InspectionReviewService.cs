using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.MultiTenancy;
using Harborline.Kernel.WorkItems;

namespace Harborline.Blocks.InspectionReview;

/// <summary>Default authenticated subject query and inspection-review decision facade.</summary>
public sealed class InspectionReviewService : IInspectionReviewService
{
    private const int MaximumNoteLength = 2000;
    private readonly ITenantContext _tenants;
    private readonly IPartyContext _parties;
    private readonly IInspectionReviewAuthorizer _authorization;
    private readonly IInspectionSubjectDirectory _subjects;
    private readonly IWorkItemKernel _workItems;

    /// <summary>Composes the facade over current scope, policy, subjects, and Work Items.</summary>
    public InspectionReviewService(
        ITenantContext tenants,
        IPartyContext parties,
        IInspectionReviewAuthorizer authorization,
        IInspectionSubjectDirectory subjects,
        IWorkItemKernel workItems)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _parties = parties ?? throw new ArgumentNullException(nameof(parties));
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _subjects = subjects ?? throw new ArgumentNullException(nameof(subjects));
        _workItems = workItems ?? throw new ArgumentNullException(nameof(workItems));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InspectionSubject>> ListSubjectsAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await ScopeForAsync(InspectionReviewOperation.ReadSubjects, cancellationToken).ConfigureAwait(false);
        return tenant is null
            ? Array.Empty<InspectionSubject>()
            : await _subjects.ListAsync(tenant.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<InspectionSubject?> GetSubjectAsync(string subjectReference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectReference);
        var tenant = await ScopeForAsync(InspectionReviewOperation.ReadSubjects, cancellationToken).ConfigureAwait(false);
        return tenant is null ? null : await _subjects.GetAsync(tenant.Value, subjectReference, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InspectionReviewView>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await ScopeForAsync(InspectionReviewOperation.ReadReviews, cancellationToken).ConfigureAwait(false);
        if (tenant is null) return Array.Empty<InspectionReviewView>();
        var snapshots = await _workItems.ListOpenAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<InspectionReviewView>();
        foreach (var snapshot in snapshots)
        {
            var view = await MapAsync(tenant.Value, snapshot, allowTerminal: false, cancellationToken).ConfigureAwait(false);
            if (view is not null) result.Add(view);
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<InspectionReviewView?> GetAsync(string reviewId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewId);
        var tenant = await ScopeForAsync(InspectionReviewOperation.ReadReviews, cancellationToken).ConfigureAwait(false);
        if (tenant is null) return null;
        var snapshot = await _workItems.GetAsync(reviewId, cancellationToken).ConfigureAwait(false);
        return snapshot is null ? null : await MapAsync(tenant.Value, snapshot, allowTerminal: false, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<InspectionReviewMutationResult> DecideAsync(
        DecideInspectionReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReviewId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        if (request.ExpectedVersion <= 0
            || !Enum.IsDefined(request.Decision)
            || request.Note?.Length > MaximumNoteLength)
            return new(InspectionReviewMutationDisposition.InvalidDecision, null);

        var tenant = await ScopeForAsync(InspectionReviewOperation.Decide, cancellationToken).ConfigureAwait(false);
        if (tenant is null) return new(InspectionReviewMutationDisposition.Denied, null);
        var before = await _workItems.GetAsync(request.ReviewId, cancellationToken).ConfigureAwait(false);
        if (before is null || await MapAsync(tenant.Value, before, allowTerminal: true, cancellationToken).ConfigureAwait(false) is null)
            return new(InspectionReviewMutationDisposition.NotFound, null);

        var decision = request.Decision == InspectionReviewDecision.Approve
            ? InspectionReviewProcess.ApproveOutcome
            : InspectionReviewProcess.ReworkOutcome;
        var resultJson = JsonSerializer.Serialize(new
        {
            decision,
            note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note,
        }, InspectionReviewJson.Options);
        var mutation = await _workItems.TransitionAsync(new TransitionWorkItemRequest
        {
            Id = request.ReviewId,
            ExpectedVersion = request.ExpectedVersion,
            IdempotencyKey = request.IdempotencyKey,
            OutcomeId = decision,
            ResultJson = resultJson,
        }, cancellationToken).ConfigureAwait(false);

        var disposition = mutation.Disposition switch
        {
            WorkItemMutationDisposition.Committed => InspectionReviewMutationDisposition.Committed,
            WorkItemMutationDisposition.Replayed => InspectionReviewMutationDisposition.Replayed,
            WorkItemMutationDisposition.IdempotencyConflict => InspectionReviewMutationDisposition.IdempotencyConflict,
            WorkItemMutationDisposition.VersionConflict => InspectionReviewMutationDisposition.VersionConflict,
            WorkItemMutationDisposition.Denied => InspectionReviewMutationDisposition.Denied,
            WorkItemMutationDisposition.NotFound => InspectionReviewMutationDisposition.NotFound,
            _ => InspectionReviewMutationDisposition.NotFound,
        };
        var view = mutation.Snapshot is null
            ? null
            : await MapAsync(tenant.Value, mutation.Snapshot, allowTerminal: true, cancellationToken).ConfigureAwait(false);
        return new(disposition, view);
    }

    private async ValueTask<TenantId?> ScopeForAsync(InspectionReviewOperation operation, CancellationToken cancellationToken)
    {
        var tenant = await InspectionReviewScope.ResolveAsync(_tenants, _parties, cancellationToken).ConfigureAwait(false);
        if (tenant is null) return null;
        try
        {
            return await _authorization.IsAllowedAsync(operation, cancellationToken).ConfigureAwait(false) ? tenant : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private async ValueTask<InspectionReviewView?> MapAsync(
        TenantId tenant,
        WorkItemSnapshot snapshot,
        bool allowTerminal,
        CancellationToken cancellationToken)
    {
        if (!StringComparer.Ordinal.Equals(snapshot.DefinitionKey, InspectionReviewProcess.DefinitionKey)
            || !StringComparer.Ordinal.Equals(snapshot.DefinitionVersion, InspectionReviewProcess.DefinitionVersion)) return null;
        var lifecycle = snapshot.CurrentStep switch
        {
            InspectionReviewProcess.ReviewState when snapshot.Status == WorkItemStatus.Parked => InspectionReviewLifecycle.Pending,
            InspectionReviewProcess.ApprovedState when snapshot.Status == WorkItemStatus.Completed => InspectionReviewLifecycle.Approved,
            InspectionReviewProcess.ReworkState when snapshot.Status == WorkItemStatus.Completed => InspectionReviewLifecycle.Rework,
            _ => (InspectionReviewLifecycle?)null,
        };
        if (lifecycle is null || (!allowTerminal && lifecycle != InspectionReviewLifecycle.Pending)) return null;
        var state = InspectionReviewJson.Deserialize(snapshot.StateJson);
        if (state is null || !StringComparer.Ordinal.Equals(state.SubjectReference, snapshot.SubjectRef)) return null;
        var subject = await _subjects.GetAsync(tenant, state.SubjectReference, cancellationToken).ConfigureAwait(false);
        if (subject is null) return null;
        var decisions = lifecycle == InspectionReviewLifecycle.Pending
            ? new[] { InspectionReviewDecision.Approve, InspectionReviewDecision.Rework }
            : Array.Empty<InspectionReviewDecision>();
        return new(
            snapshot.Id,
            state.SubmissionId,
            subject,
            state.ConditionScore,
            state.ConditionOutcome,
            lifecycle.Value,
            snapshot.Version,
            state.SubmittedAt,
            decisions);
    }
}
