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
}
