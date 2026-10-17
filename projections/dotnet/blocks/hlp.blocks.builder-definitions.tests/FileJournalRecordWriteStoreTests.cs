using System.Text.Json.Nodes;
using Harborline.Foundation.Authorization;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class FileJournalRecordWriteStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "records-journal-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_directory, "records.json");
    private static readonly DateTimeOffset At = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static RecordWriteOperationKey Key(string id = "operation-1") => new("tenant-a", "alice", id);

    private static RecordWriteRow Row(RecordWriteOperationKey key, string recordId = "record-1", string digest = "intent-secret")
        => new(key.Tenant, recordId, "eam.asset-class", "1.0.0", "definition-digest", new SchemaId("schema:fixture"),
            "worker", key.OperationId, null, "records-write-intent/1", digest, new byte[] { 123, 125 }, At)
        { OriginatorId = "alice", AcceptedCommandId = "command-1", AcceptanceAuditId = "acceptance-1" };

    private static async Task<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> Stage(
        FileJournalRecordWriteStore store, RecordWriteOperationKey key, string recordId = "record-1", string digest = "intent-secret")
    {
        var transaction = await store.BeginAsync();
        await transaction.StageOperationAsync(new(key.OperationId, key.IdempotencyKey, digest));
        await transaction.StageRecordAsync(Row(key, recordId, digest));
        await transaction.StageAuditAsync(new("audit-1", "worker", At, new byte[] { 0, 255, 7 })
        { AuthoritySnapshot = new byte[] { 1, 2, 3 }, OriginatorId = "alice" });
        return transaction;
    }

    private JsonObject Journal() => JsonNode.Parse(File.ReadAllText(PathName))!.AsObject();
    private void Rewrite(JsonObject journal) => File.WriteAllText(PathName, journal.ToJsonString());

    [Fact]
    public async Task staged_values_are_invisible_and_commit_publishes_all_evidence_exactly_once()
    {
        var store = new FileJournalRecordWriteStore(PathName);
        await using var transaction = await Stage(store, Key());
        Assert.Null(await new FileJournalRecordWriteStore(PathName).FindAsync(Key()));
        Assert.False(File.Exists(PathName));
        var result = await transaction.CommitAsync();
        Assert.Equal(RecordWriteCommitDisposition.Created, result.Disposition);
        Assert.Equal("operation-1", result.Receipt!.OperationId);
        var journal = Journal();
        Assert.Equal("records-write-file-journal/1", journal["Format"]!.GetValue<string>());
        var entry = Assert.Single(journal["Entries"]!.AsArray())!;
        Assert.Equal("record-1", entry["Row"]!["RecordId"]!.GetValue<string>());
        Assert.Equal("alice", entry["Row"]!["OriginatorId"]!.GetValue<string>());
        Assert.Equal("alice", entry["Audit"]!["OriginatorId"]!.GetValue<string>());
        Assert.Equal("AQID", entry["Audit"]!["AuthoritySnapshot"]!.GetValue<string>());
        Assert.Equal("AP8H", entry["Audit"]!["Payload"]!.GetValue<string>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync().AsTask());
        var reopened = await new FileJournalRecordWriteStore(PathName).FindAsync(Key());
        Assert.Equal(result.Receipt, reopened!.Receipt);
        Assert.Equal("schema:fixture", reopened.Receipt!.Schema.Value);
        Assert.Single(Journal()["Entries"]!.AsArray());
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("rollback")]
    [InlineData("cancel")]
    public async Task discarded_or_cancelled_staging_leaves_no_evidence_after_reopen(string fault)
    {
        var store = new FileJournalRecordWriteStore(PathName);
        await using (var transaction = await Stage(store, Key()))
        {
            if (fault == "rollback") await transaction.RollbackAsync();
            if (fault == "cancel")
            {
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transaction.CommitAsync(cancellation.Token).AsTask());
            }
        }
        Assert.Null(await new FileJournalRecordWriteStore(PathName).FindAsync(Key()));
        Assert.False(File.Exists(PathName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task kernel_prepare_and_before_commit_faults_discard_everything(bool beforeCommit)
    {
        var store = new FileJournalRecordWriteStore(PathName);
        await Assert.ThrowsAsync<InjectedFault>(() => KernelTransactionBoundary.ExecutePreparedAsync<RecordWriteRow, RecordWriteCommit>(
            _ => beforeCommit ? ValueTask.FromResult(new KernelCommand<RecordWriteRow>(
                new("operation-1", Key().IdempotencyKey, "intent-secret"), Row(Key()), new("audit", "worker", At, ReadOnlyMemory<byte>.Empty)))
                : throw new InjectedFault(), new BeforeCommitPort(store)).AsTask());
        Assert.Null(await new FileJournalRecordWriteStore(PathName).FindAsync(Key()));
        Assert.False(File.Exists(PathName));
    }

    [Theory]
    [InlineData("intent-secret", RecordWriteCommitDisposition.Replayed)]
    [InlineData("different-intent", RecordWriteCommitDisposition.Conflict)]
    public async Task replay_and_conflict_return_stored_operation_without_publishing(string digest, RecordWriteCommitDisposition disposition)
    {
        var store = new FileJournalRecordWriteStore(PathName);
        await using var original = await Stage(store, Key());
        var created = await original.CommitAsync();
        var bytes = File.ReadAllBytes(PathName);
        await using var duplicate = await Stage(new FileJournalRecordWriteStore(PathName), Key(), "record-must-not-publish", digest);
        var result = await duplicate.CommitAsync();
        Assert.Equal(disposition, result.Disposition);
        Assert.Equal(created.Receipt, result.Stored!.Receipt);
        Assert.Equal("records-write-intent/1", result.Stored.IntentEncodingVersion);
        Assert.Equal("intent-secret", result.Stored.IntentDigest);
        Assert.Equal(bytes, File.ReadAllBytes(PathName));
    }

    [Fact]
    public async Task two_concurrent_first_writers_in_separate_instances_create_one_record()
    {
        await using var first = await Stage(new FileJournalRecordWriteStore(PathName), Key(), "first");
        await using var second = await Stage(new FileJournalRecordWriteStore(PathName), Key(), "second");
        var results = await Task.WhenAll(Task.Run(() => first.CommitAsync().AsTask()), Task.Run(() => second.CommitAsync().AsTask()));
        Assert.Single(results, result => result.Disposition == RecordWriteCommitDisposition.Created);
        Assert.Single(results, result => result.Disposition == RecordWriteCommitDisposition.Replayed);
        Assert.Single(Journal()["Entries"]!.AsArray());
    }

    [Fact]
    public async Task publication_replaces_the_file_without_writing_the_existing_reader_handle()
    {
        var store = new FileJournalRecordWriteStore(PathName);
        await using var first = await Stage(store, Key());
        await first.CommitAsync();
        var original = File.ReadAllBytes(PathName);
        using var reader = new FileStream(PathName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await using var second = await Stage(store, Key("operation-2"), "record-2");
        RecordWriteCommit? committed = null;
        try { committed = await second.CommitAsync(); }
        // Windows can refuse replacing an open file; failure must preserve the old journal too.
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { }
        using var old = new MemoryStream();
        await reader.CopyToAsync(old);
        Assert.Equal(original, old.ToArray());
        if (committed is null)
        {
            Assert.Equal(original, File.ReadAllBytes(PathName));
            Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
            reader.Dispose();
            await using var retry = await Stage(store, Key("operation-2"), "record-2");
            committed = await retry.CommitAsync();
        }
        Assert.Equal(RecordWriteCommitDisposition.Created, committed.Disposition);
        Assert.Equal(2, Journal()["Entries"]!.AsArray().Count);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{broken authority-snapshot-secret fingerprint-secret intent-secret")]
    [InlineData("{\"Format\":\"records-write-file-journal/1\",\"Entries\":null}")]
    public void corrupt_journals_refuse_open_without_reset_or_disclosure(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, content);
        var error = Assert.Throws<InvalidDataException>(() => new FileJournalRecordWriteStore(PathName));
        Assert.Equal("Records journal is invalid.", error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("secret", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(content, File.ReadAllText(PathName));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("string")]
    [InlineData("partial")]
    [InlineData("missing")]
    public async Task unreadable_or_null_receipt_still_returns_the_accepted_operation_and_refuses_through_real_operation(string corruption)
    {
        var store = new FileJournalRecordWriteStore(PathName);
        var operation = await Operation(store);
        var created = await operation.CreateAsync(new(Tenant, "alice"), Request());
        Assert.True(created.Committed);
        var journal = Journal();
        var entry = journal["Entries"]![0]!.AsObject();
        entry["Receipt"] = corruption switch
        {
            "string" => JsonValue.Create("unreadable"),
            "partial" => new JsonObject { ["OperationId"] = "operation-1" },
            _ => null,
        };
        if (corruption == "missing") entry.Remove("Receipt");
        Rewrite(journal);
        var bytes = File.ReadAllBytes(PathName);
        var reopened = new FileJournalRecordWriteStore(PathName);
        Assert.NotNull(await reopened.FindAsync(Key()));
        Assert.Null((await reopened.FindAsync(Key()))!.Receipt);
        var outcome = await (await Operation(reopened)).CreateAsync(new(Tenant, "alice"), Request());
        Assert.Equal("records.write.receipt_unsupported", outcome.Refusal!.Code);
        Assert.Equal(bytes, File.ReadAllBytes(PathName));
    }

    [Fact]
    public async Task an_existing_unreadable_journal_never_opens_as_an_empty_store()
    {
        await using var transaction = await Stage(new(PathName), Key());
        await transaction.CommitAsync();
        using (var exclusive = new FileStream(PathName, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsAny<IOException>(() => new FileJournalRecordWriteStore(PathName));
        Assert.NotNull(await new FileJournalRecordWriteStore(PathName).FindAsync(Key()));
    }

    [Fact]
    public async Task real_operation_replays_after_restart_and_refuses_another_intent()
    {
        var first = await (await Operation(new(PathName))).CreateAsync(new(Tenant, "alice"), Request());
        var operation = await Operation(new(PathName));
        var replay = await operation.CreateAsync(new(Tenant, "alice"), Request());
        Assert.True(replay.Replayed);
        Assert.Equal(first.Receipt, replay.Receipt);
        var conflict = await operation.CreateAsync(new(Tenant, "alice"), Request("A-2"));
        Assert.Equal("records.write.replay_conflict", conflict.Refusal!.Code);
        Assert.Equal(["/operation_id"], conflict.Refusal.Pointers);
        Assert.Single(Journal()["Entries"]!.AsArray());
    }

    [Fact]
    public async Task unknown_stored_intent_encoding_is_preserved_and_refused_by_real_operation()
    {
        await (await Operation(new(PathName))).CreateAsync(new(Tenant, "alice"), Request());
        var journal = Journal();
        journal["Entries"]![0]!["Row"]!["IntentEncodingVersion"] = "future-intent/9";
        Rewrite(journal);
        var store = new FileJournalRecordWriteStore(PathName);
        Assert.Equal("future-intent/9", (await store.FindAsync(Key()))!.IntentEncodingVersion);
        var outcome = await (await Operation(store)).CreateAsync(new(Tenant, "alice"), Request());
        Assert.Equal("records.write.receipt_unsupported", outcome.Refusal!.Code);
        Assert.Single(Journal()["Entries"]!.AsArray());
    }

    [Fact]
    public async Task another_delegate_recovers_the_same_accepted_command_after_restart()
    {
        var body = Request().Body;
        var access = new AccessRequest("forms:submit-gate", "alice", Tenant,
            new AccessRecord(Tenant, "form", "capture@1.0.0", new Dictionary<string, JsonNode?>()), At);
        var snapshot = AuthoritySnapshotEncoding.Encode(new AuthorizationDecisionEvidence(access, true, "None", "grant:fixture@1", [], [], kind: "Gate"));
        var command = new AcceptedRecordCreate(Tenant, "cmd-1", "alice", "op-accepted-1", "eam.asset-class", body,
            "records-write-intent/1", RecordWriteIntent.Digest(RecordWriteIntent.Encode("eam.asset-class", null, body)),
            "capture@1.0.0", "acceptance-audit-1", At, At, snapshot);
        var first = await (await Operation(new(PathName), accepted: true)).CompleteAcceptedAsync(new(Tenant, "sys.records-worker"), command);
        Assert.True(first.Committed);
        var replay = await (await Operation(new(PathName), accepted: true)).CompleteAcceptedAsync(new(Tenant, "sys.records-worker-2"), command);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Single(Journal()["Entries"]!.AsArray());
    }

    private static RecordCreateRequest Request(string asset = "A-1")
        => new("eam.asset-class", "operation-1", new JsonObject { ["asset_tag"] = asset, ["quantity"] = 3, ["notes"] = "spare" });

    private static async Task<RecordWriteOperation> Operation(FileJournalRecordWriteStore store, bool accepted = false)
    {
        var host = Host();
        await host.Records.CreateDraftAsync(accepted ? AssetClass("1.0.0") : RoleGatedAssetClass("1.0.0"), "create");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        return new(new AccessProvider(new Gate()), host.Records, host.Registry, store, store, new Clock());
    }

    private sealed class Gate : IAuthorizationDecider
    {
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuthorizationDecisionEvidence(request, true, "None", "grant:fixture@1", [], []));
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => At; }
    private sealed class InjectedFault : Exception;
    private sealed class BeforeCommitPort(FileJournalRecordWriteStore store) : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>
    {
        public async ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default)
            => new FaultingTransaction(await store.BeginAsync(cancellationToken));
    }
    private sealed class FaultingTransaction(IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit> inner)
        : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>
    {
        public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => inner.StageOperationAsync(operation, cancellationToken);
        public ValueTask StageRecordAsync(RecordWriteRow row, CancellationToken cancellationToken = default) => inner.StageRecordAsync(row, cancellationToken);
        public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => inner.StageAuditAsync(audit, cancellationToken);
        public ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default) => throw new InjectedFault();
        public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
