using Harborline.Foundation.Authorization;
using Xunit;

namespace Harborline.Foundation.Authorization.Tests;

public sealed class AccessProviderTests
{
    [Fact]
    public async Task Validation_rechecks_at_predicate_instant_after_stage_one_allow()
    {
        var at = DateTimeOffset.Parse("2026-09-18T12:00:00Z");
        var request = new AccessRequest("records:write", "alice", "a", new("a", "work", "1", new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), at);
        var gate = new RevocableGate();
        var access = new AccessProvider(gate);
        Assert.True((await access.CheckAsync(request)).Allowed);
        gate.Revoked = true;
        var committed = false;
        var validated = false;
        var result = await access.ValidateAsync(request with { At = at.AddSeconds(1) }, (instant, ct) =>
        {
            validated = true;
            Assert.Equal(at.AddSeconds(1), instant);
            return ValueTask.FromResult(new AccessCheck(true, "valid"));
        });
        if (result.Allowed) committed = true;
        Assert.False(committed);
        Assert.False(validated);
        Assert.Equal("permission_revoked", result.Reason);
        Assert.Equal(at.AddSeconds(1), gate.LastInstant);
    }

    [Fact]
    public async Task Cross_tenant_records_refuse_without_calling_the_decider()
    {
        var gate = new RevocableGate();
        var request = new AccessRequest("records:read", "alice", "a", new("b", "work", "1",
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), DateTimeOffset.UtcNow);
        Assert.Equal("access.context_invalid", (await new AccessProvider(gate).CheckAsync(request)).Reason);
        Assert.Equal(default, gate.LastInstant);
    }

    [Fact]
    public async Task Evidence_for_another_record_cannot_authorize_the_requested_record()
    {
        var at = DateTimeOffset.Parse("2026-09-18T12:00:00Z");
        var request = new AccessRequest("records:read", "alice", "a", new("a", "work", "requested",
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), at);
        var otherRecord = request with { Record = request.Record with { Id = "other" } };
        var evidence = new AuthorizationDecisionEvidence(otherRecord, true, "None", "grant:1", [], []);

        var result = await new AccessProvider(new FixedEvidenceGate(evidence)).CheckAsync(request);

        Assert.False(result.Allowed);
        Assert.Equal("access.decision_mismatch", result.Reason);
    }

    [Fact]
    public async Task Evidence_must_match_every_requested_context_field()
    {
        var request = new AccessRequest("records:read", "alice", "a", new("a", "work", "1",
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), DateTimeOffset.Parse("2026-09-18T12:00:00Z"));
        AccessRequest[] otherContexts =
        [
            request with { Principal = "bob" },
            request with { Tenant = "b" },
            request with { At = request.At.AddSeconds(1) },
            request with { Operation = "records:write" },
            request with { Record = request.Record with { Kind = "other" } },
        ];

        foreach (var context in otherContexts)
        {
            var evidence = new AuthorizationDecisionEvidence(context, true, "None", "grant:1", [], []);
            var result = await new AccessProvider(new FixedEvidenceGate(evidence)).CheckAsync(request);
            Assert.False(result.Allowed);
            Assert.Equal("access.decision_mismatch", result.Reason);
        }
    }

    [Fact]
    public async Task Missing_request_context_refuses_without_consulting_the_decider()
    {
        var request = new AccessRequest("records:read", "alice", "a", new("a", "work", "1",
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), DateTimeOffset.Parse("2026-09-18T12:00:00Z"));
        AccessRequest[] invalidRequests =
        [
            request with { Principal = " " },
            request with { Tenant = " " },
            request with { Operation = " " },
            request with { Record = request.Record with { Id = " " } },
            request with { Record = request.Record with { Kind = " " } },
        ];

        var gate = new RevocableGate();
        foreach (var invalid in invalidRequests)
        {
            var result = await new AccessProvider(gate).CheckAsync(invalid);
            Assert.False(result.Allowed);
            Assert.Equal("access.context_invalid", result.Reason);
            Assert.Equal(default, gate.LastInstant);
        }
    }

    [Fact]
    public async Task Bound_predicate_refuses_a_different_record_kind_without_consulting_the_decider()
    {
        var gate = new RevocableGate();
        var predicate = new AccessProvider(gate).Bind("records:read", "alice", "a", "work", DateTimeOffset.UtcNow);
        var record = new AccessRecord("a", "other", "1", new Dictionary<string, System.Text.Json.Nodes.JsonNode?>());

        var result = await predicate.CheckAsync(record);

        Assert.False(result.Allowed);
        Assert.Equal("access.record_kind_mismatch", result.Reason);
        Assert.Equal(default, gate.LastInstant);
    }

    [Fact]
    public async Task Bound_filter_honors_cancellation_even_when_a_record_kind_does_not_match()
    {
        var predicate = new AccessProvider(new RevocableGate()).Bind("records:read", "alice", "a", "work", DateTimeOffset.UtcNow);
        var record = new AccessRecord("a", "other", "1", new Dictionary<string, System.Text.Json.Nodes.JsonNode?>());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await predicate.FilterAsync([record], row => row, cancellation.Token));
    }

    [Fact]
    public async Task Row_check_rejects_null_request_and_cancellation_before_the_decider()
    {
        var at = DateTimeOffset.UtcNow;
        var request = new AccessRequest("records:read", "alice", "a", new("a", "work", "1",
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), at);
        var gate = new RevocableGate();
        var access = new AccessProvider(gate);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await access.CheckAsync(null!));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await access.CheckAsync(request, cancellation.Token));
        Assert.Equal(default, gate.LastInstant);
    }

    [Fact]
    public async Task Allowed_row_check_reports_allowed_reason_and_validation_requires_a_callback()
    {
        var request = new AccessRequest("records:read", "alice", "a", new("a", "work", "1",
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), DateTimeOffset.UtcNow);
        var access = new AccessProvider(new RevocableGate());

        var result = await access.CheckAsync(request);

        Assert.True(result.Allowed);
        Assert.Equal("access.allowed", result.Reason);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await access.ValidateAsync(request, null!));
    }

    [Fact]
    public async Task Allowed_control_validates_once_at_the_check_instant_and_preserves_validation_refusal()
    {
        var gate = new RevocableGate();
        var at = DateTimeOffset.Parse("2026-09-18T12:00:00Z");
        var request = new AccessRequest("records:write", "alice", "a", new("a", "work", "1",
            new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), at);
        var validations = 0;
        var result = await new AccessProvider(gate).ValidateAsync(request, (instant, _) =>
        {
            validations++;
            Assert.Equal(gate.LastInstant, instant);
            return ValueTask.FromResult(new AccessCheck(false, "record.invalid"));
        });
        Assert.Equal(1, validations);
        Assert.False(result.Allowed);
        Assert.Equal("record.invalid", result.Reason);
    }

    // External host gate seam: this test changes its verdict between the two calls.
    private sealed class RevocableGate : IAuthorizationDecider
    {
        public bool Revoked { get; set; }
        public DateTimeOffset LastInstant { get; private set; }
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            LastInstant = request.At;
            return ValueTask.FromResult(new AuthorizationDecisionEvidence(request, !Revoked,
                Revoked ? "permission_revoked" : "None", "grant:1", [], []));
        }
    }

    private sealed class FixedEvidenceGate(AuthorizationDecisionEvidence evidence) : IAuthorizationDecider
    {
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(evidence);
    }
}
