namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Public evidence intent identity and rationale; the whole switch request is its immutable input.</summary>
public sealed record ConfigurationEvidenceIntent(string Id, string Reason);

/// <summary>One switch input: exact baseline, candidate, projection, ownership, acting principal and evidence intent.</summary>
/// <remarks>The prepared candidate binds baseline and ownership, so neither can be substituted independently.</remarks>
public sealed record ConfigurationActivationRequest(PreparedConfigurationGeneration Prepared,
    string Principal, ConfigurationEvidenceIntent EvidenceIntent);

/// <summary>A host Access decision obtained for this request at point of use, never a workflow approval.</summary>
/// <remarks>The callback must authorize the entire request. DecisionId identifies the retained Access decision.</remarks>
public sealed record ConfigurationActivationAuthority(bool Allowed, string DecisionId);

/// <summary>A pure switch decision. Permission to commit does not establish that a transaction committed.</summary>
public sealed class ConfigurationActivationDecision
{
    internal ConfigurationActivationDecision(ConfigurationActivationRequest request, ConfigurationGeneration current,
        ConfigurationActivationAuthority? authority, ConfigurationActivationRefusal? refusal)
    {
        Request = request;
        PriorGeneration = current;
        Authority = authority;
        Refusal = refusal;
    }

    /// <summary>All inputs that must commit together, including the evidence intent.</summary>
    public ConfigurationActivationRequest Request { get; }
    /// <summary>The complete generation observed under the host's transaction boundary.</summary>
    public ConfigurationGeneration PriorGeneration { get; }
    /// <summary>The point-of-use Access decision to bind to evidence.</summary>
    public ConfigurationActivationAuthority? Authority { get; }
    /// <summary>Why no switch may occur; a stale baseline is a refusal, never a retry instruction.</summary>
    public ConfigurationActivationRefusal? Refusal { get; }
    /// <summary>The proposed new effective generation, available only when all decision checks pass.</summary>
    public ConfigurationGeneration? NewGeneration => Refusal is null ? Request.Prepared.Candidate : null;
}

/// <summary>The platform's deterministic compare-and-swap decision contract.</summary>
public static class ConfigurationActivation
{
    /// <summary>Checks the baseline again and resolves authority at the switch's point of use.</summary>
    /// <remarks>
    /// Call inside the host transaction that fences the current generation. The host must also fence
    /// destination compatibility and verify the prepared projection remains available and unchanged.
    /// Do not publish this decision as effective until ownership, pointer, authority and evidence intent
    /// commit together. An exception or cancellation produces no decision and must abort that transaction.
    /// </remarks>
    public static ConfigurationActivationDecision DecideCompareAndSwap(ConfigurationGeneration current,
        ConfigurationActivationRequest request,
        Func<ConfigurationActivationRequest, ConfigurationActivationAuthority> authorize)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Prepared);
        ArgumentNullException.ThrowIfNull(authorize);
        ConfigurationActivationDecision Refuse(string code, string target, string message,
            ConfigurationActivationAuthority? authority = null) => new(request, current, authority, new(code, target, message));
        if (ConfigurationPreparation.Tenant(current) != ConfigurationPreparation.Tenant(request.Prepared.Candidate))
            return Refuse("configuration-tenant-mismatch", "candidate", "The candidate belongs to another tenant.");
        if (current.Digest != request.Prepared.Baseline.Digest)
            return Refuse("configuration-baseline-stale", "expectedBaselineDigest", "The expected baseline is no longer effective.");
        if (string.IsNullOrWhiteSpace(request.Principal))
            return Refuse("configuration-principal-required", "principal", "An acting principal is required.");
        if (request.EvidenceIntent is null || string.IsNullOrWhiteSpace(request.EvidenceIntent.Id)
            || string.IsNullOrWhiteSpace(request.EvidenceIntent.Reason))
            return Refuse("configuration-evidence-required", "evidenceIntent", "An evidence intent identity and reason are required.");
        var authority = authorize(request);
        if (authority is null || !authority.Allowed || string.IsNullOrWhiteSpace(authority.DecisionId))
            return Refuse("configuration-authority-refused", "authority", "Point-of-use activation authority was not established.", authority);
        return new(request, current, authority, null);
    }
}

/// <summary>The API implements this atomic persistence boundary; the platform supplies no host transaction.</summary>
public interface IConfigurationActivationTarget
{
    /// <summary>Returns either a committed new effective generation or a refusal with the current generation.</summary>
    /// <remarks>
    /// Under one transaction, read the current generation, verify projection and destination compatibility,
    /// call DecideCompareAndSwap with live Access authority, and conditionally commit candidate ownership,
    /// effective pointer, authority decision and reconstructable evidence intent together. Never retry a stale
    /// baseline. Pin every read/write unit of work to one generation. Publish evidence from the committed
    /// outbox. A lost acknowledgement is indeterminate: recover by evidence intent identity, never turn an
    /// unknown commit into a refusal or blindly repeat it. Return ConfirmCommitted only after durable commit.
    /// Scope intent identities to the tenant and refuse reuse with different decision inputs.
    /// </remarks>
    ValueTask<ConfigurationActivationOutcome> CompareAndSwapAsync(ConfigurationActivationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>A host-confirmed commit or a refusal; contradictory effective/refused states cannot be constructed.</summary>
public sealed class ConfigurationActivationOutcome
{
    private ConfigurationActivationOutcome(ConfigurationActivationDecision decision)
    {
        Decision = decision;
        EffectiveGeneration = decision.NewGeneration ?? decision.PriorGeneration;
    }

    /// <summary>The switch decision, including every bound input and any refusal.</summary>
    public ConfigurationActivationDecision Decision { get; }
    /// <summary>The newly committed generation, or the still-effective generation on refusal.</summary>
    public ConfigurationGeneration EffectiveGeneration { get; }

    /// <summary>Host attestation after durable atomic commit; never call before or on an unknown commit.</summary>
    public static ConfigurationActivationOutcome ConfirmCommitted(ConfigurationActivationDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.NewGeneration is null) throw new ArgumentException("configuration-switch-refused");
        return new(decision);
    }

    /// <summary>Returns a refusal without changing the effective generation.</summary>
    public static ConfigurationActivationOutcome Refused(ConfigurationActivationDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Refusal is null) throw new ArgumentException("configuration-refusal-required");
        return new(decision);
    }

    /// <summary>Host refusal before mutation, including a missing projection or changed destination compatibility.</summary>
    public static ConfigurationActivationOutcome Refused(ConfigurationGeneration current,
        ConfigurationActivationRequest request, ConfigurationActivationRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Prepared);
        ArgumentNullException.ThrowIfNull(refusal);
        if (string.IsNullOrWhiteSpace(refusal.Code) || string.IsNullOrWhiteSpace(refusal.Target)
            || string.IsNullOrWhiteSpace(refusal.Message)) throw new ArgumentException("configuration-validation-invalid");
        return new(new(request, current, null, refusal));
    }
}
