using System.Text.Json;

using Harborline.Contracts.Authorization;
using Harborline.Contracts.Workflow;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.Workflow.Durable;

// ─────────────────────────────────────────────────────────────────────────────
//  WF-KEY — the canonical WIRE → lean-admission-MODEL parser. The .NET mirror of the
//  Harborline App builder's `fromWorkflowDefinition` (apps/carrier/.../workflows/builder/model.ts):
//  it reads the authored @harborline-software/contracts WorkflowDefinition JSON (states / transitions /
//  triggers / actions — display chrome ignored) into the lean WorkflowDefinition the
//  admission validator + the load-time re-validator + the A1 interpreter all reason over.
//
//  MOVED here from apps/local-node-host (the PUT route) so ONE parse feeds every path — the
//  register-time admission (WorkflowDefinitionRoutes PUT), the LOAD-time re-validation
//  (WorkflowDefinitionLoadValidator, ADR 0135 A1 R-1 / ADR 0143 R1-E), and the eventual A1
//  interpreter — with no drift between "the shape we admitted" and "the shape we execute".
//
//  FAIL-CLOSED mapping: an unknown/absent action classification ⇒ Unspecified (refused at
//  admission); an unknown trigger kind ⇒ Event (autonomous — so a CP downstream of it is
//  refused). A missing required id/field throws. Pure JsonElement → POCO; no I/O, no host deps.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Stable codes for a gate reference the mapper cannot read, the same ones the api's reader gives.</summary>
public static class WorkflowGateReferenceCodes
{
    /// <summary>A requiredStandings entry is not a bare standing name.</summary>
    public const string InvalidStanding = "authorization.gate_reference.required_standings_invalid";

    /// <summary>A requiredRoles entry is not a qualified role reference.</summary>
    public const string InvalidRole = "authorization.gate_reference.required_roles_invalid";
}

/// <summary>A role or standing the authored definition states in a shape no gate can read (fail-closed).</summary>
public sealed class WorkflowGateReferenceException(string code, string field, string value)
    : ArgumentException($"Gate reference refused: {code}; field={field}; value={value}.", field)
{
    /// <summary>A <see cref="WorkflowGateReferenceCodes"/> value.</summary>
    public string Code { get; } = code;

    /// <summary>The offending field, with the entry's index.</summary>
    public string Field { get; } = field;

    /// <summary>The refused value.</summary>
    public string Value { get; } = value;
}

/// <summary>
/// Maps the Harborline App's authored <c>@harborline-software/contracts</c> <c>WorkflowDefinition</c> JSON onto the lean
/// <see cref="WorkflowDefinition"/> admission model. Reads only the STRUCTURAL fields the admission gate
/// reasons over. The single canonical parse shared by register-time admission and load-time re-validation.
/// </summary>
public static class WorkflowDefinitionWireMapper
{
    /// <summary>
    /// Parses the authored definition JSON into the lean admission model, stamping the server-owned
    /// <paramref name="tenant"/> / <paramref name="key"/> / <paramref name="version"/> (never trusted from
    /// the body). Fail-closed on absent classifications/trigger kinds (see the file header).
    /// </summary>
    public static WorkflowDefinition ToModel(JsonElement el, string tenant, string key, string version)
    {
        var states = ReadArray(el, "states").Select(s => new WorkflowStateDef
        {
            Id = RequireString(s, "id", "state"),
            Kind = ParseEnum(s, "kind", WorkflowStateKind.Normal),
            ViewHint = OptionalString(s, "viewHint"),
        }).ToList();

        var transitions = ReadArray(el, "transitions").Select(t => new WorkflowTransitionDef
        {
            Id = RequireString(t, "id", "transition"),
            From = RequireString(t, "from", "transition.from"),
            On = RequireString(t, "on", "transition.on"),
            To = RequireString(t, "to", "transition.to"),
            Guard = OptionalString(t, "guard"),
            RequiredRoles = ReadRoles(t, "transition.requiredRoles"),
            RequiredStandings = ReadStandings(t, "transition.requiredStandings"),
        }).ToList();

        var triggers = ReadArray(el, "triggers").Select(tr => new WorkflowTriggerBindingDef
        {
            Id = RequireString(tr, "id", "trigger"),
            Kind = ParseEnum(tr, "kind", WorkflowTriggerKind.Event),
            EventType = OptionalString(tr, "eventType"),
            Rrule = OptionalString(tr, "rrule"),
            Task = OptionalString(tr, "task"),
            Dependency = OptionalString(tr, "dependency"),
        }).ToList();

        var actions = ReadArray(el, "actions").Select(a =>
        {
            string? onState = null, onTransition = null;
            if (a.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.Object)
            {
                if (on.TryGetProperty("state", out var st) && st.ValueKind == JsonValueKind.String)
                {
                    onState = st.GetString();
                }
                else if (on.TryGetProperty("transition", out var trn) && trn.ValueKind == JsonValueKind.String)
                {
                    onTransition = trn.GetString();
                }
            }

            return new WorkflowActionBindingDef
            {
                Id = RequireString(a, "id", "action"),
                OnState = onState,
                OnTransition = onTransition,
                Kind = ParseEnum(a, "kind", WorkflowActionKind.Notify),
                CapabilityRef = OptionalString(a, "capabilityRef") ?? string.Empty,
                // Fail-closed: an absent/unknown classification ⇒ Unspecified ⇒ refused at admission.
                Classification = ParseEnum(a, "classification", ActionClassification.Unspecified),
                Condition = OptionalString(a, "condition"),
                RequiredRoles = ReadRoles(a, "action.requiredRoles"),
                RequiredStandings = ReadStandings(a, "action.requiredStandings"),
            };
        }).ToList();

        var guardIds = ReadArray(el, "guards")
            .Select(g => OptionalString(g, "id"))
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToList();

        return new WorkflowDefinition
        {
            Envelope = ReadEnvelope(el),
            Key = key,
            Version = version,
            Status = WorkflowDefinitionStatus.Draft,
            Tenant = tenant,
            Subject = ReadSubject(el),
            Mutability = ParseEnum(el, "mutability", WorkflowMutability.Locked),
            InitialState = OptionalString(el, "initialState") ?? string.Empty,
            States = states,
            Transitions = transitions,
            Triggers = triggers,
            Actions = actions,
            GuardRuleIds = guardIds,
        };
    }

    /// <summary>
    /// Reads the optional envelope as authored. Its identity, version and tenant are kept, not overwritten with the
    /// server-owned stamp, so that admission can refuse an envelope naming some other definition.
    /// </summary>
    private static WorkflowDefinitionEnvelope? ReadEnvelope(JsonElement el)
    {
        if (!el.TryGetProperty("envelope", out var envelope)) return null;
        if (envelope.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("workflow definition 'envelope' must be an object.");
        if (!envelope.TryGetProperty("legalHold", out var legalHold) || legalHold.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException("workflow definition envelope is missing a boolean 'legalHold'.");
        if (!envelope.TryGetProperty("requires", out var requires) || requires.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("workflow definition envelope is missing a 'requires' array.");
        return new WorkflowDefinitionEnvelope(
            RequireString(envelope, "identity", "envelope"),
            RequireString(envelope, "version", "envelope"),
            RequireString(envelope, "tenant", "envelope"),
            ReadCascadeLayer(envelope),
            envelope.TryGetProperty("provenance", out var provenance) ? provenance.Clone() : null,
            RequireString(envelope, "retentionClass", "envelope"),
            legalHold.GetBoolean(),
            requires.EnumerateArray().Select(requirement => new WorkflowDefinitionRequirement(
                requirement.ValueKind == JsonValueKind.Object ? OptionalString(requirement, "capability") ?? string.Empty : string.Empty)).ToArray(),
            ReadContract(envelope));
    }

    // Exact names only: Enum.TryParse would also take "4" and any casing, and an absent layer has no safe fallback.
    private static WorkflowCascadeLayer ReadCascadeLayer(JsonElement envelope)
    {
        var name = OptionalString(envelope, "cascadeLayer");
        return name is not null && Enum.GetNames<WorkflowCascadeLayer>().Contains(name, StringComparer.Ordinal)
            ? Enum.Parse<WorkflowCascadeLayer>(name)
            : throw new ArgumentException($"workflow definition envelope has an unknown 'cascadeLayer' '{name}'.");
    }

    // Absent stays null, so admission gives the stable definition.contract.missing refusal.
    private static DefinitionContractVersion? ReadContract(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("contract", out var contract)) return null;
        return contract.ValueKind == JsonValueKind.Object
            && contract.TryGetProperty("major", out var major) && major.ValueKind == JsonValueKind.Number && major.TryGetInt32(out var majorValue)
            && contract.TryGetProperty("minor", out var minor) && minor.ValueKind == JsonValueKind.Number && minor.TryGetInt32(out var minorValue)
                ? new DefinitionContractVersion(majorValue, minorValue)
                : throw new ArgumentException("workflow definition envelope 'contract' must carry integer 'major' and 'minor'.");
    }

    // An entry is the api's "vocabulary/name" string or the contract wire's {vocabulary, name} object.
    private static IReadOnlyList<RoleReference> ReadRoles(JsonElement el, string field) =>
        ReadGateEntries(el, "requiredRoles", field, WorkflowGateReferenceCodes.InvalidRole, (entry, at) =>
        {
            if (entry.ValueKind == JsonValueKind.String) return ParseRole(entry.GetString()!, at);
            return entry.ValueKind == JsonValueKind.Object
                && OptionalString(entry, "vocabulary") is { Length: > 0 } vocabulary
                && OptionalString(entry, "name") is { Length: > 0 } name
                    ? new RoleReference(vocabulary, name)
                    : throw new WorkflowGateReferenceException(WorkflowGateReferenceCodes.InvalidRole, at, entry.GetRawText());
        });

    private static RoleReference ParseRole(string value, string field)
    {
        var separator = value.IndexOf('/', StringComparison.Ordinal);
        if (separator > 0 && separator == value.LastIndexOf('/') && separator < value.Length - 1)
            return new RoleReference(value[..separator], value[(separator + 1)..]);

        // Compatibility spellings the api's reader resolves into the one platform vocabulary: a wire alias,
        // not a second role catalogue.
        if (value is "Admin" or "Member" or "node:operator") return RoleReference.Administrator;
        if (value is "Viewer") return RoleReference.Auditor;
        throw new WorkflowGateReferenceException(WorkflowGateReferenceCodes.InvalidRole, field, value);
    }

    // An entry is the api's bare string or the contract wire's {name} object.
    private static IReadOnlyList<RecordStandingReference> ReadStandings(JsonElement el, string field) =>
        ReadGateEntries(el, "requiredStandings", field, WorkflowGateReferenceCodes.InvalidStanding, (entry, at) =>
        {
            var name = entry.ValueKind switch
            {
                JsonValueKind.String => entry.GetString(),
                JsonValueKind.Object => OptionalString(entry, "name"),
                _ => null,
            };
            return WorkflowGateVocabulary.IsStandingName(name)
                ? new RecordStandingReference(name!)
                : throw new WorkflowGateReferenceException(WorkflowGateReferenceCodes.InvalidStanding, at, name ?? entry.GetRawText());
        });

    private static IReadOnlyList<T> ReadGateEntries<T>(
        JsonElement el, string prop, string field, string code, Func<JsonElement, string, T> read)
    {
        if (!el.TryGetProperty(prop, out var entries)) return [];
        if (entries.ValueKind != JsonValueKind.Array)
            throw new WorkflowGateReferenceException(code, field, entries.GetRawText());
        return entries.EnumerateArray().Select((entry, index) => read(entry, $"{field}[{index}]")).ToArray();
    }

    // Exactly one of record, set or case (ADR 0099), as the contract wire reads it; a malformed subject throws rather
    // than reading as no subject.
    private static WorkflowSubjectDef? ReadSubject(JsonElement el)
    {
        if (!el.TryGetProperty("subject", out var subject)) return null;
        string[] kinds = subject.ValueKind == JsonValueKind.Object
            ? [.. new[] { "record", "set", "case" }.Where(kind => subject.TryGetProperty(kind, out _))]
            : [];
        if (kinds.Length != 1 || subject.GetProperty(kinds[0]).ValueKind != JsonValueKind.Object)
            throw new ArgumentException("workflow definition 'subject' must be an object naming exactly one of record, set or case.");
        var body = subject.GetProperty(kinds[0]);
        return kinds[0] switch
        {
            "record" => WorkflowSubjectDef.Record(RequireString(body, "recordType", "subject.record")),
            "set" => WorkflowSubjectDef.Set(RequireString(body, "recordType", "subject.set")),
            _ => WorkflowSubjectDef.Case,
        };
    }

    private static IEnumerable<JsonElement> ReadArray(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string RequireString(JsonElement el, string prop, string what)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.GetString())
            ? v.GetString()!
            : throw new ArgumentException($"workflow definition {what} is missing a non-empty '{prop}'.");

    private static string? OptionalString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static TEnum ParseEnum<TEnum>(JsonElement el, string prop, TEnum fallback) where TEnum : struct, Enum
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            && Enum.TryParse<TEnum>(v.GetString(), ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
}
