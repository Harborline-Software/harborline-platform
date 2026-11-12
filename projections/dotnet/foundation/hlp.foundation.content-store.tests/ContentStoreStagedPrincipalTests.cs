using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Xunit;
using System.Reflection;
using Harborline.Kernel.Core;

namespace Harborline.Foundation.ContentStore.Tests;

public sealed class ContentStoreStagedPrincipalTests
{
    [Theory]
    [InlineData("tenant-a", "another-principal")]
    [InlineData("tenant-a", "sys.records-worker")]
    [InlineData("tenant-b", "uploader")]
    public async Task Another_principal_or_tenant_gets_staging_expired_and_leaves_the_stage_untouched(string tenant, string actor)
    {
        // ADR-0142 decision 3: a stage is bound to the authenticated put actor and tenant.
        var storage = new InMemoryContentStoreStorage();
        var store = Store(storage);
        var stage = await store.PutAsync(new("tenant-a"), new MemoryStream("evidence"u8.ToArray()), "text/plain", null,
            ContentStoreWriteEvidence.New("uploader"));
        var before = Assert.Single(await storage.Bookkeeping.StagedCreatedAtOrBeforeAsync(DateTimeOffset.MaxValue));

        var refusal = await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(
            new(tenant), stage, new("record", "evidence"), "evidence.txt", ContentStoreWriteEvidence.New(actor)));

        Assert.Equal("content.staging-expired", refusal.Code);
        Assert.Equal("content-store", refusal.Definition);
        Assert.Empty(refusal.Detail);
        Assert.Equal(before, Assert.Single(await storage.Bookkeeping.StagedCreatedAtOrBeforeAsync(DateTimeOffset.MaxValue)));
        Assert.Equal(0, store.CommittedCount);
        // The original principal can still commit: the rejected attempt neither consumes nor destroys the stage.
        var reference = await store.CommitReferenceAsync(new("tenant-a"), stage, new("record", "evidence"), "evidence.txt",
            ContentStoreWriteEvidence.New("uploader"));
        Assert.Equal(8, reference.Length);
        var delivery = await store.ReadAsync(new("tenant-a"), reference, true, ContentStoreWriteEvidence.Reader);
        using (delivery.Body)
        using (var body = new StreamReader(delivery.Body))
            Assert.Equal("evidence", await body.ReadToEndAsync());
        Assert.Equal(0, store.StagedCount);
        Assert.Equal(1, store.CommittedCount);
    }

    [Fact]
    public async Task An_originator_bound_write_keeps_the_worker_as_content_audit_actor()
    {
        var storage = new InMemoryContentStoreStorage();
        var store = Store(storage);
        var stage = await store.PutAsync(new("tenant-a"), new MemoryStream("evidence"u8.ToArray()), "text/plain", null,
            ContentStoreWriteEvidence.New("alice"));
        await store.CommitReferenceAsync(new("tenant-a"), stage, new("record", "evidence"), "evidence.txt",
            ContentStoreWriteEvidence.New("sys.records-worker") with { Originator = "alice" });
        var audit = ((InMemoryContentBookkeeping)storage.Bookkeeping).Snapshot().Audits[^1];
        Assert.Equal("sys.records-worker", audit.ActorId);
        Assert.Equal("alice", audit.OriginatorId);
        Assert.Equal(0, store.StagedCount);
        Assert.Equal(1, store.CommittedCount);
    }

    [Fact]
    public async Task Committed_stage_is_single_use_even_for_the_same_principal()
    {
        var store = Store(new());
        var stage = await store.PutAsync(new("tenant-a"), new MemoryStream("evidence"u8.ToArray()), "text/plain", null,
            ContentStoreWriteEvidence.New("uploader"));
        await store.CommitReferenceAsync(new("tenant-a"), stage, new("record", "evidence"), "one.txt", ContentStoreWriteEvidence.New("uploader"));
        var error = await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(new("tenant-a"),
            stage, new("another-record", "evidence"), "two.txt", ContentStoreWriteEvidence.New("uploader")));
        Assert.Equal("content.staging-expired", error.Code);
        Assert.Equal(1, store.CommittedCount);
        Assert.Equal(0, store.StagedCount);
    }

    [Fact]
    public void Field_admission_metadata_publishes_the_measured_limit_and_stable_closed_allowlist()
    {
        // DES-0054 ck-5/ck-7: these are host/store facts, not a second field-owned allowlist.
        Assert.Equal(100, Store(new()).ItemLimitBytes);
        Assert.Equal(["application/pdf", "image/jpeg", "image/png", "image/webp", "text/csv", "text/plain"], InMemoryContentStore.SupportedMediaTypes);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)InMemoryContentStore.SupportedMediaTypes)[0] = "application/exe");
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("command")]
    [InlineData("fingerprint")]
    public async Task A_stage_commit_requires_valid_kernel_evidence_before_stage_lookup(string missing)
    {
        // The full boundary requires an identified command and actor even when the stage does not exist.
        var evidence = ContentStoreWriteEvidence.New("uploader");
        evidence = missing switch
        {
            "actor" => evidence with { Audit = evidence.Audit with { ActorId = "" } },
            "command" => evidence with { Operation = evidence.Operation with { CommandId = "" } },
            _ => evidence with { Operation = evidence.Operation with { Fingerprint = "" } },
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await Store(new()).CommitReferenceAsync(new("tenant-a"),
            Guid.Parse("11111111-1111-1111-1111-111111111111"), new("record", "evidence"), "file.txt", evidence));
    }

    [Fact]
    public async Task A_staged_display_name_starting_with_a_percent_hex_pair_is_served_safely()
    {
        // RFC 6266/8187, DES-0054 eng-7/Q33: percent-hex is escaped even at offset zero.
        var store = Store(new());
        var stage = await store.PutAsync(new("tenant-a"), new MemoryStream("evidence"u8.ToArray()), "text/plain", null, ContentStoreWriteEvidence.New("uploader"));
        var reference = await store.CommitReferenceAsync(new("tenant-a"), stage, new("record", "evidence"), "%41.txt", ContentStoreWriteEvidence.New("uploader"));
        var delivery = await store.ReadAsync(new("tenant-a"), reference, true, ContentStoreWriteEvidence.Reader);
        using var body = delivery.Body;
        Assert.Equal("attachment; filename=\"_41.txt\"; filename*=UTF-8''%2541.txt", delivery.ContentDisposition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Store_writes_do_not_capture_a_callers_context_when_the_bookkeeping_port_suspends(bool put)
    {
        var storage = new InMemoryContentStoreStorage();
        var store = Store(storage);
        var staged = await store.PutAsync(new("tenant-a"), new MemoryStream("evidence"u8.ToArray()), "text/plain", null, ContentStoreWriteEvidence.New("uploader"));
        var port = new ContextPort(storage.Bookkeeping, put ? "AddStagedAsync" : "FindStagedForUpdateAsync");
        var context = new CountingContext();
        var previous = SynchronizationContext.Current;
        Task<KernelTransactionResult<bool>> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = KernelTransactionBoundary.ExecutePreparedAsync(async token =>
            {
                var write = ContentStoreWriteEvidence.New("uploader", port.Session);
                if (put) await store.PutAsync(new("tenant-a"), new MemoryStream("more"u8.ToArray()), "text/plain", null, write, cancellationToken: token).ConfigureAwait(false);
                else await store.CommitReferenceAsync(new("tenant-a"), staged, new("record", "evidence"), "evidence.txt", write, token).ConfigureAwait(false);
                return new KernelCommand<string>(ContentStoreWriteEvidence.New("uploader").Operation, "record", ContentStoreWriteEvidence.New("uploader").Audit);
            }, port).AsTask();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.True(port.Delay.Entered);
        Assert.False(pending.IsCompleted);
        port.Delay.Release.SetResult();
        Assert.True((await pending.WaitAsync(TimeSpan.FromSeconds(30))).Committed);
        Assert.Equal(0, context.Posts);
        Assert.Equal(put ? 2 : 0, store.StagedCount);
        Assert.Equal(put ? 0 : 1, store.CommittedCount);
    }

    private sealed class CountingContext : SynchronizationContext
    {
        private int posts;
        public int Posts => Volatile.Read(ref posts);
        public override void Post(SendOrPostCallback callback, object? state)
        { Interlocked.Increment(ref posts); ThreadPool.QueueUserWorkItem(_ => callback(state)); }
    }

    // A database-port decorator, not a store mock: it forwards every operation to the real backing and suspends one IO.
    public class DelayedSession : DispatchProxy
    {
        public IContentBookkeepingSession Inner { get; set; } = null!;
        public string Method { get; set; } = "";
        public bool Entered { get; private set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = targetMethod!.Invoke(Inner, args);
            if (targetMethod.Name != Method) return result;
            Entered = true;
            return Method == "AddStagedAsync" ? Delay((ValueTask)result!) : DelayFind((ValueTask<ContentStoreItem?>)result!);
        }
        private async ValueTask Delay(ValueTask operation)
        { await Release.Task.ConfigureAwait(false); await operation.ConfigureAwait(false); }
        private async ValueTask<ContentStoreItem?> DelayFind(ValueTask<ContentStoreItem?> operation)
        { await Release.Task.ConfigureAwait(false); return await operation.ConfigureAwait(false); }
    }

    private sealed class ContextPort(IContentBookkeeping bookkeeping, string method) : IKernelPreparedTransactionPort<string, bool>
    {
        public DelayedSession Delay { get; private set; } = null!;
        public IContentBookkeepingSession Session { get; private set; } = null!;
        public async ValueTask<IKernelPreparedTransaction<string, bool>> BeginAsync(CancellationToken cancellationToken = default)
        {
            var transaction = await bookkeeping.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            Session = DispatchProxy.Create<IContentBookkeepingSession, DelayedSession>();
            Delay = (DelayedSession)Session; Delay.Inner = transaction; Delay.Method = method;
            return new Transaction(transaction);
        }
        private sealed class Transaction(IContentBookkeepingTransaction content) : IKernelPreparedTransaction<string, bool>
        {
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageOperationAsync(operation, cancellationToken);
            public ValueTask StageRecordAsync(string record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageAuditAsync(audit, cancellationToken);
            public ValueTask<bool> CommitAsync(CancellationToken cancellationToken = default) => content.CommitAsync(cancellationToken);
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => content.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => content.DisposeAsync();
        }
    }

    private static InMemoryContentStore Store(InMemoryContentStoreStorage storage) =>
        new(new(100, 1000, 10000, TimeSpan.FromHours(1)), new Keys(), TimeProvider.System, storage);
    private sealed class Keys : ITenantContentKeyProvider
    {
        public byte[] GetKey(TenantId tenant) => SHA256.HashData(Encoding.UTF8.GetBytes("test:" + tenant.Value));
    }
}
