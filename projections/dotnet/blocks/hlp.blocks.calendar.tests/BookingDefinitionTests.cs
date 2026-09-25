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
        var refusals = Admit(DefinitionKind.Resources, Fixtures.Resource(body => body["from_type_id"] = typeId));
        AssertRefusals(refusals, expected is null ? [] : [(expected, "/from_type_id")]);
    }

    [Fact(DisplayName = "booking-ck-4: capacity is exclusive by default, or a pool with its size; a size without a pool refuses")]
    public void CapacityIsExclusiveByDefault()
    {
        var exclusive = BookingResourceDefinition.Parse(Fixtures.Resource(body => body.Remove("capacity_kind")).ToJsonString());
        Assert.Equal(CapacityKind.Exclusive, exclusive.CapacityKind);
        Assert.Null(exclusive.PoolSize);
        var pool = BookingResourceDefinition.Parse(Fixtures.Resource(body =>
        {
            body["capacity_kind"] = "pool";
            body["pool_size"] = 4;
        }).ToJsonString());
        Assert.Equal(CapacityKind.Pool, pool.CapacityKind);
        Assert.Equal(4, pool.PoolSize);
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource(body => body["capacity_kind"] = "scalar")),
            [(BookingDefinitionCodes.CapacityKindUnknown, "/capacity_kind")]);
        // T-724 Q4: a pool size is refused, never ignored, unless the capacity is a pool.
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource(item => item["pool_size"] = 1)),
            [(BookingDefinitionCodes.PoolSizeWithoutPool, "/pool_size")]);
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource(item => { item.Remove("capacity_kind"); item["pool_size"] = 4; })),
            [(BookingDefinitionCodes.PoolSizeWithoutPool, "/pool_size")]);
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
        var body = Fixtures.Resource(item =>
        {
            item["capacity_kind"] = "pool";
            if (size is null) item.Remove("pool_size");
            else item["pool_size"] = JsonNode.Parse(size);
        });
        AssertRefusals(Admit(DefinitionKind.Resources, body), expected is null ? [] : [(expected, "/pool_size")]);
    }

    [Fact(DisplayName = "booking-ck-5: setup and cleanup minutes are declared on the Resource; a negative buffer refuses")]
    public void BuffersAreDeclaredOnTheResource()
    {
        var resource = BookingResourceDefinition.Parse(Fixtures.Resource().ToJsonString());
        Assert.Equal(10, resource.SetupMinutes);
        Assert.Equal(15, resource.CleanupMinutes);
        var unbuffered = BookingResourceDefinition.Parse(Fixtures.Resource(body =>
        {
            body.Remove("setup_minutes");
            body.Remove("cleanup_minutes");
        }).ToJsonString());
        Assert.Equal((0, 0), (unbuffered.SetupMinutes, unbuffered.CleanupMinutes));
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource(body =>
        {
            body["setup_minutes"] = -1;
            body["cleanup_minutes"] = "15";
        })), [(BookingDefinitionCodes.BufferInvalid, "/setup_minutes"), (BookingDefinitionCodes.BufferInvalid, "/cleanup_minutes")]);
    }

    [Fact(DisplayName = "booking-ck-6: maintenance names the resource record's own state; a literal block list refuses")]
    public void MaintenanceIsReadFromTheRecord()
    {
        Assert.Equal(["field.out_of_service"],
            BookingResourceDefinition.Parse(Fixtures.Resource().ToJsonString()).MaintenanceWindows);
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource(body => body["maintenance_windows"] = new JsonArray(
            "field.out_of_service",
            new JsonObject { ["start"] = "2026-10-01T08:00:00Z", ["end"] = "2026-10-01T12:00:00Z" }))),
            [(BookingDefinitionCodes.MaintenanceNotFromRecord, "/maintenance_windows/1")]);
    }

    [Fact(DisplayName = "booking-ck-7: availability_from names the base-hours source; a Resource without one refuses")]
    public void AvailabilitySourceIsRequired()
    {
        Assert.Equal("supply.base-hours", BookingResourceDefinition.Parse(Fixtures.Resource().ToJsonString()).AvailabilityFrom);
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource(body => body.Remove("availability_from"))),
            [(BookingDefinitionCodes.AvailabilitySourceRequired, "/availability_from")]);
        AssertRefusals(Admit(DefinitionKind.Resources, Fixtures.Resource(body => body["availability_from"] = " ")),
            [(BookingDefinitionCodes.AvailabilitySourceRequired, "/availability_from")]);
    }

    [Fact(DisplayName = "booking-ck-10,15: a Bookable declares positive duration intervals and the record type it is offered against")]
    public void BookableDeclaresDurationAndOfferedType()
    {
        var bookable = BookingBookableDefinition.Parse(Fixtures.Bookable().ToJsonString());
        Assert.Equal([90], bookable.DurationIntervals);
        Assert.Equal("type.learner", bookable.OnTypeId);
        AssertRefusals(Admit(DefinitionKind.Bookables, Fixtures.Bookable(body => body["on_type_id"] = "type.missing")),
            [(BookingDefinitionCodes.TypeUnknown, "/on_type_id")]);
    }

    [Theory(DisplayName = "booking-auth-13: a zero or negative duration refuses, because only intervals can overlap")]
    [InlineData("[90]", null, null)]
    [InlineData("[30, 60]", null, null)]
    [InlineData("[0]", BookingDefinitionCodes.DurationInvalid, "/duration_intervals/0")]
    [InlineData("[30, -15]", BookingDefinitionCodes.DurationInvalid, "/duration_intervals/1")]
    [InlineData("[]", BookingDefinitionCodes.DurationInvalid, "/duration_intervals")]
    [InlineData("90", BookingDefinitionCodes.DurationInvalid, "/duration_intervals")]
    public void DurationMustBePositive(string durations, string? code, string? location)
    {
        var refusals = Admit(DefinitionKind.Bookables, Fixtures.Bookable(body => body["duration_intervals"] = JsonNode.Parse(durations)));
        AssertRefusals(refusals, code is null ? [] : [(code, location!)]);
    }

    [Fact(DisplayName = "booking-ck-11: required resources are a conjunction; a candidate list refuses")]
    public void RequiredResourcesAreAConjunction()
    {
        Assert.Equal(["instructor", "room"], BookingBookableDefinition.Parse(Fixtures.Bookable().ToJsonString()).Requires);
        AssertRefusals(Admit(DefinitionKind.Bookables, Fixtures.Bookable(body => body.Remove("require_all"))), []);
        AssertRefusals(Admit(DefinitionKind.Bookables, Fixtures.Bookable(body => body["require_all"] = false)),
            [(BookingDefinitionCodes.CandidateListForbidden, "/require_all")]);
    }

    [Fact(DisplayName = "booking-auth-11: every required resource that is not an admitted Resource refuses, one refusal each")]
    public void RequiredResourceMustBeAdmitted()
    {
        var body = Fixtures.Bookable(item => item["requires"] = new JsonArray("nurse", "room", "pump", "line"));
        AssertRefusals(Admit(DefinitionKind.Bookables, body), [
            (BookingDefinitionCodes.RequiredResourceUnknown, "/requires/0"),
            (BookingDefinitionCodes.RequiredResourceUnknown, "/requires/2"),
            (BookingDefinitionCodes.RequiredResourceUnknown, "/requires/3"),
        ]);
        AssertRefusals(Admit(DefinitionKind.Bookables, body, Fixtures.Context("nurse", "room", "pump", "line")), []);
    }

    [Fact(DisplayName = "booking-ck-12: the book gate names platform or domain roles and capabilities")]
    public void BookGateNamesRolesAndCapabilities()
    {
        var bookable = BookingBookableDefinition.Parse(Fixtures.Bookable().ToJsonString());
        Assert.Equal([
            new BookGateEntry(new("sys.platform-roles", "administrator"), null),
            new BookGateEntry(new("tax.roles", "trainer"), null),
            new BookGateEntry(null, "training.book"),
        ], bookable.BookGate);
        AssertRefusals(Admit(DefinitionKind.Bookables, Fixtures.Bookable(body => body["book_gate"] = new JsonArray(
            new JsonObject { ["role"] = new JsonObject { ["vocabulary"] = "tenant.custom", ["name"] = "x" } },
            new JsonObject { ["capability"] = "training.book", ["role"] = new JsonObject { ["vocabulary"] = "tax.roles", ["name"] = "x" } }))),
            [(BookingDefinitionCodes.GateEntryInvalid, "/book_gate/0"), (BookingDefinitionCodes.GateEntryInvalid, "/book_gate/1")]);
    }

    [Fact(DisplayName = "booking-auth-14: a standing in the book gate refuses; the allocation being created does not exist yet")]
    public void BookGateRefusesAStanding()
        => AssertRefusals(Admit(DefinitionKind.Bookables, Fixtures.Bookable(body => ((JsonArray)body["book_gate"]!).Add(
            new JsonObject { ["standing"] = new JsonObject { ["name"] = "author" } }))),
            [(BookingDefinitionCodes.GateStandingForbidden, "/book_gate/3")]);

    [Fact(DisplayName = "booking-ck-13: eligibility is an optional Rules predicate carried verbatim for server-side evaluation")]
    public void EligibilityIsCarriedVerbatim()
    {
        Assert.Equal("subject.qualification.current == true",
            BookingBookableDefinition.Parse(Fixtures.Bookable().ToJsonString()).EligibilityExpression);
        Assert.Null(BookingBookableDefinition.Parse(Fixtures.Bookable(body => body.Remove("eligibility_expression")).ToJsonString()).EligibilityExpression);
        AssertRefusals(Admit(DefinitionKind.Bookables, Fixtures.Bookable(body => body["eligibility_expression"] = new JsonObject())),
            [(BookingDefinitionCodes.EligibilityInvalid, "/eligibility_expression")]);
    }

    [Fact(DisplayName = "booking-ck-14: waitlist is a flag declaring only that a queue exists")]
    public void WaitlistIsAFlag()
    {
        Assert.True(BookingBookableDefinition.Parse(Fixtures.Bookable().ToJsonString()).Waitlist);
        Assert.False(BookingBookableDefinition.Parse(Fixtures.Bookable(body => body.Remove("waitlist")).ToJsonString()).Waitlist);
    }

    [Fact(DisplayName = "booking-auth-16: an expiring-offer lifecycle authored on the Bookable refuses; that is a Workflow")]
    public void WaitlistOfferLifecycleRefuses()
        => AssertRefusals(Admit(DefinitionKind.Bookables, Fixtures.Bookable(body => body["waitlist"] = new JsonObject
        {
            ["offer_expires_after_minutes"] = 30,
            ["on_expiry"] = "offer_next",
        })), [(BookingDefinitionCodes.WaitlistLifecycleForbidden, "/waitlist")]);

    [Fact(DisplayName = "booking-auth-21: a Bookable offered against a Schedulable type that is not a Resource is not refused")]
    public void SchedulableTypeIsNotRefused()
    {
        AssertRefusals(Admit(DefinitionKind.Bookables, Fixtures.Bookable(body => body["on_type_id"] = "type.crew")), []);
        AssertRefusals(Admit(DefinitionKind.Bookables, Fixtures.Bookable(body => body["on_type_id"] = "type.room")), []);
    }

    [Theory(DisplayName = "booking-ck-17: both definitions carry the full envelope; the store owns identity, version and tenant, and the pack carries all eight")]
    [InlineData(DefinitionKind.Resources)]
    [InlineData(DefinitionKind.Bookables)]
    public void BothDefinitionsCarryTheEnvelope(DefinitionKind kind)
    {
        JsonObject Body(Action<JsonObject> edit) => kind == DefinitionKind.Resources
            ? Fixtures.Resource(body => edit((JsonObject)body["envelope"]!))
            : Fixtures.Bookable(body => edit((JsonObject)body["envelope"]!));
        AssertRefusals(Admit(kind, Body(_ => { })), []);
        foreach (var member in new[] { "cascade_layer", "provenance", "retention_class", "legal_hold", "requires" })
            AssertRefusals(Admit(kind, Body(envelope => envelope.Remove(member))), [(BookingDefinitionCodes.EnvelopeInvalid, "/envelope/" + member)]);
        // Store metadata is never duplicated into the stored body, so a restored draft cannot disagree with it.
        AssertRefusals(Admit(kind, Body(envelope => envelope["version"] = "1.0.0")), [(BookingDefinitionCodes.EnvelopeStoreOwned, "/envelope/version")]);

        var contentKind = BookingPackIdentity.ContentKindOf(kind);
        var packed = Fixtures.Packed(Body(_ => { }), "x");
        var entry = (JsonObject body) => new BookingPackEntry(contentKind, "x", "1.0.0",
            PlatformPackageContent.PresentJson(System.Text.Encoding.UTF8.GetBytes(body.ToJsonString())));
        Assert.Empty(BookingDefinitionPackage.Admit([entry(packed)], Fixtures.Context()));
        foreach (var member in new[] { "identity", "version", "tenant" })
        {
            var missing = (JsonObject)packed.DeepClone();
            ((JsonObject)missing["envelope"]!).Remove(member);
            Assert.Equal([new DefinitionRefusal(BookingDefinitionCodes.EnvelopeInvalid, "/entries/0/envelope/" + member)],
                BookingDefinitionPackage.Admit([entry(missing)], Fixtures.Context()));
        }
        var stranger = (JsonObject)packed.DeepClone();
        stranger["envelope"]!["identity"] = "y";
        Assert.Equal([new DefinitionRefusal(BookingDefinitionCodes.EnvelopeMismatch, "/entries/0/envelope/identity")],
            BookingDefinitionPackage.Admit([entry(stranger)], Fixtures.Context()));
        Assert.Equal([new DefinitionRefusal(BookingDefinitionCodes.KindMismatch, "/kind")],
            BookingDefinitionAdmission.Validate(Fixtures.Document(kind == DefinitionKind.Resources ? DefinitionKind.Bookables : DefinitionKind.Resources,
                Body(_ => { })), Fixtures.Context()));
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
            "type.note" or "type.learner" => new HashSet<string>(),
            _ => null,
        },
        id => (resources.Length == 0 ? ["instructor", "room"] : resources).Contains(id));

    /// <summary>The stored envelope: identity, version and tenant are the store's document metadata.</summary>
    public static JsonObject Envelope() => new()
    {
        ["cascade_layer"] = "domain_package",
        ["provenance"] = new JsonObject { ["kind"] = "package", ["package"] = "training" },
        ["retention_class"] = "definition",
        ["legal_hold"] = false,
        ["requires"] = new JsonArray(),
    };

    public static JsonObject Resource(Action<JsonObject>? edit = null)
    {
        var body = new JsonObject
        {
            ["kind"] = "resource",
            ["envelope"] = Envelope(),
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

    public static JsonObject Bookable(Action<JsonObject>? edit = null)
    {
        var body = new JsonObject
        {
            ["kind"] = "bookable",
            ["envelope"] = Envelope(),
            ["name"] = "Induction class",
            ["on_type_id"] = "type.learner",
            ["duration_intervals"] = new JsonArray(90),
            ["requires"] = new JsonArray("instructor", "room"),
            ["require_all"] = true,
            ["book_gate"] = new JsonArray(
                new JsonObject { ["role"] = new JsonObject { ["vocabulary"] = "sys.platform-roles", ["name"] = "administrator" } },
                new JsonObject { ["role"] = new JsonObject { ["vocabulary"] = "tax.roles", ["name"] = "trainer" } },
                new JsonObject { ["capability"] = "training.book" }),
            ["eligibility_expression"] = "subject.qualification.current == true",
            ["waitlist"] = true,
        };
        edit?.Invoke(body);
        return body;
    }

    public static DefinitionDocument Document(DefinitionKind kind, JsonObject body, string id = "x", string version = "1.0.0")
        => new(new(Tenant, kind, id), $"{id}@{version}", version, body.ToJsonString());

    /// <summary>The body as it travels: the full envelope with identity, version and tenant.</summary>
    public static JsonObject Packed(JsonObject body, string id, string version = "1.0.0")
    {
        var packed = (JsonObject)body.DeepClone();
        var envelope = (JsonObject)packed["envelope"]!;
        envelope["identity"] = id;
        envelope["version"] = version;
        envelope["tenant"] = Tenant;
        return packed;
    }
}
