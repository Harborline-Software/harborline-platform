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

    internal void Validate()
    {
        if (MaximumCandidateBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaximumCandidateBytes));
        ArgumentException.ThrowIfNullOrWhiteSpace(EngineVersion);
        ArgumentNullException.ThrowIfNull(LocaleChain);
        if (LocaleChain.Count == 0 || LocaleChain.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty locale is required.", nameof(LocaleChain));
    }
}
