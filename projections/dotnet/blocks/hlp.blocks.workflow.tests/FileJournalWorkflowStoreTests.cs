using System.Buffers.Binary;
using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// The durable-seam proofs for <see cref="FileJournalWorkflowStore"/> — the Harborline
/// replacement for the earlier source EF/SQLite workflow store rows (ticket 074 item 4). The true
/// crash rows run the restart PROBE as a child OS process (the Forms restart precedent —
/// never same-object dispose/reopen); the corruption rows prove the journal fails closed;
/// the frame-format rows are the narrowed analogues of the earlier source EF migration-path rows.
/// </summary>
public sealed class FileJournalWorkflowStoreTests
{
    // ── The atomic-advance contract ─────────────────────────────────────────

    [Fact]
    public async Task AtomicAdvance_CrashRollsBackEffect_ResumePostsExactlyOnce()
    {
        using var fixture = new JournalFixture();
        using var store = fixture.Open();
        await CreateInstanceAsync(store, "inst-atomic");
        var key = new WorkflowStepKey("inst-atomic", 0, "post");

        // A staging failure mid-advance commits NOTHING — no event, no idempotency row, no effect.
        var failing = new WorkflowEffect((unitOfWork, _) =>
        {
            ((FileJournalWorkflowUnitOfWork)unitOfWork).StageEffectPayload("must-not-survive");
            throw new InvalidOperationException("staging crash");
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AdvanceAsync(key, failing, "{}", "Advanced", "{}", "posted", WorkflowStatus.Completed));

        Assert.Null(await store.FindStepResultAsync(key));
        Assert.Empty(await store.CommittedEffectPayloadsAsync("inst-atomic"));
        Assert.Equal(WorkflowStatus.Running, (await store.LoadAsync("inst-atomic"))!.Status);

        // The resume re-runs cleanly and posts exactly once.
        await store.AdvanceAsync(key, Effect("posted-je"), "{}", "Advanced", "{}", "posted", WorkflowStatus.Completed);
        Assert.Equal(new[] { "posted-je" }, await store.CommittedEffectPayloadsAsync("inst-atomic"));
        Assert.NotNull(await store.FindStepResultAsync(key));
    }

    [Fact]
    public async Task CleanAdvance_RecordsIdempotency_AndDeterministicEffectIdIsUnique()
    {
        using var fixture = new JournalFixture();
        using var store = fixture.Open();
        await CreateInstanceAsync(store, "inst-clean");
        var key = new WorkflowStepKey("inst-clean", 0, "post");

        await store.AdvanceAsync(key, Effect("je"), "{\"ok\":true}", "Advanced", "{}", "posted", WorkflowStatus.Completed);

        var recorded = await store.FindStepResultAsync(key);
        Assert.NotNull(recorded);
        Assert.Equal("{\"ok\":true}", recorded!.ResultJson);

        // The deterministic derived id is stable per key and distinct across purposes/steps.
        Assert.Equal(key.ToDeterministicGuid("source-reference"), key.ToDeterministicGuid("source-reference"));
        Assert.NotEqual(key.ToDeterministicGuid("source-reference"), key.ToDeterministicGuid("draft-id"));
        Assert.NotEqual(
            key.ToDeterministicGuid("source-reference"),
            new WorkflowStepKey("inst-clean", 1, "post").ToDeterministicGuid("source-reference"));
    }

    // ── True process-crash durability (the restart probe rows) ──────────────

    [Fact]
    public async Task AtomicAdvance_SurvivesProcessRestart_EffectCommittedExactlyOnce()
    {
        using var fixture = new JournalFixture();
        await RunProbeAsync(fixture.JournalPath, "write-crash");

        using var restarted = fixture.Open();
        Assert.Equal(new[] { "restart-effect" }, await restarted.CommittedEffectPayloadsAsync("restart-1"));
        Assert.NotNull(await restarted.FindStepResultAsync(new WorkflowStepKey("restart-1", 0, "post")));
        Assert.Equal((1, 2, 1, 1), await restarted.CountsAsync());
    }

    [Fact]
    public async Task Park_SurvivesRestart_ResumeAdvancesOnce()
    {
        using var fixture = new JournalFixture();
        await RunProbeAsync(fixture.JournalPath, "write-crash");

        using var restarted = fixture.Open();
        var instance = await restarted.LoadAsync("restart-1");
        Assert.NotNull(instance);
        Assert.Equal(WorkflowStatus.Parked, instance!.Status);
        Assert.Equal("approve", instance.CurrentStep);

        // Resume: the parked human task advances exactly once through the dispatcher.
        var dispatcher = new WorkflowTriggerDispatcher(restarted, new[] { new StaticHandler() });
        var result = await dispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "restart-1", "approve", "{\"decision\":\"approve\"}"));
        Assert.Equal(WorkflowDispatchResult.Advanced, result);

        var replay = await dispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "restart-1", "approve", "{\"decision\":\"approve\"}"));
        Assert.True(replay is WorkflowDispatchResult.ReplayedNoOp or WorkflowDispatchResult.Terminal);
        Assert.Equal(new[] { "restart-effect", "resumed-post" }, await restarted.CommittedEffectPayloadsAsync("restart-1"));
    }

    [Fact]
    public async Task SendBack_BumpsDurableIteration_ReadBackOnRestart()
    {
        using var fixture = new JournalFixture();
        await RunProbeAsync(fixture.JournalPath, "write-crash");

        using var restarted = fixture.Open();
        // The probe parked at iteration 1 (a loop-back bump). The counter is read back verbatim —
        // never recomputed from the event log (bug-1337 class).
        Assert.Equal(1, (await restarted.LoadAsync("restart-1"))!.Iteration);
    }

    [Fact]
    public async Task DefinitionVersion_IsPinned_AndDurable()
    {
        using var fixture = new JournalFixture();
        await RunProbeAsync(fixture.JournalPath, "write-crash");

        using var restarted = fixture.Open();
        Assert.Equal("2026-06-23.1", (await restarted.LoadAsync("restart-1"))!.DefinitionVersion);
        Assert.Equal("invoice-approval", (await restarted.LoadAsync("restart-1"))!.DefinitionKey);
    }

    [Fact]
    public async Task Events_AreAppendOnly_WithMonotonicSeq()
    {
        using var fixture = new JournalFixture();
        await RunProbeAsync(fixture.JournalPath, "write-crash");

        using var restarted = fixture.Open();
        var before = await restarted.EventsAsync("restart-1");
        Assert.Equal(new long[] { 1, 2 }, before.Select(e => e.Seq).ToArray());

        await restarted.ParkAsync("restart-1", "approve", "{}", iteration: 1);
        var after = await restarted.EventsAsync("restart-1");
        Assert.Equal(new long[] { 1, 2, 3 }, after.Select(e => e.Seq).ToArray());
        // The earlier rows are untouched — append-only history.
        Assert.Equal(before.Select(e => (e.Seq, e.EventType)), after.Take(2).Select(e => (e.Seq, e.EventType)));
    }

    // ── Journal-format fail-closed rows (the narrowed EF migration-path analogues) ──

    [Fact]
    public async Task IncompleteFinalFrame_IsTruncatedWithoutLosingCommittedRows()
    {
        using var fixture = new JournalFixture();
        await RunProbeAsync(fixture.JournalPath, "write-partial-crash");
        var committedLength = new FileInfo(fixture.JournalPath).Length;

        using var restarted = fixture.Open();

        Assert.NotNull(await restarted.LoadAsync("restart-1"));
        Assert.Equal(committedLength - 3, new FileInfo(fixture.JournalPath).Length);
    }

    [Fact]
    public async Task CorruptCompleteFrame_FailsClosed()
    {
        using var fixture = new JournalFixture();
        await RunProbeAsync(fixture.JournalPath, "write-crash");
        using (var stream = new FileStream(fixture.JournalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = 60;
            var value = stream.ReadByte();
            stream.Position = 60;
            stream.WriteByte((byte)(value ^ 0xff));
            stream.Flush(flushToDisk: true);
        }

        Assert.Throws<InvalidDataException>(() => fixture.Open());
    }

    [Fact]
    public async Task CorruptLengthField_FailsClosedWithoutTruncatingCommittedFrame()
    {
        using var fixture = new JournalFixture();
        await RunProbeAsync(fixture.JournalPath, "write-crash");
        var committedLength = new FileInfo(fixture.JournalPath).Length;
        using (var stream = new FileStream(fixture.JournalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var length = new byte[4];
            stream.Position = 8;
            stream.ReadExactly(length);
            BinaryPrimitives.WriteInt32LittleEndian(length, checked(BinaryPrimitives.ReadInt32LittleEndian(length) + 1024));
            stream.Position = 8;
            stream.Write(length);
            stream.Flush(flushToDisk: true);
        }

        Assert.Throws<InvalidDataException>(() => fixture.Open());
        Assert.Equal(committedLength, new FileInfo(fixture.JournalPath).Length);
    }

    [Fact]
    public async Task MigratedJournal_StoreRoundTrips()
    {
        // The narrowed analogue of "MigrateAsync_ThenStoreRoundTrips": a journal written by one
        // adapter instance round-trips completely through a fresh adapter over the same file.
        using var fixture = new JournalFixture();
        using (var store = fixture.Open())
        {
            await CreateInstanceAsync(store, "inst-roundtrip");
            await store.AdvanceAsync(
                new WorkflowStepKey("inst-roundtrip", 0, "step-1"), Effect("e1"),
                "{}", "Advanced", "{}", "step-2", WorkflowStatus.Running);
        }

        using var reopened = fixture.Open();
        var instance = await reopened.LoadAsync("inst-roundtrip");
        Assert.Equal("step-2", instance!.CurrentStep);
        Assert.Equal(new[] { "e1" }, await reopened.CommittedEffectPayloadsAsync("inst-roundtrip"));
    }

    // ── Adapter lifetime + composition ──────────────────────────────────────

    [Fact]
    public async Task AdapterLifetime_HoldsExclusiveProcessLock()
    {
        using var fixture = new JournalFixture();
        using var first = fixture.Open();
        Assert.Throws<IOException>(() => fixture.Open());
        await Task.CompletedTask;
    }

    [Fact]
    public void FileWorkflowStore_RegistersSingletonAndRefusesLayering()
    {
        using var fixture = new JournalFixture();
        var services = new ServiceCollection();
        services.AddFileJournalWorkflowStore(new FileJournalWorkflowStoreOptions { JournalPath = fixture.JournalPath });

        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(services, row => row.ServiceType == typeof(IWorkflowStore)).Lifetime);
        Assert.Throws<InvalidOperationException>(() =>
            services.AddFileJournalWorkflowStore(new FileJournalWorkflowStoreOptions { JournalPath = fixture.JournalPath }));
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    internal static WorkflowEffect Effect(string payload) => new((unitOfWork, _) =>
    {
        ((FileJournalWorkflowUnitOfWork)unitOfWork).StageEffectPayload(payload);
        return Task.CompletedTask;
    });

    internal static Task CreateInstanceAsync(FileJournalWorkflowStore store, string id, string tenant = "tenant:acme")
        => store.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            Id = id,
            TenantId = tenant,
            DefinitionKey = "invoice-approval",
            DefinitionVersion = "2026-06-23.1",
            CurrentStep = "decide",
            Status = WorkflowStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

    /// <summary>A minimal handler for restart-resume: any approve advances to posted with an effect.</summary>
    private sealed class StaticHandler : IWorkflowStepHandler
    {
        public string DefinitionKey => "invoice-approval";

        public ValueTask<WorkflowStepOutcome> DecideAsync(
            WorkflowInstanceRecord instance, WorkflowTrigger trigger, CancellationToken ct = default)
            => ValueTask.FromResult(WorkflowStepOutcome.Complete("posted", Effect("resumed-post")));
    }

    internal static async Task RunProbeAsync(string journalPath, string mode)
    {
        var root = FindRepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var probe = Path.Combine(
            root,
            "projections/dotnet/blocks/hlp.blocks.workflow.restart-probe/bin",
            configuration,
            "net11.0",
            "Harborline.Blocks.Workflow.RestartProbe.dll");
        Assert.True(File.Exists(probe), $"Restart probe was not built: {probe}");
        var start = new ProcessStartInfo(ResolveDotnetMuxer())
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(probe);
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(journalPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start restart probe.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = await process.StandardError.ReadToEndAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, $"Restart probe failed ({process.ExitCode}): {stderr}");
        Assert.Contains("written", stdout, StringComparison.Ordinal);
    }

    private static string ResolveDotnetMuxer()
    {
        // On Windows VSTest launches testhost.exe (an apphost); re-invoking that with a DLL
        // argument fails host resolution. Resolve the real muxer: DOTNET_HOST_PATH first, then
        // the muxer owning the running runtime, then ProcessPath only when it IS the muxer.
        var muxerFileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(hostPath) && File.Exists(hostPath)) return hostPath;
        var runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var fromRuntime = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..", muxerFileName));
        if (File.Exists(fromRuntime)) return fromRuntime;
        var processPath = Environment.ProcessPath;
        if (processPath is not null
            && Path.GetFileName(processPath).Equals(muxerFileName, StringComparison.OrdinalIgnoreCase)) return processPath;
        throw new InvalidOperationException("Could not resolve the dotnet muxer for the restart probe.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Harborline.Platform.slnx"))) return directory.FullName;
        }
        throw new InvalidOperationException("Could not locate the Harborline Platform repository root.");
    }

    internal sealed class JournalFixture : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "hlp-workflow-journal-" + Guid.NewGuid().ToString("N"));

        public JournalFixture()
        {
            Directory.CreateDirectory(_directory);
            JournalPath = Path.Combine(_directory, "workflow.hlwj");
        }

        public string JournalPath { get; }

        public FileJournalWorkflowStore Open()
            => new(new FileJournalWorkflowStoreOptions { JournalPath = JournalPath });

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }
}
