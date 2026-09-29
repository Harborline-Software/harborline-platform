using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Harborline.Kernel.Core;

/// <summary>Refusal codes returned by <see cref="ConfigurationRecovery.RecoverAsync"/>.</summary>
public static class KernelRecoveryErrors
{
    /// <summary>The actor does not hold the configuration-recovery capability for the tenant, or no capability check is configured.</summary>
    public const string CapabilityRequired = "kernel.configuration-recovery-capability-required";
    /// <summary>The request carries no reason.</summary>
    public const string ReasonRequired = "kernel.recovery-reason-required";
    /// <summary>The request carries no authority snapshot.</summary>
    public const string AuthoritySnapshotRequired = "kernel.recovery-authority-snapshot-required";
    /// <summary>The profile read back belongs to a different tenant than the request names.</summary>
    public const string TenantMismatch = "kernel.recovery-tenant-mismatch";
    /// <summary>The tenant has no profile, no effective pointer, or no effective content.</summary>
    public const string EffectiveGenerationMissing = "kernel.effective-generation-missing";
    /// <summary>The effective content's SHA-256 does not match the pointer's digest.</summary>
    public const string EffectiveGenerationCorrupt = "kernel.effective-generation-corrupt";
    /// <summary>The effective pointer was committed without an evidence intent.</summary>
    public const string EffectivePointerUnbound = "kernel.effective-pointer-unbound";
}

/// <summary>
/// The kernel profile record: the capabilities the kernel declares with its own implementation,
/// before and without any catalogue or released definition.
/// </summary>
public static class KernelProfile
{
    /// <summary>The capability that admits <see cref="Core.ConfigurationRecovery"/>.</summary>
    public const string ConfigurationRecovery = "configuration-recovery";

    /// <summary>Every capability the kernel profile declares.</summary>
    public static IReadOnlyCollection<string> Capabilities { get; } = [ConfigurationRecovery];
}

/// <summary>Point-of-use admission of the constrained configuration-recovery capability.</summary>
public interface IKernelConfigurationRecoveryCapability
{
    /// <summary>Returns true when <paramref name="actorId"/> may run configuration recovery for <paramref name="tenantKey"/>.</summary>
    ValueTask<bool> CanRecoverAsync(string actorId, string tenantKey, CancellationToken cancellationToken = default);
}

/// <summary>A prepared generation a crash left behind. Recovery never activates it.</summary>
public sealed record PreparedGenerationResidue(string CandidateDigest, string ProjectionKey);

/// <summary>The effective pointer and the evidence intent its commit was bound to.</summary>
public sealed record EffectivePointer(string Digest, string? EvidenceIntentId);

/// <summary>One committed evidence intent and whether the outbox has published it.</summary>
public sealed record EvidenceOutboxEntry(string EvidenceIntentId, bool Published);

/// <summary>What the host's kernel profile store holds for one tenant. No catalogue read is part of it.</summary>
public sealed record KernelProfileSnapshot(
    string TenantKey,
    EffectivePointer? EffectivePointer,
    ReadOnlyMemory<byte>? EffectiveContent,
    PreparedGenerationResidue? Prepared,
    IReadOnlyList<EvidenceOutboxEntry> Outbox);

/// <summary>Reads a tenant's kernel profile from the host's store.</summary>
public interface IKernelProfileReader
{
    /// <summary>Returns the profile for <paramref name="tenantKey"/>, or null when the tenant has none.</summary>
    ValueTask<KernelProfileSnapshot?> ReadAsync(string tenantKey, CancellationToken cancellationToken = default);
}

/// <summary>An operator's request to recover one tenant's configuration after a crash.</summary>
/// <param name="RecoveryId">Identifies this recovery; also its idempotency key.</param>
/// <param name="TenantKey">The tenant to recover.</param>
/// <param name="ActorId">The operator running the recovery.</param>
/// <param name="Reason">Why recovery is needed; blank is refused with <see cref="KernelRecoveryErrors.ReasonRequired"/>.</param>
/// <param name="AuthoritySnapshot">The authority the operator acts under; blank is refused with <see cref="KernelRecoveryErrors.AuthoritySnapshotRequired"/>.</param>
public sealed record ConfigurationRecoveryRequest(
    string RecoveryId,
    string TenantKey,
    string ActorId,
    string Reason,
    string AuthoritySnapshot);

/// <summary>The kind of crash residue a recovery brings to a terminal state.</summary>
public enum ConfigurationResidue
{
    /// <summary>A prepared generation that never became effective.</summary>
    PreparedGeneration,
    /// <summary>The tenant's effective pointer.</summary>
    EffectivePointer,
    /// <summary>A committed evidence intent the outbox has not published.</summary>
    EvidenceOutbox,
}

/// <summary>The terminal state a recovery records for one residue.</summary>
public enum ConfigurationTerminalState
{
    /// <summary>A prepared generation is discarded and never activated.</summary>
    Abandoned,
    /// <summary>The effective pointer is verified against its content digest and left where it is.</summary>
    Confirmed,
    /// <summary>An unpublished evidence intent is marked for publication.</summary>
    Published,
}

/// <summary>The terminal state recorded for one residue.</summary>
/// <param name="Residue">The kind of residue.</param>
/// <param name="Identity">The residue's identity: a candidate digest, the pointer's digest, or an evidence intent id.</param>
/// <param name="Terminal">The state it is brought to.</param>
public sealed record ConfigurationRepair(ConfigurationResidue Residue, string Identity, ConfigurationTerminalState Terminal);

/// <summary>The one record a recovery commits: each residue's terminal state, with the reason and authority snapshot behind it.</summary>
public sealed record ConfigurationRecoveryRecord(
    string RecoveryId,
    string TenantKey,
    string EffectiveDigest,
    IReadOnlyList<ConfigurationRepair> Repairs,
    string Reason,
    string AuthoritySnapshot);

/// <summary>Why no write happened; State names the profile state the operator must resolve.</summary>
public sealed record ConfigurationRecoveryRefusal(string Code, string State);

/// <summary>The outcome of a recovery: the committed record and host value, or a refusal with neither.</summary>
/// <typeparam name="TResult">The host's commit result type.</typeparam>
/// <param name="Record">The committed recovery record; null when refused.</param>
/// <param name="Value">The host's commit result; default when refused.</param>
/// <param name="Refusal">Null when the recovery committed.</param>
public sealed record ConfigurationRecoveryResult<TResult>(
    ConfigurationRecoveryRecord? Record,
    TResult? Value,
    ConfigurationRecoveryRefusal? Refusal)
{
    /// <summary>True when the recovery committed, that is when <see cref="Refusal"/> is null.</summary>
    public bool Committed => Refusal is null;
}

/// <summary>
/// Brings a prepared generation, the effective pointer and the evidence outbox left by a crash to a
/// terminal state, or refuses before any write naming the state. Reads the kernel profile only; it
/// holds no seam to a catalogue, a pack or a released definition, so authority is never established
/// through one. The pointer is never moved: an earlier generation becomes effective only through a
/// new evidenced activation.
/// </summary>
public sealed class ConfigurationRecovery
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);
    private readonly IKernelProfileReader _profile;
    private readonly IKernelConfigurationRecoveryCapability? _capability;
    private readonly KernelClock _clock;

    /// <summary>Creates a recovery over the host's profile store; a null <paramref name="capability"/> refuses every request.</summary>
    public ConfigurationRecovery(IKernelProfileReader profile, IKernelConfigurationRecoveryCapability? capability, KernelClock clock)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _capability = capability;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Checks the reason, authority snapshot, capability and effective generation, refusing before any write with a
    /// <see cref="KernelRecoveryErrors"/> code, then commits one <see cref="ConfigurationRecoveryRecord"/> and its audit
    /// through <see cref="KernelTransactionBoundary"/>.
    /// </summary>
    public async ValueTask<ConfigurationRecoveryResult<TResult>> RecoverAsync<TResult>(
        ConfigurationRecoveryRequest request,
        IKernelTransactionPort<ConfigurationRecoveryRecord, TResult> port,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(port);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RecoveryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActorId);
        static ConfigurationRecoveryResult<TResult> Refuse(string code, string state) => new(null, default, new(code, state));

        if (string.IsNullOrWhiteSpace(request.Reason)) return Refuse(KernelRecoveryErrors.ReasonRequired, "reason-absent");
        if (string.IsNullOrWhiteSpace(request.AuthoritySnapshot))
            return Refuse(KernelRecoveryErrors.AuthoritySnapshotRequired, "authority-snapshot-absent");
        if (_capability is null
            || !await _capability.CanRecoverAsync(request.ActorId, request.TenantKey, cancellationToken).ConfigureAwait(false))
            return Refuse(KernelRecoveryErrors.CapabilityRequired, KernelProfile.ConfigurationRecovery);

        var profile = await _profile.ReadAsync(request.TenantKey, cancellationToken).ConfigureAwait(false);
        if (profile is null) return Refuse(KernelRecoveryErrors.EffectiveGenerationMissing, "profile-absent");
        if (!string.Equals(profile.TenantKey, request.TenantKey, StringComparison.Ordinal))
            return Refuse(KernelRecoveryErrors.TenantMismatch, "profile-tenant-differs");
        if (profile.EffectivePointer is null || string.IsNullOrWhiteSpace(profile.EffectivePointer.Digest))
            return Refuse(KernelRecoveryErrors.EffectiveGenerationMissing, "pointer-missing");
        if (profile.EffectiveContent is not { } content)
            return Refuse(KernelRecoveryErrors.EffectiveGenerationMissing, "content-missing");
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(content.Span)), profile.EffectivePointer.Digest, StringComparison.Ordinal))
            return Refuse(KernelRecoveryErrors.EffectiveGenerationCorrupt, "content-digest-mismatch");
        if (string.IsNullOrWhiteSpace(profile.EffectivePointer.EvidenceIntentId))
            return Refuse(KernelRecoveryErrors.EffectivePointerUnbound, "pointer-without-evidence-intent");

        var repairs = new List<ConfigurationRepair>();
        if (profile.Prepared is not null)
            repairs.Add(new(ConfigurationResidue.PreparedGeneration, profile.Prepared.CandidateDigest, ConfigurationTerminalState.Abandoned));
        repairs.Add(new(ConfigurationResidue.EffectivePointer, profile.EffectivePointer.Digest, ConfigurationTerminalState.Confirmed));
        foreach (var entry in profile.Outbox ?? [])
            if (!entry.Published)
                repairs.Add(new(ConfigurationResidue.EvidenceOutbox, entry.EvidenceIntentId, ConfigurationTerminalState.Published));

        var record = new ConfigurationRecoveryRecord(request.RecoveryId, request.TenantKey, profile.EffectivePointer.Digest,
            repairs.AsReadOnly(), request.Reason, request.AuthoritySnapshot);
        var audit = new KernelAuditEvidence(
            $"{request.RecoveryId}:audit",
            request.ActorId,
            _clock.GetUtcNow(),
            JsonSerializer.SerializeToUtf8Bytes(new { capability = KernelProfile.ConfigurationRecovery, record }, AuditJson));
        var operation = new KernelOperationIdentity(request.RecoveryId, request.RecoveryId, Fingerprint(request));
        var committed = await KernelTransactionBoundary.ExecuteAsync([new(operation, record, audit)], port, cancellationToken)
            .ConfigureAwait(false);
        return committed.Committed
            ? new(record, committed.Value, null)
            : Refuse(committed.Refusal!.Code, "transaction-refused");
    }

    private static string Fingerprint(ConfigurationRecoveryRequest request) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join('\n', request.TenantKey, request.ActorId, request.Reason, request.AuthoritySnapshot))));
}
