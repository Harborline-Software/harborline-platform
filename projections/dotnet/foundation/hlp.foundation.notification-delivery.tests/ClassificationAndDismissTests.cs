using Harborline.Foundation.NotificationDelivery;
using Xunit;

namespace Harborline.Foundation.NotificationDelivery.Tests;

public sealed class ClassificationAndDismissTests
{
    // Oracle: DES-0055 ck-8's exhaustive four-member vocabulary (control #1263).
    // Mutation outcome-added: add Unexpected = 4 to DeliveryOutcomeClass.
    [Fact(DisplayName = "notification-delivery-ck-8: outcome vocabulary is closed")]
    public void Outcome_vocabulary_is_closed()
    {
        Assert.Equal(new[] { "Accepted", "Delivered", "Transient", "Permanent" }, Enum.GetNames<DeliveryOutcomeClass>());
        Assert.Equal(new[] { 0, 1, 2, 3 }, Enum.GetValues<DeliveryOutcomeClass>().Select(value => (int)value));
    }

    // Oracle: ck-1 requires a classifier; no unset capability is admitted (control #1263).
    // Mutation classifier-unset: replace `classifyResult is null` with false.
    [Fact(DisplayName = "notification-delivery-ck-1: unset outcome classification refuses")]
    public void Unset_classifier_refuses()
    {
        var refusal = Assert.Throws<NotificationDeliveryRefusedException>(() => Channel(null!));
        Assert.Equal("notification_delivery.classification_required", refusal.Code);
        Assert.Equal("A channel must declare its outcome classification.", refusal.Message);
    }

    // Oracle: ck-8 requires diagnostic preservation, regardless of the declared mapping.
    // Mutation diagnostic-replaced: return an empty diagnostic from Classify.
    [Theory(DisplayName = "notification-delivery-ck-1 notification-delivery-ck-8: declared classifier preserves diagnostic verbatim")]
    [InlineData(DeliveryOutcomeClass.Accepted)]
    [InlineData(DeliveryOutcomeClass.Delivered)]
    [InlineData(DeliveryOutcomeClass.Transient)]
    [InlineData(DeliveryOutcomeClass.Permanent)]
    public void Declared_classifier_preserves_diagnostic(DeliveryOutcomeClass expected)
    {
        var result = new DeliveryTransportResult(false, "  550\r\n opaque: Ä; do not parse  ");
        DeliveryTransportResult? seen = null;
        var channel = Channel(value => { seen = value; return expected; });
        var outcome = channel.Classify(result);
        Assert.Same(result, seen);
        Assert.Equal(expected, outcome.Classification);
        Assert.Equal("  550\r\n opaque: Ä; do not parse  ", outcome.Diagnostic);
        Assert.Equal(DeliveryChannelKey.Inbox, channel.Key);
        Assert.Equal(new[] { "credential" }, channel.RequiredSecretReferenceNames);
        Assert.Equal(new[] { "retry" }, channel.AllowedRetryProfiles);
        Assert.Equal(ChannelContentClass.LinkOnly, channel.ContentClass);
    }

    // Oracle: only the four ck-8 classes may cross the boundary.
    // Mutation unknown-admitted: replace !Enum.IsDefined(classification) with false.
    [Fact(DisplayName = "notification-delivery-ck-8: undefined classifier outcome refuses")]
    public void Undefined_outcome_refuses()
    {
        var refusal = Assert.Throws<NotificationDeliveryRefusedException>(() =>
            Channel(_ => (DeliveryOutcomeClass)99).Classify(new(false, "opaque")));
        Assert.Equal("notification_delivery.outcome_unknown", refusal.Code);
        Assert.Equal("The transport classifier returned an unknown outcome.", refusal.Message);
        Assert.Throws<ArgumentNullException>(() => Channel(_ => DeliveryOutcomeClass.Delivered).Classify(null!));
    }

    // Oracle: control #1263 / DES-0055 ruling 7: committed = delivered, failed = permanent.
    // Mutation inbox-inverted: negate result.Succeeded in the inbox classifier.
    [Theory(DisplayName = "notification-delivery-ck-1 notification-delivery-ck-8: inbox classifies committed and failed writes without retry")]
    [InlineData(false, DeliveryOutcomeClass.Delivered)]
    [InlineData(true, DeliveryOutcomeClass.Permanent)]
    public async Task Inbox_classifies_write_completion(bool fail, DeliveryOutcomeClass expected)
    {
        IInboxStore store = fail ? new FailingStore() : new InMemoryInboxStore();
        var writer = new InboxChannel(store, TimeProvider.System);
        DeliveryTransportResult result;
        try
        {
            var entry = await writer.WriteAsync(new("tenant-a", "person-a", "information", "Title", "Body", "Subject", "/subject", false));
            Assert.Single(await store.ListAsync("tenant-a", "person-a"));
            Assert.Equal(InboxReadState.Unread, entry.ReadState);
            result = new(true, "committed verbatim");
        }
        catch (InvalidOperationException exception)
        {
            result = new(false, exception.Message);
        }
        var channel = DeliveryChannels.Require(DeliveryChannelKey.Inbox);
        var outcome = channel.Classify(result);
        Assert.Equal(expected, outcome.Classification);
        Assert.Equal(fail ? "store defect verbatim\r\n" : "committed verbatim", outcome.Diagnostic);
        Assert.Empty(channel.AllowedRetryProfiles);
    }

    // Oracle: ck-7 is per delivery, not per recipient or tenant; dismissal clears unread count.
    // Mutation dismiss-read: write Read rather than Dismissed in DismissAsync.
    [Fact(DisplayName = "notification-delivery-ck-7: dismiss changes only one scoped delivery")]
    public async Task Dismiss_changes_only_one_delivery()
    {
        IInboxStore store = new InMemoryInboxStore();
        var target = Entry("tenant-a", "person-a");
        var other = Entry("tenant-a", "person-a");
        var otherPerson = Entry("tenant-a", "person-b");
        var otherTenant = Entry("tenant-b", "person-a");
        foreach (var entry in new[] { target, other, otherPerson, otherTenant }) await store.AppendAsync(entry);
        await store.DismissAsync("tenant-a", "person-a", target.Id);
        await store.DismissAsync("tenant-a", "person-a", target.Id); // idempotent
        var listed = await store.ListAsync("tenant-a", "person-a");
        Assert.Equal(InboxReadState.Dismissed, listed.Single(entry => entry.Id == target.Id).ReadState);
        Assert.Equal(InboxReadState.Unread, listed.Single(entry => entry.Id == other.Id).ReadState);
        Assert.Equal(1, await store.UnreadCountAsync("tenant-a", "person-a"));
        Assert.Equal(otherPerson, Assert.Single(await store.ListAsync("tenant-a", "person-b")));
        Assert.Equal(otherTenant, Assert.Single(await store.ListAsync("tenant-b", "person-a")));
        await store.MarkAllReadAsync("tenant-a", "person-a");
        Assert.Equal(InboxReadState.Dismissed, (await store.ListAsync("tenant-a", "person-a")).Single(entry => entry.Id == target.Id).ReadState);
    }

    // Oracle: ck-7 / ck-10 tenant data belongs to one person; wrong scope and absent ids refuse identically.
    // Mutations dismiss-tenant / dismiss-recipient: remove the corresponding scope predicate.
    [Theory(DisplayName = "notification-delivery-ck-7: dismissal refuses unknown and foreign deliveries")]
    [InlineData("tenant-b", "person-a", false)]
    [InlineData("tenant-a", "person-b", false)]
    [InlineData("tenant-a", "person-a", true)]
    public async Task Dismiss_refuses_foreign_or_unknown(string tenant, string recipient, bool missing)
    {
        IInboxStore store = new InMemoryInboxStore();
        var entry = Entry("tenant-a", "person-a");
        await store.AppendAsync(entry);
        var id = missing ? Guid.NewGuid() : entry.Id;
        var refusal = await Assert.ThrowsAsync<NotificationDeliveryRefusedException>(() => store.DismissAsync(tenant, recipient, id));
        Assert.Equal("notification_delivery.inbox_entry_unknown", refusal.Code);
        Assert.Equal($"Inbox entry '{id}' is not in this recipient's inbox.", refusal.Message);
        Assert.Equal(entry, Assert.Single(await store.ListAsync("tenant-a", "person-a")));
    }

    // Oracle: IInboxStore's cancellation contract prevents a cancelled mutation.
    // Mutation dismiss-cancellation: remove ThrowIfCancellationRequested in DismissAsync.
    [Fact(DisplayName = "notification-delivery-ck-7: cancelled dismissal leaves delivery unchanged")]
    public async Task Cancelled_dismissal_leaves_delivery_unchanged()
    {
        IInboxStore store = new InMemoryInboxStore();
        var entry = Entry("tenant-a", "person-a");
        await store.AppendAsync(entry);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var refusal = await Assert.ThrowsAsync<OperationCanceledException>(() => store.DismissAsync("tenant-a", "person-a", entry.Id, cancellation.Token));
        Assert.Equal(cancellation.Token, refusal.CancellationToken);
        Assert.Equal(entry, Assert.Single(await store.ListAsync("tenant-a", "person-a")));
    }

    private static DeliveryChannel Channel(Func<DeliveryTransportResult, DeliveryOutcomeClass> classifier) =>
        new(DeliveryChannelKey.Inbox, new HashSet<string> { "credential" }, new HashSet<string> { "retry" }, ChannelContentClass.LinkOnly, classifier);

    private static InboxEntry Entry(string tenant, string recipient) => new(Guid.NewGuid(), tenant, recipient,
        "information", "Title", "Body", "Subject", "/subject", false, DateTimeOffset.UnixEpoch, InboxReadState.Unread);

    private sealed class FailingStore : IInboxStore
    {
        public Task<InboxEntry> AppendAsync(InboxEntry entry, CancellationToken cancellationToken = default) => throw new InvalidOperationException("store defect verbatim\r\n");
        public Task<IReadOnlyList<InboxEntry>> ListAsync(string tenant, string recipient, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> UnreadCountAsync(string tenant, string recipient, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkReadAsync(string tenant, string recipient, Guid entryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DismissAsync(string tenant, string recipient, Guid entryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkAllReadAsync(string tenant, string recipient, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
