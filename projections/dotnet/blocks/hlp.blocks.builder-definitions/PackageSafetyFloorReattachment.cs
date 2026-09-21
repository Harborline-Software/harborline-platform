using System.Text.Json.Nodes;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The result of applying seeded safety-floor constraints during package reattachment.</summary>
/// <param name="Succeeded">Whether the reattached content is admissible.</param>
/// <param name="Content">The independent, clamped content snapshot on success.</param>
/// <param name="RefusalCode">The stable refusal code on failure.</param>
/// <param name="Member">The malformed safety-floor member on failure.</param>
public sealed record PackageSafetyFloorReattachmentResult(
    bool Succeeded,
    JsonNode? Content,
    string? RefusalCode,
    string? Member);

/// <summary>Enforces raise-only safety floors when tenant content is reattached to a released seed.</summary>
public static class PackageSafetyFloorReattachment
{
    /// <summary>The reserved member containing named integer safety floors.</summary>
    public const string FloorsMember = "safetyFloors";

    /// <summary>
    /// Returns an independent candidate with every seeded floor restored or raised to the seed value.
    /// A present non-integer floor refuses the complete reattachment and names the malformed member.
    /// </summary>
    public static PackageSafetyFloorReattachmentResult Apply(JsonNode seed, JsonNode candidate)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(candidate);

        if (seed is not JsonObject seedObject
            || seedObject[FloorsMember] is not JsonObject seedFloors
            || seedFloors.Count == 0)
        {
            return Accepted(candidate.DeepClone());
        }

        if (candidate.DeepClone() is not JsonObject reattached)
        {
            return Refused(FloorsMember);
        }

        if (reattached[FloorsMember] is not JsonObject candidateFloors)
        {
            candidateFloors = (JsonObject)seedFloors.DeepClone();
            reattached[FloorsMember] = candidateFloors;
        }

        foreach (var (member, seedValue) in seedFloors)
        {
            if (seedValue is not JsonValue seedJson || !seedJson.TryGetValue<int>(out var seedFloor))
            {
                return Refused(member);
            }

            if (!candidateFloors.TryGetPropertyValue(member, out var candidateValue) || candidateValue is null)
            {
                candidateFloors[member] = seedFloor;
                continue;
            }

            if (candidateValue is not JsonValue candidateJson
                || !candidateJson.TryGetValue<int>(out var candidateFloor))
            {
                return Refused(member);
            }

            if (candidateFloor < seedFloor)
            {
                candidateFloors[member] = seedFloor;
            }
        }

        return Accepted(reattached);
    }

    private static PackageSafetyFloorReattachmentResult Accepted(JsonNode content)
        => new(true, content, null, null);

    private static PackageSafetyFloorReattachmentResult Refused(string member)
        => new(false, null, "platform-package-safety-floor-malformed", member);
}
