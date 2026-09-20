namespace Harborline.Kernel.Core;

public static class KernelClockErrors
{
    public const string BackdateCapabilityRequired = "kernel.backdate-capability-required";
}

public sealed record KernelStampRequest(
    string ActorId,
    DateTimeOffset? RequestedRecordedAt = null,
    DateTimeOffset? CapturedAt = null);

public interface IKernelBackdateCapability
{
    ValueTask<bool> CanBackdateAsync(
        string actorId,
        DateTimeOffset requestedRecordedAt,
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

    public async ValueTask<DateTimeOffset> ResolveRecordedAtAsync(
        KernelStampRequest request,
        IKernelBackdateCapability? backdateCapability = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActorId);
        var now = GetUtcNow();
        if (request.RequestedRecordedAt is not { } requested || requested == now) return now;
        if (backdateCapability is null
            || !await backdateCapability.CanBackdateAsync(request.ActorId, requested, cancellationToken).ConfigureAwait(false))
            throw new KernelClockRefusalException(KernelClockErrors.BackdateCapabilityRequired);
        return requested;
    }
}
