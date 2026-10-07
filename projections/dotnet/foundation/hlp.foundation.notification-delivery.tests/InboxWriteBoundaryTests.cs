using Harborline.Foundation.NotificationDelivery;
using Xunit;

namespace Harborline.Foundation.NotificationDelivery.Tests;

public sealed class InboxWriteBoundaryTests
{
    private static readonly DateTimeOffset WrittenAt = new(2026, 9, 30, 12, 34, 56, TimeSpan.Zero);

    public static IEnumerable<object[]> InvalidRequiredContent()
    {
        foreach (var field in new[] { "Tenant", "Recipient", "Tier", "Title", "RenderedContent", "SubjectDisplayName", "SubjectLink" })
        {
            foreach (var value in new string?[] { null, string.Empty, " \t\r\n " })
            {
                yield return new object[] { field, value! };
            }
        }
    }

    // DES-0055 notification-delivery-ck-10 defines the inbox content role. Required fields and
    // exact diagnostic wording below pin the local API compatibility contract.
    // The recording store is independent of InMemoryInboxStore's validation and query behavior.
    [Theory]
    [MemberData(nameof(InvalidRequiredContent))]
    public async Task Notification_delivery_ck_10_missing_required_content_refuses_before_any_append(string field, string? value)
    {
        var store = new RecordingInboxStore();
        var channel = new InboxChannel(store, new FixedClock());
        var valid = Request(answerOwed: false);
        var invalid = field switch
        {
            "Tenant" => valid with { Tenant = value! },
            "Recipient" => valid with { Recipient = value! },
            "Tier" => valid with { Tier = value! },
            "Title" => valid with { Title = value! },
            "RenderedContent" => valid with { RenderedContent = value! },
            "SubjectDisplayName" => valid with { SubjectDisplayName = value! },
            "SubjectLink" => valid with { SubjectLink = value! },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        var exception = await Assert.ThrowsAsync<NotificationDeliveryRefusedException>(() => channel.WriteAsync(invalid));

        Assert.Equal("notification_delivery.inbox_entry_invalid", exception.Code);
        Assert.Equal($"Inbox entry field '{field}' is required.", exception.Message);
        Assert.Equal(0, store.AppendCalls);
        Assert.Null(store.Appended);
    }

    [Fact]
    public async Task Notification_delivery_ck_10_null_request_refuses_before_any_append()
    {
        var store = new RecordingInboxStore();
        var channel = new InboxChannel(store, new FixedClock());

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => channel.WriteAsync(null!));

        Assert.Equal("request", exception.ParamName);
        Assert.Equal(0, store.AppendCalls);
        Assert.Null(store.Appended);
    }

    // DES-0055 notification-delivery-ck-7 and ck-10: full on-node content, unread state, answer owed and time.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Notification_delivery_ck_10_write_preserves_all_content_and_stamps_a_new_unread_entry(bool answerOwed)
    {
        var store = new RecordingInboxStore();
        var channel = new InboxChannel(store, new FixedClock());
        using var cancellation = new CancellationTokenSource();

        var written = await channel.WriteAsync(Request(answerOwed), cancellation.Token);

        Assert.Equal(1, store.AppendCalls);
        Assert.Same(store.Appended, written);
        Assert.NotEqual(Guid.Empty, written.Id);
        Assert.Equal("tenant-a", written.Tenant);
        Assert.Equal("principal-a", written.Recipient);
        Assert.Equal("decision", written.Tier);
        Assert.Equal("Approval required", written.Title);
        Assert.Equal("The full rendered message stays on the node.", written.RenderedContent);
        Assert.Equal("Record 17", written.SubjectDisplayName);
        Assert.Equal("/records/17", written.SubjectLink);
        Assert.Equal(answerOwed, written.AnswerOwed);
        Assert.Equal(WrittenAt, written.CreatedAt);
        Assert.Equal(InboxReadState.Unread, written.ReadState);
        Assert.Equal(cancellation.Token, store.AppendedToken);

        var second = await channel.WriteAsync(Request(answerOwed));
        Assert.NotEqual(Guid.Empty, second.Id);
        Assert.NotEqual(written.Id, second.Id);
        Assert.Equal(2, store.AppendCalls);
    }

    [Fact]
    public async Task Notification_delivery_ck_10_write_waits_for_commit_and_returns_the_stores_committed_entry()
    {
        var completion = new TaskCompletionSource<InboxEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingInboxStore { AppendResult = _ => completion.Task };
        var channel = new InboxChannel(store, new FixedClock());

        var writing = channel.WriteAsync(Request(answerOwed: true));

        Assert.Equal(1, store.AppendCalls);
        Assert.False(writing.IsCompleted);
        var committed = new InboxEntry(Guid.Parse("00000000-0000-0000-0000-000000000017"),
            "tenant-a", "principal-a", "decision", "Committed title", "Committed rendered content",
            "Record 17", "/records/17", true, WrittenAt, InboxReadState.Unread);
        completion.SetResult(committed);

        Assert.Same(committed, await writing);
    }

    [Fact]
    public async Task Notification_delivery_ck_10_persistence_failure_propagates_without_returning_an_uncommitted_entry()
    {
        var refusal = new InvalidOperationException("Store commit refused");
        var store = new RecordingInboxStore { AppendResult = _ => Task.FromException<InboxEntry>(refusal) };
        var channel = new InboxChannel(store, new FixedClock());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => channel.WriteAsync(Request(answerOwed: true)));

        Assert.Same(refusal, exception);
        Assert.Equal(1, store.AppendCalls);
    }

    [Fact]
    public async Task Notification_delivery_ck_10_cancelled_write_never_persists_an_entry()
    {
        var store = new InMemoryInboxStore();
        var channel = new InboxChannel(store, new FixedClock());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => channel.WriteAsync(Request(false), cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Empty(await store.ListAsync("tenant-a", "principal-a"));
    }

    private static WriteInboxEntry Request(bool answerOwed) => new("tenant-a", "principal-a", "decision",
        "Approval required", "The full rendered message stays on the node.", "Record 17", "/records/17", answerOwed);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => WrittenAt;
    }

    private sealed class RecordingInboxStore : IInboxStore
    {
        public int AppendCalls { get; private set; }
        public InboxEntry? Appended { get; private set; }
        public CancellationToken AppendedToken { get; private set; }
        public Func<InboxEntry, Task<InboxEntry>> AppendResult { get; init; } = Task.FromResult;

        public Task<InboxEntry> AppendAsync(InboxEntry entry, CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            Appended = entry;
            AppendedToken = cancellationToken;
            return AppendResult(entry);
        }

        public Task<IReadOnlyList<InboxEntry>> ListAsync(string tenant, string recipient, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<int> UnreadCountAsync(string tenant, string recipient, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task MarkReadAsync(string tenant, string recipient, Guid entryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task MarkAllReadAsync(string tenant, string recipient, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
