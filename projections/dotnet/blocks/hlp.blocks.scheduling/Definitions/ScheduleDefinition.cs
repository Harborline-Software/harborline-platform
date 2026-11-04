using System.Text.Json;
using System.Text.Json.Serialization;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Foundation.Definitions;

namespace Harborline.Blocks.Scheduling.Definitions;

/// <summary>
/// The portable envelope (DES-0022 scheduling-ck-19, ADR 0006). <see cref="Contract"/> is required with no default (T-572).
/// </summary>
/// <param name="Identity">The stable definition identifier.</param>
/// <param name="Version">
/// The semantic version. The store's document version is its one source: a stored envelope omits it, because restore
/// copies a body under a new version, and the catalogue fills it in on read.
/// </param>
/// <param name="Tenant">The owning tenant.</param>
/// <param name="CascadeLayer">The contributing cascade layer.</param>
/// <param name="Provenance">The producer-supplied provenance document, a JSON object.</param>
/// <param name="RetentionClass">The governed retention class.</param>
/// <param name="LegalHold">Whether legal hold applies.</param>
/// <param name="Requires">The required platform capabilities.</param>
/// <param name="Contract">The declared application-contract version.</param>
public sealed record ScheduleDefinitionEnvelope(
    string Identity,
    string? Version,
    string Tenant,
    [property: JsonRequired] LayoutCascadeLayer CascadeLayer,
    JsonElement? Provenance,
    string RetentionClass,
    [property: JsonRequired] bool LegalHold,
    IReadOnlyList<LayoutDefinitionRequirement> Requires,
    DefinitionContractVersion? Contract);

/// <summary>What makes work due (scheduling-ck-4): four generators, of which a schedule picks one and only one.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(FixedCalendarBasis), "fixed_calendar")]
[JsonDerivedType(typeof(CompletionOffsetBasis), "completion_offset")]
[JsonDerivedType(typeof(ThresholdBasis), "threshold")]
[JsonDerivedType(typeof(EventAnchoredChainBasis), "event_anchored_chain")]
public abstract record ScheduleBasis
{
    internal ScheduleBasis()
    {
    }
}

/// <summary>A calendar recurrence. The RRULE text is kept as authored: which subset is the contract is DES-0022 open ruling 1.</summary>
/// <param name="Rrule">The authored RRULE text.</param>
public sealed record FixedCalendarBasis(string Rrule) : ScheduleBasis;

/// <summary>Due a stated time after the last completion.</summary>
/// <param name="Offset">A non-negative ISO 8601 duration after the last completion.</param>
public sealed record CompletionOffsetBasis(string Offset) : ScheduleBasis;

/// <summary>The direction a reading crosses its threshold.</summary>
public enum ThresholdCrossing
{
    /// <summary>The reading rises past the value.</summary>
    Above,
    /// <summary>The reading falls past the value.</summary>
    Below,
}

/// <summary>Due when a reading crosses a value.</summary>
/// <param name="Reading">The reading the threshold watches.</param>
/// <param name="Crossing">The direction that makes work due.</param>
/// <param name="Value">The threshold value.</param>
public sealed record ThresholdBasis(string Reading, [property: JsonRequired] ThresholdCrossing Crossing, [property: JsonRequired] decimal Value) : ScheduleBasis;

/// <summary>
/// A generator distinct from recurrence: one external anchor, so moving it re-anchors the whole chain (L449). A node
/// cannot name an anchor of its own.
/// </summary>
/// <param name="Anchor">The chain's one external anchor.</param>
/// <param name="Nodes">The nodes, each anchored on the chain or an earlier node.</param>
public sealed record EventAnchoredChainBasis(string Anchor, IReadOnlyList<ScheduleChainNode> Nodes) : ScheduleBasis;

/// <summary>One step of an event-anchored chain, carrying its own tolerance rather than a date (scheduling-auth-6).</summary>
/// <param name="NodeId">The chain-local node id.</param>
/// <param name="AfterNodeId">The earlier node this one follows, or null to follow the chain's anchor.</param>
/// <param name="Offset">A non-negative ISO 8601 duration after what it follows.</param>
/// <param name="Tolerance">This node's own on-time band.</param>
public sealed record ScheduleChainNode(string NodeId, string? AfterNodeId, string Offset, ScheduleTolerance Tolerance);

/// <summary>
/// A validity window, never a single due point (scheduling-ck-5, L483). With no basis each bound is an ISO 8601 instant
/// with an offset, the one due date of L1445. With a basis each bound is an ISO 8601 duration from the generated due point.
/// </summary>
/// <param name="Earliest">The opening bound.</param>
/// <param name="Latest">The closing bound, after <paramref name="Earliest"/>.</param>
public sealed record ScheduleWindow(string Earliest, string Latest);

/// <summary>What still counts as on time (scheduling-ck-6), a field of its own and never grace.</summary>
/// <param name="Early">A non-negative ISO 8601 duration before the due point.</param>
/// <param name="Late">A non-negative ISO 8601 duration after the due point.</param>
public sealed record ScheduleTolerance(string Early, string Late);

/// <summary>The two missed-behaviour kinds (scheduling-ck-8, L458).</summary>
public enum ScheduleMissedBehaviour
{
    /// <summary>Record the miss and re-plan.</summary>
    RecordAndReplan,
    /// <summary>The window closed; the occurrence is not re-planned.</summary>
    WindowClosed,
}

/// <summary>Who hears about a miss, and when (scheduling-ck-9).</summary>
/// <param name="After">A non-negative ISO 8601 duration after the miss.</param>
/// <param name="To">The party escalated to.</param>
public sealed record ScheduleEscalation(string After, string To);

/// <summary>
/// The planning problem (scheduling-ck-10): the seven Harborline names of ADR 0048, a Harborline vocabulary no standards
/// body owns (R-0094), plus <see cref="None"/>, the default, which names no planner (L1149).
/// </summary>
public enum ScheduleProblemClass
{
    /// <summary>No planner: most schedules need no solver.</summary>
    [JsonStringEnumMemberName("None")] None,
    /// <summary>Workforce.</summary>
    [JsonStringEnumMemberName("Workforce")] Workforce,
    /// <summary>Timetabling.</summary>
    [JsonStringEnumMemberName("Timetabling")] Timetabling,
    /// <summary>Protocol and admission.</summary>
    [JsonStringEnumMemberName("ProtocolAdmission")] ProtocolAdmission,
    /// <summary>Project.</summary>
    [JsonStringEnumMemberName("Project")] Project,
    /// <summary>Job shop.</summary>
    [JsonStringEnumMemberName("JobShop")] JobShop,
    /// <summary>Mobility.</summary>
    [JsonStringEnumMemberName("Mobility")] Mobility,
    /// <summary>Composite and recovery.</summary>
    [JsonStringEnumMemberName("CompositeRecovery")] CompositeRecovery,
}

/// <summary>A typed constraint module the problem class compiles into (scheduling-ck-11). What it means is module admission's.</summary>
/// <param name="Module">The module's name.</param>
/// <param name="Parameters">The module's parameters, unread here.</param>
public sealed record ScheduleConstraintModule(string Module, JsonElement? Parameters = null);

/// <summary>What a schedule can start (scheduling-ck-13).</summary>
public enum ScheduleStartKind
{
    /// <summary>A form.</summary>
    Form,
    /// <summary>A workflow.</summary>
    Workflow,
    /// <summary>A work item.</summary>
    WorkItem,
}

/// <summary>The work a schedule starts. A schedule never does the work itself (scheduling-ck-13).</summary>
/// <param name="Kind">What is started.</param>
/// <param name="TargetId">The definition it names.</param>
public sealed record ScheduleStarts([property: JsonRequired] ScheduleStartKind Kind, string TargetId);

/// <summary>
/// The Scheduling definition, content kind 11 (scheduling-ck-1, DES-0022 section 2). A due date and a form are a complete
/// schedule: the basis and every planning member are optional, and <see cref="ProblemClass"/> defaults to no solver (L1445).
/// </summary>
/// <param name="Envelope">The portable envelope. Required past authoring.</param>
/// <param name="Name">The schedule's name.</param>
/// <param name="Starts">What it starts.</param>
/// <param name="OnTypeId">The type it runs on.</param>
/// <param name="Basis">The one generator, or null for a single due date.</param>
/// <param name="Window">The validity window. An event-anchored chain's nodes carry their own tolerance instead.</param>
/// <param name="Tolerance">The on-time band.</param>
/// <param name="Grace">An ISO 8601 duration of tolerance for lateness after a miss, never sharing a field with tolerance.</param>
/// <param name="MissedBehaviour">What a miss does.</param>
/// <param name="Escalation">Who hears about a miss.</param>
/// <param name="ProblemClass">The planning problem.</param>
/// <param name="Modules">The constraint modules the problem class compiles into.</param>
/// <param name="LegalityRef">A citation to a Legality Rule Set that Scheduling never interprets (scheduling-ck-12).</param>
public sealed record ScheduleDefinition(
    ScheduleDefinitionEnvelope? Envelope,
    string Name,
    ScheduleStarts Starts,
    string? OnTypeId = null,
    ScheduleBasis? Basis = null,
    ScheduleWindow? Window = null,
    ScheduleTolerance? Tolerance = null,
    string? Grace = null,
    ScheduleMissedBehaviour? MissedBehaviour = null,
    ScheduleEscalation? Escalation = null,
    ScheduleProblemClass ProblemClass = ScheduleProblemClass.None,
    IReadOnlyList<ScheduleConstraintModule>? Modules = null,
    string? LegalityRef = null);

/// <summary>The sealed trait that admits a type to planning (scheduling-ck-22, L482, L1084).</summary>
public static class SchedulableTrait
{
    /// <summary>The trait's seeded id.</summary>
    public const string Id = "platform.trait.schedulable";

    /// <summary>The slots the trait declares.</summary>
    public static IReadOnlyList<string> Slots { get; } = ["work_calendar", "qualification_set", "capacity_slots"];

    /// <summary>Whether a type's declared traits admit it to planning.</summary>
    public static bool AdmitsToPlanning(IEnumerable<string> declaredTraits)
    {
        ArgumentNullException.ThrowIfNull(declaredTraits);
        return declaredTraits.Contains(Id, StringComparer.Ordinal);
    }
}

/// <summary>
/// Provider-neutral JSON for <see cref="ScheduleDefinition"/>. An unknown member, kind or literal fails to read, and so does a
/// missing member marked <see cref="JsonRequiredAttribute"/>: a value type would otherwise read as its default, which nobody authored.
/// </summary>
public static class ScheduleDefinitionJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes a definition as UTF-8 JSON text.</summary>
    public static string Serialize(ScheduleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return JsonSerializer.Serialize(definition, Options);
    }

    /// <summary>Reads a definition, throwing <see cref="JsonException"/> or <see cref="NotSupportedException"/> for one it cannot.</summary>
    public static ScheduleDefinition Deserialize(string json)
        => JsonSerializer.Deserialize<ScheduleDefinition>(json, Options)
            ?? throw new JsonException("The schedule definition payload is null.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowOutOfOrderMetadataProperties = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }
}
