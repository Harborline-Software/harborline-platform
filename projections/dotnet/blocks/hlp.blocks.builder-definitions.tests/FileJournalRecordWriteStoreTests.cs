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

    [Theory]
    [InlineData("intent_encoding_version", null)]
    [InlineData("intent_encoding_version", "")]
    [InlineData("intent_encoding_version", " ")]
    [InlineData("intent_encoding_version", "\t\r\n")]
    [InlineData("intent_digest", null)]
    [InlineData("intent_digest", "")]
    [InlineData("intent_digest", " ")]
    [InlineData("intent_digest", "\t\r\n")]
    [InlineData("schema", null)]
    public async Task malformed_row_metadata_refuses_at_stage_without_poisoning_the_journal_or_transaction(
        string member, string? value)
    {
        var store = new FileJournalRecordWriteStore(PathName);
        await using var seed = await Stage(store, Key());
        Assert.Equal(RecordWriteCommitDisposition.Created, (await seed.CommitAsync()).Disposition);
        var before = File.ReadAllBytes(PathName);
        var nextKey = Key("metadata-attempt");
        await using var transaction = await store.BeginAsync();
        await transaction.StageOperationAsync(new(nextKey.OperationId, nextKey.IdempotencyKey, "intent-secret"));
        var lawful = Row(nextKey, "record-2");
        var invalid = member switch
        {
            "intent_encoding_version" => lawful with { IntentEncodingVersion = value! },
            "intent_digest" => lawful with { IntentDigest = value! },
            "schema" => lawful with { Schema = default },
            _ => throw new InvalidOperationException("Unknown literal metadata fixture."),
        };

        await Assert.ThrowsAnyAsync<ArgumentException>(() => transaction.StageRecordAsync(invalid).AsTask());

        Assert.Equal(before, File.ReadAllBytes(PathName));
        var reopened = new FileJournalRecordWriteStore(PathName);
        Assert.Equal("record-1", (await reopened.FindAsync(Key()))!.Receipt!.RecordId);
        Assert.Null(await reopened.FindAsync(nextKey));

        // The refused row never occupied _row: a lawful stage in this same transaction still works.
        await transaction.StageRecordAsync(lawful);
        await transaction.StageAuditAsync(new("audit-2", "worker", At, new byte[] { 0, 255, 7 })
        { AuthoritySnapshot = new byte[] { 1, 2, 3 }, OriginatorId = "alice" });
        Assert.Equal(RecordWriteCommitDisposition.Created, (await transaction.CommitAsync()).Disposition);
        reopened = new FileJournalRecordWriteStore(PathName);
        Assert.Equal("record-1", (await reopened.FindAsync(Key()))!.Receipt!.RecordId);
        var stored = await reopened.FindAsync(nextKey);
        Assert.NotNull(stored);
        var receipt = stored.Receipt!;
        Assert.Equal("record-2", receipt.RecordId);
        Assert.Equal("metadata-attempt", receipt.OperationId);
        Assert.Equal("schema:fixture", receipt.Schema.Value);
    }

    [Theory]
    [InlineData("records-write-intent/1")]
    [InlineData("future-intent/9")]
    public async Task store_preserves_nonblank_metadata_without_imposing_an_encoding_or_registry_policy(string encoding)
    {
        var store = new FileJournalRecordWriteStore(PathName);
        await using var transaction = await store.BeginAsync();
        var key = Key();
        await transaction.StageOperationAsync(new("operation-1", key.IdempotencyKey, "intent-secret"));
        await transaction.StageRecordAsync(Row(key) with { IntentEncodingVersion = encoding });
        await transaction.StageAuditAsync(new("audit-1", "worker", At, new byte[] { 0, 255, 7 })
        { AuthoritySnapshot = new byte[] { 1, 2, 3 }, OriginatorId = "alice" });

        var committed = await transaction.CommitAsync();

        Assert.Equal(RecordWriteCommitDisposition.Created, committed.Disposition);
        var stored = await new FileJournalRecordWriteStore(PathName).FindAsync(key);
        Assert.NotNull(stored);
        Assert.Equal(encoding, stored.IntentEncodingVersion);
        Assert.Equal("intent-secret", stored.IntentDigest);
        var receipt = stored.Receipt!;
        Assert.Equal("record-1", receipt.RecordId);
        Assert.Equal("schema:fixture", receipt.Schema.Value);
    }

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
        var closed = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync().AsTask());
        Assert.Equal("Records transaction is closed.", closed.Message);
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
        catch (Exception error) when (OperatingSystem.IsWindows() && error is UnauthorizedAccessException or IOException) { }
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

    [Theory]
    [InlineData("RecordId")]
    [InlineData("RecordTypeId")]
    [InlineData("Version")]
    [InlineData("DefinitionDigest")]
    public async Task null_required_receipt_metadata_preserves_acceptance_but_refuses_replay(string member)
    {
        await (await Operation(new(PathName))).CreateAsync(new(Tenant, "alice"), Request());
        var journal = Journal();
        var receipt = journal["Entries"]![0]!["Receipt"]!.AsObject();
        Assert.True(receipt.ContainsKey(member));
        receipt[member] = null;
        Rewrite(journal);
        var bytes = File.ReadAllBytes(PathName);
        var reopened = new FileJournalRecordWriteStore(PathName);
        var stored = await reopened.FindAsync(Key());
        Assert.NotNull(stored);
        Assert.Null(stored.Receipt);
        var outcome = await (await Operation(reopened)).CreateAsync(new(Tenant, "alice"), Request());
        Assert.Equal("records.write.receipt_unsupported", outcome.Refusal!.Code);
        Assert.Equal(bytes, File.ReadAllBytes(PathName));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task duplicate_commit_with_one_unknown_intent_version_conflicts_without_publication(bool storedUnknown)
    {
        var store = new FileJournalRecordWriteStore(PathName);
        await using (var first = await Stage(store, Key())) await first.CommitAsync();
        if (storedUnknown)
        {
            var journal = Journal();
            journal["Entries"]![0]!["Row"]!["IntentEncodingVersion"] = "future-intent/9";
            Rewrite(journal);
        }
        var reopened = new FileJournalRecordWriteStore(PathName);
        var original = await reopened.FindAsync(Key());
        var bytes = File.ReadAllBytes(PathName);
        await using var duplicate = await reopened.BeginAsync();
        await duplicate.StageOperationAsync(new("another-command", Key().IdempotencyKey, "intent-secret"));
        await duplicate.StageRecordAsync(Row(Key(), "must-not-publish") with
        { IntentEncodingVersion = storedUnknown ? "records-write-intent/1" : "future-intent/9" });
        await duplicate.StageAuditAsync(new("audit-2", "worker", At, ReadOnlyMemory<byte>.Empty));
        var result = await duplicate.CommitAsync();
        Assert.Equal(RecordWriteCommitDisposition.Conflict, result.Disposition);
        Assert.Equal(original, result.Stored);
        Assert.Equal(original!.Receipt, result.Receipt);
        Assert.Equal(bytes, File.ReadAllBytes(PathName));
    }

    [Theory]
    [InlineData("begin")]
    [InlineData("operation")]
    [InlineData("record")]
    [InlineData("audit")]
    public async Task pre_cancelled_entry_points_reject_without_publishing(string entryPoint)
    {
        var store = new FileJournalRecordWriteStore(PathName);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var transaction = await store.BeginAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            switch (entryPoint)
            {
                case "begin":
                    await using (await store.BeginAsync(cancellation.Token)) { }
                    break;
                case "operation":
                    await transaction.StageOperationAsync(new("operation-1", Key().IdempotencyKey, "intent-secret"), cancellation.Token);
                    break;
                case "record":
                    await transaction.StageRecordAsync(Row(Key()), cancellation.Token);
                    break;
                case "audit":
                    await transaction.StageAuditAsync(new("audit-1", "worker", At, ReadOnlyMemory<byte>.Empty), cancellation.Token);
                    break;
            }
        });
        await transaction.RollbackAsync();
        Assert.Null(await new FileJournalRecordWriteStore(PathName).FindAsync(Key()));
        Assert.False(File.Exists(PathName));
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("rollback")]
    [InlineData("dispose")]
    public async Task terminal_transactions_reject_complete_restaging_of_another_operation(string terminal)
    {
        var store = new FileJournalRecordWriteStore(PathName);
        await using var transaction = await Stage(store, Key());
        if (terminal == "commit") await transaction.CommitAsync();
        else if (terminal == "rollback") await transaction.RollbackAsync();
        else await transaction.DisposeAsync();
        var bytes = File.Exists(PathName) ? File.ReadAllBytes(PathName) : null;
        var next = Key("operation-2");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transaction.StageOperationAsync(new("operation-2", next.IdempotencyKey, "intent-secret"));
            await transaction.StageRecordAsync(Row(next, "record-2"));
            await transaction.StageAuditAsync(new("audit-2", "worker", At, ReadOnlyMemory<byte>.Empty));
            await transaction.CommitAsync();
        });
        Assert.Null(await new FileJournalRecordWriteStore(PathName).FindAsync(next));
        if (bytes is null) Assert.False(File.Exists(PathName));
        else Assert.Equal(bytes, File.ReadAllBytes(PathName));
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("record")]
    [InlineData("audit")]
    public async Task incomplete_transactions_reject_without_publishing(string omitted)
    {
        var store = new FileJournalRecordWriteStore(PathName);
        await using var transaction = await store.BeginAsync();
        if (omitted != "operation") await transaction.StageOperationAsync(new("operation-1", Key().IdempotencyKey, "intent-secret"));
        if (omitted != "record") await transaction.StageRecordAsync(Row(Key()));
        if (omitted != "audit") await transaction.StageAuditAsync(new("audit-1", "worker", At, ReadOnlyMemory<byte>.Empty));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync().AsTask());
        Assert.Equal("Records transaction is incomplete.", error.Message);
        Assert.Null(await new FileJournalRecordWriteStore(PathName).FindAsync(Key()));
        Assert.False(File.Exists(PathName));
    }

    [Theory]
    [InlineData("", "worker", null)]
    [InlineData("audit-1", "", null)]
    [InlineData("audit-1", "worker", " ")]
    [InlineData("audit-1", "worker", "worker")]
    public async Task invalid_audit_identity_is_rejected_before_publication(string auditId, string actor, string? originator)
    {
        var store = new FileJournalRecordWriteStore(PathName);
        await using var transaction = await store.BeginAsync();
        var audit = new KernelAuditEvidence(auditId, actor, At, ReadOnlyMemory<byte>.Empty) { OriginatorId = originator };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => transaction.StageAuditAsync(audit).AsTask());
        await transaction.RollbackAsync();
        Assert.Null(await new FileJournalRecordWriteStore(PathName).FindAsync(Key()));
        Assert.False(File.Exists(PathName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\u00A0")]
    [InlineData("\u2003")]
    public void null_or_blank_journal_path_is_rejected(string? path)
        => Assert.ThrowsAny<ArgumentException>(() => new FileJournalRecordWriteStore(path!));

    [Fact]
    public async Task null_lookup_key_is_rejected()
    {
        var store = new FileJournalRecordWriteStore(PathName);
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.FindAsync(null!).AsTask());
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("record")]
    [InlineData("audit")]
    public async Task null_staging_arguments_are_rejected(string item)
    {
        await using var transaction = await new FileJournalRecordWriteStore(PathName).BeginAsync();
        await Assert.ThrowsAsync<ArgumentNullException>(() => item switch
        {
            "operation" => transaction.StageOperationAsync(null!).AsTask(),
            "record" => transaction.StageRecordAsync(null!).AsTask(),
            _ => transaction.StageAuditAsync(null!).AsTask(),
        });
        Assert.False(File.Exists(PathName));
    }

    [Theory]
    [InlineData("operation", false)]
    [InlineData("record", false)]
    [InlineData("audit", false)]
    [InlineData("operation", true)]
    [InlineData("record", true)]
    [InlineData("audit", true)]
    public async Task each_stage_rejects_a_closed_transaction_with_a_fixed_message(string item, bool committed)
    {
        await using var transaction = await Stage(new(PathName), Key());
        if (committed) await transaction.CommitAsync();
        else await transaction.RollbackAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => item switch
        {
            "operation" => transaction.StageOperationAsync(new("operation-2", Key("operation-2").IdempotencyKey, "intent-secret")).AsTask(),
            "record" => transaction.StageRecordAsync(Row(Key("operation-2"))).AsTask(),
            _ => transaction.StageAuditAsync(new("audit-2", "worker", At, ReadOnlyMemory<byte>.Empty)).AsTask(),
        });
        Assert.Equal("Records transaction is closed.", error.Message);
    }

    [Theory]
    [InlineData("operation", "Records operation is already staged.")]
    [InlineData("record", "Records row is already staged.")]
    [InlineData("audit", "Records audit is already staged.")]
    public async Task duplicate_staging_is_rejected_with_a_fixed_message(string item, string message)
    {
        await using var transaction = await Stage(new(PathName), Key());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => item switch
        {
            "operation" => transaction.StageOperationAsync(new("another-command", Key().IdempotencyKey, "intent-secret")).AsTask(),
            "record" => transaction.StageRecordAsync(Row(Key())).AsTask(),
            _ => transaction.StageAuditAsync(new("audit-2", "worker", At, ReadOnlyMemory<byte>.Empty)).AsTask(),
        });
        Assert.Equal(message, error.Message);
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public async Task staging_copies_the_callers_record_and_audit_buffers()
    {
        byte[] body = [123, 125];
        byte[] payload = [0, 255, 7];
        byte[] snapshot = [1, 2, 3];
        await using var transaction = await new FileJournalRecordWriteStore(PathName).BeginAsync();
        await transaction.StageOperationAsync(new("operation-1", Key().IdempotencyKey, "intent-secret"));
        await transaction.StageRecordAsync(Row(Key()) with { Body = body });
        await transaction.StageAuditAsync(new("audit-1", "worker", At, payload) { AuthoritySnapshot = snapshot });
        Array.Fill(body, (byte)9);
        Array.Fill(payload, (byte)9);
        Array.Fill(snapshot, (byte)9);
        await transaction.CommitAsync();
        var entry = Assert.Single(Journal()["Entries"]!.AsArray())!;
        Assert.Equal("e30=", entry["Row"]!["Body"]!.GetValue<string>());
        Assert.Equal("AP8H", entry["Audit"]!["Payload"]!.GetValue<string>());
        Assert.Equal("AQID", entry["Audit"]!["AuthoritySnapshot"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("duplicate-key")]
    [InlineData("key-mismatch")]
    [InlineData("invalid-operation")]
    [InlineData("invalid-audit")]
    public async Task corrupt_journal_evidence_refuses_reopen_without_changing_the_file(string corruption)
    {
        await using (var transaction = await Stage(new(PathName), Key())) await transaction.CommitAsync();
        var journal = Journal();
        var entries = journal["Entries"]!.AsArray();
        var entry = entries[0]!;
        switch (corruption)
        {
            case "duplicate-key": entries.Add(entry.DeepClone()); break;
            case "key-mismatch": entry["Key"] = "another-key"; break;
            case "invalid-operation": entry["Operation"]!["CommandId"] = " "; break;
            case "invalid-audit": entry["Audit"]!["AuditId"] = " "; break;
        }
        Rewrite(journal);
        var bytes = File.ReadAllBytes(PathName);
        var error = Assert.Throws<InvalidDataException>(() => new FileJournalRecordWriteStore(PathName));
        Assert.Equal("Records journal is invalid.", error.Message);
        Assert.Null(error.InnerException);
        Assert.Equal(bytes, File.ReadAllBytes(PathName));
    }

    private static RecordCreateRequest Request(string asset = "A-1")
        => new("eam.asset-class", "operation-1", new JsonObject { ["asset_tag"] = asset, ["quantity"] = 3, ["notes"] = "spare" });

    private static async Task<RecordWriteOperation> Operation(FileJournalRecordWriteStore store, bool accepted = false)
    {
        var host = Host();
        await host.Records.CreateDraftAsync(accepted ? AssetClass("1.0.0") : RoleGatedAssetClass("1.0.0"), "create");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        return new(new AccessProvider(new Gate()), host.Records, host.Registry, store, store, new Clock(),
            Harborline.Blocks.BuilderDefinitions.Tests.RecordWriteFieldRuntimeTests.FixtureKinds(),
            Harborline.Blocks.BuilderDefinitions.Tests.RecordWriteFieldRuntimeTests.FixtureDomains(new Clock()));
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
