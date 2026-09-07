using System.Text.Json;

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
            };
        }).ToList();

        var guardIds = ReadArray(el, "guards")
            .Select(g => OptionalString(g, "id"))
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToList();

        return new WorkflowDefinition
        {
            Key = key,
            Version = version,
            Status = WorkflowDefinitionStatus.Draft,
            Tenant = tenant,
            SubjectFormRef = ReadSubjectFormRef(el),
            Mutability = ParseEnum(el, "mutability", WorkflowMutability.Locked),
            InitialState = OptionalString(el, "initialState") ?? string.Empty,
            States = states,
            Transitions = transitions,
            Triggers = triggers,
            Actions = actions,
            GuardRuleIds = guardIds,
        };
    }

    private static FormDefinitionRef? ReadSubjectFormRef(JsonElement el)
    {
        if (!el.TryGetProperty("subjectFormRef", out var s) || s.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var formId = OptionalString(s, "formId");
        var version = OptionalString(s, "version");
        if (string.IsNullOrEmpty(formId) || string.IsNullOrEmpty(version))
        {
            return null;
        }
        return new FormDefinitionRef { FormId = formId, Version = version };
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
