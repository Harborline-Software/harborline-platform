using System.Text.Json.Nodes;
using Harborline.Foundation.Authorization;
using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

public sealed class AccessViewFilterTests
{
    [Fact]
    public async Task Production_filter_excludes_interleaved_hidden_rows_before_count_group_sum_and_page()
    {
        var instant = DateTimeOffset.Parse("2026-09-18T12:00:00Z");
        var gate = new FixtureRecordGate();
        var filter = await new AccessViewFilter(new AccessProvider(gate), "records:read")
            .BuildAsync("a", "alice", "work", instant);
        ViewRow[] rows = [Row("1", "alice", 10), Row("2", "bob", 1000), Row("3", "alice", 20), Row("4", "bob", 2000), Row("5", "alice", 30)];
        var source = new InMemoryViewRowSource(rows);
        var plan = new ViewQueryPlan("a", "work", new Dictionary<string, ViewRecordFieldKind>(), [],
            [new(ViewPredicateSource.Access, filter)], [], "owner", null, new(1, 2), instant);
        var page = await source.QueryAsync(plan);
        Assert.Equal(3, page.Total);
        Assert.Equal(new[] { "3", "5" }, page.Rows.Select(row => row.Id));
        Assert.Equal(60, page.CurrentRows.Sum(row => (int)row.Values["amount"]!));
        Assert.Equal("alice", Assert.Single(page.Groups).Key);
        Assert.Equal(3, page.Groups[0].Count);
        Assert.All(gate.Instants, at => Assert.Equal(instant, at));
    }

    private static ViewRow Row(string id, string owner, int amount) => new(id,
        new Dictionary<string, object?> { ["owner"] = owner, ["amount"] = amount });

    // Host boundary fixture; exercises the existing scope evaluator rather than an allow-all provider.
    private sealed class FixtureRecordGate : IAuthorizationDecider
    {
        public List<DateTimeOffset> Instants { get; } = [];
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            Instants.Add(request.At);
            var scope = new AccessScopeEvaluator().Evaluate("{\"==\":[{\"var\":\"record.owner\"},{\"var\":\"principal\"}]}", request,
                new(request.Principal, request.Tenant, request.Record.Kind, request.Record.Id, request.At, ["record.owner", "principal"]));
            return ValueTask.FromResult(new AuthorizationDecisionEvidence(request, scope.Allowed, scope.Reason, "grant:owner", [], []));
        }
    }
}
