using System.Text.Json.Nodes;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Calendar.Booking;
using Harborline.Blocks.Calendar.DependencyInjection;
using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

public sealed class BookingInterpreterTests
{
    private static readonly TenantId Tenant = new(Fixtures.Tenant);
    private static readonly ParticipantRef Room = ParticipantRef.Asset("room-1");
    private static readonly ParticipantRef Instructor = ParticipantRef.Party("instructor-1");
    private static readonly DateTimeOffset Start = new(2026, 3, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddMinutes(90);

    [Fact]
    [Trait("Holds", "booking-eng-1")]
    [Trait("Holds", "booking-eng-19")]
    [Trait("Holds", "booking-run-1")]
    public async Task Published_heads_and_conjunctive_buffered_candidates_survive_success_and_refusal()
    {
        var definitions = Store();
        await Publish(definitions, DefinitionKind.Bookables, "class", Fixtures.Bookable());
        await Publish(definitions, DefinitionKind.Resources, "room", Fixtures.Resource());
        await Publish(definitions, DefinitionKind.Resources, "instructor", Fixtures.Resource(b =>
        {
            b["name"] = "Different display name";
            b["setup_minutes"] = 20;
            b["cleanup_minutes"] = 5;
        }));
        await definitions.SaveDraftAsync(Fixtures.Document(DefinitionKind.Resources,
            Fixtures.Resource(b => b["setup_minutes"] = 60), "room", "2.0.0"), 2, "draft-2");
        var runtime = new Probe();
        var events = new InMemoryCalendarEventStore();
        var services = new ServiceCollection();
        services.AddSingleton<IVersionedDefinitionStore>(definitions);
        services.AddSingleton<IAvailabilityRuntime>(runtime);
        services.AddSingleton<ICalendarEventStore>(events);
        services.AddScoped<IPartyContext>(_ => new FixedRequester(Guid.NewGuid()));
        services.AddBlocksCalendar();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var interpreter = scope.ServiceProvider.GetRequiredService<IBookingInterpreter>();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICalendarStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICalendarParticipantCalendarQuery>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IResourceAvailabilityStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICalendarContextQuery>());
        var suppliers = new Dictionary<string, ParticipantRef> { ["room"] = Room, ["instructor"] = Instructor };

        var success = await interpreter.Claim(Tenant, "class", suppliers, Start, End);
        Assert.True(success.Claimed);
        Assert.Equal(2, success.Events.Count);
        Assert.Equal(new[] { Instructor, Room }, runtime.Request!.Resources.Select(r => r.Resource));
        var expected = new[] {
            new BookableClaimCandidate(Instructor, new(Start.AddMinutes(-20), End.AddMinutes(5))),
            new BookableClaimCandidate(Room, new(Start.AddMinutes(-10), End.AddMinutes(15))) };
        Assert.Equal(expected, success.Candidates);

        runtime.Deny = true;
        var refused = await interpreter.Claim(Tenant, "class", suppliers, Start, End);
        Assert.Equal("booking.claim.capacity_exhausted", refused.Refusal);
        Assert.Equal(expected, refused.Candidates);
        Assert.Empty(refused.Events);
        Assert.Equal(2, (await events.ListAsync(Tenant)).Count);
    }

    [Fact]
    [Trait("Holds", "booking-eng-1")]
    public async Task Missing_and_draft_only_heads_and_missing_suppliers_refuse_by_identity_without_mutation()
    {
        var definitions = Store();
        var runtime = new Probe();
        var events = new InMemoryCalendarEventStore();
        var interpreter = new BookingInterpreter(definitions, new(runtime, events, new FixedRequester(Guid.NewGuid())));
        await Assert.ThrowsAsync<ArgumentNullException>(() => interpreter.Claim(Tenant, "class", null!, Start, End));
        var suppliers = new Dictionary<string, ParticipantRef> { ["room"] = Room, ["instructor"] = Instructor };
        await definitions.SaveDraftAsync(Fixtures.Document(DefinitionKind.Bookables, Fixtures.Bookable(), "class"), 0, "draft");
        var missingBookable = await interpreter.Claim(Tenant, "class", suppliers, Start, End);
        Assert.Equal("class", missingBookable.UnresolvedName);
        Assert.Equal("booking.claim.requirement_unresolved", missingBookable.Refusal);
        await definitions.PublishAsync(new(Fixtures.Tenant, DefinitionKind.Bookables, "class"), "class@1.0.0", 1, "publish");
        await definitions.SaveDraftAsync(Fixtures.Document(DefinitionKind.Resources, Fixtures.Resource(), "instructor"), 0, "draft");
        var missingResource = await interpreter.Claim(Tenant, "class", suppliers, Start, End);
        Assert.Equal("instructor", missingResource.UnresolvedName);
        Assert.Equal("booking.claim.requirement_unresolved", missingResource.Refusal);
        await definitions.PublishAsync(new(Fixtures.Tenant, DefinitionKind.Resources, "instructor"), "instructor@1.0.0", 1, "publish");
        suppliers.Remove("instructor");
        var missingSupplier = await interpreter.Claim(Tenant, "class", suppliers, Start, End);
        Assert.Equal("instructor", missingSupplier.UnresolvedName);
        Assert.Equal("booking.claim.requirement_unresolved", missingSupplier.Refusal);
        Assert.Equal(0, runtime.Calls);
        Assert.Empty(await events.ListAsync(Tenant));
    }

    private static InMemoryVersionedDefinitionStore Store()
    {
        var admission = BookingDefinitionAdmission.For(Fixtures.Context());
        return new(new Dictionary<DefinitionKind, DefinitionAdmission> {
            [DefinitionKind.Bookables] = admission, [DefinitionKind.Resources] = admission });
    }

    private static async Task Publish(InMemoryVersionedDefinitionStore store, DefinitionKind kind, string id, JsonObject body)
    {
        var document = Fixtures.Document(kind, body, id);
        await store.SaveDraftAsync(document, 0, "draft");
        await store.PublishAsync(document.Key, document.VersionId, 1, "publish");
    }

    private sealed class Probe : IAvailabilityRuntime
    {
        public bool Deny { get; set; }
        public int Calls { get; private set; }
        public AvailabilityRequest? Request { get; private set; }
        public Task<AvailabilityAnswer> Read(TenantId tenantId, AvailabilityRequest request, CancellationToken ct = default)
        {
            Calls++;
            Request = request;
            return Task.FromResult(new AvailabilityAnswer(null, request.Resources.Select(r =>
                new ResourceAvailabilityRead(r.Resource, [], [], Deny ? 0 : 1,
                    Deny ? Unavailability.CapacityExhausted : Unavailability.None)).ToArray()));
        }
    }
}
