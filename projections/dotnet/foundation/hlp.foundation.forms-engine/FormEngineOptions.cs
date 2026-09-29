namespace Harborline.Foundation.Forms.Engine;

/// <summary>Represents the form engine options contract used by this package.</summary>
public sealed class FormEngineOptions
{
    /// <summary>Provides the default maximum candidate bytes associated with this value.</summary>
    public const int DefaultMaximumCandidateBytes = 1024 * 1024;
    /// <summary>Provides the maximum recovery batch associated with this value.</summary>
    public const int MaximumRecoveryBatch = 1000;

    /// <summary>Provides the maximum candidate bytes associated with this value.</summary>
    public int MaximumCandidateBytes { get; init; } = DefaultMaximumCandidateBytes;
    /// <summary>Provides the engine version associated with this value.</summary>
    public string EngineVersion { get; init; } = "harborline-jsonlogic/v1";
    /// <summary>Provides the locale chain associated with this value.</summary>
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
