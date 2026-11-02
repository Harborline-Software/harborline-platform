using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Foundation.Authorization;
using Xunit;

namespace Harborline.Foundation.Authorization.Tests;

/// <summary>
/// Revision 6 (plan W6/W8, decision D6): one byte grammar for a stored authority snapshot, shared by every Platform writer,
/// so the acceptance snapshot a form submit gate commits can be read back by a Records completion without re-deriving it.
/// Oracles: the T-616 003c Records encoder, copied here verbatim as it stood before it moved (so the move is proven
/// byte-identical), literal fact values, and literal malformed byte strings. Nothing is read back from the encoder to
/// build an expectation.
/// </summary>
public sealed class AuthoritySnapshotEncodingTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-11T09:00:00Z");

    private const string Minimal =
        """{"tenant":"t","principal":"p","version":2,"steps":[{"ordinal":1,"stage":"act","facts":["act:forms:submit-gate"]},{"ordinal":2,"stage":"effective-roles","facts":[]},{"ordinal":3,"stage":"standings","facts":[]},{"ordinal":4,"stage":"verdict","facts":["verdict:allowed"]}]}""";

    [Fact]
    public void The_encoding_is_byte_identical_to_the_records_003c_grammar()
    {
        var allowed = Evidence(allowed: true);
        var denied = Evidence(allowed: false);

        Assert.Equal(Legacy003c(allowed), AuthoritySnapshotEncoding.Encode(allowed));
        Assert.Equal(Legacy003c(denied), AuthoritySnapshotEncoding.Encode(denied));
    }

    [Fact]
    public void A_snapshot_decodes_back_to_the_gates_own_facts_without_any_verdict_calculation()
    {
        var snapshot = AuthoritySnapshotEncoding.TryDecode(AuthoritySnapshotEncoding.Encode(Evidence(allowed: true)));

        Assert.NotNull(snapshot);
        Assert.Equal("tenant-a", snapshot.Tenant);
        Assert.Equal("alice", snapshot.Principal);
        Assert.Equal(2, snapshot.Version);
        Assert.Equal(["act", "effective-roles", "standings", "verdict"], snapshot.Steps.Select(step => step.Stage));
        Assert.Equal([1, 2, 3, 4], snapshot.Steps.Select(step => step.Ordinal));
        Assert.Equal("forms:submit-gate", AuthoritySnapshotEncoding.Fact(snapshot, "act", "act:"));
        Assert.Equal("form/inspection@1.0.0", AuthoritySnapshotEncoding.Fact(snapshot, "act", "target:"));
        Assert.Equal("alice", AuthoritySnapshotEncoding.Fact(snapshot, "act", "principal:"));
        Assert.Equal("tenant-a", AuthoritySnapshotEncoding.Fact(snapshot, "act", "tenant:"));
        Assert.Equal("2026-10-11T09:00:00.0000000+00:00", AuthoritySnapshotEncoding.Fact(snapshot, "act", "at:"));
        Assert.Equal("allowed", AuthoritySnapshotEncoding.Fact(snapshot, "verdict", "verdict:"));
        Assert.Equal("grant:fixture@1", AuthoritySnapshotEncoding.Fact(snapshot, "effective-roles", "deciding:"));
    }

    [Fact]
    public void A_denied_decision_reads_back_as_denied()
    {
        var snapshot = AuthoritySnapshotEncoding.TryDecode(AuthoritySnapshotEncoding.Encode(Evidence(allowed: false)));

        Assert.NotNull(snapshot);
        Assert.Equal("denied", AuthoritySnapshotEncoding.Fact(snapshot, "verdict", "verdict:"));
        Assert.Equal("NoGrant", AuthoritySnapshotEncoding.Fact(snapshot, "verdict", "refusal:"));
    }

    [Fact]
    public void The_minimal_literal_snapshot_decodes()
    {
        var snapshot = AuthoritySnapshotEncoding.TryDecode(Encoding.UTF8.GetBytes(Minimal));

        Assert.NotNull(snapshot);
        Assert.Equal("allowed", AuthoritySnapshotEncoding.Fact(snapshot, "verdict", "verdict:"));
        Assert.Null(AuthoritySnapshotEncoding.Fact(snapshot, "verdict", "refusal:"));
        Assert.Null(AuthoritySnapshotEncoding.Fact(snapshot, "act", "verdict:"));
    }

    public static TheoryData<string> Malformed => new()
    {
        "",
        "not json",
        "[]",
        "null",
        """{"tenant":"t","principal":"p","version":2}""",
        Minimal.Replace("\"version\":2,", "\"version\":2,\"extra\":1,", StringComparison.Ordinal),
        Minimal.Replace("\"tenant\":\"t\",\"principal\":\"p\"", "\"principal\":\"p\",\"tenant\":\"t\"", StringComparison.Ordinal),
        Minimal.Replace("\"tenant\":\"t\"", "\"tenant\":\" \"", StringComparison.Ordinal),
        Minimal.Replace("\"principal\":\"p\"", "\"principal\":null", StringComparison.Ordinal),
        Minimal.Replace("\"version\":2", "\"version\":\"2\"", StringComparison.Ordinal),
        Minimal.Replace("\"version\":2", "\"version\":2.5", StringComparison.Ordinal),
        Minimal.Replace(",{\"ordinal\":4,\"stage\":\"verdict\",\"facts\":[\"verdict:allowed\"]}", "", StringComparison.Ordinal),
        Minimal.Replace("\"stage\":\"standings\"", "\"stage\":\"verdict\"", StringComparison.Ordinal),
        Minimal.Replace("\"ordinal\":2", "\"ordinal\":5", StringComparison.Ordinal),
        Minimal.Replace("[\"verdict:allowed\"]", "[1]", StringComparison.Ordinal),
        Minimal.Replace("\"facts\":[]", "\"facts\":[],\"note\":\"x\"", StringComparison.Ordinal),
        Minimal + "{}",
        Minimal.Replace("\"tenant\":\"t\",", "\"tenant\":\"t\",\"tenant\":\"t\",", StringComparison.Ordinal),
        Minimal.Replace("\"version\":2,", "\"version\":2, /* c */", StringComparison.Ordinal),
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Anything_but_exactly_the_grammar_is_not_a_snapshot(string bytes) =>
        Assert.Null(AuthoritySnapshotEncoding.TryDecode(Encoding.UTF8.GetBytes(bytes)));

    // Characterize the accepted native argument boundary; no missing argument is a snapshot or a fact query.
    [Fact]
    public void Encoding_requires_an_evidence_argument()
    {
        Assert.Throws<ArgumentNullException>(() => AuthoritySnapshotEncoding.Encode(null!));
    }

    [Fact]
    public void Fact_lookup_requires_a_snapshot_argument()
    {
        Assert.Throws<ArgumentNullException>(() => AuthoritySnapshotEncoding.Fact(null!, "verdict", "verdict:"));
    }

    [Theory]
    [InlineData("", "verdict:")]
    [InlineData("verdict", "")]
    public void Fact_lookup_requires_nonempty_stage_and_prefix(string stage, string prefix)
    {
        var snapshot = AuthoritySnapshotEncoding.TryDecode(Encoding.UTF8.GetBytes(Minimal));
        Assert.NotNull(snapshot);
        Assert.Equal("allowed", AuthoritySnapshotEncoding.Fact(snapshot, "verdict", "verdict:"));

        Assert.Throws<ArgumentException>(() => AuthoritySnapshotEncoding.Fact(snapshot, stage, prefix));
    }

    private static AuthorizationDecisionEvidence Evidence(bool allowed) => new(
        new AccessRequest("forms:submit-gate", "alice", "tenant-a",
            new AccessRecord("tenant-a", "form", "inspection@1.0.0", new Dictionary<string, JsonNode?>()), At),
        allowed, allowed ? "None" : "NoGrant", "grant:fixture@1", [], []);

    // RecordWriteOperation.AuthoritySnapshotOf as committed at 003c (e89e9cd2), before it moved to this package.
    private static byte[] Legacy003c(AuthorizationDecisionEvidence evidence)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("tenant", evidence.Tenant);
            writer.WriteString("principal", evidence.Principal);
            writer.WriteNumber("version", evidence.Version);
            writer.WriteStartArray("steps");
            foreach (var step in evidence.Project())
            {
                writer.WriteStartObject();
                writer.WriteNumber("ordinal", step.Ordinal);
                writer.WriteString("stage", step.Stage);
                writer.WriteStartArray("facts");
                foreach (var fact in step.Facts) writer.WriteStringValue(fact);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}
