using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class RecordWriteJournalBindingTests
{
    // Independent SPEC V01. These expected bytes/hashes are never computed by the implementation under test.
    internal const string Digest = "090e7e663979ab9ef3853467f8200aac3cb3db343140c0d066dccccdb9d299e5";
    internal const string V01Hash = "85b2c8e9600272a9d07fd16f4c0af87fc49b2f6f7cc235c88336399f8307be24";
    internal static readonly DateTimeOffset At = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void additive_publication_identity_preserves_the_thirteen_and_seven_field_public_constructor_shapes()
    {
        var legacy = new RecordWriteRow("tenant", "record", "type", "1.0.0", "digest", new("opaque-schema"), "actor", "operation",
            null, "intent-version", "intent-digest", "{}"u8.ToArray(), new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var (tenant, record, type, version, digest, schema, actor, operation, requested, encoding, intent, body, at) = legacy;
        Assert.Equal(("tenant", "record", "type", "1.0.0", "digest", "opaque-schema", "actor", "operation", "intent-version", "intent-digest"),
            (tenant, record, type, version, digest, schema.Value, actor, operation, encoding, intent));
        Assert.Null(requested); Assert.Null(legacy.PublicationVersionId); Assert.Equal("{}"u8.ToArray(), body.ToArray());
        Assert.Equal(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero), at);
        var bound = legacy with { PublicationVersionId = "immutable-publication" };
        Assert.Equal("immutable-publication", (bound with { Body = "[]"u8.ToArray() }).PublicationVersionId);
        Assert.Null(legacy.PublicationVersionId);
        var receipt = new RecordWriteReceipt("record", "operation", "type", "1.0.0", "digest", new("opaque-schema"), at);
        var (receiptRecord, receiptOperation, receiptType, receiptVersion, receiptDigest, receiptSchema, receiptAt) = receipt;
        Assert.Equal(("record", "operation", "type", "1.0.0", "digest", "opaque-schema"),
            (receiptRecord, receiptOperation, receiptType, receiptVersion, receiptDigest, receiptSchema.Value));
        Assert.Equal(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero), receiptAt);
    }

    internal static string Fixture(string relative, [CallerFilePath] string source = "")
        => Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", relative.Replace('/', Path.DirectorySeparatorChar));

    public static IEnumerable<object[]> ValidVectors()
    {
        using var vectors = JsonDocument.Parse(File.ReadAllBytes(Fixture("RecordJournalBinding-v2-vectors.json")));
        foreach (var vector in vectors.RootElement.GetProperty("valid").EnumerateArray()) yield return [vector.Clone()];
    }

    [Theory]
    [MemberData(nameof(ValidVectors))]
    public void exact_encoder_matches_all_independent_framing_and_hash_vectors(JsonElement vector)
    {
        var row = FromInputs(vector.GetProperty("inputs"));
        Assert.Equal(Convert.FromHexString(vector.GetProperty("encoded_hex").GetString()!), RecordWriteJournalBinding.Encode(row));
        Assert.Equal(vector.GetProperty("binding_hash").GetString(), RecordWriteJournalBinding.Hash(row));
        Assert.Equal(vector.GetProperty("encoded_byte_length").GetInt32(), RecordWriteJournalBinding.Encode(row).Length);
    }

    [Fact]
    public async Task invalid_input_vectors_refuse_without_replacement_or_trimming()
    {
        using var vectors = JsonDocument.Parse(File.ReadAllBytes(Fixture("RecordJournalBinding-v2-vectors.json")));
        foreach (var vector in vectors.RootElement.GetProperty("invalid_inputs").EnumerateArray())
        {
            var error = Record.Exception(() => { _ = RecordWriteJournalBinding.Hash(FromInputs(vector.GetProperty("inputs"))); });
            Assert.True(error is InvalidOperationException or ArgumentException, vector.GetProperty("id").GetString());
            var originalBound = Bound().ToJsonString();
            var raw = originalBound;
            var fields = new Dictionary<string, string> { ["tenant"] = "Tenant", ["record_type_id"] = "RecordTypeId",
                ["version_id"] = "PublicationVersionId", ["version"] = "Version", ["definition_digest"] = "DefinitionDigest", ["schema_id"] = "Schema" };
            var baseline = Bound()["Row"]!;
            foreach (var pair in fields)
            {
                var text = InputText(vector.GetProperty("inputs").GetProperty(pair.Key));
                var literal = text is null ? "null" : "\"" + string.Concat(text.Select(c => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture))) + "\"";
                raw = raw.Replace("\"" + pair.Value + "\":" + baseline[pair.Value]!.ToJsonString(), "\"" + pair.Value + "\":" + literal, StringComparison.Ordinal);
            }
            await AllPaths(Encoding.UTF8.GetBytes(BaseJournal().ToJsonString().Replace(originalBound, raw, StringComparison.Ordinal)), false);
        }
    }

    [Fact]
    public async Task H01_through_H05_fail_all_journal_paths_and_all_eight_wrong_encodings_differ()
    {
        using var vectors = JsonDocument.Parse(File.ReadAllBytes(Fixture("RecordJournalBinding-v2-vectors.json")));
        foreach (var control in vectors.RootElement.GetProperty("wrong_encoding_controls").EnumerateArray())
        {
            Assert.NotEqual(control.GetProperty("wrong_hash").GetString(), RecordWriteJournalBinding.Hash(Row()));
            Assert.NotEqual(control.GetProperty("encoded_hex").GetString(), Convert.ToHexStringLower(RecordWriteJournalBinding.Encode(Row())));
        }
        foreach (var vector in vectors.RootElement.GetProperty("invalid_stored_hash").EnumerateArray())
        {
            var journal = BaseJournal(); var bound = journal["Entries"]![1]!;
            bound["Binding"]!["Hash"] = vector.GetProperty("stored_hash").GetString();
            if (vector.GetProperty("id").GetString() == "H04")
            { bound["Row"]!["Schema"] = "schema:example-b"; bound["Receipt"]!["Schema"] = "schema:example-b"; }
            if (vector.GetProperty("id").GetString() == "H05") bound["Row"]!["PublicationVersionId"] = "1.0.1";
            await AllPaths(Encoding.UTF8.GetBytes(journal.ToJsonString()), false);
        }
    }

    [Theory]
    [InlineData("Row", "OriginatorId")]
    [InlineData("Row", "AcceptanceAuditId")]
    [InlineData("Audit", "AuthoritySnapshot")]
    [InlineData("Audit", "OriginatorId")]
    [InlineData("Audit", "Payload")]
    [InlineData("Row", "Body")]
    public async Task nullable_members_are_present_and_byte_payload_null_is_not_empty(string owner, string member)
    {
        var root = BaseJournal(); var owned = root["Entries"]![1]![owner]!.AsObject();
        Assert.True(owned.Remove(member));
        await AllPaths(Encoding.UTF8.GetBytes(root.ToJsonString()), false);
        if (member is "Body" or "Payload" or "AuthoritySnapshot")
        {
            owned[member] = null;
            await AllPaths(Encoding.UTF8.GetBytes(root.ToJsonString()), false);
        }
    }

    [Theory]
    [InlineData("Operation", "CommandId")]
    [InlineData("Row", "RecordId")]
    [InlineData("Audit", "ActorId")]
    [InlineData("Receipt", "RecordId")]
    [InlineData("Binding", "Hash")]
    public async Task decoded_duplicate_and_unknown_members_refuse_inside_every_owned_object(string owner, string member)
    {
        var root = BaseJournal(); var owned = root["Entries"]![1]![owner]!.AsObject();
        owned["Extra"] = 0;
        await AllPaths(Encoding.UTF8.GetBytes(root.ToJsonString()), false);
        owned.Remove("Extra");
        // Duplicate the exact decoded name within the selected object, not a global serializer setting.
        var duplicate = owned.ToJsonString().Insert(1, "\"" + member + "\":" + owned[member]!.ToJsonString() + ",");
        var raw = root.ToJsonString().Replace(owned.ToJsonString(), duplicate, StringComparison.Ordinal);
        await AllPaths(Encoding.UTF8.GetBytes(raw), false);
    }

    [Theory]
    [InlineData("RecordId")]
    [InlineData("OperationId")]
    [InlineData("RecordTypeId")]
    [InlineData("Version")]
    [InlineData("DefinitionDigest")]
    [InlineData("Schema")]
    [InlineData("RecordedAt")]
    public async Task a_corrupt_receipt_in_another_entry_blocks_even_the_valid_requested_entry(string member)
    {
        var root = BaseJournal(); var other = Bound();
        other["Key"] = "other-key"; other["Operation"]!["IdempotencyKey"] = "other-key";
        other["Receipt"]![member] = member == "RecordedAt" ? "2030-01-02T04:04:05+01:00" : "different";
        root["Entries"]!.AsArray().Add(other);
        await AllPaths(Encoding.UTF8.GetBytes(root.ToJsonString()), false);
    }

    [Theory]
    [InlineData("Tenant", "Acme")]
    [InlineData("Tenant", "acme ")]
    [InlineData("Tenant", "caf\u00e9")]
    [InlineData("Tenant", "cafe\u0301")]
    [InlineData("RecordTypeId", "Sales.customer")]
    [InlineData("RecordTypeId", "sales.customer ")]
    [InlineData("PublicationVersionId", "1.0.1")]
    [InlineData("PublicationVersionId", "1.0.0 ")]
    [InlineData("PublicationVersionId", " ")]
    [InlineData("Version", "1.0.1")]
    [InlineData("Version", "1.0.0 ")]
    [InlineData("Schema", "Schema:example-a")]
    [InlineData("Schema", "schema:example-a ")]
    public async Task component_changes_fail_even_with_matching_receipt_fields(string member, string changed)
    {
        var root = BaseJournal(); var entry = root["Entries"]![1]!;
        Assert.NotEqual(changed, entry["Row"]![member]!.GetValue<string>());
        entry["Row"]![member] = changed;
        if (entry["Receipt"]!.AsObject().ContainsKey(member)) entry["Receipt"]![member] = changed;
        await AllPaths(Encoding.UTF8.GetBytes(root.ToJsonString()), false);
    }

    [Theory]
    [InlineData("Operation", "Fingerprint", "other-intent")]
    [InlineData("Audit", "RecordedAt", "2030-01-02T04:04:05+01:00")]
    [InlineData("Audit", "OriginatorId", "someone-else")]
    public async Task required_operation_and_audit_associations_are_not_inferred(string owner, string member, string changed)
    {
        var root = BaseJournal(); root["Entries"]![1]![owner]![member] = changed;
        await AllPaths(Encoding.UTF8.GetBytes(root.ToJsonString()), false);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("array")]
    [InlineData("missing-field")]
    public async Task malformed_receipts_refuse_instead_of_being_regenerated(string shape)
    {
        var root = BaseJournal(); var entry = root["Entries"]![1]!;
        if (shape == "string") entry["Receipt"] = "not-a-receipt";
        else if (shape == "array") entry["Receipt"] = new JsonArray();
        else entry["Receipt"]!.AsObject().Remove("OperationId");
        await AllPaths(Encoding.UTF8.GetBytes(root.ToJsonString()), false);
    }

    [Fact]
    public async Task every_SPEC_owned_member_is_required_even_if_its_value_may_be_null()
    {
        var required = new Dictionary<string, string[]>
        {
            ["Operation"] = ["CommandId", "IdempotencyKey", "Fingerprint"],
            ["Row"] = ["Tenant", "RecordId", "RecordTypeId", "Version", "DefinitionDigest", "Schema", "ActorId", "OperationId",
                "RequestedVersion", "IntentEncodingVersion", "IntentDigest", "Body", "RecordedAt", "OriginatorId", "AcceptedCommandId", "AcceptanceAuditId", "PublicationVersionId"],
            ["Audit"] = ["AuditId", "ActorId", "RecordedAt", "Payload", "AuthoritySnapshot", "OriginatorId"],
            ["Receipt"] = ["RecordId", "OperationId", "RecordTypeId", "Version", "DefinitionDigest", "Schema", "RecordedAt"],
            ["Binding"] = ["Encoding", "Hash"],
        };
        foreach (var owner in required)
        foreach (var member in owner.Value)
        {
            var root = BaseJournal();
            Assert.True(root["Entries"]![1]![owner.Key]!.AsObject().Remove(member));
            await AllPaths(Encoding.UTF8.GetBytes(root.ToJsonString()), false);
        }
    }

    internal static RecordWriteRow FromInputs(JsonElement input)
    {
        string? Read(string key) => InputText(input.GetProperty(key));
        return new(Read("tenant")!, "bound-record", Read("record_type_id")!, Read("version")!,
            Read("definition_digest")!, new SchemaId(Read("schema_id")!), "alice", "op-new", null,
            "records-write-intent/1", "intent", "{}"u8.ToArray(), At) { PublicationVersionId = Read("version_id") };
    }

    private static string? InputText(JsonElement field)
    {
        if (field.ValueKind == JsonValueKind.Null) return null;
        if (field.ValueKind == JsonValueKind.String) return field.GetString();
        if (field.TryGetProperty("text_utf16_code_units", out var units))
        {
            if (units.ValueKind == JsonValueKind.Null) return null;
            return new string(units.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => (char)int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray());
        }
        return field.GetProperty("text").GetString();
    }

    internal static RecordWriteRow Row(string id = "new-record", string operation = "new-operation")
        => new("acme", id, "sales.customer", "1.0.0", Digest, new("schema:example-a"), "alice", operation, null,
            "records-write-intent/1", "intent", "{}"u8.ToArray(), At) { PublicationVersionId = "1.0.0" };

    internal static async Task<RecordWriteCommit> Append(FileJournalRecordWriteStore store, RecordWriteRow? supplied = null,
        CancellationToken cancellationToken = default)
    {
        var row = supplied ?? Row();
        await using var transaction = await store.BeginAsync(cancellationToken);
        var key = new RecordWriteOperationKey(row.Tenant, row.ActorId, row.OperationId);
        await transaction.StageOperationAsync(new(row.RecordId, key.IdempotencyKey, row.IntentDigest), cancellationToken);
        await transaction.StageRecordAsync(row, cancellationToken);
        await transaction.StageAuditAsync(new("audit-" + row.OperationId, row.ActorId, row.RecordedAt, ReadOnlyMemory<byte>.Empty)
            { OriginatorId = row.OriginatorId }, cancellationToken);
        return await transaction.CommitAsync(cancellationToken);
    }

    internal static JsonObject Bound()
        => JsonNode.Parse("""
        {"Tag":"bound/2","Key":"4:acme5:alice6:op-new","Operation":{"CommandId":"bound-record","IdempotencyKey":"4:acme5:alice6:op-new","Fingerprint":"intent"},"Row":{"Tenant":"acme","RecordId":"bound-record","RecordTypeId":"sales.customer","PublicationVersionId":"1.0.0","Version":"1.0.0","DefinitionDigest":"090e7e663979ab9ef3853467f8200aac3cb3db343140c0d066dccccdb9d299e5","Schema":"schema:example-a","ActorId":"alice","OperationId":"op-new","RequestedVersion":null,"IntentEncodingVersion":"records-write-intent/1","IntentDigest":"intent","Body":"e30=","RecordedAt":"2030-01-02T03:04:05+00:00","OriginatorId":null,"AcceptedCommandId":null,"AcceptanceAuditId":null},"Audit":{"AuditId":"audit-new","ActorId":"alice","RecordedAt":"2030-01-02T03:04:05+00:00","Payload":"","AuthoritySnapshot":"","OriginatorId":null},"Receipt":{"RecordId":"bound-record","OperationId":"op-new","RecordTypeId":"sales.customer","Version":"1.0.0","DefinitionDigest":"090e7e663979ab9ef3853467f8200aac3cb3db343140c0d066dccccdb9d299e5","Schema":"schema:example-a","RecordedAt":"2030-01-02T03:04:05+00:00"},"Binding":{"Encoding":"hlp.records.publication-binding/2","Hash":"85b2c8e9600272a9d07fd16f4c0af87fc49b2f6f7cc235c88336399f8307be24"}}
        """)!.AsObject();

    internal static JsonObject BaseJournal()
    {
        var old = Bound();
        old.Remove("Tag"); old.Remove("Binding"); old["Row"]!.AsObject().Remove("PublicationVersionId");
        old["Key"] = "4:acme5:alice6:op-old";
        old["Operation"]!["IdempotencyKey"] = "4:acme5:alice6:op-old";
        old["Operation"]!["CommandId"] = "legacy-record";
        old["Row"]!["RecordId"] = "legacy-record"; old["Row"]!["OperationId"] = "op-old";
        old["Receipt"]!["RecordId"] = "legacy-record"; old["Receipt"]!["OperationId"] = "op-old";
        return new() { ["Format"] = "records-write-file-journal/2", ["LegacyEntryCount"] = 1,
            ["Entries"] = new JsonArray(new JsonObject { ["Tag"] = "legacy/1", ["Legacy"] = old }, Bound()) };
    }

    public static IEnumerable<object[]> StructureCases() => Enumerable.Range(0, 56).Select(id => new object[] { id });

    [Fact]
    public async Task negative_zero_is_an_integer_token_within_the_approved_nonnegative_count_range()
    {
        var root = BaseJournal();
        root["Entries"]!.AsArray().RemoveAt(0);
        root["LegacyEntryCount"] = 0;
        var raw = root.ToJsonString().Replace("\"LegacyEntryCount\":0", "\"LegacyEntryCount\":-0", StringComparison.Ordinal);
        Assert.Contains("\"LegacyEntryCount\":-0", raw, StringComparison.Ordinal);
        await AllPaths(Encoding.UTF8.GetBytes(raw), true);
    }

    [Theory]
    [MemberData(nameof(StructureCases))]
    public async Task spec_J00_through_J55_have_the_same_outcome_on_all_four_real_paths(int id)
    {
        var root = BaseJournal();
        var entries = root["Entries"]!.AsArray(); var legacy = entries[0]!; var bound = entries[1]!;
        string? raw = null;
        switch (id)
        {
            case 0: break;
            case 1: root["LegacyEntryCount"] = 2; break;
            case 2: root["LegacyEntryCount"] = 0; break;
            case 3: root["LegacyEntryCount"] = 3; break;
            case 4: root["LegacyEntryCount"] = -1; break;
            case 5: root.Remove("LegacyEntryCount"); break;
            case 6: raw = root.ToJsonString().Replace("\"LegacyEntryCount\":1", "\"LegacyEntryCount\":1.0", StringComparison.Ordinal); break;
            case 7: root["LegacyEntryCount"] = "1"; break;
            case 8: raw = root.ToJsonString().Replace("\"LegacyEntryCount\":1", "\"LegacyEntryCount\":1e0", StringComparison.Ordinal); break;
            case 9: root["Entries"] = new JsonArray(bound.DeepClone(), legacy.DeepClone()); break;
            case 10: bound.AsObject().Remove("Tag"); break;
            case 11: bound["Tag"] = "bound/1"; break;
            case 12: legacy["Tag"] = "Legacy/1"; break;
            case 13: bound["Tag"] = 2; break;
            case 14: bound.AsObject().Remove("Binding"); break;
            case 15: bound["Binding"] = null; break;
            case 16: bound["Binding"]!["Encoding"] = "hlp.records.publication-binding/2\0"; break;
            case 17: bound["Binding"]!["Encoding"] = "hlp.records.publication-binding/1"; break;
            case 18: root["Extra"] = 0; break;
            case 19: raw = root.ToJsonString().Replace("\"LegacyEntryCount\":1", "\"LegacyEntryCount\":1,\"LegacyEntryCount\":1", StringComparison.Ordinal); break;
            case 20: root["Format"] = "records-write-file-journal/1"; break;
            case 21: root["Format"] = "records-write-file-journal/3"; break;
            case 22: break;
            case 23: legacy["Legacy"]!["Ignored"] = "INVALID_UTF8"; break;
            case 24: entries.RemoveAt(1); break;
            case 25: bound["Receipt"] = null; break;
            case 26: bound["Receipt"]!["Version"] = "1.0.1"; break;
            case 27: bound["Key"] = "4:acme5:alice6:op-old"; bound["Operation"]!["IdempotencyKey"] = "4:acme5:alice6:op-old"; break;
            case 28: bound["Extra"] = true; break;
            case 29: legacy["Note"] = "x"; break;
            case 30: legacy["Legacy"]!["FutureField"] = 1; break;
            case 31: bound["Row"]!["Schema"] = "schema:example-b"; break;
            case 32: bound["Row"]!["Extra"] = 1; break;
            case 33: bound["Row"]!["Tenant"] = "BAD_HIGH"; raw = root.ToJsonString().Replace("BAD_HIGH", "acme\\ud800", StringComparison.Ordinal); break;
            case 34: bound["Row"]!["ActorId"] = "BAD_LOW"; raw = root.ToJsonString().Replace("BAD_LOW", "alice\\udc00", StringComparison.Ordinal); break;
            case 35: raw = root.ToJsonString().Replace("\"Tenant\":\"acme\"", "\"Tenant\":\"\\u0061cme\"", StringComparison.Ordinal); break;
            case 36: bound["Row"]!.AsObject().Remove("AcceptedCommandId"); break;
            case 37: raw = root.ToJsonString().Replace("\"Hash\":", "\"\\u0048ash\":\"" + V01Hash + "\",\"Hash\":", StringComparison.Ordinal); break;
            case 38: bound["Row"]!.AsObject().Remove("PublicationVersionId"); break;
            case 39: bound["Binding"]!["VersionId"] = "1.0.0"; break;
            case 40: bound["Receipt"]!["RecordId"] = "other"; break;
            case 41: bound["Receipt"]!["OperationId"] = "other"; break;
            case 42: bound["Receipt"]!["RecordTypeId"] = "other"; break;
            case 43: bound["Receipt"]!["DefinitionDigest"] = new string('a', 64); break;
            case 44: bound["Receipt"]!["Schema"] = "schema:other"; break;
            case 45: bound["Receipt"]!["RecordedAt"] = "2030-01-02T04:04:05+01:00"; break;
            case 46: bound.AsObject().Remove("Receipt"); break;
            case 47: bound["Operation"]!["CommandId"] = "other"; break;
            case 48: bound["Audit"]!["ActorId"] = "other"; break;
            case 49: var extra = bound.DeepClone(); extra["Key"] = "other-key"; extra["Operation"]!["IdempotencyKey"] = "other-key";
                extra["Receipt"]!["Version"] = "other"; entries.Add(extra); break;
            case 50: bound["Row"]!["Body"] = Convert.ToBase64String("{\"x\":1,\"x\":2,\"unknown\":0}"u8); break;
            case 51: bound["Row"]!["PublicationVersionId"] = "1.0.1"; break;
            case 52: raw = root.ToJsonString().Replace("\"Encoding\":", "\"\\u0045ncoding\":", StringComparison.Ordinal); break;
            case 53: bound["Row"]!["PublicationVersionId"] = "BAD_ID"; raw = root.ToJsonString().Replace("BAD_ID", "\\udc00\\ud800", StringComparison.Ordinal); break;
            case 54: raw = root.ToJsonString().Insert(1, "\"Ignored\":\"\\ud800\","); break;
            case 55: raw = root.ToJsonString().Insert(1, "\"Ignored\\ud800\":0,"); break;
            default: throw new InvalidOperationException();
        }
        var bytes = Encoding.UTF8.GetBytes(raw ?? root.ToJsonString());
        if (id == 22) bytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray();
        if (id == 23)
        {
            var marker = Encoding.UTF8.GetBytes("INVALID_UTF8");
            var offset = bytes.AsSpan().IndexOf(marker);
            Assert.True(offset >= 0);
            bytes = bytes[..offset].Concat(new byte[] { 0xc3, 0x28 }).Concat(bytes[(offset + marker.Length)..]).ToArray();
        }
        await AllPaths(bytes, id is 0 or 30 or 35 or 50 or 52);
    }

    internal static async Task AllPaths(byte[] input, bool accept)
    {
        var directory = Path.Combine(Path.GetTempPath(), "q32-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "records.json");
        try
        {
            for (var route = 0; route < 4; route++)
            {
                File.WriteAllText(path, BaseJournal().ToJsonString());
                var store = new FileJournalRecordWriteStore(path);
                File.WriteAllBytes(path, input);
                async Task Act()
                {
                    switch (route)
                    {
                        case 0: _ = new FileJournalRecordWriteStore(path); break;
                        case 1: _ = await store.FindAsync(new RecordWriteOperationKey("acme", "alice", "op-new")); break;
                        case 2: _ = await store.FindAsync("acme", "bound-record"); break;
                        case 3: await Append(store); break;
                    }
                }
                if (accept) await Act();
                else
                {
                    var error = await Assert.ThrowsAsync<InvalidDataException>(Act);
                    Assert.Equal("Records journal is invalid.", error.Message);
                    Assert.Null(error.InnerException);
                }
                if (!accept || route != 3) Assert.Equal(input, File.ReadAllBytes(path));
                else
                {
                    using var original = JsonDocument.Parse(input);
                    using var appended = JsonDocument.Parse(File.ReadAllBytes(path));
                    Assert.Equal(original.RootElement.GetProperty("LegacyEntryCount").GetInt32(), appended.RootElement.GetProperty("LegacyEntryCount").GetInt32());
                    var prefix = original.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
                    Assert.Equal(prefix.Length + 1, appended.RootElement.GetProperty("Entries").GetArrayLength());
                    for (var i = 0; i < prefix.Length; i++)
                        Assert.Equal(prefix[i].GetRawText(), appended.RootElement.GetProperty("Entries")[i].GetRawText());
                }
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}
