using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// The CP separation-of-duties gate for the human-action approve funnel (ADR 0143 D-INV-5) — the .NET
/// mirror of the carrier-sdk broker's confirm-side check, pinned into the code floor
/// (<see cref="HumanApprovalHandlerBase.EnsureApproverSeparationOfDuties"/>). The reconciled rule
/// matches the TS broker + the security-engineering F2 amendment:
///   (1) a CP confirmation MUST be made by a HUMAN principal (an agent may propose, only a human confirms);
///   (2) an AGENT-proposed CP op requires a DISTINCT confirmer — but a HUMAN proposer MAY self-confirm on
///       the single-user desktop (the accountable-human compensating control §4.4).
/// </summary>
public sealed class WorkflowApprovalSeparationOfDutiesTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Agent = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static void Ensure(Guid proposer, bool proposerHuman, Guid confirmer, bool confirmerHuman)
        => HumanApprovalHandlerBase.EnsureApproverSeparationOfDuties(
            proposer, proposerHuman, confirmer, confirmerHuman, "approve");

    [Fact]
    public void Rejects_a_non_human_confirmer_even_for_a_human_proposed_op()
    {
        // A genuine CP confirm is human — an agent/service principal can never confirm.
        var ex = Assert.Throws<WorkflowSodViolationException>(
            () => Ensure(Alice, proposerHuman: true, Agent, confirmerHuman: false));
        Assert.Equal(WorkflowApprovalCodes.SeparationOfDutiesViolation, ex.Code);
    }

    [Fact]
    public void Rejects_an_agent_self_approval_confirmer_equals_proposer_both_non_human()
    {
        // The shipped hole (F2 / red-team Chain 3): an agent proposes then confirms its OWN CP op.
        var ex = Assert.Throws<WorkflowSodViolationException>(
            () => Ensure(Agent, proposerHuman: false, Agent, confirmerHuman: false));
        Assert.Equal(WorkflowApprovalCodes.SeparationOfDutiesViolation, ex.Code);
    }

    [Fact]
    public void Admits_a_valid_distinct_human_confirmation()
        => Ensure(Alice, proposerHuman: true, Bob, confirmerHuman: true); // must not throw

    [Fact]
    public void Admits_a_human_self_confirm_on_the_single_user_desktop()
        // Harborline App's shipped single-user flow: one human proposes AND confirms — the accountable-human
        // compensating control (§4.4). Strict actor≠approver would break this; it must stay allowed.
        => Ensure(Alice, proposerHuman: true, Alice, confirmerHuman: true); // must not throw

    [Fact]
    public void Admits_an_agent_proposed_op_confirmed_by_a_distinct_human_the_on_the_loop_guard()
        => Ensure(Agent, proposerHuman: false, Alice, confirmerHuman: true); // must not throw
}
