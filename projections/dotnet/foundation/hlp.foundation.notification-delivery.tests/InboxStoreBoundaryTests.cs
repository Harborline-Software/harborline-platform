using Harborline.Foundation.NotificationDelivery;
using Xunit;

namespace Harborline.Foundation.NotificationDelivery.Tests;

public sealed class InboxStoreBoundaryTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    // DES-0055 notification-delivery-ck-10 and notification-delivery-cc-2: durable tenant data, per person.
    [Fact]
    public async Task Notification_delivery_cc_2_list_isolates_both_tenant_and_recipient()
    {
        var (store, entries) = await MixedInboxAsync();

        var listed = await store.ListAsync("tenant-a", "principal-a");

        Assert.Equal(new[] { Id(4), Id(3), Id(2), Id(1) }, listed.Select(entry => entry.Id));
        Assert.Equal(entries.Where(entry => entry.Id == Id(4) || entry.Id == Id(3) || entry.Id == Id(2) || entry.Id == Id(1))
            .Reverse(), listed);
        Assert.Empty(await store.ListAsync("tenant-missing", "principal-a"));
        Assert.Empty(await store.ListAsync("tenant-a", "principal-missing"));
    }

    [Fact]
    public async Task Notification_delivery_cc_2_unread_count_excludes_read_dismissed_and_other_inboxes()
    {
        var (store, _) = await MixedInboxAsync();

        Assert.Equal(2, await store.UnreadCountAsync("tenant-a", "principal-a"));
        Assert.Equal(1, await store.UnreadCountAsync("tenant-a", "principal-b"));
        Assert.Equal(1, await store.UnreadCountAsync("tenant-b", "principal-a"));
        Assert.Equal(0, await store.UnreadCountAsync("tenant-missing", "principal-a"));
        Assert.Equal(0, await store.UnreadCountAsync("tenant-a", "principal-missing"));
    }

    [Fact]
    public async Task Notification_delivery_cc_2_entries_are_newest_first_even_when_appended_out_of_order()
    {
        var store = new InMemoryInboxStore();
        await store.AppendAsync(Entry(1, "tenant-a", "principal-a", InboxReadState.Unread, Epoch));
        await store.AppendAsync(Entry(3, "tenant-a", "principal-a", InboxReadState.Read, Epoch.AddMinutes(2)));
        await store.AppendAsync(Entry(2, "tenant-a", "principal-a", InboxReadState.Unread, Epoch.AddMinutes(1)));

        Assert.Equal(new[] { Id(3), Id(2), Id(1) }, (await store.ListAsync("tenant-a", "principal-a")).Select(entry => entry.Id));
    }

    // Local compatibility regression: DES-0055 cc-2 specifies newest-first, but leaves ties unspecified.
    [Fact]
    public async Task Inbox_equal_timestamp_entries_use_ascending_id_as_a_repeatable_tiebreak()
    {
        var store = new InMemoryInboxStore();
        await store.AppendAsync(Entry(3, "tenant-a", "principal-a", InboxReadState.Unread, Epoch));
        await store.AppendAsync(Entry(1, "tenant-a", "principal-a", InboxReadState.Unread, Epoch));
        await store.AppendAsync(Entry(2, "tenant-a", "principal-a", InboxReadState.Unread, Epoch));

        Assert.Equal(new[] { Id(1), Id(2), Id(3) }, (await store.ListAsync("tenant-a", "principal-a")).Select(entry => entry.Id));
    }

    // DES-0055 notification-delivery-ck-7: reading one item changes only that person's item.
    [Fact]
    public async Task Notification_delivery_ck_7_mark_read_changes_only_the_named_entry_and_is_idempotent()
    {
        var (store, entries) = await MixedInboxAsync();

        await store.MarkReadAsync("tenant-a", "principal-a", Id(1));
        await store.MarkReadAsync("tenant-a", "principal-a", Id(1));

        Assert.Equal(1, await store.UnreadCountAsync("tenant-a", "principal-a"));
        foreach (var original in entries)
        {
            var actual = Assert.Single((await store.ListAsync(original.Tenant, original.Recipient)), entry => entry.Id == original.Id);
            Assert.Equal(original.Id == Id(1) ? original with { ReadState = InboxReadState.Read } : original, actual);
        }
    }

    [Theory]
    [InlineData("tenant-b", "principal-a", 1)]
    [InlineData("tenant-a", "principal-b", 1)]
    [InlineData("tenant-b", "principal-b", 1)]
    [InlineData("Tenant-a", "principal-a", 1)]
    [InlineData("tenant-a", "Principal-a", 1)]
    [InlineData("tenant-a", "principal-a", 99)]
    public async Task Notification_delivery_ck_7_wrong_scope_or_missing_entry_refuses_without_changing_any_inbox(
        string tenant, string recipient, int entryNumber)
    {
        var (store, entries) = await MixedInboxAsync();

        var exception = await Assert.ThrowsAsync<NotificationDeliveryRefusedException>(
            () => store.MarkReadAsync(tenant, recipient, Id(entryNumber)));

        Assert.Equal("notification_delivery.inbox_entry_unknown", exception.Code);
        Assert.Equal($"Inbox entry '{Id(entryNumber)}' is not in this recipient's inbox.", exception.Message);
        await AssertUnchangedAsync(store, entries);
    }

    [Fact]
    public async Task Notification_delivery_ck_7_mark_all_read_changes_only_scoped_unread_entries_and_is_idempotent()
    {
        // IInboxStore.MarkAllReadAsync promises unread-only updates; dismissed entries stay dismissed.
        var (store, entries) = await MixedInboxAsync();

        await store.MarkAllReadAsync("tenant-a", "principal-a");
        await store.MarkAllReadAsync("tenant-a", "principal-a");

        Assert.Equal(0, await store.UnreadCountAsync("tenant-a", "principal-a"));
        Assert.Equal(1, await store.UnreadCountAsync("tenant-a", "principal-b"));
        Assert.Equal(1, await store.UnreadCountAsync("tenant-b", "principal-a"));
        foreach (var original in entries)
        {
            var actual = Assert.Single((await store.ListAsync(original.Tenant, original.Recipient)), entry => entry.Id == original.Id);
            Assert.Equal(original.Id == Id(1) || original.Id == Id(2)
                ? original with { ReadState = InboxReadState.Read }
                : original, actual);
        }
    }

    [Fact]
    public async Task Notification_delivery_ck_7_mark_all_read_of_missing_scope_leaves_all_inboxes_unchanged()
    {
        var (store, entries) = await MixedInboxAsync();

        await store.MarkAllReadAsync("tenant-missing", "principal-a");
        await store.MarkAllReadAsync("tenant-a", "principal-missing");

        await AssertUnchangedAsync(store, entries);
    }

    [Fact]
    public async Task Notification_delivery_ck_10_append_rejects_null_and_duplicate_ids_without_losing_the_first_entry()
    {
        var store = new InMemoryInboxStore();
        var nullRefusal = await Assert.ThrowsAsync<ArgumentNullException>(() => store.AppendAsync(null!));
        Assert.Equal("entry", nullRefusal.ParamName);

        var first = Entry(1, "tenant-a", "principal-a", InboxReadState.Unread, Epoch);
        Assert.Equal(first, await store.AppendAsync(first));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendAsync(first with { Title = "Replacement must refuse" }));

        Assert.Equal(first, Assert.Single(await store.ListAsync("tenant-a", "principal-a")));
    }

    [Theory]
    [InlineData("append")]
    [InlineData("list")]
    [InlineData("unread-count")]
    [InlineData("mark-read")]
    [InlineData("mark-all-read")]
    public async Task Inbox_cancelled_operation_throws_without_changing_any_inbox(string operation)
    {
        var (store, entries) = await MixedInboxAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => operation switch
        {
            "append" => store.AppendAsync(Entry(99, "tenant-a", "principal-a", InboxReadState.Unread, Epoch), cancelled.Token),
            "list" => store.ListAsync("tenant-a", "principal-a", cancelled.Token),
            "unread-count" => store.UnreadCountAsync("tenant-a", "principal-a", cancelled.Token),
            "mark-read" => store.MarkReadAsync("tenant-a", "principal-a", Id(1), cancelled.Token),
            "mark-all-read" => store.MarkAllReadAsync("tenant-a", "principal-a", cancelled.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        });

        Assert.Equal(cancelled.Token, exception.CancellationToken);
        await AssertUnchangedAsync(store, entries);
    }

    private static async Task AssertUnchangedAsync(InMemoryInboxStore store, IReadOnlyList<InboxEntry> entries)
    {
        foreach (var scope in entries.Select(entry => (entry.Tenant, entry.Recipient)).Distinct())
        {
            var expected = entries.Where(entry => entry.Tenant == scope.Tenant && entry.Recipient == scope.Recipient);
            var actual = await store.ListAsync(scope.Tenant, scope.Recipient);
            Assert.Equal(expected.OrderByDescending(entry => entry.CreatedAt).ThenBy(entry => entry.Id), actual);
        }
    }

    private static async Task<(InMemoryInboxStore Store, InboxEntry[] Entries)> MixedInboxAsync()
    {
        var entries = new[]
        {
            Entry(1, "tenant-a", "principal-a", InboxReadState.Unread, Epoch),
            Entry(2, "tenant-a", "principal-a", InboxReadState.Unread, Epoch.AddMinutes(1)),
            Entry(3, "tenant-a", "principal-a", InboxReadState.Read, Epoch.AddMinutes(2)),
            Entry(4, "tenant-a", "principal-a", InboxReadState.Dismissed, Epoch.AddMinutes(3)),
            Entry(5, "tenant-a", "principal-b", InboxReadState.Unread, Epoch.AddMinutes(4)),
            Entry(6, "tenant-b", "principal-a", InboxReadState.Unread, Epoch.AddMinutes(5)),
            Entry(7, "tenant-b", "principal-b", InboxReadState.Unread, Epoch.AddMinutes(6)),
            Entry(8, "Tenant-a", "principal-a", InboxReadState.Unread, Epoch.AddMinutes(7)),
            Entry(9, "tenant-a", "Principal-a", InboxReadState.Unread, Epoch.AddMinutes(8)),
        };
        var store = new InMemoryInboxStore();
        foreach (var entry in entries) await store.AppendAsync(entry);
        return (store, entries);
    }

    private static InboxEntry Entry(int number, string tenant, string recipient, InboxReadState state, DateTimeOffset createdAt) =>
        new(Id(number), tenant, recipient, "information", $"Entry {number}", $"Full content {number}",
            $"Record {number}", $"/records/{number}", false, createdAt, state);

    private static Guid Id(int number) => Guid.Parse(FormattableString.Invariant($"00000000-0000-0000-0000-{number:000000000000}"));
}
