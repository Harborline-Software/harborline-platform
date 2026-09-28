namespace Harborline.Kernel.Core;

public static class KernelClockErrors
{
    public const string BackdateCapabilityRequired = "kernel.backdate-capability-required";
}

public interface IKernelBackdateCapability
{
    ValueTask<bool> CanBackdateAsync(
        string actorId,
        DateTimeOffset requestedEffectiveFrom,
        CancellationToken cancellationToken = default);
}

public sealed class KernelClockRefusalException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

/// <summary>The one authoritative clock supplied by a host composition root.</summary>
public sealed class KernelClock
{
    private readonly TimeProvider _provider;

    public KernelClock(TimeProvider provider) =>
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public DateTimeOffset GetUtcNow() => _provider.GetUtcNow();

    public bool IsExpired(DateTimeOffset expiresAt) => GetUtcNow() >= expiresAt;

    public async ValueTask<DateTimeOffset> ResolveEffectiveFromAsync(
        string actorId,
        DateTimeOffset admittedAt,
        DateTimeOffset? requestedEffectiveFrom,
        IKernelBackdateCapability? backdateCapability = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (requestedEffectiveFrom is not { } requested) return admittedAt;
        if (requested >= admittedAt) return requested;
        if (backdateCapability is null
            || !await backdateCapability.CanBackdateAsync(actorId, requested, cancellationToken).ConfigureAwait(false))
            throw new KernelClockRefusalException(KernelClockErrors.BackdateCapabilityRequired);
        return requested;
    }
}
