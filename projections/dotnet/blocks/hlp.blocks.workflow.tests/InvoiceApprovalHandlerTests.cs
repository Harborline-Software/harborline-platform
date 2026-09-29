using System.Text.Json;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// ADR 0135 slice 2 — Handler A (<c>invoice &gt; $5k → approve → post</c>) UNIT coverage at the
/// blocks-workflow seam (no financial dependency — the post effect + amount + preview come from a fake
/// <see cref="IInvoiceApprovalContext"/>). Asserts the threshold branch, the CP-park-by-name invariant + the
/// FE-1 basis payload, the typed human-action outcomes, and the D7 pinned-version evaluation.
/// </summary>
public sealed class InvoiceApprovalHandlerTests
{
    // The v1 threshold table: two effective-dated versions. The pinned version is what an instance evaluates.
    private static ThresholdDecisionTable Table() => new(new[]
    {
        new ThresholdDecisionTableVersion
        {
            Version = "2026-06-23.1",
            EffectiveFrom = new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero),
            Rows = new[]
            {
                new ThresholdDecisionRow("under-5k", 0m, ApprovalDecision.AutoApprove),
                new ThresholdDecisionRow("over-5k", 5000.01m, ApprovalDecision.RequireApproval),
            },
        },
        // A LATER version that raises the threshold to $10k — proves an in-flight instance pinned to .1 is
        // NOT retroactively moved (D7).
        new ThresholdDecisionTableVersion
        {
            Version = "2026-09-01.1",
            EffectiveFrom = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            Rows = new[]
            {
                new ThresholdDecisionRow("under-10k", 0m, ApprovalDecision.AutoApprove),
                new ThresholdDecisionRow("over-10k", 10000.01m, ApprovalDecision.RequireApproval),
            },
        },
    });

    private sealed class FakeContext : IInvoiceApprovalContext
    {
        public required decimal Amount { get; init; }
        public bool PostEffectBuilt { get; private set; }
        public WorkflowDispatchAuthority? ReceivedDecision { get; private set; }

        public decimal GetInvoiceAmount(WorkflowInstanceRecord instance) => Amount;
        public DateTimeOffset GetBusinessTime(WorkflowInstanceRecord instance) => DateTimeOffset.UnixEpoch;

        public WorkflowEffect BuildPostEffect(
            WorkflowInstanceRecord instance, WorkflowStepKey postStepKey, WorkflowDispatchAuthority? admittedDecision)
        {
            PostEffectBuilt = true;
            ReceivedDecision = admittedDecision;
            return new WorkflowEffect((_, _) => Task.CompletedTask);
        }

        public string RenderPostingPreview(WorkflowInstanceRecord instance, decimal amount)
            => $"Debit AR {amount}; Credit Income {amount}";
    }

    private static WorkflowInstanceRecord Instance(
        string pinnedVersion = "2026-06-23.1",
        string currentStep = InvoiceApprovalSteps.Decide) => new()
    {
        Id = "inst-A",
        TenantId = "t",
        DefinitionKey = InvoiceApprovalSteps.DefinitionKey,
        DefinitionVersion = pinnedVersion,
        CurrentStep = currentStep,
        Status = WorkflowStatus.Running,
    };

    [Fact(DisplayName = "Handler A: an UNDER-threshold invoice auto-posts directly (Complete with effect, no park)")]
    public async Task UnderThreshold_AutoPosts()
    {
        var ctx = new FakeContext { Amount = 1000m };
        var handler = new InvoiceApprovalHandler(Table(), ctx);

        var outcome = await handler.DecideAsync(
            Instance(), WorkflowTrigger.For(WorkflowTriggerKind.Event, "inst-A", InvoiceApprovalSteps.Decide));

        Assert.Equal(WorkflowStepOutcomeKind.Advance, outcome.Kind);
        Assert.Equal(WorkflowStatus.Completed, outcome.NextStatus);
        Assert.Equal(InvoiceApprovalSteps.Posted, outcome.NextStep);
        Assert.NotNull(outcome.Effect);          // the post effect is attached (auto-post)
        Assert.True(ctx.PostEffectBuilt);
    }

    [Fact(DisplayName = "Handler A (CP-park BY NAME): an OVER-threshold invoice PARKS on the approve human-task — it does NOT auto-reach post; the park carries the FE-1 basis (preview + fired row/version)")]
    public async Task OverThreshold_ParksHumanTask_WithBasisPayload()
    {
        var ctx = new FakeContext { Amount = 7500m };
        var handler = new InvoiceApprovalHandler(Table(), ctx);

        var outcome = await handler.DecideAsync(
            Instance(), WorkflowTrigger.For(WorkflowTriggerKind.Event, "inst-A", InvoiceApprovalSteps.Decide));

        // CP-park invariant: the outcome is a PARK on the approve human-task — NOT an advance to post.
        Assert.Equal(WorkflowStepOutcomeKind.Park, outcome.Kind);
        Assert.Equal(InvoiceApprovalSteps.Approve, outcome.NextStep);
        Assert.Null(outcome.Effect);             // NO effect is staged on a park (the post is not reached)
        Assert.False(ctx.PostEffectBuilt);       // the post effect was never built over threshold

        // FE-1 basis payload: the park reason carries the posting preview + the fired decision row + version.
        using var basis = JsonDocument.Parse(outcome.EventDataJson);
        var root = basis.RootElement;
        Assert.Equal("cp-approval-basis", root.GetProperty("kind").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("postingPreview").GetString()));
        var decision = root.GetProperty("decision");
        Assert.Equal("2026-06-23.1", decision.GetProperty("version").GetString());   // D7 pinned version
        Assert.Equal("over-5k", decision.GetProperty("row").GetString());            // the row that fired
        Assert.Equal("RequireApproval", decision.GetProperty("outcome").GetString());
        // The typed outcome set is present for the confirm surface.
        var outcomes = root.GetProperty("typedOutcomes").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "approve", "reject", "send-back" }, outcomes);
    }

    // The host gate's ledger:post decision for the approve act (T-525): tenant "t" is Instance()'s tenant.
    private static readonly WorkflowDispatchAuthority LedgerPost = new(
        "user:approver", "t", InvoiceApprovalHandler.PostOperation, InvoiceApprovalHandler.PostRecordKind,
        "je-for-inst-A", Allowed: true);

    private static WorkflowTrigger ApproveTrigger(WorkflowDispatchAuthority? decision) =>
        WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "inst-A", InvoiceApprovalSteps.Approve,
            "{\"decision\":\"approve\"}") with { AdmittedDecision = decision };

    [Fact(DisplayName = "Handler A (T-525): approve with no ledger:post decision is refused and builds no post effect")]
    public async Task Approve_WithoutAnAdmittedDecision_IsRefused()
    {
        var ctx = new FakeContext { Amount = 7500m };
        var handler = new InvoiceApprovalHandler(Table(), ctx);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await handler.DecideAsync(
            Instance(currentStep: InvoiceApprovalSteps.Approve), ApproveTrigger(null)));

        Assert.False(ctx.PostEffectBuilt);
    }

    [Theory(DisplayName = "Handler A (T-525): approve under a decision that does not admit the post is refused and builds no post effect")]
    [InlineData("denied")]
    [InlineData("other-tenant")]
    [InlineData("records-write")]
    [InlineData("other-record-kind")]
    [InlineData("no-principal")]
    public async Task Approve_UnderADecisionThatDoesNotAdmitThePost_IsRefused(string variant)
    {
        var ctx = new FakeContext { Amount = 7500m };
        var handler = new InvoiceApprovalHandler(Table(), ctx);
        var decision = variant switch
        {
            "denied" => LedgerPost with { Allowed = false },
            "other-tenant" => LedgerPost with { Tenant = "tenant:zenith" },
            "records-write" => LedgerPost with { Operation = WorkflowDispatchAuthority.RequiredOperation },
            "other-record-kind" => LedgerPost with { RecordKind = WorkflowDispatchAuthority.RequiredRecordKind },
            _ => LedgerPost with { Principal = " " },
        };

        var refusal = await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await handler.DecideAsync(
            Instance(currentStep: InvoiceApprovalSteps.Approve), ApproveTrigger(decision)));

        Assert.Contains("'inst-A'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'ledger:post' decision", refusal.Message, StringComparison.Ordinal);
        Assert.False(ctx.PostEffectBuilt);
    }

    [Fact(DisplayName = "Handler A (T-525): approve under an allowed ledger:post decision posts and hands that decision to the host effect")]
    public async Task Approve_UnderAnAllowedLedgerPostDecision_HandsTheDecisionToTheEffect()
    {
        var ctx = new FakeContext { Amount = 7500m };
        var handler = new InvoiceApprovalHandler(Table(), ctx);

        var outcome = await handler.DecideAsync(Instance(currentStep: InvoiceApprovalSteps.Approve), ApproveTrigger(LedgerPost));

        Assert.Equal(InvoiceApprovalSteps.Posted, outcome.NextStep);
        Assert.Same(LedgerPost, ctx.ReceivedDecision);
    }

    [Fact(DisplayName = "Handler A (T-525): reject needs no ledger:post decision — it performs no post")]
    public async Task Reject_WithoutAnAdmittedDecision_Completes()
    {
        var ctx = new FakeContext { Amount = 7500m };
        var handler = new InvoiceApprovalHandler(Table(), ctx);

        var outcome = await handler.DecideAsync(
            Instance(currentStep: InvoiceApprovalSteps.Approve),
            WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "inst-A", InvoiceApprovalSteps.Approve,
                "{\"decision\":\"reject\"}"));

        Assert.Equal(InvoiceApprovalSteps.Rejected, outcome.NextStep);
        Assert.False(ctx.PostEffectBuilt);
    }

    [Fact(DisplayName = "Handler A: approve on the parked human-task advances to the CP post step WITH the effect (the human approval is the ONLY path to post over threshold)")]
    public async Task Approve_AdvancesToPost_WithEffect()
    {
        var ctx = new FakeContext { Amount = 7500m };
        var handler = new InvoiceApprovalHandler(Table(), ctx);

        var outcome = await handler.DecideAsync(
            Instance(currentStep: InvoiceApprovalSteps.Approve), ApproveTrigger(LedgerPost));

        Assert.Equal(WorkflowStepOutcomeKind.Advance, outcome.Kind);
        Assert.Equal(WorkflowStatus.Completed, outcome.NextStatus);
        Assert.Equal(InvoiceApprovalSteps.Posted, outcome.NextStep);
        Assert.NotNull(outcome.Effect);
        Assert.True(ctx.PostEffectBuilt);
    }

    [Fact(DisplayName = "Handler A: reject on the parked human-task terminates to `rejected` with NO effect (no post)")]
    public async Task Reject_NoPost()
    {
        var ctx = new FakeContext { Amount = 7500m };
        var handler = new InvoiceApprovalHandler(Table(), ctx);

        var outcome = await handler.DecideAsync(
            Instance(currentStep: InvoiceApprovalSteps.Approve),
            WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "inst-A", InvoiceApprovalSteps.Approve,
                "{\"decision\":\"reject\"}"));

        Assert.Equal(WorkflowStatus.Completed, outcome.NextStatus);
        Assert.Equal(InvoiceApprovalSteps.Rejected, outcome.NextStep);
        Assert.Null(outcome.Effect);             // NO post on reject
        Assert.False(ctx.PostEffectBuilt);
    }

    [Fact(DisplayName = "Handler A: send-back on the parked human-task parks back on decide (round-trip)")]
    public async Task SendBack_ParksOnDecide()
    {
        var ctx = new FakeContext { Amount = 7500m };
        var handler = new InvoiceApprovalHandler(Table(), ctx);

        var outcome = await handler.DecideAsync(
            Instance(currentStep: InvoiceApprovalSteps.Approve),
            WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "inst-A", InvoiceApprovalSteps.Approve,
                "{\"decision\":\"send-back\"}"));

        Assert.Equal(WorkflowStepOutcomeKind.Park, outcome.Kind);
        Assert.Equal(InvoiceApprovalSteps.Decide, outcome.NextStep);
    }

    [Fact(DisplayName = "Handler A (D7): a $7.5k invoice pinned to the LATER 10k version auto-posts — the pinned version, not the as-of-now one, decides (deterministic replay)")]
    public async Task D7_PinnedLaterVersion_AutoPostsAt7500()
    {
        var ctx = new FakeContext { Amount = 7500m };
        var handler = new InvoiceApprovalHandler(Table(), ctx);

        // Same $7.5k that PARKED under the .1 (5k) version auto-posts under the pinned 10k version — proving
        // the handler evaluates the PINNED version (D7), not whichever is newest.
        var outcome = await handler.DecideAsync(
            Instance(pinnedVersion: "2026-09-01.1"),
            WorkflowTrigger.For(WorkflowTriggerKind.Event, "inst-A", InvoiceApprovalSteps.Decide));

        Assert.Equal(WorkflowStepOutcomeKind.Advance, outcome.Kind);
        Assert.Equal(InvoiceApprovalSteps.Posted, outcome.NextStep);
        Assert.NotNull(outcome.Effect);
    }
}
