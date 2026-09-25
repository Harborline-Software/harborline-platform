using System.Text.Json.Nodes;

using Harborline.Blocks.Calendar.Models;

using static Harborline.Blocks.Calendar.Booking.BookingDefinitionAdmission;

namespace Harborline.Blocks.Calendar.Booking;

/// <summary>
/// A Resource: what supplies capacity (DES-0025 booking-ck-2 to ck-7). Read from an admitted body;
/// the stored JSON, not this view, is the definition.
/// </summary>
public sealed record BookingResourceDefinition(
    string Id,
    string Version,
    string Name,
    string FromTypeId,
    CapacityKind CapacityKind,
    int? PoolSize,
    int SetupMinutes,
    int CleanupMinutes,
    IReadOnlyList<string> MaintenanceWindows,
    string AvailabilityFrom)
{
    /// <summary>Reads an admitted Resource body.</summary>
    /// <exception cref="FormatException">The body was not admitted as a Resource.</exception>
    public static BookingResourceDefinition Parse(string bodyJson)
    {
        var body = Admitted(bodyJson, "resource");
        var pool = Text(body["capacity_kind"]) == "pool";
        return new(
            Required(body["envelope"]?["identity"]), Required(body["envelope"]?["version"]), Required(body["name"]),
            Required(body["from_type_id"]),
            pool ? CapacityKind.Pool : CapacityKind.Exclusive,
            pool ? Whole(body["pool_size"]) : null,
            Whole(body["setup_minutes"]) ?? 0,
            Whole(body["cleanup_minutes"]) ?? 0,
            Strings(body["maintenance_windows"]),
            Required(body["availability_from"]));
    }

    internal static JsonObject Admitted(string bodyJson, string kind)
        => BookingDefinitionAdmission.Parse(bodyJson) is { } body && Text(body["kind"]) == kind ? body
            : throw new FormatException(BookingDefinitionCodes.KindMismatch);

    internal static string Required(JsonNode? node) => Text(node) ?? throw new FormatException(BookingDefinitionCodes.BodyInvalid);

    internal static IReadOnlyList<string> Strings(JsonNode? node)
        => node is JsonArray items ? items.Select(Required).ToArray() : [];
}
