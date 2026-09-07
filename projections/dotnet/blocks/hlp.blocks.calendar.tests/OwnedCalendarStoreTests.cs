using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;

using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Calendar-productization #149 slice C1 — the owned-<see cref="OwnedCalendar"/> entity invariants +
/// the <see cref="InMemoryCalendarStore"/> round-trip / ordering / cross-tenant isolation. The durable
/// EF store is covered by the node's <c>NodeEfCalendarCollectionStoreTests</c>; this proves the
/// store-agnostic block contract.
/// </summary>
public sealed class OwnedCalendarStoreTests
{
    private static readonly TenantId TenantA = new("11111111-1111-1111-1111-111111111111");
    private static readonly TenantId TenantB = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Owner = Guid.NewGuid();

    // ── entity invariants ───────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "entity: CreateDefault yields a default personal calendar named by the i18n key")]
    public void CreateDefault_IsDefaultPersonalWithNameKey()
    {
        var cal = OwnedCalendar.CreateDefault(TenantA, Owner);

        Assert.True(cal.IsDefault);
        Assert.Equal(CalendarKind.Personal, cal.Kind);
        Assert.Equal(OwnedCalendar.DefaultNameKey, cal.Name); // never a hardcoded English literal (§1.6)
        Assert.Equal("calendar.defaultName", cal.Name);
        Assert.Null(cal.ResourceRef);
        Assert.Equal(Owner, cal.OwnerActorId);
    }

    [Fact(DisplayName = "entity: a Resource calendar REQUIRES a resource ref")]
    public void Create_ResourceKind_RequiresRef()
    {
        Assert.Throws<ArgumentException>(() =>
            OwnedCalendar.Create(TenantA, "Exam Room 3", CalendarKind.Resource, Owner, resourceRef: null));
    }

    [Fact(DisplayName = "entity: a Personal/Team calendar must NOT carry a resource ref")]
    public void Create_NonResourceKind_RejectsRef()
    {
        Assert.Throws<ArgumentException>(() =>
            OwnedCalendar.Create(TenantA, "Care team", CalendarKind.Team, Owner,
                resourceRef: ParticipantRef.Party("party-1")));
    }

    [Fact(DisplayName = "entity: a blank name is rejected")]
    public void Create_BlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            OwnedCalendar.Create(TenantA, "   ", CalendarKind.Personal, Owner));
    }

    // ── store round-trip ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "store: a resource calendar round-trips every field through (de)serialization")]
    public async Task Save_Get_RoundTripsAllFields()
    {
        var store = new InMemoryCalendarStore();
        var room = ParticipantRef.Asset("room-7");
        var cal = OwnedCalendar.Create(TenantA, "Exam Room 3", CalendarKind.Resource, Owner,
            resourceRef: room, colorToken: "calendar-accent-3");
        await store.SaveAsync(cal);

        var reloaded = await store.GetAsync(TenantA, cal.Id);

        Assert.NotNull(reloaded);
        Assert.Equal("Exam Room 3", reloaded!.Name);
        Assert.Equal(CalendarKind.Resource, reloaded.Kind);
        Assert.Equal("calendar-accent-3", reloaded.ColorToken);
        Assert.Equal(Owner, reloaded.OwnerActorId);
        Assert.False(reloaded.IsDefault);
        Assert.NotNull(reloaded.ResourceRef);
        Assert.Equal(ParticipantKind.Asset, reloaded.ResourceRef!.Kind);
        Assert.Equal("room-7", reloaded.ResourceRef.Value);
    }

    [Fact(DisplayName = "store: list returns the default calendar first, then by created time")]
    public async Task List_DefaultFirst()
    {
        var store = new InMemoryCalendarStore();
        var now = DateTimeOffset.UtcNow;
        // Save a non-default BEFORE the default so insertion order != expected order.
        await store.SaveAsync(OwnedCalendar.Create(TenantA, "Team", CalendarKind.Team, Owner,
            createdAt: now));
        await store.SaveAsync(OwnedCalendar.CreateDefault(TenantA, Owner, createdAt: now.AddMinutes(1)));
        await store.SaveAsync(OwnedCalendar.Create(TenantA, "Later", CalendarKind.Personal, Owner,
            createdAt: now.AddMinutes(2)));

        var list = await store.ListAsync(TenantA);

        Assert.Equal(3, list.Count);
        Assert.True(list[0].IsDefault);                 // default sorts first regardless of created time
        Assert.Equal("Team", list[1].Name);            // then by created time
        Assert.Equal("Later", list[2].Name);
    }

    [Fact(DisplayName = "store: get + list are cross-tenant isolated")]
    public async Task Get_List_CrossTenantIsolated()
    {
        var store = new InMemoryCalendarStore();
        var cal = OwnedCalendar.CreateDefault(TenantA, Owner);
        await store.SaveAsync(cal);

        Assert.Null(await store.GetAsync(TenantB, cal.Id));
        Assert.Empty(await store.ListAsync(TenantB));
        Assert.Single(await store.ListAsync(TenantA));
    }

    [Fact(DisplayName = "store: remove is idempotent")]
    public async Task Remove_Idempotent()
    {
        var store = new InMemoryCalendarStore();
        var cal = OwnedCalendar.Create(TenantA, "Team", CalendarKind.Team, Owner);
        await store.SaveAsync(cal);

        Assert.True(await store.RemoveAsync(TenantA, cal.Id));
        Assert.False(await store.RemoveAsync(TenantA, cal.Id)); // second remove is a no-op
        Assert.Null(await store.GetAsync(TenantA, cal.Id));
    }

    [Fact(DisplayName = "store: re-save upserts on the composite key (no duplicate row)")]
    public async Task Save_Upserts()
    {
        var store = new InMemoryCalendarStore();
        var cal = OwnedCalendar.Create(TenantA, "Team", CalendarKind.Team, Owner);
        await store.SaveAsync(cal);

        cal.Rename("Care team", Owner);
        await store.SaveAsync(cal);

        var list = await store.ListAsync(TenantA);
        Assert.Single(list);
        Assert.Equal("Care team", list[0].Name);
    }
}
