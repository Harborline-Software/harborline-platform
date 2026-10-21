using Harborline.Foundation.NotificationDelivery;
using Xunit;

namespace Harborline.Foundation.NotificationDelivery.Tests;

public sealed class DeliveryChannelBoundaryTests
{
    // DES-0055 notification-delivery-ck-1: the R1 transport key is literally "inbox".
    [Fact]
    public void Notification_delivery_ck_1_inbox_key_renders_its_stable_name_and_default_renders_empty()
    {
        Assert.Equal("inbox", DeliveryChannelKey.Inbox.Value);
        Assert.Equal("inbox", DeliveryChannelKey.Inbox.ToString());
        Assert.Null(default(DeliveryChannelKey).Value);
        Assert.Equal(string.Empty, default(DeliveryChannelKey).ToString());
    }

    [Fact]
    public void Notification_delivery_ck_1_unknown_channel_refuses_with_a_code_and_operator_explanation()
    {
        var exception = Assert.Throws<NotificationDeliveryRefusedException>(() => DeliveryChannels.Require(default));

        Assert.Equal("notification_delivery.channel_unknown", exception.Code);
        Assert.Equal("Channel '' is not registered.", exception.Message);
    }

    // DES-0055 notification-delivery-ck-2: empty secret references are valid for an inbox binding.
    [Theory]
    [InlineData(true, "notification-sender")]
    [InlineData(false, null)]
    public void Notification_delivery_ck_2_empty_binding_preserves_tenant_channel_enablement_and_sender(
        bool enabled, string? senderIdentity)
    {
        var binding = new ChannelBinding("tenant-a", DeliveryChannelKey.Inbox, enabled, senderIdentity, []);

        Assert.Equal("tenant-a", binding.Tenant);
        Assert.Equal("inbox", binding.Channel.Value);
        Assert.Equal(enabled, binding.Enabled);
        Assert.Equal(senderIdentity, binding.SenderIdentity);
        Assert.Empty(binding.SecretReferenceIds);
    }

    [Fact]
    public void Notification_delivery_ck_2_binding_copies_references_instead_of_retaining_the_callers_list()
    {
        var references = new List<string>();
        var binding = new ChannelBinding("tenant-a", DeliveryChannelKey.Inbox, true, null, references);

        references.Add("secret://smtp/password");

        Assert.Empty(binding.SecretReferenceIds);
    }

    [Fact]
    public void Notification_delivery_ck_2_null_reference_collection_is_refused_before_binding()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ChannelBinding(
            "tenant-a", DeliveryChannelKey.Inbox, true, null, null!));

        Assert.Equal("secretReferenceIds", exception.ParamName);
    }

    [Fact]
    public void Notification_delivery_ck_2_binding_an_unknown_channel_refuses_even_without_secrets()
    {
        var exception = Assert.Throws<NotificationDeliveryRefusedException>(() => new ChannelBinding(
            "tenant-a", default, true, null, []));

        Assert.Equal("notification_delivery.channel_unknown", exception.Code);
        Assert.Equal("Channel '' is not registered.", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Notification_delivery_ck_2_inbox_secrets_refuse_even_when_the_binding_is_disabled(bool enabled)
    {
        var exception = Assert.Throws<NotificationDeliveryRefusedException>(() => new ChannelBinding(
            "tenant-a", DeliveryChannelKey.Inbox, enabled, null, ["secret://smtp/password"]));

        Assert.Equal("notification_delivery.secret_reference_not_allowed", exception.Code);
        Assert.Equal("Channel 'inbox' declares no secret references.", exception.Message);
    }
}
