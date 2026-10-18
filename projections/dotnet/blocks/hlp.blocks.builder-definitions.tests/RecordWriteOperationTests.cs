using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-616 first bounded slice (DES-0015 section 3, spec record-write-path): one record create of a published Record
/// Type through the host's gate, the catalogue's published head, the real registry and the kernel transaction boundary.
/// Stage names are ADR 0038's; codes are DES-0015's (RW-3 <c>entity.validation.schema_unknown</c>, RW-2
/// <c>entity.validation.body_invalid</c>); the act and target kind are the API gate's (<c>records:write</c>,
/// <c>record</c>). Every expected value is a literal here, never read from the operation. Intent encodings and their
/// SHA-256 digests were frozen by an independent reference encoder (records-write-intent/1, proposed), not by this code.
/// The fixture types are role-gated (decision D1): a principal's direct create of a form_only type, the default, is refused
/// (K9; RecordCreationAuthorizationTests), so this slice's write path is exercised on the creation gate that admits it.
/// </summary>
public sealed class RecordWriteOperationTests
{
    private static readonly DateTimeOffset At = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private const string Actor = "operator-7";
    private const string ValidBody = """{"asset_tag":"A-1","quantity":3,"notes":"spare"}""";

    [Fact]
    [Trait("Holds", "records-eng-2")]
    public async Task a_gate_denial_reaches_no_binder_registry_or_store_and_carries_the_gates_reason()
    {
        var fixture = await Published(allow: false);

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-deny", ValidBody));

        Assert.False(outcome.Committed);
        Assert.Null(outcome.Receipt);
        Assert.Equal("authorize", outcome.Refusal!.Stage);
        Assert.Equal("NoEffectiveRole", outcome.Refusal.Code);
        Assert.Empty(outcome.Refusal.Pointers);
        var asked = Assert.Single(fixture.Gate.Requests);
        Assert.Equal("records:write", asked.Operation);
        Assert.Equal(Actor, asked.Principal);
        Assert.Equal("tenant-a", asked.Tenant);
        Assert.Equal("tenant-a", asked.Record.Tenant);
        Assert.Equal("record", asked.Record.Kind);
        Assert.Matches("^[0-9a-f]{32}$", asked.Record.Id);
        Assert.Empty(asked.Record.Fields);
        // W9: the target is typed by the requested Record Type; it is a context field, never a record field.
        Assert.Equal("eam.asset-class", asked.Record.RecordTypeId);
        Assert.Equal("eam.asset-class", outcome.Decision!.RecordTypeId);
        Assert.Equal(At, asked.At);
        Assert.Equal(0, fixture.Catalogue.HeadReads);
        Assert.Equal(0, fixture.Registry.Registrations);
        Assert.Equal(0, fixture.Registry.Validations);
        Assert.Empty(fixture.Port.Events);
    }

    [Fact]
    [Trait("Holds", "records-eng-7")]
    public async Task an_unpublished_record_type_refuses_at_bind_by_name_and_never_opens_the_store()
    {
        var fixture = await Published();

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor),
            new("eam.unpublished-type", "op-unknown", JsonNode.Parse(ValidBody)!.AsObject()));

        Assert.Equal("bind", outcome.Refusal!.Stage);
        Assert.Equal("entity.validation.schema_unknown", outcome.Refusal.Code);
        Assert.Empty(outcome.Refusal.Pointers);
        Assert.Equal(1, fixture.Catalogue.HeadReads);
        Assert.Equal(0, fixture.Registry.Validations);
        Assert.Empty(fixture.Port.Events);
    }

    [Fact]
    [Trait("Holds", "records-eng-6")]
    [Trait("Holds", "records-eng-8")]
    public async Task a_body_the_schema_refuses_persists_nothing_and_names_only_the_pointer()
    {
        var fixture = await Published();

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor),
            Request("op-invalid", """{"asset_tag":"SECRET-7731","notes":5}"""));

        Assert.Equal("validate", outcome.Refusal!.Stage);
        Assert.Equal("entity.validation.body_invalid", outcome.Refusal.Code);
        Assert.Equal(["/notes"], outcome.Refusal.Pointers);
        Assert.DoesNotContain("SECRET-7731", $"{outcome.Refusal.Stage}|{outcome.Refusal.Code}|{string.Join(",", outcome.Refusal.Pointers)}");
        Assert.Equal(1, fixture.Registry.Validations);
        Assert.Equal(["begin", "rollback", "dispose"], fixture.Port.Events);
        Assert.Empty(fixture.Port.Published);
    }

    [Fact]
    [Trait("Holds", "records-eng-7")]
    public async Task a_published_head_that_no_longer_admits_refuses_at_bind_by_name()
    {
        // The writing host's contract window has moved past the published body's contract 1.0.
        var fixture = await Published(window: new(2, 0, 2));

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-window", ValidBody));

        Assert.Equal("bind", outcome.Refusal!.Stage);
        Assert.Equal("entity.validation.schema_unknown", outcome.Refusal.Code);
        Assert.Empty(outcome.Refusal.Pointers);
        Assert.Equal(1, fixture.Catalogue.HeadReads);
        Assert.Equal(0, fixture.Registry.Registrations);
        Assert.Empty(fixture.Port.Events);
    }

    [Fact]
    [Trait("Holds", "records-eng-7")]
    public async Task a_schema_the_validating_registry_does_not_hold_refuses_by_name_at_validate()
    {
        var fixture = await Published(validatingRegistry: new InMemorySchemaRegistry());

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-foreign", ValidBody));

        Assert.Equal("validate", outcome.Refusal!.Stage);
        Assert.Equal("entity.validation.schema_unknown", outcome.Refusal.Code);
        Assert.Empty(outcome.Refusal.Pointers);
        Assert.Equal(["begin", "rollback", "dispose"], fixture.Port.Events);
        Assert.Empty(fixture.Port.Published);
    }

    [Fact]
    [Trait("Holds", "records-eng-26")]
    [Trait("Holds", "records-run-3")]
    public async Task a_valid_create_commits_one_record_operation_identity_and_audit_set()
    {
        var fixture = await Published();

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));

        Assert.True(outcome.Committed);
        Assert.False(outcome.Replayed);
        Assert.Equal(["begin", "operation", "record", "audit", "commit", "dispose"], fixture.Port.Events);
        var (operation, row, audit) = Assert.Single(fixture.Port.Published);
        const string canonical = """{"asset_tag":"A-1","notes":"spare","quantity":3}""";
        // records-write-intent/1 of (eam.asset-class, unpinned, ValidBody); digest frozen by the reference encoder.
        const string expectedFingerprint = "d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5";

        Assert.Matches("^[0-9a-f]{32}$", row.RecordId);
        Assert.Equal(row.RecordId, Assert.Single(fixture.Gate.Requests).Record.Id);
        Assert.Equal(row.RecordId, operation.CommandId);
        Assert.Equal("8:tenant-a10:operator-77:op-0001", operation.IdempotencyKey);
        Assert.Equal(expectedFingerprint, operation.Fingerprint);
        Assert.Equal("tenant-a", row.Tenant);
        Assert.Equal("eam.asset-class", row.RecordTypeId);
        Assert.Equal("1.0.0", row.Version);
        Assert.Equal(fixture.Publication.Revision.Digest, row.DefinitionDigest);
        Assert.Equal(fixture.Publication.Schema.Id, row.Schema);
        Assert.Equal(Actor, row.ActorId);
        Assert.Equal("op-0001", row.OperationId);
        Assert.Null(row.RequestedVersion);
        Assert.Equal("records-write-intent/1", row.IntentEncodingVersion);
        Assert.Equal(expectedFingerprint, row.IntentDigest);
        Assert.Equal(canonical, Encoding.UTF8.GetString(row.Body.Span));
        Assert.Equal(At, row.RecordedAt);

        Assert.Matches("^[0-9a-f]{32}$", audit.AuditId);
        Assert.NotEqual(row.RecordId, audit.AuditId);
        Assert.Equal(Actor, audit.ActorId);
        Assert.Equal(At, audit.RecordedAt);
        // D6: exactly seven identifier keys, in the ruled order. version is the published Record Type definition version
        // the write bound to. The minted record id and the publication's digest and schema id are the only observed holes.
        var payloadText = Encoding.UTF8.GetString(audit.Payload.Span);
        Assert.Equal("{\"act\":\"records:write\",\"operation_id\":\"op-0001\",\"record_id\":\"" + row.RecordId
            + "\",\"record_type_id\":\"eam.asset-class\",\"version\":\"1.0.0\",\"definition_digest\":\""
            + fixture.Publication.Revision.Digest + "\",\"schema_id\":\"" + fixture.Publication.Schema.Id.Value + "\"}", payloadText);
        var payload = JsonNode.Parse(audit.Payload.Span)!.AsObject();
        Assert.Equal(["act", "operation_id", "record_id", "record_type_id", "version", "definition_digest", "schema_id"],
            payload.Select(member => member.Key));
        // No body value, no intent digest, no authority evidence; tenant and executor are envelope facts.
        Assert.DoesNotContain("spare", payloadText);
        Assert.DoesNotContain("fingerprint", payloadText);
        Assert.DoesNotContain(expectedFingerprint, payloadText);
        Assert.DoesNotContain("tenant-a", payloadText);
        Assert.DoesNotContain(Actor, payloadText);
        Assert.DoesNotContain("verdict", payloadText);

        var receipt = outcome.Receipt!;
        Assert.Equal(row.RecordId, receipt.RecordId);
        Assert.Equal("op-0001", receipt.OperationId);
        Assert.Equal("eam.asset-class", receipt.RecordTypeId);
        Assert.Equal("1.0.0", receipt.Version);
        Assert.Equal(fixture.Publication.Revision.Digest, receipt.DefinitionDigest);
        Assert.Equal(fixture.Publication.Schema.Id, receipt.Schema);
        Assert.Equal(At, receipt.RecordedAt);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task a_replayed_operation_id_returns_the_first_record_and_writes_no_second_set()
    {
        var fixture = await Published();
        var first = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));

        // The same members in another order are the same body.
        var replay = await fixture.Operation.CreateAsync(new("tenant-a", Actor),
            Request("op-0001", """{"notes":"spare","quantity":3,"asset_tag":"A-1"}"""));

        Assert.True(replay.Committed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Single(fixture.Port.Published);
        Assert.Equal(2, fixture.Gate.Requests.Count);
        Assert.NotEqual(fixture.Gate.Requests[0].Record.Id, fixture.Gate.Requests[1].Record.Id);
        // Settled from the stored receipt: no second begin, bind, validation, stage or commit.
        Assert.Equal(["begin", "operation", "record", "audit", "commit", "dispose"], fixture.Port.Events);
        Assert.Equal(1, fixture.Catalogue.HeadReads);
        Assert.Equal(1, fixture.Registry.Validations);

        var conflict = await fixture.Operation.CreateAsync(new("tenant-a", Actor),
            Request("op-0001", """{"asset_tag":"A-2","quantity":3,"notes":"spare"}"""));

        Assert.Equal("commit", conflict.Refusal!.Stage);
        Assert.Equal("records.write.replay_conflict", conflict.Refusal.Code);
        Assert.Equal(["/operation_id"], conflict.Refusal.Pointers);
        Assert.Null(conflict.Receipt);
        Assert.Single(fixture.Port.Published);

        // Another caller's operation id is another lookup scope: it never sees the first receipt, and commits its own.
        var foreign = await fixture.Operation.CreateAsync(new("tenant-a", "operator-8"), Request("op-0001", ValidBody));

        Assert.True(foreign.Committed);
        Assert.False(foreign.Replayed);
        Assert.NotEqual(first.Receipt!.RecordId, foreign.Receipt!.RecordId);
        Assert.Equal(2, fixture.Port.Published.Count);
        Assert.Equal("8:tenant-a10:operator-87:op-0001", fixture.Port.Published[1].Operation.IdempotencyKey);
    }

    [Theory]
    [Trait("Holds", "records-eng-26")]
    [InlineData("begin", new[] { "begin" })]
    [InlineData("operation", new[] { "begin", "operation", "rollback", "dispose" })]
    [InlineData("record", new[] { "begin", "operation", "record", "rollback", "dispose" })]
    [InlineData("audit", new[] { "begin", "operation", "record", "audit", "rollback", "dispose" })]
    [InlineData("commit", new[] { "begin", "operation", "record", "audit", "commit", "rollback", "dispose" })]
    public async Task a_fault_before_commit_completes_rolls_back_and_leaves_the_operation_id_unused(string fault, string[] events)
    {
        var fixture = await Published();
        fixture.Port.Fault = fault;

        await Assert.ThrowsAsync<InjectedFault>(() =>
            fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody)).AsTask());

        Assert.Equal(events, fixture.Port.Events);
        Assert.Empty(fixture.Port.Published);

        fixture.Port.Fault = null;
        var retry = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));
        Assert.True(retry.Committed);
        Assert.False(retry.Replayed);
        Assert.Single(fixture.Port.Published);
    }

    [Theory]
    [Trait("Holds", "records-eng-9")]
    [InlineData("throw")]
    [InlineData("cancel")]
    public async Task a_registry_fault_or_cancellation_propagates_after_rollback_and_is_never_a_pass(string fault)
    {
        var fixture = await Published();
        fixture.Registry.ValidateFault = fault;

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody)).AsTask());

        Assert.IsType(fault == "throw" ? typeof(InvalidOperationException) : typeof(OperationCanceledException), thrown);
        Assert.Equal(["begin", "rollback", "dispose"], fixture.Port.Events);
        Assert.Empty(fixture.Port.Published);
    }

    [Fact]
    [Trait("Holds", "records-eng-8")]
    public async Task unsorted_duplicate_validator_pointers_refuse_once_each_in_ordinal_order_without_messages()
    {
        var fixture = await Published();
        // The validator reports pointers unsorted and repeated, and its messages quote the submitted value.
        fixture.Registry.StubResult = new(false,
        [
            new("/quantity", "quantity: SECRET-7731 is not a count", "type"),
            new("/notes", "notes: SECRET-7731 is not a string", "type"),
            new("/quantity", "quantity: SECRET-7731 is below the minimum", "minimum"),
            new("/Quantity", "Quantity: SECRET-7731 is not declared", "additional-properties"),
            new("", "SECRET-7731 failed", "invalid"),
            new("/notes", "notes: SECRET-7731 is too long", "maxLength"),
        ]);

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-pointers", ValidBody));

        Assert.Equal("validate", outcome.Refusal!.Stage);
        Assert.Equal("entity.validation.body_invalid", outcome.Refusal.Code);
        // Ordinal: "" < "/Quantity" (0x51) < "/notes" (0x6E) < "/quantity" (0x71); a culture sort would put "/Quantity" last.
        Assert.Equal(["", "/Quantity", "/notes", "/quantity"], outcome.Refusal.Pointers);
        Assert.DoesNotContain("SECRET-7731", $"{outcome.Refusal.Stage}|{outcome.Refusal.Code}|{string.Join(",", outcome.Refusal.Pointers)}");
        Assert.Equal(1, fixture.Registry.Validations);
        Assert.Equal(["begin", "rollback", "dispose"], fixture.Port.Events);
        Assert.Empty(fixture.Port.Published);
    }

    [Fact]
    [Trait("Holds", "records-eng-26")]
    public async Task nested_members_are_validated_and_staged_in_recursive_ordinal_order_with_nulls_and_array_order_kept()
    {
        var fixture = await Published(unboundFieldsForCanonicalBytes: true);
        // The validator admits the body so the staged bytes are observable; this case is about canonical form only.
        fixture.Registry.StubResult = new(true, []);
        const string submitted = """{"z":{"b":[3,null,{"d":null,"c":"x"},1],"a":null},"n":null,"m":[],"a":{},"B":1}""";
        const string canonical = """{"B":1,"a":{},"m":[],"n":null,"z":{"a":null,"b":[3,null,{"c":"x","d":null},1]}}""";

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-nested", submitted));

        Assert.True(outcome.Committed);
        var (operation, row, _) = Assert.Single(fixture.Port.Published);
        Assert.Equal(canonical, Encoding.UTF8.GetString(row.Body.Span));
        Assert.Equal(canonical, Encoding.UTF8.GetString(fixture.Registry.LastValidated!));
        // records-write-intent/1 of (eam.asset-class, unpinned, submitted); digest frozen by the reference encoder.
        const string expectedFingerprint = "a4a3ca95298c62e12c0579a371596621e537070761dd67230559d388543a16ed";
        Assert.Equal(expectedFingerprint, row.IntentDigest);
        Assert.Equal(expectedFingerprint, operation.Fingerprint);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task a_body_reordered_at_every_depth_replays_the_first_record()
    {
        var fixture = await Published(unboundFieldsForCanonicalBytes: true);
        fixture.Registry.StubResult = new(true, []);
        var first = await fixture.Operation.CreateAsync(new("tenant-a", Actor),
            Request("op-depth", """{"z":{"b":[3,null,{"d":null,"c":"x"},1],"a":null},"n":null,"m":[],"a":{},"B":1}"""));

        var replay = await fixture.Operation.CreateAsync(new("tenant-a", Actor),
            Request("op-depth", """{"B":1,"a":{},"m":[],"n":null,"z":{"a":null,"b":[3,null,{"c":"x","d":null},1]}}"""));
        var reversed = await fixture.Operation.CreateAsync(new("tenant-a", Actor),
            Request("op-depth", """{"B":1,"a":{},"m":[],"n":null,"z":{"a":null,"b":[1,{"c":"x","d":null},null,3]}}"""));

        Assert.True(replay.Replayed);
        Assert.Equal(first.Receipt, replay.Receipt);
        // Array order is content: the reversed array is a different body, so the reused id conflicts.
        Assert.Equal("commit", reversed.Refusal!.Stage);
        Assert.Equal("records.write.replay_conflict", reversed.Refusal.Code);
        Assert.Single(fixture.Port.Published);
    }

    [Fact]
    [Trait("Holds", "records-eng-7")]
    public async Task a_moved_window_refusal_never_reaches_the_validator()
    {
        var fixture = await Published(window: new(2, 0, 2));

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-window-validator", ValidBody));

        Assert.Equal("bind", outcome.Refusal!.Stage);
        Assert.Equal(0, fixture.Registry.Validations);
        Assert.Null(fixture.Registry.LastValidated);
    }

    [Fact]
    [Trait("Holds", "records-eng-10")]
    public async Task a_newer_publication_with_a_changed_schema_governs_the_next_write()
    {
        var fixture = await Published();
        Assert.True((await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-text", """{"asset_tag":"A-1"}"""))).Committed);

        var head = (await fixture.Host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"))!;
        var rekinded = head with
        {
            Fields =
            [
                head.Fields[0] with
                {
                    Governance = null,
                    Binding = new FieldBindingDefinition(new FieldKindReference("count", "1.0.0", new Dictionary<string, string>()),
                        new FieldConstraintDefinition(true, 0, 1, [], null)),
                },
                head.Fields[1],
                head.Fields[2],
            ],
        };
        await fixture.Host.Records.SaveDraftAsync("eam.asset-class", rekinded, "1.1.0", 2, "rekind");
        await fixture.Host.Records.PublishAsync(Tenant, "eam.asset-class", "1.1.0", 3, "publish-1.1.0");

        var stale = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-stale", """{"asset_tag":"A-1"}"""));
        var current = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-count", """{"asset_tag":7}"""));

        Assert.Equal("validate", stale.Refusal!.Stage);
        Assert.Equal("entity.validation.body_invalid", stale.Refusal.Code);
        Assert.True(current.Committed);
        Assert.Equal("1.1.0", current.Receipt!.Version);
        Assert.NotEqual(fixture.Publication.Schema.Id, current.Receipt.Schema);
        Assert.Equal(["1.0.0", "1.1.0"], fixture.Port.Published.Select(set => set.Row.Version));
    }

    // ---- Approved semantics (goal5 DECISION ac1e100f, CORRECTION-HANDOFF d6ec74af). Encodings and digests below were
    // ---- frozen by an independent reference encoder; each row also re-hashes its literal encoding here.

    [Theory]
    [Trait("Holds", "records-run-3")]
    [InlineData("3", "D+1:3+0;", "26f16eacafe8370a4dfb8db3c921f03edbc2644e662a8d53aba5c483d3933ad7")]
    [InlineData("3.0", "D+1:3+0;", "26f16eacafe8370a4dfb8db3c921f03edbc2644e662a8d53aba5c483d3933ad7")]
    [InlineData("3e0", "D+1:3+0;", "26f16eacafe8370a4dfb8db3c921f03edbc2644e662a8d53aba5c483d3933ad7")]
    [InlineData("30E-1", "D+1:3+0;", "26f16eacafe8370a4dfb8db3c921f03edbc2644e662a8d53aba5c483d3933ad7")]
    [InlineData("\"3\"", "S1:3", "75a72f550b32d697ac307f3d87f0c897af4ac90a212fdd95ef43179a47c71d56")]
    [InlineData("null", "N", "8ce86a6ae65d3692e7305e2c58ac62eebd97d3d943e093f577da25c36988246b")]
    [InlineData("true", "T", "e632b7095b0bf32c260fa4c539e9fd7b852d0de454e9be26f24d0d6f91d069d3")]
    [InlineData("false", "F", "f67ab10ad4e4c53121b6a5fe4da9c10ddee905b978d3788d2723d7bfacbe28a9")]
    [InlineData("0", "Z", "bbeebd879e1dff6918546dc0c179fdde505f2a21591c9a9c96e36b054ec5af83")]
    [InlineData("-0", "Z", "bbeebd879e1dff6918546dc0c179fdde505f2a21591c9a9c96e36b054ec5af83")]
    [InlineData("-0.000e7", "Z", "bbeebd879e1dff6918546dc0c179fdde505f2a21591c9a9c96e36b054ec5af83")]
    [InlineData("9007199254740992", "D+16:9007199254740992+0;", "7dbeab664ff9077b19fa6353a3e35dd2f34e249fc7c90c8b6a5c7113f912a996")]
    [InlineData("9007199254740993", "D+16:9007199254740993+0;", "89209ddf1d8644791baf062f71eb12c384834f3dd19e40971bc7420f91e3debc")]
    [InlineData("9223372036854775807", "D+19:9223372036854775807+0;", "4f4cb26aea4a947d857f420d3a920c1aedea0283c5605d4e8296972b2c10d6eb")]
    [InlineData("9223372036854775808", "D+19:9223372036854775808+0;", "1816061349fb0ff3137637a350f4896adaa1972a84328c08e7b4284a595137bd")]
    [InlineData("-9223372036854775808", "D-19:9223372036854775808+0;", "c4abe98ab5201380266806abe9cdafac5ddb40fda86ef1e56e2cc4963df11498")]
    [InlineData("12345678901234567890.000000000000000001", "D+38:12345678901234567890000000000000000001-18;", "121cc0641e248b4b1cf7d57cbf93fa982cf765be6763f999aa76489a56aad673")]
    [InlineData("1e1000000000", "D+1:1+1000000000;", "ef67b51da4fc71806b469c4c42b8854c75ef928867340bdc7691ccf9da6cc8bb")]
    [InlineData("10e999999999", "D+1:1+1000000000;", "ef67b51da4fc71806b469c4c42b8854c75ef928867340bdc7691ccf9da6cc8bb")]
    [InlineData("-1e-1000000000", "D-1:1-1000000000;", "d2b16af39ff5233d4d7dc280a8b7a58fa68640cd5dacf75ab454c55937448aa5")]
    [InlineData("[1,2]", "A2:D+1:1+0;D+1:2+0;", "d5e1e3fcfcc6adef1f0a965ba80b1683fe54bca577b170747e1a7f7cc3588bae")]
    [InlineData("[2,1]", "A2:D+1:2+0;D+1:1+0;", "d651c9f1f1d66666b20827dd32f84bfde7611a8501500d9b3adf032b7b600cde")]
    [InlineData("""{"a":1,"b":{"d":[null,true],"c":"x"}}""", "O2:S1:aD+1:1+0;S1:bO2:S1:cS1:xS1:dA2:NT", "abd05f310d449671fcf6e87ae42f8ef05385e6533b0e7177379d0bdec7c0e931")]
    [InlineData("""{"b":{"c":"x","d":[null,true]},"a":1.0}""", "O2:S1:aD+1:1+0;S1:bO2:S1:cS1:xS1:dA2:NT", "abd05f310d449671fcf6e87ae42f8ef05385e6533b0e7177379d0bdec7c0e931")]
    [InlineData("""{"a":null}""", "O1:S1:aN", "553cc22be387deeaafcd1f13463f1b0075b99c4e8c3fff0b6a1dc94cf90c6cdf")]
    [InlineData("{}", "O0:", "59a61a1c69e8fb08433bd945daf29f1cc3b124a19d74e2a4c070aa70afeb42f8")]
    [InlineData("""{"a":"b","c":"d"}""", "O2:S1:aS1:bS1:cS1:d", "f262a3da59ade51f8c64e5ee58e3162b69b4cf8098abfcebc3bc9301b444d1a6")]
    [InlineData("""{"a":"b\",\"c\":\"d"}""", """O1:S1:aS9:b","c":"d""", "3436e4203631ec73fb5ddc543049b949b673ca1fe10b176c3078ffc0c828db72")]
    [InlineData("{\"\u00e9\":\"\u00e9\",\"z\":\"\U0001F600\"}", "O2:S1:zS4:\U0001F600S2:\u00e9S2:\u00e9", "844e01cb2f97296a672073be16698d07ec6f3752ffaf736c053aaee6c2a1fe5a")]
    // Keys order by UTF-8 bytes: U+FF01 (EF BC 81) precedes U+1F600 (F0 9F 98 80), where UTF-16 ordinal order is reversed.
    [InlineData("{\"\U0001F600\":1,\"\uFF01\":2}", "O2:S3:\uFF01D+1:2+0;S4:\U0001F600D+1:1+0;", "04479476d90d67f7221ca13bb231beb7e181c749ff275f60fa214c4d910e5f55")]
    public void the_intent_value_encoding_is_frozen_byte_for_byte(string json, string encoding, string sha256)
    {
        var bytes = RecordWriteIntent.EncodeValue(JsonNode.Parse(json));

        Assert.Equal(encoding, Encoding.UTF8.GetString(bytes));
        Assert.Equal(sha256, RecordWriteIntent.Digest(bytes));
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(encoding))));
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public void intent_equality_is_exact_numeric_typed_and_structural()
    {
        Assert.Equal(Digest("3"), Digest("3.0"));
        Assert.Equal(Digest("3"), Digest("3e0"));
        Assert.NotEqual(Digest("3"), Digest("\"3\""));
        Assert.Equal(Digest("0"), Digest("-0"));
        Assert.NotEqual(Digest("9007199254740992"), Digest("9007199254740993"));
        Assert.NotEqual(Digest("9223372036854775807"), Digest("9223372036854775808"));
        Assert.NotEqual(Digest("-9223372036854775808"), Digest("-9223372036854775809"));
        Assert.NotEqual(Digest("12345678901234567890"), Digest("12345678901234567890.000000000000000001"));
        Assert.Equal(Digest("1e1000000000"), Digest("10e999999999"));
        Assert.Equal(Digest("1e5"), Digest("1e00005"));
        Assert.Equal(Digest("1e5"), Digest("1E+0000000000000000000000005"));
        Assert.Equal(Digest("1e-1000000000"), Digest("1e-00000000000000000000001000000000"));
        Assert.NotEqual(Digest("1e1000000000"), Digest("1e1000000001"));
        Assert.Equal(Digest("""{"a":1,"b":{"d":[null,true],"c":"x"}}"""), Digest("""{"b":{"c":"x","d":[null,true]},"a":1.0}"""));
        Assert.NotEqual(Digest("[1,2]"), Digest("[2,1]"));
        Assert.NotEqual(Digest("""{"a":null}"""), Digest("{}"));
        Assert.NotEqual(Digest("""{"a":"b","c":"d"}"""), Digest("""{"a":"b\",\"c\":\"d"}"""));
        Assert.NotEqual(Digest("""{"ab":"c"}"""), Digest("""{"a":"bc"}"""));
        Assert.NotEqual(Digest("[\"a\",\"b\"]"), Digest("[\"ab\"]"));
        Assert.NotEqual(Digest("true"), Digest("\"T\""));
        Assert.NotEqual(Digest("null"), Digest("\"N\""));

        static string Digest(string json) => RecordWriteIntent.Digest(RecordWriteIntent.EncodeValue(JsonNode.Parse(json)));
    }

    // ---- Exponent bound. A written exponent is read without arbitrary precision: leading zeros are skipped and more than
    // ---- 18 significant digits has no intent. Encodings and digests below were computed by an independent reference
    // ---- encoder of the documented grammar; each row also re-hashes its literal encoding here.

    [Theory]
    [Trait("Holds", "records-run-3")]
    [InlineData("1e999999999999999999", "D+1:1+999999999999999999;", "ad6fd4447335c316068bdee49f894be45c763c314b8010bd63b7ca709d4cb2fa")]
    [InlineData("1E+999999999999999999", "D+1:1+999999999999999999;", "ad6fd4447335c316068bdee49f894be45c763c314b8010bd63b7ca709d4cb2fa")]
    [InlineData("1e-999999999999999999", "D+1:1-999999999999999999;", "1637c9c60c9f2e1b703ad1e1e75e9032c90691b8bba39412cace00398bd015e0")]
    [InlineData("-9.99e999999999999999999", "D-3:999+999999999999999997;", "7c3033381758258cb26becfc8c5fb3e11418bc5fcd0a02958ebfba6218b0e13a")]
    // The bound is on the written exponent; the normalized exponent may pass it by the significand's own digit counts.
    [InlineData("10e999999999999999999", "D+1:1+1000000000000000000;", "c2433c72d359c74611328f709130105297745873bd19f1989e8e23b3a9a21391")]
    [InlineData("0.1e-999999999999999999", "D+1:1-1000000000000000000;", "764fcee705f2db1e373ee4b350468a265ab7933909760a5a5af2ff8fce35a942")]
    [InlineData("1e000000000000000000000000005", "D+1:1+5;", "aa865a6dea57b162e9c63076db53540409465b24d5b63c4420edcb79cd60206a")]
    [InlineData("1e-0000000000000000000000000001000000000", "D+1:1-1000000000;", "e9eef74d8b914e4c9f4eab67331b4576d3a371d5058caaee96abc63c07e01f0a")]
    [InlineData("1e00000000000000000000999999999999999999", "D+1:1+999999999999999999;", "ad6fd4447335c316068bdee49f894be45c763c314b8010bd63b7ca709d4cb2fa")]
    [InlineData("1e+0", "D+1:1+0;", "a60e9c8ce63e03e8b4e59d9af89eb77f831d4c24ddd7db4c395abf933c1908a6")]
    [InlineData("1e-0", "D+1:1+0;", "a60e9c8ce63e03e8b4e59d9af89eb77f831d4c24ddd7db4c395abf933c1908a6")]
    [InlineData("0e999999999999999999", "Z", "bbeebd879e1dff6918546dc0c179fdde505f2a21591c9a9c96e36b054ec5af83")]
    public void an_exponent_within_the_bound_encodes_exactly_byte_for_byte(string json, string encoding, string sha256)
    {
        var bytes = RecordWriteIntent.EncodeValue(JsonNode.Parse(json));

        Assert.Equal(encoding, Encoding.UTF8.GetString(bytes));
        Assert.Equal(sha256, RecordWriteIntent.Digest(bytes));
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(encoding))));
    }

    [Theory]
    [Trait("Holds", "records-run-3")]
    [InlineData("1e1000000000000000000")]
    [InlineData("1e-1000000000000000000")]
    [InlineData("1e+99999999999999999999")]
    [InlineData("-1E-99999999999999999999")]
    [InlineData("0e1000000000000000000")]
    [InlineData("-0.0e-1000000000000000000")]
    [InlineData("1e00000000000000000001000000000000000000")]
    [InlineData("[1,{\"a\":2.5E+1000000000000000000}]")]
    public void an_exponent_beyond_the_bound_has_no_intent(string json)
    {
        var value = JsonNode.Parse(json);

        Assert.Throws<FormatException>(() => RecordWriteIntent.EncodeValue(value));
    }

    [Theory]
    [Trait("Holds", "records-run-3")]
    [InlineData("", 4096)]
    [InlineData("-", 4096)]
    [InlineData("", 1_000_000)]
    [InlineData("+", 1_000_000)]
    public void an_enormous_exponent_has_no_intent(string sign, int nines)
    {
        var number = JsonNode.Parse("1e" + sign + new string('9', nines));

        Assert.Throws<FormatException>(() => RecordWriteIntent.EncodeValue(number));
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public void a_zero_padded_exponent_encodes_as_its_short_form_in_allocation_bounded_by_its_own_digits()
    {
        var lexeme = "1e" + new string('0', 1_000_000) + "5";
        var number = JsonNode.Parse(lexeme);
        _ = RecordWriteIntent.EncodeValue(JsonNode.Parse("1e05"));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var bytes = RecordWriteIntent.EncodeValue(number);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal("D+1:1+5;", Encoding.ASCII.GetString(bytes));
        Assert.Equal(RecordWriteIntent.EncodeValue(JsonNode.Parse("1e5")), bytes);
        // The lexeme's own UTF-16 copy is about two bytes per character; twice that bounds everything else.
        Assert.InRange(allocated, 0, 4L * lexeme.Length);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public void a_create_intent_frames_its_version_record_type_pin_and_body()
    {
        var unpinned = RecordWriteIntent.Encode("eam.asset-class", null, JsonNode.Parse(ValidBody)!.AsObject());
        var pinned = RecordWriteIntent.Encode("eam.asset-class", "1.0.0", JsonNode.Parse(ValidBody)!.AsObject());

        Assert.Equal("records-write-intent/1", RecordWriteIntent.EncodingVersion);
        Assert.Equal("S22:records-write-intent/1S15:eam.asset-classNO3:S9:asset_tagS3:A-1S5:notesS5:spareS8:quantityD+1:3+0;",
            Encoding.UTF8.GetString(unpinned));
        Assert.Equal("d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5", RecordWriteIntent.Digest(unpinned));
        Assert.Equal("S22:records-write-intent/1S15:eam.asset-classS5:1.0.0O3:S9:asset_tagS3:A-1S5:notesS5:spareS8:quantityD+1:3+0;",
            Encoding.UTF8.GetString(pinned));
        Assert.Equal("1e958ec9c62ccfcfaa83914261d79259c0e8cda2b8d6dba5c502f450dee9a224", RecordWriteIntent.Digest(pinned));
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public void the_operation_key_frames_tenant_caller_and_operation_id()
    {
        Assert.Equal("8:tenant-a10:operator-77:op-0001", new RecordWriteOperationKey("tenant-a", "operator-7", "op-0001").IdempotencyKey);
        Assert.NotEqual(new RecordWriteOperationKey("a", "b1:c", "d").IdempotencyKey, new RecordWriteOperationKey("a1:b", "c", "d").IdempotencyKey);
        Assert.Equal("1:a4:b1:c1:d", new RecordWriteOperationKey("a", "b1:c", "d").IdempotencyKey);
        Assert.Equal("4:a1:b1:c1:d", new RecordWriteOperationKey("a1:b", "c", "d").IdempotencyKey);
    }

    [Fact]
    [Trait("Holds", "records-eng-6")]
    public async Task new_writes_validate_the_submitted_numeric_lexemes_against_existing_facets()
    {
        var fixture = await Measured();

        var amount = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-amount", """{"amount":3.0}""", null, "eam.measured"));
        var amountResult = fixture.Registry.LastResult!;
        var price = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-price", """{"price":12.30}""", null, "eam.measured"));
        var priceResult = fixture.Registry.LastResult!;
        var measure = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-measure", """{"measure":3.0}""", null, "eam.measured"));

        // fraction_digits 0 refuses 3.0 and fraction_digits 1 refuses 12.30: the numbers reach the facets as submitted.
        Assert.Equal("validate", amount.Refusal!.Stage);
        Assert.Equal("entity.validation.body_invalid", amount.Refusal.Code);
        Assert.Equal(["/amount"], amount.Refusal.Pointers);
        var amountError = Assert.Single(amountResult.Errors);
        Assert.Equal("/amount", amountError.JsonPointer);
        Assert.Equal("field.fraction_digits_exceeded", amountError.Code);
        Assert.Equal(["/price"], price.Refusal!.Pointers);
        var priceError = Assert.Single(priceResult.Errors);
        Assert.Equal("/price", priceError.JsonPointer);
        Assert.Equal("field.fraction_digits_exceeded", priceError.Code);
        Assert.True(measure.Committed);
        var (_, row, _) = Assert.Single(fixture.Port.Published);
        Assert.Equal("""{"measure":3.0}""", Encoding.UTF8.GetString(row.Body.Span));
        Assert.Equal("""{"measure":3.0}""", Encoding.UTF8.GetString(fixture.Registry.LastValidated!));
        Assert.Equal("5b69416220bf597b7823bb74490638a46457205bb7423e487fab83a06ee9f063", row.IntentDigest);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task an_equivalent_numeric_retry_returns_the_first_receipt_without_re_executing()
    {
        var fixture = await Measured();
        var first = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-n", """{"measure":3.0}""", null, "eam.measured"));
        var events = fixture.Port.Events.ToArray();

        var integer = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-n", """{"measure":3}""", null, "eam.measured"));
        var exponent = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-n", """{"measure":3e0}""", null, "eam.measured"));
        var text = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-n", """{"measure":"3"}""", null, "eam.measured"));

        Assert.True(integer.Replayed);
        Assert.True(exponent.Replayed);
        Assert.Equal(first.Receipt, integer.Receipt);
        Assert.Equal(first.Receipt, exponent.Receipt);
        Assert.Equal(events, fixture.Port.Events);
        Assert.Equal(1, fixture.Registry.Validations);
        var (_, row, _) = Assert.Single(fixture.Port.Published);
        Assert.Equal("""{"measure":3.0}""", Encoding.UTF8.GetString(row.Body.Span));
        Assert.Equal("commit", text.Refusal!.Stage);
        Assert.Equal("records.write.replay_conflict", text.Refusal.Code);
        Assert.Null(text.Receipt);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task a_zero_padded_exponent_retry_replays_and_an_exponent_beyond_the_bound_refuses_before_any_lookup()
    {
        var fixture = await Measured();
        var first = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-e", """{"measure":3.0}""", null, "eam.measured"));
        var events = fixture.Port.Events.ToArray();

        var padded = await fixture.Operation.CreateAsync(new("tenant-a", Actor),
            Request("op-e", """{"measure":3e+0000000000000000000000000000}""", null, "eam.measured"));
        var lookups = fixture.Port.Lookups.Count;
        var beyond = await fixture.Operation.CreateAsync(new("tenant-a", Actor),
            Request("op-e", """{"measure":3e+99999999999999999999}""", null, "eam.measured"));

        Assert.True(padded.Replayed);
        Assert.Equal(first.Receipt, padded.Receipt);
        Assert.Equal(("validate", "entity.validation.body_invalid"), (beyond.Refusal!.Stage, beyond.Refusal.Code));
        Assert.Empty(beyond.Refusal.Pointers);
        Assert.Null(beyond.Receipt);
        Assert.Equal(lookups, fixture.Port.Lookups.Count);
        Assert.Equal(events, fixture.Port.Events);
        Assert.Equal(1, fixture.Registry.Validations);
    }

    [Theory]
    [Trait("Holds", "records-eng-8")]
    [InlineData("", 19)]
    [InlineData("+", 20)]
    [InlineData("-", 19)]
    [InlineData("", 1_000_000)]
    public async Task a_body_number_whose_exponent_exceeds_the_bound_refuses_before_any_lookup(string sign, int nines)
    {
        var fixture = await Published();
        var body = """{"asset_tag":"A-1","quantity":1e""" + sign + new string('9', nines) + "}";

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-exponent", body));

        Assert.Equal(("validate", "entity.validation.body_invalid"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Empty(outcome.Refusal.Pointers);
        Assert.Null(outcome.Receipt);
        Assert.Same(Assert.Single(fixture.Gate.Returned), outcome.Decision);
        Assert.Empty(fixture.Port.Lookups);
        Assert.Empty(fixture.Port.Events);
        Assert.Equal(0, fixture.Catalogue.HeadReads);
        Assert.Equal(0, fixture.Registry.Validations);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task another_caller_or_tenant_reusing_the_operation_id_never_sees_the_first_receipt()
    {
        var fixture = await Published();
        var first = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));

        var otherCaller = await fixture.Operation.CreateAsync(new("tenant-a", "operator-8"), Request("op-0001", """{"asset_tag":"A-9"}"""));
        var otherTenant = await fixture.Operation.CreateAsync(new("tenant-b", Actor), Request("op-0001", ValidBody));

        Assert.True(otherCaller.Committed);
        Assert.False(otherCaller.Replayed);
        Assert.NotEqual(first.Receipt!.RecordId, otherCaller.Receipt!.RecordId);
        // tenant-b has no such Record Type; its own scope finds no operation, so it binds and refuses, disclosing nothing.
        Assert.Equal("bind", otherTenant.Refusal!.Stage);
        Assert.Equal("entity.validation.schema_unknown", otherTenant.Refusal.Code);
        Assert.Null(otherTenant.Receipt);
        Assert.Equal(["8:tenant-a10:operator-77:op-0001", "8:tenant-a10:operator-87:op-0001", "8:tenant-b10:operator-77:op-0001"],
            fixture.Port.Lookups.Select(key => key.IdempotencyKey));
        Assert.Equal(2, fixture.Port.Published.Count);
    }

    [Fact]
    [Trait("Holds", "records-eng-10")]
    public async Task an_unpinned_retry_after_republication_returns_the_original_binding_and_a_new_operation_binds_the_head()
    {
        var fixture = await Published();
        var first = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-x", """{"asset_tag":"A-1"}"""));
        await RekindAssetTagToCount(fixture);
        var events = fixture.Port.Events.ToArray();

        var retry = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-x", """{"asset_tag":"A-1"}"""));
        var fresh = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-y", """{"asset_tag":"A-1"}"""));

        Assert.True(retry.Replayed);
        Assert.Equal(first.Receipt, retry.Receipt);
        Assert.Equal("1.0.0", retry.Receipt!.Version);
        Assert.Equal(fixture.Publication.Schema.Id, retry.Receipt.Schema);
        Assert.Equal(fixture.Publication.Revision.Digest, retry.Receipt.DefinitionDigest);
        Assert.Equal("validate", fresh.Refusal!.Stage);
        Assert.Equal("entity.validation.body_invalid", fresh.Refusal.Code);
        // The retry neither bound nor validated: only the first write and the fresh one did.
        Assert.Equal(2, fixture.Catalogue.HeadReads);
        Assert.Equal(2, fixture.Registry.Validations);
        Assert.Equal(events.Concat(new[] { "begin", "rollback", "dispose" }), fixture.Port.Events);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task an_explicit_pin_binds_that_version_and_a_changed_or_dropped_pin_conflicts()
    {
        var fixture = await Published();
        await RekindAssetTagToCount(fixture);

        var pinned = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-p", """{"asset_tag":"A-1"}""", "1.0.0"));
        var same = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-p", """{"asset_tag":"A-1"}""", "1.0.0"));
        var moved = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-p", """{"asset_tag":"A-1"}""", "1.1.0"));
        var dropped = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-p", """{"asset_tag":"A-1"}"""));
        var unknown = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-q", """{"asset_tag":"A-1"}""", "9.9.9"));

        Assert.True(pinned.Committed);
        Assert.Equal("1.0.0", pinned.Receipt!.Version);
        Assert.Equal("1.0.0", Assert.Single(fixture.Port.Published).Row.RequestedVersion);
        Assert.True(same.Replayed);
        Assert.Equal(pinned.Receipt, same.Receipt);
        Assert.Equal(("commit", "records.write.replay_conflict"), (moved.Refusal!.Stage, moved.Refusal.Code));
        Assert.Equal(("commit", "records.write.replay_conflict"), (dropped.Refusal!.Stage, dropped.Refusal.Code));
        Assert.Equal(("bind", "entity.validation.schema_unknown"), (unknown.Refusal!.Stage, unknown.Refusal.Code));
        // Only the first pinned write and the unknown pin read the catalogue; conflicts were settled before any bind.
        Assert.Equal(2, fixture.Catalogue.PinReads);
        Assert.Equal(0, fixture.Catalogue.HeadReads);
    }

    [Fact]
    [Trait("Holds", "records-eng-2")]
    public async Task a_current_gate_denial_after_a_commit_discloses_neither_the_receipt_nor_a_conflict()
    {
        var fixture = await Published();
        Assert.True((await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody))).Committed);
        fixture.Gate.Allow = false;
        var events = fixture.Port.Events.ToArray();

        var replay = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));
        var changed = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", """{"asset_tag":"A-2"}"""));

        Assert.Equal(("authorize", "NoEffectiveRole"), (replay.Refusal!.Stage, replay.Refusal.Code));
        Assert.Equal(("authorize", "NoEffectiveRole"), (changed.Refusal!.Stage, changed.Refusal.Code));
        Assert.Null(replay.Receipt);
        Assert.Null(changed.Receipt);
        Assert.Single(fixture.Port.Lookups);
        Assert.Equal(events, fixture.Port.Events);
        Assert.Equal(3, fixture.Gate.Requests.Count);
    }

    [Theory]
    [Trait("Holds", "records-run-3")]
    [InlineData("records-write-intent/0", "d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5", true, "op-0001")]
    [InlineData("records-write-intent/2", "d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5", true, "op-0001")]
    [InlineData("records-write-intent/1", "", true, "op-0001")]
    [InlineData("records-write-intent/1", "d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5", false, "op-0001")]
    [InlineData("records-write-intent/1", "d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5", true, "op-other")]
    public async Task an_unsupported_or_malformed_stored_operation_refuses_without_disclosure(
        string version, string digest, bool hasReceipt, string receiptOperationId)
    {
        var fixture = await Published();
        var stored = new RecordWriteReceipt("0123456789abcdef0123456789abcdef", receiptOperationId, "eam.asset-class", "1.0.0",
            fixture.Publication.Revision.Digest, fixture.Publication.Schema.Id, At);
        fixture.Port.Seed(new("tenant-a", Actor, "op-0001"), new(version, digest, hasReceipt ? stored : null));

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));

        Assert.Equal(("commit", "records.write.receipt_unsupported"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Null(outcome.Receipt);
        Assert.Empty(outcome.Refusal.Pointers);
        Assert.Empty(fixture.Port.Events);
        Assert.Equal(0, fixture.Catalogue.HeadReads);
    }

    [Fact]
    [Trait("Holds", "records-eng-8")]
    public async Task a_body_string_that_is_not_unicode_text_refuses_before_any_lookup()
    {
        var fixture = await Published();
        var body = new JsonObject { ["asset_tag"] = "A-1", ["notes"] = "\ud800" };

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), new("eam.asset-class", "op-surrogate", body));

        Assert.Equal(("validate", "entity.validation.body_invalid"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Empty(outcome.Refusal.Pointers);
        Assert.Null(outcome.Receipt);
        Assert.Empty(fixture.Port.Lookups);
        Assert.Empty(fixture.Port.Events);
        Assert.Equal(0, fixture.Catalogue.HeadReads);
        Assert.Single(fixture.Gate.Requests);
    }

    [Fact]
    [Trait("Holds", "records-eng-8")]
    public async Task a_parsed_lone_surrogate_escape_refuses_before_any_lookup()
    {
        var fixture = await Published();

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor),
            Request("op-escape", """{"asset_tag":"A-1","notes":"\ud800"}"""));

        Assert.Equal(("validate", "entity.validation.body_invalid"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Empty(outcome.Refusal.Pointers);
        Assert.Null(outcome.Receipt);
        Assert.Empty(fixture.Port.Lookups);
        Assert.Empty(fixture.Port.Events);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task a_string_value_held_as_another_clr_type_encodes_as_its_json_string()
    {
        var fixture = await Published();
        var body = new JsonObject { ["asset_tag"] = "A-1", ["notes"] = JsonValue.Create(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")) };

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), new("eam.asset-class", "op-guid", body));

        Assert.True(outcome.Committed);
        var (_, row, _) = Assert.Single(fixture.Port.Published);
        Assert.Equal("""{"asset_tag":"A-1","notes":"0f8fad5b-d9cb-469f-a165-70867728950e"}""", Encoding.UTF8.GetString(row.Body.Span));
        Assert.Equal(RecordWriteIntent.Digest(RecordWriteIntent.Encode("eam.asset-class", null,
            JsonNode.Parse("""{"asset_tag":"A-1","notes":"0f8fad5b-d9cb-469f-a165-70867728950e"}""")!.AsObject())), row.IntentDigest);
    }

    [Fact]
    [Trait("Holds", "records-eng-26")]
    public async Task a_concurrent_first_write_is_settled_by_the_atomic_operation_stage()
    {
        var fixture = await Published();
        var first = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-race", ValidBody));

        // Each later request looks up before the first commit is visible to it, so it reaches the atomic stage.
        fixture.Port.HideNextLookup = true;
        var same = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-race", ValidBody));
        fixture.Port.HideNextLookup = true;
        var divergent = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-race", """{"asset_tag":"A-2"}"""));

        Assert.True(same.Replayed);
        Assert.Equal(first.Receipt, same.Receipt);
        Assert.Equal(("commit", "records.write.replay_conflict"), (divergent.Refusal!.Stage, divergent.Refusal.Code));
        Assert.Equal(["/operation_id"], divergent.Refusal.Pointers);
        Assert.Null(divergent.Receipt);
        Assert.Single(fixture.Port.Published);
        Assert.Equal(
        [
            "begin", "operation", "record", "audit", "commit", "dispose",
            "begin", "operation", "record", "audit", "commit", "dispose",
            "begin", "operation", "record", "audit", "commit", "dispose",
        ], fixture.Port.Events);
    }

    // ---- 003c (D6, approved 2026-10-10): records.write.replay_conflict names the wire member /operation_id, and
    // ---- records.write.receipt_unsupported separates an accepted operation without a readable receipt from a lookup miss.

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task a_replay_conflict_names_only_the_operation_id_wire_member_and_discloses_no_digest()
    {
        var fixture = await Published();
        await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));

        // Once settled by the lookup, once by the atomic stage of a concurrent first write.
        var looked = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", """{"asset_tag":"A-2"}"""));
        fixture.Port.HideNextLookup = true;
        var staged = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", """{"asset_tag":"A-3"}"""));

        foreach (var conflict in new[] { looked, staged })
        {
            Assert.Equal("commit", conflict.Refusal!.Stage);
            Assert.Equal("records.write.replay_conflict", conflict.Refusal.Code);
            Assert.Equal(["/operation_id"], conflict.Refusal.Pointers);
            Assert.Null(conflict.Receipt);
            Assert.False(conflict.Replayed);
            var disclosed = $"{conflict.Refusal.Stage}|{conflict.Refusal.Code}|{string.Join(",", conflict.Refusal.Pointers)}";
            Assert.DoesNotContain("d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5", disclosed);
            Assert.DoesNotContain("op-0001", disclosed);
        }
        Assert.Single(fixture.Port.Published);
    }

    [Theory]
    [Trait("Holds", "records-run-3")]
    [Trait("Holds", "records-eng-26")]
    // An accepted operation found by the lookup: settled before any transaction opens.
    [InlineData(false, null, new string[0])]
    [InlineData(false, "op-other", new string[0])]
    // The same accepted operation reached through the atomic stage (the lookup missed it): the host settles it Replayed
    // and writes nothing new; the missing or foreign receipt still refuses.
    [InlineData(true, null, new[] { "begin", "operation", "record", "audit", "commit", "dispose" })]
    [InlineData(true, "op-other", new[] { "begin", "operation", "record", "audit", "commit", "dispose" })]
    public async Task an_accepted_operation_without_a_readable_receipt_refuses_and_never_inserts_a_record(
        bool lookupMisses, string? receiptOperationId, string[] events)
    {
        var fixture = await Published();
        var stored = receiptOperationId is null ? null : new RecordWriteReceipt("0123456789abcdef0123456789abcdef", receiptOperationId,
            "eam.asset-class", "1.0.0", fixture.Publication.Revision.Digest, fixture.Publication.Schema.Id, At);
        // records-write-intent/1 of (eam.asset-class, unpinned, ValidBody), frozen by the reference encoder.
        fixture.Port.Seed(new("tenant-a", Actor, "op-0001"),
            new("records-write-intent/1", "d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5", stored));

        fixture.Port.HideNextLookup = lookupMisses;
        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));
        fixture.Port.HideNextLookup = lookupMisses;
        var again = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));

        foreach (var refused in new[] { outcome, again })
        {
            Assert.Equal(("commit", "records.write.receipt_unsupported"), (refused.Refusal!.Stage, refused.Refusal.Code));
            Assert.Empty(refused.Refusal.Pointers);
            Assert.Null(refused.Receipt);
            Assert.False(refused.Replayed);
        }
        Assert.Empty(fixture.Port.Published);
        Assert.Equal(events.Concat(events), fixture.Port.Events);

        // A lookup miss for another operation id is a first-seen operation: it binds, validates and commits its own record.
        var fresh = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0002", ValidBody));
        Assert.True(fresh.Committed);
        Assert.False(fresh.Replayed);
        Assert.Equal("op-0002", Assert.Single(fixture.Port.Published).Row.OperationId);
    }

    [Theory]
    [Trait("Holds", "records-run-3")]
    [InlineData(false, new string[0])]
    [InlineData(true, new[] { "begin", "operation", "record", "audit", "commit", "dispose" })]
    public async Task an_accepted_operation_without_a_receipt_still_conflicts_for_another_intent_and_inserts_nothing(
        bool lookupMisses, string[] events)
    {
        var fixture = await Published();
        fixture.Port.Seed(new("tenant-a", Actor, "op-0001"),
            new("records-write-intent/1", "d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5", null));
        fixture.Port.HideNextLookup = lookupMisses;

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", """{"asset_tag":"A-2"}"""));

        // The lookup and the atomic stage settle the same stored state alike: another intent conflicts, needing no receipt.
        Assert.Equal(("commit", "records.write.replay_conflict"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Equal(["/operation_id"], outcome.Refusal.Pointers);
        Assert.Null(outcome.Receipt);
        Assert.Empty(fixture.Port.Published);
        Assert.Equal(events, fixture.Port.Events);
    }

    [Theory]
    [Trait("Holds", "records-run-3")]
    [InlineData("records-write-intent/0", "d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5")]
    [InlineData("records-write-intent/1", "")]
    public async Task an_unsupported_or_blank_stored_encoding_settles_alike_through_the_atomic_stage(string version, string digest)
    {
        var fixture = await Published();
        var stored = new RecordWriteReceipt("0123456789abcdef0123456789abcdef", "op-0001", "eam.asset-class", "1.0.0",
            fixture.Publication.Revision.Digest, fixture.Publication.Schema.Id, At);
        fixture.Port.Seed(new("tenant-a", Actor, "op-0001"), new(version, digest, stored));
        // The lookup misses it, so the atomic stage finds it; the host hands back the operation as stored.
        fixture.Port.HideNextLookup = true;

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));

        // The same answer the lookup gives for this stored state: never a conflict, never a disclosure, never an insert.
        Assert.Equal(("commit", "records.write.receipt_unsupported"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Empty(outcome.Refusal.Pointers);
        Assert.Null(outcome.Receipt);
        Assert.Empty(fixture.Port.Published);
        Assert.Equal(["begin", "operation", "record", "audit", "commit", "dispose"], fixture.Port.Events);
        Assert.Same(Assert.Single(fixture.Gate.Returned), outcome.Decision);
    }

    [Theory]
    [Trait("Holds", "records-run-3")]
    // Same intent: the host answers Replayed. Another intent: the host answers Conflict.
    [InlineData("""{"asset_tag":"A-1","quantity":3,"notes":"spare"}""")]
    [InlineData("""{"asset_tag":"A-2"}""")]
    public async Task a_replayed_or_conflict_commit_without_the_stored_operation_refuses_and_discloses_nothing(string body)
    {
        var fixture = await Published();
        var first = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));
        fixture.Port.HideNextLookup = true;
        fixture.Port.CommitWithoutStored = true;

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", body));

        // Even a readable receipt beside the disposition is not used: without the stored operation nothing settles.
        Assert.Equal(("commit", "records.write.receipt_unsupported"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Empty(outcome.Refusal.Pointers);
        Assert.Null(outcome.Receipt);
        Assert.False(outcome.Replayed);
        Assert.False(outcome.Committed);
        var disclosed = $"{outcome.Refusal.Stage}|{outcome.Refusal.Code}|{string.Join(",", outcome.Refusal.Pointers)}";
        Assert.DoesNotContain(first.Receipt!.RecordId, disclosed);
        Assert.DoesNotContain("d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5", disclosed);
        // Nothing new was published, and the evidence (persisted nowhere) comes back for the trace plumbing.
        Assert.Single(fixture.Port.Published);
        Assert.Same(fixture.Gate.Returned[1], outcome.Decision);
    }

    [Theory]
    [Trait("Holds", "records-run-3")]
    [InlineData("missing")]
    [InlineData("foreign")]
    public async Task a_created_commit_whose_receipt_does_not_name_the_operation_refuses_and_returns_no_decision(string receipt)
    {
        var fixture = await Published();
        fixture.Port.CreatedReceipt = receipt;

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));

        Assert.Equal(("commit", "records.write.receipt_unsupported"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Empty(outcome.Refusal.Pointers);
        Assert.Null(outcome.Receipt);
        Assert.False(outcome.Replayed);
        // The host did create the record with the evidence as its snapshot, so the evidence is not handed out again.
        var (_, row, audit) = Assert.Single(fixture.Port.Published);
        Assert.Contains("\"target:record/" + row.RecordId + "\"", Encoding.UTF8.GetString(audit.AuthoritySnapshot.Span));
        Assert.Null(outcome.Decision);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task the_same_payload_under_another_operation_id_is_a_new_operation_and_the_same_id_with_another_payload_conflicts()
    {
        var fixture = await Published();
        var first = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-a", ValidBody));

        var sameBodyOtherId = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-b", ValidBody));
        var sameIdOtherBody = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-a", """{"asset_tag":"A-2"}"""));
        var otherIdReplay = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-b", ValidBody));

        Assert.True(sameBodyOtherId.Committed);
        Assert.False(sameBodyOtherId.Replayed);
        Assert.NotEqual(first.Receipt!.RecordId, sameBodyOtherId.Receipt!.RecordId);
        Assert.Equal("op-b", sameBodyOtherId.Receipt.OperationId);
        Assert.Equal(("commit", "records.write.replay_conflict"), (sameIdOtherBody.Refusal!.Stage, sameIdOtherBody.Refusal.Code));
        Assert.Equal(["/operation_id"], sameIdOtherBody.Refusal.Pointers);
        // Each id replays only its own receipt.
        Assert.True(otherIdReplay.Replayed);
        Assert.Equal(sameBodyOtherId.Receipt, otherIdReplay.Receipt);
        Assert.Equal(["8:tenant-a10:operator-74:op-a", "8:tenant-a10:operator-74:op-b"],
            fixture.Port.Published.Select(entry => entry.Operation.IdempotencyKey));
        // Both rows carry the same intent digest: the operation id, not the payload, is the identity.
        Assert.Equal(["d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5", "d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5"],
            fixture.Port.Published.Select(entry => entry.Row.IntentDigest));
    }

    // ---- 003c (D6, approved 2026-10-10): an operation id is nonempty, at most 255 UTF-8 bytes, and never trimmed or
    // ---- normalized. Ids are built here from a literal unit, count and tail; the byte count column is the oracle.

    [Theory]
    [Trait("Holds", "records-run-3")]
    [InlineData("a", 255, "", 255, "255")]
    [InlineData("\u00e9", 127, "a", 255, "128")]
    [InlineData("\u20ac", 85, "", 255, "85")]
    [InlineData("a", 251, "\U0001F600", 255, "253")]
    [InlineData(" ", 1, "", 1, "1")]
    public async Task an_operation_id_of_at_most_255_utf8_bytes_is_accepted_exactly_as_sent(
        string unit, int count, string tail, int bytes, string utf16Length)
    {
        var fixture = await Published();
        var operationId = string.Concat(Enumerable.Repeat(unit, count)) + tail;
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(operationId));

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request(operationId, ValidBody));

        Assert.True(outcome.Committed);
        Assert.False(outcome.Replayed);
        Assert.Equal(operationId, outcome.Receipt!.OperationId);
        var (operation, row, _) = Assert.Single(fixture.Port.Published);
        Assert.Equal(operationId, row.OperationId);
        Assert.Equal("8:tenant-a10:operator-7" + utf16Length + ":" + operationId, operation.IdempotencyKey);
    }

    [Theory]
    [Trait("Holds", "records-run-3")]
    [InlineData("", 0, "", 0)]
    [InlineData("a", 256, "", 256)]
    [InlineData("\u00e9", 128, "", 256)]
    [InlineData("\u20ac", 85, "a", 256)]
    [InlineData("a", 252, "\U0001F600", 256)]
    [InlineData("\U0001F600", 64, "", 256)]
    [InlineData("a", 1000, "", 1000)]
    public async Task an_empty_or_over_255_utf8_byte_operation_id_refuses_at_its_pointer_after_the_gate(string unit, int count, string tail, int bytes)
    {
        var fixture = await Published();
        var operationId = string.Concat(Enumerable.Repeat(unit, count)) + tail;
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(operationId));

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request(operationId, ValidBody));

        Assert.Equal(("validate", "records.write.operation_id_invalid"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Equal(["/operation_id"], outcome.Refusal.Pointers);
        Assert.Null(outcome.Receipt);
        // The gate decided first; nothing was looked up, bound or staged.
        Assert.Same(Assert.Single(fixture.Gate.Returned), outcome.Decision);
        Assert.Empty(fixture.Port.Lookups);
        Assert.Empty(fixture.Port.Events);
        Assert.Equal(0, fixture.Catalogue.HeadReads);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task an_operation_id_that_is_not_unicode_text_refuses_at_its_pointer()
    {
        var fixture = await Published();

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-\ud800", ValidBody));

        Assert.Equal(("validate", "records.write.operation_id_invalid"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Equal(["/operation_id"], outcome.Refusal.Pointers);
        Assert.Empty(fixture.Port.Lookups);
        Assert.Empty(fixture.Port.Events);
    }

    [Fact]
    [Trait("Holds", "records-run-3")]
    public async Task an_operation_id_is_never_trimmed_or_normalized()
    {
        var fixture = await Published();
        string[] ids = ["op", " op", "op ", " ", "\u00e9", "e\u0301"];

        foreach (var id in ids)
            Assert.False((await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request(id, ValidBody))).Replayed);
        var replay = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request(" op", ValidBody));

        // Six distinct operations, each stored and keyed exactly as sent; the NFC and NFD spellings of e-acute differ.
        Assert.Equal(ids, fixture.Port.Published.Select(entry => entry.Row.OperationId));
        Assert.Equal(["8:tenant-a10:operator-72:op", "8:tenant-a10:operator-73: op", "8:tenant-a10:operator-73:op ",
            "8:tenant-a10:operator-71: ", "8:tenant-a10:operator-71:\u00e9", "8:tenant-a10:operator-72:e\u0301"],
            fixture.Port.Published.Select(entry => entry.Operation.IdempotencyKey));
        Assert.True(replay.Replayed);
        Assert.Equal(" op", replay.Receipt!.OperationId);
        Assert.Equal(fixture.Port.Published[1].Row.RecordId, replay.Receipt.RecordId);
    }

    // ---- 003b: the gate's own decision evidence travels with every outcome (RW-1), through
    // ---- AccessProvider.CheckWithEvidenceAsync (hlp.foundation.actor revision 4); the gate is asked once.

    [Fact]
    [Trait("Holds", "records-eng-2")]
    public async Task a_denial_carries_the_gates_own_four_step_trace_and_reaches_nothing_else()
    {
        var fixture = await Published(allow: false);

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-deny", ValidBody));

        var asked = Assert.Single(fixture.Gate.Requests);
        Assert.Same(Assert.Single(fixture.Gate.Returned), outcome.Decision);
        var steps = outcome.Decision!.Project();
        Assert.Equal(["act", "effective-roles", "standings", "verdict"], steps.Select(step => step.Stage));
        Assert.Equal(["kind:Gate", "act:records:write", "target:record/" + asked.Record.Id, "principal:operator-7", "tenant:tenant-a",
            "at:2030-01-02T03:04:05.0000000+00:00"], steps[0].Facts.ToArray());
        Assert.Equal(["roles:none", "deciding:grant:records-writer"], steps[1].Facts.ToArray());
        Assert.Equal(["standings:none"], steps[2].Facts.ToArray());
        Assert.Equal(["verdict:denied", "refusal:NoEffectiveRole", "version:2"], steps[3].Facts.ToArray());
        Assert.Equal(("authorize", "NoEffectiveRole"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Empty(fixture.Port.Lookups);
        Assert.Empty(fixture.Port.Events);
    }

    [Fact]
    [Trait("Holds", "records-eng-2")]
    public async Task a_denial_after_a_commit_returns_the_current_denial_and_discloses_nothing()
    {
        var fixture = await Published();
        var first = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));
        fixture.Gate.Allow = false;
        var events = fixture.Port.Events.ToArray();

        var denied = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));

        Assert.Equal(("authorize", "NoEffectiveRole"), (denied.Refusal!.Stage, denied.Refusal.Code));
        Assert.Same(fixture.Gate.Returned[1], denied.Decision);
        Assert.Equal(events, fixture.Port.Events);
        Assert.Null(denied.Receipt);
        Assert.False(denied.Decision!.Allowed);
        Assert.Equal("NoEffectiveRole", denied.Decision.Refusal);
        Assert.NotEqual(first.Receipt!.RecordId, denied.Decision.RecordId);
        Assert.Equal(["verdict:denied", "refusal:NoEffectiveRole", "version:2"], denied.Decision.Project()[3].Facts.ToArray());
        Assert.Single(fixture.Port.Lookups);
    }

    [Fact]
    [Trait("Holds", "records-eng-2")]
    public async Task an_allowed_create_commits_its_decision_and_its_replay_returns_its_own()
    {
        var fixture = await Published();

        var created = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));
        var replay = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));

        // 003c: the create's evidence was committed with its record, so the outcome does not hand it out for a second append.
        Assert.Null(created.Decision);
        var (_, row, audit) = Assert.Single(fixture.Port.Published);
        Assert.Equal(created.Receipt!.RecordId, row.RecordId);
        Assert.Equal(row.RecordId, fixture.Gate.Returned[0].RecordId);
        Assert.Contains("\"target:record/" + row.RecordId + "\"", Encoding.UTF8.GetString(audit.AuthoritySnapshot.Span));
        Assert.Same(fixture.Gate.Returned[1], replay.Decision);
        Assert.True(replay.Decision!.Allowed);
        // The replay is decided afresh for its own minted context; the receipt it returns is the first one.
        Assert.NotEqual(replay.Receipt!.RecordId, replay.Decision.RecordId);
        Assert.Equal(["verdict:allowed", "refusal:None", "version:2"], replay.Decision.Project()[3].Facts.ToArray());
        Assert.Equal(2, fixture.Gate.Requests.Count);
    }

    // ---- 003c (D6, approved 2026-10-10): the gate's own evidence is the create's authority snapshot, staged in the one
    // ---- audit call through KernelTransactionBoundary and committed with the record, never attached after the commit.

    [Fact]
    [Trait("Holds", "records-eng-26")]
    [Trait("Holds", "records-eng-2")]
    public async Task a_create_commits_the_gates_authority_snapshot_with_its_record_and_audit_in_one_transaction()
    {
        var fixture = await Published();

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));

        Assert.True(outcome.Committed);
        // One audit stage carries the snapshot before the one commit; nothing is appended after it.
        Assert.Equal(["begin", "operation", "record", "audit", "commit", "dispose"], fixture.Port.Events);
        var staged = Assert.Single(fixture.Port.StagedAudits);
        var (_, row, audit) = Assert.Single(fixture.Port.Published);
        Assert.Same(staged, audit);
        Assert.Equal(
            "{\"tenant\":\"tenant-a\",\"principal\":\"operator-7\",\"version\":2,\"steps\":["
            + "{\"ordinal\":1,\"stage\":\"act\",\"facts\":[\"kind:Gate\",\"act:records:write\",\"target:record/" + row.RecordId
            + "\",\"principal:operator-7\",\"tenant:tenant-a\",\"at:2030-01-02T03:04:05.0000000\\u002B00:00\"]},"
            + "{\"ordinal\":2,\"stage\":\"effective-roles\",\"facts\":[\"roles:none\",\"deciding:grant:records-writer\"]},"
            + "{\"ordinal\":3,\"stage\":\"standings\",\"facts\":[\"standings:none\"]},"
            + "{\"ordinal\":4,\"stage\":\"verdict\",\"facts\":[\"verdict:allowed\",\"refusal:None\",\"version:2\"]}]}",
            Encoding.UTF8.GetString(audit.AuthoritySnapshot.Span));
        // It is the gate's own evidence for this very write, and it is persisted, so the outcome does not return it.
        Assert.Equal(row.RecordId, Assert.Single(fixture.Gate.Returned).RecordId);
        Assert.Null(outcome.Decision);
        // The evidence and the intent digest stay out of the payload and the receipt.
        var payloadText = Encoding.UTF8.GetString(audit.Payload.Span);
        Assert.DoesNotContain("grant:records-writer", payloadText);
        Assert.DoesNotContain("d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5", payloadText);
        Assert.DoesNotContain("d8aaf9e5e4d27d1d3292ab997a13f2e3bba54091ffc7092a476eb62c3af82af5", outcome.Receipt!.ToString());
    }

    [Theory]
    [Trait("Holds", "records-eng-26")]
    [InlineData("operation", 0)]
    [InlineData("record", 0)]
    [InlineData("audit", 0)]
    [InlineData("commit", 1)]
    public async Task a_fault_after_the_gate_commits_neither_the_record_nor_its_audit_nor_its_authority_snapshot(string fault, int stagedAudits)
    {
        var fixture = await Published();
        fixture.Port.Fault = fault;

        await Assert.ThrowsAsync<InjectedFault>(() =>
            fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody)).AsTask());

        Assert.True(Assert.Single(fixture.Gate.Returned).Allowed);
        Assert.Equal(stagedAudits, fixture.Port.StagedAudits.Count);
        Assert.Empty(fixture.Port.Published);
        Assert.Equal(["rollback", "dispose"], fixture.Port.Events[^2..]);

        // The operation id stays unused: a retry commits one set carrying the retry's own evidence.
        fixture.Port.Fault = null;
        var retry = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));
        Assert.True(retry.Committed);
        var (_, row, audit) = Assert.Single(fixture.Port.Published);
        Assert.Equal(row.RecordId, fixture.Gate.Returned[1].RecordId);
        Assert.Contains("\"target:record/" + row.RecordId + "\"", Encoding.UTF8.GetString(audit.AuthoritySnapshot.Span));
        Assert.DoesNotContain("\"target:record/" + fixture.Gate.Returned[0].RecordId + "\"", Encoding.UTF8.GetString(audit.AuthoritySnapshot.Span));
    }

    [Fact]
    [Trait("Holds", "records-eng-2")]
    public async Task every_allowed_outcome_that_commits_nothing_returns_the_gates_evidence_instead()
    {
        var fixture = await Published();

        var invalid = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-invalid", """{"notes":5}"""));
        await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));
        var conflict = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", """{"asset_tag":"A-2"}"""));
        fixture.Port.HideNextLookup = true;
        var raced = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-0001", ValidBody));

        Assert.Equal(("validate", "entity.validation.body_invalid"), (invalid.Refusal!.Stage, invalid.Refusal.Code));
        Assert.Same(fixture.Gate.Returned[0], invalid.Decision);
        Assert.Equal(("commit", "records.write.replay_conflict"), (conflict.Refusal!.Stage, conflict.Refusal.Code));
        Assert.Same(fixture.Gate.Returned[2], conflict.Decision);
        // The raced replay staged its snapshot, but the host settled it Replayed and wrote nothing.
        Assert.True(raced.Replayed);
        Assert.Same(fixture.Gate.Returned[3], raced.Decision);
        Assert.Single(fixture.Port.Published);
        Assert.Equal(2, fixture.Port.StagedAudits.Count);
    }

    [Fact]
    [Trait("Holds", "records-eng-2")]
    public async Task gate_evidence_for_another_context_refuses_and_is_withheld()
    {
        var fixture = await Published();
        fixture.Gate.DecideAs = asked => asked with { Principal = "mallory" };

        var outcome = await fixture.Operation.CreateAsync(new("tenant-a", Actor), Request("op-forged", ValidBody));

        Assert.Equal(("authorize", "access.decision_mismatch"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Null(outcome.Decision);
        Assert.Null(outcome.Receipt);
        Assert.Single(fixture.Gate.Requests);
        Assert.Empty(fixture.Port.Lookups);
        Assert.Empty(fixture.Port.Events);
    }

    [Fact]
    public void the_operation_refuses_a_missing_collaborator()
    {
        var host = Host();
        var access = new AccessProvider(new FixtureGate(true));
        var port = new RecordingRecordPort();
        var clock = new FixedClock(At);

        var kinds = RecordWriteFieldRuntimeTests.FixtureKinds();
        var domains = RecordWriteFieldRuntimeTests.FixtureDomains(clock);
        Assert.Throws<ArgumentNullException>(() => new RecordWriteOperation(null!, host.Records, host.Registry, port, port, clock, kinds, domains));
        Assert.Throws<ArgumentNullException>(() => new RecordWriteOperation(access, null!, host.Registry, port, port, clock, kinds, domains));
        Assert.Throws<ArgumentNullException>(() => new RecordWriteOperation(access, host.Records, null!, port, port, clock, kinds, domains));
        Assert.Throws<ArgumentNullException>(() => new RecordWriteOperation(access, host.Records, host.Registry, null!, port, clock, kinds, domains));
        Assert.Throws<ArgumentNullException>(() => new RecordWriteOperation(access, host.Records, host.Registry, port, null!, clock, kinds, domains));
        Assert.Throws<ArgumentNullException>(() => new RecordWriteOperation(access, host.Records, host.Registry, port, port, null!, kinds, domains));
    }

    [Fact]
    public async Task a_missing_scope_request_or_body_refuses_before_the_gate()
    {
        var fixture = await Published();

        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Operation.CreateAsync(null!, Request("op-1", ValidBody)).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Operation.CreateAsync(new("tenant-a", Actor), null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            fixture.Operation.CreateAsync(new("tenant-a", Actor), new("eam.asset-class", "op-1", null!)).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            fixture.Operation.CreateAsync(new("tenant-a", Actor), new("eam.asset-class", null!, JsonNode.Parse(ValidBody)!.AsObject())).AsTask());

        Assert.Empty(fixture.Gate.Requests);
        Assert.Empty(fixture.Port.Events);
    }

    [Theory]
    [InlineData(" ", Actor, "eam.asset-class", "op-1")]
    [InlineData("tenant-a", " ", "eam.asset-class", "op-1")]
    [InlineData("tenant-a", Actor, " ", "op-1")]
    public async Task a_blank_scope_or_request_member_refuses_before_the_gate(string tenant, string actor, string type, string operationId)
    {
        var fixture = await Published();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => fixture.Operation.CreateAsync(new(tenant, actor),
            new(type, operationId, JsonNode.Parse(ValidBody)!.AsObject())).AsTask());

        Assert.Empty(fixture.Gate.Requests);
        Assert.Empty(fixture.Port.Events);
    }

    private static RecordCreateRequest Request(string operationId, string body)
        => new("eam.asset-class", operationId, JsonNode.Parse(body)!.AsObject());

    private static RecordCreateRequest Request(string operationId, string body, string? version, string type = "eam.asset-class")
        => new(type, operationId, JsonNode.Parse(body)!.AsObject(), version);

    private sealed record Fixture(TestHost Host, RecordTypePublication Publication, RecordWriteOperation Operation,
        FixtureGate Gate, CountingCatalogue Catalogue, CountingRegistry Registry, RecordingRecordPort Port);

    // The real catalogue and registry from the Record Type store tests, with the asset class published at 1.0.0.
    private static async Task<Fixture> Published(bool allow = true, ISchemaRegistry? validatingRegistry = null,
        DefinitionContractWindow? window = null, bool unboundFieldsForCanonicalBytes = false)
    {
        var host = Host();
        var type = RoleGatedAssetClass("1.0.0");
        // Only the two arbitrary-body canonical-byte tests use this genuine unbound declaration.
        // Their existing schema stub isolates encoding; the field runtime itself is never replaced.
        if (unboundFieldsForCanonicalBytes)
            type = type with { Fields = type.Fields.Select(field => field with { Binding = null }).ToArray() };
        await host.Records.CreateDraftAsync(type, "create");
        var publication = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        var catalogue = new CountingCatalogue(host.Catalogue);
        var registry = new CountingRegistry(validatingRegistry ?? host.Registry);
        var records = new RecordTypeDefinitionStore(catalogue, host.Compiler, host.Defaults,
            validatingRegistry is null ? registry : new CountingRegistry(host.Registry), window ?? Window);
        var gate = new FixtureGate(allow);
        var port = new RecordingRecordPort();
        var clock = new FixedClock(At);
        var operation = new RecordWriteOperation(new AccessProvider(gate), records, registry, port, port, clock,
            RecordWriteFieldRuntimeTests.FixtureKinds(), RecordWriteFieldRuntimeTests.FixtureDomains(clock));
        return new(host, publication, operation, gate, catalogue, registry, port);
    }

    // A second published Record Type, eam.measured, whose number fields carry the field runtime's existing numeric facets.
    private static async Task<Fixture> Measured()
    {
        var kinds = new FieldKindRuntime(new FieldKindRegistry([new("amount", "1.0.0", null, FieldScalarValueShape.Number)]));
        var validator = new RecordsIntentValidator();
        var compiler = new RecordTypeSchemaCompiler(validator, kinds, new SharedValueDomainAdmission());
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Records] = RecordTypeDefinitionStore.Admission(Window, validator),
            [DefinitionKind.Classes] = ClassDefinitionStore.Admission(Window),
        });
        var classes = new ClassDefinitionStore(store);
        await classes.CreateDraftAsync(new(Tenant, "eam", "Equipment", "1.0.0", new(1, 0), "eam-core"), "equipment");
        await classes.PublishAsync(Tenant, "eam.equipment", "1.0.0", 1, "equipment-publish");
        var defaults = new RecordFieldDefaults(kinds);
        var authoring = new RecordTypeDefinitionStore(store, compiler, defaults, registry, Window, roles: CreationRoles);
        await authoring.CreateDraftAsync(new(Tenant, "eam", "Measured", "1.0.0",
            [Amount("amount", "Amount", "4", "0"), Amount("price", "Price", "4", "1"), Amount("measure", "Measure", null, null)],
            new(1, 0), ClassId: "eam.equipment", RecordClass: RecordClass.Master, PackageId: "eam-core",
            CreationGate: RecordCreationGate.Role, CreationRoleId: "asset-author"), "measured");
        var publication = await authoring.PublishAsync(Tenant, "eam.measured", "1.0.0", 1, "measured-publish");
        var catalogue = new CountingCatalogue(store);
        var counting = new CountingRegistry(registry);
        var records = new RecordTypeDefinitionStore(catalogue, compiler, defaults, counting, Window);
        var gate = new FixtureGate(true);
        var port = new RecordingRecordPort();
        var clock = new FixedClock(At);
        var operation = new RecordWriteOperation(new AccessProvider(gate), records, counting, port, port, clock, kinds,
            RecordWriteFieldRuntimeTests.FixtureDomains(clock));
        return new(Host(), publication, operation, gate, catalogue, counting, port);

        static FieldDefinition Amount(string key, string name, string? totalDigits, string? fractionDigits)
        {
            var parameters = new Dictionary<string, string>();
            if (totalDigits is not null) parameters["total_digits"] = totalDigits;
            if (fractionDigits is not null) parameters["fraction_digits"] = fractionDigits;
            return new(key, name, new FieldBindingDefinition(new FieldKindReference("amount", "1.0.0", parameters),
                new FieldConstraintDefinition(false, 0, 1, [], null)));
        }
    }

    // Publishes eam.asset-class 1.1.0 with asset_tag rebound from text to count, as the re-publication case does.
    private static async Task RekindAssetTagToCount(Fixture fixture)
    {
        var head = (await fixture.Host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"))!;
        var rekinded = head with
        {
            Fields =
            [
                head.Fields[0] with
                {
                    Governance = null,
                    Binding = new FieldBindingDefinition(new FieldKindReference("count", "1.0.0", new Dictionary<string, string>()),
                        new FieldConstraintDefinition(true, 0, 1, [], null)),
                },
                head.Fields[1],
                head.Fields[2],
            ],
        };
        await fixture.Host.Records.SaveDraftAsync("eam.asset-class", rekinded, "1.1.0", 2, "rekind");
        await fixture.Host.Records.PublishAsync(Tenant, "eam.asset-class", "1.1.0", 3, "publish-1.1.0");
    }

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    // Stands in for the host's adapter to its existing gate; the verdict is the fixture's, never computed from grants.
    private sealed class FixtureGate(bool allow) : IAuthorizationDecider
    {
        public List<AccessRequest> Requests { get; } = [];
        public List<AuthorizationDecisionEvidence> Returned { get; } = [];
        public bool Allow { get; set; } = allow;
        // When set, the gate reports a decision for another context than it was asked about.
        public Func<AccessRequest, AccessRequest>? DecideAs { get; set; }

        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var evidence = new AuthorizationDecisionEvidence(DecideAs?.Invoke(request) ?? request, Allow,
                Allow ? "None" : "NoEffectiveRole", "grant:records-writer", [], []);
            Returned.Add(evidence);
            return ValueTask.FromResult(evidence);
        }
    }

    private sealed class InjectedFault : Exception { }

    // A host transaction double with the idempotency the port contract assigns to the host: a committed operation
    // replays with the same intent digest and conflicts with another, and its reader answers from the same committed
    // state. Nothing is visible before commit.
    private sealed class RecordingRecordPort : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>, IRecordWriteReceiptReader
    {
        private readonly Dictionary<string, RecordWriteCommittedOperation> _committed = new(StringComparer.Ordinal);

        public List<string> Events { get; } = [];
        public List<(KernelOperationIdentity Operation, RecordWriteRow Row, KernelAuditEvidence Audit)> Published { get; } = [];
        public List<RecordWriteOperationKey> Lookups { get; } = [];
        // Every audit staged into any transaction, committed or not.
        public List<KernelAuditEvidence> StagedAudits { get; } = [];
        public string? Fault { get; set; }
        // Simulates a concurrent writer committing between this request's lookup and its commit.
        public bool HideNextLookup { get; set; }
        // "missing" or "foreign": the receipt a Created commit hands back.
        public string? CreatedReceipt { get; set; }
        // A non-conforming host that reports an accepted operation without returning it as stored.
        public bool CommitWithoutStored { get; set; }

        public void Seed(RecordWriteOperationKey key, RecordWriteCommittedOperation committed) => _committed[key.IdempotencyKey] = committed;

        public ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
        {
            Lookups.Add(key);
            if (HideNextLookup)
            {
                HideNextLookup = false;
                return ValueTask.FromResult<RecordWriteCommittedOperation?>(null);
            }
            return ValueTask.FromResult(_committed.GetValueOrDefault(key.IdempotencyKey));
        }

        public ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("begin");
            if (Fault == "begin") throw new InjectedFault();
            return ValueTask.FromResult<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>>(new Transaction(this));
        }

        private sealed class Transaction(RecordingRecordPort owner) : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>
        {
            private KernelOperationIdentity? _operation;
            private RecordWriteRow? _row;
            private KernelAuditEvidence? _audit;

            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
                => Stage("operation", () => _operation = operation);

            public ValueTask StageRecordAsync(RecordWriteRow record, CancellationToken cancellationToken = default)
                => Stage("record", () => _row = record);

            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
                => Stage("audit", () =>
                {
                    _audit = audit;
                    owner.StagedAudits.Add(audit);
                });

            public ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default)
            {
                owner.Events.Add("commit");
                if (owner.Fault == "commit") throw new InjectedFault();
                var operation = _operation!;
                // The unique operation key: an accepted operation publishes nothing and is returned exactly as stored.
                if (owner._committed.TryGetValue(operation.IdempotencyKey, out var prior))
                {
                    return ValueTask.FromResult(new RecordWriteCommit(prior.IntentDigest == operation.Fingerprint
                        ? RecordWriteCommitDisposition.Replayed : RecordWriteCommitDisposition.Conflict, prior.Receipt)
                    {
                        Stored = owner.CommitWithoutStored ? null : prior,
                    });
                }
                var row = _row!;
                var receipt = new RecordWriteReceipt(row.RecordId, row.OperationId, row.RecordTypeId, row.Version,
                    row.DefinitionDigest, row.Schema, row.RecordedAt);
                owner._committed.Add(operation.IdempotencyKey, new(row.IntentEncodingVersion, row.IntentDigest, receipt));
                owner.Published.Add((operation, row, _audit!));
                // A faulty host may hand back no receipt, or one naming another operation id, for what it created.
                var returned = owner.CreatedReceipt switch
                {
                    "missing" => null,
                    "foreign" => receipt with { OperationId = "op-other" },
                    _ => receipt,
                };
                return ValueTask.FromResult(new RecordWriteCommit(RecordWriteCommitDisposition.Created, returned));
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
            {
                owner.Events.Add("rollback");
                return ValueTask.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                owner.Events.Add("dispose");
                return ValueTask.CompletedTask;
            }

            private ValueTask Stage(string step, Action stage)
            {
                owner.Events.Add(step);
                if (owner.Fault == step) throw new InjectedFault();
                stage();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class CountingRegistry(ISchemaRegistry inner) : ISchemaRegistry
    {
        public int Registrations { get; private set; }
        public int Validations { get; private set; }
        public string? ValidateFault { get; set; }
        public SchemaValidationResult? StubResult { get; set; }
        public byte[]? LastValidated { get; private set; }
        public SchemaValidationResult? LastResult { get; private set; }

        public ValueTask<Schema?> GetAsync(SchemaId id, CancellationToken cancellationToken = default)
            => inner.GetAsync(id, cancellationToken);

        public ValueTask<Schema> RegisterAsync(string jsonSchemaText, IReadOnlyList<SchemaId>? parents = null,
            IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default)
        {
            Registrations++;
            return inner.RegisterAsync(jsonSchemaText, parents, tags, cancellationToken);
        }

        public ValueTask<SchemaValidationResult> ValidateAsync(SchemaId id, ReadOnlyMemory<byte> documentBytes,
            CancellationToken cancellationToken = default)
        {
            Validations++;
            LastValidated = documentBytes.ToArray();
            return ValidateFault switch
            {
                "throw" => throw new InvalidOperationException("registry fault"),
                "cancel" => throw new OperationCanceledException(),
                _ => CaptureAsync(id, documentBytes, cancellationToken),
            };
        }

        private async ValueTask<SchemaValidationResult> CaptureAsync(SchemaId id, ReadOnlyMemory<byte> documentBytes,
            CancellationToken cancellationToken)
        {
            var result = StubResult ?? await inner.ValidateAsync(id, documentBytes, cancellationToken).ConfigureAwait(false);
            LastResult = result;
            return result;
        }

        public IAsyncEnumerable<Schema> ListAsync(string? tagFilter = null, CancellationToken cancellationToken = default)
            => inner.ListAsync(tagFilter, cancellationToken);
    }

    private sealed class CountingCatalogue(IVersionedDefinitionStore inner) : IVersionedDefinitionStore
    {
        public int HeadReads { get; private set; }
        public int PinReads { get; private set; }

        public ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind, CancellationToken cancellationToken = default)
            => inner.ListKeysAsync(tenant, kind, cancellationToken);
        public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
            => inner.SaveDraftAsync(document, expectedRevision, requestId, cancellationToken);
        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision, string requestId, CancellationToken cancellationToken = default)
            => inner.PublishAsync(key, versionId, expectedRevision, requestId, cancellationToken);
        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision, string requestId,
            IReadOnlyList<DefinitionPublishedHeadCondition> conditions, CancellationToken cancellationToken = default)
            => inner.PublishAsync(key, versionId, expectedRevision, requestId, conditions, cancellationToken);
        public ValueTask<DefinitionRevision?> GetPublicationReplayAsync(DefinitionKey key, string versionId, long expectedRevision,
            string requestId, CancellationToken cancellationToken = default)
            => inner.GetPublicationReplayAsync(key, versionId, expectedRevision, requestId, cancellationToken);
        public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId, string draftVersionId, string draftVersion,
            long expectedRevision, string requestId, CancellationToken cancellationToken = default)
            => inner.RestoreAsDraftAsync(key, sourceVersionId, draftVersionId, draftVersion, expectedRevision, requestId, cancellationToken);
        public ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key, CancellationToken cancellationToken = default)
            => inner.ListHistoryAsync(key, cancellationToken);
        public ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key, CancellationToken cancellationToken = default)
        {
            HeadReads++;
            return inner.GetPublishedHeadAsync(key, cancellationToken);
        }
        public ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding, CancellationToken cancellationToken = default)
        {
            PinReads++;
            return inner.ResolvePublishedAsync(binding, cancellationToken);
        }
    }
}
