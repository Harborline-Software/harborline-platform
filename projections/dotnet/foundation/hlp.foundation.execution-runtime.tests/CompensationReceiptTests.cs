using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.ExecutionRuntime;
using Xunit;

namespace Harborline.Foundation.ExecutionRuntime.Tests;

/// <summary>
/// T-1028: literal identities and refusals pin the owner-approved reciprocal association contract.
/// Receipt metadata is deliberately shared or different; neither is an association oracle.
/// </summary>
public sealed class CompensationReceiptTests
{
    private static readonly TenantId Tenant = new("tenant-a");
    private static readonly TenantId OtherTenant = new("tenant-b");
    private static readonly EffectId Original = new("original-a");
    private static readonly EffectId Compensation = new("compensation-c");
    private static readonly EffectId OtherEffect = new("other-effect");
    private static readonly DateTimeOffset Started = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "T-1028: compensated missing target is reported separately from recording")]
    public Task Compensated_cross_check_reports_missing_compensation_receipt() =>
        MissingTarget(EffectStatus.Compensated);

    [Fact(DisplayName = "T-1028: compensation-failed missing target is reported separately from recording")]
    public Task Compensation_failed_cross_check_reports_missing_compensation_receipt() =>
        MissingTarget(EffectStatus.CompensationFailed);

    [Fact(DisplayName = "T-1028: compensated original accepts its independently recorded compensation")]
    public Task Compensated_cross_check_accepts_recorded_compensation() =>
        RecordedTarget(EffectStatus.Compensated, EffectStatus.Succeeded);

    [Fact(DisplayName = "T-1028: compensation-failed original accepts its independently recorded compensation")]
    public Task Compensation_failed_cross_check_accepts_recorded_compensation() =>
        RecordedTarget(EffectStatus.CompensationFailed, EffectStatus.Failed);

    [Theory]
    [InlineData(EffectStatus.Compensated)]
    [InlineData(EffectStatus.CompensationFailed)]
    public async Task Compensation_cross_check_cannot_resolve_another_tenants_receipt(EffectStatus status)
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        var original = await ledger.RecordAsync(OriginalReceipt(status));
        var otherTenantTarget = TargetReceipt() with { TenantId = OtherTenant };
        await ledger.RecordAsync(otherTenantTarget);

        var missing = await Invalid(() => ledger.CheckCompensationAsync(Tenant, Original).AsTask());
        Assert.Equal(
            "execution.effect_receipt_invalid: Original effect receipt 'original-a' names compensation receipt 'compensation-c', which the tenant does not hold.",
            missing.Message);

        await ledger.RecordAsync(TargetReceipt());
        await ledger.CheckCompensationAsync(Tenant, Original);

        Assert.Same(original, await ledger.GetAsync(Tenant, Original));
        Assert.Same(otherTenantTarget, await ledger.GetAsync(OtherTenant, Compensation));
    }

    [Theory]
    [InlineData(EffectStatus.Compensated)]
    [InlineData(EffectStatus.CompensationFailed)]
    public async Task Compensation_cross_check_rejects_unrelated_or_legacy_effect_receipt(EffectStatus status)
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        await ledger.RecordAsync(OriginalReceipt(status));
        // Same run, action, capability and fingerprint as the original; no authoritative backlink.
        var legacyTarget = await ledger.RecordAsync(Receipt(Compensation));

        var refused = await Invalid(() => ledger.CheckCompensationAsync(Tenant, Original).AsTask());

        Assert.Equal(
            "execution.effect_receipt_invalid: Compensation receipt 'compensation-c' does not identify original effect 'original-a' in the same tenant.",
            refused.Message);
        Assert.Same(legacyTarget, await ledger.GetAsync(Tenant, Compensation));
        Assert.Null(legacyTarget.CompensatesEffectId);
    }

    [Theory]
    [InlineData(EffectStatus.Compensated)]
    [InlineData(EffectStatus.CompensationFailed)]
    public async Task Compensation_cross_check_rejects_a_binding_to_another_original_effect(EffectStatus status)
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        await ledger.RecordAsync(OriginalReceipt(status));
        var target = await ledger.RecordAsync(TargetReceipt() with { CompensatesEffectId = OtherEffect });

        var refused = await Invalid(() => ledger.CheckCompensationAsync(Tenant, Original).AsTask());

        Assert.Contains("'original-a'", refused.Message);
        Assert.Contains("'compensation-c'", refused.Message);
        Assert.Equal(new EffectId("other-effect"), target.CompensatesEffectId);
    }

    [Fact]
    public async Task One_compensation_receipt_cannot_attest_to_two_original_effects()
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        await ledger.RecordAsync(OriginalReceipt(EffectStatus.Compensated));
        await ledger.RecordAsync(OriginalReceipt(EffectStatus.CompensationFailed) with { EffectId = OtherEffect });
        await ledger.RecordAsync(TargetReceipt());

        await ledger.CheckCompensationAsync(Tenant, Original);
        var refused = await Invalid(() => ledger.CheckCompensationAsync(Tenant, OtherEffect).AsTask());

        Assert.Contains("'other-effect'", refused.Message);
        Assert.Contains("'compensation-c'", refused.Message);
    }

    [Fact]
    public async Task Compensation_can_name_its_original_and_own_compensation_without_recursive_checking()
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        await ledger.RecordAsync(OriginalReceipt(EffectStatus.Compensated));
        var target = await ledger.RecordAsync(TargetReceipt() with
        {
            Status = EffectStatus.CompensationFailed,
            CompensationEffectId = OtherEffect,
        });

        await ledger.CheckCompensationAsync(Tenant, Original);
        var missingNext = await Invalid(() => ledger.CheckCompensationAsync(Tenant, Compensation).AsTask());

        Assert.Equal(new EffectId("original-a"), target.CompensatesEffectId);
        Assert.Equal(new EffectId("other-effect"), target.CompensationEffectId);
        Assert.Contains("'compensation-c'", missingNext.Message);
        Assert.Contains("'other-effect'", missingNext.Message);
    }

    [Theory]
    [InlineData(EffectStatus.Succeeded)]
    [InlineData(EffectStatus.Failed)]
    [InlineData(EffectStatus.Ambiguous)]
    [InlineData(EffectStatus.Compensated)]
    [InlineData(EffectStatus.CompensationFailed)]
    public async Task Checker_attests_linkage_without_target_status_or_workflow_rules(EffectStatus targetStatus)
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        await ledger.RecordAsync(OriginalReceipt(EffectStatus.Compensated));
        await ledger.RecordAsync(TargetReceipt() with
        {
            Status = targetStatus,
            WorkflowRunId = new RunId(new RunKind("workflow-run"), Guid.Parse("20101010-1000-4000-8000-000000000002")),
            ActionNodeId = "different-action",
            CapabilityReference = "service:different-capability",
            RequestFingerprint = "sha256:" + new string('b', 64),
            CompensationEffectId = targetStatus is EffectStatus.Compensated or EffectStatus.CompensationFailed ? OtherEffect : null,
            Reconciliation = targetStatus == EffectStatus.Ambiguous
                ? new EffectReconciliation("service.lookup", "unknown", OtherEffect)
                : null,
        });

        await ledger.CheckCompensationAsync(Tenant, Original);
    }

    [Theory]
    [InlineData(EffectStatus.Succeeded)]
    [InlineData(EffectStatus.Failed)]
    [InlineData(EffectStatus.Ambiguous)]
    public async Task Other_original_statuses_impose_no_compensation_check(EffectStatus status)
    {
        var original = Receipt(Original) with
        {
            Status = status,
            CompensationEffectId = status == EffectStatus.Ambiguous ? null : Compensation,
            Reconciliation = status == EffectStatus.Ambiguous
                ? new EffectReconciliation("service.lookup", "unknown", OtherEffect)
                : null,
        };
        var store = new ReadOnlyStore(original, null);

        await new EffectReceiptLedger(store).CheckCompensationAsync(Tenant, Original);

        Assert.Equal(new[] { (Tenant, Original) }, store.Reads);
    }

    [Fact]
    public async Task Checker_uses_two_tenant_scoped_reads_without_writes()
    {
        var store = new ReadOnlyStore(OriginalReceipt(EffectStatus.Compensated), TargetReceipt());

        await new EffectReceiptLedger(store).CheckCompensationAsync(Tenant, Original);

        Assert.Equal(new[] { (Tenant, Original), (Tenant, Compensation) }, store.Reads);
    }

    [Fact]
    public async Task Missing_original_is_reported_by_its_known_identity()
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());

        var refused = await Invalid(() => ledger.CheckCompensationAsync(Tenant, Original).AsTask());

        Assert.Equal("execution.effect_receipt_invalid: The tenant holds no original effect receipt 'original-a'.", refused.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Returned_receipts_must_match_the_requested_tenant_and_identity(bool corruptTarget, bool corruptTenant)
    {
        var original = OriginalReceipt(EffectStatus.Compensated);
        var target = TargetReceipt();
        var corrupt = corruptTarget ? target : original;
        corrupt = corruptTenant ? corrupt with { TenantId = OtherTenant } : corrupt with { EffectId = OtherEffect };
        var store = new ReadOnlyStore(corruptTarget ? original : corrupt, corruptTarget ? corrupt : target);

        var refused = await Invalid(() => new EffectReceiptLedger(store).CheckCompensationAsync(Tenant, Original).AsTask());

        Assert.Contains("'original-a'", refused.Message);
        Assert.Equal(corruptTarget ? 2 : 1, store.Reads.Count);
    }

    [Fact]
    public async Task Stored_terminal_original_without_a_forward_link_is_refused()
    {
        var store = new ReadOnlyStore(OriginalReceipt(EffectStatus.Compensated) with { CompensationEffectId = null }, TargetReceipt());

        var refused = await Invalid(() => new EffectReceiptLedger(store).CheckCompensationAsync(Tenant, Original).AsTask());

        Assert.Equal("execution.effect_receipt_invalid: Original effect receipt 'original-a' must name its separate compensation receipt.", refused.Message);
        Assert.Single(store.Reads);
    }

    [Fact]
    public async Task Stored_terminal_original_with_a_self_link_is_refused()
    {
        var store = new ReadOnlyStore(OriginalReceipt(EffectStatus.CompensationFailed) with { CompensationEffectId = Original }, TargetReceipt());

        var refused = await Invalid(() => new EffectReceiptLedger(store).CheckCompensationAsync(Tenant, Original).AsTask());

        Assert.Equal("execution.effect_receipt_invalid: Original effect receipt 'original-a' cannot name itself as its compensation receipt.", refused.Message);
        Assert.Single(store.Reads);
    }

    [Fact]
    public async Task Stored_terminal_original_with_a_default_forward_id_is_refused()
    {
        var store = new ReadOnlyStore(OriginalReceipt(EffectStatus.Compensated) with { CompensationEffectId = default(EffectId) }, TargetReceipt());

        var refused = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => new EffectReceiptLedger(store).CheckCompensationAsync(Tenant, Original).AsTask());

        Assert.Equal("execution.effect_identity_invalid", refused.Code);
        Assert.Single(store.Reads);
    }

    [Theory]
    [InlineData(EffectStatus.Compensated)]
    [InlineData(EffectStatus.CompensationFailed)]
    public async Task Stored_malformed_compensation_backlink_retains_identity_refusal(EffectStatus status)
    {
        var store = new ReadOnlyStore(
            OriginalReceipt(status), TargetReceipt() with { CompensatesEffectId = default(EffectId) });

        var refused = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => new EffectReceiptLedger(store).CheckCompensationAsync(Tenant, Original).AsTask());

        Assert.Equal("execution.effect_identity_invalid", refused.Code);
        Assert.Equal(new[] { (Tenant, Original), (Tenant, Compensation) }, store.Reads);
    }

    [Fact]
    public async Task Recording_refuses_a_default_backlink()
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());

        var refused = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(
            () => ledger.RecordAsync(TargetReceipt() with { CompensatesEffectId = default(EffectId) }).AsTask());

        Assert.Equal("execution.effect_identity_invalid", refused.Code);
        Assert.Null(await ledger.GetAsync(Tenant, Compensation));
    }

    [Fact]
    public async Task Recording_refuses_a_self_backlink()
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());

        var refused = await Invalid(() => ledger.RecordAsync(TargetReceipt() with { CompensatesEffectId = Compensation }).AsTask());

        Assert.Equal("execution.effect_receipt_invalid: A compensation receipt cannot identify itself as its original effect.", refused.Message);
        Assert.Null(await ledger.GetAsync(Tenant, Compensation));
    }

    [Fact]
    public async Task Recording_a_backlink_does_not_require_an_original_lookup()
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        var target = await ledger.RecordAsync(TargetReceipt());

        Assert.Same(target, await ledger.GetAsync(Tenant, Compensation));
        Assert.Null(await ledger.GetAsync(Tenant, Original));
    }

    [Fact]
    public async Task Checker_validates_tenant_and_original_identity_before_reading()
    {
        var store = new ReadOnlyStore(OriginalReceipt(EffectStatus.Compensated), TargetReceipt());
        var ledger = new EffectReceiptLedger(store);

        var tenantRefusal = await Assert.ThrowsAsync<ArgumentException>(() => ledger.CheckCompensationAsync(default, Original).AsTask());
        var idRefusal = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => ledger.CheckCompensationAsync(Tenant, default).AsTask());

        Assert.Equal("tenantId", tenantRefusal.ParamName);
        Assert.Equal("execution.effect_identity_invalid", idRefusal.Code);
        Assert.Empty(store.Reads);
    }

    [Fact]
    public async Task Checker_honors_cancellation_before_original_lookup()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new ReadOnlyStore(OriginalReceipt(EffectStatus.Compensated), TargetReceipt());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new EffectReceiptLedger(store).CheckCompensationAsync(Tenant, Original, cancellation.Token).AsTask());

        Assert.Empty(store.Reads);
    }

    [Fact]
    public async Task Checker_forwards_cancellation_to_compensation_lookup()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new ReadOnlyStore(OriginalReceipt(EffectStatus.Compensated), TargetReceipt(), cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new EffectReceiptLedger(store).CheckCompensationAsync(Tenant, Original, cancellation.Token).AsTask());

        Assert.Single(store.Reads);
    }

    private static async Task MissingTarget(EffectStatus status)
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        var original = await ledger.RecordAsync(OriginalReceipt(status));

        var refused = await Invalid(() => ledger.CheckCompensationAsync(Tenant, Original).AsTask());

        Assert.Equal(
            "execution.effect_receipt_invalid: Original effect receipt 'original-a' names compensation receipt 'compensation-c', which the tenant does not hold.",
            refused.Message);
        Assert.Same(original, await ledger.GetAsync(Tenant, Original));
        Assert.Null(await ledger.GetAsync(Tenant, Compensation));
    }

    private static async Task RecordedTarget(EffectStatus originalStatus, EffectStatus targetStatus)
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        var original = await ledger.RecordAsync(OriginalReceipt(originalStatus));
        var target = await ledger.RecordAsync(TargetReceipt() with { Status = targetStatus });

        await ledger.CheckCompensationAsync(Tenant, Original);

        Assert.Same(original, await ledger.GetAsync(Tenant, Original));
        Assert.Same(target, await ledger.GetAsync(Tenant, Compensation));
        Assert.Equal(new EffectId("original-a"), target.CompensatesEffectId);
    }

    private static async Task<ExecutionRuntimeRefusedException> Invalid(Func<Task> operation)
    {
        var refused = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(operation);
        Assert.Equal("execution.effect_receipt_invalid", refused.Code);
        return refused;
    }

    private static EffectReceipt OriginalReceipt(EffectStatus status) => Receipt(Original) with
    {
        Status = status,
        CompensationEffectId = Compensation,
    };

    private static EffectReceipt TargetReceipt() => Receipt(Compensation) with { CompensatesEffectId = Original };

    private static EffectReceipt Receipt(EffectId id) => new()
    {
        EffectId = id,
        TenantId = Tenant,
        WorkflowRunId = new RunId(new RunKind("workflow-run"), Guid.Parse("20101010-1000-4000-8000-000000000001")),
        ActionNodeId = "invoke-service",
        CapabilityReference = "service:orders",
        CapabilityVersion = "1.0.0",
        AuthorityFloor = EffectAuthorityFloor.AP,
        Status = EffectStatus.Succeeded,
        StartedUtc = Started,
        CompletedUtc = Started.AddSeconds(1),
        RequestFingerprint = "sha256:" + new string('a', 64),
        RetryProfile = RetryProfileName.ExternalApiStandard,
    };

    private sealed class ReadOnlyStore(EffectReceipt original, EffectReceipt? target, Action? afterOriginal = null) : IEffectReceiptStore
    {
        public List<(TenantId Tenant, EffectId Effect)> Reads { get; } = [];

        public ValueTask CreateAsync(EffectReceipt receipt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The cross-check must not write receipts.");

        public ValueTask<EffectReceipt?> GetAsync(TenantId tenantId, EffectId effectId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads.Add((tenantId, effectId));
            if (Reads.Count == 1)
            {
                afterOriginal?.Invoke();
                return ValueTask.FromResult<EffectReceipt?>(original);
            }

            return ValueTask.FromResult(target);
        }
    }
}
