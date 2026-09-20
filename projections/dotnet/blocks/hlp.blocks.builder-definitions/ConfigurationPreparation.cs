namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>A stable refusal code and the public component or reference that failed.</summary>
public sealed record ConfigurationActivationRefusal(string Code, string Target, string Message);

/// <summary>Host-produced immutable projection reference and explicit compatibility findings.</summary>
/// <remarks>An empty finding list means checked and compatible; null never means success.</remarks>
public sealed record ConfigurationProjectionValidation(ConfigurationReference Projection,
    IReadOnlyList<ConfigurationActivationRefusal> Findings);

/// <summary>A candidate validated against one exact baseline; construct only through preparation.</summary>
public sealed class PreparedConfigurationGeneration
{
    internal PreparedConfigurationGeneration(ConfigurationGeneration baseline, ConfigurationGeneration candidate,
        ConfigurationReference projection)
    {
        Baseline = baseline;
        Candidate = candidate;
        Projection = projection;
        Ownership = Array.AsReadOnly(candidate.References.GetProperty("ownership").EnumerateArray()
            .Select(owner => new ConfigurationOwnership(owner.GetProperty("definitionKey").GetString()!,
                owner.GetProperty("packageKey").GetString()!)).ToArray());
    }

    /// <summary>The complete immutable baseline used for validation.</summary>
    public ConfigurationGeneration Baseline { get; }
    /// <summary>The identity resolved by ConfigurationGeneration.Resolve, without rehashing.</summary>
    public ConfigurationGeneration Candidate { get; }
    /// <summary>The complete immutable host projection; the host verifies its bytes before switching.</summary>
    public ConfigurationReference Projection { get; }
    /// <summary>Ownership from the candidate's canonical snapshot, never a second caller selection.</summary>
    public IReadOnlyList<ConfigurationOwnership> Ownership { get; }
}

/// <summary>A preparation success or refusal. Preparation never changes the effective generation.</summary>
public sealed class ConfigurationPreparation
{
    private ConfigurationPreparation(ConfigurationGeneration baseline, string expectedBaselineDigest, ConfigurationGeneration candidate,
        PreparedConfigurationGeneration? prepared, IReadOnlyList<ConfigurationActivationRefusal> refusals)
    {
        Baseline = baseline;
        ExpectedBaselineDigest = expectedBaselineDigest;
        Candidate = candidate;
        Prepared = prepared;
        Refusals = Array.AsReadOnly(refusals.ToArray());
    }

    /// <summary>The effective snapshot observed when preparation began; it may subsequently become stale.</summary>
    public ConfigurationGeneration Baseline { get; }
    /// <summary>The caller's baseline fence, retained even when stale.</summary>
    public string ExpectedBaselineDigest { get; }
    /// <summary>The immutable candidate identity.</summary>
    public ConfigurationGeneration Candidate { get; }
    /// <summary>Available only after successful projection and compatibility validation.</summary>
    public PreparedConfigurationGeneration? Prepared { get; }
    /// <summary>Named failures; an unsuccessful preparation never carries a prepared candidate.</summary>
    public IReadOnlyList<ConfigurationActivationRefusal> Refusals { get; }

    /// <summary>Builds and validates an isolated projection against an explicit expected baseline.</summary>
    /// <remarks>
    /// The trusted host callback must check all content kinds, platform and policy compatibility,
    /// including destination data and pinned work. Unknown compatibility is a finding, never a pass.
    /// It may create isolated artifacts but must not mutate ownership, evidence or the effective pointer.
    /// Exceptions propagate: interruption does not produce a prepared token or an effective outcome.
    /// </remarks>
    public static ConfigurationPreparation Prepare(ConfigurationGeneration baseline, string expectedBaselineDigest,
        ConfigurationGeneration candidate,
        Func<ConfigurationGeneration, ConfigurationGeneration, ConfigurationProjectionValidation> validate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(validate);
        ConfigurationPreparation Refuse(string code, string target, string message) =>
            new(baseline, expectedBaselineDigest, candidate, null, [new(code, target, message)]);
        if (baseline.Digest != expectedBaselineDigest)
            return Refuse("configuration-baseline-stale", "expectedBaselineDigest", "The expected baseline is no longer effective.");
        if (Tenant(baseline) != Tenant(candidate))
            return Refuse("configuration-tenant-mismatch", "candidate", "The candidate belongs to another tenant.");
        var validation = validate(baseline, candidate);
        if (validation is null || validation.Findings is null)
            return Refuse("configuration-validation-required", "projection", "Explicit projection and compatibility validation is required.");
        var findings = validation.Findings.ToArray();
        if (findings.Any(finding => finding is null || string.IsNullOrWhiteSpace(finding.Code)
            || string.IsNullOrWhiteSpace(finding.Target) || string.IsNullOrWhiteSpace(finding.Message)))
            return Refuse("configuration-validation-invalid", "projection", "A validation finding is incomplete.");
        if (findings.Length > 0) return new(baseline, expectedBaselineDigest, candidate, null, findings);
        var projection = validation.Projection;
        if (projection is null || string.IsNullOrWhiteSpace(projection.Key) || string.IsNullOrWhiteSpace(projection.Revision)
            || projection.Digest is not { Length: 64 }
            || projection.Digest.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            return Refuse("configuration-projection-required", "projection", "A complete immutable projection reference is required.");
        return new(baseline, expectedBaselineDigest, candidate, new(baseline, candidate, projection), []);
    }

    internal static string Tenant(ConfigurationGeneration generation) => generation.References.GetProperty("tenantKey").GetString()!;
}
