using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class RetentionConformanceTests
{
    [Fact]
    public async Task Lifecycle_policy_derives_retention_and_legal_hold_prevents_disposition()
    {
        var runs = new InMemoryExchangeRunStore();
        var policy = new FakeLifecyclePolicy { Retention = new(DateTimeOffset.UnixEpoch, false) };
        var runtime = new DataExchangeRuntime(runs, TimeProvider.System, policy);
        var review = await runtime.CreateDryRunAsync(Fixtures.DryRunRequest());
        Assert.Equal(policy.Retention.RetainUntil, review.RetainUntil);
        Assert.Equal((review.TenantId, "standard-7y", review.RequestedAt), Assert.Single(policy.Calls));
        Assert.True(await runtime.CanDisposeDryRunAsync(review.Id));
        policy.Retention = policy.Retention with { LegalHold = true };
        Assert.False(await runtime.CanDisposeDryRunAsync(review.Id));
        var held = await runtime.CreateDryRunAsync(Fixtures.DryRunRequest());
        Assert.True(held.LegalHold);
        policy.Retention = policy.Retention with { LegalHold = false };
        Assert.False(await runtime.CanDisposeDryRunAsync(held.Id));
    }

    [Fact]
    public void Caller_supplied_retain_until_is_refused()
    {
        var input = JsonSerializer.SerializeToNode(Fixtures.DryRunRequest())!.AsObject();
        input["RetainUntil"] = "2000-01-01T00:00:00Z";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DryRunRequest>(input.ToJsonString()));
    }
}
