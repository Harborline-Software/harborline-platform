using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ExecutionRuntime.Tests;

public sealed class CapabilityRetryPolicyIsolationTests
{
    private static readonly TenantId Tenant = new() { Value = "retry-policy-tenant" };
    private static readonly RunKind Workflow = new("workflow-run");
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Changing_the_original_input_cannot_rewrite_the_declared_policy()
    {
        var input = new HashSet<RetryProfileName> { RetryProfileName.ExternalApiStandard };
        var policy = new CapabilityRetryPolicy(RetryProfileName.ExternalApiStandard, input);

        Assert.True(input.Add(RetryProfileName.DurableDelivery));
        Assert.True(input.Remove(RetryProfileName.ExternalApiStandard));
        Assert.Equal("durable-delivery", Assert.Single(input).Value);
        AssertDeclaredPolicy(policy);

        input.Clear();
        Assert.Empty(input);
        AssertDeclaredPolicy(policy);
    }

    // DES-0056 execution-runtime-ck-8 and the module contract: selection stays within
    // the capability's declaration. The expected names and refusal are literal oracles.
    [Theory]
    [InlineData("ISet.Add")]
    [InlineData("ISet.Remove")]
    [InlineData("ISet.UnionWith")]
    [InlineData("ISet.IntersectWith")]
    [InlineData("ISet.ExceptWith")]
    [InlineData("ISet.SymmetricExceptWith")]
    [InlineData("ICollection.Add")]
    [InlineData("ICollection.Remove")]
    [InlineData("ICollection.Clear")]
    public async Task Changing_an_exposed_collection_cannot_change_real_run_admission(string operation)
    {
        var policy = new CapabilityRetryPolicy(
            RetryProfileName.ExternalApiStandard, [RetryProfileName.ExternalApiStandard]);
        TryMutation(policy.Allowed, operation);
        var registry = new RunKindRegistry();
        registry.Register(Workflow, "workflows");
        var store = new InMemoryRunStore();
        var lifecycle = new RunLifecycle(registry, store, new FixedClock());

        // Call the real producer before the direct policy assertions: an expanded
        // declaration must not create a run with a formerly disallowed profile.
        var refused = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => lifecycle.StartAsync(
            new StartRun(Workflow, Tenant, policy, RetryProfileName.DurableDelivery)).AsTask());
        Assert.Equal("execution.retry_profile_not_allowed", refused.Code);
        Assert.Empty(await store.ListByStatusAsync(Tenant, RunStatus.Pending));

        // Removing or clearing the published declaration must not invalidate its
        // original default or a lawful explicit selection.
        var defaulted = await lifecycle.StartAsync(new StartRun(Workflow, Tenant, policy));
        var selected = await lifecycle.StartAsync(new StartRun(
            Workflow, Tenant, policy, RetryProfileName.ExternalApiStandard));
        AssertPendingRun(defaulted);
        AssertPendingRun(selected);
        Assert.NotEqual(defaulted.Id, selected.Id);
        var stored = await store.ListByStatusAsync(Tenant, RunStatus.Pending);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, AssertPendingRun);
        AssertDeclaredPolicy(policy);
    }

    private static void AssertDeclaredPolicy(CapabilityRetryPolicy policy)
    {
        Assert.Equal("external-api-standard", policy.Default.Value);
        Assert.Equal("external-api-standard", Assert.Single(policy.Allowed).Value);
        Assert.Equal("external-api-standard", policy.Resolve(null).Name.Value);
        Assert.Equal("external-api-standard", policy.Resolve(RetryProfileName.ExternalApiStandard).Name.Value);
        var refused = Assert.Throws<ExecutionRuntimeRefusedException>(
            () => policy.Resolve(RetryProfileName.DurableDelivery));
        Assert.Equal("execution.retry_profile_not_allowed", refused.Code);
    }

    private static void AssertPendingRun(RunRecord run)
    {
        Assert.Equal(Tenant, run.TenantId);
        Assert.Equal(RunStatus.Pending, run.Status);
        Assert.Equal("external-api-standard", run.RetryProfile.Value);
        Assert.Equal(1, run.Version);
        Assert.Equal(Now, run.CreatedUtc);
        Assert.Equal(Now, run.UpdatedUtc);
        Assert.Empty(run.Attempts);
        Assert.Null(run.NextAttemptDueUtc);
        Assert.Null(run.DeadLetter);
    }

    private static void TryMutation(IReadOnlySet<RetryProfileName> allowed, string operation)
    {
        // Some readonly views expose no mutable interface, and some expose setters
        // that refuse. A detached writable view is also valid. Never skip IsReadOnly:
        // the oracle is whether the retained policy and actual producer can change.
        try
        {
            switch (operation)
            {
                case "ISet.Add":
                    if (allowed is ISet<RetryProfileName> setAdd)
                        setAdd.Add(RetryProfileName.DurableDelivery);
                    break;
                case "ISet.Remove":
                    if (allowed is ISet<RetryProfileName> setRemove)
                        setRemove.Remove(RetryProfileName.ExternalApiStandard);
                    break;
                case "ISet.UnionWith":
                    if (allowed is ISet<RetryProfileName> setUnion)
                        setUnion.UnionWith([RetryProfileName.DurableDelivery]);
                    break;
                case "ISet.IntersectWith":
                    if (allowed is ISet<RetryProfileName> setIntersect)
                        setIntersect.IntersectWith(Array.Empty<RetryProfileName>());
                    break;
                case "ISet.ExceptWith":
                    if (allowed is ISet<RetryProfileName> setExcept)
                        setExcept.ExceptWith([RetryProfileName.ExternalApiStandard]);
                    break;
                case "ISet.SymmetricExceptWith":
                    if (allowed is ISet<RetryProfileName> setSymmetric)
                        setSymmetric.SymmetricExceptWith([RetryProfileName.DurableDelivery]);
                    break;
                case "ICollection.Add":
                    if (allowed is ICollection<RetryProfileName> collectionAdd)
                        collectionAdd.Add(RetryProfileName.DurableDelivery);
                    break;
                case "ICollection.Remove":
                    if (allowed is ICollection<RetryProfileName> collectionRemove)
                        collectionRemove.Remove(RetryProfileName.ExternalApiStandard);
                    break;
                case "ICollection.Clear":
                    if (allowed is ICollection<RetryProfileName> collectionClear)
                        collectionClear.Clear();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown mutation route.");
            }
        }
        catch (NotSupportedException)
        {
            // Explicit readonly refusal is permitted; no other failure is swallowed.
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
