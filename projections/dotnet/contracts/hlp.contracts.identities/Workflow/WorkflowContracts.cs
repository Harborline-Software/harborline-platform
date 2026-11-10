#nullable enable
#pragma warning disable CS1591

using System.Text.Json;
using System.Text.Json.Serialization;
using Harborline.Contracts.Authorization;
using Harborline.Contracts.Forms;
using Harborline.Contracts.Rules;
using Harborline.Contracts.Text;
using Harborline.Contracts.Serialization;

namespace Harborline.Contracts.Workflow;

[JsonConverter(typeof(ClosedEnumJsonConverter<WorkflowDefinitionStatus>))]
public enum WorkflowDefinitionStatus
{
    [JsonStringEnumMemberName("Draft")] Draft,
    [JsonStringEnumMemberName("Published")] Published,
    [JsonStringEnumMemberName("Deprecated")] Deprecated,
    [JsonStringEnumMemberName("Withdrawn")] Withdrawn,
}

[JsonConverter(typeof(ClosedEnumJsonConverter<WorkflowCascadeLayer>))]
public enum WorkflowCascadeLayer
{
    [JsonStringEnumMemberName("KernelCore")] KernelCore,
    [JsonStringEnumMemberName("Subsystem")] Subsystem,
    [JsonStringEnumMemberName("PlatformPackage")] PlatformPackage,
    [JsonStringEnumMemberName("DomainPackage")] DomainPackage,
    [JsonStringEnumMemberName("TenantConfiguration")] TenantConfiguration,
}

[JsonConverter(typeof(ClosedEnumJsonConverter<WorkflowMutability>))]
public enum WorkflowMutability
{
    [JsonStringEnumMemberName("Locked")] Locked,
    [JsonStringEnumMemberName("Editable")] Editable,
}

[JsonConverter(typeof(ClosedEnumJsonConverter<WorkflowStateKind>))]
public enum WorkflowStateKind
{
    [JsonStringEnumMemberName("Normal")] Normal,
    [JsonStringEnumMemberName("Terminal")] Terminal,
}

[JsonConverter(typeof(ClosedEnumJsonConverter<WorkflowLockScope>))]
public enum WorkflowLockScope
{
    [JsonStringEnumMemberName("every field")] EveryField,
    [JsonStringEnumMemberName("dates only")] DatesOnly,
    [JsonStringEnumMemberName("attachments only")] AttachmentsOnly,
    [JsonStringEnumMemberName("nothing")] Nothing,
}

[JsonConverter(typeof(ClosedEnumJsonConverter<WorkflowNotificationTier>))]
public enum WorkflowNotificationTier
{
    [JsonStringEnumMemberName("decision")] Decision,
    [JsonStringEnumMemberName("observation")] Observation,
    [JsonStringEnumMemberName("information")] Information,
}

[JsonConverter(typeof(ClosedEnumJsonConverter<WorkflowViewHint>))]
public enum WorkflowViewHint
{
    [JsonStringEnumMemberName("flow")] Flow,
    [JsonStringEnumMemberName("gantt")] Gantt,
    [JsonStringEnumMemberName("kanban")] Kanban,
}

[JsonConverter(typeof(ClosedEnumJsonConverter<WorkflowTriggerKind>))]
public enum WorkflowTriggerKind
{
    [JsonStringEnumMemberName("Event")] Event,
    [JsonStringEnumMemberName("Schedule")] Schedule,
    [JsonStringEnumMemberName("HumanAction")] HumanAction,
    [JsonStringEnumMemberName("DependencyComplete")] DependencyComplete,
}

[JsonConverter(typeof(ClosedEnumJsonConverter<WorkflowActionKind>))]
public enum WorkflowActionKind
{
    [JsonStringEnumMemberName("Notify")] Notify,
    [JsonStringEnumMemberName("CreateRecord")] CreateRecord,
    [JsonStringEnumMemberName("UpdateField")] UpdateField,
    [JsonStringEnumMemberName("InvokeService")] InvokeService,
    [JsonStringEnumMemberName("StartSubProcess")] StartSubProcess,
    [JsonStringEnumMemberName("EmitEvent")] EmitEvent,
}

[JsonConverter(typeof(ClosedEnumJsonConverter<ActionClassification>))]
public enum ActionClassification
{
    [JsonStringEnumMemberName("CP")] CP,
    [JsonStringEnumMemberName("AP")] AP,
}

[JsonConverter(typeof(GuardRefJsonConverter))]
public readonly record struct GuardRef(string Value)
{
    public static implicit operator GuardRef(string value) => new(value);
    public static implicit operator string(GuardRef value) => value.Value;
    public override string ToString() => Value;
}

[JsonConverter(typeof(WorkflowEffectParamsJsonConverter))]
public readonly record struct WorkflowEffectParams(IReadOnlyDictionary<string, JsonElement> Values);

public sealed record WorkflowFieldRequirement
{
    [JsonPropertyName("field"), JsonRequired] public required string Field { get; init; }
    [JsonPropertyName("condition"), JsonRequired] public required string Condition { get; init; }
}

public sealed record WorkflowEscalation
{
    [JsonPropertyName("schedule"), JsonRequired] public required string Schedule { get; init; }
}

public sealed record WorkflowNotification
{
    [JsonPropertyName("eventKey"), JsonRequired] public required string EventKey { get; init; }
    [JsonPropertyName("batchingWindow"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> BatchingWindow { get; init; }
    [JsonPropertyName("tier"), JsonRequired] public required WorkflowNotificationTier Tier { get; init; }
}

public sealed record WorkflowState
{
    [JsonPropertyName("id"), JsonRequired] public required string Id { get; init; }
    [JsonPropertyName("label"), JsonRequired] public required InternationalizedText Label { get; init; }
    [JsonPropertyName("kind"), JsonRequired] public required WorkflowStateKind Kind { get; init; }
    [JsonPropertyName("viewHint"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<WorkflowViewHint> ViewHint { get; init; }
    [JsonPropertyName("fieldRequirements"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<IReadOnlyList<WorkflowFieldRequirement>> FieldRequirements { get; init; }
    [JsonPropertyName("locks"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<WorkflowLockScope> Locks { get; init; }
    [JsonPropertyName("escalation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<WorkflowEscalation> Escalation { get; init; }
}

public sealed record WorkflowTransition
{
    [JsonPropertyName("id"), JsonRequired] public required string Id { get; init; }
    [JsonPropertyName("from"), JsonRequired] public required string From { get; init; }
    [JsonPropertyName("on"), JsonRequired] public required string On { get; init; }
    [JsonPropertyName("to"), JsonRequired] public required string To { get; init; }
    [JsonPropertyName("guard"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<GuardRef> Guard { get; init; }
    [JsonPropertyName("requiredRoles"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<RoleReference>> RequiredRoles { get; init; }
    [JsonPropertyName("requiredStandings"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<RecordStandingReference>> RequiredStandings { get; init; }
}

public sealed record WorkflowTriggerBinding
{
    [JsonPropertyName("id"), JsonRequired] public required string Id { get; init; }
    [JsonPropertyName("kind"), JsonRequired] public required WorkflowTriggerKind Kind { get; init; }
    [JsonPropertyName("eventType"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> EventType { get; init; }
    [JsonPropertyName("rrule"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> Rrule { get; init; }
    [JsonPropertyName("task"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> Task { get; init; }
    [JsonPropertyName("dependency"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<string> Dependency { get; init; }
}

[JsonConverter(typeof(WorkflowActionTargetJsonConverter))]
public abstract record WorkflowActionTarget;
public sealed record WorkflowStateActionTarget(string State) : WorkflowActionTarget;
public sealed record WorkflowTransitionActionTarget(string Transition) : WorkflowActionTarget;

public sealed record WorkflowActionBinding
{
    [JsonPropertyName("id"), JsonRequired] public required string Id { get; init; }
    [JsonPropertyName("on"), JsonRequired] public required WorkflowActionTarget On { get; init; }
    [JsonPropertyName("kind"), JsonRequired] public required WorkflowActionKind Kind { get; init; }
    [JsonPropertyName("capabilityRef"), JsonRequired] public required string CapabilityRef { get; init; }
    [JsonPropertyName("classification"), JsonRequired] public required ActionClassification Classification { get; init; }
    [JsonPropertyName("condition"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<GuardRef> Condition { get; init; }
    [JsonPropertyName("requiredRoles"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<IReadOnlyList<RoleReference>> RequiredRoles { get; init; }
    [JsonPropertyName("requiredStandings"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<IReadOnlyList<RecordStandingReference>> RequiredStandings { get; init; }
    [JsonPropertyName("params"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<WorkflowEffectParams> Params { get; init; }
    [JsonPropertyName("notification"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<WorkflowNotification> Notification { get; init; }
}

public sealed record WorkflowDefinitionLineage
{
    [JsonPropertyName("parentDefinitionKey"), JsonRequired] public required string ParentDefinitionKey { get; init; }
    [JsonPropertyName("parentVersion"), JsonRequired] public required string ParentVersion { get; init; }
}

/// <summary>
/// What a Workflow runs on (DES-0019 workflows-ck-12, ADR 0099): exactly one of a record, a set or a case, written
/// <c>{"record": {...}}</c> or <c>{"set": {...}}</c>. ADR 0126 decides what each kind names.
/// </summary>
[JsonConverter(typeof(WorkflowSubjectJsonConverter))]
public abstract record WorkflowSubject;
/// <summary>One record of the named Record Type (ADR 0126 decision 1).</summary>
public sealed record WorkflowRecordSubject(string RecordType) : WorkflowSubject;
/// <summary>A set of records of the named Record Type (ADR 0126 decision 2).</summary>
public sealed record WorkflowSetSubject(string RecordType) : WorkflowSubject;

/// <summary>A definition's declared application-contract version on the wire.</summary>
public sealed record WorkflowContractVersion
{
    [JsonPropertyName("major"), JsonRequired] public required int Major { get; init; }
    [JsonPropertyName("minor"), JsonRequired] public required int Minor { get; init; }
}

/// <summary>A platform capability the envelope requires.</summary>
public sealed record WorkflowEnvelopeRequirement
{
    [JsonPropertyName("capability"), JsonRequired] public required string Capability { get; init; }
}

/// <summary>
/// The workflow control envelope on the wire (DES-0019 workflows-ck-2, ADR 0006). Named without the
/// *DefinitionEnvelope suffix on purpose: that fence demands Foundation.Definitions' contract type, which
/// the dependency-free Contracts artifact cannot name. The engine's typed WorkflowDefinitionEnvelope carries it.
/// A missing contract survives the read so that admission, not the reader, gives the one stable refusal.
/// </summary>
public sealed record WorkflowEnvelope
{
    [JsonPropertyName("identity"), JsonRequired] public required string Identity { get; init; }
    [JsonPropertyName("version"), JsonRequired] public required string Version { get; init; }
    [JsonPropertyName("tenant"), JsonRequired] public required string Tenant { get; init; }
    [JsonPropertyName("cascadeLayer"), JsonRequired] public required WorkflowCascadeLayer CascadeLayer { get; init; }
    [JsonPropertyName("provenance"), JsonRequired] public required JsonElement Provenance { get; init; }
    [JsonPropertyName("retentionClass"), JsonRequired] public required string RetentionClass { get; init; }
    [JsonPropertyName("legalHold"), JsonRequired] public required bool LegalHold { get; init; }
    [JsonPropertyName("requires"), JsonRequired] public required IReadOnlyList<WorkflowEnvelopeRequirement> Requires { get; init; }
    [JsonPropertyName("contract"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<WorkflowContractVersion> Contract { get; init; }
}

public sealed record WorkflowDefinition
{
    [JsonPropertyName("envelope"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<WorkflowEnvelope> Envelope { get; init; }
    [JsonPropertyName("key"), JsonRequired] public required string Key { get; init; }
    [JsonPropertyName("version"), JsonRequired] public required string Version { get; init; }
    [JsonPropertyName("status"), JsonRequired] public required WorkflowDefinitionStatus Status { get; init; }
    [JsonPropertyName("tenant"), JsonRequired] public required string Tenant { get; init; }
    [JsonPropertyName("owner"), JsonRequired] public required IdentityRef Owner { get; init; }
    [JsonPropertyName("title"), JsonRequired] public required InternationalizedText Title { get; init; }
    [JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<InternationalizedText> Description { get; init; }
    [JsonPropertyName("subject"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<WorkflowSubject> Subject { get; init; }
    [JsonPropertyName("mutability"), JsonRequired] public required WorkflowMutability Mutability { get; init; }
    [JsonPropertyName("initialState"), JsonRequired] public required string InitialState { get; init; }
    [JsonPropertyName("states"), JsonRequired] public required IReadOnlyList<WorkflowState> States { get; init; }
    [JsonPropertyName("transitions"), JsonRequired] public required IReadOnlyList<WorkflowTransition> Transitions { get; init; }
    [JsonPropertyName("triggers"), JsonRequired] public required IReadOnlyList<WorkflowTriggerBinding> Triggers { get; init; }
    [JsonPropertyName("actions"), JsonRequired] public required IReadOnlyList<WorkflowActionBinding> Actions { get; init; }
    [JsonPropertyName("guards"), JsonRequired] public required IReadOnlyList<RuleDefinition> Guards { get; init; }
    [JsonPropertyName("lineage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public Optional<WorkflowDefinitionLineage> Lineage { get; init; }
    [JsonPropertyName("createdAt"), JsonRequired] public required string CreatedAt { get; init; }
    [JsonPropertyName("updatedAt"), JsonRequired] public required string UpdatedAt { get; init; }
}
