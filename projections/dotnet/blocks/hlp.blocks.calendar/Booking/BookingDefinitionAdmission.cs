using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Blocks.Calendar.Booking;

/// <summary>Stable refusal codes of Booking definition admission (DES-0025 sections 2, 4 and 7).</summary>
public static class BookingDefinitionCodes
{
    /// <summary>The body is not a JSON object.</summary>
    public const string BodyInvalid = "booking.definition.body_invalid";
    /// <summary>The body's <c>kind</c> is not the archive namespace's definition kind.</summary>
    public const string KindMismatch = "booking.definition.kind_mismatch";
    /// <summary>A member outside the fixed vocabulary (L536).</summary>
    public const string MemberUnknown = "booking.definition.member_unknown";
    /// <summary>The definition has no name.</summary>
    public const string NameRequired = "booking.definition.name_required";
    /// <summary>A referenced record type does not exist.</summary>
    public const string TypeUnknown = "booking.definition.type_unknown";
    /// <summary>An envelope member is missing or malformed (booking-ck-17).</summary>
    public const string EnvelopeInvalid = "booking.envelope.invalid";
    /// <summary>A packed envelope's identity or version disagrees with its pack entry.</summary>
    public const string EnvelopeMismatch = "booking.envelope.mismatch";
    /// <summary>A stored body repeats identity, version or tenant, which the store owns as document metadata.</summary>
    public const string EnvelopeStoreOwned = "booking.envelope.store_owned";
    /// <summary>The Resource's type lacks the sealed Bookable Resource trait (booking-ck-3, L1085).</summary>
    public const string ResourceTypeNotAdmitted = "booking.resource.type_not_admitted";
    /// <summary>Capacity is neither exclusive nor pool (booking-ck-4).</summary>
    public const string CapacityKindUnknown = "booking.resource.capacity_kind_unknown";
    /// <summary>A pool without a whole size of at least one (booking-auth-12).</summary>
    public const string PoolSizeInvalid = "booking.resource.pool_size_invalid";
    /// <summary>A pool size on a resource whose capacity is not a pool; refused, never ignored (booking-ck-4, T-724 Q4).</summary>
    public const string PoolSizeWithoutPool = "booking.resource.pool_size_without_pool";
    /// <summary>A setup or cleanup buffer that is not a whole number of minutes at or above zero (booking-ck-5).</summary>
    public const string BufferInvalid = "booking.resource.buffer_invalid";
    /// <summary>Maintenance authored as a block list instead of named record state (booking-ck-6, L506).</summary>
    public const string MaintenanceNotFromRecord = "booking.resource.maintenance_not_from_record";
    /// <summary>The Resource names no base-hours source (booking-ck-7).</summary>
    public const string AvailabilitySourceRequired = "booking.resource.availability_source_required";
    /// <summary>A duration list that is empty, or holds a duration that is not a positive whole number of minutes (booking-auth-13).</summary>
    public const string DurationInvalid = "booking.bookable.duration_invalid";
    /// <summary>A required resource that is not an admitted Resource (booking-auth-11, L1085).</summary>
    public const string RequiredResourceUnknown = "booking.bookable.required_resource_unknown";
    /// <summary>Required resources authored as alternatives rather than a conjunction (booking-ck-11, L504).</summary>
    public const string CandidateListForbidden = "booking.bookable.candidate_list_forbidden";
    /// <summary>A book-gate entry that is not exactly one platform or domain role or one capability (booking-ck-12).</summary>
    public const string GateEntryInvalid = "booking.bookable.gate_entry_invalid";
    /// <summary>A standing in the book gate (booking-auth-14, L509).</summary>
    public const string GateStandingForbidden = "booking.bookable.gate_standing_forbidden";
    /// <summary>An eligibility expression that is not a nonblank predicate text (booking-ck-13).</summary>
    public const string EligibilityInvalid = "booking.bookable.eligibility_invalid";
    /// <summary>A waitlist authored as anything but a flag, such as an expiring-offer lifecycle (booking-auth-16, L508).</summary>
    public const string WaitlistLifecycleForbidden = "booking.bookable.waitlist_lifecycle_forbidden";
    /// <summary>An Allocation or a hold authored as a definition: instances are tenant data (booking-auth-17, L499, L531).</summary>
    public const string RuntimeDataNotADefinition = "booking.allocation.not_a_definition";
    /// <summary>An Allocation or a hold on the pack path (booking-auth-18, L532).</summary>
    public const string RuntimeDataInPack = "booking.pack.runtime_data_forbidden";
    /// <summary>A pack entry whose content kind is not a Booking definition kind.</summary>
    public const string PackContentUnsupported = "booking.pack.content_unsupported";
    /// <summary>Only an immutable published version exports.</summary>
    public const string PublishedVersionRequired = "booking.pack.published_version_required";
}

/// <summary>
/// What admission reads from the host: the sealed traits a record type bears (<see langword="null"/>
/// for an unknown type), and whether an id names an admitted Resource definition.
/// </summary>
public sealed record BookingAdmissionContext(
    Func<string, IReadOnlySet<string>?> RecordTypeTraits,
    Func<string, bool> IsAdmittedResource);

/// <summary>
/// Structural admission of Booking definitions (DES-0025 booking-eng-23, ADR 0071). One pure check
/// serves validate, publish and install; it reports every refusal and changes nothing.
/// </summary>
public static class BookingDefinitionAdmission
{
    /// <summary>The sealed trait that admits a record type to reservation (platform seed ck-4, L1085).</summary>
    public const string BookableResourceTrait = "platform.trait.bookable-resource";

    /// <summary>Envelope members the shared store owns as document metadata; the pack carries them.</summary>
    internal static readonly string[] StoreOwnedEnvelopeMembers = ["identity", "version", "tenant"];
    private static readonly string[] EnvelopeMembers =
        ["identity", "version", "tenant", "cascade_layer", "provenance", "retention_class", "legal_hold", "requires"];
    private static readonly string[] CascadeLayers =
        ["kernel_core", "subsystem", "platform_package", "domain_package", "tenant_configuration"];
    private static readonly string[] ResourceMembers =
        ["kind", "envelope", "name", "from_type_id", "capacity_kind", "pool_size", "setup_minutes", "cleanup_minutes",
         "maintenance_windows", "availability_from"];
    private static readonly string[] BookableMembers =
        ["kind", "envelope", "name", "on_type_id", "duration_intervals", "requires", "require_all", "book_gate",
         "eligibility_expression", "waitlist"];

    /// <summary>The shared store's validator for <see cref="DefinitionKind.Resources"/> and <see cref="DefinitionKind.Bookables"/>.</summary>
    public static DefinitionAdmission For(BookingAdmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return (document, _) => Validate(document, context);
    }

    /// <summary>Returns every structural refusal of one document, in document order.</summary>
    public static IReadOnlyList<DefinitionRefusal> Validate(DefinitionDocument document, BookingAdmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        if (Parse(document.BodyJson) is not { } body) return [new(BookingDefinitionCodes.BodyInvalid, "")];
        if (IsRuntimeData(body)) return [new(BookingDefinitionCodes.RuntimeDataNotADefinition, "/kind")];
        var expected = document.Key.Kind switch
        {
            DefinitionKind.Resources => "resource",
            DefinitionKind.Bookables => "bookable",
            _ => null,
        };
        if (expected is null) return [new("definition.registry_unknown", "/registry")];
        if (Text(body["kind"]) != expected) return [new(BookingDefinitionCodes.KindMismatch, "/kind")];

        var refusals = new List<DefinitionRefusal>();
        Members(body, expected == "resource" ? ResourceMembers : BookableMembers, "", refusals);
        Envelope(body["envelope"], refusals);
        if (string.IsNullOrWhiteSpace(Text(body["name"]))) refusals.Add(new(BookingDefinitionCodes.NameRequired, "/name"));
        if (expected == "resource") Resource(body, context, refusals);
        else Bookable(body, context, refusals);
        return refusals;
    }

    private static void Bookable(JsonObject body, BookingAdmissionContext context, List<DefinitionRefusal> refusals)
    {
        // Offered against any known type: Schedulable and Resource admission are independent (booking-auth-21).
        RecordType(body, "on_type_id", context, refusals);

        if (body["duration_intervals"] is not JsonArray { Count: > 0 } durations)
            refusals.Add(new(BookingDefinitionCodes.DurationInvalid, "/duration_intervals"));
        else
            for (var index = 0; index < durations.Count; index++)
                if (Whole(durations[index]) is not > 0)
                    refusals.Add(new(BookingDefinitionCodes.DurationInvalid, $"/duration_intervals/{index}"));

        if (body["requires"] is not JsonArray requires)
            refusals.Add(new(BookingDefinitionCodes.RequiredResourceUnknown, "/requires"));
        else
            for (var index = 0; index < requires.Count; index++)
                if (Text(requires[index]) is not { } id || string.IsNullOrWhiteSpace(id) || !context.IsAdmittedResource(id))
                    refusals.Add(new(BookingDefinitionCodes.RequiredResourceUnknown, $"/requires/{index}"));
        if (body.ContainsKey("require_all") && body["require_all"]?.GetValueKind() != JsonValueKind.True)
            refusals.Add(new(BookingDefinitionCodes.CandidateListForbidden, "/require_all"));

        if (body["book_gate"] is JsonArray gate)
        {
            for (var index = 0; index < gate.Count; index++)
            {
                var entry = gate[index] as JsonObject;
                if (entry?.ContainsKey("standing") == true)
                    refusals.Add(new(BookingDefinitionCodes.GateStandingForbidden, $"/book_gate/{index}"));
                else if (entry is null || BookGateEntry.Read(entry) is null)
                    refusals.Add(new(BookingDefinitionCodes.GateEntryInvalid, $"/book_gate/{index}"));
            }
        }
        else if (body.ContainsKey("book_gate"))
            refusals.Add(new(BookingDefinitionCodes.GateEntryInvalid, "/book_gate"));

        if (body.ContainsKey("eligibility_expression") && string.IsNullOrWhiteSpace(Text(body["eligibility_expression"])))
            refusals.Add(new(BookingDefinitionCodes.EligibilityInvalid, "/eligibility_expression"));

        if (body.ContainsKey("waitlist") && body["waitlist"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
            refusals.Add(new(BookingDefinitionCodes.WaitlistLifecycleForbidden, "/waitlist"));
    }

    private static void Resource(JsonObject body, BookingAdmissionContext context, List<DefinitionRefusal> refusals)
    {
        if (RecordType(body, "from_type_id", context, refusals) is { } traits && !traits.Contains(BookableResourceTrait))
            refusals.Add(new(BookingDefinitionCodes.ResourceTypeNotAdmitted, "/from_type_id"));

        var capacity = body["capacity_kind"] is null ? "exclusive" : Text(body["capacity_kind"]);
        if (capacity is not ("exclusive" or "pool"))
            refusals.Add(new(BookingDefinitionCodes.CapacityKindUnknown, "/capacity_kind"));
        else if (capacity == "pool" && Whole(body["pool_size"]) is not >= 1)
            refusals.Add(new(BookingDefinitionCodes.PoolSizeInvalid, "/pool_size"));
        else if (capacity != "pool" && body.ContainsKey("pool_size"))
            refusals.Add(new(BookingDefinitionCodes.PoolSizeWithoutPool, "/pool_size"));

        foreach (var buffer in new[] { "setup_minutes", "cleanup_minutes" })
            if (body.ContainsKey(buffer) && Whole(body[buffer]) is not >= 0)
                refusals.Add(new(BookingDefinitionCodes.BufferInvalid, "/" + buffer));

        if (body["maintenance_windows"] is JsonArray windows)
        {
            for (var index = 0; index < windows.Count; index++)
                if (string.IsNullOrWhiteSpace(Text(windows[index])))
                    refusals.Add(new(BookingDefinitionCodes.MaintenanceNotFromRecord, $"/maintenance_windows/{index}"));
        }
        else if (body.ContainsKey("maintenance_windows"))
            refusals.Add(new(BookingDefinitionCodes.MaintenanceNotFromRecord, "/maintenance_windows"));

        if (string.IsNullOrWhiteSpace(Text(body["availability_from"])))
            refusals.Add(new(BookingDefinitionCodes.AvailabilitySourceRequired, "/availability_from"));
    }

    private static IReadOnlySet<string>? RecordType(JsonObject body, string member, BookingAdmissionContext context,
        List<DefinitionRefusal> refusals)
    {
        var typeId = Text(body[member]);
        var traits = string.IsNullOrWhiteSpace(typeId) ? null : context.RecordTypeTraits(typeId);
        if (traits is null) refusals.Add(new(BookingDefinitionCodes.TypeUnknown, "/" + member));
        return traits;
    }

    private static void Envelope(JsonNode? node, List<DefinitionRefusal> refusals)
    {
        if (node is not JsonObject envelope)
        {
            refusals.Add(new(BookingDefinitionCodes.EnvelopeInvalid, "/envelope"));
            return;
        }
        Members(envelope, EnvelopeMembers, "/envelope", refusals);
        foreach (var member in StoreOwnedEnvelopeMembers)
            if (envelope.ContainsKey(member)) refusals.Add(new(BookingDefinitionCodes.EnvelopeStoreOwned, "/envelope/" + member));
        if (!CascadeLayers.Contains(Text(envelope["cascade_layer"])))
            refusals.Add(new(BookingDefinitionCodes.EnvelopeInvalid, "/envelope/cascade_layer"));
        if (envelope["provenance"] is not JsonObject)
            refusals.Add(new(BookingDefinitionCodes.EnvelopeInvalid, "/envelope/provenance"));
        if (string.IsNullOrWhiteSpace(Text(envelope["retention_class"])))
            refusals.Add(new(BookingDefinitionCodes.EnvelopeInvalid, "/envelope/retention_class"));
        if (envelope["legal_hold"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
            refusals.Add(new(BookingDefinitionCodes.EnvelopeInvalid, "/envelope/legal_hold"));
        if (envelope["requires"] is not JsonArray requires)
            refusals.Add(new(BookingDefinitionCodes.EnvelopeInvalid, "/envelope/requires"));
        else
            for (var index = 0; index < requires.Count; index++)
                if (string.IsNullOrWhiteSpace(Text((requires[index] as JsonObject)?["capability"])))
                    refusals.Add(new(BookingDefinitionCodes.EnvelopeInvalid, $"/envelope/requires/{index}"));
    }

    private static void Members(JsonObject node, string[] allowed, string pointer, List<DefinitionRefusal> refusals)
    {
        foreach (var member in node)
            if (!allowed.Contains(member.Key))
                refusals.Add(new(BookingDefinitionCodes.MemberUnknown, $"{pointer}/{Escape(member.Key)}"));
    }

    internal static JsonObject? Parse(string? json)
    {
        try { return JsonNode.Parse(json ?? "") as JsonObject; }
        catch (JsonException) { return null; }
    }

    /// <summary>An Allocation or a hold is runtime state, never definition or pack content.</summary>
    internal static bool IsRuntimeData(JsonObject body) => Text(body["kind"]) is "allocation" or "hold";

    internal static string? Text(JsonNode? node)
        => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    internal static int? Whole(JsonNode? node)
        => node?.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue<int>(out var value) ? value : null;

    private static string Escape(string member) => member.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
