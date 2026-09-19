using System.Collections.Immutable;
using System.Globalization;

namespace Harborline.Foundation.Authorization;

/// <summary>One effective role, copied from the enforcing gate's evidence.</summary>
public sealed record AuthorizationEvidenceRole(string Role, string Scope, DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil, string BindingId, long? BindingVersion, string DefinitionId, string Atom,
    bool? InForce, bool Deciding);

/// <summary>One standing already computed by the gate.</summary>
public sealed record AuthorizationEvidenceStanding(string Role, string RuleId, string EvidenceVersion);

/// <summary>One immutable public trace step.</summary>
public sealed class AuthorizationTraceStep(int ordinal, string stage, IEnumerable<string> facts)
{
    /// <summary>The stored ordinal.</summary>
    public int Ordinal { get; } = ordinal;
    /// <summary>The public stage name.</summary>
    public string Stage { get; } = stage;
    /// <summary>The frozen public facts.</summary>
    public ImmutableArray<string> Facts { get; } = [.. facts];
}

/// <summary>
/// Pure projection promoted from the API evidence contract. The host supplies the enforcing verdict,
/// its classified refusal and deciding binding; this type never calculates any of them.
/// Roster, attenuation and approval facts remain host-computed inputs, with no policy here.
/// </summary>
public sealed class AuthorizationDecisionEvidence
{
    /// <summary>The API trace schema version.</summary>
    public const int CurrentVersion = 2;
    /// <summary>The number of steps in one decision.</summary>
    public const int StepCount = 4;
    /// <summary>The act stage.</summary>
    public const string ActStage = "act";
    /// <summary>The effective roles stage.</summary>
    public const string EffectiveRolesStage = "effective-roles";
    /// <summary>The standings stage.</summary>
    public const string StandingsStage = "standings";
    /// <summary>The enforcing verdict stage.</summary>
    public const string VerdictStage = "verdict";

    /// <summary>Freezes values computed by the sole decider; no grant, role or scope evaluation occurs.</summary>
    public AuthorizationDecisionEvidence(AccessRequest request, bool allowed, string refusal, string decidingBinding,
        IEnumerable<AuthorizationEvidenceRole> roles, IEnumerable<AuthorizationEvidenceStanding> standings,
        IEnumerable<string>? bindingFacts = null, string? grantRefusal = null, string kind = "Gate",
        IEnumerable<string>? approvals = null, string? target = null, string? act = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        Operation = request.Operation;
        Act = act ?? request.Operation;
        Principal = request.Principal;
        Tenant = request.Tenant;
        RecordKind = request.Record.Kind;
        RecordId = request.Record.Id;
        At = request.At;
        Allowed = allowed;
        Refusal = refusal;
        var bindings = kind == "SeparationOfDuty"
            ? (approvals ?? []).ToImmutableArray() : roles.Select(Describe).ToImmutableArray();
        var standingFacts = standings.Select(item => $"role:{item.Role};rule:{item.RuleId};evidence:{item.EvidenceVersion}").ToArray();
        Steps = [
            new(1, ActStage, [$"kind:{kind}", $"act:{Act}", $"target:{target ?? $"{RecordKind}/{RecordId}"}",
                $"principal:{Principal}", $"tenant:{Tenant}", $"at:{At:O}"]),
            new(2, EffectiveRolesStage, [.. bindings.Length == 0 ? ["roles:none"] : bindings,
                $"deciding:{decidingBinding}", .. bindingFacts ?? []]),
            new(3, StandingsStage, standingFacts.Length == 0 ? ["standings:none"] : standingFacts),
            new(4, VerdictStage, [$"verdict:{(Allowed ? "allowed" : "denied")}", $"refusal:{Refusal}",
                $"version:{CurrentVersion}", .. grantRefusal is null ? Array.Empty<string>() : [$"grant-refusal:{grantRefusal}"]]),
        ];
    }

    /// <summary>The decided operation.</summary>
    public string Operation { get; }
    /// <summary>The original scoped act spelling supplied by the gate adapter.</summary>
    public string Act { get; }
    /// <summary>The schema version of the projected evidence.</summary>
    public int Version => CurrentVersion;
    /// <summary>The decided principal.</summary>
    public string Principal { get; }
    /// <summary>The decided tenant.</summary>
    public string Tenant { get; }
    /// <summary>The target record kind.</summary>
    public string RecordKind { get; }
    /// <summary>The target record identity.</summary>
    public string RecordId { get; }
    /// <summary>The predicate instant.</summary>
    public DateTimeOffset At { get; }
    /// <summary>The enforcing gate's verdict, preserved verbatim.</summary>
    public bool Allowed { get; }
    /// <summary>The enforcing gate's stable refusal reason.</summary>
    public string Refusal { get; }
    private ImmutableArray<AuthorizationTraceStep> Steps { get; }
    /// <summary>Returns exactly the four steps frozen at construction, without evaluation.</summary>
    public IReadOnlyList<AuthorizationTraceStep> Project() => Steps;

    private static string Describe(AuthorizationEvidenceRole role) =>
        $"role:{role.Role};scope:{role.Scope};valid:{role.ValidFrom:O}..{(role.ValidUntil is { } end ? end.ToString("O", CultureInfo.InvariantCulture) : "open")}"
        + $";binding:{role.BindingId}@{(role.BindingVersion is { } version ? version.ToString(CultureInfo.InvariantCulture) : "-")}"
        + $";definition:{role.DefinitionId};atom:{role.Atom};in-force:{role.InForce switch { true => "yes", false => "no", null => "not-computed" }}"
        + (role.Deciding ? ";deciding" : string.Empty);
}
