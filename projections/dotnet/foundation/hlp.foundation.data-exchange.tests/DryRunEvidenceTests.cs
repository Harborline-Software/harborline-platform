using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class DryRunEvidenceTests
{
    [Fact]
    public async Task Dry_run_persists_immutable_policy_owned_evidence_outside_the_definition()
    {
        var store = new InMemoryExchangeRunStore();
        var runtime = new DataExchangeRuntime(store, TimeProvider.System, new FakeLifecyclePolicy());
        var request = Fixtures.DryRunRequest();

        var first = await runtime.CreateDryRunAsync(request);
        var second = await runtime.CreateDryRunAsync(request);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("tenant-a", first.TenantId);
        Assert.Equal("sha256:source", first.Proposal.SourceFingerprint);
        Assert.Equal("standard-7y", first.RetentionClass);
        Assert.Equal(new DateTimeOffset(2033, 9, 17, 12, 0, 0, TimeSpan.Zero), first.RetainUntil);
        Assert.Equal("snapshot://protected/source-1", first.SnapshotReference);
        Assert.DoesNotContain("Alice", first.NormalizedEffects[0].Metadata.Values);

        var stored = await store.GetDryRunAsync(first.Id);
        Assert.NotNull(stored);
        Assert.Equal(first.Id, stored.Id);
        Assert.Equal(first.Proposal, stored.Proposal);
        Assert.Equal(first.NormalizedEffects, stored.NormalizedEffects);
        await Assert.ThrowsAsync<ExchangeRunConflictException>(
            () => store.SaveDryRunAsync(first).AsTask());
    }

    [Fact]
    public async Task Supersession_cannot_cross_tenants()
    {
        var store = new InMemoryExchangeRunStore();
        var runtime = new DataExchangeRuntime(store, TimeProvider.System, new FakeLifecyclePolicy());
        var first = await runtime.CreateDryRunAsync(Fixtures.DryRunRequest());
        var refusal = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(() => runtime.CreateDryRunAsync(
            Fixtures.DryRunRequest() with { TenantId = "tenant-b", SupersedesDryRunId = first.Id }).AsTask());
        Assert.Equal("run.stale", refusal.Code);
    }
}

internal static partial class Fixtures
{
    public static TabularMappingDocument Mapping() => new(
        TabularMappingProfile.Family,
        TabularMappingProfile.SchemaUri,
        "1.0.0",
        new CanonicalTarget("records.customer/v1", "/customers"),
        [new MappingColumn("CustomerNumber", "string", true, "/customerNumber")],
        new Dictionary<string, string> { ["hl:operation"] = "upsert" });

    public static DryRunRequest DryRunRequest() => new(
        "tenant-a",
        "actor-7",
        new ProposalFingerprint(
            "sha256:source",
            "rows:1-2",
            "exchange.customers",
            "1.2.0",
            "mapping.customers",
            "1.0.0",
            "sha256:map",
            "erpnext",
            "4.1.0",
            "records.customer/v1",
            "sha256:dependencies", TabularMappingProfile.Family, "sha256:transforms-v1",
            "sha256:lookups-v1", "sha256:matches", "selection:all"),
        [
            new ProposedEffect(
                1,
                "customer-42",
                "source-v3",
                "primary",
                "cursor:a",
                new Dictionary<string, string> { ["target"] = "customer-42", ["digest"] = "sha256:effect-1" }),
            new ProposedEffect(
                2,
                "customer-43",
                "source-v1",
                "primary",
                "cursor:b",
                new Dictionary<string, string> { ["target"] = "customer-43", ["digest"] = "sha256:effect-2" }),
        ],
        "cursor:b",
        "snapshot://protected/source-1",
        "auth-context://review-7",
        "standard-7y");
}
