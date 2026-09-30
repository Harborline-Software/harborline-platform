namespace Harborline.Kernel.Core;

/// <summary>Refusal codes raised by <see cref="KernelClock"/>.</summary>
public static class KernelClockErrors
{
    /// <summary>An effective-from earlier than admission was requested without a granted backdate capability.</summary>
    public const string BackdateCapabilityRequired = "kernel.backdate-capability-required";
}

/// <summary>Decides whether an actor may record a change as effective before the moment it was admitted.</summary>
public interface IKernelBackdateCapability
{
    /// <summary>Returns true when <paramref name="actorId"/> may backdate to <paramref name="requestedEffectiveFrom"/>; false refuses the backdate.</summary>
    ValueTask<bool> CanBackdateAsync(
        string actorId,
        DateTimeOffset requestedEffectiveFrom,
        CancellationToken cancellationToken = default);
}

/// <summary>A clock rule refused the request; <see cref="Code"/> is a <see cref="KernelClockErrors"/> value.</summary>
/// <param name="code">The refusal code, also used as the message.</param>
public sealed class KernelClockRefusalException(string code) : InvalidOperationException(code)
{
    /// <summary>The stable refusal code, one of <see cref="KernelClockErrors"/>.</summary>
    public string Code { get; } = code;
}

/// <summary>The one authoritative clock supplied by a host composition root.</summary>
public sealed class KernelClock
{
    private readonly TimeProvider _provider;

    /// <summary>Wraps the host's time source; throws <see cref="ArgumentNullException"/> when it is null.</summary>
    public KernelClock(TimeProvider provider) =>
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <summary>The current instant in UTC, read from the host's time provider.</summary>
    public DateTimeOffset GetUtcNow() => _provider.GetUtcNow();

    /// <summary>True when now is at or past <paramref name="expiresAt"/>; the expiry instant itself counts as expired.</summary>
    public bool IsExpired(DateTimeOffset expiresAt) => GetUtcNow() >= expiresAt;

    /// <summary>
    /// Returns the instant a change takes effect: <paramref name="admittedAt"/> when none is requested, the request when it is not
    /// earlier than admission, and an earlier request only when <paramref name="backdateCapability"/> grants it; otherwise throws
    /// <see cref="KernelClockRefusalException"/> with <see cref="KernelClockErrors.BackdateCapabilityRequired"/>.
    /// </summary>
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
