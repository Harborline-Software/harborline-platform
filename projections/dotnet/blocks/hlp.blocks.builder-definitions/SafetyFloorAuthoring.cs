using System.Text.Json.Nodes;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>A sealed floor member as a read reports it: its seeded floor, and whether and why it may be written.</summary>
public sealed record SafetyFloorMemberState(string Member, int SeedFloor, bool Writable, string Reason);

/// <summary>An authoring verdict: every refusal as a stable code and RFC 6901 pointer, and content only when there are none.</summary>
public sealed record SafetyFloorAuthoringResult(IReadOnlyList<DefinitionRefusal> Refusals, JsonNode? Content);

/// <summary>
/// Authoring a sealed safety floor (DES-0018 <c>rules-auth-8</c>): a downstream author may raise a seeded floor and
/// never lower it. The <c>author-floor</c> grant is Access's verdict, supplied by the host; Rules only narrows.
/// Reattachment clamps and reports (<see cref="PackageSafetyFloorReattachment"/>); authoring refuses instead,
/// reporting every lowered or malformed member at once and changing nothing.
/// </summary>
public static class SafetyFloorAuthoring
{
    /// <summary>The member is writable, upward only.</summary>
    public const string RaiseOnly = "rules.floor.raise_only";

    /// <summary>The actor lacks the Access <c>author-floor</c> grant.</summary>
    public const string AuthorFloorDenied = "rules.floor.author_floor_denied";

    /// <summary>The candidate lowers, removes or omits a seeded floor.</summary>
    public const string Lowered = "rules.floor.lowered";

    /// <summary>A seeded floor member is present as a non-integer.</summary>
    public const string Malformed = "platform-package-safety-floor-malformed";

    /// <summary>Reports each sealed floor member's writability and reason before any write is attempted.</summary>
    public static IReadOnlyList<SafetyFloorMemberState> Describe(JsonNode seed, bool authorFloorAllowed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        return [.. SeedFloors(seed).Select(floor => new SafetyFloorMemberState(floor.Member, floor.Floor,
            authorFloorAllowed, authorFloorAllowed ? RaiseOnly : AuthorFloorDenied))];
    }

    /// <summary>Admits <paramref name="candidate"/> only when every seeded floor is present and at least its seed value.</summary>
    public static SafetyFloorAuthoringResult Author(JsonNode seed, JsonNode candidate, bool authorFloorAllowed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!authorFloorAllowed) return new([new(AuthorFloorDenied, "/" + PackageSafetyFloorReattachment.FloorsMember)], null);

        var floors = (candidate as JsonObject)?[PackageSafetyFloorReattachment.FloorsMember] as JsonObject;
        var refusals = new List<DefinitionRefusal>();
        foreach (var (member, seedFloor) in SeedFloors(seed))
        {
            string pointer = "/" + PackageSafetyFloorReattachment.FloorsMember + "/" + member.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
            if (floors?[member] is not { } value) refusals.Add(new(Lowered, pointer));
            else if (value is not JsonValue json || !json.TryGetValue<int>(out var floor)) refusals.Add(new(Malformed, pointer));
            else if (floor < seedFloor) refusals.Add(new(Lowered, pointer));
        }
        return refusals.Count == 0 ? new([], candidate.DeepClone()) : new(refusals.AsReadOnly(), null);
    }

    private static IEnumerable<(string Member, int Floor)> SeedFloors(JsonNode seed)
    {
        if ((seed as JsonObject)?[PackageSafetyFloorReattachment.FloorsMember] is not JsonObject floors) yield break;
        foreach (var (member, value) in floors)
            yield return value is JsonValue json && json.TryGetValue<int>(out var floor)
                ? (member, floor)
                : throw new ArgumentException($"Seeded floor '{member}' is not an integer.", nameof(seed));
    }
}
