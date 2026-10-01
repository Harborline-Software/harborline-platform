using System.Security.Cryptography;
using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Kernel.Core.Tests;

/// <summary>
/// The kernel core is library code: no await in the transaction boundary (ck transaction-boundary), the clock, the bootstrap
/// catalogue or configuration recovery may resume on the calling synchronization context, or a host that blocks on the
/// result from its UI or request context deadlocks. Each case makes exactly one awaited step complete later, on another
/// thread, while every earlier step completes inline, so the caller's context is still captured when that step is awaited.
/// </summary>
public sealed class KernelAwaitContextTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2030-01-02T03:04:05Z");

    private sealed class CountingContext : SynchronizationContext
    {
        public int Posts;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref Posts);
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    private static async Task NeverResumesOnTheCallersContext(Func<Task> act)
    {
        var context = new CountingContext();
        var prior = SynchronizationContext.Current;
        Task running;
        SynchronizationContext.SetSynchronizationContext(context);
        try { running = act(); }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }
        await running;
        Assert.Equal(0, context.Posts);
    }

    private static ValueTask Later(string step, string hopAt) =>
        step == hopAt ? new(Task.Delay(20)) : ValueTask.CompletedTask;

    private static async ValueTask<T> Later<T>(string step, string hopAt, T value)
    {
        if (step == hopAt) await Task.Delay(20).ConfigureAwait(false);
        return value;
    }

    private sealed class Fault : Exception { }

    private static KernelCommand<string> Command(string id = "one") => new(
        new(id, $"key-{id}", $"fingerprint-{id}"), $"record-{id}", new($"audit-{id}", "actor", Now, new byte[] { 1 }));

    private sealed class Port(string hopAt, bool fault = false)
        : IKernelTransactionPort<string, string>, IKernelPreparedTransactionPort<string, string>
    {
        public async ValueTask<IKernelTransaction<string, string>> BeginAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) =>
            await Later("begin", hopAt, (IKernelTransaction<string, string>)new Tx(hopAt, fault)).ConfigureAwait(false);

        async ValueTask<IKernelPreparedTransaction<string, string>> IKernelPreparedTransactionPort<string, string>.BeginAsync(CancellationToken cancellationToken) =>
            await Later("begin", hopAt, (IKernelPreparedTransaction<string, string>)new Tx(hopAt, fault)).ConfigureAwait(false);
    }

    private sealed class Tx(string hopAt, bool fault) : IKernelTransaction<string, string>, IKernelPreparedTransaction<string, string>
    {
        public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => Later("operation", hopAt);
        public ValueTask StageRecordAsync(string record, CancellationToken cancellationToken = default) => Later("record", hopAt);
        public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Later("audit", hopAt);
        public ValueTask<string> CommitAsync(CancellationToken cancellationToken = default) =>
            fault ? throw new Fault() : Later("commit", hopAt, "committed");
        public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => Later("rollback", hopAt);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Participant(string hopAt) : IKernelTransactionParticipant<string>
    {
        public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => Later("operation", hopAt);
        public ValueTask StageRecordAsync(string record, CancellationToken cancellationToken = default) => Later("record", hopAt);
        public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Later("audit", hopAt);
    }

    [Theory]
    [InlineData("begin", false)]
    [InlineData("record", false)]
    [InlineData("audit", false)]
    [InlineData("commit", false)]
    [InlineData("rollback", true)]
    public Task TransactionBoundaryExecuteNeverResumesOnTheCallersContext(string hopAt, bool fault) =>
        NeverResumesOnTheCallersContext(async () =>
        {
            try { await KernelTransactionBoundary.ExecuteAsync([Command()], new Port(hopAt, fault)).ConfigureAwait(false); }
            catch (Fault) when (fault) { }
        });

    [Theory]
    [InlineData("begin", false)]
    [InlineData("prepare", false)]
    [InlineData("operation", false)]
    [InlineData("record", false)]
    [InlineData("audit", false)]
    [InlineData("commit", false)]
    [InlineData("rollback", true)]
    public Task TransactionBoundaryExecutePreparedNeverResumesOnTheCallersContext(string hopAt, bool fault) =>
        NeverResumesOnTheCallersContext(async () =>
        {
            try
            {
                await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(
                    async _ => await Later("prepare", hopAt, Command()).ConfigureAwait(false), new Port(hopAt, fault)).ConfigureAwait(false);
            }
            catch (Fault) when (fault) { }
        });

    [Theory]
    [InlineData("operation")]
    [InlineData("record")]
    [InlineData("audit")]
    public Task TransactionBoundaryJoinNeverResumesOnTheCallersContext(string hopAt) =>
        NeverResumesOnTheCallersContext(async () =>
            await KernelTransactionBoundary.ExecutePreparedAsync<string, string>(async ct =>
            {
                await KernelTransactionBoundary.JoinAsync(Command("joined"), new Participant(hopAt), ct).ConfigureAwait(false);
                return Command();
            }, new Port("none")).ConfigureAwait(false));

    private sealed class Backdate(string hopAt) : IKernelBackdateCapability
    {
        public ValueTask<bool> CanBackdateAsync(string actorId, DateTimeOffset requestedEffectiveFrom, CancellationToken cancellationToken = default) =>
            Later("backdate", hopAt, true);
    }

    [Fact]
    public Task ClockBackdateCheckNeverResumesOnTheCallersContext() =>
        NeverResumesOnTheCallersContext(async () =>
        {
            var effective = await new KernelClock(TimeProvider.System)
                .ResolveEffectiveFromAsync("actor", Now, Now.AddDays(-1), new Backdate("backdate")).ConfigureAwait(false);
            Assert.Equal(Now.AddDays(-1), effective);
        });

    private sealed class Catalogue(string hopAt) : IKernelCatalogueReader
    {
        public ValueTask<CompiledBootstrapShape?> ReadAsync(CompiledShapeIdentity identity, CancellationToken cancellationToken = default) =>
            Later<CompiledBootstrapShape?>("read", hopAt, null);
    }

    [Fact]
    public Task BootstrapCatalogueLookupNeverResumesOnTheCallersContext() =>
        NeverResumesOnTheCallersContext(async () =>
            Assert.Null(await new CompiledBootstrapCatalogue(new Catalogue("read")).ResolveAsync(new("tenant.not-in-floor")).ConfigureAwait(false)));

    private static readonly byte[] Content = "effective-generation"u8.ToArray();

    private sealed class RecoveryHost(string hopAt)
        : IKernelProfileReader, IKernelConfigurationRecoveryCapability, IKernelTransactionPort<ConfigurationRecoveryRecord, string>
    {
        public ValueTask<bool> CanRecoverAsync(string actorId, string tenantKey, CancellationToken cancellationToken = default) =>
            Later("capability", hopAt, true);

        public ValueTask<KernelProfileSnapshot?> ReadAsync(string tenantKey, CancellationToken cancellationToken = default) =>
            Later<KernelProfileSnapshot?>("profile", hopAt, new(tenantKey,
                new(Convert.ToHexStringLower(SHA256.HashData(Content)), "intent-1"), Content, null, []));

        public ValueTask<IKernelTransaction<ConfigurationRecoveryRecord, string>> BeginAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) =>
            Later<IKernelTransaction<ConfigurationRecoveryRecord, string>>("begin", hopAt, new RecoveryTx(hopAt));

        private sealed class RecoveryTx(string hopAt) : IKernelTransaction<ConfigurationRecoveryRecord, string>
        {
            public ValueTask StageRecordAsync(ConfigurationRecoveryRecord record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask<string> CommitAsync(CancellationToken cancellationToken = default) => Later("commit", hopAt, "committed");
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Theory]
    [InlineData("capability")]
    [InlineData("profile")]
    [InlineData("begin")]
    public Task ConfigurationRecoveryNeverResumesOnTheCallersContext(string hopAt) =>
        NeverResumesOnTheCallersContext(async () =>
        {
            var host = new RecoveryHost(hopAt);
            var result = await new ConfigurationRecovery(host, host, new KernelClock(TimeProvider.System))
                .RecoverAsync(new("recovery-1", "tenant-a", "operator-1", "Node crashed", "{}"), host).ConfigureAwait(false);
            Assert.True(result.Committed);
        });
}
