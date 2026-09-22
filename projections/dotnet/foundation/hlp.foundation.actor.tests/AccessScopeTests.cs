using System.Text.Json.Nodes;
using Harborline.Foundation.Authorization;
using Xunit;

namespace Harborline.Foundation.Authorization.Tests;

public sealed class AccessScopeTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-18T12:00:00Z");

    [Fact]
    public void Scope_requires_exact_authority_and_declared_references_before_evaluation()
    {
        var request = new AccessRequest("records:read", "alice", "tenant-a",
            new AccessRecord("tenant-a", "work", "1", new Dictionary<string, JsonNode?> { ["owner"] = JsonValue.Create("alice") }), At);
        var expression = "{\"==\":[{\"var\":\"record.owner\"},{\"var\":\"principal\"}]}";
        var evaluations = 0;
        var evaluator = new AccessScopeEvaluator(at =>
        {
            evaluations++;
            Assert.Equal(At, at);
            return new Harborline.Foundation.RuleEngine.GuardEvaluator(new FixedTimeProvider(at));
        });
        var authority = new AccessAuthorityContext("alice", "tenant-a", "work", "1", At, ["record.owner", "principal"]);
        Assert.Equal("access.authority_missing", evaluator.Evaluate(expression, request, null).Reason);
        Assert.Equal("access.authority_mismatch", evaluator.Evaluate(expression, request with { Tenant = "tenant-b" }, authority).Reason);
        Assert.Equal("access.reference_undeclared", evaluator.Evaluate(expression, request,
            new("alice", "tenant-a", "work", "1", At, ["principal"])).Reason);
        Assert.Equal(0, evaluations);
        Assert.True(evaluator.Evaluate(expression, request, authority).Allowed);
        Assert.Equal(1, evaluations);
    }

    [Theory]
    [InlineData("{\"or\":[true,{\"var\":\"record.secret\"}]}")]
    [InlineData("{\"var\":[{\"var\":\"principal\"},true]}")]
    [InlineData("{\"agg\":[\"sum\",\"otherTenant\",\"amount\"]}")]
    public void Undeclared_even_unreachable_or_dynamic_references_never_evaluate(string expression)
    {
        var request = new AccessRequest("records:read", "alice", "a", new("a", "work", "1", new Dictionary<string, JsonNode?>()), At);
        var evaluator = new AccessScopeEvaluator(_ => throw new InvalidOperationException("must refuse before evaluation"));
        Assert.Equal("access.reference_undeclared", evaluator.Evaluate(expression, request,
            new("alice", "a", "work", "1", At, ["principal"])).Reason);
    }
}

file sealed class FixedTimeProvider(DateTimeOffset instant) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => instant;
}
