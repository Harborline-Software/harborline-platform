using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// The granted bounded T-617 current-row producer: committed lookup, the real AccessProvider and exact published binding.
/// Expected identities, bytes, refusal codes and event order are literals or stated isolation properties. The fresh type
/// is seeded directly, without T-615 create defaults or shared fixtures. These tests claim no policy or history acceptance.
/// </summary>
public sealed class RecordReadOperationTests
{
    private const string Tenant = "tenant-a";
    private const string Actor = "reader-7";
    private const string RecordId = "Record-7";
    private const string TypeId = "work.inspection";
    private const string BodyText = """{"note":"kept"}""";
    private const string TypeJson =
        """{"class_id":"work.items","envelope":{"contract":{"major":1,"minor":0},"package_id":"read-fixture","section":"work","tenant":"tenant-a"},"fields":[{"display_name":"Note","field_key":"note"}],"name":"Inspection","record_class":"transactional","record_type_id":"work.inspection"}""" + "\n";
    // Independently SHA-256 hashed from the 277 literal UTF-8 bytes above, not from the read operation or serializer.
    private const string TypeDigest = "012a4e41806914f6092dd5846857c99129622f6a28a348895c1e18dbf78a6147";
    private static readonly DefinitionContractWindow Window = new(1, 0, 1);
    private static readonly DateTimeOffset At = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset Recorded = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    [Fact]
    public async Task a_read_looks_up_authorizes_and_binds_once_before_projecting_only_public_record_facts()
    {
        var fixture = await Published();

        var outcome = await fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId));

        Assert.True(outcome.Found);
        Assert.Null(outcome.Refusal);
        var record = Assert.IsType<RecordReadResult>(outcome.Record);
        Assert.Equal("Record-7", record.RecordId);
        Assert.Equal("work.inspection", record.RecordTypeId);
        Assert.Equal("1.0.0", record.Version);
        Assert.Equal(TypeDigest, record.DefinitionDigest);
        // Schema is an independently published fixture input, not a value read back from this operation.
        Assert.Equal(fixture.Schema, record.Schema);
        Assert.Equal("""{"note":"kept"}""", Encoding.UTF8.GetString(record.Body.Span));
        Assert.Equal(new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero), record.RecordedAt);
        Assert.Equal(["lookup", "authorize", "bind"], fixture.Events);
        Assert.Equal(1, fixture.Clock.Reads);
        Assert.Equal(("tenant-a", "Record-7"), Assert.Single(fixture.Store.Lookups));

        var asked = Assert.Single(fixture.Gate.Requests);
        Assert.Equal("records:read", asked.Operation);
        Assert.Equal("reader-7", asked.Principal);
        Assert.Equal("tenant-a", asked.Tenant);
        Assert.Equal("tenant-a", asked.Record.Tenant);
        Assert.Equal("record", asked.Record.Kind);
        Assert.Equal("Record-7", asked.Record.Id);
        Assert.Equal("work.inspection", asked.Record.RecordTypeId);
        Assert.Equal(["note"], asked.Record.Fields.Keys);
        Assert.Equal("kept", asked.Record.Fields["note"]!.GetValue<string>());
        Assert.Equal(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero), asked.At);
        Assert.Same(Assert.Single(fixture.Gate.Decisions), outcome.Decision);
        Assert.Equal("1.0.0", Assert.Single(fixture.Catalogue.Pins).VersionId);
        Assert.Equal(0, fixture.Catalogue.RecordHeadReads);
    }

    [Fact]
    public void the_public_projection_omits_write_intent_actor_provenance_standings_and_permitted_acts()
    {
        Assert.Equal(["Body", "DefinitionDigest", "RecordId", "RecordTypeId", "RecordedAt", "Schema", "Version"],
            typeof(RecordReadResult).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["Decision", "Found", "Record", "Refusal"],
            typeof(RecordReadOutcome).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["RecordId"], typeof(RecordReadRequest).GetProperties().Select(property => property.Name));
    }

    [Theory]
    [InlineData("tenant-b", "Record-7")]
    [InlineData("TENANT-A", "Record-7")]
    [InlineData("tenant-a ", "Record-7")]
    [InlineData("tenant-a", "record-7")]
    [InlineData("tenant-a", "Record-7 ")]
    [InlineData("tenant-a", "missing")]
    public async Task a_tenant_scoped_ordinal_miss_makes_no_decision_or_bind(string tenant, string recordId)
    {
        var fixture = await Published();

        var outcome = await fixture.Operation.ReadAsync(new(tenant, Actor), new(recordId));

        Assert.False(outcome.Found);
        Assert.Null(outcome.Record);
        Assert.Equal("records.read.not_found", outcome.Refusal);
        Assert.Null(outcome.Decision);
        Assert.Equal((tenant, recordId), Assert.Single(fixture.Store.Lookups));
        Assert.Equal(["lookup"], fixture.Events);
        Assert.Empty(fixture.Gate.Requests);
        Assert.Empty(fixture.Catalogue.Pins);
    }

    [Fact]
    public async Task a_denial_preserves_the_enforcing_reason_and_decision_without_record_data_or_binding()
    {
        var fixture = await Published();
        fixture.Gate.Allow = false;

        var outcome = await fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId));

        Assert.False(outcome.Found);
        Assert.Null(outcome.Record);
        Assert.Equal("ReadDenied", outcome.Refusal);
        Assert.Same(Assert.Single(fixture.Gate.Decisions), outcome.Decision);
        Assert.False(outcome.Decision!.Allowed);
        Assert.Equal(["lookup", "authorize"], fixture.Events);
        Assert.Empty(fixture.Catalogue.Pins);
        var json = JsonSerializer.Serialize(outcome);
        Assert.DoesNotContain("kept", json, StringComparison.Ordinal);
        Assert.DoesNotContain(TypeDigest, json, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0.0", json, StringComparison.Ordinal);
        Assert.DoesNotContain("writer-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("fingerprint-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("origin-secret", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("principal")]
    [InlineData("operation")]
    [InlineData("kind")]
    [InlineData("record")]
    [InlineData("type")]
    [InlineData("instant")]
    public async Task evidence_for_another_context_refuses_without_disclosing_the_foreign_decision(string context)
    {
        var fixture = await Published();
        fixture.Gate.DecideAs = request => context switch
        {
            "tenant" => request with { Tenant = "tenant-b" },
            "principal" => request with { Principal = "other-reader" },
            "operation" => request with { Operation = "records:write" },
            "kind" => request with { Record = request.Record with { Kind = "other-kind" } },
            "record" => request with { Record = request.Record with { Id = "other-record" } },
            "type" => request with { Record = request.Record with { RecordTypeId = "work.other" } },
            "instant" => request with { At = At.AddSeconds(1) },
            _ => throw new InvalidOperationException("Unknown literal mismatch case."),
        };

        var outcome = await fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId));

        Assert.False(outcome.Found);
        Assert.Null(outcome.Record);
        Assert.Equal("access.decision_mismatch", outcome.Refusal);
        Assert.Null(outcome.Decision);
        Assert.Single(fixture.Gate.Decisions);
        Assert.Empty(fixture.Catalogue.Pins);
        Assert.Equal(["lookup", "authorize"], fixture.Events);
    }

    [Fact]
    public async Task a_newer_published_head_does_not_replace_the_rows_pinned_version_or_digest()
    {
        var fixture = await Published();
        var nextBody = TypeJson.Replace("\"Inspection\"", "\"Inspection current\"", StringComparison.Ordinal);
        await fixture.Definitions.SaveDraftAsync(new(new(Tenant, DefinitionKind.Records, TypeId), "1.1.0", "1.1.0", nextBody),
            2, "second-draft");
        await fixture.Publisher.PublishAsync(Tenant, TypeId, "1.1.0", 3, "second-publish");

        var outcome = await fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId));

        var record = Assert.IsType<RecordReadResult>(outcome.Record);
        Assert.Equal("1.0.0", record.Version);
        Assert.Equal(TypeDigest, record.DefinitionDigest);
        var binding = Assert.Single(fixture.Catalogue.Pins);
        Assert.Equal(new DefinitionKey("tenant-a", DefinitionKind.Records, "work.inspection"), binding.Key);
        Assert.Equal("1.0.0", binding.VersionId);
        Assert.Equal(0, fixture.Catalogue.RecordHeadReads);
    }

    [Fact]
    public async Task a_null_stored_version_is_corruption_and_never_falls_back_to_the_published_head()
    {
        var fixture = await Published();
        fixture.Store.Rows[0] = fixture.Store.Rows[0] with { Version = null! };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId)).AsTask());

        Assert.Equal(["lookup", "authorize"], fixture.Events);
        Assert.Single(fixture.Gate.Decisions);
        Assert.Empty(fixture.Catalogue.Pins);
        Assert.Equal(0, fixture.Catalogue.RecordHeadReads);
    }

    [Fact]
    public async Task an_unresolvable_pin_refuses_without_record_data_and_retains_the_current_read_decision()
    {
        var fixture = await Published();
        fixture.Store.Rows[0] = fixture.Store.Rows[0] with { Version = "9.0.0" };

        var outcome = await fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId));

        Assert.False(outcome.Found);
        Assert.Null(outcome.Record);
        Assert.Equal("entity.validation.schema_unknown", outcome.Refusal);
        Assert.Same(Assert.Single(fixture.Gate.Decisions), outcome.Decision);
        Assert.Equal("9.0.0", Assert.Single(fixture.Catalogue.Pins).VersionId);
        Assert.Equal(["lookup", "authorize", "bind"], fixture.Events);
    }

    [Fact]
    public async Task a_pinned_definition_that_no_longer_admits_refuses_as_schema_unknown()
    {
        var fixture = await Published(readerWindow: new(2, 0, 2));

        var outcome = await fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId));

        Assert.Null(outcome.Record);
        Assert.Equal("entity.validation.schema_unknown", outcome.Refusal);
        Assert.Same(Assert.Single(fixture.Gate.Decisions), outcome.Decision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task digest_or_schema_corruption_is_an_exception_not_a_new_refusal(bool digest)
    {
        var fixture = await Published();
        fixture.Store.Rows[0] = digest
            ? fixture.Store.Rows[0] with { DefinitionDigest = new string('f', 64) }
            : fixture.Store.Rows[0] with { Schema = new("foreign-schema") };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId)).AsTask());

        Assert.Equal(["lookup", "authorize", "bind"], fixture.Events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task a_reader_returning_a_foreign_tenant_or_record_is_rejected_before_authorization(bool tenant)
    {
        var fixture = await Published();
        fixture.Store.ForcedRow = tenant
            ? fixture.Store.Rows[0] with { Tenant = "TENANT-A" }
            : fixture.Store.Rows[0] with { RecordId = "record-7" };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId)).AsTask());

        Assert.Equal(["lookup"], fixture.Events);
        Assert.Empty(fixture.Gate.Requests);
        Assert.Empty(fixture.Catalogue.Pins);
    }

    [Fact]
    public async Task a_nonobject_stored_body_is_corruption_and_never_becomes_a_miss()
    {
        var fixture = await Published();
        fixture.Store.Rows[0] = fixture.Store.Rows[0] with { Body = Encoding.UTF8.GetBytes("null") };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId)).AsTask());

        Assert.Equal(["lookup"], fixture.Events);
    }

    [Fact]
    public async Task malformed_committed_json_is_the_same_safe_corruption_before_authorization()
    {
        var fixture = await Published();
        fixture.Store.Rows[0] = fixture.Store.Rows[0] with { Body = Encoding.UTF8.GetBytes("{\"value\":") };

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId)).AsTask());

        Assert.Equal("The committed record body is not a JSON object.", error.Message);
        Assert.Null(error.InnerException);
        Assert.Equal(["lookup"], fixture.Events);
        Assert.Empty(fixture.Gate.Requests);
        Assert.Empty(fixture.Catalogue.Pins);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task lazy_duplicate_or_invalid_utf8_members_are_safe_corruption_before_authorization(bool duplicate)
    {
        var fixture = await Published();
        // The second literal is {"<invalid UTF8>":1}; materializing its member name must not
        // escape the same corruption boundary as a duplicate dictionary member.
        byte[] body = duplicate
            ? Encoding.UTF8.GetBytes("""{"a":1,"a":2}""")
            : [0x7b, 0x22, 0xc3, 0x28, 0x22, 0x3a, 0x31, 0x7d];
        var expectedBody = body.ToArray();
        fixture.Store.Rows[0] = fixture.Store.Rows[0] with { Body = body };

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId)).AsTask());

        Assert.Equal("The committed record body is not a JSON object.", error.Message);
        Assert.Null(error.InnerException);
        Assert.Equal(["lookup"], fixture.Events);
        Assert.Empty(fixture.Gate.Requests);
        Assert.Empty(fixture.Catalogue.Pins);
        Assert.Equal(expectedBody, fixture.Store.Rows[0].Body.ToArray());
    }

    [Fact]
    public async Task returned_bytes_are_detached_from_the_store_and_from_a_later_read()
    {
        var fixture = await Published();
        var first = await fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId));
        var firstRecord = Assert.IsType<RecordReadResult>(first.Record);
        Assert.True(MemoryMarshal.TryGetArray(firstRecord.Body, out ArraySegment<byte> bytes));
        bytes.Array![bytes.Offset] = (byte)'!';

        Assert.Equal("""{"note":"kept"}""", Encoding.UTF8.GetString(fixture.Store.Rows[0].Body.Span));
        var second = await fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId));
        Assert.Equal("""{"note":"kept"}""", Encoding.UTF8.GetString(Assert.IsType<RecordReadResult>(second.Record).Body.Span));
    }

    [Fact]
    public async Task store_and_binder_operational_failures_propagate_without_a_not_found_substitute()
    {
        var lookup = await Published();
        var lookupFailure = new IOException("fixture lookup failure");
        lookup.Store.Fault = lookupFailure;
        Assert.Same(lookupFailure, await Assert.ThrowsAsync<IOException>(
            () => lookup.Operation.ReadAsync(new(Tenant, Actor), new(RecordId)).AsTask()));
        Assert.Equal(["lookup"], lookup.Events);

        var binding = await Published();
        var bindingFailure = new IOException("fixture binding failure");
        binding.Catalogue.Fault = bindingFailure;
        Assert.Same(bindingFailure, await Assert.ThrowsAsync<IOException>(
            () => binding.Operation.ReadAsync(new(Tenant, Actor), new(RecordId)).AsTask()));
        Assert.Equal(["lookup", "authorize", "bind"], binding.Events);
    }

    [Fact]
    public async Task cancellation_is_forwarded_to_every_producer_and_a_precancelled_read_starts_nothing()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = await Published();
        await fixture.Operation.ReadAsync(new(Tenant, Actor), new(RecordId), cancellation.Token);
        Assert.Equal(cancellation.Token, fixture.Store.LastToken);
        Assert.Equal(cancellation.Token, fixture.Gate.LastToken);
        Assert.Equal(cancellation.Token, fixture.Catalogue.LastToken);

        var stopped = await Published();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => stopped.Operation.ReadAsync(new(Tenant, Actor), new(RecordId), cancellation.Token).AsTask());
        Assert.Empty(stopped.Events);
        Assert.Equal(0, stopped.Clock.Reads);
    }

    private sealed record Fixture(RecordReadOperation Operation, RowStore Store, Gate Gate, ObservingCatalogue Catalogue,
        CountingClock Clock, InMemoryVersionedDefinitionStore Definitions, RecordTypeDefinitionStore Publisher,
        SchemaId Schema, List<string> Events);

    private static async Task<Fixture> Published(DefinitionContractWindow? readerWindow = null)
    {
        var validator = new RecordsIntentValidator();
        var compiler = new RecordTypeSchemaCompiler(validator);
        var defaults = new RecordFieldDefaults(new FieldKindRuntime(new FieldKindRegistry([])));
        var registry = new InMemorySchemaRegistry();
        var definitions = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Records] = RecordTypeDefinitionStore.Admission(Window, validator),
            [DefinitionKind.Classes] = ClassDefinitionStore.Admission(Window),
        });
        var classes = new ClassDefinitionStore(definitions);
        await classes.CreateDraftAsync(new(Tenant, "work", "Items", "1.0.0", new(1, 0), "read-fixture"), "fresh-class");
        await classes.PublishAsync(Tenant, "work.items", "1.0.0", 1, "fresh-class-publish");
        var publisher = new RecordTypeDefinitionStore(definitions, compiler, defaults, registry, Window);
        await definitions.SaveDraftAsync(new(new(Tenant, DefinitionKind.Records, TypeId), "1.0.0", "1.0.0", TypeJson),
            0, "fresh-type");
        var publication = await publisher.PublishAsync(Tenant, TypeId, "1.0.0", 1, "fresh-publish");
        Assert.Equal(TypeDigest, publication.Revision.Digest);
        var events = new List<string>();
        var observed = new ObservingCatalogue(definitions, events);
        var readerTypes = new RecordTypeDefinitionStore(observed, compiler, defaults, registry, readerWindow ?? Window);
        var rows = new RowStore(events);
        rows.Rows.Add(new(Tenant, RecordId, TypeId, "1.0.0", TypeDigest, publication.Schema.Id, "writer-secret",
            "write-operation-secret", null, "intent-encoding-secret", "fingerprint-secret", Encoding.UTF8.GetBytes(BodyText), Recorded)
        {
            OriginatorId = "origin-secret",
            AcceptedCommandId = "command-secret",
            AcceptanceAuditId = "acceptance-secret",
        });
        var gate = new Gate(events);
        var clock = new CountingClock();
        return new(new(rows, new AccessProvider(gate), readerTypes, clock), rows, gate, observed, clock,
            definitions, publisher, publication.Schema.Id, events);
    }

    private sealed class CountingClock : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() => At.AddSeconds(Reads++);
    }

    private sealed class RowStore(List<string> events) : IRecordReadStore
    {
        public List<RecordWriteRow> Rows { get; } = [];
        public List<(string Tenant, string RecordId)> Lookups { get; } = [];
        public RecordWriteRow? ForcedRow { get; set; }
        public Exception? Fault { get; set; }
        public CancellationToken LastToken { get; private set; }

        public ValueTask<RecordWriteRow?> FindAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("lookup");
            Lookups.Add((tenant, recordId));
            LastToken = cancellationToken;
            if (Fault is not null) throw Fault;
            return ValueTask.FromResult(ForcedRow ?? Rows.SingleOrDefault(row =>
                string.Equals(row.Tenant, tenant, StringComparison.Ordinal)
                && string.Equals(row.RecordId, recordId, StringComparison.Ordinal)));
        }
    }

    private sealed class Gate(List<string> events) : IAuthorizationDecider
    {
        public bool Allow { get; set; } = true;
        public Func<AccessRequest, AccessRequest>? DecideAs { get; set; }
        public List<AccessRequest> Requests { get; } = [];
        public List<AuthorizationDecisionEvidence> Decisions { get; } = [];
        public CancellationToken LastToken { get; private set; }

        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("authorize");
            Requests.Add(request);
            LastToken = cancellationToken;
            var decision = new AuthorizationDecisionEvidence(DecideAs?.Invoke(request) ?? request, Allow,
                Allow ? "None" : "ReadDenied", "fixture-read-binding", [], []);
            Decisions.Add(decision);
            return ValueTask.FromResult(decision);
        }
    }

    private sealed class ObservingCatalogue(IVersionedDefinitionStore inner, List<string> events) : IVersionedDefinitionStore
    {
        public List<DefinitionBinding> Pins { get; } = [];
        public int RecordHeadReads { get; private set; }
        public Exception? Fault { get; set; }
        public CancellationToken LastToken { get; private set; }

        public ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind, CancellationToken cancellationToken = default)
            => inner.ListKeysAsync(tenant, kind, cancellationToken);
        public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision,
            string requestId, CancellationToken cancellationToken = default)
            => inner.SaveDraftAsync(document, expectedRevision, requestId, cancellationToken);
        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision,
            string requestId, CancellationToken cancellationToken = default)
            => inner.PublishAsync(key, versionId, expectedRevision, requestId, cancellationToken);
        public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId,
            string draftVersionId, string draftVersion, long expectedRevision, string requestId,
            CancellationToken cancellationToken = default)
            => inner.RestoreAsDraftAsync(key, sourceVersionId, draftVersionId, draftVersion, expectedRevision, requestId, cancellationToken);
        public ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key, CancellationToken cancellationToken = default)
            => inner.ListHistoryAsync(key, cancellationToken);
        public ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key, CancellationToken cancellationToken = default)
        {
            if (key.Kind == DefinitionKind.Records) RecordHeadReads++;
            return inner.GetPublishedHeadAsync(key, cancellationToken);
        }
        public ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding, CancellationToken cancellationToken = default)
        {
            if (binding.Key.Kind == DefinitionKind.Records)
            {
                events.Add("bind");
                Pins.Add(binding);
                LastToken = cancellationToken;
                if (Fault is not null) throw Fault;
            }
            return inner.ResolvePublishedAsync(binding, cancellationToken);
        }
    }
}
