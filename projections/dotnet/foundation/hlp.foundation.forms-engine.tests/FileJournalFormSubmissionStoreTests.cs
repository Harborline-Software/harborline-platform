using System.Diagnostics;
using System.Buffers.Binary;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.DependencyInjection;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class FileJournalFormSubmissionStoreTests
{
    private static readonly TenantId Tenant = new("tenant-engine");
    private static readonly EntityId Instance = new("harborline", "forms", "restart-probe");

    [Fact]
    public async Task SaveAsync_DurableAdapterSurvivesProcessRestart()
    {
        using var fixture = new JournalFixture();
        await RunWriterProcessAsync(fixture.JournalPath, "write-crash");

        using var restarted = fixture.Open();
        var submission = await restarted.GetAsync(Tenant, Instance);
        var pending = Assert.Single(await restarted.LeasePendingAsync(10));

        Assert.NotNull(submission);
        Assert.Equal("restart-fingerprint", submission.RequestFingerprint);
        Assert.Equal("outbox-restart", pending.OutboxId);
        Assert.Equal((1, 1, 1, 0), await restarted.CountsAsync());
    }

    [Fact]
    public async Task ProjectionDelivery_RestartRecoversPendingRows()
    {
        using var fixture = new JournalFixture();
        await RunWriterProcessAsync(fixture.JournalPath, "write-crash");
        var projection = new RecordingProjection();
        using (var restarted = fixture.Open())
        {
            var result = await FormProjectionRecovery.RecoverAsync(restarted, projection, 10, default);
            Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), result);
            Assert.Equal("outbox-restart", Assert.Single(projection.Delivered).OutboxId);
            Assert.Empty(await restarted.LeasePendingAsync(10));
            Assert.Equal((1, 1, 0, 1), await restarted.CountsAsync());
        }
        using var afterAcknowledgementRestart = fixture.Open();
        Assert.Empty(await afterAcknowledgementRestart.LeasePendingAsync(10));
        Assert.Equal((1, 1, 0, 1), await afterAcknowledgementRestart.CountsAsync());
    }

    [Fact]
    public async Task IncompleteFinalFrame_IsTruncatedWithoutLosingCommittedRows()
    {
        using var fixture = new JournalFixture();
        await RunWriterProcessAsync(fixture.JournalPath, "write-partial-crash");
        var committedLength = new FileInfo(fixture.JournalPath).Length;

        using var restarted = fixture.Open();

        Assert.NotNull(await restarted.GetAsync(Tenant, Instance));
        Assert.Equal(committedLength - 3, new FileInfo(fixture.JournalPath).Length);
    }

    [Fact]
    public async Task CorruptCompleteFrame_FailsClosed()
    {
        using var fixture = new JournalFixture();
        await RunWriterProcessAsync(fixture.JournalPath, "write-crash");
        using (var stream = new FileStream(fixture.JournalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = 20;
            var value = stream.ReadByte();
            stream.Position = 20;
            stream.WriteByte((byte)(value ^ 0xff));
            stream.Flush(flushToDisk: true);
        }

        Assert.Throws<InvalidDataException>(() => fixture.Open());
    }

    [Fact]
    public async Task CorruptLengthField_FailsClosedWithoutTruncatingCommittedFrame()
    {
        using var fixture = new JournalFixture();
        await RunWriterProcessAsync(fixture.JournalPath, "write-crash");
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
    public async Task ConcurrentRecovery_LeasesEachPendingEnvelopeOnce()
    {
        using var fixture = new JournalFixture();
        await RunWriterProcessAsync(fixture.JournalPath, "write-crash");
        using var store = fixture.Open();
        var blocking = new BlockingProjection();
        var first = FormProjectionRecovery.RecoverAsync(store, blocking, 10, default).AsTask();
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var secondProjection = new RecordingProjection();
        var second = await FormProjectionRecovery.RecoverAsync(store, secondProjection, 10, default);
        blocking.Release.TrySetResult();
        var firstResult = await first;

        Assert.Equal(new FormProjectionRecoveryResult(0, 0, 0), second);
        Assert.Empty(secondProjection.Delivered);
        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), firstResult);
    }

    [Fact]
    public async Task AdapterLifetime_HoldsExclusiveProcessLock()
    {
        using var fixture = new JournalFixture();
        using var first = fixture.Open();
        Assert.Throws<IOException>(() => fixture.Open());
        await Task.CompletedTask;
    }

    [Fact]
    public void FileSubmissionStore_RegistersSingletonAndRefusesLayering()
    {
        using var fixture = new JournalFixture();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddHarborlineFormsEngineFileSubmissionStore(options => options.JournalPath = fixture.JournalPath);

        Assert.Equal(
            Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton,
            Assert.Single(services, row => row.ServiceType == typeof(IFormSubmissionTransactionStore)).Lifetime);
        Assert.Throws<InvalidOperationException>(() =>
            services.AddHarborlineFormsEngineFileSubmissionStore(options => options.JournalPath = fixture.JournalPath));
    }

    private static async Task RunWriterProcessAsync(string journalPath, string mode)
    {
        var root = FindRepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var probe = Path.Combine(
            root,
            "projections/dotnet/foundation/hlp.foundation.forms-engine.restart-probe/bin",
            configuration,
            "net11.0",
            "Harborline.Foundation.Forms.Engine.RestartProbe.dll");
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
        // Environment.ProcessPath is the dotnet muxer only where the test host runs under
        // `dotnet exec testhost.dll` (macOS/Linux). On Windows VSTest launches testhost.exe,
        // an apphost, and re-invoking that with a DLL argument fails host resolution
        // ("hostpolicy.dll was not found"). Resolve the muxer the same way the tooling's
        // resolve-dotnet.mjs does: DOTNET_HOST_PATH first, then the muxer that owns the
        // running runtime, then ProcessPath only when it actually is the muxer.
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

    private sealed class RecordingProjection : IFormProjectionSink
    {
        public List<FormProjectionEnvelope> Delivered { get; } = [];

        public ValueTask<FormProjectionDeliveryResult> DeliverAsync(
            FormProjectionEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Delivered.Add(envelope);
            return ValueTask.FromResult(new FormProjectionDeliveryResult([]));
        }
    }

    private sealed class BlockingProjection : IFormProjectionSink
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<FormProjectionDeliveryResult> DeliverAsync(
            FormProjectionEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new([]);
        }
    }

    private sealed class JournalFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"harborline-forms-journal-{Guid.NewGuid():N}");

        public JournalFixture()
        {
            Directory.CreateDirectory(_directory);
            JournalPath = Path.Combine(_directory, "submissions.hlfj");
        }

        public string JournalPath { get; }

        public FileJournalFormSubmissionStore Open() => new(new() { JournalPath = JournalPath });

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
