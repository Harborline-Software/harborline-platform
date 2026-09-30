using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ExecutionRuntime.Tests;

/// <summary>Pins each effect-receipt refusal's code and message, and the accepted boundary beside it.</summary>
public sealed class EffectReceiptRefusalTests
{
    private static readonly TenantId Tenant = new("tenant-a");
    private static readonly RunId WorkflowRun = new(new RunKind("workflow-run"), Guid.Parse("8b82c87a-20dc-491d-b870-0a2f0dc81f1a"));
    private static readonly EffectId Effect = new("effect:customer-42:email-v1");
    private static readonly DateTimeOffset Started = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const string Invalid = ExecutionRuntimeRefusals.EffectReceiptInvalid;

    private static async Task<ExecutionRuntimeRefusedException> Refused(EffectReceipt receipt) =>
        await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => new EffectReceiptLedger(new InMemoryEffectReceiptStore()).RecordAsync(receipt).AsTask());

    private static async Task Accepted(EffectReceipt receipt) =>
        Assert.Same(receipt, await new EffectReceiptLedger(new InMemoryEffectReceiptStore()).RecordAsync(receipt));

    private static async Task RefusedWith(EffectReceipt receipt, string code, string message)
    {
        var refused = await Refused(receipt);
        Assert.Equal(code, refused.Code);
        Assert.Equal($"{code}: {message}", refused.Message);
    }

    [Fact(DisplayName = "T-528 ck-5: effect identity refuses empty, blank, control and over-long values and accepts 256 characters")]
    public void Effect_identity_bounds()
    {
        const string Message = "An effect identity must be a non-empty, non-control string of at most 256 characters.";
        foreach (var bad in new string?[] { null, "", " ", "a\nb", new string('x', 257) })
        {
            var refused = Assert.Throws<ExecutionRuntimeRefusedException>(() => new EffectId(bad!));
            Assert.Equal(ExecutionRuntimeRefusals.EffectIdentityInvalid, refused.Code);
            Assert.Equal($"{ExecutionRuntimeRefusals.EffectIdentityInvalid}: {Message}", refused.Message);
        }

        Assert.Equal(256, new EffectId(new string('x', 256)).Value.Length);
        Assert.Equal("a b", new EffectId("a b").ToString());
        Assert.Equal(string.Empty, default(EffectId).ToString());
    }

    [Fact(DisplayName = "T-528 ck-5: a duplicate receipt refusal names the effect")]
    public async Task Duplicate_names_the_effect()
    {
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        await ledger.RecordAsync(Receipt());
        var refused = await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => ledger.RecordAsync(Receipt()).AsTask());
        Assert.Equal($"{ExecutionRuntimeRefusals.EffectReceiptDuplicate}: Effect 'effect:customer-42:email-v1' already has a receipt for this tenant.", refused.Message);
    }

    [Fact(DisplayName = "T-528 ck-5: the ledger and store guard null receipts, cancellation, missing tenants and missing effect ids")]
    public async Task Ledger_and_store_guard_arguments()
    {
        var store = new InMemoryEffectReceiptStore();
        var ledger = new EffectReceiptLedger(store);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAsync<ArgumentNullException>(() => ledger.RecordAsync(null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.CreateAsync(null!).AsTask());
        Assert.Throws<ArgumentNullException>(() => new EffectReceiptLedger(null!));
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.CreateAsync(Receipt(), cancelled.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.GetAsync(Tenant, Effect, cancelled.Token).AsTask());
        Assert.Null(await store.GetAsync(Tenant, Effect));

        var noTenant = Assert.Throws<ArgumentException>(() => ledger.GetAsync(default, Effect));
        Assert.StartsWith("An effect receipt requires a tenant.", noTenant.Message);
        Assert.Equal("tenantId", noTenant.ParamName);
        var noEffect = Assert.Throws<ExecutionRuntimeRefusedException>(() => ledger.GetAsync(Tenant, default));
        Assert.Equal(ExecutionRuntimeRefusals.EffectIdentityInvalid, noEffect.Code);
        Assert.Equal($"{ExecutionRuntimeRefusals.EffectIdentityInvalid}: An effect receipt requires an effect identity.", noEffect.Message);
        Assert.Throws<ArgumentException>(() => ledger.GetAsync(default, default));
        await Assert.ThrowsAsync<ArgumentException>(() => ledger.RecordAsync(Receipt() with { TenantId = default }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => ledger.RecordAsync(Receipt() with { TenantId = new TenantId { Value = " " } }).AsTask());
        await Assert.ThrowsAsync<ExecutionRuntimeRefusedException>(() => ledger.RecordAsync(Receipt() with { EffectId = default }).AsTask());
    }

    [Fact(DisplayName = "T-528 ck-5: the receipt names a workflow-run identity")]
    public async Task Workflow_run_kind_is_required()
    {
        var other = new RunId(new RunKind("exchange-run"), Guid.NewGuid());
        await RefusedWith(Receipt() with { WorkflowRunId = other }, Invalid, "An effect receipt must name its workflow-run identity.");
    }

    [Theory(DisplayName = "T-528 ck-5: action node, capability reference and version are required text")]
    [InlineData("action", "action node id")]
    [InlineData("reference", "capability reference")]
    [InlineData("version", "capability version")]
    public async Task Required_text_fields(string field, string name)
    {
        foreach (var blank in new string?[] { null, "", "  " })
        {
            var receipt = field switch
            {
                "action" => Receipt() with { ActionNodeId = blank! },
                "reference" => Receipt() with { CapabilityReference = blank! },
                _ => Receipt() with { CapabilityVersion = blank! },
            };
            await RefusedWith(receipt, Invalid, $"An effect receipt requires a {name}.");
        }
    }

    [Fact(DisplayName = "T-528 ck-5: an unknown authority floor or status is refused on its own")]
    public async Task Unknown_enum_values()
    {
        const string Message = "An effect receipt must use known authority and status values.";
        await RefusedWith(Receipt() with { AuthorityFloor = (EffectAuthorityFloor)9 }, Invalid, Message);
        await RefusedWith(Receipt() with { Status = (EffectStatus)9 }, Invalid, Message);
        await Accepted(Receipt() with { AuthorityFloor = EffectAuthorityFloor.CP });
    }

    [Fact(DisplayName = "T-528 ck-5: an effect may complete at the instant it starts but never before")]
    public async Task Completion_time_boundary()
    {
        await RefusedWith(Receipt() with { CompletedUtc = Started.AddTicks(-1) }, Invalid, "An effect receipt cannot complete before it starts.");
        await Accepted(Receipt() with { CompletedUtc = Started });
    }

    [Theory(DisplayName = "T-528 ck-5: the request fingerprint is exactly sha256: plus 64 lowercase hex characters")]
    [InlineData("sha256:")]
    [InlineData("sha512:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256:Aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256:gaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256:/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256::aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("sha256:`aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("xsha256aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Fingerprint_shape(string fingerprint)
    {
        await RefusedWith(Receipt() with { RequestFingerprint = fingerprint }, Invalid, "An effect receipt requires a non-secret SHA-256 request fingerprint.");
    }

    [Fact(DisplayName = "T-528 ck-5: fingerprint boundary characters and a null fingerprint")]
    public async Task Fingerprint_boundaries()
    {
        await Refused(Receipt() with { RequestFingerprint = null! });
        await Accepted(Receipt() with { RequestFingerprint = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" });
        await Accepted(Receipt() with { RequestFingerprint = "sha256:9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f" });
    }

    [Fact(DisplayName = "T-528 ck-5: each attempt-ordering rule refuses alone and equal instants are accepted")]
    public async Task Attempt_ordering()
    {
        const string Message = "Effect attempts must be ordered and cannot end before they start.";
        await RefusedWith(Receipt() with { Attempts = [new EffectAttempt(2, Started, Started, null, null)] }, Invalid, Message);
        await RefusedWith(Receipt() with { Attempts = [new EffectAttempt(1, Started.AddTicks(-1), null, null, null)] }, Invalid, Message);
        await RefusedWith(Receipt() with { Attempts = [new EffectAttempt(1, Started.AddSeconds(1), Started, null, null)] }, Invalid, Message);
        await RefusedWith(Receipt() with { Attempts = [new EffectAttempt(1, Started, Started, null, null), new EffectAttempt(1, Started, Started, null, null)] }, Invalid, Message);
        await Accepted(Receipt() with { Attempts = [new EffectAttempt(1, Started, Started, null, null), new EffectAttempt(2, Started, null, null, null)] });
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        await Assert.ThrowsAsync<ArgumentNullException>(() => ledger.RecordAsync(Receipt() with { Attempts = null! }).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => ledger.RecordAsync(Receipt() with { Attempts = [null!] }).AsTask());
    }

    [Theory(DisplayName = "T-528 ck-5: only opaque secret references are retained")]
    [InlineData("hunter2")]
    [InlineData("secret://")]
    [InlineData("secretref:")]
    [InlineData("secret://has space")]
    [InlineData("secretref:has space")]
    [InlineData("secret://a@b")]
    [InlineData("secretref:a$b")]
    [InlineData("secret:/a")]
    public async Task Secret_reference_shape(string reference)
    {
        await RefusedWith(Receipt() with { SecretReferenceIds = [reference] }, ExecutionRuntimeRefusals.SecretReferenceInvalid, "Effect receipts retain opaque secret references, never secret values.");
    }

    [Fact(DisplayName = "T-528 ck-5: every permitted secret reference character is accepted")]
    public async Task Secret_reference_characters()
    {
        await Accepted(Receipt() with { SecretReferenceIds = ["secret://a.b_c:d/e-f9Z", "secretref:a.b_c:d/e-f9Z"] });
        await Accepted(Receipt() with { SecretReferenceIds = [] });
        var ledger = new EffectReceiptLedger(new InMemoryEffectReceiptStore());
        await Assert.ThrowsAsync<ArgumentNullException>(() => ledger.RecordAsync(Receipt() with { SecretReferenceIds = null! }).AsTask());
    }

    [Fact(DisplayName = "T-528 ck-7: reconciliation state is required when ambiguous, forbidden otherwise, and its fields are required")]
    public async Task Reconciliation_rules()
    {
        var reconciliation = new EffectReconciliation("smtp.lookup", "delivered", Effect);
        await RefusedWith(Receipt() with { Status = EffectStatus.Ambiguous }, ExecutionRuntimeRefusals.EffectReconciliationRequired, "An ambiguous effect receipt must carry capability-specific reconciliation state.");
        await RefusedWith(Receipt() with { Reconciliation = reconciliation }, Invalid, "Only an ambiguous effect receipt carries reconciliation state.");
        var ambiguous = Receipt() with { Status = EffectStatus.Ambiguous };
        await RefusedWith(ambiguous with { Reconciliation = reconciliation with { CapabilityReconciliation = " " } }, Invalid, "An effect receipt requires a capability reconciliation.");
        await RefusedWith(ambiguous with { Reconciliation = reconciliation with { Outcome = "" } }, Invalid, "An effect receipt requires a reconciliation outcome.");
        await RefusedWith(ambiguous with { Reconciliation = reconciliation with { ResolvesToReceiptId = default } }, ExecutionRuntimeRefusals.EffectIdentityInvalid, "An effect receipt requires an effect identity.");
        await Accepted(ambiguous with { Reconciliation = reconciliation });
    }

    [Fact(DisplayName = "T-528 ck-5: compensation refusals carry their contract messages")]
    public async Task Compensation_messages()
    {
        var other = new EffectId("effect:customer-42:email-v1:compensation");
        await RefusedWith(Receipt() with { Status = EffectStatus.Compensated }, Invalid, "A compensated effect receipt must name its separate compensation effect.");
        await RefusedWith(Receipt() with { Status = EffectStatus.Compensated, CompensationEffectId = Effect }, Invalid, "An effect cannot compensate itself.");
        await RefusedWith(Receipt() with { CompensationEffectId = default(EffectId) }, ExecutionRuntimeRefusals.EffectIdentityInvalid, "An effect receipt requires an effect identity.");
        await RefusedWith(
            Receipt() with { Status = EffectStatus.Ambiguous, Reconciliation = new EffectReconciliation("smtp.lookup", "unknown", Effect), CompensationEffectId = other },
            ExecutionRuntimeRefusals.BlindCompensationRefused,
            "An ambiguous effect must reconcile before any compensation is recorded.");
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
        RetryProfile = RetryProfileName.ExternalApiStandard,
        SecretReferenceIds = ["secret://tenant-a/smtp"],
    };
}
