using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>
/// The execution receipt of one run (DES-0056 <c>execution-runtime-ck-3</c>): its run identity, its status and
/// the stable identity of the applicable Access decision. The receipt links that decision and never carries or
/// recomputes Access's authorization evidence (<c>execution-runtime-ck-2</c>). It is durable tenant data and
/// never pack content. Properties are get-only, so a <c>with</c> expression cannot bypass validation.
/// </summary>
public sealed record ExecutionReceipt
{
    /// <summary>Creates a receipt, refusing a missing tenant or run identity, a status outside the vocabulary, or a blank decision identity.</summary>
    /// <param name="tenantId">The tenant the run belongs to.</param>
    /// <param name="runId">The run's kind-qualified identity.</param>
    /// <param name="status">The run's status, one of the closed run status vocabulary.</param>
    /// <param name="accessDecisionId">The applicable Access decision's stable identity, or null when none applies.</param>
    public ExecutionReceipt(TenantId tenantId, RunId runId, RunStatus status, string? accessDecisionId)
    {
        ExecutionReceiptRules.RequireTenant(tenantId);
        ExecutionReceiptRules.RequireRun(runId, ExecutionRuntimeRefusals.ExecutionReceiptInvalid);
        if (!Enum.IsDefined(status))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.ExecutionReceiptInvalid,
                $"Status '{RunStatusTransitions.WireName(status)}' is outside the run status vocabulary.");
        }

        if (accessDecisionId is not null && string.IsNullOrWhiteSpace(accessDecisionId))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.ExecutionReceiptInvalid,
                "An Access decision identity, when present, must not be blank; record null when no decision applies.");
        }

        TenantId = tenantId;
        RunId = runId;
        Status = status;
        AccessDecisionId = accessDecisionId;
    }

    /// <summary>The tenant the run belongs to.</summary>
    public TenantId TenantId { get; }

    /// <summary>The run's kind-qualified identity.</summary>
    public RunId RunId { get; }

    /// <summary>The run's status.</summary>
    public RunStatus Status { get; }

    /// <summary>The applicable Access decision's stable identity, opaque to this substrate; null when none applies.</summary>
    public string? AccessDecisionId { get; }
}

/// <summary>
/// One step of a run's execution trace (DES-0056 <c>execution-runtime-ck-4</c>): the run's own progress, distinct
/// from Access's four-stage authorization trace. Ordinals start at 1 and are consecutive per run.
/// </summary>
public sealed record ExecutionTraceStep
{
    /// <summary>Creates a step, refusing an ordinal below 1 or a blank phase or status.</summary>
    /// <param name="ordinal">The step's 1-based position in the run's trace.</param>
    /// <param name="phase">The engine's name for the phase the step records.</param>
    /// <param name="status">The engine's status for that phase, recorded verbatim.</param>
    public ExecutionTraceStep(int ordinal, string phase, string status)
    {
        if (ordinal < 1)
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.TraceStepInvalid, $"Trace ordinal {ordinal} is below 1.");
        }

        if (string.IsNullOrWhiteSpace(phase) || string.IsNullOrWhiteSpace(status))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.TraceStepInvalid, $"Trace step {ordinal} requires a phase and a status.");
        }

        Ordinal = ordinal;
        Phase = phase;
        Status = status;
    }

    /// <summary>The step's 1-based position in the run's trace.</summary>
    public int Ordinal { get; }

    /// <summary>The engine's name for the phase the step records.</summary>
    public string Phase { get; }

    /// <summary>The engine's status for that phase, verbatim.</summary>
    public string Status { get; }
}

/// <summary>
/// The tenant-scoped port through which an engine emits a run's execution receipt and trace
/// (DES-0056 <c>execution-runtime-eng-1</c>, <c>execution-runtime-cc-1</c>) and a durable host adapter keeps them.
/// Every read and write is keyed by tenant and kind-qualified run identity; another tenant's data is never read.
/// </summary>
public interface IExecutionReceiptStore
{
    /// <summary>Stores the tenant's receipt for its run, replacing any earlier receipt for that run.</summary>
    ValueTask PutReceiptAsync(ExecutionReceipt receipt, CancellationToken cancellationToken = default);

    /// <summary>The tenant's receipt for a run, or null when none is stored.</summary>
    ValueTask<ExecutionReceipt?> GetReceiptAsync(TenantId tenantId, RunId runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a step to the tenant's trace for a run; refuses <c>execution.trace_step_out_of_order</c> unless
    /// the step's ordinal is exactly one past the last stored step (1 for the first).
    /// </summary>
    ValueTask AppendTraceStepAsync(TenantId tenantId, RunId runId, ExecutionTraceStep step, CancellationToken cancellationToken = default);

    /// <summary>The tenant's trace for a run in ordinal order; empty when no step is stored.</summary>
    ValueTask<IReadOnlyList<ExecutionTraceStep>> GetTraceAsync(TenantId tenantId, RunId runId, CancellationToken cancellationToken = default);
}

/// <summary>Single-process reference implementation of <see cref="IExecutionReceiptStore"/> for tests and hosts without durable storage.</summary>
public sealed class InMemoryExecutionReceiptStore : IExecutionReceiptStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(TenantId Tenant, RunId Run), ExecutionReceipt> _receipts = new();
    private readonly Dictionary<(TenantId Tenant, RunId Run), List<ExecutionTraceStep>> _traces = new();

    /// <inheritdoc />
    public ValueTask PutReceiptAsync(ExecutionReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _receipts[(receipt.TenantId, receipt.RunId)] = receipt;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<ExecutionReceipt?> GetReceiptAsync(TenantId tenantId, RunId runId, CancellationToken cancellationToken = default)
    {
        ExecutionReceiptRules.RequireTenant(tenantId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_receipts.GetValueOrDefault((tenantId, runId)));
        }
    }

    /// <inheritdoc />
    public ValueTask AppendTraceStepAsync(TenantId tenantId, RunId runId, ExecutionTraceStep step, CancellationToken cancellationToken = default)
    {
        ExecutionReceiptRules.RequireTenant(tenantId);
        ExecutionReceiptRules.RequireRun(runId, ExecutionRuntimeRefusals.TraceStepInvalid);
        ArgumentNullException.ThrowIfNull(step);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (tenantId, runId);
            if (!_traces.TryGetValue(key, out var steps))
            {
                steps = [];
                _traces[key] = steps;
            }

            if (step.Ordinal != steps.Count + 1)
            {
                throw new ExecutionRuntimeRefusedException(
                    ExecutionRuntimeRefusals.TraceStepOutOfOrder,
                    $"Run '{runId}' expects trace ordinal {steps.Count + 1}, not {step.Ordinal}.");
            }

            steps.Add(step);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ExecutionTraceStep>> GetTraceAsync(TenantId tenantId, RunId runId, CancellationToken cancellationToken = default)
    {
        ExecutionReceiptRules.RequireTenant(tenantId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<ExecutionTraceStep> trace = _traces.TryGetValue((tenantId, runId), out var steps) ? steps.ToArray() : [];
            return ValueTask.FromResult(trace);
        }
    }
}

internal static class ExecutionReceiptRules
{
    internal static void RequireTenant(TenantId tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId.Value))
        {
            throw new ArgumentException("An execution receipt or trace requires a tenant.", nameof(tenantId));
        }
    }

    internal static void RequireRun(RunId runId, string code)
    {
        if (runId.Kind.Value is null)
        {
            throw new ExecutionRuntimeRefusedException(code, "An execution receipt or trace requires a run identity.");
        }
    }
}
