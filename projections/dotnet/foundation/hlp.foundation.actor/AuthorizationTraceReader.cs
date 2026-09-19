using System.Collections.Immutable;

namespace Harborline.Foundation.Authorization;

/// <summary>The API trace-read outcomes; absence and pre-decision refusal are not decisions.</summary>
public enum AuthorizationTraceAvailability
{
    /// <summary>The stored four-step trace exists.</summary>
    Available,
    /// <summary>No decision trace was recorded.</summary>
    NotAvailable,
    /// <summary>The read was refused; even existence is hidden.</summary>
    Refused,
    /// <summary>A guard refused before the decider ran.</summary>
    PreDecisionRefusal,
}

/// <summary>Public guard evidence, excluding classified diagnostics.</summary>
public sealed record AuthorizationPreDecisionRefusal(string Code, string Detail, string Remediation);

/// <summary>The stored counterfactual, promoted without any applying or calculating behavior.</summary>
public sealed record AuthorizationCounterfactualSnapshot(int Version, string Kind, string Direction,
    string Binding, int BindingOrdinal, string Description);

/// <summary>The reusable snapshot read contract. Audit payloads, signatures and HTTP stay in the host.</summary>
public sealed class AuthorizationTraceSnapshot(string tenant, string? principal, int? version,
    IEnumerable<AuthorizationTraceStep> steps, AuthorizationCounterfactualSnapshot? counterfactual = null,
    AuthorizationPreDecisionRefusal? refusal = null)
{
    /// <summary>The stored tenant.</summary>
    public string Tenant { get; } = tenant;
    /// <summary>The subject of the recorded decision.</summary>
    public string? Principal { get; } = principal;
    /// <summary>The stored schema version.</summary>
    public int? Version { get; } = version;
    /// <summary>The frozen trace; may include later separation-of-duty ordinals.</summary>
    public ImmutableArray<AuthorizationTraceStep> Steps { get; } = [.. steps];
    /// <summary>The original counterfactual.</summary>
    public AuthorizationCounterfactualSnapshot? Counterfactual { get; } = counterfactual;
    /// <summary>The public pre-decision guard report, if present.</summary>
    public AuthorizationPreDecisionRefusal? Refusal { get; } = refusal;
}

/// <summary>The narrow host storage port; it locates one entry inside the requested tenant.</summary>
public interface IAuthorizationTraceStore
{
    /// <summary>Returns only the stored public authorization snapshot, never an audit payload.</summary>
    ValueTask<AuthorizationTraceSnapshot?> FindAsync(string tenant, string entryId, CancellationToken cancellationToken = default);
}

/// <summary>A read of original stored evidence, never a replay.</summary>
public sealed record AuthorizationTraceRead(AuthorizationTraceAvailability Availability, int? Version,
    IReadOnlyList<AuthorizationTraceStep> Steps, AuthorizationCounterfactualSnapshot? Counterfactual,
    AuthorizationPreDecisionRefusal? Refusal = null);

/// <summary>
/// Promoted API read orchestration: authorize the current read with the gate, then return the historical
/// snapshot. The old operation never reaches the decider, even when its grants have since changed.
/// </summary>
public sealed class AuthorizationTraceReader(IAuthorizationTraceStore store, IAuthorizationDecider gate,
    IEqualityComparer<string>? principalComparer = null)
{
    private readonly IAuthorizationTraceStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly AccessProvider _access = new(gate);
    private readonly IEqualityComparer<string> _principals = principalComparer ?? StringComparer.Ordinal;

    /// <summary>Reads the caller's own trace with audit:trace-read, or another subject's with audit:read.</summary>
    public async ValueTask<AuthorizationTraceRead> ReadAsync(string tenant, string caller, string entryId,
        DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.FindAsync(tenant, entryId, cancellationToken).ConfigureAwait(false);
        if (snapshot?.Tenant != tenant) snapshot = null;
        var operation = snapshot?.Principal is { } subject && _principals.Equals(subject, caller) ? "audit:trace-read" : "audit:read";
        var check = await _access.CheckAsync(new(operation, caller, tenant,
            new(tenant, "audit", entryId, ImmutableDictionary<string, System.Text.Json.Nodes.JsonNode?>.Empty), at), cancellationToken).ConfigureAwait(false);
        if (!check.Allowed) return new(AuthorizationTraceAvailability.Refused, null, [], null);
        if (snapshot?.Refusal is { } refusal)
            return new(AuthorizationTraceAvailability.PreDecisionRefusal, null, [], null, refusal);

        // Select by ordinal: approval traces reuse the stage names under ordinals 5..8.
        var steps = (snapshot?.Steps ?? []).Where(step => step.Ordinal is >= 1 and <= AuthorizationDecisionEvidence.StepCount)
            .OrderBy(step => step.Ordinal).ToImmutableArray();
        string[] stages = ["act", "effective-roles", "standings", "verdict"];
        return steps.Length == AuthorizationDecisionEvidence.StepCount && snapshot?.Version is not null
            && steps.Select(step => step.Ordinal).SequenceEqual([1, 2, 3, 4])
            && steps.Select(step => step.Stage).SequenceEqual(stages)
                ? new(AuthorizationTraceAvailability.Available, snapshot.Version, steps, snapshot.Counterfactual)
                : new(AuthorizationTraceAvailability.NotAvailable, null, [], null);
    }
}
