using System.Text.Json.Serialization;

namespace Harborline.Blocks.EntityViews;

/// <summary>A registry entity summary row.</summary>
/// <remarks>Timestamps remain ISO-8601 strings because these records mirror an existing wire contract byte-for-byte; parsing would change accepted inputs and serialization.</remarks>
public sealed record EntitySummary(string Id, string Type, string DisplayName, string? ScanKey, string CreatedAt, string? RetiredAt);

/// <summary>An entity and its containment context.</summary>
public sealed record EntityDetail(string Id, string Type, string DisplayName, string? ScanKey, string CreatedAt, string? RetiredAt, string? ContainerId, IReadOnlyList<string> Path, FormRef? PropertyForm);

/// <summary>A pinned form definition and version.</summary>
public sealed record FormRef(string Definition, string Version);

/// <summary>The direct contents of a container at an instant.</summary>
public sealed record TreeView(string Container, string AsOf, IReadOnlyList<string> Path, IReadOnlyList<EntitySummary> Children);

/// <summary>A condition assessment with submission-field provenance — mirrors the pinned
/// <c>ConditionWire</c> field-for-field (int grade/scale, trailing free-text observations).</summary>
public sealed record ConditionAssessment(string Id, string Entity, int Grade, int ScaleMax, string? Label, double Normalized, string ObservedAt, string? AssessorRef, string? SourceForm, string? SourceField, string? Observations);

/// <summary>The condition assessments recorded for one entity.</summary>
public sealed record ConditionHistory(string Entity, IReadOnlyList<ConditionAssessment> History);

/// <summary>One submitted form instance for an entity: its instance id, form id, submission time and optional assessor.</summary>
public sealed record SubmissionSummary(string InstanceId, string FormId, string SubmittedAt, string? AssessorRef);

/// <summary>The submissions linked to one entity.</summary>
public sealed record SubmissionList(string Entity, IReadOnlyList<SubmissionSummary> Submissions);

/// <summary>Request body for creating an entity of a given type, with a display name and an optional scan key.</summary>
public sealed record CreateEntityBody(string Type, string DisplayName, string? ScanKey);

/// <summary>How an edge relates two entities; serialized as a kebab-case string.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EdgeKind>))]
public enum EdgeKind
{
    /// <summary>The From entity contains the To entity.</summary>
    [JsonStringEnumMemberName("contains")]
    Contains,
    /// <summary>The From entity is located at the To entity.</summary>
    [JsonStringEnumMemberName("located-at")]
    LocatedAt,
    /// <summary>The From entity is a part of the To system.</summary>
    [JsonStringEnumMemberName("part-of-system")]
    PartOfSystem,
}

/// <summary>Request body for adding an edge of a given kind from one entity to another.</summary>
public sealed record AddEdgeBody(EdgeKind Kind, string From, string To);

/// <summary>A recorded edge: its id, kind, endpoints and the time it took effect.</summary>
public sealed record EdgeSummary(string Id, EdgeKind Kind, string From, string To, string EffectiveFrom);

/// <summary>How the tree/list groups entities (the pinned facet groupings).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FacetGrouping>))]
public enum FacetGrouping
{
    /// <summary>Group entities by their containment hierarchy.</summary>
    [JsonStringEnumMemberName("containment")]
    Containment,
    /// <summary>Group entities by physical location.</summary>
    [JsonStringEnumMemberName("location")]
    Location,
    /// <summary>Group entities by discipline.</summary>
    [JsonStringEnumMemberName("discipline")]
    Discipline,
    /// <summary>Group entities by entity type.</summary>
    [JsonStringEnumMemberName("type")]
    Type,
}

/// <summary>Constants shared by the entity-views model.</summary>
public static class EntityViewsModel
{
    /// <summary>Condition-scale maximum used when a submission supplies none.</summary>
    public const int DefaultConditionScaleMax = 5;
}
