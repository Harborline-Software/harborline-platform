using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Foundation.Assistance;
using Xunit;

namespace Harborline.Foundation.Assistance.Tests;

public sealed class AssistanceDelegationTests
{
    private static JsonNode Fixture() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "delegation-cases.json")))!;

    [Fact(DisplayName = "pilot-ck-15 pilot-ck-18: canonical bytes and digest; oracle: RFC 8785 fixture bytes and independent shasum literals")]
    public void Proposal_canonical_bytes_and_digest_match_the_shared_oracle()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "delegation-cases.json")));
        foreach (var item in fixture.RootElement.GetProperty("envelopes").EnumerateArray())
        {
            var proposal = AssistanceDelegationJson.Deserialize<AssistanceProposalEnvelope>(item.GetProperty("value").GetRawText());
            var canonical = AssistanceDelegationJson.SerializeCanonical(proposal);
            Assert.Equal(Encoding.UTF8.GetBytes(item.GetProperty("canonical").GetString()!), canonical);
            Assert.Equal(item.GetProperty("digest").GetString(), AssistanceProposalDigest.Compute(proposal));
            Assert.Equal(canonical, AssistanceDelegationJson.SerializeCanonical(AssistanceDelegationJson.Deserialize<AssistanceProposalEnvelope>(Encoding.UTF8.GetString(canonical))));
        }
    }

    [Fact(DisplayName = "pilot-ck-15: unknown, duplicate, missing and unsupported envelope members refuse; oracle: DES-0026 pilot.proposal/3")]
    public void Proposal_deserialization_is_strict()
    {
        var envelope = Fixture()["envelopes"]![0]!["value"]!;
        foreach (var member in new[] { "schema", "surface", "command", "args" })
        {
            var invalid = envelope.DeepClone().AsObject();
            invalid.Remove(member);
            Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceProposalEnvelope>(invalid.ToJsonString()));
        }
        foreach (var replacement in new[] { "pilot.proposal/2", "", null })
        {
            var invalid = envelope.DeepClone();
            invalid["schema"] = replacement;
            Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceProposalEnvelope>(invalid.ToJsonString()));
        }
        var unknown = envelope.DeepClone();
        unknown["content"] = "secret";
        Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceProposalEnvelope>(unknown.ToJsonString()));
        Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceProposalEnvelope>("null"));
        Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceProposalEnvelope>("{\"schema\":\"pilot.proposal/3\",\"schema\":\"pilot.proposal/3\",\"surface\":\"s\",\"command\":\"c\",\"args\":{}}"));
    }

    [Fact(DisplayName = "pilot-ck-17 pilot-ck-18 pilot-ck-19 pilot-ck-21: delegation fixture round trips; oracle: DES-0026, ADR-0090/0091 and L609/L1645")]
    public void Delegations_round_trip_and_actor_has_exactly_five_members()
    {
        foreach (var record in Fixture()["delegations"]!.AsArray()) RoundTrip<AssistanceDelegationRecord>(record!);
        Assert.Equal(new[] { "binding_key", "binding_version", "model_version", "proposal_digest", "provider_id" },
            typeof(AssistanceDelegationActor).GetProperties().Select(property => JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name)).Order(StringComparer.Ordinal));
        var hand = AssistanceDelegationJson.Deserialize<AssistanceDelegationRecord>(Fixture()["delegations"]![5]!.ToJsonString());
        Assert.Null(hand.Actor);
        Assert.Equal("approve", hand.Disposition.Kind);
    }

    [Fact(DisplayName = "pilot-ck-17 pilot-ck-18: actor refuses proposal content or an invalid digest; oracle: ADR-0091 five-member actor and L1286")]
    public void Actor_cannot_carry_content()
    {
        foreach (var member in new[] { "content", "context", "args", "proposal" })
        {
            var record = Fixture()["delegations"]![0]!.DeepClone();
            record["actor"]![member] = "secret";
            Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceDelegationRecord>(record.ToJsonString()));
        }
        foreach (var digest in new[] { "secret", "sha256:" + new string('A', 64), "sha256:" + new string('0', 63), "sha256:" + new string('0', 64) + "\n" })
        {
            var record = Fixture()["delegations"]![0]!.DeepClone();
            record["actor"]!["proposal_digest"] = digest;
            Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceDelegationRecord>(record.ToJsonString()));
        }
    }

    [Theory(DisplayName = "pilot-ck-19 pilot-ck-21: only five disposition forms admit; oracle: DES-0026 and L1645")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"auto-apply\"}")]
    [InlineData("{\"kind\":\"card\"}")]
    [InlineData("{\"kind\":\"hide\"}")]
    [InlineData("{\"kind\":\"standing\"}")]
    [InlineData("{\"kind\":\"approve\",\"approver\":\"alice\"}")]
    [InlineData("{\"kind\":\"dismiss\",\"content\":\"secret\"}")]
    public void Invalid_dispositions_refuse(string json) => Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceDisposition>(json));

    [Fact(DisplayName = "pilot-ck-21: schedule and revocation survive; oracle: shared fixture, ADR-0107 and RFC 5545 section 3.3.10")]
    public void Standing_approvals_round_trip()
    {
        foreach (var approval in Fixture()["standing_approvals"]!.AsArray()) RoundTrip<AssistanceStandingApproval>(approval!);
    }

    [Fact(DisplayName = "pilot-ck-21: approver, nonempty command scope and UTC expiry are required; oracle: ADR-0107")]
    public void Invalid_standing_approvals_refuse()
    {
        foreach (var member in new[] { "id", "approver", "binding_key", "scope", "schedule", "expires_at", "revoked_at" })
        {
            var approval = Fixture()["standing_approvals"]![0]!.DeepClone().AsObject();
            approval.Remove(member);
            Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceStandingApproval>(approval.ToJsonString()));
        }
        foreach (var expiry in new[] { "2026-02-30T12:00:00Z", "2026-12-31T23:59:59+01:00", "yesterday", null })
        {
            var approval = Fixture()["standing_approvals"]![0]!.DeepClone();
            approval["expires_at"] = expiry;
            Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceStandingApproval>(approval.ToJsonString()));
        }
        var empty = Fixture()["standing_approvals"]![0]!.DeepClone();
        empty["scope"]!["commands"] = new JsonArray();
        Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceStandingApproval>(empty.ToJsonString()));
        empty = Fixture()["standing_approvals"]![0]!.DeepClone();
        empty["approver"] = "";
        Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceStandingApproval>(empty.ToJsonString()));
    }

    [Fact(DisplayName = "pilot-ck-15 pilot-ck-18: UTF-16 sorting and number spelling; oracle: RFC 8785 sections 3.2.2 and 3.2.3")]
    public void Canonical_order_and_number_boundaries()
    {
        var proposal = AssistanceDelegationJson.Deserialize<AssistanceProposalEnvelope>("{\"schema\":\"pilot.proposal/3\",\"surface\":\"forms.builder\",\"command\":\"draft.edit\",\"args\":{\"דּ\":1,\"😀\":2,\"\\r\":3,\"€\":4}}");
        Assert.Equal("{\"args\":{\"\\r\":3,\"€\":4,\"😀\":2,\"דּ\":1},\"command\":\"draft.edit\",\"schema\":\"pilot.proposal/3\",\"surface\":\"forms.builder\"}", Encoding.UTF8.GetString(AssistanceDelegationJson.SerializeCanonical(proposal)));
        using var numbers = JsonDocument.Parse("[0,-0,1e-7,1e-6,1e20,1e21,-1.25]");
        Assert.Equal("{\"args\":[0,0,1e-7,0.000001,100000000000000000000,1e+21,-1.25],\"command\":\"draft.edit\",\"schema\":\"pilot.proposal/3\",\"surface\":\"forms.builder\"}",
            Encoding.UTF8.GetString(AssistanceDelegationJson.SerializeCanonical(proposal with { Args = numbers.RootElement.Clone() })));
    }

    [Fact(DisplayName = "pilot-ck-16: no .NET receipt wire type exists; oracle: runtime-identity-only receipt contract")]
    public void No_dispatch_receipt_wire_type() => Assert.DoesNotContain(typeof(AssistanceProposalEnvelope).Assembly.GetExportedTypes(), type => type.Name.Contains("DispatchReceipt", StringComparison.Ordinal));

    [Fact(DisplayName = "pilot-ck-15 pilot-ck-18: control escaping and JSON-only arguments; oracle: RFC 8785 section 3.2.2.2")]
    public void Canonical_escaping_and_invalid_json_values()
    {
        var proposal = AssistanceDelegationJson.Deserialize<AssistanceProposalEnvelope>(Fixture()["envelopes"]![0]!["value"]!.ToJsonString());
        using var escaped = JsonDocument.Parse("\"\\u0001\\b\\t\\n\\f\\r\\\"\\\\\\u2028\\ud83d\\ude00\"");
        Assert.Equal("{\"args\":\"\\u0001\\b\\t\\n\\f\\r\\\"\\\\\u2028😀\",\"command\":\"draft.edit\",\"schema\":\"pilot.proposal/3\",\"surface\":\"forms.builder\"}",
            Encoding.UTF8.GetString(AssistanceDelegationJson.SerializeCanonical(proposal with { Args = escaped.RootElement.Clone() })));
        foreach (var args in new[] { "{\"duplicate\":1,\"duplicate\":2}", "1e999" })
        {
            using var invalid = JsonDocument.Parse(args);
            Assert.Throws<JsonException>(() => AssistanceDelegationJson.SerializeCanonical(proposal with { Args = invalid.RootElement.Clone() }));
        }
        Assert.Throws<JsonException>(() => AssistanceDelegationJson.SerializeCanonical(proposal with { Command = "\ud800" }));
    }

    [Fact(DisplayName = "pilot-ck-21: exact UTC spelling; oracle: shared UTC wire contract and ADR-0107")]
    public void Standing_approval_utc_spelling_is_preserved_and_strict()
    {
        foreach (var instant in new[] { "2026-12-31T23:59:59+00:00", "2026-12-31T23:59:59.1234567Z" })
        {
            var approval = Fixture()["standing_approvals"]![0]!.DeepClone();
            approval["expires_at"] = instant;
            RoundTrip<AssistanceStandingApproval>(approval);
        }
        foreach (var instant in new[] { "2026-12-31T23:59:59.Z", "2026-12-31T23:59:59-00:00", "2026-12-31T23:59:59Z\n" })
        {
            var approval = Fixture()["standing_approvals"]![0]!.DeepClone();
            approval["expires_at"] = instant;
            Assert.Throws<JsonException>(() => AssistanceDelegationJson.Deserialize<AssistanceStandingApproval>(approval.ToJsonString()));
        }
    }

    private static void RoundTrip<T>(JsonNode source)
    {
        var parsed = AssistanceDelegationJson.Deserialize<T>(source.ToJsonString());
        var bytes = AssistanceDelegationJson.SerializeCanonical(parsed);
        Assert.True(JsonNode.DeepEquals(source, JsonNode.Parse(bytes)));
        Assert.Equal(bytes, AssistanceDelegationJson.SerializeCanonical(AssistanceDelegationJson.Deserialize<T>(Encoding.UTF8.GetString(bytes))));
    }
}
