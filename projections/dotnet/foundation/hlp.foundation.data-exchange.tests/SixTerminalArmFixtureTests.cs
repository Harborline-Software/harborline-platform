using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class SixTerminalArmFixtureTests
{
    [Fact]
    public async Task Dry_run_classifies_one_effect_into_each_terminal_arm()
    {
        var mapping = new TabularMappingDocument(
            TabularMappingProfile.Family,
            TabularMappingProfile.SchemaUri,
            "1.0.0",
            new CanonicalTarget("records.fixture/v1", "/fixtures"),
            [new("Value", "integer", true, "/fixtures/value")],
            new Dictionary<string, string>());
        var definition = new DataExchangeDefinition(
            "tenant-fixture",
            "exchange.fixture",
            "1.0.0",
            "Six terminal arms",
            new ExchangeSourceBinding("erpnext", "4.1.0", "secret://fixture", new Dictionary<string, string>()),
            mapping,
            ReplayPolicy.AppendDeduplicate,
            [],
            null);
        var context = new ExchangeEvaluationContext("fixture-actor", "sha256:fixture", "fixture-window", null, "authz://fixture", "fixture");
        var batch = ExchangeIdentity.DeriveBatch(BatchIdentityInputs.From(definition.Tenant, Proposal(definition, mapping, context)));
        var ledger = new FakeExchangeLedger();
        ledger.SetOutcome(Entry(batch, "skipped", ExchangeEffectStatus.Applied, PayloadDigest(mapping, "2")));
        ledger.SetOutcome(Entry(batch, "conflicted", ExchangeEffectStatus.Skipped, "sha256:different-content"));
        ledger.SetOutcome(Entry(batch, "failed", ExchangeEffectStatus.Failed));
        ledger.SetOutcome(Entry(batch, "halted", ExchangeEffectStatus.Halted));

        var source = new StubSource(
            new DiscoveredSourceShape([new("Value", "integer")]),
            [
                Row(0, "applied", "1"),
                Row(1, "skipped", "2"),
                Row(2, "conflicted", "3"),
                Row(3, "rejected", "not-an-integer"),
                Row(4, "failed", "5"),
                Row(5, "halted", "6"),
            ]);
        var interpreter = new DataExchangeInterpreter(
            new StubCapabilities(source, ledger),
            new DataExchangeRuntime(new InMemoryExchangeRunStore(), TimeProvider.System, new FakeLifecyclePolicy()),
            new InMemoryProtectedEffectPayloadStore());

        var dryRun = await interpreter.CreateDryRunAsync(definition, context);

        Assert.Equal(6, dryRun.Census.Accounted);
        Assert.Equal(1, dryRun.Census.Applied);
        Assert.Equal(1, dryRun.Census.Skipped);
        Assert.Equal(1, dryRun.Census.Conflicted);
        Assert.Equal(1, dryRun.Census.Rejected);
        Assert.Equal(1, dryRun.Census.Failed);
        Assert.Equal(1, dryRun.Census.Halted);
    }

    private static AcquiredSourceRecord Row(int ordinal, string identity, string value) => new(
        ordinal,
        identity,
        "v1",
        $"cursor-{ordinal}",
        new Dictionary<string, string?> { ["Value"] = value });

    private static ProposalFingerprint Proposal(
        DataExchangeDefinition definition,
        TabularMappingDocument mapping,
        ExchangeEvaluationContext context) => new(
        context.SourceFingerprint,
        context.InputBoundary,
        definition.Key,
        definition.Version,
        definition.Key + ":mapping",
        mapping.Version,
        Digest(mapping),
        definition.Source.CapabilityId,
        definition.Source.ConnectorVersion,
        mapping.Target.Contract,
        context.DependencyFingerprint,
        mapping.Profile,
        context.TransformVersionsFingerprint,
        context.LookupVersionsFingerprint,
        context.MatchingInputsFingerprint,
        context.SelectedBoundary ?? context.InputBoundary);

    private static EffectLedgerEntry Entry(
        BatchIdentity batch,
        string sourceRecordIdentity,
        ExchangeEffectStatus status,
        string? payloadDigest = null)
    {
        var identity = ExchangeIdentity.DeriveEffect(batch, "records.fixture/v1", sourceRecordIdentity, "v1", "canonical-record");
        return new EffectLedgerEntry(
            batch,
            identity,
            AttemptId.New(),
            new EffectTerminalOutcome(status, "fixture.recorded"),
            DateTimeOffset.UnixEpoch,
            payloadDigest);
    }

    private static string PayloadDigest(TabularMappingDocument mapping, string value) => Digest(
        new CanonicalEffectPayload(mapping.Target.Contract, new Dictionary<string, object?> { ["/fixtures/value"] = long.Parse(value) }));

    private static string Digest<T>(T value)
    {
        var bytes = value is TabularMappingDocument mapping
            ? Encoding.UTF8.GetBytes(TabularMappingJson.Serialize(mapping))
            : JsonSerializer.SerializeToUtf8Bytes(value);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
