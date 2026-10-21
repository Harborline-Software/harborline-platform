using System.Diagnostics;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Engine.Persistence;
using Xunit;
using static Harborline.Foundation.Forms.Engine.Tests.AcceptedCommandFixtures;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>
/// Plan W6/W7 and DES-0029 section 9 condition 5 (recovery proven after real process loss): the durable journal keeps an
/// accepted command, its acceptance snapshot and its lifecycle fence across a reopen in journal format 3, while an ordinary
/// submission stays format 2; a separate process that accepts a command (and, in one case, claims it) and then dies leaves
/// a journal from which a restarted worker completes the command exactly once, and a cancel after the durable claim is too
/// late. The quarantine lifecycle (review R2-M1, R2-L1) survives reopens the same way. Oracles: literal ids, frame header
/// bytes and states; the probe writes its acceptance snapshot as literal bytes.
/// </summary>
public sealed class FormAcceptedCommandRestartTests
{
    private static readonly TenantId Tenant = new("tenant-engine");
    private const string ProbeOutbox = "outbox-restart-accepted";
    private const string ProbeOperation = "harborline:forms/restart-accepted";

    [Fact(DisplayName = "W6: the journal keeps the command, its snapshot and its claim across a reopen; a cancel after the claim is too late")]
    public async Task JournalKeepsCommandAndFenceAcrossReopen()
    {
        using var fixture = new JournalFixture();
        using (var store = fixture.Open())
        {
            await CommitAsync(store, AcceptedCommit());
            Assert.True((await store.ClaimAcceptedCommandAsync("outbox-1", Worker, Now))!.Changed);
        }

        using var reopened = fixture.Open();
        var command = await reopened.GetAcceptedCommandAsync(Tenant, "outbox-1");
        var cancel = await reopened.CancelAcceptedCommandAsync(Tenant, "outbox-1", CancelAudit("cancel-audit-1"));
        var pending = Assert.Single(await reopened.LeasePendingAsync(10));

        Assert.NotNull(command);
        Assert.Equal(FormAcceptedCommandState.Claimed, command.State);
        Assert.Equal(Worker, command.StateActorId);
        Assert.Equal(Acceptance("alice", allowed: true, Now), command.AcceptanceSnapshot.ToArray());
        Assert.False(cancel!.Changed);
        Assert.Equal(FormAcceptedCommandState.Claimed, cancel.Command.State);
        Assert.Equal(FormAcceptedCommandState.Claimed, pending.AcceptedCommand!.State);
    }

    [Fact(DisplayName = "W6: a cancelled command stays cancelled across a reopen and is never leased again")]
    public async Task CancelledCommandStaysCancelledAcrossReopen()
    {
        using var fixture = new JournalFixture();
        using (var store = fixture.Open())
        {
            await CommitAsync(store, AcceptedCommit());
            Assert.True((await store.CancelAcceptedCommandAsync(Tenant, "outbox-1", CancelAudit("cancel-audit-1")))!.Changed);
        }

        using var reopened = fixture.Open();
        var command = await reopened.GetAcceptedCommandAsync(Tenant, "outbox-1");
        var claim = await reopened.ClaimAcceptedCommandAsync("outbox-1", Worker, Now);

        Assert.Equal(FormAcceptedCommandState.Cancelled, command!.State);
        Assert.Equal("cancel-decision"u8.ToArray(), command.StateSnapshot.ToArray());
        Assert.False(claim!.Changed);
        Assert.Equal(FormAcceptedCommandState.Cancelled, claim.Command.State);
        Assert.Empty(await reopened.LeasePendingAsync(10));
        Assert.Null(await reopened.GetAcceptedCommandAsync(new TenantId("tenant-other"), "outbox-1"));
        // Review M1: the cancellation's audit was in the same frame and survives the reopen beside the acceptance audit.
        Assert.Equal((1, 2, 0, 1), await reopened.CountsAsync());
    }

    [Fact(DisplayName = "C2: a torn claim frame is truncated on reopen; the command is pending again, unclaimed and still cancellable")]
    public async Task TornClaimFrameLeavesTheCommandPending()
    {
        using var fixture = new JournalFixture();
        using (var store = fixture.Open())
        {
            await CommitAsync(store, AcceptedCommit());
            await store.ClaimAcceptedCommandAsync("outbox-1", Worker, Now);
        }
        var bytes = await File.ReadAllBytesAsync(fixture.JournalPath);
        await File.WriteAllBytesAsync(fixture.JournalPath, bytes[..^5]);

        using var reopened = fixture.Open();
        var command = await reopened.GetAcceptedCommandAsync(Tenant, "outbox-1");
        var cancel = await reopened.CancelAcceptedCommandAsync(Tenant, "outbox-1", CancelAudit("cancel-audit-1"));

        Assert.Equal(FormAcceptedCommandState.Pending, command!.State);
        Assert.True(cancel!.Changed);
        Assert.Equal(FormAcceptedCommandState.Cancelled, cancel.Command.State);
    }

    [Fact(DisplayName = "M1: a refusal and its audit are one frame; both survive a reopen, and a stale expected state refuses nothing")]
    public async Task RefusalAndItsAuditSurviveAReopen()
    {
        using var fixture = new JournalFixture();
        using (var store = fixture.Open())
        {
            await CommitAsync(store, AcceptedCommit());
            await store.ClaimAcceptedCommandAsync("outbox-1", Worker, Now);
            var stale = await store.RefuseAcceptedCommandAsync("outbox-1", FormAcceptedCommandState.Pending, "records.command.binding_mismatch",
                new Harborline.Kernel.Core.KernelAuditEvidence("refusal-audit-0", Worker, Now, "{}"u8.ToArray()));
            Assert.False(stale!.Changed);
            Assert.True((await store.RefuseAcceptedCommandAsync("outbox-1", FormAcceptedCommandState.Claimed, "records.command.binding_mismatch",
                new Harborline.Kernel.Core.KernelAuditEvidence("refusal-audit-1", Worker, Now, "{}"u8.ToArray())))!.Changed);
        }

        using var reopened = fixture.Open();
        var command = await reopened.GetAcceptedCommandAsync(Tenant, "outbox-1");

        Assert.Equal(FormAcceptedCommandState.Refused, command!.State);
        Assert.Equal("records.command.binding_mismatch", command.RefusalCode);
        Assert.Equal((1, 2, 0, 1), await reopened.CountsAsync());
    }

    [Fact(DisplayName = "R2-M1, R2-L1: a binding failure, a quarantine with its audit, and an abandonment with its audit are format 3 frames that survive reopens; a quarantined command is never leased")]
    public async Task QuarantineLifecycleSurvivesReopens()
    {
        using var fixture = new JournalFixture();
        using (var store = fixture.Open())
        {
            await CommitAsync(store, AcceptedCommit());
            await store.ClaimAcceptedCommandAsync("outbox-1", Worker, Now);
            Assert.True((await store.RecordBindingFailureAsync("outbox-1", "records.command.binding_mismatch"))!.Changed);
        }
        FormAcceptedRecordCommand? failed;
        FormProjectionEnvelope counted;
        using (var reopened = fixture.Open())
        {
            failed = await reopened.GetAcceptedCommandAsync(Tenant, "outbox-1");
            counted = Assert.Single(await reopened.LeasePendingAsync(10));
            await reopened.ReleaseProjectionLeaseAsync("outbox-1");
            Assert.True((await reopened.QuarantineAcceptedCommandAsync("outbox-1", "records.command.binding_mismatch",
                new Harborline.Kernel.Core.KernelAuditEvidence("quarantine-audit-1", Worker, Now, "{\"op\":\"accepted-command-quarantined\"}"u8.ToArray())))!.Changed);
        }
        FormAcceptedRecordCommand? quarantined;
        IReadOnlyList<FormProjectionEnvelope> leasable;
        (int, int, int, int) quarantinedCounts;
        using (var reopened = fixture.Open())
        {
            quarantined = await reopened.GetAcceptedCommandAsync(Tenant, "outbox-1");
            leasable = await reopened.LeasePendingAsync(10);
            quarantinedCounts = await reopened.CountsAsync();
            Assert.True((await reopened.AbandonQuarantinedCommandAsync(Tenant, "outbox-1", CancelAudit("abandon-audit-1")))!.Changed);
        }

        FormAcceptedRecordCommand? abandoned;
        (int, int, int, int) finalCounts;
        using (var final = fixture.Open())
        {
            abandoned = await final.GetAcceptedCommandAsync(Tenant, "outbox-1");
            finalCounts = await final.CountsAsync();
        }
        // An open store holds its journal exclusively (FileShare.None, an advisory lock on Linux and macOS), so the frames are
        // read only after the last reopen is closed.
        var frames = FrameVersions(await File.ReadAllBytesAsync(fixture.JournalPath));

        Assert.Equal((FormAcceptedCommandState.Claimed, 1), (failed!.State, failed.BindingFailures));
        Assert.Equal((1, "records.command.binding_mismatch"), (counted.Attempts, counted.LastErrorCode));
        Assert.Equal((FormAcceptedCommandState.Quarantined, "records.command.binding_mismatch"), (quarantined!.State, quarantined.RefusalCode));
        Assert.Empty(leasable);
        Assert.Equal((1, 2, 1, 0), quarantinedCounts);
        Assert.Equal(FormAcceptedCommandState.Cancelled, abandoned!.State);
        Assert.Equal((1, 3, 0, 1), finalCounts);
        Assert.Equal([(3, 1), (3, 4), (3, 8), (3, 7), (3, 9)], frames);
    }

    [Fact(DisplayName = "R3-L1: the journal refuses a direct completion of a quarantined row; its audited resolution is one frame and survives a reopen")]
    public async Task QuarantinedRowResolvedWithItsAuditSurvivesAReopen()
    {
        using var fixture = new JournalFixture();
        using (var store = fixture.Open())
        {
            await CommitAsync(store, AcceptedCommit());
            await store.ClaimAcceptedCommandAsync("outbox-1", Worker, Now);
            await store.QuarantineAcceptedCommandAsync("outbox-1", "records.write.replay_conflict",
                new Harborline.Kernel.Core.KernelAuditEvidence("quarantine-audit-1", Worker, Now, "{\"op\":\"accepted-command-quarantined\"}"u8.ToArray()));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.CompleteProjectionAsync("outbox-1", []));
            Assert.True((await store.ResolveQuarantinedCommandAsync(Tenant, "outbox-1",
                new Harborline.Kernel.Core.KernelAuditEvidence("resolve-audit-1", Worker, Now, "{\"op\":\"accepted-command-resolved\"}"u8.ToArray())))!.Changed);
        }

        FormAcceptedRecordCommand? command;
        (int, int, int, int) counts;
        using (var reopened = fixture.Open())
        {
            command = await reopened.GetAcceptedCommandAsync(Tenant, "outbox-1");
            counts = await reopened.CountsAsync();
        }
        // The open store holds its journal exclusively, so the frames are read after the reopen is closed.
        var frames = FrameVersions(await File.ReadAllBytesAsync(fixture.JournalPath));

        Assert.Equal(FormAcceptedCommandState.Completed, command!.State);
        Assert.Equal((1, 3, 0, 1), counts);
        Assert.Equal([(3, 1), (3, 4), (3, 7), (3, 10)], frames);
    }

    [Fact(DisplayName = "R2-L4: the lease queue is rebuilt from the journal's frame order on reopen; a failed row stays behind rows waiting at its failure")]
    public async Task LeaseQueueIsRebuiltFromFrameOrder()
    {
        using var fixture = new JournalFixture();
        using (var store = fixture.Open())
        {
            await CommitAsync(store, OrdinaryCommit("queue-a", Now));
            await CommitAsync(store, OrdinaryCommit("queue-b", Now.AddHours(1)));
            await store.RetryProjectionAsync("outbox-queue-a", "form.engine.projection-pending");
        }

        using var reopened = fixture.Open();
        var first = Assert.Single(await reopened.LeasePendingAsync(1));
        var second = Assert.Single(await reopened.LeasePendingAsync(1));

        Assert.Equal(("outbox-queue-b", "outbox-queue-a"), (first.OutboxId, second.OutboxId));
    }

    [Fact(DisplayName = "W6: frames carrying an accepted command are format 3; an ordinary commit stays format 2")]
    public async Task OnlyAcceptedCommandFramesAreFormatThree()
    {
        using var accepted = new JournalFixture();
        using (var store = accepted.Open())
        {
            await CommitAsync(store, AcceptedCommit());
            await store.ClaimAcceptedCommandAsync("outbox-1", Worker, Now);
        }
        using var ordinary = new JournalFixture();
        using (var store = ordinary.Open())
            await CommitAsync(store, AcceptedCommit() with
            {
                Audit = AcceptedCommit().Audit with { AuthoritySnapshot = ReadOnlyMemory<byte>.Empty },
                Projection = AcceptedCommit().Projection with { AcceptedCommand = null },
            });

        var acceptedFrames = FrameVersions(await File.ReadAllBytesAsync(accepted.JournalPath));
        var ordinaryFrames = FrameVersions(await File.ReadAllBytesAsync(ordinary.JournalPath));

        Assert.Equal([(3, 1), (3, 4)], acceptedFrames);
        Assert.Equal([(2, 1)], ordinaryFrames);
    }

    [Fact(DisplayName = "W6: a commit carrying an accepted command but labelled format 2 is refused on reopen, never loaded without its format")]
    public async Task CommandCommitRelabelledAsFormatTwoIsRefused()
    {
        using var fixture = new JournalFixture();
        using (var store = fixture.Open())
            await CommitAsync(store, AcceptedCommit());
        var bytes = await File.ReadAllBytesAsync(fixture.JournalPath);
        var length = BitConverter.ToInt32(bytes, 8);
        bytes[4] = 2;
        System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(0, 12)).CopyTo(bytes, 12);
        var framed = bytes.AsSpan(0, 12).ToArray().Concat(bytes.AsSpan(44, length).ToArray()).ToArray();
        System.Security.Cryptography.SHA256.HashData(framed).CopyTo(bytes, 44 + length);
        await File.WriteAllBytesAsync(fixture.JournalPath, bytes);

        var refusal = Assert.Throws<InvalidDataException>(() => fixture.Open());

        Assert.Equal("The Forms submission journal commit carries an accepted command outside format 3.", refusal.Message);
    }

    [Fact(DisplayName = "W7 (process loss): a command accepted by a process that died before any execution is completed exactly once by a restarted worker")]
    public async Task ProcessLossAfterAcceptanceRecoversExactlyOnce()
    {
        using var fixture = new JournalFixture();
        var schema = (await AcceptingAsync()).Definition.SchemaRef.Value;
        await RunProbeAsync("accept-crash", fixture.JournalPath, schema);
        var sink = new RecordsSink();
        FormProjectionRecoveryResult recovered;
        using (var journal = fixture.Open())
        {
            var worker = await AcceptingAsync(worker: Worker, sink: sink, actorId: "sys.forms-recovery", storeDecorator: _ => journal);
            recovered = await worker.Engine.RecoverProjectionsAsync(10);
        }

        using var reopened = fixture.Open();
        var command = await reopened.GetAcceptedCommandAsync(Tenant, ProbeOutbox);

        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), recovered);
        var record = Assert.Single(sink.Records);
        Assert.Equal(("tenant-engine", "alice", ProbeOperation), record.Key);
        Assert.Equal(Worker, record.Value);
        Assert.Equal(FormAcceptedCommandState.Completed, command!.State);
        Assert.Empty(await reopened.LeasePendingAsync(10));
    }

    [Fact(DisplayName = "W7 (process loss after the claim): the claim survives, a cancel is too late, and recovery replays the committed effect without a second insert")]
    public async Task ProcessLossAfterTheClaimReplaysWithoutASecondInsert()
    {
        using var fixture = new JournalFixture();
        var schema = (await AcceptingAsync()).Definition.SchemaRef.Value;
        await RunProbeAsync("claim-crash", fixture.JournalPath, schema);
        // The Records effect committed before the process died; only its completion mark was lost.
        var sink = new RecordsSink(new() { [("tenant-engine", "alice", ProbeOperation)] = Worker });
        var decider = new Decider();
        decider.CancelGrants.Add("tenant-admin");
        FormAcceptedCommandCancelResult late;
        FormProjectionRecoveryResult recovered;
        using (var journal = fixture.Open())
        {
            late = await new FormAcceptedCommandCancellation(journal, new AccessProvider(decider), new FixedClock(Now))
                .CancelAsync(new(Tenant, "tenant-admin"), ProbeOutbox);
            var worker = await AcceptingAsync(worker: DelegateWorker, sink: sink, actorId: "sys.forms-recovery", storeDecorator: _ => journal);
            recovered = await worker.Engine.RecoverProjectionsAsync(10);
        }

        Assert.Equal(FormAcceptedCommandCancelStatus.TooLate, late.Status);
        Assert.Equal(new FormProjectionRecoveryResult(1, 1, 0), recovered);
        Assert.Equal(DelegateWorker, Assert.Single(sink.Executions).WorkerId);
        var record = Assert.Single(sink.Records);
        Assert.Equal(Worker, record.Value);
        using var reopened = fixture.Open();
        Assert.Equal(FormAcceptedCommandState.Completed, (await reopened.GetAcceptedCommandAsync(Tenant, ProbeOutbox))!.State);
    }

    // A cancellation's audit evidence: the canceller, the instant and the cancel decision's snapshot (review M1).
    private static Harborline.Kernel.Core.KernelAuditEvidence CancelAudit(string auditId) =>
        new(auditId, "tenant-admin", Now, "{\"op\":\"accepted-command-cancelled\"}"u8.ToArray()) { AuthoritySnapshot = "cancel-decision"u8.ToArray() };

    private static async Task CommitAsync(IFormSubmissionTransactionStore store, FormSubmissionCommit commit)
    {
        await using var scope = await store.BeginTransactionAsync();
        Assert.Equal(FormSubmissionCommitDisposition.Created, (await scope.CommitAsync(commit)).Disposition);
    }

    private static FormSubmissionCommit AcceptedCommit()
    {
        var party = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var instance = new EntityId("harborline", "forms", "instance-1");
        var form = new Harborline.Foundation.Forms.Models.FormDefinitionId("inspection");
        var version = new Harborline.Foundation.Forms.Models.SemanticVersion(1, 0, 0);
        var schema = new SchemaId("sha256:accepted");
        var bytes = "{}"u8.ToArray();
        var snapshot = Acceptance("alice", allowed: true, Now);
        return new FormSubmissionCommit(
            "op-1",
            new FormSubmissionRecord(instance, Tenant, party, "alice", form, version, "fingerprint-1", bytes, Now, schema, Target),
            new FormMutationAuditEnvelope("audit-1", instance, Tenant, "alice", bytes, Now) { AuthoritySnapshot = snapshot },
            new FormProjectionEnvelope("outbox-1", instance, Tenant, party, "alice", form, version, null, bytes, Now,
                SchemaRef: schema, RecordTypeId: Target)
            {
                AcceptedCommand = new FormAcceptedRecordCommand("outbox-1", instance.ToString(), Target, "fingerprint-1", Now, snapshot)
                {
                    AcceptanceAuditId = "audit-1", AcceptanceDecidedAt = Now, BodyDigestEncoding = "records-write-intent/1", BodyDigest = "digest-1",
                },
            },
            new FormSubmitReceipt(instance, Now, FormProjectionStatus.Pending, []));
    }

    // One ordinary (untargeted) submission at the given instant.
    private static FormSubmissionCommit OrdinaryCommit(string id, DateTimeOffset at)
    {
        var party = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var instance = new EntityId("harborline", "forms", id);
        var form = new Harborline.Foundation.Forms.Models.FormDefinitionId("inspection");
        var version = new Harborline.Foundation.Forms.Models.SemanticVersion(1, 0, 0);
        var bytes = "{}"u8.ToArray();
        return new FormSubmissionCommit(
            "key-" + id,
            new FormSubmissionRecord(instance, Tenant, party, "alice", form, version, "fingerprint-" + id, bytes, at),
            new FormMutationAuditEnvelope("audit-" + id, instance, Tenant, "alice", bytes, at),
            new FormProjectionEnvelope("outbox-" + id, instance, Tenant, party, "alice", form, version, null, bytes, at),
            new FormSubmitReceipt(instance, at, FormProjectionStatus.Pending, []));
    }

    // Each frame's (format version, record type) from its 12-byte header, walking header, header checksum, payload and
    // frame checksum.
    private static List<(int Version, int Type)> FrameVersions(byte[] journal)
    {
        var frames = new List<(int Version, int Type)>();
        for (var at = 0; at < journal.Length;)
        {
            var length = BitConverter.ToInt32(journal, at + 8);
            frames.Add((journal[at + 4], journal[at + 5]));
            at += 12 + 32 + length + 32;
        }
        return frames;
    }

    private static async Task RunProbeAsync(string mode, string journalPath, string schemaRef)
    {
        var root = FindRepositoryRoot();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var probe = Path.Combine(root, "projections/dotnet/foundation/hlp.foundation.forms-engine.restart-probe/bin",
            configuration, "net11.0", "Harborline.Foundation.Forms.Engine.RestartProbe.dll");
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
        start.ArgumentList.Add(schemaRef);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start restart probe.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = await process.StandardError.ReadToEndAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, $"Restart probe failed ({process.ExitCode}): {stderr}");
        Assert.Contains("written", stdout, StringComparison.Ordinal);
    }

    // As FileJournalFormSubmissionStoreTests resolves them.
    private static string ResolveDotnetMuxer()
    {
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

    private sealed class JournalFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"harborline-forms-accepted-{Guid.NewGuid():N}");

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
