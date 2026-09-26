using System.Reflection;
using System.Text;
using System.Text.Json;
using Harborline.Foundation.Taxonomy;
using Xunit;

namespace Harborline.Foundation.Taxonomy.Tests;

public sealed class TaxonomyDefinitionTests
{
    [Fact(DisplayName = "taxonomy-ck-1 through taxonomy-ck-15, taxonomy-ck-21 and taxonomy-ck-22: members round-trip through canonical JSON")]
    public void Canonical_json_round_trips_byte_identically()
    {
        var first = TaxonomyDefinitionJson.SerializeCanonical(Definition());
        var parsed = TaxonomyDefinitionJson.Deserialize(first);
        Assert.Equal(first, TaxonomyDefinitionJson.SerializeCanonical(parsed));
        Assert.Equal((byte)'\n', first[^1]);
        Assert.Equal("acme.health.icd", JsonDocument.Parse(first).RootElement.GetProperty("definition_id").GetString());
        Assert.Equal(new TaxonomyNodeId(Id, "root"), new TaxonomyNodeId(TaxonomyDefinitionId.Parse(parsed.DefinitionId.ToString()), "root"));
        Assert.Empty(TaxonomyDefinitionAdmission.Validate(parsed, TaxonomyAdmissionPhase.Install));
        Assert.Equal(7, TaxonomyDefinitionPackExporter.Export(parsed).ContentKind);
    }

    [Fact(DisplayName = "taxonomy-ck-2: only three non-empty id segments parse")]
    public void Definition_id_parse_refuses_invalid_shape() => Assert.Throws<FormatException>(() => TaxonomyDefinitionId.Parse("acme..icd"));

    [Fact(DisplayName = "taxonomy-auth-2 and taxonomy-ck-5: Authoritative governance and owner rules refuse, then corrected counterparts admit")]
    public void Governance_and_owner_rules_admit_corrected_counterparts()
    {
        RefusesThenAdmits(Definition() with { Governance = TaxonomyGovernanceRegime.Authoritative, Owner = TaxonomyDefinitionAdmission.HarborlineActorId }, "definition.governance_not_authorable", TaxonomyAdmissionPhase.Author);
        RefusesThenAdmits(Definition() with { Governance = TaxonomyGovernanceRegime.Authoritative, Owner = "other" }, "definition.authoritative_owner_invalid", TaxonomyAdmissionPhase.Install);
    }

    [Fact(DisplayName = "taxonomy-auth-7 and taxonomy-auth-20: tombstone and successor refusals return corrected counterparts")]
    public void Tombstone_and_successor_rules_admit_corrected_counterparts()
    {
        RefusesThenAdmits(Definition([Node("a", status: TaxonomyNodeStatus.Tombstoned, reason: " ")]), "definition.deprecation_reason_required");
        RefusesThenAdmits(Definition([Node("a", successor: "missing")]), "definition.successor_unknown");
        RefusesThenAdmits(Definition([Node("a", successor: "b"), Node("b", status: TaxonomyNodeStatus.Tombstoned)]), "definition.successor_tombstoned");
    }

    [Fact(DisplayName = "taxonomy-auth-17 and taxonomy-ck-15: monotonic tombstones and display history are checked against previous")]
    public void Previous_definition_rules_admit_corrected_counterparts()
    {
        var prior = Definition([Node("a", status: TaxonomyNodeStatus.Tombstoned)]);
        RefusesThenAdmits(Definition([Node("a")]), "definition.tombstone_reactivated", previous: prior);
        var oldHistory = new DisplayHistoryEntry("old", "old description", DateTimeOffset.UnixEpoch);
        var historyPrior = Definition([Node("a", history: [oldHistory])]);
        RefusesThenAdmits(Definition([Node("a", history: [])]), "definition.display_history_not_append_only", previous: historyPrior);
    }

    [Fact(DisplayName = "taxonomy-auth-18, taxonomy-eng-8 and taxonomy-eng-18: parent cycles and depth-bound excess refuse, then corrected counterparts admit")]
    public void Parent_graph_rules_admit_corrected_counterparts()
    {
        var cycle = Definition([Node("a", parent: "b"), Node("b", parent: "a")]);
        var refusals = TaxonomyDefinitionAdmission.Validate(cycle, TaxonomyAdmissionPhase.Author);
        Assert.Contains(refusals, refusal => refusal.Code == "definition.parent_cycle" && refusal.Pointer.Contains("a,b", StringComparison.Ordinal));
        Assert.Empty(TaxonomyDefinitionAdmission.Validate(Definition(), TaxonomyAdmissionPhase.Author));
        var deepNodes = Enumerable.Range(0, 66).Select(index => Node($"n{index}", parent: index == 0 ? null : $"n{index - 1}")).ToArray();
        RefusesThenAdmits(Definition(deepNodes), "definition.traversal_depth_exceeded");
    }

    [Fact(DisplayName = "taxonomy-auth-21 and taxonomy-ck-22: publish envelope and non-empty-node requirements refuse, then corrected counterparts admit")]
    public void Publication_rules_admit_corrected_counterparts()
    {
        RefusesThenAdmits(Definition() with { Nodes = [] }, "definition.no_terms", TaxonomyAdmissionPhase.Publish);
        RefusesThenAdmits(Definition() with { Envelope = null }, "definition.envelope_required", TaxonomyAdmissionPhase.Publish);
        RefusesThenAdmits(Definition() with { Envelope = Envelope(identity: "other") }, "definition.envelope_mismatch", TaxonomyAdmissionPhase.Publish);
        RefusesThenAdmits(Definition() with { Version = "1.0" }, "definition.version_invalid");
        RefusesThenAdmits(Definition() with { SchemaVersion = 2 }, "definition.schema_version_unsupported");
    }

    [Fact(DisplayName = "taxonomy-auth-11: catalogue coordinates disagreeing with body refuse, then matching coordinates admit")]
    public void Catalogue_coordinates_admit_corrected_counterpart()
    {
        var body = Encoding.UTF8.GetString(TaxonomyDefinitionJson.SerializeCanonical(Definition()));
        var refused = TaxonomyDefinitionAdmission.AdmitJson(body, TaxonomyAdmissionPhase.Install, new("other", Id, "1.0.0"));
        var admitted = TaxonomyDefinitionAdmission.AdmitJson(body, TaxonomyAdmissionPhase.Install, new("tenant-a", Id, "1.0.0"));
        Assert.Contains(refused, refusal => refusal.Code == "definition.catalogue_mismatch"); Assert.Empty(admitted);
    }

    [Fact(DisplayName = "taxonomy-auth-18 and taxonomy-auth-20: duplicate and unknown parent codes refuse, then corrected counterparts admit")]
    public void Code_and_parent_rules_admit_corrected_counterparts()
    {
        RefusesThenAdmits(Definition([Node("a"), Node("a")]), "definition.node_code_duplicate");
        RefusesThenAdmits(Definition([Node("a", parent: "missing")]), "definition.parent_unknown");
    }

    [Fact(DisplayName = "taxonomy-auth-15 and taxonomy-auth-19: no delete operation and one nullable ParentCode make those shapes inexpressible")]
    public void Delete_and_second_parent_are_structurally_impossible()
    {
        Assert.DoesNotContain(typeof(TaxonomyDefinition).GetMethods(BindingFlags.Instance | BindingFlags.Public), method => method.Name.Contains("delete", StringComparison.OrdinalIgnoreCase));
        var parent = typeof(TaxonomyNode).GetProperty(nameof(TaxonomyNode.ParentCode));
        Assert.NotNull(parent); Assert.Equal(typeof(string), Nullable.GetUnderlyingType(parent!.PropertyType) ?? parent.PropertyType);
        Assert.False(typeof(System.Collections.IEnumerable).IsAssignableFrom(parent.PropertyType) && parent.PropertyType != typeof(string));
    }

    [Fact(DisplayName = "taxonomy-auth-25: independent structural refusals are accumulated in one list")]
    public void Independent_refusals_are_accumulated()
    {
        var refusals = TaxonomyDefinitionAdmission.Validate(Definition([Node("a", successor: "missing"), Node("a")]), TaxonomyAdmissionPhase.Author);
        Assert.Contains(refusals, refusal => refusal.Code == "definition.node_code_duplicate");
        Assert.Contains(refusals, refusal => refusal.Code == "definition.successor_unknown");
    }

    private static readonly TaxonomyDefinitionId Id = new("acme", "health", "icd");
    private static TaxonomyDefinition Definition(IReadOnlyList<TaxonomyNode>? nodes = null) => new("tenant-a", Id, "1.0.0", TaxonomyGovernanceRegime.Civilian, "author", new("source", "0.9.0", "author", DateTimeOffset.UnixEpoch, "derivation"), nodes ?? [Node("root")], Envelope: Envelope());
    private static TaxonomyDefinitionEnvelope Envelope(string? identity = null) => new(identity ?? Id.ToString(), "1.0.0", "tenant-a", TaxonomyCascadeLayer.Tenant, JsonDocument.Parse("{\"source\":\"tenant\"}").RootElement.Clone(), []);
    private static TaxonomyNode Node(string code, string? parent = null, TaxonomyNodeStatus status = TaxonomyNodeStatus.Active, string? successor = null, string? reason = "retired", IReadOnlyList<DisplayHistoryEntry>? history = null) => new(code, $"Display {code}", $"Description {code}", parent, status, DateTimeOffset.UnixEpoch, status == TaxonomyNodeStatus.Tombstoned ? DateTimeOffset.UnixEpoch : null, successor, reason, history ?? [new($"Display {code}", $"Description {code}", DateTimeOffset.UnixEpoch)]);
    private static void RefusesThenAdmits(TaxonomyDefinition refused, string code, TaxonomyAdmissionPhase phase = TaxonomyAdmissionPhase.Author, TaxonomyDefinition? previous = null)
    {
        Assert.Contains(TaxonomyDefinitionAdmission.Validate(refused, phase, previous), refusal => refusal.Code == code);
        Assert.Empty(TaxonomyDefinitionAdmission.Validate(Definition(), phase));
    }
}