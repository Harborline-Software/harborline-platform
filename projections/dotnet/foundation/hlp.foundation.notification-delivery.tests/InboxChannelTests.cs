using Harborline.Foundation.NotificationDelivery;
using Xunit;

namespace Harborline.Foundation.NotificationDelivery.Tests;

public sealed class InboxChannelTests
{
    [Fact]
    public async Task Write_then_read_and_mark_read_share_one_principal_inbox()
    {
        var inbox = new InMemoryInboxStore();
        const string recipient = "principal-a";
        var channel = new InboxChannel(inbox, TimeProvider.System);
        var written = await channel.WriteAsync(new WriteInboxEntry(
            "tenant-a",
            recipient,
            "decision",
            "Approval required",
            "The full rendered message stays on the node.",
            "Record 17",
            "/records/17",
            true));

        var beforeRead = await inbox.ListAsync("tenant-a", recipient);
        Assert.Single(beforeRead);
        Assert.Equal("The full rendered message stays on the node.", beforeRead[0].RenderedContent);
        Assert.Equal(InboxReadState.Unread, beforeRead[0].ReadState);
        Assert.Equal(1, await inbox.UnreadCountAsync("tenant-a", recipient));

        await inbox.MarkReadAsync("tenant-a", recipient, written.Id);

        Assert.Equal(0, await inbox.UnreadCountAsync("tenant-a", recipient));
        Assert.Equal(InboxReadState.Read, (await inbox.ListAsync("tenant-a", recipient))[0].ReadState);
    }

    [Fact]
    public void Inbox_is_the_only_registered_initial_channel_and_needs_no_secret_or_retry_profile()
    {
        var channel = DeliveryChannels.Require(DeliveryChannelKey.Inbox);

        Assert.Equal(DeliveryChannelKey.Inbox, channel.Key);
        Assert.Empty(channel.RequiredSecretReferenceNames);
        Assert.Empty(channel.AllowedRetryProfiles);
        Assert.Equal(ChannelContentClass.OnNodeFull, channel.ContentClass);
        Assert.Equal(ChannelContentClass.LinkOnly, DeliveryChannels.ExternalDefaultContentClass);
        Assert.Throws<NotificationDeliveryRefusedException>(() => DeliveryChannels.Require(default));
    }

    [Fact]
    public void Inbox_binding_refuses_secret_references_because_the_inbox_needs_none()
    {
        var exception = Assert.Throws<NotificationDeliveryRefusedException>(() => new ChannelBinding(
            "tenant-a",
            DeliveryChannelKey.Inbox,
            enabled: true,
            senderIdentity: null,
            secretReferenceIds: ["secret://smtp/password"]));

        Assert.Equal(NotificationDeliveryRefusals.SecretReferenceNotAllowed, exception.Code);
    }

    [Fact]
    public async Task Inbox_refuses_an_entry_missing_rendered_content()
    {
        var channel = new InboxChannel(new InMemoryInboxStore(), TimeProvider.System);

        var exception = await Assert.ThrowsAsync<NotificationDeliveryRefusedException>(() => channel.WriteAsync(new WriteInboxEntry(
            "tenant-a",
            "principal-a",
            "information",
            "Update",
            " ",
            "Record 17",
            "/records/17",
            false)));

        Assert.Equal(NotificationDeliveryRefusals.InboxEntryInvalid, exception.Code);
    }
}
