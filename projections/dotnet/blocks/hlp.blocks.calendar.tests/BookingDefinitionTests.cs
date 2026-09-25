using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Calendar.Booking;
using Harborline.Blocks.Calendar.Models;

using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>T-605: Booking's Resource and Bookable definitions and their structural admission (DES-0025).</summary>
public sealed class BookingDefinitionTests
{
    // The transport's content kinds 0..16 (harborline-api PackContentKind at e81d52fa) plus the
    // platform's additive Layout kind; primitive buckets 0..11 and 99 plus Layout's bucket.
    private static readonly int[] TakenContentKinds = [.. Enumerable.Range(0, 17), LayoutPackIdentity.ContentKind];
    private static readonly int[] TakenPrimitives = [.. Enumerable.Range(0, 12), 99, LayoutPackIdentity.Primitive];

    [Fact(DisplayName = "booking-ck-1,2,9: Booking takes an unused primitive bucket, two unused content kinds and two archive namespaces")]
    public void BookingWireValuesDoNotCollide()
    {
        Assert.DoesNotContain(BookingPackIdentity.Primitive, TakenPrimitives);
        Assert.DoesNotContain(BookingPackIdentity.ResourceContentKind, TakenContentKinds);
        Assert.DoesNotContain(BookingPackIdentity.BookableContentKind, TakenContentKinds);
        Assert.NotEqual(BookingPackIdentity.ResourceContentKind, BookingPackIdentity.BookableContentKind);
        Assert.Equal(BookingPackIdentity.ResourceContentKind, BookingPackIdentity.ContentKindOf(DefinitionKind.Resources));
        Assert.Equal(BookingPackIdentity.BookableContentKind, BookingPackIdentity.ContentKindOf(DefinitionKind.Bookables));
        Assert.NotEqual(DefinitionKind.Resources, DefinitionKind.Bookables);
        Assert.Equal(Enum.GetValues<DefinitionKind>().Length, Enum.GetValues<DefinitionKind>().Distinct().Count());
    }

    [Theory(DisplayName = "booking-ck-3,21: a Resource admits a type by the sealed Bookable Resource trait alone; Schedulable is a separate admission")]
    [InlineData("type.room", null)]            // Resource only
    [InlineData("type.nurse", null)]           // Resource and Schedulable
    [InlineData("type.crew", BookingDefinitionCodes.ResourceTypeNotAdmitted)]  // Schedulable only
    [InlineData("type.note", BookingDefinitionCodes.ResourceTypeNotAdmitted)]  // neither
    [InlineData("type.missing", BookingDefinitionCodes.TypeUnknown)]
    public void ResourceAdmissionReadsOnlyTheBookableResourceTrait(string typeId, string? expected)
    {
        var refusals = Admit(DefinitionKind.Resources, Fixtures.Resource("room", body => body["from_type_id"] = typeId));
        AssertRefusals(refusals, expected is null ? [] : [(expected, "/from_type_id")]);
    }

    [Fact(DisplayName = "booking-ck-4: capacity is exclusive by default, or a pool with its size")]
    public void CapacityIsExclusiveByDefault()
    {
        var exclusive = BookingResourceDefinition.Parse(Fixtures.Resource("room", body => body.Remove("capacity_kind")).ToJsonString());
        Assert.Equal(CapacityKind.Exclusive, exclusive.CapacityKind);
        Assert.Null(exclusive.PoolSize);
        var pool = BookingResourceDefinition.Parse(Fixtures.Resource("room", body =>
        {
            body["capacity_kind"] = "pool";
            body["pool_size"] = 4;
        }).ToJsonString());
        Assert.Equal(CapacityKind.Pool, pool.CapacityKind);
        Assert.Equal(4, pool.PoolSize);
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource("room", body => body["capacity_kind"] = "scalar")),
            [(BookingDefinitionCodes.CapacityKindUnknown, "/capacity_kind")]);
    }

    [Theory(DisplayName = "booking-auth-12: a pool with no size, or a size below one, refuses; size one passes")]
    [InlineData("1", null)]
    [InlineData("4", null)]
    [InlineData("0", BookingDefinitionCodes.PoolSizeInvalid)]
    [InlineData("-1", BookingDefinitionCodes.PoolSizeInvalid)]
    [InlineData("1.5", BookingDefinitionCodes.PoolSizeInvalid)]
    [InlineData("\"2\"", BookingDefinitionCodes.PoolSizeInvalid)]
    [InlineData(null, BookingDefinitionCodes.PoolSizeInvalid)]
    public void PoolSizeMustBeAtLeastOne(string? size, string? expected)
    {
        var body = Fixtures.Resource("room", item =>
        {
            item["capacity_kind"] = "pool";
            if (size is null) item.Remove("pool_size");
            else item["pool_size"] = JsonNode.Parse(size);
        });
        AssertRefusals(Admit(DefinitionKind.Resources, body), expected is null ? [] : [(expected, "/pool_size")]);
        // An exclusive resource declares no pool size: capacity is two kinds, not a number.
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource("room", item => item["pool_size"] = 1)),
            [(BookingDefinitionCodes.PoolSizeInvalid, "/pool_size")]);
    }

    [Fact(DisplayName = "booking-ck-5: setup and cleanup minutes are declared on the Resource; a negative buffer refuses")]
    public void BuffersAreDeclaredOnTheResource()
    {
        var resource = BookingResourceDefinition.Parse(Fixtures.Resource("room").ToJsonString());
        Assert.Equal(10, resource.SetupMinutes);
        Assert.Equal(15, resource.CleanupMinutes);
        var unbuffered = BookingResourceDefinition.Parse(Fixtures.Resource("room", body =>
        {
            body.Remove("setup_minutes");
            body.Remove("cleanup_minutes");
        }).ToJsonString());
        Assert.Equal((0, 0), (unbuffered.SetupMinutes, unbuffered.CleanupMinutes));
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource("room", body =>
        {
            body["setup_minutes"] = -1;
            body["cleanup_minutes"] = "15";
        })), [(BookingDefinitionCodes.BufferInvalid, "/setup_minutes"), (BookingDefinitionCodes.BufferInvalid, "/cleanup_minutes")]);
    }

    [Fact(DisplayName = "booking-ck-6: maintenance names the resource record's own state; a literal block list refuses")]
    public void MaintenanceIsReadFromTheRecord()
    {
        Assert.Equal(["field.out_of_service"],
            BookingResourceDefinition.Parse(Fixtures.Resource("room").ToJsonString()).MaintenanceWindows);
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource("room", body => body["maintenance_windows"] = new JsonArray(
            "field.out_of_service",
            new JsonObject { ["start"] = "2026-10-01T08:00:00Z", ["end"] = "2026-10-01T12:00:00Z" }))),
            [(BookingDefinitionCodes.MaintenanceNotFromRecord, "/maintenance_windows/1")]);
    }

    [Fact(DisplayName = "booking-ck-7: availability_from names the base-hours source; a Resource without one refuses")]
    public void AvailabilitySourceIsRequired()
    {
        Assert.Equal("supply.base-hours", BookingResourceDefinition.Parse(Fixtures.Resource("room").ToJsonString()).AvailabilityFrom);
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource("room", body => body.Remove("availability_from"))),
            [(BookingDefinitionCodes.AvailabilitySourceRequired, "/availability_from")]);
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource("room", body => body["availability_from"] = " ")),
            [(BookingDefinitionCodes.AvailabilitySourceRequired, "/availability_from")]);
    }

    internal static IReadOnlyList<DefinitionRefusal> Admit(DefinitionKind kind, JsonObject body, BookingAdmissionContext? context = null)
        => BookingDefinitionAdmission.For(context ?? Fixtures.Context())(Fixtures.Document(kind, body), DefinitionAdmissionPhase.Author);

    internal static void AssertRefusals(IReadOnlyList<DefinitionRefusal> actual, (string Code, string Pointer)[] expected)
        => Assert.Equal(expected.Select(item => new DefinitionRefusal(item.Code, item.Pointer)), actual);
}

/// <summary>US-0031 and US-0032 shaped fixtures: an exclusive instructor, a pooled room, nurse, pump and line.</summary>
internal static class Fixtures
{
    public const string Tenant = "tenant.a";

    public static BookingAdmissionContext Context(params string[] resources) => new(
        typeId => typeId switch
        {
            "type.room" => new HashSet<string> { BookingDefinitionAdmission.BookableResourceTrait },
            "type.nurse" => new HashSet<string> { BookingDefinitionAdmission.BookableResourceTrait, "platform.trait.schedulable" },
            "type.crew" => new HashSet<string> { "platform.trait.schedulable" },
            "type.note" => new HashSet<string>(),
            _ => null,
        },
        id => (resources.Length == 0 ? ["instructor", "room"] : resources).Contains(id));

    public static JsonObject Envelope(string id, string version = "1.0.0") => new()
    {
        ["identity"] = id,
        ["version"] = version,
        ["tenant"] = Tenant,
        ["cascade_layer"] = "domain_package",
        ["provenance"] = new JsonObject { ["kind"] = "package", ["package"] = "training" },
        ["retention_class"] = "definition",
        ["legal_hold"] = false,
        ["requires"] = new JsonArray(),
    };

    public static JsonObject Resource(string id, Action<JsonObject>? edit = null, string version = "1.0.0")
    {
        var body = new JsonObject
        {
            ["kind"] = "resource",
            ["envelope"] = Envelope(id, version),
            ["name"] = "Induction room",
            ["from_type_id"] = "type.room",
            ["capacity_kind"] = "exclusive",
            ["setup_minutes"] = 10,
            ["cleanup_minutes"] = 15,
            ["maintenance_windows"] = new JsonArray("field.out_of_service"),
            ["availability_from"] = "supply.base-hours",
        };
        edit?.Invoke(body);
        return body;
    }

    public static DefinitionDocument Document(DefinitionKind kind, JsonObject body, string? versionId = null)
    {
        var envelope = body["envelope"] as JsonObject;
        var id = envelope?["identity"]?.GetValue<string>() ?? "unnamed";
        var version = envelope?["version"]?.GetValue<string>() ?? "1.0.0";
        return new(new(Tenant, kind, id), versionId ?? $"{id}@{version}", version, body.ToJsonString());
    }
}
