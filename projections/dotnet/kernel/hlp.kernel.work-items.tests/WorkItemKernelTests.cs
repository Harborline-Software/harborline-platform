using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.MultiTenancy;
using System.Text.Json;
using Xunit;

namespace Harborline.Kernel.WorkItems.Tests;

public sealed class WorkItemKernelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedLifecycleFixture_PassesInMemoryAndFileJournal(bool durable)
    {
        using var fixtures = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures.yaml")));
        Assert.Equal("hlp.kernel.work-items", fixtures.RootElement.GetProperty("moduleId").GetString());
        Assert.Equal(6, fixtures.RootElement.GetProperty("cases").GetArrayLength());
        var path = TempPath();
        IWorkItemStore store = durable ? new FileJournalWorkItemStore(path) : new InMemoryWorkItemStore();
        try
        {
            var kernel = Kernel("tenant-a", "actor-a", store);
            var created = await kernel.CreateAsync(Create("item-shared"));
            var replayed = await kernel.CreateAsync(Create("item-shared"));
            var transitioned = await kernel.TransitionAsync(Transition("item-shared", 1, "approve"));
            Assert.Equal(WorkItemMutationDisposition.Committed, created.Disposition);
            Assert.Equal(WorkItemMutationDisposition.Replayed, replayed.Disposition);
            Assert.Equal(WorkItemMutationDisposition.Committed, transitioned.Disposition);
            Assert.Null(await Kernel("tenant-b", "actor-b", store).GetAsync("item-shared"));
        }
        finally
        {
            if (store is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Create_CommitsSnapshotEventAuditOutbox_AndExactReplay()
    {
        var store = new InMemoryWorkItemStore();
        var kernel = Kernel("tenant-a", "actor-a", store);
        var request = Create("item-1");

        var first = await kernel.CreateAsync(request);
        var replay = await kernel.CreateAsync(request);

        Assert.Equal(WorkItemMutationDisposition.Committed, first.Disposition);
        Assert.Equal(WorkItemMutationDisposition.Replayed, replay.Disposition);
        Assert.Equal(first.Snapshot, replay.Snapshot);
        Assert.Single(await store.ReadEventsAsync("tenant-a", "item-1"));
        var audit = Assert.Single(await store.ReadAuditAsync("tenant-a", "item-1"));
        Assert.Equal("11111111-1111-1111-1111-111111111111", audit.ActorId);
        Assert.Single(await store.ReadOutboxAsync("tenant-a"));
    }

    [Fact]
    public async Task ReusingKeyForDifferentCreateContent_ConflictsWithoutMutation()
    {
        var store = new InMemoryWorkItemStore();
        var kernel = Kernel("tenant-a", "actor-a", store);
        await kernel.CreateAsync(Create("item-1"));

        var conflict = await kernel.CreateAsync(Create("item-2"));

        Assert.Equal(WorkItemMutationDisposition.IdempotencyConflict, conflict.Disposition);
        Assert.Null(await kernel.GetAsync("item-2"));
    }

    [Fact]
    public async Task CanonicalFingerprint_ReplaysReorderedObjectsAndOutcomeSet()
    {
        var store = new InMemoryWorkItemStore();
        var kernel = Kernel("tenant-a", "actor-a", store);
        var first = Create("item-1") with {StateJson = "{\"a\":1,\"b\":2}"};
        var reordered = first with
        {
            StateJson = "{\"b\":2,\"a\":1}",
            AllowedOutcomes = first.AllowedOutcomes.Reverse().ToArray(),
        };

        await kernel.CreateAsync(first);
        var replay = await kernel.CreateAsync(reordered);

        Assert.Equal(WorkItemMutationDisposition.Replayed, replay.Disposition);
    }

    [Fact]
    public async Task UnknownAndForeignTenantItems_AreIndistinguishable()
    {
        var store = new InMemoryWorkItemStore();
        await Kernel("tenant-a", "actor-a", store).CreateAsync(Create("item-1"));
        var tenantB = Kernel("tenant-b", "actor-b", store);

        Assert.Null(await tenantB.GetAsync("item-1"));
        Assert.Null(await tenantB.GetAsync("missing"));
        Assert.Empty(await tenantB.ListOpenAsync());
        var result = await tenantB.TransitionAsync(Transition("item-1", 1, "approve"));
        Assert.Equal(WorkItemMutationDisposition.NotFound, result.Disposition);
    }

    [Fact]
    public async Task InactiveTenant_IsDeniedWithoutExistenceOracle()
    {
        var store = new InMemoryWorkItemStore();
        var context = new TestContext("tenant-a", "actor-a", TenantStatus.Suspended);
        var kernel = new WorkItemKernel(context, new TestPartyContext(), store);

        Assert.Equal(WorkItemMutationDisposition.Denied, (await kernel.CreateAsync(Create("item-1"))).Disposition);
        Assert.Null(await kernel.GetAsync("item-1"));
        Assert.Empty(await kernel.ListOpenAsync());
    }

    [Fact]
    public async Task Transition_IsVersionGuardedAndLoopBackIterationIsDurable()
    {
        var store = new InMemoryWorkItemStore();
        var kernel = Kernel("tenant-a", "actor-a", store);
        await kernel.CreateAsync(Create("item-1"));

        var stale = await kernel.TransitionAsync(Transition("item-1", 2, "approve"));
        var committed = await kernel.TransitionAsync(Transition("item-1", 1, "rework"));

        Assert.Equal(WorkItemMutationDisposition.VersionConflict, stale.Disposition);
        Assert.Equal(WorkItemMutationDisposition.Committed, committed.Disposition);
        Assert.Equal(1, committed.Snapshot!.Iteration);
        Assert.Equal(2, committed.Snapshot.Version);
        Assert.StartsWith("wi1:", WorkItemKernel.DeriveStepKey("tenant-a", committed.Snapshot));
    }

    [Fact]
    public async Task TransitionReplay_ReturnsOriginalReceiptAndDoesNotDuplicateOutbox()
    {
        var store = new InMemoryWorkItemStore();
        var kernel = Kernel("tenant-a", "actor-a", store);
        await kernel.CreateAsync(Create("item-1"));
        var request = Transition("item-1", 1, "approve");

        var first = await kernel.TransitionAsync(request);
        var replay = await kernel.TransitionAsync(request);

        Assert.Equal(WorkItemMutationDisposition.Committed, first.Disposition);
        Assert.Equal(WorkItemMutationDisposition.Replayed, replay.Disposition);
        Assert.Equal(2, (await store.ReadEventsAsync("tenant-a", "item-1")).Count);
        Assert.Equal(2, (await store.ReadOutboxAsync("tenant-a")).Count);
    }

    [Fact]
    public async Task TransitionKeyWithDifferentOutcome_ConflictsWithoutSecondEvent()
    {
        var store = new InMemoryWorkItemStore();
        var kernel = Kernel("tenant-a", "actor-a", store);
        await kernel.CreateAsync(Create("item-1"));
        await kernel.TransitionAsync(Transition("item-1", 1, "approve"));

        var conflict = await kernel.TransitionAsync(Transition("item-1", 1, "rework"));

        Assert.Equal(WorkItemMutationDisposition.IdempotencyConflict, conflict.Disposition);
        Assert.Equal(2, (await store.ReadEventsAsync("tenant-a", "item-1")).Count);
    }

    [Fact]
    public async Task ConcurrentExpectedVersionWriters_CommitExactlyOnce()
    {
        var store = new InMemoryWorkItemStore();
        var kernel = Kernel("tenant-a", "actor-a", store);
        await kernel.CreateAsync(Create("item-1"));
        var left = Transition("item-1", 1, "approve") with {IdempotencyKey = "left"};
        var right = Transition("item-1", 1, "rework") with {IdempotencyKey = "right"};

        var results = await Task.WhenAll(kernel.TransitionAsync(left), kernel.TransitionAsync(right));

        Assert.Single(results, result => result.Disposition == WorkItemMutationDisposition.Committed);
        Assert.Single(results, result => result.Disposition == WorkItemMutationDisposition.VersionConflict);
        Assert.Equal(2, (await store.ReadEventsAsync("tenant-a", "item-1")).Count);
    }

    [Fact]
    public async Task StructuredStepKey_IsStableAndDelimiterCollisionResistant()
    {
        var store = new InMemoryWorkItemStore();
        var firstKernel = Kernel("tenant:a", "actor-a", store);
        var secondKernel = Kernel("tenant", "actor-a", store);
        var first = (await firstKernel.CreateAsync(Create("item") with {IdempotencyKey = "one"})).Snapshot!;
        var second = (await secondKernel.CreateAsync(Create("a:item") with {IdempotencyKey = "two"})).Snapshot!;

        var firstKey = WorkItemKernel.DeriveStepKey("tenant:a", first);
        Assert.Equal(firstKey, WorkItemKernel.DeriveStepKey("tenant:a", first));
        Assert.NotEqual(firstKey, WorkItemKernel.DeriveStepKey("tenant", second));
    }

    [Fact]
    public async Task TerminalItem_CannotTransitionAgain()
    {
        var store = new InMemoryWorkItemStore();
        var kernel = Kernel("tenant-a", "actor-a", store);
        await kernel.CreateAsync(Create("item-1"));
        await kernel.TransitionAsync(Transition("item-1", 1, "approve"));

        var result = await kernel.TransitionAsync(Transition("item-1", 2, "approve") with {IdempotencyKey = "transition-2"});

        Assert.Equal(WorkItemMutationDisposition.InvalidTransition, result.Disposition);
    }

    [Fact]
    public async Task FileJournal_RestartRecoversParkAndResumeState()
    {
        var path = TempPath();
        try
        {
            await using (var store = new FileJournalWorkItemStore(path))
            {
                var kernel = Kernel("tenant-a", "actor-a", store);
                await kernel.CreateAsync(Create("item-1"));
                await kernel.TransitionAsync(Transition("item-1", 1, "rework"));
            }

            await using var recovered = new FileJournalWorkItemStore(path);
            var snapshot = await Kernel("tenant-a", "actor-a", recovered).GetAsync("item-1");
            Assert.NotNull(snapshot);
            Assert.Equal("inspect", snapshot.CurrentStep);
            Assert.Equal(1, snapshot.Iteration);
            Assert.Equal(2, (await recovered.ReadEventsAsync("tenant-a", "item-1")).Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FileJournal_TruncatesOnlyIncompleteFinalFrame()
    {
        var path = TempPath();
        try
        {
            await using (var store = new FileJournalWorkItemStore(path))
                await Kernel("tenant-a", "actor-a", store).CreateAsync(Create("item-1"));
            var validLength = new FileInfo(path).Length;
            await File.AppendAllTextAsync(path, "HLWI1 100 deadbeef\npartial");

            await using var recovered = new FileJournalWorkItemStore(path);
            Assert.NotNull(await Kernel("tenant-a", "actor-a", recovered).GetAsync("item-1"));
            Assert.Equal(validLength, new FileInfo(path).Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FileJournal_CompleteFrameCorruptionFailsStartup()
    {
        var path = TempPath();
        try
        {
            await using (var store = new FileJournalWorkItemStore(path))
                await Kernel("tenant-a", "actor-a", store).CreateAsync(Create("item-1"));
            var bytes = await File.ReadAllBytesAsync(path);
            bytes[^2] ^= 0x01;
            await File.WriteAllBytesAsync(path, bytes);

            Assert.Throws<InvalidDataException>(() => new FileJournalWorkItemStore(path));
        }
        finally { File.Delete(path); }
    }

    private static WorkItemKernel Kernel(string tenantId, string actorId, IWorkItemStore store) =>
        new(new TestContext(tenantId, actorId, TenantStatus.Active), new TestPartyContext(), store, new FixedTimeProvider());

    private static CreateWorkItemRequest Create(string id) => new()
    {
        Id = id,
        SubjectRef = "inspection-submission:42",
        DefinitionKey = "inspection-review",
        DefinitionVersion = "1",
        InitialStep = "review",
        InitialStatus = WorkItemStatus.Parked,
        StateJson = "{\"condition\":\"attention\"}",
        BasisJson = "{\"score\":7}",
        AllowedOutcomes =
        [
            new("approve", "review", "done", WorkItemStatus.Completed),
            new("rework", "review", "inspect", WorkItemStatus.Parked, IsLoopBack: true),
        ],
        IdempotencyKey = "create-1",
    };

    private static TransitionWorkItemRequest Transition(string id, long version, string action) => new()
    {
        Id = id,
        ExpectedVersion = version,
        IdempotencyKey = "transition-1",
        OutcomeId = action,
        ResultJson = "{\"accepted\":true}",
    };

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"hl-work-items-{Guid.NewGuid():N}.journal");

    private sealed class TestContext(string tenantId, string userId, TenantStatus status) : IAuthenticatedActorContext
    {
        public string UserId { get; } = userId;
        public IReadOnlyList<string> Roles { get; } = ["Inspector"];
        public TenantMetadata? Tenant { get; } = new() {Id = new TenantId(tenantId), Name = tenantId, Status = status};
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class TestPartyContext : IPartyContext
    {
        public ValueTask<Guid> GetCurrentPartyIdAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }
}
