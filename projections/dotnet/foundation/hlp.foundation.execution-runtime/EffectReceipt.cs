using System.Collections.Concurrent;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>An opaque, stable identity for one logical effect, shared by every delivery attempt and replay.</summary>
public readonly record struct EffectId
{
    /// <summary>Creates an opaque effect identity from its stable external value.</summary>
    public EffectId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.EffectIdentityInvalid,
                "An effect identity must be a non-empty, non-control string of at most 256 characters.");
        }

        Value = value;
    }

    /// <summary>The opaque value that identifies the logical effect.</summary>
    public string Value { get; }

    /// <summary>Returns the opaque effect identity unchanged for logs and stable links.</summary>
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>The authority floor the capability required when the effect was invoked.</summary>
public enum EffectAuthorityFloor
{
    /// <summary>The capability requires a control-plane authority decision. Wire name <c>CP</c>.</summary>
    CP = 0,

    /// <summary>The capability requires an application-plane authority decision. Wire name <c>AP</c>.</summary>
    AP = 1,
}

/// <summary>The closed status vocabulary for one effect receipt.</summary>
public enum EffectStatus
{
    /// <summary>The effect completed successfully. Wire name <c>succeeded</c>.</summary>
    Succeeded = 0,

    /// <summary>The effect completed with a known failure. Wire name <c>failed</c>.</summary>
    Failed = 1,

    /// <summary>The effect may have transmitted before its timeout; reconciliation is required. Wire name <c>ambiguous</c>.</summary>
    Ambiguous = 2,

    /// <summary>The effect's declared compensation completed. Wire name <c>compensated</c>.</summary>
    Compensated = 3,

    /// <summary>The effect's declared compensation failed. Wire name <c>compensation-failed</c>.</summary>
    CompensationFailed = 4,
}

/// <summary>Maps the closed effect-status domain to its stable wire names.</summary>
public static class EffectStatusVocabulary
{
    /// <summary>Returns the stable wire name for a known status, or its numeric value when unknown.</summary>
    public static string WireName(EffectStatus status) => status switch
    {
        EffectStatus.Succeeded => "succeeded",
        EffectStatus.Failed => "failed",
        EffectStatus.Ambiguous => "ambiguous",
        EffectStatus.Compensated => "compensated",
        EffectStatus.CompensationFailed => "compensation-failed",
        _ => ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>One infrastructure attempt in an effect's delivery history.</summary>
/// <param name="Number">The one-based position of this attempt within the effect.</param>
/// <param name="StartedUtc">When this attempt began.</param>
/// <param name="CompletedUtc">When this attempt finished.</param>
/// <param name="ExternalCorrelationId">The non-secret identifier supplied by the external service, when available.</param>
/// <param name="ExternalStatus">The non-secret status reported by the external service, when available.</param>
public sealed record EffectAttempt(
    int Number,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc,
    string? ExternalCorrelationId,
    string? ExternalStatus);

/// <summary>The capability-specific reconciliation owed for a timeout after transmission.</summary>
/// <param name="CapabilityReconciliation">The capability-owned reconciliation procedure to execute.</param>
/// <param name="Outcome">The non-secret outcome the procedure reported.</param>
/// <param name="ResolvesToReceiptId">The receipt whose evidence this reconciliation resolves to.</param>
public sealed record EffectReconciliation(
    string CapabilityReconciliation,
    string Outcome,
    EffectId ResolvesToReceiptId);

/// <summary>
/// Durable evidence for one logical effect. It records metadata and references only: secret values and
/// engine-private payloads never enter the receipt (DES-0056 <c>execution-runtime-ck-5</c> to <c>ck-7</c>).
/// </summary>
public sealed record EffectReceipt
{
    /// <summary>The stable identity of the logical effect this is the sole receipt for.</summary>
    public required EffectId EffectId { get; init; }

    /// <summary>The tenant that owns this durable effect evidence.</summary>
    public required TenantId TenantId { get; init; }

    /// <summary>The workflow run that invoked the effect.</summary>
    public required RunId WorkflowRunId { get; init; }

    /// <summary>The workflow action node that invoked the capability.</summary>
    public required string ActionNodeId { get; init; }

    /// <summary>The stable reference of the service capability invoked.</summary>
    public required string CapabilityReference { get; init; }

    /// <summary>The exact version of the service capability invoked.</summary>
    public required string CapabilityVersion { get; init; }

    /// <summary>The authority floor the service capability required.</summary>
    public required EffectAuthorityFloor AuthorityFloor { get; init; }

    /// <summary>The closed outcome vocabulary for the logical effect.</summary>
    public required EffectStatus Status { get; init; }

    /// <summary>The first time the effect began execution.</summary>
    public required DateTimeOffset StartedUtc { get; init; }

    /// <summary>The time the current terminal effect outcome was recorded.</summary>
    public required DateTimeOffset CompletedUtc { get; init; }

    /// <summary>A non-secret SHA-256 request fingerprint that supports diagnosis without retaining the request.</summary>
    public required string RequestFingerprint { get; init; }

    /// <summary>The latest non-secret correlation identifier reported by the external service, when one exists.</summary>
    public string? ExternalCorrelationId { get; init; }

    /// <summary>The latest non-secret status reported by the external service, when one exists.</summary>
    public string? ExternalStatus { get; init; }

    /// <summary>The substrate-owned retry profile selected for this effect.</summary>
    public required RetryProfileName RetryProfile { get; init; }

    /// <summary>The separate logical effect that compensates this one, when compensation was started.</summary>
    public EffectId? CompensationEffectId { get; init; }

    /// <summary>Opaque references to secrets used by the capability; the receipt never stores their values.</summary>
    public IReadOnlyList<string> SecretReferenceIds { get; init; } = [];

    /// <summary>The ordered delivery history; its count is the receipt's attempt count.</summary>
    public IReadOnlyList<EffectAttempt> Attempts { get; init; } = [];

    /// <summary>The capability-specific reconciliation evidence required when <see cref="Status"/> is ambiguous.</summary>
    public EffectReconciliation? Reconciliation { get; init; }

    /// <summary>The number of delivery attempts retained in <see cref="Attempts"/>.</summary>
    public int AttemptCount => Attempts.Count;
}

/// <summary>Tenant-scoped durable storage for effect receipts, keyed by their logical effect identity.</summary>
public interface IEffectReceiptStore
{
    /// <summary>Stores a receipt, refusing a second receipt for the same tenant effect.</summary>
    ValueTask CreateAsync(EffectReceipt receipt, CancellationToken cancellationToken = default);

    /// <summary>Returns the tenant's receipt for an effect, or null when it has not been recorded.</summary>
    ValueTask<EffectReceipt?> GetAsync(TenantId tenantId, EffectId effectId, CancellationToken cancellationToken = default);
}

/// <summary>Single-process reference storage for effect receipts, suitable for tests and hosts without durable storage.</summary>
public sealed class InMemoryEffectReceiptStore : IEffectReceiptStore
{
    private readonly ConcurrentDictionary<(TenantId Tenant, EffectId Effect), EffectReceipt> _receipts = new();

    /// <inheritdoc />
    public ValueTask CreateAsync(EffectReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_receipts.TryAdd((receipt.TenantId, receipt.EffectId), receipt))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.EffectReceiptDuplicate,
                $"Effect '{receipt.EffectId}' already has a receipt for this tenant.");
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<EffectReceipt?> GetAsync(TenantId tenantId, EffectId effectId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_receipts.GetValueOrDefault((tenantId, effectId)));
    }
}

/// <summary>
/// Records the execution runtime's one effect receipt per logical effect and refuses evidence that would
/// weaken ambiguity, duplicate an effect, or retain a secret value.
/// </summary>
public sealed class EffectReceiptLedger
{
    private readonly IEffectReceiptStore _store;

    /// <summary>Creates the ledger over a tenant-scoped receipt store.</summary>
    public EffectReceiptLedger(IEffectReceiptStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Validates and records the only receipt for the supplied logical effect.</summary>
    public async ValueTask<EffectReceipt> RecordAsync(EffectReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Validate(receipt);
        await _store.CreateAsync(receipt, cancellationToken).ConfigureAwait(false);
        return receipt;
    }

    /// <summary>Reads an effect receipt through the common tenant-scoped contract.</summary>
    public ValueTask<EffectReceipt?> GetAsync(TenantId tenantId, EffectId effectId, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);
        RequireEffectId(effectId);
        return _store.GetAsync(tenantId, effectId, cancellationToken);
    }

    private static void Validate(EffectReceipt receipt)
    {
        RequireTenant(receipt.TenantId);
        RequireEffectId(receipt.EffectId);
        if (receipt.WorkflowRunId.Kind.Value != "workflow-run")
        {
            Refuse(ExecutionRuntimeRefusals.EffectReceiptInvalid, "An effect receipt must name its workflow-run identity.");
        }

        RequireText(receipt.ActionNodeId, "action node id");
        RequireText(receipt.CapabilityReference, "capability reference");
        RequireText(receipt.CapabilityVersion, "capability version");
        if (!Enum.IsDefined(receipt.AuthorityFloor) || !Enum.IsDefined(receipt.Status))
        {
            Refuse(ExecutionRuntimeRefusals.EffectReceiptInvalid, "An effect receipt must use known authority and status values.");
        }

        if (receipt.CompletedUtc < receipt.StartedUtc)
        {
            Refuse(ExecutionRuntimeRefusals.EffectReceiptInvalid, "An effect receipt cannot complete before it starts.");
        }

        if (!IsRequestFingerprint(receipt.RequestFingerprint))
        {
            Refuse(ExecutionRuntimeRefusals.EffectReceiptInvalid, "An effect receipt requires a non-secret SHA-256 request fingerprint.");
        }

        _ = RetryProfiles.Get(receipt.RetryProfile);
        ValidateAttempts(receipt);
        foreach (var secretReference in receipt.SecretReferenceIds ?? throw new ArgumentNullException(nameof(receipt)))
        {
            if (!IsSecretReference(secretReference))
            {
                Refuse(ExecutionRuntimeRefusals.SecretReferenceInvalid, "Effect receipts retain opaque secret references, never secret values.");
            }
        }

        if (receipt.Status == EffectStatus.Ambiguous && receipt.Reconciliation is null)
        {
            Refuse(ExecutionRuntimeRefusals.EffectReconciliationRequired, "An ambiguous effect receipt must carry capability-specific reconciliation state.");
        }

        if (receipt.Status != EffectStatus.Ambiguous && receipt.Reconciliation is not null)
        {
            Refuse(ExecutionRuntimeRefusals.EffectReceiptInvalid, "Only an ambiguous effect receipt carries reconciliation state.");
        }

        if (receipt.Reconciliation is { } reconciliation)
        {
            RequireText(reconciliation.CapabilityReconciliation, "capability reconciliation");
            RequireText(reconciliation.Outcome, "reconciliation outcome");
            RequireEffectId(reconciliation.ResolvesToReceiptId);
        }

        if (receipt.CompensationEffectId is { } compensation)
        {
            RequireEffectId(compensation);
            if (compensation == receipt.EffectId)
            {
                Refuse(ExecutionRuntimeRefusals.EffectReceiptInvalid, "An effect cannot compensate itself.");
            }

            if (receipt.Status == EffectStatus.Ambiguous)
            {
                Refuse(ExecutionRuntimeRefusals.BlindCompensationRefused, "An ambiguous effect must reconcile before any compensation is recorded.");
            }
        }
    }

    private static void ValidateAttempts(EffectReceipt receipt)
    {
        var attempts = receipt.Attempts ?? throw new ArgumentNullException(nameof(receipt));
        for (var index = 0; index < attempts.Count; index++)
        {
            var attempt = attempts[index] ?? throw new ArgumentNullException(nameof(receipt));
            if (attempt.Number != index + 1 || attempt.StartedUtc < receipt.StartedUtc || attempt.CompletedUtc < attempt.StartedUtc)
            {
                Refuse(ExecutionRuntimeRefusals.EffectReceiptInvalid, "Effect attempts must be ordered and cannot end before they start.");
            }
        }
    }

    private static bool IsRequestFingerprint(string value) => value is { Length: 71 }
        && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value[7..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsSecretReference(string value)
    {
        const string SecretScheme = "secret://";
        const string ReferenceScheme = "secretref:";
        var remainder = value.StartsWith(SecretScheme, StringComparison.Ordinal)
            ? value[SecretScheme.Length..]
            : value.StartsWith(ReferenceScheme, StringComparison.Ordinal)
                ? value[ReferenceScheme.Length..]
                : string.Empty;
        return remainder.Length > 0 && remainder.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or ':' or '/' or '-');
    }

    private static void RequireTenant(TenantId tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId.Value))
        {
            throw new ArgumentException("An effect receipt requires a tenant.", nameof(tenantId));
        }
    }

    private static void RequireEffectId(EffectId effectId)
    {
        if (effectId.Value is null)
        {
            Refuse(ExecutionRuntimeRefusals.EffectIdentityInvalid, "An effect receipt requires an effect identity.");
        }
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Refuse(ExecutionRuntimeRefusals.EffectReceiptInvalid, $"An effect receipt requires a {name}.");
        }
    }

    private static void Refuse(string code, string message) => throw new ExecutionRuntimeRefusedException(code, message);
}
