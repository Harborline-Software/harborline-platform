namespace Harborline.Foundation.Forms.Engine;

public sealed class FormEngineOptions
{
    public const int DefaultMaximumCandidateBytes = 1024 * 1024;
    public const int MaximumRecoveryBatch = 1000;

    public int MaximumCandidateBytes { get; init; } = DefaultMaximumCandidateBytes;
    public string EngineVersion { get; init; } = "harborline-jsonlogic/v1";
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
