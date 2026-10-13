namespace Harborline.Foundation.Forms.Engine;

/// <summary>Configures candidate limits, engine versioning, and locale fallback for Forms Engine operations.</summary>
public sealed class FormEngineOptions
{
    /// <summary>Sets the default maximum JSON candidate size to one mebibyte.</summary>
    public const int DefaultMaximumCandidateBytes = 1024 * 1024;
    /// <summary>Caps one recovery operation at one thousand projection deliveries.</summary>
    public const int MaximumRecoveryBatch = 1000;

    /// <summary>Limits the serialized candidate size accepted by validation and submission.</summary>
    public int MaximumCandidateBytes { get; init; } = DefaultMaximumCandidateBytes;
    /// <summary>Identifies the rules and serialization contract used by this engine instance.</summary>
    public string EngineVersion { get; init; } = "harborline-jsonlogic/v1";
    /// <summary>Supplies the ordered locale fallback chain used for localized form content.</summary>
    public IReadOnlyList<string> LocaleChain { get; init; } = ["en"];

    /// <summary>
    /// The identity durably accepted Records commands execute as (ADR-0110 decision 4, plan W7): host configuration, never
    /// derived from a request, a token or a stored row. It is the actual executor the Records completion decides and audits;
    /// the submitter is carried only as provenance, and a command whose submitter equals this identity is never executed
    /// (no impersonation). Null, the default, executes no accepted command: each stays pending, with its fence open to a
    /// cancel, until a host configures its worker. Ordinary projections are unaffected.
    /// </summary>
    public string? AcceptedCommandWorkerId { get; init; }

    /// <summary>
    /// Review M5 and R2-L3: the bound on each host call made while an acceptance holds the store's exclusion scope: the
    /// submit-gate decision (the decision 1 reading) and the mapped-body digest (<see cref="IFormAcceptedRecordBodies"/>).
    /// Past it the engine stops waiting, cancels the call's token, refuses the submit with <c>form.engine.resource-bound</c>
    /// and releases the scope. Defaults to five seconds.
    /// </summary>
    public TimeSpan AcceptanceDecisionTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Review M2, R2-L1 and R2-M1: the execution of a claimed accepted command that finds its stored binding broken for the
    /// limit-th time quarantines it (audited, non-terminal, never leased again; DES-0029 section 9 forbids making it terminal
    /// while an earlier run may have committed its effect); each earlier such execution is counted in
    /// <see cref="Persistence.FormAcceptedRecordCommand.BindingFailures"/> and leaves it pending. Only binding failures
    /// count, never infrastructure failures. A pending command with a broken binding is refused at once. Defaults to three.
    /// </summary>
    public int AcceptedCommandBindingRetryLimit { get; init; } = 3;

    internal void Validate()
    {
        if (AcceptedCommandWorkerId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(AcceptedCommandWorkerId);
        if (AcceptanceDecisionTimeout <= TimeSpan.Zero || AcceptanceDecisionTimeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(AcceptanceDecisionTimeout));
        if (AcceptedCommandBindingRetryLimit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(AcceptedCommandBindingRetryLimit));
        if (MaximumCandidateBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumCandidateBytes));
        ArgumentException.ThrowIfNullOrWhiteSpace(EngineVersion);
        ArgumentNullException.ThrowIfNull(LocaleChain);
        if (LocaleChain.Count == 0 || LocaleChain.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty locale is required.", nameof(LocaleChain));
    }
}
