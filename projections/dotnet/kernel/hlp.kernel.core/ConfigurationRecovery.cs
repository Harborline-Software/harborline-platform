using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Harborline.Kernel.Core;

public static class KernelRecoveryErrors
{
    public const string CapabilityRequired = "kernel.configuration-recovery-capability-required";
    public const string ReasonRequired = "kernel.recovery-reason-required";
    public const string AuthoritySnapshotRequired = "kernel.recovery-authority-snapshot-required";
    public const string TenantMismatch = "kernel.recovery-tenant-mismatch";
    public const string EffectiveGenerationMissing = "kernel.effective-generation-missing";
    public const string EffectiveGenerationCorrupt = "kernel.effective-generation-corrupt";
    public const string EffectivePointerUnbound = "kernel.effective-pointer-unbound";
}

/// <summary>
/// The kernel profile record: the capabilities the kernel declares with its own implementation,
/// before and without any catalogue or released definition.
/// </summary>
public static class KernelProfile
{
    public const string ConfigurationRecovery = "configuration-recovery";

    public static IReadOnlyCollection<string> Capabilities { get; } = [ConfigurationRecovery];
}

/// <summary>Point-of-use admission of the constrained configuration-recovery capability.</summary>
public interface IKernelConfigurationRecoveryCapability
{
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

public interface IKernelProfileReader
{
    ValueTask<KernelProfileSnapshot?> ReadAsync(string tenantKey, CancellationToken cancellationToken = default);
}

public sealed record ConfigurationRecoveryRequest(
    string RecoveryId,
    string TenantKey,
    string ActorId,
    string Reason,
    string AuthoritySnapshot);

public enum ConfigurationResidue { PreparedGeneration, EffectivePointer, EvidenceOutbox }

public enum ConfigurationTerminalState { Abandoned, Confirmed, Published }

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

public sealed record ConfigurationRecoveryResult<TResult>(
    ConfigurationRecoveryRecord? Record,
    TResult? Value,
    ConfigurationRecoveryRefusal? Refusal)
{
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

    public ConfigurationRecovery(IKernelProfileReader profile, IKernelConfigurationRecoveryCapability? capability, KernelClock clock)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _capability = capability;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

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
