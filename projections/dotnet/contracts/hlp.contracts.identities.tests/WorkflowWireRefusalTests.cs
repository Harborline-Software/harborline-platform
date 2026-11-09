using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Contracts.Workflow;

using Xunit;

namespace Harborline.Contracts.Tests;

/// <summary>
/// T-892: every refusal the workflow wire reader can make, one at a time, against the shared fixture's valid definition.
/// Each case breaks exactly one property and asserts the exact code, wire type and detail, so a dropped check, a
/// weakened kind test or a mislabelled refusal is seen. The oracle is the reader's own published shape:
/// missing-required-property (detail = the property), invalid-object / invalid-string / invalid-array /
/// invalid-integer / invalid-boolean, unknown-closed-value, unknown-role-vocabulary and exclusive-union-mismatch.
/// </summary>
public sealed class WorkflowWireRefusalTests
{
    private static readonly JsonObject Fixture = JsonNode.Parse(File.ReadAllText(FindFixture()))!.AsObject();

    public static TheoryData<string, string, string, string?> Cases()
    {
        var data = new TheoryData<string, string, string, string?>();
        void Add(string id, string code, string type, string? detail = null) => data.Add(id, code, type, detail);

        Add("definition-not-object", "invalid-object", "WorkflowDefinition");
        foreach (var name in new[] { "key", "version", "tenant", "initialState", "createdAt", "updatedAt" })
        {
            Add($"definition-missing-{name}", "missing-required-property", "WorkflowDefinition", name);
            Add($"definition-number-{name}", "invalid-string", $"WorkflowDefinition.{name}");
        }
        Add("definition-missing-status", "missing-required-property", "WorkflowDefinition", "status");
        Add("definition-bad-status", "unknown-closed-value", "WorkflowDefinitionStatus");
        Add("definition-missing-mutability", "missing-required-property", "WorkflowDefinition", "mutability");
        Add("definition-bad-mutability", "unknown-closed-value", "WorkflowMutability");
        foreach (var name in new[] { "states", "transitions", "triggers", "actions", "guards" })
        {
            Add($"definition-missing-{name}", "missing-required-property", "WorkflowDefinition", name);
            Add($"definition-object-{name}", "invalid-array", $"WorkflowDefinition.{name}");
        }

        Add("state-not-object", "invalid-object", "WorkflowState");
        Add("state-missing-id", "missing-required-property", "WorkflowState", "id");
        Add("state-number-id", "invalid-string", "WorkflowState.id");
        Add("state-missing-label", "missing-required-property", "WorkflowState", "label");
        Add("state-missing-kind", "missing-required-property", "WorkflowState", "kind");
        Add("state-bad-kind", "unknown-closed-value", "WorkflowStateKind");
        Add("state-bad-view-hint", "unknown-closed-value", "WorkflowViewHint");

        Add("transition-not-object", "invalid-object", "WorkflowTransition");
        foreach (var name in new[] { "id", "from", "on", "to" })
        {
            Add($"transition-missing-{name}", "missing-required-property", "WorkflowTransition", name);
            Add($"transition-number-{name}", "invalid-string", $"WorkflowTransition.{name}");
        }
        Add("transition-number-guard", "invalid-string", "WorkflowTransition.guard");
        Add("transition-object-roles", "invalid-array", "WorkflowTransition.requiredRoles");
        Add("transition-object-standings", "invalid-array", "WorkflowTransition.requiredStandings");
        Add("role-not-object", "invalid-object", "RoleReference");
        Add("role-missing-vocabulary", "missing-required-property", "RoleReference", "vocabulary");
        Add("role-number-vocabulary", "invalid-string", "RoleReference.vocabulary");
        Add("role-unknown-vocabulary", "unknown-role-vocabulary", "RoleReference");
        Add("role-missing-name", "missing-required-property", "RoleReference", "name");
        Add("role-number-name", "invalid-string", "RoleReference.name");
        Add("standing-not-object", "invalid-object", "RecordStandingReference");
        Add("standing-missing-name", "missing-required-property", "RecordStandingReference", "name");
        Add("standing-number-name", "invalid-string", "RecordStandingReference.name");

        Add("trigger-not-object", "invalid-object", "WorkflowTriggerBinding");
        Add("trigger-missing-id", "missing-required-property", "WorkflowTriggerBinding", "id");
        Add("trigger-number-id", "invalid-string", "WorkflowTriggerBinding.id");
        Add("trigger-missing-kind", "missing-required-property", "WorkflowTriggerBinding", "kind");
        Add("trigger-bad-kind", "unknown-closed-value", "WorkflowTriggerKind");
        foreach (var name in new[] { "eventType", "rrule", "task", "dependency" })
            Add($"trigger-number-{name}", "invalid-string", $"WorkflowTriggerBinding.{name}");

        Add("action-not-object", "invalid-object", "WorkflowActionBinding");
        Add("action-missing-id", "missing-required-property", "WorkflowActionBinding", "id");
        Add("action-number-id", "invalid-string", "WorkflowActionBinding.id");
        Add("action-missing-on", "missing-required-property", "WorkflowActionBinding", "on");
        Add("action-on-not-object", "invalid-object", "WorkflowActionBinding.on");
        Add("action-on-number-target", "invalid-string", "WorkflowActionBinding.on");
        Add("action-on-number-state", "invalid-string", "WorkflowActionBinding.on");
        Add("action-missing-kind", "missing-required-property", "WorkflowActionBinding", "kind");
        Add("action-bad-kind", "unknown-closed-value", "WorkflowActionKind");
        Add("action-missing-capability", "missing-required-property", "WorkflowActionBinding", "capabilityRef");
        Add("action-number-capability", "invalid-string", "WorkflowActionBinding.capabilityRef");
        Add("action-missing-classification", "missing-required-property", "WorkflowActionBinding", "classification");
        Add("action-bad-classification", "unknown-closed-value", "ActionClassification");
        Add("action-number-condition", "invalid-string", "WorkflowActionBinding.condition");
        Add("action-object-roles", "invalid-array", "WorkflowActionBinding.requiredRoles");
        Add("action-object-standings", "invalid-array", "WorkflowActionBinding.requiredStandings");
        Add("action-string-params", "invalid-object", "WorkflowEffectParams");

        Add("envelope-not-object", "invalid-object", "WorkflowEnvelope");
        foreach (var name in new[] { "identity", "version", "tenant", "retentionClass" })
        {
            Add($"envelope-missing-{name}", "missing-required-property", "WorkflowEnvelope", name);
            Add($"envelope-number-{name}", "invalid-string", $"WorkflowEnvelope.{name}");
        }
        Add("envelope-missing-cascade", "missing-required-property", "WorkflowEnvelope", "cascadeLayer");
        Add("envelope-bad-cascade", "unknown-closed-value", "WorkflowCascadeLayer");
        Add("envelope-missing-provenance", "missing-required-property", "WorkflowEnvelope", "provenance");
        Add("envelope-missing-legal-hold", "missing-required-property", "WorkflowEnvelope", "legalHold");
        Add("envelope-string-legal-hold", "invalid-boolean", "WorkflowEnvelope.legalHold");
        Add("envelope-missing-requires", "missing-required-property", "WorkflowEnvelope", "requires");
        Add("envelope-object-requires", "invalid-array", "WorkflowEnvelope.requires");
        Add("requirement-not-object", "invalid-object", "WorkflowEnvelopeRequirement");
        Add("requirement-missing-capability", "missing-required-property", "WorkflowEnvelopeRequirement", "capability");
        Add("requirement-number-capability", "invalid-string", "WorkflowEnvelopeRequirement.capability");
        Add("contract-not-object", "invalid-object", "WorkflowContractVersion");
        Add("contract-missing-major", "missing-required-property", "WorkflowContractVersion", "major");
        Add("contract-missing-minor", "missing-required-property", "WorkflowContractVersion", "minor");
        Add("contract-string-major", "invalid-integer", "WorkflowContractVersion.major");
        Add("contract-fraction-minor", "invalid-integer", "WorkflowContractVersion.minor");

        // workflows-ck-12: the subject is exactly one of record, set or case (ADR 0099); record and set name a Record Type
        // and a case names none (ADR 0125).
        Add("subject-not-object", "invalid-object", "WorkflowSubject");
        Add("subject-null", "invalid-object", "WorkflowSubject");
        Add("subject-no-kind", "exclusive-union-mismatch", "WorkflowSubject");
        Add("subject-two-kinds", "exclusive-union-mismatch", "WorkflowSubject");
        Add("subject-record-not-object", "invalid-object", "WorkflowSubject.record");
        Add("subject-record-missing-type", "missing-required-property", "WorkflowSubject.record", "recordType");
        Add("subject-record-number-type", "invalid-string", "WorkflowSubject.record.recordType");
        Add("subject-set-not-object", "invalid-object", "WorkflowSubject.set");
        Add("subject-set-missing-type", "missing-required-property", "WorkflowSubject.set", "recordType");
        Add("subject-set-number-type", "invalid-string", "WorkflowSubject.set.recordType");
        Add("subject-case-not-object", "invalid-object", "WorkflowSubject.case");
        Add("lineage-not-object", "invalid-object", "WorkflowDefinitionLineage");
        Add("lineage-missing-parent", "missing-required-property", "WorkflowDefinitionLineage", "parentDefinitionKey");
        Add("lineage-number-parent", "invalid-string", "WorkflowDefinitionLineage.parentDefinitionKey");
        Add("lineage-missing-version", "missing-required-property", "WorkflowDefinitionLineage", "parentVersion");
        Add("lineage-number-version", "invalid-string", "WorkflowDefinitionLineage.parentVersion");
        return data;
    }

    [Theory(DisplayName = "T-892: the workflow wire reader refuses each broken property by its exact code, type and detail")]
    [MemberData(nameof(Cases))]
    public void EachBrokenPropertyIsRefusedExactly(string id, string code, string type, string? detail)
    {
        // The unbroken document is admitted, so each refusal below is the break's alone.
        WorkflowWire.Deserialize("WorkflowDefinition", Valid().ToJsonString());

        var error = Assert.Throws<WorkflowWireException>(() => WorkflowWire.Deserialize("WorkflowDefinition", Break(id)));

        Assert.Equal((code, type, detail), (error.Code, error.WireType, error.Detail));

        // Validate is public on its own, so it must refuse the same break without the deserializer behind it.
        using var broken = JsonDocument.Parse(Break(id));
        var validated = Assert.Throws<WorkflowWireException>(() => WorkflowWire.Validate("WorkflowDefinition", broken.RootElement));
        Assert.Equal((code, type, detail), (validated.Code, validated.WireType, validated.Detail));
    }

    [Fact(DisplayName = "T-892: an unknown wire type is refused by name")]
    public void AnUnknownTypeIsRefused()
    {
        var error = Assert.Throws<WorkflowWireException>(() => WorkflowWire.Deserialize("NotAWorkflowType", "{}"));
        Assert.Equal(("unknown-type", "NotAWorkflowType"), (error.Code, error.WireType));
        Assert.Equal("unknown-type", Assert.Throws<WorkflowWireException>(() => WorkflowWire.Serialize("NotAWorkflowType", new object())).Code);
    }

    [Fact(DisplayName = "T-892: each frozen closed-value member is admitted, and a value outside the frozen set is refused")]
    public void ClosedValuesAreExact()
    {
        // The oracle is the fixture's frozen closed.values case, never the production table WorkflowWire.Validate reads.
        var frozen = FrozenClosedValues();
        Assert.NotEmpty(frozen);
        foreach (var (type, members) in frozen)
        {
            foreach (var member in members)
                WorkflowWire.Validate(type, JsonDocument.Parse(JsonSerializer.Serialize(member)).RootElement);
            Assert.Equal(("unknown-closed-value", type), Code(() => WorkflowWire.Validate(type, JsonDocument.Parse("\"not-a-member\"").RootElement)));
            Assert.Equal(("unknown-closed-value", type), Code(() => WorkflowWire.Validate(type, JsonDocument.Parse("1").RootElement)));
        }
    }

    private static Dictionary<string, string[]> FrozenClosedValues() =>
        Fixture["cases"]!.AsArray()
            .Single(row => (string?)row!["operation"] == "closed.values")!["expected"]!.AsObject()
            .ToDictionary(entry => entry.Key, entry => entry.Value!.AsArray().Select(member => (string)member!).ToArray(), StringComparer.Ordinal);

    private static (string, string) Code(Action act)
    {
        var error = Assert.Throws<WorkflowWireException>(act);
        return (error.Code, error.WireType);
    }

    /// <summary>The fixture's canonical definition, with every optional member the reader checks present and valid.</summary>
    private static JsonObject Valid()
    {
        var root = Fixture["canonical"]!.DeepClone().AsObject();
        root["envelope"] = Fixture["envelope"]!.DeepClone();
        root["envelope"]!["requires"] = new JsonArray(new JsonObject { ["capability"] = "ledger.post-journal-entry" });
        root["lineage"] = new JsonObject { ["parentDefinitionKey"] = "invoice-approval", ["parentVersion"] = "0.9.0" };
        root["description"] = root["title"]!.DeepClone();
        var state = (JsonObject)root["states"]![0]!;
        state["viewHint"] = Fixture["canonical"]!["states"]![0]!["viewHint"]?.DeepClone() ?? JsonValue.Create(FirstMember("WorkflowViewHint"));
        var transition = (JsonObject)root["transitions"]![1]!;
        transition["requiredStandings"] = new JsonArray(new JsonObject { ["name"] = "assigned-approver" });
        var trigger = (JsonObject)root["triggers"]![0]!;
        trigger["rrule"] = "FREQ=DAILY";
        trigger["task"] = "t";
        trigger["dependency"] = "d";
        var action = (JsonObject)root["actions"]![0]!;
        action["requiredStandings"] = new JsonArray(new JsonObject { ["name"] = "assigned-approver" });
        return root;
    }

    private static string FirstMember(string type) => FrozenClosedValues()[type].First();

    private static string Break(string id)
    {
        var root = Valid();
        var state = (JsonObject)root["states"]![0]!;
        var transition = (JsonObject)root["transitions"]![1]!;
        var trigger = (JsonObject)root["triggers"]![0]!;
        var action = (JsonObject)root["actions"]![0]!;
        var envelope = (JsonObject)root["envelope"]!;
        var role = (JsonObject)transition["requiredRoles"]![0]!;
        var standing = (JsonObject)transition["requiredStandings"]![0]!;
        var requirement = (JsonObject)envelope["requires"]![0]!;
        var contract = (JsonObject)envelope["contract"]!;
        var lineage = (JsonObject)root["lineage"]!;
        JsonNode Number() => JsonValue.Create(7);

        void Remove(JsonObject o, string name) => Assert.True(o.Remove(name), $"{id}: '{name}' was not present");
        void SetNumber(JsonObject o, string name) { Assert.True(o.ContainsKey(name), $"{id}: '{name}' was not present"); o[name] = Number(); }
        void Set(JsonObject o, string name, JsonNode value) => o[name] = value;

        var parts = id.Split('-', 3);
        var (owner, verb, member) = (parts[0], parts[1], parts.Length > 2 ? parts[2] : string.Empty);
        if (id == "definition-not-object") return "[]";
        switch (owner, verb, member)
        {
            case ("definition", "missing", _): Remove(root, member); break;
            case ("definition", "number", _): SetNumber(root, member); break;
            case ("definition", "bad", _): Set(root, member, "nope"); break;
            case ("definition", "object", _): Set(root, member, new JsonObject()); break;
            case ("state", "not", _): ((JsonArray)root["states"]!)[0] = "x"; break;
            case ("state", "missing", _): Remove(state, member); break;
            case ("state", "number", _): SetNumber(state, member); break;
            case ("state", "bad", "kind"): Set(state, "kind", "nope"); break;
            case ("state", "bad", "view-hint"): Set(state, "viewHint", "nope"); break;
            case ("transition", "not", _): ((JsonArray)root["transitions"]!)[1] = 1; break;
            case ("transition", "missing", _): Remove(transition, member); break;
            case ("transition", "number", "guard"): Set(transition, "guard", Number()); break;
            case ("transition", "number", _): SetNumber(transition, member); break;
            case ("transition", "object", "roles"): Set(transition, "requiredRoles", new JsonObject()); break;
            case ("transition", "object", "standings"): Set(transition, "requiredStandings", new JsonObject()); break;
            case ("role", "not", _): ((JsonArray)transition["requiredRoles"]!)[0] = "auditor"; break;
            case ("role", "missing", _): Remove(role, member); break;
            case ("role", "number", _): SetNumber(role, member); break;
            case ("role", "unknown", _): Set(role, "vocabulary", "no.such.vocabulary"); break;
            case ("standing", "not", _): ((JsonArray)transition["requiredStandings"]!)[0] = "x"; break;
            case ("standing", "missing", _): Remove(standing, member); break;
            case ("standing", "number", _): SetNumber(standing, member); break;
            case ("trigger", "not", _): ((JsonArray)root["triggers"]!)[0] = true; break;
            case ("trigger", "missing", _): Remove(trigger, member); break;
            case ("trigger", "number", _): SetNumber(trigger, member); break;
            case ("trigger", "bad", _): Set(trigger, "kind", "nope"); break;
            case ("action", "not", _): ((JsonArray)root["actions"]!)[0] = "x"; break;
            case ("action", "missing", "capability"): Remove(action, "capabilityRef"); break;
            case ("action", "missing", _): Remove(action, member); break;
            case ("action", "on", "not-object"): Set(action, "on", "t-approve"); break;
            case ("action", "on", "number-target"): Set(action, "on", new JsonObject { ["transition"] = Number() }); break;
            case ("action", "on", "number-state"): Set(action, "on", new JsonObject { ["state"] = Number() }); break;
            case ("action", "number", "capability"): SetNumber(action, "capabilityRef"); break;
            case ("action", "number", "condition"): Set(action, "condition", Number()); break;
            case ("action", "number", _): SetNumber(action, member); break;
            case ("action", "bad", _): Set(action, member, "nope"); break;
            case ("action", "object", "roles"): Set(action, "requiredRoles", new JsonObject()); break;
            case ("action", "object", "standings"): Set(action, "requiredStandings", new JsonObject()); break;
            case ("action", "string", _): Set(action, "params", "x"); break;
            case ("envelope", "not", _): Set(root, "envelope", "x"); break;
            case ("envelope", "missing", "cascade"): Remove(envelope, "cascadeLayer"); break;
            case ("envelope", "missing", "legal-hold"): Remove(envelope, "legalHold"); break;
            case ("envelope", "missing", _): Remove(envelope, member); break;
            case ("envelope", "number", _): SetNumber(envelope, member); break;
            case ("envelope", "bad", _): Set(envelope, "cascadeLayer", "nope"); break;
            case ("envelope", "string", _): Set(envelope, "legalHold", "yes"); break;
            case ("envelope", "object", _): Set(envelope, "requires", new JsonObject()); break;
            case ("requirement", "not", _): ((JsonArray)envelope["requires"]!)[0] = 1; break;
            case ("requirement", "missing", _): Remove(requirement, member); break;
            case ("requirement", "number", _): SetNumber(requirement, member); break;
            case ("contract", "not", _): Set(envelope, "contract", "1.0"); break;
            case ("contract", "missing", _): Remove(contract, member); break;
            case ("contract", "string", _): Set(contract, "major", "1"); break;
            case ("contract", "fraction", _): Set(contract, "minor", 1.5); break;
            case ("subject", "not", _): Set(root, "subject", "invoice"); break;
            case ("subject", "null", _): root["subject"] = null; break;
            case ("subject", "no", _): Set(root, "subject", new JsonObject()); break;
            case ("subject", "two", _): Set(root, "subject", new JsonObject { ["record"] = new JsonObject { ["recordType"] = "invoice" }, ["case"] = new JsonObject() }); break;
            case ("subject", var kind, "not-object"): Set(root, "subject", new JsonObject { [kind] = "invoice" }); break;
            case ("subject", var kind, "missing-type"): Set(root, "subject", new JsonObject { [kind] = new JsonObject() }); break;
            case ("subject", var kind, "number-type"): Set(root, "subject", new JsonObject { [kind] = new JsonObject { ["recordType"] = Number() } }); break;
            case ("lineage", _, _): BreakRef(lineage, root, "lineage", id[("lineage-".Length)..], new() { ["parent"] = "parentDefinitionKey", ["version"] = "parentVersion" }); break;
            default: throw new InvalidOperationException($"no break for '{id}'");
        }
        return root.ToJsonString();

        void BreakRef(JsonObject target, JsonObject parent, string member, string rest, Dictionary<string, string> names)
        {
            if (rest == "not-object") { parent[member] = 1; return; }
            var split = rest.Split('-', 2);
            var name = names[split[1]];
            if (split[0] == "missing") Remove(target, name); else SetNumber(target, name);
        }
    }

    private static string FindFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "conformance", "hlp.contracts.workflow", "fixtures.yaml");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("conformance/hlp.contracts.workflow/fixtures.yaml");
    }
}
