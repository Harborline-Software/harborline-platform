using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class ProtectedEffectPayloadSnapshotTests
{
    // SHA-256 of the literal UTF-8 fixture:
    // {"TargetContract":"records.tags/v1","Values":{"/tags":["red","blue"]}}
    private const string MappedPayloadDigest = "sha256:41a5cf562f1b201b53cb48900e37b4adda0f9e49046a4f35081f89733691d037";

    [Fact]
    public async Task Separator_payload_exports_cannot_change_the_protected_payload_or_review_evidence()
    {
        var mapping = new TabularMappingDocument(
            "hl:tabular-mapping/v1",
            "https://schemas.harborline.software/mapping/tabular/v1",
            "1.0.0",
            new CanonicalTarget("records.tags/v1", "/"),
            [new("Tags", "string", true, "/tags", Separator: "|")],
            new Dictionary<string, string>());
        var definition = new DataExchangeDefinition(
            "tenant-a", "exchange.tags", "1.0.0", "Tag import",
            new ExchangeSourceBinding("erpnext", "4.1.0", "secret://erpnext/tags",
                new Dictionary<string, string>()),
            mapping, ReplayPolicy.Append, [], null);
        var source = new StubSource(
            new DiscoveredSourceShape([new("Tags", "string")]),
            [new(0, "tag-1", "v1", "cursor-1",
                new Dictionary<string, string?> { ["Tags"] = "red|blue" })]);
        var target = new ObservingTargetPort();
        var payloads = new InMemoryProtectedEffectPayloadStore();
        var runs = new InMemoryExchangeRunStore();
        var interpreter = new DataExchangeInterpreter(
            new StubCapabilities(source, target),
            new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()),
            payloads);

        Assert.Empty(TabularMappingAdmission.Refusals(mapping));
        var review = await interpreter.CreateDryRunAsync(definition,
            new ExchangeEvaluationContext("actor", "sha256:source", "window", null, "authz", "standard"));

        Assert.Equal(1, review.Census.Accounted);
        Assert.Equal(1, review.Census.Applied);
        var evaluation = Assert.Single(review.Evaluations);
        Assert.Equal("Applied", evaluation.Outcome.Status.ToString());
        Assert.Equal("mapping.proposed", evaluation.Outcome.Code);
        var effect = Assert.Single(review.NormalizedEffects);
        Assert.Equal("tag-1", effect.SourceRecordIdentity);
        Assert.Equal("cursor-1", effect.BoundaryAfter);
        Assert.Equal("records.tags/v1", effect.Metadata["targetContract"]);
        Assert.Equal(MappedPayloadDigest, effect.Metadata["payloadDigest"]);
        Assert.NotNull(effect.PayloadReference);
        Assert.Equal(1, target.LedgerReads);
        Assert.Equal(0, target.WriteAttempts);

        var first = Assert.IsType<CanonicalEffectPayload>(
            await payloads.GetAsync(effect.PayloadReference!));
        var firstVector = Assert.IsType<object?[]>(first.Values["/tags"]);
        Assert.Equal(new object?[] { "red", "blue" }, firstVector);
        firstVector[0] = "changed-first-export";
        Assert.Equal("changed-first-export", firstVector[0]);

        var second = Assert.IsType<CanonicalEffectPayload>(
            await payloads.GetAsync(effect.PayloadReference!));
        var secondVector = Assert.IsType<object?[]>(second.Values["/tags"]);
        Assert.NotSame(firstVector, secondVector);
        Assert.Equal(new object?[] { "red", "blue" }, secondVector);
        secondVector[1] = "changed-second-export";
        Assert.Equal("changed-second-export", secondVector[1]);

        var third = Assert.IsType<CanonicalEffectPayload>(
            await payloads.GetAsync(effect.PayloadReference!));
        Assert.Equal(new object?[] { "red", "blue" }, Assert.IsType<object?[]>(third.Values["/tags"]));
        var storedReview = Assert.IsType<DryRunArtifact>(await runs.GetDryRunAsync(review.Id));
        var storedEffect = Assert.Single(storedReview.NormalizedEffects);
        Assert.Equal(effect.PayloadReference, storedEffect.PayloadReference);
        Assert.Equal(MappedPayloadDigest, storedEffect.Metadata["payloadDigest"]);
        Assert.Equal("records.tags/v1", storedEffect.Metadata["targetContract"]);
        Assert.Equal(1, storedReview.Census.Accounted);
        Assert.Equal(1, storedReview.Census.Applied);
        Assert.Equal("mapping.proposed", Assert.Single(storedReview.Evaluations).Outcome.Code);
        Assert.Equal(1, target.LedgerReads);
        Assert.Equal(0, target.WriteAttempts);
    }

    [Fact]
    public async Task Saving_a_scalar_vector_does_not_retain_the_callers_array()
    {
        object?[] callerOwned = ["red", "blue"];
        var payloads = new InMemoryProtectedEffectPayloadStore();
        var reference = await payloads.SaveAsync(new DryRunId("ingress"), 7,
            new CanonicalEffectPayload("records.tags/v1",
                new Dictionary<string, object?> { ["/tags"] = callerOwned }));

        Assert.Equal("protected-effect://ingress/7", reference);
        callerOwned[0] = "changed-input";
        Assert.Equal("changed-input", callerOwned[0]);
        var first = Assert.IsType<CanonicalEffectPayload>(await payloads.GetAsync(reference));
        var exported = Assert.IsType<object?[]>(first.Values["/tags"]);
        Assert.NotSame(callerOwned, exported);
        Assert.Equal(new object?[] { "red", "blue" }, exported);

        exported[1] = "changed-export";
        Assert.Equal("changed-export", exported[1]);
        var next = Assert.IsType<CanonicalEffectPayload>(await payloads.GetAsync(reference));
        var nextVector = Assert.IsType<object?[]>(next.Values["/tags"]);
        Assert.NotSame(exported, nextVector);
        Assert.Equal(new object?[] { "red", "blue" }, nextVector);
    }

    [Fact]
    public async Task Snapshots_preserve_scalar_types_nulls_array_types_and_ordinal_keys()
    {
        var date = new DateOnly(2026, 10, 18);
        var timestamp = new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.FromHours(2));
        object?[] vector = ["label", null, 7L, 12.5m, true, date, timestamp];
        string[] covariant = ["red", "blue"];
        var payloads = new InMemoryProtectedEffectPayloadStore();
        var reference = await payloads.SaveAsync(new DryRunId("representation"), 0,
            new CanonicalEffectPayload("records.types/v1", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["/vector"] = vector,
                ["/strings"] = covariant,
                ["/text"] = "plain",
                ["/null"] = null,
                ["/integer"] = 7L,
                ["/decimal"] = 12.5m,
                ["/boolean"] = true,
                ["/date"] = date,
                ["/timestamp"] = timestamp,
                ["/Case"] = "upper",
                ["/case"] = "lower",
            }));
        vector[0] = "changed-input";
        covariant[0] = "changed-input";
        Assert.Equal("changed-input", vector[0]);
        Assert.Equal("changed-input", covariant[0]);

        var snapshot = Assert.IsType<CanonicalEffectPayload>(await payloads.GetAsync(reference));
        var copied = Assert.IsType<object?[]>(snapshot.Values["/vector"]);
        Assert.NotSame(vector, copied);
        Assert.Collection(copied,
            value => Assert.Equal("label", Assert.IsType<string>(value)),
            value => Assert.Null(value),
            value => Assert.Equal(7L, Assert.IsType<long>(value)),
            value => Assert.Equal(12.5m, Assert.IsType<decimal>(value)),
            value => Assert.True(Assert.IsType<bool>(value)),
            value => Assert.Equal(new DateOnly(2026, 10, 18), Assert.IsType<DateOnly>(value)),
            value => Assert.Equal(
                new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.FromHours(2)),
                Assert.IsType<DateTimeOffset>(value)));
        var copiedStrings = Assert.IsType<string[]>(snapshot.Values["/strings"]);
        Assert.NotSame(covariant, copiedStrings);
        Assert.Equal(new[] { "red", "blue" }, copiedStrings);
        Assert.Equal("plain", Assert.IsType<string>(snapshot.Values["/text"]));
        Assert.Null(snapshot.Values["/null"]);
        Assert.Equal(7L, Assert.IsType<long>(snapshot.Values["/integer"]));
        Assert.Equal(12.5m, Assert.IsType<decimal>(snapshot.Values["/decimal"]));
        Assert.True(Assert.IsType<bool>(snapshot.Values["/boolean"]));
        Assert.Equal(new DateOnly(2026, 10, 18), Assert.IsType<DateOnly>(snapshot.Values["/date"]));
        var copiedTimestamp = Assert.IsType<DateTimeOffset>(snapshot.Values["/timestamp"]);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.FromHours(2)), copiedTimestamp);
        Assert.Equal(TimeSpan.FromHours(2), copiedTimestamp.Offset);
        Assert.Equal("upper", snapshot.Values["/Case"]);
        Assert.Equal("lower", snapshot.Values["/case"]);
        Assert.False(snapshot.Values.ContainsKey("/CASE"));

        copiedStrings[1] = "changed-export";
        Assert.Equal("changed-export", copiedStrings[1]);
        var next = Assert.IsType<CanonicalEffectPayload>(await payloads.GetAsync(reference));
        Assert.Equal(new[] { "red", "blue" }, Assert.IsType<string[]>(next.Values["/strings"]));
    }

    [Fact]
    public async Task Reference_conflicts_unknown_reads_and_cancellation_keep_their_existing_semantics()
    {
        var payloads = new InMemoryProtectedEffectPayloadStore();
        var id = new DryRunId("reference");
        var payload = new CanonicalEffectPayload("records.tags/v1",
            new Dictionary<string, object?> { ["/tags"] = new object?[] { "red", "blue" } });
        var reference = await payloads.SaveAsync(id, 3, payload);

        Assert.Equal("protected-effect://reference/3", reference);
        Assert.Null(await payloads.GetAsync("protected-effect://unknown/3"));
        await Assert.ThrowsAsync<ExchangeRunConflictException>(() =>
            payloads.SaveAsync(id, 3, payload).AsTask());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            payloads.GetAsync(reference, cancellation.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            payloads.SaveAsync(new DryRunId("cancelled"), 0, payload, cancellation.Token).AsTask());
        Assert.Null(await payloads.GetAsync("protected-effect://cancelled/0"));
        var retained = Assert.IsType<CanonicalEffectPayload>(await payloads.GetAsync(reference));
        Assert.Equal(new object?[] { "red", "blue" }, Assert.IsType<object?[]>(retained.Values["/tags"]));
    }

    private sealed class ObservingTargetPort : ICanonicalTargetCommandPort
    {
        public int LedgerReads { get; private set; }
        public int WriteAttempts { get; private set; }

        public ValueTask<EffectLedgerEntry?> GetOutcomeAsync(
            EffectIdempotencyIdentity identity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LedgerReads++;
            return ValueTask.FromResult<EffectLedgerEntry?>(null);
        }

        public ValueTask<EffectClaim> ClaimAsync(
            BatchIdentity batchIdentity, EffectIdempotencyIdentity effectIdentity, AttemptId attemptId,
            DateTimeOffset claimedAt, DateTimeOffset leaseUntil, CancellationToken cancellationToken = default)
            => throw UnexpectedWrite();

        public ValueTask<EffectTerminalOutcome> ExecuteAndRecordAsync(
            CanonicalRecordsCommand command, CancellationToken cancellationToken = default)
            => throw UnexpectedWrite();

        public ValueTask<EffectTerminalOutcome> CorrectAndRecordAsync(
            CanonicalForwardCorrectionCommand command, CancellationToken cancellationToken = default)
            => throw UnexpectedWrite();

        private InvalidOperationException UnexpectedWrite()
        {
            WriteAttempts++;
            return new InvalidOperationException("Dry runs must not write to the target.");
        }
    }
}
