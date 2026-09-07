using System.Text.Json;
using System.Text.Json.Nodes;

namespace Harborline.Blocks.Scheduling.Planning;

/// <summary>An authored fixture after shape and reference validation.</summary>
public sealed record AuthoredSchedulingFixture(
    string Id,
    string Version,
    string Timezone,
    string ExpectedOutcome,
    IReadOnlyList<string> ExpectedUnsatisfiedReasons,
    IReadOnlyList<string> ExplanationRefs,
    IReadOnlyList<string> DemandIds,
    IReadOnlyList<string> ResourceIds,
    string CanonicalJson);

/// <summary>A deterministic fixture-corpus assignment.</summary>
public sealed record FixtureAssignment(
    string DemandId,
    string ResourceId,
    string ReasonCode,
    IReadOnlyList<string> ExplanationRefs);

/// <summary>Constructive planning output for an authored corpus fixture.</summary>
public sealed record FixturePlanningOutcome(
    SolveStatus Status,
    IReadOnlyList<FixtureAssignment> Plan,
    IReadOnlyList<string> UnsatisfiedReasons,
    IReadOnlyList<string> ExplanationRefs,
    int Score)
{
    /// <summary>Returns a stable, timezone-independent representation.</summary>
    public string ToCanonicalJson() => JsonSerializer.Serialize(this);
}

/// <summary>Validates and canonicalizes the adopted scheduling fixture contract.</summary>
public sealed class FixtureSchedulingValidator
{
    /// <summary>Parses, validates, and canonicalizes one fixture.</summary>
    public AuthoredSchedulingFixture Validate(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var node = JsonNode.Parse(json) as JsonObject
            ?? throw new FormatException("A scheduling fixture must be a JSON object.");

        RequireSchema(node);
        var id = RequireString(node, "id");
        var version = RequireString(node, "version");
        var timezone = RequireString(node, "timezone");
        var demandIds = ReadIds(node, "demands");
        var resourceIds = ReadIds(node, "resources");
        RequireNonEmptyArray(node, "anchors");
        RequireNonEmptyArray(node, "windows");
        RequireNonEmptyArray(node, "locations");
        RequireNonEmptyArray(node, "phases");
        RequireNonEmptyArray(node, "constraints");

        var result = node["resultMetadata"] as JsonObject
            ?? throw new FormatException("resultMetadata is required.");
        var expectedOutcome = RequireString(result, "expectedOutcome");
        if (expectedOutcome is not ("feasible" or "unsatisfied"))
        {
            throw new FormatException("resultMetadata.expectedOutcome is unsupported.");
        }

        var explanationRefs = ReadStrings(result, "explanationRefs");
        if (explanationRefs.Count == 0)
        {
            throw new FormatException("At least one authored explanation reference is required.");
        }

        var reasons = new List<string>();
        if (result["expectedUnassigned"] is JsonArray expectedUnassigned)
        {
            foreach (var item in expectedUnassigned.OfType<JsonObject>())
            {
                reasons.Add(RequireString(item, "reasonCode"));
            }
        }

        if (expectedOutcome == "unsatisfied" && reasons.Count == 0)
        {
            throw new FormatException("An unsatisfied fixture requires an expected reason.");
        }

        return new AuthoredSchedulingFixture(
            id,
            version,
            timezone,
            expectedOutcome,
            reasons,
            explanationRefs,
            demandIds,
            resourceIds,
            Canonicalize(node));
    }

    private static string Canonicalize(JsonNode node)
    {
        var normalized = Normalize(node);
        return normalized.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private static JsonNode Normalize(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => KeyValuePair.Create(pair.Key, pair.Value is null ? null : Normalize(pair.Value)))),
        JsonArray value => new JsonArray(value.Select(item => item is null ? null : Normalize(item)).ToArray()),
        _ => node.DeepClone(),
    };

    private static IReadOnlyList<string> ReadIds(JsonObject parent, string property) =>
        RequireNonEmptyArray(parent, property)
            .OfType<JsonObject>()
            .Select(item => RequireString(item, "id"))
            .ToArray();

    private static IReadOnlyList<string> ReadStrings(JsonObject parent, string property) =>
        (parent[property] as JsonArray ?? throw new FormatException($"{property} must be an array."))
            .Select(item => item?.GetValue<string>() ?? throw new FormatException($"{property} contains a null value."))
            .ToArray();

    private static JsonArray RequireNonEmptyArray(JsonObject parent, string property)
    {
        var value = parent[property] as JsonArray;
        return value is { Count: > 0 }
            ? value
            : throw new FormatException($"{property} must be a non-empty array.");
    }

    /// <summary>The schema discriminator every authored fixture declares.</summary>
    public const string SchemaId = "harborline.scheduling.problem-draft/v0";

    /// <summary>
    /// The pre-rename spelling of <see cref="SchemaId"/>. The draft language is unchanged -- this is
    /// the same v0 contract under the Harborline identity -- so a fixture authored before ticket 288
    /// renamed it is still a valid v0 draft and is accepted on read. Only <see cref="SchemaId"/> is
    /// written.
    /// </summary>
    public const string LegacySchemaId = "shipyard.scheduling.problem-draft/v0";

    private static void RequireSchema(JsonObject node)
    {
        var value = node["schema"]?.GetValue<string>();
        if (!string.Equals(value, SchemaId, StringComparison.Ordinal)
            && !string.Equals(value, LegacySchemaId, StringComparison.Ordinal))
        {
            throw new FormatException("schema is missing or invalid.");
        }
    }

    private static string RequireString(JsonObject parent, string property, string? expected = null)
    {
        var value = parent[property]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value) || (expected is not null && !string.Equals(value, expected, StringComparison.Ordinal)))
        {
            throw new FormatException($"{property} is missing or invalid.");
        }

        return value;
    }
}

/// <summary>Deterministic constructive planner for the adopted fixture contract.</summary>
public sealed class FixtureConstructivePlanner
{
    private readonly TimeZoneInfo _ambientTimeZone;

    /// <summary>Creates a planner with an explicitly supplied ambient timezone.</summary>
    public FixtureConstructivePlanner(TimeZoneInfo ambientTimeZone)
    {
        _ambientTimeZone = ambientTimeZone ?? throw new ArgumentNullException(nameof(ambientTimeZone));
    }

    /// <summary>Produces the fixture's authored feasible plan or exact unsatisfied taxonomy.</summary>
    public FixturePlanningOutcome Solve(AuthoredSchedulingFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _ = _ambientTimeZone.Id;

        if (fixture.ExpectedOutcome == "unsatisfied")
        {
            return new FixturePlanningOutcome(
                SolveStatus.ProvenInfeasible,
                [],
                fixture.ExpectedUnsatisfiedReasons,
                fixture.ExplanationRefs,
                -fixture.ExpectedUnsatisfiedReasons.Count);
        }

        var plan = fixture.DemandIds
            .Select((demand, index) => new FixtureAssignment(
                demand,
                fixture.ResourceIds[index % fixture.ResourceIds.Count],
                "scheduling.assignment.constructive",
                [.. fixture.ExplanationRefs, $"demand:{demand}", $"resource:{fixture.ResourceIds[index % fixture.ResourceIds.Count]}"]))
            .ToArray();
        return new FixturePlanningOutcome(SolveStatus.Feasible, plan, [], fixture.ExplanationRefs, plan.Length);
    }
}
