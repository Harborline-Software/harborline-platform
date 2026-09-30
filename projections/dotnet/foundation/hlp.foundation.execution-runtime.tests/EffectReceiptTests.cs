using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.ExecutionRuntime;
using Xunit;

namespace Harborline.Foundation.ExecutionRuntime.Tests;

public sealed class EffectReceiptTests
{
    private static readonly TenantId Tenant = new("tenant-a");
    private static readonly RunId WorkflowRun = new(new RunKind("workflow-run"), Guid.Parse("8b82c87a-20dc-491d-b870-0a2f0dc81f1a"));
    private static readonly EffectId Effect = new("effect:customer-42:email-v1");
    private static readonly DateTimeOffset Started = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "T-528 S2 ck-5: one effect has exactly one tenant-scoped receipt")]
    public async Task One_effect_has_exactly_one_receipt()
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        var receipt = Receipt();

        await ledger.RecordAsync(receipt);
        var duplicate = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => ledger.RecordAsync(receipt).AsTask());

        Assert.Equal(ExecutionRuntimeRefusals.EffectReceiptDuplicate, duplicate.Code);
        Assert.Equal(receipt, await ledger.GetAsync(Tenant, Effect));
    }

    [Fact(DisplayName = "T-528 S2 ck-5: an effect receipt retains the required non-secret execution evidence")]
    public async Task Receipt_retains_required_execution_evidence()
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        var receipt = Receipt() with
        {
            Attempts =
            [
                new EffectAttempt(1, Started, Started.AddSeconds(2), "smtp-812", "429"),
                new EffectAttempt(2, Started.AddSeconds(4), Started.AddSeconds(7), "smtp-813", "202"),
            ],
            ExternalCorrelationId = "smtp-813",
            ExternalStatus = "202",
        };

        var stored = await ledger.RecordAsync(receipt);

        Assert.Equal(2, stored.AttemptCount);
        Assert.Equal(WorkflowRun, stored.WorkflowRunId);
        Assert.Equal("send-email", stored.ActionNodeId);
        Assert.Equal("service:notifications", stored.CapabilityReference);
        Assert.Equal("2.1.0", stored.CapabilityVersion);
        Assert.Equal(EffectAuthorityFloor.AP, stored.AuthorityFloor);
        Assert.Equal("sha256:" + new string('a', 64), stored.RequestFingerprint);
        Assert.Equal(RetryProfileName.ExternalApiStandard, stored.RetryProfile);
        Assert.Equal(["secret://tenant-a/smtp"], stored.SecretReferenceIds);
    }

    [Fact(DisplayName = "T-528 S2 ck-6: effect status is the closed five-value vocabulary")]
    public void Effect_status_vocabulary_is_closed()
    {
        var statuses = Enum.GetValues<EffectStatus>();

        Assert.Equal(
            [EffectStatus.Succeeded, EffectStatus.Failed, EffectStatus.Ambiguous, EffectStatus.Compensated, EffectStatus.CompensationFailed],
            statuses);
        Assert.Equal(
            ["succeeded", "failed", "ambiguous", "compensated", "compensation-failed"],
            statuses.Select(EffectStatusVocabulary.WireName));
        Assert.Equal("99", EffectStatusVocabulary.WireName((EffectStatus)99));
    }

    [Fact(DisplayName = "T-528 S2 ck-7: ambiguity carries its capability reconciliation and refuses blind compensation")]
    public async Task Ambiguity_requires_reconciliation_and_refuses_blind_compensation()
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        var missingReconciliation = Receipt() with { Status = EffectStatus.Ambiguous };

        var missing = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => ledger.RecordAsync(missingReconciliation).AsTask());
        Assert.Equal(ExecutionRuntimeRefusals.EffectReconciliationRequired, missing.Code);

        var blindCompensation = missingReconciliation with
        {
            Reconciliation = new EffectReconciliation("smtp.lookup", "accepted", new EffectId("effect:customer-42:email-v1:resolution")),
            CompensationEffectId = new EffectId("effect:customer-42:email-v1:compensate"),
        };
        var blind = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => ledger.RecordAsync(blindCompensation).AsTask());
        Assert.Equal(ExecutionRuntimeRefusals.BlindCompensationRefused, blind.Code);

        var ambiguous = await ledger.RecordAsync(blindCompensation with { CompensationEffectId = null });
        Assert.Equal(EffectStatus.Ambiguous, ambiguous.Status);
        Assert.Equal("smtp.lookup", ambiguous.Reconciliation!.CapabilityReconciliation);
    }

    [Fact(DisplayName = "T-528 S2 ck-5: secret values are refused; receipt evidence accepts opaque secret references only")]
    public async Task Secret_values_are_refused()
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        var value = Receipt() with { SecretReferenceIds = ["hunter2"] };

        var refused = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => ledger.RecordAsync(value).AsTask());

        Assert.Equal(ExecutionRuntimeRefusals.SecretReferenceInvalid, refused.Code);
    }

    private static EffectReceipt Receipt() => new()
    {
        EffectId = Effect,
        TenantId = Tenant,
        WorkflowRunId = WorkflowRun,
        ActionNodeId = "send-email",
        CapabilityReference = "service:notifications",
        CapabilityVersion = "2.1.0",
        AuthorityFloor = EffectAuthorityFloor.AP,
        Status = EffectStatus.Succeeded,
        StartedUtc = Started,
        CompletedUtc = Started.AddSeconds(1),
        RequestFingerprint = "sha256:" + new string('a', 64),
        ExternalCorrelationId = "smtp-812",
        ExternalStatus = "202",
        RetryProfile = RetryProfileName.ExternalApiStandard,
        SecretReferenceIds = ["secret://tenant-a/smtp"],
    };
}
