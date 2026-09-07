using Xunit;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Blocks.ActivityTimeline.Tests;

public sealed class ActivityTimelineTests
{
    private static readonly TenantId Tenant = new("tenant-1");
    private static readonly ActivitySessionId Session = new("session-1");
    private static InMemoryActivityEntrySource NewSource() => new(new DictionaryAttribution());

    [Fact]
    public void Starts_empty()
    {
        var source = NewSource();
        Assert.Empty(source.Notifications);
        Assert.Empty(source.Read(Tenant, Session));
    }

    [Fact]
    public void AddResult_adds_a_notification_and_an_activity_entry()
    {
        var source = NewSource();
        source.AddResult(Tenant, Session, "job-1", "TTS complete", "Voice: Samantha", true);
        var notification = Assert.Single(source.Notifications);
        Assert.Equal("result:job-1", notification.Id);
        Assert.Equal("result", notification.Kind);
        Assert.False(notification.Read);
        Assert.Equal(ActivityCategory.CapabilityResult, Assert.Single(source.Read(Tenant, Session)).Category);
    }

    [Fact]
    public void AddResult_with_succeeded_false_uses_capability_error_category()
    {
        var source = NewSource();
        source.AddResult(Tenant, Session, "j2", "Failed", null, false);
        Assert.Equal(ActivityCategory.CapabilityError, Assert.Single(source.Read(Tenant, Session)).Category);
    }

    [Fact]
    public void DismissNotification_removes_the_item_by_id()
    {
        var source = NewSource();
        source.AddResult(Tenant, Session, "j1", "A", null, true);
        source.AddResult(Tenant, Session, "j2", "B", null, true);
        Assert.Equal(2, source.Notifications.Count);
        source.DismissNotification("result:j1");
        Assert.Equal("result:j2", Assert.Single(source.Notifications).Id);
    }

    [Fact]
    public void MarkAllRead_marks_every_notification_as_read()
    {
        var source = NewSource();
        source.AddResult(Tenant, Session, "j1", "A", null, true);
        source.AddResult(Tenant, Session, "j2", "B", null, true);
        Assert.Contains(source.Notifications, item => !item.Read);
        source.MarkAllRead();
        Assert.All(source.Notifications, item => Assert.True(item.Read));
    }

    [Fact]
    public void Notifications_prepend_newest_first()
    {
        var source = NewSource();
        source.AddResult(Tenant, Session, "1", "First", null, true);
        source.AddResult(Tenant, Session, "2", "Second", null, true);
        Assert.Equal("result:2", source.Notifications[0].Id);
        Assert.Equal("result:1", source.Notifications[1].Id);
        Assert.Equal("act:result:2", source.Read(Tenant, Session)[0].Id);
        Assert.Equal(2, source.Count(Tenant, Session));
    }

    [Fact]
    public void DismissNotification_does_not_touch_activityLog()
    {
        var source = NewSource();
        source.AddResult(Tenant, Session, "j1", "A", null, true);
        var logLength = source.Count(Tenant, Session);
        source.DismissNotification("result:j1");
        Assert.Equal(logLength, source.Count(Tenant, Session));
    }

    // AUTHORED (obligation 3): no pinned evidence — authored per WAVE-STATE ruling 2
    [Fact]
    public void Authored_Foreign_tenant_read_fails_closed()
    {
        var source = NewSource();
        source.AddResult(Tenant, Session, "j1", "A", null, true);
        Assert.Empty(source.Read(new TenantId("foreign-tenant"), Session));
        Assert.Equal(0, source.Count(new TenantId("foreign-tenant"), Session));
    }

    // AUTHORED (obligation 3): no pinned evidence — authored per WAVE-STATE ruling 2
    [Fact]
    public void Authored_Foreign_session_read_fails_closed()
    {
        var source = NewSource();
        source.AddResult(Tenant, Session, "j1", "A", null, true);
        Assert.Empty(source.Read(Tenant, new ActivitySessionId("foreign-session")));
    }

    // AUTHORED (obligation 5): no pinned evidence — authored per WAVE-STATE ruling 2
    [Fact]
    public void Authored_Returned_entry_has_no_raw_actor_id_member()
    {
        var names = typeof(ActivityEntry).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("ActorId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Equals("Actor", StringComparison.OrdinalIgnoreCase));
    }

    // AUTHORED (obligation 5): no pinned evidence — authored per WAVE-STATE ruling 2
    [Fact]
    public void Authored_Attribution_seals_raw_ids_as_display_identity()
    {
        var source = NewSource();
        source.Append(new("entry-1", Tenant, Session, "raw-proposer-007", "raw-confirmer-009",
            ActivityCategory.ProposalConfirmed, "12:00",
            new ActivityDetail("confirmed demo-cp-op", "demo-cp-op({ dryRun: true })")));
        var entry = Assert.Single(source.Read(Tenant, Session));
        Assert.Equal("app-agent", entry.ProposedBy?.DisplayName);
        Assert.Equal("Chris", entry.ConfirmedBy?.DisplayName);
        Assert.DoesNotContain("raw-", entry.ToString(), StringComparison.Ordinal);
    }

    private sealed class DictionaryAttribution : IActivityAttribution
    {
        public ActivityDisplayIdentity Resolve(string actorId) => new(actorId switch
        {
            "raw-proposer-007" => "app-agent",
            "raw-confirmer-009" => "Chris",
            _ => "System"
        });
    }
}
