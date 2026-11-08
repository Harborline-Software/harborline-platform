#nullable enable
#pragma warning disable CS1591

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harborline.Contracts.Authorization;
using Harborline.Contracts.Forms;
using Harborline.Contracts.Serialization;

namespace Harborline.Contracts.Workflow;

public sealed class GuardRefJsonConverter : JsonConverter<GuardRef>
{
    public override GuardRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? new(reader.GetString()!) : throw new JsonException("GuardRef must be a string.");
    public override void Write(Utf8JsonWriter writer, GuardRef value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

public sealed class WorkflowEffectParamsJsonConverter : JsonConverter<WorkflowEffectParams>
{
    public override WorkflowEffectParams Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(JsonSerializer.Deserialize<IReadOnlyDictionary<string, JsonElement>>(ref reader, options)
            ?? throw new JsonException("WorkflowEffectParams must be an object."));
    public override void Write(Utf8JsonWriter writer, WorkflowEffectParams value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Values, options);
}

public sealed class WorkflowActionTargetJsonConverter : JsonConverter<WorkflowActionTarget>
{
    public override WorkflowActionTarget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var value = document.RootElement;
        var hasState = value.TryGetProperty("state", out var state);
        var hasTransition = value.TryGetProperty("transition", out var transition);
        if (value.ValueKind != JsonValueKind.Object || hasState == hasTransition)
            throw new WorkflowWireException("exclusive-union-mismatch", "WorkflowActionBinding.on");
        if (hasState && state.ValueKind == JsonValueKind.String) return new WorkflowStateActionTarget(state.GetString()!);
        if (hasTransition && transition.ValueKind == JsonValueKind.String) return new WorkflowTransitionActionTarget(transition.GetString()!);
        throw new WorkflowWireException("invalid-string", "WorkflowActionBinding.on");
    }

    public override void Write(Utf8JsonWriter writer, WorkflowActionTarget value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value is WorkflowStateActionTarget state) writer.WriteString("state", state.State);
        else if (value is WorkflowTransitionActionTarget transition) writer.WriteString("transition", transition.Transition);
        else throw new JsonException("Unknown WorkflowActionTarget subtype.");
        writer.WriteEndObject();
    }
}

public sealed class WorkflowWireException(string code, string type, string? detail = null)
    : JsonException($"{code}: {type}{(detail is null ? string.Empty : $" ({detail})")}")
{
    public string Code { get; } = code;
    public string WireType { get; } = type;
    public string? Detail { get; } = detail;
}

public static class WorkflowContractSurface
{
    public static IReadOnlyList<string> Exports { get; } =
    [
        "WorkflowDefinitionStatus", "WorkflowMutability", "WorkflowStateKind", "WorkflowViewHint",
        "WorkflowTriggerKind", "WorkflowActionKind", "ActionClassification", "GuardRef", "WorkflowState",
        "WorkflowTransition", "WorkflowTriggerBinding", "WorkflowEffectParams", "WorkflowActionBinding",
        "WorkflowDefinitionLineage", "FormDefinitionRef", "WorkflowDefinition",
        "WorkflowCascadeLayer", "WorkflowContractVersion", "WorkflowEnvelopeRequirement", "WorkflowEnvelope", "WorkflowGateRole",
    ];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ClosedValues { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["WorkflowDefinitionStatus"] = ["Draft", "Published", "Deprecated", "Withdrawn"],
            ["WorkflowMutability"] = ["Locked", "Editable"],
            ["WorkflowStateKind"] = ["Normal", "Terminal"],
            ["WorkflowViewHint"] = ["flow", "gantt", "kanban"],
            ["WorkflowTriggerKind"] = ["Event", "Schedule", "HumanAction", "DependencyComplete"],
            ["WorkflowActionKind"] = ["Notify", "CreateRecord", "UpdateField", "InvokeService", "StartSubProcess", "EmitEvent"],
            ["ActionClassification"] = ["CP", "AP"],
            ["WorkflowCascadeLayer"] = ["KernelCore", "Subsystem", "PlatformPackage", "DomainPackage", "TenantConfiguration"],
        };
}

public static class WorkflowWire
{
    private static readonly Dictionary<string, Type> Types = new()
    {
        ["WorkflowDefinitionStatus"] = typeof(WorkflowDefinitionStatus), ["WorkflowMutability"] = typeof(WorkflowMutability),
        ["WorkflowStateKind"] = typeof(WorkflowStateKind), ["WorkflowViewHint"] = typeof(WorkflowViewHint),
        ["WorkflowTriggerKind"] = typeof(WorkflowTriggerKind), ["WorkflowActionKind"] = typeof(WorkflowActionKind),
        ["ActionClassification"] = typeof(ActionClassification), ["GuardRef"] = typeof(GuardRef),
        ["WorkflowState"] = typeof(WorkflowState), ["WorkflowTransition"] = typeof(WorkflowTransition),
        ["WorkflowTriggerBinding"] = typeof(WorkflowTriggerBinding), ["WorkflowEffectParams"] = typeof(WorkflowEffectParams),
        ["WorkflowActionBinding"] = typeof(WorkflowActionBinding), ["WorkflowDefinitionLineage"] = typeof(WorkflowDefinitionLineage),
        ["FormDefinitionRef"] = typeof(FormDefinitionRef), ["WorkflowDefinition"] = typeof(WorkflowDefinition),
        ["WorkflowCascadeLayer"] = typeof(WorkflowCascadeLayer), ["WorkflowContractVersion"] = typeof(WorkflowContractVersion),
        ["WorkflowEnvelopeRequirement"] = typeof(WorkflowEnvelopeRequirement), ["WorkflowEnvelope"] = typeof(WorkflowEnvelope),
    };

    public static object Deserialize(string type, string json)
    {
        using var document = JsonDocument.Parse(json);
        Validate(type, document.RootElement);
        if (!Types.TryGetValue(type, out var target)) throw new WorkflowWireException("unknown-type", type);
        try
        {
            return JsonSerializer.Deserialize(document.RootElement, target, FormsJson.Options)
                ?? throw new WorkflowWireException("invalid-null", type);
        }
        catch (WorkflowWireException) { throw; }
        catch (JsonException error) { throw new WorkflowWireException("invalid-wire-value", type, error.Message); }
    }

    public static string Serialize(string type, object value)
    {
        if (!Types.TryGetValue(type, out var target)) throw new WorkflowWireException("unknown-type", type);
        return JsonSerializer.Serialize(value, target, FormsJson.Options);
    }

    public static void Validate(string type, JsonElement value)
    {
        if (WorkflowContractSurface.ClosedValues.TryGetValue(type, out var values))
        {
            Require(value.ValueKind == JsonValueKind.String && values.Contains(value.GetString()!), "unknown-closed-value", type);
            return;
        }
        switch (type)
        {
            case "GuardRef": String(value, type); break;
            case "WorkflowEffectParams": Object(value, type); break;
            case "WorkflowState": State(value); break;
            case "WorkflowTransition": Transition(value); break;
            case "WorkflowTriggerBinding": Trigger(value); break;
            case "WorkflowActionBinding": Action(value); break;
            case "WorkflowDefinitionLineage": Lineage(value); break;
            case "FormDefinitionRef": FormRef(value); break;
            case "WorkflowDefinition": Definition(value); break;
            case "WorkflowContractVersion": ContractVersion(value); break;
            case "WorkflowEnvelopeRequirement": Requirement(value); break;
            case "WorkflowEnvelope": Envelope(value); break;
            default: throw new WorkflowWireException("unknown-type", type);
        }
    }

    private static void State(JsonElement value)
    {
        Object(value, "WorkflowState");
        String(Required(value, "id", "WorkflowState"), "WorkflowState.id");
        FormsJson.Validate("InternationalizedText", Required(value, "label", "WorkflowState"));
        Validate("WorkflowStateKind", Required(value, "kind", "WorkflowState"));
        Optional(value, "viewHint", item => Validate("WorkflowViewHint", item));
    }

    private static void Transition(JsonElement value)
    {
        Object(value, "WorkflowTransition");
        foreach (var name in new[] { "id", "from", "on", "to" }) String(Required(value, name, "WorkflowTransition"), $"WorkflowTransition.{name}");
        Optional(value, "guard", item => String(item, "WorkflowTransition.guard"));
        Optional(value, "requiredRoles", item => RoleArray(item, "WorkflowTransition.requiredRoles"));
        Optional(value, "requiredStandings", item => StandingArray(item, "WorkflowTransition.requiredStandings"));
    }

    private static void Trigger(JsonElement value)
    {
        Object(value, "WorkflowTriggerBinding");
        String(Required(value, "id", "WorkflowTriggerBinding"), "WorkflowTriggerBinding.id");
        Validate("WorkflowTriggerKind", Required(value, "kind", "WorkflowTriggerBinding"));
        foreach (var name in new[] { "eventType", "rrule", "task", "dependency" }) Optional(value, name, item => String(item, $"WorkflowTriggerBinding.{name}"));
    }

    private static void Action(JsonElement value)
    {
        Object(value, "WorkflowActionBinding");
        String(Required(value, "id", "WorkflowActionBinding"), "WorkflowActionBinding.id");
        var on = Required(value, "on", "WorkflowActionBinding");
        Object(on, "WorkflowActionBinding.on");
        var hasState = on.TryGetProperty("state", out var state);
        var hasTransition = on.TryGetProperty("transition", out var transition);
        Require(hasState != hasTransition, "exclusive-union-mismatch", "WorkflowActionBinding.on");
        String(hasState ? state : transition, "WorkflowActionBinding.on");
        Validate("WorkflowActionKind", Required(value, "kind", "WorkflowActionBinding"));
        String(Required(value, "capabilityRef", "WorkflowActionBinding"), "WorkflowActionBinding.capabilityRef");
        Validate("ActionClassification", Required(value, "classification", "WorkflowActionBinding"));
        Optional(value, "condition", item => String(item, "WorkflowActionBinding.condition"));
        Optional(value, "requiredRoles", item => RoleArray(item, "WorkflowActionBinding.requiredRoles"));
        Optional(value, "requiredStandings", item => StandingArray(item, "WorkflowActionBinding.requiredStandings"));
        Optional(value, "params", item => Object(item, "WorkflowEffectParams"));
    }

    private static void ContractVersion(JsonElement value)
    {
        Object(value, "WorkflowContractVersion");
        foreach (var name in new[] { "major", "minor" })
        {
            var member = Required(value, name, "WorkflowContractVersion");
            Require(member.ValueKind == JsonValueKind.Number && member.TryGetInt32(out _), "invalid-integer", $"WorkflowContractVersion.{name}");
        }
    }

    private static void Requirement(JsonElement value)
    {
        Object(value, "WorkflowEnvelopeRequirement");
        String(Required(value, "capability", "WorkflowEnvelopeRequirement"), "WorkflowEnvelopeRequirement.capability");
    }

    private static void Envelope(JsonElement value)
    {
        Object(value, "WorkflowEnvelope");
        foreach (var name in new[] { "identity", "version", "tenant", "retentionClass" }) String(Required(value, name, "WorkflowEnvelope"), $"WorkflowEnvelope.{name}");
        Validate("WorkflowCascadeLayer", Required(value, "cascadeLayer", "WorkflowEnvelope"));
        Object(Required(value, "provenance", "WorkflowEnvelope"), "WorkflowEnvelope.provenance");
        var legalHold = Required(value, "legalHold", "WorkflowEnvelope");
        Require(legalHold.ValueKind is JsonValueKind.True or JsonValueKind.False, "invalid-boolean", "WorkflowEnvelope.legalHold");
        Array(value, "requires", "WorkflowEnvelope", Requirement);
        Optional(value, "contract", ContractVersion);
    }

    private static void Lineage(JsonElement value)
    {
        Object(value, "WorkflowDefinitionLineage");
        String(Required(value, "parentDefinitionKey", "WorkflowDefinitionLineage"), "WorkflowDefinitionLineage.parentDefinitionKey");
        String(Required(value, "parentVersion", "WorkflowDefinitionLineage"), "WorkflowDefinitionLineage.parentVersion");
    }

    private static void FormRef(JsonElement value)
    {
        Object(value, "FormDefinitionRef");
        String(Required(value, "formId", "FormDefinitionRef"), "FormDefinitionRef.formId");
        String(Required(value, "version", "FormDefinitionRef"), "FormDefinitionRef.version");
    }

    private static void Definition(JsonElement value)
    {
        Object(value, "WorkflowDefinition");
        foreach (var name in new[] { "key", "version", "tenant", "initialState", "createdAt", "updatedAt" }) String(Required(value, name, "WorkflowDefinition"), $"WorkflowDefinition.{name}");
        Validate("WorkflowDefinitionStatus", Required(value, "status", "WorkflowDefinition"));
        FormsJson.Validate("IdentityRef", Required(value, "owner", "WorkflowDefinition"));
        FormsJson.Validate("InternationalizedText", Required(value, "title", "WorkflowDefinition"));
        Optional(value, "description", item => FormsJson.Validate("InternationalizedText", item));
        Optional(value, "subjectFormRef", FormRef);
        Optional(value, "envelope", Envelope);
        Validate("WorkflowMutability", Required(value, "mutability", "WorkflowDefinition"));
        Array(value, "states", "WorkflowDefinition", State);
        Array(value, "transitions", "WorkflowDefinition", Transition);
        Array(value, "triggers", "WorkflowDefinition", Trigger);
        Array(value, "actions", "WorkflowDefinition", Action);
        Array(value, "guards", "WorkflowDefinition", item => FormsJson.Validate("RuleDefinition", item));
        Optional(value, "lineage", Lineage);
    }

    private static JsonElement Required(JsonElement source, string name, string type)
    {
        Require(source.TryGetProperty(name, out var value), "missing-required-property", type, name);
        return value;
    }
    private static void Optional(JsonElement source, string name, Action<JsonElement> validate) { if (source.TryGetProperty(name, out var value)) validate(value); }
    private static void Array(JsonElement source, string name, string type, Action<JsonElement> validate)
    {
        var value = Required(source, name, type); Require(value.ValueKind == JsonValueKind.Array, "invalid-array", $"{type}.{name}");
        foreach (var item in value.EnumerateArray()) validate(item);
    }
    private static void Object(JsonElement value, string type) => Require(value.ValueKind == JsonValueKind.Object, "invalid-object", type);
    private static void String(JsonElement value, string type) => Require(value.ValueKind == JsonValueKind.String, "invalid-string", type);
    private static void RoleArray(JsonElement value, string type)
    {
        Require(value.ValueKind == JsonValueKind.Array, "invalid-array", type);
        foreach (var role in value.EnumerateArray())
        {
            Object(role, "RoleReference");
            var vocabulary = Required(role, "vocabulary", "RoleReference");
            String(vocabulary, "RoleReference.vocabulary");
            Require(WorkflowGateVocabulary.IsKnownVocabulary(vocabulary.GetString()), "unknown-role-vocabulary", "RoleReference");
            String(Required(role, "name", "RoleReference"), "RoleReference.name");
        }
    }
    private static void StandingArray(JsonElement value, string type)
    {
        Require(value.ValueKind == JsonValueKind.Array, "invalid-array", type);
        foreach (var standing in value.EnumerateArray())
        {
            Object(standing, "RecordStandingReference");
            String(Required(standing, "name", "RecordStandingReference"), "RecordStandingReference.name");
        }
    }
    private static void Require(bool condition, string code, string type, string? detail = null) { if (!condition) throw new WorkflowWireException(code, type, detail); }
}

public static class WorkflowAdmissionCodes
{
    public const string ClassificationMismatch = "workflow.admission.classification_mismatch";
    public const string CpReachableWithoutHumanTask = "workflow.admission.cp_reachable_without_human_task";
    public const string InitialStateMissing = "workflow.admission.initial_state_missing";
    public const string DeadEndState = "workflow.admission.dead_end_state";
    public const string TerminalHasOutgoing = "workflow.admission.terminal_has_outgoing";
    public const string UnreachableState = "workflow.admission.unreachable_state";
    public const string DanglingReference = "workflow.admission.dangling_reference";
    public const string ActionBindingInvalid = "workflow.admission.action_binding_invalid";
    public const string DuplicateId = "workflow.admission.duplicate_id";
    public const string UnknownRoleReference = "workflow.admission.unknown_role_reference";
    public const string InvalidStanding = "workflow.admission.invalid_standing";
    public const string EnvelopeRequired = "workflow.admission.envelope_required";
    public const string EnvelopeMismatch = "workflow.admission.envelope_mismatch";
    public const string EnvelopeInvalid = "workflow.admission.envelope_invalid";
    public const string ContractMissing = "definition.contract.missing";
    public const string ContractOutOfWindow = "definition.contract.out_of_window";
}

/// <summary>
/// The vocabulary a workflow gate may name (ADR 0041, DES-0019 workflows-ck-15 and workflows-auth-10/11). sys.roles is
/// sealed here, because the engine works its four record-relative positions out for itself; the other two
/// vocabularies stay references that Access resolves. The engine's model reads this same list.
/// </summary>
public static class WorkflowGateVocabulary
{
    /// <summary>The sealed record-relative vocabulary.</summary>
    public const string SealedRoles = "sys.roles";

    /// <summary>The only names sys.roles carries (L421).</summary>
    public static IReadOnlyList<string> SealedRoleNames { get; } = ["Anyone", "Author", "Reviewer", "Approver"];

    /// <summary>True for the three ADR 0041 vocabularies.</summary>
    public static bool IsKnownVocabulary(string? vocabulary) =>
        vocabulary is SealedRoles or RoleVocabularies.Platform or RoleVocabularies.Domain;

    /// <summary>
    /// True when the reference can name a role: a known vocabulary, a non-blank name and, inside sys.roles, one of the
    /// sealed names compared exactly.
    /// </summary>
    public static bool IsAdmissibleRole(RoleReference role) =>
        IsKnownVocabulary(role.Vocabulary)
        && !string.IsNullOrWhiteSpace(role.Name)
        && (role.Vocabulary != SealedRoles || SealedRoleNames.Contains(role.Name, StringComparer.Ordinal));

    /// <summary>
    /// True for a bare standing name. Standings are record-derived facts that a gate adds to and every one of them must
    /// hold, so a gate can only narrow; a role path, a wildcard or a negation has no bare name and cannot be stated.
    /// </summary>
    public static bool IsStandingName(string? name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsAsciiLetter(name[0])) return false;
        foreach (var c in name)
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')) return false;
        return true;
    }
}

/// <summary>The wire mirror of DefinitionContractWindow: same rule, same codes, proven by the shared fixtures.</summary>
public sealed record WorkflowContractWindow(int Major, int Minor, int OldestMajor)
{
    /// <summary>Returns the refusal code when <paramref name="declared"/> is absent, invalid, or outside this window.</summary>
    public string? Check(WorkflowContractVersion? declared)
    {
        if (declared is null || declared.Major < 0 || declared.Minor < 0) return WorkflowAdmissionCodes.ContractMissing;
        var outside = declared.Major > Major
            || declared.Major < OldestMajor
            || (declared.Major == Major && declared.Minor > Minor)
            || (Major == 0 && declared.Major == 0 && declared.Minor != Minor);
        return outside ? WorkflowAdmissionCodes.ContractOutOfWindow : null;
    }
}

public sealed record WorkflowAdmissionViolation(string Code, string Message, string Locator);
public sealed record WorkflowAdmissionResult(bool IsValid, IReadOnlyList<WorkflowAdmissionViolation> Violations);
public interface IWorkflowAuthorityResolver { ActionClassification AuthorityOf(string capabilityRef); }

public sealed class ImmutableWorkflowAuthorityResolver : IWorkflowAuthorityResolver
{
    private readonly Dictionary<string, ActionClassification> _registry;
    public ImmutableWorkflowAuthorityResolver(IReadOnlyDictionary<string, ActionClassification> registry)
    {
        foreach (var (capabilityRef, classification) in registry)
            if (classification is not ActionClassification.CP and not ActionClassification.AP)
                throw new ArgumentOutOfRangeException(nameof(registry), $"Unknown authority classification for '{capabilityRef}'.");
        _registry = new Dictionary<string, ActionClassification>(registry, StringComparer.Ordinal);
    }
    public ActionClassification AuthorityOf(string capabilityRef) => _registry.TryGetValue(capabilityRef, out var value) ? value : ActionClassification.CP;
}

public static class WorkflowAdmissionValidator
{
    public static WorkflowAdmissionResult Validate(WorkflowDefinition definition, IWorkflowAuthorityResolver authority, RoleVocabulary? roleVocabulary = null, WorkflowContractWindow? contractWindow = null)
    {
        var violations = new List<WorkflowAdmissionViolation>();
        void Add(string code, string message, string locator) => violations.Add(new(code, message, locator));
        CheckEnvelope(definition, contractWindow, Add);
        var stateIds = Unique(definition.States, row => row.Id, "state", Add);
        var triggerById = UniqueMap(definition.Triggers, row => row.Id, "trigger", Add);
        var transitionById = UniqueMap(definition.Transitions, row => row.Id, "transition", Add);
        _ = Unique(definition.Actions, row => row.Id, "action", Add);
        var guardIds = Unique(definition.Guards, row => row.Id, "guard", Add);

        if (!stateIds.Contains(definition.InitialState)) Add(WorkflowAdmissionCodes.InitialStateMissing, "initial state does not exist", definition.InitialState);
        foreach (var transition in definition.Transitions)
        {
            if (!stateIds.Contains(transition.From) || !stateIds.Contains(transition.To) || !triggerById.ContainsKey(transition.On))
                Add(WorkflowAdmissionCodes.DanglingReference, "transition contains a dangling reference", transition.Id);
            if (transition.Guard.HasValue && !guardIds.Contains(transition.Guard.Value.Value))
                Add(WorkflowAdmissionCodes.DanglingReference, "transition guard does not exist", transition.Id);
            if (transition.RequiredRoles.HasValue && transition.RequiredRoles.Value!.Any(role => !RoleAdmits(role, roleVocabulary)))
                Add(WorkflowAdmissionCodes.UnknownRoleReference, "transition requires an unknown role", transition.Id);
            if (transition.RequiredStandings.HasValue && transition.RequiredStandings.Value!.Any(standing => !WorkflowGateVocabulary.IsStandingName(standing?.Name)))
                Add(WorkflowAdmissionCodes.InvalidStanding, "transition requires a malformed standing", transition.Id);
        }
        foreach (var action in definition.Actions)
        {
            if (action.On is WorkflowStateActionTarget state && !stateIds.Contains(state.State)) Add(WorkflowAdmissionCodes.DanglingReference, "action state does not exist", action.Id);
            else if (action.On is WorkflowTransitionActionTarget transition && !transitionById.ContainsKey(transition.Transition)) Add(WorkflowAdmissionCodes.DanglingReference, "action transition does not exist", action.Id);
            else if (action.On is not WorkflowStateActionTarget and not WorkflowTransitionActionTarget) Add(WorkflowAdmissionCodes.ActionBindingInvalid, "action target is invalid", action.Id);
            if (action.Condition.HasValue && !guardIds.Contains(action.Condition.Value.Value)) Add(WorkflowAdmissionCodes.DanglingReference, "action condition does not exist", action.Id);
            if (action.RequiredRoles.HasValue && action.RequiredRoles.Value!.Any(role => !RoleAdmits(role, roleVocabulary)))
                Add(WorkflowAdmissionCodes.UnknownRoleReference, "action requires an unknown role", action.Id);
            if (action.RequiredStandings.HasValue && action.RequiredStandings.Value!.Any(standing => !WorkflowGateVocabulary.IsStandingName(standing?.Name)))
                Add(WorkflowAdmissionCodes.InvalidStanding, "action requires a malformed standing", action.Id);
        }

        if (stateIds.Contains(definition.InitialState))
        {
            var reachable = Reachable(definition);
            var outgoing = definition.Transitions.Select(row => row.From).ToHashSet(StringComparer.Ordinal);
            foreach (var state in definition.States)
            {
                if (!reachable.Contains(state.Id)) Add(WorkflowAdmissionCodes.UnreachableState, "state is unreachable", state.Id);
                else if (state.Kind != WorkflowStateKind.Terminal && !outgoing.Contains(state.Id)) Add(WorkflowAdmissionCodes.DeadEndState, "state is a dead end", state.Id);
                if (state.Kind == WorkflowStateKind.Terminal && outgoing.Contains(state.Id)) Add(WorkflowAdmissionCodes.TerminalHasOutgoing, "terminal state has outgoing transition", state.Id);
            }
            foreach (var action in definition.Actions)
            {
                var derived = authority.AuthorityOf(action.CapabilityRef);
                if (action.Classification != derived) { Add(WorkflowAdmissionCodes.ClassificationMismatch, "declared classification does not match authority registry", action.Id); continue; }
                if (derived == ActionClassification.CP && FiresWithoutHumanGate(action, definition, transitionById, triggerById))
                    Add(WorkflowAdmissionCodes.CpReachableWithoutHumanTask, "CP action can fire without a human gate", action.Id);
            }
        }
        return new(violations.Count == 0, violations);
    }

    private static bool RoleAdmits(RoleReference role, RoleVocabulary? roleVocabulary) =>
        role is not null
        && WorkflowGateVocabulary.IsAdmissibleRole(role)
        && (role.Vocabulary == WorkflowGateVocabulary.SealedRoles || roleVocabulary is null || roleVocabulary.Resolve(role) is not null);

    private static void CheckEnvelope(WorkflowDefinition definition, WorkflowContractWindow? window, Action<string, string, string> add)
    {
        if (!definition.Envelope.HasValue)
        {
            if (window is not null) add(WorkflowAdmissionCodes.EnvelopeRequired, "definition carries no envelope", "/envelope");
            return;
        }
        var envelope = definition.Envelope.Value!;
        if (envelope.Identity != definition.Key || envelope.Version != definition.Version || envelope.Tenant != definition.Tenant)
            add(WorkflowAdmissionCodes.EnvelopeMismatch, "envelope does not name this definition", "/envelope");
        if (envelope.Provenance.ValueKind != JsonValueKind.Object)
            add(WorkflowAdmissionCodes.EnvelopeInvalid, "envelope provenance is not an object", "/envelope/provenance");
        if (string.IsNullOrWhiteSpace(envelope.RetentionClass))
            add(WorkflowAdmissionCodes.EnvelopeInvalid, "envelope retention class is blank", "/envelope/retentionClass");
        if (envelope.Requires is null) add(WorkflowAdmissionCodes.EnvelopeInvalid, "envelope requires is missing", "/envelope/requires");
        else
            for (var index = 0; index < envelope.Requires.Count; index++)
                if (string.IsNullOrWhiteSpace(envelope.Requires[index]?.Capability))
                    add(WorkflowAdmissionCodes.EnvelopeInvalid, "envelope requirement names no capability", $"/envelope/requires/{index}");
        if (window?.Check(envelope.Contract.HasValue ? envelope.Contract.Value : null) is { } refusal)
            add(refusal, "envelope contract is absent or outside the window", "/envelope/contract");
    }

    private static HashSet<string> Unique<T>(IEnumerable<T> rows, Func<T, string> id, string kind, Action<string, string, string> add)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows) if (!ids.Add(id(row))) add(WorkflowAdmissionCodes.DuplicateId, $"duplicate {kind} id", id(row));
        return ids;
    }
    private static Dictionary<string, T> UniqueMap<T>(IEnumerable<T> rows, Func<T, string> id, string kind, Action<string, string, string> add)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var row in rows) { if (map.ContainsKey(id(row))) add(WorkflowAdmissionCodes.DuplicateId, $"duplicate {kind} id", id(row)); map[id(row)] = row; }
        return map;
    }
    private static HashSet<string> Reachable(WorkflowDefinition definition)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { definition.InitialState }; var queue = new Queue<string>(); queue.Enqueue(definition.InitialState);
        while (queue.TryDequeue(out var state)) foreach (var transition in definition.Transitions.Where(row => row.From == state)) if (visited.Add(transition.To)) queue.Enqueue(transition.To);
        return visited;
    }
    private static bool FiresWithoutHumanGate(WorkflowActionBinding action, WorkflowDefinition definition, Dictionary<string, WorkflowTransition> transitions, Dictionary<string, WorkflowTriggerBinding> triggers)
    {
        bool Human(string trigger) => triggers.TryGetValue(trigger, out var binding) && binding.Kind == WorkflowTriggerKind.HumanAction;
        if (action.On is WorkflowTransitionActionTarget transition) return transitions.TryGetValue(transition.Transition, out var binding) && !Human(binding.On);
        if (action.On is not WorkflowStateActionTarget state) return false;
        if (state.State == definition.InitialState) return true;
        var incoming = definition.Transitions.Where(row => row.To == state.State).ToArray();
        return incoming.Length == 0 || incoming.Any(row => !Human(row.On));
    }
}
