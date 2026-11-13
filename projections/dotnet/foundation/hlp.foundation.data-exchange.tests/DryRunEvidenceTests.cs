using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class DryRunEvidenceTests
{
    [Fact]
    public async Task Halt_takes_precedence_over_refusals()
    {
        var request = Fixtures.DryRunRequest();
        var evaluations = new[]
        {
            new DryRunEffectEvaluation(request.Effects[0], new(ExchangeEffectStatus.Rejected, "fixture")),
            new DryRunEffectEvaluation(request.Effects[1], new(ExchangeEffectStatus.Halted, "fixture")),
        };
        var run = await new DataExchangeRuntime(new InMemoryExchangeRunStore(), TimeProvider.System, new FakeLifecyclePolicy())
            .CreateDryRunAsync(request with { Evaluations = evaluations });
        Assert.Equal(ExchangeRunTerminalStatus.Halted, run.TerminalStatus);
    }

    [Theory]
    [InlineData(ExchangeEffectStatus.Applied, ExchangeRunTerminalStatus.Completed)]
    [InlineData(ExchangeEffectStatus.Skipped, ExchangeRunTerminalStatus.CompletedWithRefusals)]
    [InlineData(ExchangeEffectStatus.Conflicted, ExchangeRunTerminalStatus.CompletedWithRefusals)]
    [InlineData(ExchangeEffectStatus.Rejected, ExchangeRunTerminalStatus.CompletedWithRefusals)]
    [InlineData(ExchangeEffectStatus.Failed, ExchangeRunTerminalStatus.CompletedWithRefusals)]
    [InlineData(ExchangeEffectStatus.Halted, ExchangeRunTerminalStatus.Halted)]
    [Trait("Holds", "data-exchange-eng-9")]
    [Trait("Holds", "data-exchange-run-3")]
    public async Task Dry_run_terminal_result_follows_its_census(ExchangeEffectStatus status, ExchangeRunTerminalStatus expected)
    {
        var store = new InMemoryExchangeRunStore();
        var request = Fixtures.DryRunRequest();
        var evaluations = request.Effects.Select(effect => new DryRunEffectEvaluation(effect, new(status, "fixture"))).ToArray();
        var run = await new DataExchangeRuntime(store, TimeProvider.System, new FakeLifecyclePolicy())
            .CreateDryRunAsync(request with { Evaluations = evaluations });

        Assert.Equal(2, run.Census.Accounted);
        Assert.Equal(expected, run.TerminalStatus);
        Assert.Equal(expected, (await store.GetDryRunAsync(run.Id))!.TerminalStatus);
    }

    [Fact]
    [Trait("Holds", "data-exchange-eng-11")]
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

    [Theory]
    [InlineData("source")]
    [InlineData("definition")]
    [InlineData("effect")]
    [Trait("Holds", "data-exchange-eng-12")]
    [Trait("Holds", "data-exchange-run-4")]
    [Trait("Holds", "data-exchange-auth-10")]
    public async Task Evidence_read_reports_drift_without_changing_stored_artifact(string drift)
    {
        var store = new InMemoryExchangeRunStore();
        var request = Fixtures.DryRunRequest();
        var run = await new DataExchangeRuntime(store, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(request);
        var before = System.Text.Json.JsonSerializer.Serialize(await store.GetDryRunAsync(run.Id));
        var evaluator = new FakeProposalEvaluator();
        var reader = new DryRunEvidenceReader(store, evaluator, new ReadAuthority(true));
        var fresh = await reader.ReadAsync("tenant-a", run.Id);
        Assert.NotNull(fresh);
        Assert.False(fresh.IsStale);
        Assert.Null(fresh.DriftReason);
        Assert.Equal(2, fresh.Census.Applied);
        Assert.Equal(ExchangeRunTerminalStatus.Completed, fresh.TerminalStatus);
        Assert.Equal("standard-7y", fresh.RetentionClass);
        Assert.Null(await reader.ReadAsync("tenant-b", run.Id));
        Assert.Null(await reader.ReadAsync("tenant-a", new DryRunId("missing")));
        var denied = new DryRunEvidenceReader(store, evaluator, new ReadAuthority(false));
        var refusal = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(() => denied.ReadAsync("tenant-a", run.Id).AsTask());
        Assert.Equal("run.forbidden", refusal.Code);
        evaluator.Current = drift switch
        {
            "source" => request with { Proposal = request.Proposal with { SourceFingerprint = "changed" } },
            "definition" => request with { Proposal = request.Proposal with { DefinitionVersion = "changed" } },
            _ => request with { Effects = request.Effects.Select((e, i) => i == 0 ? e with { BoundaryAfter = "changed" } : e).ToArray() },
        };
        var stale = await reader.ReadAsync("tenant-a", run.Id);
        Assert.True(stale!.IsStale);
        Assert.Equal(drift == "effect" ? "effects.changed" : drift + ".changed", stale.DriftReason);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(await store.GetDryRunAsync(run.Id)));
        var json = System.Text.Json.JsonSerializer.Serialize(stale);
        Assert.DoesNotContain("snapshot://", json);
        Assert.DoesNotContain("customer-42", json);
    }
    [Fact]
    public async Task Evidence_read_detects_other_proposal_and_evaluated_effect_drift()
    {
        var store = new InMemoryExchangeRunStore();
        var request = Fixtures.DryRunRequest();
        var run = await new DataExchangeRuntime(store, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(request);
        var evaluator = new FakeProposalEvaluator { Current = request with { Proposal = request.Proposal with { MappingDigest = "changed" } } };
        var reader = new DryRunEvidenceReader(store, evaluator, new ReadAuthority(true));
        Assert.Equal("proposal.changed", (await reader.ReadAsync("tenant-a", run.Id))!.DriftReason);
        evaluator.Current = request with { Evaluations = request.Effects.Select(e => new DryRunEffectEvaluation(e with { BoundaryAfter = "changed" }, new(ExchangeEffectStatus.Applied, "fixture"))).ToArray() };
        Assert.Equal("effects.changed", (await reader.ReadAsync("tenant-a", run.Id))!.DriftReason);
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

internal sealed class ReadAuthority(bool allowed) : IExchangeRunReadAuthority
{
    public ValueTask<bool> CanReadAsync(DryRunArtifact run, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(allowed);
}