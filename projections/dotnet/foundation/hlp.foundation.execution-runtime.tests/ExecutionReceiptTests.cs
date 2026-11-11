using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ExecutionRuntime.Tests;

/// <summary>
/// T-528/execution-receipt-and-trace. Oracles are literals taken from DES-0056 rows ck-2, ck-3, ck-4, eng-1 and
/// cc-1 and from the Layout consumer shape (<c>_shared/layout/execution-observation.json</c>: a decision identity
/// <c>audit.decision.…</c>, a null decision, and 1-based trace ordinals), never from the code under test.
/// </summary>
public sealed class ExecutionReceiptTests
{
    private static readonly TenantId TenantA = new("tenant-a");
    private static readonly TenantId TenantB = new("tenant-b");
    private static readonly RunKind WorkflowRun = new("workflow-run");
    private static readonly RunKind PlanRun = new("plan-run");
    private static readonly Guid Opaque = new("16300000-0000-0000-0000-000000000163");
    private const string DecisionId = "audit.decision.16300000-0000-0000-0000-000000000001";
    private static readonly int[] FirstTwoOrdinals = [1, 2];
    private static readonly string[] ReceiptFields = ["AccessDecisionId", "RunId", "Status", "TenantId"];
    private static readonly string[] TraceStepFields = ["Ordinal", "Phase", "Status"];

    [Fact(DisplayName = "execution-runtime-ck-2: the receipt links the Access decision by stable identity, an absent decision stays absent, and no authorization evidence is carried")]
    public async Task Access_decision_link()
    {
        // Oracle: DES-0056 ck-2/ck-3 rows (run identity, status, decision identity only) and the Layout fixture's
        // present and null decisions. Mutation: store `AccessDecisionId ?? ""` in the ExecutionReceipt constructor.
        var store = new InMemoryExecutionReceiptStore();
        var linked = new RunId(WorkflowRun, Opaque);
        var unlinked = new RunId(WorkflowRun, Guid.Parse("16300000-0000-0000-0000-000000000002"));
        await store.PutReceiptAsync(new ExecutionReceipt(TenantA, linked, RunStatus.Succeeded, DecisionId));
        await store.PutReceiptAsync(new ExecutionReceipt(TenantA, unlinked, RunStatus.DeadLettered, null));

        Assert.Equal(DecisionId, (await store.GetReceiptAsync(TenantA, linked))!.AccessDecisionId);
        Assert.Null((await store.GetReceiptAsync(TenantA, unlinked))!.AccessDecisionId);
        await store.PutReceiptAsync(new ExecutionReceipt(TenantA, unlinked, RunStatus.DeadLettered, null));
        Assert.Null((await store.GetReceiptAsync(TenantA, unlinked))!.AccessDecisionId);

        // Progress and authorization evidence stay distinct: the receipt holds the link, the step holds progress.
        Assert.Equal(
            ReceiptFields,
            typeof(ExecutionReceipt).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            TraceStepFields,
            typeof(ExecutionTraceStep).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));

        var blank = Assert.Throws<ExecutionRuntimeRefusedException>(() => new ExecutionReceipt(TenantA, linked, RunStatus.Succeeded, " "));
        Assert.Equal("execution.execution_receipt_invalid", blank.Code);
    }

    [Fact(DisplayName = "execution-runtime-ck-3: a receipt round-trips under its tenant with a stable identity, and another tenant reads nothing")]
    public async Task Receipt_round_trip()
    {
        // Oracle: slice acceptance 1-2 and DES-0056 ck-3 (durable tenant data under the run identity).
        // Mutation: key InMemoryExecutionReceiptStore receipts by run alone (drop the tenant from GetReceiptAsync's key).
        var store = new InMemoryExecutionReceiptStore();
        var run = new RunId(WorkflowRun, Opaque);
        await store.PutReceiptAsync(new ExecutionReceipt(TenantA, run, RunStatus.Running, DecisionId));

        var first = (await store.GetReceiptAsync(TenantA, run))!;
        var second = (await store.GetReceiptAsync(TenantA, run))!;
        Assert.Equal("workflow-run:16300000-0000-0000-0000-000000000163", first.RunId.ToString());
        Assert.Equal(first.RunId, second.RunId);
        Assert.Equal(TenantA, first.TenantId);
        Assert.Equal(RunStatus.Running, first.Status);
        Assert.Null(await store.GetReceiptAsync(TenantB, run));
        Assert.Empty(await store.GetTraceAsync(TenantB, run));

        await store.PutReceiptAsync(new ExecutionReceipt(TenantA, run, RunStatus.Succeeded, DecisionId));
        var updated = (await store.GetReceiptAsync(TenantA, run))!;
        Assert.Equal(RunStatus.Succeeded, updated.Status);
        Assert.Equal(first.RunId, updated.RunId);
        Assert.Equal(DecisionId, updated.AccessDecisionId);
        Assert.Null(await store.GetReceiptAsync(TenantB, run));

        Assert.Equal("execution.execution_receipt_invalid",
            Assert.Throws<ExecutionRuntimeRefusedException>(() => new ExecutionReceipt(TenantA, default, RunStatus.Running, null)).Code);
        Assert.Equal("execution.execution_receipt_invalid",
            Assert.Throws<ExecutionRuntimeRefusedException>(() => new ExecutionReceipt(TenantA, run, (RunStatus)6, null)).Code);
        Assert.Throws<ArgumentException>(() => new ExecutionReceipt(default, run, RunStatus.Running, null));
    }

    [Fact(DisplayName = "execution-runtime-ck-4: trace steps keep their recorded order, only the next ordinal is accepted, and another tenant reads none")]
    public async Task Trace_order()
    {
        // Oracle: slice acceptance 2 and the Layout fixture's 1-based ordinals (submitted, approval, posted).
        // Mutation: drop the `step.Ordinal != steps.Count + 1` refusal in AppendTraceStepAsync.
        var store = new InMemoryExecutionReceiptStore();
        var run = new RunId(WorkflowRun, Opaque);
        await store.AppendTraceStepAsync(TenantA, run, new ExecutionTraceStep(1, "submitted", "succeeded"));
        await store.AppendTraceStepAsync(TenantA, run, new ExecutionTraceStep(2, "approval", "succeeded"));
        await store.AppendTraceStepAsync(TenantA, run, new ExecutionTraceStep(3, "posted", "succeeded"));

        var skipped = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => store.AppendTraceStepAsync(TenantA, run, new ExecutionTraceStep(5, "late", "succeeded")).AsTask());
        Assert.Equal("execution.trace_step_out_of_order", skipped.Code);
        var repeated = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => store.AppendTraceStepAsync(TenantA, run, new ExecutionTraceStep(3, "posted", "failed")).AsTask());
        Assert.Equal("execution.trace_step_out_of_order", repeated.Code);
        var early = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => store.AppendTraceStepAsync(TenantB, run, new ExecutionTraceStep(2, "approval", "succeeded")).AsTask());
        Assert.Equal("execution.trace_step_out_of_order", early.Code);

        var trace = await store.GetTraceAsync(TenantA, run);
        Assert.Equal(new[] { (1, "submitted", "succeeded"), (2, "approval", "succeeded"), (3, "posted", "succeeded") },
            trace.Select(s => (s.Ordinal, s.Phase, s.Status)));
        Assert.Empty(await store.GetTraceAsync(TenantB, run));

        Assert.Equal("execution.trace_step_invalid",
            Assert.Throws<ExecutionRuntimeRefusedException>(() => new ExecutionTraceStep(0, "submitted", "succeeded")).Code);
        Assert.Equal("execution.trace_step_invalid",
            Assert.Throws<ExecutionRuntimeRefusedException>(() => new ExecutionTraceStep(1, " ", "succeeded")).Code);
        Assert.Equal("execution.trace_step_invalid",
            Assert.Throws<ExecutionRuntimeRefusedException>(() => new ExecutionTraceStep(1, "submitted", "")).Code);
        Assert.Equal("execution.trace_step_invalid", (await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => store.AppendTraceStepAsync(TenantA, default, new ExecutionTraceStep(1, "submitted", "succeeded")).AsTask())).Code);
    }

    [Fact(DisplayName = "execution-runtime-eng-1: an engine emits a receipt and trace through the tenant-scoped port, and a read returns a copy the reader cannot change")]
    public async Task Engine_emits_through_port()
    {
        // Oracle: DES-0056 eng-1 (emit a tenant-scoped receipt and trace under the common run identity/status
        // contract), driven through the lifecycle's own run identity and status. Mutation: return the stored
        // List itself from GetTraceAsync instead of a copy.
        var registry = new RunKindRegistry();
        registry.Register(WorkflowRun, "workflows");
        var runs = new InMemoryRunStore();
        var lifecycle = new RunLifecycle(registry, runs, TimeProvider.System);
        var port = new InMemoryExecutionReceiptStore();

        var run = await lifecycle.StartAsync(new StartRun(WorkflowRun, TenantA,
            new CapabilityRetryPolicy(RetryProfileName.ImmediateOrFail, [RetryProfileName.ImmediateOrFail])));
        run = await lifecycle.BeginAttemptAsync(TenantA, run.Id);
        await port.PutReceiptAsync(new ExecutionReceipt(TenantA, run.Id, run.Status, DecisionId));
        await port.AppendTraceStepAsync(TenantA, run.Id, new ExecutionTraceStep(1, "submitted", "succeeded"));

        var read = await port.GetTraceAsync(TenantA, run.Id);
        if (read is IList<ExecutionTraceStep> list && !list.IsReadOnly) list.Clear();
        Assert.Single(await port.GetTraceAsync(TenantA, run.Id));

        run = await lifecycle.SucceedAsync(TenantA, run.Id);
        await port.PutReceiptAsync(new ExecutionReceipt(TenantA, run.Id, run.Status, DecisionId));
        await port.AppendTraceStepAsync(TenantA, run.Id, new ExecutionTraceStep(2, "posted", "succeeded"));

        var receipt = (await port.GetReceiptAsync(TenantA, run.Id))!;
        Assert.Equal(RunStatus.Succeeded, receipt.Status);
        Assert.Equal("succeeded", RunStatusTransitions.WireName(receipt.Status));
        Assert.Equal(run.Id, receipt.RunId);
        Assert.Equal(FirstTwoOrdinals, (await port.GetTraceAsync(TenantA, run.Id)).Select(s => s.Ordinal));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => port.PutReceiptAsync(receipt, cancelled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => port.AppendTraceStepAsync(TenantA, run.Id, new ExecutionTraceStep(3, "x", "y"), cancelled.Token).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => port.GetReceiptAsync(default, run.Id).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => port.GetTraceAsync(default, run.Id).AsTask());
        Assert.Equal(2, (await port.GetTraceAsync(TenantA, run.Id)).Count);
    }

    [Fact(DisplayName = "execution-runtime-cc-1: receipts and traces of two run kinds sharing an opaque value never merge")]
    public async Task Kinds_never_merge()
    {
        // Oracle: DES-0056 cc-1 with cc-5 (identities never merge; the kind is part of the run identity).
        // Mutation: key the in-memory receipt store by RunId.Value (the Guid) instead of the kind-qualified RunId.
        var store = new InMemoryExecutionReceiptStore();
        var workflow = new RunId(WorkflowRun, Opaque);
        var plan = new RunId(PlanRun, Opaque);
        await store.PutReceiptAsync(new ExecutionReceipt(TenantA, workflow, RunStatus.Succeeded, DecisionId));
        await store.PutReceiptAsync(new ExecutionReceipt(TenantA, plan, RunStatus.Cancelled, null));
        await store.AppendTraceStepAsync(TenantA, workflow, new ExecutionTraceStep(1, "submitted", "succeeded"));
        await store.AppendTraceStepAsync(TenantA, plan, new ExecutionTraceStep(1, "solve", "cancelled"));

        var workflowReceipt = (await store.GetReceiptAsync(TenantA, workflow))!;
        var planReceipt = (await store.GetReceiptAsync(TenantA, plan))!;
        Assert.Equal((RunStatus.Succeeded, DecisionId), (workflowReceipt.Status, workflowReceipt.AccessDecisionId));
        Assert.Equal((RunStatus.Cancelled, (string?)null), (planReceipt.Status, planReceipt.AccessDecisionId));
        Assert.Equal("solve", Assert.Single(await store.GetTraceAsync(TenantA, plan)).Phase);
        Assert.Equal("submitted", Assert.Single(await store.GetTraceAsync(TenantA, workflow)).Phase);
    }
}
