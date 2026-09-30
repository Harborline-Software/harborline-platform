namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>
/// The name of a retry profile the substrate defines. Only a defined name constructs, so a raw attempt
/// count, error predicate or backoff parameter can never be passed where a profile is expected
/// (DES-0056 <c>execution-runtime-ck-8</c>; DES-0019 <c>workflows-ck-27</c>).
/// </summary>
public readonly record struct RetryProfileName
{
    private RetryProfileName(string value) => Value = value;

    /// <summary>One attempt; a failure dead-letters.</summary>
    public static RetryProfileName ImmediateOrFail { get; } = new("immediate-or-fail");

    /// <summary>A short, capped exponential retry for a synchronous external call.</summary>
    public static RetryProfileName ExternalApiStandard { get; } = new("external-api-standard");

    /// <summary>A long, capped exponential retry for delivery that must eventually land.</summary>
    public static RetryProfileName DurableDelivery { get; } = new("durable-delivery");

    /// <summary>One attempt; a failure dead-letters for an operator to reconcile.</summary>
    public static RetryProfileName ManualReconciliation { get; } = new("manual-reconciliation");

    /// <summary>The wire name.</summary>
    public string Value { get; }

    /// <summary>Parses a wire name, refusing any name the substrate does not define.</summary>
    public static RetryProfileName Parse(string value) =>
        RetryProfiles.All.Select(profile => profile.Name).FirstOrDefault(name => string.Equals(name.Value, value, StringComparison.Ordinal)) is { Value: not null } name
            ? name
            : throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RetryProfileUnknown,
                $"'{value}' is not a retry profile the execution runtime defines.");

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// One substrate-defined retry profile. Its parameters are set here, once, and are never authored;
/// no constructor is public.
/// </summary>
public sealed class RetryProfile
{
    internal RetryProfile(RetryProfileName name, int maxAttempts, TimeSpan initialDelay, TimeSpan maxDelay)
    {
        Name = name;
        MaxAttempts = maxAttempts;
        InitialDelay = initialDelay;
        MaxDelay = maxDelay;
    }

    /// <summary>The profile's name.</summary>
    public RetryProfileName Name { get; }

    /// <summary>The total attempts allowed, the first included.</summary>
    public int MaxAttempts { get; }

    /// <summary>The wait after the first failed attempt.</summary>
    public TimeSpan InitialDelay { get; }

    /// <summary>The cap on any single wait.</summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>True when a run that has made <paramref name="attemptsMade"/> attempts may make another.</summary>
    public bool AllowsAnotherAttempt(int attemptsMade) => attemptsMade < MaxAttempts;

    /// <summary>The wait after failed attempt <paramref name="attemptNumber"/> (1-based): doubling from the initial delay, capped.</summary>
    public TimeSpan DelayAfter(int attemptNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptNumber, 1);
        var delay = InitialDelay;
        for (var attempt = 1; attempt < attemptNumber && delay < MaxDelay; attempt++)
        {
            delay += delay;
        }

        return delay < MaxDelay ? delay : MaxDelay;
    }
}

/// <summary>The retry profiles the substrate defines: the one retry rule every engine runs under.</summary>
public static class RetryProfiles
{
    /// <summary>Every defined profile, in declaration order.</summary>
    public static IReadOnlyList<RetryProfile> All { get; } =
    [
        new(RetryProfileName.ImmediateOrFail, 1, TimeSpan.Zero, TimeSpan.Zero),
        new(RetryProfileName.ExternalApiStandard, 3, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30)),
        new(RetryProfileName.DurableDelivery, 10, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1)),
        new(RetryProfileName.ManualReconciliation, 1, TimeSpan.Zero, TimeSpan.Zero),
    ];

    /// <summary>The profile a name denotes.</summary>
    public static RetryProfile Get(RetryProfileName name) =>
        All.FirstOrDefault(profile => profile.Name == name)
            ?? throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RetryProfileUnknown,
                $"'{name}' is not a retry profile the execution runtime defines.");
}

/// <summary>
/// The retry profiles a capability allows and its default (ADR 0098 amendment point 19). The author of an
/// action binding selects one of the allowed names, or takes the default; nothing else is expressible.
/// </summary>
public sealed class CapabilityRetryPolicy
{
    /// <summary>Declares a capability's allowed profiles and its default, which must be among them.</summary>
    public CapabilityRetryPolicy(RetryProfileName defaultProfile, IEnumerable<RetryProfileName> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        var allowedSet = allowed.ToHashSet();
        foreach (var name in allowedSet.Append(defaultProfile))
        {
            RetryProfiles.Get(name);
        }

        if (!allowedSet.Contains(defaultProfile))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RetryProfileNotAllowed,
                $"The capability's default profile '{defaultProfile}' is not among its allowed profiles.");
        }

        Default = defaultProfile;
        Allowed = allowedSet;
    }

    /// <summary>The profile a binding gets when it selects none.</summary>
    public RetryProfileName Default { get; }

    /// <summary>The profiles a binding may select.</summary>
    public IReadOnlySet<RetryProfileName> Allowed { get; }

    /// <summary>Resolves an author's selection, refusing a profile the capability does not allow.</summary>
    public RetryProfile Resolve(RetryProfileName? selected)
    {
        var name = selected ?? Default;
        if (!Allowed.Contains(name))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RetryProfileNotAllowed,
                $"Retry profile '{name}' is not among the capability's allowed profiles.");
        }

        return RetryProfiles.Get(name);
    }
}
