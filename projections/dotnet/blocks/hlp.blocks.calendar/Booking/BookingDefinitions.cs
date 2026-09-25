using System.Text.Json.Nodes;

using Harborline.Blocks.Calendar.Models;
using Harborline.Contracts.Authorization;

using static Harborline.Blocks.Calendar.Booking.BookingDefinitionAdmission;

namespace Harborline.Blocks.Calendar.Booking;

/// <summary>
/// A Resource: what supplies capacity (DES-0025 booking-ck-2 to ck-7). Read from an admitted body;
/// the stored JSON, not this view, is the definition, and identity and version are the store's.
/// </summary>
public sealed record BookingResourceDefinition(
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
            Required(body["name"]),
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

/// <summary>
/// One book-gate entry: exactly one platform or domain role, or one authorization capability, and
/// never a standing (DES-0025 booking-ck-12, L509).
/// </summary>
public sealed record BookGateEntry(RoleReference? Role, string? Capability)
{
    internal static BookGateEntry? Read(JsonObject entry)
    {
        if (entry.Count != 1) return null;
        if (entry["capability"] is { } capability)
            return string.IsNullOrWhiteSpace(Text(capability)) ? null : new(null, Text(capability));
        if (entry["role"] is not JsonObject role
            || Text(role["vocabulary"]) is not (RoleVocabularies.Platform or RoleVocabularies.Domain)
            || string.IsNullOrWhiteSpace(Text(role["name"]))
            || role.Count != 2) return null;
        return new(new(Text(role["vocabulary"])!, Text(role["name"])!), null);
    }
}

/// <summary>
/// A Bookable: what may be booked (DES-0025 booking-ck-9 to ck-15). Read from an admitted body.
/// </summary>
public sealed record BookingBookableDefinition(
    string Name,
    string OnTypeId,
    IReadOnlyList<int> DurationIntervals,
    IReadOnlyList<string> Requires,
    IReadOnlyList<BookGateEntry> BookGate,
    string? EligibilityExpression,
    bool Waitlist)
{
    /// <summary>Reads an admitted Bookable body.</summary>
    /// <exception cref="FormatException">The body was not admitted as a Bookable.</exception>
    public static BookingBookableDefinition Parse(string bodyJson)
    {
        var body = BookingResourceDefinition.Admitted(bodyJson, "bookable");
        return new(
            BookingResourceDefinition.Required(body["name"]),
            BookingResourceDefinition.Required(body["on_type_id"]),
            ((JsonArray)body["duration_intervals"]!).Select(item => Whole(item) ?? throw new FormatException(BookingDefinitionCodes.DurationInvalid)).ToArray(),
            BookingResourceDefinition.Strings(body["requires"]),
            body["book_gate"] is JsonArray gate
                ? gate.Select(item => BookGateEntry.Read((JsonObject)item!) ?? throw new FormatException(BookingDefinitionCodes.GateEntryInvalid)).ToArray()
                : [],
            Text(body["eligibility_expression"]),
            body["waitlist"]?.GetValueKind() == System.Text.Json.JsonValueKind.True);
    }
}
