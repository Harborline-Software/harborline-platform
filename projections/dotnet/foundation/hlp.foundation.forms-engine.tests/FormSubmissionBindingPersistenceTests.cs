using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>
/// D3 review fixes F1 and F4: the submission and projection rows keep the bound schema reference and record type target
/// for recovery, the durable journal stores them in format 2 while still loading format 1 frames, and both stores hand
/// back the stored write on an idempotent replay. Oracles: literal row values and a hand-built format-1 frame whose
/// layout (magic, version, type, reserved, length, SHA-256 header and frame checksums) is written out here.
/// </summary>
public sealed class FormSubmissionBindingPersistenceTests
{
    private static readonly TenantId Tenant = new("tenant-engine");
    private static readonly EntityId Instance = new("harborline", "forms", "binding-1");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-10T12:00:00Z");

    [Fact(DisplayName = "F4: the durable journal keeps the schema reference and record type target across a reopen")]
    public async Task JournalKeepsBindingMembersAcrossReopen()
    {
        using var fixture = new Fixture();
        using (var store = fixture.Open())
            await CommitAsync(store, Commit("op-1"));

        using var reopened = fixture.Open();
        var submission = await reopened.GetAsync(Tenant, Instance);
        var pending = Assert.Single(await reopened.LeasePendingAsync(10));

        Assert.NotNull(submission);
        Assert.Equal(new SchemaId("sha256:binding"), submission.SchemaRef);
        Assert.Equal("notes.entry", submission.RecordTypeId);
        Assert.Equal(new SchemaId("sha256:binding"), pending.SchemaRef);
        Assert.Equal("notes.entry", pending.RecordTypeId);
        Assert.Equal("case-1", pending.CaseReference);
    }

    [Fact(DisplayName = "F4: a new frame is written as journal format 2")]
    public async Task NewFramesAreFormatTwo()
    {
        using var fixture = new Fixture();
        using (var store = fixture.Open())
            await CommitAsync(store, Commit("op-1"));

        var bytes = await File.ReadAllBytesAsync(fixture.JournalPath);

        Assert.Equal(0x4A464C48u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)));
        Assert.Equal(2, bytes[4]);
    }

    [Fact(DisplayName = "F4: a format 1 commit frame still loads, with the binding members absent")]
    public async Task FormatOneFrameStillLoads()
    {
        using var fixture = new Fixture();
        await File.WriteAllBytesAsync(fixture.JournalPath, Frame(1, LegacyCommitPayload()));

        using var store = fixture.Open();
        var submission = await store.GetAsync(Tenant, Instance);

        Assert.NotNull(submission);
        Assert.Equal("legacy-fingerprint", submission.RequestFingerprint);
        Assert.Null(submission.SchemaRef);
        Assert.Null(submission.RecordTypeId);
    }

    [Fact(DisplayName = "F4: a frame of an unknown journal format is refused")]
    public void UnknownFormatIsRefused()
    {
        using var fixture = new Fixture();
        File.WriteAllBytes(fixture.JournalPath, Frame(3, LegacyCommitPayload()));

        var refusal = Assert.Throws<InvalidDataException>(() => fixture.Open());

        Assert.Equal("The Forms submission journal version is unsupported.", refusal.Message);
    }

    [Fact(DisplayName = "F1: both stores return a copy of the stored write on an idempotent replay, and none on a new commit")]
    public async Task ReplayReturnsTheStoredWrite()
    {
        using var fixture = new Fixture();
        using var journal = fixture.Open();
        var memory = new InMemoryFormSubmissionStore();
        foreach (var store in new IFormSubmissionTransactionStore[] { journal, memory })
        {
            var created = await CommitAsync(store, Commit("op-1"));
            var replay = Commit("op-1") with
            {
                Submission = Commit("op-1").Submission with { InstanceId = new EntityId("harborline", "forms", "binding-2") },
            };
            replay = replay with
            {
                Audit = replay.Audit with { InstanceId = replay.Submission.InstanceId, AuditId = "audit-2" },
                Projection = replay.Projection with { InstanceId = replay.Submission.InstanceId, OutboxId = "outbox-2" },
                Receipt = replay.Receipt with { InstanceId = replay.Submission.InstanceId },
            };

            var replayed = await CommitAsync(store, replay);

            Assert.Equal(FormSubmissionCommitDisposition.Created, created.Disposition);
            Assert.Null(created.Stored);
            Assert.Equal(FormSubmissionCommitDisposition.Replayed, replayed.Disposition);
            var stored = Assert.IsType<FormSubmissionCommit>(replayed.Stored);
            Assert.Equal(Instance, stored.Submission.InstanceId);
            Assert.Equal("alice", stored.Submission.ActorId);
            Assert.Equal("case-1", stored.Projection.CaseReference);
            Assert.Equal("notes.entry", stored.Submission.RecordTypeId);
        }
    }

    [Fact(DisplayName = "F4: a commit whose submission and projection rows name different targets is refused before anything is stored")]
    public async Task DisagreeingRowsAreRefused()
    {
        var store = new InMemoryFormSubmissionStore();
        var commit = Commit("op-1");
        commit = commit with { Projection = commit.Projection with { RecordTypeId = "notes.other" } };

        await Assert.ThrowsAsync<ArgumentException>(async () => await CommitAsync(store, commit));

        Assert.Null(await store.GetAsync(Tenant, Instance));
    }

    private static async Task<FormSubmissionCommitResult> CommitAsync(IFormSubmissionTransactionStore store, FormSubmissionCommit commit)
    {
        await using var scope = await store.BeginTransactionAsync();
        return await scope.CommitAsync(commit);
    }

    private static FormSubmissionCommit Commit(string key)
    {
        var party = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var form = new FormDefinitionId("inspection");
        var version = new SemanticVersion(1, 0, 0);
        var schema = new SchemaId("sha256:binding");
        var bytes = "{}"u8.ToArray();
        return new FormSubmissionCommit(
            key,
            new FormSubmissionRecord(Instance, Tenant, party, "alice", form, version, "fingerprint-1", bytes, Now, schema, "notes.entry"),
            new FormMutationAuditEnvelope("audit-1", Instance, Tenant, "alice", bytes, Now),
            new FormProjectionEnvelope("outbox-1", Instance, Tenant, party, "alice", form, version, "case-1", bytes, Now,
                SchemaRef: schema, RecordTypeId: "notes.entry"),
            new FormSubmitReceipt(Instance, Now, FormProjectionStatus.Pending, []));
    }

    // The format 1 commit payload: the member names and shapes of the journal's commit record before the binding members.
    private static byte[] LegacyCommitPayload()
    {
        var instance = Instance.ToString();
        var at = Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var json = $$$"""
            {"IdempotencyKey":"op-legacy",
             "Submission":{"InstanceId":"{{{instance}}}","Tenant":"tenant-engine","PartyId":"11111111-1111-1111-1111-111111111111","ActorId":"alice","FormId":"inspection","VersionMajor":1,"VersionMinor":0,"VersionPatch":0,"RequestFingerprint":"legacy-fingerprint","ProtectedAcceptedCandidate":"e30=","SubmittedAt":"{{{at}}}"},
             "Audit":{"AuditId":"audit-legacy","InstanceId":"{{{instance}}}","Tenant":"tenant-engine","ActorId":"alice","Payload":"e30=","RecordedAt":"{{{at}}}"},
             "Projection":{"OutboxId":"outbox-legacy","InstanceId":"{{{instance}}}","Tenant":"tenant-engine","PartyId":"11111111-1111-1111-1111-111111111111","ActorId":"alice","FormId":"inspection","VersionMajor":1,"VersionMinor":0,"VersionPatch":0,"CaseReference":null,"ProtectedAcceptedValues":"e30=","SubmittedAt":"{{{at}}}","Attempts":0,"LastErrorCode":null},
             "Receipt":{"InstanceId":"{{{instance}}}","SubmittedAt":"{{{at}}}","ProjectionStatus":0,"Skips":[]}}
            """;
        return Encoding.UTF8.GetBytes(json);
    }

    // Header: magic "HLFJ" (little-endian 0x4A464C48), format version, record type 1 (commit), two reserved bytes, payload
    // length; then SHA-256 of the header, the payload, and SHA-256 of header plus payload.
    private static byte[] Frame(byte version, byte[] payload)
    {
        var header = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 0x4A464C48);
        header[4] = version;
        header[5] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), payload.Length);
        var checksum = SHA256.HashData(header.Concat(payload).ToArray());
        return header.Concat(SHA256.HashData(header)).Concat(payload).Concat(checksum).ToArray();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"harborline-forms-binding-{Guid.NewGuid():N}");

        public Fixture()
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
