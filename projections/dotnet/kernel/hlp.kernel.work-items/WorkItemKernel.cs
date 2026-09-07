using Harborline.Foundation.Authorization;
using Harborline.Foundation.MultiTenancy;
using System.Text.Json;

namespace Harborline.Kernel.WorkItems;

/// <summary>Default work-item deep-module implementation.</summary>
public sealed class WorkItemKernel : IWorkItemKernel
{
    private readonly ITenantContext _tenantContext;
    private readonly IPartyContext _partyContext;
    private readonly IWorkItemStore _store;
    private readonly TimeProvider _time;

    /// <summary>Constructs the kernel over one scoped actor context and atomic store.</summary>
    public WorkItemKernel(ITenantContext tenantContext, IPartyContext partyContext, IWorkItemStore store, TimeProvider? timeProvider = null)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _partyContext = partyContext ?? throw new ArgumentNullException(nameof(partyContext));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<WorkItemMutationResult> CreateAsync(CreateWorkItemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCreate(request);
        var scope = await ScopeAsync(cancellationToken).ConfigureAwait(false);
        if (scope is null) return Denied();

        string fingerprint;
        try { fingerprint = WorkItemCanonical.CreateFingerprint(scope.Value.TenantId, request); }
        catch (JsonException) { return Invalid(); }

        var replay = await ReplayAsync(scope.Value.TenantId, request.IdempotencyKey, fingerprint, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return replay;

        var now = _time.GetUtcNow();
        var outcomes = request.AllowedOutcomes.ToArray();
        var snapshot = new WorkItemSnapshot(
            request.Id, request.SubjectRef, request.DefinitionKey, request.DefinitionVersion, request.InitialStep, 0, 1,
            request.InitialStatus, WorkItemCanonical.NormalizeJson(request.StateJson),
            WorkItemCanonical.NormalizeJson(request.BasisJson), outcomes, now, now);
        var resultJson = "{\"created\":true}";
        var commit = new WorkItemCommit
        {
            TenantId = scope.Value.TenantId,
            ActorId = scope.Value.ActorId,
            Fingerprint = fingerprint,
            IdempotencyKey = request.IdempotencyKey,
            Snapshot = snapshot,
            Event = new WorkItemEvent(request.Id, 0, 0, request.InitialStep, "Created", snapshot.BasisJson, now),
            ResultJson = resultJson,
            Outbox = [CreateOutbox(scope.Value.TenantId, snapshot, "Created", snapshot.BasisJson)],
        };
        return FromStore(await _store.CommitCreateAsync(commit, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<WorkItemSnapshot?> GetAsync(string workItemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        var scope = await ScopeAsync(cancellationToken).ConfigureAwait(false);
        return scope is null ? null : await _store.GetAsync(scope.Value.TenantId, workItemId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkItemSnapshot>> ListOpenAsync(CancellationToken cancellationToken = default)
    {
        var scope = await ScopeAsync(cancellationToken).ConfigureAwait(false);
        return scope is null
            ? Array.Empty<WorkItemSnapshot>()
            : await _store.ListOpenAsync(scope.Value.TenantId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WorkItemMutationResult> TransitionAsync(TransitionWorkItemRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTransition(request);
        var scope = await ScopeAsync(cancellationToken).ConfigureAwait(false);
        if (scope is null) return Denied();

        string fingerprint;
        try { fingerprint = WorkItemCanonical.TransitionFingerprint(scope.Value.TenantId, request); }
        catch (JsonException) { return Invalid(); }

        var replay = await ReplayAsync(scope.Value.TenantId, request.IdempotencyKey, fingerprint, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return replay;

        var current = await _store.GetAsync(scope.Value.TenantId, request.Id, cancellationToken).ConfigureAwait(false);
        if (current is null) return new(WorkItemMutationDisposition.NotFound, null, null);
        if (current.Version != request.ExpectedVersion)
            return new(WorkItemMutationDisposition.VersionConflict, current, null);
        if (current.Status is WorkItemStatus.Completed or WorkItemStatus.Failed)
            return Invalid();

        var outcome = current.AllowedOutcomes.SingleOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id, request.OutcomeId)
            && StringComparer.Ordinal.Equals(candidate.FromStep, current.CurrentStep));
        if (outcome is null) return Invalid();

        var now = _time.GetUtcNow();
        var next = current with
        {
            CurrentStep = outcome.NextStep,
            Iteration = current.Iteration + (outcome.IsLoopBack ? 1 : 0),
            Version = current.Version + 1,
            Status = outcome.NextStatus,
            UpdatedAt = now,
        };
        var resultJson = WorkItemCanonical.NormalizeJson(request.ResultJson);
        var commit = new WorkItemCommit
        {
            TenantId = scope.Value.TenantId,
            ActorId = scope.Value.ActorId,
            Fingerprint = fingerprint,
            IdempotencyKey = request.IdempotencyKey,
            Snapshot = next,
            Event = new WorkItemEvent(request.Id, current.Version, next.Iteration, current.CurrentStep, outcome.Id, resultJson, now),
            ResultJson = resultJson,
            Outbox = [CreateOutbox(scope.Value.TenantId, next, outcome.Id, resultJson)],
            ExpectedVersion = request.ExpectedVersion,
            ExpectedStep = current.CurrentStep,
        };
        return FromStore(await _store.CommitTransitionAsync(commit, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Returns the stable deterministic key for a current snapshot.</summary>
    public static string DeriveStepKey(string tenantId, WorkItemSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(snapshot);
        return WorkItemCanonical.StepKey(tenantId, snapshot);
    }

    private async Task<WorkItemMutationResult?> ReplayAsync(string tenantId, string key, string fingerprint, CancellationToken cancellationToken)
    {
        var receipt = await _store.GetReceiptAsync(tenantId, key, cancellationToken).ConfigureAwait(false);
        if (receipt is null) return null;
        return StringComparer.Ordinal.Equals(receipt.Fingerprint, fingerprint)
            ? new(WorkItemMutationDisposition.Replayed, receipt.Snapshot, receipt.ResultJson)
            : new(WorkItemMutationDisposition.IdempotencyConflict, null, null);
    }

    private async ValueTask<(string TenantId, string ActorId)?> ScopeAsync(CancellationToken cancellationToken)
    {
        var tenant = _tenantContext.Tenant;
        if (tenant is null || tenant.Status != TenantStatus.Active || tenant.Id.IsSystemSentinel) return null;
        try
        {
            var partyId = await _partyContext.GetCurrentPartyIdAsync(cancellationToken).ConfigureAwait(false);
            return partyId == Guid.Empty ? null : (tenant.Id.Value, partyId.ToString("D"));
        }
        catch (PrincipalPartyResolutionException)
        {
            return null;
        }
    }

    private static WorkItemOutboxMessage CreateOutbox(string tenantId, WorkItemSnapshot snapshot, string action, string dataJson) =>
        new(
            $"{tenantId}:{snapshot.Id}:{snapshot.Version}",
            snapshot.Version == 1 ? "work-item.created" : "work-item.transitioned",
            JsonSerializer.Serialize(new
            {
                tenantId,
                workItemId = snapshot.Id,
                snapshot.SubjectRef,
                snapshot.Version,
                snapshot.Iteration,
                snapshot.CurrentStep,
                status = snapshot.Status.ToString(),
                action,
                data = ParseJson(dataJson),
            }));

    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private static WorkItemMutationResult FromStore(WorkItemStoreResult result) =>
        new(result.Disposition, result.Receipt?.Snapshot, result.Receipt?.ResultJson);
    private static WorkItemMutationResult Denied() => new(WorkItemMutationDisposition.Denied, null, null);
    private static WorkItemMutationResult Invalid() => new(WorkItemMutationDisposition.InvalidTransition, null, null);

    private static void ValidateCreate(CreateWorkItemRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubjectRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DefinitionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DefinitionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InitialStep);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        if (request.AllowedOutcomes.Count == 0) throw new ArgumentException("At least one typed outcome is required.", nameof(request));
        var outcomeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var outcome in request.AllowedOutcomes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outcome.Id);
            ArgumentException.ThrowIfNullOrWhiteSpace(outcome.FromStep);
            ArgumentException.ThrowIfNullOrWhiteSpace(outcome.NextStep);
            if (!outcomeKeys.Add($"{outcome.FromStep}\u001f{outcome.Id}"))
                throw new ArgumentException("Outcome IDs must be unique within a source step.", nameof(request));
        }
        if (request.InitialStatus is WorkItemStatus.Completed or WorkItemStatus.Failed)
            throw new ArgumentException("A new work item must be running or parked.", nameof(request));
    }

    private static void ValidateTransition(TransitionWorkItemRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.ExpectedVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutcomeId);
    }
}
