using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class ExchangeDryRunRowContinuationProofTests
{
    [Fact]
    [Trait("Holds", "data-exchange-eng-10")]
    [Trait("Holds", "data-exchange-eng-11")]
    [Trait("Holds", "data-exchange-auth-24")]
    public async Task Row_continuation_preserves_census_and_ordered_refusal_evidence()
    {
        var (run, runs, payloads, ledger) = await CreateAsync("not-integer");
        Assert.Equal(3, run.Census.Accounted);
        Assert.Equal(2, run.Census.Applied);
        Assert.Equal(0, run.Census.Skipped);
        Assert.Equal(0, run.Census.Conflicted);
        Assert.Equal(1, run.Census.Rejected);
        Assert.Equal(0, run.Census.Failed);
        Assert.Equal(0, run.Census.Halted);
        Assert.Equal(ExchangeRunTerminalStatus.CompletedWithRefusals, run.TerminalStatus);
        Assert.Equal(new[] { 0, 1, 2 }, run.Evaluations.Select(x => x.Effect.SourceOrdinal));
        Assert.Equal(new[] { "first", "bad", "last" }, run.Evaluations.Select(x => x.Effect.SourceRecordIdentity));
        Assert.Equal(new[] { ExchangeEffectStatus.Applied, ExchangeEffectStatus.Rejected, ExchangeEffectStatus.Applied }, run.Evaluations.Select(x => x.Outcome.Status));
        Assert.Equal(new[] { "mapping.proposed", "mapping.datatype_invalid", "mapping.proposed" }, run.Evaluations.Select(x => x.Outcome.Code));
        Assert.Equal(new[] { "canonical-record", "mapping-refusal", "canonical-record" }, run.NormalizedEffects.Select(x => x.EffectDiscriminator));
        Assert.Equal("/columns/Value", run.NormalizedEffects[1].Metadata["refusalPointer"]);
        var stored = await runs.GetDryRunAsync(run.Id);
        Assert.NotNull(stored);
        Assert.Equal(new[] { 0, 1, 2 }, run.NormalizedEffects.Select(x => x.SourceOrdinal));
        Assert.Equal(new[] { 0, 1, 2 }, stored!.NormalizedEffects.Select(x => x.SourceOrdinal));
        Assert.Equal("mapping-refusal", stored.NormalizedEffects[1].EffectDiscriminator);
        Assert.Equal("/columns/Value", stored.NormalizedEffects[1].Metadata["refusalPointer"]);
        Assert.Equal(1L, (await payloads.GetAsync(run.NormalizedEffects[0].PayloadReference!))!.Values["/customers/value"]);
        Assert.Equal(3L, (await payloads.GetAsync(run.NormalizedEffects[2].PayloadReference!))!.Values["/customers/value"]);
        Assert.Equal(0, ledger.WriteAttempts);

        var corrected = await RunAsync("2", runs, payloads, ledger);
        Assert.NotEqual(run.Id, corrected.Id);
        Assert.Equal(new[] { ExchangeEffectStatus.Applied, ExchangeEffectStatus.Applied, ExchangeEffectStatus.Applied }, corrected.Evaluations.Select(x => x.Outcome.Status));
        Assert.Equal(3, corrected.Census.Applied);
        Assert.Equal(0, corrected.Census.Rejected);
        var earlier = await runs.GetDryRunAsync(run.Id);
        Assert.Equal("mapping-refusal", earlier!.NormalizedEffects[1].EffectDiscriminator);
        Assert.Equal("mapping.datatype_invalid", earlier.Evaluations[1].Outcome.Code);
        Assert.Equal(0, ledger.WriteAttempts);
    }

    [Fact]
    [Trait("Holds", "data-exchange-eng-10")]
    public async Task Row_continuation_keeps_last_row()
    {
        var (run, _, _, _) = await CreateAsync("not-integer");
        Assert.Contains(run.Evaluations, x => x.Effect.SourceOrdinal == 2 && x.Effect.SourceRecordIdentity == "last");
    }

    [Fact]
    [Trait("Holds", "data-exchange-eng-11")]
    public async Task Row_continuation_keeps_exact_refusal_code()
    {
        var (run, _, _, _) = await CreateAsync("not-integer");
        Assert.Equal("mapping.datatype_invalid", run.Evaluations.Single(x => x.Effect.SourceOrdinal == 1).Outcome.Code);
    }

    [Fact]
    [Trait("Holds", "data-exchange-auth-24")]
    public async Task Row_continuation_keeps_final_candidate_boundary()
    {
        var (run, runs, _, _) = await CreateAsync("not-integer");
        Assert.Equal("cursor-c", run.CandidateCheckpoint);
        Assert.Equal("cursor-c", (await runs.GetDryRunAsync(run.Id))!.CandidateCheckpoint);
    }

    private static async Task<(DryRunArtifact, InMemoryExchangeRunStore, InMemoryProtectedEffectPayloadStore, FakeExchangeLedger)> CreateAsync(string middle)
    {
        var runs = new InMemoryExchangeRunStore();
        var payloads = new InMemoryProtectedEffectPayloadStore();
        var ledger = new FakeExchangeLedger();
        return (await RunAsync(middle, runs, payloads, ledger), runs, payloads, ledger);
    }

    private static async Task<DryRunArtifact> RunAsync(string middle, InMemoryExchangeRunStore runs, InMemoryProtectedEffectPayloadStore payloads, FakeExchangeLedger ledger)
    {
        var source = new StubSource(new DiscoveredSourceShape([new("Value", "integer")]),
        [
            new(0, "first", "v1", "cursor-a", new Dictionary<string, string?> { ["Value"] = "1" }),
            new(1, "bad", "v1", "cursor-b", new Dictionary<string, string?> { ["Value"] = middle }),
            new(2, "last", "v1", "cursor-c", new Dictionary<string, string?> { ["Value"] = "3" }),
        ]);
        var definition = new DataExchangeDefinition("tenant-a", "exchange.values", "1.0.0", "Values",
            new ExchangeSourceBinding("erpnext", "4.1.0", "secret://source", new Dictionary<string, string>()),
            new TabularMappingDocument(TabularMappingProfile.Family, TabularMappingProfile.SchemaUri, "1.0.0",
                new CanonicalTarget("records.customer/v1", "/customers"),
                [new("Value", "integer", true, "/customers/value")], new Dictionary<string, string>()),
            ReplayPolicy.AppendDeduplicate, [], null);
        return await new DataExchangeInterpreter(new StubCapabilities(source, ledger),
            new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()), payloads)
            .CreateDryRunAsync(definition, new ExchangeEvaluationContext("actor", "sha256:source", "window", null, "authz", "standard"));
    }
}
