using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Foundation.Authorization;
using Harborline.Kernel.SchemaValidation;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordWriteJournalBindingTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class FileJournalFormatTransitionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "q32-transition-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_directory, "records.json");

    private byte[] OldCopy(string relative = "Q31/journal/records.json")
    {
        var original = File.ReadAllBytes(Fixture(relative));
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(PathName, original);
        return original;
    }

    [Fact]
    public async Task authentic_old_four_entries_keep_raw_spans_rows_receipts_and_replay_on_conversion_and_later_appends()
    {
        var original = OldCopy();
        using var old = JsonDocument.Parse(original);
        var originals = old.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
        Assert.Equal(4, originals.Length); // Independent authentic capture count, not candidate output.
        var store = new FileJournalRecordWriteStore(PathName);
        var settlements = new List<RecordWriteCommittedOperation>();
        var rows = new List<RecordWriteRow>();
        foreach (var entry in originals)
        {
            var row = entry.GetProperty("Row");
            var key = new RecordWriteOperationKey(row.GetProperty("Tenant").GetString()!, row.GetProperty("ActorId").GetString()!,
                row.GetProperty("OperationId").GetString()!);
            settlements.Add((await store.FindAsync(key))!);
            AssertOriginalReceipt(entry.GetProperty("Receipt"), settlements[^1].Receipt);
            rows.Add((await store.FindAsync(row.GetProperty("Tenant").GetString()!, row.GetProperty("RecordId").GetString()!))!);
        }
        Assert.Equal(original, File.ReadAllBytes(PathName)); // Open/read/receipt lookup did not convert.
        for (var append = 0; append < 3; append++)
        {
            Assert.Equal(RecordWriteCommitDisposition.Created,
                (await Append(store, Row("new-" + append, "append-" + append))).Disposition);
            using var converted = JsonDocument.Parse(File.ReadAllBytes(PathName));
            Assert.Equal("records-write-file-journal/2", converted.RootElement.GetProperty("Format").GetString());
            Assert.Equal(4, converted.RootElement.GetProperty("LegacyEntryCount").GetInt32());
            var entries = converted.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
            Assert.Equal(5 + append, entries.Length);
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal("legacy/1", entries[i].GetProperty("Tag").GetString());
                Assert.Equal(Encoding.UTF8.GetBytes(originals[i].GetRawText()), Encoding.UTF8.GetBytes(entries[i].GetProperty("Legacy").GetRawText()));
                var row = rows[i];
                var key = new RecordWriteOperationKey(row.Tenant, row.ActorId, row.OperationId);
                Assert.Equal(settlements[i], await store.FindAsync(key));
                AssertOriginalReceipt(originals[i].GetProperty("Receipt"), (await store.FindAsync(key))!.Receipt);
                var current = (await store.FindAsync(row.Tenant, row.RecordId))!;
                Assert.Null(current.PublicationVersionId);
                Assert.Equal(row with { Body = default }, current with { Body = default });
                Assert.Equal(row.Body.ToArray(), current.Body.ToArray());
                var bytes = File.ReadAllBytes(PathName);
                var replay = await Append(store, row with { PublicationVersionId = "1.0.0" });
                Assert.Equal(RecordWriteCommitDisposition.Replayed, replay.Disposition);
                Assert.Equal(settlements[i].Receipt, replay.Receipt);
                AssertOriginalReceipt(originals[i].GetProperty("Receipt"), replay.Receipt);
                var conflict = await Append(store, row with { PublicationVersionId = "1.0.0", IntentDigest = "different-intent" });
                Assert.Equal(RecordWriteCommitDisposition.Conflict, conflict.Disposition);
                Assert.Equal(settlements[i].Receipt, conflict.Receipt);
                AssertOriginalReceipt(originals[i].GetProperty("Receipt"), conflict.Receipt);
                Assert.Equal(bytes, File.ReadAllBytes(PathName));
            }
        }
        Assert.Equal(original, File.ReadAllBytes(Fixture("Q31/journal/records.json")));
    }

    private static void AssertOriginalReceipt(JsonElement original, RecordWriteReceipt? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(original.GetProperty("RecordId").GetString(), actual.RecordId);
        Assert.Equal(original.GetProperty("OperationId").GetString(), actual.OperationId);
        Assert.Equal(original.GetProperty("RecordTypeId").GetString(), actual.RecordTypeId);
        Assert.Equal(original.GetProperty("Version").GetString(), actual.Version);
        Assert.Equal(original.GetProperty("DefinitionDigest").GetString(), actual.DefinitionDigest);
        Assert.Equal(original.GetProperty("Schema").GetString(), actual.Schema.Value);
        Assert.True(DateTimeOffset.Parse(original.GetProperty("RecordedAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            .EqualsExact(actual.RecordedAt));
    }

    [Fact]
    public async Task authentic_old_publications_and_cold_registries_bind_identical_current_rows_before_and_after_conversion()
    {
        var original = OldCopy();
        var host = RecordTypeDefinitionStoreTests.Host();
        foreach (var item in new[] { ("eam.asset-class", "1.0.0", 0L), ("eam.asset-class", "1.1.0", 2L), ("eam.meter-class", "1.0.0", 0L) })
        {
            var key = RecordTypeDefinitionStore.KeyOf("tenant-a", item.Item1);
            var body = File.ReadAllText(Fixture("Q31/publications/record-type." + item.Item1 + "@" + item.Item2 + ".json"));
            await host.Catalogue.SaveDraftAsync(new(key, item.Item2, item.Item2, body), item.Item3, "capture-draft-" + item.Item1 + item.Item2);
            await host.Catalogue.PublishAsync(key, item.Item2, item.Item3 + 1, "capture-publish-" + item.Item1 + item.Item2);
        }
        using var captured = JsonDocument.Parse(original);
        var store = new FileJournalRecordWriteStore(PathName);
        for (var phase = 0; phase < 2; phase++)
        {
            if (phase == 1) await Append(store);
            var bytes = File.ReadAllBytes(PathName);
            var cold = new InMemorySchemaRegistry(fieldKindRuntime: RecordWriteFieldRuntimeTests.FixtureKinds());
            var records = new RecordTypeDefinitionStore(host.Catalogue, host.Compiler, host.Defaults, cold,
                RecordTypeDefinitionStoreTests.Window, roles: RecordTypeDefinitionStoreTests.CreationRoles);
            var read = new RecordReadOperation(store, new AccessProvider(new ReadGate()), records, TimeProvider.System);
            foreach (var entry in captured.RootElement.GetProperty("Entries").EnumerateArray())
            {
                var row = entry.GetProperty("Row");
                var id = row.GetProperty("RecordId").GetString()!;
                var result = await read.ReadAsync(new("tenant-a", "reader"), new(id));
                Assert.True(result.Found);
                Assert.Equal(row.GetProperty("RecordTypeId").GetString(), result.Record!.RecordTypeId);
                Assert.Equal(row.GetProperty("Version").GetString(), result.Record.Version);
                Assert.Equal(row.GetProperty("DefinitionDigest").GetString(), result.Record.DefinitionDigest);
                Assert.Equal(row.GetProperty("Schema").GetString(), result.Record.Schema.Value);
                Assert.Equal(Convert.FromBase64String(row.GetProperty("Body").GetString()!), result.Record.Body.ToArray());
                Assert.True(DateTimeOffset.Parse(row.GetProperty("RecordedAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture)
                    .EqualsExact(result.Record.RecordedAt));
                Assert.Null((await store.FindAsync("tenant-a", id))!.PublicationVersionId);
            }
            Assert.Equal(bytes, File.ReadAllBytes(PathName));
        }
        Assert.Equal(original, File.ReadAllBytes(Fixture("Q31/journal/records.json")));
    }

    private sealed class ReadGate : IAuthorizationDecider
    {
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuthorizationDecisionEvidence(request, true, "None", "literal-read-grant", [], []));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("missing")]
    [InlineData("unreadable")]
    public async Task legacy_unreadable_receipts_remain_unsupported_after_conversion(string variant)
    {
        OldCopy();
        var root = JsonNode.Parse(File.ReadAllBytes(PathName))!;
        var entry = root["Entries"]![0]!.AsObject();
        if (variant == "missing") entry.Remove("Receipt");
        else entry["Receipt"] = variant == "null" ? null : JsonValue.Create("old-unreadable");
        File.WriteAllText(PathName, root.ToJsonString());
        var store = new FileJournalRecordWriteStore(PathName);
        var row = entry["Row"]!;
        var key = new RecordWriteOperationKey(row["Tenant"]!.GetValue<string>(), row["ActorId"]!.GetValue<string>(), row["OperationId"]!.GetValue<string>());
        var before = await store.FindAsync(key);
        Assert.NotNull(before); Assert.Null(before.Receipt);
        var raw = entry.ToJsonString();
        await Append(store);
        Assert.Equal(before, await store.FindAsync(key));
        Assert.Null((await store.FindAsync(key))!.Receipt);
        using var after = JsonDocument.Parse(File.ReadAllBytes(PathName));
        Assert.Equal(raw, after.RootElement.GetProperty("Entries")[0].GetProperty("Legacy").GetRawText());
    }

    [Fact]
    public async Task cancellation_after_flush_keeps_original_and_retry_converts_once_then_replays()
    {
        var original = OldCopy();
        using var cancellation = new CancellationTokenSource();
        var reached = 0;
        var store = new FileJournalRecordWriteStore(PathName, () => { reached++; cancellation.Cancel(); });
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Append(store, cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken); Assert.Equal(1, reached);
        Assert.Equal(original, File.ReadAllBytes(PathName)); Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        var retry = new FileJournalRecordWriteStore(PathName);
        var created = await Append(retry);
        var converted = File.ReadAllBytes(PathName);
        var replay = await Append(new FileJournalRecordWriteStore(PathName));
        Assert.Equal(RecordWriteCommitDisposition.Created, created.Disposition);
        Assert.Equal(RecordWriteCommitDisposition.Replayed, replay.Disposition);
        Assert.Equal(created.Receipt, replay.Receipt); Assert.Equal(converted, File.ReadAllBytes(PathName));
    }

    [Fact]
    public async Task file_move_failure_at_the_pre_replace_boundary_preserves_source_and_retry()
    {
        var original = OldCopy();
        var reached = 0;
        var store = new FileJournalRecordWriteStore(PathName, () =>
        {
            reached++;
            // A real BCL move to an existing directory fails; the source journal remains untouched.
            File.Move(PathName, _directory);
        });
        var error = await Record.ExceptionAsync(() => Append(store));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(1, reached); Assert.Equal(original, File.ReadAllBytes(PathName));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Equal(RecordWriteCommitDisposition.Created, (await Append(new FileJournalRecordWriteStore(PathName))).Disposition);
    }

    [Theory]
    [InlineData("a-ignored-invalid-utf8")]
    [InlineData("b-ignored-lone-surrogate")]
    public async Task authentic_old_reader_positive_inputs_still_read_but_conversion_refuses_without_repair(string variant)
    {
        var original = OldCopy("Q44/variants/" + variant + "/records.json");
        // The authenticated Q44 evidence records old5ec acceptance; this assertion executes the candidate /1 path.
        var store = new FileJournalRecordWriteStore(PathName);
        await Assert.ThrowsAsync<InvalidDataException>(() => Append(store));
        Assert.Equal(original, File.ReadAllBytes(PathName)); Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData("\"Ignored\":\"\\ud800\",")]
    [InlineData("\"Ignored\\ud800\":0,")]
    [InlineData("\"Ignored\":{\"nested\":[\"\\udc00\"]},")]
    [InlineData("\"Ignored\":{\"nested\\ud800\":[0]},")]
    public async Task conversion_scans_ignored_root_and_nested_names_and_values(string inserted)
    {
        var original = OldCopy();
        var poisoned = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Insert(1, inserted));
        File.WriteAllBytes(PathName, poisoned);
        var store = new FileJournalRecordWriteStore(PathName);
        await Assert.ThrowsAsync<InvalidDataException>(() => Append(store));
        Assert.Equal(poisoned, File.ReadAllBytes(PathName)); Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\u0085\u3000")]
    public async Task a_new_invalid_binding_never_creates_files_or_converts_a_legacy_journal(string? publication)
    {
        var original = OldCopy();
        var store = new FileJournalRecordWriteStore(PathName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Append(store, Row() with { PublicationVersionId = publication }));
        Assert.Equal(original, File.ReadAllBytes(PathName)); Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task every_invalid_encoder_vector_leaves_the_authentic_old_journal_unchanged_on_new_write()
    {
        var original = OldCopy();
        using var vectors = JsonDocument.Parse(File.ReadAllBytes(Fixture("RecordJournalBinding-v2-vectors.json")));
        foreach (var vector in vectors.RootElement.GetProperty("invalid_inputs").EnumerateArray())
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                var row = FromInputs(vector.GetProperty("inputs"));
                await Append(new FileJournalRecordWriteStore(PathName), row);
            });
            Assert.True(error is InvalidOperationException or ArgumentException, vector.GetProperty("id").GetString());
            Assert.Equal(original, File.ReadAllBytes(PathName)); Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        }
    }

    [Fact]
    public async Task a_valid_legacy_downgrade_is_accepted_and_loses_the_bound_operation_documenting_the_cutover_hazard()
    {
        var old = OldCopy();
        await Append(new FileJournalRecordWriteStore(PathName));
        Assert.NotNull(await new FileJournalRecordWriteStore(PathName).FindAsync(new RecordWriteOperationKey("acme", "alice", "new-operation")));
        // Models an old process renaming a valid previously loaded /1 snapshot over /2. No downgrade detector is claimed.
        File.WriteAllBytes(PathName, old);
        var downgraded = new FileJournalRecordWriteStore(PathName);
        Assert.Null(await downgraded.FindAsync(new RecordWriteOperationKey("acme", "alice", "new-operation")));
        Assert.Equal(old, File.ReadAllBytes(PathName));
    }

    [Fact]
    public async Task Legacy_depth_64_converts_and_reopens_without_losing_rows_or_receipts()
    {
        var original = OldCopy();
        var depth64 = LegacyWithIgnoredArrays(original, 61);
        using var source = JsonDocument.Parse(depth64);
        var originals = source.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
        Assert.Equal(4, originals.Length);
        File.WriteAllBytes(PathName, depth64);
        var store = new FileJournalRecordWriteStore(PathName);
        var rows = new List<RecordWriteRow>();
        foreach (var entry in originals)
        {
            var row = entry.GetProperty("Row");
            rows.Add((await store.FindAsync(row.GetProperty("Tenant").GetString()!,
                row.GetProperty("RecordId").GetString()!))!);
        }
        await AssertDepthLegacyRows(store, originals, rows);
        Assert.Equal(depth64, File.ReadAllBytes(PathName));

        for (var append = 0; append < 2; append++)
        {
            var added = Row("depth-record-" + append, "depth-operation-" + append);
            var committed = await Append(store, added);
            Assert.Equal(RecordWriteCommitDisposition.Created, committed.Disposition);
            Assert.Equal(added.RecordId, committed.Receipt!.RecordId);
            var converted = File.ReadAllBytes(PathName);
            AssertDepthPrefix(depth64, converted, 5 + append);
            store = new FileJournalRecordWriteStore(PathName);
            await AssertDepthLegacyRows(store, originals, rows);
            for (var retained = 0; retained <= append; retained++)
            {
                var expected = Row("depth-record-" + retained, "depth-operation-" + retained);
                var current = await store.FindAsync("acme", expected.RecordId);
                Assert.NotNull(current);
                Assert.Equal(expected with { Body = default }, current with { Body = default });
                Assert.Equal("{}"u8.ToArray(), current.Body.ToArray());
                var settlement = await store.FindAsync(new RecordWriteOperationKey("acme", "alice", expected.OperationId));
                Assert.NotNull(settlement);
                Assert.Equal(expected.RecordId, settlement.Receipt.RecordId);
                Assert.Equal(expected.OperationId, settlement.Receipt.OperationId);
            }
            Assert.Equal(converted, File.ReadAllBytes(PathName));
        }
        Assert.Equal(original, File.ReadAllBytes(Fixture("Q31/journal/records.json")));
    }

    [Fact]
    public async Task Legacy_depth_65_remains_refused_by_every_journal_path()
    {
        var original = OldCopy();
        var depth65 = LegacyWithIgnoredArrays(original, 62);
        Assert.Throws<JsonException>(() => { using var document = JsonDocument.Parse(depth65); });
        await AllPaths(depth65, false);
        Assert.Equal(original, File.ReadAllBytes(Fixture("Q31/journal/records.json")));
    }

    [Fact]
    public async Task Independent_v2_depth_65_is_readable_replayable_and_appendable()
    {
        var original = OldCopy();
        var legacy64 = LegacyWithIgnoredArrays(original, 61);
        var bound65 = WrapDepthLegacy(legacy64);
        using var source = JsonDocument.Parse(legacy64);
        var originals = source.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
        File.WriteAllBytes(PathName, bound65);
        var store = new FileJournalRecordWriteStore(PathName);
        var rows = new List<RecordWriteRow>();
        foreach (var entry in originals)
        {
            var row = entry.GetProperty("Row");
            rows.Add((await store.FindAsync(row.GetProperty("Tenant").GetString()!,
                row.GetProperty("RecordId").GetString()!))!);
        }
        await AssertDepthLegacyRows(store, originals, rows);
        Assert.Equal(bound65, File.ReadAllBytes(PathName));
        AssertDepthPrefix(legacy64, bound65, 5);
        var added = Row("independent-depth-record", "independent-depth-operation");
        var committed = await Append(store, added);
        Assert.Equal(RecordWriteCommitDisposition.Created, committed.Disposition);
        Assert.Equal("independent-depth-record", committed.Receipt!.RecordId);
        var appended = File.ReadAllBytes(PathName);
        AssertDepthPrefix(legacy64, appended, 6);
        store = new FileJournalRecordWriteStore(PathName);
        await AssertDepthLegacyRows(store, originals, rows);
        var current = await store.FindAsync("acme", "independent-depth-record");
        Assert.NotNull(current);
        Assert.Equal(added with { Body = default }, current with { Body = default });
        Assert.Equal("{}"u8.ToArray(), current.Body.ToArray());
        Assert.Equal(appended, File.ReadAllBytes(PathName));
        Assert.Equal(original, File.ReadAllBytes(Fixture("Q31/journal/records.json")));
    }

    [Fact]
    public async Task Independent_v2_depth_66_is_refused_by_every_journal_path()
    {
        var original = OldCopy();
        var depth66 = WrapDepthLegacy(LegacyWithIgnoredArrays(original, 62));
        Assert.Throws<JsonException>(() =>
        {
            using var document = JsonDocument.Parse(depth66, new JsonDocumentOptions { MaxDepth = 65 });
        });
        await AllPaths(depth66, false);
        Assert.Equal(original, File.ReadAllBytes(Fixture("Q31/journal/records.json")));
    }

    [Fact]
    public async Task Commit_refuses_an_overdepth_replacement_before_publication()
    {
        var original = OldCopy();
        var reached = 0;
        var store = new FileJournalRecordWriteStore(PathName, () => reached++);
        var depth66 = WrapDepthLegacy(LegacyWithIgnoredArrays(original, 62));
        File.WriteAllBytes(PathName, depth66);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Append(store, Row("refused-depth-record", "refused-depth-operation")));
        Assert.Equal(0, reached);
        Assert.Equal(depth66, File.ReadAllBytes(PathName));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Equal(original, File.ReadAllBytes(Fixture("Q31/journal/records.json")));
    }

    private static byte[] LegacyWithIgnoredArrays(byte[] original, int arrays)
    {
        using var document = JsonDocument.Parse(original);
        var first = document.RootElement.GetProperty("Entries")[0].GetRawText();
        var extended = first[..^1] + ",\"IgnoredDepth\":" + new string('[', arrays) +
            "0" + new string(']', arrays) + "}";
        return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original)
            .Replace(first, extended, StringComparison.Ordinal));
    }

    private static byte[] WrapDepthLegacy(byte[] legacy)
    {
        using var document = JsonDocument.Parse(legacy, new JsonDocumentOptions { MaxDepth = 65 });
        var entries = document.RootElement.GetProperty("Entries").EnumerateArray()
            .Select(entry => "{\"Tag\":\"legacy/1\",\"Legacy\":" + entry.GetRawText() + "}");
        return Encoding.UTF8.GetBytes("{\"Format\":\"records-write-file-journal/2\",\"LegacyEntryCount\":4,\"Entries\":[" +
            string.Join(",", entries) + "," + Bound().ToJsonString() + "]}");
    }

    private static void AssertDepthPrefix(byte[] legacy, byte[] converted, int count)
    {
        using var original = JsonDocument.Parse(legacy);
        using var document = JsonDocument.Parse(converted, new JsonDocumentOptions { MaxDepth = 65 });
        Assert.Equal("records-write-file-journal/2", document.RootElement.GetProperty("Format").GetString());
        Assert.Equal(4, document.RootElement.GetProperty("LegacyEntryCount").GetInt32());
        var oldEntries = original.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
        var entries = document.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
        Assert.Equal(4, oldEntries.Length);
        Assert.Equal(count, entries.Length);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal("legacy/1", entries[i].GetProperty("Tag").GetString());
            Assert.Equal(Encoding.UTF8.GetBytes(oldEntries[i].GetRawText()),
                Encoding.UTF8.GetBytes(entries[i].GetProperty("Legacy").GetRawText()));
        }
    }

    private async Task AssertDepthLegacyRows(FileJournalRecordWriteStore store, JsonElement[] originals,
        IReadOnlyList<RecordWriteRow> rows)
    {
        for (var i = 0; i < 4; i++)
        {
            var original = originals[i].GetProperty("Row");
            var tenant = original.GetProperty("Tenant").GetString()!;
            var recordId = original.GetProperty("RecordId").GetString()!;
            var key = new RecordWriteOperationKey(tenant, original.GetProperty("ActorId").GetString()!,
                original.GetProperty("OperationId").GetString()!);
            var settlement = await store.FindAsync(key);
            Assert.NotNull(settlement);
            AssertOriginalReceipt(originals[i].GetProperty("Receipt"), settlement.Receipt);
            var current = await store.FindAsync(tenant, recordId);
            Assert.NotNull(current);
            Assert.Equal(tenant, current.Tenant);
            Assert.Equal(recordId, current.RecordId);
            Assert.Null(current.PublicationVersionId);
            Assert.Equal(rows[i] with { Body = default }, current with { Body = default });
            Assert.Equal(Convert.FromBase64String(original.GetProperty("Body").GetString()!), current.Body.ToArray());
            var before = File.ReadAllBytes(PathName);
            var replay = await Append(store, rows[i] with { PublicationVersionId = "1.0.0" });
            Assert.Equal(RecordWriteCommitDisposition.Replayed, replay.Disposition);
            AssertOriginalReceipt(originals[i].GetProperty("Receipt"), replay.Receipt);
            var conflict = await Append(store, rows[i] with
            {
                PublicationVersionId = "1.0.0",
                IntentDigest = "different-depth-intent"
            });
            Assert.Equal(RecordWriteCommitDisposition.Conflict, conflict.Disposition);
            AssertOriginalReceipt(originals[i].GetProperty("Receipt"), conflict.Receipt);
            Assert.Equal(before, File.ReadAllBytes(PathName));
        }
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
