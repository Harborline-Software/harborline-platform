using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ExecutionRuntime.Tests;

/// <summary>Actual ledger/store ownership boundaries, with independent literal evidence after mutation.</summary>
public sealed class EffectReceiptStoreSnapshotTests
{
    private static readonly TenantId Tenant = new("tenant-a");
    private static readonly EffectId Effect = new("effect:snapshot");
    private static readonly DateTimeOffset Started = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task caller_input_and_actual_RecordAsync_return_cannot_mutate_recorded_sequences(bool ambiguous)
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        var attempts = new List<EffectAttempt> { OriginalAttempt() };
        var references = new List<string> { "secretref:original" };
        var input = Receipt(attempts, references, ambiguous);

        var returned = await ledger.RecordAsync(input);

        attempts[0] = new(41, Started, Started.AddSeconds(1), "input-mutated", "409");
        attempts.Add(new(2, Started, Started.AddSeconds(1), "input-extra", "503"));
        references[0] = "secretref:input-mutated";
        references.Add("secretref:input-extra");
        Assert.Equal(41, attempts[0].Number);
        Assert.Equal(2, attempts.Count);
        Assert.Equal("secretref:input-mutated", references[0]);
        Assert.Equal(2, references.Count);
        AssertOriginal(await ledger.GetAsync(Tenant, Effect), ambiguous);

        // Attack the actual producer return as well, without an object-identity assertion.
        var returnedAttempts = Assert.IsAssignableFrom<IList<EffectAttempt>>(returned.Attempts);
        var returnedReferences = Assert.IsAssignableFrom<IList<string>>(returned.SecretReferenceIds);
        returnedAttempts[0] = new(42, Started, Started.AddSeconds(1), "return-mutated", "410");
        returnedReferences[0] = "secretref:return-mutated";
        Assert.Equal(42, returned.Attempts[0].Number);
        Assert.Equal("secretref:return-mutated", returned.SecretReferenceIds[0]);
        AssertOriginal(await ledger.GetAsync(Tenant, Effect), ambiguous);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_exports_cannot_mutate_the_recorded_attempt_or_reference_by_index(bool ambiguous)
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        await ledger.RecordAsync(Receipt(new List<EffectAttempt> { OriginalAttempt() },
            new List<string> { "secretref:original" }, ambiguous));
        var exported = Assert.IsType<EffectReceipt>(await ledger.GetAsync(Tenant, Effect));
        AssertOriginal(exported, ambiguous);

        // Arrays expose writable index setters even though IList.IsReadOnly is true. Always
        // attempt that setter when the interface exists; only an explicit refusal is acceptable.
        if (exported.Attempts is IList<EffectAttempt> attempts)
        {
            var changed = false;
            try
            {
                attempts[0] = new(91, Started, Started.AddSeconds(1), "export-mutated", "500");
                changed = true;
            }
            catch (NotSupportedException) { }
            if (changed) Assert.Equal(91, exported.Attempts[0].Number);
        }
        AssertOriginal(await ledger.GetAsync(Tenant, Effect), ambiguous);

        if (exported.SecretReferenceIds is IList<string> references)
        {
            var changed = false;
            try
            {
                references[0] = "secretref:export-mutated";
                changed = true;
            }
            catch (NotSupportedException) { }
            if (changed) Assert.Equal("secretref:export-mutated", exported.SecretReferenceIds[0]);
        }
        AssertOriginal(await ledger.GetAsync(Tenant, Effect), ambiguous);
    }

    [Fact]
    public async Task snapshots_preserve_create_only_tenant_miss_and_cancellation_behavior()
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        await ledger.RecordAsync(Receipt(new List<EffectAttempt> { OriginalAttempt() },
            new List<string> { "secretref:original" }, false));

        var duplicate = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => ledger.RecordAsync(
            Receipt(new List<EffectAttempt> { OriginalAttempt() }, new List<string> { "secretref:original" }, false)).AsTask());
        Assert.Equal("execution.effect_receipt_duplicate", duplicate.Code);
        Assert.Null(await ledger.GetAsync(new TenantId("tenant-b"), Effect));
        Assert.Null(await ledger.GetAsync(Tenant, new EffectId("effect:unknown")));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = Receipt(new List<EffectAttempt> { OriginalAttempt() },
            new List<string> { "secretref:original" }, false) with { EffectId = new("effect:cancelled") };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ledger.RecordAsync(cancelled, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ledger.GetAsync(Tenant, Effect, cancellation.Token).AsTask());
        Assert.Null(await ledger.GetAsync(Tenant, new EffectId("effect:cancelled")));
        AssertOriginal(await ledger.GetAsync(Tenant, Effect), false);
    }

    private static EffectAttempt OriginalAttempt()
        => new(1, Started, Started.AddSeconds(1), "attempt-original", "202");

    private static EffectReceipt Receipt(IReadOnlyList<EffectAttempt> attempts, IReadOnlyList<string> references, bool ambiguous) => new()
    {
        EffectId = Effect,
        TenantId = Tenant,
        WorkflowRunId = new(new RunKind("workflow-run"), Guid.Parse("10101010-2000-4000-8000-000000000001")),
        ActionNodeId = "send-status",
        CapabilityReference = "service:notifications",
        CapabilityVersion = "2.1.0",
        AuthorityFloor = EffectAuthorityFloor.AP,
        Status = ambiguous ? EffectStatus.Ambiguous : EffectStatus.Succeeded,
        StartedUtc = Started,
        CompletedUtc = Started.AddSeconds(2),
        RequestFingerprint = "sha256:" + new string('a', 64),
        ExternalCorrelationId = "external-original",
        ExternalStatus = "202",
        RetryProfile = RetryProfileName.ExternalApiStandard,
        CompensationEffectId = ambiguous ? null : new EffectId("effect:compensation"),
        CompensatesEffectId = new("effect:prior"),
        Attempts = attempts,
        SecretReferenceIds = references,
        Reconciliation = ambiguous ? new("service.lookup", "pending", new EffectId("effect:resolution")) : null,
    };

    private static void AssertOriginal(EffectReceipt? actual, bool ambiguous)
    {
        var stored = Assert.IsType<EffectReceipt>(actual);
        Assert.Equal("effect:snapshot", stored.EffectId.Value);
        Assert.Equal("tenant-a", stored.TenantId.Value);
        Assert.Equal("workflow-run", stored.WorkflowRunId.Kind.Value);
        Assert.Equal(Guid.Parse("10101010-2000-4000-8000-000000000001"), stored.WorkflowRunId.Value);
        Assert.Equal("send-status", stored.ActionNodeId);
        Assert.Equal("service:notifications", stored.CapabilityReference);
        Assert.Equal("2.1.0", stored.CapabilityVersion);
        Assert.Equal(EffectAuthorityFloor.AP, stored.AuthorityFloor);
        Assert.Equal(ambiguous ? EffectStatus.Ambiguous : EffectStatus.Succeeded, stored.Status);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), stored.StartedUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 12, 0, 2, TimeSpan.Zero), stored.CompletedUtc);
        Assert.Equal("sha256:" + new string('a', 64), stored.RequestFingerprint);
        Assert.Equal("external-original", stored.ExternalCorrelationId);
        Assert.Equal("202", stored.ExternalStatus);
        Assert.Equal("external-api-standard", stored.RetryProfile.Value);
        Assert.Equal("effect:prior", stored.CompensatesEffectId?.Value);
        Assert.Equal(1, stored.AttemptCount);
        var attempt = Assert.Single(stored.Attempts);
        Assert.Equal(1, attempt.Number);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), attempt.StartedUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 12, 0, 1, TimeSpan.Zero), attempt.CompletedUtc);
        Assert.Equal("attempt-original", attempt.ExternalCorrelationId);
        Assert.Equal("202", attempt.ExternalStatus);
        Assert.Equal("secretref:original", Assert.Single(stored.SecretReferenceIds));
        if (ambiguous)
        {
            Assert.Null(stored.CompensationEffectId);
            var reconciliation = Assert.IsType<EffectReconciliation>(stored.Reconciliation);
            Assert.Equal("service.lookup", reconciliation.CapabilityReconciliation);
            Assert.Equal("pending", reconciliation.Outcome);
            Assert.Equal("effect:resolution", reconciliation.ResolvesToReceiptId.Value);
        }
        else
        {
            Assert.Equal("effect:compensation", stored.CompensationEffectId?.Value);
            Assert.Null(stored.Reconciliation);
        }
    }
}
