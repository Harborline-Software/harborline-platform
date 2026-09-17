using System.Collections.Concurrent;
using System.Globalization;
using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class DataExchangeInterpreterTests
{
    [Fact]
    public async Task Dry_run_acquires_maps_and_protects_payload_without_writing_records()
    {
        var source = new StubSource(
            new DiscoveredSourceShape(
                [new("CustomerNumber", "string"), new("Email", "string"), new("CreditLimit", "decimal")]),
            [
                new(0, "customer-42", "v3", "cursor-42", new Dictionary<string, string?>
                {
                    ["CustomerNumber"] = " C-0042 ",
                    ["Email"] = " PERSON@EXAMPLE.COM ",
                    ["CreditLimit"] = "",
                }),
            ]);
        var capabilities = new StubCapabilities(source);
        var payloads = new InMemoryProtectedEffectPayloadStore();
        var runs = new InMemoryExchangeRunStore();
        var interpreter = new DataExchangeInterpreter(
            capabilities,
            new DataExchangeRuntime(runs, TimeProvider.System),
            payloads);

        var dryRun = await interpreter.CreateDryRunAsync(
            Definition(),
            new ExchangeEvaluationContext(
                "actor-7",
                "sha256:source",
                "window-1",
                "snapshot://protected/source-1",
                "authz://decision-1",
                "tenant-standard",
                DateTimeOffset.Parse("2030-01-01T00:00:00Z", CultureInfo.InvariantCulture)));

        var effect = Assert.Single(dryRun.NormalizedEffects);
        Assert.Equal("CustomerNumber=C-0042", effect.SourceRecordIdentity);
        Assert.Equal("cursor-42", effect.BoundaryAfter);
        Assert.DoesNotContain("PERSON@EXAMPLE.COM", effect.Metadata.Values);
        Assert.StartsWith("sha256:", effect.Metadata["payloadDigest"], StringComparison.Ordinal);
        var payload = await payloads.GetAsync(effect.PayloadReference!);
        Assert.NotNull(payload);
        Assert.Equal("C-0042", payload!.Values["/customers/customerNumber"]);
        Assert.Equal("person@example.com", payload.Values["/customers/email"]);
        Assert.Equal(0m, payload.Values["/customers/creditLimit"]);
        Assert.Equal(0, capabilities.CommandsApplied);
    }

    [Fact]
    public async Task Invalid_row_is_recorded_as_rejected_and_later_rows_are_still_proposed()
    {
        var source = new StubSource(
            new DiscoveredSourceShape([new("CustomerNumber", "string"), new("Email", "string"), new("CreditLimit", "decimal")]),
            [
                new(0, "bad", "v1", "cursor-a", new Dictionary<string, string?> { ["CustomerNumber"] = "A", ["CreditLimit"] = "not-decimal" }),
                new(1, "good", "v1", "cursor-b", new Dictionary<string, string?> { ["CustomerNumber"] = "B", ["CreditLimit"] = "12.50" }),
            ]);
        var runs = new InMemoryExchangeRunStore();
        var payloads = new InMemoryProtectedEffectPayloadStore();
        var interpreter = new DataExchangeInterpreter(
            new StubCapabilities(source),
            new DataExchangeRuntime(runs, TimeProvider.System),
            payloads);

        var dryRun = await interpreter.CreateDryRunAsync(
            Definition(),
            new ExchangeEvaluationContext("actor", "sha256:source", "window", null, "authz", "standard", DateTimeOffset.UtcNow.AddDays(30)));

        Assert.Equal(2, dryRun.Evaluations.Count);
        Assert.Equal(ExchangeEffectStatus.Rejected, dryRun.Evaluations[0].Outcome.Status);
        Assert.Equal("mapping.datatype_invalid", dryRun.Evaluations[0].Outcome.Code);
        Assert.Equal(ExchangeEffectStatus.Applied, dryRun.Evaluations[1].Outcome.Status);
        Assert.Equal(2, dryRun.NormalizedEffects.Count);
        Assert.Equal(2, dryRun.Census.Accounted);
        Assert.Equal(1, dryRun.Census.Rejected);

        var target = new CapturingTarget();
        var commit = await new DataExchangeCommitter(
            runs,
            new RecordingCommitAuthority(true),
            target,
            target,
            new InMemoryEffectOutcomeStore(),
            new InMemoryAcquisitionCheckpointStore(),
            payloads,
            TimeProvider.System,
            new CommitBounds(10, 2, 64 * 1024))
            .CommitAsync(dryRun.Id, dryRun.Proposal);

        Assert.Single(target.Commands);
        Assert.Equal(1, commit.Census.Rejected);
        Assert.Equal(1, commit.Census.Applied);
        Assert.Equal("cursor-b", commit.DurableCheckpoint);
        Assert.Equal(ExchangeRunTerminalStatus.CompletedWithRefusals, commit.TerminalStatus);
    }

    [Fact]
    public async Task Commit_loads_the_protected_payload_for_each_canonical_command()
    {
        var runs = new InMemoryExchangeRunStore();
        var payloads = new InMemoryProtectedEffectPayloadStore();
        var interpreter = new DataExchangeInterpreter(
            new StubCapabilities(new StubSource(
                new DiscoveredSourceShape([new("CustomerNumber", "string"), new("Email", "string"), new("CreditLimit", "decimal")]),
                [new(0, "customer-42", "v3", "cursor-42", new Dictionary<string, string?>
                {
                    ["CustomerNumber"] = "C-0042",
                    ["Email"] = "person@example.com",
                    ["CreditLimit"] = "10",
                })])),
            new DataExchangeRuntime(runs, TimeProvider.System),
            payloads);
        var dryRun = await interpreter.CreateDryRunAsync(
            Definition(),
            new ExchangeEvaluationContext("actor", "sha256:source", "window", null, "authz", "standard", DateTimeOffset.UtcNow.AddDays(30)));
        var target = new CapturingTarget();
        var committer = new DataExchangeCommitter(
            runs,
            new RecordingCommitAuthority(true),
            target,
            target,
            new InMemoryEffectOutcomeStore(),
            new InMemoryAcquisitionCheckpointStore(),
            payloads,
            TimeProvider.System,
            new CommitBounds(10, 2, 64 * 1024));

        await committer.CommitAsync(dryRun.Id, dryRun.Proposal);

        var command = Assert.Single(target.Commands);
        Assert.Equal("C-0042", command.Payload.Values["/customers/customerNumber"]);
    }

    private static DataExchangeDefinition Definition() => new(
        "tenant-a",
        "exchange.customers",
        "1.0.0",
        "Customer opening load",
        new ExchangeSourceBinding("erpnext", "4.1.0", "secret://erpnext/customers", new Dictionary<string, string>()),
        new TabularMappingDocument(
            TabularMappingProfile.Family,
            TabularMappingProfile.SchemaUri,
            "1.0.0",
            new CanonicalTarget("records.customer/v1", "/customers"),
            [
                new("CustomerNumber", "string", true, "/customers/customerNumber", Extensions: new Dictionary<string, string> { ["hl:transform"] = "trim" }),
                new("Email", "string", false, "/customers/email", Extensions: new Dictionary<string, string> { ["hl:transform"] = "normalizeEmail" }),
                new("CreditLimit", "decimal", false, "/customers/creditLimit", Default: "0"),
            ],
            new Dictionary<string, string>()),
        ReplayPolicy.AppendDeduplicate,
        ["CustomerNumber"],
        null);
}

internal sealed class StubSource(DiscoveredSourceShape shape, IReadOnlyList<AcquiredSourceRecord> records)
    : IReadOnlyAcquisitionSource
{
    public string CapabilityId => "erpnext";
    public string ConnectorVersion => "4.1.0";
    public ValueTask<DiscoveredSourceShape> DiscoverAsync(ExchangeSourceBinding binding, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(shape);

    public async IAsyncEnumerable<AcquiredSourceRecord> ReadAsync(
        ExchangeSourceBinding binding,
        string inputBoundary,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
            await Task.Yield();
        }
    }
}

internal sealed class StubCapabilities(IReadOnlyAcquisitionSource source) : IDataExchangeCapabilityRegistry
{
    public int CommandsApplied { get; private set; }

    public IReadOnlyAcquisitionSource ResolveSource(string capabilityId, string connectorVersion)
        => capabilityId == source.CapabilityId && connectorVersion == source.ConnectorVersion
            ? source
            : throw new DataExchangeCapabilityException("source.unregistered");

    public INamedMappingTransform ResolveTransform(string name) => name switch
    {
        "trim" => new DelegateTransform(value => (value as string)?.Trim()),
        "normalizeEmail" => new DelegateTransform(value => (value as string)?.Trim().ToLowerInvariant()),
        _ => throw new DataExchangeCapabilityException("transform.unregistered"),
    };

    public bool CanWrite(string targetContract, string targetPointer) => true;

    private sealed class DelegateTransform(Func<object?, object?> transform) : INamedMappingTransform
    {
        public object? Apply(object? value) => transform(value);
    }
}

internal sealed class CapturingTarget : ITargetAccessGate, ICanonicalRecordsCommandPort
{
    public ConcurrentQueue<CanonicalRecordsCommand> Commands { get; } = new();

    public ValueTask<bool> CanApplyAsync(ProposedEffect effect, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);

    public ValueTask<EffectTerminalOutcome> ApplyAsync(CanonicalRecordsCommand command, CancellationToken cancellationToken = default)
    {
        Commands.Enqueue(command);
        return ValueTask.FromResult(new EffectTerminalOutcome(ExchangeEffectStatus.Applied, "records.applied"));
    }
}
