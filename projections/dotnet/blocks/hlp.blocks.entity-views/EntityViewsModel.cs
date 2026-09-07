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

public sealed record ConditionHistory(string Entity, IReadOnlyList<ConditionAssessment> History);

public sealed record SubmissionSummary(string InstanceId, string FormId, string SubmittedAt, string? AssessorRef);

public sealed record SubmissionList(string Entity, IReadOnlyList<SubmissionSummary> Submissions);

public sealed record CreateEntityBody(string Type, string DisplayName, string? ScanKey);

[JsonConverter(typeof(JsonStringEnumConverter<EdgeKind>))]
public enum EdgeKind
{
    [JsonStringEnumMemberName("contains")]
    Contains,
    [JsonStringEnumMemberName("located-at")]
    LocatedAt,
    [JsonStringEnumMemberName("part-of-system")]
    PartOfSystem,
}

public sealed record AddEdgeBody(EdgeKind Kind, string From, string To);

public sealed record EdgeSummary(string Id, EdgeKind Kind, string From, string To, string EffectiveFrom);

/// <summary>How the tree/list groups entities (the pinned facet groupings).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FacetGrouping>))]
public enum FacetGrouping
{
    [JsonStringEnumMemberName("containment")]
    Containment,
    [JsonStringEnumMemberName("location")]
    Location,
    [JsonStringEnumMemberName("discipline")]
    Discipline,
    [JsonStringEnumMemberName("type")]
    Type,
}

public static class EntityViewsModel
{
    public const int DefaultConditionScaleMax = 5;
}
