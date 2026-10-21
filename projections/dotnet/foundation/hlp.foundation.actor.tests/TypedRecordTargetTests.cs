using System.Text.Json.Nodes;
using Harborline.Foundation.Authorization;
using Xunit;

namespace Harborline.Foundation.Authorization.Tests;

/// <summary>
/// Plan W9 (C2), revision 5: an access request may name a typed Records record-type target, so the sole decider can
/// decide an act on one Record Type (decision D1, a role-gated type's creation role). The type is a context field like
/// the record kind and id: a blank one never reaches the decider, and evidence naming another type, or dropping or adding
/// one, is withheld and refuses <c>access.decision_mismatch</c>. The trace's four steps are unchanged.
/// Oracles are literals; the fixture decider freezes whatever request it is told to answer and computes no verdict.
/// </summary>
public sealed class TypedRecordTargetTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-11T09:00:00Z");

    [Fact]
    public async Task A_typed_request_reaches_the_decider_and_its_evidence_names_the_same_type()
    {
        var gate = new AnsweringGate();
        var request = Typed("eam.work-order");

        var result = await new AccessProvider(gate).CheckWithEvidenceAsync(request);

        Assert.True(result.Check.Allowed);
        Assert.Equal("access.allowed", result.Check.Reason);
        var asked = Assert.Single(gate.Asked);
        Assert.Equal("eam.work-order", asked.Record.RecordTypeId);
        var evidence = Assert.IsType<AuthorizationDecisionEvidence>(result.Evidence);
        Assert.Equal("eam.work-order", evidence.RecordTypeId);
        Assert.Equal("record", evidence.RecordKind);
        Assert.Equal("r-1", evidence.RecordId);
    }

    [Fact]
    public async Task An_untyped_request_keeps_a_null_type_on_both_sides()
    {
        var gate = new AnsweringGate();
        var request = Untyped();

        var result = await new AccessProvider(gate).CheckWithEvidenceAsync(request);

        Assert.True(result.Check.Allowed);
        Assert.Null(Assert.Single(gate.Asked).Record.RecordTypeId);
        Assert.Null(result.Evidence!.RecordTypeId);
    }

    public static TheoryData<string?, string?> MismatchedTypes => new()
    {
        { "eam.work-order", "eam.asset" },
        { "eam.work-order", null },
        { null, "eam.work-order" },
        { "eam.work-order", "EAM.WORK-ORDER" },
        { "eam.work-order", "eam.work-order " },
    };

    [Theory]
    [MemberData(nameof(MismatchedTypes))]
    public async Task Evidence_naming_another_record_type_is_withheld_and_refuses(string? asked, string? answered)
    {
        var request = asked is null ? Untyped() : Typed(asked);
        var answeredRequest = request with { Record = request.Record with { RecordTypeId = answered } };
        var gate = new AnsweringGate(_ => answeredRequest);

        var result = await new AccessProvider(gate).CheckWithEvidenceAsync(request);

        Assert.False(result.Check.Allowed);
        Assert.Equal("access.decision_mismatch", result.Check.Reason);
        Assert.Null(result.Evidence);
        Assert.Single(gate.Asked);
        Assert.Equal("access.decision_mismatch", (await new AccessProvider(gate).CheckAsync(request)).Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task A_blank_typed_target_is_an_invalid_context_and_never_reaches_the_decider(string blank)
    {
        var gate = new AnsweringGate();

        var result = await new AccessProvider(gate).CheckWithEvidenceAsync(Typed(blank));

        Assert.False(result.Check.Allowed);
        Assert.Equal("access.context_invalid", result.Check.Reason);
        Assert.Null(result.Evidence);
        Assert.Empty(gate.Asked);
    }

    [Fact]
    public async Task A_typed_denial_keeps_the_gates_reason_and_its_evidence()
    {
        var gate = new AnsweringGate(allowed: false);

        var result = await new AccessProvider(gate).CheckWithEvidenceAsync(Typed("eam.work-order"));

        Assert.False(result.Check.Allowed);
        Assert.Equal("NoEffectiveRole", result.Check.Reason);
        Assert.Equal("eam.work-order", result.Evidence!.RecordTypeId);
        Assert.False(result.Evidence.Allowed);
    }

    [Fact]
    public void The_type_adds_no_trace_fact_so_the_four_steps_are_unchanged()
    {
        var typed = new AuthorizationDecisionEvidence(Typed("eam.work-order"), true, "None", "grant:1", [], []);
        var untyped = new AuthorizationDecisionEvidence(Untyped(), true, "None", "grant:1", [], []);

        Assert.Equal(["kind:Gate", "act:records:write", "target:record/r-1", "principal:alice", "tenant:a",
            "at:2026-10-11T09:00:00.0000000+00:00"], typed.Project()[0].Facts.ToArray());
        Assert.Equal(
            untyped.Project().Select(step => (step.Ordinal, step.Stage, string.Join("|", step.Facts))),
            typed.Project().Select(step => (step.Ordinal, step.Stage, string.Join("|", step.Facts))));
    }

    [Fact]
    public void Records_with_and_without_the_same_type_are_distinct_values()
    {
        var plain = Untyped().Record;

        Assert.NotEqual(plain, plain with { RecordTypeId = "eam.work-order" });
        Assert.Equal(plain with { RecordTypeId = "eam.work-order" }, plain with { RecordTypeId = "eam.work-order" });
    }

    private static AccessRequest Untyped() =>
        new("records:write", "alice", "a", new("a", "record", "r-1", new Dictionary<string, JsonNode?>()), At);

    private static AccessRequest Typed(string recordTypeId)
    {
        var request = Untyped();
        return request with { Record = request.Record with { RecordTypeId = recordTypeId } };
    }

    // Stands in for the host's adapter; it freezes the request it is told to answer and decides nothing.
    private sealed class AnsweringGate(Func<AccessRequest, AccessRequest>? answerFor = null, bool allowed = true) : IAuthorizationDecider
    {
        public List<AccessRequest> Asked { get; } = [];

        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            Asked.Add(request);
            return ValueTask.FromResult(new AuthorizationDecisionEvidence(answerFor?.Invoke(request) ?? request, allowed,
                allowed ? "None" : "NoEffectiveRole", "grant:1", [], []));
        }
    }
}
